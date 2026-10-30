const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function reveal(content) {
  const node = tag => ({ tag, children: [], append(...children) { this.children.push(...children); },
    setAttribute() {}, addEventListener() {}, focus() {} });
  const document = { body: node('body'), createElement: node };
  const context = vm.createContext({ document, content });
  vm.runInContext(fs.readFileSync(path.join(__dirname, '../ProjectDelve.Web/wwwroot/loot-ui.js'), 'utf8').replace(/^export /gm, ''), context);
  vm.runInContext('lootCardDialog(content, true, () => {})', context);
  return document.body.children[0].children[0];
}

test('typed Immediate Loot displays supplied content without inheriting the unused legacy Kind artwork', () => {
  const card = reveal({ kind:'PocketBreakfast', lootCardId:'restorative-letter', category:'Immediate',
    name:'A Restorative Letter', text:'Restore 3 HP, without exceeding maximum HP.' });
  assert.equal(card.children[1].textContent, '◆');
  assert.equal(card.children[2].textContent, 'A Restorative Letter');
  assert.equal(card.children[3].textContent, 'Restore 3 HP, without exceeding maximum HP.');
});

test('explicit legacy presentation preserves canonical Immediate Loot artwork', () => {
  const legacy = reveal({ kind:'PocketBreakfast', category:'Immediate', name:'Pocket Breakfast', text:'Restore 2 HP.' });
  const typed = reveal({ kind:'PocketBreakfast', lootCardId:'pocket-breakfast', legacyKind:'PocketBreakfast',
    category:'Immediate', name:'Pocket Breakfast', text:'Restore 2 HP.' });
  assert.equal(legacy.children[1].textContent, '🥐');
  assert.equal(typed.children[1].textContent, legacy.children[1].textContent);
});
