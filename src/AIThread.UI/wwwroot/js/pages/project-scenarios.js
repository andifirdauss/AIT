// Project > Detail → Skenario test: the project's multi-step test scenarios (shared by the team through the project key),
// their Excel sheets (download, upload) and runs. A run uses the thread the scenario came from; its progress shows there.
(() => {
  'use strict';
  const { isEn, t, api, toast, escapeHtml, fmt } = window.AIThread;
  const projectId = window.projectId;
  const el = id => document.getElementById(id);
  const base = `/api/projects/${projectId}/test-scenarios`;
  const outcomes = { passed: ['success', (isEn ? 'Passed' : 'Lolos')], failed: ['danger', t('Gagal', 'Failed')], blocked: ['warning', t('Tidak bisa dicek', 'Could not check')], skipped: ['secondary', t('Dilewati', 'Skipped')] };
  const sideEffects = t('Skenario dijalankan di aplikasi yang sedang berjalan: submit dan approve sungguhan membuat data dan bisa mengirim email atau notifikasi. ', 'Scenarios run in the running application: real submits and approvals create data and can send emails or notifications. ') +
    t('Pastikan database dan email di lingkungan dev tidak mengarah ke orang asli.', 'Make sure the database and email of the dev environment do not reach real people.');
  let scenarios = [];

  async function load() {
    try {
      scenarios = await api.get(base);
      render();
    } catch (error) {
      el('testScenarioRows').innerHTML = `<tr><td colspan="5" class="text-danger">${escapeHtml(error.message)}</td></tr>`;
    }
  }

  function render() {
    el('testScenarioCount').textContent = scenarios.length ? `· ${scenarios.length} skenario` : '';
    el('testScenarioRows').innerHTML = scenarios.length
      ? scenarios.map(rowHtml).join('')
      : t('<tr><td colspan="5" class="text-secondary">Belum ada skenario. Buat dari test lanjutan yang disarankan Tester di halaman thread.</td></tr>', '<tr><td colspan="5" class="text-secondary">No scenario yet. Create one from a follow-up test the Tester suggested on the thread page.</td></tr>');
  }

  function rowHtml(s) {
    const [color, label] = outcomes[s.lastOutcome] || ['light border', t('Belum dijalankan', 'Not run yet')];
    const roles = s.roles.map(r => `<span class="mono">${escapeHtml(r.role)}</span>: ${r.username ? `<span class="mono">${escapeHtml(r.username)}</span>` : t('<span class="text-warning">belum diisi</span>', '<span class="text-warning">not filled in yet</span>')}`).join('<br>');
    const missing = s.missing.length ? `<div class="small text-warning">${s.missing.map(escapeHtml).join('<br>')}</div>` : '';
    return (isEn ? `<tr>
      <td><strong>${escapeHtml(s.title)}</strong>
        <div class="small mono text-secondary">${escapeHtml(s.slug)} · version ${s.version}${s.fields.length ? ` · fields: ${s.fields.map(escapeHtml).join(', ')}` : ''}</div>
        ${s.description ? `<div class="small text-secondary">${escapeHtml(s.description)}</div>` : ''}${missing}</td>
      <td>${s.steps.length}</td>
      <td class="small">${roles || '–'}</td>
      <td><span class="badge text-bg-${color}">${escapeHtml(label)}</span>${s.lastRunAt ? `<div class="small text-secondary">${fmt.relative(s.lastRunAt)}${s.lastRunBy ? ` · ${escapeHtml(s.lastRunBy)}` : ''}</div>` : ''}
        ${s.sourceThreadId ? `<div class="small"><a href="/Thread/Detail/${s.sourceThreadId}">thread</a></div>` : ''}</td>
      <td class="text-end text-nowrap">
        <a class="btn btn-sm btn-outline-secondary" href="${base}/${s.id}/excel" title="Download the Excel file to fill in">Excel</a>
        <button type="button" class="btn btn-sm btn-outline-primary" data-run="${s.id}" ${s.missing.length ? 'disabled title="Complete the Excel file first"' : ''}>Run</button>
        <button type="button" class="btn btn-sm btn-outline-danger" data-delete="${s.id}">Delete</button>
      </td>
    </tr>` : `<tr>
      <td><strong>${escapeHtml(s.title)}</strong>
        <div class="small mono text-secondary">${escapeHtml(s.slug)} · versi ${s.version}${s.fields.length ? ` · kolom: ${s.fields.map(escapeHtml).join(', ')}` : ''}</div>
        ${s.description ? `<div class="small text-secondary">${escapeHtml(s.description)}</div>` : ''}${missing}</td>
      <td>${s.steps.length}</td>
      <td class="small">${roles || '–'}</td>
      <td><span class="badge text-bg-${color}">${escapeHtml(label)}</span>${s.lastRunAt ? `<div class="small text-secondary">${fmt.relative(s.lastRunAt)}${s.lastRunBy ? ` · ${escapeHtml(s.lastRunBy)}` : ''}</div>` : ''}
        ${s.sourceThreadId ? `<div class="small"><a href="/Thread/Detail/${s.sourceThreadId}">thread</a></div>` : ''}</td>
      <td class="text-end text-nowrap">
        <a class="btn btn-sm btn-outline-secondary" href="${base}/${s.id}/excel" title="Unduh file Excel untuk diisi">Excel</a>
        <button type="button" class="btn btn-sm btn-outline-primary" data-run="${s.id}" ${s.missing.length ? 'disabled title="Lengkapi dulu file Excel-nya"' : ''}>Jalankan</button>
        <button type="button" class="btn btn-sm btn-outline-danger" data-delete="${s.id}">Hapus</button>
      </td>
    </tr>`);
  }

  el('testScenarioRows').addEventListener('click', async event => {
    const run = event.target.closest('[data-run]');
    const remove = event.target.closest('[data-delete]');
    const id = run?.dataset.run ?? remove?.dataset.delete;
    const scenario = scenarios.find(s => s.id === id);
    if (!scenario) return;
    try {
      if (run) {
        if (!confirm((isEn ? `Run scenario "${scenario.title}" (${scenario.steps.length} steps)?\n\n${sideEffects}` : `Jalankan skenario "${scenario.title}" (${scenario.steps.length} langkah)?\n\n${sideEffects}`))) return;
        run.disabled = true;
        const threadId = await api.post(`${base}/${scenario.id}/run`, { acknowledge: true });
        toast(t('Skenario dijalankan; kemajuannya tampil di halaman thread.', 'Scenario started; its progress shows on the thread page.'), 'info');
        window.location.href = `/Thread/Detail/${threadId}`;
      } else if (confirm((isEn ? `Delete scenario "${scenario.title}"? It disappears for every developer of the project.` : `Hapus skenario "${scenario.title}"? Skenario hilang untuk semua developer project.`))) {
        await api.del(`${base}/${scenario.id}`);
        toast(t('Skenario dihapus.', 'Scenario deleted.'));
        await load();
      }
    } catch (error) {
      toast(error.message, 'error');
      if (run) run.disabled = false;
    }
  });

  el('testScenarioFile').addEventListener('change', async event => {
    const file = event.target.files[0];
    event.target.value = '';
    if (!file) return;
    const form = new FormData();
    form.append('file', file);
    try {
      const result = await api.post(`${base}/import`, form);
      toast((isEn ? `Scenario "${result.scenario.title}" version ${result.scenario.version} saved${result.accountsAdded.length ? `; new test accounts: ${result.accountsAdded.join(', ')}` : ''}.` : `Skenario "${result.scenario.title}" versi ${result.scenario.version} disimpan${result.accountsAdded.length ? `; akun uji baru: ${result.accountsAdded.join(', ')}` : ''}.`));
      result.warnings.forEach(w => toast(w, 'warning'));
      await load();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  load();
})();
