(() => {
  'use strict';
  const { api, toast, escapeHtml, fmt, toDate, stream, debounce } = window.AiThread;
  const el = id => document.getElementById(id);
  const shortMemoryHours = 24;
  const pageSize = 30;
  const items = new Map();
  let page = 1;
  let total = 0;
  let projects = [];

  const modal = bootstrap.Modal.getOrCreateInstance(el('memoryModal'));

  function labelChips(label) {
    return label.split(',').map(k => k.trim()).filter(Boolean).map(k => `<span class="chip">${escapeHtml(k)}</span>`).join('');
  }

  // Short memories move to the database once they are older than the short-term window.
  function levelCell(m) {
    if (m.level === 'Long') return '<span class="badge text-bg-secondary">Long</span>';
    const ageHours = (Date.now() - toDate(m.createdAt).getTime()) / 3600000;
    const left = Math.max(0, shortMemoryHours - ageHours);
    return `<span class="badge text-bg-info">Short</span><div class="small text-secondary">±${left.toFixed(0)} jam lagi ke long memory</div>`;
  }

  function row(m) {
    return `<tr>
      <td>${levelCell(m)}</td>
      <td><span class="badge ${m.type === 'Project' ? 'text-bg-primary' : 'text-bg-success'}">${m.type}</span></td>
      <td class="small">${escapeHtml(m.projectName || (m.type === 'Knowledge' ? 'semua project' : '–'))}</td>
      <td><div>${labelChips(m.label)}</div><div class="small text-secondary text-truncate-2">${escapeHtml(m.description)}</div></td>
      <td class="text-nowrap small" title="${fmt.dateTime(m.createdAt)}">${fmt.relative(m.createdAt)}${m.sourceThreadId ? `<div><a href="/Thread/Detail/${m.sourceThreadId}">thread</a></div>` : ''}</td>
      <td>${m.level === 'Long' ? fmt.number(m.hitCount) : '–'}</td>
      <td class="text-end text-nowrap">
        <button type="button" class="btn btn-sm btn-outline-secondary" data-edit="${m.id}">Buka</button>
        ${m.level === 'Short' ? `<button type="button" class="btn btn-sm btn-outline-info" data-promote="${m.id}" title="Pindahkan ke long memory sekarang">→ DB</button>` : ''}
        <button type="button" class="btn btn-sm btn-outline-danger" data-delete="${m.id}">Hapus</button>
      </td>
    </tr>`;
  }

  async function loadStats() {
    try {
      const s = await api.get('/api/memory/stats');
      el('statShort').textContent = fmt.number(s.shortCount);
      el('statLong').textContent = fmt.number(s.longCount);
      el('statProject').textContent = fmt.number(s.projectCount);
      el('statKnowledge').textContent = fmt.number(s.knowledgeCount);
      el('statRedis').innerHTML = s.redisConnected
        ? `<span class="text-success">Terhubung</span> · ${escapeHtml(s.shortStore || '')}`
        : `<span class="text-danger">${escapeHtml(s.shortStore || 'Short memory')} tidak terhubung — memory baru langsung ke long memory</span>`;
    } catch (error) {
      toast(error.message, 'error');
    }
  }

  async function loadProjects() {
    try {
      projects = await api.get('/api/projects');
    } catch {
      projects = [];
    }
    const options = projects.map(p => `<option value="${p.id}">${escapeHtml(p.name)}</option>`).join('');
    el('filterProject').innerHTML = `<option value="">Semua project</option>${options}`;
    el('memoryProject').innerHTML = options || '<option value="">(belum ada project)</option>';
    if (window.initialProjectId) el('filterProject').value = window.initialProjectId;
  }

  async function load() {
    const params = new URLSearchParams({ page, pageSize });
    if (el('filterLevel').value) params.set('level', el('filterLevel').value);
    if (el('filterType').value) params.set('type', el('filterType').value);
    if (el('filterProject').value) params.set('projectId', el('filterProject').value);
    if (el('filterSearch').value.trim()) params.set('search', el('filterSearch').value.trim());

    const tbody = el('memoryRows');
    try {
      const result = await api.get(`/api/memory?${params}`);
      total = result.total;
      items.clear();
      result.items.forEach(m => items.set(m.id, m));
      tbody.innerHTML = result.items.length
        ? result.items.map(row).join('')
        : '<tr><td colspan="7" class="empty-state">Belum ada memory. Memory terbentuk otomatis setelah thread selesai.</td></tr>';
      const from = total === 0 ? 0 : (page - 1) * pageSize + 1;
      el('pageInfo').textContent = `${from}–${Math.min(page * pageSize, total)} dari ${total}`;
      el('btnPrev').disabled = page <= 1;
      el('btnNext').disabled = page * pageSize >= total;
    } catch (error) {
      tbody.innerHTML = `<tr><td colspan="7" class="empty-state text-danger">${escapeHtml(error.message)}</td></tr>`;
    }
  }

  function syncProjectField() {
    const knowledge = el('memoryType').value === 'Knowledge';
    el('memoryProject').disabled = knowledge;
  }

  function openModal(memory) {
    el('memoryId').value = memory?.id || '';
    el('memoryLevel').value = memory?.level || '';
    el('memoryType').value = memory?.type || 'Project';
    el('memoryProject').value = memory?.projectId || el('filterProject').value || (projects[0]?.id ?? '');
    el('memoryLabel').value = memory?.label || '';
    el('memoryDescription').value = memory?.description || '';
    el('memoryModalTitle').textContent = memory ? `Memory · ${memory.level === 'Short' ? 'Short' : 'Long'}` : 'Tambah memory (masuk ke short memory)';
    el('memoryMeta').textContent = memory
      ? `Dibuat ${fmt.dateTime(memory.createdAt)}${memory.promotedAt ? ` · dipindah ke DB ${fmt.dateTime(memory.promotedAt)}` : ''}`
      : '';
    syncProjectField();
    modal.show();
  }

  el('memoryForm').addEventListener('submit', async event => {
    event.preventDefault();
    const id = el('memoryId').value;
    const type = el('memoryType').value;
    const body = {
      type,
      projectId: type === 'Knowledge' ? null : (el('memoryProject').value || null),
      label: el('memoryLabel').value.trim(),
      description: el('memoryDescription').value.trim()
    };
    try {
      if (id) await api.put(`/api/memory/${el('memoryLevel').value}/${id}`, body);
      else await api.post('/api/memory', body);
      modal.hide();
      toast('Memory tersimpan.');
      load();
      loadStats();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('memoryRows').addEventListener('click', async event => {
    const target = event.target.closest('button');
    if (!target) return;

    if (target.dataset.edit) {
      openModal(items.get(target.dataset.edit));
      return;
    }

    if (target.dataset.promote) {
      try {
        await api.post(`/api/memory/${target.dataset.promote}/promote`);
        toast('Memory dipindah ke long memory.');
        load();
        loadStats();
      } catch (error) {
        toast(error.message, 'error');
      }
      return;
    }

    if (target.dataset.delete) {
      const memory = items.get(target.dataset.delete);
      if (!confirm(`Hapus memory "${memory.label}"?`)) return;
      try {
        await api.del(`/api/memory/${memory.level}/${memory.id}`);
        toast('Memory dihapus.');
        load();
        loadStats();
      } catch (error) {
        toast(error.message, 'error');
      }
    }
  });

  el('btnConsolidate').addEventListener('click', async () => {
    try {
      const moved = await api.post('/api/memory/consolidate');
      toast(moved ? `${moved} memory dipindah ke long memory.` : 'Tidak ada short memory yang sudah lewat 1 hari.', 'info');
      load();
      loadStats();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  const resetAndLoad = () => { page = 1; load(); };
  el('btnAdd').addEventListener('click', () => openModal(null));
  el('memoryType').addEventListener('change', syncProjectField);
  ['filterLevel', 'filterType', 'filterProject'].forEach(id => el(id).addEventListener('change', resetAndLoad));
  el('filterSearch').addEventListener('input', debounce(resetAndLoad, 350));
  el('btnPrev').addEventListener('click', () => { page = Math.max(1, page - 1); load(); });
  el('btnNext').addEventListener('click', () => { page += 1; load(); });

  loadStats();
  loadProjects().then(load);
  stream('types=memory', { 'memory.changed': debounce(() => { load(); loadStats(); }, 500) });
})();
