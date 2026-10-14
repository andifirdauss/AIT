(() => {
  'use strict';
  const { api, toast, escapeHtml, fmt, toDate, statusBadge, isActiveStatus, sourceLabel, stream, debounce, md, diffHtml, winFrame } = window.AiThread;

  const threadId = window.threadId;
  const el = id => document.getElementById(id);
  const tasks = new Map();
  const outputs = new Map();
  const expanded = new Set();
  let detail = null;
  let timelineTimer = null;

  function renderHeader() {
    const s = detail.summary;
    el('threadShortId').textContent = s.id.substring(0, 8);
    el('threadStatus').innerHTML = statusBadge(s.status);
    el('threadMeta').innerHTML = [
      s.projectName ? `<a href="/Project/Detail/${s.projectId}">${escapeHtml(s.projectName)}</a>` : 'tanpa project',
      sourceLabel(s.source),
      s.sessionId ? `sesi <span class="mono">${escapeHtml(s.sessionId.substring(0, 8))}</span>` : null,
      fmt.dateTime(s.createdAt),
      detail.workingDirectory ? `<span class="mono">${escapeHtml(detail.workingDirectory)}</span>` : null
    ].filter(Boolean).join(' · ');
    el('threadPrompt').textContent = s.prompt;
    el('btnCancel').classList.toggle('d-none', !isActiveStatus(s.status) && s.status !== 'AwaitingInput');

    // The server decides in the end; these only hide buttons that cannot work.
    const finished = ['Completed', 'Failed', 'Cancelled'].includes(s.status);
    el('btnRetry').classList.toggle('d-none', !finished || !hasUnfinishedPlannedTasks());
    el('btnRollback').classList.toggle('d-none', !finished || !detail.tasks.some(t => t.status === 'Succeeded'));
    if (s.status === 'RolledBack' && detail.rolledBackAt) {
      el('threadMeta').insertAdjacentHTML('beforeend', ` · <span class="text-warning">perubahan file dibatalkan ${fmt.dateTime(detail.rolledBackAt)}</span>`);
    }
  }

  function planTasks() {
    try {
      return detail.planJson ? JSON.parse(detail.planJson).tasks || [] : [];
    } catch {
      return [];
    }
  }

  function hasUnfinishedPlannedTasks() {
    const succeeded = new Set(detail.tasks.filter(t => t.status === 'Succeeded').map(t => t.taskKey.toLowerCase()));
    return planTasks().some(t => !succeeded.has(String(t.id).toLowerCase()));
  }

  function renderPlan() {
    let plan = null;
    try {
      plan = detail.planJson ? JSON.parse(detail.planJson) : null;
    } catch {
      plan = null;
    }

    el('planSummary').textContent = plan?.summary || detail.summary.planSummary ||
      (isActiveStatus(detail.summary.status) ? 'Planner sedang menganalisis…' : 'Tidak ada rencana.');
    const contract = plan?.contract || [];
    el('contractRows').innerHTML = contract.length
      ? contract.map(c => `<tr><td>${escapeHtml(c.name)}</td><td class="mono">${escapeHtml(c.value)}${c.note ? ` <span class="text-secondary">— ${escapeHtml(c.note)}</span>` : ''}</td></tr>`).join('')
      : '<tr><td colspan="2" class="text-secondary">–</td></tr>';

    if (plan?.answer && !(plan.tasks || []).length && !detail.finalResponse) {
      el('finalResponse').innerHTML = md(plan.answer);
    }
  }

  // The cached share is input the provider served from its prompt cache (cheaper than fresh input).
  function plannerTokens() {
    const total = detail.plannerTokens;
    const cached = detail.plannerCachedTokens || 0;
    if (!total || !cached) return fmt.number(total);
    return `${fmt.number(total)} <span class="text-secondary small">(${Math.round(cached * 100 / total)}% cache)</span>`;
  }

  function renderMetrics() {
    const s = detail.summary;
    const rows = [
      ['Planner', detail.plannerModel ? `<span class="mono">${escapeHtml(detail.plannerModel)}</span>` : '–'],
      ['Reviewer', detail.reviewerModel ? `<span class="mono">${escapeHtml(detail.reviewerModel)}</span>` : '–'],
      ['Wrapper', detail.wrapperModel ? `<span class="mono">${escapeHtml(detail.wrapperModel)}</span>` : '–'],
      ['Langkah planner', fmt.number(detail.plannerSteps)],
      ['Planning', fmt.ms(detail.planningMs)],
      ['Eksekusi (nyata)', fmt.ms(s.executionWallMs)],
      ['Eksekusi (jika sekuensial)', fmt.ms(s.executionSumMs)],
      ['Speedup paralel', fmt.speedup(s.executionSumMs, s.executionWallMs)],
      ['Verifikasi', fmt.ms(detail.verificationMs)],
      ['Wrapping', fmt.ms(detail.wrappingMs)],
      ['Total', fmt.ms(s.totalMs)],
      ['Percobaan ulang', detail.retryCount ? `${fmt.number(detail.retryCount)}×` : '–'],
      ['Token planner', plannerTokens()],
      ['Token executor', fmt.number(detail.executorTokens)],
      ['Token reviewer', fmt.number(detail.reviewerTokens)],
      ['Token wrapper', fmt.number(detail.wrapperTokens)]
    ];
    el('metrics').innerHTML = rows.map(([k, v]) => `<dt class="col-6 fw-normal text-secondary">${k}</dt><dd class="col-6 text-end mb-1">${v}</dd>`).join('');

    if (detail.error) {
      el('metrics').insertAdjacentHTML('beforeend', `<dt class="col-12 mt-2 text-danger fw-normal">${escapeHtml(detail.error)}</dt>`);
    }
  }

  const verificationOutcomes = {
    passed: ['success', 'Lolos'],
    failed: ['warning', 'Masih ada masalah'],
    unverified: ['secondary', 'Tidak dapat dipastikan']
  };
  const buildIcons = { passed: '✅', locked: '✅', failed: '❌', timeout: '⏱️', error: '⚠️' };

  function list(items) {
    return `<ul class="small mb-1 ps-3">${items.join('')}</ul>`;
  }

  function roundHtml(r) {
    const parts = [];
    if (r.build) {
      const b = r.build;
      const more = b.errorCount > b.errors.length ? `\n… ${b.errorCount - b.errors.length} error lain` : '';
      parts.push(`<div class="small mb-1">${buildIcons[b.status] || ''} ${escapeHtml(b.headline)}</div>`
        + (b.errors.length ? winFrame({ id: `build-errors-${r.round}`, title: `Error build · putaran ${r.round}`, className: 'mb-2',
          body: `<pre class="code-block small">${escapeHtml(b.errors.join('\n') + more)}</pre>` }) : '')
        + (b.output ? winFrame({ id: `build-output-${r.round}`, title: `Output build · putaran ${r.round}`, className: 'mb-2',
          body: `<pre class="code-block small">${escapeHtml(b.output)}</pre>` }) : ''));
    }
    if (r.review) {
      const v = r.review;
      if (v.error) parts.push(`<div class="small">⚠️ Reviewer tidak memberi hasil: ${escapeHtml(v.error)}</div>`);
      else if (!v.issues.length) parts.push(`<div class="small">✅ Reviewer: ${escapeHtml(v.summary || 'tidak ada masalah.')}</div>`);
      else {
        parts.push(`<div class="small">⚠️ Reviewer menemukan ${v.issues.length} masalah${v.summary ? `: ${escapeHtml(v.summary)}` : ''}</div>`
          + list(v.issues.map(i => `<li><span class="mono">${escapeHtml(i.file)}</span> — ${escapeHtml(i.problem)}<div class="text-secondary">${escapeHtml(i.fix)}</div></li>`)));
      }
    }
    if (r.unfixable?.length) {
      parts.push('<div class="small text-secondary">Tidak bisa diperbaiki otomatis:</div>' + list(r.unfixable.map(u => `<li>${escapeHtml(u)}</li>`)));
    }
    if (r.fixTasks?.length) {
      parts.push(`<div class="small">🛠️ Tugas perbaikan: ${r.fixTasks.map(k => `<span class="mono">${escapeHtml(k)}</span>`).join(', ')}</div>`);
    }
    return `<div class="border-top pt-2 mt-2"><div class="fw-semibold small mb-1">Putaran ${r.round}</div>${parts.join('')}</div>`;
  }

  function renderVerification() {
    let report = null;
    try {
      report = detail.verificationJson ? JSON.parse(detail.verificationJson) : null;
    } catch {
      report = null;
    }

    const verifying = detail.summary.status === 'Verifying';
    el('verificationCard').classList.toggle('d-none', !report && !verifying);
    if (!report) {
      el('verificationOutcome').innerHTML = verifying ? statusBadge('Verifying') : '';
      el('verificationBody').innerHTML = verifying ? '<div class="text-secondary small">Build dan/atau review sedang berjalan…</div>' : '';
      return;
    }

    const [color, label] = verificationOutcomes[report.outcome] || ['secondary', report.outcome];
    el('verificationOutcome').innerHTML = `<span class="badge text-bg-${color}">${label}</span>`;
    const head = [
      report.command ? `Build: <span class="mono">${escapeHtml(report.command)}</span>` : 'Tanpa build',
      report.reviewerModel ? `Reviewer: <span class="mono">${escapeHtml(report.reviewerModel)}</span>` : 'Tanpa reviewer'
    ].join(' · ');
    el('verificationBody').innerHTML = `<div class="small text-secondary">${head}</div>${(report.rounds || []).map(roundHtml).join('')}`;
  }

  function renderFinal() {
    if (detail.finalResponse) el('finalResponse').innerHTML = md(detail.finalResponse);
    el('contextPackCard').classList.toggle('d-none', !detail.contextPack);
    if (detail.contextPack) el('contextPack').innerHTML = md(detail.contextPack);
  }

  // Delete, move and extract tasks are done by AiThread itself, without a model.
  const isFileOperation = t => t.action === 'delete' || t.action === 'move' || t.action === 'extract';
  const taskPath = t => t.targetPath ? `${t.filePath} → ${t.targetPath}` : t.filePath;

  function taskDetailHtml(t) {
    const output = outputs.get(t.id) || '';
    const outputHtml = isFileOperation(t) ? '' : winFrame({
      id: `output-${t.id}`,
      title: `Output model · ${t.taskKey}${t.attempts > 1 ? ` (${t.attempts} percobaan)` : ''}`,
      body: `<pre class="code-block" data-output-for="${t.id}">${escapeHtml(output || (t.status === 'Pending' ? 'Menunggu giliran…' : 'Output live hanya tersedia selama halaman terbuka saat tugas berjalan.'))}</pre>`
    });
    return `<tr class="task-detail" data-detail-for="${t.id}"><td colspan="7" class="bg-body-tertiary">
      <div class="mb-2"><span class="text-secondary small">Instruksi</span><div class="pre-wrap small">${escapeHtml(t.instruction)}</div></div>
      ${t.dependsOn ? `<div class="mb-2 small"><span class="text-secondary">Bergantung pada:</span> ${escapeHtml(t.dependsOn)}</div>` : ''}
      ${t.summary ? `<div class="mb-2 small"><span class="text-secondary">Ringkasan:</span> ${escapeHtml(t.summary)}</div>` : ''}
      ${t.error ? `<div class="mb-2 small text-danger">${escapeHtml(t.error)}</div>` : ''}
      ${t.diff ? winFrame({ id: `diff-${t.id}`, title: `Diff · ${taskPath(t)}`, className: 'mb-2', body: `<pre class="code-block">${diffHtml(t.diff)}</pre>` }) : ''}
      ${outputHtml}
    </td></tr>`;
  }

  function renderTasks() {
    const list = [...tasks.values()].sort((a, b) => a.order - b.order);
    const done = list.filter(t => t.status === 'Succeeded').length;
    el('taskCounter').textContent = list.length ? `${done}/${list.length} berhasil` : '';

    el('taskRows').innerHTML = list.length
      ? list.map(t => `
        <tr data-task="${t.id}" style="cursor:pointer">
          <td class="mono">${escapeHtml(t.taskKey)}</td>
          <td><div>${escapeHtml(t.title)}</div><div class="small text-secondary mono">${escapeHtml(t.action)} · ${escapeHtml(taskPath(t))}</div></td>
          <td class="small">${t.nodeName ? `${escapeHtml(t.nodeName)}<div class="text-secondary mono">${escapeHtml(t.model || '')}</div>` : isFileOperation(t) ? '<span class="text-secondary">tanpa model</span>' : '–'}</td>
          <td>${statusBadge(t.status)}${t.attempts > 1 ? `<div class="small text-secondary">${t.attempts}× percobaan</div>` : ''}</td>
          <td class="text-nowrap">${fmt.ms(t.durationMs)}</td>
          <td>${fmt.number(t.tokens)}</td>
          <td class="text-end small text-secondary">${expanded.has(t.id) ? '▲' : '▼'}</td>
        </tr>${expanded.has(t.id) ? taskDetailHtml(t) : ''}`).join('')
      : `<tr><td colspan="7" class="empty-state">${isActiveStatus(detail?.summary.status) ? 'Menunggu rencana…' : 'Tidak ada tugas eksekusi.'}</td></tr>`;

    renderTimeline();
  }

  // Bars share one time axis: overlapping bars are tasks that ran in parallel.
  function renderTimeline() {
    const list = [...tasks.values()].filter(t => t.startedAt).sort((a, b) => a.order - b.order);
    const container = el('timeline');
    if (!list.length) {
      container.innerHTML = '<div class="text-secondary small">Belum ada tugas yang berjalan.</div>';
      return;
    }

    const now = Date.now();
    const start = Math.min(...list.map(t => toDate(t.startedAt).getTime()));
    const end = Math.max(...list.map(t => t.completedAt ? toDate(t.completedAt).getTime() : now));
    const span = Math.max(end - start, 1);

    container.innerHTML = list.map(t => {
      const s = toDate(t.startedAt).getTime();
      const e = t.completedAt ? toDate(t.completedAt).getTime() : now;
      const left = ((s - start) / span) * 100;
      const width = Math.max(((e - s) / span) * 100, 0.8);
      return `<div class="timeline-row">
        <span class="mono">${escapeHtml(t.taskKey)}</span>
        <div class="timeline-track" title="${escapeHtml(taskPath(t))}"><div class="timeline-bar ${t.status}" style="left:${left}%;width:${width}%"></div></div>
        <span class="text-secondary text-end">${fmt.ms(t.durationMs ?? (e - s))}</span>
      </div>`;
    }).join('') + `<div class="small text-secondary mt-1">Rentang: ${fmt.ms(span)} — tugas yang overlap berjalan paralel.</div>`;

    const running = list.some(t => t.status === 'Running');
    if (running && !timelineTimer) timelineTimer = setInterval(renderTimeline, 1000);
    if (!running && timelineTimer) {
      clearInterval(timelineTimer);
      timelineTimer = null;
    }
  }

  function activityLine(time, category, message, level = 'Info') {
    return `<div class="log-line level-${level}"><span class="log-time">${fmt.time(time)}</span><span class="log-category">${escapeHtml(category)}</span><span class="log-message">${escapeHtml(message)}</span></div>`;
  }

  // Red window button: empty the view; new entries keep arriving.
  el('activityWindow').addEventListener('win:close', () => {
    el('activity').innerHTML = '<div class="empty-state">Aktivitas dikosongkan. Aktivitas baru akan muncul di sini.</div>';
  });

  function addActivity(html) {
    const panel = el('activity');
    panel.querySelector('.empty-state')?.remove();
    panel.insertAdjacentHTML('afterbegin', html);
  }

  async function loadDetail() {
    try {
      detail = await api.get(`/api/threads/${threadId}`);
    } catch (error) {
      el('threadMeta').textContent = error.message;
      return;
    }
    tasks.clear();
    detail.tasks.forEach(t => tasks.set(t.id, t));
    renderHeader();
    renderPlan();
    renderMetrics();
    renderVerification();
    renderFinal();
    renderTasks();
  }

  async function loadActivity() {
    try {
      const logs = await api.get(`/api/dashboard/logs?take=300&minLevel=Debug&threadId=${threadId}`);
      el('activity').innerHTML = logs.length
        ? logs.map(l => activityLine(l.createdAt, l.category, l.message, l.level)).join('')
        : '<div class="empty-state">Belum ada aktivitas.</div>';
    } catch {
      el('activity').innerHTML = '<div class="empty-state">Log tidak tersedia.</div>';
    }
  }

  const reloadDetail = debounce(loadDetail, 300);

  el('taskRows').addEventListener('click', event => {
    const row = event.target.closest('tr[data-task]');
    if (!row) return;
    const id = row.dataset.task;
    expanded.has(id) ? expanded.delete(id) : expanded.add(id);
    renderTasks();
  });

  el('btnCancel').addEventListener('click', async () => {
    if (!confirm('Batalkan thread ini? Tugas yang belum selesai tidak akan dijalankan.')) return;
    try {
      await api.post(`/api/threads/${threadId}/cancel`);
      toast('Pembatalan dikirim.', 'info');
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('btnRetry').addEventListener('click', async () => {
    if (!confirm('Jalankan lagi tugas yang gagal, dilewati, atau dibatalkan? Tugas yang sudah berhasil tidak diulang dan planner tidak dipanggil lagi.')) return;
    el('btnRetry').disabled = true;
    try {
      await api.post(`/api/threads/${threadId}/retry`);
      toast('Tugas yang gagal dijalankan ulang.', 'info');
      outputs.clear();
    } catch (error) {
      toast(error.message, 'error');
    } finally {
      el('btnRetry').disabled = false;
    }
  });

  const rollbackModal = bootstrap.Modal.getOrCreateInstance(el('rollbackModal'));
  const rollbackActions = {
    restore: ['success', 'Dikembalikan ke isi asli'],
    delete: ['success', 'Dihapus (dibuat oleh thread ini)'],
    unchanged: ['secondary', 'Sudah sama dengan aslinya — tidak disentuh'],
    conflict: ['danger', 'Sudah berubah lagi — menghalangi pembatalan']
  };

  el('btnRollback').addEventListener('click', async () => {
    el('rollbackReason').innerHTML = '';
    el('rollbackRows').innerHTML = '<tr><td colspan="2" class="text-secondary">Memeriksa file…</td></tr>';
    el('btnRollbackConfirm').disabled = true;
    rollbackModal.show();
    try {
      const preview = await api.get(`/api/threads/${threadId}/rollback`);
      el('rollbackRows').innerHTML = preview.files.length
        ? preview.files.map(f => {
          const [color, label] = rollbackActions[f.action] || ['secondary', f.action];
          return `<tr><td class="mono small">${escapeHtml(f.filePath)}</td>
            <td><span class="badge text-bg-${color}">${escapeHtml(label)}</span>${f.problem ? `<div class="small text-secondary">${escapeHtml(f.problem)}</div>` : ''}</td></tr>`;
        }).join('')
        : '<tr><td colspan="2" class="text-secondary">Tidak ada file yang tercatat.</td></tr>';
      el('rollbackReason').innerHTML = preview.reason ? `<div class="alert alert-warning small">${escapeHtml(preview.reason)}</div>` : '';
      el('btnRollbackConfirm').disabled = !preview.canRollback;
    } catch (error) {
      el('rollbackRows').innerHTML = '';
      el('rollbackReason').innerHTML = `<div class="alert alert-danger small">${escapeHtml(error.message)}</div>`;
    }
  });

  el('btnRollbackConfirm').addEventListener('click', async () => {
    el('btnRollbackConfirm').disabled = true;
    try {
      const result = await api.post(`/api/threads/${threadId}/rollback`);
      rollbackModal.hide();
      toast(`Perubahan dibatalkan: ${result.restored} file dikembalikan, ${result.deleted} file dihapus.`);
      loadDetail();
    } catch (error) {
      el('rollbackReason').innerHTML = `<div class="alert alert-danger small">${escapeHtml(error.message)}</div>`;
    }
  });

  loadDetail();
  loadActivity();

  stream(`threadId=${threadId}`, {
    'thread.updated': reloadDetail,
    'task.updated': payload => {
      tasks.set(payload.data.id, payload.data);
      renderTasks();
    },
    'task.output': payload => {
      const { taskId, text } = payload.data;
      outputs.set(taskId, (outputs.get(taskId) || '') + text);
      const pre = document.querySelector(`[data-output-for="${taskId}"]`);
      if (pre) {
        pre.textContent = outputs.get(taskId);
        pre.scrollTop = pre.scrollHeight;
      }
    },
    'thread.output': payload => {
      if (payload.data.kind === 'planner.tool') addActivity(activityLine(payload.timestamp, 'planner', `${payload.data.tool} ${payload.data.detail}`, 'Debug'));
    },
    'log': payload => {
      const l = payload.data;
      if (l.level !== 'Debug') addActivity(activityLine(l.createdAt, l.category, l.message, l.level));
    }
  });
})();
