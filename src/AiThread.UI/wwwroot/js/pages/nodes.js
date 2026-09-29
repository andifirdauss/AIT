(() => {
  'use strict';
  const { api, toast, escapeHtml, fmt, stream, debounce } = window.AiThread;
  const el = id => document.getElementById(id);
  const roles = ['Planner', 'Executor', 'Wrapper', 'ContextKeeper'];
  const roleLabels = { ContextKeeper: 'Context Keeper' };
  const defaults = {
    Planner: { maxTokens: 16384, concurrency: 2, temperature: 0.2 },
    Executor: { maxTokens: 8192, concurrency: 6, temperature: 0.1 },
    Wrapper: { maxTokens: 4096, concurrency: 4, temperature: 0.3 },
    ContextKeeper: { maxTokens: 4096, concurrency: 2, temperature: 0.2 }
  };
  const nodes = new Map();
  let models = [];
  let modelsLoaded = false;

  const modal = bootstrap.Modal.getOrCreateInstance(el('nodeModal'));

  function nodeCard(n) {
    return `<div class="card node-card role-${n.role} mb-2 ${n.isActive ? '' : 'inactive'}">
      <div class="card-body py-2">
        <div class="d-flex justify-content-between align-items-start gap-2">
          <div>
            <div class="fw-semibold">${escapeHtml(n.name)}
              ${n.backend === 'ClaudeCli' ? '<span class="badge text-bg-info">Claude CLI</span>' : ''}
              ${n.isActive ? '' : '<span class="badge text-bg-secondary">nonaktif</span>'}</div>
            <div class="mono small">${escapeHtml(n.model)}</div>
          </div>
          <div class="btn-group btn-group-sm">
            <button type="button" class="btn btn-outline-secondary" data-test="${n.id}">Test</button>
            <button type="button" class="btn btn-outline-secondary" data-edit="${n.id}">Edit</button>
            <button type="button" class="btn btn-outline-danger" data-delete="${n.id}">Hapus</button>
          </div>
        </div>
        <div class="small text-secondary mt-1">
          ${n.backend === 'ClaudeCli' ? '' : `temp ${n.temperature ?? 'default'} · ${fmt.number(n.maxTokens)} token · `}${n.maxConcurrency} paralel · prioritas ${n.priority}
        </div>
        ${n.systemPrompt ? `<div class="small mt-1 text-truncate-2" title="${escapeHtml(n.systemPrompt)}">📝 ${escapeHtml(n.systemPrompt)}</div>` : ''}
        <div class="small mt-1" data-test-result="${n.id}"></div>
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
          : `<div class="card"><div class="empty-state small">Belum ada ${(roleLabels[role] || role).toLowerCase()}.</div></div>`;
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

  // Aliases accepted by `claude --model` (always the latest model of that family); full model names can be typed.
  const cliModels = [
    { id: 'opus', ownedBy: 'Alias Claude Code', note: 'Opus terbaru' },
    { id: 'fable', ownedBy: 'Alias Claude Code', note: 'Fable terbaru' },
    { id: 'sonnet', ownedBy: 'Alias Claude Code', note: 'Sonnet terbaru — lebih cepat & hemat kuota' },
    { id: 'haiku', ownedBy: 'Alias Claude Code', note: 'Haiku terbaru — paling ringan' }
  ];

  const isCli = () => el('nodeBackend').value === 'ClaudeCli';
  const modelSource = () => isCli() ? cliModels : models;

  function groupOf(m) {
    return m.isCombo ? 'Combo' : (m.ownedBy || 'lainnya');
  }

  // Every whitespace-separated term must appear in the id or group; combos first, then one group per provider.
  function filterModels(query) {
    const terms = query.toLowerCase().split(/\s+/).filter(Boolean);
    const matches = modelSource().filter(m => {
      const haystack = `${m.id} ${groupOf(m)}`.toLowerCase();
      return terms.every(t => haystack.includes(t));
    });
    // The CLI aliases keep their curated order.
    return isCli() ? matches : matches.sort((a, b) => (b.isCombo - a.isCombo) || groupOf(a).localeCompare(groupOf(b)));
  }

  function renderModelList() {
    const query = modelInput.value.trim();
    // Show the full list when the input still holds an exact, already-chosen id.
    visibleModels = modelSource().some(m => m.id === query) ? filterModels('') : filterModels(query);
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
    if (!isCli() && (!modelsLoaded || models.length === 0)) return;
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

  // The CLI backend exists for planners only; other roles always go through 9router.
  function syncBackend() {
    const planner = el('nodeRole').value === 'Planner';
    el('nodeBackendGroup').classList.toggle('d-none', !planner);
    if (!planner) el('nodeBackend').value = 'Router';
    renderModelOptions(modelInput.value.trim());
  }

  function renderModelOptions(selected) {
    modelInput.value = selected || '';
    closeModelList();
    if (isCli()) {
      el('nodeModelLabel').textContent = 'Model Claude Code';
      modelInput.placeholder = 'Pilih alias (opus, sonnet, …) atau ketik nama model lengkap';
      el('modelHint').textContent = 'Diteruskan ke "claude --model". Temperature dan max tokens tidak dipakai oleh CLI. Klik Test setelah menyimpan untuk memastikan CLI terpasang & sudah login.';
      return;
    }
    el('nodeModelLabel').textContent = 'Model / combo 9router';
    if (!modelsLoaded || models.length === 0) {
      modelInput.placeholder = 'Model tidak termuat — ketik id model, mis. cc/claude-sonnet-5';
      el('modelHint').textContent = '9router tidak dapat dihubungi — isi id model manual.';
      return;
    }
    modelInput.placeholder = 'Cari model atau combo, mis. "sonnet" atau "combo"…';
    const unknown = selected && !models.some(m => m.id === selected) ? ' Model saat ini tidak ada di 9router.' : '';
    el('modelHint').textContent = `${models.length} model tersedia (${models.filter(m => m.isCombo).length} combo). Combo 9router otomatis fallback antar provider.${unknown}`;
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
    el('nodeBackend').value = node?.backend || 'Router';
    el('nodePriority').value = node?.priority ?? 100;
    el('nodeSystemPrompt').value = node?.systemPrompt || '';
    el('nodeActive').checked = node?.isActive ?? true;
    if (node) {
      el('nodeMaxTokens').value = node.maxTokens;
      el('nodeConcurrency').value = node.maxConcurrency;
      el('nodeTemperature').value = node.temperature ?? '';
    } else {
      applyRoleDefaults(el('nodeRole').value);
    }
    modelInput.value = node?.model || '';
    syncBackend();
    el('nodeModalTitle').textContent = node ? `Edit node · ${node.name}` : 'Tambah node';
    modal.show();
  }

  el('nodeRole').addEventListener('change', () => {
    if (!el('nodeId').value) applyRoleDefaults(el('nodeRole').value);
    syncBackend();
  });
  // A 9router id (provider/model) means nothing to the CLI and a CLI alias means nothing to 9router.
  el('nodeBackend').addEventListener('change', () => {
    const current = modelInput.value.trim();
    if (isCli() && !cliModels.some(m => m.id === current)) modelInput.value = cliModels[0].id;
    else if (!isCli() && cliModels.some(m => m.id === current)) modelInput.value = '';
    syncBackend();
  });

  el('nodeForm').addEventListener('submit', async event => {
    event.preventDefault();
    const id = el('nodeId').value;
    const model = modelInput.value.trim();
    const temperature = el('nodeTemperature').value;
    const body = {
      name: el('nodeName').value.trim(),
      role: el('nodeRole').value,
      backend: el('nodeBackend').value,
      model,
      temperature: temperature === '' ? null : Number(temperature),
      maxTokens: Number(el('nodeMaxTokens').value),
      maxConcurrency: Number(el('nodeConcurrency').value),
      priority: Number(el('nodePriority').value),
      systemPrompt: el('nodeSystemPrompt').value.trim() || null,
      isActive: el('nodeActive').checked
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

  document.querySelector('.row.g-3').addEventListener('click', async event => {
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

  el('btnAddNode').addEventListener('click', () => openModal(null));
  el('btnRefreshModels').addEventListener('click', () => loadModels(true));

  loadNodes();
  loadModels();
  stream('types=node', { 'node.changed': debounce(loadNodes, 300) });
})();
