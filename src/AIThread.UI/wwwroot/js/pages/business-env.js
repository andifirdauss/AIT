// Memory → Business memory, per environment: the wiki as it holds in dev / staging / prod (read-only, copied from the
// working wiki once the changes behind a version reached that environment's branch), and the compare of two sides.
(() => {
  'use strict';
  const { isEn, t, api, toast, escapeHtml, fmt, stream, debounce } = window.AIThread;
  const el = id => document.getElementById(id);
  const working = 'work';
  const compareModal = bootstrap.Modal.getOrCreateInstance(el('bizCompareModal'));
  let environments = [];
  let openPage = null;

  const projectId = () => el('bizProject').value;
  const environment = () => el('bizEnv').value;
  const sideName = side => side === working ? t('Wiki kerja', 'Working wiki') : side;

  async function loadEnvironments() {
    if (!projectId()) return;
    try {
      environments = await api.get(`/api/projects/${projectId()}/environments`);
    } catch {
      environments = [];
    }
    const selected = environment();
    el('bizEnv').innerHTML = `<option value="${working}">${escapeHtml(sideName(working))}</option>` + environmentOptions();
    el('bizEnv').value = environments.some(e => e.name === selected) ? selected : working;
    el('btnBizCompare').disabled = environments.length === 0;
    show();
  }

  // Registered environments first, then this clone's remote branches (environments by themselves), newest first.
  function environmentOptions() {
    const option = e => `<option value="${escapeHtml(e.name)}" title="${escapeHtml(e.ref)}">${escapeHtml(e.name)}${e.isProduction ? ' ★' : ''}</option>`;
    const registered = environments.filter(e => !e.isAuto);
    const branches = environments.filter(e => e.isAuto);
    return (registered.length ? `<optgroup label="${escapeHtml(t('Environment terdaftar', 'Registered environments'))}">${registered.map(option).join('')}</optgroup>` : '')
      + (branches.length ? `<optgroup label="${escapeHtml(t('Branch remote', 'Remote branches'))}">${branches.map(option).join('')}</optgroup>` : '');
  }

  function show() {
    const env = environment();
    const onEnvironment = env !== working;
    el('bizMain').classList.toggle('d-none', onEnvironment);
    el('bizEnvPanel').classList.toggle('d-none', !onEnvironment);
    el('btnBizNew').disabled = onEnvironment;
    el('btnBizDigest').disabled = onEnvironment;
    if (onEnvironment) loadPages();
  }

  async function loadPages() {
    const env = environment();
    try {
      const overview = await api.get(`/api/business/projects/${projectId()}/environments/${encodeURIComponent(env)}`);
      const info = environments.find(e => e.name === env);
      el('bizEnvInfo').innerHTML = `<span class="mono">${escapeHtml(info?.ref || '')}</span>`
        + (info && !info.refFound ? ` · <span class="text-warning">${t('ref belum ada di clone ini (fetch dulu)', 'ref not in this clone yet (fetch first)')}</span>` : '')
        + (isEn ? ` · ${overview.pages.length} page(s), ${overview.missing} not here yet` : ` · ${overview.pages.length} halaman, ${overview.missing} belum masuk`);
      el('bizEnvPages').innerHTML = overview.pages.length
        ? overview.pages.map(p => `<button type="button" class="list-group-item list-group-item-action ${openPage === p.pageId ? 'active' : ''}" data-env-page="${p.pageId}">
            <div class="small text-secondary">${escapeHtml(p.module)}</div>
            <div>${escapeHtml(p.title)}</div>
            ${p.workingVersion != null && p.workingVersion !== p.version
              ? `<div class="small"><span class="badge text-bg-warning">${isEn ? `version ${p.version}, working ${p.workingVersion}` : `versi ${p.version}, kerja ${p.workingVersion}`}</span></div>` : ''}
          </button>`).join('')
        : `<div class="list-group-item text-secondary">${t('Belum ada halaman yang perubahannya masuk ke environment ini.', 'No page whose changes reached this environment yet.')}</div>`;
      if (openPage && !overview.pages.some(p => p.pageId === openPage)) clearPage();
    } catch (error) {
      el('bizEnvInfo').innerHTML = `<span class="text-danger">${escapeHtml(error.message)}</span>`;
    }
  }

  function clearPage() {
    openPage = null;
    el('bizEnvPageMeta').textContent = t('Pilih halaman di kiri.', 'Choose a page on the left.');
    el('bizEnvContent').innerHTML = '';
  }

  el('bizEnvPages').addEventListener('click', async event => {
    const item = event.target.closest('[data-env-page]');
    if (!item) return;
    try {
      const page = await api.get(`/api/business/pages/${item.dataset.envPage}/environments/${encodeURIComponent(environment())}`);
      openPage = page.pageId;
      el('bizEnvPageMeta').innerHTML = `<span class="badge text-bg-secondary">${escapeHtml(page.module)}</span> ${escapeHtml(page.title)} · `
        + (isEn ? `version ${page.version} in ${escapeHtml(page.environment)}` : `versi ${page.version} di ${escapeHtml(page.environment)}`)
        + (page.workingVersion != null && page.workingVersion !== page.version
          ? (isEn ? ` · the working wiki is at version ${page.workingVersion}` : ` · wiki kerja sudah versi ${page.workingVersion}`) : '')
        + (page.updatedAt ? ` · ${fmt.relative(page.updatedAt)}` : '');
      el('bizEnvContent').innerHTML = page.html;
      loadPages();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('bizEnv').addEventListener('change', () => {
    clearPage();
    show();
  });
  el('bizProject').addEventListener('change', () => {
    el('bizEnv').value = working;
    clearPage();
    loadEnvironments();
  });

  // --- Compare ---

  el('btnBizCompare').addEventListener('click', () => {
    const options = `<option value="${working}">${escapeHtml(sideName(working))}</option>` + environmentOptions();
    el('bizCompareLeft').innerHTML = options;
    el('bizCompareRight').innerHTML = options;
    // By default the last two registered environments in order (e.g. staging → prod); without them the working wiki
    // against the most recently changed remote branch.
    const names = environments.filter(e => !e.isAuto).map(e => e.name);
    const branch = environments.find(e => e.isAuto)?.name;
    el('bizCompareLeft').value = names.length > 1 ? names[names.length - 2] : working;
    el('bizCompareRight').value = names.length > 0 ? names[names.length - 1] : (branch ?? working);
    el('bizCompareResult').innerHTML = t('Pilih dua sisi, mis. staging dan prod.', 'Choose two sides, e.g. staging and prod.');
    compareModal.show();
  });

  const statusLabels = {
    changed: ['warning', t('berbeda', 'differs')],
    left: ['info', t('hanya di kiri', 'left only')],
    right: ['info', t('hanya di kanan', 'right only')],
    same: ['secondary', t('sama', 'same')]
  };

  function diffHtml(lines) {
    return `<pre class="small border rounded p-2 mb-0" style="white-space:pre-wrap">${lines.map(l => {
      if (l.kind === 'skip') return `<div class="text-secondary">… ${isEn ? `${l.text} unchanged line(s)` : `${l.text} baris sama`} …</div>`;
      const cls = l.kind === 'add' ? 'bg-success-subtle' : l.kind === 'del' ? 'bg-danger-subtle' : '';
      const sign = l.kind === 'add' ? '+ ' : l.kind === 'del' ? '- ' : '  ';
      return `<div class="${cls}">${sign}${escapeHtml(l.text)}</div>`;
    }).join('')}</pre>`;
  }

  el('btnBizCompareRun').addEventListener('click', async () => {
    const left = el('bizCompareLeft').value;
    const right = el('bizCompareRight').value;
    if (left === right) {
      toast(t('Pilih dua sisi yang berbeda.', 'Choose two different sides.'), 'error');
      return;
    }
    el('bizCompareResult').textContent = t('Membandingkan…', 'Comparing…');
    try {
      const result = await api.get(`/api/business/projects/${projectId()}/compare?left=${encodeURIComponent(left)}&right=${encodeURIComponent(right)}`);
      const differing = result.pages.filter(p => p.status !== 'same');
      const pages = differing.length
        ? differing.map(p => {
          const [color, label] = statusLabels[p.status];
          const versions = `${escapeHtml(sideName(left))} ${p.leftVersion ?? '–'} · ${escapeHtml(sideName(right))} ${p.rightVersion ?? '–'}`;
          return `<details class="mb-2" ${differing.length <= 3 ? 'open' : ''}>
              <summary><span class="badge text-bg-${color}">${label}</span> <span class="text-secondary">${escapeHtml(p.module)}</span> ${escapeHtml(p.title)}
                <span class="text-secondary small">(${isEn ? 'version' : 'versi'} ${versions})</span></summary>
              ${p.diff.length ? diffHtml(p.diff) : ''}
            </details>`;
        }).join('')
        : `<div class="text-secondary">${t('Semua halaman sama.', 'Every page is the same.')}</div>`;
      const changeRows = result.changes.map(c => `<tr>
          <td>${c.side === 'left' ? escapeHtml(sideName(left)) : escapeHtml(sideName(right))}</td>
          <td><a href="/Thread/Detail/${c.threadId}">${escapeHtml(c.title || c.threadId.substring(0, 8))}</a></td>
          <td>${escapeHtml(c.author || '–')}</td>
          <td class="mono">${escapeHtml(c.branch || '–')}</td>
          <td class="mono">${c.commit ? escapeHtml(c.commit.substring(0, 8)) : '–'}</td></tr>`).join('');
      const changes = result.changes.length
        ? `<h6 class="mt-3">${t('Perubahan yang hanya ada di satu sisi', 'Changes on one side only')}</h6>
           <div class="table-responsive"><table class="table table-sm small"><thead><tr><th>${t('Ada di', 'In')}</th><th>${t('Perubahan', 'Change')}</th><th>Author</th><th>Branch</th><th>Commit</th></tr></thead>
           <tbody>${changeRows}</tbody></table></div>`
        : `<div class="text-secondary mt-3">${t('Tidak ada perubahan yang hanya di satu sisi.', 'No change on one side only.')}</div>`;
      el('bizCompareResult').innerHTML = `<h6>${t('Halaman', 'Pages')} (${differing.length}/${result.pages.length} ${t('berbeda', 'differ')})</h6>${pages}${changes}`;
    } catch (error) {
      el('bizCompareResult').innerHTML = `<span class="text-danger">${escapeHtml(error.message)}</span>`;
    }
  });

  // The page list of business.js loads the projects first; the environments follow once a project is chosen.
  const waitForProject = setInterval(() => {
    if (!projectId()) return;
    clearInterval(waitForProject);
    loadEnvironments();
  }, 300);

  stream('types=business,project', {
    'business.changed': debounce(() => { if (environment() !== working) loadPages(); }, 800),
    'project.changed': debounce(loadEnvironments, 800)
  });
})();
