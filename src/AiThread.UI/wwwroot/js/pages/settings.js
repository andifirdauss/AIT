(() => {
  'use strict';
  const { api, toast, escapeHtml, renderRouterPill, routerKeyState } = window.AiThread;
  const el = id => document.getElementById(id);
  let settings = null;

  const scope = () => document.querySelector('input[name="claudeScope"]:checked').value;

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
    if (!el('proxyUrl').value) el('proxyUrl').value = s.proxyBaseUrl;
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

  async function loadProjects() {
    try {
      const projects = await api.get('/api/projects');
      el('claudeProject').innerHTML = projects.length
        ? projects.map(p => `<option value="${p.id}">${escapeHtml(p.name)} — ${escapeHtml(p.rootPath)}</option>`).join('')
        : '<option value="">Belum ada project terdaftar (daftarkan di menu Project)</option>';
    } catch {
      el('claudeProject').innerHTML = '<option value="">Project tidak dapat dimuat</option>';
    }
  }

  async function load() {
    try {
      const s = await api.get('/api/settings');
      fill(s);
      await Promise.all([loadModels(s.passthroughModel), loadProjects(), showStoredStatus()]);
      syncScope();
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
    toast('Key baru dibuat. Simpan setting, lalu terapkan ulang ke Claude Code.', 'info');
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
      requestTimeoutSeconds: Number(el('requestTimeout').value)
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
      refreshClaude();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  function claudeQuery() {
    const params = new URLSearchParams({ scope: scope() });
    if (scope() === 'Project' && el('claudeProject').value) params.set('projectId', el('claudeProject').value);
    return params;
  }

  function renderClaude(status) {
    const box = el('claudeStatus');
    box.classList.remove('alert-success', 'alert-secondary', 'alert-warning');
    if (status.pointsToAiThread) {
      box.classList.add('alert-success');
      box.innerHTML = `✓ Claude Code sudah diarahkan ke AiThread.<br><span class="mono">${escapeHtml(status.path)}</span>`;
    } else if (status.currentBaseUrl) {
      box.classList.add('alert-warning');
      box.innerHTML = `ANTHROPIC_BASE_URL saat ini: <span class="mono">${escapeHtml(status.currentBaseUrl)}</span> (bukan AiThread).<br><span class="mono">${escapeHtml(status.path)}</span>`;
    } else {
      box.classList.add('alert-secondary');
      box.innerHTML = `Belum diarahkan. ${status.fileExists ? 'File ada' : 'File akan dibuat'}: <span class="mono">${escapeHtml(status.path)}</span>`;
    }
    el('claudePreview').textContent = status.previewJson;
  }

  async function refreshClaude() {
    if (scope() === 'Project' && !el('claudeProject').value) {
      el('claudeStatus').className = 'alert alert-secondary small';
      el('claudeStatus').textContent = 'Pilih project terlebih dahulu.';
      el('claudePreview').textContent = '';
      return;
    }
    try {
      renderClaude(await api.get(`/api/settings/claude?${claudeQuery()}`));
    } catch (error) {
      el('claudeStatus').className = 'alert alert-warning small';
      el('claudeStatus').textContent = error.message;
    }
  }

  function syncScope() {
    el('claudeProjectGroup').classList.toggle('d-none', scope() !== 'Project');
    refreshClaude();
  }

  function claudeBody() {
    return {
      scope: scope(),
      projectId: scope() === 'Project' ? (el('claudeProject').value || null) : null,
      proxyBaseUrl: el('proxyUrl').value.trim(),
      timeoutMs: null
    };
  }

  el('btnApplyClaude').addEventListener('click', async () => {
    if (scope() === 'User' && !confirm('Semua sesi Claude Code di komputer ini akan melewati AiThread. Lanjutkan?')) return;
    try {
      renderClaude(await api.post('/api/settings/claude/apply', claudeBody()));
      toast('Claude Code diarahkan ke AiThread. Buka sesi baru agar berlaku.');
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('btnRevertClaude').addEventListener('click', async () => {
    try {
      renderClaude(await api.post('/api/settings/claude/revert', claudeBody()));
      toast('Env AiThread dihapus dari settings Claude Code.', 'info');
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  document.querySelectorAll('input[name="claudeScope"]').forEach(r => r.addEventListener('change', syncScope));
  el('claudeProject').addEventListener('change', refreshClaude);

  load();
})();
