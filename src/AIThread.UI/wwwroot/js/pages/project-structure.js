// Project > Detail → Struktur kode: the project's folders and declarations, loaded a level at a time.
// Folder view: folders → files → types → members. Class view: namespaces (or folders) → types (partial classes merged)
// → members. A declaration row copies "path:line" for pasting into a prompt.
(() => {
  'use strict';
  const { isEn, t, api, toast, escapeHtml, fmt, debounce } = window.AIThread;
  const projectId = window.projectId;
  const el = id => document.getElementById(id);
  const base = `/api/projects/${projectId}/structure`;
  const tree = el('structureTree');
  const viewKey = 'aithread.structure.view';

  const typeBadges = { class: 'C', interface: 'I', enum: 'E', struct: 'S', record: 'R', trait: 'T', delegate: 'D', object: 'O', annotation: '@' };
  const memberBadges = { method: 'm', constructor: 'c', property: 'p', event: 'e', function: 'f' };
  const languageBadges = { 'C#': 'cs', PHP: 'php', JavaScript: 'js', TypeScript: 'ts', Python: 'py', Java: 'java', Kotlin: 'kt' };

  let view = 'folder';
  try { if (localStorage.getItem(viewKey) === 'class') view = 'class'; } catch { /* default view */ }
  el(view === 'class' ? 'structureViewClass' : 'structureViewFolder').checked = true;

  const url = (path, params = {}) => `${base}${path}?${new URLSearchParams(params)}`;

  // --- Rows ---

  // A branch: children are fetched by load(node) the first time it opens.
  function branch({ badge, badgeClass = '', name, meta = '', dim = false, data = {} }) {
    const attrs = Object.entries(data).map(([k, v]) => ` data-${k}="${escapeHtml(v ?? '')}"`).join('');
    return `<div class="st-node${dim ? ' st-dim' : ''}" role="treeitem" aria-expanded="false"${attrs}>
      <button type="button" class="st-row"><span class="st-caret" aria-hidden="true"></span>${badgeHtml(badge, badgeClass)}<span class="st-name">${escapeHtml(name)}</span><span class="st-meta">${meta}</span></button>
      <div class="st-children" role="group"></div>
    </div>`;
  }

  // A declaration: clicking copies where it is.
  function leaf({ badge, badgeClass = '', name, meta = '', ref, dim = false }) {
    return (isEn ? `<div class="st-node st-leaf${dim ? ' st-dim' : ''}" role="treeitem">
      <button type="button" class="st-row" data-ref="${escapeHtml(ref)}" title="Click to copy ${escapeHtml(ref)}"><span class="st-caret" aria-hidden="true"></span>${badgeHtml(badge, badgeClass)}<span class="st-name">${escapeHtml(name)}</span><span class="st-meta">${meta}</span></button>
    </div>` : `<div class="st-node st-leaf${dim ? ' st-dim' : ''}" role="treeitem">
      <button type="button" class="st-row" data-ref="${escapeHtml(ref)}" title="Klik untuk menyalin ${escapeHtml(ref)}"><span class="st-caret" aria-hidden="true"></span>${badgeHtml(badge, badgeClass)}<span class="st-name">${escapeHtml(name)}</span><span class="st-meta">${meta}</span></button>
    </div>`);
  }

  // A preloaded branch (a type inside an opened file): its members are already known.
  function openBranch(args, childrenHtml) {
    return branch(args).replace('<div class="st-children" role="group"></div>', `<div class="st-children" role="group" data-loaded="1">${childrenHtml}</div>`);
  }

  const badgeHtml = (text, cls) => text ? `<span class="st-badge ${cls}">${escapeHtml(text)}</span>` : '';
  const note = (text, cls = 'text-secondary') => `<div class="st-note ${cls}">${text}</div>`;
  const count = (n, word) => `${fmt.number(n)} ${word}`;

  // A function whose calls the tools node has read opens onto them; any other member copies where it is.
  function memberLeaf(member, path) {
    const where = member.path || path;
    const args = {
      badge: memberBadges[member.kind] || 'm',
      badgeClass: 't-member',
      name: member.name,
      meta: `<span class="st-sig">${escapeHtml(member.signature || '')}</span> · ${member.path ? escapeHtml(fileName(member.path)) : ''}:${member.line}`
        + (member.calls != null ? ` · <span class="st-calls">${count(member.calls, 'panggilan')}</span>` : ''),
      ref: `${where}:${member.line}`
    };
    return member.key && member.calls ? branch({ ...args, data: { load: 'calls', key: member.key } }) : leaf(args);
  }

  function typeBranch(type, path) {
    const meta = `${escapeHtml(type.kind)}${type.isPartial ? ' · partial' : ''}${type.container ? (isEn ? ` · in ${escapeHtml(type.container)}` : ` · di ${escapeHtml(type.container)}`) : ''} · ${count(type.members.length, 'member')} · :${type.line}`;
    const head = leaf({ badge: '↳', name: 'deklarasi', meta: `${escapeHtml(path)}:${type.line}`, ref: `${path}:${type.line}` });
    return openBranch({ badge: typeBadges[type.kind] || 'C', badgeClass: 't-type', name: type.name, meta }, head + type.members.map(m => memberLeaf(m, path)).join(''));
  }

  const fileName = path => path.split('/').pop();

  // --- Loaders ---

  async function loadFolder(path) {
    const folder = await api.get(url('/folder', { path }));
    const folders = folder.folders.map(f => branch({
      badge: '▤',
      badgeClass: 't-folder',
      name: f.name,
      meta: `${count(f.files, 'file')}${f.types ? ` · ${count(f.types, 'type')}` : ''}${f.thirdParty ? ' · pihak ketiga' : ''}`,
      dim: f.thirdParty,
      data: { load: 'folder', path: f.path }
    })).join('');
    const files = folder.files.map(fileRow).join('');
    const hidden = folder.hiddenFiles ? note((isEn ? `${fmt.number(folder.hiddenFiles)} more files not shown; use the search.` : `${fmt.number(folder.hiddenFiles)} file lain tidak ditampilkan; pakai pencarian.`)) : '';
    return folders + files + hidden || note(t('Folder kosong.', 'Empty folder.'));
  }

  function fileRow(f) {
    const badge = languageBadges[f.language] || '·';
    if (!f.parsed) {
      const why = f.thirdParty ? (isEn ? 'third party' : 'pihak ketiga') : f.language ? t('tidak dibaca', 'not read') : '';
      return leaf({ badge, name: f.name, meta: why, ref: `${f.path}:1`, dim: f.thirdParty });
    }
    if (!f.types && !f.members) return leaf({ badge, badgeClass: 't-file', name: f.name, meta: t('tanpa deklarasi', 'no declarations'), ref: `${f.path}:1` });
    return branch({
      badge,
      badgeClass: 't-file',
      name: f.name,
      meta: `${f.types ? count(f.types, 'type') : ''}${f.types && f.members ? ' · ' : ''}${f.members ? count(f.members, 'member') : ''}`,
      data: { load: 'file', path: f.path }
    });
  }

  async function loadFile(path) {
    const file = await api.get(url('/file', { path }));
    const types = file.types.map(t => typeBranch(t, file.path)).join('');
    const functions = file.functions.map(m => memberLeaf(m, file.path)).join('');
    return types + functions || note(t('Tidak ada class atau fungsi di file ini.', 'No class or function in this file.'));
  }

  async function loadGroups() {
    const groups = await api.get(url('/groups'));
    if (!groups.length) return note(t('Belum ada class terbaca di project ini.', 'No class read in this project yet.'));
    return groups.map(g => branch({
      badge: g.isFolder ? '▤' : '{}',
      badgeClass: g.isFolder ? 't-folder' : 't-ns',
      name: g.name,
      meta: `${count(g.types, 'type')}${g.isFolder ? t(' · folder (tanpa namespace)', ' · folder (no namespace)') : ''}`,
      data: { load: 'group', group: g.key }
    })).join('');
  }

  async function loadTypes(group) {
    const types = await api.get(url('/types', { group }));
    return types.map(t => branch({
      badge: typeBadges[t.kind] || 'C',
      badgeClass: 't-type',
      name: t.container ? `${t.container}.${t.name}` : t.name,
      meta: `${escapeHtml(t.kind)}${t.isPartial ? ' · partial' : ''}${t.files > 1 ? ` · ${count(t.files, 'file')}` : ''} · ${count(t.members, 'member')}`,
      data: { load: 'class', group, name: t.name, container: t.container || '' }
    })).join('') || note(t('Tidak ada type.', 'No type.'));
  }

  async function loadClass(group, name, container) {
    const type = await api.get(url('/class', { group, name, container }));
    const fileLeaf = f => leaf({ badge: '↳', name: fileName(f.path), meta: `${escapeHtml(f.path)}:${f.line}`, ref: `${f.path}:${f.line}` });
    // One file: its declaration line; a partial class: its files folded below the members.
    const files = type.files.length === 1
      ? fileLeaf(type.files[0])
      : openBranch({ badge: '↳', name: (isEn ? `Declared in ${type.files.length} file(s)` : `Dideklarasikan di ${type.files.length} file`), meta: '' }, type.files.map(fileLeaf).join(''));
    const members = type.members.map(m => memberLeaf(m, type.files[0]?.path || '')).join('');
    return type.files.length === 1 ? files + members : (members || note(t('Tidak ada member.', 'No member.'))) + files;
  }

  const callBadges = { Function: 'f', Method: 'm', Http: 'http', View: 'view', Sql: 'sql', Other: '·' };

  // What a function calls (read by the tools node); each call points at its declaration when AIT found it.
  async function loadCalls(key) {
    const fn = await api.get(url('/calls', { key }));
    const head = leaf({ badge: '↳', name: 'deklarasi', meta: `${escapeHtml(fn.path)}:${fn.startLine}-${fn.endLine}`, ref: `${fn.path}:${fn.startLine}` });
    const calls = fn.calls.map(c => leaf({
      badge: callBadges[c.kind] || '·',
      badgeClass: 't-call',
      name: c.target,
      meta: c.resolvedPath
        ? `→ ${escapeHtml(c.resolvedSymbol || fileName(c.resolvedPath))} · ${escapeHtml(c.resolvedPath)}:${c.resolvedLine ?? 1}`
        : (isEn ? `:${c.line} · not found in the project yet` : `:${c.line} · belum ditemukan di project`),
      ref: c.resolvedPath ? `${c.resolvedPath}:${c.resolvedLine ?? 1}` : `${fn.path}:${c.line}`,
      dim: !c.resolvedPath
    })).join('');
    const changed = fn.changed ? note(t('Isi function sudah berubah sejak dibaca; dibaca ulang saat planner membacanya atau thread mengubahnya.', 'The function body changed since it was read; it is read again when the planner reads it or a thread changes it.'), 'text-warning') : '';
    return head + (calls || note(t('Tidak memanggil kode project.', 'Calls no project code.'))) + changed
      + note((isEn ? `Read ${fmt.relative(fn.scannedAt)}${fn.model ? ` by ${escapeHtml(fn.model)}` : ''}.` : `Dibaca ${fmt.relative(fn.scannedAt)}${fn.model ? ` oleh ${escapeHtml(fn.model)}` : ''}.`));
  }

  const loaders = {
    folder: node => loadFolder(node.dataset.path),
    file: node => loadFile(node.dataset.path),
    group: node => loadTypes(node.dataset.group),
    class: node => loadClass(node.dataset.group, node.dataset.name, node.dataset.container),
    calls: node => loadCalls(node.dataset.key)
  };

  async function toggle(node) {
    const children = node.querySelector(':scope > .st-children');
    const open = node.classList.toggle('st-open');
    node.setAttribute('aria-expanded', String(open));
    if (!open || children.dataset.loaded) return;
    children.dataset.loaded = '1';
    children.innerHTML = note(t('Memuat…', 'Loading…'));
    try {
      children.innerHTML = await loaders[node.dataset.load](node);
    } catch (error) {
      delete children.dataset.loaded;
      children.innerHTML = note(escapeHtml(error.message), 'text-danger');
    }
  }

  async function copy(ref) {
    try {
      await navigator.clipboard.writeText(ref);
      toast((isEn ? `Copied: ${ref}` : `Disalin: ${ref}`), 'info');
    } catch {
      toast(ref, 'info');
    }
  }

  tree.addEventListener('click', event => {
    const row = event.target.closest('.st-row');
    if (!row) return;
    if (row.dataset.ref) copy(row.dataset.ref);
    else toggle(row.closest('.st-node'));
  });

  // --- Summary, root and search ---

  function renderSummary(s) {
    const languages = s.languages.map(l => `<span class="chip">${escapeHtml(l.language)} ${fmt.number(l.files)}</span>`).join('');
    el('structureSummary').innerHTML = `${count(s.folders, 'folder')} · ${count(s.files, 'file')} · <strong>${count(s.types, 'class/type')}</strong> · ${count(s.members, 'member')}`
      + (isEn ? ` · ${fmt.number(s.parsedFiles)} files read${s.thirdPartyFiles ? ` · ${fmt.number(s.thirdPartyFiles)} third-party/build files skipped` : ''}` : ` · ${fmt.number(s.parsedFiles)} file dibaca${s.thirdPartyFiles ? ` · ${fmt.number(s.thirdPartyFiles)} file pihak ketiga/build dilewati` : ''}`)
      + (isEn ? ` · scan ${fmt.number(s.scanMs)} ms (${fmt.number(s.reparsed)} files read again) · ${fmt.relative(s.scannedAt)}` : ` · scan ${fmt.number(s.scanMs)} ms (${fmt.number(s.reparsed)} file dibaca ulang) · ${fmt.relative(s.scannedAt)}`)
      + (s.functionsWithCalls ? (isEn ? ` · <span class="st-calls">${count(s.functionsWithCalls, 'function')} with their calls read</span>` : ` · <span class="st-calls">${count(s.functionsWithCalls, 'function')} sudah dibaca panggilannya</span>`) : '')
      + (languages ? `<div class="mt-1">${languages}</div>` : '')
      + (s.truncated ? t('<div class="text-warning mt-1">Project terlalu besar: hanya 100.000 file pertama yang ditampilkan.</div>', '<div class="text-warning mt-1">Project too big: only the first 100,000 files are shown.</div>') : '');
  }

  async function renderRoot() {
    if (el('structureSearch').value.trim().length >= 2) return search();
    tree.innerHTML = note(t('Memuat…', 'Loading…'));
    try {
      tree.innerHTML = view === 'class' ? await loadGroups() : await loadFolder('');
    } catch (error) {
      tree.innerHTML = note(escapeHtml(error.message), 'text-danger');
    }
  }

  async function load(rescan = false) {
    el('btnStructureRescan').disabled = true;
    if (rescan) el('structureSummary').textContent = t('Memindai ulang…', 'Scanning again…');
    try {
      renderSummary(await api.get(url('', { rescan })));
      await renderRoot();
    } catch (error) {
      el('structureSummary').innerHTML = `<span class="text-danger">${escapeHtml(error.message)}</span>`;
      tree.innerHTML = '';
    } finally {
      el('btnStructureRescan').disabled = false;
    }
  }

  const hitBadges = { type: ['C', 't-type'], member: ['m', 't-member'], file: ['·', ''] };

  async function search() {
    const q = el('structureSearch').value.trim();
    if (q.length < 2) return renderRoot();
    tree.innerHTML = note((isEn ? 'Searching…' : 'Mencari…'));
    try {
      const hits = await api.get(url('/search', { q }));
      tree.innerHTML = hits.length
        ? hits.map(h => leaf({
            badge: hitBadges[h.kind]?.[0] || '·',
            badgeClass: hitBadges[h.kind]?.[1] || '',
            name: h.name,
            meta: `${h.detail ? `${escapeHtml(h.detail)} · ` : ''}${escapeHtml(h.path)}:${h.line}`,
            ref: `${h.path}:${h.line}`
          })).join('') + (hits.length >= 100 ? note(t('Menampilkan 100 hasil teratas; perjelas pencarian.', 'Showing the top 100 results; refine the search.')) : '')
        : note((isEn ? `Nothing matches "${escapeHtml(q)}".` : `Tidak ada yang cocok dengan "${escapeHtml(q)}".`));
    } catch (error) {
      tree.innerHTML = note(escapeHtml(error.message), 'text-danger');
    }
  }

  document.querySelectorAll('input[name="structureView"]').forEach(radio => radio.addEventListener('change', () => {
    view = radio.value;
    try { localStorage.setItem(viewKey, view); } catch { /* not remembered */ }
    renderRoot();
  }));
  el('structureSearch').addEventListener('input', debounce(search, 300));
  el('btnStructureRescan').addEventListener('click', () => load(true));

  load();
})();
