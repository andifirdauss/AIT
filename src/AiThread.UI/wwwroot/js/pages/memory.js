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

  function filterParams(params = new URLSearchParams()) {
    if (el('filterLevel').value) params.set('level', el('filterLevel').value);
    if (el('filterType').value) params.set('type', el('filterType').value);
    if (el('filterProject').value) params.set('projectId', el('filterProject').value);
    if (el('filterSearch').value.trim()) params.set('search', el('filterSearch').value.trim());
    return params;
  }

  async function load() {
    const params = filterParams(new URLSearchParams({ page, pageSize }));
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

  // Export: the memories matching the filters, downloaded as JSON. Fetched first so an error shows as a toast.
  el('btnExport').addEventListener('click', async () => {
    let response;
    try {
      response = await fetch(`/api/memory/export?${filterParams()}`);
    } catch {
      toast('Tidak dapat menghubungi server AIT.', 'error');
      return;
    }
    if (!response.ok) {
      const payload = await response.json().catch(() => null);
      toast(payload?.message || `HTTP ${response.status}`, 'error');
      return;
    }

    const name = /filename="?([^";]+)"?/.exec(response.headers.get('Content-Disposition') || '')?.[1] || 'ait-memory.json';
    const url = URL.createObjectURL(await response.blob());
    const link = Object.assign(document.createElement('a'), { href: url, download: name });
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    toast('File export diunduh. Isinya teks biasa — jangan dibagikan bila memuat info internal.', 'info');
  });

  // Import: a dry run first, shown in the modal; the confirm button runs the same request for real.
  const importModal = bootstrap.Modal.getOrCreateInstance(el('importModal'));
  let importDocument = null;

  function importSummary(r) {
    const projectRows = r.projects.map(p => `<tr>
        <td class="mono small">${escapeHtml(p.projectKey)}</td>
        <td class="small">${escapeHtml(p.projectName || '–')}</td>
        <td class="small">${fmt.number(p.count)}</td>
        <td class="small">${p.registered ? '<span class="text-success">terdaftar</span>' : '<span class="text-warning">belum terdaftar</span>'}</td>
      </tr>`).join('');
    const warnings = r.warnings.map(w => `<li>${escapeHtml(w)}</li>`).join('');
    return `
      <div class="row g-2 mb-3 text-center">
        <div class="col"><div class="stat-label">Di file</div><div class="fs-5">${fmt.number(r.total)}</div></div>
        <div class="col"><div class="stat-label">Ditambahkan</div><div class="fs-5 text-success">${fmt.number(r.added)}</div></div>
        <div class="col"><div class="stat-label">Ditimpa</div><div class="fs-5">${fmt.number(r.updated)}</div></div>
        <div class="col"><div class="stat-label">Sudah ada</div><div class="fs-5 text-secondary">${fmt.number(r.skipped + r.duplicates)}</div></div>
        <div class="col"><div class="stat-label">Tidak valid</div><div class="fs-5 ${r.invalid ? 'text-danger' : 'text-secondary'}">${fmt.number(r.invalid)}</div></div>
      </div>
      ${projectRows ? `<div class="small fw-semibold mb-1">Project di file</div>
        <div class="table-responsive mb-2"><table class="table table-sm mb-0">
          <thead><tr><th>Project key</th><th>Nama</th><th>Memory</th><th>Di mesin ini</th></tr></thead>
          <tbody>${projectRows}</tbody></table></div>
        <div class="small text-secondary mb-2">Memory project yang belum terdaftar tetap disimpan di bawah key-nya dan muncul begitu project itu ditambahkan.</div>` : ''}
      ${warnings ? `<div class="alert alert-warning small mb-0"><ul class="mb-0 ps-3">${warnings}</ul></div>` : ''}`;
  }

  async function previewImport() {
    el('btnImportConfirm').disabled = true;
    el('importPreview').innerHTML = '<span class="text-secondary small">Memeriksa file…</span>';
    try {
      const result = await api.post(`/api/memory/import?dryRun=true&mode=${el('importMode').value}`, importDocument);
      el('importPreview').innerHTML = importSummary(result);
      el('btnImportConfirm').disabled = result.added + result.updated === 0;
    } catch (error) {
      el('importPreview').innerHTML = `<div class="text-danger small">${escapeHtml(error.message)}</div>`;
    }
  }

  el('btnImport').addEventListener('click', () => el('importFile').click());
  el('importFile').addEventListener('change', async () => {
    const file = el('importFile').files[0];
    el('importFile').value = '';
    if (!file) return;
    try {
      importDocument = JSON.parse(await file.text());
    } catch {
      toast(`${file.name} bukan file JSON yang valid.`, 'error');
      return;
    }
    el('importFileInfo').textContent = `${file.name} · ${fmt.number(Math.ceil(file.size / 1024))} KB`
      + (importDocument?.exportedAt ? ` · diekspor ${fmt.dateTime(importDocument.exportedAt)}` : '')
      + (importDocument?.appVersion ? ` · AIT ${importDocument.appVersion}` : '');
    el('importMode').value = 'Skip';
    importModal.show();
    previewImport();
  });
  el('importMode').addEventListener('change', previewImport);
  el('btnImportConfirm').addEventListener('click', async () => {
    el('btnImportConfirm').disabled = true;
    try {
      const result = await api.post(`/api/memory/import?mode=${el('importMode').value}`, importDocument);
      importModal.hide();
      toast(`${result.added} memory ditambahkan, ${result.updated} ditimpa, ${result.skipped + result.duplicates} dilewati.`);
      load();
      loadStats();
    } catch (error) {
      toast(error.message, 'error');
      el('btnImportConfirm').disabled = false;
    }
  });
  el('importModal').addEventListener('hidden.bs.modal', () => { importDocument = null; });

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
