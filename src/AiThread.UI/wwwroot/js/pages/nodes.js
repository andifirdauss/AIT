(() => {
  'use strict';
  const { api, toast, escapeHtml, fmt, stream, debounce } = window.AiThread;
  const el = id => document.getElementById(id);
  const roles = ['Planner', 'Executor', 'Wrapper'];
  const defaults = {
    Planner: { maxTokens: 16384, concurrency: 2, temperature: 0.2 },
    Executor: { maxTokens: 8192, concurrency: 6, temperature: 0.1 },
    Wrapper: { maxTokens: 4096, concurrency: 4, temperature: 0.3 }
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
            <div class="fw-semibold">${escapeHtml(n.name)} ${n.isActive ? '' : '<span class="badge text-bg-secondary">nonaktif</span>'}</div>
            <div class="mono small">${escapeHtml(n.model)}</div>
          </div>
          <div class="btn-group btn-group-sm">
            <button type="button" class="btn btn-outline-secondary" data-test="${n.id}">Test</button>
            <button type="button" class="btn btn-outline-secondary" data-edit="${n.id}">Edit</button>
            <button type="button" class="btn btn-outline-danger" data-delete="${n.id}">Hapus</button>
          </div>
        </div>
        <div class="small text-secondary mt-1">
          temp ${n.temperature ?? 'default'} · ${fmt.number(n.maxTokens)} token · ${n.maxConcurrency} paralel · prioritas ${n.priority}
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
          : `<div class="card"><div class="empty-state small">Belum ada ${role.toLowerCase()}.</div></div>`;
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

  // Combos first, then one group per provider prefix.
  function renderModelOptions(selected) {
    const select = el('nodeModel');
    const manual = el('nodeModelManual');
    if (!modelsLoaded || models.length === 0) {
      select.classList.add('d-none');
      manual.classList.remove('d-none');
      manual.value = selected || '';
      el('modelHint').textContent = '9router tidak dapat dihubungi — isi id model manual.';
      return;
    }

    select.classList.remove('d-none');
    manual.classList.add('d-none');
    const groups = new Map();
    for (const m of models) {
      const group = m.isCombo ? 'Combo' : (m.ownedBy || 'lainnya');
      if (!groups.has(group)) groups.set(group, []);
      groups.get(group).push(m);
    }
    let html = '<option value="">Pilih model…</option>';
    if (selected && !models.some(m => m.id === selected)) {
      html += `<option value="${escapeHtml(selected)}">${escapeHtml(selected)} (tidak ada di 9router)</option>`;
    }
    for (const [group, list] of groups) {
      html += `<optgroup label="${escapeHtml(group)}">` + list.map(m => {
        const ctx = m.contextLength ? ` · ${Math.round(m.contextLength / 1000)}k ctx` : '';
        return `<option value="${escapeHtml(m.id)}">${escapeHtml(m.id)}${ctx}</option>`;
      }).join('') + '</optgroup>';
    }
    select.innerHTML = html;
    select.value = selected || '';
    el('modelHint').textContent = `${models.length} model tersedia (${models.filter(m => m.isCombo).length} combo). Combo 9router otomatis fallback antar provider.`;
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
    el('nodeActive').checked = node?.isActive ?? true;
    if (node) {
      el('nodeMaxTokens').value = node.maxTokens;
      el('nodeConcurrency').value = node.maxConcurrency;
      el('nodeTemperature').value = node.temperature ?? '';
    } else {
      applyRoleDefaults(el('nodeRole').value);
    }
    renderModelOptions(node?.model);
    el('nodeModalTitle').textContent = node ? `Edit node · ${node.name}` : 'Tambah node';
    modal.show();
  }

  el('nodeRole').addEventListener('change', () => {
    if (!el('nodeId').value) applyRoleDefaults(el('nodeRole').value);
  });

  el('nodeForm').addEventListener('submit', async event => {
    event.preventDefault();
    const id = el('nodeId').value;
    const model = el('nodeModel').classList.contains('d-none') ? el('nodeModelManual').value.trim() : el('nodeModel').value;
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
