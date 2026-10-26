// Setup page: one card per first-run step. The status of every step comes from /api/setup/status (computed from the
// live state); each card completes its step in place through the same APIs the other menus use.
(() => {
  'use strict';
  const { isEn, t, api, toast, escapeHtml, fmt, stream, debounce, renderRouterPill } = window.AIThread;
  const el = id => document.getElementById(id);
  const roleLabels = {
    Planner: 'Planner', ToolsNode: 'Tools Node', Executor: 'Executor', Reviewer: 'Reviewer', Tester: 'Tester', Wrapper: 'Wrapper',
    ContextKeeper: 'Context Keeper'
  };
  const sectionLabels = {
    Router: t('Koneksi 9router', '9router connection'), Orchestration: (isEn ? 'Proxy & orchestration' : 'Proxy & orkestrasi'), Nodes: 'Node', Memory: t('Penyimpanan memory', 'Memory storage'), Business: 'Business memory', Skills: 'Skill'
  };
  // Names of the nodes the preset creates (SetupService.PresetNames).
  const presetNames = {
    Planner: 'AIT Planner', ToolsNode: 'AIT Tools Node', Executor: 'AIT Executor', Reviewer: 'AIT Reviewer', Tester: 'AIT Tester', Wrapper: 'AIT Wrapper',
    ContextKeeper: 'AIT Context Keeper'
  };
  const isPreset = n => n.name.toLowerCase() === (presetNames[n.role] || '').toLowerCase();

  let status = null;
  let firstRender = true;

  // This browser has seen the Setup page: site.js no longer sends a fresh instance here from the dashboard.
  try {
    localStorage.setItem('aithread.setup.opened', '1');
  } catch {
    // Not remembered; the dashboard may send this browser here once more.
  }

  // --- Status ---

  function stateBadge(step) {
    if (step.done) return t('<span class="badge text-bg-success">✓ Selesai</span>', '<span class="badge text-bg-success">✓ Done</span>');
    return step.required ? t('<span class="badge text-bg-warning">Belum</span>', '<span class="badge text-bg-warning">Not yet</span>') : (isEn ? '<span class="badge text-bg-secondary">Optional</span>' : '<span class="badge text-bg-secondary">Opsional</span>');
  }

  function renderStatus(previous) {
    const { steps, requiredCount, requiredDone, isComplete } = status;
    el('setupSummary').textContent = isComplete
      ? (isEn ? `All ${requiredCount} required steps done.` : `Semua ${requiredCount} langkah wajib selesai.`)
      : (isEn ? `${requiredDone} of ${requiredCount} required steps done.` : `${requiredDone} dari ${requiredCount} langkah wajib selesai.`);
    el('setupChecked').textContent = `diperiksa ${fmt.time(new Date())}`;
    const percent = requiredCount ? Math.round(requiredDone * 100 / requiredCount) : 0;
    el('setupBar').style.width = `${percent}%`;
    el('setupBar').classList.toggle('bg-success', isComplete);
    el('setupProgress').setAttribute('aria-valuenow', String(percent));

    steps.forEach((step, index) => {
      const box = el(`step-${step.id}`);
      if (!box) return;
      box.classList.toggle('done', step.done);
      box.querySelector('.setup-step-num').textContent = step.done ? '✓' : String(index + 1);
      box.querySelector('[data-detail]').textContent = step.detail;
      box.querySelector('[data-state]').innerHTML = stateBadge(step);
    });

    // Open the step the user came for (#step-router from the tour), else the first required step still open; when a
    // step has just been completed, fold it and move on to the next one.
    const justDone = previous ? steps.filter(s => s.done && !previous.steps.find(p => p.id === s.id)?.done) : [];
    const next = steps.find(s => s.required && !s.done);
    if (firstRender) {
      const hashed = location.hash ? document.getElementById(location.hash.slice(1)) : null;
      const target = hashed?.classList.contains('setup-step') ? hashed : null;
      (target || (next && el(`step-${next.id}`)))?.setAttribute('open', '');
      target?.scrollIntoView({ block: 'start' });
      firstRender = false;
    } else if (justDone.length) {
      justDone.forEach(s => el(`step-${s.id}`)?.removeAttribute('open'));
      if (next) el(`step-${next.id}`)?.setAttribute('open', '');
    }
    el('setupDone').classList.toggle('d-none', !isComplete);
  }

  async function loadStatus(refresh = false) {
    try {
      const previous = status;
      status = await api.get(`/api/setup/status${refresh ? '?refresh=true' : ''}`);
      renderStatus(previous);
      window.AIThread.renderSetupStatus?.(status);
    } catch (error) {
      el('setupSummary').textContent = error.message;
    }
  }

  // --- 9router ---

  async function loadRouter() {
    try {
      const s = await api.get('/api/settings');
      el('setupRouterUrl').value = s.nineRouterBaseUrl;
      el('setupRouterKey').value = '';
      el('setupRouterKey').placeholder = s.hasNineRouterApiKey ? s.nineRouterApiKeyMasked : '';
      el('setupRouterKeyHint').textContent = s.hasNineRouterApiKey
        ? t('Tersimpan terenkripsi. Kosongkan untuk tetap memakainya.', 'Stored encrypted. Leave empty to keep using it.')
        : t('Belum ada key tersimpan. Kosongkan bila 9router tidak mewajibkannya.', 'No key stored yet. Leave empty when 9router does not require one.');
      el('routerDashboard').href = s.nineRouterBaseUrl;
    } catch (error) {
      el('routerResult').innerHTML = `<span class="text-danger">${escapeHtml(error.message)}</span>`;
    }
  }

  el('routerForm').addEventListener('submit', async event => {
    event.preventDefault();
    const button = el('btnSaveRouter');
    button.disabled = true;
    button.textContent = (isEn ? 'Testing…' : 'Menguji…');
    el('routerResult').innerHTML = (isEn ? '<span class="text-secondary">Contacting 9router…</span>' : '<span class="text-secondary">Menghubungi 9router…</span>');
    try {
      const r = await api.post('/api/setup/router', { baseUrl: el('setupRouterUrl').value.trim(), apiKey: el('setupRouterKey').value.trim() || null });
      el('routerResult').innerHTML = r.saved
        ? `<span class="text-success">✓ ${escapeHtml(r.message)}</span>`
        : `<span class="text-danger">✕ ${escapeHtml(r.message)}</span>`;
      renderRouterPill(r.status);
      if (r.saved) await Promise.all([loadRouter(), loadNodesStep()]);
      await loadStatus();
    } catch (error) {
      el('routerResult').innerHTML = `<span class="text-danger">${escapeHtml(error.message)}</span>`;
    } finally {
      button.disabled = false;
      button.textContent = t('Test & simpan', 'Test & save');
    }
  });

  // Starting can take a while: the first start prepares 9router's runtime, a missing 9router is installed with npm.
  el('btnStartRouter').addEventListener('click', async () => {
    const button = el('btnStartRouter');
    button.disabled = true;
    button.innerHTML = t('<span class="spinner-border spinner-border-sm me-1"></span>Menjalankan…', '<span class="spinner-border spinner-border-sm me-1"></span>Starting…');
    try {
      const r = await api.post('/api/router/start');
      toast(r.message, r.isOnline ? 'success' : 'warning');
      await Promise.all([loadStatus(true), loadNodesStep()]);
    } catch (error) {
      toast(error.message, 'error');
    } finally {
      button.disabled = false;
      button.textContent = t('Jalankan 9router', 'Start 9router');
    }
  });

  // --- Model (preset pipeline) ---

  let models = [];
  let nodes = [];
  let presetChoicesLoaded = false;

  function groupOf(m) {
    return m.isCombo ? 'Combo' : (m.ownedBy || 'lainnya');
  }

  // Rough guesses from the model id, only to preselect something sensible; the user picks. Size words count only as
  // a whole part of the id ("gemini" is not "mini").
  const cheapTrait = /haiku|flash|(^|[-/._])(mini|nano|lite|small|tiny|free)([-/._]|$)/;

  function strongScore(id) {
    const name = id.toLowerCase();
    if (cheapTrait.test(name)) return -1;
    if (/fable|opus/.test(name)) return 3;
    if (/sonnet|gpt-[5-9]|(^|[-/._])pro([-/._]|$)|ultra|max/.test(name)) return 2;
    return 0;
  }

  function cheapScore(id) {
    const name = id.toLowerCase();
    if (/haiku/.test(name)) return 3;
    if (/flash/.test(name) && !/lite/.test(name)) return 2;
    return cheapTrait.test(name) ? 1 : 0;
  }

  const providerOf = id => id.includes('/') ? id.slice(0, id.indexOf('/')).toLowerCase() : '';

  // The strongest model, and the cheapest of the same provider when it has one (same account, same quota).
  function guessModels() {
    const best = (list, score) => list.reduce((top, m) => (top === null || score(m) > score(top) ? m : top), null);
    const strong = best(models, m => strongScore(m.id))?.id || '';
    const cheap = best(models, m => cheapScore(m.id) * 2 + (providerOf(m.id) === providerOf(strong) ? 1 : 0));
    return { strong, cheap: cheap && cheapScore(cheap.id) > 0 ? cheap.id : strong };
  }

  function modelInUse(role) {
    const own = nodes.find(n => n.role === role && isPreset(n));
    const active = nodes.filter(n => n.role === role && n.isActive).sort((a, b) => a.priority - b.priority)[0];
    return (own || active)?.model;
  }

  function modelOptions(selected) {
    const sorted = [...models].sort((a, b) => (b.isCombo - a.isCombo) || groupOf(a).localeCompare(groupOf(b)) || a.id.localeCompare(b.id));
    let html = '';
    let group = null;
    for (const m of sorted) {
      if (groupOf(m) !== group) {
        if (group !== null) html += '</optgroup>';
        group = groupOf(m);
        html += `<optgroup label="${escapeHtml(group)}">`;
      }
      html += `<option value="${escapeHtml(m.id)}" ${m.id === selected ? 'selected' : ''}>${escapeHtml(m.id)}</option>`;
    }
    if (group !== null) html += '</optgroup>';
    if (selected && !models.some(m => m.id === selected)) html = (isEn ? `<option value="${escapeHtml(selected)}" selected>${escapeHtml(selected)} (not in 9router)</option>${html}` : `<option value="${escapeHtml(selected)}" selected>${escapeHtml(selected)} (tidak ada di 9router)</option>${html}`);
    return html;
  }

  async function loadNodesStep() {
    [models, nodes] = await Promise.all([
      api.get('/api/router/models?refresh=true').catch(() => []),
      api.get('/api/nodes').catch(() => [])
    ]);
    // A choice the user already made in the form is kept when the lists are reloaded.
    const guess = guessModels();
    const strong = el('presetStrong').value || modelInUse('Planner') || guess.strong;
    const cheap = el('presetCheap').value || modelInUse('Executor') || guess.cheap;
    el('presetStrong').innerHTML = modelOptions(strong);
    el('presetCheap').innerHTML = modelOptions(cheap);

    const noModels = !models.length;
    el('presetNoModels').classList.toggle('d-none', !noModels);
    el('presetNoModels').textContent = noModels ? t('Belum ada model dari 9router. Selesaikan langkah 9router dulu (provider terhubung di dashboard 9router).', 'No models from 9router yet. Finish the 9router step first (providers connected in the 9router dashboard).') : '';
    el('btnPreset').disabled = !strong;

    // Running the preset again starts from what it set up last time.
    if (!presetChoicesLoaded && nodes.some(isPreset)) {
      const on = role => nodes.some(n => n.role === role && isPreset(n) && n.isActive);
      el('presetTools').checked = on('ToolsNode');
      el('presetContext').checked = on('ContextKeeper');
      el('presetReviewer').checked = on('Reviewer');
      el('presetTester').checked = on('Tester');
    }
    presetChoicesLoaded = true;
  }

  function presetRow(item) {
    const n = item.node;
    const state = n.isActive
      ? (isEn ? '<span class="text-secondary">Testing…</span>' : '<span class="text-secondary">Menguji…</span>')
      : t('<span class="text-secondary">Dinonaktifkan (tidak dicentang)</span>', '<span class="text-secondary">Turned off (not ticked)</span>');
    return `<tr>
      <td class="text-nowrap">${escapeHtml(roleLabels[n.role] || n.role)}</td>
      <td>${escapeHtml(n.name)} ${item.created ? t('<span class="badge text-bg-primary">baru</span>', '<span class="badge text-bg-primary">new</span>') : t('<span class="badge text-bg-secondary">diperbarui</span>', '<span class="badge text-bg-secondary">updated</span>')}</td>
      <td class="mono small">${escapeHtml(n.model)}</td>
      <td class="small" data-preset-test="${n.id}">${state}</td>
    </tr>`;
  }

  async function testNode(node) {
    const cell = document.querySelector(`[data-preset-test="${node.id}"]`);
    try {
      const r = await api.post(`/api/nodes/${node.id}/test`);
      cell.innerHTML = r.success
        ? (isEn ? `<span class="text-success">✓ ${fmt.ms(r.latencyMs)} · reply "${escapeHtml((r.reply || '').substring(0, 30))}"</span>` : `<span class="text-success">✓ ${fmt.ms(r.latencyMs)} · balasan "${escapeHtml((r.reply || '').substring(0, 30))}"</span>`)
        : `<span class="text-danger">✕ ${escapeHtml(r.error)}</span>`;
      return r.success;
    } catch (error) {
      cell.innerHTML = `<span class="text-danger">✕ ${escapeHtml(error.message)}</span>`;
      return false;
    }
  }

  el('presetForm').addEventListener('submit', async event => {
    event.preventDefault();
    const strongModel = el('presetStrong').value;
    if (!strongModel) return toast(t('Pilih model kuat untuk planner.', 'Choose a strong model for the planner.'), 'warning');
    const button = el('btnPreset');
    button.disabled = true;
    button.textContent = (isEn ? 'Creating nodes…' : 'Membuat node…');
    try {
      const result = await api.post('/api/setup/preset', {
        strongModel,
        cheapModel: el('presetCheap').value || null,
        toolsNode: el('presetTools').checked,
        reviewer: el('presetReviewer').checked,
        tester: el('presetTester').checked,
        contextKeeper: el('presetContext').checked
      });
      el('presetResult').innerHTML = (isEn ? `<div class="table-responsive"><table class="table table-sm align-middle mb-0">
          <thead><tr><th>Role</th><th>Node</th><th>Model</th><th>Test</th></tr></thead>
          <tbody>${result.nodes.map(presetRow).join('')}</tbody>
        </table></div>` : `<div class="table-responsive"><table class="table table-sm align-middle mb-0">
          <thead><tr><th>Peran</th><th>Node</th><th>Model</th><th>Uji</th></tr></thead>
          <tbody>${result.nodes.map(presetRow).join('')}</tbody>
        </table></div>`);
      button.textContent = (isEn ? 'Testing nodes…' : 'Menguji node…');
      const active = result.nodes.map(i => i.node).filter(n => n.isActive);
      const passed = (await Promise.all(active.map(testNode))).filter(Boolean).length;
      toast(passed === active.length
        ? (isEn ? `${active.length} node(s) ready and answering.` : `${active.length} node siap dan menjawab.`)
        : (isEn ? `${active.length - passed} node(s) failed the test. Check the provider in 9router or choose another model.` : `${active.length - passed} node gagal diuji. Periksa provider di 9router atau pilih model lain.`), passed === active.length ? 'success' : 'warning');
      await Promise.all([loadStatus(), loadNodesStep()]);
    } catch (error) {
      toast(error.message, 'error');
    } finally {
      button.disabled = false;
      button.textContent = t('Buat node & uji', 'Create nodes & test');
    }
  });

  // --- Project ---

  async function loadProjects() {
    try {
      const list = await api.get('/api/projects');
      const shown = list.slice(0, 12);
      el('setupProjects').innerHTML = list.length
        ? `<div class="d-flex flex-wrap gap-1">${shown.map(p => `<a class="badge text-bg-secondary text-decoration-none" href="/Project/Detail/${p.id}" title="${escapeHtml(p.rootPath)}">${escapeHtml(p.name)}</a>`).join('')}${list.length > shown.length ? `<span class="text-secondary ms-1">+${list.length - shown.length} lainnya</span>` : ''}</div>`
        : t('<span class="text-secondary">Belum ada project terdaftar.</span>', '<span class="text-secondary">No project registered yet.</span>');
    } catch (error) {
      el('setupProjects').innerHTML = `<span class="text-danger">${escapeHtml(error.message)}</span>`;
    }
  }

  const projectsChanged = () => Promise.all([loadProjects(), loadStatus(), loadClis()]);

  el('btnSetupRegister').addEventListener('click', async () => {
    const path = el('setupProjectPath').value.trim();
    if (!path) return toast(t('Isi path folder project, atau klik Telusuri….', 'Fill in the project folder path, or click Browse….'), 'warning');
    try {
      const project = await api.post('/api/projects', { path });
      toast((isEn ? `Project ${project.name} registered.` : `Project ${project.name} terdaftar.`));
      el('setupProjectPath').value = '';
      await projectsChanged();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('btnSetupBrowse').addEventListener('click', () =>
    window.AIThread.projectBrowser.open({ startPath: el('setupProjectPath').value.trim(), onRegistered: projectsChanged }));

  // --- CLI ---

  let proxyUrl = '';
  let clis = [];

  function cliBadge(row) {
    if (!row.global) return t('<span class="badge text-bg-secondary">Status tidak terbaca</span>', '<span class="badge text-bg-secondary">Status unreadable</span>');
    if (row.global.enabled) return t('<span class="badge text-bg-success">Global aktif</span>', '<span class="badge text-bg-success">Global on</span>');
    if (row.projectCount) return (isEn ? `<span class="badge text-bg-primary">${row.projectCount} project(s) on</span>` : `<span class="badge text-bg-primary">${row.projectCount} project aktif</span>`);
    if (row.global.currentBaseUrl) return (isEn ? `<span class="badge text-bg-warning" title="${escapeHtml(row.global.currentBaseUrl)}">Points to another address</span>` : `<span class="badge text-bg-warning" title="${escapeHtml(row.global.currentBaseUrl)}">Mengarah ke alamat lain</span>`);
    return t('<span class="badge text-bg-secondary">Nonaktif</span>', '<span class="badge text-bg-secondary">Off</span>');
  }

  function cliRow(row) {
    const { cli, global } = row;
    return `<div class="list-group-item d-flex align-items-center gap-3">
      <div class="form-check form-switch mb-0">
        <input class="form-check-input" type="checkbox" role="switch" data-cli="${escapeHtml(cli.id)}" aria-label="${escapeHtml(cli.name)} global"
          ${global?.enabled ? 'checked' : ''} ${global ? '' : 'disabled'} />
      </div>
      <div class="flex-grow-1" style="min-width: 0">
        <div class="fw-semibold">${escapeHtml(cli.name)}</div>
        <div class="small text-secondary">${escapeHtml(cli.description)}</div>
      </div>
      <div class="text-end text-nowrap">${cliBadge(row)}</div>
    </div>`;
  }

  async function loadClis() {
    try {
      const list = await api.get('/api/cli');
      proxyUrl = list.proxyBaseUrl;
      clis = await Promise.all(list.clis.map(async cli => {
        const [global, projects] = await Promise.all([
          api.get(`/api/cli/${cli.id}/status?${new URLSearchParams({ scope: 'Global', proxyBaseUrl: proxyUrl })}`).catch(() => null),
          cli.projectFile ? api.get(`/api/cli/${cli.id}/projects?${new URLSearchParams({ proxyBaseUrl: proxyUrl })}`).catch(() => []) : []
        ]);
        return { cli, global, projectCount: projects.filter(p => p.enabled).length };
      }));
      el('setupClis').innerHTML = clis.map(cliRow).join('');
    } catch (error) {
      el('setupClis').innerHTML = `<div class="list-group-item small text-danger">${escapeHtml(error.message)}</div>`;
    }
  }

  el('setupClis').addEventListener('change', async event => {
    const input = event.target.closest('input[data-cli]');
    if (!input) return;
    const { cli } = clis.find(c => c.cli.id === input.dataset.cli);
    const enabled = input.checked;

    // Same rule as the CLI menu: a CLI with project files redirects every session, so it is confirmed.
    if (enabled && cli.projectFile && !confirm((isEn ? `Every ${cli.name} session on this computer will go through AIT. Continue?` : `Semua sesi ${cli.name} di komputer ini akan melewati AIT. Lanjutkan?`))) {
      input.checked = false;
      return;
    }

    input.disabled = true;
    try {
      await api.put(`/api/cli/${cli.id}/toggle`, { scope: 'Global', projectId: null, enabled, proxyBaseUrl: proxyUrl });
      toast(enabled ? (isEn ? `${cli.name} now points to AIT. ${cli.note || ''}` : `${cli.name} diarahkan ke AIT. ${cli.note || ''}`) : (isEn ? `${cli.name} no longer points to AIT.` : `${cli.name} tidak lagi diarahkan ke AIT.`), enabled ? 'success' : 'info');
    } catch (error) {
      toast(error.message, 'error');
    }
    await Promise.all([loadClis(), loadStatus()]);
  });

  // --- Team profile ---

  const profileModal = bootstrap.Modal.getOrCreateInstance(el('profileModal'));
  let profileDocument = null;
  let selectedSections = null;
  let applied = false;

  document.querySelectorAll('[data-profile-import]').forEach(button => button.addEventListener('click', () => el('profileFile').click()));

  el('profileFile').addEventListener('change', async () => {
    const file = el('profileFile').files[0];
    el('profileFile').value = '';
    if (!file) return;
    try {
      profileDocument = JSON.parse(await file.text());
    } catch {
      toast(t('File bukan JSON yang valid.', 'The file is not valid JSON.'), 'error');
      return;
    }
    el('profileInfo').textContent = `${file.name}`
      + (profileDocument?.exportedAt ? (isEn ? ` · exported ${fmt.dateTime(profileDocument.exportedAt)}` : ` · diekspor ${fmt.dateTime(profileDocument.exportedAt)}`) : '')
      + (profileDocument?.appVersion ? ` · AIT ${profileDocument.appVersion}` : '');
    selectedSections = null;
    applied = false;
    el('profileSections').innerHTML = '';
    el('profileSecrets').innerHTML = '';
    el('profileSecretsBox').classList.add('d-none');
    el('profileNodeMode').value = 'Merge';
    el('profileCopy').checked = true;
    el('btnProfileApply').textContent = (isEn ? 'Apply profile' : 'Terapkan profil');
    el('btnProfileClose').textContent = t('Batal', 'Cancel');
    profileModal.show();
    previewProfile();
  });

  function readSecrets() {
    const value = id => el(`secret-${id}`)?.value || null;
    return {
      nineRouterApiKey: value('nineRouterApiKey'),
      shortPassword: value('shortPassword'),
      longPassword: value('longPassword'),
      businessPassword: value('businessPassword')
    };
  }

  function importBody(dryRun) {
    return {
      profile: profileDocument,
      sections: selectedSections,
      nodeMode: el('profileNodeMode').value,
      secrets: readSecrets(),
      copyExistingMemory: el('profileCopy').checked,
      dryRun
    };
  }

  function renderSections(sections) {
    if (selectedSections === null) selectedSections = [...sections];
    el('profileSections').innerHTML = sections.map(s => `<div class="form-check mb-0">
        <input class="form-check-input" type="checkbox" id="section-${s}" data-section="${s}" ${selectedSections.includes(s) ? 'checked' : ''} />
        <label class="form-check-label small" for="section-${s}">${escapeHtml(sectionLabels[s] || s)}</label>
      </div>`).join('') || t('<span class="small text-secondary">Profil ini kosong.</span>', '<span class="small text-secondary">This profile is empty.</span>');
    el('profileNodeModeBox').classList.toggle('d-none', !selectedSections.includes('Nodes'));
    el('profileCopyBox').classList.toggle('d-none', !selectedSections.includes('Memory'));
  }

  // Typed values survive a re-render of the list.
  function renderSecrets(secrets) {
    const typed = readSecrets();
    el('profileSecretsBox').classList.toggle('d-none', !secrets.length);
    el('profileSecrets').innerHTML = secrets.map(s => `<div class="col-md-6">
        <label class="form-label small mb-1" for="secret-${s.id}">${escapeHtml(s.label)}</label>
        <input type="password" class="form-control form-control-sm mono" id="secret-${s.id}" data-secret autocomplete="new-password"
          placeholder="${s.stored ? t('tersimpan — kosongkan untuk tetap memakai', 'stored — leave empty to keep using it') : t('belum ada', 'none yet')}" />
      </div>`).join('');
    for (const [key, value] of Object.entries(typed)) {
      if (value && el(`secret-${key}`)) el(`secret-${key}`).value = value;
    }
  }

  function changesHtml(r) {
    const parts = [];
    if (!r.changes.length) {
      parts.push(`<div class="alert alert-secondary small mb-2">${r.dryRun ? t('Tidak ada perubahan: komputer ini sudah sesuai dengan profil.', 'No changes: this computer already matches the profile.') : t('Tidak ada yang perlu diubah.', 'Nothing needs to change.')}</div>`);
    } else {
      parts.push(`<div class="small fw-semibold mb-1">${r.dryRun ? t('Yang akan berubah', 'What will change') : t('Yang diterapkan', 'What was applied')}</div>`);
      for (const section of Object.keys(sectionLabels)) {
        const items = r.changes.filter(c => c.section === section);
        if (!items.length) continue;
        parts.push(`<div class="mb-2"><div class="small text-secondary">${escapeHtml(sectionLabels[section])}</div>
          <ul class="small mb-0">${items.map(c => `<li>${escapeHtml(c.text)}</li>`).join('')}</ul></div>`);
      }
    }
    if (r.warnings.length) {
      parts.push(`<div class="alert alert-warning small mb-2"><ul class="mb-0 ps-3">${r.warnings.map(w => `<li>${escapeHtml(w)}</li>`).join('')}</ul></div>`);
    }
    if (r.errors.length) {
      parts.push((isEn ? `<div class="alert alert-danger small mb-0"><div class="fw-semibold">Not applied yet</div><ul class="mb-0 ps-3">${r.errors.map(e => `<li>${escapeHtml(e)}</li>`).join('')}</ul>
        <div class="mt-1">Fix it (e.g. the password), then click <em>Try again</em>; the parts already applied do not change again.</div></div>` : `<div class="alert alert-danger small mb-0"><div class="fw-semibold">Belum diterapkan</div><ul class="mb-0 ps-3">${r.errors.map(e => `<li>${escapeHtml(e)}</li>`).join('')}</ul>
        <div class="mt-1">Perbaiki (mis. password), lalu klik <em>Coba lagi</em>; bagian yang sudah diterapkan tidak berubah lagi.</div></div>`));
    }
    return parts.join('');
  }

  async function previewProfile() {
    if (!profileDocument) return;
    el('btnProfileApply').disabled = true;
    el('profilePreview').innerHTML = t('<span class="small text-secondary">Memeriksa profil…</span>', '<span class="small text-secondary">Checking the profile…</span>');
    try {
      const r = await api.post('/api/setup/profile/import', importBody(true));
      renderSections(r.sections);
      renderSecrets(r.secrets);
      el('profilePreview').innerHTML = changesHtml(r);
      el('btnProfileApply').disabled = !selectedSections.length;
    } catch (error) {
      el('profilePreview').innerHTML = `<div class="alert alert-danger small mb-0">${escapeHtml(error.message)}</div>`;
    }
  }

  const previewSoon = debounce(previewProfile, 300);

  el('profileSections').addEventListener('change', () => {
    selectedSections = [...el('profileSections').querySelectorAll('[data-section]:checked')].map(box => box.dataset.section);
    previewSoon();
  });
  el('profileNodeMode').addEventListener('change', previewSoon);
  // A typed secret shows up in the preview (e.g. "API key 9router diganti") once the field is left.
  el('profileSecrets').addEventListener('change', previewSoon);

  el('btnProfileApply').addEventListener('click', async () => {
    if (applied) return profileModal.hide();
    const button = el('btnProfileApply');
    button.disabled = true;
    button.textContent = (isEn ? 'Applying…' : 'Menerapkan…');
    try {
      const r = await api.post('/api/setup/profile/import', importBody(false));
      el('profilePreview').innerHTML = changesHtml(r);
      if (r.errors.length) {
        toast((isEn ? `Profile partly applied: ${r.errors.length} part(s) failed.` : `Profil diterapkan sebagian: ${r.errors.length} bagian gagal.`), 'warning');
        button.textContent = t('Coba lagi', 'Try again');
      } else {
        toast((isEn ? `Profile applied: ${r.changes.length} change(s).` : `Profil diterapkan: ${r.changes.length} perubahan.`));
        applied = true;
        button.textContent = t('Tutup', 'Close');
        el('btnProfileClose').textContent = t('Tutup', 'Close');
      }
      await loadAll(true);
    } catch (error) {
      toast(error.message, 'error');
      button.textContent = (isEn ? 'Apply profile' : 'Terapkan profil');
    } finally {
      button.disabled = false;
    }
  });

  el('profileModal').addEventListener('hidden.bs.modal', () => {
    profileDocument = null;
    el('profileSecrets').innerHTML = '';
  });

  // --- Page ---

  function loadAll(refresh = false) {
    return Promise.all([loadStatus(refresh), loadRouter(), loadNodesStep(), loadProjects(), loadClis()]);
  }

  // A tour button pointing at a step of this page only changes the hash: close the tour and open that step.
  window.addEventListener('hashchange', () => {
    const step = document.getElementById(location.hash.slice(1));
    if (!step?.classList.contains('setup-step')) return;
    bootstrap.Modal.getInstance(document.getElementById('onboardingModal'))?.hide();
    step.setAttribute('open', '');
    step.scrollIntoView({ block: 'start', behavior: 'smooth' });
  });

  el('btnRecheck').addEventListener('click', () => loadAll(true));
  el('btnTour').addEventListener('click', () => document.getElementById('onboardingOpen')?.click());

  // Back from the 9router dashboard (another tab) with a provider connected or a key copied: check again.
  document.addEventListener('visibilitychange', debounce(() => {
    if (document.visibilityState === 'visible') Promise.all([loadStatus(true), loadNodesStep()]);
  }, 300));

  stream('types=node,project', {
    'node.changed': debounce(() => Promise.all([loadStatus(), loadNodesStep()]), 500),
    'project.changed': debounce(projectsChanged, 500)
  });

  loadAll(true);
})();
