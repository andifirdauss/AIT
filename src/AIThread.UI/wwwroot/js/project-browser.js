// Project folder browser (Views/Shared/_ProjectBrowser.cshtml), shared by the Project and Setup pages:
// AIThread.projectBrowser.open({ startPath, onRegistered }). onRegistered runs after projects were registered.
(() => {
  'use strict';
  const { isEn, t, api, toast, escapeHtml, fmt } = window.AIThread;
  const el = id => document.getElementById(id);
  const browsePathKey = 'aithread-browse-path';

  // A folder marked as project without a recognized stack is a plain repository (.git only).
  function stackBadges(stacks, gitFallback) {
    if (stacks?.length) return stacks.map(s => `<span class="badge text-bg-primary me-1">${escapeHtml(s)}</span>`).join('');
    return gitFallback ? '<span class="badge text-bg-secondary">git</span>' : '<span class="small text-secondary">–</span>';
  }

  window.AIThread.projectBrowser = { open: () => Promise.resolve(), stackBadges };
  if (!el('browseModal')) return;

  const browseModal = bootstrap.Modal.getOrCreateInstance(el('browseModal'));
  const selected = new Set();
  let current = null;
  let lastScan = null;
  let onRegistered = () => {};

  function rememberPath(path) {
    try {
      localStorage.setItem(browsePathKey, path || '');
    } catch {
      // Storage unavailable — the browser just starts at the top level next time.
    }
  }

  function storedPath() {
    try {
      return localStorage.getItem(browsePathKey) || '';
    } catch {
      return '';
    }
  }

  function projectItem(c, subtitle) {
    const status = c.isRegistered
      ? (isEn ? `<a class="badge text-bg-success text-decoration-none" href="/Project/Detail/${c.projectId}">Registered</a>` : `<a class="badge text-bg-success text-decoration-none" href="/Project/Detail/${c.projectId}">Terdaftar</a>`)
      : t('<span class="badge text-bg-secondary">Baru</span>', '<span class="badge text-bg-secondary">New</span>');
    const checked = c.isRegistered || selected.has(c.path) ? 'checked' : '';
    return `<label class="list-group-item d-flex align-items-center gap-2">
      <input class="form-check-input flex-shrink-0 mt-0" type="checkbox" data-select="${escapeHtml(c.path)}" ${checked} ${c.isRegistered ? 'disabled' : ''} />
      <span class="flex-grow-1 text-break"><span class="fw-semibold">${escapeHtml(c.name)}</span> ${stackBadges(c.stacks, true)}
        <span class="d-block small text-secondary mono">${escapeHtml(subtitle)}</span></span>
      ${status}
    </label>`;
  }

  function renderQuick(locations) {
    el('quickLocations').innerHTML = locations
      .map(l => `<button type="button" class="btn btn-sm btn-outline-secondary" data-path="${escapeHtml(l.path)}" title="${escapeHtml(l.path)}">${escapeHtml(l.name)}</button>`)
      .join('');
  }

  function renderFolders() {
    el('folderTitle').textContent = current.path ? `Folder (${current.folders.length})` : 'Drive';
    el('folderList').innerHTML = current.folders.length
      ? current.folders.map(f => `<button type="button" class="list-group-item list-group-item-action d-flex justify-content-between align-items-center" data-path="${escapeHtml(f.path)}">
          <span class="text-break">📁 ${escapeHtml(f.name)}</span>${f.isProject ? `<span>${stackBadges(f.stacks, true)}</span>` : ''}
        </button>`).join('')
      : t('<div class="list-group-item small text-secondary">Tidak ada sub-folder.</div>', '<div class="list-group-item small text-secondary">No sub-folders.</div>');
  }

  function renderProjects() {
    el('projectList').innerHTML = !current.path
      ? t('<div class="list-group-item small text-secondary">Pilih drive atau lokasi untuk mulai.</div>', '<div class="list-group-item small text-secondary">Choose a drive or location to start.</div>')
      : current.projects.length
        ? current.projects.map(c => projectItem(c, c.path)).join('')
        : t('<div class="list-group-item small text-secondary">Folder ini bukan root project (tidak ada .git atau file build). Buka folder yang bertanda stack atau <span class="badge text-bg-secondary">git</span>, gunakan Scan, atau isi path-nya di kolom Daftarkan manual.</div>', '<div class="list-group-item small text-secondary">This folder is not a project root (no .git or build file). Open a folder marked with a stack or <span class="badge text-bg-secondary">git</span>, use Scan, or fill in its path in the Register manually field.</div>');
  }

  function renderScan() {
    const button = el('btnSelectAllNew');
    if (!lastScan) {
      el('scanResults').innerHTML = '';
      button.classList.add('d-none');
      return;
    }

    const truncated = lastScan.truncated ? t(' · <span class="text-warning">berhenti lebih awal (batas waktu/jumlah) — persempit folder atau kedalaman</span>', ' · <span class="text-warning">stopped early (time/count limit) — narrow the folder or the depth</span>') : '';
    el('scanInfo').innerHTML = (isEn ? `${lastScan.projects.length} project(s) in ${fmt.number(lastScan.foldersScanned)} folders under <span class="mono">${escapeHtml(lastScan.root)}</span> · ${fmt.ms(lastScan.elapsedMs)}${truncated}` : `${lastScan.projects.length} project dari ${fmt.number(lastScan.foldersScanned)} folder di <span class="mono">${escapeHtml(lastScan.root)}</span> · ${fmt.ms(lastScan.elapsedMs)}${truncated}`);
    el('scanResults').innerHTML = lastScan.projects.length
      ? lastScan.projects.map(c => projectItem(c, relativeTo(lastScan.root, c.path))).join('')
      : t('<div class="list-group-item small text-secondary">Tidak ditemukan folder project (.git atau file build).</div>', '<div class="list-group-item small text-secondary">No project folder found (.git or build file).</div>');
    button.classList.toggle('d-none', !lastScan.projects.some(c => !c.isRegistered));
  }

  function relativeTo(root, directory) {
    if (directory.toLowerCase() === root.toLowerCase()) return '.';
    return directory.toLowerCase().startsWith(root.toLowerCase())
      ? directory.substring(root.length).replace(/^[\\/]+/, '')
      : directory;
  }

  function renderSelection() {
    const count = selected.size;
    el('selectionInfo').textContent = count ? (isEn ? `${count} project(s) chosen.` : `${count} project dipilih.`) : t('Belum ada project dipilih.', 'No project chosen yet.');
    el('btnRegisterSelected').disabled = count === 0;
    el('btnRegisterSelected').textContent = count ? (isEn ? `Register chosen (${count})` : `Daftarkan terpilih (${count})`) : t('Daftarkan terpilih', 'Register chosen');
  }

  async function fetchFolder(path) {
    return api.get(`/api/projects/browse${path ? `?path=${encodeURIComponent(path)}` : ''}`);
  }

  async function browse(path) {
    el('folderList').innerHTML = t('<div class="list-group-item small text-secondary">Memuat…</div>', '<div class="list-group-item small text-secondary">Loading…</div>');
    try {
      current = await fetchFolder(path);
    } catch (error) {
      // Unknown or unreadable folder: stay where we were, or fall back to the drive list.
      toast(error.message, 'error');
      try {
        current ??= await fetchFolder('');
      } catch (fallbackError) {
        el('folderList').innerHTML = `<div class="list-group-item small text-danger">${escapeHtml(fallbackError.message)}</div>`;
        return;
      }
    }

    el('browsePath').value = current.path || '';
    el('btnUp').disabled = !current.path;
    el('btnScan').disabled = !current.path;
    rememberPath(current.path);
    renderQuick(current.quickLocations);
    renderFolders();
    renderProjects();
  }

  async function scan() {
    if (!current?.path) return;
    const button = el('btnScan');
    button.disabled = true;
    el('scanInfo').textContent = (isEn ? 'Scanning sub-folders…' : 'Memindai sub-folder…');
    el('scanResults').innerHTML = (isEn ? '<div class="list-group-item small text-secondary"><span class="spinner-border spinner-border-sm me-2"></span>Scanning…</div>' : '<div class="list-group-item small text-secondary"><span class="spinner-border spinner-border-sm me-2"></span>Memindai…</div>');
    try {
      lastScan = await api.get(`/api/projects/scan?path=${encodeURIComponent(current.path)}&depth=${el('scanDepth').value}`);
      renderScan();
    } catch (error) {
      el('scanResults').innerHTML = '';
      el('scanInfo').textContent = error.message;
    } finally {
      button.disabled = false;
    }
  }

  async function open({ startPath = '', onRegistered: callback } = {}) {
    onRegistered = callback || (() => {});
    selected.clear();
    lastScan = null;
    renderScan();
    renderSelection();
    el('scanInfo').textContent = t('Folder build, VCS, tersembunyi, dan junction dilewati.', 'Build, VCS, hidden and junction folders are skipped.');
    browseModal.show();
    await browse(startPath || storedPath());
  }

  async function registerSelected() {
    const paths = [...selected];
    if (!paths.length) return;
    const button = el('btnRegisterSelected');
    button.disabled = true;
    try {
      const result = await api.post('/api/projects/batch', { paths });
      toast(`${result.registered} ${isEn ? 'project(s) registered' : 'project terdaftar'}${result.alreadyRegistered ? (isEn ? `, ${result.alreadyRegistered} already registered` : `, ${result.alreadyRegistered} sudah terdaftar`) : ''}.`);
      result.errors.forEach(e => toast(e, 'warning'));

      const failed = new Set(result.errors.map(e => e.split(': ')[0]));
      selected.clear();
      if (lastScan) lastScan.projects.forEach(c => { if (paths.includes(c.path) && !failed.has(c.path)) c.isRegistered = true; });
      await browse(current?.path);
      renderScan();
      renderSelection();
      onRegistered();
    } catch (error) {
      toast(error.message, 'error');
      button.disabled = false;
    }
  }

  el('btnUp').addEventListener('click', () => browse(current?.parent || ''));
  el('btnOpen').addEventListener('click', () => browse(el('browsePath').value.trim()));
  el('browsePath').addEventListener('keydown', event => {
    if (event.key === 'Enter') {
      event.preventDefault();
      browse(el('browsePath').value.trim());
    }
  });
  el('btnScan').addEventListener('click', scan);
  el('btnRegisterSelected').addEventListener('click', registerSelected);
  el('btnSelectAllNew').addEventListener('click', () => {
    lastScan?.projects.filter(c => !c.isRegistered).forEach(c => selected.add(c.path));
    renderScan();
    renderProjects();
    renderSelection();
  });

  // Folder buttons and quick locations navigate; checkboxes build the selection.
  el('browseModal').addEventListener('click', event => {
    const target = event.target.closest('[data-path]');
    if (target) browse(target.dataset.path);
  });
  el('browseModal').addEventListener('change', event => {
    const box = event.target.closest('[data-select]');
    if (!box) return;
    box.checked ? selected.add(box.dataset.select) : selected.delete(box.dataset.select);
    // The same project can appear in both lists; keep them in sync.
    el('browseModal').querySelectorAll('[data-select]').forEach(other => {
      if (other.dataset.select === box.dataset.select && !other.disabled) other.checked = box.checked;
    });
    renderSelection();
  });

  window.AIThread.projectBrowser = { open, stackBadges };
})();
