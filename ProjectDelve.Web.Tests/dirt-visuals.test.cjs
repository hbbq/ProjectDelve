const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = name => fs.readFileSync(path.resolve(__dirname, '../ProjectDelve.Web/wwwroot', name), 'utf8');

test('typed Dirt uses shared deterministic art and rotation across overlapping world windows', () => {
  const context = vm.createContext({});
  vm.runInContext(source('board-visuals.js').replace(/^export /gm, ''), context);
  const run = code => vm.runInContext(code, context);
  run(`const board={terrainTypes:[{id:'dirt',name:'Dirt',presentation:{recipe:'Dirt'}}]};
    const display=terrainDisplay(board,{typeId:'dirt'});
    const sample=(offsetX,offsetY,x,y)=>terrainLayers(display.kind,{seed:'WLD1-fixture',x:offsetX+x,y:offsetY+y});`);
  assert.equal(run('display.kind'), 'Dirt');
  assert.equal(run('display.fallback'), false);
  for (let y = -8; y < 8; y++) for (let x = -8; x < 8; x++) {
    assert.equal(run(`JSON.stringify(sample(-50,-50,${x+50},${y+50}))`), run(`JSON.stringify(sample(0,0,${x},${y}))`));
    assert.match(run(`sample(0,0,${x},${y}).base.image`), /terrain\/dirt\/0[123]\.webp$/);
    assert.ok([0,90,180,270].includes(run(`sample(0,0,${x},${y}).rotation`)));
  }
});

test('Designer accepts canonical Dirt presentation without inventing a legacy terrain identity', () => {
  const context = vm.createContext({structuredClone});
  vm.runInContext(source('designer-state.js').replace(/^export /gm, ''), context);
  vm.runInContext(`const draft=new DesignerDraft({maxBoardSize:50,terrains:['StoneFloor','Grass'],
    terrainPresentationRecipes:['Dirt'],terrainDefinitions:[{id:'dirt',name:'Dirt',presentation:{recipe:'Dirt'}}]});
    draft.definition.board.cells=[{position:{x:1,y:1},terrainTypeId:'dirt'}];`, context);
  assert.equal(vm.runInContext('draft.terrainRecipeAt({x:1,y:1})', context), 'Dirt');
  assert.equal(vm.runInContext('draft.terrainNameAt({x:1,y:1})', context), 'Dirt');
  assert.equal(vm.runInContext("draft.legacyTerrainKind('type:dirt')", context), undefined);
  assert.match(source('styles.css'), /\.cell\.Dirt\s*\{/);
});
