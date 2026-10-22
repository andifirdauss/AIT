(() => {
  'use strict';
  const { api, toast, escapeHtml, fmt, threadRow, bindRowLinks, stream, debounce, pager } = window.AIThread;
  const projectId = window.projectId;
  const el = id => document.getElementById(id);
  const sessionPager = pager('sessionPager', rows => {
    el('sessionRows').innerHTML = rows.length
      ? rows.map(s => `<tr><td class="mono" title="${escapeHtml(s.sessionId)}">${escapeHtml(s.sessionId.substring(0, 8))}</td><td>${s.threadCount}</td><td>${fmt.relative(s.lastSeenAt)}</td></tr>`).join('')
      : '<tr><td colspan="3" class="text-secondary">Belum ada sesi Claude Code.</td></tr>';
  });

  const threadPager = pager('threadPager', rows => {
    el('threadRows').innerHTML = rows.length
      ? rows.map(threadRow).join('')
      : '<tr><td colspan="8" class="empty-state">Belum ada thread.</td></tr>';
  });

  async function loadDetail() {
    try {
      const detail = await api.get(`/api/projects/${projectId}`);
      const p = detail.project;
      el('projectName').textContent = p.name;
      el('projectPath').textContent = p.rootPath + (p.exists ? '' : '  (folder tidak ditemukan)');
      el('projectStacks').innerHTML = p.stacks.map(s => `<span class="badge text-bg-primary me-1">${escapeHtml(s)}</span>`).join('');
      el('statThreads').textContent = fmt.number(p.threadCount);
      el('statSessions').textContent = fmt.number(p.sessionCount);
      el('statMemory').textContent = fmt.number(p.memoryCount);
      el('statLastSeen').textContent = fmt.relative(p.lastSeenAt);
      renderKey(p);

      el('projectModules').innerHTML = detail.modules.length
        ? detail.modules.map(m => `<li class="list-group-item">${escapeHtml(m)}</li>`).join('')
        : '<li class="list-group-item text-secondary">Tidak ada file solution/build terbaca.</li>';

      sessionPager.set(detail.sessions);
    } catch (error) {
      el('projectName').textContent = error.message;
    }
  }

  const keySources = {
    File: 'dari .aithread/project.json di repository',
    Manual: 'key manual',
    GitRemote: 'dari git remote',
    Local: 'lokal — memory hanya ada di mesin ini. Tambah git remote, file .aithread/project.json, atau isi key manual.'
  };

  function renderKey(p) {
    el('projectKey').textContent = p.key;
    el('projectKeySource').textContent = keySources[p.keySource] || p.keySource;
    el('projectKeySource').className = `small mb-2 ${p.keySource === 'Local' ? 'text-warning' : 'text-secondary'}`;
    // Do not overwrite what is being typed when an SSE refresh arrives.
    if (document.activeElement !== el('manualKey')) el('manualKey').value = p.manualKey || '';
  }

  el('btnSaveKey').addEventListener('click', async () => {
    try {
      const project = await api.put(`/api/projects/${projectId}/key`, { key: el('manualKey').value.trim() || null });
      toast(`Project key: ${project.key}`);
      el('manualKey').blur();
      loadDetail();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  async function loadThreads() {
    try {
      threadPager.set(await api.get(`/api/threads?take=50&projectId=${projectId}`));
    } catch (error) {
      el('threadRows').innerHTML = `<tr><td colspan="8" class="empty-state text-danger">${escapeHtml(error.message)}</td></tr>`;
    }
  }

  // --- Modules, code map and merges: the first layer of memory labels ---

  const moduleModal = bootstrap.Modal.getOrCreateInstance(el('moduleModal'));
  const codeMapModal = bootstrap.Modal.getOrCreateInstance(el('codeMapModal'));
  const sources = { Heuristic: 'aturan', AI: 'AI', Manual: 'manual' };
  const mergeStatuses = { Applied: 'digabung', Undone: 'dibatalkan', Suggested: 'saran', Rejected: 'ditolak' };
  let modules = [];
  let codeMapQuery = null;

  const modulePager = pager('modulePager', rows => {
    el('moduleRows').innerHTML = rows.length
      ? rows.map(m => `<tr>
          <td><span class="fw-semibold">${escapeHtml(m.name)}</span>${m.description ? `<div class="small text-secondary">${escapeHtml(m.description)}</div>` : ''}</td>
          <td class="small">${m.aliases.map(a => `<span class="chip">${escapeHtml(a)}</span>`).join('') || '<span class="text-secondary">–</span>'}</td>
          <td><a href="/Memory?projectId=${projectId}" class="text-decoration-none">${fmt.number(m.memories)}</a></td>
          <td><button type="button" class="btn btn-link btn-sm p-0" data-files="${escapeHtml(m.name)}">${fmt.number(m.files)}</button></td>
          <td class="text-end"><button type="button" class="btn btn-sm btn-outline-secondary" data-module="${escapeHtml(m.name)}">Atur</button></td>
        </tr>`).join('')
      : '<tr><td colspan="5" class="text-secondary">Belum ada modul. Modul terbentuk dari peta kode, memory, dan wiki bisnis.</td></tr>';
  });

  const codeMapPager = pager('codeMapPager', rows => {
    el('codeMapRows').innerHTML = rows.length
      ? rows.map(e => `<tr>
          <td class="mono small text-break">${escapeHtml(e.path)}</td>
          <td>${escapeHtml(e.module)}</td>
          <td><span class="badge text-bg-light border">${e.layer}</span></td>
          <td class="small">${escapeHtml(e.area || '–')}</td>
          <td class="small ${e.source === 'Manual' ? 'fw-semibold' : 'text-secondary'}">${sources[e.source] || e.source}</td>
          <td class="text-end"><button type="button" class="btn btn-sm btn-outline-secondary" data-path="${escapeHtml(e.path)}" data-module="${escapeHtml(e.module)}" data-layer="${e.layer}">Ubah</button></td>
        </tr>`).join('')
      : '<tr><td colspan="6" class="text-secondary">Belum ada file terpetakan. Tekan Scan, atau biarkan terisi dari thread berikutnya.</td></tr>';
  });

  const mergePager = pager('mergePager', rows => {
    el('mergeRows').innerHTML = rows.length
      ? rows.map(g => `<div class="list-group-item d-flex flex-wrap align-items-center gap-2">
          <div class="me-auto">
            <span class="fw-semibold">${escapeHtml(g.from)}</span> → <span class="fw-semibold">${escapeHtml(g.into)}</span>
            <span class="badge text-bg-light border ms-1">${mergeStatuses[g.status] || g.status}</span>
            <div class="text-secondary">${fmt.relative(g.decidedAt || g.createdAt)}${g.decidedBy ? ` · ${escapeHtml(g.decidedBy)}` : ''}${g.status === 'Applied' ? ` · ${fmt.number(g.memories)} memory, ${fmt.number(g.files)} file, ${fmt.number(g.pages)} halaman` : ''}${g.reason ? ` · ${escapeHtml(g.reason)}` : ''}</div>
          </div>
          ${g.status === 'Applied' ? `<button type="button" class="btn btn-sm btn-outline-secondary" data-undo="${g.id}">Batalkan</button>` : ''}
        </div>`).join('')
      : '<div class="list-group-item text-secondary">Belum ada modul yang digabung.</div>';
  });

  async function loadModules() {
    try {
      modules = await api.get(`/api/memory/modules?projectId=${projectId}`);
      modulePager.set(modules);
      const current = el('codeMapModule').value;
      el('codeMapModule').innerHTML = '<option value="">Semua modul</option>' + modules.map(m => `<option value="${escapeHtml(m.name)}">${escapeHtml(m.name)}</option>`).join('');
      if (modules.some(m => m.name === current)) el('codeMapModule').value = current;
      el('codeMapModuleList').innerHTML = modules.map(m => `<option value="${escapeHtml(m.name)}"></option>`).join('');
    } catch (error) {
      el('moduleRows').innerHTML = `<tr><td colspan="5" class="text-danger">${escapeHtml(error.message)}</td></tr>`;
    }
  }

  async function loadCodeMap() {
    const params = new URLSearchParams();
    if (el('codeMapSearch').value.trim()) params.set('search', el('codeMapSearch').value.trim());
    if (el('codeMapModule').value) params.set('module', el('codeMapModule').value);
    try {
      const query = params.toString();
      const entries = await api.get(`/api/projects/${projectId}/codemap?${query}`);
      // A new search or module filter starts at page 1; a refresh of the same query keeps the page.
      codeMapPager.set(entries, query !== codeMapQuery);
      codeMapQuery = query;
    } catch (error) {
      el('codeMapRows').innerHTML = `<tr><td colspan="6" class="text-danger">${escapeHtml(error.message)}</td></tr>`;
    }
  }

  async function loadMerges() {
    try {
      mergePager.set(await api.get(`/api/memory/merges?projectId=${projectId}`));
    } catch (error) {
      el('mergeRows').innerHTML = `<div class="list-group-item text-danger">${escapeHtml(error.message)}</div>`;
    }
  }

  function openModule(name) {
    const module = modules.find(m => m.name === name);
    if (!module) return;
    el('moduleName').value = module.name;
    el('moduleNewName').value = module.name;
    el('moduleModalTitle').textContent = `Modul ${module.name}`;
    el('moduleDescription').value = module.description || '';
    el('moduleAliases').value = module.aliases.join(', ');
    el('moduleMergeInto').innerHTML = modules.filter(m => m.name !== module.name)
      .map(m => `<option value="${escapeHtml(m.name)}">${escapeHtml(m.name)}</option>`).join('') || '<option value="">(tidak ada modul lain)</option>';
    el('btnModuleMerge').disabled = modules.length < 2;
    moduleModal.show();
  }

  function openCodeMap(path, module, layer) {
    el('codeMapPath').value = path || '';
    el('codeMapPath').readOnly = !!path;
    el('codeMapModuleInput').value = module || '';
    el('codeMapLayer').value = layer || '';
    codeMapModal.show();
  }

  el('moduleRows').addEventListener('click', event => {
    const target = event.target.closest('button');
    if (!target) return;
    if (target.dataset.files) {
      el('codeMapModule').value = target.dataset.files;
      loadCodeMap();
      el('codeMapRows').closest('.card').scrollIntoView({ behavior: 'smooth' });
    } else if (target.dataset.module) {
      openModule(target.dataset.module);
    }
  });

  el('moduleForm').addEventListener('submit', async event => {
    event.preventDefault();
    const name = el('moduleName').value;
    try {
      const module = await api.put(`/api/memory/modules/${encodeURIComponent(name)}?projectId=${projectId}`, {
        name: el('moduleNewName').value.trim(),
        description: el('moduleDescription').value.trim(),
        aliases: el('moduleAliases').value.split(',').map(a => a.trim()).filter(Boolean)
      });
      moduleModal.hide();
      toast(module.name !== name ? `Modul ${name} → ${module.name}.` : 'Modul diperbarui.');
      loadModules();
      if (module.name !== name) loadCodeMap();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('btnModuleMerge').addEventListener('click', async () => {
    const from = el('moduleName').value;
    const into = el('moduleMergeInto').value;
    if (!into || !confirm(`Gabungkan modul ${from} ke ${into}? Memory, peta kode, dan halaman wiki ikut pindah.`)) return;
    try {
      await api.post('/api/memory/modules/merge', { projectId, from, into });
      moduleModal.hide();
      toast(`Modul ${from} digabung ke ${into}.`);
      refreshCatalog();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('btnCheckModules').addEventListener('click', async () => {
    el('btnCheckModules').disabled = true;
    try {
      const found = await api.post(`/api/memory/modules/check?projectId=${projectId}`);
      toast(found ? `${found} modul ganda digabung atau disarankan.` : 'Tidak ada modul ganda.', 'info');
      refreshCatalog();
    } catch (error) {
      toast(error.message, 'error');
    } finally {
      el('btnCheckModules').disabled = false;
    }
  });

  el('codeMapRows').addEventListener('click', event => {
    const target = event.target.closest('button[data-path]');
    if (target) openCodeMap(target.dataset.path, target.dataset.module, target.dataset.layer);
  });
  el('btnCodeMapAdd').addEventListener('click', () => openCodeMap('', '', ''));

  el('codeMapForm').addEventListener('submit', async event => {
    event.preventDefault();
    try {
      const entry = await api.put(`/api/projects/${projectId}/codemap`, {
        path: el('codeMapPath').value.trim(),
        module: el('codeMapModuleInput').value.trim(),
        layer: el('codeMapLayer').value || null
      });
      codeMapModal.hide();
      toast(`${entry.path} → ${entry.module} · ${entry.layer}`);
      refreshCatalog();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('btnCodeMapScan').addEventListener('click', async () => {
    el('btnCodeMapScan').disabled = true;
    toast('Memetakan file project… (bisa memakan waktu untuk repository besar)', 'info');
    try {
      const result = await api.post(`/api/projects/${projectId}/codemap/scan`);
      toast(`${result.mapped} dari ${result.files} file terpetakan ke ${result.modules} modul.`);
      refreshCatalog();
    } catch (error) {
      toast(error.message, 'error');
    } finally {
      el('btnCodeMapScan').disabled = false;
    }
  });

  el('mergeRows').addEventListener('click', async event => {
    const target = event.target.closest('button[data-undo]');
    if (!target || !confirm('Batalkan gabung modul ini? Memory, peta kode, dan halaman wiki kembali ke modul asal.')) return;
    try {
      await api.post(`/api/memory/merges/${target.dataset.undo}/undo`);
      toast('Gabung modul dibatalkan.');
      refreshCatalog();
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  el('codeMapSearch').addEventListener('input', debounce(loadCodeMap, 350));
  el('codeMapModule').addEventListener('change', loadCodeMap);

  function refreshCatalog() {
    loadModules();
    loadCodeMap();
    loadMerges();
  }

  bindRowLinks(el('threadRows'));
  loadDetail();
  loadThreads();
  refreshCatalog();

  const refresh = debounce(() => { loadDetail(); loadThreads(); }, 500);
  // The page's one event stream; the unit test card (project-unittests.js) listens through a DOM event.
  stream('types=thread.created,thread.updated,memory,project', {
    'thread.created': refresh,
    'thread.updated': payload => {
      refresh();
      document.dispatchEvent(new CustomEvent('ait:thread-updated', { detail: payload }));
    },
    'memory.changed': debounce(() => { loadDetail(); refreshCatalog(); }, 800),
    'project.changed': debounce(loadDetail, 500)
  });
})();
