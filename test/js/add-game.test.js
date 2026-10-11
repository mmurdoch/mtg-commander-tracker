const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const read = (name) =>
  fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'wwwroot', 'js', name), 'utf8');
const comboScript = read('color-filter.js');
const addGameScript = read('add-game.js');

const people = [[1, 'Alice'], [2, 'Bob'], [3, 'Cara']];
const decks = [[10, 'Alpha (Cats)', 1], [20, 'Beta', 2], [30, 'Gamma', 3]];

function combo(kind, i, items, hiddenClass) {
  const lis = items
    .map(([id, name, owner]) =>
      `<li role="option" data-value="${id}" data-name="${name}"${owner ? ` data-owner-id="${owner}"` : ''}>${name}</li>`)
    .join('');
  return `<div data-combo>
    <input type="text" id="${kind}${i}Filter" class="combo-filter" value="" />
    <button type="button" class="combo-toggle"></button>
    <input type="hidden" class="combo-value ${hiddenClass}" value="" />
    <ul class="combo-list" hidden>${lis}</ul>
  </div>`;
}

function setup() {
  let rows = '';
  for (let i = 0; i < 10; i++) {
    rows += `<div class="participant-row">
      ${combo('Deck', i, decks, 'deck-id')}
      ${combo('Player', i, people, 'player-id')}
      <input type="radio" name="Input.WinnerPlayerId" value="0" class="winner-radio" />
    </div>`;
  }
  const dom = new JSDOM(
    `<form>
       <input type="date" class="game-date" value="2026-10-10" />
       <input type="radio" class="player-count-option" id="players-2" name="c" value="2" />
       <input type="radio" class="player-count-option" id="players-3" name="c" value="3" />
       <input type="radio" class="player-count-option" id="players-4" name="c" value="4" checked />
       <input type="radio" class="player-count-option" id="players-more" name="c" value="more" />
       <input type="number" id="morePlayerCount" value="5" />
       <input type="checkbox" class="draw-checkbox" />
       <div id="participantRows">${rows}</div>
       <button type="submit" id="add">Add</button>
     </form>`,
    { runScripts: 'outside-only' });
  dom.window.eval(comboScript);
  dom.window.eval(addGameScript);
  const { document } = dom.window;
  const ctx = {
    window: dom.window,
    add: document.getElementById('add'),
    draw: document.querySelector('.draw-checkbox'),
    row: (i) => document.querySelectorAll('.participant-row')[i],
  };
  ctx.choose = (i, kind, id) => {
    const li = ctx.row(i).querySelectorAll(`[data-combo]`)[kind === 'deck' ? 0 : 1]
      .querySelector(`li[data-value="${id}"]`);
    li.dispatchEvent(new dom.window.MouseEvent('click', { bubbles: true }));
  };
  ctx.playerValue = (i) => ctx.row(i).querySelector('.player-id').value;
  ctx.playerText = (i) => ctx.row(i).querySelector('#Player' + i + 'Filter').value;
  ctx.winner = (i) => ctx.row(i).querySelector('.winner-radio');
  ctx.setCount = (n) => {
    const radio = document.getElementById('players-' + n);
    radio.checked = true;
    radio.dispatchEvent(new dom.window.Event('change', { bubbles: true }));
  };
  ctx.fill = (n) => {
    for (let i = 0; i < n; i++) {
      ctx.choose(i, 'deck', decks[i % 3][0]);
      ctx.choose(i, 'player', people[(i + 1) % 3][0]);
    }
  };
  ctx.pickWinner = (i) => {
    ctx.winner(i).checked = true;
    ctx.winner(i).dispatchEvent(new dom.window.Event('change', { bubbles: true }));
  };
  ctx.toggleDraw = (checked) => {
    ctx.draw.checked = checked;
    ctx.draw.dispatchEvent(new dom.window.Event('change', { bubbles: true }));
  };
  return ctx;
}

test('Add is disabled initially', () => {
  assert.equal(setup().add.disabled, true);
});

test('Add is enabled once every visible row has deck, player and a winner', () => {
  const ctx = setup();
  ctx.setCount(2);
  ctx.fill(2);
  assert.equal(ctx.add.disabled, true, 'no winner yet');
  ctx.pickWinner(1);
  assert.equal(ctx.add.disabled, false);
});

test('Add stays disabled when a visible row lacks a deck or player', () => {
  const ctx = setup();
  ctx.setCount(2);
  ctx.choose(0, 'deck', 10);
  ctx.choose(0, 'player', 1);
  ctx.choose(1, 'player', 2);
  ctx.pickWinner(0);
  assert.equal(ctx.add.disabled, true);
});

test('Add is disabled again when the player count grows beyond the filled rows', () => {
  const ctx = setup();
  ctx.setCount(2);
  ctx.fill(2);
  ctx.pickWinner(0);
  assert.equal(ctx.add.disabled, false);
  ctx.setCount(3);
  assert.equal(ctx.add.disabled, true);
});

test('a draw satisfies the winner requirement', () => {
  const ctx = setup();
  ctx.setCount(2);
  ctx.fill(2);
  ctx.toggleDraw(true);
  assert.equal(ctx.add.disabled, false);
});

test('checking draw deselects every winner radio', () => {
  const ctx = setup();
  ctx.setCount(2);
  ctx.fill(2);
  ctx.pickWinner(1);
  ctx.toggleDraw(true);
  assert.equal(ctx.winner(0).checked, false);
  assert.equal(ctx.winner(1).checked, false);
});

test('picking a winner clears the draw checkbox', () => {
  const ctx = setup();
  ctx.setCount(2);
  ctx.fill(2);
  ctx.toggleDraw(true);
  ctx.pickWinner(0);
  assert.equal(ctx.draw.checked, false);
});

test('winner radio value follows the selected player', () => {
  const ctx = setup();
  ctx.choose(0, 'player', 2);
  assert.equal(ctx.winner(0).value, '2');
});

test('selecting a deck defaults the player to the deck owner', () => {
  const ctx = setup();
  ctx.choose(0, 'deck', 20);
  assert.equal(ctx.playerValue(0), '2');
  assert.equal(ctx.playerText(0), 'Bob');
  assert.equal(ctx.winner(0).value, '2');
});

test('changing the deck follows the owner while the player was only defaulted', () => {
  const ctx = setup();
  ctx.choose(0, 'deck', 20);
  ctx.choose(0, 'deck', 30);
  assert.equal(ctx.playerValue(0), '3');
});

test('Tab from a player combo box focuses the winner radio in the same row', () => {
  const ctx = setup();
  for (const i of [0, 1, 2]) {
    const filter = ctx.row(i).querySelector('#Player' + i + 'Filter');
    filter.focus();
    const event = new ctx.window.KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
    filter.dispatchEvent(event);
    assert.equal(event.defaultPrevented, true);
    assert.equal(ctx.window.document.activeElement, ctx.winner(i));
  }
});

test('Shift+Tab from a player combo box keeps the default focus movement', () => {
  const ctx = setup();
  const filter = ctx.row(1).querySelector('#Player1Filter');
  filter.focus();
  const event = new ctx.window.KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true });
  filter.dispatchEvent(event);
  assert.equal(event.defaultPrevented, false);
});

test('Tab from a deck combo box is left alone', () => {
  const ctx = setup();
  const filter = ctx.row(1).querySelector('#Deck1Filter');
  const event = new ctx.window.KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
  filter.dispatchEvent(event);
  assert.equal(event.defaultPrevented, false);
});

test('a manually chosen player is not overridden by the deck owner', () => {
  const ctx = setup();
  ctx.choose(0, 'player', 1);
  ctx.choose(0, 'deck', 20);
  assert.equal(ctx.playerValue(0), '1');
  ctx.choose(0, 'deck', 30);
  assert.equal(ctx.playerValue(0), '1');
});
