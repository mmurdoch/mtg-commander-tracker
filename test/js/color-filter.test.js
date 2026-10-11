const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(
  path.join(__dirname, '..', '..', 'src', 'wwwroot', 'js', 'color-filter.js'),
  'utf8');

const colors = [
  [1, 'Mono White', 'W'],
  [5, 'Mono Green', 'G'],
  [10, 'Selesnya', 'GW'],
  [13, 'Golgari', 'BG'],
  [18, 'Jund', 'BRG'],
];

function setup(initialId) {
  const items = colors
    .map(([id, name, abbr]) =>
      `<li role="option" data-value="${id}" data-name="${name}" data-abbreviation="${abbr}">${name} (${abbr})</li>`)
    .join('');
  const initial = colors.find(([id]) => id === initialId);
  const dom = new JSDOM(
    `<form><div id="ColorCombo">
       <input id="ColorFilter" type="text" role="combobox" value="${initial ? `${initial[1]} (${initial[2]})` : ''}" />
       <button type="button" id="ColorToggle"></button>
       <input type="hidden" id="ColorDataId" name="ColorDataId" value="${initial ? initial[0] : ''}" />
       <ul id="ColorList" role="listbox" hidden>${items}</ul>
     </div></form>`,
    { runScripts: 'outside-only' });
  dom.window.eval(script);
  const { document } = dom.window;
  return {
    window: dom.window,
    filter: document.getElementById('ColorFilter'),
    value: document.getElementById('ColorDataId'),
    list: document.getElementById('ColorList'),
    toggle: document.getElementById('ColorToggle'),
  };
}

function type(ctx, text) {
  ctx.filter.value = text;
  ctx.filter.dispatchEvent(new ctx.window.Event('input', { bubbles: true }));
}

function key(ctx, name) {
  const event = new ctx.window.KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true });
  ctx.filter.dispatchEvent(event);
  return event;
}

function click(ctx, el) {
  el.dispatchEvent(new ctx.window.MouseEvent('click', { bubbles: true }));
}

const shown = (ctx) =>
  Array.from(ctx.list.children).filter((li) => !li.hidden).map((li) => li.getAttribute('data-name'));
const highlighted = (ctx) =>
  Array.from(ctx.list.children).filter((li) => li.getAttribute('aria-selected') === 'true').map((li) => li.getAttribute('data-name'));
const item = (ctx, id) => Array.from(ctx.list.children).find((li) => li.getAttribute('data-value') === String(id));

test('typing opens the list and highlights the first match without committing', () => {
  const ctx = setup();
  type(ctx, 'mono');
  assert.equal(ctx.list.hidden, false);
  assert.deepEqual(shown(ctx), ['Mono White', 'Mono Green']);
  assert.deepEqual(highlighted(ctx), ['Mono White']);
  assert.equal(ctx.value.value, '');
  assert.equal(ctx.filter.getAttribute('aria-expanded'), 'true');
});

test('color letters match in any order', () => {
  for (const q of ['bg', 'GB']) {
    const ctx = setup();
    type(ctx, q);
    assert.ok(shown(ctx).includes('Golgari'), q);
    assert.ok(!shown(ctx).includes('Selesnya'), q);
  }
});

test('clicking an item commits it into the text box and hidden value and closes the list', () => {
  const ctx = setup();
  type(ctx, 'mono');
  click(ctx, item(ctx, 5));
  assert.equal(ctx.filter.value, 'Mono Green (G)');
  assert.equal(ctx.value.value, '5');
  assert.equal(ctx.list.hidden, true);
  assert.equal(ctx.filter.getAttribute('aria-expanded'), 'false');
});

test('Enter commits the highlighted item and does not submit', () => {
  const ctx = setup();
  type(ctx, 'gol');
  const event = key(ctx, 'Enter');
  assert.ok(event.defaultPrevented);
  assert.equal(ctx.filter.value, 'Golgari (BG)');
  assert.equal(ctx.value.value, '13');
  assert.equal(ctx.list.hidden, true);
});

test('Tab commits the highlighted item among several matches and lets focus move on', () => {
  const ctx = setup();
  type(ctx, 'mono');
  key(ctx, 'ArrowDown');
  const event = key(ctx, 'Tab');
  assert.ok(!event.defaultPrevented);
  assert.equal(ctx.filter.value, 'Mono Green (G)');
  assert.equal(ctx.value.value, '5');
  assert.equal(ctx.list.hidden, true);
});

test('Tab commits the first match when typing has highlighted it by default', () => {
  const ctx = setup();
  type(ctx, 'mono');
  key(ctx, 'Tab');
  assert.equal(ctx.filter.value, 'Mono White (W)');
  assert.equal(ctx.value.value, '1');
});

test('Tab with the list closed or nothing highlighted changes nothing', () => {
  const closed = setup(13);
  key(closed, 'Tab');
  assert.equal(closed.value.value, '13');

  const ctx = setup();
  click(ctx, ctx.toggle);
  key(ctx, 'Tab');
  assert.equal(ctx.value.value, '');
});

test('Tab on the already selected item does not fire a change event', () => {
  const ctx = setup(13);
  let changes = 0;
  ctx.value.addEventListener('change', () => changes++);
  click(ctx, ctx.filter);
  key(ctx, 'Tab');
  assert.equal(changes, 0);
  assert.equal(ctx.value.value, '13');
});

test('Enter with the list closed is left alone so the form can submit', () => {
  const ctx = setup(13);
  const event = key(ctx, 'Enter');
  assert.ok(!event.defaultPrevented);
});

test('arrow keys move the highlight and ArrowDown opens a closed list', () => {
  const ctx = setup();
  key(ctx, 'ArrowDown');
  assert.equal(ctx.list.hidden, false);
  assert.equal(shown(ctx).length, colors.length);
  type(ctx, 'mono');
  key(ctx, 'ArrowDown');
  assert.deepEqual(highlighted(ctx), ['Mono Green']);
  key(ctx, 'ArrowDown');
  assert.deepEqual(highlighted(ctx), ['Mono Green']);
  key(ctx, 'ArrowUp');
  assert.deepEqual(highlighted(ctx), ['Mono White']);
});

test('Escape closes the list and reverts uncommitted text', () => {
  const ctx = setup(13);
  type(ctx, 'mono');
  key(ctx, 'Escape');
  assert.equal(ctx.list.hidden, true);
  assert.equal(ctx.filter.value, 'Golgari (BG)');
  assert.equal(ctx.value.value, '13');
});

test('leaving the control reverts uncommitted text and closes the list', () => {
  const ctx = setup();
  type(ctx, 'mono');
  ctx.filter.dispatchEvent(new ctx.window.FocusEvent('focusout', { bubbles: true, relatedTarget: null }));
  assert.equal(ctx.filter.value, '');
  assert.equal(ctx.value.value, '');
  assert.equal(ctx.list.hidden, true);
});

test('leaving the control selects the only remaining match', () => {
  const ctx = setup();
  let changes = 0;
  ctx.value.addEventListener('change', () => changes++);
  type(ctx, 'gol');
  ctx.filter.dispatchEvent(new ctx.window.FocusEvent('focusout', { bubbles: true, relatedTarget: null }));
  assert.equal(ctx.value.value, '13');
  assert.equal(ctx.filter.value, 'Golgari (BG)');
  assert.equal(ctx.list.hidden, true);
  assert.equal(changes, 1);
});

test('leaving the control with no matches reverts the text', () => {
  const ctx = setup();
  type(ctx, 'zzz');
  ctx.filter.dispatchEvent(new ctx.window.FocusEvent('focusout', { bubbles: true, relatedTarget: null }));
  assert.equal(ctx.filter.value, '');
  assert.equal(ctx.value.value, '');
});

test('leaving the control after merely opening the full list does not select anything', () => {
  const ctx = setup();
  click(ctx, ctx.toggle);
  ctx.filter.dispatchEvent(new ctx.window.FocusEvent('focusout', { bubbles: true, relatedTarget: null }));
  assert.equal(ctx.value.value, '');
});

test('the toggle button opens the full list and closes it again', () => {
  const ctx = setup(13);
  click(ctx, ctx.toggle);
  assert.equal(ctx.list.hidden, false);
  assert.equal(shown(ctx).length, colors.length);
  assert.deepEqual(highlighted(ctx), ['Golgari']);
  click(ctx, ctx.toggle);
  assert.equal(ctx.list.hidden, true);
});

test('clearing the text box clears the committed value', () => {
  const ctx = setup(13);
  type(ctx, '');
  assert.equal(ctx.value.value, '');
});

test('an initial value rendered by the server is preserved', () => {
  const ctx = setup(13);
  assert.equal(ctx.value.value, '13');
  assert.equal(ctx.filter.value, 'Golgari (BG)');
});

test('the Owner combo box is wired up and matches by name only', () => {
  const dom = new JSDOM(
    `<form><div id="OwnerCombo">
       <input id="OwnerFilter" type="text" role="combobox" value="" />
       <button type="button" id="OwnerToggle"></button>
       <input type="hidden" id="OwnerId" name="OwnerId" value="" />
       <ul id="OwnerList" role="listbox" hidden>
         <li role="option" data-value="1" data-name="Alice">Alice</li>
         <li role="option" data-value="2" data-name="Bob">Bob</li>
       </ul>
     </div></form>`,
    { runScripts: 'outside-only' });
  dom.window.eval(script);
  const { document } = dom.window;
  const filter = document.getElementById('OwnerFilter');
  const list = document.getElementById('OwnerList');

  filter.value = 'bo';
  filter.dispatchEvent(new dom.window.Event('input', { bubbles: true }));
  assert.equal(list.hidden, false);
  assert.deepEqual(
    Array.from(list.children).filter((li) => !li.hidden).map((li) => li.textContent),
    ['Bob']);

  Array.from(list.children)[1].dispatchEvent(new dom.window.MouseEvent('click', { bubbles: true }));
  assert.equal(filter.value, 'Bob');
  assert.equal(document.getElementById('OwnerId').value, '2');
  assert.equal(list.hidden, true);
});

test('committing a selection fires a change event on the hidden value', () => {
  const ctx = setup();
  let changes = 0;
  ctx.value.addEventListener('change', () => { changes += 1; });
  type(ctx, 'mono');
  click(ctx, item(ctx, 5));
  assert.equal(changes, 1);
});

test('clearing the text box fires a change event on the hidden value', () => {
  const ctx = setup(13);
  let changes = 0;
  ctx.value.addEventListener('change', () => { changes += 1; });
  type(ctx, '');
  assert.equal(changes, 1);
});

test('combo boxes marked with data-combo are wired up by class, independently of each other', () => {
  const combo = (n) => `<div data-combo>
       <input class="combo-filter" type="text" role="combobox" value="" />
       <button type="button" class="combo-toggle"></button>
       <input class="combo-value" type="hidden" value="" />
       <ul class="combo-list" role="listbox" hidden>
         <li role="option" data-value="1" data-name="Alice">Alice</li>
         <li role="option" data-value="2" data-name="Bob">Bob</li>
       </ul>
     </div>`;
  const dom = new JSDOM(`<form>${combo(0)}${combo(1)}</form>`, { runScripts: 'outside-only' });
  dom.window.eval(script);
  const [first, second] = Array.from(dom.window.document.querySelectorAll('[data-combo]'));

  const filter = first.querySelector('.combo-filter');
  filter.value = 'bo';
  filter.dispatchEvent(new dom.window.Event('input', { bubbles: true }));
  first.querySelector('.combo-list').children[1].dispatchEvent(new dom.window.MouseEvent('click', { bubbles: true }));

  assert.equal(filter.value, 'Bob');
  assert.equal(first.querySelector('.combo-value').value, '2');
  assert.equal(second.querySelector('.combo-value').value, '');
  assert.equal(second.querySelector('.combo-list').hidden, true);
});

test('exposes an initialiser for combos that are not marked data-combo', () => {
  const dom = new JSDOM(
    `<div id="c"><input id="f" type="text" /><input id="v" type="hidden" value="" />
     <ul id="l" hidden><li data-value="a" data-name="Alpha">Alpha</li></ul></div>`,
    { runScripts: 'outside-only' });
  dom.window.eval(script);
  const { document, comboBox } = dom.window;
  comboBox.init(document.getElementById('c'), document.getElementById('f'),
    document.getElementById('v'), document.getElementById('l'), null);
  document.getElementById('f').dispatchEvent(new dom.window.MouseEvent('click', { bubbles: true }));
  assert.equal(document.getElementById('l').hidden, false);
});

test('items added after initialisation can be filtered and selected', () => {
  const ctx = setup();
  const li = ctx.window.document.createElement('li');
  li.setAttribute('data-value', '99');
  li.setAttribute('data-name', 'Zebra');
  li.textContent = 'Zebra';
  ctx.list.appendChild(li);
  type(ctx, 'zeb');
  assert.deepEqual(shown(ctx), ['Zebra']);
  click(ctx, li);
  assert.equal(ctx.value.value, '99');
  assert.equal(ctx.filter.value, 'Zebra');
});

test('combo:refresh re-reads the selected item text without firing change', () => {
  const ctx = setup(13);
  let changes = 0;
  ctx.value.addEventListener('change', () => changes++);
  item(ctx, 13).textContent = 'Golgari - Bob';
  ctx.value.dispatchEvent(new ctx.window.Event('combo:refresh'));
  assert.equal(ctx.filter.value, 'Golgari - Bob');
  assert.equal(changes, 0);
  ctx.filter.dispatchEvent(new ctx.window.FocusEvent('focusout', { bubbles: true, relatedTarget: null }));
  assert.equal(ctx.filter.value, 'Golgari - Bob');
});
