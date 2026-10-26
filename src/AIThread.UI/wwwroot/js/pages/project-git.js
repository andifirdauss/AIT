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
    el('envRows').innerHTML = list.length ? list.map(envRow).join('') : `<div class="text-secondary">${t('Belum ada environment.', 'No environment yet.')}</div>`;
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

  load();
  loadEnvironments();
})();
