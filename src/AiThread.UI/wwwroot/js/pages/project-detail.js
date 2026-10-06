(() => {
  'use strict';
  const { api, toast, escapeHtml, fmt, threadRow, bindRowLinks, stream, debounce } = window.AiThread;
  const projectId = window.projectId;
  const el = id => document.getElementById(id);

  async function loadDetail() {
    try {
      const detail = await api.get(`/api/projects/${projectId}`);
      const p = detail.project;
      el('projectName').textContent = p.name;
      el('projectPath').textContent = p.rootPath + (p.exists ? '' : '  (folder tidak ditemukan)');
      el('projectStacks').innerHTML = p.stacks.map(s => `<span class="badge text-bg-primary me-1">${escapeHtml(s)}</span>`).join('');
      el('statThreads').textContent = fmt.number(p.threadCount);
      el('statSessions').textContent = fmt.number(p.sessionCount);
      el('statMemory').textContent = fmt.number(p.memoryCount);
      el('statLastSeen').textContent = fmt.relative(p.lastSeenAt);
      renderKey(p);

      el('projectModules').innerHTML = detail.modules.length
        ? detail.modules.map(m => `<li class="list-group-item">${escapeHtml(m)}</li>`).join('')
        : '<li class="list-group-item text-secondary">Tidak ada file solution/build terbaca.</li>';

      el('sessionRows').innerHTML = detail.sessions.length
        ? detail.sessions.map(s => `<tr><td class="mono" title="${escapeHtml(s.sessionId)}">${escapeHtml(s.sessionId.substring(0, 8))}</td><td>${s.threadCount}</td><td>${fmt.relative(s.lastSeenAt)}</td></tr>`).join('')
        : '<tr><td colspan="3" class="text-secondary">Belum ada sesi Claude Code.</td></tr>';
    } catch (error) {
      el('projectName').textContent = error.message;
    }
  }

  const keySources = {
    File: 'dari .aithread/project.json di repository',
    Manual: 'key manual',
    GitRemote: 'dari git remote',
    Local: 'lokal — memory hanya ada di mesin ini. Tambah git remote, file .aithread/project.json, atau isi key manual.'
  };

  function renderKey(p) {
    el('projectKey').textContent = p.key;
    el('projectKeySource').textContent = keySources[p.keySource] || p.keySource;
    el('projectKeySource').className = `small mb-2 ${p.keySource === 'Local' ? 'text-warning' : 'text-secondary'}`;
    // Do not overwrite what is being typed when an SSE refresh arrives.
    if (document.activeElement !== el('manualKey')) el('manualKey').value = p.manualKey || '';
  }

  el('btnSaveKey').addEventListener('click', async () => {
    try {
      const project = await api.put(`/api/projects/${projectId}/key`, { key: el('manualKey').value.trim() || null });
      toast(`Project key: ${project.key}`);
      el('manualKey').blur();
      loadDetail();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  async function loadThreads() {
    try {
      const threads = await api.get(`/api/threads?take=50&projectId=${projectId}`);
      el('threadRows').innerHTML = threads.length
        ? threads.map(threadRow).join('')
        : '<tr><td colspan="8" class="empty-state">Belum ada thread.</td></tr>';
    } catch (error) {
      el('threadRows').innerHTML = `<tr><td colspan="8" class="empty-state text-danger">${escapeHtml(error.message)}</td></tr>`;
    }
  }

  bindRowLinks(el('threadRows'));
  loadDetail();
  loadThreads();

  const refresh = debounce(() => { loadDetail(); loadThreads(); }, 500);
  stream('types=thread.created,thread.updated,memory,project', {
    'thread.created': refresh,
    'thread.updated': refresh,
    'memory.changed': debounce(loadDetail, 500),
    'project.changed': debounce(loadDetail, 500)
  });
})();
