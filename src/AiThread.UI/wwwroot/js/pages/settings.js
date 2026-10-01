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
      ? ' · <span class="text-warning">key ini belum disimpan — klik Simpan</span>'
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

  el('settingForm').addEventListener('submit', async event => {
    event.preventDefault();
    const body = {
      nineRouterBaseUrl: el('routerUrl').value.trim(),
      nineRouterApiKey: el('routerKey').value.trim() || null,
      clearNineRouterApiKey: el('routerKeyClear').checked,
      proxyApiKey: el('proxyKey').value.trim() || null,
      passthroughModel: el('passthroughModel').value || null,
      orchestrationEnabled: el('orchestrationEnabled').checked,
      streamProgressToClient: el('streamProgress').checked,
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

  stream('types=telegram', { 'telegram.changed': debounce(() => loadTelegram(true), 300) });

  load();
  loadTelegram(false);
})();
