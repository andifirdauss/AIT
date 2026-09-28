(() => {
  'use strict';
  const { api, toast, escapeHtml, fmt, bindRowLinks, stream, debounce } = window.AiThread;
  const el = id => document.getElementById(id);

  function row(p) {
    const missing = p.exists ? '' : ' <span class="badge text-bg-warning">file hilang</span>';
    return `<tr data-href="/Project/Detail/${p.id}">
      <td><div class="fw-semibold">${escapeHtml(p.name)}${missing}</div><div class="small text-secondary mono">${escapeHtml(p.rootPath)}</div></td>
      <td class="small mono">${escapeHtml(p.solutionPath)}</td>
      <td>${fmt.number(p.threadCount)}</td>
      <td>${fmt.number(p.sessionCount)}</td>
      <td><a href="/Memory?projectId=${p.id}">${fmt.number(p.memoryCount)}</a></td>
      <td class="text-nowrap" title="${fmt.dateTime(p.lastSeenAt)}">${fmt.relative(p.lastSeenAt)}</td>
      <td class="text-end"><button type="button" class="btn btn-sm btn-outline-danger" data-delete="${p.id}" data-name="${escapeHtml(p.name)}">Hapus</button></td>
    </tr>`;
  }

  async function load() {
    const tbody = el('projectRows');
    try {
      const projects = await api.get('/api/projects');
      tbody.innerHTML = projects.length
        ? projects.map(row).join('')
        : '<tr><td colspan="7" class="empty-state">Belum ada project. Jalankan Claude Code melalui AiThread atau daftarkan manual di atas.</td></tr>';
    } catch (error) {
      tbody.innerHTML = `<tr><td colspan="7" class="empty-state text-danger">${escapeHtml(error.message)}</td></tr>`;
    }
  }

  el('btnRegister').addEventListener('click', async () => {
    const path = el('projectPath').value.trim();
    if (!path) {
      toast('Isi path .sln/.slnx atau foldernya.', 'warning');
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
    if (!confirm(`Hapus project "${button.dataset.name}"? Riwayat thread tetap ada, tetapi memory project ini ikut terhapus.`)) return;
    try {
      await api.del(`/api/projects/${button.dataset.delete}`);
      toast('Project dihapus.');
      load();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  bindRowLinks(el('projectRows'));
  load();
  stream('types=project,thread.created', { 'project.changed': debounce(load, 300), 'thread.created': debounce(load, 1000) });
})();
