// Project > Detail → Git: automatic fetch of the remotes and the git state of the project's threads.
(() => {
  'use strict';
  const { isEn, t, api, toast, escapeHtml, fmt } = window.AIThread;
  const projectId = window.projectId;
  const el = id => document.getElementById(id);

  function render(git) {
    el('gitCard').classList.toggle('d-none', !git.isRepository);
    if (!git.isRepository) return;
    const fetch = !git.hasRemote
      ? t('Tidak ada git remote: fetch tidak dijalankan.', 'No git remote: no fetch.')
      : git.effectiveFetchMinutes === 0
        ? t('Fetch otomatis mati.', 'Automatic fetch is off.')
        : (isEn ? `Fetched every ${git.effectiveFetchMinutes} minutes${git.fetchMinutes == null ? ' (default)' : ''}.`
          : `Fetch otomatis tiap ${git.effectiveFetchMinutes} menit${git.fetchMinutes == null ? ' (bawaan)' : ''}.`);
    const last = git.lastFetchAt
      ? (git.lastFetchError
        ? `<div class="text-danger">${t('Fetch terakhir gagal', 'Last fetch failed')} (${fmt.relative(git.lastFetchAt)}): ${escapeHtml(git.lastFetchError)}</div>`
        : `<div>${t('Fetch terakhir', 'Last fetch')}: ${fmt.relative(git.lastFetchAt)}</div>`)
      : '';
    const followed = isEn ? `${fmt.number(git.followedThreads)} thread(s) followed (last 30 days).` : `${fmt.number(git.followedThreads)} thread diikuti (30 hari terakhir).`;
    el('gitStatus').innerHTML = `<div>${fetch}</div>${last}<div>${followed}</div>`;
    if (document.activeElement !== el('gitFetchMinutes')) el('gitFetchMinutes').value = git.fetchMinutes ?? '';
    el('gitFetchMinutes').placeholder = String(git.effectiveFetchMinutes);
    el('btnGitFetch').disabled = !git.hasRemote;
  }

  async function load() {
    try {
      render(await api.get(`/api/projects/${projectId}/git`));
    } catch (error) {
      el('gitStatus').textContent = error.message;
    }
  }

  async function run(button, action) {
    button.disabled = true;
    try {
      await action();
    } catch (error) {
      toast(error.message, 'error');
    } finally {
      button.disabled = false;
    }
  }

  el('btnSaveGit').addEventListener('click', () => run(el('btnSaveGit'), async () => {
    const value = el('gitFetchMinutes').value.trim();
    render(await api.put(`/api/projects/${projectId}/git`, { fetchMinutes: value === '' ? null : Number(value) }));
    el('gitFetchMinutes').blur();
    toast(t('Pengaturan git disimpan.', 'Git settings saved.'));
  }));

  el('btnGitFetch').addEventListener('click', () => run(el('btnGitFetch'), async () => {
    const git = await api.post(`/api/projects/${projectId}/git/fetch`);
    render(git);
    if (git.lastFetchError) toast(git.lastFetchError, 'error');
    else toast(t('git fetch selesai.', 'git fetch done.'));
  }));

  el('btnGitSync').addEventListener('click', () => run(el('btnGitSync'), async () => {
    const changed = await api.post(`/api/projects/${projectId}/git/refresh`);
    toast(isEn ? `Git state synced: ${changed} thread(s) changed.` : `Status git disinkronkan: ${changed} thread berubah.`);
  }));

  // --- Environments (shared by the project key) ---

  function envRow(e = { name: '', ref: '', isProduction: false }) {
    const status = e.id
      ? (e.refFound
        ? `<span class="text-secondary">${isEn ? `${fmt.number(e.changes)} change(s)${e.lastSeenAt ? `, latest ${fmt.relative(e.lastSeenAt)}` : ''}` : `${fmt.number(e.changes)} perubahan${e.lastSeenAt ? `, terbaru ${fmt.relative(e.lastSeenAt)}` : ''}`}</span>`
        : `<span class="text-warning">${t('ref belum ada di clone ini', 'ref not in this clone yet')}</span>`)
      : '';
    return `<div class="input-group input-group-sm mb-1" data-env-row>
        <input type="text" class="form-control" data-env-name maxlength="50" placeholder="prod" value="${escapeHtml(e.name)}" />
        <input type="text" class="form-control mono" data-env-ref maxlength="250" placeholder="origin/main" value="${escapeHtml(e.ref)}" />
        <span class="input-group-text"><input class="form-check-input mt-0" type="checkbox" data-env-prod title="Production" ${e.isProduction ? 'checked' : ''} />&nbsp;prod</span>
        <button class="btn btn-outline-danger" type="button" data-env-remove title="${t('Hapus', 'Remove')}">✕</button>
      </div>${status ? `<div class="small mb-2">${status}</div>` : ''}`;
  }

  function renderEnvironments(list) {
    const registered = list.filter(e => !e.isAuto);
    const branches = list.filter(e => e.isAuto);
    el('envRows').innerHTML = registered.length
      ? registered.map(envRow).join('')
      : `<div class="text-secondary">${t('Belum ada environment terdaftar.', 'No registered environment yet.')}</div>`;
    // Remote branches are environments by themselves: listed, not edited (register one to rename it or mark it prod).
    el('envBranches').innerHTML = branches.length
      ? `<div class="small text-secondary mb-1">${isEn ? `${branches.length} remote branch(es) of this clone, also environments:` : `${branches.length} branch remote clone ini, otomatis jadi environment:`}</div>`
        + `<div class="d-flex flex-wrap gap-1">${branches.map(e => `<span class="badge text-bg-light mono" title="${escapeHtml(`${e.ref} · ${isEn ? `${fmt.number(e.changes)} change(s)` : `${fmt.number(e.changes)} perubahan`}`)}">${escapeHtml(e.name)}</span>`).join('')}</div>`
      : `<div class="small text-secondary">${t('Tidak ada branch remote (belum ada remote atau belum di-fetch).', 'No remote branch (no remote yet, or not fetched yet).')}</div>`;
  }

  async function loadEnvironments() {
    try {
      renderEnvironments(await api.get(`/api/projects/${projectId}/environments`));
    } catch (error) {
      el('envRows').textContent = error.message;
    }
  }

  el('btnEnvAdd').addEventListener('click', () => {
    if (!el('envRows').querySelector('[data-env-row]')) el('envRows').innerHTML = '';
    el('envRows').insertAdjacentHTML('beforeend', envRow());
  });

  el('envRows').addEventListener('click', event => {
    const remove = event.target.closest('[data-env-remove]');
    if (!remove) return;
    const row = remove.closest('[data-env-row]');
    if (row.nextElementSibling && !row.nextElementSibling.matches('[data-env-row]')) row.nextElementSibling.remove();
    row.remove();
  });

  el('btnEnvSave').addEventListener('click', () => run(el('btnEnvSave'), async () => {
    const environments = [...el('envRows').querySelectorAll('[data-env-row]')]
      .map(row => ({
        name: row.querySelector('[data-env-name]').value.trim(),
        ref: row.querySelector('[data-env-ref]').value.trim(),
        isProduction: row.querySelector('[data-env-prod]').checked
      }))
      .filter(e => e.name || e.ref);
    renderEnvironments(await api.put(`/api/projects/${projectId}/environments`, { environments }));
    toast(t('Environment disimpan.', 'Environments saved.'));
  }));

  el('btnEnvRefresh').addEventListener('click', () => run(el('btnEnvRefresh'), async () => {
    renderEnvironments(await api.post(`/api/projects/${projectId}/environments/refresh`));
    toast(t('Environment dicek.', 'Environments checked.'));
  }));

  // --- Commits made outside AIT (shared by the project key) ---

  const ingestLabels = {
    Done: ['success', t('dirangkum', 'summarized')],
    Skipped: ['secondary', t('dilewati', 'skipped')],
    Failed: ['danger', t('gagal', 'failed')],
    Pending: ['info', t('diproses', 'in progress')]
  };

  // Notes the ingestor stores (for the list): AIT's own words, shown in the UI language.
  function ingestNote(note) {
    if (!note) return '';
    const copy = /^copy of (\w+)$/.exec(note);
    if (copy) return isEn ? `copy of ${copy[1]} (rebase/cherry-pick)` : `salinan ${copy[1]} (rebase/cherry-pick)`;
    const squash = /^squash of (\d+) commit\(s\)$/.exec(note);
    if (squash) return isEn ? `squash of ${squash[1]} summarized commit(s)` : `squash dari ${squash[1]} commit yang sudah dirangkum`;
    const known = { 'no files': t('tanpa file', 'no files'), 'nothing to keep': t('tidak ada yang perlu diingat', 'nothing to keep') };
    return known[note] || note;
  }

  let lastIngest = null;

  function renderIngest(ingest) {
    lastIngest = ingest;
    if (document.activeElement !== el('ingestRef')) el('ingestRef').value = ingest.ref || '';
    if (document.activeElement !== el('ingestMax')) el('ingestMax').value = ingest.maxPerRun;
    el('ingestEnabled').checked = ingest.enabled;
    el('ingestWorkingBranch').checked = ingest.workingBranch;
    el('btnIngestRun').disabled = !ingest.enabled;
    const source = ingest.effectiveRef
      ? (isEn ? `Reads <span class="mono">${escapeHtml(ingest.effectiveRef)}</span>` : `Membaca <span class="mono">${escapeHtml(ingest.effectiveRef)}</span>`)
      : `<span class="text-warning">${t('ref tidak ditemukan di clone ini', 'ref not found in this clone')}</span>`;
    const branch = ingest.workingBranch && ingest.currentBranch ? ` + branch <span class="mono">${escapeHtml(ingest.currentBranch)}</span>` : '';
    el('ingestStatus').innerHTML = `${source}${branch} · ${isEn ? `since ${fmt.dateTime(ingest.since)}` : `sejak ${fmt.dateTime(ingest.since)}`}`
      + ` · ${isEn ? `${ingest.done} summarized, ${ingest.skipped} skipped, ${ingest.failed} failed` : `${ingest.done} dirangkum, ${ingest.skipped} dilewati, ${ingest.failed} gagal`}`;

    // Off while commits made outside AIT are piling up: memory does not know them.
    const outside = !ingest.enabled && ingest.outside > 0 ? ingest.outside : 0;
    el('ingestSuggest').classList.toggle('d-none', outside === 0);
    if (outside > 0) {
      const where = escapeHtml(ingest.effectiveRef || '') + (ingest.workingBranch && ingest.currentBranch ? ` + ${escapeHtml(ingest.currentBranch)}` : '');
      el('ingestSuggestText').innerHTML = isEn
        ? `There ${outside === 1 ? 'is' : 'are'} <b>${outside}${outside >= 200 ? '+' : ''}</b> commit(s) made outside AIT in the last ${ingest.outsideDays} days on <span class="mono">${where}</span> that memory does not know yet (e.g. a prompt sent to a provider directly). Turn on commit ingest so they are summarized, ${ingest.maxPerRun} per pass.`
        : `Ada <b>${outside}${outside >= 200 ? '+' : ''}</b> commit di luar AIT dalam ${ingest.outsideDays} hari terakhir di <span class="mono">${where}</span> yang belum diketahui memory (mis. prompt langsung ke provider lain). Nyalakan ingest commit supaya ikut dirangkum, ${ingest.maxPerRun} per putaran.`;
    }

    el('ingestRecent').innerHTML = ingest.recent.map(c => {
      const [color, label] = ingestLabels[c.status] || ['secondary', c.status];
      const from = c.branch ? ` <span class="badge text-bg-light border mono" title="${escapeHtml(t('Dibaca dari branch kerja', 'Read from the working branch'))}">${escapeHtml(c.branch)}</span>` : '';
      const note = ingestNote(c.note);
      return `<li class="mb-1"><span class="badge text-bg-${color}">${label}</span>${from} <span class="mono">${escapeHtml(c.commit.substring(0, 8))}</span>
        ${escapeHtml(c.title || '')} <span class="text-secondary">— ${escapeHtml(c.author || '')}${note ? ` · ${escapeHtml(note)}` : ''}</span></li>`;
    }).join('');
  }

  async function loadIngest() {
    try {
      renderIngest(await api.get(`/api/projects/${projectId}/git/ingest`));
    } catch (error) {
      el('ingestStatus').textContent = error.message;
    }
  }

  el('btnIngestSave').addEventListener('click', () => run(el('btnIngestSave'), async () => {
    if (el('ingestEnabled').checked && !confirm(t('Setiap commit di luar AIT akan dirangkum model (memakai token). Lanjut?', 'Every commit made outside AIT will be summarized by a model (costs tokens). Continue?'))) return;
    const max = el('ingestMax').value.trim();
    renderIngest(await api.put(`/api/projects/${projectId}/git/ingest`, {
      enabled: el('ingestEnabled').checked, ref: el('ingestRef').value.trim() || null, since: null, maxPerRun: max === '' ? null : Number(max),
      workingBranch: el('ingestWorkingBranch').checked
    }));
    toast(t('Pengaturan ingest commit disimpan.', 'Commit ingest settings saved.'));
  }));

  // The suggestion: on from the start of the counted window, so the commits it counted are summarized.
  el('btnIngestSuggest').addEventListener('click', () => run(el('btnIngestSuggest'), async () => {
    if (!lastIngest) return;
    const count = lastIngest.outside;
    if (!confirm(isEn
      ? `${count} commit(s) made outside AIT will be summarized by a model (costs tokens), ${lastIngest.maxPerRun} per pass, and new ones from now on. Continue?`
      : `${count} commit di luar AIT akan dirangkum model (memakai token), ${lastIngest.maxPerRun} per putaran, begitu juga commit baru berikutnya. Lanjut?`)) return;
    const since = new Date(Date.now() - lastIngest.outsideDays * 24 * 3600 * 1000).toISOString();
    renderIngest(await api.put(`/api/projects/${projectId}/git/ingest`, {
      enabled: true, ref: lastIngest.ref, since, maxPerRun: lastIngest.maxPerRun, workingBranch: lastIngest.workingBranch
    }));
    const result = await api.post(`/api/projects/${projectId}/git/ingest/run`);
    toast(result.problem || (isEn ? `Commit ingest on: ${result.ingested} commit(s) summarized in the first pass.`
      : `Ingest commit menyala: ${result.ingested} commit dirangkum di putaran pertama.`), result.problem ? 'error' : 'success');
    loadIngest();
  }));

  el('btnIngestRun').addEventListener('click', () => run(el('btnIngestRun'), async () => {
    const result = await api.post(`/api/projects/${projectId}/git/ingest/run`);
    toast(result.problem || (isEn ? `${result.ingested} commit(s) summarized, ${result.skipped} skipped, ${result.failed} failed.`
      : `${result.ingested} commit dirangkum, ${result.skipped} dilewati, ${result.failed} gagal.`), result.problem ? 'error' : 'success');
    loadIngest();
  }));

  load();
  loadEnvironments();
  loadIngest();
})();
