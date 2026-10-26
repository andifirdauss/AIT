// Memory → Business memory: the business wiki of a project (business memory). Pages are shared by every developer whose
// AIThread points at the same business memory store, so saves carry the page version and a conflict is reported
// instead of overwriting someone else's change.
(() => {
  'use strict';
  const { isEn, t, api, toast, escapeHtml, fmt, stream, debounce } = window.AIThread;
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

  const editKinds = { Ai: 'AI', Manual: 'manual', Restore: t('dipulihkan', 'restored') };

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
        : t('<option value="">Belum ada project</option>', '<option value="">No project yet</option>');
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
      el('bizPages').innerHTML = t('<div class="list-group-item text-secondary">Daftarkan project dulu di menu Proyek.</div>', '<div class="list-group-item text-secondary">Register a project first in the Project menu.</div>');
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
      : t('<span class="badge text-bg-warning" title="Tambah git remote, file .aithread/project.json, atau isi key manual di detail project.">key lokal — wiki belum dibagi</span>', '<span class="badge text-bg-warning" title="Add a git remote, a .aithread/project.json file, or fill in a manual key in the project detail.">local key — wiki not shared yet</span>');
    const pending = o.pendingThreads > 0
      ? (isEn ? `<strong>${fmt.number(o.pendingThreads)}</strong> thread(s) waiting to be summarized` : `<strong>${fmt.number(o.pendingThreads)}</strong> thread menunggu dirangkum`)
      : t('semua thread sudah dirangkum', 'every thread is summarized');
    const last = o.lastDigestAt ? (isEn ? ` · last summarized ${fmt.relative(o.lastDigestAt)}` : ` · terakhir dirangkum ${fmt.relative(o.lastDigestAt)}`) : '';
    const running = o.digestRunning ? t(' · <span class="badge text-bg-info">sedang merangkum…</span>', ' · <span class="badge text-bg-info">summarizing…</span>') : '';
    el('bizInfo').innerHTML = `${key} · ${pending}${last}${running} · <span title="Store business memory">${escapeHtml(o.storeDescription)}</span>`;
    el('btnBizDigest').disabled = o.digestRunning;
  }

  function renderPages() {
    const filter = el('bizFilter').value.trim().toLowerCase();
    const pages = overview.pages.filter(p => !filter
      || `${p.module} ${p.title} ${p.labels} ${p.summary || ''}`.toLowerCase().includes(filter));
    if (pages.length === 0) {
      el('bizPages').innerHTML = `<div class="list-group-item text-secondary">${overview.pages.length ? t('Tidak ada yang cocok.', 'Nothing matches.') : t('Belum ada halaman. Klik <strong>Perbarui sekarang</strong> atau <strong>Halaman baru</strong>.', 'No page yet. Click <strong>Update now</strong> or <strong>New page</strong>.')}</div>`;
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
    el('bizPageMeta').innerHTML = (isEn ? `<span class="badge text-bg-secondary">${escapeHtml(p.module)}</span> version ${p.version}
      · ${editKinds[p.lastEditKind] || p.lastEditKind} by ${escapeHtml(p.updatedBy || '–')}
      · <span title="${fmt.dateTime(when)}">${fmt.relative(when)}</span>` : `<span class="badge text-bg-secondary">${escapeHtml(p.module)}</span> versi ${p.version}
      · ${editKinds[p.lastEditKind] || p.lastEditKind} oleh ${escapeHtml(p.updatedBy || '–')}
      · <span title="${fmt.dateTime(when)}">${fmt.relative(when)}</span>`);
    el('bizAutoUpdate').checked = p.autoUpdate;
    el('bizContent').innerHTML = current.html;
  }

  el('bizAutoUpdate').addEventListener('change', async () => {
    const p = current.page;
    try {
      current = await api.put(`/api/business/pages/${p.id}`, {
        projectId: projectId(), module: p.module, title: p.title, summary: p.summary, labels: p.labels,
        content: current.content, autoUpdate: el('bizAutoUpdate').checked, version: p.version,
        changeNote: el('bizAutoUpdate').checked ? t('Boleh diperbarui AI lagi', 'AI may update it again') : t('Dikunci dari pembaruan AI', 'Locked from AI updates')
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
    if (!confirm((isEn ? `Delete page "${p.module} — ${p.title}" with its history? The page disappears for every developer using the same business memory store.` : `Hapus halaman "${p.module} — ${p.title}" beserta riwayatnya? Halaman ini hilang untuk semua developer yang memakai store business memory yang sama.`))) return;
    try {
      await api.del(`/api/business/pages/${p.id}`);
      toast(t('Halaman dihapus.', 'Page deleted.'));
      showEmpty();
      loadOverview();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  // --- Editor ---

  function openEditor(page) {
    editing = page ? { id: page.page.id, version: page.page.version } : { id: null, version: null };
    el('bizEditorTitle').textContent = page ? (isEn ? `Edit · ${page.page.module} — ${page.page.title} (version ${page.page.version})` : `Edit · ${page.page.module} — ${page.page.title} (versi ${page.page.version})`) : t('Halaman baru', 'New page');
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
    return !editing || !editorDirty || confirm(t('Perubahan di editor belum disimpan. Tinggalkan?', 'The editor has unsaved changes. Leave?'));
  }

  function newPageTemplate() {
    return [
      t('# Modul — Judul', '# Module — Title'), '', t('> Ringkasan singkat modul ini.', '> A short summary of this module.'), '',
      t('## Tujuan', '## Purpose'), '', t('## Alur & status', '## Flow & status'), '', t('| Status | Arti | Pelaku | Berikutnya |', '| Status | Meaning | Actor | Next |'), '|---|---|---|---|', '|  |  |  |  |', '',
      t('## Aturan bisnis', '## Business rules'), '', '1. **BR-XX-01** — ', '',
      t('## Validasi', '## Validation'), '', t('| Field | Aturan | Pesan |', '| Field | Rule | Message |'), '|---|---|---|', '|  |  |  |', '',
      t('## Tampilan', '## Screens'), ''
    ].join('\n');
  }

  el('bizEditor').addEventListener('input', () => { editorDirty = true; });
  el('btnBizNew').addEventListener('click', () => {
    if (!projectId()) return toast(t('Pilih project dulu.', 'Choose a project first.'), 'warning');
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
    if (!body.module || !body.title || !body.content.trim()) return toast(t('Modul, judul, dan isi wajib diisi.', 'Module, title and content are required.'), 'warning');

    const button = el('btnBizSave');
    button.disabled = true;
    try {
      current = editing.id
        ? await api.put(`/api/business/pages/${editing.id}`, body)
        : await api.post('/api/business/pages', body);
      toast(t('Halaman disimpan.', 'Page saved.'));
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
    el('bizPreview').innerHTML = t('<span class="text-secondary">Memuat pratinjau…</span>', '<span class="text-secondary">Loading preview…</span>');
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
      reader.onerror = () => reject(new Error(t('Gambar tidak dapat dibaca.', 'The image cannot be read.')));
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
    if (file.size > 5 * 1024 * 1024) return toast(t('Gambar maksimal 5 MB.', 'Images are at most 5 MB.'), 'warning');
    const placeholder = `![mengunggah ${escapeHtml(file.name || t('gambar', 'image'))}…]()`;
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
    el('bizHistoryTitle').textContent = (isEn ? `History · ${current.page.module} — ${current.page.title}` : `Riwayat · ${current.page.module} — ${current.page.title}`);
    el('bizRevisionMeta').textContent = '';
    el('btnBizRestore').classList.add('d-none');
    el('bizRevisionContent').innerHTML = t('<span class="text-secondary">Pilih versi.</span>', '<span class="text-secondary">Choose a version.</span>');
    el('bizRevisions').innerHTML = t('<div class="list-group-item text-secondary">Memuat…</div>', '<div class="list-group-item text-secondary">Loading…</div>');
    historyModal.show();
    try {
      const revisions = await api.get(`/api/business/pages/${current.page.id}/revisions`);
      el('bizRevisions').innerHTML = revisions.map(r => (isEn ? `<button type="button" class="list-group-item list-group-item-action" data-version="${r.version}">
          <div class="d-flex"><strong>Version ${r.version}</strong><span class="ms-auto text-secondary" title="${fmt.dateTime(r.createdAt)}">${fmt.relative(r.createdAt)}</span></div>
          <div>${escapeHtml(editKinds[r.editKind] || r.editKind)} · ${escapeHtml(r.author || '–')}</div>
          ${r.changeNote ? `<div class="text-secondary">${escapeHtml(r.changeNote)}</div>` : ''}
          ${r.sourceThreadIds.length ? `<div class="text-secondary">${r.sourceThreadIds.length} thread</div>` : ''}
        </button>` : `<button type="button" class="list-group-item list-group-item-action" data-version="${r.version}">
          <div class="d-flex"><strong>Versi ${r.version}</strong><span class="ms-auto text-secondary" title="${fmt.dateTime(r.createdAt)}">${fmt.relative(r.createdAt)}</span></div>
          <div>${escapeHtml(editKinds[r.editKind] || r.editKind)} · ${escapeHtml(r.author || '–')}</div>
          ${r.changeNote ? `<div class="text-secondary">${escapeHtml(r.changeNote)}</div>` : ''}
          ${r.sourceThreadIds.length ? `<div class="text-secondary">${r.sourceThreadIds.length} thread</div>` : ''}
        </button>`)).join('') || t('<div class="list-group-item text-secondary">Belum ada riwayat.</div>', '<div class="list-group-item text-secondary">No history yet.</div>');
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
      el('bizRevisionMeta').innerHTML = (isEn ? `Version ${revision.version} · ${escapeHtml(revision.author || '–')}${threads ? ` · from ${threads}` : ''}` : `Versi ${revision.version} · ${escapeHtml(revision.author || '–')}${threads ? ` · dari ${threads}` : ''}`);
      el('btnBizRestore').classList.toggle('d-none', revision.version === current.page.version);
      el('bizRevisionContent').innerHTML = await api.post('/api/business/preview', { content: revision.content });
    } catch (error) {
      el('bizRevisionContent').innerHTML = `<span class="text-danger">${escapeHtml(error.message)}</span>`;
    }
  });

  el('btnBizRestore').addEventListener('click', async () => {
    if (selectedRevision === null || !confirm((isEn ? `Restore the page to version ${selectedRevision}? The current version stays in the history.` : `Pulihkan halaman ke versi ${selectedRevision}? Versi sekarang tetap ada di riwayat.`))) return;
    try {
      current = await api.post(`/api/business/pages/${current.page.id}/revisions/${selectedRevision}/restore`);
      historyModal.hide();
      toast((isEn ? `Page restored from version ${selectedRevision}.` : `Halaman dipulihkan dari versi ${selectedRevision}.`));
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
    button.textContent = (isEn ? 'Summarizing…' : 'Merangkum…');
    try {
      const result = await api.post(`/api/business/digest?projectId=${projectId()}`);
      toast(result.threads === 0 && result.warnings.length ? result.warnings[0]
        : (isEn ? `${result.threads} thread(s) summarized: ${result.pagesCreated} new page(s), ${result.pagesUpdated} updated.` : `${result.threads} thread dirangkum: ${result.pagesCreated} halaman baru, ${result.pagesUpdated} diperbarui.`),
        result.threads === 0 ? 'info' : 'success');
      result.warnings.filter(w => result.threads > 0).forEach(w => toast(w, 'warning'));
    } catch (error) {
      toast(error.message, 'error');
    } finally {
      button.textContent = t('Perbarui sekarang', 'Update now');
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
