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

  load();
})();
