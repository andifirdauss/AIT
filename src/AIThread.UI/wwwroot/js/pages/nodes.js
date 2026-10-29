(() => {
  'use strict';
  const { isEn, t, api, toast, escapeHtml, fmt, stream, debounce } = window.AIThread;
  const el = id => document.getElementById(id);
  const roles = ['Planner', 'ToolsNode', 'Executor', 'Reviewer', 'Tester', 'Wrapper', 'ContextKeeper'];
  const roleLabels = { ContextKeeper: 'Context Keeper', ToolsNode: 'Tools Node' };
  const defaults = {
    Planner: { maxTokens: 16384, concurrency: 2, temperature: 0.2 },
    ToolsNode: { maxTokens: 4096, concurrency: 4, temperature: 0.1 },
    Executor: { maxTokens: 8192, concurrency: 6, temperature: 0.1 },
    Reviewer: { maxTokens: 4096, concurrency: 2, temperature: 0.1 },
    Tester: { maxTokens: 4096, concurrency: 1, temperature: 0.1 },
    Wrapper: { maxTokens: 4096, concurrency: 4, temperature: 0.3 },
    ContextKeeper: { maxTokens: 4096, concurrency: 2, temperature: 0.2 }
  };
  // Planner tools a tools node can run instead (PlannerTools.Delegable), with what they cost the planner.
  const delegableTools = [
    ['list_directory', t('daftar isi folder', 'list folder contents')],
    ['find_files', t('cari file dengan glob', 'find files by glob')],
    ['search_text', t('cari teks — hasil bisa panjang', 'search text — results can be long')],
    ['find_symbol', t('cari deklarasi', 'find declarations')],
    ['find_references', t('cari pemakaian — hasil bisa panjang', 'find usages — results can be long')],
    ['read_file', t('baca file — paling boros token', 'read a file — uses the most tokens')],
    ['search_memory', t('cari label memory', 'search memory labels')],
    ['read_memory', t('baca isi memory / wiki', 'read memory / wiki content')],
    ['read_thread', t('baca thread sebelumnya', 'read an earlier thread')],
    ['git_status', t('file yang belum di-commit (hanya repo git)', 'uncommitted files (git repos only)')],
    ['git_diff', t('diff perubahan — hasil bisa panjang', 'diff of changes — results can be long')],
    ['git_log', t('riwayat commit', 'commit history')],
    ['git_show', t('isi satu commit — hasil bisa panjang', 'content of one commit — results can be long')],
    ['git_blame', t('commit terakhir per baris', 'last commit per line')],
    ['run_build', t('build proyek & daftar error', 'build the project & list errors')],
    ['run_command', t('perintah baca saja (curl GET localhost, git, versi) — planner tetap memegangnya untuk yang butuh izin', 'read-only commands (curl GET localhost, git, versions) — the planner keeps it for those that need permission')],
    ['web_fetch', t('baca halaman web/API (GET) — host luar hanya yang sudah diizinkan', 'read a web page/API (GET) — outside hosts only when already allowed')]
  ];
  const nodes = new Map();
  let models = [];
  let modelsLoaded = false;

  const modal = bootstrap.Modal.getOrCreateInstance(el('nodeModal'));

  // Open by default; once someone folds the guide it stays folded on this browser.
  const guide = el('nodeGuide');
  try {
    if (localStorage.getItem('aithread.nodeGuide') === 'closed') guide.open = false;
  } catch {
    // Storage unavailable: keep the default.
  }
  guide.addEventListener('toggle', () => {
    try {
      localStorage.setItem('aithread.nodeGuide', guide.open ? 'open' : 'closed');
    } catch {
      // Not remembered; harmless.
    }
  });

  // Every line is single-line (long names/models are truncated, full text in the tooltip) so all cards share one
  // height and line up across the role columns.
  function nodeCardBody(n) {
    const prompt = n.systemPrompt
      ? ` · <span title="${escapeHtml(n.systemPrompt)}">📝 instruksi</span>`
      : '';
    const skills = n.skills?.length
      ? ` · <span title="${n.skills.length} skill: ${escapeHtml(n.skills.map(s => s.name).join(', '))}">🧩 ${n.skills.length}</span>`
      : '';
    const fallback = n.fallbackNodeName
      ? (isEn ? ` · <span title="Fallback node: ${escapeHtml(n.fallbackNodeName)}">↪ ${escapeHtml(n.fallbackNodeName)}</span>` : ` · <span title="Node cadangan: ${escapeHtml(n.fallbackNodeName)}">↪ ${escapeHtml(n.fallbackNodeName)}</span>`)
      : '';
    return `${nodeCardHead(n)}
        <div class="small text-secondary mt-1 text-truncate">
          temp ${n.temperature ?? 'default'} · ${fmt.number(n.maxTokens)} token · ${n.maxConcurrency} paralel · prioritas ${n.priority}${prompt}${skills}${delegationBadge(n)}${testingBadge(n)}${fallback}
        </div>`;
  }

  // The tester Auto Testing uses: the active one with the smallest priority (NodeService.GetActiveAsync order).
  const activeTester = () => [...nodes.values()].filter(t => t.role === 'Tester' && t.isActive)
    .sort((a, b) => a.priority - b.priority || a.name.localeCompare(b.name))[0];

  // Planner with Auto Testing: tested by the active tester, if there is one.
  function testingBadge(n) {
    if (n.role !== 'Planner' || !n.autoTesting) return '';
    const tester = activeTester();
    return tester
      ? (isEn ? ` · <span title="Auto Testing: changes are tested by ${escapeHtml(tester.name)} (${escapeHtml(tester.model)}) after verification.">🧪 auto test</span>` : ` · <span title="Auto Testing: perubahan dites oleh ${escapeHtml(tester.name)} (${escapeHtml(tester.model)}) setelah verifikasi.">🧪 auto test</span>`)
      : t(' · <span class="text-warning" title="Auto Testing menyala, tetapi belum ada node Tester aktif — testing dilewati.">🧪 tanpa tester</span>', ' · <span class="text-warning" title="Auto Testing is on, but there is no active Tester node yet — testing is skipped.">🧪 no tester</span>');
  }

  // Planner: the tools it hands to tools nodes. Tools node: the planners that use it.
  function delegationBadge(n) {
    if (n.toolDelegations?.length) {
      const detail = n.toolDelegations
        .map(d => `${d.tool} → ${d.toolsNodeName}${d.toolsNodeActive ? '' : t(' (nonaktif: dijalankan planner)', ' (off: run by the planner)')}`)
        .join('\n');
      return ` · <span title="${escapeHtml(detail)}">🛰 ${n.toolDelegations.length} tool</span>`;
    }
    if (n.role !== 'ToolsNode') return '';
    const users = [...nodes.values()]
      .map(p => ({ name: p.name, tools: (p.toolDelegations || []).filter(d => d.toolsNodeId === n.id).map(d => d.tool) }))
      .filter(p => p.tools.length);
    if (!users.length) return t(' · <span title="Belum ada planner yang mendelegasikan tool ke node ini (Edit node Planner).">🛰 belum dipakai</span>', ' · <span title="No planner delegates tools to this node yet (edit the Planner node).">🛰 not used yet</span>');
    const detail = users.map(p => `${p.name}: ${p.tools.join(', ')}`).join('\n');
    return ` · <span title="${escapeHtml(detail)}">🛰 ${users.length} planner</span>`;
  }

  function nodeCardHead(n) {
    return (isEn ? `<div class="d-flex justify-content-between align-items-start gap-2">
          <div class="node-card-title">
            <div class="fw-semibold text-truncate" title="${escapeHtml(n.name)}">${escapeHtml(n.name)}
              ${n.isActive ? '' : '<span class="badge text-bg-secondary">off</span>'}</div>
            <div class="mono small text-truncate" title="${escapeHtml(n.model)}">${escapeHtml(n.model)}</div>
          </div>
          <div class="btn-group btn-group-sm flex-shrink-0">
            <button type="button" class="btn btn-outline-secondary" data-test="${n.id}">Test</button>
            <button type="button" class="btn btn-outline-secondary" data-edit="${n.id}">Edit</button>
            <button type="button" class="btn btn-outline-danger" data-delete="${n.id}">Delete</button>
          </div>
        </div>` : `<div class="d-flex justify-content-between align-items-start gap-2">
          <div class="node-card-title">
            <div class="fw-semibold text-truncate" title="${escapeHtml(n.name)}">${escapeHtml(n.name)}
              ${n.isActive ? '' : '<span class="badge text-bg-secondary">nonaktif</span>'}</div>
            <div class="mono small text-truncate" title="${escapeHtml(n.model)}">${escapeHtml(n.model)}</div>
          </div>
          <div class="btn-group btn-group-sm flex-shrink-0">
            <button type="button" class="btn btn-outline-secondary" data-test="${n.id}">Test</button>
            <button type="button" class="btn btn-outline-secondary" data-edit="${n.id}">Edit</button>
            <button type="button" class="btn btn-outline-danger" data-delete="${n.id}">Hapus</button>
          </div>
        </div>`);
  }

  function nodeCard(n) {
    return `<div class="card node-card role-${n.role} mb-2 ${n.isActive ? '' : 'inactive'}">
      <div class="card-body py-2">
        ${nodeCardBody(n)}
        <div class="small mt-1" data-test-result="${n.id}"></div>
      </div>
    </div>`;
  }

  // The empty slot holds an invisible sample card so it is exactly as tall as a real one.
  function emptyCard(role) {
    const sample = { name: '-', model: '-', isActive: true, temperature: 0, maxTokens: 0, maxConcurrency: 0, priority: 0 };
    return (isEn ? `<div class="card node-card node-empty mb-2">
      <div class="card-body py-2">
        <div class="node-empty-sample" aria-hidden="true">${nodeCardBody(sample)}</div>
        <div class="node-empty-label small">No ${escapeHtml((roleLabels[role] || role).toLowerCase())} yet.</div>
      </div>
    </div>` : `<div class="card node-card node-empty mb-2">
      <div class="card-body py-2">
        <div class="node-empty-sample" aria-hidden="true">${nodeCardBody(sample)}</div>
        <div class="node-empty-label small">Belum ada ${escapeHtml((roleLabels[role] || role).toLowerCase())}.</div>
      </div>
    </div>`);
  }

  async function loadNodes() {
    try {
      const list = await api.get('/api/nodes');
      nodes.clear();
      list.forEach(n => nodes.set(n.id, n));
      for (const role of roles) {
        const ofRole = list.filter(n => n.role === role);
        el(`nodes-${role}`).innerHTML = ofRole.length
          ? ofRole.map(nodeCard).join('')
          : emptyCard(role);
      }
    } catch (error) {
      toast(error.message, 'error');
    }
    loadPipeline();
    // "Automatic" task models follow the active planner.
    renderTasks();
  }

  async function loadPipeline() {
    try {
      const p = await api.get('/api/nodes/pipeline');
      const alert = el('pipelineAlert');
      alert.classList.remove('d-none', 'alert-success', 'alert-warning');
      alert.classList.add(p.isReady && !p.issues.length ? 'alert-success' : 'alert-warning');
      alert.innerHTML = p.isReady && !p.issues.length
        ? (isEn ? `Pipeline ready: ${p.plannerCount} planner · ${p.executorCount} executor · ${p.wrapperCount} wrapper.` : `Pipeline siap: ${p.plannerCount} planner · ${p.executorCount} executor · ${p.wrapperCount} wrapper.`)
        : `<strong>${p.isReady ? t('Pipeline siap, dengan catatan:', 'Pipeline ready, with notes:') : t('Pipeline belum lengkap:', 'Pipeline not complete yet:')}</strong> ${p.issues.map(escapeHtml).join(' ')}`;
    } catch {
      // Shown by the node load error already.
    }
  }

  async function loadModels(refresh = false) {
    try {
      models = await api.get(`/api/router/models${refresh ? '?refresh=true' : ''}`);
      modelsLoaded = true;
      if (refresh) toast((isEn ? `${models.length} models loaded from 9router.` : `${models.length} model dimuat dari 9router.`), 'info');
    } catch (error) {
      models = [];
      modelsLoaded = false;
      if (refresh) toast((isEn ? `Could not load the 9router models: ${error.message}` : `Model 9router gagal dimuat: ${error.message}`), 'error');
    }
  }

  // Searchable model combobox. Free text is always accepted, so an id missing from 9router can still be saved.
  const modelInput = el('nodeModel');
  const modelList = el('nodeModelList');
  let visibleModels = [];
  let activeIndex = -1;

  function groupOf(m) {
    return m.isCombo ? 'Combo' : (m.ownedBy || 'lainnya');
  }

  // Every whitespace-separated term must appear in the id or group; combos first, then one group per provider.
  function filterModels(query) {
    const terms = query.toLowerCase().split(/\s+/).filter(Boolean);
    const matches = models.filter(m => {
      const haystack = `${m.id} ${groupOf(m)}`.toLowerCase();
      return terms.every(t => haystack.includes(t));
    });
    return matches.sort((a, b) => (b.isCombo - a.isCombo) || groupOf(a).localeCompare(groupOf(b)));
  }

  function renderModelList() {
    const query = modelInput.value.trim();
    // Show the full list when the input still holds an exact, already-chosen id.
    visibleModels = models.some(m => m.id === query) ? filterModels('') : filterModels(query);
    activeIndex = visibleModels.findIndex(m => m.id === query);

    if (visibleModels.length === 0) {
      modelList.innerHTML = (isEn ? `<div class="dropdown-item-text small text-secondary">No matching model — id "${escapeHtml(query)}" will be used as it is.</div>` : `<div class="dropdown-item-text small text-secondary">Tidak ada model cocok — id "${escapeHtml(query)}" akan dipakai apa adanya.</div>`);
      return;
    }
    let html = '';
    let lastGroup = null;
    visibleModels.forEach((m, i) => {
      const group = groupOf(m);
      if (group !== lastGroup) {
        html += `<h6 class="dropdown-header">${escapeHtml(group)}</h6>`;
        lastGroup = group;
      }
      const detail = m.note || (m.contextLength ? `${Math.round(m.contextLength / 1000)}k ctx` : '');
      const extra = detail ? `<span class="text-secondary small ms-2">${escapeHtml(detail)}</span>` : '';
      html += `<button type="button" class="dropdown-item mono small ${i === activeIndex ? 'active' : ''}" role="option" data-index="${i}">${escapeHtml(m.id)}${extra}</button>`;
    });
    modelList.innerHTML = html;
    scrollActiveIntoView();
  }

  function scrollActiveIntoView() {
    modelList.querySelector('.dropdown-item.active')?.scrollIntoView({ block: 'nearest' });
  }

  function setActive(index) {
    if (visibleModels.length === 0) return;
    activeIndex = (index + visibleModels.length) % visibleModels.length;
    modelList.querySelectorAll('.dropdown-item').forEach(item =>
      item.classList.toggle('active', Number(item.dataset.index) === activeIndex));
    scrollActiveIntoView();
  }

  function openModelList() {
    if (!modelsLoaded || models.length === 0) return;
    renderModelList();
    modelList.classList.add('show');
    modelInput.setAttribute('aria-expanded', 'true');
  }

  function closeModelList() {
    modelList.classList.remove('show');
    modelInput.setAttribute('aria-expanded', 'false');
  }

  function chooseModel(index) {
    const m = visibleModels[index];
    if (!m) return;
    modelInput.value = m.id;
    closeModelList();
  }

  modelInput.addEventListener('focus', openModelList);
  modelInput.addEventListener('click', openModelList);
  modelInput.addEventListener('input', () => {
    openModelList();
    // While typing, pre-select the first match so Enter picks it.
    if (visibleModels.length && activeIndex < 0) setActive(0);
  });
  modelInput.addEventListener('blur', closeModelList);
  modelInput.addEventListener('keydown', event => {
    const isOpen = modelList.classList.contains('show');
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      if (!isOpen) return openModelList();
      setActive(activeIndex + (event.key === 'ArrowDown' ? 1 : -1));
    } else if (event.key === 'Enter' && isOpen && activeIndex >= 0) {
      event.preventDefault();
      chooseModel(activeIndex);
    } else if (event.key === 'Escape' && isOpen) {
      // Keep the modal open; only close the list.
      event.preventDefault();
      event.stopPropagation();
      closeModelList();
    }
  });
  // mousedown (not click) so the choice lands before the input's blur closes the list.
  modelList.addEventListener('mousedown', event => {
    event.preventDefault();
    const item = event.target.closest('[data-index]');
    if (item) chooseModel(Number(item.dataset.index));
  });

  function renderModelOptions(selected) {
    modelInput.value = selected || '';
    closeModelList();
    if (!modelsLoaded || models.length === 0) {
      modelInput.placeholder = t('Model tidak termuat — ketik id model, mis. cc/claude-sonnet-5', 'Models not loaded — type a model id, e.g. cc/claude-sonnet-5');
      el('modelHint').textContent = t('9router tidak dapat dihubungi — isi id model manual.', '9router cannot be reached — fill in the model id by hand.');
      return;
    }
    modelInput.placeholder = t('Cari model atau combo, mis. "sonnet" atau "combo"…', 'Search a model or combo, e.g. "sonnet" or "combo"…');
    const unknown = selected && !models.some(m => m.id === selected) ? t(' Model saat ini tidak ada di 9router.', ' The current model is not in 9router.') : '';
    el('modelHint').textContent = (isEn ? `${models.length} models available (${models.filter(m => m.isCombo).length} combo). 9router combos fall back across providers automatically.${unknown}` : `${models.length} model tersedia (${models.filter(m => m.isCombo).length} combo). Combo 9router otomatis fallback antar provider.${unknown}`);
  }

  // Nodes of the same role, except the one being edited; inactive ones are listed too (pure backups).
  function renderFallbackOptions(selected) {
    const id = el('nodeId').value;
    const role = el('nodeRole').value;
    const candidates = [...nodes.values()].filter(n => n.role === role && n.id !== id);
    el('nodeFallback').innerHTML = t('<option value="">Tidak ada</option>', '<option value="">None</option>') + candidates
      .map(n => `<option value="${n.id}" ${n.id === selected ? 'selected' : ''}>${escapeHtml(n.name)} — ${escapeHtml(n.model)}${n.isActive ? '' : t(' (nonaktif)', ' (off)')}</option>`)
      .join('');
  }

  // Only planners test their plans; the hint says which tester would run them.
  function renderAutoTesting(checked) {
    const isPlanner = el('nodeRole').value === 'Planner';
    el('nodeAutoTestingBox').classList.toggle('d-none', !isPlanner);
    el('nodeAutoTesting').checked = checked;
    const tester = activeTester();
    el('nodeAutoTestingHint').innerHTML = tester
      ? (isEn ? `Tester in use: <strong>${escapeHtml(tester.name)}</strong> <span class="mono">${escapeHtml(tester.model)}</span>.` : `Tester yang dipakai: <strong>${escapeHtml(tester.name)}</strong> <span class="mono">${escapeHtml(tester.model)}</span>.`)
      : t('<span class="text-warning">Belum ada node Tester aktif. Tambahkan node ber-role Tester dulu; tanpa itu testing dilewati.</span>', '<span class="text-warning">No active Tester node yet. Add a node with the Tester role first; without it testing is skipped.</span>');
  }

  // One select per delegable tool; only planners delegate. Keeps the choices made in the form when re-rendered.
  function renderDelegations(selected) {
    const isPlanner = el('nodeRole').value === 'Planner';
    el('nodeDelegationBox').classList.toggle('d-none', !isPlanner);
    if (!isPlanner) return;

    const chosen = new Map((selected || []).map(d => [d.tool, d.toolsNodeId]));
    const toolsNodes = [...nodes.values()].filter(n => n.role === 'ToolsNode');
    el('nodeDelegations').innerHTML = delegableTools.map(([tool, note]) => {
      const current = chosen.get(tool) || '';
      const options = toolsNodes
        .map(n => `<option value="${n.id}" ${n.id === current ? 'selected' : ''}>${escapeHtml(n.name)} — ${escapeHtml(n.model)}${n.isActive ? '' : t(' (nonaktif)', ' (off)')}</option>`)
        .join('');
      return `<div class="col-md-6">
          <label class="d-flex align-items-center gap-2 small mb-0" for="delegate-${tool}">
            <span class="mono text-nowrap" style="min-width: 9.5rem">${tool}</span>
            <select class="form-select form-select-sm" id="delegate-${tool}" data-delegate="${tool}" title="${escapeHtml(note)}" ${toolsNodes.length ? '' : 'disabled'}>
              <option value="">${isEn ? 'Planner itself' : 'Planner langsung'}</option>${options}
            </select>
          </label>
          <div class="text-secondary" style="font-size: .75rem; margin-left: 10rem">${escapeHtml(note)}</div>
        </div>`;
    }).join('');
    el('nodeDelegationHint').textContent = toolsNodes.length
      ? t('Saran hemat: read_file, search_text, find_references, git_diff dan git_show (output terbesar) ke tools node; find_symbol, list_directory dan git_status murah dibiarkan di planner.', 'Thrifty choice: read_file, search_text, find_references, git_diff and git_show (the largest output) to the tools node; the cheap find_symbol, list_directory and git_status stay with the planner.')
      : t('Belum ada node Tools Node. Tambahkan node ber-role Tools Node (model murah) dulu, lalu pilih di sini.', 'No Tools Node yet. Add a node with the Tools Node role (a cheap model) first, then choose it here.');
  }

  function readDelegations() {
    return [...el('nodeDelegations').querySelectorAll('select[data-delegate]')]
      .filter(select => select.value)
      .map(select => ({ tool: select.dataset.delegate, toolsNodeId: select.value }));
  }

  function applyRoleDefaults(role) {
    const d = defaults[role];
    el('nodeMaxTokens').value = d.maxTokens;
    el('nodeConcurrency').value = d.concurrency;
    el('nodeTemperature').value = d.temperature;
  }

  async function openModal(node, role = 'Executor') {
    if (!modelsLoaded) await loadModels();
    el('nodeId').value = node?.id || '';
    el('nodeName').value = node?.name || '';
    el('nodeRole').value = node?.role || role;
    el('nodePriority').value = node?.priority ?? 100;
    el('nodeSystemPrompt').value = node?.systemPrompt || '';
    nodeSkills = (node?.skills || []).map(s => ({ ...s }));
    el('nodeSkillPrompt').value = '';
    el('nodeSkillSuggestions').innerHTML = '';
    el('nodeSkillSearch').value = '';
    el('nodeSkillSearchResults').classList.remove('show');
    renderSkillChips();
    el('nodeActive').checked = node?.isActive ?? true;
    if (node) {
      el('nodeMaxTokens').value = node.maxTokens;
      el('nodeConcurrency').value = node.maxConcurrency;
      el('nodeTemperature').value = node.temperature ?? '';
    } else {
      applyRoleDefaults(el('nodeRole').value);
    }
    renderModelOptions(node?.model || '');
    renderFallbackOptions(node?.fallbackNodeId || '');
    renderDelegations(node?.toolDelegations || []);
    renderAutoTesting(node?.autoTesting ?? false);
    el('nodeModalTitle').textContent = node ? `Edit node · ${node.name}` : t('Tambah node', 'Add node');
    modal.show();
  }

  el('nodeRole').addEventListener('change', () => {
    if (!el('nodeId').value) applyRoleDefaults(el('nodeRole').value);
    renderFallbackOptions(el('nodeFallback').value);
    renderDelegations(readDelegations());
    renderAutoTesting(el('nodeAutoTesting').checked);
  });

  el('nodeForm').addEventListener('submit', async event => {
    event.preventDefault();
    const id = el('nodeId').value;
    const model = modelInput.value.trim();
    const temperature = el('nodeTemperature').value;
    const body = {
      name: el('nodeName').value.trim(),
      role: el('nodeRole').value,
      model,
      temperature: temperature === '' ? null : Number(temperature),
      maxTokens: Number(el('nodeMaxTokens').value),
      maxConcurrency: Number(el('nodeConcurrency').value),
      priority: Number(el('nodePriority').value),
      systemPrompt: el('nodeSystemPrompt').value.trim() || null,
      isActive: el('nodeActive').checked,
      skillIds: nodeSkills.map(s => s.id),
      fallbackNodeId: el('nodeFallback').value || null,
      toolDelegations: el('nodeRole').value === 'Planner' ? readDelegations() : [],
      autoTesting: el('nodeRole').value === 'Planner' && el('nodeAutoTesting').checked
    };
    try {
      if (id) await api.put(`/api/nodes/${id}`, body);
      else await api.post('/api/nodes', body);
      modal.hide();
      toast(t('Node tersimpan.', 'Node saved.'));
      loadNodes();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  document.querySelector('.node-columns').addEventListener('click', async event => {
    const button = event.target.closest('button');
    if (!button) return;

    if (button.dataset.edit) {
      openModal(nodes.get(button.dataset.edit));
    } else if (button.dataset.delete) {
      const node = nodes.get(button.dataset.delete);
      if (!confirm((isEn ? `Delete node "${node.name}"?` : `Hapus node "${node.name}"?`))) return;
      try {
        await api.del(`/api/nodes/${node.id}`);
        toast(t('Node dihapus.', 'Node deleted.'));
        loadNodes();
      } catch (error) {
        toast(error.message, 'error');
      }
    } else if (button.dataset.test) {
      const target = document.querySelector(`[data-test-result="${button.dataset.test}"]`);
      target.innerHTML = (isEn ? '<span class="text-secondary">Testing…</span>' : '<span class="text-secondary">Menguji…</span>');
      button.disabled = true;
      try {
        const r = await api.post(`/api/nodes/${button.dataset.test}/test`);
        target.innerHTML = r.success
          ? (isEn ? `<span class="text-success">✓ ${fmt.ms(r.latencyMs)} · ${r.tokens} tokens · reply: "${escapeHtml((r.reply || '').substring(0, 60))}"</span>` : `<span class="text-success">✓ ${fmt.ms(r.latencyMs)} · ${r.tokens} token · balasan: "${escapeHtml((r.reply || '').substring(0, 60))}"</span>`)
          : `<span class="text-danger">✕ ${escapeHtml(r.error)}</span>`;
      } catch (error) {
        target.innerHTML = `<span class="text-danger">✕ ${escapeHtml(error.message)}</span>`;
      } finally {
        button.disabled = false;
      }
    }
  });

  // --- Skills: suggested by the skill orchestrator, picked by the user, saved as part of the node ---

  const maxSkills = 10;
  let nodeSkills = [];
  let suggestions = [];

  function renderSkillChips() {
    el('nodeSkillChips').innerHTML = nodeSkills.length
      ? nodeSkills.map((s, i) => `<span class="badge rounded-pill text-bg-primary d-inline-flex align-items-center gap-1" title="${escapeHtml(s.description || '')}">
          🧩 ${escapeHtml(s.name)}
          <button type="button" class="btn-close btn-close-white" style="font-size: .55rem" data-remove-skill="${i}" aria-label="Lepas ${escapeHtml(s.name)}"></button>
        </span>`).join('')
      : t('<span class="small text-secondary">Belum ada skill.</span>', '<span class="small text-secondary">No skill yet.</span>');
  }

  el('nodeSkillChips').addEventListener('click', event => {
    const button = event.target.closest('[data-remove-skill]');
    if (!button) return;
    nodeSkills.splice(Number(button.dataset.removeSkill), 1);
    renderSkillChips();
    if (el('nodeSkillSearchResults').classList.contains('show')) renderSearchResults();
  });

  // --- Manual search: dropdown over the skill store, matching name, description, tags, source and SKILL.md content ---

  const maxSearchResults = 50;
  let searchResults = [];
  let searchTerms = [];
  let searchSeq = 0;

  function highlight(text, terms) {
    const safe = escapeHtml(text || '');
    if (!terms.length) return safe;
    const pattern = terms.map(t => escapeHtml(t).replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|');
    return safe.replace(new RegExp(`(${pattern})`, 'gi'), '<mark class="px-0">$1</mark>');
  }

  function renderSearchResults(message) {
    const target = el('nodeSkillSearchResults');
    if (message) {
      target.innerHTML = `<div class="dropdown-item-text small text-secondary">${message}</div>`;
    } else if (!searchResults.length) {
      target.innerHTML = searchTerms.length
        ? t('<div class="dropdown-item-text small text-secondary">Tidak ada skill yang cocok.</div>', '<div class="dropdown-item-text small text-secondary">No matching skill.</div>')
        : t('<div class="dropdown-item-text small text-secondary">Gudang skill kosong. Tambahkan sumber di menu <a href="/Skill">Skill</a>.</div>', '<div class="dropdown-item-text small text-secondary">The skill store is empty. Add a source in the <a href="/Skill">Skill</a> menu.</div>');
    } else {
      const shown = searchResults.slice(0, maxSearchResults);
      target.innerHTML = shown.map((s, i) => {
        const attached = nodeSkills.some(n => n.id === s.id);
        return `<button type="button" class="dropdown-item text-wrap border-bottom py-2 ${attached ? 'active' : ''}" data-search-result="${i}">
          <span class="fw-semibold">${attached ? '✓ ' : ''}${highlight(s.name, searchTerms)}</span>
          <span class="small text-secondary">· ${escapeHtml(s.sourceName)}${attached ? t(' · terpasang (klik untuk melepas)', ' · attached (click to detach)') : ''}</span>
          <span class="d-block small">${highlight(s.description, searchTerms)}</span>
          ${s.snippet ? (isEn ? `<span class="d-block small text-secondary fst-italic">content: ${highlight(s.snippet, searchTerms)}</span>` : `<span class="d-block small text-secondary fst-italic">isi: ${highlight(s.snippet, searchTerms)}</span>`) : ''}
        </button>`;
      }).join('') + (searchResults.length > shown.length
        ? (isEn ? `<div class="dropdown-item-text small text-secondary">${searchResults.length - shown.length} more skills — narrow the keywords.</div>` : `<div class="dropdown-item-text small text-secondary">${searchResults.length - shown.length} skill lain — persempit kata kunci.</div>`)
        : '');
    }
    target.classList.add('show');
  }

  async function searchSkills() {
    const query = el('nodeSkillSearch').value.trim();
    const seq = ++searchSeq;
    renderSearchResults((isEn ? 'Searching…' : 'Mencari…'));
    try {
      const results = await api.get(`/api/skills${query ? `?query=${encodeURIComponent(query)}` : ''}`);
      if (seq !== searchSeq) return;
      searchResults = results;
      searchTerms = query.split(/\s+/).filter(Boolean);
      renderSearchResults();
    } catch (error) {
      if (seq === searchSeq) renderSearchResults(`<span class="text-danger">${escapeHtml(error.message)}</span>`);
    }
  }

  function closeSearch() {
    el('nodeSkillSearchResults').classList.remove('show');
  }

  el('nodeSkillSearch').addEventListener('input', debounce(searchSkills, 250));
  el('nodeSkillSearch').addEventListener('focus', searchSkills);
  el('nodeSkillSearch').addEventListener('keydown', event => {
    if (event.key === 'Escape' && el('nodeSkillSearchResults').classList.contains('show')) {
      event.preventDefault();
      event.stopPropagation();
      closeSearch();
    } else if (event.key === 'Enter') {
      event.preventDefault();
    }
  });
  el('nodeSkillSearchResults').addEventListener('mousedown', event => event.preventDefault());
  el('nodeSkillSearchResults').addEventListener('click', event => {
    const item = event.target.closest('[data-search-result]');
    if (!item) return;
    const skill = searchResults[Number(item.dataset.searchResult)];
    const index = nodeSkills.findIndex(n => n.id === skill.id);
    if (index >= 0) {
      nodeSkills.splice(index, 1);
    } else if (nodeSkills.length >= maxSkills) {
      return toast((isEn ? `A node takes at most ${maxSkills} skills.` : `Satu node maksimal ${maxSkills} skill.`), 'info');
    } else {
      nodeSkills.push({ id: skill.id, name: skill.name, description: skill.description, sourceName: skill.sourceName });
    }
    renderSkillChips();
    renderSearchResults();
  });
  document.addEventListener('click', event => {
    if (!el('nodeSkillSearchBox').contains(event.target)) closeSearch();
  });

  function renderSuggestions() {
    const target = el('nodeSkillSuggestions');
    if (!suggestions.length) {
      target.innerHTML = t('<div class="small text-secondary">Tidak ada skill di gudang yang cocok. Coba tulis kebutuhan dengan kata lain, atau tambahkan sumber skill di menu <a href="/Skill">Skill</a>.</div>', '<div class="small text-secondary">No skill in the store matches. Try describing the need in other words, or add a skill source in the <a href="/Skill">Skill</a> menu.</div>');
      return;
    }
    const items = suggestions.map((s, i) => {
      const attached = nodeSkills.some(n => n.id === s.id);
      return `<label class="list-group-item d-flex gap-2 align-items-start">
        <input class="form-check-input mt-1" type="checkbox" data-suggestion="${i}" ${s.recommended || attached ? 'checked' : ''} ${attached ? 'disabled' : ''} />
        <span>
          <span class="fw-semibold">${escapeHtml(s.name)}</span>
          <span class="small text-secondary">· ${escapeHtml(s.sourceName)} · cocok ${Math.round(s.score * 100)}%${attached ? t(' · sudah terpasang', ' · already attached') : ''}</span>
          <span class="d-block small">${escapeHtml(s.reason || s.description)}</span>
        </span>
      </label>`;
    }).join('');
    target.innerHTML = (isEn ? `<div class="list-group list-group-flush border rounded mb-2">${items}</div>
      <div class="d-flex justify-content-end gap-2">
        <button type="button" class="btn btn-outline-secondary btn-sm" id="btnDismissSkills">Close</button>
        <button type="button" class="btn btn-primary btn-sm" id="btnApplySkills">Apply chosen skills</button>
      </div>` : `<div class="list-group list-group-flush border rounded mb-2">${items}</div>
      <div class="d-flex justify-content-end gap-2">
        <button type="button" class="btn btn-outline-secondary btn-sm" id="btnDismissSkills">Tutup</button>
        <button type="button" class="btn btn-primary btn-sm" id="btnApplySkills">Terapkan skill terpilih</button>
      </div>`);
  }

  el('btnSuggestSkills').addEventListener('click', async () => {
    const prompt = el('nodeSkillPrompt').value.trim();
    if (!prompt) {
      el('nodeSkillPrompt').focus();
      return toast(t('Tulis dulu kebutuhan node, mis. "aku butuh kamu bisa menjadi engineer yang handal".', 'Describe what the node needs first, e.g. "I need you to be a reliable engineer".'), 'info');
    }
    const button = el('btnSuggestSkills');
    button.disabled = true;
    el('nodeSkillSuggestions').innerHTML = t('<div class="small text-secondary">Skill Orchestrator sedang mencari di gudang skill…</div>', '<div class="small text-secondary">The Skill Orchestrator is searching the skill store…</div>');
    try {
      suggestions = await api.post('/api/skills/suggest', { prompt, role: el('nodeRole').value });
      renderSuggestions();
    } catch (error) {
      suggestions = [];
      el('nodeSkillSuggestions').innerHTML = `<div class="small text-danger">${escapeHtml(error.message)}</div>`;
    } finally {
      button.disabled = false;
    }
  });

  el('nodeSkillSuggestions').addEventListener('click', event => {
    if (event.target.id === 'btnDismissSkills') {
      suggestions = [];
      el('nodeSkillSuggestions').innerHTML = '';
    } else if (event.target.id === 'btnApplySkills') {
      const picked = Array.from(el('nodeSkillSuggestions').querySelectorAll('input[data-suggestion]:checked:not(:disabled)'))
        .map(input => suggestions[Number(input.dataset.suggestion)]);
      const room = maxSkills - nodeSkills.length;
      if (picked.length > room) toast((isEn ? `A node takes at most ${maxSkills} skills — only ${Math.max(room, 0)} added.` : `Satu node maksimal ${maxSkills} skill — hanya ${Math.max(room, 0)} yang ditambahkan.`), 'info');
      picked.slice(0, Math.max(room, 0)).forEach(s => nodeSkills.push({ id: s.id, name: s.name, description: s.description, sourceName: s.sourceName }));
      renderSkillChips();
      suggestions = [];
      el('nodeSkillSuggestions').innerHTML = picked.length
        ? t('<div class="small text-success">Skill ditempel. Klik Simpan untuk menyimpan node.</div>', '<div class="small text-success">Skills attached. Click Save to save the node.</div>')
        : '';
    }
  });

  // --- Background task models: the wiki writer and the Skill Orchestrator. Saved through the same settings API as the
  // Setting menu, so both places show one choice. ---
  const taskModal = bootstrap.Modal.getOrCreateInstance(el('taskModal'));
  const tasks = { wiki: null, skill: null };

  // The planner an empty task model falls back to (BusinessMemoryWriter/SkillAi.ResolveModelAsync).
  const activePlanner = () => [...nodes.values()].filter(n => n.role === 'Planner' && n.isActive)
    .sort((a, b) => a.priority - b.priority || a.name.localeCompare(b.name))[0];

  function taskModelLine(model) {
    if (model) return `<div class="mono small text-truncate" title="${escapeHtml(model)}">${escapeHtml(model)}</div>`;
    const planner = activePlanner();
    const text = planner
      ? (isEn ? `automatic → ${planner.model}` : `otomatis → ${planner.model}`)
      : t('otomatis — belum ada node Planner aktif', 'automatic — no active Planner node yet');
    return `<div class="mono small text-truncate ${planner ? 'text-secondary' : 'text-warning'}" title="${escapeHtml(text)}">${escapeHtml(text)}</div>`;
  }

  function taskCard(kind, role, name, model, detail, isActive, link) {
    return `<div class="card node-card role-${role} mb-2 ${isActive ? '' : 'inactive'}">
      <div class="card-body py-2">
        <div class="d-flex justify-content-between align-items-start gap-2">
          <div class="node-card-title">
            <div class="fw-semibold text-truncate" title="${escapeHtml(name)}">${escapeHtml(name)}
              ${isActive ? '' : `<span class="badge text-bg-secondary">${t('nonaktif', 'off')}</span>`}</div>
            ${taskModelLine(model)}
          </div>
          <div class="btn-group btn-group-sm flex-shrink-0">
            ${link ? `<a class="btn btn-outline-secondary" href="${link.href}">${link.label}</a>` : ''}
            <button type="button" class="btn btn-outline-secondary" data-task-edit="${kind}">Edit</button>
          </div>
        </div>
        <div class="small text-secondary mt-1 text-truncate" title="${escapeHtml(detail)}">${escapeHtml(detail)}</div>
      </div>
    </div>`;
  }

  function renderTasks() {
    const loading = t('<div class="small text-secondary">Memuat…</div>', '<div class="small text-secondary">Loading…</div>');
    const wiki = tasks.wiki;
    el('task-wiki').innerHTML = wiki
      ? taskCard('wiki', 'WikiWriter', t('Penulis wiki bisnis', 'Business wiki writer'), wiki.model,
        (wiki.enabled
          ? (isEn ? `automatic every ${wiki.intervalHours} h` : `otomatis tiap ${wiki.intervalHours} jam`)
          : t('otomatis mati — Perbarui sekarang di menu Memori tetap bisa', 'automatic off — Update now in the Memory menu still works'))
          + (wiki.effectiveDeveloperName ? ` · ${wiki.effectiveDeveloperName}` : ''),
        wiki.enabled, null)
      : loading;
    const skill = tasks.skill;
    const skilled = [...nodes.values()].filter(n => n.skills?.length).length;
    el('task-skill').innerHTML = skill
      ? taskCard('skill', 'SkillOrchestrator', 'Skill Orchestrator', skill.model,
        isEn
          ? `${fmt.number(skill.maxPromptChars)} skill characters per node · ${skilled} nodes use skills`
          : `batas ${fmt.number(skill.maxPromptChars)} karakter skill per node · ${skilled} node memakai skill`,
        true, { href: '/Skill', label: t('Gudang', 'Store') })
      : loading;
  }

  async function loadTasks() {
    try {
      [tasks.wiki, tasks.skill] = await Promise.all([api.get('/api/business/settings'), api.get('/api/skills/settings')]);
    } catch (error) {
      toast(error.message, 'error');
    }
    renderTasks();
  }

  async function openTaskModal(kind) {
    const s = tasks[kind];
    if (!s) return;
    if (!modelsLoaded) await loadModels();
    const isWiki = kind === 'wiki';
    el('taskKind').value = kind;
    el('taskModalTitle').textContent = isWiki ? 'Wiki Writer' : 'Skill Orchestrator';

    const planner = activePlanner();
    const option = id => `<option value="${escapeHtml(id)}">${escapeHtml(id)}</option>`;
    let html = `<option value="">${escapeHtml(planner
      ? (isEn ? `Automatic — the Planner node model (${planner.model})` : `Otomatis — model node Planner (${planner.model})`)
      : t('Otomatis — model node Planner', 'Automatic — the Planner node model'))}</option>`;
    if (s.model && !models.some(m => m.id === s.model)) html += option(s.model);
    html += models.map(m => `<option value="${escapeHtml(m.id)}">${m.isCombo ? '[combo] ' : ''}${escapeHtml(m.id)}</option>`).join('');
    el('taskModel').innerHTML = html;
    el('taskModel').value = s.model || '';
    el('taskModelHint').textContent = !modelsLoaded
      ? t('9router tidak dapat dihubungi — hanya model tersimpan yang bisa dipilih.', '9router cannot be reached — only the saved model can be chosen.')
      : isWiki
        ? t('Model yang teliti dan bisa membaca gambar (untuk screenshot), mis. model planner.', 'A careful model that can read images (for screenshots), e.g. the planner model.')
        : t('Pilih model yang pandai menilai, mis. model planner. Dipakai saat menarik link dan saat mencari skill untuk node.', 'Choose a model that judges well, e.g. the planner model. Used when pulling links and when finding skills for a node.');

    el('taskWikiFields').classList.toggle('d-none', !isWiki);
    el('taskSkillFields').classList.toggle('d-none', isWiki);
    if (isWiki) {
      el('taskWikiEnabled').checked = s.enabled;
      el('taskWikiInterval').value = s.intervalHours;
      el('taskWikiDeveloper').value = s.developerName || '';
      el('taskWikiDeveloper').placeholder = s.gitUserName || s.effectiveDeveloperName || '';
    } else {
      el('taskSkillMaxChars').value = s.maxPromptChars;
    }
    taskModal.show();
  }

  // Saved from the first-run popup while this page is open.
  document.addEventListener('aithread:developer-name', event => {
    tasks.wiki = event.detail;
    renderTasks();
  });

  el('taskColumns').addEventListener('click', event => {
    const button = event.target.closest('[data-task-edit]');
    if (button) openTaskModal(button.dataset.taskEdit);
  });

  el('taskForm').addEventListener('submit', async event => {
    event.preventDefault();
    const model = el('taskModel').value || null;
    try {
      if (el('taskKind').value === 'wiki') {
        tasks.wiki = await api.put('/api/business/settings', {
          enabled: el('taskWikiEnabled').checked,
          intervalHours: Number(el('taskWikiInterval').value),
          model,
          developerName: el('taskWikiDeveloper').value.trim() || null
        });
        toast(t('Pengaturan business memory tersimpan.', 'Business memory settings saved.'));
      } else {
        tasks.skill = await api.put('/api/skills/settings', { model, maxPromptChars: Number(el('taskSkillMaxChars').value) });
        toast(t('Pengaturan Skill tersimpan.', 'Skill settings saved.'));
      }
      taskModal.hide();
      renderTasks();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('btnAddNode').addEventListener('click', () => openModal(null));
  el('btnRefreshModels').addEventListener('click', () => loadModels(true));

  loadNodes();
  loadModels();
  loadTasks();
  // A pulled or deleted skill source can add or remove skills from nodes.
  stream('types=node', { 'node.changed': debounce(loadNodes, 300) });
})();
