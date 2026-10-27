// Project > Detail → AIT permissions: the "always allow" grants of this project on this machine, with revoke.
(() => {
  'use strict';
  const { t, api, toast, escapeHtml, fmt, stream } = window.AIThread;
  const projectId = window.projectId;
  const el = id => document.getElementById(id);

  const kinds = {
    WriteOutside: () => t('Ubah file di luar project', 'Change files outside the project'),
    Command: () => t('Perintah', 'Command'),
    Host: () => t('Host luar', 'Outside host'),
    Commit: () => 'Commit',
    Push: () => 'Push'
  };

  function render(grants) {
    el('approvalRows').innerHTML = grants.length
      ? grants.map(g => `
        <tr>
          <td class="text-nowrap">${escapeHtml((kinds[g.kind] || (() => g.kind))())}</td>
          <td class="mono small text-break">${escapeHtml(g.target)}</td>
          <td class="small text-nowrap">${fmt.dateTime(g.createdAt)}${g.createdBy ? `<div class="text-secondary">${escapeHtml(g.createdBy)}</div>` : ''}</td>
          <td class="small text-nowrap">${g.lastUsedAt ? fmt.relative(g.lastUsedAt) : '–'}<div class="text-secondary">${fmt.number(g.useCount)}×</div></td>
          <td class="text-end"><button type="button" class="btn btn-outline-danger btn-sm" data-revoke="${g.id}">${t('Cabut', 'Revoke')}</button></td>
        </tr>`).join('')
      : `<tr><td colspan="5" class="text-secondary">${t('Belum ada izin berkelanjutan. AIT menanyakan setiap aksi di luar batas project.', 'No standing permission yet. AIT asks before every action beyond the project.')}</td></tr>`;
  }

  async function load() {
    try {
      render(await api.get(`/api/projects/${projectId}/approvals`));
    } catch (error) {
      el('approvalRows').innerHTML = `<tr><td colspan="5" class="text-danger">${escapeHtml(error.message)}</td></tr>`;
    }
  }

  el('approvalRows').addEventListener('click', async event => {
    const button = event.target.closest('[data-revoke]');
    if (!button) return;
    if (!confirm(t('Cabut izin ini? AIT akan bertanya lagi sebelum aksi tersebut.', 'Revoke this permission? AIT will ask again before that action.'))) return;
    button.disabled = true;
    try {
      await api.del(`/api/projects/${projectId}/approvals/${button.dataset.revoke}`);
      toast(t('Izin dicabut.', 'Permission revoked.'));
      await load();
    } catch (error) {
      toast(error.message, 'error');
      button.disabled = false;
    }
  });

  load();
  stream('types=approval.', {
    'approval.changed': payload => {
      if (!payload.data?.projectId || payload.data.projectId === projectId) load();
    }
  });
})();
