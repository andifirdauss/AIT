// Project > Detail → Unit test: the project's unit test catalog (shared by every developer of the project), its
// settings, scans and manual runs, and the confirmation of tests a thread marked obsolete. Snapshots are never shown.
(() => {
  'use strict';
  const { api, toast, escapeHtml, fmt, debounce, pager } = window.AIThread;
  const projectId = window.projectId;
  const el = id => document.getElementById(id);
  const base = `/api/projects/${projectId}/unit-tests`;

  const states = { Active: ['success', 'Aktif'], Obsolete: ['warning', 'Obsolete'], Quarantined: ['secondary', 'Karantina'], Removed: ['dark', 'Dihapus'] };
  const results = {
    Passed: ['success', 'Lolos'], Failed: ['danger', 'Gagal'], Error: ['danger', 'Error'], Skipped: ['secondary', 'Dilewati'],
    NotFound: ['warning', 'Tidak dilaporkan'], Unknown: ['light border', 'Belum dijalankan']
  };
  const origins = { Ait: 'Dibuat AIT', Scanned: 'Scan kode', Regenerated: 'Ditulis ulang (perlu konfirmasi)' };
  const modes = { Off: 'mati', Relevant: 'test terkait saja', All: 'semua test aktif' };
  const authorings = { OnRequest: 'hanya saat diminta', AutoLogic: 'otomatis untuk perubahan Logic', Off: 'tidak pernah' };
  const phases = { baseline: 'sebelum perubahan', after: 'setelah perubahan', manual: 'manual', flaky: 'tidak stabil' };

  const settingsModal = bootstrap.Modal.getOrCreateInstance(el('unitTestSettingsModal'));
  const detailModal = bootstrap.Modal.getOrCreateInstance(el('unitTestDetailModal'));
  let catalog = null;
  let query = null;

  const badge = ([color, label]) => `<span class="badge text-bg-${color}">${escapeHtml(label)}</span>`;
  const firstLine = text => String(text || '').split('\n').find(l => l.trim()) || '';
  const isFailing = t => t.lastResult === 'Failed' || t.lastResult === 'Error';

  const testPager = pager('unitTestPager', rows => {
    el('unitTestRows').innerHTML = rows.length
      ? rows.map(rowHtml).join('')
      : `<tr><td colspan="7" class="text-secondary">${catalog?.tests.length ? 'Tidak ada test yang cocok dengan filter.' : 'Belum ada unit test di katalog. Tekan Scan test untuk mendaftarkan test yang sudah ada di kode.'}</td></tr>`;
  }, 15);

  function rowHtml(t) {
    const state = badge(states[t.state] || ['secondary', t.state]);
    const result = badge(results[t.lastResult] || ['secondary', t.lastResult]);
    const lastRun = t.lastRunAt
      ? `<div class="small text-secondary">${fmt.relative(t.lastRunAt)}${t.lastRunBy ? ` · ${escapeHtml(t.lastRunBy)}` : ''}${t.lastDurationMs != null ? ` · ${fmt.ms(t.lastDurationMs)}` : ''}</div>`
      : '';
    const why = t.lastResult === 'Skipped' && t.lastSkipReason
      ? `<div class="small text-secondary">${escapeHtml(t.lastSkipReason)}</div>`
      : isFailing(t) && t.lastMessage
        ? `<div class="small text-danger text-truncate" style="max-width:22rem" title="${escapeHtml(t.lastMessage)}">${escapeHtml(firstLine(t.lastMessage))}</div>`
        : '';
    const stateNote = t.state === 'Obsolete'
      ? `<div class="small mt-1"><span class="text-secondary">Alasan:</span> ${escapeHtml(t.obsoleteReason || '–')}</div>
         <div class="small text-secondary">${escapeHtml(t.obsoleteBy || '')}${t.obsoleteAt ? ` · ${fmt.relative(t.obsoleteAt)}` : ''}${t.obsoleteThreadId ? ` · <a href="/Thread/Detail/${t.obsoleteThreadId}">thread</a>` : ''}</div>`
      : t.state === 'Quarantined' && t.quarantineReason
        ? `<div class="small text-secondary mt-1">${escapeHtml(t.quarantineReason)}</div>`
        : '';
    const actions = [];
    if (t.state !== 'Removed') actions.push(`<button type="button" class="btn btn-sm btn-outline-secondary" data-run="${t.id}" title="Jalankan test ini sekarang">Jalankan</button>`);
    if (t.state === 'Obsolete') {
      actions.push(`<button type="button" class="btn btn-sm btn-outline-primary" data-reactivate="${t.id}">Kembalikan</button>`);
      actions.push(`<button type="button" class="btn btn-sm btn-outline-danger" data-remove="${t.id}">Konfirmasi hapus</button>`);
    }
    if (t.state === 'Quarantined') actions.push(`<button type="button" class="btn btn-sm btn-outline-primary" data-reactivate="${t.id}">Aktifkan</button>`);
    return `<tr>
      <td class="mono text-nowrap">${escapeHtml(t.displayNumber)}</td>
      <td>
        <button type="button" class="btn btn-link p-0 text-start fw-semibold" data-detail="${t.id}">${escapeHtml(t.title)}</button>
        ${t.purpose ? `<div class="small text-secondary">${escapeHtml(t.purpose)}</div>` : ''}
        <div class="small mono text-secondary text-break">${escapeHtml(t.testFile)} · ${escapeHtml(t.methodName)}</div>
      </td>
      <td class="small">${escapeHtml(t.module || '–')}</td>
      <td>${state}${stateNote}</td>
      <td>${result}${lastRun}${why}</td>
      <td class="small">${escapeHtml(origins[t.origin] || t.origin)}${t.createdBy ? `<div class="text-secondary">${escapeHtml(t.createdBy)}</div>` : ''}</td>
      <td class="text-end"><div class="d-flex flex-wrap gap-1 justify-content-end">${actions.join('')}</div></td>
    </tr>`;
  }

  function filtered() {
    const search = el('unitTestSearch').value.trim().toLowerCase();
    const state = el('unitTestStateFilter').value;
    const module = el('unitTestModuleFilter').value;
    return (catalog?.tests || []).filter(t =>
      (state === 'Failing' ? isFailing(t) && t.state !== 'Removed' : state ? t.state === state : t.state !== 'Removed')
      && (!module || t.module === module)
      && (!search || [t.displayNumber, t.title, t.purpose, t.testFile, t.methodName].some(v => String(v || '').toLowerCase().includes(search))));
  }

  function render(reset = false) {
    const key = `${el('unitTestSearch').value}|${el('unitTestStateFilter').value}|${el('unitTestModuleFilter').value}`;
    testPager.set(filtered(), reset || key !== query);
    query = key;
  }

  function renderInfo() {
    const tests = catalog.tests;
    const count = state => tests.filter(t => t.state === state).length;
    const failing = tests.filter(t => t.state === 'Active' && isFailing(t)).length;
    el('unitTestCounts').textContent = tests.length
      ? `· ${fmt.number(count('Active'))} aktif${count('Obsolete') ? `, ${count('Obsolete')} obsolete` : ''}${count('Quarantined') ? `, ${count('Quarantined')} karantina` : ''}${failing ? `, ${failing} gagal terakhir` : ''}`
      : '';

    const s = catalog.settings;
    const suites = catalog.suites.length
      ? catalog.suites.map(x => `<span class="chip" title="${escapeHtml(x.problem || x.stack)}">${escapeHtml(x.framework)} · <span class="mono">${escapeHtml(x.manifest)}</span>${x.problem ? ' ⚠️' : x.perTestResults ? '' : ' (tanpa hasil per test)'}</span>`).join('')
      : '<span class="text-warning">Tidak ada test project/runner yang dikenali (dotnet test, jest, vitest, mocha, pytest, PHPUnit, Maven, Gradle, go test, cargo test).</span>';
    const problems = catalog.suites.filter(x => x.problem).map(x => `<div class="text-warning">⚠️ ${escapeHtml(x.manifest)}: ${escapeHtml(x.problem)}</div>`).join('');
    el('unitTestInfo').innerHTML = `
      <div class="mb-1">Katalog dipakai bersama semua developer project ini (key <span class="mono">${escapeHtml(catalog.projectKey)}</span>). Setiap thread menjalankan
        <strong>${escapeHtml(modes[s.mode] || s.mode)}</strong> sebelum dan sesudah perubahan; test yang lolos sebelumnya lalu gagal diperbaiki di kode (maks. 3 putaran).
        Test baru: <strong>${escapeHtml(authorings[s.authoring] || s.authoring)}</strong>. Snapshot: <strong>${s.keepSnapshots ? 'disimpan' : 'tidak disimpan'}</strong>.
        Test yang hilang dari repository dipulihkan otomatis.</div>
      <div class="mb-1">Runner: ${suites}</div>
      ${problems}
      ${catalog.testerAvailable ? '' : '<div class="text-warning">Belum ada node Tester aktif: kegagalan unit test diperbaiki di kode tanpa triase. Tambahkan node Tester (model kuat) di menu Node.</div>'}`;

    const current = el('unitTestModuleFilter').value;
    const modules = [...new Set(tests.map(t => t.module).filter(Boolean))].sort();
    el('unitTestModuleFilter').innerHTML = '<option value="">Semua modul</option>' + modules.map(m => `<option value="${escapeHtml(m)}">${escapeHtml(m)}</option>`).join('');
    if (modules.includes(current)) el('unitTestModuleFilter').value = current;
  }

  async function load() {
    try {
      catalog = await api.get(base);
      renderInfo();
      render();
    } catch (error) {
      el('unitTestInfo').innerHTML = `<span class="text-danger">${escapeHtml(error.message)}</span>`;
      el('unitTestRows').innerHTML = '<tr><td colspan="7" class="text-secondary">–</td></tr>';
    }
  }

  async function run(body, button) {
    const label = button?.textContent;
    if (button) {
      button.disabled = true;
      button.textContent = 'Menjalankan…';
    }
    toast('Menjalankan unit test… (bisa memakan waktu, termasuk build project test)', 'info');
    try {
      const result = await api.post(`${base}/run`, body);
      const failed = result.tests.filter(t => t.result === 'Failed' || t.result === 'Error');
      toast(`${result.ran} test: ${result.passed} lolos, ${result.failed} gagal, ${result.skipped} dilewati.${failed.length ? ` Gagal: ${failed.map(t => t.displayNumber).join(', ')}` : ''}`,
        result.failed ? 'warning' : 'success');
      result.warnings.forEach(w => toast(w, 'warning'));
      await load();
    } catch (error) {
      toast(error.message, 'error');
    } finally {
      if (button) {
        button.disabled = false;
        button.textContent = label;
      }
    }
  }

  async function openDetail(id) {
    const t = catalog.tests.find(x => x.id === id);
    if (!t) return;
    el('unitTestDetailTitle').textContent = `${t.displayNumber} · ${t.title}`;
    const field = (label, value) => value ? `<dt class="col-sm-3 fw-normal text-secondary">${label}</dt><dd class="col-sm-9">${value}</dd>` : '';
    const list = values => values.length ? values.map(v => `<span class="chip mono">${escapeHtml(v)}</span>`).join('') : null;
    el('unitTestDetailBody').innerHTML = `
      <dl class="row small mb-3">
        ${field('Tujuan', t.purpose ? escapeHtml(t.purpose) : null)}
        ${field('Skenario', t.scenario ? `<div class="pre-wrap">${escapeHtml(t.scenario)}</div>` : null)}
        ${field('Modul', t.module ? escapeHtml(t.module) : null)}
        ${field('Kode yang diuji', list(t.targetFiles))}
        ${field('File test', `<span class="mono">${escapeHtml(t.testFile)}</span>`)}
        ${field('Selector', `<span class="mono text-break">${escapeHtml(t.selector)}</span>`)}
        ${field('Framework', `${escapeHtml(t.framework)}${t.testProject ? ` · <span class="mono">${escapeHtml(t.testProject)}</span>` : ''}`)}
        ${field('Kebutuhan', list(t.requires))}
        ${field('Asal', `${escapeHtml(origins[t.origin] || t.origin)}${t.createdBy ? ` · ${escapeHtml(t.createdBy)}` : ''} · ${fmt.dateTime(t.createdAt)}${t.createdThreadId ? ` · <a href="/Thread/Detail/${t.createdThreadId}">thread</a>` : ''}`)}
        ${field('Snapshot', t.hasSnapshot ? 'tersimpan (untuk pemulihan otomatis, tidak ditampilkan)' : 'belum ada')}
        ${t.state === 'Obsolete' ? field('Alasan obsolete', escapeHtml(t.obsoleteReason || '–')) : ''}
        ${t.lastMessage && isFailing(t) ? field('Pesan gagal', `<pre class="code-block small mb-0">${escapeHtml(t.lastMessage)}</pre>`) : ''}
      </dl>
      <h6 class="small text-secondary text-uppercase">Riwayat run</h6>
      <div id="unitTestHistory" class="small text-secondary">Memuat…</div>`;
    detailModal.show();
    try {
      const runs = await api.get(`${base}/${id}/history`);
      el('unitTestHistory').innerHTML = runs.length
        ? `<table class="table table-sm mb-0"><thead><tr><th>Waktu</th><th>Fase</th><th>Hasil</th><th>Oleh</th><th>Keterangan</th></tr></thead><tbody>${runs.map(r => `<tr>
            <td class="text-nowrap">${fmt.dateTime(r.runAt)}</td>
            <td>${escapeHtml(phases[r.phase] || r.phase)}${r.threadId ? ` · <a href="/Thread/Detail/${r.threadId}">thread</a>` : ''}</td>
            <td>${badge(results[r.result] || ['secondary', r.result])}${r.durationMs != null ? ` <span class="text-secondary">${fmt.ms(r.durationMs)}</span>` : ''}</td>
            <td>${escapeHtml(r.runBy || '–')}${r.machine ? `<div class="text-secondary mono">${escapeHtml(r.machine)}</div>` : ''}</td>
            <td class="text-break">${escapeHtml(r.skipReason || firstLine(r.message))}</td></tr>`).join('')}</tbody></table>`
        : 'Belum pernah dijalankan.';
    } catch (error) {
      el('unitTestHistory').textContent = error.message;
    }
  }

  el('unitTestRows').addEventListener('click', async event => {
    const target = event.target.closest('button');
    if (!target) return;
    if (target.dataset.detail) return openDetail(target.dataset.detail);
    if (target.dataset.run) return run({ ids: [target.dataset.run], all: false }, target);
    const test = catalog.tests.find(t => t.id === (target.dataset.reactivate || target.dataset.remove));
    if (!test) return;
    try {
      if (target.dataset.reactivate) {
        await api.post(`${base}/${test.id}/reactivate`);
        toast(`${test.displayNumber} aktif lagi.`);
      } else {
        if (!confirm(`Hapus ${test.displayNumber} "${test.title}" dari katalog?\n\nAlasan obsolete: ${test.obsoleteReason || '–'}\n\nTest tidak akan dipulihkan lagi, dan sebuah thread menghapus kodenya (bisa di-rollback dari halaman thread).`)) return;
        const result = await api.post(`${base}/${test.id}/confirm-removal`, { removeCode: true });
        toast(result.threadId ? `${test.displayNumber} dihapus dari katalog; thread penghapus kode berjalan.` : `${test.displayNumber} dihapus dari katalog.`);
        if (result.threadId) setTimeout(() => { window.location.href = `/Thread/Detail/${result.threadId}`; }, 800);
      }
      await load();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('btnUnitTestRunAll').addEventListener('click', event => {
    if (!confirm('Jalankan semua unit test aktif sekarang? Ini bisa memakan waktu beberapa menit untuk project besar.')) return;
    run({ ids: [], all: true }, event.currentTarget);
  });

  el('btnUnitTestScan').addEventListener('click', async () => {
    el('btnUnitTestScan').disabled = true;
    toast('Mencari test di kode dan memberi label… (model murah dipakai untuk judul dan tujuan)', 'info');
    try {
      const result = await api.post(`${base}/scan`);
      toast(`${result.found} test ditemukan: ${result.added} baru, ${result.moved} pindah file, ${result.missing} hilang (dipulihkan di thread berikutnya), ${result.labeled} diberi label.`);
      result.warnings.forEach(w => toast(w, 'warning'));
      await load();
    } catch (error) {
      toast(error.message, 'error');
    } finally {
      el('btnUnitTestScan').disabled = false;
    }
  });

  el('btnUnitTestSettings').addEventListener('click', () => {
    if (!catalog) return;
    const s = catalog.settings;
    el('unitTestMode').value = s.mode;
    el('unitTestAuthoring').value = s.authoring;
    el('unitTestKeepSnapshots').checked = s.keepSnapshots;
    el('unitTestTimeout').value = s.timeoutSeconds ?? '';
    settingsModal.show();
  });

  el('unitTestSettingsForm').addEventListener('submit', async event => {
    event.preventDefault();
    const keep = el('unitTestKeepSnapshots').checked;
    if (catalog.settings.keepSnapshots && !keep && !confirm('Mematikan snapshot ikut menghapus snapshot yang sudah tersimpan. Test yang hilang hanya bisa ditulis ulang dari skenarionya. Lanjutkan?')) return;
    try {
      await api.put(`${base}/settings`, {
        mode: el('unitTestMode').value,
        authoring: el('unitTestAuthoring').value,
        keepSnapshots: keep,
        timeoutSeconds: el('unitTestTimeout').value ? Number(el('unitTestTimeout').value) : null
      });
      settingsModal.hide();
      toast('Setting unit test disimpan.');
      await load();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('unitTestSearch').addEventListener('input', debounce(() => render(), 250));
  el('unitTestStateFilter').addEventListener('change', () => render(true));
  el('unitTestModuleFilter').addEventListener('change', () => render(true));

  load();
  // A finished thread may have added, retired or run tests (project-detail.js owns the page's event stream).
  document.addEventListener('ait:thread-updated', debounce(load, 2000));
})();
