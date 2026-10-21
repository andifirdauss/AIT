(() => {
  'use strict';
  const { api, toast, escapeHtml, fmt, stream, debounce } = window.AIThread;
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
    ['list_directory', 'daftar isi folder'],
    ['find_files', 'cari file dengan glob'],
    ['search_text', 'cari teks — hasil bisa panjang'],
    ['find_symbol', 'cari deklarasi'],
    ['find_references', 'cari pemakaian — hasil bisa panjang'],
    ['read_file', 'baca file — paling boros token'],
    ['search_memory', 'cari label memory'],
    ['read_memory', 'baca isi memory / wiki'],
    ['read_thread', 'baca thread sebelumnya'],
    ['git_status', 'file yang belum di-commit (hanya repo git)'],
    ['git_diff', 'diff perubahan — hasil bisa panjang'],
    ['git_log', 'riwayat commit'],
    ['git_show', 'isi satu commit — hasil bisa panjang'],
    ['git_blame', 'commit terakhir per baris'],
    ['run_build', 'build proyek & daftar error']
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
      ? ` · <span title="Node cadangan: ${escapeHtml(n.fallbackNodeName)}">↪ ${escapeHtml(n.fallbackNodeName)}</span>`
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
      ? ` · <span title="Auto Testing: perubahan dites oleh ${escapeHtml(tester.name)} (${escapeHtml(tester.model)}) setelah verifikasi.">🧪 auto test</span>`
      : ' · <span class="text-warning" title="Auto Testing menyala, tetapi belum ada node Tester aktif — testing dilewati.">🧪 tanpa tester</span>';
  }

  // Planner: the tools it hands to tools nodes. Tools node: the planners that use it.
  function delegationBadge(n) {
    if (n.toolDelegations?.length) {
      const detail = n.toolDelegations
        .map(d => `${d.tool} → ${d.toolsNodeName}${d.toolsNodeActive ? '' : ' (nonaktif: dijalankan planner)'}`)
        .join('\n');
      return ` · <span title="${escapeHtml(detail)}">🛰 ${n.toolDelegations.length} tool</span>`;
    }
    if (n.role !== 'ToolsNode') return '';
    const users = [...nodes.values()]
      .map(p => ({ name: p.name, tools: (p.toolDelegations || []).filter(d => d.toolsNodeId === n.id).map(d => d.tool) }))
      .filter(p => p.tools.length);
    if (!users.length) return ' · <span title="Belum ada planner yang mendelegasikan tool ke node ini (Edit node Planner).">🛰 belum dipakai</span>';
    const detail = users.map(p => `${p.name}: ${p.tools.join(', ')}`).join('\n');
    return ` · <span title="${escapeHtml(detail)}">🛰 ${users.length} planner</span>`;
  }

  function nodeCardHead(n) {
    return `<div class="d-flex justify-content-between align-items-start gap-2">
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
        </div>`;
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
    return `<div class="card node-card node-empty mb-2">
      <div class="card-body py-2">
        <div class="node-empty-sample" aria-hidden="true">${nodeCardBody(sample)}</div>
        <div class="node-empty-label small">Belum ada ${escapeHtml((roleLabels[role] || role).toLowerCase())}.</div>
      </div>
    </div>`;
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
  }

  async function loadPipeline() {
    try {
      const p = await api.get('/api/nodes/pipeline');
      const alert = el('pipelineAlert');
      alert.classList.remove('d-none', 'alert-success', 'alert-warning');
      alert.classList.add(p.isReady && !p.issues.length ? 'alert-success' : 'alert-warning');
      alert.innerHTML = p.isReady && !p.issues.length
        ? `Pipeline siap: ${p.plannerCount} planner · ${p.executorCount} executor · ${p.wrapperCount} wrapper.`
        : `<strong>${p.isReady ? 'Pipeline siap, dengan catatan:' : 'Pipeline belum lengkap:'}</strong> ${p.issues.map(escapeHtml).join(' ')}`;
    } catch {
      // Shown by the node load error already.
    }
  }

  async function loadModels(refresh = false) {
    try {
      models = await api.get(`/api/router/models${refresh ? '?refresh=true' : ''}`);
      modelsLoaded = true;
      if (refresh) toast(`${models.length} model dimuat dari 9router.`, 'info');
    } catch (error) {
      models = [];
      modelsLoaded = false;
      if (refresh) toast(`Model 9router gagal dimuat: ${error.message}`, 'error');
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
      modelList.innerHTML = `<div class="dropdown-item-text small text-secondary">Tidak ada model cocok — id "${escapeHtml(query)}" akan dipakai apa adanya.</div>`;
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
      modelInput.placeholder = 'Model tidak termuat — ketik id model, mis. cc/claude-sonnet-5';
      el('modelHint').textContent = '9router tidak dapat dihubungi — isi id model manual.';
      return;
    }
    modelInput.placeholder = 'Cari model atau combo, mis. "sonnet" atau "combo"…';
    const unknown = selected && !models.some(m => m.id === selected) ? ' Model saat ini tidak ada di 9router.' : '';
    el('modelHint').textContent = `${models.length} model tersedia (${models.filter(m => m.isCombo).length} combo). Combo 9router otomatis fallback antar provider.${unknown}`;
  }

  // Nodes of the same role, except the one being edited; inactive ones are listed too (pure backups).
  function renderFallbackOptions(selected) {
    const id = el('nodeId').value;
    const role = el('nodeRole').value;
    const candidates = [...nodes.values()].filter(n => n.role === role && n.id !== id);
    el('nodeFallback').innerHTML = '<option value="">Tidak ada</option>' + candidates
      .map(n => `<option value="${n.id}" ${n.id === selected ? 'selected' : ''}>${escapeHtml(n.name)} — ${escapeHtml(n.model)}${n.isActive ? '' : ' (nonaktif)'}</option>`)
      .join('');
  }

  // Only planners test their plans; the hint says which tester would run them.
  function renderAutoTesting(checked) {
    const isPlanner = el('nodeRole').value === 'Planner';
    el('nodeAutoTestingBox').classList.toggle('d-none', !isPlanner);
    el('nodeAutoTesting').checked = checked;
    const tester = activeTester();
    el('nodeAutoTestingHint').innerHTML = tester
      ? `Tester yang dipakai: <strong>${escapeHtml(tester.name)}</strong> <span class="mono">${escapeHtml(tester.model)}</span>.`
      : '<span class="text-warning">Belum ada node Tester aktif. Tambahkan node ber-role Tester dulu; tanpa itu testing dilewati.</span>';
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
        .map(n => `<option value="${n.id}" ${n.id === current ? 'selected' : ''}>${escapeHtml(n.name)} — ${escapeHtml(n.model)}${n.isActive ? '' : ' (nonaktif)'}</option>`)
        .join('');
      return `<div class="col-md-6">
          <label class="d-flex align-items-center gap-2 small mb-0" for="delegate-${tool}">
            <span class="mono text-nowrap" style="min-width: 9.5rem">${tool}</span>
            <select class="form-select form-select-sm" id="delegate-${tool}" data-delegate="${tool}" title="${escapeHtml(note)}" ${toolsNodes.length ? '' : 'disabled'}>
              <option value="">Planner langsung</option>${options}
            </select>
          </label>
          <div class="text-secondary" style="font-size: .75rem; margin-left: 10rem">${escapeHtml(note)}</div>
        </div>`;
    }).join('');
    el('nodeDelegationHint').textContent = toolsNodes.length
      ? 'Saran hemat: read_file, search_text, find_references, git_diff dan git_show (output terbesar) ke tools node; find_symbol, list_directory dan git_status murah dibiarkan di planner.'
      : 'Belum ada node Tools Node. Tambahkan node ber-role Tools Node (model murah) dulu, lalu pilih di sini.';
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
    el('nodeModalTitle').textContent = node ? `Edit node · ${node.name}` : 'Tambah node';
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
      toast('Node tersimpan.');
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
      if (!confirm(`Hapus node "${node.name}"?`)) return;
      try {
        await api.del(`/api/nodes/${node.id}`);
        toast('Node dihapus.');
        loadNodes();
      } catch (error) {
        toast(error.message, 'error');
      }
    } else if (button.dataset.test) {
      const target = document.querySelector(`[data-test-result="${button.dataset.test}"]`);
      target.innerHTML = '<span class="text-secondary">Menguji…</span>';
      button.disabled = true;
      try {
        const r = await api.post(`/api/nodes/${button.dataset.test}/test`);
        target.innerHTML = r.success
          ? `<span class="text-success">✓ ${fmt.ms(r.latencyMs)} · ${r.tokens} token · balasan: "${escapeHtml((r.reply || '').substring(0, 60))}"</span>`
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
      : '<span class="small text-secondary">Belum ada skill.</span>';
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
        ? '<div class="dropdown-item-text small text-secondary">Tidak ada skill yang cocok.</div>'
        : '<div class="dropdown-item-text small text-secondary">Gudang skill kosong. Tambahkan sumber di <a href="/Setting#skills">Setting → Skill</a>.</div>';
    } else {
      const shown = searchResults.slice(0, maxSearchResults);
      target.innerHTML = shown.map((s, i) => {
        const attached = nodeSkills.some(n => n.id === s.id);
        return `<button type="button" class="dropdown-item text-wrap border-bottom py-2 ${attached ? 'active' : ''}" data-search-result="${i}">
          <span class="fw-semibold">${attached ? '✓ ' : ''}${highlight(s.name, searchTerms)}</span>
          <span class="small text-secondary">· ${escapeHtml(s.sourceName)}${attached ? ' · terpasang (klik untuk melepas)' : ''}</span>
          <span class="d-block small">${highlight(s.description, searchTerms)}</span>
          ${s.snippet ? `<span class="d-block small text-secondary fst-italic">isi: ${highlight(s.snippet, searchTerms)}</span>` : ''}
        </button>`;
      }).join('') + (searchResults.length > shown.length
        ? `<div class="dropdown-item-text small text-secondary">${searchResults.length - shown.length} skill lain — persempit kata kunci.</div>`
        : '');
    }
    target.classList.add('show');
  }

  async function searchSkills() {
    const query = el('nodeSkillSearch').value.trim();
    const seq = ++searchSeq;
    renderSearchResults('Mencari…');
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
      return toast(`Satu node maksimal ${maxSkills} skill.`, 'info');
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
      target.innerHTML = '<div class="small text-secondary">Tidak ada skill di gudang yang cocok. Coba tulis kebutuhan dengan kata lain, atau tambahkan sumber skill di <a href="/Setting#skills">Setting → Skill</a>.</div>';
      return;
    }
    const items = suggestions.map((s, i) => {
      const attached = nodeSkills.some(n => n.id === s.id);
      return `<label class="list-group-item d-flex gap-2 align-items-start">
        <input class="form-check-input mt-1" type="checkbox" data-suggestion="${i}" ${s.recommended || attached ? 'checked' : ''} ${attached ? 'disabled' : ''} />
        <span>
          <span class="fw-semibold">${escapeHtml(s.name)}</span>
          <span class="small text-secondary">· ${escapeHtml(s.sourceName)} · cocok ${Math.round(s.score * 100)}%${attached ? ' · sudah terpasang' : ''}</span>
          <span class="d-block small">${escapeHtml(s.reason || s.description)}</span>
        </span>
      </label>`;
    }).join('');
    target.innerHTML = `<div class="list-group list-group-flush border rounded mb-2">${items}</div>
      <div class="d-flex justify-content-end gap-2">
        <button type="button" class="btn btn-outline-secondary btn-sm" id="btnDismissSkills">Tutup</button>
        <button type="button" class="btn btn-primary btn-sm" id="btnApplySkills">Terapkan skill terpilih</button>
      </div>`;
  }

  el('btnSuggestSkills').addEventListener('click', async () => {
    const prompt = el('nodeSkillPrompt').value.trim();
    if (!prompt) {
      el('nodeSkillPrompt').focus();
      return toast('Tulis dulu kebutuhan node, mis. "aku butuh kamu bisa menjadi engineer yang handal".', 'info');
    }
    const button = el('btnSuggestSkills');
    button.disabled = true;
    el('nodeSkillSuggestions').innerHTML = '<div class="small text-secondary">Skill Orchestrator sedang mencari di gudang skill…</div>';
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
      if (picked.length > room) toast(`Satu node maksimal ${maxSkills} skill — hanya ${Math.max(room, 0)} yang ditambahkan.`, 'info');
      picked.slice(0, Math.max(room, 0)).forEach(s => nodeSkills.push({ id: s.id, name: s.name, description: s.description, sourceName: s.sourceName }));
      renderSkillChips();
      suggestions = [];
      el('nodeSkillSuggestions').innerHTML = picked.length
        ? '<div class="small text-success">Skill ditempel. Klik Simpan untuk menyimpan node.</div>'
        : '';
    }
  });

  el('btnAddNode').addEventListener('click', () => openModal(null));
  el('btnRefreshModels').addEventListener('click', () => loadModels(true));

  loadNodes();
  loadModels();
  // A pulled or deleted skill source can add or remove skills from nodes.
  stream('types=node', { 'node.changed': debounce(loadNodes, 300) });
})();
