(() => {
  'use strict';
  const { isEn, t, api, toast, escapeHtml, winFrame } = window.AIThread;
  const el = id => document.getElementById(id);

  let clis = [];
  let projects = [];
  const openSections = new Set();
  const CLAUDE = 'claude-code';

  // Card header logos keyed by integration id: product marks from LobeHub icons (MIT, @lobehub/icons-static-svg@1.95.1)
  // and oh-my-pi's own assets/icon.svg (MIT). Gradient ids are prefixed cli-logo- so they stay unique on the page; an
  // integration without a logo gets the first letter of its name in its brand color (site.css .cli-card[data-cli-brand]).
  const LOGOS = {
    'claude-code': '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="#D97757" fill-rule="evenodd" d="M20.998 10.949H24v3.102h-3v3.028h-1.487V20H18v-2.921h-1.487V20H15v-2.921H9V20H7.488v-2.921H6V20H4.487v-2.921H3V14.05H0V10.95h3V5h17.998v5.949zM6 10.949h1.488V8.102H6v2.847zm10.51 0H18V8.102h-1.49v2.847z"/></svg>',
    'oh-my-pi-provider': '<svg viewBox="0 0 120 90" aria-hidden="true"><rect x="10" y="8" width="100" height="12" rx="2" fill="currentColor"/><rect x="25" y="20" width="12" height="62" rx="2" fill="currentColor"/><rect x="75" y="20" width="12" height="45" rx="2" fill="currentColor"/><rect x="71" y="55" width="20" height="16" rx="3" fill="#f97316"/><rect x="76" y="59" width="3" height="8" rx="1" fill="#0d0d0d"/><rect x="82" y="59" width="3" height="8" rx="1" fill="#0d0d0d"/><circle cx="18" cy="14" r="2" fill="#f97316" opacity=".8"/><circle cx="102" cy="14" r="2" fill="#f97316" opacity=".8"/></svg>',
    codex: '<svg viewBox="0 0 24 24" aria-hidden="true"><defs><linearGradient id="cli-logo-codex" gradientUnits="userSpaceOnUse" x1="12" x2="12" y1="0" y2="24"><stop stop-color="#B1A7FF"/><stop offset=".5" stop-color="#7A9DFF"/><stop offset="1" stop-color="#3941FF"/></linearGradient></defs><path fill="url(#cli-logo-codex)" fill-rule="evenodd" d="M8.086.457a6.105 6.105 0 013.046-.415c1.333.153 2.521.72 3.564 1.7a.117.117 0 00.107.029c1.408-.346 2.762-.224 4.061.366l.063.03.154.076c1.357.703 2.33 1.77 2.918 3.198.278.679.418 1.388.421 2.126a5.655 5.655 0 01-.18 1.631.167.167 0 00.04.155 5.982 5.982 0 011.578 2.891c.385 1.901-.01 3.615-1.183 5.14l-.182.22a6.063 6.063 0 01-2.934 1.851.162.162 0 00-.108.102c-.255.736-.511 1.364-.987 1.992-1.199 1.582-2.962 2.462-4.948 2.451-1.583-.008-2.986-.587-4.21-1.736a.145.145 0 00-.14-.032c-.518.167-1.04.191-1.604.185a5.924 5.924 0 01-2.595-.622 6.058 6.058 0 01-2.146-1.781c-.203-.269-.404-.522-.551-.821a7.740 7.740 0 01-.495-1.283 6.110 6.110 0 01-.017-3.064.166.166 0 00.008-.074.115.115 0 00-.037-.064 5.958 5.958 0 01-1.380-2.202 5.196 5.196 0 01-.333-1.589 6.915 6.915 0 01.188-2.132c.450-1.484 1.309-2.648 2.577-3.493.282-.188.550-.334.802-.438.286-.120.573-.220.861-.304a.129.129 0 00.087-.087A6.016 6.016 0 015.635 2.310C6.315 1.464 7.132.846 8.086.457zm-.804 7.850a.848.848 0 00-1.473.842l1.694 2.965-1.688 2.848a.849.849 0 001.460.864l1.940-3.272a.849.849 0 00.007-.854l-1.940-3.393zm5.446 6.240a.849.849 0 000 1.695h4.848a.849.849 0 000-1.696h-4.848z"/></svg>',
    'gemini-cli': '<svg viewBox="0 0 24 24" aria-hidden="true"><defs><linearGradient id="cli-logo-gemini" gradientUnits="userSpaceOnUse" x1="24" x2="0" y1="6.587" y2="16.494"><stop stop-color="#EE4D5D"/><stop offset=".328" stop-color="#B381DD"/><stop offset=".476" stop-color="#207CFE"/></linearGradient></defs><path fill="url(#cli-logo-gemini)" d="M0 4.391A4.391 4.391 0 014.391 0h15.217A4.391 4.391 0 0124 4.391v15.217A4.391 4.391 0 0119.608 24H4.391A4.391 4.391 0 010 19.608V4.391z"/><path fill="#1E1E2E" fill-rule="evenodd" d="M19.74 1.444a2.816 2.816 0 012.816 2.816v15.48a2.816 2.816 0 01-2.816 2.816H4.26a2.816 2.816 0 01-2.816-2.816V4.26A2.816 2.816 0 014.26 1.444h15.48zM7.236 8.564l7.752 3.728-7.752 3.727v2.802l9.557-4.596v-3.866L7.236 5.763v2.801z"/></svg>',
    opencode: '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" fill-rule="evenodd" d="M16 6H8v12h8V6zm4 16H4V2h16v20z"/></svg>',
    'qwen-code': '<svg viewBox="0 0 24 24" aria-hidden="true"><defs><linearGradient id="cli-logo-qwen" x1="0%" x2="100%" y1="0%" y2="0%"><stop offset="0%" stop-color="#6336E7"/><stop offset="100%" stop-color="#6F69F7"/></linearGradient></defs><path fill="url(#cli-logo-qwen)" d="M12.604 1.34c.393.69.784 1.382 1.174 2.075a.18.18 0 00.157.091h5.552c.174 0 .322.11.446.327l1.454 2.57c.19.337.24.478.024.837-.26.43-.513.864-.76 1.3l-.367.658c-.106.196-.223.28-.04.512l2.652 4.637c.172.301.111.494-.043.77-.437.785-.882 1.564-1.335 2.34-.159.272-.352.375-.68.37-.777-.016-1.552-.01-2.327.016a.099.099 0 00-.081.05 575.097 575.097 0 01-2.705 4.74c-.169.293-.38.363-.725.364-.997.003-2.002.004-3.017.002a.537.537 0 01-.465-.271l-1.335-2.323a.09.09 0 00-.083-.049H4.982c-.285.03-.553-.001-.805-.092l-1.603-2.77a.543.543 0 01-.002-.54l1.207-2.12a.198.198 0 000-.197 550.951 550.951 0 01-1.875-3.272l-.79-1.395c-.16-.31-.173-.496.095-.965.465-.813.927-1.625 1.387-2.436.132-.234.304-.334.584-.335a338.3 338.3 0 012.589-.001.124.124 0 00.107-.063l2.806-4.895a.488.488 0 01.422-.246c.524-.001 1.053 0 1.583-.006L11.704 1c.341-.003.724.032.9.34zm-3.432.403a.06.06 0 00-.052.03L6.254 6.788a.157.157 0 01-.135.078H3.253c-.056 0-.07.025-.041.074l5.81 10.156c.025.042.013.062-.034.063l-2.795.015a.218.218 0 00-.2.116l-1.32 2.31c-.044.078-.021.118.068.118l5.716.008c.046 0 .08.02.104.061l1.403 2.454c.046.081.092.082.139 0l5.006-8.76.783-1.382a.055.055 0 01.096 0l1.424 2.53a.122.122 0 00.107.062l2.763-.02a.04.04 0 00.035-.02.041.041 0 000-.04l-2.9-5.086a.108.108 0 010-.113l.293-.507 1.12-1.977c.024-.041.012-.062-.035-.062H9.2c-.059 0-.073-.026-.043-.077l1.434-2.505a.107.107 0 000-.114L9.225 1.774a.06.06 0 00-.053-.031zm6.29 8.02c.046 0 .058.02.034.06l-.832 1.465-2.613 4.585a.056.056 0 01-.05.029.058.058 0 01-.05-.029L8.498 9.841c-.02-.034-.01-.052.028-.054l.216-.012 6.722-.012z"/></svg>'
  };
  const logoHtml = cli => `<span class="cli-logo">${LOGOS[cli.id] || `<span class="cli-logo-text">${escapeHtml(cli.name.charAt(0))}</span>`}</span>`;

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
    return (isEn ? `<div class="col-md-6 col-xl-4">
      <div class="card h-100 cli-card" id="card-${cli.id}" data-cli-brand="${escapeHtml(cli.id)}">
        <div class="card-header d-flex align-items-center gap-2">
          ${logoHtml(cli)}
          <span>${escapeHtml(cli.name)}</span>
          <span class="ms-auto small" id="summary-${cli.id}"></span>
        </div>
        <div class="card-body" id="body-${cli.id}"><span class="text-secondary small">Loading…</span></div>
      </div>
    </div>` : `<div class="col-md-6 col-xl-4">
      <div class="card h-100 cli-card" id="card-${cli.id}" data-cli-brand="${escapeHtml(cli.id)}">
        <div class="card-header d-flex align-items-center gap-2">
          ${logoHtml(cli)}
          <span>${escapeHtml(cli.name)}</span>
          <span class="ms-auto small" id="summary-${cli.id}"></span>
        </div>
        <div class="card-body" id="body-${cli.id}"><span class="text-secondary small">Memuat…</span></div>
      </div>
    </div>`);
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
        ? t('<span class="badge text-bg-success">Global aktif</span>', '<span class="badge text-bg-success">Global on</span>')
        : enabledProjects
          ? (isEn ? `<span class="badge text-bg-primary">${enabledProjects} project(s) on</span>` : `<span class="badge text-bg-primary">${enabledProjects} project aktif</span>`)
          : t('<span class="badge text-bg-secondary">Nonaktif</span>', '<span class="badge text-bg-secondary">Off</span>');
      if (cli.id === CLAUDE) await refreshHooks();
    } catch (error) {
      box.innerHTML = `<div class="alert alert-warning small mb-0">${escapeHtml(error.message)}</div>`;
    }
  }

  function sectionHtml(id, title, body) {
    return `<details class="cli-section border rounded mb-2" id="${id}"${openSections.has(id) ? ' open' : ''}>
      <summary class="small fw-semibold px-2 py-1">${title}</summary>
      <div class="px-2 pb-2">${body}</div>
    </details>`;
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
    if (enabled) return t('<span class="badge text-bg-success">Aktif</span>', '<span class="badge text-bg-success">On</span>');
    if (currentBaseUrl) return (isEn ? `<span class="badge text-bg-warning" title="${escapeHtml(currentBaseUrl)}">Points to another address</span>` : `<span class="badge text-bg-warning" title="${escapeHtml(currentBaseUrl)}">Mengarah ke alamat lain</span>`);
    return t('<span class="badge text-bg-secondary">Nonaktif</span>', '<span class="badge text-bg-secondary">Off</span>');
  }

  function cardBody(cli, global, statuses) {
    const byProject = new Map(statuses.map(s => [s.projectId, s]));
    const activeCount = statuses.filter(s => s.enabled).length;
    const rows = projects.length
      ? projects.map(p => {
          const s = byProject.get(p.id);
          const usable = p.exists && !s?.error;
          return `<tr>
            <td style="width:3rem">${switchHtml(cli, 'Project', p.id, s?.enabled, !usable)}</td>
            <td>${escapeHtml(p.name)}<div class="small text-secondary mono">${escapeHtml(s?.path || p.rootPath)}</div></td>
            <td class="text-end text-nowrap">${p.exists ? stateHtml(s?.enabled, s?.currentBaseUrl, s?.error) : t('<span class="text-danger small">Folder tidak ada</span>', '<span class="text-danger small">Folder missing</span>')}</td>
          </tr>`;
        }).join('')
      : t('<tr><td colspan="3" class="text-secondary small">Belum ada project terdaftar — daftarkan di menu Project.</td></tr>', '<tr><td colspan="3" class="text-secondary small">No project registered yet — register one in the Project menu.</td></tr>');

    return (isEn ? `
      <p class="small text-secondary mb-2">${escapeHtml(cli.description)}</p>

      <div class="d-flex align-items-center gap-3 border rounded p-2 mb-2">
        ${switchHtml(cli, 'Global', null, global.enabled, false)}
        <div class="flex-grow-1">
          <div>Global <span class="text-secondary small">— every ${escapeHtml(cli.name)} session on this computer</span></div>
          <div class="small text-secondary mono">${escapeHtml(global.path)}</div>
        </div>
        ${stateHtml(global.enabled, global.currentBaseUrl)}
      </div>

      ${cli.projectFile
        ? sectionHtml(`cli-projects-${cli.id}`, `Per project <span class="fw-normal text-secondary">· ${activeCount} of ${projects.length} on</span>`, `<div class="table-responsive"><table class="table table-sm align-middle mb-0"><tbody>${rows}</tbody></table></div>`)
        : '<p class="small text-secondary mb-2">Global only — this CLI has no per-project configuration file.</p>'}

      ${winFrame({
        id: `cli-preview-${cli.id}`,
        title: `What will be written · ${global.path}`,
        minimized: true,
        className: 'mb-2',
        body: `<pre class="code-block">${escapeHtml(global.preview)}</pre>`
      })}
      ${cli.note ? `<p class="small text-secondary mb-0">${escapeHtml(cli.note)}</p>` : ''}
      ${cli.id === CLAUDE ? sectionHtml('cli-hooks', 'Hooks: Context Keeper <span class="mono">/clear</span> &amp; secret restorer', hooksShell()) : ''}` : `
      <p class="small text-secondary mb-2">${escapeHtml(cli.description)}</p>

      <div class="d-flex align-items-center gap-3 border rounded p-2 mb-2">
        ${switchHtml(cli, 'Global', null, global.enabled, false)}
        <div class="flex-grow-1">
          <div>Global <span class="text-secondary small">— semua sesi ${escapeHtml(cli.name)} di komputer ini</span></div>
          <div class="small text-secondary mono">${escapeHtml(global.path)}</div>
        </div>
        ${stateHtml(global.enabled, global.currentBaseUrl)}
      </div>

      ${cli.projectFile
        ? sectionHtml(`cli-projects-${cli.id}`, `Per project <span class="fw-normal text-secondary">· ${activeCount} aktif dari ${projects.length}</span>`, `<div class="table-responsive"><table class="table table-sm align-middle mb-0"><tbody>${rows}</tbody></table></div>`)
        : '<p class="small text-secondary mb-2">Hanya global — CLI ini tidak punya file konfigurasi per project.</p>'}

      ${winFrame({
        id: `cli-preview-${cli.id}`,
        title: `Yang akan ditulis · ${global.path}`,
        minimized: true,
        className: 'mb-2',
        body: `<pre class="code-block">${escapeHtml(global.preview)}</pre>`
      })}
      ${cli.note ? `<p class="small text-secondary mb-0">${escapeHtml(cli.note)}</p>` : ''}
      ${cli.id === CLAUDE ? sectionHtml('cli-hooks', 'Hook: Context Keeper <span class="mono">/clear</span> &amp; pemulih secret', hooksShell()) : ''}`);
  }

  el('cliCards').addEventListener('toggle', event => {
    const d = event.target;
    if (!(d instanceof HTMLDetailsElement) || !d.classList.contains('cli-section')) return;
    if (d.open) openSections.add(d.id); else openSections.delete(d.id);
  }, true);

  el('cliCards').addEventListener('change', async event => {
    const input = event.target.closest('input[data-cli][data-scope]');
    if (!input) return;
    const cli = clis.find(c => c.id === input.dataset.cli);
    const enabled = input.checked;

    // A CLI without project files only gains an extra provider; nothing is redirected, so no confirmation.
    if (input.dataset.scope === 'Global' && enabled && cli.projectFile
        && !confirm((isEn ? `Every ${cli.name} session on this computer will go through AIT. Continue?` : `Semua sesi ${cli.name} di komputer ini akan melewati AIT. Lanjutkan?`))) {
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
      toast(enabled ? (isEn ? `${cli.name} now points to AIT. ${cli.note || ''}` : `${cli.name} diarahkan ke AIT. ${cli.note || ''}`) : (isEn ? `${cli.name} no longer points to AIT.` : `${cli.name} tidak lagi diarahkan ke AIT.`), enabled ? 'success' : 'info');
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
    return (isEn ? `<p class="small text-secondary">
        Official Claude Code HTTP hooks (<span class="mono">SessionEnd</span> + <span class="mono">SessionStart</span>). When you run
        <span class="mono">/clear</span>, AIT summarizes the old session and puts the summary into the new one. It also works for sessions
        <em>not</em> routed through the proxy.
      </p>
      <p class="small text-secondary">
        The <span class="mono">PreToolUse</span> hook restores secrets: the model only sees the placeholder <span class="mono">{{AIT_SECRET:…}}</span>,
        and before Claude Code runs Edit/Write/Bash, AIT replaces that placeholder with the real value (you are still asked to confirm).
        Placeholders it does not know are refused, so they are never written to a file as they are.
      </p>
      <select class="form-select form-select-sm mb-2" id="hooksTarget">${options.join('')}</select>
      <div class="alert small" id="hooksStatus">Loading status…</div>
      ${winFrame({
        id: 'cli-hooks-preview',
        title: 'Hook block that will be written',
        minimized: true,
        className: 'mb-2',
        body: '<pre class="code-block" id="hooksPreview"></pre>'
      })}
      <div class="d-flex gap-2">
        <button type="button" class="btn btn-primary btn-sm" id="btnApplyHooks">Install hooks</button>
        <button type="button" class="btn btn-outline-danger btn-sm" id="btnRevertHooks">Remove hooks</button>
      </div>
      <p class="small text-secondary mt-2 mb-0">
        After <span class="mono">/clear</span>, Claude Code waits at most about 40 seconds for the summary. AIT must be running;
        if it is not, the new session still starts, without a summary.
      </p>` : `<p class="small text-secondary">
        Hook HTTP resmi Claude Code (<span class="mono">SessionEnd</span> + <span class="mono">SessionStart</span>). Saat Anda menjalankan
        <span class="mono">/clear</span>, AIT meringkas sesi lama dan menyisipkannya ke sesi baru. Berlaku juga untuk sesi yang
        <em>tidak</em> diarahkan ke proxy.
      </p>
      <p class="small text-secondary">
        Hook <span class="mono">PreToolUse</span> memulihkan secret: model hanya melihat placeholder <span class="mono">{{AIT_SECRET:…}}</span>,
        dan sebelum Claude Code menjalankan Edit/Write/Bash, AIT mengganti placeholder itu dengan nilai aslinya (Anda tetap diminta konfirmasi).
        Placeholder yang tidak dikenali akan ditolak, supaya tidak tertulis ke file apa adanya.
      </p>
      <select class="form-select form-select-sm mb-2" id="hooksTarget">${options.join('')}</select>
      <div class="alert small" id="hooksStatus">Memuat status…</div>
      ${winFrame({
        id: 'cli-hooks-preview',
        title: 'Blok hook yang akan ditulis',
        minimized: true,
        className: 'mb-2',
        body: '<pre class="code-block" id="hooksPreview"></pre>'
      })}
      <div class="d-flex gap-2">
        <button type="button" class="btn btn-primary btn-sm" id="btnApplyHooks">Pasang hook</button>
        <button type="button" class="btn btn-outline-danger btn-sm" id="btnRevertHooks">Lepas hook</button>
      </div>
      <p class="small text-secondary mt-2 mb-0">
        Setelah <span class="mono">/clear</span>, Claude Code menunggu ringkasan maksimal ±40 detik. AIT harus berjalan;
        bila tidak, sesi baru tetap dimulai tanpa ringkasan.
      </p>`);
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
      box.innerHTML = (isEn ? `✓ /clear hook and secret restorer installed.<br><span class="mono">${escapeHtml(status.path)}</span>` : `✓ Hook /clear dan pemulih secret terpasang.<br><span class="mono">${escapeHtml(status.path)}</span>`);
    } else if (status.installedBaseUrl) {
      box.classList.add('alert-warning');
      box.innerHTML = (isEn ? `Hook of an older version or to another address: <span class="mono">${escapeHtml(status.installedBaseUrl)}</span> — install again to update it.<br><span class="mono">${escapeHtml(status.path)}</span>` : `Hook versi lama atau ke alamat lain: <span class="mono">${escapeHtml(status.installedBaseUrl)}</span> — pasang ulang untuk memperbarui.<br><span class="mono">${escapeHtml(status.path)}</span>`);
    } else {
      box.classList.add('alert-secondary');
      box.innerHTML = (isEn ? `Not installed: <span class="mono">${escapeHtml(status.path)}</span>` : `Belum terpasang: <span class="mono">${escapeHtml(status.path)}</span>`);
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
      toast(apply ? t('Hook /clear terpasang. Berlaku untuk sesi Claude Code berikutnya.', '/clear hook installed. It applies to the next Claude Code sessions.') : (isEn ? 'AIT /clear hook removed.' : 'Hook /clear AIT dilepas.'), apply ? 'success' : 'info');
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  load();
})();
