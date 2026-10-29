import { libraryRoute, campaignEntries, filterLibrary } from './library-state.js';
import { mountCampaignEditor } from './campaign-editor.js';

const $ = id => document.getElementById(id);
const node = (tag, text, className) => { const el = document.createElement(tag); if (text) el.textContent = text; if (className) el.className = className; return el; };
const link = (text, href) => { const el = node('a', text, 'library-button'); el.href = href; return el; };
const button = (text, action) => { const el = node('button', text); el.type = 'button'; el.addEventListener('click', action); return el; };
let route = libraryRoute(location.hash), library = null, requestNumber = 0, playing = false;
let editor;
async function api(path, options) {
  const response = await fetch(`/api/library${path}`, { cache: 'no-store', ...options });
  if (response.status === 204) return null;
  const data = await response.json();
  if (!response.ok) throw new Error(data.detail ?? data.errors?.[0]?.message ?? 'Library request failed. Please retry.');
  return data;
}
function showMode() {
  const browsing = ['home', 'scenarios', 'scenario', 'campaign'].includes(route.mode);
  globalThis.delveSetDesignerMode?.(route.mode === 'designer');
  $('library-main').hidden = !browsing;
  $('campaign-editor').hidden = route.mode !== 'campaigns-edit';
  $('designer-main').hidden = route.mode !== 'designer';
  $('playtest-main').hidden = route.mode !== 'play';
  $('playtest-tools').hidden = route.mode !== 'play';
  document.body.classList.toggle('designing', route.mode === 'designer');
  document.body.classList.toggle('browsing', browsing || route.mode === 'campaigns-edit');
  for (const [id, mode] of [['home', 'home'], ['scenarios', 'scenarios'], ['designer', 'designer'], ['playtest', 'play']]) $('mode-' + id).setAttribute('aria-pressed', String(route.mode === mode));
  document.title = `Project Delve${route.mode === 'designer' ? ' · Designer' : route.mode === 'play' ? ' · Play' : ''}`;
}
function navigate(mode, id) {
  const next = '#' + mode + (id ? '/' + encodeURIComponent(id) : '');
  if (location.hash === next) { showMode(); return; }
  location.hash = next;
}
globalThis.delveNavigate = navigate;
globalThis.delveDesignerReady = showMode;
editor = mountCampaignEditor($('campaign-editor'), {
  onSaved: campaign => { library = null; navigate(campaign ? 'campaign' : 'home', campaign?.id); },
  onCancel: () => navigate('home')
});
function art(cover, name) {
  const frame = node('div', null, 'library-art');
  const mark = node('span', '◇', 'library-art-mark'); mark.setAttribute('aria-hidden', 'true'); frame.append(mark);
  if (cover) {
    const img = node('img'); img.src = cover; img.alt = ''; img.loading = 'lazy'; img.referrerPolicy = 'no-referrer';
    img.addEventListener('error', () => img.remove()); frame.append(img);
  }
  return frame;
}
function sourceLabel(source) { return source === 'bundled' ? 'Bundled' : source === 'override' ? 'Local override' : 'Local'; }
function restoreControl(entry, kind, label = 'Restore bundled version') {
  const control = node('div', null, 'library-restore-control');
  const start = button(label + '…', () => {
    start.hidden = true;
    const confirm = button('Confirm restore', async () => {
      confirm.disabled = true;
      try {
        await api(`/${kind}/${encodeURIComponent(entry.id)}/restore`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ revision: entry.revision, changeToken: entry.changeToken }) });
        globalThis.dispatchEvent?.(new Event('delve-library-changed')); await refresh();
      } catch (error) { $('library-status').textContent = error.message; confirm.disabled = false; }
    }); confirm.dataset.restoreId = entry.id;
    control.append(node('p', 'Use the bundled version and discard any local override for this entry?'), confirm, button('Cancel', () => render()));
  });
  control.append(start); return control;
}
function renderHidden() {
  const target = $('library-hidden'); if (!target) return;
  target.replaceChildren();
  const entries = [...(library.hiddenScenarios ?? []).map(entry => ({ ...entry, kind: 'scenarios' })), ...(library.hiddenCampaigns ?? []).map(entry => ({ ...entry, kind: 'campaigns' }))];
  target.hidden = entries.length === 0;
  target.append(node('summary', `Hidden bundled content (${entries.length})`));
  for (const entry of entries) {
    const row = node('div', null, 'library-hidden-row');
    row.append(node('strong', entry.name), restoreControl(entry, entry.kind, 'Restore')); target.append(row);
  }
}
async function playScenario(scenario, trigger) {
  if (playing) return;
  const originRoute = location.hash;
  playing = true; trigger.disabled = true; $('library-status').textContent = `Starting ${scenario.name}…`;
  try {
    const data = await api(`/scenarios/${encodeURIComponent(scenario.id)}/transport`);
    if (!await globalThis.delvePlayDesign?.(data.transport)) throw new Error('The game is not ready. Please retry in a moment.');
    if (location.hash === originRoute) navigate('play');
  } catch (error) { $('library-status').textContent = error.message; }
  finally { playing = false; trigger.disabled = false; }
}
function scenarioCard(scenario, index, detail = false) {
  const card = node('article', null, 'library-card');
  card.dataset.scenarioId = scenario.id;
  card.append(art(scenario.coverArt, scenario.name));
  const content = node('div', null, 'library-card-content');
  if (index != null) content.append(node('span', `SCENARIO ${index + 1}`, 'library-eyebrow'));
  content.append(node('h3', scenario.name));
  if (!scenario.missing) content.append(node('span', sourceLabel(scenario.source), 'library-source'));
  const actions = node('div', null, 'toolbar');
  if (scenario.missing) content.append(node('p', 'This scenario was removed or is unavailable. Other scenarios can still be played.'));
  else {
    const play = button('Play scenario', () => playScenario(scenario, play)); play.className = 'library-primary'; actions.append(play);
    if (!detail) actions.append(link('Details', '#scenario/' + encodeURIComponent(scenario.id)));
    else {
      actions.append(button('Open in Designer', async () => {
        try { if (!globalThis.delveOpenLibraryScenario) throw new Error('Designer is loading. Please retry.'); await globalThis.delveOpenLibraryScenario(scenario.id); }
        catch (error) { $('library-status').textContent = error.message; }
      }));
      if (scenario.source === 'override') actions.append(restoreControl(scenario, 'scenarios'));
      const bundled = scenario.source === 'bundled' || scenario.source === 'override';
      const remove = button(bundled ? 'Hide locally' : 'Delete from library', () => {
        remove.hidden = true;
        const confirm = button('Confirm deletion', async () => {
          confirm.disabled = true;
          try { await api(`/scenarios/${encodeURIComponent(scenario.id)}?revision=${scenario.revision}${scenario.changeToken ? `&changeToken=${encodeURIComponent(scenario.changeToken)}` : ''}`, { method: 'DELETE' }); await refresh(); }
          catch (error) { $('library-status').textContent = error.message; confirm.disabled = false; }
        });
        actions.append(node('p', bundled ? 'Campaign references will be unavailable locally. Restore the bundled scenario from Hidden bundled content.' : 'Campaign references will remain marked unavailable.'), confirm, button('Cancel', () => render()));
      }); actions.append(remove);
    }
  }
  content.append(actions); card.append(content); return card;
}
function render() {
  if (!library) return;
  const cards = $('library-cards'); cards.replaceChildren();
  renderHidden();
  $('library-status').textContent = '';
  $('library-description').textContent = '';
  $('library-breadcrumb').replaceChildren(link('Campaigns', '#home'));
  document.querySelector('.library-hero').hidden = route.mode !== 'home';
  $('library-search').parentElement.hidden = route.mode === 'scenario';
  let entries = [], kind = 'scenario';
  if (route.mode === 'home') { $('library-title').textContent = 'Campaigns'; entries = library.campaigns; kind = 'campaign'; }
  else if (route.mode === 'scenarios') { $('library-title').textContent = 'Scenario library'; entries = library.scenarios; $('library-description').textContent = 'Every saved scenario, ready to play on its own.'; }
  else if (route.mode === 'campaign') {
    const campaign = library.campaigns.find(c => c.id === route.id);
    if (!campaign) { $('library-title').textContent = 'Campaign unavailable'; $('library-status').textContent = 'It may have been deleted. Choose another campaign.'; return; }
    $('library-title').textContent = campaign.name;
    $('library-description').textContent = campaign.description ?? '';
    $('library-breadcrumb').append(link('Edit campaign', '#campaigns-edit/' + encodeURIComponent(campaign.id)));
    entries = campaignEntries(campaign, library.scenarios);
  } else if (route.mode === 'scenario') {
    const scenario = library.scenarios.find(s => s.id === route.id);
    $('library-title').textContent = scenario?.name ?? 'Scenario unavailable';
    if (scenario) cards.append(scenarioCard(scenario, null, true));
    else $('library-status').textContent = 'This scenario is no longer in the library.';
    return;
  } else return;
  for (const entry of filterLibrary(entries, $('library-search').value)) {
    if (kind === 'scenario') { cards.append(scenarioCard(entry, route.mode === 'campaign' ? entries.indexOf(entry) : null)); continue; }
    const card = node('article', null, 'library-card'); card.dataset.campaignId = entry.id;
    card.append(art(entry.coverArt, entry.name));
    const content = node('div', null, 'library-card-content');
    content.append(node('span', `${entry.scenarioIds.length} SCENARIOS`, 'library-eyebrow'), node('h3', entry.name));
    content.append(node('span', sourceLabel(entry.source), 'library-source'));
    if (entry.description) content.append(node('p', entry.description));
    const actions = node('div', null, 'toolbar');
    const choose = link('Choose scenario', '#campaign/' + encodeURIComponent(entry.id)); choose.dataset.openCampaign = entry.id;
    actions.append(choose, link('Edit', '#campaigns-edit/' + encodeURIComponent(entry.id)));
    content.append(actions); card.append(content); cards.append(card);
  }
  if (!cards.children.length) $('library-status').textContent = entries.length ? 'No matching names.' : kind === 'campaign' ? 'No campaigns yet. Create a campaign or browse all scenarios.' : 'No scenarios here yet. Save one in Designer, then add it to a campaign.';
}
async function refresh() {
  const request = ++requestNumber; $('library-status').textContent = 'Loading library…';
  try { const result = await api(''); if (request !== requestNumber) return; library = result; render(); }
  catch (error) { if (request === requestNumber) { $('library-status').replaceChildren(node('span', error.message), button('Retry', refresh)); } }
}
async function applyRoute() {
  const next = libraryRoute(location.hash);
  if (route.mode === 'campaigns-edit' && (next.mode !== route.mode || next.id !== route.id) && !editor.canLeave()) {
    history.replaceState(null, '', '#' + route.mode + (route.id ? '/' + encodeURIComponent(route.id) : '')); return;
  }
  route = next; showMode(); $('library-search').value = '';
  if (route.mode === 'campaigns-edit') await editor.open(route.id);
  else if (['home', 'scenarios', 'scenario', 'campaign'].includes(route.mode)) await refresh();
}
for (const [id, mode] of [['home', 'home'], ['scenarios', 'scenarios'], ['playtest', 'play'], ['designer', 'designer']]) $('mode-' + id).addEventListener('click', () => navigate(mode));
$('library-search').addEventListener('input', render);
globalThis.addEventListener('hashchange', applyRoute);
globalThis.addEventListener('delve-designer-ready', showMode);
globalThis.addEventListener('delve-library-changed', () => { library = null; if (!$('library-main').hidden) void refresh(); });
void applyRoute();
