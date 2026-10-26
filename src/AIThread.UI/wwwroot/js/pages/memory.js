(() => {
  'use strict';
  const { isEn, t, api, toast, escapeHtml, fmt, toDate, stream, debounce } = window.AIThread;
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
  const changeTemplate = t('### Tambah kolom Deskripsi di User\n**Fitur:** ...\n\n**File berubah:**\n- `path/ke/file` (ubah) — ...\n\n**Validasi:**\n- ...\n\n**Flow:**\n1. ...\n\n**Catatan:**\n- ...', '### Add a Description column to User\n**Feature:** ...\n\n**Changed files:**\n- `path/to/file` (modified) — ...\n\n**Validation:**\n- ...\n\n**Flow:**\n1. ...\n\n**Notes:**\n- ...');

  // The description is Markdown (change entries and module summaries are written that way): Tulis edits the text,
  // Pratinjau renders it on the server like a wiki page (no raw HTML, safe links).
  let previewRequest = 0;
  async function setDescriptionPreview(on) {
    el('btnMemoryWrite').classList.toggle('active', !on);
    el('btnMemoryPreview').classList.toggle('active', on);
    el('memoryDescription').classList.toggle('d-none', on);
    el('memoryPreview').classList.toggle('d-none', !on);
    const request = ++previewRequest;
    if (!on) return;
    const text = el('memoryDescription').value;
    if (!text.trim()) {
      el('memoryPreview').innerHTML = t('<span class="text-secondary">Deskripsi masih kosong.</span>', '<span class="text-secondary">The description is still empty.</span>');
      return;
    }
    el('memoryPreview').innerHTML = t('<span class="text-secondary">Memuat pratinjau…</span>', '<span class="text-secondary">Loading preview…</span>');
    try {
      const html = await api.post('/api/business/preview', { content: text });
      if (request === previewRequest) el('memoryPreview').innerHTML = html;
    } catch (error) {
      if (request === previewRequest) el('memoryPreview').innerHTML = `<span class="text-danger">${escapeHtml(error.message)}</span>`;
    }
  }

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
    const review = m.reviewReason ? (isEn ? `<div class="small text-warning">Needs review: ${escapeHtml(m.reviewReason)}</div>` : `<div class="small text-warning">Perlu review: ${escapeHtml(m.reviewReason)}</div>`) : '';
    return (isEn ? `<div><span class="badge text-bg-warning me-1">unlabeled</span>${chips(old)}</div>${review}` : `<div><span class="badge text-bg-warning me-1">belum berlabel</span>${chips(old)}</div>${review}`);
  }

  // Short memories move to the database once they are older than the short-term window.
  function levelCell(m) {
    if (m.level === 'Long') return '<span class="badge text-bg-secondary">Long</span>';
    const ageHours = (Date.now() - toDate(m.createdAt).getTime()) / 3600000;
    const left = Math.max(0, shortMemoryHours - ageHours);
    return (isEn ? `<span class="badge text-bg-info">Short</span><div class="small text-secondary">±${left.toFixed(0)} h left before long memory</div>` : `<span class="badge text-bg-info">Short</span><div class="small text-secondary">±${left.toFixed(0)} jam lagi ke long memory</div>`);
  }

  // Branch the memory's thread worked on; author and commit on hover.
  function gitSource(m) {
    if (!m.gitBranch && !m.gitAuthor) return '';
    const title = [m.gitAuthor, m.gitCommit ? `${t('dari commit', 'from commit')} ${m.gitCommit.substring(0, 8)}` : null].filter(Boolean).join(' · ');
    return `<div class="mono text-secondary text-truncate" style="max-width:12rem" title="${escapeHtml(title)}">${escapeHtml(m.gitBranch || m.gitAuthor)}</div>`;
  }

  function row(m) {
    return (isEn ? `<tr>
      <td>${levelCell(m)}</td>
      <td><span class="badge ${m.type === 'Project' ? 'text-bg-primary' : 'text-bg-success'}">${m.type}</span></td>
      <td class="small">${escapeHtml(m.projectName || (m.type === 'Knowledge' ? 'all projects' : '–'))}</td>
      <td>${labelCell(m)}<div class="small text-secondary text-truncate-2">${escapeHtml(m.description)}</div></td>
      <td class="text-nowrap small" title="${fmt.dateTime(m.createdAt)}">${fmt.relative(m.createdAt)}${m.sourceThreadId ? `<div><a href="/Thread/Detail/${m.sourceThreadId}">thread</a></div>` : ''}${gitSource(m)}</td>
      <td>${m.level === 'Long' ? fmt.number(m.hitCount) : '–'}</td>
      <td class="text-end text-nowrap">
        <button type="button" class="btn btn-sm btn-outline-secondary" data-edit="${m.id}">Open</button>
        ${m.level === 'Short' ? `<button type="button" class="btn btn-sm btn-outline-info" data-promote="${m.id}" title="Move to long memory now">→ DB</button>` : ''}
        <button type="button" class="btn btn-sm btn-outline-danger" data-delete="${m.id}">Delete</button>
      </td>
    </tr>` : `<tr>
      <td>${levelCell(m)}</td>
      <td><span class="badge ${m.type === 'Project' ? 'text-bg-primary' : 'text-bg-success'}">${m.type}</span></td>
      <td class="small">${escapeHtml(m.projectName || (m.type === 'Knowledge' ? 'semua project' : '–'))}</td>
      <td>${labelCell(m)}<div class="small text-secondary text-truncate-2">${escapeHtml(m.description)}</div></td>
      <td class="text-nowrap small" title="${fmt.dateTime(m.createdAt)}">${fmt.relative(m.createdAt)}${m.sourceThreadId ? `<div><a href="/Thread/Detail/${m.sourceThreadId}">thread</a></div>` : ''}${gitSource(m)}</td>
      <td>${m.level === 'Long' ? fmt.number(m.hitCount) : '–'}</td>
      <td class="text-end text-nowrap">
        <button type="button" class="btn btn-sm btn-outline-secondary" data-edit="${m.id}">Buka</button>
        ${m.level === 'Short' ? `<button type="button" class="btn btn-sm btn-outline-info" data-promote="${m.id}" title="Pindahkan ke long memory sekarang">→ DB</button>` : ''}
        <button type="button" class="btn btn-sm btn-outline-danger" data-delete="${m.id}">Hapus</button>
      </td>
    </tr>`);
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
        ? (isEn ? `<a href="#review" data-open-review>${fmt.number(s.reviewCount)} need review</a>` : `<a href="#review" data-open-review>${fmt.number(s.reviewCount)} perlu review</a>`)
        : t('dilabeli ulang otomatis', 'relabeled automatically');
      el('reviewBadge').textContent = s.reviewCount ? fmt.number(s.reviewCount) : '';
      el('reviewBadge').classList.toggle('d-none', !s.reviewCount);
      el('statRedis').innerHTML = s.redisConnected
        ? (isEn ? `<span class="text-success">Connected</span> · ${escapeHtml(s.shortStore || '')}` : `<span class="text-success">Terhubung</span> · ${escapeHtml(s.shortStore || '')}`)
        : (isEn ? `<span class="text-danger">${escapeHtml(s.shortStore || 'Short memory')} not connected — new memory goes straight to long memory</span>` : `<span class="text-danger">${escapeHtml(s.shortStore || 'Short memory')} tidak terhubung — memory baru langsung ke long memory</span>`);
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
        (isEn ? `<strong>Memory label migration</strong>: ${fmt.number(s.pending)} waiting · ${fmt.number(s.labeled)} labeled · ${fmt.number(s.needsReview)} need review` : `<strong>Migrasi label memory</strong>: ${fmt.number(s.pending)} menunggu · ${fmt.number(s.labeled)} sudah berlabel · ${fmt.number(s.needsReview)} perlu review`),
        s.running
          ? (isEn ? `running${s.phase ? ` (${escapeHtml(s.phase)}${s.detail ? `: ${escapeHtml(s.detail)}` : ''})` : ''}` : `sedang berjalan${s.phase ? ` (${escapeHtml(s.phase)}${s.detail ? `: ${escapeHtml(s.detail)}` : ''})` : ''}`)
          : (s.lastRunAt ? (isEn ? `last run ${fmt.relative(s.lastRunAt)}` : `terakhir ${fmt.relative(s.lastRunAt)}`) : t('menunggu giliran', 'waiting for its turn')),
        s.lastMessage ? escapeHtml(s.lastMessage) : null,
        s.backupPath ? `backup: <span class="mono">${escapeHtml(s.backupPath)}</span>` : null
      ].filter(Boolean);
      el('relabelText').innerHTML = parts.join(' · ');
      el('btnRelabel').disabled = s.running;
    } catch {
      el('relabelBanner').classList.add('d-none');
    }
  }

  // The migration of older project facts (Rule, Flow, Pitfall, …) into the module summaries: shown while any wait.
  async function loadChangelog() {
    try {
      const s = await api.get('/api/memory/changelog');
      const show = s.pendingFacts > 0 || s.shortFacts > 0;
      el('changelogBanner').classList.toggle('d-none', !show);
      if (!show) return;
      // Overall progress comes from the store and a file next to the backup, so it survives a restart of AIT.
      const percent = Math.round(100 * s.migratedFacts / Math.max(1, s.totalFacts));
      const parts = [
        (isEn ? `<strong>Migration of older memory into module summaries</strong>: ${fmt.number(s.migratedFacts)}/${fmt.number(s.totalFacts)} migrated (${percent}%)` : `<strong>Migrasi memory lama ke ringkasan modul</strong>: ${fmt.number(s.migratedFacts)}/${fmt.number(s.totalFacts)} dimigrasi (${percent}%)`)
          + (isEn ? ` · ${fmt.number(s.pendingFacts)} waiting in ${fmt.number(s.pendingModules)} module(s) · ${fmt.number(s.summaries)} module summaries` : ` · ${fmt.number(s.pendingFacts)} menunggu di ${fmt.number(s.pendingModules)} modul · ${fmt.number(s.summaries)} ringkasan modul`),
        s.shortFacts > 0 ? (isEn ? `${fmt.number(s.shortFacts)} still in short memory (included once moved to long memory)` : `${fmt.number(s.shortFacts)} masih di short memory (ikut setelah pindah ke long memory)`) : null,
        s.running
          ? (isEn ? `this round: module ${fmt.number(Math.min(s.done + 1, s.total))}/${fmt.number(s.total)}${s.module ? ` (${escapeHtml(s.module)})` : ''}` : `putaran ini: modul ${fmt.number(Math.min(s.done + 1, s.total))}/${fmt.number(s.total)}${s.module ? ` (${escapeHtml(s.module)})` : ''}`)
          : (!s.aiAvailable ? t('menunggu node Tools, Wrapper, atau Planner yang aktif', 'waiting for an active Tools node, Wrapper or Planner')
            : (s.lastRunAt ? (isEn ? `last run ${fmt.relative(s.lastRunAt)}, next at the consolidation round` : `terakhir ${fmt.relative(s.lastRunAt)}, berikutnya pada putaran konsolidasi`) : t('menunggu putaran konsolidasi', 'waiting for the consolidation round'))),
        s.lastMessage ? escapeHtml(s.lastMessage) : null,
        s.backupPath ? `backup: <span class="mono">${escapeHtml(s.backupPath)}</span>` : null
      ].filter(Boolean);
      el('changelogText').innerHTML = parts.join(' · ');
      el('changelogProgress').setAttribute('aria-valuenow', percent);
      el('changelogBar').style.width = `${percent}%`;
      el('changelogBar').classList.toggle('progress-bar-striped', s.running);
      el('changelogBar').classList.toggle('progress-bar-animated', s.running);
    } catch {
      el('changelogBanner').classList.add('d-none');
    }
  }

  async function loadProjects() {
    try {
      projects = await api.get('/api/projects');
    } catch {
      projects = [];
    }
    const options = projects.map(p => `<option value="${p.id}">${escapeHtml(p.name)}</option>`).join('');
    el('filterProject').innerHTML = (isEn ? `<option value="">All projects</option>${options}` : `<option value="">Semua project</option>${options}`);
    el('reviewProject').innerHTML = (isEn ? `<option value="">All projects (memory only)</option>${options}` : `<option value="">Semua project (memory saja)</option>${options}`);
    el('memoryProject').innerHTML = options || t('<option value="">(belum ada project)</option>', '<option value="">(no project yet)</option>');
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
    el('filterModule').innerHTML = t('<option value="">Semua modul</option>', '<option value="">All modules</option>')
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
        : t('<tr><td colspan="7" class="empty-state">Belum ada memory. Memory terbentuk otomatis setelah thread selesai.</td></tr>', '<tr><td colspan="7" class="empty-state">No memory yet. Memory is created automatically after a thread finishes.</td></tr>');
      const from = total === 0 ? 0 : (page - 1) * pageSize + 1;
      el('pageInfo').textContent = (isEn ? `${from}–${Math.min(page * pageSize, total)} of ${total}` : `${from}–${Math.min(page * pageSize, total)} dari ${total}`);
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
    el('memoryModule').innerHTML = t('<option value="" disabled>Pilih modul…</option>', '<option value="" disabled>Choose a module…</option>')
      + names.sort((a, b) => a.localeCompare(b)).map(option).join('')
      + (isEn ? `<option value="${newModuleValue}">＋ New module…</option>` : `<option value="${newModuleValue}">＋ Modul baru…</option>`);
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
    el('memoryExtraSelect').innerHTML = `<option value="">${full ? t('Sudah 3 modul', 'Already 3 modules') : t('＋ Tambah modul lain', '＋ Add another module')}</option>`
      + (full ? '' : available.map(name => `<option value="${escapeHtml(name)}">${escapeHtml(name)}</option>`).join('')
        + (isEn ? `<option value="${newModuleValue}">＋ New module…</option>` : `<option value="${newModuleValue}">＋ Modul baru…</option>`));
    el('memoryExtraSelect').disabled = full;
  }

  function openModuleEdit(mode) {
    moduleEditMode = mode;
    el('memoryModuleEditLabel').textContent = mode === 'rename' ? (isEn ? `Rename ${el('memoryModule').value}` : `Ubah nama ${el('memoryModule').value}`) : mode === 'extra' ? t('Modul lain baru', 'New other module') : t('Modul baru', 'New module');
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
        toast((isEn ? `Module ${old} → ${module.name}.` : `Modul ${old} → ${module.name}.`));
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
    el('memoryLegacy').textContent = legacy ? (isEn ? `Old label: ${legacy}` : `Label lama: ${legacy}`) : '';
    el('memoryLegacy').classList.toggle('d-none', !legacy);
    el('memoryDescription').value = memory?.description || '';
    el('memoryDescription').placeholder = changeTemplate;
    // Markdown preview by default; an empty description (a new memory) opens ready to write.
    setDescriptionPreview(Boolean(memory?.description));
    el('memoryModalTitle').textContent = memory ? `Memory · ${memory.level === 'Short' ? 'Short' : 'Long'}` : t('Tambah memory (masuk ke short memory)', 'Add memory (goes to short memory)');
    el('memoryMeta').textContent = memory
      ? (isEn ? `Created ${fmt.dateTime(memory.createdAt)}${memory.promotedAt ? (memory.kind === 'Summary'
        // A summary that has folded no change entry yet carries the start value (year 2000) instead of a time.
        ? (toDate(memory.promotedAt).getUTCFullYear() <= 2000
          ? ' · holds no change entry yet (from older memory)'
          : ` · holds the change entries up to ${fmt.dateTime(memory.promotedAt)}`)
        : ` · moved to the DB ${fmt.dateTime(memory.promotedAt)}`) : ''}${memory.reviewReason ? ` · needs review: ${memory.reviewReason}` : ''}` : `Dibuat ${fmt.dateTime(memory.createdAt)}${memory.promotedAt ? (memory.kind === 'Summary'
        // A summary that has folded no change entry yet carries the start value (year 2000) instead of a time.
        ? (toDate(memory.promotedAt).getUTCFullYear() <= 2000
          ? ' · belum memuat catatan perubahan (dari memory lama)'
          : ` · memuat catatan perubahan sampai ${fmt.dateTime(memory.promotedAt)}`)
        : ` · dipindah ke DB ${fmt.dateTime(memory.promotedAt)}`) : ''}${memory.reviewReason ? ` · perlu review: ${memory.reviewReason}` : ''}`)
      : '';
    syncProjectField(memory?.module || '');
    syncKeywordCount();
    modal.show();
  }

  el('memoryForm').addEventListener('submit', async event => {
    event.preventDefault();
    const keywords = keywordList();
    if (keywords.length > maxKeywords) {
      toast((isEn ? `At most ${maxKeywords} keywords.` : `Maksimal ${maxKeywords} keyword.`), 'error');
      return;
    }
    if (!el('memoryModule').value || el('memoryModule').value === newModuleValue) {
      toast(t('Pilih modul dulu.', 'Choose a module first.'), 'error');
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
      toast(t('Memory tersimpan.', 'Memory saved.'));
      refreshAll();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  async function promote(id) {
    try {
      await api.post(`/api/memory/${id}/promote`);
      toast(t('Memory dipindah ke long memory.', 'Memory moved to long memory.'));
      refreshAll();
    } catch (error) {
      toast(error.message, 'error');
    }
  }

  async function remove(memory) {
    if (!confirm((isEn ? `Delete memory "${memory.label}"?` : `Hapus memory "${memory.label}"?`))) return;
    try {
      await api.del(`/api/memory/${memory.level}/${memory.id}`);
      toast(t('Memory dihapus.', 'Memory deleted.'));
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
      toast(moved ? (isEn ? `${moved} memories moved to long memory.` : `${moved} memory dipindah ke long memory.`) : t('Tidak ada short memory yang sudah lewat 1 hari.', 'No short memory older than 1 day.'), 'info');
      refreshAll();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('btnRelabel').addEventListener('click', async () => {
    try {
      await api.post('/api/memory/relabel');
      toast(t('Migrasi label dijalankan di belakang.', 'Label migration started in the background.'), 'info');
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
        ? review.memories.map(m => memoryItem(m, (isEn ? `
            <button type="button" class="btn btn-sm btn-outline-primary" data-edit="${m.id}">Label it</button>
            <button type="button" class="btn btn-sm btn-outline-danger" data-delete="${m.id}">Delete</button>` : `
            <button type="button" class="btn btn-sm btn-outline-primary" data-edit="${m.id}">Beri label</button>
            <button type="button" class="btn btn-sm btn-outline-danger" data-delete="${m.id}">Hapus</button>`))).join('')
        : t('<div class="list-group-item text-secondary small">Tidak ada memory yang menunggu label.</div>', '<div class="list-group-item text-secondary small">No memory waiting for a label.</div>');

      el('reviewMergeCount').textContent = review.merges.length ? `(${review.merges.length})` : '';
      el('reviewMerges').innerHTML = review.merges.length
        ? review.merges.map(g => (isEn ? `<div class="list-group-item d-flex flex-wrap align-items-center gap-2">
            <div class="me-auto">
              <span class="fw-semibold">${escapeHtml(g.from)}</span> → <span class="fw-semibold">${escapeHtml(g.into)}</span>
              ${g.confidence != null ? `<span class="badge text-bg-light border ms-1">confidence ${Math.round(g.confidence * 100)}%</span>` : ''}
              ${g.reason ? `<div class="small text-secondary">${escapeHtml(g.reason)}</div>` : ''}
            </div>
            <button type="button" class="btn btn-sm btn-outline-primary" data-accept="${g.id}">Merge</button>
            <button type="button" class="btn btn-sm btn-outline-secondary" data-reject="${g.id}">Not a duplicate</button>
          </div>` : `<div class="list-group-item d-flex flex-wrap align-items-center gap-2">
            <div class="me-auto">
              <span class="fw-semibold">${escapeHtml(g.from)}</span> → <span class="fw-semibold">${escapeHtml(g.into)}</span>
              ${g.confidence != null ? `<span class="badge text-bg-light border ms-1">keyakinan ${Math.round(g.confidence * 100)}%</span>` : ''}
              ${g.reason ? `<div class="small text-secondary">${escapeHtml(g.reason)}</div>` : ''}
            </div>
            <button type="button" class="btn btn-sm btn-outline-primary" data-accept="${g.id}">Gabungkan</button>
            <button type="button" class="btn btn-sm btn-outline-secondary" data-reject="${g.id}">Bukan duplikat</button>
          </div>`)).join('')
        : `<div class="list-group-item text-secondary small">${projectId ? t('Tidak ada saran gabung modul.', 'No module merge suggestions.') : t('Pilih project untuk melihat saran gabung modulnya.', 'Choose a project to see its module merge suggestions.')}</div>`;

      el('reviewDuplicateCount').textContent = review.duplicates.length ? `(${review.duplicates.length})` : '';
      el('reviewDuplicates').innerHTML = review.duplicates.length
        ? review.duplicates.map(d => `<div class="list-group-item">
            <div class="small text-secondary mb-1">${isEn ? 'Keyword similarity' : 'Kemiripan keyword'} ${Math.round(d.similarity * 100)}%</div>
            <div class="row g-2">
              ${[d.first, d.second].map(m => (isEn ? `<div class="col-md-6"><div class="border rounded p-2 h-100">
                ${labelCell(m)}<div class="small text-truncate-2">${escapeHtml(m.description)}</div>
                <div class="mt-1"><button type="button" class="btn btn-sm btn-outline-secondary" data-edit="${m.id}">Open</button>
                <button type="button" class="btn btn-sm btn-outline-danger" data-delete="${m.id}">Delete</button></div>
              </div></div>` : `<div class="col-md-6"><div class="border rounded p-2 h-100">
                ${labelCell(m)}<div class="small text-truncate-2">${escapeHtml(m.description)}</div>
                <div class="mt-1"><button type="button" class="btn btn-sm btn-outline-secondary" data-edit="${m.id}">Buka</button>
                <button type="button" class="btn btn-sm btn-outline-danger" data-delete="${m.id}">Hapus</button></div>
              </div></div>`)).join('')}
            </div>
          </div>`).join('')
        : t('<div class="list-group-item text-secondary small">Tidak ada kandidat duplikat.</div>', '<div class="list-group-item text-secondary small">No duplicate candidates.</div>');
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
      toast(action === 'accept' ? t('Modul digabung.', 'Modules merged.') : t('Saran ditolak.', 'Suggestion rejected.'));
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
      toast(t('Tidak dapat menghubungi server AIT.', 'Cannot reach the AIT server.'), 'error');
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
    toast(t('File export diunduh. Isinya teks biasa — jangan dibagikan bila memuat info internal.', 'Export file downloaded. It is plain text — do not share it if it holds internal information.'), 'info');
  });

  // Import: a dry run first, shown in the modal; the confirm button runs the same request for real.
  const importModal = bootstrap.Modal.getOrCreateInstance(el('importModal'));
  let importDocument = null;

  function importSummary(r) {
    const projectRows = r.projects.map(p => `<tr>
        <td class="mono small">${escapeHtml(p.projectKey)}</td>
        <td class="small">${escapeHtml(p.projectName || '–')}</td>
        <td class="small">${fmt.number(p.count)}</td>
        <td class="small">${p.registered ? '<span class="text-success">terdaftar</span>' : t('<span class="text-warning">belum terdaftar</span>', '<span class="text-warning">not registered</span>')}</td>
      </tr>`).join('');
    const warnings = r.warnings.map(w => `<li>${escapeHtml(w)}</li>`).join('');
    const unitTests = r.unitTestsAdded + r.unitTestsUpdated + r.unitTestsSkipped > 0
      ? (isEn ? `<div class="small mb-2"><span class="fw-semibold">Unit test catalog:</span> ${fmt.number(r.unitTestsAdded)} added, ${fmt.number(r.unitTestsUpdated)} updated,
          ${fmt.number(r.unitTestsSkipped)} already present${r.unitTestsRenumbered ? `, ${fmt.number(r.unitTestsRenumbered)} got a new number` : ''}.
          <span class="text-secondary">Test snapshots come along (not shown) and are used to restore missing tests.</span></div>` : `<div class="small mb-2"><span class="fw-semibold">Katalog unit test:</span> ${fmt.number(r.unitTestsAdded)} ditambahkan, ${fmt.number(r.unitTestsUpdated)} diperbarui,
          ${fmt.number(r.unitTestsSkipped)} sudah ada${r.unitTestsRenumbered ? `, ${fmt.number(r.unitTestsRenumbered)} mendapat nomor baru` : ''}.
          <span class="text-secondary">Snapshot test ikut masuk (tidak ditampilkan) dan dipakai untuk memulihkan test yang hilang.</span></div>`)
      : '';
    return (isEn ? `
      <div class="row g-2 mb-3 text-center">
        <div class="col"><div class="stat-label">In the file</div><div class="fs-5">${fmt.number(r.total)}</div></div>
        <div class="col"><div class="stat-label">Added</div><div class="fs-5 text-success">${fmt.number(r.added)}</div></div>
        <div class="col"><div class="stat-label">Overwritten</div><div class="fs-5">${fmt.number(r.updated)}</div></div>
        <div class="col"><div class="stat-label">Already present</div><div class="fs-5 text-secondary">${fmt.number(r.skipped + r.duplicates)}</div></div>
        <div class="col"><div class="stat-label">Invalid</div><div class="fs-5 ${r.invalid ? 'text-danger' : 'text-secondary'}">${fmt.number(r.invalid)}</div></div>
      </div>
      ${unitTests}
      ${projectRows ? `<div class="small fw-semibold mb-1">Projects in the file</div>
        <div class="table-responsive mb-2"><table class="table table-sm mb-0">
          <thead><tr><th>Project key</th><th>Name</th><th>Memory</th><th>On this machine</th></tr></thead>
          <tbody>${projectRows}</tbody></table></div>
        <div class="small text-secondary mb-2">Project memory of a project not registered here is kept under its key and shows up once that project is added.</div>` : ''}
      ${warnings ? `<div class="alert alert-warning small mb-0"><ul class="mb-0 ps-3">${warnings}</ul></div>` : ''}` : `
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
      ${warnings ? `<div class="alert alert-warning small mb-0"><ul class="mb-0 ps-3">${warnings}</ul></div>` : ''}`);
  }

  async function previewImport() {
    el('btnImportConfirm').disabled = true;
    el('importPreview').innerHTML = t('<span class="text-secondary small">Memeriksa file…</span>', '<span class="text-secondary small">Checking the file…</span>');
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
      toast((isEn ? `${file.name} is not a valid JSON file.` : `${file.name} bukan file JSON yang valid.`), 'error');
      return;
    }
    el('importFileInfo').textContent = `${file.name} · ${fmt.number(Math.ceil(file.size / 1024))} KB`
      + (importDocument?.exportedAt ? (isEn ? ` · exported ${fmt.dateTime(importDocument.exportedAt)}` : ` · diekspor ${fmt.dateTime(importDocument.exportedAt)}`) : '')
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
      toast((isEn ? `${result.added} memories added, ${result.updated} overwritten, ${result.skipped + result.duplicates} skipped.` : `${result.added} memory ditambahkan, ${result.updated} ditimpa, ${result.skipped + result.duplicates} dilewati.`)
        + (result.unitTestsAdded + result.unitTestsUpdated ? (isEn ? ` Unit tests: ${result.unitTestsAdded} added, ${result.unitTestsUpdated} updated.` : ` Unit test: ${result.unitTestsAdded} ditambahkan, ${result.unitTestsUpdated} diperbarui.`) : ''));
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
    loadChangelog();
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
  el('btnMemoryWrite').addEventListener('click', () => setDescriptionPreview(false));
  el('btnMemoryPreview').addEventListener('click', () => setDescriptionPreview(true));
  el('memoryPreview').addEventListener('dblclick', () => {
    setDescriptionPreview(false);
    el('memoryDescription').focus();
  });
  // A hidden required field cannot show its message: back to writing so the browser can.
  el('memoryDescription').addEventListener('invalid', () => setDescriptionPreview(false));
  ['filterLevel', 'filterType', 'filterModule', 'filterLayer', 'filterKind', 'filterLabeled'].forEach(id => el(id).addEventListener('change', resetAndLoad));
  el('filterProject').addEventListener('change', () => { loadModuleFilter(); resetAndLoad(); });
  el('filterSearch').addEventListener('input', debounce(resetAndLoad, 350));
  el('btnPrev').addEventListener('click', () => { page = Math.max(1, page - 1); load(); });
  el('btnNext').addEventListener('click', () => { page += 1; load(); });

  loadStats();
  loadRelabel();
  loadChangelog();
  loadProjects().then(() => { loadModuleFilter(); load(); });
  stream('types=memory', { 'memory.changed': debounce(refreshAll, 500) });
})();
