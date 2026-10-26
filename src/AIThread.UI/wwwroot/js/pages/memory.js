(() => {
  'use strict';
  const { api, toast, escapeHtml, fmt, toDate, stream, debounce } = window.AIThread;
  const el = id => document.getElementById(id);
  const shortMemoryHours = 24;
  const pageSize = 10;
  const maxKeywords = 16;
  const maxExtraModules = 3;
  const items = new Map();
  let page = 1;
  let total = 0;
  let projects = [];
  let reviewLoaded = false;

  const modal = bootstrap.Modal.getOrCreateInstance(el('memoryModal'));
  const reviewTab = el('review-tab');

  function chips(values) {
    return (values || []).map(k => `<span class="chip">${escapeHtml(k)}</span>`).join('');
  }

  // One segmented tag: [Area] Module(s) | Layers | Kind, the parts of MemoryLabels.Display. Every module of a memory
  // gets its own segment, the main one first.
  function labelTag(m) {
    const part = (text, cls = '') => `<span class="mem-tag-part ${cls}">${escapeHtml(text)}</span>`;
    const layers = (m.layers || []).join('/');
    return `<span class="mem-tag" title="${escapeHtml(m.label)}">`
      + (m.area ? part(m.area, 'mem-tag-area') : '')
      + [m.module, ...(m.extraModules || [])].map(name => part(name, 'mem-tag-module')).join('')
      + (layers ? part(layers) : '')
      + part(m.kind, 'mem-tag-kind')
      + '</span>';
  }

  // The shape AIT writes change entries in (ChangeLog.Render), shown in an empty description.
  const changeTemplate = '### Tambah kolom Deskripsi di User\n**Fitur:** ...\n\n**File berubah:**\n- `path/ke/file` (ubah) — ...\n\n**Validasi:**\n- ...\n\n**Flow:**\n1. ...\n\n**Catatan:**\n- ...';

  function keywords(values) {
    return (values || []).map(k => `<span class="mem-kw">${escapeHtml(k)}</span>`).join('');
  }

  // Labeled: the tag then the keywords. Unlabeled (older versions): the free label, until the relabeling worker (or a
  // save from the form) gives it a label.
  function labelCell(m) {
    if (m.labeled) {
      return `<div class="mem-label">${labelTag(m)}${keywords(m.keywords)}</div>`;
    }
    const old = m.label.split(',').map(k => k.trim()).filter(Boolean);
    const review = m.reviewReason ? `<div class="small text-warning">Perlu review: ${escapeHtml(m.reviewReason)}</div>` : '';
    return `<div><span class="badge text-bg-warning me-1">belum berlabel</span>${chips(old)}</div>${review}`;
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
      <td>${labelCell(m)}<div class="small text-secondary text-truncate-2">${escapeHtml(m.description)}</div></td>
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
      el('statUnlabeled').textContent = fmt.number(s.unlabeledCount);
      el('statReview').innerHTML = s.reviewCount
        ? `<a href="#review" data-open-review>${fmt.number(s.reviewCount)} perlu review</a>`
        : 'dilabeli ulang otomatis';
      el('reviewBadge').textContent = s.reviewCount ? fmt.number(s.reviewCount) : '';
      el('reviewBadge').classList.toggle('d-none', !s.reviewCount);
      el('statRedis').innerHTML = s.redisConnected
        ? `<span class="text-success">Terhubung</span> · ${escapeHtml(s.shortStore || '')}`
        : `<span class="text-danger">${escapeHtml(s.shortStore || 'Short memory')} tidak terhubung — memory baru langsung ke long memory</span>`;
    } catch (error) {
      toast(error.message, 'error');
    }
  }

  // The relabeling of memory written by older versions: shown while anything waits or runs.
  async function loadRelabel() {
    try {
      const s = await api.get('/api/memory/relabel');
      const show = s.running || s.pending > 0;
      el('relabelBanner').classList.toggle('d-none', !show);
      if (!show) return;
      const parts = [
        `<strong>Migrasi label memory</strong>: ${fmt.number(s.pending)} menunggu · ${fmt.number(s.labeled)} sudah berlabel · ${fmt.number(s.needsReview)} perlu review`,
        s.running
          ? `sedang berjalan${s.phase ? ` (${escapeHtml(s.phase)}${s.detail ? `: ${escapeHtml(s.detail)}` : ''})` : ''}`
          : (s.lastRunAt ? `terakhir ${fmt.relative(s.lastRunAt)}` : 'menunggu giliran'),
        s.lastMessage ? escapeHtml(s.lastMessage) : null,
        s.backupPath ? `backup: <span class="mono">${escapeHtml(s.backupPath)}</span>` : null
      ].filter(Boolean);
      el('relabelText').innerHTML = parts.join(' · ');
      el('btnRelabel').disabled = s.running;
    } catch {
      el('relabelBanner').classList.add('d-none');
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
    el('reviewProject').innerHTML = `<option value="">Semua project (memory saja)</option>${options}`;
    el('memoryProject').innerHTML = options || '<option value="">(belum ada project)</option>';
    if (window.initialProjectId) {
      el('filterProject').value = window.initialProjectId;
      el('reviewProject').value = window.initialProjectId;
    }
  }

  async function modulesOf(projectId, knowledge) {
    if (!projectId && !knowledge) return [];
    const params = new URLSearchParams({ knowledge });
    if (projectId) params.set('projectId', projectId);
    try {
      return await api.get(`/api/memory/modules?${params}`);
    } catch {
      return [];
    }
  }

  // The project's modules and the knowledge modules, for the module filter.
  async function loadModuleFilter() {
    const projectId = el('filterProject').value;
    const [project, knowledge] = await Promise.all([modulesOf(projectId, false), modulesOf(null, true)]);
    const option = m => `<option value="${escapeHtml(m.name)}">${escapeHtml(m.name)} (${fmt.number(m.memories)})</option>`;
    const current = el('filterModule').value;
    el('filterModule').innerHTML = '<option value="">Semua modul</option>'
      + (project.length ? `<optgroup label="Project">${project.map(option).join('')}</optgroup>` : '')
      + (knowledge.length ? `<optgroup label="Knowledge">${knowledge.map(option).join('')}</optgroup>` : '');
    if ([...project, ...knowledge].some(m => m.name === current)) el('filterModule').value = current;
  }

  function filterParams(params = new URLSearchParams()) {
    const set = (name, id) => { if (el(id).value) params.set(name, el(id).value); };
    set('level', 'filterLevel');
    set('type', 'filterType');
    set('projectId', 'filterProject');
    set('module', 'filterModule');
    set('layer', 'filterLayer');
    set('kind', 'filterKind');
    set('labeled', 'filterLabeled');
    if (el('filterSearch').value.trim()) params.set('search', el('filterSearch').value.trim());
    return params;
  }

  async function load() {
    const params = filterParams(new URLSearchParams({ page, pageSize }));
    const tbody = el('memoryRows');
    try {
      const result = await api.get(`/api/memory?${params}`);
      total = result.total;
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

  // --- Form: every save writes the full label ---

  function keywordList() {
    return el('memoryKeywords').value.split(',').map(k => k.trim()).filter(Boolean);
  }

  // --- Module fields: a dropdown of the key's modules; a new name is created on save, a rename applies right away ---

  const newModuleValue = '__new__';
  let formModules = [];
  let extraModules = [];
  let moduleEditMode = null; // 'new' | 'extra' | 'rename'

  function sameModule(a, b) {
    return (a || '').toLowerCase() === (b || '').toLowerCase();
  }

  function renderModuleSelect(selected) {
    const names = formModules.map(m => m.name);
    if (selected && !names.some(n => sameModule(n, selected))) names.push(selected);
    const option = name => {
      const info = formModules.find(m => m.name === name);
      const hint = info ? [info.description, info.aliases.join(', ')].filter(Boolean).join(' · ') : '';
      return `<option value="${escapeHtml(name)}" title="${escapeHtml(hint)}">${escapeHtml(name)}</option>`;
    };
    el('memoryModule').innerHTML = '<option value="" disabled>Pilih modul…</option>'
      + names.sort((a, b) => a.localeCompare(b)).map(option).join('')
      + `<option value="${newModuleValue}">＋ Modul baru…</option>`;
    el('memoryModule').value = names.find(n => sameModule(n, selected)) || '';
    el('btnModuleRename').disabled = !formModules.some(m => m.name === el('memoryModule').value);
  }

  function renderExtraModules() {
    const main = el('memoryModule').value;
    extraModules = extraModules.filter(name => !sameModule(name, main));
    el('memoryExtraModules').innerHTML = extraModules.map(name => `<span class="mem-chip">${escapeHtml(name)}`
      + `<button type="button" class="mem-chip-remove" data-remove-extra="${escapeHtml(name)}" title="Lepas ${escapeHtml(name)}" aria-label="Lepas ${escapeHtml(name)}">×</button></span>`).join('');
    const available = formModules.map(m => m.name).filter(name => !sameModule(name, main) && !extraModules.some(e => sameModule(e, name)));
    const full = extraModules.length >= maxExtraModules;
    el('memoryExtraSelect').innerHTML = `<option value="">${full ? 'Sudah 3 modul' : '＋ Tambah modul lain'}</option>`
      + (full ? '' : available.map(name => `<option value="${escapeHtml(name)}">${escapeHtml(name)}</option>`).join('')
        + `<option value="${newModuleValue}">＋ Modul baru…</option>`);
    el('memoryExtraSelect').disabled = full;
  }

  function openModuleEdit(mode) {
    moduleEditMode = mode;
    el('memoryModuleEditLabel').textContent = mode === 'rename' ? `Ubah nama ${el('memoryModule').value}` : mode === 'extra' ? 'Modul lain baru' : 'Modul baru';
    el('memoryModuleName').value = mode === 'rename' ? el('memoryModule').value : '';
    el('memoryModuleEdit').classList.remove('d-none');
    el('memoryModuleName').focus();
  }

  function closeModuleEdit() {
    moduleEditMode = null;
    el('memoryModuleEdit').classList.add('d-none');
  }

  function moduleScope() {
    const knowledge = el('memoryType').value === 'Knowledge';
    const params = new URLSearchParams({ knowledge });
    if (!knowledge && el('memoryProject').value) params.set('projectId', el('memoryProject').value);
    return params;
  }

  async function saveModuleEdit() {
    const name = el('memoryModuleName').value.trim();
    if (!name) {
      el('memoryModuleName').focus();
      return;
    }
    if (moduleEditMode === 'new') {
      renderModuleSelect(name);
      renderExtraModules();
    } else if (moduleEditMode === 'extra') {
      if (!extraModules.some(e => sameModule(e, name)) && !sameModule(name, el('memoryModule').value)) extraModules.push(name);
      renderExtraModules();
    } else if (moduleEditMode === 'rename') {
      const old = el('memoryModule').value;
      try {
        const module = await api.put(`/api/memory/modules/${encodeURIComponent(old)}?${moduleScope()}`, { name });
        extraModules = extraModules.map(e => sameModule(e, old) ? module.name : e);
        await syncModuleList(module.name);
        toast(`Modul ${old} → ${module.name}.`);
        refreshAll();
      } catch (error) {
        toast(error.message, 'error');
        return;
      }
    }
    closeModuleEdit();
  }

  function syncKeywordCount() {
    const count = keywordList().length;
    el('memoryKeywordCount').textContent = `(${count}/${maxKeywords})`;
    el('memoryKeywordCount').className = count > maxKeywords ? 'text-danger' : 'text-secondary';
  }

  async function syncModuleList(selected = el('memoryModule').value) {
    const knowledge = el('memoryType').value === 'Knowledge';
    formModules = await modulesOf(knowledge ? null : el('memoryProject').value, knowledge);
    renderModuleSelect(selected === newModuleValue ? '' : selected);
    renderExtraModules();
  }

  function syncProjectField(selected) {
    const knowledge = el('memoryType').value === 'Knowledge';
    el('memoryProject').disabled = knowledge;
    // Knowledge memory is never project specific: business rules, flows and conventions are project memory.
    // A module summary is written by AIT from the change entries (one per module): it can be edited, not added.
    const adding = !el('memoryId').value;
    [...el('memoryKind').options].forEach(o => {
      o.disabled = (knowledge && !['Pitfall', 'Pattern', 'Architecture'].includes(o.value)) || (adding && o.value === 'Summary');
    });
    if (el('memoryKind').selectedOptions[0]?.disabled) el('memoryKind').value = knowledge ? 'Pattern' : 'Change';
    closeModuleEdit();
    syncModuleList(selected);
  }

  function openModal(memory) {
    el('memoryId').value = memory?.id || '';
    el('memoryLevel').value = memory?.level || '';
    el('memoryType').value = memory?.type || 'Project';
    el('memoryProject').value = memory?.projectId || el('filterProject').value || (projects[0]?.id ?? '');
    extraModules = [...(memory?.extraModules || [])];
    el('memoryKind').value = memory?.kind || 'Change';
    el('memoryArea').value = memory?.area || '';
    document.querySelectorAll('#memoryLayers input').forEach(box => { box.checked = (memory?.layers || []).includes(box.value); });
    // An unlabeled memory starts from its old keywords.
    el('memoryKeywords').value = memory
      ? (memory.labeled ? memory.keywords : memory.label.split(',').map(k => k.trim()).filter(Boolean)).join(', ')
      : '';
    const legacy = memory?.legacyLabel || (memory && !memory.labeled ? memory.label : null);
    el('memoryLegacy').textContent = legacy ? `Label lama: ${legacy}` : '';
    el('memoryLegacy').classList.toggle('d-none', !legacy);
    el('memoryDescription').value = memory?.description || '';
    el('memoryDescription').placeholder = changeTemplate;
    el('memoryModalTitle').textContent = memory ? `Memory · ${memory.level === 'Short' ? 'Short' : 'Long'}` : 'Tambah memory (masuk ke short memory)';
    el('memoryMeta').textContent = memory
      ? `Dibuat ${fmt.dateTime(memory.createdAt)}${memory.promotedAt ? (memory.kind === 'Summary'
        ? ` · memuat catatan perubahan sampai ${fmt.dateTime(memory.promotedAt)}`
        : ` · dipindah ke DB ${fmt.dateTime(memory.promotedAt)}`) : ''}${memory.reviewReason ? ` · perlu review: ${memory.reviewReason}` : ''}`
      : '';
    syncProjectField(memory?.module || '');
    syncKeywordCount();
    modal.show();
  }

  el('memoryForm').addEventListener('submit', async event => {
    event.preventDefault();
    const keywords = keywordList();
    if (keywords.length > maxKeywords) {
      toast(`Maksimal ${maxKeywords} keyword.`, 'error');
      return;
    }
    if (!el('memoryModule').value || el('memoryModule').value === newModuleValue) {
      toast('Pilih modul dulu.', 'error');
      return;
    }
    const id = el('memoryId').value;
    const type = el('memoryType').value;
    const body = {
      type,
      projectId: type === 'Knowledge' ? null : (el('memoryProject').value || null),
      module: el('memoryModule').value,
      extraModules,
      kind: el('memoryKind').value,
      keywords,
      layers: [...document.querySelectorAll('#memoryLayers input:checked')].map(box => box.value),
      area: el('memoryArea').value.trim() || null,
      description: el('memoryDescription').value.trim()
    };
    try {
      if (id) await api.put(`/api/memory/${el('memoryLevel').value}/${id}`, body);
      else await api.post('/api/memory', body);
      modal.hide();
      toast('Memory tersimpan.');
      refreshAll();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  async function promote(id) {
    try {
      await api.post(`/api/memory/${id}/promote`);
      toast('Memory dipindah ke long memory.');
      refreshAll();
    } catch (error) {
      toast(error.message, 'error');
    }
  }

  async function remove(memory) {
    if (!confirm(`Hapus memory "${memory.label}"?`)) return;
    try {
      await api.del(`/api/memory/${memory.level}/${memory.id}`);
      toast('Memory dihapus.');
      refreshAll();
    } catch (error) {
      toast(error.message, 'error');
    }
  }

  el('memoryRows').addEventListener('click', event => {
    const target = event.target.closest('button');
    if (!target) return;
    if (target.dataset.edit) openModal(items.get(target.dataset.edit));
    else if (target.dataset.promote) promote(target.dataset.promote);
    else if (target.dataset.delete) remove(items.get(target.dataset.delete));
  });

  el('btnConsolidate').addEventListener('click', async () => {
    try {
      const moved = await api.post('/api/memory/consolidate');
      toast(moved ? `${moved} memory dipindah ke long memory.` : 'Tidak ada short memory yang sudah lewat 1 hari.', 'info');
      refreshAll();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('btnRelabel').addEventListener('click', async () => {
    try {
      await api.post('/api/memory/relabel');
      toast('Migrasi label dijalankan di belakang.', 'info');
      setTimeout(loadRelabel, 800);
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  // --- Perlu review: memories without a label, suggested merges, likely duplicates ---

  function memoryItem(m, actions) {
    return `<div class="list-group-item">
      <div class="d-flex flex-wrap align-items-start gap-2">
        <div class="me-auto">
          <div class="small text-secondary">${m.level} · ${m.type}${m.projectName ? ` · ${escapeHtml(m.projectName)}` : ''}</div>
          ${labelCell(m)}
          <div class="small text-truncate-2">${escapeHtml(m.description)}</div>
        </div>
        <div class="text-nowrap">${actions}</div>
      </div>
    </div>`;
  }

  async function loadReview() {
    reviewLoaded = true;
    const projectId = el('reviewProject').value;
    try {
      const review = await api.get(`/api/memory/review${projectId ? `?projectId=${projectId}` : ''}`);
      [...review.memories, ...review.duplicates.flatMap(d => [d.first, d.second])].forEach(m => items.set(m.id, m));

      el('reviewMemoryCount').textContent = review.memories.length ? `(${review.memories.length})` : '';
      el('reviewMemories').innerHTML = review.memories.length
        ? review.memories.map(m => memoryItem(m, `
            <button type="button" class="btn btn-sm btn-outline-primary" data-edit="${m.id}">Beri label</button>
            <button type="button" class="btn btn-sm btn-outline-danger" data-delete="${m.id}">Hapus</button>`)).join('')
        : '<div class="list-group-item text-secondary small">Tidak ada memory yang menunggu label.</div>';

      el('reviewMergeCount').textContent = review.merges.length ? `(${review.merges.length})` : '';
      el('reviewMerges').innerHTML = review.merges.length
        ? review.merges.map(g => `<div class="list-group-item d-flex flex-wrap align-items-center gap-2">
            <div class="me-auto">
              <span class="fw-semibold">${escapeHtml(g.from)}</span> → <span class="fw-semibold">${escapeHtml(g.into)}</span>
              ${g.confidence != null ? `<span class="badge text-bg-light border ms-1">keyakinan ${Math.round(g.confidence * 100)}%</span>` : ''}
              ${g.reason ? `<div class="small text-secondary">${escapeHtml(g.reason)}</div>` : ''}
            </div>
            <button type="button" class="btn btn-sm btn-outline-primary" data-accept="${g.id}">Gabungkan</button>
            <button type="button" class="btn btn-sm btn-outline-secondary" data-reject="${g.id}">Bukan duplikat</button>
          </div>`).join('')
        : `<div class="list-group-item text-secondary small">${projectId ? 'Tidak ada saran gabung modul.' : 'Pilih project untuk melihat saran gabung modulnya.'}</div>`;

      el('reviewDuplicateCount').textContent = review.duplicates.length ? `(${review.duplicates.length})` : '';
      el('reviewDuplicates').innerHTML = review.duplicates.length
        ? review.duplicates.map(d => `<div class="list-group-item">
            <div class="small text-secondary mb-1">Kemiripan keyword ${Math.round(d.similarity * 100)}%</div>
            <div class="row g-2">
              ${[d.first, d.second].map(m => `<div class="col-md-6"><div class="border rounded p-2 h-100">
                ${labelCell(m)}<div class="small text-truncate-2">${escapeHtml(m.description)}</div>
                <div class="mt-1"><button type="button" class="btn btn-sm btn-outline-secondary" data-edit="${m.id}">Buka</button>
                <button type="button" class="btn btn-sm btn-outline-danger" data-delete="${m.id}">Hapus</button></div>
              </div></div>`).join('')}
            </div>
          </div>`).join('')
        : '<div class="list-group-item text-secondary small">Tidak ada kandidat duplikat.</div>';
    } catch (error) {
      el('reviewMemories').innerHTML = `<div class="list-group-item text-danger small">${escapeHtml(error.message)}</div>`;
    }
  }

  el('tab-review').addEventListener('click', async event => {
    const target = event.target.closest('button');
    if (!target) return;
    if (target.dataset.edit) return openModal(items.get(target.dataset.edit));
    if (target.dataset.delete) return remove(items.get(target.dataset.delete));
    const action = target.dataset.accept ? 'accept' : target.dataset.reject ? 'reject' : null;
    if (!action) return;
    try {
      await api.post(`/api/memory/merges/${target.dataset.accept || target.dataset.reject}/${action}`);
      toast(action === 'accept' ? 'Modul digabung.' : 'Saran ditolak.');
      refreshAll();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  reviewTab.addEventListener('shown.bs.tab', () => { if (!reviewLoaded) loadReview(); });
  el('reviewProject').addEventListener('change', loadReview);
  document.addEventListener('click', event => {
    if (!event.target.closest('[data-open-review]')) return;
    event.preventDefault();
    bootstrap.Tab.getOrCreateInstance(reviewTab).show();
  });
  if (location.hash === '#review') bootstrap.Tab.getOrCreateInstance(reviewTab).show();

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
    const unitTests = r.unitTestsAdded + r.unitTestsUpdated + r.unitTestsSkipped > 0
      ? `<div class="small mb-2"><span class="fw-semibold">Katalog unit test:</span> ${fmt.number(r.unitTestsAdded)} ditambahkan, ${fmt.number(r.unitTestsUpdated)} diperbarui,
          ${fmt.number(r.unitTestsSkipped)} sudah ada${r.unitTestsRenumbered ? `, ${fmt.number(r.unitTestsRenumbered)} mendapat nomor baru` : ''}.
          <span class="text-secondary">Snapshot test ikut masuk (tidak ditampilkan) dan dipakai untuk memulihkan test yang hilang.</span></div>`
      : '';
    return `
      <div class="row g-2 mb-3 text-center">
        <div class="col"><div class="stat-label">Di file</div><div class="fs-5">${fmt.number(r.total)}</div></div>
        <div class="col"><div class="stat-label">Ditambahkan</div><div class="fs-5 text-success">${fmt.number(r.added)}</div></div>
        <div class="col"><div class="stat-label">Ditimpa</div><div class="fs-5">${fmt.number(r.updated)}</div></div>
        <div class="col"><div class="stat-label">Sudah ada</div><div class="fs-5 text-secondary">${fmt.number(r.skipped + r.duplicates)}</div></div>
        <div class="col"><div class="stat-label">Tidak valid</div><div class="fs-5 ${r.invalid ? 'text-danger' : 'text-secondary'}">${fmt.number(r.invalid)}</div></div>
      </div>
      ${unitTests}
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
      el('btnImportConfirm').disabled = result.added + result.updated + result.unitTestsAdded + result.unitTestsUpdated === 0;
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
      toast(`${result.added} memory ditambahkan, ${result.updated} ditimpa, ${result.skipped + result.duplicates} dilewati.`
        + (result.unitTestsAdded + result.unitTestsUpdated ? ` Unit test: ${result.unitTestsAdded} ditambahkan, ${result.unitTestsUpdated} diperbarui.` : ''));
      refreshAll();
    } catch (error) {
      toast(error.message, 'error');
      el('btnImportConfirm').disabled = false;
    }
  });
  el('importModal').addEventListener('hidden.bs.modal', () => { importDocument = null; });

  function refreshAll() {
    load();
    loadModuleFilter();
    loadStats();
    loadRelabel();
    if (reviewLoaded) loadReview();
  }

  const resetAndLoad = () => { page = 1; load(); };
  el('btnAdd').addEventListener('click', () => openModal(null));
  el('memoryType').addEventListener('change', () => syncProjectField(el('memoryModule').value));
  el('memoryProject').addEventListener('change', () => syncModuleList());
  el('memoryModule').addEventListener('change', () => {
    closeModuleEdit();
    if (el('memoryModule').value === newModuleValue) {
      el('memoryModule').value = '';
      el('btnModuleRename').disabled = true;
      openModuleEdit('new');
      return;
    }
    el('btnModuleRename').disabled = !formModules.some(m => m.name === el('memoryModule').value);
    renderExtraModules();
  });
  el('btnModuleRename').addEventListener('click', () => openModuleEdit('rename'));
  el('memoryExtraSelect').addEventListener('change', () => {
    const value = el('memoryExtraSelect').value;
    if (value === newModuleValue) openModuleEdit('extra');
    else if (value) extraModules.push(value);
    renderExtraModules();
  });
  el('memoryExtraModules').addEventListener('click', event => {
    const button = event.target.closest('[data-remove-extra]');
    if (!button) return;
    extraModules = extraModules.filter(name => name !== button.dataset.removeExtra);
    renderExtraModules();
  });
  el('btnModuleNameSave').addEventListener('click', saveModuleEdit);
  el('btnModuleNameCancel').addEventListener('click', () => {
    if (moduleEditMode === 'new') renderModuleSelect('');
    closeModuleEdit();
  });
  el('memoryModuleName').addEventListener('keydown', event => {
    if (event.key === 'Enter') {
      event.preventDefault();
      saveModuleEdit();
    } else if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      closeModuleEdit();
    }
  });
  el('memoryKeywords').addEventListener('input', syncKeywordCount);
  ['filterLevel', 'filterType', 'filterModule', 'filterLayer', 'filterKind', 'filterLabeled'].forEach(id => el(id).addEventListener('change', resetAndLoad));
  el('filterProject').addEventListener('change', () => { loadModuleFilter(); resetAndLoad(); });
  el('filterSearch').addEventListener('input', debounce(resetAndLoad, 350));
  el('btnPrev').addEventListener('click', () => { page = Math.max(1, page - 1); load(); });
  el('btnNext').addEventListener('click', () => { page += 1; load(); });

  loadStats();
  loadRelabel();
  loadProjects().then(() => { loadModuleFilter(); load(); });
  stream('types=memory', { 'memory.changed': debounce(refreshAll, 500) });
})();
