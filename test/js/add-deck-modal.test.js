const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const read = (name) =>
  fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'wwwroot', 'js', name), 'utf8');
const comboScript = read('color-filter.js');
const modalScript = read('add-deck-modal.js');

const people = [[1, 'Alice'], [2, 'Bob']];
const decks = [[10, 'Alpha', 1], [20, 'Beta', 2]];
const commanders = [
  { name: 'Niv-Mizzet', colors: 'UR' },
  { name: 'Tymna the Weaver', colors: 'WB' },
];

function lis(items, withOwner) {
  return items
    .map(([id, name, owner]) =>
      `<li data-value="${id}" data-name="${name}"${withOwner ? ` data-owner-id="${owner}"` : ''}>${name}</li>`)
    .join('');
}

function row(i) {
  return `<div class="participant-row">
    <div data-combo>
      <input type="text" id="Deck${i}Filter" class="combo-filter" value="" />
      <input type="hidden" class="combo-value deck-id" value="" />
      <ul class="combo-list" hidden>${lis(decks, true)}</ul>
    </div>
    <button type="button" class="new-deck-btn">+ New deck</button>
    <div data-combo>
      <input type="text" id="Player${i}Filter" class="combo-filter" value="" />
      <input type="hidden" class="combo-value player-id" value="" />
      <ul class="combo-list" hidden>${lis(people, false)}</ul>
    </div>
  </div>`;
}

function modalCombo(name, list) {
  return `<div data-modal-combo>
    <input type="text" class="combo-filter" />
    <button type="button" class="combo-toggle"></button>
    <input type="hidden" name="${name}" class="combo-value" value="" />
    <ul class="combo-list ${name.endsWith('ommander') ? 'commander-list' : ''}" hidden>${list}</ul>
  </div>`;
}

function response(status, body) {
  return { ok: status >= 200 && status < 300, status, json: () => Promise.resolve(body) };
}

const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

function setup(responses = []) {
  const dom = new JSDOM(
    `<form id="game">${row(0)}${row(1)}<button type="submit">Add</button></form>
     <div id="newDeckModal" data-commanders-url="/AddGame?handler=Commanders">
       <form id="newDeckForm" method="post" action="/AddGame?handler=AddDeck">
         <input type="hidden" name="__RequestVerificationToken" value="tok" />
         <input type="text" id="NewDeckName" name="name" />
         ${modalCombo('commander', '')}
         ${modalCombo('secondCommander', '')}
         ${modalCombo('ownerId', lis(people, false))}
         <span data-error-for="Commander"></span>
         <span data-error-for="OwnerId"></span>
         <div id="newDeckError" hidden></div>
         <button type="submit" id="newDeckSubmit">Add deck</button>
       </form>
     </div>`,
    { runScripts: 'outside-only', url: 'http://localhost/AddGame' });
  const { window } = dom;
  const calls = [];
  const queue = [...responses];
  window.fetch = (url, options) => {
    calls.push({ url, options });
    return Promise.resolve(queue.shift());
  };
  const modal = { shown: 0, hidden: 0 };
  window.bootstrap = {
    Modal: {
      getOrCreateInstance: () => ({
        show: () => { modal.shown++; },
        hide: () => { modal.hidden++; },
      }),
    },
  };
  window.eval(comboScript);
  window.eval(modalScript);

  const { document } = window;
  const form = document.getElementById('newDeckForm');
  const ctx = { window, calls, modal, form };
  ctx.row = (i) => document.querySelectorAll('.participant-row')[i];
  ctx.deckValue = (i) => ctx.row(i).querySelector('.deck-id').value;
  ctx.deckText = (i) => ctx.row(i).querySelector('#Deck' + i + 'Filter').value;
  ctx.deckLabels = (i) =>
    Array.from(ctx.row(i).querySelectorAll('.deck-id ~ ul li')).map((li) => li.textContent);
  ctx.field = (name) => form.querySelector(`[name="${name}"]`);
  ctx.commanderLabels = (name) =>
    Array.from(ctx.field(name).parentNode.querySelectorAll('li')).map((li) => li.textContent);
  ctx.open = async (i) => {
    ctx.row(i).querySelector('.new-deck-btn').dispatchEvent(new window.MouseEvent('click', { bubbles: true }));
    await flush();
  };
  ctx.submit = async () => {
    form.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));
    await flush();
  };
  ctx.pick = (name, text) => {
    const combo = ctx.field(name).closest('[data-modal-combo]');
    const li = Array.from(combo.querySelectorAll('li')).find((x) => x.textContent === text);
    li.dispatchEvent(new window.MouseEvent('click', { bubbles: true }));
  };
  return ctx;
}

const commandersOk = () => response(200, commanders);

test('clicking + New deck shows the modal', async () => {
  const ctx = setup([commandersOk()]);
  await ctx.open(0);
  assert.equal(ctx.modal.shown, 1);
});

test('commanders are fetched once, on first open, into both commander lists', async () => {
  const ctx = setup([commandersOk()]);
  await ctx.open(0);
  await ctx.open(1);
  assert.equal(ctx.calls.length, 1);
  assert.equal(ctx.calls[0].url, '/AddGame?handler=Commanders');
  assert.deepEqual(ctx.commanderLabels('commander'), ['Niv-Mizzet', 'Tymna the Weaver']);
  assert.deepEqual(ctx.commanderLabels('secondCommander'), ['Niv-Mizzet', 'Tymna the Weaver']);
  const li = ctx.field('commander').parentNode.querySelector('li');
  assert.equal(li.getAttribute('data-value'), 'Niv-Mizzet');
  assert.equal(li.getAttribute('data-colors'), 'UR');
});

test('a failed commanders fetch is retried on the next open', async () => {
  const ctx = setup([response(500, {}), commandersOk()]);
  await ctx.open(0);
  await ctx.open(0);
  assert.equal(ctx.calls.length, 2);
  assert.equal(ctx.commanderLabels('commander').length, 2);
});

test('the owner defaults to the row\'s player when one is chosen', async () => {
  const ctx = setup([commandersOk()]);
  const player = ctx.row(1).querySelector('.player-id');
  player.value = '2';
  await ctx.open(1);
  assert.equal(ctx.field('ownerId').value, '2');
  assert.equal(ctx.field('ownerId').closest('[data-modal-combo]').querySelector('.combo-filter').value, 'Bob');
});

test('the owner is blank when the row has no player', async () => {
  const ctx = setup([commandersOk()]);
  await ctx.open(0);
  assert.equal(ctx.field('ownerId').value, '');
});

test('submitting posts the form data including the antiforgery token', async () => {
  const ctx = setup([commandersOk(), response(200, { deckId: 30, decks: [] })]);
  await ctx.open(0);
  ctx.pick('commander', 'Niv-Mizzet');
  ctx.pick('ownerId', 'Alice');
  const event = new ctx.window.Event('submit', { bubbles: true, cancelable: true });
  ctx.form.dispatchEvent(event);
  await flush();
  assert.ok(event.defaultPrevented);
  const post = ctx.calls[1];
  assert.equal(post.url, '/AddGame?handler=AddDeck');
  assert.equal(post.options.method, 'POST');
  assert.equal(post.options.body.get('commander'), 'Niv-Mizzet');
  assert.equal(post.options.body.get('ownerId'), '1');
  assert.equal(post.options.body.get('__RequestVerificationToken'), 'tok');
});

const created = () => response(200, {
  deckId: 30,
  decks: [
    { id: 10, label: 'Alpha - Alice', ownerId: 1 },
    { id: 20, label: 'Beta', ownerId: 2 },
    { id: 30, label: 'Gamma', ownerId: 2 },
  ],
});

test('on success every row\'s deck list is rebuilt and the modal closes', async () => {
  const ctx = setup([commandersOk(), created()]);
  await ctx.open(0);
  await ctx.submit();
  assert.deepEqual(ctx.deckLabels(0), ['Alpha - Alice', 'Beta', 'Gamma']);
  assert.deepEqual(ctx.deckLabels(1), ['Alpha - Alice', 'Beta', 'Gamma']);
  const li = ctx.row(1).querySelector('.deck-id ~ ul li:last-child');
  assert.equal(li.getAttribute('data-value'), '30');
  assert.equal(li.getAttribute('data-owner-id'), '2');
  assert.equal(li.getAttribute('data-name'), 'Gamma');
  assert.equal(ctx.modal.hidden, 1);
});

test('on success the new deck is selected in the originating row only, firing change', async () => {
  const ctx = setup([commandersOk(), created()]);
  let changes = 0;
  ctx.row(1).querySelector('.deck-id').addEventListener('change', () => changes++);
  await ctx.open(1);
  await ctx.submit();
  assert.equal(ctx.deckValue(1), '30');
  assert.equal(ctx.deckText(1), 'Gamma');
  assert.equal(changes, 1);
  assert.equal(ctx.deckValue(0), '');
});

test('on success other rows keep their selection and pick up a relabelled deck', async () => {
  const ctx = setup([commandersOk(), created()]);
  const deck = ctx.row(0).querySelector('.deck-id');
  deck.value = '10';
  await ctx.open(1);
  await ctx.submit();
  assert.equal(ctx.deckValue(0), '10');
  assert.equal(ctx.deckText(0), 'Alpha - Alice');
});

test('the new deck is selectable through the rebuilt list', async () => {
  const ctx = setup([commandersOk(), created()]);
  await ctx.open(0);
  await ctx.submit();
  ctx.row(1).querySelector('.deck-id ~ ul li:last-child')
    .dispatchEvent(new ctx.window.MouseEvent('click', { bubbles: true }));
  assert.equal(ctx.deckValue(1), '30');
  assert.equal(ctx.deckText(1), 'Gamma');
});

test('validation errors are shown beside their fields and the modal stays open', async () => {
  const ctx = setup([commandersOk(), response(400, { errors: { Commander: 'Commander is required.', OwnerId: 'Please select an owner.' } })]);
  await ctx.open(0);
  await ctx.submit();
  const text = (key) => ctx.form.querySelector(`[data-error-for="${key}"]`).textContent;
  assert.equal(text('Commander'), 'Commander is required.');
  assert.equal(text('OwnerId'), 'Please select an owner.');
  assert.equal(ctx.modal.hidden, 0);
  assert.equal(ctx.deckValue(0), '');
  assert.deepEqual(ctx.deckLabels(0), ['Alpha', 'Beta']);
});

test('an error for an unknown field or a network failure shows a general message', async () => {
  const ctx = setup([commandersOk(), response(500, {})]);
  await ctx.open(0);
  await ctx.submit();
  const general = ctx.window.document.getElementById('newDeckError');
  assert.equal(general.hidden, false);
  assert.ok(general.textContent.length > 0);
  assert.equal(ctx.modal.hidden, 0);
});

test('reopening clears earlier input and errors', async () => {
  const ctx = setup([commandersOk(), response(400, { errors: { Commander: 'Commander is required.' } })]);
  await ctx.open(0);
  ctx.window.document.getElementById('NewDeckName').value = 'Arcane';
  await ctx.submit();
  await ctx.open(0);
  assert.equal(ctx.window.document.getElementById('NewDeckName').value, '');
  assert.equal(ctx.form.querySelector('[data-error-for="Commander"]').textContent, '');
});

test('submit is disabled while the request is in flight', async () => {
  let release;
  const ctx = setup([commandersOk()]);
  ctx.window.fetch = (url, options) => {
    ctx.calls.push({ url, options });
    if (ctx.calls.length === 1) return Promise.resolve(commandersOk());
    return new Promise((resolve) => { release = () => resolve(created()); });
  };
  await ctx.open(0);
  ctx.form.dispatchEvent(new ctx.window.Event('submit', { bubbles: true, cancelable: true }));
  await flush();
  const button = ctx.window.document.getElementById('newDeckSubmit');
  assert.equal(button.disabled, true);
  release();
  await flush();
  assert.equal(button.disabled, false);
});
