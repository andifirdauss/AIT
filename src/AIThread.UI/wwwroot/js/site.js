// Shared helpers for every AIT page: API calls, SSE, formatting, toasts and small renderers.
(() => {
  'use strict';

  // The language of the page (Setting → Bahasa / Language), rendered into <html lang>. Every text is written in place
  // as a pair: t('Indonesia', 'English'); a template with expressions as (isEn ? `English ${x}` : `Indonesia ${x}`),
  // so only one of them is built.
  const lang = document.documentElement.lang === 'en' ? 'en' : 'id';
  const isEn = lang === 'en';
  const locale = isEn ? 'en-US' : 'id-ID';
  const t = (indonesian, english) => (isEn ? english : indonesian);

  async function api(method, url, body) {
    const options = { method, headers: { Accept: 'application/json' } };
    if (body instanceof FormData) {
      // A file upload: the browser sets the multipart boundary itself.
      options.body = body;
    } else if (body !== undefined) {
      options.headers['Content-Type'] = 'application/json';
      options.body = JSON.stringify(body);
    }

    let response;
    try {
      response = await fetch(url, options);
    } catch {
      throw new Error(t('Tidak dapat menghubungi server AIT.', 'Cannot reach the AIT server.'));
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

  // A neutral notification card; the type shows as an accent stripe and icon (site.css .app-toast), so the red close
  // button never sits on a colored background.
  const toastIcons = { success: '✓', error: '✕', warning: '!', info: 'i' };

  function toast(message, type = 'success') {
    const container = document.getElementById('toastContainer');
    if (!container) return;
    if (!toastIcons[type]) type = 'info';
    const element = document.createElement('div');
    element.className = `toast app-toast app-toast-${type}`;
    element.setAttribute('role', 'status');
    element.innerHTML = `<div class="win-dismissible">${winControls({ kind: 'toast' })}<span class="app-toast-icon" aria-hidden="true">${toastIcons[type]}</span><div class="toast-body">${escapeHtml(message)}</div></div>`;
    container.appendChild(element);
    element.addEventListener('hidden.bs.toast', () => element.remove());
    bootstrap.Toast.getOrCreateInstance(element, { delay: type === 'error' ? 7000 : 3500 }).show();
  }

  // --- Window controls (macOS style): the project standard for close / minimize / maximize. ---
  // Razor views use <win-controls /> (TagHelpers/WinControlsTagHelper.cs); this renders the same markup.
  //   kind: 'window' (a .win panel), 'modal', 'alert' or 'toast' (the last two get the close button alone)
  //   closeLabel: tooltip of the red button; disabled: buttons shown grey, e.g. ['close']
  function winControls({ kind = 'window', closeLabel = t('Tutup', 'Close'), disabled = [] } = {}) {
    const button = (name, label, action) => {
      const text = escapeHtml(label);
      return `<button type="button" class="win-btn win-${name}"${disabled.includes(name) ? ' disabled' : ` ${action}`} aria-label="${text}" title="${text}"></button>`;
    };
    const dismiss = kind === 'window' ? 'data-win="close"' : `data-bs-dismiss="${kind}"`;
    let html = button('close', closeLabel, dismiss);
    if (kind !== 'alert' && kind !== 'toast') {
      html += button('min', kind === 'modal' ? t('Kecilkan', 'Shrink') : t('Lipat', 'Minimize'), 'data-win="min"')
        + button('max', t('Layar penuh', 'Full screen'), 'data-win="max"');
    }
    return (isEn ? `<div class="win-controls" role="group" aria-label="Window controls">${html}</div>` : `<div class="win-controls" role="group" aria-label="Kontrol jendela">${html}</div>`);
  }

  // A window around body (HTML). id keeps its folded/full-screen state when the page re-renders it; minimized is
  // the state it starts in. Without canClose the red button is inactive (a code block has nothing to close); with
  // it the window fires "win:close" for the page to handle.
  function winFrame({ id = '', title = '', body = '', minimized = false, closeLabel, canClose = false, className = '' } = {}) {
    const attrs = id ? ` data-win-id="${escapeHtml(id)}"` : '';
    return `<div class="win${minimized ? ' win-minimized' : ''}${className ? ` ${className}` : ''}"${attrs}>
      <div class="win-bar">${winControls({ closeLabel, disabled: canClose ? [] : ['close'] })}<span class="win-title small mono">${escapeHtml(title)}</span></div>
      ${body}
    </div>`;
  }

  // Folded / full-screen state per data-win-id, so a window keeps it when the page re-renders it. Windows marked
  // data-win-persist also remember being folded across page loads.
  const winStates = new Map();
  const winStoreKey = id => `aithread.win.${id}`;

  function winState(win) {
    const id = win.dataset.winId;
    if (!id) return null;
    if (!winStates.has(id)) {
      let stored = null;
      if (win.hasAttribute('data-win-persist')) {
        try { stored = localStorage.getItem(winStoreKey(id)); } catch { /* not remembered */ }
      }
      winStates.set(id, { min: stored === null ? win.classList.contains('win-minimized') : stored === '1', max: false });
    }
    return winStates.get(id);
  }

  function paintWin(win, state) {
    win.classList.toggle('win-minimized', state.min && !state.max);
    win.classList.toggle('win-maximized', state.max);
    win.querySelector(':scope > .win-bar .win-max')?.setAttribute('aria-pressed', String(state.max));
    win.querySelector(':scope > .win-bar .win-min')?.setAttribute('aria-expanded', String(!state.min));
  }

  function syncMaxLock() {
    document.body.classList.toggle('win-has-max', !!document.querySelector('.win.win-maximized'));
  }

  function setWin(win, change) {
    const state = winState(win) || { min: win.classList.contains('win-minimized'), max: win.classList.contains('win-maximized') };
    Object.assign(state, change);
    if (change.max) state.min = false;
    if (change.min) state.max = false;
    paintWin(win, state);
    if ('min' in change && win.dataset.winId && win.hasAttribute('data-win-persist')) {
      try { localStorage.setItem(winStoreKey(win.dataset.winId), state.min ? '1' : '0'); } catch { /* not remembered */ }
    }
    syncMaxLock();
  }

  // Modal yellow button: dock the dialog at the bottom of the screen and give the page back. Bootstrap keeps pulling the focus
  // into an open dialog, so its focus trap is paused while docked.
  function setModalDocked(modal, docked) {
    modal.classList.toggle('win-docked', docked);
    modal.querySelector('.modal-dialog')?.classList.remove('modal-fullscreen');
    modal.querySelector('.win-max')?.setAttribute('aria-pressed', 'false');
    document.body.classList.toggle('win-modal-docked', !!document.querySelector('.modal.show.win-docked'));
    const trap = bootstrap.Modal.getInstance(modal)?._focustrap;
    if (docked) trap?.deactivate();
    else trap?.activate();
  }

  function toggleModalMax(modal) {
    if (modal.classList.contains('win-docked')) setModalDocked(modal, false);
    const full = modal.querySelector('.modal-dialog').classList.toggle('modal-fullscreen');
    modal.querySelector('.win-max')?.setAttribute('aria-pressed', String(full));
  }

  function initWindows() {
    document.addEventListener('click', event => {
      const button = event.target.closest('[data-win]');
      if (button && !button.disabled) {
        const win = button.closest('.win');
        const modal = win ? null : button.closest('.modal');
        const action = button.dataset.win;
        if (modal) {
          if (action === 'min') setModalDocked(modal, !modal.classList.contains('win-docked'));
          if (action === 'max') toggleModalMax(modal);
        } else if (win) {
          if (action === 'close') win.dispatchEvent(new CustomEvent('win:close', { bubbles: true }));
          if (action === 'min') setWin(win, { min: !win.classList.contains('win-minimized') });
          if (action === 'max') setWin(win, { max: !win.classList.contains('win-maximized') });
        }
        return;
      }
      // A docked modal comes back when its title bar is clicked.
      const dockedHeader = event.target.closest('.modal.win-docked .modal-header');
      if (dockedHeader && !event.target.closest('.win-controls')) setModalDocked(dockedHeader.closest('.modal'), false);
    });

    // Double-clicking a title bar toggles full screen, as on macOS.
    document.addEventListener('dblclick', event => {
      if (event.target.closest('.win-controls, .win-actions, button, a, input, select, textarea')) return;
      const bar = event.target.closest('.win-bar');
      if (!bar) return;
      if (bar.classList.contains('modal-header')) toggleModalMax(bar.closest('.modal'));
      else if (bar.parentElement?.classList.contains('win')) setWin(bar.parentElement, { max: !bar.parentElement.classList.contains('win-maximized') });
    });

    // Esc leaves full screen (an open dialog handles its own Esc).
    document.addEventListener('keydown', event => {
      if (event.key !== 'Escape' || document.querySelector('.modal.show:not(.win-docked)')) return;
      const maximized = [...document.querySelectorAll('.win.win-maximized')].pop();
      if (maximized) setWin(maximized, { max: false });
    });

    document.addEventListener('hidden.bs.modal', event => {
      const modal = event.target;
      if (modal.classList.contains('win-docked')) setModalDocked(modal, false);
      modal.querySelector('.modal-dialog')?.classList.remove('modal-fullscreen');
      modal.querySelector('.win-max')?.setAttribute('aria-pressed', 'false');
    });

    // Windows rendered later, or re-rendered, get their remembered state back.
    const restore = root => {
      const wins = root.matches('.win[data-win-id]') ? [root] : [];
      wins.push(...root.querySelectorAll('.win[data-win-id]'));
      wins.forEach(win => paintWin(win, winState(win)));
    };
    restore(document.body);
    new MutationObserver(mutations => {
      let changed = false;
      for (const mutation of mutations) {
        for (const node of mutation.addedNodes) {
          if (node instanceof Element && (node.matches('.win') || node.querySelector('.win'))) {
            restore(node);
            changed = true;
          }
        }
        if (mutation.removedNodes.length) changed = true;
      }
      if (changed) syncMaxLock();
    }).observe(document.body, { childList: true, subtree: true });
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
      return date ? date.toLocaleString(locale, { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit', second: '2-digit' }) : '–';
    },
    time(value) {
      const date = toDate(value);
      return date ? date.toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit', second: '2-digit' }) : '–';
    },
    relative(value) {
      const date = toDate(value);
      if (!date) return '–';
      const seconds = Math.round((Date.now() - date.getTime()) / 1000);
      if (seconds < 60) return (isEn ? `${Math.max(seconds, 0)}s ago` : `${Math.max(seconds, 0)} dtk lalu`);
      if (seconds < 3600) return (isEn ? `${Math.floor(seconds / 60)} min ago` : `${Math.floor(seconds / 60)} mnt lalu`);
      if (seconds < 86400) return (isEn ? `${Math.floor(seconds / 3600)} h ago` : `${Math.floor(seconds / 3600)} jam lalu`);
      return (isEn ? `${Math.floor(seconds / 86400)} d ago` : `${Math.floor(seconds / 86400)} hari lalu`);
    },
    ms(value) {
      if (value === null || value === undefined) return '–';
      return value < 1000 ? `${value} ms` : `${(value / 1000).toFixed(1)} s`;
    },
    number(value) {
      return value === null || value === undefined ? '–' : Number(value).toLocaleString(locale);
    },
    speedup(sum, wall) {
      return sum && wall ? `${(sum / wall).toFixed(1)}x` : '–';
    }
  };

  const statusStyles = {
    Pending: ['secondary', t('Menunggu', 'Pending')],
    Planning: ['info', 'Planning'],
    Executing: ['primary', t('Eksekusi', 'Executing')],
    Verifying: ['info', t('Verifikasi', 'Verifying')],
    Testing: ['info', 'Testing'],
    Wrapping: ['info', 'Wrapping'],
    AwaitingInput: ['warning', t('Menunggu jawaban', 'Awaiting answer')],
    Completed: ['success', t('Selesai', 'Completed')],
    Failed: ['danger', t('Gagal', 'Failed')],
    Cancelled: ['warning', t('Dibatalkan', 'Cancelled')],
    RolledBack: ['secondary', t('Perubahan dibatalkan', 'Changes rolled back')],
    Running: ['primary', t('Berjalan', 'Running')],
    Succeeded: ['success', t('Berhasil', 'Succeeded')],
    Skipped: ['secondary', t('Dilewati', 'Skipped')]
  };

  function statusBadge(status) {
    const [color, label] = statusStyles[status] || ['secondary', status];
    const spinner = ['Planning', 'Executing', 'Verifying', 'Testing', 'Wrapping', 'Running'].includes(status)
      ? '<span class="spinner-border spinner-border-sm me-1" style="width:.7rem;height:.7rem"></span>' : '';
    return `<span class="badge text-bg-${color}">${spinner}${escapeHtml(label)}</span>`;
  }

  const sourceLabels = { ClaudeCode: 'CLI', Dashboard: t('Dasbor', 'Dashboard'), Telegram: 'Telegram' };

  function sourceLabel(source) {
    return sourceLabels[source] || source;
  }

  function isActiveStatus(status) {
    return ['Pending', 'Planning', 'Executing', 'Verifying', 'Testing', 'Wrapping'].includes(status);
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

  // Client-side pager: the list is kept whole, only one page of rows is rendered.
  // The page survives refreshes (clamped when the list shrinks); `reset` jumps back to page 1.
  function pager(footerId, render, pageSize = 10) {
    const footer = document.getElementById(footerId);
    footer.innerHTML = t(`<span class="text-secondary"></span>
      <div class="btn-group btn-group-sm">
        <button type="button" class="btn btn-outline-secondary" data-page="-1">‹ Sebelumnya</button>
        <button type="button" class="btn btn-outline-secondary" data-page="1">Berikutnya ›</button>
      </div>`, `<span class="text-secondary"></span>
      <div class="btn-group btn-group-sm">\n        <button type="button" class="btn btn-outline-secondary" data-page="-1">‹ Previous</button>
        <button type="button" class="btn btn-outline-secondary" data-page="1">Next ›</button>
      </div>`);
    const [prev, next] = footer.querySelectorAll('button');
    let items = [];
    let page = 1;

    function draw() {
      const pages = Math.max(1, Math.ceil(items.length / pageSize));
      page = Math.min(Math.max(1, page), pages);
      const start = (page - 1) * pageSize;
      render(items.slice(start, start + pageSize));
      footer.classList.toggle('d-none', items.length <= pageSize);
      footer.firstElementChild.textContent = (isEn ? `${start + 1}–${Math.min(start + pageSize, items.length)} of ${fmt.number(items.length)}` : `${start + 1}–${Math.min(start + pageSize, items.length)} dari ${fmt.number(items.length)}`);
      prev.disabled = page <= 1;
      next.disabled = page >= pages;
    }

    footer.addEventListener('click', event => {
      const target = event.target.closest('button[data-page]');
      if (!target) return;
      page += Number(target.dataset.page);
      draw();
    });

    return {
      set(list, reset = false) {
        items = list || [];
        if (reset) page = 1;
        draw();
      }
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
    // The short id is the one the CLI shows after each answer (thread `1a2b3c4d`); the full id is in the tooltip.
    return `<tr data-href="/Thread/Detail/${t.id}">
      <td class="mono small text-nowrap" title="${escapeHtml(t.id)}">${escapeHtml(t.id.replace(/-/g, '').substring(0, 8))}</td>
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
    if (status.apiKeyValid === false) return [status.hasApiKey ? t('API key ditolak', 'API key rejected') : t('API key wajib, belum disimpan', 'API key required, not saved yet'), true];
    return [status.hasApiKey ? 'API key OK' : t('tanpa API key (tidak diwajibkan)', 'no API key (not required)'), false];
  }

  async function refreshRouterPill() {
    try {
      renderRouterPill(await api.get('/api/router/status'));
    } catch {
      renderRouterPill({ isOnline: false, baseUrl: '', error: t('server tidak merespons', 'server not responding') });
    }
  }

  // Setting → language of AIT (web UI, output to the CLI, logs): saved on the server, the page reloads to show it.
  function initLanguageSelect() {
    const select = document.getElementById('languageSelect');
    if (!select) return;
    select.value = lang;
    select.addEventListener('change', async () => {
      select.disabled = true;
      try {
        await api.put('/api/settings/language', { language: select.value });
        window.location.reload();
      } catch (error) {
        toast(error.message, 'error');
        select.value = lang;
        select.disabled = false;
      }
    });
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

  // The "what's new" bar stays hidden once dismissed, until the app version changes. It shows the newest version's
  // note; the toggle opens the notes of the other versions released the same day.
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
    const more = bar.querySelector('.update-more');
    const toggle = bar.querySelector('.update-toggle');
    if (more && toggle) {
      toggle.addEventListener('click', () => {
        const open = more.hidden;
        more.hidden = !open;
        toggle.setAttribute('aria-expanded', String(open));
        toggle.textContent = open ? toggle.dataset.less : toggle.dataset.more;
      });
    }
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
      link.querySelector('.app-update-text').textContent = (isEn ? `Update available · v${version}` : `Update tersedia · v${version}`);
      link.title = (isEn ? `Installed version ${link.dataset.version}. Run: npm i -g aithread@latest` : `Versi terpasang ${link.dataset.version}. Jalankan: npm i -g aithread@latest`);
      link.classList.remove('d-none');
    } catch {
      // Offline or blocked — no update hint.
    }
  }

  // The illustrated tour (install → 9router → Setting → Node → Project → CLI → use), opened from the sidebar button or
  // the Setup page at the step last seen. A fresh instance goes to the Setup page instead (initSetupStatus), which does
  // the steps in place.
  function initOnboarding() {
    const modalElement = document.getElementById('onboardingModal');
    if (!modalElement) return;

    const doneKey = 'aithread.onboarding.done';
    const stepKey = 'aithread.onboarding.step';
    const read = key => { try { return localStorage.getItem(key); } catch { return null; } };
    const write = (key, value) => { try { localStorage.setItem(key, value); } catch { /* not remembered */ } };

    const carouselElement = document.getElementById('onboardingCarousel');
    const carousel = bootstrap.Carousel.getOrCreateInstance(carouselElement, { interval: false, ride: false, wrap: false, touch: true, keyboard: false });
    const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
    const items = [...carouselElement.querySelectorAll('.carousel-item')];
    const prev = document.getElementById('onboardingPrev');
    const next = document.getElementById('onboardingNext');
    const dots = document.getElementById('onboardingDots');
    dots.innerHTML = items.map((_, i) =>
      (isEn ? `<button type="button" data-step="${i}" aria-label="Step ${i + 1}"></button>` : `<button type="button" data-step="${i}" aria-label="Langkah ${i + 1}"></button>`)).join('');

    const current = () => Math.max(0, items.findIndex(item => item.classList.contains('active')));
    function render(index) {
      document.getElementById('onboardingStep').textContent = `${index + 1} / ${items.length}`;
      dots.querySelectorAll('button').forEach((dot, i) => {
        dot.classList.toggle('active', i === index);
        dot.setAttribute('aria-current', i === index ? 'step' : 'false');
      });
      prev.disabled = index === 0;
      next.textContent = index === items.length - 1 ? t('Mulai pakai AIT', 'Start using AIT') : t('Lanjut ›', 'Next ›');
    }
    // Jumps without animation; used while the dialog is hidden, where a slide transition would never finish.
    function jump(index) {
      items.forEach((item, i) => item.classList.toggle('active', i === index));
      render(index);
    }

    carouselElement.addEventListener('slid.bs.carousel', event => render(event.to));
    dots.addEventListener('click', event => {
      const dot = event.target.closest('button[data-step]');
      if (dot) carousel.to(Number(dot.dataset.step));
    });
    prev.addEventListener('click', () => carousel.prev());
    next.addEventListener('click', () => (current() === items.length - 1 ? modal.hide() : carousel.next()));
    modalElement.addEventListener('keydown', event => {
      if (event.target.closest('input, textarea, select')) return;
      if (event.key === 'ArrowRight' && current() < items.length - 1) carousel.next();
      if (event.key === 'ArrowLeft' && current() > 0) carousel.prev();
    });
    // Closing the tour, or leaving it through one of its links, counts as seen.
    const remember = () => {
      write(doneKey, '1');
      write(stepKey, String(current() === items.length - 1 ? 0 : current()));
    };
    modalElement.addEventListener('hide.bs.modal', remember);
    modalElement.addEventListener('click', event => {
      if (event.target.closest('a[href]')) remember();
    });
    document.getElementById('onboardingOpen')?.addEventListener('click', () => {
      jump(Math.min(Number(read(stepKey)) || 0, items.length - 1));
      modal.show();
    });
    render(0);
  }

  // Setup progress: a badge on the sidebar link and a notice above the page while a required step is still open. A
  // dismissed notice stays away until another step becomes open.
  const setupNoticeKey = 'aithread.setupNotice.dismissed';

  function renderSetupStatus(status) {
    const open = status.steps.filter(s => s.required && !s.done);
    const badge = document.getElementById('setupBadge');
    if (badge) {
      badge.textContent = `${status.requiredDone}/${status.requiredCount}`;
      badge.title = open.length ? (isEn ? `Not yet: ${open.map(s => s.title).join(', ')}` : `Belum: ${open.map(s => s.title).join(', ')}`) : '';
      badge.classList.toggle('d-none', status.isComplete);
    }

    const notice = document.getElementById('setupNotice');
    if (!notice) return;
    const openIds = open.map(s => s.id).join(',');
    let dismissed = null;
    try {
      dismissed = localStorage.getItem(setupNoticeKey);
    } catch {
      // Storage unavailable — the notice shows while setup is incomplete.
    }
    if (status.isComplete || dismissed === openIds) {
      notice.classList.add('d-none');
      return;
    }
    notice.querySelector('[data-setup-count]').textContent = (isEn ? `${status.requiredDone} of ${status.requiredCount} required steps done` : `${status.requiredDone} dari ${status.requiredCount} langkah wajib selesai`);
    notice.querySelector('[data-setup-open]').textContent = open.map(s => s.title.toLowerCase()).join(', ');
    notice.dataset.open = openIds;
    notice.classList.remove('d-none');
  }

  // Resolves true when the page is leaving for the Setup page: a fresh instance (no pipeline yet) opening the
  // dashboard is sent there once per browser, the way the tour used to open by itself.
  async function initSetupStatus() {
    const notice = document.getElementById('setupNotice');
    notice?.addEventListener('closed.bs.alert', () => {
      try {
        localStorage.setItem(setupNoticeKey, notice.dataset.open || '');
      } catch {
        // Not remembered; the notice comes back on the next page.
      }
    });

    let status;
    try {
      status = await api.get('/api/setup/status');
    } catch {
      return false; // Server not ready; the next page load tries again.
    }
    renderSetupStatus(status);

    let opened = null;
    try {
      opened = localStorage.getItem('aithread.setup.opened');
    } catch {
      opened = '1'; // Without storage the redirect could repeat on every visit.
    }
    const fresh = status.steps.some(s => s.id === 'nodes' && !s.done);
    if (fresh && !opened && ['/', '/home', '/home/index'].includes(location.pathname.toLowerCase())) {
      location.replace('/Setup');
      return true;
    }
    return false;
  }

  // The secret guard in one line, for the security notice and the Setting tab.
  function securityStatusText(s) {
    const masking = s.maskingEnabled
      ? (isEn ? `Secret masking on${s.passthroughMasking ? ', including passthrough requests' : ', except passthrough requests'}` : `Penyamaran secret aktif${s.passthroughMasking ? ', termasuk request passthrough' : ', kecuali request passthrough'}`)
      : t('Penyamaran secret NONAKTIF, aktifkan lagi di Pengaturan → Keamanan & privasi', 'Secret masking OFF, turn it back on in Setting → Security & privacy');
    const count = n => Number(n || 0).toLocaleString(locale);
    return (isEn ? `${masking}. Since AIT started: ${count(s.masked)} secrets masked, ${count(s.restored)} put back when writing, ` : `${masking}. Sejak AIT berjalan: ${count(s.masked)} secret disamarkan, ${count(s.restored)} dipasang kembali saat menulis, `)
      + (isEn ? `${count(s.blockedReads)} reads of credential files blocked.` : `${count(s.blockedReads)} akses ke file kredensial ditolak.`);
  }

  // Security & privacy: opens by itself until it is closed once on this installation (the server remembers it, so
  // another browser does not show it again), and from the sidebar button. Resolves when it is closed, or at once when
  // it does not open, so the next first-run dialog never stacks on it.
  async function initSecurityNotice() {
    const modalElement = document.getElementById('securityNoticeModal');
    if (!modalElement) return;
    const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
    const status = document.getElementById('securityNoticeStatus');
    let acknowledged = true;

    async function refresh() {
      try {
        const s = await api.get('/api/security');
        acknowledged = Boolean(s.noticeAcknowledgedAt);
        status.textContent = securityStatusText(s);
        return s;
      } catch {
        return null; // Server or database not ready; the notice tries again on a later page load.
      }
    }

    // keepalive: the request survives a link out of the dialog (Setting, Docs).
    function acknowledge() {
      if (acknowledged) return;
      acknowledged = true;
      fetch('/api/security/acknowledge', { method: 'POST', keepalive: true }).catch(() => { /* asked again next time */ });
    }

    modalElement.addEventListener('hidden.bs.modal', acknowledge);
    modalElement.addEventListener('click', event => {
      if (event.target.closest('a[href]')) acknowledge();
    });
    document.getElementById('securityNoticeOpen')?.addEventListener('click', () => {
      refresh();
      modal.show();
    });

    const first = await refresh();
    if (!first || first.noticeAcknowledgedAt) return;
    const closed = new Promise(resolve => modalElement.addEventListener('hidden.bs.modal', resolve, { once: true }));
    modal.show();
    await closed;
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
        toast((isEn ? `Hello, ${saved.developerName}!` : `Halo, ${saved.developerName}!`));
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

  // Menus whose page has sections (Memori, Pengaturan, Log & diagnostik): the parent only folds/unfolds its submenu,
  // and each submenu link opens one section (the page's hidden tab). Folding or unfolding the other menus by hand is
  // remembered per menu on this browser.
  function initNavGroups() {
    const groups = Array.from(document.querySelectorAll('.app-nav-group'));
    const storeKey = group => `aithread.nav.${group.dataset.navGroup}`;
    const setOpen = (group, open) => {
      group.classList.toggle('open', open);
      group.querySelector('[data-nav-toggle]').setAttribute('aria-expanded', String(open));
    };
    groups.forEach(group => {
      let stored = null;
      try { stored = localStorage.getItem(storeKey(group)); } catch { /* not remembered */ }
      // The open page has no tabs of its own, so its submenu is always shown on load.
      setOpen(group, !!group.querySelector('.app-nav-link.active') || stored === 'open');
      group.querySelector('[data-nav-toggle]').addEventListener('click', () => {
        const open = !group.classList.contains('open');
        setOpen(group, open);
        try { localStorage.setItem(storeKey(group), open ? 'open' : 'closed'); } catch { /* not remembered */ }
      });
    });

    // On the menu's own page a submenu link only changes the hash: the hash picks the tab, and the open tab marks its link.
    const current = groups.find(group => group.querySelector('.app-nav-link.active'));
    if (!current) return;
    const tabs = () => Array.from(document.querySelectorAll('.nav-tabs [data-bs-toggle="tab"][data-tab]'));
    const mark = name => current.querySelectorAll('.app-nav-sublink')
      .forEach(link => link.classList.toggle('active', link.dataset.subTab === name));
    const showHashTab = () => {
      const name = decodeURIComponent(location.hash.slice(1));
      const tab = tabs().find(button => button.dataset.tab === name);
      if (tab) bootstrap.Tab.getOrCreateInstance(tab).show();
    };
    document.addEventListener('shown.bs.tab', event => {
      const name = event.target.dataset?.tab;
      if (name === undefined) return;
      mark(name);
      // The hash follows the open tab, so a later click on that tab's submenu link still changes the hash.
      history.replaceState(null, '', name ? `#${name}` : location.pathname + location.search);
    });
    window.addEventListener('hashchange', showHashTab);
    showHashTab();
    mark(tabs().find(button => button.classList.contains('active'))?.dataset.tab);
  }

  document.addEventListener('DOMContentLoaded', () => {
    initWindows();
    initNavGroups();
    initThemeToggle();
    initLanguageSelect();
    initUpdateBar();
    initNpmUpdate();
    initOnboarding();
    initSetupStatus().then(async leaving => {
      if (leaving) return;
      await initSecurityNotice();
      initDeveloperName();
    });
    refreshRouterPill();
    setInterval(refreshRouterPill, 20000);
  });

  window.AIThread = { lang, isEn, locale, t, api, toast, escapeHtml, winControls, winFrame, fmt, toDate, statusBadge, isActiveStatus, sourceLabel, statusDot, stream, debounce, md, diffHtml, threadRow, bindRowLinks, renderRouterPill, routerKeyState, pager, renderSetupStatus, securityStatusText };
})();
