const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(
  path.join(__dirname, '..', '..', 'src', 'wwwroot', 'js', 'color-identity.js'),
  'utf8');

const colorTable = [
  { name: 'Colorless', abbreviation: 'C' },
  { name: 'Dimir', abbreviation: 'UB' },
  { name: 'Golgari', abbreviation: 'BG' },
  { name: 'Simic', abbreviation: 'GU' },
  { name: 'Witch-Maw', abbreviation: 'GWUB' },
  { name: 'Mono Blue', abbreviation: 'U' },
];

const commanders = [
  ['Meren', 'BG'],
  ['Tymna', 'WB'],
  ['Thrasios', 'GU'],
  ['Kozilek', ''],
  ['Quote\'s "Commander"', 'U'],
];

function setup({ commander = '', second = '' } = {}) {
  const items = commanders
    .map(([name, colors]) => `<li data-colors="${colors}"></li>`)
    .join('');
  const dom = new JSDOM(
    `<form>
       <input type="hidden" id="Commander" value="${commander}" />
       <ul id="CommanderList">${items}</ul>
       <input type="hidden" id="SecondCommander" value="${second}" />
       <ul id="SecondCommanderList">${items}</ul>
       <input type="text" id="ColorIdentityDisplay" readonly />
     </form>`,
    { runScripts: 'outside-only' });
  const { document } = dom.window;
  Array.from(document.querySelectorAll('li')).forEach((li, i) => {
    li.setAttribute('data-value', commanders[i % commanders.length][0]);
  });
  document.getElementById('ColorIdentityDisplay').setAttribute('data-colors', JSON.stringify(colorTable));
  dom.window.eval(script);
  const set = (id, value) => {
    const input = document.getElementById(id);
    input.value = value;
    input.dispatchEvent(new dom.window.Event('change', { bubbles: true }));
  };
  return { display: document.getElementById('ColorIdentityDisplay'), set };
}

test('display is empty until a commander is chosen', () => {
  assert.equal(setup().display.value, '');
});

test('choosing a commander shows its color identity name and abbreviation', () => {
  const ctx = setup();
  ctx.set('Commander', 'Meren');
  assert.equal(ctx.display.value, 'Golgari (BG)');
});

test('a second commander is combined with the first', () => {
  const ctx = setup();
  ctx.set('Commander', 'Tymna');
  ctx.set('SecondCommander', 'Thrasios');
  assert.equal(ctx.display.value, 'Witch-Maw (GWUB)');
});

test('overlapping colors are not double counted and order does not matter', () => {
  const ctx = setup();
  ctx.set('Commander', 'Thrasios');
  ctx.set('SecondCommander', 'Meren');
  assert.equal(ctx.display.value, '');
  ctx.set('SecondCommander', 'Quote\'s "Commander"');
  assert.equal(ctx.display.value, 'Simic (GU)');
});

test('a colorless commander shows Colorless', () => {
  const ctx = setup();
  ctx.set('Commander', 'Kozilek');
  assert.equal(ctx.display.value, 'Colorless (C)');
});

test('clearing the second commander reverts to the first commander only', () => {
  const ctx = setup();
  ctx.set('Commander', 'Meren');
  ctx.set('SecondCommander', 'Tymna');
  ctx.set('SecondCommander', '');
  assert.equal(ctx.display.value, 'Golgari (BG)');
});

test('clearing the first commander empties the display', () => {
  const ctx = setup({ commander: 'Meren' });
  ctx.set('Commander', '');
  assert.equal(ctx.display.value, '');
});

test('commanders already selected on load are reflected when either changes', () => {
  const ctx = setup({ commander: 'Tymna' });
  ctx.set('SecondCommander', 'Thrasios');
  assert.equal(ctx.display.value, 'Witch-Maw (GWUB)');
});
