// Project > Detail → Database: the project's databases (kept on this machine, password encrypted), a connection test,
// and the backups AIT made before each change_database, each of which can restore its table.
(() => {
  'use strict';
  const { isEn, t, api, toast, escapeHtml, fmt } = window.AIThread;
  const projectId = window.projectId;
  const el = id => document.getElementById(id);
  const base = `/api/projects/${projectId}/databases`;
  const modal = bootstrap.Modal.getOrCreateInstance(el('databaseModal'));
  const providers = { SqlServer: 'SQL Server', PostgreSql: 'PostgreSQL', MySql: 'MySQL', Oracle: 'Oracle', Sqlite: 'SQLite' };
  const ports = { SqlServer: 1433, PostgreSql: 5432, MySql: 3306, Oracle: 1521 };
  const envColors = { Dev: 'secondary', Staging: 'info', Prod: 'danger' };
  let databases = [];
  let backups = [];
  let editing = null;

  async function load() {
    try {
      [databases, backups] = await Promise.all([api.get(base), api.get(`${base}/backups`)]);
      render();
    } catch (error) {
      el('databaseRows').innerHTML = `<tr><td colspan="6" class="text-danger">${escapeHtml(error.message)}</td></tr>`;
    }
  }

  function render() {
    el('databaseCount').textContent = databases.length ? `· ${databases.length}` : '';
    el('databaseRows').innerHTML = databases.length
      ? databases.map(d => `<tr>
          <td class="mono">${escapeHtml(d.name)}${d.description ? `<div class="small text-secondary">${escapeHtml(d.description)}</div>` : ''}</td>
          <td class="small mono">${escapeHtml(d.display)}</td>
          <td><span class="badge text-bg-${envColors[d.environment] || 'secondary'}">${escapeHtml(d.environment)}</span></td>
          <td class="small">${d.allowWrites ? (d.environment === 'Prod' ? t('baca + ubah (selalu ditanya, latihan disarankan)', 'read + change (always asked, rehearsal suggested)') : t('baca + ubah dengan izin', 'read + change with permission')) : t('baca saja', 'read only')}</td>
          <td class="small mono">${escapeHtml(d.maskedColumns || t('otomatis', 'automatic'))}</td>
          <td class="text-end text-nowrap">
            <button type="button" class="btn btn-sm btn-outline-secondary" data-edit="${d.id}">${t('Ubah', 'Edit')}</button>
            <button type="button" class="btn btn-sm btn-outline-danger" data-delete="${d.id}">${t('Hapus', 'Delete')}</button>
          </td>
        </tr>`).join('')
      : `<tr><td colspan="6" class="text-secondary">${t('Belum ada database. Tambahkan database dev/staging/prod project ini supaya planner dan Tester bisa membaca datanya.', 'No database yet. Add the dev/staging/prod databases of this project so the planner and the Tester can read their data.')}</td></tr>`;

    el('databaseBackupCount').textContent = backups.length ? `· ${backups.length}` : '';
    el('databaseBackupRows').innerHTML = backups.length
      ? backups.map(b => {
          const status = b.restoredAt
            ? `<span class="badge text-bg-success">${t('dipulihkan', 'restored')}</span> <span class="small text-secondary">${fmt.relative(b.restoredAt)}${b.restoredBy ? ` · ${escapeHtml(b.restoredBy)}` : ''}</span>`
            : b.executedAt
              ? `<span class="badge text-bg-warning">${isEn ? `executed · ${b.affectedRows} rows` : `dieksekusi · ${b.affectedRows} baris`}</span>`
              : `<span class="badge text-bg-light border">${t('tidak dieksekusi', 'not executed')}</span>`;
          const canRestore = b.fileExists && b.executedAt;
          return `<tr>
            <td class="small text-nowrap">${fmt.dateTime(b.createdAt)}${b.threadId ? `<div><a class="small" href="/Thread/Detail/${b.threadId}">thread</a></div>` : ''}</td>
            <td class="small"><span class="mono">${escapeHtml(b.databaseName)}</span> <span class="badge text-bg-${envColors[b.environment] || 'secondary'}">${escapeHtml(b.environment)}</span></td>
            <td class="small mono">${escapeHtml(b.table)}<div class="text-secondary">${isEn ? `${b.rows} rows · ${Math.ceil(b.bytes / 1024)} KB` : `${b.rows} baris · ${Math.ceil(b.bytes / 1024)} KB`}</div></td>
            <td class="small mono text-break" style="max-width: 28rem">${escapeHtml(b.statement)}</td>
            <td class="small">${status}${b.fileExists ? '' : `<div class="text-danger">${t('file backup hilang', 'backup file missing')}</div>`}</td>
            <td class="text-end"><button type="button" class="btn btn-sm btn-outline-warning" data-restore="${b.id}" ${canRestore ? '' : 'disabled'}>${t('Pulihkan', 'Restore')}</button></td>
          </tr>`;
        }).join('')
      : `<tr><td colspan="6" class="text-secondary">${t('Belum ada backup. AIT membuatnya otomatis sebelum setiap change_database.', 'No backup yet. AIT makes one before every change_database.')}</td></tr>`;
  }

  function providerChanged() {
    const provider = el('databaseProvider').value;
    const sqlite = provider === 'Sqlite';
    document.querySelectorAll('#databaseModal .database-server').forEach(e => e.classList.toggle('d-none', sqlite));
    el('databaseDatabaseLabel').textContent = sqlite ? t('Path file SQLite', 'SQLite file path') : 'Database';
    el('databaseDatabase').placeholder = sqlite ? 'C:\\data\\app.db' : (provider === 'Oracle' ? 'ORCLPDB1' : 'app_dev');
    el('databasePort').placeholder = ports[provider] || '';
    el('databaseUsernameHint').textContent = provider === 'SqlServer' ? t('Kosong = Windows authentication.', 'Empty = Windows authentication.') : '';
  }

  function open(database) {
    editing = database;
    el('databaseModalTitle').textContent = database ? (isEn ? `Database ${database.name}` : `Database ${database.name}`) : t('Tambah database', 'Add database');
    el('databaseName').value = database?.name || '';
    el('databaseProvider').value = database?.provider || 'SqlServer';
    el('databaseEnvironment').value = database?.environment || 'Dev';
    el('databaseHost').value = database?.host || '';
    el('databasePort').value = database?.port || '';
    el('databaseDatabase').value = database?.database || '';
    el('databaseUsername').value = database?.username || '';
    el('databasePassword').value = '';
    el('databasePasswordHint').textContent = database?.hasPassword ? t('Tersimpan. Kosongkan untuk tetap memakai password lama.', 'Stored. Leave empty to keep the stored password.') : '';
    el('databaseOptions').value = database?.options || '';
    el('databaseMasked').value = database?.maskedColumns || '';
    el('databaseMaxRows').value = database?.maxRows || 50;
    el('databaseDescription').value = database?.description || '';
    el('databaseAllowWrites').checked = database ? database.allowWrites : true;
    el('databaseTestResult').innerHTML = '';
    providerChanged();
    modal.show();
  }

  function body() {
    const password = el('databasePassword').value;
    return {
      name: el('databaseName').value.trim(),
      provider: el('databaseProvider').value,
      host: el('databaseHost').value.trim() || null,
      port: el('databasePort').value ? Number(el('databasePort').value) : null,
      database: el('databaseDatabase').value.trim(),
      username: el('databaseUsername').value.trim() || null,
      // Empty keeps the stored password of a saved database.
      password: password.length ? password : (editing ? null : ''),
      options: el('databaseOptions').value.trim() || null,
      environment: el('databaseEnvironment').value,
      maskedColumns: el('databaseMasked').value.trim() || null,
      maxRows: Number(el('databaseMaxRows').value) || 50,
      allowWrites: el('databaseAllowWrites').checked,
      description: el('databaseDescription').value.trim() || null
    };
  }

  el('databaseProvider').addEventListener('change', providerChanged);
  el('btnDatabaseAdd').addEventListener('click', () => open(null));

  el('btnDatabaseTest').addEventListener('click', async event => {
    const button = event.currentTarget;
    button.disabled = true;
    el('databaseTestResult').innerHTML = `<span class="text-secondary">${t('Menguji…', 'Testing…')}</span>`;
    try {
      const result = await api.post(`${base}/test`, { id: editing?.id || null, database: body() });
      el('databaseTestResult').innerHTML = `<span class="text-${result.success ? 'success' : 'danger'}">${result.success ? '✅' : '⛔'} ${escapeHtml(result.message)}</span>`
        + (result.serverVersion ? ` <span class="text-secondary">· ${escapeHtml(result.serverVersion)}</span>` : '');
    } catch (error) {
      el('databaseTestResult').innerHTML = `<span class="text-danger">${escapeHtml(error.message)}</span>`;
    } finally {
      button.disabled = false;
    }
  });

  el('databaseForm').addEventListener('submit', async event => {
    event.preventDefault();
    el('btnDatabaseSave').disabled = true;
    try {
      if (editing) await api.put(`${base}/${editing.id}`, body());
      else await api.post(base, body());
      modal.hide();
      toast(t('Database disimpan.', 'Database saved.'), 'success');
      await load();
    } catch (error) {
      toast(error.message, 'error');
    } finally {
      el('btnDatabaseSave').disabled = false;
    }
  });

  el('databaseRows').addEventListener('click', async event => {
    const edit = event.target.closest('[data-edit]');
    if (edit) return open(databases.find(d => d.id === edit.dataset.edit));
    const remove = event.target.closest('[data-delete]');
    if (!remove) return;
    const database = databases.find(d => d.id === remove.dataset.delete);
    if (!confirm(isEn ? `Delete database ${database.name}? Its backups on this machine are deleted too; the database itself is not touched.` : `Hapus database ${database.name}? Backup-nya di mesin ini ikut dihapus; database-nya sendiri tidak disentuh.`)) return;
    try {
      await api.del(`${base}/${database.id}`);
      toast(t('Database dihapus.', 'Database deleted.'), 'success');
      await load();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('databaseBackupRows').addEventListener('click', async event => {
    const button = event.target.closest('[data-restore]');
    if (!button) return;
    const backup = backups.find(b => b.id === button.dataset.restore);
    const warning = isEn
      ? `Restore table ${backup.table} on ${backup.databaseName} (${backup.environment}) to the backup of ${fmt.dateTime(backup.createdAt)}?\n\nRows changed, added or deleted since then — by AIT or by anyone else — go back to how they were. It commits only when the table then matches the backup exactly.`
      : `Pulihkan tabel ${backup.table} di ${backup.databaseName} (${backup.environment}) ke backup ${fmt.dateTime(backup.createdAt)}?\n\nBaris yang diubah, ditambah, atau dihapus sejak saat itu — oleh AIT maupun orang lain — dikembalikan seperti semula. Baru di-commit bila tabel lalu sama persis dengan backup.`;
    if (!confirm(warning)) return;
    if (backup.environment === 'Prod' && !confirm(t('Ini database PROD. Yakin memulihkan sekarang?', 'This is a PROD database. Restore it now?'))) return;
    button.disabled = true;
    try {
      const result = await api.post(`${base}/backups/${backup.id}/restore`);
      toast(result.message, 'success');
      await load();
    } catch (error) {
      toast(error.message, 'error');
      button.disabled = false;
    }
  });

  load();
})();
