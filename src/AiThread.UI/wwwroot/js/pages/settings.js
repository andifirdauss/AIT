(() => {
  'use strict';
  const { api, toast, escapeHtml, fmt, stream, debounce, renderRouterPill, routerKeyState } = window.AiThread;
  const el = id => document.getElementById(id);
  let settings = null;

  function fill(s) {
    settings = s;
    el('routerUrl').value = s.nineRouterBaseUrl;
    el('routerKey').value = '';
    el('routerKeyClear').checked = false;
    el('routerKeyHint').textContent = s.hasNineRouterApiKey
      ? `Tersimpan terenkripsi: ${s.nineRouterApiKeyMasked}`
      : 'Belum ada key. Salin dari dashboard 9router (menu API Keys) bila "Require API key" aktif.';
    el('proxyKey').value = s.proxyApiKey || '';
    el('orchestrationEnabled').checked = s.orchestrationEnabled;
    el('streamProgress').checked = s.streamProgressToClient;
    el('plannerCanAsk').checked = s.plannerCanAsk;
    el('plannerMaxSteps').value = s.plannerMaxSteps;
    el('executorMaxRetries').value = s.executorMaxRetries;
    el('maxParallelTasks').value = s.maxParallelTasks;
    el('requestTimeout').value = s.requestTimeoutSeconds;
    el('buildEnabled').checked = s.buildEnabled;
    el('buildTimeout').value = s.buildTimeoutSeconds;
    el('maxFixRounds').value = s.maxFixRounds;
  }

  async function loadModels(selected) {
    const select = el('passthroughModel');
    let html = '<option value="">Otomatis — model node Wrapper</option>';
    try {
      const models = await api.get('/api/router/models');
      if (selected && !models.some(m => m.id === selected)) html += `<option value="${escapeHtml(selected)}">${escapeHtml(selected)}</option>`;
      html += models.map(m => `<option value="${escapeHtml(m.id)}">${m.isCombo ? '[combo] ' : ''}${escapeHtml(m.id)}</option>`).join('');
    } catch {
      if (selected) html += `<option value="${escapeHtml(selected)}">${escapeHtml(selected)}</option>`;
    }
    select.innerHTML = html;
    select.value = selected || '';
  }

  async function load() {
    try {
      const s = await api.get('/api/settings');
      fill(s);
      await Promise.all([loadModels(s.passthroughModel), showStoredStatus()]);
    } catch (error) {
      toast(error.message, 'error');
    }
  }

  // Status of the saved configuration, so a missing or rejected key is visible without pressing Test.
  async function showStoredStatus() {
    try {
      el('routerTestResult').innerHTML = routerResultHtml(await api.get('/api/router/status?refresh=true'), false);
    } catch {
      // Test koneksi remains available.
    }
  }

  function routerResultHtml(r, keyTypedButNotSaved) {
    if (!r.isOnline) return `<span class="text-danger">✕ Offline — ${escapeHtml(r.error || '')}</span>`;
    const [keyText, keyProblem] = routerKeyState(r);
    const key = keyProblem ? `<span class="text-danger">${keyText}</span>` : keyText;
    const unsaved = keyTypedButNotSaved && !keyProblem
      ? ' · <span class="text-warning">key ini belum disimpan — klik Simpan setting</span>'
      : '';
    return `<span class="text-success">✓ Online</span> · ${r.latencyMs} ms · ${r.modelCount} model · ${r.comboCount} combo · ${key}${unsaved}`;
  }

  el('btnTestRouter').addEventListener('click', async () => {
    el('routerTestResult').innerHTML = '<span class="text-secondary">Menguji…</span>';
    const typedKey = el('routerKey').value.trim() || null;
    try {
      const r = await api.post('/api/settings/test-router', { baseUrl: el('routerUrl').value.trim(), apiKey: typedKey });
      el('routerTestResult').innerHTML = routerResultHtml(r, typedKey !== null);
    } catch (error) {
      el('routerTestResult').innerHTML = `<span class="text-danger">${escapeHtml(error.message)}</span>`;
    }
  });

  el('btnGenerateKey').addEventListener('click', () => {
    const bytes = new Uint8Array(24);
    crypto.getRandomValues(bytes);
    el('proxyKey').value = 'ait-' + Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
    toast('Key baru dibuat. Simpan setting, lalu matikan dan aktifkan lagi CLI di menu CLI agar key baru tertulis.', 'info');
  });

  // --- Tabs ---

  const tabButtons = Array.from(document.querySelectorAll('#settingTabs [data-bs-toggle="tab"]'));

  function showTab(name) {
    const button = tabButtons.find(b => b.dataset.tab === name);
    if (button) bootstrap.Tab.getOrCreateInstance(button).show();
  }

  // The hash keeps the open tab across reloads and lets other pages link to e.g. /Setting#telegram.
  tabButtons.forEach(button => button.addEventListener('shown.bs.tab', () => {
    history.replaceState(null, '', `#${button.dataset.tab}`);
  }));
  showTab(location.hash.slice(1));

  // The form is novalidate because the browser cannot report an invalid field inside a hidden tab;
  // open that field's tab first, then let the browser show its message.
  function revealInvalidField(form) {
    const invalid = Array.from(form.elements).find(f => f.willValidate && !f.checkValidity());
    if (!invalid) return false;
    const pane = invalid.closest('.tab-pane');
    if (pane) showTab(tabButtons.find(b => b.dataset.bsTarget === `#${pane.id}`)?.dataset.tab);
    invalid.reportValidity();
    return true;
  }

  el('settingForm').addEventListener('submit', async event => {
    event.preventDefault();
    if (revealInvalidField(event.currentTarget)) return;
    const body = {
      nineRouterBaseUrl: el('routerUrl').value.trim(),
      nineRouterApiKey: el('routerKey').value.trim() || null,
      clearNineRouterApiKey: el('routerKeyClear').checked,
      proxyApiKey: el('proxyKey').value.trim() || null,
      passthroughModel: el('passthroughModel').value || null,
      orchestrationEnabled: el('orchestrationEnabled').checked,
      streamProgressToClient: el('streamProgress').checked,
      plannerCanAsk: el('plannerCanAsk').checked,
      plannerMaxSteps: Number(el('plannerMaxSteps').value),
      executorMaxRetries: Number(el('executorMaxRetries').value),
      maxParallelTasks: Number(el('maxParallelTasks').value),
      requestTimeoutSeconds: Number(el('requestTimeout').value),
      buildEnabled: el('buildEnabled').checked,
      buildTimeoutSeconds: Number(el('buildTimeout').value),
      maxFixRounds: Number(el('maxFixRounds').value)
    };
    try {
      fill(await api.put('/api/settings', body));
      toast('Setting tersimpan.');
      try {
        // The save already re-checked 9router with the stored key; show that instead of the stale test result.
        const status = await api.get('/api/router/status');
        renderRouterPill(status);
        el('routerTestResult').innerHTML = routerResultHtml(status, false);
      } catch {
        el('routerTestResult').innerHTML = '';
      }
      await loadModels(settings.passthroughModel);
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  // --- Penyimpanan memory ---

  const memoryPrefixes = ['stm', 'ltm', 'bzm'];
  const memoryLevels = { stm: 'Short', ltm: 'Long', bzm: 'Business' };
  const sqlFields = ['server', 'database', 'credentials', 'options'];
  const memoryProviders = {
    Default: {
      fields: [],
      help: {
        stm: 'Server Garnet (protokol Redis) yang berjalan di dalam AiThread — tanpa instalasi apa pun.',
        ltm: 'Tabel LongMemories di database utama AiThread (ConnectionStrings:Default) — hanya di komputer ini.',
        bzm: 'Tabel BusinessPages di database utama AiThread — hanya di komputer ini. Pilih database bersama agar wiki dibangun dari semua developer.'
      }
    },
    Redis: {
      fields: sqlFields, port: 6379, database: ['Database (indeks, opsional)', '0'], options: 'ssl=true',
      help: 'Redis, Valkey, atau server berprotokol Redis lain. Username hanya untuk ACL Redis 6+; kosongkan bila tidak dipakai.'
    },
    SqlServer: {
      fields: sqlFields, port: 1433, database: ['Database', 'AiThreadMemory'], options: 'Encrypt=True',
      help: 'Username kosong = Windows authentication. Port kosong = port default; untuk named instance tulis host\\INSTANCE dan kosongkan port.'
    },
    PostgreSql: {
      fields: sqlFields, port: 5432, database: ['Database', 'aithread'], options: 'SSL Mode=Require',
      help: 'Database dibuat otomatis bila belum ada dan user punya hak CREATEDB.'
    },
    MySql: {
      fields: sqlFields, port: 3306, database: ['Database', 'aithread'], options: 'SslMode=Required',
      help: 'MySQL 8+. Database dibuat otomatis bila belum ada dan user punya hak CREATE.'
    },
    Oracle: {
      fields: sqlFields, port: 1521, database: ['Service name', 'FREEPDB1'], options: 'Pooling=false',
      help: 'Easy Connect host:port/service. Tabel dibuat di schema milik user yang login.'
    },
    Sqlite: {
      fields: ['database'], database: ['File database', '%LOCALAPPDATA%\\AiThread\\memory.db'],
      help: 'File lokal, dibuat otomatis. Kosongkan untuk lokasi default. Cocok bila AiThread hanya dipakai di satu komputer.'
    }
  };
  let memoryStorage = null;

  const memEl = (prefix, name) => el(prefix + name);

  function applyMemoryProvider(prefix) {
    const provider = memEl(prefix, 'Provider').value;
    const info = memoryProviders[provider] || memoryProviders.Default;
    document.querySelectorAll(`.memory-store[data-prefix="${prefix}"] [data-field]`).forEach(field => {
      field.hidden = !info.fields.includes(field.dataset.field);
    });
    memEl(prefix, 'Help').textContent = typeof info.help === 'string' ? info.help : info.help[prefix];
    memEl(prefix, 'Port').placeholder = info.port ? String(info.port) : '';
    if (info.database) {
      memEl(prefix, 'DatabaseLabel').textContent = info.database[0];
      memEl(prefix, 'Database').placeholder = info.database[1];
    }
    memEl(prefix, 'Options').placeholder = info.options || '';
    memEl(prefix, 'OptionsHint').textContent = provider === 'Redis'
      ? 'Opsi StackExchange.Redis, dipisah koma.'
      : 'Keyword connection string, dipisah titik koma. Menimpa isian di atas.';
    memEl(prefix, 'TestResult').innerHTML = '';
  }

  function memoryStatusHtml(status) {
    return status.connected
      ? `<span class="badge text-bg-success">Terhubung</span> <span class="text-secondary">${escapeHtml(status.description)}</span>`
      : `<span class="badge text-bg-danger">Tidak terhubung</span> <span class="text-secondary">${escapeHtml(status.description)}</span>`;
  }

  function fillMemoryStorage(m) {
    memoryStorage = m;
    const stores = { stm: [m.shortMemory, m.shortStatus], ltm: [m.longMemory, m.longStatus], bzm: [m.businessMemory, m.businessStatus] };
    for (const prefix of memoryPrefixes) {
      const [store, status] = stores[prefix];
      memEl(prefix, 'Provider').value = store.provider;
      memEl(prefix, 'Host').value = store.host || '';
      memEl(prefix, 'Port').value = store.port ?? '';
      memEl(prefix, 'Database').value = store.database || '';
      memEl(prefix, 'Username').value = store.username || '';
      memEl(prefix, 'Password').value = '';
      memEl(prefix, 'PasswordClear').checked = false;
      memEl(prefix, 'PasswordHint').textContent = store.hasPassword ? 'Password tersimpan terenkripsi.' : 'Belum ada password tersimpan.';
      memEl(prefix, 'Options').value = store.options || '';
      memEl(prefix, 'Status').innerHTML = memoryStatusHtml(status);
      applyMemoryProvider(prefix);
    }
  }

  function memoryStoreBody(prefix) {
    const port = memEl(prefix, 'Port').value.trim();
    return {
      provider: memEl(prefix, 'Provider').value,
      host: memEl(prefix, 'Host').value.trim() || null,
      port: port ? Number(port) : null,
      database: memEl(prefix, 'Database').value.trim() || null,
      username: memEl(prefix, 'Username').value.trim() || null,
      password: memEl(prefix, 'Password').value || null,
      clearPassword: memEl(prefix, 'PasswordClear').checked,
      options: memEl(prefix, 'Options').value.trim() || null
    };
  }

  async function loadMemoryStorage() {
    try {
      fillMemoryStorage(await api.get('/api/settings/memory-storage'));
    } catch (error) {
      for (const prefix of memoryPrefixes) memEl(prefix, 'Status').innerHTML = `<span class="text-danger">${escapeHtml(error.message)}</span>`;
    }
  }

  memoryPrefixes.forEach(prefix => memEl(prefix, 'Provider').addEventListener('change', () => applyMemoryProvider(prefix)));

  document.querySelectorAll('[data-memory-test]').forEach(button => button.addEventListener('click', async () => {
    const prefix = button.dataset.memoryTest;
    const result = memEl(prefix, 'TestResult');
    result.innerHTML = '<span class="text-secondary">Menguji…</span>';
    button.disabled = true;
    try {
      const r = await api.post('/api/settings/memory-storage/test', {
        level: memoryLevels[prefix],
        store: memoryStoreBody(prefix)
      });
      result.innerHTML = r.success
        ? `<span class="text-success">✓ ${escapeHtml(r.message)}</span> <span class="text-secondary">· ${r.latencyMs} ms</span>`
        : `<span class="text-danger">✕ ${escapeHtml(r.message)}</span>`;
    } catch (error) {
      result.innerHTML = `<span class="text-danger">${escapeHtml(error.message)}</span>`;
    } finally {
      button.disabled = false;
    }
  }));

  el('memoryStorageForm').addEventListener('submit', async event => {
    event.preventDefault();
    const body = {
      shortMemory: memoryStoreBody('stm'),
      longMemory: memoryStoreBody('ltm'),
      businessMemory: memoryStoreBody('bzm'),
      copyExistingData: el('memoryCopyExisting').checked
    };
    const button = el('btnSaveMemoryStorage');
    button.disabled = true;
    button.textContent = 'Menguji & menyimpan…';
    try {
      const result = await api.put('/api/settings/memory-storage', body);
      fillMemoryStorage(result.settings);
      const copied = result.copiedShort + result.copiedLong + result.copiedBusiness > 0
        ? ` ${result.copiedShort} short, ${result.copiedLong} long memory & ${result.copiedBusiness} halaman bisnis disalin.`
        : '';
      toast(`Penyimpanan memory tersimpan.${copied}`);
      result.warnings.forEach(warning => toast(warning, 'warning'));
    } catch (error) {
      toast(error.message, 'error');
    } finally {
      button.disabled = false;
      button.textContent = 'Simpan penyimpanan memory';
    }
  });

  // --- Business memory ---

  async function loadBusinessModels(selected) {
    const select = el('bizModel');
    let html = '<option value="">Otomatis — model node Planner</option>';
    try {
      const models = await api.get('/api/router/models');
      if (selected && !models.some(m => m.id === selected)) html += `<option value="${escapeHtml(selected)}">${escapeHtml(selected)}</option>`;
      html += models.map(m => `<option value="${escapeHtml(m.id)}">${m.isCombo ? '[combo] ' : ''}${escapeHtml(m.id)}</option>`).join('');
    } catch {
      if (selected) html += `<option value="${escapeHtml(selected)}">${escapeHtml(selected)}</option>`;
    }
    select.innerHTML = html;
    select.value = selected || '';
  }

  async function fillBusinessSettings(s) {
    el('bizEnabled').checked = s.enabled;
    el('bizInterval').value = s.intervalHours;
    el('bizDeveloper').value = s.developerName || '';
    el('bizDeveloper').placeholder = s.gitUserName || s.effectiveDeveloperName || '';
    await loadBusinessModels(s.model);
  }

  // Saved from the first-run popup while this page is open.
  document.addEventListener('aithread:developer-name', event => { el('bizDeveloper').value = event.detail.developerName || ''; });

  async function loadBusinessSettings() {
    try {
      await fillBusinessSettings(await api.get('/api/business/settings'));
    } catch (error) {
      toast(error.message, 'error');
    }
  }

  el('businessSettingsForm').addEventListener('submit', async event => {
    event.preventDefault();
    const body = {
      enabled: el('bizEnabled').checked,
      intervalHours: Number(el('bizInterval').value),
      model: el('bizModel').value || null,
      developerName: el('bizDeveloper').value.trim() || null
    };
    try {
      await fillBusinessSettings(await api.put('/api/business/settings', body));
      toast('Setting business memory tersimpan.');
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  // --- Telegram ---

  const telegramStates = {
    Disabled: ['secondary', 'Nonaktif'],
    Starting: ['info', 'Menghubungkan…'],
    Running: ['success', 'Aktif'],
    Error: ['danger', 'Bermasalah']
  };

  function renderTelegram(t, keepForm) {
    if (!keepForm) {
      el('telegramEnabled').checked = t.enabled;
      el('telegramToken').value = '';
      el('telegramTokenClear').checked = false;
    }
    el('telegramTokenHint').textContent = t.hasBotToken
      ? `Tersimpan terenkripsi: ${t.botTokenMasked}`
      : 'Belum ada token. Buat bot lewat @BotFather (/newbot) lalu tempel tokennya di sini.';

    const [color, label] = telegramStates[t.state] || ['secondary', t.state];
    const bot = t.botUsername
      ? ` <a class="mono" href="https://t.me/${encodeURIComponent(t.botUsername)}" target="_blank" rel="noopener">@${escapeHtml(t.botUsername)}</a>`
      : '';
    const polled = t.state === 'Running' && t.lastPollAt ? ` <span class="text-secondary">· cek terakhir ${fmt.relative(t.lastPollAt)}</span>` : '';
    const error = t.error ? `<div class="text-danger mt-1">${escapeHtml(t.error)}</div>` : '';
    el('telegramState').innerHTML = `<span class="badge text-bg-${color}">${label}</span>${bot}${polled}${error}`;

    const start = t.botUsername ? `kirim <span class="mono">/start</span> ke @${escapeHtml(t.botUsername)}` : 'aktifkan bot lalu kirim <span class="mono">/start</span> ke bot Anda';
    el('telegramChatRows').innerHTML = t.chats.length
      ? t.chats.map(telegramChatRow).join('')
      : `<tr><td colspan="5" class="text-secondary small">Belum ada chat — ${start} dari Telegram.</td></tr>`;
  }

  function telegramChatRow(c) {
    const name = `${escapeHtml(c.displayName)}${c.username ? ` <span class="text-secondary">@${escapeHtml(c.username)}</span>` : ''}
      <div class="small text-secondary mono">ID ${escapeHtml(c.chatId)}</div>`;
    const status = c.isAllowed
      ? '<span class="badge text-bg-success">Diizinkan</span>'
      : '<span class="badge text-bg-warning">Menunggu izin</span>';
    const actions = c.isAllowed
      ? `<button type="button" class="btn btn-outline-danger btn-sm" data-tg-revoke="${c.id}">Cabut</button>`
      : `<button type="button" class="btn btn-primary btn-sm me-1" data-tg-allow="${c.id}">Izinkan</button>
         <button type="button" class="btn btn-outline-secondary btn-sm" data-tg-delete="${c.id}">Hapus</button>`;
    return `<tr>
      <td>${name}</td>
      <td>${status}</td>
      <td>${escapeHtml(c.activeProjectName || '–')}</td>
      <td class="text-nowrap small">${fmt.relative(c.lastMessageAt)}</td>
      <td class="text-end text-nowrap">${actions}</td>
    </tr>`;
  }

  // keepForm: a live refresh must not wipe a token the user is typing.
  async function loadTelegram(keepForm) {
    try {
      renderTelegram(await api.get('/api/telegram'), keepForm);
    } catch (error) {
      el('telegramState').innerHTML = `<span class="text-danger">${escapeHtml(error.message)}</span>`;
    }
  }

  el('telegramForm').addEventListener('submit', async event => {
    event.preventDefault();
    const body = {
      enabled: el('telegramEnabled').checked,
      botToken: el('telegramToken').value.trim() || null,
      clearBotToken: el('telegramTokenClear').checked
    };
    try {
      renderTelegram(await api.put('/api/telegram', body));
      toast(body.enabled ? 'Setting Telegram tersimpan. Bot sedang dihubungkan…' : 'Bot Telegram dinonaktifkan.');
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('telegramChatRows').addEventListener('click', async event => {
    const button = event.target.closest('button[data-tg-allow], button[data-tg-delete], button[data-tg-revoke]');
    if (!button) return;
    const { tgAllow, tgDelete, tgRevoke } = button.dataset;
    if (tgRevoke && !confirm('Cabut akses chat ini? Chat tidak bisa lagi mengirim prompt ke AiThread.')) return;
    button.disabled = true;
    try {
      if (tgAllow) {
        await api.post(`/api/telegram/chats/${tgAllow}/allow`);
        toast('Chat diizinkan. Bot mengirim pesan konfirmasi ke chat tersebut.');
      } else {
        await api.del(`/api/telegram/chats/${tgDelete || tgRevoke}`);
        toast(tgRevoke ? 'Akses chat dicabut.' : 'Chat dihapus.', 'info');
      }
      await loadTelegram(true);
    } catch (error) {
      button.disabled = false;
      toast(error.message, 'error');
    }
  });

  // --- Skill ---

  const skillStates = {
    Queued: ['secondary', 'Antre'],
    Fetching: ['info', 'Mengunduh…'],
    Analyzing: ['info', 'Dianalisis AI…'],
    Ready: ['success', 'Siap'],
    Failed: ['danger', 'Gagal']
  };
  const skillKinds = { GitHub: 'GitHub', GitLab: 'GitLab', Git: 'git', RawFile: 'file', Web: 'web' };
  const skillModal = bootstrap.Modal.getOrCreateInstance(el('skillModal'));

  async function loadSkillModels(selected) {
    const select = el('skillModel');
    let html = '<option value="">Otomatis — model node Planner</option>';
    try {
      const models = await api.get('/api/router/models');
      if (selected && !models.some(m => m.id === selected)) html += `<option value="${escapeHtml(selected)}">${escapeHtml(selected)}</option>`;
      html += models.map(m => `<option value="${escapeHtml(m.id)}">${m.isCombo ? '[combo] ' : ''}${escapeHtml(m.id)}</option>`).join('');
    } catch {
      if (selected) html += `<option value="${escapeHtml(selected)}">${escapeHtml(selected)}</option>`;
    }
    select.innerHTML = html;
    select.value = selected || '';
  }

  function fillSkillSettings(s) {
    el('skillMaxChars').value = s.maxPromptChars;
    el('skillStoreDir').textContent = s.storeDirectory;
    return loadSkillModels(s.model);
  }

  async function loadSkillSettings() {
    try {
      await fillSkillSettings(await api.get('/api/skills/settings'));
    } catch (error) {
      toast(error.message, 'error');
    }
  }

  function skillSourceRow(s) {
    const [color, label] = skillStates[s.status] || ['secondary', s.status];
    const busy = ['Queued', 'Fetching', 'Analyzing'].includes(s.status);
    const error = s.error ? `<div class="small text-danger text-wrap">${escapeHtml(s.error)}</div>` : '';
    const progress = busy && s.progress ? `<div class="small text-secondary text-nowrap">${escapeHtml(s.progress)}</div>` : '';
    return `<tr>
      <td>
        <div class="fw-semibold text-truncate" title="${escapeHtml(s.name)}">${escapeHtml(s.name)}</div>
        <div class="small text-secondary mono text-truncate"><span class="badge text-bg-light">${skillKinds[s.kind] || s.kind}</span>
          <a href="${escapeHtml(s.url)}" target="_blank" rel="noopener">${escapeHtml(s.url)}</a></div>
        ${error}
      </td>
      <td><span class="badge text-bg-${color}">${label}</span>${progress}</td>
      <td>${s.skillCount}</td>
      <td class="text-nowrap small">${s.lastPulledAt ? fmt.relative(s.lastPulledAt) : '–'}</td>
      <td class="text-end text-nowrap">
        <button type="button" class="btn btn-outline-secondary btn-sm me-1" data-skill-sync="${s.id}" ${busy ? 'disabled' : ''}>Sync</button>
        <button type="button" class="btn btn-outline-danger btn-sm" data-skill-delete="${s.id}" data-name="${escapeHtml(s.name)}">Hapus</button>
      </td>
    </tr>`;
  }

  async function loadSkillSources() {
    try {
      const sources = await api.get('/api/skills/sources');
      el('skillSourceRows').innerHTML = sources.length
        ? sources.map(skillSourceRow).join('')
        : '<tr><td colspan="5" class="text-secondary small">Belum ada sumber — tempel link repository skill di atas, mis. https://github.com/anthropics/skills.</td></tr>';
      const busy = sources.filter(s => ['Queued', 'Fetching', 'Analyzing'].includes(s.status)).length;
      el('skillSourceSummary').textContent = `${sources.length} sumber${busy ? ` · ${busy} sedang diproses` : ''}`;
    } catch (error) {
      el('skillSourceRows').innerHTML = `<tr><td colspan="5" class="text-danger small">${escapeHtml(error.message)}</td></tr>`;
    }
  }

  function skillRow(s) {
    const tags = s.tags ? `<div class="small text-secondary text-truncate" title="${escapeHtml(s.tags)}">🏷️ ${escapeHtml(s.tags)}</div>` : '';
    return `<tr>
      <td>
        <div class="fw-semibold">${escapeHtml(s.name)}</div>
        <div class="small text-secondary">${escapeHtml(s.description)}</div>
        ${tags}
      </td>
      <td class="small text-nowrap">${escapeHtml(s.sourceName)}</td>
      <td class="small text-nowrap">${s.nodeCount ? `${s.nodeCount} node` : '–'}</td>
      <td class="text-end"><button type="button" class="btn btn-outline-secondary btn-sm" data-skill-view="${s.id}">Lihat</button></td>
    </tr>`;
  }

  async function loadSkills() {
    const query = el('skillSearch').value.trim();
    try {
      const skills = await api.get(`/api/skills${query ? `?query=${encodeURIComponent(query)}` : ''}`);
      el('skillRows').innerHTML = skills.length
        ? skills.map(skillRow).join('')
        : `<tr><td colspan="4" class="text-secondary small">${query ? 'Tidak ada skill yang cocok.' : 'Gudang masih kosong.'}</td></tr>`;
      el('skillCount').textContent = `${skills.length} skill`;
    } catch (error) {
      el('skillRows').innerHTML = `<tr><td colspan="4" class="text-danger small">${escapeHtml(error.message)}</td></tr>`;
    }
  }

  async function showSkill(id) {
    el('skillModalTitle').textContent = 'Memuat…';
    el('skillModalBody').innerHTML = '';
    skillModal.show();
    try {
      const s = await api.get(`/api/skills/${id}`);
      el('skillModalTitle').textContent = s.name;
      const files = s.files.length
        ? `<details class="mb-2"><summary class="small">${s.files.length} file di folder skill</summary><div class="small mono">${s.files.map(escapeHtml).join('<br>')}</div></details>`
        : '';
      el('skillModalBody').innerHTML = `
        <p class="mb-1">${escapeHtml(s.description)}</p>
        <div class="small text-secondary mb-1">Sumber: <a href="${escapeHtml(s.sourceUrl)}" target="_blank" rel="noopener">${escapeHtml(s.sourceName)}</a></div>
        ${s.tags ? `<div class="small text-secondary mb-1">🏷️ ${escapeHtml(s.tags)}</div>` : ''}
        <div class="small text-secondary mb-1">Dipakai node: ${s.nodeNames.length ? s.nodeNames.map(escapeHtml).join(', ') : '–'}</div>
        <div class="small text-secondary mono mb-2">${escapeHtml(s.folder)}</div>
        ${files}
        <pre class="border rounded p-2 small mb-0" style="white-space: pre-wrap">${escapeHtml(s.content)}</pre>`;
    } catch (error) {
      el('skillModalTitle').textContent = 'Skill';
      el('skillModalBody').innerHTML = `<span class="text-danger">${escapeHtml(error.message)}</span>`;
    }
  }

  el('skillForm').addEventListener('submit', async event => {
    event.preventDefault();
    const body = { model: el('skillModel').value || null, maxPromptChars: Number(el('skillMaxChars').value) };
    try {
      await fillSkillSettings(await api.put('/api/skills/settings', body));
      toast('Setting Skill tersimpan.');
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('skillSourceForm').addEventListener('submit', async event => {
    event.preventDefault();
    const button = event.submitter;
    if (button) button.disabled = true;
    try {
      await api.post('/api/skills/sources', { url: el('skillSourceUrl').value.trim() });
      el('skillSourceUrl').value = '';
      toast('Link ditambahkan — Skill Orchestrator sedang memeriksa dan menarik skill-nya.', 'info');
      await loadSkillSources();
    } catch (error) {
      toast(error.message, 'error');
    } finally {
      if (button) button.disabled = false;
    }
  });

  el('skillSourceRows').addEventListener('click', async event => {
    const button = event.target.closest('button[data-skill-sync], button[data-skill-delete]');
    if (!button) return;
    const { skillSync, skillDelete, name } = button.dataset;
    if (skillDelete && !confirm(`Hapus sumber "${name}" beserta semua skill-nya? Skill ini juga dilepas dari node yang memakainya.`)) return;
    button.disabled = true;
    try {
      if (skillSync) {
        await api.post(`/api/skills/sources/${skillSync}/sync`);
        toast('Sumber masuk antrean untuk ditarik ulang.', 'info');
      } else {
        await api.del(`/api/skills/sources/${skillDelete}`);
        toast('Sumber skill dihapus.', 'info');
      }
      await Promise.all([loadSkillSources(), loadSkills()]);
    } catch (error) {
      button.disabled = false;
      toast(error.message, 'error');
    }
  });

  el('skillRows').addEventListener('click', event => {
    const button = event.target.closest('button[data-skill-view]');
    if (button) showSkill(button.dataset.skillView);
  });

  el('skillSearch').addEventListener('input', debounce(loadSkills, 250));

  const refreshSkills = debounce(() => Promise.all([loadSkillSources(), loadSkills()]), 300);

  // One EventSource per page (site.js): every live feed of this page goes through this call.
  stream('types=telegram,skill', {
    'telegram.changed': debounce(() => loadTelegram(true), 300),
    'skill.changed': refreshSkills
  });

  load();
  loadMemoryStorage();
  loadBusinessSettings();
  loadTelegram(false);
  loadSkillSettings();
  loadSkillSources();
  loadSkills();
})();
