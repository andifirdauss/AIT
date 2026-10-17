// Shared helpers for every AIT page: API calls, SSE, formatting, toasts and small renderers.
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
      throw new Error('Tidak dapat menghubungi server AIT.');
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
  function winControls({ kind = 'window', closeLabel = 'Tutup', disabled = [] } = {}) {
    const button = (name, label, action) => {
      const text = escapeHtml(label);
      return `<button type="button" class="win-btn win-${name}"${disabled.includes(name) ? ' disabled' : ` ${action}`} aria-label="${text}" title="${text}"></button>`;
    };
    const dismiss = kind === 'window' ? 'data-win="close"' : `data-bs-dismiss="${kind}"`;
    let html = button('close', closeLabel, dismiss);
    if (kind !== 'alert' && kind !== 'toast') {
      html += button('min', kind === 'modal' ? 'Kecilkan' : 'Lipat', 'data-win="min"')
        + button('max', 'Layar penuh', 'data-win="max"');
    }
    return `<div class="win-controls" role="group" aria-label="Kontrol jendela">${html}</div>`;
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

  // First run: a step-by-step tour (install → 9router → Setting → Node → Project → CLI → use). It opens by itself once
  // per browser and only while no node exists yet, so an instance that is already set up is not treated as new. The
  // sidebar button reopens it at the step last seen. Resolves when the tour is closed or not shown, so the developer
  // name dialog never stacks on top of it.
  function initOnboarding() {
    const modalElement = document.getElementById('onboardingModal');
    if (!modalElement) return Promise.resolve();

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
      `<button type="button" data-step="${i}" aria-label="Langkah ${i + 1}"></button>`).join('');

    const current = () => Math.max(0, items.findIndex(item => item.classList.contains('active')));
    function render(index) {
      document.getElementById('onboardingStep').textContent = `${index + 1} / ${items.length}`;
      dots.querySelectorAll('button').forEach((dot, i) => {
        dot.classList.toggle('active', i === index);
        dot.setAttribute('aria-current', i === index ? 'step' : 'false');
      });
      prev.disabled = index === 0;
      next.textContent = index === items.length - 1 ? 'Mulai pakai AIT' : 'Lanjut ›';
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

    return new Promise(async resolve => {
      if (read(doneKey)) return resolve();
      let nodes;
      try {
        nodes = await api.get('/api/nodes');
      } catch {
        return resolve(); // Server or database not ready; decide on a later page load.
      }
      if (nodes.length) {
        write(doneKey, '1');
        return resolve();
      }
      modalElement.addEventListener('hidden.bs.modal', () => resolve(), { once: true });
      modal.show();
    });
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
    initWindows();
    initThemeToggle();
    initUpdateBar();
    initNpmUpdate();
    initOnboarding().then(initDeveloperName);
    refreshRouterPill();
    setInterval(refreshRouterPill, 20000);
  });

  window.AiThread = { api, toast, escapeHtml, winControls, winFrame, fmt, toDate, statusBadge, isActiveStatus, sourceLabel, statusDot, stream, debounce, md, diffHtml, threadRow, bindRowLinks, renderRouterPill, routerKeyState };
})();
