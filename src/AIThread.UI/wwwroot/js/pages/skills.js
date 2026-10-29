(() => {
  'use strict';
  const { isEn, t, api, toast, escapeHtml, fmt, stream, debounce, pager } = window.AIThread;
  const el = id => document.getElementById(id);

  const skillStates = {
    Queued: ['secondary', (isEn ? 'Queued' : 'Antre')],
    Fetching: ['info', (isEn ? 'Downloading…' : 'Mengunduh…')],
    Analyzing: ['info', (isEn ? 'Analysed by AI…' : 'Dianalisis AI…')],
    Ready: ['success', (isEn ? 'Ready' : 'Siap')],
    Failed: ['danger', t('Gagal', 'Failed')]
  };
  const skillKinds = { GitHub: 'GitHub', GitLab: 'GitLab', Git: 'git', RawFile: 'file', Web: 'web' };
  const skillModal = bootstrap.Modal.getOrCreateInstance(el('skillModal'));

  async function loadSkillModels(selected) {
    const select = el('skillModel');
    let html = t('<option value="">Otomatis — model node Planner</option>', '<option value="">Automatic — the Planner node model</option>');
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
    return (isEn ? `<tr>
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
        <button type="button" class="btn btn-outline-danger btn-sm" data-skill-delete="${s.id}" data-name="${escapeHtml(s.name)}">Delete</button>
      </td>
    </tr>` : `<tr>
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
    </tr>`);
  }

  const skillSourcePager = pager('skillSourcePager', rows => {
    el('skillSourceRows').innerHTML = rows.length
      ? rows.map(skillSourceRow).join('')
      : t('<tr><td colspan="5" class="text-secondary small">Belum ada sumber — tempel link repository skill di atas, mis. https://github.com/anthropics/skills.</td></tr>', '<tr><td colspan="5" class="text-secondary small">No source yet — paste a skill repository link above, e.g. https://github.com/anthropics/skills.</td></tr>');
  });

  async function loadSkillSources() {
    try {
      const sources = await api.get('/api/skills/sources');
      skillSourcePager.set(sources);
      const busy = sources.filter(s => ['Queued', 'Fetching', 'Analyzing'].includes(s.status)).length;
      el('skillSourceSummary').textContent = `${sources.length} sumber${busy ? (isEn ? ` · ${busy} being processed` : ` · ${busy} sedang diproses`) : ''}`;
    } catch (error) {
      skillSourcePager.set([]);
      el('skillSourceRows').innerHTML = `<tr><td colspan="5" class="text-danger small">${escapeHtml(error.message)}</td></tr>`;
    }
  }

  function skillRow(s) {
    const tags = s.tags ? `<div class="small text-secondary text-truncate" title="${escapeHtml(s.tags)}">🏷️ ${escapeHtml(s.tags)}</div>` : '';
    return (isEn ? `<tr>
      <td>
        <div class="fw-semibold">${escapeHtml(s.name)}</div>
        <div class="small text-secondary">${escapeHtml(s.description)}</div>
        ${tags}
      </td>
      <td class="small text-nowrap">${escapeHtml(s.sourceName)}</td>
      <td class="small text-nowrap">${s.nodeCount ? `${s.nodeCount} node` : '–'}</td>
      <td class="text-end"><button type="button" class="btn btn-outline-secondary btn-sm" data-skill-view="${s.id}">View</button></td>
    </tr>` : `<tr>
      <td>
        <div class="fw-semibold">${escapeHtml(s.name)}</div>
        <div class="small text-secondary">${escapeHtml(s.description)}</div>
        ${tags}
      </td>
      <td class="small text-nowrap">${escapeHtml(s.sourceName)}</td>
      <td class="small text-nowrap">${s.nodeCount ? `${s.nodeCount} node` : '–'}</td>
      <td class="text-end"><button type="button" class="btn btn-outline-secondary btn-sm" data-skill-view="${s.id}">Lihat</button></td>
    </tr>`);
  }

  let skillQuery = '';
  const skillPager = pager('skillPager', rows => {
    el('skillRows').innerHTML = rows.length
      ? rows.map(skillRow).join('')
      : `<tr><td colspan="4" class="text-secondary small">${skillQuery ? t('Tidak ada skill yang cocok.', 'No matching skill.') : t('Gudang masih kosong.', 'The store is still empty.')}</td></tr>`;
  });

  async function loadSkills() {
    const query = el('skillSearch').value.trim();
    try {
      const skills = await api.get(`/api/skills${query ? `?query=${encodeURIComponent(query)}` : ''}`);
      const reset = query !== skillQuery;
      skillQuery = query;
      skillPager.set(skills, reset);
      el('skillCount').textContent = `${skills.length} skill`;
    } catch (error) {
      skillPager.set([]);
      el('skillRows').innerHTML = `<tr><td colspan="4" class="text-danger small">${escapeHtml(error.message)}</td></tr>`;
    }
  }

  async function showSkill(id) {
    el('skillModalTitle').textContent = t('Memuat…', 'Loading…');
    el('skillModalBody').innerHTML = '';
    skillModal.show();
    try {
      const s = await api.get(`/api/skills/${id}`);
      el('skillModalTitle').textContent = s.name;
      const files = s.files.length
        ? (isEn ? `<details class="mb-2"><summary class="small">${s.files.length} file(s) in the skill folder</summary><div class="small mono">${s.files.map(escapeHtml).join('<br>')}</div></details>` : `<details class="mb-2"><summary class="small">${s.files.length} file di folder skill</summary><div class="small mono">${s.files.map(escapeHtml).join('<br>')}</div></details>`)
        : '';
      el('skillModalBody').innerHTML = (isEn ? `
        <p class="mb-1">${escapeHtml(s.description)}</p>
        <div class="small text-secondary mb-1">Source: <a href="${escapeHtml(s.sourceUrl)}" target="_blank" rel="noopener">${escapeHtml(s.sourceName)}</a></div>
        ${s.tags ? `<div class="small text-secondary mb-1">🏷️ ${escapeHtml(s.tags)}</div>` : ''}
        <div class="small text-secondary mb-1">Used by nodes: ${s.nodeNames.length ? s.nodeNames.map(escapeHtml).join(', ') : '–'}</div>
        <div class="small text-secondary mono mb-2">${escapeHtml(s.folder)}</div>
        ${files}
        <pre class="border rounded p-2 small mb-0" style="white-space: pre-wrap">${escapeHtml(s.content)}</pre>` : `
        <p class="mb-1">${escapeHtml(s.description)}</p>
        <div class="small text-secondary mb-1">Sumber: <a href="${escapeHtml(s.sourceUrl)}" target="_blank" rel="noopener">${escapeHtml(s.sourceName)}</a></div>
        ${s.tags ? `<div class="small text-secondary mb-1">🏷️ ${escapeHtml(s.tags)}</div>` : ''}
        <div class="small text-secondary mb-1">Dipakai node: ${s.nodeNames.length ? s.nodeNames.map(escapeHtml).join(', ') : '–'}</div>
        <div class="small text-secondary mono mb-2">${escapeHtml(s.folder)}</div>
        ${files}
        <pre class="border rounded p-2 small mb-0" style="white-space: pre-wrap">${escapeHtml(s.content)}</pre>`);
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
      toast(t('Pengaturan Skill tersimpan.', 'Skill settings saved.'));
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
      toast(t('Link ditambahkan — Skill Orchestrator sedang memeriksa dan menarik skill-nya.', 'Link added — the Skill Orchestrator is checking it and pulling its skills.'), 'info');
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
    if (skillDelete && !confirm((isEn ? `Delete source "${name}" with all its skills? They are also detached from the nodes using them.` : `Hapus sumber "${name}" beserta semua skill-nya? Skill ini juga dilepas dari node yang memakainya.`))) return;
    button.disabled = true;
    try {
      if (skillSync) {
        await api.post(`/api/skills/sources/${skillSync}/sync`);
        toast(t('Sumber masuk antrean untuk ditarik ulang.', 'Source queued to be pulled again.'), 'info');
      } else {
        await api.del(`/api/skills/sources/${skillDelete}`);
        toast(t('Sumber skill dihapus.', 'Skill source deleted.'), 'info');
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
  stream('types=skill', { 'skill.changed': refreshSkills });

  loadSkillSettings();
  loadSkillSources();
  loadSkills();
})();
