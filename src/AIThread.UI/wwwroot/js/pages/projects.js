(() => {
  'use strict';
  const { isEn, t, api, toast, escapeHtml, fmt, bindRowLinks, stream, debounce, pager } = window.AIThread;
  const { stackBadges } = window.AIThread.projectBrowser;
  const el = id => document.getElementById(id);

  // The key that shares memory between the clones of a repository on every developer's machine.
  function projectKeyBadge(p) {
    if (p.keySource === 'Local') {
      return t('<div class="small"><span class="badge text-bg-warning" title="Memory project ini hanya ada di mesin ini. Tambah git remote, file .aithread/project.json, atau isi key manual di detail project.">key lokal — belum dibagi</span></div>', '<div class="small"><span class="badge text-bg-warning" title="The memory of this project exists on this machine only. Add a git remote, a .aithread/project.json file, or fill in a manual key in the project detail.">local key — not shared yet</span></div>');
    }
    const source = { File: '.aithread/project.json', Manual: 'manual', GitRemote: 'git remote' }[p.keySource] || p.keySource;
    return (isEn ? `<div class="small text-secondary" title="Memory is shared between developers through this key (${escapeHtml(source)})">🔑 <span class="mono">${escapeHtml(p.key)}</span></div>` : `<div class="small text-secondary" title="Memory dibagi antar developer lewat key ini (${escapeHtml(source)})">🔑 <span class="mono">${escapeHtml(p.key)}</span></div>`);
  }

  function row(p) {
    const missing = p.exists ? '' : (isEn ? ' <span class="badge text-bg-warning">folder missing</span>' : ' <span class="badge text-bg-warning">folder hilang</span>');
    return (isEn ? `<tr data-href="/Project/Detail/${p.id}">
      <td><div class="fw-semibold">${escapeHtml(p.name)}${missing}</div><div class="small text-secondary mono">${escapeHtml(p.rootPath)}</div>${projectKeyBadge(p)}</td>
      <td>${stackBadges(p.stacks, false)}</td>
      <td>${fmt.number(p.threadCount)}</td>
      <td>${fmt.number(p.sessionCount)}</td>
      <td><a href="/Memory?projectId=${p.id}">${fmt.number(p.memoryCount)}</a></td>
      <td class="text-nowrap" title="${fmt.dateTime(p.lastSeenAt)}">${fmt.relative(p.lastSeenAt)}</td>
      <td class="text-end"><button type="button" class="btn btn-sm btn-outline-danger" data-delete="${p.id}" data-name="${escapeHtml(p.name)}" data-local-key="${p.keySource === 'Local'}">Delete</button></td>
    </tr>` : `<tr data-href="/Project/Detail/${p.id}">
      <td><div class="fw-semibold">${escapeHtml(p.name)}${missing}</div><div class="small text-secondary mono">${escapeHtml(p.rootPath)}</div>${projectKeyBadge(p)}</td>
      <td>${stackBadges(p.stacks, false)}</td>
      <td>${fmt.number(p.threadCount)}</td>
      <td>${fmt.number(p.sessionCount)}</td>
      <td><a href="/Memory?projectId=${p.id}">${fmt.number(p.memoryCount)}</a></td>
      <td class="text-nowrap" title="${fmt.dateTime(p.lastSeenAt)}">${fmt.relative(p.lastSeenAt)}</td>
      <td class="text-end"><button type="button" class="btn btn-sm btn-outline-danger" data-delete="${p.id}" data-name="${escapeHtml(p.name)}" data-local-key="${p.keySource === 'Local'}">Hapus</button></td>
    </tr>`);
  }

  const projectPager = pager('projectPager', rows => {
    el('projectRows').innerHTML = rows.length
      ? rows.map(row).join('')
      : t('<tr><td colspan="7" class="empty-state">Belum ada project. Klik <strong>Telusuri…</strong> untuk mencari folder project, atau buka CLI Anda di folder project (berisi .git atau file build) melalui AIT.</td></tr>', '<tr><td colspan="7" class="empty-state">No project yet. Click <strong>Browse…</strong> to find a project folder, or open your CLI in a project folder (with .git or a build file) through AIT.</td></tr>');
  });

  async function load() {
    try {
      projectPager.set(await api.get('/api/projects'));
    } catch (error) {
      projectPager.set([]);
      el('projectRows').innerHTML = `<tr><td colspan="7" class="empty-state text-danger">${escapeHtml(error.message)}</td></tr>`;
    }
  }

  el('btnRegister').addEventListener('click', async () => {
    const path = el('projectPath').value.trim();
    if (!path) {
      toast(t('Isi path folder project, atau klik Telusuri….', 'Fill in the project folder path, or click Browse….'), 'warning');
      return;
    }
    try {
      const project = await api.post('/api/projects', { path });
      toast((isEn ? `Project ${project.name} registered.` : `Project ${project.name} terdaftar.`));
      el('projectPath').value = '';
      load();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('projectRows').addEventListener('click', async event => {
    const button = event.target.closest('[data-delete]');
    if (!button) return;
    if (!confirm((isEn ? `Delete project "${button.dataset.name}"? The thread history stays; memory that exists only on this machine is deleted too.` : `Hapus project "${button.dataset.name}"? Riwayat thread tetap ada; memory yang hanya ada di mesin ini ikut terhapus.`))) return;
    // Memory in a shared store also belongs to the other developers of this project: kept unless asked.
    const includeShared = button.dataset.localKey !== 'true'
      && confirm(t('Jika memory disimpan di database bersama (Setting → Penyimpanan memory), hapus juga memory project ini di sana untuk SEMUA developer?\n\nOK = hapus juga · Batal = simpan untuk developer lain', 'When memory is kept in a shared database (Setting → Memory storage), also delete the memory of this project there for ALL developers?\n\nOK = delete it too · Cancel = keep it for the other developers'));
    try {
      await api.del(`/api/projects/${button.dataset.delete}${includeShared ? '?includeSharedMemory=true' : ''}`);
      toast(t('Project dihapus.', 'Project deleted.'));
      load();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('btnBrowse').addEventListener('click', () =>
    window.AIThread.projectBrowser.open({ startPath: el('projectPath').value.trim(), onRegistered: load }));

  bindRowLinks(el('projectRows'));
  load();
  stream('types=project,thread.created', { 'project.changed': debounce(load, 300), 'thread.created': debounce(load, 1000) });
})();
