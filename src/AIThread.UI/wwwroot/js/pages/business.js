// Memory → Business Memory: the business wiki of a project (business memory). Pages are shared by every developer whose
// AIThread points at the same business memory store, so saves carry the page version and a conflict is reported
// instead of overwriting someone else's change.
(() => {
  'use strict';
  const { api, toast, escapeHtml, fmt, stream, debounce } = window.AIThread;
  const el = id => document.getElementById(id);
  const projectKeyStorage = 'aithread-business-project';

  const historyModal = bootstrap.Modal.getOrCreateInstance(el('bizHistoryModal'));
  const tabButton = el('business-tab');
  let loaded = false;
  let overview = null;
  let current = null;        // BusinessPageDto on display
  let editing = null;        // { id, version } of the page in the editor; id null = new page
  let editorDirty = false;
  let selectedRevision = null;

  const editKinds = { Ai: 'AI', Manual: 'manual', Restore: 'dipulihkan' };

  // --- Tabs: #bisnis opens this tab, and the hash follows the open tab ---

  document.querySelectorAll('#memoryTabs [data-bs-toggle="tab"]').forEach(button => button.addEventListener('shown.bs.tab', () => {
    history.replaceState(null, '', button.dataset.tab ? `#${button.dataset.tab}` : location.pathname + location.search);
    if (button === tabButton && !loaded) init();
  }));
  if (location.hash === '#bisnis') bootstrap.Tab.getOrCreateInstance(tabButton).show();

  function storedProject() {
    try { return localStorage.getItem(projectKeyStorage); } catch { return null; }
  }

  function rememberProject(id) {
    try { localStorage.setItem(projectKeyStorage, id); } catch { /* private window */ }
  }

  async function init() {
    loaded = true;
    try {
      const projects = await api.get('/api/projects');
      const select = el('bizProject');
      select.innerHTML = projects.length
        ? projects.map(p => `<option value="${p.id}">${escapeHtml(p.name)}</option>`).join('')
        : '<option value="">Belum ada project</option>';
      const wanted = [window.initialProjectId, storedProject()].find(id => id && projects.some(p => p.id === id));
      if (wanted) select.value = wanted;
      await loadOverview();
    } catch (error) {
      el('bizInfo').innerHTML = `<span class="text-danger">${escapeHtml(error.message)}</span>`;
    }
  }

  const projectId = () => el('bizProject').value;

  // --- Overview and page list ---

  async function loadOverview() {
    if (!projectId()) {
      el('bizPages').innerHTML = '<div class="list-group-item text-secondary">Daftarkan project dulu di menu Project.</div>';
      return;
    }
    try {
      overview = await api.get(`/api/business/projects/${projectId()}`);
      renderInfo();
      renderPages();
      if (current && !overview.pages.some(p => p.id === current.page.id)) showEmpty();
    } catch (error) {
      el('bizInfo').innerHTML = `<span class="text-danger">${escapeHtml(error.message)}</span>`;
    }
  }

  function renderInfo() {
    const o = overview;
    const key = o.keyIsShared
      ? `🔑 <span class="mono">${escapeHtml(o.projectKey)}</span>`
      : '<span class="badge text-bg-warning" title="Tambah git remote, file .aithread/project.json, atau isi key manual di detail project.">key lokal — wiki belum dibagi</span>';
    const pending = o.pendingThreads > 0
      ? `<strong>${fmt.number(o.pendingThreads)}</strong> thread menunggu dirangkum`
      : 'semua thread sudah dirangkum';
    const last = o.lastDigestAt ? ` · terakhir dirangkum ${fmt.relative(o.lastDigestAt)}` : '';
    const running = o.digestRunning ? ' · <span class="badge text-bg-info">sedang merangkum…</span>' : '';
    el('bizInfo').innerHTML = `${key} · ${pending}${last}${running} · <span title="Store business memory">${escapeHtml(o.storeDescription)}</span>`;
    el('btnBizDigest').disabled = o.digestRunning;
  }

  function renderPages() {
    const filter = el('bizFilter').value.trim().toLowerCase();
    const pages = overview.pages.filter(p => !filter
      || `${p.module} ${p.title} ${p.labels} ${p.summary || ''}`.toLowerCase().includes(filter));
    if (pages.length === 0) {
      el('bizPages').innerHTML = `<div class="list-group-item text-secondary">${overview.pages.length ? 'Tidak ada yang cocok.' : 'Belum ada halaman. Klik <strong>Perbarui sekarang</strong> atau <strong>Halaman baru</strong>.'}</div>`;
      return;
    }

    // Grouped by module, so a module with several pages reads as one section.
    const groups = new Map();
    for (const page of pages) {
      if (!groups.has(page.module)) groups.set(page.module, []);
      groups.get(page.module).push(page);
    }
    el('bizPages').innerHTML = Array.from(groups, ([module, items]) =>
      `<div class="list-group-item small fw-semibold text-secondary bg-body-tertiary py-1">${escapeHtml(module)}</div>` +
      items.map(p => `<button type="button" class="list-group-item list-group-item-action ${current?.page.id === p.id ? 'active' : ''}" data-page="${p.id}">
          <div>${escapeHtml(p.title)}${p.autoUpdate ? '' : ' <span title="Dikurasi manual">🔒</span>'}</div>
          ${p.summary ? `<div class="small opacity-75 text-truncate">${escapeHtml(p.summary)}</div>` : ''}
        </button>`).join('')).join('');
  }

  el('bizPages').addEventListener('click', event => {
    const item = event.target.closest('[data-page]');
    if (item && confirmLeaveEditor()) openPage(item.dataset.page);
  });

  el('bizFilter').addEventListener('input', debounce(() => overview && renderPages(), 200));

  el('bizProject').addEventListener('change', () => {
    if (!confirmLeaveEditor()) return;
    rememberProject(projectId());
    showEmpty();
    loadOverview();
  });

  // --- Page view ---

  function showEmpty() {
    current = null;
    closeEditor();
    el('bizEmpty').classList.remove('d-none');
    el('bizPage').classList.add('d-none');
  }

  async function openPage(id) {
    try {
      current = await api.get(`/api/business/pages/${id}`);
      closeEditor();
      renderPage();
      renderPages();
    } catch (error) {
      toast(error.message, 'error');
    }
  }

  function renderPage() {
    const p = current.page;
    el('bizEmpty').classList.add('d-none');
    el('bizPage').classList.remove('d-none');
    const when = p.updatedAt || p.createdAt;
    el('bizPageMeta').innerHTML = `<span class="badge text-bg-secondary">${escapeHtml(p.module)}</span> versi ${p.version}
      · ${editKinds[p.lastEditKind] || p.lastEditKind} oleh ${escapeHtml(p.updatedBy || '–')}
      · <span title="${fmt.dateTime(when)}">${fmt.relative(when)}</span>`;
    el('bizAutoUpdate').checked = p.autoUpdate;
    el('bizContent').innerHTML = current.html;
  }

  el('bizAutoUpdate').addEventListener('change', async () => {
    const p = current.page;
    try {
      current = await api.put(`/api/business/pages/${p.id}`, {
        projectId: projectId(), module: p.module, title: p.title, summary: p.summary, labels: p.labels,
        content: current.content, autoUpdate: el('bizAutoUpdate').checked, version: p.version,
        changeNote: el('bizAutoUpdate').checked ? 'Boleh diperbarui AI lagi' : 'Dikunci dari pembaruan AI'
      });
      renderPage();
      loadOverview();
    } catch (error) {
      el('bizAutoUpdate').checked = p.autoUpdate;
      toast(error.message, 'error');
    }
  });

  el('btnBizDelete').addEventListener('click', async () => {
    const p = current.page;
    if (!confirm(`Hapus halaman "${p.module} — ${p.title}" beserta riwayatnya? Halaman ini hilang untuk semua developer yang memakai store business memory yang sama.`)) return;
    try {
      await api.del(`/api/business/pages/${p.id}`);
      toast('Halaman dihapus.');
      showEmpty();
      loadOverview();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  // --- Editor ---

  function openEditor(page) {
    editing = page ? { id: page.page.id, version: page.page.version } : { id: null, version: null };
    el('bizEditorTitle').textContent = page ? `Edit · ${page.page.module} — ${page.page.title} (versi ${page.page.version})` : 'Halaman baru';
    el('bizModule').value = page?.page.module || '';
    el('bizTitle').value = page?.page.title || '';
    el('bizSummary').value = page?.page.summary || '';
    el('bizLabels').value = page?.page.labels || '';
    el('bizBody').value = page?.content || newPageTemplate();
    el('bizChangeNote').value = '';
    el('bizEditorAutoUpdate').checked = page ? page.page.autoUpdate : true;
    setPreview(false);
    editorDirty = false;
    el('bizView').classList.add('d-none');
    el('bizEditor').classList.remove('d-none');
    (page ? el('bizBody') : el('bizModule')).focus();
  }

  function closeEditor() {
    editing = null;
    editorDirty = false;
    el('bizEditor').classList.add('d-none');
    el('bizView').classList.remove('d-none');
  }

  function confirmLeaveEditor() {
    return !editing || !editorDirty || confirm('Perubahan di editor belum disimpan. Tinggalkan?');
  }

  function newPageTemplate() {
    return [
      '# Modul — Judul', '', '> Ringkasan singkat modul ini.', '',
      '## Tujuan', '', '## Alur & status', '', '| Status | Arti | Pelaku | Berikutnya |', '|---|---|---|---|', '|  |  |  |  |', '',
      '## Aturan bisnis', '', '1. **BR-XX-01** — ', '',
      '## Validasi', '', '| Field | Aturan | Pesan |', '|---|---|---|', '|  |  |  |', '',
      '## Tampilan', ''
    ].join('\n');
  }

  el('bizEditor').addEventListener('input', () => { editorDirty = true; });
  el('btnBizNew').addEventListener('click', () => {
    if (!projectId()) return toast('Pilih project dulu.', 'warning');
    if (confirmLeaveEditor()) openEditor(null);
  });
  el('btnBizEdit').addEventListener('click', () => openEditor(current));
  el('btnBizCancel').addEventListener('click', () => { if (confirmLeaveEditor()) closeEditor(); });

  el('bizEditor').addEventListener('submit', async event => {
    event.preventDefault();
    const body = {
      projectId: projectId(),
      module: el('bizModule').value.trim(),
      title: el('bizTitle').value.trim(),
      summary: el('bizSummary').value.trim() || null,
      labels: el('bizLabels').value.trim() || null,
      content: el('bizBody').value,
      autoUpdate: el('bizEditorAutoUpdate').checked,
      version: editing.version,
      changeNote: el('bizChangeNote').value.trim() || null
    };
    if (!body.module || !body.title || !body.content.trim()) return toast('Modul, judul, dan isi wajib diisi.', 'warning');

    const button = el('btnBizSave');
    button.disabled = true;
    try {
      current = editing.id
        ? await api.put(`/api/business/pages/${editing.id}`, body)
        : await api.post('/api/business/pages', body);
      toast('Halaman disimpan.');
      closeEditor();
      renderPage();
      loadOverview();
    } catch (error) {
      // A conflict keeps the editor open, so the text is not lost while the newer version is checked.
      toast(error.message, 'error');
    } finally {
      button.disabled = false;
    }
  });

  async function setPreview(on) {
    el('btnBizWrite').classList.toggle('active', !on);
    el('btnBizPreview').classList.toggle('active', on);
    el('bizBody').classList.toggle('d-none', on);
    el('bizPreview').classList.toggle('d-none', !on);
    if (!on) return;
    el('bizPreview').innerHTML = '<span class="text-secondary">Memuat pratinjau…</span>';
    try {
      el('bizPreview').innerHTML = await api.post('/api/business/preview', { content: el('bizBody').value });
    } catch (error) {
      el('bizPreview').innerHTML = `<span class="text-danger">${escapeHtml(error.message)}</span>`;
    }
  }

  el('btnBizWrite').addEventListener('click', () => setPreview(false));
  el('btnBizPreview').addEventListener('click', () => setPreview(true));

  // --- Images: paste a screenshot or pick a file; it is stored, then referenced at the cursor ---

  function readAsDataUrl(file) {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(reader.result);
      reader.onerror = () => reject(new Error('Gambar tidak dapat dibaca.'));
      reader.readAsDataURL(file);
    });
  }

  function insertAtCursor(text) {
    const area = el('bizBody');
    const start = area.selectionStart ?? area.value.length;
    const end = area.selectionEnd ?? start;
    area.value = area.value.slice(0, start) + text + area.value.slice(end);
    area.selectionStart = area.selectionEnd = start + text.length;
    area.focus();
    editorDirty = true;
  }

  async function uploadImage(file) {
    if (!file.type.startsWith('image/')) return;
    if (file.size > 5 * 1024 * 1024) return toast('Gambar maksimal 5 MB.', 'warning');
    const placeholder = `![mengunggah ${escapeHtml(file.name || 'gambar')}…]()`;
    insertAtCursor(placeholder);
    try {
      const caption = (file.name && !/^image\.\w+$/i.test(file.name)) ? file.name.replace(/\.\w+$/, '') : 'Screenshot';
      const asset = await api.post('/api/business/assets', {
        projectId: projectId(), pageId: editing?.id || null, data: await readAsDataUrl(file), caption
      });
      el('bizBody').value = el('bizBody').value.replace(placeholder, `![${caption}](asset:${asset.id})`);
    } catch (error) {
      el('bizBody').value = el('bizBody').value.replace(placeholder, '');
      toast(error.message, 'error');
    }
  }

  el('bizBody').addEventListener('paste', event => {
    const files = Array.from(event.clipboardData?.files || []).filter(f => f.type.startsWith('image/'));
    if (files.length === 0) return;
    event.preventDefault();
    files.forEach(uploadImage);
  });

  el('bizImageFile').addEventListener('change', () => {
    const file = el('bizImageFile').files[0];
    el('bizImageFile').value = '';
    if (file) uploadImage(file);
  });

  // --- History ---

  el('btnBizHistory').addEventListener('click', async () => {
    selectedRevision = null;
    el('bizHistoryTitle').textContent = `Riwayat · ${current.page.module} — ${current.page.title}`;
    el('bizRevisionMeta').textContent = '';
    el('btnBizRestore').classList.add('d-none');
    el('bizRevisionContent').innerHTML = '<span class="text-secondary">Pilih versi.</span>';
    el('bizRevisions').innerHTML = '<div class="list-group-item text-secondary">Memuat…</div>';
    historyModal.show();
    try {
      const revisions = await api.get(`/api/business/pages/${current.page.id}/revisions`);
      el('bizRevisions').innerHTML = revisions.map(r => `<button type="button" class="list-group-item list-group-item-action" data-version="${r.version}">
          <div class="d-flex"><strong>Versi ${r.version}</strong><span class="ms-auto text-secondary" title="${fmt.dateTime(r.createdAt)}">${fmt.relative(r.createdAt)}</span></div>
          <div>${escapeHtml(editKinds[r.editKind] || r.editKind)} · ${escapeHtml(r.author || '–')}</div>
          ${r.changeNote ? `<div class="text-secondary">${escapeHtml(r.changeNote)}</div>` : ''}
          ${r.sourceThreadIds.length ? `<div class="text-secondary">${r.sourceThreadIds.length} thread</div>` : ''}
        </button>`).join('') || '<div class="list-group-item text-secondary">Belum ada riwayat.</div>';
    } catch (error) {
      el('bizRevisions').innerHTML = `<div class="list-group-item text-danger">${escapeHtml(error.message)}</div>`;
    }
  });

  el('bizRevisions').addEventListener('click', async event => {
    const item = event.target.closest('[data-version]');
    if (!item) return;
    el('bizRevisions').querySelectorAll('.active').forEach(a => a.classList.remove('active'));
    item.classList.add('active');
    try {
      const revision = await api.get(`/api/business/pages/${current.page.id}/revisions/${item.dataset.version}`);
      selectedRevision = revision.version;
      const threads = revision.sourceThreadIds.map((id, i) => `<a href="/Thread/Detail/${id}">thread ${i + 1}</a>`).join(', ');
      el('bizRevisionMeta').innerHTML = `Versi ${revision.version} · ${escapeHtml(revision.author || '–')}${threads ? ` · dari ${threads}` : ''}`;
      el('btnBizRestore').classList.toggle('d-none', revision.version === current.page.version);
      el('bizRevisionContent').innerHTML = await api.post('/api/business/preview', { content: revision.content });
    } catch (error) {
      el('bizRevisionContent').innerHTML = `<span class="text-danger">${escapeHtml(error.message)}</span>`;
    }
  });

  el('btnBizRestore').addEventListener('click', async () => {
    if (selectedRevision === null || !confirm(`Pulihkan halaman ke versi ${selectedRevision}? Versi sekarang tetap ada di riwayat.`)) return;
    try {
      current = await api.post(`/api/business/pages/${current.page.id}/revisions/${selectedRevision}/restore`);
      historyModal.hide();
      toast(`Halaman dipulihkan dari versi ${selectedRevision}.`);
      renderPage();
      loadOverview();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  // --- Update now ---

  el('btnBizDigest').addEventListener('click', async () => {
    const button = el('btnBizDigest');
    button.disabled = true;
    button.textContent = 'Merangkum…';
    try {
      const result = await api.post(`/api/business/digest?projectId=${projectId()}`);
      toast(result.threads === 0 && result.warnings.length ? result.warnings[0]
        : `${result.threads} thread dirangkum: ${result.pagesCreated} halaman baru, ${result.pagesUpdated} diperbarui.`,
        result.threads === 0 ? 'info' : 'success');
      result.warnings.filter(w => result.threads > 0).forEach(w => toast(w, 'warning'));
    } catch (error) {
      toast(error.message, 'error');
    } finally {
      button.textContent = 'Perbarui sekarang';
      loadOverview();
      if (current && !editing) openPage(current.page.id);
    }
  });

  // Another developer (or the wiki writer) may change pages at any time.
  stream('types=business', {
    'business.changed': debounce(() => {
      if (!loaded) return;
      loadOverview();
      if (current && !editing) openPage(current.page.id);
    }, 800)
  });
})();
