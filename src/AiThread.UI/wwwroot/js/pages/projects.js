(() => {
  'use strict';
  const { api, toast, escapeHtml, fmt, bindRowLinks, stream, debounce } = window.AiThread;
  const el = id => document.getElementById(id);
  const browsePathKey = 'aithread-browse-path';

  // A folder marked as project without a recognized stack is a plain repository (.git only).
  // The key that shares memory between the clones of a repository on every developer's machine.
  function projectKeyBadge(p) {
    if (p.keySource === 'Local') {
      return '<div class="small"><span class="badge text-bg-warning" title="Memory project ini hanya ada di mesin ini. Tambah git remote, file .aithread/project.json, atau isi key manual di detail project.">key lokal — belum dibagi</span></div>';
    }
    const source = { File: '.aithread/project.json', Manual: 'manual', GitRemote: 'git remote' }[p.keySource] || p.keySource;
    return `<div class="small text-secondary" title="Memory dibagi antar developer lewat key ini (${escapeHtml(source)})">🔑 <span class="mono">${escapeHtml(p.key)}</span></div>`;
  }

  function stackBadges(stacks, gitFallback) {
    if (stacks?.length) return stacks.map(s => `<span class="badge text-bg-primary me-1">${escapeHtml(s)}</span>`).join('');
    return gitFallback ? '<span class="badge text-bg-secondary">git</span>' : '<span class="small text-secondary">–</span>';
  }

  function row(p) {
    const missing = p.exists ? '' : ' <span class="badge text-bg-warning">folder hilang</span>';
    return `<tr data-href="/Project/Detail/${p.id}">
      <td><div class="fw-semibold">${escapeHtml(p.name)}${missing}</div><div class="small text-secondary mono">${escapeHtml(p.rootPath)}</div>${projectKeyBadge(p)}</td>
      <td>${stackBadges(p.stacks, false)}</td>
      <td>${fmt.number(p.threadCount)}</td>
      <td>${fmt.number(p.sessionCount)}</td>
      <td><a href="/Memory?projectId=${p.id}">${fmt.number(p.memoryCount)}</a></td>
      <td class="text-nowrap" title="${fmt.dateTime(p.lastSeenAt)}">${fmt.relative(p.lastSeenAt)}</td>
      <td class="text-end"><button type="button" class="btn btn-sm btn-outline-danger" data-delete="${p.id}" data-name="${escapeHtml(p.name)}" data-local-key="${p.keySource === 'Local'}">Hapus</button></td>
    </tr>`;
  }

  async function load() {
    const tbody = el('projectRows');
    try {
      const projects = await api.get('/api/projects');
      tbody.innerHTML = projects.length
        ? projects.map(row).join('')
        : '<tr><td colspan="7" class="empty-state">Belum ada project. Klik <strong>Telusuri…</strong> untuk mencari folder project, atau buka Claude Code di folder project (berisi .git atau file build) melalui AiThread.</td></tr>';
    } catch (error) {
      tbody.innerHTML = `<tr><td colspan="7" class="empty-state text-danger">${escapeHtml(error.message)}</td></tr>`;
    }
  }

  el('btnRegister').addEventListener('click', async () => {
    const path = el('projectPath').value.trim();
    if (!path) {
      toast('Isi path folder project, atau klik Telusuri….', 'warning');
      return;
    }
    try {
      const project = await api.post('/api/projects', { path });
      toast(`Project ${project.name} terdaftar.`);
      el('projectPath').value = '';
      load();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('projectRows').addEventListener('click', async event => {
    const button = event.target.closest('[data-delete]');
    if (!button) return;
    if (!confirm(`Hapus project "${button.dataset.name}"? Riwayat thread tetap ada; memory yang hanya ada di mesin ini ikut terhapus.`)) return;
    // Memory in a shared store also belongs to the other developers of this project: kept unless asked.
    const includeShared = button.dataset.localKey !== 'true'
      && confirm('Jika memory disimpan di database bersama (Setting → Penyimpanan memory), hapus juga memory project ini di sana untuk SEMUA developer?\n\nOK = hapus juga · Batal = simpan untuk developer lain');
    try {
      await api.del(`/api/projects/${button.dataset.delete}${includeShared ? '?includeSharedMemory=true' : ''}`);
      toast('Project dihapus.');
      load();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  // --- Project browser -------------------------------------------------------------------------

  const browseModal = bootstrap.Modal.getOrCreateInstance(el('browseModal'));
  const selected = new Set();
  let current = null;
  let lastScan = null;

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
      ? `<a class="badge text-bg-success text-decoration-none" href="/Project/Detail/${c.projectId}">Terdaftar</a>`
      : '<span class="badge text-bg-secondary">Baru</span>';
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
      : '<div class="list-group-item small text-secondary">Tidak ada sub-folder.</div>';
  }

  function renderProjects() {
    el('projectList').innerHTML = !current.path
      ? '<div class="list-group-item small text-secondary">Pilih drive atau lokasi untuk mulai.</div>'
      : current.projects.length
        ? current.projects.map(c => projectItem(c, c.path)).join('')
        : '<div class="list-group-item small text-secondary">Folder ini bukan root project (tidak ada .git atau file build). Buka folder yang bertanda stack atau <span class="badge text-bg-secondary">git</span>, gunakan Scan, atau isi path-nya di kolom Daftarkan manual.</div>';
  }

  function renderScan() {
    const button = el('btnSelectAllNew');
    if (!lastScan) {
      el('scanResults').innerHTML = '';
      button.classList.add('d-none');
      return;
    }

    const truncated = lastScan.truncated ? ' · <span class="text-warning">berhenti lebih awal (batas waktu/jumlah) — persempit folder atau kedalaman</span>' : '';
    el('scanInfo').innerHTML = `${lastScan.projects.length} project dari ${fmt.number(lastScan.foldersScanned)} folder di <span class="mono">${escapeHtml(lastScan.root)}</span> · ${fmt.ms(lastScan.elapsedMs)}${truncated}`;
    el('scanResults').innerHTML = lastScan.projects.length
      ? lastScan.projects.map(c => projectItem(c, relativeTo(lastScan.root, c.path))).join('')
      : '<div class="list-group-item small text-secondary">Tidak ditemukan folder project (.git atau file build).</div>';
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
    el('selectionInfo').textContent = count ? `${count} project dipilih.` : 'Belum ada project dipilih.';
    el('btnRegisterSelected').disabled = count === 0;
    el('btnRegisterSelected').textContent = count ? `Daftarkan terpilih (${count})` : 'Daftarkan terpilih';
  }

  async function fetchFolder(path) {
    return api.get(`/api/projects/browse${path ? `?path=${encodeURIComponent(path)}` : ''}`);
  }

  async function browse(path) {
    el('folderList').innerHTML = '<div class="list-group-item small text-secondary">Memuat…</div>';
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
    el('scanInfo').textContent = 'Memindai sub-folder…';
    el('scanResults').innerHTML = '<div class="list-group-item small text-secondary"><span class="spinner-border spinner-border-sm me-2"></span>Memindai…</div>';
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

  async function openBrowser() {
    selected.clear();
    lastScan = null;
    renderScan();
    renderSelection();
    el('scanInfo').textContent = 'Folder build, VCS, tersembunyi, dan junction dilewati.';
    browseModal.show();
    await browse(el('projectPath').value.trim() || storedPath());
  }

  async function registerSelected() {
    const paths = [...selected];
    if (!paths.length) return;
    const button = el('btnRegisterSelected');
    button.disabled = true;
    try {
      const result = await api.post('/api/projects/batch', { paths });
      toast(`${result.registered} project terdaftar${result.alreadyRegistered ? `, ${result.alreadyRegistered} sudah terdaftar` : ''}.`);
      result.errors.forEach(e => toast(e, 'warning'));

      const failed = new Set(result.errors.map(e => e.split(': ')[0]));
      selected.clear();
      if (lastScan) lastScan.projects.forEach(c => { if (paths.includes(c.path) && !failed.has(c.path)) c.isRegistered = true; });
      await browse(current?.path);
      renderScan();
      renderSelection();
      load();
    } catch (error) {
      toast(error.message, 'error');
      button.disabled = false;
    }
  }

  el('btnBrowse').addEventListener('click', openBrowser);
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

  bindRowLinks(el('projectRows'));
  load();
  stream('types=project,thread.created', { 'project.changed': debounce(load, 300), 'thread.created': debounce(load, 1000) });
})();
