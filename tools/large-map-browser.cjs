// Explicitly reviewed browser profiling: installed Playwright, disposable Edge
// contexts and the isolated localhost:5081 host only. Never attach to a live tab.
const fs = require('node:fs');
const path = require('node:path');
const { performance } = require('node:perf_hooks');
const [playwrightPath, fixtureDirectory, outputDirectory, phase = 'baseline'] = process.argv.slice(2);
if (!playwrightPath || !fixtureDirectory || !outputDirectory) throw Error('Expected installed Playwright path, fixtures, output, baseline|after');
const { chromium } = require(playwrightPath);
const origin = 'http://127.0.0.1:5081';
fs.mkdirSync(outputDirectory, { recursive: true });
const rows = [];
(async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: false });
  try {
    const names = fs.readdirSync(fixtureDirectory).filter(n => n.endsWith('.response.json') &&
      (phase==='final' ? /^120x(?:100|120)-Default\./.test(n) : n.startsWith('120x100-') && (phase !== 'baseline' || n.includes('-Default.'))));
    for (const mobile of [false, true]) for (const name of names) {
      const fixture = JSON.parse(fs.readFileSync(path.join(fixtureDirectory, name), 'utf8'));
      fixture.state = JSON.parse(fs.readFileSync(path.join(fixtureDirectory, name.replace('.response.', '.state.')), 'utf8'));
      if (!fixture.definition || !fixture.state) continue;
      const context = await browser.newContext({ viewport: mobile ? { width:390,height:844 } : {width:1440,height:1000}, isMobile: mobile, hasTouch: mobile });
      const page = await context.newPage();
      page.setDefaultTimeout(60000);
      const row = { phase, fixture:name, mobile, errors:[], failedRequests:[] };
      let fog=false;
      page.on('pageerror', e => { row.errors.push(String(e)); console.error(e); });
      page.on('requestfailed', r => { if (!r.url().endsWith('/updates')) row.failedRequests.push(r.url()); });
      page.on('dialog', d => d.type() === 'confirm' ? d.accept() : d.dismiss());
      await page.route('**/*', async route => {
        const request=route.request(), url=new URL(request.url());
        if (url.origin !== origin) return route.abort();
        if (url.pathname === '/api/game/updates') return route.fulfill({status:200,contentType:'text/event-stream',body:''});
        if (url.pathname === '/api/game' && request.method()==='GET') {
          const response=await route.fetch(), snapshot=await response.json();
          snapshot.result.state=fog?{...fixture.state,exploredCells:{blue:[{x:0,y:0}]}}:fixture.state; snapshot.result.events=[]; snapshot.result.decision=null; snapshot.result.resolutionSteps=[];
          snapshot.visualSeed=fixture.visualSeed; snapshot.scenarioId='large-map-fixture';
          Object.assign(snapshot.presentation,{cards:{},figures:{},machines:[],objectives:[],groundObjects:[],orders:[],decision:null,worldEffects:null,viewingSideId:fog?'blue':null,resolutionSteps:[]});
          return route.fulfill({json:snapshot});
        }
        if (phase === 'baseline' && url.pathname === '/api/designer/catalog') {
          const response=await route.fetch(), catalog=await response.json(); catalog.maxBoardSize=120;
          return route.fulfill({json:catalog});
        }
        if (phase === 'baseline' && url.pathname === '/world-generator-state.js') {
          const response=await route.fetch();
          return route.fulfill({body:(await response.text()).replaceAll('1, 50','1, 120').replaceAll('[1-9]\\d?','[1-9]\\d{0,2}'),contentType:'text/javascript'});
        }
        if (phase === 'baseline' && url.pathname === '/api/world-generation' && request.method()==='POST')
          return route.fulfill({json:fixture});
        return route.continue();
      });
      await page.addInitScript(() => {
        // Detached fixtures use the supported HTTP refresh adapter, not SSE.
        window.EventSource=undefined;
        localStorage.setItem('delve.board.view.v1','enhanced');
        window.__largeTasks=[];
        new PerformanceObserver(list=>{for(const e of list.getEntries())window.__largeTasks.push(e.duration);}).observe({entryTypes:['longtask']});
      });
      const started=performance.now(); await page.goto(origin);
      await page.locator('#mode-playtest').click();
      console.log('Loaded', name, mobile, await page.locator('#error').textContent(), await page.locator('#board').evaluate(e=>e.children.length));
      await page.waitForFunction(() => document.querySelector('#board')?.children.length>=12000);
      row.domLoadMs=performance.now()-started;
      console.log('DOM ready');
      await page.locator('#open-world-generator').dispatchEvent('click');
      await page.locator('#world-generator').evaluate(e=>e.querySelectorAll('details').forEach(d=>d.open=true));
      await page.locator('#world-generator-code-input').fill(fixture.code);
      await page.locator('#world-generator-load').click();
      let t=performance.now(); await page.locator('#world-generator-preview').click();
      await page.waitForFunction(code=>document.querySelector('#world-generator-board').dataset.generationCode===code,fixture.code);
      row.previewWallMs=performance.now()-t;
      row.previewBuildMs=Number(await page.locator('#world-generator-board').getAttribute('data-render-ms'));
      console.log('Preview ready',row.previewBuildMs);
      await page.locator('#world-generator-view').selectOption('classic');
      await page.locator('#world-generator-view').selectOption('enhanced');
      if(phase==='final') {
        await page.locator('#world-generator-zoom').selectOption('24');
        await page.locator('#world-generator-board').evaluate(e=>e.parentElement.scrollTo(1200,800));
        await page.screenshot({path:path.join(outputDirectory,`${name}-${mobile?'mobile':'desktop'}-preview-zoom.png`)});
        if(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth))throw Error('Preview zoom overflowed page');
        await page.locator('#world-generator-zoom').selectOption('fit');
      }
      await page.locator('#world-generator-export').click();
      row.transportLength=(await page.locator('#world-generator-transport').inputValue()).length;
      await page.screenshot({path:path.join(outputDirectory,`${name}-${mobile?'mobile':'desktop'}-preview.png`)});
      t=performance.now(); await page.locator('#world-generator-designer').click();
      await page.waitForFunction(()=>document.querySelectorAll('#designer-board [data-editor-hit=cell]').length>=12000);
      row.designerLoadMs=performance.now()-t;
      console.log('Designer ready',row.designerLoadMs);
      await page.locator('#designer-tools [data-tool=Terrain]').click();
      await page.locator('#designer-terrain').selectOption('Gravel');
      t=performance.now();
      // Dispatch at the cell center without tiny fit-mode edge hit targets intercepting.
      await page.locator('#designer-board [data-editor-hit=cell][data-x="0"][data-y="0"]').press('Enter');
      row.terrainEditMs=performance.now()-t;
      t=performance.now(); await page.locator('#designer-undo').click(); row.undoMs=performance.now()-t;
      await page.locator('#designer-redo').click();
      await page.locator('#designer-tools [data-tool=Edge]').click();
      await page.locator('#designer-edge').selectOption('ClosedDoor');
      t=performance.now(); await page.locator('#designer-board [data-editor-hit=edge][data-x="0"][data-y="0"][data-direction=Right]').press('Enter');
      row.edgeEditMs=performance.now()-t;
      await page.locator('#designer-undo').click();
      await page.locator('[data-designer-tab=document]').click();
      const downloadPromise=page.waitForEvent('download');
      t=performance.now();await page.locator('#designer-save').click();
      const download=await downloadPromise, savedPath=path.join(outputDirectory,`${name}-${mobile?'mobile':'desktop'}.delve-draft.json`);
      await download.saveAs(savedPath);row.draftSaveMs=performance.now()-t;
      const savedDefinition=JSON.parse(fs.readFileSync(savedPath,'utf8')).definition;
      if(savedDefinition.board.width!==fixture.definition.board.width||savedDefinition.board.height!==fixture.definition.board.height||!savedDefinition.board.cells.some(c=>c.position.x===0&&c.position.y===0&&c.terrain==='Gravel'))throw Error('Large terrain edit or file save lost content');
      t=performance.now(); await page.locator('#designer-file').setInputFiles(savedPath);
      await page.waitForFunction(saved=>JSON.parse(localStorage.getItem('project-delve.designer.v1'))?.savedAt!==saved,JSON.parse(fs.readFileSync(savedPath,'utf8')).savedAt);
      row.draftOpenMs=performance.now()-t;
      if (phase!=='baseline') {
        await page.locator('#designer-title').fill(`Large map QA ${name} ${mobile}`);
        t=performance.now(); await page.locator('#designer-library-save').click();
        await page.waitForFunction(()=>document.querySelector('#designer-library-status').textContent.startsWith('Saved'));
        row.librarySaveMs=performance.now()-t;
        await page.waitForFunction(()=>!document.querySelector('#designer-library-save').disabled);
        const savedId=await page.evaluate(()=>JSON.parse(localStorage.getItem('project-delve.designer.v1')).librarySource.id);
        await page.locator('#designer-library-select').selectOption(savedId);
        t=performance.now(); await page.locator('#designer-library-open').click();
        await page.waitForFunction(()=>document.querySelector('#designer-main').getAttribute('aria-busy')==='false');
        row.libraryOpenMs=performance.now()-t;
        const original=JSON.parse(fs.readFileSync(savedPath,'utf8')).definition;
        const reopened=await page.evaluate(()=>JSON.parse(localStorage.getItem('project-delve.designer.v1')).definition);
        if(JSON.stringify(original)!==JSON.stringify(reopened)) throw Error('Library reopen changed edited terrain');
        await page.locator('#designer-validate').click();
        await page.waitForFunction(()=>document.querySelector('#designer-status').textContent.startsWith('Valid'));
        if(phase==='final') {
          await page.locator('#designer-export').click();
          await page.waitForFunction(()=>document.querySelector('#designer-transport').value.startsWith('DELVE1:'));
          await page.locator('#designer-import').click();
          await page.waitForFunction(()=>document.querySelector('#designer-title').value==='Imported scenario');
          const imported=await page.evaluate(()=>JSON.parse(localStorage.getItem('project-delve.designer.v1')).definition);
          if(JSON.stringify(imported)!==JSON.stringify(original))throw Error('DELVE1 Designer import changed edited content');
          row.delve1Import='exact';
        }
      }
      await page.screenshot({path:path.join(outputDirectory,`${name}-${mobile?'mobile':'desktop'}-designer.png`)});
      await page.locator('#mode-playtest').dispatchEvent('click');
      for(const mode of ['three','tabletop','enhanced','classic','three']) {
        t=performance.now();await page.locator('#board-view').selectOption(mode,{force:true});
        if(['three','tabletop'].includes(mode)){
          await page.waitForFunction(mode=>{const text=document.querySelector('.three-metrics')?.textContent;if(!text)return false;const m=JSON.parse(text);return m.presentation===(mode==='three'?'flat':'tabletop')&&m.textureLoads===0;},mode);
          row[mode+'SwitchMs']=performance.now()-t;
          row[mode]=await page.locator('.three-metrics').evaluate(e=>JSON.parse(e.textContent));
        } else row[mode+'SwitchMs']=performance.now()-t;
      }
      const canvas=page.locator('.three-canvas'); await canvas.hover(); await page.mouse.wheel(0,-250);
      await page.locator('#three-viewport').press('ArrowRight');
      await page.locator('#three-viewport').press('Shift+ArrowRight');
      await page.screenshot({path:path.join(outputDirectory,`${name}-${mobile?'mobile':'desktop'}-three.png`)});
      if(phase==='final') {
        row.rendererResources=[];
        for(let i=0;i<3;i++) {
          for(let j=0;j<12;j++) {await page.locator('#three-viewport').press(j%2?'Shift+ArrowLeft':'Shift+ArrowRight');await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>r())));}
          for(const mode of ['enhanced','tabletop','three'])await page.locator('#board-view').selectOption(mode,{force:true});
          await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));
          row.rendererResources.push(await page.locator('.three-metrics').evaluate(e=>JSON.parse(e.textContent)));
        }
        row.camera=row.rendererResources.at(-1);
        fog=true;await page.locator('#board-view').selectOption('enhanced',{force:true});await page.locator('#refresh').dispatchEvent('click');
        await page.waitForFunction(n=>document.querySelectorAll('#board .Unknown').length===n,fixture.definition.board.width*fixture.definition.board.height-1);
        row.fogUnknown=await page.locator('#board .Unknown').count();
        await page.locator('#board-view').selectOption('tabletop',{force:true});
        await page.screenshot({path:path.join(outputDirectory,`${name}-${mobile?'mobile':'desktop'}-fog.png`)});
      }
      row.memory=await page.evaluate(()=>performance.memory?{usedJSHeapSize:performance.memory.usedJSHeapSize,totalJSHeapSize:performance.memory.totalJSHeapSize}:null);
      row.longTasks=await page.evaluate(()=>window.__largeTasks);
      rows.push(row);fs.writeFileSync(path.join(outputDirectory,'measurements.json'),JSON.stringify(rows,null,2));
      console.log(JSON.stringify(row)); await context.close();
    }
  }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
