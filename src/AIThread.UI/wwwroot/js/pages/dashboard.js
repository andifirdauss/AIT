(() => {
  'use strict';
  const { isEn, t, api, toast, escapeHtml, fmt, threadRow, bindRowLinks, debounce, stream, renderRouterPill, routerKeyState, pager } = window.AIThread;

  const levelOrder = { Debug: 0, Info: 1, Success: 2, Warning: 3, Error: 4 };
  const maxLogLines = 300;
  let logPaused = false;

  const el = id => document.getElementById(id);

  function setState(prefix, ok, text, detail, warn = false) {
    el(`${prefix}Dot`).className = `status-dot ${ok ? (warn ? 'warn' : 'ok') : 'err'}`;
    el(`${prefix}State`).textContent = text;
    el(`${prefix}Detail`).textContent = detail || ' ';
  }

  function renderRouter(status) {
    if (!status) {
      setState('router', false, t('Belum dicek', 'Not checked yet'), t('Atur URL 9router di menu Setting.', 'Set the 9router URL in the Setting menu.'));
      return;
    }
    const [keyText, keyWarn] = routerKeyState(status);
    setState('router', status.isOnline, status.isOnline ? (keyWarn ? t('Online · cek API key', 'Online · check the API key') : 'Online') : 'Offline',
      status.isOnline
        ? `${status.latencyMs} ms · ${status.modelCount} model · ${status.comboCount} combo · ${keyText}`
        : `${status.baseUrl} — ${status.error || t('tidak terhubung', 'not connected')}`,
      keyWarn);
    renderRouterPill(status);
    el('btnStartRouter').classList.toggle('d-none', status.isOnline);
  }

  // Can take minutes when 9router has to be installed first; the request waits until it answers or gives up.
  async function startRouter() {
    const button = el('btnStartRouter');
    button.disabled = true;
    button.textContent = (isEn ? 'Starting…' : 'Menyalakan…');
    try {
      const result = await api.post('/api/router/start');
      toast(result.message, result.isOnline ? 'success' : 'warning');
      renderRouter(await api.get('/api/router/status?refresh=true'));
    } catch (error) {
      toast(error.message, 'error');
    } finally {
      button.disabled = false;
      button.textContent = t('Jalankan 9router', 'Start 9router');
    }
  }

  function renderSummary(summary) {
    renderRouter(summary.router);
    setState('redis', summary.redisConnected, summary.redisConnected ? t('Terhubung', 'Connected') : t('Tidak terhubung', 'Not connected'),
      summary.redisConnected ? (isEn ? `${summary.memory.shortCount} active short memories · ${summary.memory.shortStore || ''}` : `${summary.memory.shortCount} short memory aktif · ${summary.memory.shortStore || ''}`) : t('Memory baru disimpan langsung ke long memory.', 'New memory is saved straight to long memory.'));
    setState('db', summary.databaseConnected, summary.databaseConnected ? t('Terhubung', 'Connected') : t('Tidak terhubung', 'Not connected'),
      summary.databaseConnected ? (isEn ? `${summary.memory.longCount} long memories · ${summary.threadsTotal} threads` : `${summary.memory.longCount} long memory · ${summary.threadsTotal} thread`) : t('Periksa ConnectionStrings:Default.', 'Check ConnectionStrings:Default.'));

    const p = summary.pipeline;
    setState('pipeline', p.isReady, p.isReady ? (isEn ? 'Ready' : 'Siap') : t('Belum lengkap', 'Not complete yet'),
      p.issues.length ? p.issues.join(' ') : `${p.plannerCount} planner · ${p.executorCount} executor · ${p.wrapperCount} wrapper`,
      p.isReady && p.issues.length > 0);

    el('statToday').textContent = fmt.number(summary.threadsToday);
    el('statTotal').textContent = `${fmt.number(summary.threadsTotal)} total`;
    el('statActive').textContent = fmt.number(summary.activeThreads);
    el('statSuccess').textContent = summary.threadsTotal ? `${summary.successRate}%` : '–';
    el('statSpeedup').textContent = summary.averageSpeedup ? `${summary.averageSpeedup}x` : '–';
    el('statTokens').textContent = fmt.number(summary.tokensToday);
    el('statProjects').textContent = fmt.number(summary.projectCount);
    el('statMemory').textContent = (isEn ? `${summary.memory.shortCount} short · ${summary.memory.longCount} long memories` : `${summary.memory.shortCount} short · ${summary.memory.longCount} long memory`);
  }

  async function loadSummary() {
    try {
      renderSummary(await api.get('/api/dashboard/summary'));
    } catch (error) {
      toast(error.message, 'error');
    }
  }

  const threadPager = pager('threadPager', rows => {
    el('threadRows').innerHTML = rows.length
      ? rows.map(threadRow).join('')
      : t('<tr><td colspan="8" class="empty-state">Belum ada thread. Arahkan CLI Anda ke AIT lewat menu CLI, lalu beri perintah dari folder project.</td></tr>', '<tr><td colspan="8" class="empty-state">No thread yet. Point your CLI to AIT from the CLI menu, then give it a command from a project folder.</td></tr>');
  });

  async function loadThreads() {
    try {
      threadPager.set(await api.get('/api/threads?take=25'));
    } catch (error) {
      threadPager.set([]);
      el('threadRows').innerHTML = `<tr><td colspan="8" class="empty-state text-danger">${escapeHtml(error.message)}</td></tr>`;
    }
  }

  async function loadProjects() {
    try {
      const projects = await api.get('/api/projects');
      el('runProject').innerHTML = t('<option value="">Pilih project…</option>', '<option value="">Choose a project…</option>') +
        projects.map(p => `<option value="${p.id}">${escapeHtml(p.name)}</option>`).join('');
    } catch {
      // The run form stays usable once the database is back.
    }
  }

  function logLine(entry) {
    const threadLink = entry.threadId ? ` <a href="/Thread/Detail/${entry.threadId}" class="small">thread</a>` : '';
    return `<div class="log-line level-${entry.level}" data-level="${entry.level}">
      <span class="log-time">${fmt.time(entry.createdAt)}</span>
      <span class="log-category">${escapeHtml(entry.category)}</span>
      <span class="log-message">${escapeHtml(entry.message)}${threadLink}</span>
    </div>`;
  }

  function levelVisible(level) {
    return levelOrder[level] >= levelOrder[el('logLevel').value];
  }

  async function loadLogs() {
    const panel = el('logPanel');
    try {
      const logs = await api.get(`/api/dashboard/logs?take=200&minLevel=${el('logLevel').value}`);
      panel.innerHTML = logs.length ? logs.map(logLine).join('') : t('<div class="empty-state">Belum ada log.</div>', '<div class="empty-state">No log yet.</div>');
    } catch (error) {
      panel.innerHTML = `<div class="empty-state text-danger">${escapeHtml(error.message)}</div>`;
    }
  }

  function appendLog(entry) {
    if (logPaused || !levelVisible(entry.level)) return;
    const panel = el('logPanel');
    panel.querySelector('.empty-state')?.remove();
    panel.insertAdjacentHTML('afterbegin', logLine(entry));
    while (panel.children.length > maxLogLines) panel.lastElementChild.remove();
  }

  async function runThread() {
    const projectId = el('runProject').value;
    const prompt = el('runPrompt').value.trim();
    if (!projectId || !prompt) {
      toast(t('Pilih project dan isi prompt terlebih dahulu.', 'Choose a project and fill in a prompt first.'), 'warning');
      return;
    }
    const button = el('btnRun');
    button.disabled = true;
    try {
      const threadId = await api.post('/api/threads', { projectId, prompt });
      window.location.href = `/Thread/Detail/${threadId}`;
    } catch (error) {
      toast(error.message, 'error');
      button.disabled = false;
    }
  }

  const refreshThreads = debounce(loadThreads, 400);
  const refreshSummary = debounce(loadSummary, 800);

  bindRowLinks(el('threadRows'));
  el('btnRun').addEventListener('click', runThread);
  el('logLevel').addEventListener('change', loadLogs);
  // Red window button: empty the view; new entries keep arriving.
  el('logWindow').addEventListener('win:close', () => {
    el('logPanel').innerHTML = t('<div class="empty-state">Log dikosongkan. Log baru akan muncul di sini.</div>', '<div class="empty-state">Log cleared. New log lines will show up here.</div>');
  });
  el('btnPauseLog').addEventListener('click', event => {
    logPaused = !logPaused;
    event.target.textContent = logPaused ? t('Lanjut', 'Continue') : (isEn ? 'Paused' : 'Jeda');
  });
  el('btnStartRouter').addEventListener('click', startRouter);
  el('btnRefreshRouter').addEventListener('click', async () => {
    try {
      renderRouter(await api.get('/api/router/status?refresh=true'));
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  loadSummary();
  loadThreads();
  loadProjects();
  loadLogs();

  stream('types=log,router,thread.created,thread.updated,memory,project,node', {
    'log': payload => appendLog(payload.data),
    'router.status': payload => renderRouter(payload.data),
    'thread.created': () => { refreshThreads(); refreshSummary(); },
    'thread.updated': () => { refreshThreads(); refreshSummary(); },
    'memory.changed': refreshSummary,
    'project.changed': () => { refreshSummary(); loadProjects(); },
    'node.changed': refreshSummary
  });
})();
