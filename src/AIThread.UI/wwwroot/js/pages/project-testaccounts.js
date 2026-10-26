// Project > Detail → Akun uji: the project's test accounts (shared by every developer of the project), its development
// password, and how "login as" works in its app. The tester switches between these accounts while testing.
(() => {
  'use strict';
  const { isEn, t, api, toast, escapeHtml, fmt } = window.AIThread;
  const projectId = window.projectId;
  const el = id => document.getElementById(id);
  const base = `/api/projects/${projectId}`;

  const accountModal = bootstrap.Modal.getOrCreateInstance(el('testAccountModal'));
  const impersonationModal = bootstrap.Modal.getOrCreateInstance(el('impersonationModal'));
  const statuses = { available: ['success', t('Tersedia', 'Available')], none: ['secondary', t('Tidak ada', 'None')], unknown: ['light border', t('Belum diketahui', 'Unknown yet')] };
  const sources = { scan: t('deteksi dari kode', 'detected from the code'), tester: t('dilihat Tester saat testing', 'seen by the Tester while testing'), developer: (isEn ? 'set by a developer' : 'diisi developer') };
  let data = null;
  let editing = null;

  const badge = ([color, label]) => `<span class="badge text-bg-${color}">${escapeHtml(label)}</span>`;

  async function load() {
    try {
      data = await api.get(`${base}/test-accounts`);
      render();
    } catch (error) {
      el('testAccountRows').innerHTML = `<tr><td colspan="5" class="text-danger">${escapeHtml(error.message)}</td></tr>`;
    }
  }

  function render() {
    const disabled = !data.canSave;
    ['btnTestAccountAdd', 'btnSaveDevPassword', 'btnDetectImpersonation', 'btnEditImpersonation', 'devPassword'].forEach(id => { el(id).disabled = disabled; });
    el('testAccountCount').textContent = data.accounts.length ? (isEn ? `· ${data.accounts.length} account(s)` : `· ${data.accounts.length} akun`) : '';
    if (disabled) {
      el('testAccountRows').innerHTML = `<tr><td colspan="5" class="text-secondary">${escapeHtml(data.problem || t('Akun uji belum bisa disimpan.', 'Test accounts cannot be saved yet.'))}</td></tr>`;
    } else {
      el('testAccountRows').innerHTML = data.accounts.length
        ? data.accounts.map(rowHtml).join('')
        : t('<tr><td colspan="5" class="text-secondary">Belum ada akun uji. Tambahkan akun per peran (pengaju, atasan, approver per level) supaya Tester bisa mencoba alur approval.</td></tr>', '<tr><td colspan="5" class="text-secondary">No test account yet. Add accounts per role (requester, manager, approver per level) so the Tester can try approval flows.</td></tr>');
    }

    const settings = data.settings;
    el('devPassword').value = settings.devPasswordIsDefault ? '' : settings.devPassword;
    el('devPassword').placeholder = (isEn ? `empty = type anything (AIT uses ${settings.devPassword})` : `kosong = ketik apa saja (AIT memakai ${settings.devPassword})`);
    el('devPasswordHint').textContent = settings.devPasswordIsDefault
      ? (isEn ? `Now: default ${settings.devPassword}.` : `Sekarang: bawaan ${settings.devPassword}.`)
      : (isEn ? `Now: set ${settings.devPasswordSource === 'chat' ? 'through the chat' : 'by a developer'}${settings.updatedBy ? ` (${settings.updatedBy}` : ''}${settings.updatedAt ? `, ${fmt.relative(settings.updatedAt)})` : settings.updatedBy ? ')' : ''}.` : `Sekarang: diisi ${settings.devPasswordSource === 'chat' ? 'lewat chat' : 'developer'}${settings.updatedBy ? ` (${settings.updatedBy}` : ''}${settings.updatedAt ? `, ${fmt.relative(settings.updatedAt)})` : settings.updatedBy ? ')' : ''}.`);
    renderImpersonation(settings.impersonation);

    el('testAccountRoleList').innerHTML = [...new Set(data.accounts.map(a => a.role).filter(Boolean))].map(r => `<option value="${escapeHtml(r)}"></option>`).join('');
    el('testAccountKeyList').innerHTML = data.attributeKeys.map(k => `<option value="${escapeHtml(k)}"></option>`).join('');
  }

  function rowHtml(a) {
    const attributes = Object.entries(a.attributes || {})
      .map(([k, v]) => `<span class="badge text-bg-light border me-1 mb-1"><span class="text-secondary">${escapeHtml(k)}:</span> ${escapeHtml(v)}</span>`).join('');
    return (isEn ? `<tr>
      <td class="mono">${escapeHtml(a.username)}</td>
      <td>${escapeHtml(a.role || '–')}</td>
      <td>${attributes || '<span class="text-secondary">–</span>'}</td>
      <td class="small">${escapeHtml(a.notes || '')}<div class="text-secondary">${escapeHtml(a.updatedBy || a.createdBy || '')}</div></td>
      <td class="text-end text-nowrap">
        <button type="button" class="btn btn-sm btn-outline-secondary" data-edit="${a.id}">Edit</button>
        <button type="button" class="btn btn-sm btn-outline-danger" data-delete="${a.id}">Delete</button>
      </td>
    </tr>` : `<tr>
      <td class="mono">${escapeHtml(a.username)}</td>
      <td>${escapeHtml(a.role || '–')}</td>
      <td>${attributes || '<span class="text-secondary">–</span>'}</td>
      <td class="small">${escapeHtml(a.notes || '')}<div class="text-secondary">${escapeHtml(a.updatedBy || a.createdBy || '')}</div></td>
      <td class="text-end text-nowrap">
        <button type="button" class="btn btn-sm btn-outline-secondary" data-edit="${a.id}">Ubah</button>
        <button type="button" class="btn btn-sm btn-outline-danger" data-delete="${a.id}">Hapus</button>
      </td>
    </tr>`);
  }

  function renderImpersonation(info) {
    if (!info) {
      el('impersonationInfo').innerHTML = t('<span class="text-secondary">Belum dideteksi. Tekan Deteksi untuk mencari fitur login sebagai user lain di kode project, atau Ubah untuk mengisinya sendiri.</span>', '<span class="text-secondary">Not detected yet. Press Detect to search the project code for a log-in-as-another-user feature, or Edit to fill it in yourself.</span>');
      return;
    }
    const parts = [badge(statuses[info.status] || ['secondary', info.status])];
    parts.push(info.confirmed ? (isEn ? '<span class="badge text-bg-primary">Confirmed</span>' : '<span class="badge text-bg-primary">Dikonfirmasi</span>') : t('<span class="badge text-bg-warning">Belum dikonfirmasi</span>', '<span class="badge text-bg-warning">Not confirmed yet</span>'));
    let html = parts.join(' ');
    if (info.how) html += (isEn ? `<div class="mt-1"><span class="text-secondary">How:</span> ${escapeHtml(info.how)}</div>` : `<div class="mt-1"><span class="text-secondary">Cara:</span> ${escapeHtml(info.how)}</div>`);
    if (info.back) html += (isEn ? `<div><span class="text-secondary">Back:</span> ${escapeHtml(info.back)}</div>` : `<div><span class="text-secondary">Kembali:</span> ${escapeHtml(info.back)}</div>`);
    if (info.roles?.length) html += (isEn ? `<div><span class="text-secondary">Roles:</span> ${info.roles.map(r => `<span class="mono">${escapeHtml(r)}</span>`).join(', ')}</div>` : `<div><span class="text-secondary">Peran:</span> ${info.roles.map(r => `<span class="mono">${escapeHtml(r)}</span>`).join(', ')}</div>`);
    if (info.evidence?.length) html += (isEn ? `<div class="text-secondary text-break">Evidence: ${info.evidence.map(e => `<span class="mono">${escapeHtml(e)}</span>`).join(', ')}</div>` : `<div class="text-secondary text-break">Dasar: ${info.evidence.map(e => `<span class="mono">${escapeHtml(e)}</span>`).join(', ')}</div>`);
    html += `<div class="text-secondary">${escapeHtml(sources[info.source] || info.source)}${info.detectedBy ? ` · ${escapeHtml(info.detectedBy)}` : ''}${info.detectedAt ? ` · ${fmt.relative(info.detectedAt)}` : ''}</div>`;
    el('impersonationInfo').innerHTML = html;
  }

  function attributeRow(key = '', value = '') {
    const row = document.createElement('div');
    row.className = 'input-group input-group-sm mb-1';
    row.innerHTML = (isEn ? `<input type="text" class="form-control" data-key list="testAccountKeyList" maxlength="60" placeholder="Name, e.g. NIK" value="${escapeHtml(key)}" aria-label="Attribute name" />
      <input type="text" class="form-control" data-value maxlength="300" placeholder="Value" value="${escapeHtml(value)}" aria-label="Attribute value" />
      <button type="button" class="btn btn-outline-secondary" data-remove-attribute title="Delete attribute">✕</button>` : `<input type="text" class="form-control" data-key list="testAccountKeyList" maxlength="60" placeholder="Nama, mis. NIK" value="${escapeHtml(key)}" aria-label="Nama atribut" />
      <input type="text" class="form-control" data-value maxlength="300" placeholder="Nilai" value="${escapeHtml(value)}" aria-label="Nilai atribut" />
      <button type="button" class="btn btn-outline-secondary" data-remove-attribute title="Hapus atribut">✕</button>`);
    el('testAccountAttributes').appendChild(row);
  }

  function openAccount(account) {
    editing = account;
    el('testAccountModalTitle').textContent = account ? (isEn ? `Edit account ${account.username}` : `Ubah akun ${account.username}`) : t('Tambah akun uji', 'Add test account');
    el('testAccountUsername').value = account?.username || '';
    el('testAccountRole').value = account?.role || '';
    el('testAccountNotes').value = account?.notes || '';
    el('testAccountAttributes').innerHTML = '';
    const entries = Object.entries(account?.attributes || {});
    // A new account starts with the keys the project already uses, so the columns stay the same across accounts.
    if (entries.length) entries.forEach(([k, v]) => attributeRow(k, v));
    else (data.attributeKeys.length ? data.attributeKeys.slice(0, 6) : ['']).forEach(k => attributeRow(k, ''));
    accountModal.show();
  }

  el('btnTestAccountAdd').addEventListener('click', () => openAccount(null));
  el('btnTestAccountAttribute').addEventListener('click', () => attributeRow());
  el('testAccountAttributes').addEventListener('click', event => {
    if (event.target.closest('[data-remove-attribute]')) event.target.closest('.input-group').remove();
  });

  el('testAccountForm').addEventListener('submit', async event => {
    event.preventDefault();
    const attributes = {};
    el('testAccountAttributes').querySelectorAll('.input-group').forEach(row => {
      const key = row.querySelector('[data-key]').value.trim();
      const value = row.querySelector('[data-value]').value.trim();
      if (key && value) attributes[key] = value;
    });
    const body = {
      username: el('testAccountUsername').value.trim(),
      role: el('testAccountRole').value.trim() || null,
      attributes,
      notes: el('testAccountNotes').value.trim() || null
    };
    try {
      if (editing) await api.put(`${base}/test-accounts/${editing.id}`, body);
      else await api.post(`${base}/test-accounts`, body);
      toast(editing ? (isEn ? `Account ${body.username} saved.` : `Akun ${body.username} disimpan.`) : (isEn ? `Account ${body.username} added.` : `Akun ${body.username} ditambahkan.`));
      accountModal.hide();
      await load();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('testAccountRows').addEventListener('click', async event => {
    const edit = event.target.closest('[data-edit]');
    const remove = event.target.closest('[data-delete]');
    if (edit) openAccount(data.accounts.find(a => a.id === edit.dataset.edit));
    if (remove) {
      const account = data.accounts.find(a => a.id === remove.dataset.delete);
      if (!confirm((isEn ? `Delete test account ${account.username}? It disappears for every developer of the project.` : `Hapus akun uji ${account.username}? Akun ini hilang untuk semua developer project.`))) return;
      try {
        await api.del(`${base}/test-accounts/${account.id}`);
        toast((isEn ? `Account ${account.username} deleted.` : `Akun ${account.username} dihapus.`));
        await load();
      } catch (error) {
        toast(error.message, 'error');
      }
    }
  });

  el('btnSaveDevPassword').addEventListener('click', async () => {
    try {
      await api.put(`${base}/testing-settings`, { devPassword: el('devPassword').value.trim() || null });
      toast(el('devPassword').value.trim() ? t('Password dev disimpan.', 'Dev password saved.') : t('Password dev kembali ke bawaan.', 'Dev password back to the default.'));
      await load();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('btnDetectImpersonation').addEventListener('click', async event => {
    const button = event.currentTarget;
    button.disabled = true;
    toast(t('Mencari fitur login as di kode project… (model murah dipakai bila ada temuan)', 'Searching the project code for a login-as feature… (a cheap model is used when something is found)'), 'info');
    try {
      const settings = await api.post(`${base}/testing-settings/impersonation/detect`);
      const info = settings.impersonation;
      toast(info?.status === 'available' ? t('Login as ditemukan; cek dan konfirmasi caranya.', 'Login as found; check and confirm how it works.') : t('Fitur login as tidak ditemukan di kode.', 'No login-as feature found in the code.'));
      await load();
    } catch (error) {
      toast(error.message, 'error');
    } finally {
      button.disabled = !data?.canSave;
    }
  });

  el('btnEditImpersonation').addEventListener('click', () => {
    const info = data.settings.impersonation;
    el('impersonationStatus').value = info?.status || 'available';
    el('impersonationHow').value = info?.how || '';
    el('impersonationBack').value = info?.back || '';
    el('impersonationRoles').value = (info?.roles || []).join(', ');
    el('impersonationConfirmed').checked = true;
    impersonationModal.show();
  });

  el('impersonationForm').addEventListener('submit', async event => {
    event.preventDefault();
    try {
      await api.put(`${base}/testing-settings/impersonation`, {
        status: el('impersonationStatus').value,
        how: el('impersonationHow').value.trim() || null,
        back: el('impersonationBack').value.trim() || null,
        roles: el('impersonationRoles').value.split(',').map(r => r.trim()).filter(Boolean),
        confirmed: el('impersonationConfirmed').checked
      });
      toast(t('Login as disimpan.', 'Login as saved.'));
      impersonationModal.hide();
      await load();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  load();
})();
