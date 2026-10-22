(() => {
  'use strict';
  const { api, toast, escapeHtml, fmt, toDate, statusBadge, isActiveStatus, sourceLabel, stream, debounce, md, diffHtml, winFrame, pager } = window.AIThread;

  const threadId = window.threadId;
  const el = id => document.getElementById(id);
  const tasks = new Map();
  const outputs = new Map();
  const expanded = new Set();
  let detail = null;
  let timelineTimer = null;

  const contractPager = pager('contractPager', rows => {
    el('contractRows').innerHTML = rows.length
      ? rows.map(c => `<tr><td>${escapeHtml(c.name)}</td><td class="mono">${escapeHtml(c.value)}${c.note ? ` <span class="text-secondary">— ${escapeHtml(c.note)}</span>` : ''}</td></tr>`).join('')
      : '<tr><td colspan="2" class="text-secondary">–</td></tr>';
  });
  const taskPager = pager('taskPager', rows => {
    el('taskRows').innerHTML = rows.length
      ? rows.map(t => `
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
  });

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
    el('btnRetest').classList.toggle('d-none', s.status !== 'Completed' || !planTest()?.level?.match(/^(ui|api)$/)
      || !detail.tasks.some(t => t.status === 'Succeeded'));
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

  // The planner's test section (Auto Testing); null when the plan has none.
  function planTest() {
    try {
      return detail.planJson ? JSON.parse(detail.planJson).test || null : null;
    } catch {
      return null;
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
    contractPager.set(contract);

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
      ['Tester', detail.testerModel ? `<span class="mono">${escapeHtml(detail.testerModel)}</span>` : '–'],
      ['Wrapper', detail.wrapperModel ? `<span class="mono">${escapeHtml(detail.wrapperModel)}</span>` : '–'],
      ['Langkah planner', fmt.number(detail.plannerSteps)],
      ['Planning', fmt.ms(detail.planningMs)],
      ['Eksekusi (nyata)', fmt.ms(s.executionWallMs)],
      ['Eksekusi (jika sekuensial)', fmt.ms(s.executionSumMs)],
      ['Speedup paralel', fmt.speedup(s.executionSumMs, s.executionWallMs)],
      ['Verifikasi', fmt.ms(detail.verificationMs)],
      ['Unit test', fmt.ms(detail.unitTestMs)],
      ['Testing', fmt.ms(detail.testingMs)],
      ['Wrapping', fmt.ms(detail.wrappingMs)],
      ['Total', fmt.ms(s.totalMs)],
      ['Percobaan ulang', detail.retryCount ? `${fmt.number(detail.retryCount)}×` : '–'],
      ['Token planner', plannerTokens()],
      ['Token tools node', detail.toolsNodeTokens ? fmt.number(detail.toolsNodeTokens) : '–'],
      ['Token executor', fmt.number(detail.executorTokens)],
      ['Token reviewer', fmt.number(detail.reviewerTokens)],
      ['Token tester', detail.testerTokens ? fmt.number(detail.testerTokens) : '–'],
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
    if (r.unitTests) {
      const u = r.unitTests;
      const regressions = u.regressions?.length ? ` · <span class="text-danger">masih gagal: ${u.regressions.map(escapeHtml).join(', ')}</span>` : '';
      parts.push(`<div class="small">${u.regressions?.length ? '🔴' : '🧪'} Unit test: ${u.ran} dijalankan · ${u.passed} lolos, ${u.failed} gagal, ${u.skipped} dilewati${regressions}`
        + `${u.triage ? `<div class="text-secondary">Triase Tester${u.testerModel ? ` (${escapeHtml(u.testerModel)})` : ''}: ${escapeHtml(u.triage)}</div>` : ''}</div>`);
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

  const unitOutcomes = {
    passed: ['success', 'Lolos'],
    failed: ['danger', 'Ada yang gagal'],
    skipped: ['secondary', 'Tidak ada yang dijalankan']
  };
  const unitVerdicts = {
    ok: ['success', 'Lolos'],
    fixed: ['success', 'Sekarang lolos'],
    regression: ['danger', 'Regresi'],
    'new-failing': ['danger', 'Test baru gagal'],
    failing: ['danger', 'Gagal'],
    preexisting: ['warning', 'Sudah gagal sebelumnya'],
    skipped: ['secondary', 'Dilewati'],
    notfound: ['secondary', 'Tidak dilaporkan'],
    missing: ['warning', 'Tidak ditemukan'],
    flaky: ['secondary', 'Tidak stabil'],
    obsolete: ['warning', 'Obsolete']
  };
  const unitDecisions = { keep: 'tetap', update: 'diperbarui', obsolete: 'obsolete', new: 'baru' };
  const unitResults = { passed: '✅ lolos', failed: '❌ gagal', error: '❌ error', skipped: '⏭️ dilewati', notfound: '❔ tidak dilaporkan', unknown: '–' };
  const restoreLabels = {
    moved: 'pindah file', snapshot: 'dipulihkan dari snapshot', method: 'method dikembalikan dari snapshot',
    task: 'dikembalikan executor', regenerate: 'ditulis ulang dari skenario', missing: 'tidak bisa dipulihkan'
  };

  function renderUnitTests() {
    let report = null;
    try {
      report = detail.unitTestJson ? JSON.parse(detail.unitTestJson) : null;
    } catch {
      report = null;
    }
    el('unitTestCard').classList.toggle('d-none', !report);
    if (!report) return;

    const [color, label] = unitOutcomes[report.outcome] || ['secondary', report.outcome];
    el('unitTestOutcome').innerHTML = `<span class="badge text-bg-${color}">${label}</span>`;
    const head = [
      report.testOnly ? 'Hanya pengujian (kode tidak diubah)' : `Mode ${escapeHtml(report.mode || '–')}`,
      report.baselineRan || report.baselineCached ? `baseline: ${report.baselineRan} dijalankan, ${report.baselineCached} dari hasil sebelumnya${report.baselineMs ? ` (${fmt.ms(report.baselineMs)})` : ''}` : null,
      report.testerModel ? `triase: <span class="mono">${escapeHtml(report.testerModel)}</span>` : null
    ].filter(Boolean).join(' · ');
    const parts = [`<div class="small text-secondary">${head}</div>`];
    if (report.suites?.length) parts.push(`<div class="small text-secondary">Runner: ${report.suites.map(s => `<span class="chip mono">${escapeHtml(s)}</span>`).join('')}</div>`);
    (report.problems || []).forEach(p => parts.push(`<div class="small text-warning">⚠️ ${escapeHtml(p)}</div>`));
    if (report.summary) parts.push(`<div class="small mt-2">${escapeHtml(report.summary)}</div>`);

    if (report.tests?.length) {
      parts.push(`<div class="table-responsive mt-2"><table class="table table-sm align-middle mb-0 small">
        <thead><tr><th>No</th><th>Test</th><th>Keputusan</th><th>Sebelum</th><th>Sesudah</th><th>Hasil</th></tr></thead><tbody>
        ${report.tests.map(t => {
          const [vc, vl] = unitVerdicts[t.verdict] || ['secondary', t.verdict];
          const note = t.skipReason || (['ok', 'fixed'].includes(t.verdict) ? null : t.message);
          const triage = t.triage ? `<div class="text-secondary">Tester: ${escapeHtml(t.triage)}${t.triageReason ? ` — ${escapeHtml(t.triageReason)}` : ''}</div>` : '';
          return `<tr>
            <td class="mono text-nowrap">${escapeHtml(t.number)}</td>
            <td>${escapeHtml(t.title)}${t.file ? `<div class="mono text-secondary text-break">${escapeHtml(t.file)}</div>` : ''}${note ? `<div class="text-secondary text-break">${escapeHtml(note.split(/\r?\n/)[0])}</div>` : ''}${triage}</td>
            <td>${escapeHtml(unitDecisions[t.decision] || t.decision || '–')}</td>
            <td class="text-nowrap">${t.baseline ? `${unitResults[t.baseline] || escapeHtml(t.baseline)}${t.baselineCached ? ' <span class="text-secondary">(cache)</span>' : ''}` : '–'}</td>
            <td class="text-nowrap">${t.after ? unitResults[t.after] || escapeHtml(t.after) : '–'}</td>
            <td><span class="badge text-bg-${vc}">${vl}</span></td>
          </tr>`;
        }).join('')}
        </tbody></table></div>`);
    }
    if (report.restored?.length) {
      parts.push(`<div class="small mt-2 fw-semibold">Dipulihkan otomatis</div>` + list(report.restored.map(r =>
        `<li><span class="mono">${escapeHtml(r.number)}</span> <span class="mono text-secondary">${escapeHtml(r.file)}</span> — ${escapeHtml(restoreLabels[r.how] || r.how)}${r.note ? ` (${escapeHtml(r.note)})` : ''}</li>`)));
    }
    if (report.notes?.length) parts.push(list(report.notes.map(n => `<li class="text-secondary">ℹ️ ${escapeHtml(n)}</li>`)));
    el('unitTestBody').innerHTML = parts.join('');
  }

  const testingOutcomes = {
    passed: ['success', 'Lolos'],
    failed: ['danger', 'Gagal'],
    blocked: ['warning', 'Tidak bisa dicek'],
    skipped: ['secondary', 'Dilewati']
  };
  const scenarioIcons = { passed: '✅', failed: '❌', blocked: '🚫' };

  function pathOf(url) {
    try {
      const u = new URL(url);
      return u.pathname + u.search;
    } catch {
      return url;
    }
  }

  // One HTTP request of the tester: what was sent and what came back, as the developer would read it in a client.
  function requestHtml(r) {
    const head = r.error
      ? `<div class="small text-danger mb-1">Tanpa respons: ${escapeHtml(r.error)}</div>`
      : `<div class="small text-secondary mb-1">${escapeHtml(r.url)}${r.contentType ? ` · ${escapeHtml(r.contentType)}` : ''}${r.location ? ` · Location <span class="mono">${escapeHtml(r.location)}</span>` : ''}</div>`;
    const sent = r.requestBody ? `<div class="small text-secondary">Body request</div><pre class="code-block small">${escapeHtml(r.requestBody)}</pre>` : '';
    const received = r.responseBody ? `<div class="small text-secondary">Body respons</div><pre class="code-block small">${escapeHtml(r.responseBody)}</pre>` : '';
    return `<div class="p-2">${head}${sent}${received}</div>`;
  }

  function testingReport() {
    try {
      return detail?.testingJson ? JSON.parse(detail.testingJson) : null;
    } catch {
      return null;
    }
  }

  function renderTesting() {
    const report = testingReport();

    const testing = detail.summary.status === 'Testing';
    el('testingCard').classList.toggle('d-none', !report && !testing);
    if (!report || testing) {
      el('testingOutcome').innerHTML = testing ? statusBadge('Testing') : '';
      el('testingBody').innerHTML = testing ? '<div class="text-secondary small">Tester sedang mencoba perubahan di aplikasi yang berjalan… (browser tester tampil di layar komputer AIT)</div>' : '';
      return;
    }

    const [color, label] = testingOutcomes[report.outcome] || ['secondary', report.outcome];
    el('testingOutcome').innerHTML = `<span class="badge text-bg-${color}">${label}</span>`;
    const head = [
      report.level === 'ui' ? 'UI' : report.level === 'api' ? 'API' : 'Tidak bisa dites dari luar',
      report.baseUrl ? `<span class="mono">${escapeHtml(report.baseUrl)}</span>` : null,
      report.browser ? escapeHtml(report.browser) : null,
      report.testerModel ? `Tester: <span class="mono">${escapeHtml(report.testerModel)}</span>` : null,
      report.loginUser ? `login <span class="mono">${escapeHtml(report.loginUser)}</span>` : null,
      report.appStarted ? `dijalankan AIT <span class="mono">${escapeHtml(report.appStarted)}</span>` : null,
      report.steps ? `${fmt.number(report.steps)} langkah` : null
    ].filter(Boolean).join(' · ');
    const parts = [`<div class="small text-secondary">${head}</div>`];
    if (report.error) parts.push(`<div class="alert alert-warning small mt-2 mb-2">${escapeHtml(report.error)}</div>`);
    if (report.loginNeeded) {
      parts.push(`<div class="alert alert-warning small mt-2 mb-2">🔐 Tester butuh login: ${escapeHtml(report.loginNeeded)}${report.loginUrl ? ` (<span class="mono">${escapeHtml(report.loginUrl)}</span>)` : ''}.
        ${report.askedLogin ? 'Balas di chat dengan <span class="mono">login &lt;username&gt; &lt;password&gt;</span>, atau klik' : 'Klik'} <strong>🧪 Ulangi testing</strong> dan isi username &amp; password.</div>`);
    }
    if (report.summary) parts.push(`<div class="small mt-2">${escapeHtml(report.summary)}</div>`);
    if (report.scenarios?.length) {
      parts.push(`<div class="mt-2">${list(report.scenarios.map(s =>
        `<li>${scenarioIcons[s.status] || '•'} <strong>${escapeHtml(s.title)}</strong>${s.details && s.details !== report.error ? ` — ${escapeHtml(s.details)}` : ''}</li>`))}</div>`);
    }
    if (report.browserErrors?.length) {
      parts.push(winFrame({ id: 'testing-browser-errors', title: `Error di browser (${report.browserErrors.length})`, className: 'mb-2',
        body: `<pre class="code-block small">${escapeHtml(report.browserErrors.join('\n'))}</pre>` }));
    }
    (report.requests || []).forEach((r, i) => parts.push(winFrame({
      id: `testing-request-${i}`,
      title: `${r.method} ${pathOf(r.url)} → ${r.error ? 'tanpa respons' : `${r.status} ${r.reason || ''}`.trim()} · ${fmt.ms(r.durationMs)}`,
      className: 'mb-2',
      minimized: i >= 3,
      body: requestHtml(r)
    })));
    el('testingBody').innerHTML = parts.join('');
  }

  function renderFinal() {
    if (detail.finalResponse) el('finalResponse').innerHTML = md(detail.finalResponse);
    el('contextPackCard').classList.toggle('d-none', !detail.contextPack);
    if (detail.contextPack) el('contextPack').innerHTML = md(detail.contextPack);
  }

  // Delete, move and extract tasks are done by AIThread itself, without a model.
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

    taskPager.set(list);

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
    renderUnitTests();
    renderTesting();
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

  // The login typed here goes with this one request and is never stored.
  const retestModal = bootstrap.Modal.getOrCreateInstance(el('retestModal'));
  el('btnRetest').addEventListener('click', () => {
    const report = testingReport();
    el('retestLoginNeeded').innerHTML = report?.loginNeeded
      ? `<div class="alert alert-warning small">🔐 Testing sebelumnya butuh login: ${escapeHtml(report.loginNeeded)}${report.loginUrl ? ` (<span class="mono">${escapeHtml(report.loginUrl)}</span>)` : ''}</div>`
      : '';
    el('retestUsername').value = report?.loginUser || '';
    el('retestPassword').value = '';
    retestModal.show();
  });
  el('retestModal').addEventListener('hidden.bs.modal', () => {
    el('retestPassword').value = '';
  });

  el('retestForm').addEventListener('submit', async event => {
    event.preventDefault();
    const username = el('retestUsername').value.trim();
    const password = el('retestPassword').value;
    if (!username !== !password) return toast('Isi username dan password, atau kosongkan keduanya.', 'info');
    el('btnRetestConfirm').disabled = true;
    try {
      await api.post(`/api/threads/${threadId}/test`, { username: username || null, password: password || null });
      retestModal.hide();
      toast('Testing dijalankan ulang.', 'info');
    } catch (error) {
      toast(error.message, 'error');
    } finally {
      el('btnRetestConfirm').disabled = false;
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
