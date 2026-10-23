// Run with the official Playwright CLI against an isolated host and temporary browser session.
// This replaces the QA draft/game and accepts only the application's QA draft replacement prompts.
async page => {
  await page.route('**/*', route => route.continue());
  await page.setViewportSize({width:1440,height:1000});
  await page.reload();
  await page.locator('#mode-designer').click();
  await page.evaluate(() => { window.confirm = () => true; });
  const cell = (x,y) => page.locator(`#designer-board [data-editor-hit=cell][data-x="${x}"][data-y="${y}"]`);
  const doc = {format:'ProjectDelveDraft', version:1, title:'Studio QA · test-owned', definition:{
    board:{width:8,height:6,defaultTerrain:'StoneFloor',cells:[],edges:[]}, unitTypeIds:['barbarian-type','grunt-type'],
    units:[{unitTypeId:'barbarian-type',sideId:'blue',anchor:{x:1,y:1},posture:'Upright',unitId:'hero'}, {unitTypeId:'grunt-type',sideId:'red',anchor:{x:6,y:4},posture:'Upright'}],
    agency:[{unitTypeId:'barbarian-type',sideId:'blue',controller:'Human'},{unitTypeId:'grunt-type',sideId:'red',controller:'Automated'}],
    waypoints:[{id:'start',area:{x:1,y:1,w:1,h:1}},{id:'finish',area:{x:5,y:4,w:1,h:1}}],
    routes:[{id:'patrol',mode:'PingPong',waypointIds:['start','finish']}],
    orders:[{unitTypeId:'grunt-type',sideId:'red',kind:'FollowRoute',route:{routeId:'patrol',currentIndex:1,direction:-1}}]
  }};
  await page.locator('#designer-file').setInputFiles({name:'qa.delve-draft.json', mimeType:'application/json', buffer:Buffer.from(JSON.stringify(doc))});
  await page.locator('#designer-title').filter({visible:true}).waitFor();
  await page.waitForFunction(() => document.querySelector('#designer-title').value.startsWith('Studio QA'));
  await page.locator('#designer-tools [data-tool=Terrain]').click();
  await page.locator('#designer-terrain').selectOption('Grass');
  const a = await cell(2,3).boundingBox(), b = await cell(4,3).boundingBox();
  await page.mouse.move(a.x+a.width/2,a.y+a.height/2); await page.mouse.down();
  await page.mouse.move(b.x+b.width/2,b.y+b.height/2,{steps:12}); await page.mouse.up();
  await page.locator('#designer-undo').click();
  if (await page.locator('#designer-board .Grass').count()) throw new Error('Stroke did not undo atomically');
  await page.locator('#designer-redo').click();
  if (await page.locator('#designer-board .Grass').count() !== 3) throw new Error('Stroke redo failed');
  await page.locator('#designer-tools [data-tool=Select]').click();
  await cell(1,1).click();
  await page.locator('#designer-properties').getByLabel('Unit ID (optional; blank = generated)').fill('hero-edited');
  await cell(2,2).click(); await cell(1,1).click();
  if (await page.locator('#designer-properties').getByLabel('Unit ID (optional; blank = generated)').inputValue() !== 'hero-edited') throw new Error('Board selection lost a simple property edit');
  await page.locator('[data-designer-tab=routes]').click();
  const route = page.locator('#designer-routes form').filter({has:page.getByLabel('Route ID',{exact:true})}).first();
  await route.getByRole('button',{name:'Save Route',exact:true}).click();
  let saved = await page.evaluate(() => JSON.parse(localStorage.getItem('project-delve.designer.v1')));
  if (saved.definition.orders[0].route.currentIndex !== 1) throw new Error('Unchanged route reset progress');
  await page.locator('[data-designer-tab=groups]').click();
  const group = page.locator('#designer-agency form').first();
  const before = await page.evaluate(() => JSON.parse(localStorage.getItem('project-delve.designer.v1')).definition);
  await group.getByLabel('SideId',{exact:true}).fill('partly-mutated');
  await group.getByLabel('Starting Order',{exact:true}).selectOption('FollowRoute');
  // An unresolved reference is a valid incomplete draft input, but a rejected compound edit.
  await group.getByLabel('Shared Route',{exact:true}).evaluate(el => { const o = document.createElement('option'); o.value='missing'; o.textContent='missing'; el.append(o); });
  await group.getByLabel('Shared Route',{exact:true}).selectOption('missing');
  await group.getByRole('button',{name:'Apply pair',exact:true}).click();
  saved = await page.evaluate(() => JSON.parse(localStorage.getItem('project-delve.designer.v1')));
  if (JSON.stringify(saved.definition) !== JSON.stringify(before)) throw new Error('Rejected group edit changed definition');
  if (!(await page.locator('#designer-status').innerText()).includes('Edit refused')) throw new Error('Missing rejection feedback');
  await page.locator('#designer-discard-inputs').click();
  await page.locator('#designer-validate').click();
  await page.waitForFunction(() => document.querySelector('#designer-status').textContent.startsWith('Valid'));
  await page.locator('[data-designer-tab=board]').click();
  await page.locator('#designer-width').fill('unfinished');
  await page.locator('[data-designer-tab=inspect]').click();
  await page.reload();
  await page.evaluate(() => { window.confirm = () => true; });
  await page.locator('[data-designer-tab=board]').click();
  if (await page.locator('#designer-width').inputValue() !== 'unfinished') throw new Error('Recovery lost unfinished input');
  if (await page.locator('#designer-board .Grass').count() !== 3) throw new Error('Recovery lost committed terrain');
  await page.locator('#designer-validate').click();
  if (!(await page.locator('#designer-status').innerText()).includes('Unfinished')) throw new Error('Validation ignored unfinished input');
  await page.locator('#designer-discard-inputs').click();
  // Invalid HP is committed data, validated by the engine; issue navigation returns to the Unit inspector.
  await cell(1,1).click();
  await page.locator('#designer-properties').getByLabel(/Initial HP/).fill('0');
  await page.locator('#designer-validate').click();
  await page.locator('#designer-errors button').first().click();
  if (!await page.locator('#designer-properties').getByLabel(/Initial HP/).isVisible()) throw new Error('Validation navigation failed');
  await page.locator('#designer-properties').getByLabel(/Initial HP/).fill('');
  await page.locator('#designer-validate').click();
  await page.waitForFunction(() => document.querySelector('#designer-status').textContent.startsWith('Valid'));
  const downloadPromise = page.waitForEvent('download'); await page.locator('#designer-save').click();
  const download = await downloadPromise; await download.saveAs('.playwright-cli/phase1-saved.delve-draft.json');
  await page.locator('#designer-new').click();
  await page.waitForFunction(() => document.querySelector('#designer-title').value === 'Untitled scenario');
  await page.locator('#designer-file').setInputFiles('.playwright-cli/phase1-saved.delve-draft.json');
  await page.waitForFunction(() => document.querySelector('#designer-title').value.startsWith('Studio QA'));
  await page.locator('[data-designer-tab=document]').click(); await page.locator('#designer-export').click();
  await page.waitForFunction(() => document.querySelector('#designer-transport').value.startsWith('DELVE1:'));
  const transport = await page.locator('#designer-transport').inputValue();
  await page.locator('#designer-import').click();
  await page.waitForFunction(() => document.querySelector('#designer-title').value === 'Imported scenario');
  await page.locator('#designer-play').click();
  await page.locator('#playtest-main').waitFor({state:'visible'});
  await page.locator('#mode-designer').click();
  if (await page.locator('#designer-board .figure').count() !== 2) throw new Error('Playtest return lost draft');
  await page.locator('[data-designer-tab=inspect]').click(); await cell(1,1).click(); await page.mouse.move(20,20);
  await page.screenshot({path:'.playwright-cli/phase1-final-desktop.png'});
  await page.setViewportSize({width:600,height:850});
  await page.screenshot({path:'.playwright-cli/phase1-final-narrow.png',fullPage:true});
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth);
  if (overflow) throw new Error('Narrow document overflow');
  await page.setViewportSize({width:390,height:844});
  await page.locator('#designer-zoom').selectOption('48');
  if (await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)) throw new Error('Zoom escaped canvas on mobile');
  await page.locator('#designer-zoom').selectOption('fit');
  await page.locator('#designer-tools [data-tool=Terrain]').click();
  await page.locator('#designer-terrain').selectOption('Water'); await cell(0,0).click();
  await page.locator('#designer-undo').click();
  if (await cell(0,0).evaluate(el => el.classList.contains('Water'))) throw new Error('Mobile painting/Undo failed');
  await page.locator('#designer-tools [data-tool=Select]').click(); await cell(1,1).click();
  await page.mouse.move(10,10); await page.evaluate(() => scrollTo(0,0));
  await page.screenshot({path:'.playwright-cli/phase1-final-mobile.png',fullPage:true});
  return {groupRejection:'atomic', route:'progress retained', validation:'engine and error navigation passed', draftFile:download.suggestedFilename(), delve1:transport.slice(0,7), playtest:'returned with draft retained', narrow:'600px and 390px, zoom and mobile painting/Undo passed'};
}
