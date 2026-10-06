// Shared helpers for every AiThread page: API calls, SSE, formatting, toasts and small renderers.
(() => {
  'use strict';

  async function api(method, url, body) {
    const options = { method, headers: { Accept: 'application/json' } };
    if (body !== undefined) {
      options.headers['Content-Type'] = 'application/json';
      options.body = JSON.stringify(body);
    }

    let response;
    try {
      response = await fetch(url, options);
    } catch {
      throw new Error('Tidak dapat menghubungi server AiThread.');
    }

    let payload = null;
    try {
      payload = await response.json();
    } catch {
      // Empty or non-JSON body.
    }

    if (!response.ok || (payload && payload.success === false)) {
      const validation = payload?.errors ? Object.values(payload.errors).flat().join(' ') : null;
      throw new Error(payload?.message || validation || payload?.title || `HTTP ${response.status}`);
    }
    return payload && 'data' in payload ? payload.data : payload;
  }

  api.get = url => api('GET', url);
  api.post = (url, body) => api('POST', url, body ?? {});
  api.put = (url, body) => api('PUT', url, body ?? {});
  api.del = url => api('DELETE', url);

  function toast(message, type = 'success') {
    const container = document.getElementById('toastContainer');
    if (!container) return;
    const color = { success: 'text-bg-success', error: 'text-bg-danger', warning: 'text-bg-warning', info: 'text-bg-primary' }[type] || 'text-bg-secondary';
    const element = document.createElement('div');
    element.className = `toast align-items-center border-0 ${color}`;
    element.setAttribute('role', 'status');
    element.innerHTML = `<div class="d-flex"><div class="toast-body">${escapeHtml(message)}</div>
      <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Tutup"></button></div>`;
    container.appendChild(element);
    element.addEventListener('hidden.bs.toast', () => element.remove());
    bootstrap.Toast.getOrCreateInstance(element, { delay: type === 'error' ? 7000 : 3500 }).show();
  }

  function escapeHtml(value) {
    return String(value ?? '')
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('"', '&quot;')
      .replaceAll("'", '&#39;');
  }

  // Server timestamps are UTC; tolerate values that lack a zone designator.
  function toDate(value) {
    if (!value) return null;
    if (value instanceof Date) return value;
    const text = String(value);
    return new Date(/[zZ]|[+-]\d{2}:\d{2}$/.test(text) ? text : `${text}Z`);
  }

  const fmt = {
    dateTime(value) {
      const date = toDate(value);
      return date ? date.toLocaleString('id-ID', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit', second: '2-digit' }) : '–';
    },
    time(value) {
      const date = toDate(value);
      return date ? date.toLocaleTimeString('id-ID', { hour: '2-digit', minute: '2-digit', second: '2-digit' }) : '–';
    },
    relative(value) {
      const date = toDate(value);
      if (!date) return '–';
      const seconds = Math.round((Date.now() - date.getTime()) / 1000);
      if (seconds < 60) return `${Math.max(seconds, 0)} dtk lalu`;
      if (seconds < 3600) return `${Math.floor(seconds / 60)} mnt lalu`;
      if (seconds < 86400) return `${Math.floor(seconds / 3600)} jam lalu`;
      return `${Math.floor(seconds / 86400)} hari lalu`;
    },
    ms(value) {
      if (value === null || value === undefined) return '–';
      return value < 1000 ? `${value} ms` : `${(value / 1000).toFixed(1)} s`;
    },
    number(value) {
      return value === null || value === undefined ? '–' : Number(value).toLocaleString('id-ID');
    },
    speedup(sum, wall) {
      return sum && wall ? `${(sum / wall).toFixed(1)}x` : '–';
    }
  };

  const statusStyles = {
    Pending: ['secondary', 'Menunggu'],
    Planning: ['info', 'Planning'],
    Executing: ['primary', 'Eksekusi'],
    Verifying: ['info', 'Verifikasi'],
    Wrapping: ['info', 'Wrapping'],
    AwaitingInput: ['warning', 'Menunggu jawaban'],
    Completed: ['success', 'Selesai'],
    Failed: ['danger', 'Gagal'],
    Cancelled: ['warning', 'Dibatalkan'],
    RolledBack: ['secondary', 'Perubahan dibatalkan'],
    Running: ['primary', 'Berjalan'],
    Succeeded: ['success', 'Berhasil'],
    Skipped: ['secondary', 'Dilewati']
  };

  function statusBadge(status) {
    const [color, label] = statusStyles[status] || ['secondary', status];
    const spinner = ['Planning', 'Executing', 'Verifying', 'Wrapping', 'Running'].includes(status)
      ? '<span class="spinner-border spinner-border-sm me-1" style="width:.7rem;height:.7rem"></span>' : '';
    return `<span class="badge text-bg-${color}">${spinner}${escapeHtml(label)}</span>`;
  }

  const sourceLabels = { ClaudeCode: 'Claude Code', Dashboard: 'Dashboard', Telegram: 'Telegram' };

  function sourceLabel(source) {
    return sourceLabels[source] || source;
  }

  function isActiveStatus(status) {
    return ['Pending', 'Planning', 'Executing', 'Verifying', 'Wrapping'].includes(status);
  }

  function statusDot(ok, warn = false) {
    return `<span class="status-dot ${ok ? 'ok' : warn ? 'warn' : 'err'}"></span>`;
  }

  // One EventSource per page. handlers: { 'task.updated': payload => ... }; payload = { data, threadId, timestamp }.
  function stream(query, handlers) {
    const source = new EventSource(`/api/stream${query ? `?${query}` : ''}`);
    for (const [type, handler] of Object.entries(handlers)) {
      source.addEventListener(type, event => {
        try {
          handler(JSON.parse(event.data));
        } catch (error) {
          console.error('SSE handler failed', type, error);
        }
      });
    }
    source.addEventListener('router.status', event => {
      try {
        renderRouterPill(JSON.parse(event.data).data);
      } catch {
        // ignore
      }
    });
    window.addEventListener('beforeunload', () => source.close());
    return source;
  }

  function debounce(fn, wait = 300) {
    let timer;
    return (...args) => {
      clearTimeout(timer);
      timer = setTimeout(() => fn(...args), wait);
    };
  }

  // Deliberately small Markdown renderer for wrapper output: escape first, then add a few safe tags.
  function md(text) {
    if (!text) return '';
    const blocks = [];
    let html = String(text).replace(/```[\w-]*\n([\s\S]*?)```/g, (_, code) => {
      blocks.push(`<pre><code>${escapeHtml(code.replace(/\n$/, ''))}</code></pre>`);
      return `\u0000${blocks.length - 1}\u0000`;
    });
    html = escapeHtml(html)
      .replace(/^### (.*)$/gm, '<h3>$1</h3>')
      .replace(/^## (.*)$/gm, '<h2>$1</h2>')
      .replace(/^# (.*)$/gm, '<h1>$1</h1>')
      .replace(/^---$/gm, '<hr>')
      .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
      .replace(/`([^`\n]+)`/g, '<code>$1</code>')
      .replace(/((?:^[ \t]*[-*] .*(?:\n|$))+)/gm, list =>
        `<ul>${list.trim().split('\n').map(item => `<li>${item.replace(/^[ \t]*[-*] /, '')}</li>`).join('')}</ul>`)
      .replace(/\n{2,}/g, '<br><br>')
      .replace(/\n/g, '<br>');
    return html.replace(/\u0000(\d+)\u0000/g, (_, index) => blocks[Number(index)]);
  }

  function diffHtml(diff) {
    if (!diff) return '';
    return diff.split('\n').map(line => {
      const safe = escapeHtml(line);
      if (line.startsWith('+++') || line.startsWith('---')) return `<span class="text-secondary">${safe}</span>`;
      if (line.startsWith('@@')) return `<span class="diff-hunk">${safe}</span>`;
      if (line.startsWith('+')) return `<span class="diff-add">${safe}</span>`;
      if (line.startsWith('-')) return `<span class="diff-del">${safe}</span>`;
      return safe;
    }).join('\n');
  }

  function threadRow(t) {
    const tasks = t.taskCount ? `${t.tasksSucceeded}/${t.taskCount}${t.tasksFailed ? ` <span class="text-danger">(${t.tasksFailed}✕)</span>` : ''}` : '–';
    return `<tr data-href="/Thread/Detail/${t.id}">
      <td class="text-nowrap"><div>${fmt.dateTime(t.createdAt)}</div><div class="small text-secondary">${sourceLabel(t.source)}</div></td>
      <td class="text-nowrap">${escapeHtml(t.projectName || '–')}</td>
      <td><div class="text-truncate-2" title="${escapeHtml(t.prompt)}">${escapeHtml(t.prompt)}</div></td>
      <td>${statusBadge(t.status)}</td>
      <td class="text-nowrap">${tasks}</td>
      <td class="text-nowrap">${fmt.ms(t.totalMs)}</td>
      <td class="text-nowrap">${fmt.speedup(t.executionSumMs, t.executionWallMs)}</td>
      <td class="text-nowrap">${fmt.number(t.totalTokens)}</td>
    </tr>`;
  }

  function bindRowLinks(tbody) {
    tbody.addEventListener('click', event => {
      const row = event.target.closest('tr[data-href]');
      if (row && !event.target.closest('button, a, input')) window.location.href = row.dataset.href;
    });
  }

  function renderRouterPill(status) {
    const pill = document.getElementById('routerPill');
    if (!pill || !status) return;
    const warn = status.isOnline && status.apiKeyValid === false;
    pill.querySelector('.status-dot').className = `status-dot ${status.isOnline ? (warn ? 'warn' : 'ok') : 'err'}`;
    pill.querySelector('.router-pill-text').textContent = status.isOnline
      ? `9router · ${status.latencyMs ?? '?'} ms`
      : '9router offline';
    pill.title = status.isOnline
      ? `${status.baseUrl} — ${status.modelCount} model, ${status.comboCount} combo${status.error ? ` — ${status.error}` : ''}`
      : `${status.baseUrl}: ${status.error || 'offline'}`;
  }

  // How chat calls will authenticate against 9router: [label, isProblem]. Health and the model list are public,
  // so a router can be online while every chat call fails for lack of a key.
  function routerKeyState(status) {
    if (status.apiKeyValid === false) return [status.hasApiKey ? 'API key ditolak' : 'API key wajib, belum disimpan', true];
    return [status.hasApiKey ? 'API key OK' : 'tanpa API key (tidak diwajibkan)', false];
  }

  async function refreshRouterPill() {
    try {
      renderRouterPill(await api.get('/api/router/status'));
    } catch {
      renderRouterPill({ isOnline: false, baseUrl: '', error: 'server tidak merespons' });
    }
  }

  function initThemeToggle() {
    const button = document.getElementById('themeToggle');
    if (!button) return;
    button.addEventListener('click', () => {
      const next = document.documentElement.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';
      document.documentElement.setAttribute('data-bs-theme', next);
      try {
        localStorage.setItem('aithread-theme', next);
      } catch {
        // Storage unavailable (private mode) — the choice just is not remembered.
      }
    });
  }

  // The "what's new" bar stays hidden once dismissed, until the app version changes.
  function initUpdateBar() {
    const bar = document.getElementById('updateBar');
    if (!bar) return;
    const key = 'aithread.updateBar.dismissed';
    const version = bar.dataset.version;
    try {
      if (localStorage.getItem(key) === version) {
        bar.remove();
        return;
      }
    } catch {
      // Storage unavailable — show the bar every time.
    }
    bar.classList.remove('d-none');
    bar.addEventListener('closed.bs.alert', () => {
      try {
        localStorage.setItem(key, version);
      } catch {
        // Storage unavailable — the dismissal just is not remembered.
      }
    });
  }

  function isNewerVersion(latest, current) {
    const parse = value => String(value).split('-')[0].split('.').map(part => parseInt(part, 10) || 0);
    const a = parse(latest);
    const b = parse(current);
    for (let i = 0; i < Math.max(a.length, b.length); i++) {
      const diff = (a[i] || 0) - (b[i] || 0);
      if (diff !== 0) return diff > 0;
    }
    return false;
  }

  // Asks the npm registry (once per page load) whether a newer aithread exists and shows a link in the sidebar.
  async function initNpmUpdate() {
    const link = document.getElementById('npmUpdate');
    if (!link) return;
    try {
      const response = await fetch('https://registry.npmjs.org/aithread/latest', { headers: { Accept: 'application/json' } });
      if (!response.ok) return;
      const { version } = await response.json();
      if (!version || !isNewerVersion(version, link.dataset.version)) return;
      link.querySelector('.app-update-text').textContent = `Update tersedia · v${version}`;
      link.title = `Versi terpasang ${link.dataset.version}. Jalankan: npm i -g aithread@latest`;
      link.classList.remove('d-none');
    } catch {
      // Offline or blocked — no update hint.
    }
  }

  // First run: the developer name comes from git (user.name). Only without it is the name asked, once, and kept in the
  // settings so it is never asked again on this machine.
  async function initDeveloperName() {
    const modalElement = document.getElementById('developerNameModal');
    if (!modalElement) return;
    let settings;
    try {
      settings = await api.get('/api/business/settings');
    } catch {
      return; // Server or database not ready; ask on a later page load.
    }
    if (settings.developerName || settings.gitUserName) return;

    const input = document.getElementById('developerNameInput');
    input.value = settings.effectiveDeveloperName || '';
    const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
    modalElement.addEventListener('shown.bs.modal', () => input.select(), { once: true });
    document.getElementById('developerNameForm').addEventListener('submit', async event => {
      event.preventDefault();
      const name = input.value.trim();
      if (!name) {
        input.classList.add('is-invalid');
        return;
      }
      const button = document.getElementById('developerNameSave');
      button.disabled = true;
      try {
        const saved = await api.put('/api/business/developer', { name });
        modal.hide();
        toast(`Halo, ${saved.developerName}!`);
        document.dispatchEvent(new CustomEvent('aithread:developer-name', { detail: saved }));
      } catch (error) {
        toast(error.message, 'error');
      } finally {
        button.disabled = false;
      }
    });
    input.addEventListener('input', () => input.classList.remove('is-invalid'));
    modal.show();
  }

  document.addEventListener('DOMContentLoaded', () => {
    initThemeToggle();
    initUpdateBar();
    initNpmUpdate();
    initDeveloperName();
    refreshRouterPill();
    setInterval(refreshRouterPill, 20000);
  });

  window.AiThread = { api, toast, escapeHtml, fmt, toDate, statusBadge, isActiveStatus, sourceLabel, statusDot, stream, debounce, md, diffHtml, threadRow, bindRowLinks, renderRouterPill, routerKeyState };
})();
