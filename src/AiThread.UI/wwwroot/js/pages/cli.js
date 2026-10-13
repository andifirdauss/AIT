(() => {
  'use strict';
  const { api, toast, escapeHtml } = window.AiThread;
  const el = id => document.getElementById(id);

  let clis = [];
  let projects = [];
  const CLAUDE = 'claude-code';

  const proxyUrl = () => el('proxyUrl').value.trim();
  const query = params => new URLSearchParams(params).toString();

  async function load() {
    try {
      const [list, projectList] = await Promise.all([api.get('/api/cli'), api.get('/api/projects').catch(() => [])]);
      clis = list.clis;
      projects = projectList;
      if (!el('proxyUrl').value) el('proxyUrl').value = list.proxyBaseUrl;
      el('cliCards').innerHTML = clis.map(cardShell).join('');
      await Promise.all(clis.map(refresh));
    } catch (error) {
      el('cliCards').innerHTML = `<div class="col-12 text-danger small">${escapeHtml(error.message)}</div>`;
    }
  }

  function cardShell(cli) {
    return `<div class="col-xl-6">
      <div class="card h-100" id="card-${cli.id}">
        <div class="card-header d-flex align-items-center gap-2">
          <span>${escapeHtml(cli.name)}</span>
          <span class="ms-auto small" id="summary-${cli.id}"></span>
        </div>
        <div class="card-body" id="body-${cli.id}"><span class="text-secondary small">Memuat…</span></div>
      </div>
    </div>`;
  }

  // Global status and per-project statuses of one CLI, then redraw its card.
  async function refresh(cli) {
    const box = el(`body-${cli.id}`);
    try {
      const params = query({ scope: 'Global', proxyBaseUrl: proxyUrl() });
      const [global, statuses] = await Promise.all([
        api.get(`/api/cli/${cli.id}/status?${params}`),
        api.get(`/api/cli/${cli.id}/projects?${query({ proxyBaseUrl: proxyUrl() })}`).catch(() => [])
      ]);
      box.innerHTML = cardBody(cli, global, statuses);
      const enabledProjects = statuses.filter(s => s.enabled).length;
      el(`summary-${cli.id}`).innerHTML = global.enabled
        ? '<span class="badge text-bg-success">Global aktif</span>'
        : enabledProjects
          ? `<span class="badge text-bg-primary">${enabledProjects} project aktif</span>`
          : '<span class="badge text-bg-secondary">Nonaktif</span>';
      if (cli.id === CLAUDE) await refreshHooks();
    } catch (error) {
      box.innerHTML = `<div class="alert alert-warning small mb-0">${escapeHtml(error.message)}</div>`;
    }
  }

  function switchHtml(cli, scope, projectId, checked, disabled) {
    const id = `sw-${cli.id}-${projectId || 'global'}`;
    return `<div class="form-check form-switch mb-0">
      <input class="form-check-input" type="checkbox" role="switch" id="${id}" data-cli="${cli.id}" data-scope="${scope}"
        ${projectId ? `data-project="${projectId}"` : ''} ${checked ? 'checked' : ''} ${disabled ? 'disabled' : ''} />
    </div>`;
  }

  function stateHtml(enabled, currentBaseUrl, error) {
    if (error) return `<span class="text-danger small">${escapeHtml(error)}</span>`;
    if (enabled) return '<span class="badge text-bg-success">Aktif</span>';
    if (currentBaseUrl) return `<span class="badge text-bg-warning" title="${escapeHtml(currentBaseUrl)}">Mengarah ke alamat lain</span>`;
    return '<span class="badge text-bg-secondary">Nonaktif</span>';
  }

  function cardBody(cli, global, statuses) {
    const byProject = new Map(statuses.map(s => [s.projectId, s]));
    const rows = projects.length
      ? projects.map(p => {
          const s = byProject.get(p.id);
          const usable = p.exists && !s?.error;
          return `<tr>
            <td style="width:3rem">${switchHtml(cli, 'Project', p.id, s?.enabled, !usable)}</td>
            <td>${escapeHtml(p.name)}<div class="small text-secondary mono">${escapeHtml(s?.path || p.rootPath)}</div></td>
            <td class="text-end text-nowrap">${p.exists ? stateHtml(s?.enabled, s?.currentBaseUrl, s?.error) : '<span class="text-danger small">Folder tidak ada</span>'}</td>
          </tr>`;
        }).join('')
      : '<tr><td colspan="3" class="text-secondary small">Belum ada project terdaftar — daftarkan di menu Project.</td></tr>';

    return `
      <p class="small text-secondary">${escapeHtml(cli.description)}</p>

      <div class="d-flex align-items-center gap-3 border rounded p-2 mb-3">
        ${switchHtml(cli, 'Global', null, global.enabled, false)}
        <div class="flex-grow-1">
          <div>Global <span class="text-secondary small">— semua sesi ${escapeHtml(cli.name)} di komputer ini</span></div>
          <div class="small text-secondary mono">${escapeHtml(global.path)}</div>
        </div>
        ${stateHtml(global.enabled, global.currentBaseUrl)}
      </div>

      ${cli.projectFile
        ? `<div class="small fw-semibold mb-1">Per project</div>
      <div class="table-responsive mb-2">
        <table class="table table-sm align-middle mb-0"><tbody>${rows}</tbody></table>
      </div>`
        : '<p class="small text-secondary mb-2">Hanya global — CLI ini tidak punya file konfigurasi per project.</p>'}

      <details class="mb-2">
        <summary class="small">Yang akan ditulis ke ${escapeHtml(global.path)}</summary>
        <pre class="code-block mt-2 mb-0">${escapeHtml(global.preview)}</pre>
      </details>
      ${cli.note ? `<p class="small text-secondary mb-0">${escapeHtml(cli.note)}</p>` : ''}
      ${cli.id === CLAUDE ? hooksShell() : ''}`;
  }

  el('cliCards').addEventListener('change', async event => {
    const input = event.target.closest('input[data-cli][data-scope]');
    if (!input) return;
    const cli = clis.find(c => c.id === input.dataset.cli);
    const enabled = input.checked;

    // A CLI without project files only gains an extra provider; nothing is redirected, so no confirmation.
    if (input.dataset.scope === 'Global' && enabled && cli.projectFile
        && !confirm(`Semua sesi ${cli.name} di komputer ini akan melewati AIT. Lanjutkan?`)) {
      input.checked = false;
      return;
    }

    input.disabled = true;
    try {
      await api.put(`/api/cli/${cli.id}/toggle`, {
        scope: input.dataset.scope,
        projectId: input.dataset.project || null,
        enabled,
        proxyBaseUrl: proxyUrl()
      });
      toast(enabled ? `${cli.name} diarahkan ke AIT. ${cli.note || ''}` : `${cli.name} tidak lagi diarahkan ke AIT.`, enabled ? 'success' : 'info');
    } catch (error) {
      input.checked = !enabled;
      toast(error.message, 'error');
    }
    await refresh(cli);
  });

  el('proxyUrl').addEventListener('change', () => clis.forEach(refresh));

  // --- Claude Code: Context Keeper /clear hooks (independent of the proxy toggle) ---

  function hooksShell() {
    const options = ['<option value="">Global — ~/.claude/settings.json</option>']
      .concat(projects.filter(p => p.exists).map(p => `<option value="${p.id}">${escapeHtml(p.name)} — .claude/settings.local.json</option>`));
    return `<hr />
      <h3 class="h6">Context Keeper untuk <span class="mono">/clear</span></h3>
      <p class="small text-secondary">
        Hook HTTP resmi Claude Code (<span class="mono">SessionEnd</span> + <span class="mono">SessionStart</span>). Saat Anda menjalankan
        <span class="mono">/clear</span>, AIT meringkas sesi lama dan menyisipkannya ke sesi baru. Berlaku juga untuk sesi yang
        <em>tidak</em> diarahkan ke proxy.
      </p>
      <select class="form-select form-select-sm mb-2" id="hooksTarget">${options.join('')}</select>
      <div class="alert small" id="hooksStatus">Memuat status…</div>
      <details class="mb-2">
        <summary class="small">Blok yang akan ditulis</summary>
        <pre class="code-block mt-2 mb-0" id="hooksPreview"></pre>
      </details>
      <div class="d-flex gap-2">
        <button type="button" class="btn btn-primary btn-sm" id="btnApplyHooks">Pasang hook</button>
        <button type="button" class="btn btn-outline-danger btn-sm" id="btnRevertHooks">Lepas hook</button>
      </div>
      <p class="small text-secondary mt-2 mb-0">
        Setelah <span class="mono">/clear</span>, Claude Code menunggu ringkasan maksimal ±40 detik. AIT harus berjalan;
        bila tidak, sesi baru tetap dimulai tanpa ringkasan.
      </p>`;
  }

  const hooksTarget = () => el('hooksTarget')?.value || '';
  const hooksBody = () => ({
    scope: hooksTarget() ? 'Project' : 'User',
    projectId: hooksTarget() || null,
    proxyBaseUrl: proxyUrl(),
    timeoutMs: null
  });

  function renderHooks(status) {
    const box = el('hooksStatus');
    box.classList.remove('alert-success', 'alert-secondary', 'alert-warning');
    if (status.installed) {
      box.classList.add('alert-success');
      box.innerHTML = `✓ Hook /clear terpasang.<br><span class="mono">${escapeHtml(status.path)}</span>`;
    } else if (status.installedBaseUrl) {
      box.classList.add('alert-warning');
      box.innerHTML = `Hook terpasang ke alamat lain: <span class="mono">${escapeHtml(status.installedBaseUrl)}</span> — pasang ulang untuk memperbarui.<br><span class="mono">${escapeHtml(status.path)}</span>`;
    } else {
      box.classList.add('alert-secondary');
      box.innerHTML = `Belum terpasang: <span class="mono">${escapeHtml(status.path)}</span>`;
    }
    el('hooksPreview').textContent = status.previewJson;
  }

  async function refreshHooks() {
    if (!el('hooksStatus')) return;
    const body = hooksBody();
    const params = { scope: body.scope, baseUrl: body.proxyBaseUrl };
    if (body.projectId) params.projectId = body.projectId;
    try {
      renderHooks(await api.get(`/api/settings/claude/hooks?${query(params)}`));
    } catch (error) {
      el('hooksStatus').className = 'alert alert-warning small';
      el('hooksStatus').textContent = error.message;
    }
  }

  el('cliCards').addEventListener('change', event => {
    if (event.target.id === 'hooksTarget') refreshHooks();
  });

  el('cliCards').addEventListener('click', async event => {
    const button = event.target.closest('#btnApplyHooks, #btnRevertHooks');
    if (!button) return;
    const apply = button.id === 'btnApplyHooks';
    try {
      renderHooks(await api.post(`/api/settings/claude/hooks/${apply ? 'apply' : 'revert'}`, hooksBody()));
      toast(apply ? 'Hook /clear terpasang. Berlaku untuk sesi Claude Code berikutnya.' : 'Hook /clear AIT dilepas.', apply ? 'success' : 'info');
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  load();
})();
