(() => {
  'use strict';
  const { isEn, t, api, toast, escapeHtml, fmt, toDate, statusBadge, isActiveStatus, sourceLabel, stream, debounce, md, diffHtml, winFrame, pager } = window.AIThread;

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
          <td class="small">${t.nodeName ? `${escapeHtml(t.nodeName)}<div class="text-secondary mono">${escapeHtml(t.model || '')}</div>` : isFileOperation(t) ? (isEn ? '<span class="text-secondary">no model</span>' : '<span class="text-secondary">tanpa model</span>') : '–'}</td>
          <td>${statusBadge(t.status)}${t.attempts > 1 ? (isEn ? `<div class="small text-secondary">${t.attempts}× attempts</div>` : `<div class="small text-secondary">${t.attempts}× percobaan</div>`) : ''}</td>
          <td class="text-nowrap">${fmt.ms(t.durationMs)}</td>
          <td>${fmt.number(t.tokens)}</td>
          <td class="text-end small text-secondary">${expanded.has(t.id) ? '▲' : '▼'}</td>
        </tr>${expanded.has(t.id) ? taskDetailHtml(t) : ''}`).join('')
      : `<tr><td colspan="7" class="empty-state">${isActiveStatus(detail?.summary.status) ? t('Menunggu rencana…', 'Waiting for the plan…') : t('Tidak ada tugas eksekusi.', 'No execution tasks.')}</td></tr>`;
  });

  function renderHeader() {
    const s = detail.summary;
    el('threadShortId').textContent = s.id.substring(0, 8);
    el('threadStatus').innerHTML = statusBadge(s.status);
    el('threadMeta').innerHTML = [
      s.projectName ? `<a href="/Project/Detail/${s.projectId}">${escapeHtml(s.projectName)}</a>` : t('tanpa project', 'no project'),
      sourceLabel(s.source),
      s.sessionId ? (isEn ? `session <span class="mono">${escapeHtml(s.sessionId.substring(0, 8))}</span>` : `sesi <span class="mono">${escapeHtml(s.sessionId.substring(0, 8))}</span>`) : null,
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
      el('threadMeta').insertAdjacentHTML('beforeend', (isEn ? ` · <span class="text-warning">file changes rolled back ${fmt.dateTime(detail.rolledBackAt)}</span>` : ` · <span class="text-warning">perubahan file dibatalkan ${fmt.dateTime(detail.rolledBackAt)}</span>`));
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
      (isActiveStatus(detail.summary.status) ? t('Planner sedang menganalisis…', 'The planner is analysing…') : t('Tidak ada rencana.', 'No plan.'));
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
      [t('Langkah planner', 'Planner steps'), fmt.number(detail.plannerSteps)],
      ['Planning', fmt.ms(detail.planningMs)],
      [t('Eksekusi (nyata)', 'Execution (actual)'), fmt.ms(s.executionWallMs)],
      [t('Eksekusi (jika sekuensial)', 'Execution (if sequential)'), fmt.ms(s.executionSumMs)],
      [(isEn ? 'Parallel speedup' : 'Speedup paralel'), fmt.speedup(s.executionSumMs, s.executionWallMs)],
      [t('Verifikasi', 'Verification'), fmt.ms(detail.verificationMs)],
      [(isEn ? 'Unit tests' : 'Unit test'), fmt.ms(detail.unitTestMs)],
      ['Testing', fmt.ms(detail.testingMs)],
      ['Wrapping', fmt.ms(detail.wrappingMs)],
      ['Total', fmt.ms(s.totalMs)],
      [t('Percobaan ulang', 'Retries'), detail.retryCount ? `${fmt.number(detail.retryCount)}×` : '–'],
      [(isEn ? 'Planner tokens' : 'Token planner'), plannerTokens()],
      [(isEn ? 'Tools node tokens' : 'Token tools node'), detail.toolsNodeTokens ? fmt.number(detail.toolsNodeTokens) : '–'],
      [(isEn ? 'Executor tokens' : 'Token executor'), fmt.number(detail.executorTokens)],
      [(isEn ? 'Reviewer tokens' : 'Token reviewer'), fmt.number(detail.reviewerTokens)],
      [(isEn ? 'Tester tokens' : 'Token tester'), detail.testerTokens ? fmt.number(detail.testerTokens) : '–'],
      [(isEn ? 'Wrapper tokens' : 'Token wrapper'), fmt.number(detail.wrapperTokens)]
    ];
    el('metrics').innerHTML = rows.map(([k, v]) => `<dt class="col-6 fw-normal text-secondary">${k}</dt><dd class="col-6 text-end mb-1">${v}</dd>`).join('');

    if (detail.error) {
      el('metrics').insertAdjacentHTML('beforeend', `<dt class="col-12 mt-2 text-danger fw-normal">${escapeHtml(detail.error)}</dt>`);
    }
  }

  const verificationOutcomes = {
    passed: ['success', (isEn ? 'Passed' : 'Lolos')],
    failed: ['warning', t('Masih ada masalah', 'Issues remain')],
    unverified: ['secondary', t('Tidak dapat dipastikan', 'Could not be determined')]
  };
  const buildIcons = { passed: '✅', locked: '✅', failed: '❌', timeout: '⏱️', error: '⚠️' };

  function list(items) {
    return `<ul class="small mb-1 ps-3">${items.join('')}</ul>`;
  }

  function roundHtml(r) {
    const parts = [];
    if (r.build) {
      const b = r.build;
      const more = b.errorCount > b.errors.length ? (isEn ? `\n… ${b.errorCount - b.errors.length} more error(s)` : `\n… ${b.errorCount - b.errors.length} error lain`) : '';
      parts.push(`<div class="small mb-1">${buildIcons[b.status] || ''} ${escapeHtml(b.headline)}</div>`
        + (b.errors.length ? winFrame({ id: `build-errors-${r.round}`, title: (isEn ? `Build errors · round ${r.round}` : `Error build · putaran ${r.round}`), className: 'mb-2',
          body: `<pre class="code-block small">${escapeHtml(b.errors.join('\n') + more)}</pre>` }) : '')
        + (b.output ? winFrame({ id: `build-output-${r.round}`, title: (isEn ? `Build output · round ${r.round}` : `Output build · putaran ${r.round}`), className: 'mb-2',
          body: `<pre class="code-block small">${escapeHtml(b.output)}</pre>` }) : ''));
    }
    if (r.review) {
      const v = r.review;
      if (v.error) parts.push((isEn ? `<div class="small">⚠️ The reviewer gave no result: ${escapeHtml(v.error)}</div>` : `<div class="small">⚠️ Reviewer tidak memberi hasil: ${escapeHtml(v.error)}</div>`));
      else if (!v.issues.length) parts.push(`<div class="small">✅ Reviewer: ${escapeHtml(v.summary || t('tidak ada masalah.', 'no issues.'))}</div>`);
      else {
        parts.push((isEn ? `<div class="small">⚠️ The reviewer found ${v.issues.length} issue(s)${v.summary ? `: ${escapeHtml(v.summary)}` : ''}</div>` : `<div class="small">⚠️ Reviewer menemukan ${v.issues.length} masalah${v.summary ? `: ${escapeHtml(v.summary)}` : ''}</div>`)
          + list(v.issues.map(i => `<li><span class="mono">${escapeHtml(i.file)}</span> — ${escapeHtml(i.problem)}<div class="text-secondary">${escapeHtml(i.fix)}</div></li>`)));
      }
    }
    if (r.unitTests) {
      const u = r.unitTests;
      const regressions = u.regressions?.length ? (isEn ? ` · <span class="text-danger">still failing: ${u.regressions.map(escapeHtml).join(', ')}</span>` : ` · <span class="text-danger">masih gagal: ${u.regressions.map(escapeHtml).join(', ')}</span>`) : '';
      parts.push((isEn ? `<div class="small">${u.regressions?.length ? '🔴' : '🧪'} Unit tests: ${u.ran} run · ${u.passed} passed, ${u.failed} failed, ${u.skipped} skipped${regressions}` : `<div class="small">${u.regressions?.length ? '🔴' : '🧪'} Unit test: ${u.ran} dijalankan · ${u.passed} lolos, ${u.failed} gagal, ${u.skipped} dilewati${regressions}`)
        + `${u.triage ? `<div class="text-secondary">Triase Tester${u.testerModel ? ` (${escapeHtml(u.testerModel)})` : ''}: ${escapeHtml(u.triage)}</div>` : ''}</div>`);
    }
    if (r.unfixable?.length) {
      parts.push(t('<div class="small text-secondary">Tidak bisa diperbaiki otomatis:</div>', '<div class="small text-secondary">Cannot be fixed automatically:</div>') + list(r.unfixable.map(u => `<li>${escapeHtml(u)}</li>`)));
    }
    if (r.fixTasks?.length) {
      parts.push((isEn ? `<div class="small">🛠️ Fix tasks: ${r.fixTasks.map(k => `<span class="mono">${escapeHtml(k)}</span>`).join(', ')}</div>` : `<div class="small">🛠️ Tugas perbaikan: ${r.fixTasks.map(k => `<span class="mono">${escapeHtml(k)}</span>`).join(', ')}</div>`));
    }
    return (isEn ? `<div class="border-top pt-2 mt-2"><div class="fw-semibold small mb-1">Round ${r.round}</div>${parts.join('')}</div>` : `<div class="border-top pt-2 mt-2"><div class="fw-semibold small mb-1">Putaran ${r.round}</div>${parts.join('')}</div>`);
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
      el('verificationBody').innerHTML = verifying ? t('<div class="text-secondary small">Build dan/atau review sedang berjalan…</div>', '<div class="text-secondary small">Build and/or review in progress…</div>') : '';
      return;
    }

    const [color, label] = verificationOutcomes[report.outcome] || ['secondary', report.outcome];
    el('verificationOutcome').innerHTML = `<span class="badge text-bg-${color}">${label}</span>`;
    const head = [
      report.command ? `Build: <span class="mono">${escapeHtml(report.command)}</span>` : t('Tanpa build', 'No build'),
      report.reviewerModel ? `Reviewer: <span class="mono">${escapeHtml(report.reviewerModel)}</span>` : t('Tanpa reviewer', 'No reviewer')
    ].join(' · ');
    el('verificationBody').innerHTML = `<div class="small text-secondary">${head}</div>${(report.rounds || []).map(roundHtml).join('')}`;
  }

  const gitStates = {
    Uncommitted: ['secondary', t('Belum di-commit', 'Not committed')],
    Staged: ['info', t('Sudah di-stage', 'Staged')],
    Committed: ['success', t('Sudah di-commit', 'Committed')],
    ModifiedOutside: ['warning', t('Diubah lagi di luar AIT', 'Changed again outside AIT')],
    Discarded: ['danger', t('Dibuang di luar AIT', 'Discarded outside AIT')],
    OnOtherBranch: ['primary', t('Di branch lain', 'On another branch')],
    Pushed: ['success', t('Sudah di-push', 'Pushed')],
    Stashed: ['secondary', t('Di-stash', 'Stashed')],
    Lost: ['danger', t('Commit hilang', 'Commit lost')]
  };

  function gitBadge(state) {
    const [color, label] = gitStates[state] || ['secondary', state];
    return `<span class="badge text-bg-${color}">${escapeHtml(label)}</span>`;
  }

  const shortCommit = sha => `<span class="mono" title="${escapeHtml(sha)}">${escapeHtml(sha.substring(0, 8))}</span>`;

  // Where the thread started (branch, commit, author) and where its change is now, as GitChangeTracker last saw it.
  function renderGit() {
    const git = detail.git;
    el('gitCard').classList.toggle('d-none', !git);
    if (!git) return;
    el('gitOutcome').innerHTML = git.state ? gitBadge(git.state) : '';
    const origin = [
      git.branch ? `${t('Branch', 'Branch')}: <span class="mono">${escapeHtml(git.branch)}</span>` : null,
      git.baseCommit ? `${t('dari commit', 'from commit')} ${shortCommit(git.baseCommit)}` : null,
      git.author ? `${t('oleh', 'by')} ${escapeHtml(git.author)}` : null
    ].filter(Boolean).join(' · ');
    const commits = git.commits.length
      ? `<div class="small mt-1">${t('Commit', 'Commits')}: ${git.commits.map(shortCommit).join(', ')}</div>` : '';
    const checked = git.checkedAt
      ? `<div class="small text-secondary mt-1">${t('Terakhir dicek', 'Last checked')} ${fmt.dateTime(git.checkedAt)}</div>`
      : `<div class="small text-secondary mt-1">${t('Status git belum dicek.', 'Git state not checked yet.')}</div>`;
    const files = git.files.length
      ? `<table class="table table-sm mt-2 mb-0"><tbody>${git.files.map(f => `<tr><td class="mono small">${escapeHtml(f.filePath)}</td>
          <td>${f.state ? gitBadge(f.state) : '<span class="text-secondary small">–</span>'}${f.commit ? ` ${shortCommit(f.commit)}` : ''}${f.edited ? ` <span class="small text-secondary">${t('(diedit developer sebelum commit)', '(edited by the developer before the commit)')}</span>` : ''}</td></tr>`).join('')}</tbody></table>`
      : '';
    el('gitBody').innerHTML = `<div class="small">${origin || t('Asal git tidak diketahui.', 'Git origin unknown.')}</div>${commits}${checked}${files}`;
  }

  el('btnGitRefresh').addEventListener('click', async () => {
    el('btnGitRefresh').disabled = true;
    try {
      detail.git = await api.post(`/api/threads/${threadId}/git/refresh`);
      renderGit();
    } catch (error) {
      toast(error.message, 'error');
    } finally {
      el('btnGitRefresh').disabled = false;
    }
  });

  const unitOutcomes = {
    passed: ['success', (isEn ? 'Passed' : 'Lolos')],
    failed: ['danger', t('Ada yang gagal', 'Something failed')],
    skipped: ['secondary', t('Tidak ada yang dijalankan', 'Nothing was run')]
  };
  const unitVerdicts = {
    ok: ['success', (isEn ? 'Passed' : 'Lolos')],
    fixed: ['success', t('Sekarang lolos', 'Passes now')],
    regression: ['danger', (isEn ? 'Regression' : 'Regresi')],
    'new-failing': ['danger', t('Test baru gagal', 'New test fails')],
    failing: ['danger', t('Gagal', 'Failed')],
    preexisting: ['warning', t('Sudah gagal sebelumnya', 'Already failing before')],
    skipped: ['secondary', t('Dilewati', 'Skipped')],
    notfound: ['secondary', t('Tidak dilaporkan', 'Not reported')],
    missing: ['warning', t('Tidak ditemukan', 'Not found')],
    flaky: ['secondary', t('Tidak stabil', 'Flaky')],
    obsolete: ['warning', 'Obsolete']
  };
  const unitDecisions = { keep: t('tetap', 'kept'), update: t('diperbarui', 'updated'), obsolete: 'obsolete', new: t('baru', 'new') };
  const unitResults = { passed: '✅ lolos', failed: t('❌ gagal', '❌ failed'), error: '❌ error', skipped: t('⏭️ dilewati', '⏭️ skipped'), notfound: t('❔ tidak dilaporkan', '❔ not reported'), unknown: '–' };
  const restoreLabels = {
    moved: (isEn ? 'file moved' : 'pindah file'), snapshot: t('dipulihkan dari snapshot', 'restored from snapshot'), method: t('method dikembalikan dari snapshot', 'method put back from snapshot'),
    task: t('dikembalikan executor', 'restored by an executor'), regenerate: t('ditulis ulang dari skenario', 'rewritten from the scenario'), missing: t('tidak bisa dipulihkan', 'could not be restored')
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
      report.testOnly ? t('Hanya pengujian (kode tidak diubah)', 'Testing only (code unchanged)') : `Mode ${escapeHtml(report.mode || '–')}`,
      report.baselineRan || report.baselineCached ? (isEn ? `baseline: ${report.baselineRan} run, ${report.baselineCached} from earlier results${report.baselineMs ? ` (${fmt.ms(report.baselineMs)})` : ''}` : `baseline: ${report.baselineRan} dijalankan, ${report.baselineCached} dari hasil sebelumnya${report.baselineMs ? ` (${fmt.ms(report.baselineMs)})` : ''}`) : null,
      report.testerModel ? `triase: <span class="mono">${escapeHtml(report.testerModel)}</span>` : null
    ].filter(Boolean).join(' · ');
    const parts = [`<div class="small text-secondary">${head}</div>`];
    if (report.suites?.length) parts.push(`<div class="small text-secondary">Runner: ${report.suites.map(s => `<span class="chip mono">${escapeHtml(s)}</span>`).join('')}</div>`);
    (report.problems || []).forEach(p => parts.push(`<div class="small text-warning">⚠️ ${escapeHtml(p)}</div>`));
    if (report.summary) parts.push(`<div class="small mt-2">${escapeHtml(report.summary)}</div>`);

    if (report.tests?.length) {
      parts.push((isEn ? `<div class="table-responsive mt-2"><table class="table table-sm align-middle mb-0 small">
        <thead><tr><th>No</th><th>Test</th><th>Decision</th><th>Before</th><th>After</th><th>Result</th></tr></thead><tbody>
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
        </tbody></table></div>` : `<div class="table-responsive mt-2"><table class="table table-sm align-middle mb-0 small">
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
        </tbody></table></div>`));
    }
    if (report.restored?.length) {
      parts.push(t(`<div class="small mt-2 fw-semibold">Dipulihkan otomatis</div>`, `<div class="small mt-2 fw-semibold">Restored automatically</div>`) + list(report.restored.map(r =>
        `<li><span class="mono">${escapeHtml(r.number)}</span> <span class="mono text-secondary">${escapeHtml(r.file)}</span> — ${escapeHtml(restoreLabels[r.how] || r.how)}${r.note ? ` (${escapeHtml(r.note)})` : ''}</li>`)));
    }
    if (report.notes?.length) parts.push(list(report.notes.map(n => `<li class="text-secondary">ℹ️ ${escapeHtml(n)}</li>`)));
    el('unitTestBody').innerHTML = parts.join('');
  }

  const testingOutcomes = {
    passed: ['success', (isEn ? 'Passed' : 'Lolos')],
    failed: ['danger', t('Gagal', 'Failed')],
    blocked: ['warning', t('Tidak bisa dicek', 'Could not check')],
    skipped: ['secondary', t('Dilewati', 'Skipped')]
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
      ? (isEn ? `<div class="small text-danger mb-1">No response: ${escapeHtml(r.error)}</div>` : `<div class="small text-danger mb-1">Tanpa respons: ${escapeHtml(r.error)}</div>`)
      : `<div class="small text-secondary mb-1">${escapeHtml(r.url)}${r.contentType ? ` · ${escapeHtml(r.contentType)}` : ''}${r.location ? ` · Location <span class="mono">${escapeHtml(r.location)}</span>` : ''}</div>`;
    const sent = r.requestBody ? (isEn ? `<div class="small text-secondary">Request body</div><pre class="code-block small">${escapeHtml(r.requestBody)}</pre>` : `<div class="small text-secondary">Body request</div><pre class="code-block small">${escapeHtml(r.requestBody)}</pre>`) : '';
    const received = r.responseBody ? (isEn ? `<div class="small text-secondary">Response body</div><pre class="code-block small">${escapeHtml(r.responseBody)}</pre>` : `<div class="small text-secondary">Body respons</div><pre class="code-block small">${escapeHtml(r.responseBody)}</pre>`) : '';
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
      el('testingBody').innerHTML = testing ? t('<div class="text-secondary small">Tester sedang mencoba perubahan di aplikasi yang berjalan… (browser tester tampil di layar komputer AIT)</div>', '<div class="text-secondary small">The Tester is trying the change in the running application… (the tester browser shows on the screen of the AIT computer)</div>') : '';
      return;
    }

    const [color, label] = testingOutcomes[report.outcome] || ['secondary', report.outcome];
    el('testingOutcome').innerHTML = `<span class="badge text-bg-${color}">${label}</span>`;
    const head = [
      report.level === 'ui' ? 'UI' : report.level === 'api' ? 'API' : t('Tidak bisa dites dari luar', 'Cannot be tested from outside'),
      report.baseUrl ? `<span class="mono">${escapeHtml(report.baseUrl)}</span>` : null,
      report.browser ? escapeHtml(report.browser) : null,
      report.testerModel ? `Tester: <span class="mono">${escapeHtml(report.testerModel)}</span>` : null,
      report.loginUser ? `login <span class="mono">${escapeHtml(report.loginUser)}</span>` : null,
      report.accounts?.length ? (isEn ? `accounts ${report.accounts.map(a => `<span class="mono">${escapeHtml(a)}</span>`).join(', ')}` : `akun ${report.accounts.map(a => `<span class="mono">${escapeHtml(a)}</span>`).join(', ')}`) : null,
      report.appStarted ? (isEn ? `started by AIT <span class="mono">${escapeHtml(report.appStarted)}</span>` : `dijalankan AIT <span class="mono">${escapeHtml(report.appStarted)}</span>`) : null,
      report.steps ? (isEn ? `${fmt.number(report.steps)} steps` : `${fmt.number(report.steps)} langkah`) : null
    ].filter(Boolean).join(' · ');
    const parts = [`<div class="small text-secondary">${head}</div>`];
    if (report.error) parts.push(`<div class="alert alert-warning small mt-2 mb-2">${escapeHtml(report.error)}</div>`);
    if (report.defaultDevPassword) {
      parts.push((isEn ? `<div class="small text-secondary mt-2">ℹ️ Test accounts log in with the default dev password. If the application uses another password in dev, set it in
        <a href="/Project/Detail/${detail.summary.projectId}#testAccountCard">Project › Test accounts</a>.</div>` : `<div class="small text-secondary mt-2">ℹ️ Login akun uji memakai password dev bawaan. Bila aplikasi memakai password lain di dev, isi di
        <a href="/Project/Detail/${detail.summary.projectId}#testAccountCard">Project › Akun uji</a>.</div>`));
    }
    if (report.appOutput) {
      parts.push(winFrame({ id: 'testing-app-output', title: (isEn ? 'Application output' : 'Output aplikasi'), className: 'mb-2',
        body: `<pre class="code-block small">${escapeHtml(report.appOutput)}</pre>` }));
    }
    if (report.loginNeeded) {
      parts.push((isEn ? `<div class="alert alert-warning small mt-2 mb-2">🔐 The Tester needs to log in: ${escapeHtml(report.loginNeeded)}${report.loginUrl ? ` (<span class="mono">${escapeHtml(report.loginUrl)}</span>)` : ''}.
        ${report.askedLogin ? 'Reply in the chat with <span class="mono">login &lt;username&gt; &lt;password&gt;</span>, or click' : 'Click'} <strong>🧪 Test again</strong> and fill in the username &amp; password.</div>` : `<div class="alert alert-warning small mt-2 mb-2">🔐 Tester butuh login: ${escapeHtml(report.loginNeeded)}${report.loginUrl ? ` (<span class="mono">${escapeHtml(report.loginUrl)}</span>)` : ''}.
        ${report.askedLogin ? 'Balas di chat dengan <span class="mono">login &lt;username&gt; &lt;password&gt;</span>, atau klik' : 'Klik'} <strong>🧪 Ulangi testing</strong> dan isi username &amp; password.</div>`));
    }
    if (report.summary) parts.push(`<div class="small mt-2">${escapeHtml(report.summary)}</div>`);
    if (report.scenarios?.length) {
      parts.push(`<div class="mt-2">${list(report.scenarios.map(s =>
        `<li>${scenarioIcons[s.status] || '•'} <strong>${escapeHtml(s.title)}</strong>${s.actor ? ` <span class="mono small text-secondary">(${escapeHtml(s.actor)})</span>` : ''}${s.details && s.details !== report.error ? ` — ${escapeHtml(s.details)}` : ''}</li>`))}</div>`);
    }
    if (report.browserErrors?.length) {
      parts.push(winFrame({ id: 'testing-browser-errors', title: (isEn ? `Browser errors (${report.browserErrors.length})` : `Error di browser (${report.browserErrors.length})`), className: 'mb-2',
        body: `<pre class="code-block small">${escapeHtml(report.browserErrors.join('\n'))}</pre>` }));
    }
    (report.requests || []).forEach((r, i) => parts.push(winFrame({
      id: `testing-request-${i}`,
      title: `${r.method} ${pathOf(r.url)} → ${r.error ? t('tanpa respons', 'no response') : `${r.status} ${r.reason || ''}`.trim()} · ${fmt.ms(r.durationMs)}`,
      className: 'mb-2',
      minimized: i >= 3,
      body: requestHtml(r)
    })));
    if (report.values && Object.keys(report.values).length) {
      parts.push((isEn ? `<div class="small mt-2"><span class="text-secondary">Values the Tester remembered:</span> ${Object.entries(report.values)
        .map(([k, v]) => `<span class="badge text-bg-light border mono">${escapeHtml(k)} = ${escapeHtml(v)}</span>`).join(' ')}</div>` : `<div class="small mt-2"><span class="text-secondary">Nilai yang diingat Tester:</span> ${Object.entries(report.values)
        .map(([k, v]) => `<span class="badge text-bg-light border mono">${escapeHtml(k)} = ${escapeHtml(v)}</span>`).join(' ')}</div>`));
    }
    if (report.followUps?.length) {
      parts.push(winFrame({ id: 'testing-follow-ups', title: (isEn ? `Suggested follow-up tests (${report.followUps.length})` : `Test lanjutan yang disarankan (${report.followUps.length})`), className: 'mt-2 mb-2', body: followUpsHtml(report) }));
    }
    el('testingBody').innerHTML = parts.join('');
  }

  // The tester's suggestions with the result of their latest run and a button to run one (creates real data like any test).
  function followUpsHtml(report) {
    const busy = isActiveStatus(detail.summary.status);
    return `<ol class="small mb-0 ps-3">${report.followUps.map(f => {
      const actors = [...new Set((f.scenarios || []).map(s => s.actor).filter(Boolean))];
      const [lastColor, lastLabel] = f.lastRun ? testingOutcomes[f.lastRun.outcome] || ['secondary', f.lastRun.outcome] : [];
      const last = f.lastRun
        ? `<div class="mt-1"><span class="badge text-bg-${lastColor}">${escapeHtml(lastLabel)}</span>
            ${f.lastRunAt ? `<span class="text-secondary">${fmt.relative(f.lastRunAt)}</span>` : ''}
            ${list((f.lastRun.scenarios || []).map(s => `<li>${scenarioIcons[s.status] || '•'} ${escapeHtml(s.title)}${s.actor ? ` <span class="mono text-secondary">(${escapeHtml(s.actor)})</span>` : ''}${s.details ? ` — ${escapeHtml(s.details)}` : ''}</li>`))}</div>`
        : '';
      return (isEn ? `<li class="mb-2">
        <div class="d-flex flex-wrap gap-2 align-items-start">
          <div class="me-auto"><strong>${escapeHtml(f.title)}</strong>${f.why ? ` — ${escapeHtml(f.why)}` : ''}
            ${actors.length ? `<div class="text-secondary">Actors: ${actors.map(a => `<span class="mono">${escapeHtml(a)}</span>`).join(', ')}</div>` : ''}
            ${f.needs ? `<div class="text-warning">Needs: ${escapeHtml(f.needs)}</div>` : ''}</div>
          <div class="d-flex gap-1">
            <button type="button" class="btn btn-sm btn-outline-secondary" data-follow-up-sheet="${f.number}" title="Create a multi-step scenario and download an Excel file to fill in (accounts per role, form values)">${f.scenarioId ? 'Scenario Excel' : 'Create Excel scenario'}</button>
            <button type="button" class="btn btn-sm btn-outline-primary" data-follow-up="${f.number}" ${busy ? 'disabled' : ''}>${f.lastRun ? 'Run again' : 'Run'}</button>
          </div>
        </div>${last}</li>` : `<li class="mb-2">
        <div class="d-flex flex-wrap gap-2 align-items-start">
          <div class="me-auto"><strong>${escapeHtml(f.title)}</strong>${f.why ? ` — ${escapeHtml(f.why)}` : ''}
            ${actors.length ? `<div class="text-secondary">Aktor: ${actors.map(a => `<span class="mono">${escapeHtml(a)}</span>`).join(', ')}</div>` : ''}
            ${f.needs ? `<div class="text-warning">Perlu: ${escapeHtml(f.needs)}</div>` : ''}</div>
          <div class="d-flex gap-1">
            <button type="button" class="btn btn-sm btn-outline-secondary" data-follow-up-sheet="${f.number}" title="Buat skenario multi-langkah dan unduh file Excel untuk diisi (akun per peran, nilai form)">${f.scenarioId ? 'Excel skenario' : 'Buat skenario Excel'}</button>
            <button type="button" class="btn btn-sm btn-outline-primary" data-follow-up="${f.number}" ${busy ? 'disabled' : ''}>${f.lastRun ? 'Jalankan lagi' : 'Jalankan'}</button>
          </div>
        </div>${last}</li>`);
    }).join('')}</ol>`;
  }

  el('testingBody').addEventListener('click', async event => {
    const sheet = event.target.closest('[data-follow-up-sheet]');
    if (sheet) {
      sheet.disabled = true;
      try {
        const scenario = await api.post(`/api/threads/${threadId}/follow-ups/${sheet.dataset.followUpSheet}/scenario`);
        toast((isEn ? `Scenario "${scenario.title}" created. Fill in its Excel file, then upload it in Project › Test scenarios.` : `Skenario "${scenario.title}" dibuat. Isi file Excel-nya, lalu unggah di Project › Skenario test.`), 'info');
        window.location.href = `/api/projects/${detail.summary.projectId}/test-scenarios/${scenario.id}/excel`;
      } catch (error) {
        toast(error.message, 'error');
      } finally {
        sheet.disabled = false;
      }
      return;
    }
    const button = event.target.closest('[data-follow-up]');
    if (!button) return;
    const number = button.dataset.followUp;
    if (!confirm((isEn ? `Run follow-up test ${number}? Like regular testing it uses the running application and can create real data (e.g. documents and approvals).` : `Jalankan test lanjutan ${number}? Seperti testing biasa, ini memakai aplikasi yang berjalan dan bisa membuat data sungguhan (mis. dokumen dan approval).`))) return;
    button.disabled = true;
    try {
      await api.post(`/api/threads/${threadId}/follow-ups/${number}/run`);
      toast((isEn ? `Follow-up test ${number} started.` : `Test lanjutan ${number} dijalankan.`), 'info');
    } catch (error) {
      toast(error.message, 'error');
      button.disabled = false;
    }
  });

  const approvalKinds = {
    WriteOutside: () => t('Ubah file di luar project', 'Change files outside the project'),
    Command: () => t('Jalankan perintah', 'Run a command'),
    Host: () => t('Akses host luar', 'Call an outside host'),
    Commit: () => 'Commit',
    Push: () => 'Push',
    Branch: () => t('Buat branch', 'Create a branch'),
    PullRequest: () => t('Buka pull request', 'Open a pull request')
  };

  // A paused planner or a commit/push of a finished thread waiting for the developer: answered here or in the chat.
  async function loadApproval() {
    let approval = null;
    try {
      approval = await api.get(`/api/threads/${threadId}/approval`);
    } catch {
      approval = null;
    }
    el('approvalCard').classList.toggle('d-none', !approval);
    if (!approval) return;
    el('approvalReason').innerHTML = md(approval.reason || '');
    el('approvalItems').innerHTML = approval.items.map(i =>
      `<li><strong>${escapeHtml((approvalKinds[i.kind] || (() => i.kind))())}</strong> <code>${escapeHtml(i.target)}</code>${i.detail ? ` — ${escapeHtml(i.detail)}` : ''}</li>`).join('');
    el('btnApproveAlways').classList.toggle('d-none', !approval.allowAlways);
    // "Always allow the pattern" ("npm install *") only when a command has a pattern AIT can keep.
    const patterns = [...new Set(approval.items.filter(i => i.pattern).map(i => i.pattern))];
    el('btnApprovePattern').classList.toggle('d-none', !approval.allowPattern || !patterns.length);
    el('approvalPattern').textContent = patterns.join(', ');
    el('btnApproveAll').classList.toggle('d-none', !approval.allowAll);
  }

  el('approvalCard').addEventListener('click', async event => {
    const button = event.target.closest('[data-approval]');
    if (!button) return;
    el('approvalCard').querySelectorAll('button').forEach(b => b.disabled = true);
    try {
      await api.post(`/api/threads/${threadId}/approval`, { decision: button.dataset.approval });
      toast(button.dataset.approval === 'Deny' ? t('Izin ditolak.', 'Permission denied.') : t('Izin diberikan.', 'Permission given.'), 'success');
      el('approvalCard').classList.add('d-none');
    } catch (error) {
      toast(error.message, 'error');
    } finally {
      el('approvalCard').querySelectorAll('button').forEach(b => b.disabled = false);
    }
  });

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
      title: `${isEn ? 'Model output' : 'Output model'} · ${t.taskKey}${t.attempts > 1 ? (isEn ? ` (${t.attempts} attempts)` : ` (${t.attempts} percobaan)`) : ''}`,
      body: `<pre class="code-block" data-output-for="${t.id}">${escapeHtml(output || (t.status === 'Pending' ? (isEn ? 'Waiting for its turn…' : 'Menunggu giliran…') : (isEn ? 'Live output is only available while the page is open when the task runs.' : 'Output live hanya tersedia selama halaman terbuka saat tugas berjalan.')))}</pre>`
    });
    return `<tr class="task-detail" data-detail-for="${t.id}"><td colspan="7" class="bg-body-tertiary">
      <div class="mb-2"><span class="text-secondary small">${isEn ? 'Instructions' : 'Instruksi'}</span><div class="pre-wrap small">${escapeHtml(t.instruction)}</div></div>
      ${t.dependsOn ? (isEn ? `<div class="mb-2 small"><span class="text-secondary">Depends on:</span> ${escapeHtml(t.dependsOn)}</div>` : `<div class="mb-2 small"><span class="text-secondary">Bergantung pada:</span> ${escapeHtml(t.dependsOn)}</div>`) : ''}
      ${t.summary ? (isEn ? `<div class="mb-2 small"><span class="text-secondary">Summary:</span> ${escapeHtml(t.summary)}</div>` : `<div class="mb-2 small"><span class="text-secondary">Ringkasan:</span> ${escapeHtml(t.summary)}</div>`) : ''}
      ${t.error ? `<div class="mb-2 small text-danger">${escapeHtml(t.error)}</div>` : ''}
      ${t.diff ? winFrame({ id: `diff-${t.id}`, title: `Diff · ${taskPath(t)}`, className: 'mb-2', body: `<pre class="code-block">${diffHtml(t.diff)}</pre>` }) : ''}
      ${outputHtml}
    </td></tr>`;
  }

  function renderTasks() {
    const list = [...tasks.values()].sort((a, b) => a.order - b.order);
    const done = list.filter(t => t.status === 'Succeeded').length;
    el('taskCounter').textContent = list.length ? (isEn ? `${done}/${list.length} succeeded` : `${done}/${list.length} berhasil`) : '';

    taskPager.set(list);

    renderTimeline();
  }

  // Bars share one time axis: overlapping bars are tasks that ran in parallel.
  function renderTimeline() {
    const list = [...tasks.values()].filter(t => t.startedAt).sort((a, b) => a.order - b.order);
    const container = el('timeline');
    if (!list.length) {
      container.innerHTML = t('<div class="text-secondary small">Belum ada tugas yang berjalan.</div>', '<div class="text-secondary small">No task has run yet.</div>');
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
    }).join('') + (isEn ? `<div class="small text-secondary mt-1">Span: ${fmt.ms(span)} — overlapping tasks ran in parallel.</div>` : `<div class="small text-secondary mt-1">Rentang: ${fmt.ms(span)} — tugas yang overlap berjalan paralel.</div>`);

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
    el('activity').innerHTML = t('<div class="empty-state">Aktivitas dikosongkan. Aktivitas baru akan muncul di sini.</div>', '<div class="empty-state">Activity cleared. New activity will show up here.</div>');
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
    renderGit();
    renderUnitTests();
    renderTesting();
    renderFinal();
    renderTasks();
    loadApproval();
  }

  async function loadActivity() {
    try {
      const logs = await api.get(`/api/dashboard/logs?take=300&minLevel=Debug&threadId=${threadId}`);
      el('activity').innerHTML = logs.length
        ? logs.map(l => activityLine(l.createdAt, l.category, l.message, l.level)).join('')
        : t('<div class="empty-state">Belum ada aktivitas.</div>', '<div class="empty-state">No activity yet.</div>');
    } catch {
      el('activity').innerHTML = t('<div class="empty-state">Log tidak tersedia.</div>', '<div class="empty-state">Log not available.</div>');
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
    if (!confirm(t('Batalkan thread ini? Tugas yang belum selesai tidak akan dijalankan.', 'Cancel this thread? Tasks not finished yet will not run.'))) return;
    try {
      await api.post(`/api/threads/${threadId}/cancel`);
      toast(t('Pembatalan dikirim.', 'Cancellation sent.'), 'info');
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('btnRetry').addEventListener('click', async () => {
    if (!confirm(t('Jalankan lagi tugas yang gagal, dilewati, atau dibatalkan? Tugas yang sudah berhasil tidak diulang dan planner tidak dipanggil lagi.', 'Run the failed, skipped or cancelled tasks again? Tasks that succeeded are not repeated and the planner is not called again.'))) return;
    el('btnRetry').disabled = true;
    try {
      await api.post(`/api/threads/${threadId}/retry`);
      toast(t('Tugas yang gagal dijalankan ulang.', 'The failed tasks are running again.'), 'info');
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
      ? (isEn ? `<div class="alert alert-warning small">🔐 The previous testing needed a login: ${escapeHtml(report.loginNeeded)}${report.loginUrl ? ` (<span class="mono">${escapeHtml(report.loginUrl)}</span>)` : ''}</div>` : `<div class="alert alert-warning small">🔐 Testing sebelumnya butuh login: ${escapeHtml(report.loginNeeded)}${report.loginUrl ? ` (<span class="mono">${escapeHtml(report.loginUrl)}</span>)` : ''}</div>`)
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
    if (!username !== !password) return toast(t('Isi username dan password, atau kosongkan keduanya.', 'Fill in the username and password, or leave both empty.'), 'info');
    el('btnRetestConfirm').disabled = true;
    try {
      await api.post(`/api/threads/${threadId}/test`, { username: username || null, password: password || null });
      retestModal.hide();
      toast(t('Testing dijalankan ulang.', 'Testing started again.'), 'info');
    } catch (error) {
      toast(error.message, 'error');
    } finally {
      el('btnRetestConfirm').disabled = false;
    }
  });

  const rollbackModal = bootstrap.Modal.getOrCreateInstance(el('rollbackModal'));
  const rollbackActions = {
    restore: ['success', t('Dikembalikan ke isi asli', 'Restored to the original content')],
    delete: ['success', t('Dihapus (dibuat oleh thread ini)', 'Deleted (created by this thread)')],
    unchanged: ['secondary', t('Sudah sama dengan aslinya — tidak disentuh', 'Already the same as the original — not touched')],
    conflict: ['danger', t('Sudah berubah lagi — menghalangi pembatalan', 'Changed again since — blocks the rollback')]
  };

  el('btnRollback').addEventListener('click', async () => {
    el('rollbackReason').innerHTML = '';
    el('rollbackRows').innerHTML = t('<tr><td colspan="2" class="text-secondary">Memeriksa file…</td></tr>', '<tr><td colspan="2" class="text-secondary">Checking files…</td></tr>');
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
        : t('<tr><td colspan="2" class="text-secondary">Tidak ada file yang tercatat.</td></tr>', '<tr><td colspan="2" class="text-secondary">No files recorded.</td></tr>');
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
      toast((isEn ? `Changes rolled back: ${result.restored} file(s) restored, ${result.deleted} file(s) deleted.` : `Perubahan dibatalkan: ${result.restored} file dikembalikan, ${result.deleted} file dihapus.`));
      loadDetail();
    } catch (error) {
      el('rollbackReason').innerHTML = `<div class="alert alert-danger small">${escapeHtml(error.message)}</div>`;
    }
  });

  loadDetail();
  loadActivity();

  stream(`threadId=${threadId}`, {
    'thread.updated': reloadDetail,
    'thread.git': reloadDetail,
    'approval.requested': loadApproval,
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
