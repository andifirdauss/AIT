// Log & diagnostics: incidents (errors with stack trace and companion files), the activity log, the run logs of
// AIThread.exe, and the diagnostic ZIP. ?threadId=<id> narrows incidents and the activity log to one thread;
// ?incident=<id> opens that incident.
(() => {
  'use strict';
  const { t, isEn, api, toast, escapeHtml, fmt, stream, debounce, pager } = window.AIThread;
  const el = id => document.getElementById(id);
  const params = new URLSearchParams(location.search);
  const threadId = params.get('threadId') || '';

  const levelBadge = level => {
    const name = String(level);
    const css = /error|critical/i.test(name) ? 'text-bg-danger' : /warn/i.test(name) ? 'text-bg-warning' : /success/i.test(name) ? 'text-bg-success' : 'text-bg-secondary';
    return `<span class="badge ${css}">${escapeHtml(name)}</span>`;
  };
  const size = bytes => bytes < 1024 ? `${bytes} B` : bytes < 1048576 ? `${(bytes / 1024).toFixed(1)} KB` : `${(bytes / 1048576).toFixed(1)} MB`;

  if (threadId) {
    el('threadFilter').classList.remove('d-none');
    el('threadFilter').innerHTML = isEn
      ? `Showing incidents and activity of thread <a href="/Thread/Detail/${encodeURIComponent(threadId)}" class="mono">${escapeHtml(threadId.slice(0, 8))}</a>. <a href="/Log">Show all</a>`
      : `Menampilkan insiden dan aktivitas thread <a href="/Thread/Detail/${encodeURIComponent(threadId)}" class="mono">${escapeHtml(threadId.slice(0, 8))}</a>. <a href="/Log">Tampilkan semua</a>`;
    el('exportThread').value = threadId;
  }

  // ---- Incidents
  const incidentPager = pager('incidentPager', drawIncidents, 25);

  function drawIncidents(items) {
    el('incidentRows').innerHTML = items.length
      ? items.map(i => `
        <tr data-incident="${escapeHtml(i.id)}">
          <td class="text-nowrap small">${fmt.dateTime(i.lastAt)}</td>
          <td>${levelBadge(i.level)}</td>
          <td class="small">${escapeHtml(i.category)}</td>
          <td class="small text-break">${escapeHtml(i.title)}${i.threadId ? ` <span class="text-secondary mono">· thread ${escapeHtml(i.threadId.slice(0, 8))}</span>` : ''}</td>
          <td class="text-end small">${fmt.number(i.count)}×</td>
          <td class="text-end small">${i.fileCount || '–'}</td>
        </tr>`).join('')
      : `<tr><td colspan="6" class="text-secondary">${t('Belum ada insiden. Error dengan stack trace dan executor yang gagal tercatat di sini.', 'No incidents yet. Errors with a stack trace and failed executors are recorded here.')}</td></tr>`;
  }

  async function loadIncidents() {
    const query = new URLSearchParams({ take: '500' });
    if (threadId) query.set('threadId', threadId);
    const search = el('incidentSearch').value.trim();
    if (search) query.set('q', search);
    try {
      const items = await api.get(`/api/diagnostics/incidents?${query}`);
      incidentPager.set(items, true);
      el('incidentBadge').textContent = items.length;
      el('incidentBadge').classList.toggle('d-none', !items.length);
    } catch (error) {
      el('incidentRows').innerHTML = `<tr><td colspan="6" class="text-danger">${escapeHtml(error.message)}</td></tr>`;
    }
  }

  const incidentModal = bootstrap.Modal.getOrCreateInstance(el('incidentModal'));
  let current = null;

  async function openIncident(id) {
    try {
      current = await api.get(`/api/diagnostics/incidents/${encodeURIComponent(id)}`);
    } catch (error) {
      toast(error.message, 'error');
      return;
    }
    const i = current;
    el('incidentModalTitle').textContent = i.title;
    el('incidentMeta').innerHTML = [
      levelBadge(i.level),
      escapeHtml(i.categories.join(', ')),
      `${fmt.dateTime(i.createdAt)}${i.count > 1 ? (isEn ? ` · ${i.count}× until ${fmt.dateTime(i.lastAt)}` : ` · ${i.count}× sampai ${fmt.dateTime(i.lastAt)}`) : ''}`,
      i.threadId ? `<a href="/Thread/Detail/${encodeURIComponent(i.threadId)}" class="mono">thread ${escapeHtml(i.threadId.slice(0, 8))}</a>` : '',
      `<span class="mono">${escapeHtml(i.id)}</span>`
    ].filter(Boolean).join(' · ');
    el('incidentMessage').innerHTML = i.message && i.message !== i.title
      ? `<h6>${t('Pesan', 'Message')}</h6><pre class="code-block pre-wrap mb-3">${escapeHtml(i.message)}</pre>` : '';
    el('incidentStack').innerHTML = i.stackTrace
      ? `<h6>Stack trace${i.exceptionType ? ` <span class="small text-secondary mono">${escapeHtml(i.exceptionType)}</span>` : ''}</h6><pre class="code-block log-view">${escapeHtml(i.stackTrace)}</pre>` : '';
    el('incidentFiles').innerHTML = i.files.length
      ? i.files.map(f => `
        <div class="list-group-item d-flex justify-content-between align-items-center gap-2">
          <span class="mono small text-break">${escapeHtml(f.name)} <span class="text-secondary">· ${size(f.size)}</span></span>
          <span class="d-flex gap-1">
            <button type="button" class="btn btn-outline-secondary btn-sm" data-view="${escapeHtml(f.name)}">${t('Lihat', 'View')}</button>
            <a class="btn btn-outline-secondary btn-sm" href="/api/diagnostics/incidents/${encodeURIComponent(i.id)}/files/${encodeURIComponent(f.name)}?download=true">${t('Unduh', 'Download')}</a>
          </span>
        </div>`).join('')
      : `<div class="list-group-item small text-secondary">${t('Tidak ada file pendamping.', 'No companion files.')}</div>`;
    el('incidentFileView').innerHTML = '';
    incidentModal.show();
  }

  el('incidentRows').addEventListener('click', event => {
    const row = event.target.closest('tr[data-incident]');
    if (row) openIncident(row.dataset.incident);
  });

  el('incidentFiles').addEventListener('click', async event => {
    const button = event.target.closest('button[data-view]');
    if (!button || !current) return;
    const name = button.dataset.view;
    try {
      const response = await fetch(`/api/diagnostics/incidents/${encodeURIComponent(current.id)}/files/${encodeURIComponent(name)}`);
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      el('incidentFileView').innerHTML = `<h6 class="mono">${escapeHtml(name)}</h6><pre class="code-block log-view">${escapeHtml(await response.text())}</pre>`;
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('btnIncidentCopy').addEventListener('click', async () => {
    if (!current) return;
    const text = `${current.title}\n${current.categories.join(', ')} · ${current.id}\n\n${current.stackTrace || current.message || ''}`;
    try {
      await navigator.clipboard.writeText(text);
      toast(t('Disalin.', 'Copied.'), 'success');
    } catch {
      toast(t('Tidak bisa menyalin ke clipboard.', 'Cannot copy to the clipboard.'), 'error');
    }
  });

  el('btnIncidentDelete').addEventListener('click', async () => {
    if (!current || !confirm(t('Hapus insiden ini beserta file pendampingnya?', 'Delete this incident with its companion files?'))) return;
    try {
      await api.del(`/api/diagnostics/incidents/${encodeURIComponent(current.id)}`);
      incidentModal.hide();
      loadIncidents();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('btnIncidentClear').addEventListener('click', async () => {
    if (!confirm(t('Hapus semua insiden di mesin ini?', 'Delete every incident on this machine?'))) return;
    try {
      const count = await api.del('/api/diagnostics/incidents');
      toast(isEn ? `${count} incident(s) deleted.` : `${count} insiden dihapus.`, 'success');
      loadIncidents();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('incidentSearch').addEventListener('input', debounce(loadIncidents, 300));
  el('btnIncidentRefresh').addEventListener('click', loadIncidents);

  // ---- Activity log
  const activityPager = pager('activityPager', drawActivity, 100);
  let activity = [];

  function drawActivity(items) {
    el('activityRows').innerHTML = items.length
      ? items.map(l => `
        <tr>
          <td class="text-nowrap small">${fmt.dateTime(l.createdAt)}</td>
          <td>${levelBadge(l.level)}</td>
          <td class="small">${escapeHtml(l.category)}</td>
          <td class="small pre-wrap">${escapeHtml(l.message)}${l.threadId && !threadId ? ` <a href="/Log?threadId=${encodeURIComponent(l.threadId)}" class="mono text-secondary">${escapeHtml(l.threadId.slice(0, 8))}</a>` : ''}</td>
        </tr>`).join('')
      : `<tr><td colspan="4" class="text-secondary">${t('Tidak ada entri.', 'No entries.')}</td></tr>`;
  }

  function filterActivity() {
    const search = el('activitySearch').value.trim().toLowerCase();
    activityPager.set(search ? activity.filter(l => `${l.category} ${l.message}`.toLowerCase().includes(search)) : activity, true);
  }

  async function loadActivity() {
    const query = new URLSearchParams({ take: el('activityTake').value });
    if (el('activityLevel').value) query.set('minLevel', el('activityLevel').value);
    if (threadId) query.set('threadId', threadId);
    try {
      activity = await api.get(`/api/dashboard/logs?${query}`);
      filterActivity();
    } catch (error) {
      el('activityRows').innerHTML = `<tr><td colspan="4" class="text-danger">${escapeHtml(error.message)}</td></tr>`;
    }
  }

  el('activityLevel').addEventListener('change', loadActivity);
  el('activityTake').addEventListener('change', loadActivity);
  el('activitySearch').addEventListener('input', debounce(filterActivity, 200));
  el('btnActivityRefresh').addEventListener('click', loadActivity);

  // ---- Run logs
  let selectedLog = null;

  async function loadLogFiles() {
    try {
      const files = await api.get('/api/diagnostics/logs');
      if (!selectedLog && files.length) selectedLog = files[0].name;
      el('logFiles').innerHTML = files.length
        ? files.map(f => `
          <button type="button" class="list-group-item list-group-item-action small ${f.name === selectedLog ? 'active' : ''}" data-log="${escapeHtml(f.name)}">
            <div class="mono text-break">${escapeHtml(f.name)}${f.current ? ` <span class="badge text-bg-success">${t('terbaru', 'latest')}</span>` : ''}</div>
            <div class="${f.name === selectedLog ? '' : 'text-secondary'}">${fmt.dateTime(f.modifiedAt)} · ${size(f.size)}</div>
          </button>`).join('')
        : `<div class="list-group-item small text-secondary">${t('Belum ada file log. File log ditulis saat AIThread.exe berjalan dari menu atau tray; saat dijalankan dengan dotnet run, log tampil di konsol.', 'No log file yet. Log files are written while AIThread.exe runs from the menu or the tray; with dotnet run the log goes to the console.')}</div>`;
      if (selectedLog) loadLog();
    } catch (error) {
      el('logFiles').innerHTML = `<div class="list-group-item small text-danger">${escapeHtml(error.message)}</div>`;
    }
  }

  async function loadLog() {
    if (!selectedLog) return;
    const query = new URLSearchParams({ tail: el('logTail').value });
    const search = el('logSearch').value.trim();
    if (search) query.set('q', search);
    try {
      const log = await api.get(`/api/diagnostics/logs/${encodeURIComponent(selectedLog)}?${query}`);
      el('logInfo').textContent = isEn
        ? `${log.name} · ${fmt.number(log.lines.length)} line(s)${log.truncated ? ', older lines left out' : ''}`
        : `${log.name} · ${fmt.number(log.lines.length)} baris${log.truncated ? ', baris lama tidak ditampilkan' : ''}`;
      const view = el('logContent');
      view.innerHTML = log.lines.map(line => {
        const css = /\b(FAIL|ERROR|CRIT)\b/.test(line) ? 'diff-del' : /\bWARN\b/.test(line) ? 'text-warning' : '';
        return css ? `<span class="${css}">${escapeHtml(line)}</span>` : escapeHtml(line);
      }).join('\n');
      view.scrollTop = view.scrollHeight;
    } catch (error) {
      el('logInfo').textContent = error.message;
      el('logContent').textContent = '';
    }
  }

  el('logFiles').addEventListener('click', event => {
    const button = event.target.closest('button[data-log]');
    if (!button) return;
    selectedLog = button.dataset.log;
    el('logFiles').querySelectorAll('button[data-log]').forEach(b => b.classList.toggle('active', b === button));
    loadLog();
  });
  el('logSearch').addEventListener('input', debounce(loadLog, 400));
  el('logTail').addEventListener('change', loadLog);
  el('btnLogRefresh').addEventListener('click', loadLogFiles);

  // ---- Export
  const exportModal = bootstrap.Modal.getOrCreateInstance(el('exportModal'));
  el('btnExport').addEventListener('click', () => exportModal.show());

  el('btnExportRun').addEventListener('click', async () => {
    const query = new URLSearchParams({
      days: el('exportDays').value,
      incidents: el('exportIncidents').checked,
      activity: el('exportActivity').checked,
      runLogs: el('exportRunLogs').checked
    });
    const thread = el('exportThread').value.trim();
    if (thread) query.set('threadId', thread);

    const button = el('btnExportRun');
    button.disabled = true;
    let response;
    try {
      response = await fetch(`/api/diagnostics/export?${query}`);
    } catch {
      toast(t('Tidak dapat menghubungi server AIT.', 'Cannot reach the AIT server.'), 'error');
      button.disabled = false;
      return;
    }
    button.disabled = false;
    if (!response.ok) {
      const payload = await response.json().catch(() => null);
      toast(payload?.message || payload?.title || `HTTP ${response.status}`, 'error');
      return;
    }
    const name = /filename="?([^";]+)"?/.exec(response.headers.get('Content-Disposition') || '')?.[1] || 'ait-diagnostics.zip';
    const url = URL.createObjectURL(await response.blob());
    const link = Object.assign(document.createElement('a'), { href: url, download: name });
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    exportModal.hide();
    toast(t('ZIP diunduh. Secret sudah disamarkan, tapi kode dan path file bisa ada di dalamnya.', 'ZIP downloaded. Secrets are masked, but code and file paths can be inside.'), 'info');
  });

  // ---- Tabs, live updates, start
  const loaded = new Set(['incidents']);
  document.querySelectorAll('#logTabs [data-tab]').forEach(tab => tab.addEventListener('shown.bs.tab', () => {
    const name = tab.dataset.tab;
    if (loaded.has(name)) return;
    loaded.add(name);
    if (name === 'activity') loadActivity();
    if (name === 'files') loadLogFiles();
  }));

  stream('', { 'incident.recorded': () => loadIncidents() });

  loadIncidents().then(() => {
    if (params.get('incident')) openIncident(params.get('incident'));
  });
})();
