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
  type(ctx, 'gol');
  ctx.filter.dispatchEvent(new ctx.window.FocusEvent('focusout', { bubbles: true, relatedTarget: null }));
  assert.equal(ctx.filter.value, '');
  assert.equal(ctx.value.value, '');
  assert.equal(ctx.list.hidden, true);
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
