import { libraryRoute, campaignEntries, filterLibrary } from './library-state.js';
import { mountCampaignEditor } from './campaign-editor.js';

const $ = id => document.getElementById(id);
const node = (tag, text, className) => { const el = document.createElement(tag); if (text) el.textContent = text; if (className) el.className = className; return el; };
const link = (text, href) => { const el = node('a', text, 'library-button'); el.href = href; return el; };
const button = (text, action) => { const el = node('button', text); el.type = 'button'; el.addEventListener('click', action); return el; };
let route = libraryRoute(location.hash), library = null, requestNumber = 0, playing = false;
let editor;
let heroSignature, routeApplied = false;
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
  document.body.classList.toggle('playing', route.mode === 'play');
  document.body.classList.toggle('browsing', browsing || route.mode === 'campaigns-edit');
  const activeMode = ['campaign', 'campaigns-edit'].includes(route.mode) ? 'home' : route.mode === 'scenario' ? 'scenarios' : route.mode;
  for (const [id, mode] of [['home', 'home'], ['scenarios', 'scenarios'], ['designer', 'designer'], ['playtest', 'play']]) $('mode-' + id).setAttribute('aria-pressed', String(activeMode === mode));
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
function art(cover, name, eager = false) {
  const frame = node('div', null, 'library-art');
  const mark = node('span', '◇', 'library-art-mark'); mark.setAttribute('aria-hidden', 'true'); frame.append(mark);
  if (cover) {
    const img = node('img'); img.src = cover; img.alt = ''; img.loading = eager ? 'eager' : 'lazy'; img.referrerPolicy = 'no-referrer';
    img.addEventListener('error', () => img.remove()); frame.append(img);
  }
  return frame;
}
function sourceLabel(source) { return source === 'bundled' ? 'Bundled' : source === 'override' ? 'Local override' : 'Local'; }
function renderHero() {
  const hero = document.querySelector('.library-hero');
  const campaign = route.mode === 'campaign' ? library.campaigns.find(entry => entry.id === route.id)
    : library.campaigns.find(entry => entry.coverArt) ?? library.campaigns[0];
  hero.hidden = !['home', 'campaign'].includes(route.mode) || (route.mode === 'campaign' && !campaign);
  if (hero.hidden) return;
  const signature = JSON.stringify([route.mode, campaign]);
  if (signature === heroSignature) return;
  heroSignature = signature;
  hero.replaceChildren();
  hero.className = `library-hero ${route.mode === 'home' ? 'library-main-menu' : 'library-campaign-hero'}`;
  const scene = art(campaign?.coverArt, campaign?.name, true); scene.className = 'library-art library-hero-art'; scene.setAttribute('aria-hidden', 'true');
  const copy = node('div', null, 'library-hero-copy');
  if (route.mode === 'home') {
    copy.append(node('span', 'A WORLD AT YOUR TABLE', 'library-eyebrow'), node('h2', 'Every table.\nAn adventure.', 'library-home-title'));
    copy.append(node('p', 'Gather your heroes. Choose your story.\nThe next move is yours.', 'library-home-intro'));
    const menu = node('nav', null, 'library-menu'); menu.setAttribute('aria-label', 'Adventure menu');
    for (const [number, title, description, href] of [
      ['01', 'Find an adventure', 'Explore every scenario', '#scenarios'],
      ['02', 'Return to the table', 'Open the current game', '#play'],
      ['03', 'Create your world', 'Build in Scenario Designer', '#designer']
    ]) {
      const item = link('', href); item.className = 'library-menu-item';
      item.append(node('span', number, 'library-menu-number'));
      const label = node('span'); label.append(node('strong', title), node('small', description));
      item.append(label, node('span', '↗', 'library-menu-arrow')); menu.append(item);
    }
    copy.append(menu);
    if (campaign) {
      const feature = node('div', null, 'library-featured');
      feature.append(node('span', 'IN THE SPOTLIGHT', 'library-eyebrow'), node('h3', campaign.name));
      const enter = link('Explore campaign →', '#campaign/' + encodeURIComponent(campaign.id)); enter.className += ' library-primary'; enter.dataset.featuredCampaign = campaign.id;
      feature.append(node('p', `${campaign.scenarioIds.length} ${campaign.scenarioIds.length === 1 ? 'scenario' : 'scenarios'} · Choose any adventure`), enter);
      hero.append(feature);
    }
  } else {
    copy.append(link('← All campaigns', '#home'), node('span', 'THE CAMPAIGN', 'library-eyebrow'), node('h2', campaign.name));
    if (campaign.description) copy.append(node('p', campaign.description, 'library-campaign-story'));
    copy.append(node('span', `${campaign.scenarioIds.length} ${campaign.scenarioIds.length === 1 ? 'scenario' : 'scenarios'} · Play in any order`, 'library-campaign-count'));
  }
  hero.append(scene, copy);
}
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
  const returnLabel = route.mode === 'campaign' ? 'Back to campaign' : route.mode === 'scenario' ? 'Scenario details' : 'Scenario library';
  playing = true; trigger.disabled = true; trigger.textContent = 'Preparing the board…'; $('library-status').textContent = `Starting ${scenario.name}…`;
  try {
    const data = await api(`/scenarios/${encodeURIComponent(scenario.id)}/transport`);
    if (!await globalThis.delvePlayDesign?.(data.transport)) throw new Error('The game is not ready. Please retry in a moment.');
    globalThis.delveSetPlayContext?.({ title: scenario.name, returnHash: originRoute, returnLabel });
    if (location.hash === originRoute) navigate('play');
  } catch (error) { $('library-status').textContent = error.message; }
  finally { playing = false; trigger.disabled = false; trigger.textContent = 'Play scenario'; }
}
function scenarioCard(scenario, index, detail = false) {
  const card = node('article', null, `library-card${detail ? ' library-detail' : ''}${index != null ? ' library-expedition' : ''}${scenario.missing ? ' library-unavailable' : ''}`);
  card.dataset.scenarioId = scenario.id;
  card.append(art(scenario.coverArt, scenario.name, detail));
  if (index != null) card.append(node('span', String(index + 1).padStart(2, '0'), 'library-expedition-number'));
  const content = node('div', null, 'library-card-content');
  content.append(node('span', index != null ? `SCENARIO ${index + 1}` : detail ? 'SCENARIO BRIEFING' : 'SCENARIO', 'library-eyebrow'));
  content.append(node('h3', scenario.name));
  const actions = node('div', null, 'toolbar library-card-actions');
  if (scenario.missing) content.append(node('p', 'This scenario was removed or is unavailable. Other scenarios can still be played.'));
  else {
    const play = button('Play scenario', () => playScenario(scenario, play)); play.className = 'library-primary'; actions.append(play);
    if (!detail) actions.append(link('Details', '#scenario/' + encodeURIComponent(scenario.id)));
    else {
      content.append(node('p', 'A fresh table awaits.', 'library-detail-intro'));
      const campaigns = library.campaigns.filter(campaign => campaign.scenarioIds.includes(scenario.id));
      if (campaigns.length) {
        const collection = node('div', null, 'library-collections');
        collection.append(node('span', 'FROM THE CAMPAIGN', 'library-eyebrow'));
        for (const campaign of campaigns) collection.append(link(campaign.name, '#campaign/' + encodeURIComponent(campaign.id)));
        content.append(collection);
      }
      const preparation = node('div', null, 'library-preparation');
      preparation.append(node('span', 'BEFORE YOU BEGIN', 'library-eyebrow'), node('p', 'Your objectives and available choices appear at the table. Use the board and Unit cards to make your decisions.'));
      preparation.append(node('small', 'Starting this scenario replaces the shared game. Your saved scenarios and Designer draft stay available.'));
      const design = button('Open in Designer', async () => {
        try { if (!globalThis.delveOpenLibraryScenario) throw new Error('Designer is loading. Please retry.'); await globalThis.delveOpenLibraryScenario(scenario.id); }
        catch (error) { $('library-status').textContent = error.message; }
      }); design.className = 'library-open-designer'; actions.append(design);
      const management = node('details', null, 'library-management');
      management.append(node('summary', 'Manage scenario'), node('span', sourceLabel(scenario.source), 'library-source'));
      if (scenario.source === 'override') management.append(restoreControl(scenario, 'scenarios'));
      const bundled = scenario.source === 'bundled' || scenario.source === 'override';
      const remove = button(bundled ? 'Hide locally' : 'Delete from library', () => {
        remove.hidden = true;
        const confirm = button('Confirm deletion', async () => {
          confirm.disabled = true;
          try { await api(`/scenarios/${encodeURIComponent(scenario.id)}?revision=${scenario.revision}${scenario.changeToken ? `&changeToken=${encodeURIComponent(scenario.changeToken)}` : ''}`, { method: 'DELETE' }); await refresh(); }
          catch (error) { $('library-status').textContent = error.message; confirm.disabled = false; }
        });
        management.append(node('p', bundled ? 'Campaign references will be unavailable locally. Restore the bundled scenario from Hidden bundled content.' : 'Campaign references will remain marked unavailable.'), confirm, button('Cancel', () => render()));
      }); management.append(remove);
      content.append(actions, preparation, management); card.append(content); return card;
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
  renderHero();
  const headingActions = document.querySelector('.library-heading > .toolbar');
  if (headingActions) headingActions.replaceChildren(...(route.mode === 'home'
    ? [link('All scenarios', '#scenarios'), link('Create campaign', '#campaigns-edit')]
    : route.mode === 'campaign'
      ? [link('All scenarios', '#scenarios'), link('Edit campaign', '#campaigns-edit/' + encodeURIComponent(route.id))]
      : route.mode === 'scenario' ? [] : [link('All campaigns', '#home'), link('Create scenario', '#designer')]));
  $('library-main').dataset.route = route.mode;
  cards.classList.toggle('library-detail-grid', route.mode === 'scenario');
  cards.classList.toggle('library-expedition-list', route.mode === 'campaign');
  cards.classList.toggle('library-campaign-shelf', route.mode === 'home');
  $('library-search').parentElement.hidden = route.mode === 'scenario';
  $('library-search').placeholder = route.mode === 'home' ? 'Search campaigns' : 'Search scenarios';
  let entries = [], kind = 'scenario';
  if (route.mode === 'home') { $('library-title').textContent = 'The campaign collection'; $('library-description').textContent = 'Worlds to discover. Stories to bring to the table.'; entries = library.campaigns; kind = 'campaign'; }
  else if (route.mode === 'scenarios') { $('library-title').textContent = 'Scenario library'; entries = library.scenarios; $('library-description').textContent = 'Every saved scenario, ready to play on its own.'; }
  else if (route.mode === 'campaign') {
    const campaign = library.campaigns.find(c => c.id === route.id);
    if (!campaign) { $('library-title').textContent = 'Campaign unavailable'; $('library-status').replaceChildren(node('span', 'It may have been deleted. Choose another adventure.'), link('Browse campaigns', '#home')); return; }
    $('library-title').textContent = 'Choose your scenario';
    $('library-description').textContent = 'Every scenario is ready to play. Choose where your adventure begins.';
    entries = campaignEntries(campaign, library.scenarios);
  } else if (route.mode === 'scenario') {
    const scenario = library.scenarios.find(s => s.id === route.id);
    $('library-breadcrumb').append(link('All scenarios', '#scenarios'));
    $('library-title').textContent = scenario ? 'Scenario briefing' : 'Scenario unavailable';
    if (scenario) cards.append(scenarioCard(scenario, null, true));
    else $('library-status').replaceChildren(node('span', 'This scenario is no longer in the library.'), link('Browse scenarios', '#scenarios'));
    document.title = `${scenario?.name ?? $('library-title').textContent} · Project Delve`;
    return;
  } else return;
  const filtered = filterLibrary(entries, $('library-search').value);
  for (const entry of filtered) {
    if (kind === 'scenario') { cards.append(scenarioCard(entry, route.mode === 'campaign' ? entries.indexOf(entry) : null)); continue; }
    const card = node('article', null, 'library-card library-campaign-cover'); card.dataset.campaignId = entry.id;
    card.append(art(entry.coverArt, entry.name));
    const content = node('div', null, 'library-card-content');
    content.append(node('span', `${entry.scenarioIds.length} ${entry.scenarioIds.length === 1 ? 'SCENARIO' : 'SCENARIOS'}`, 'library-eyebrow'), node('h3', entry.name));
    if (entry.description) content.append(node('p', entry.description));
    const actions = node('div', null, 'toolbar library-card-actions');
    const choose = link('Choose scenario', '#campaign/' + encodeURIComponent(entry.id)); choose.dataset.openCampaign = entry.id;
    choose.className += ' library-primary';
    const edit = link('Edit campaign', '#campaigns-edit/' + encodeURIComponent(entry.id)); edit.className += ' library-quiet';
    actions.append(choose, edit);
    content.append(actions); card.append(content); cards.append(card);
  }
  const noun = kind === 'campaign' ? 'campaign' : 'scenario';
  $('library-status').textContent = `${filtered.length}${filtered.length !== entries.length ? ` of ${entries.length}` : ''} ${filtered.length === 1 ? noun : noun + 's'}`;
  if (!cards.children.length) {
    if (entries.length) {
      const clear = button('Clear search', () => { $('library-search').value = ''; render(); $('library-search').focus?.(); });
      $('library-status').replaceChildren(node('span', `No ${noun}s match “${$('library-search').value.trim()}”. Try another name.`), clear);
    } else $('library-status').replaceChildren(node('span', kind === 'campaign' ? 'Your next adventure starts here. Browse standalone scenarios or create your first campaign.' : 'This collection is empty. Create a scenario in Designer or explore the library.'), link(kind === 'campaign' ? 'Browse scenarios' : 'Open Designer', kind === 'campaign' ? '#scenarios' : '#designer'));
  }
  const pageTitle = route.mode === 'campaign' ? library.campaigns.find(c => c.id === route.id)?.name : $('library-title').textContent;
  document.title = route.mode === 'home' ? 'Project Delve' : `${pageTitle} · Project Delve`;
}
async function refresh() {
  const request = ++requestNumber; $('library-status').textContent = 'Loading library…';
  $('library-main').setAttribute('aria-busy', 'true');
  try { const result = await api(''); if (request !== requestNumber) return; library = result; render(); }
  catch (error) { if (request === requestNumber) { $('library-status').replaceChildren(node('span', error.message), button('Retry', refresh)); } }
  finally { if (request === requestNumber) $('library-main').setAttribute('aria-busy', 'false'); }
}
async function applyRoute() {
  const next = libraryRoute(location.hash);
  if (route.mode === 'campaigns-edit' && (next.mode !== route.mode || next.id !== route.id) && !editor.canLeave()) {
    history.replaceState(null, '', '#' + route.mode + (route.id ? '/' + encodeURIComponent(route.id) : '')); return;
  }
  const moved = routeApplied && (route.mode !== next.mode || route.id !== next.id);
  routeApplied = true;
  route = next; showMode(); $('library-search').value = '';
  const destination = location.hash;
  if (route.mode === 'campaigns-edit') await editor.open(route.id);
  else if (['home', 'scenarios', 'scenario', 'campaign'].includes(route.mode)) {
    await refresh();
    if (moved && location.hash === destination) {
      $('library-title').setAttribute('tabindex', '-1');
      $('library-title').focus?.({ preventScroll: true });
      globalThis.scrollTo?.(0, 0);
    }
  }
}
for (const [id, mode] of [['home', 'home'], ['scenarios', 'scenarios'], ['playtest', 'play'], ['designer', 'designer']]) $('mode-' + id).addEventListener('click', () => navigate(mode));
$('library-search').addEventListener('input', render);
globalThis.addEventListener('hashchange', applyRoute);
globalThis.addEventListener('delve-designer-ready', showMode);
globalThis.addEventListener('delve-library-changed', () => { library = null; if (!$('library-main').hidden) void refresh(); });
void applyRoute();
