const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(
  path.join(__dirname, '..', '..', 'src', 'wwwroot', 'js', 'deck-matchups.js'),
  'utf8');

const selected = { deckId: 1, label: 'Selected', commander: 'Selected', secondCommander: null };
const opponent = (deckId, label, games, wins, losses, extra = {}) =>
  ({ deckId, label, commander: label, secondCommander: null, games, wins, losses, ...extra });

const rec = (games, wins, losses) => ({ games, wins, losses });
const sizes = (two, three, four) => ({ bySize: { 2: two, 3: three, 4: four } });
const none = rec(0, 0, 0);

const matchups = [
  opponent(2, 'Radha', 10, 7, 2, { commander: 'Radha, Heart of Keld', ...sizes(rec(10, 7, 2), none, none) }), // 70% wins
  opponent(3, 'Aziza', 8, 1, 6, sizes(rec(2, 1, 0), rec(6, 0, 6), none)),                                    // 75% losses
  opponent(4, 'Tiny', 2, 2, 0, sizes(none, rec(2, 2, 0), none)),                                             // 100% wins, only 2 games
  opponent(5, 'Pair', 6, 3, 3, { commander: 'Tymna', secondCommander: 'Thrasios', ...sizes(none, none, rec(6, 3, 3)) }),
];

function setup(data = { selected, matchups }) {
  const dom = new JSDOM(
    `<script type="application/json" id="deck-matchups-data">${JSON.stringify(data)}</script>
     <input type="range" id="minGames" min="1" max="40" value="5" />
     <output class="min-games-value">5</output>
     <div id="player-filter">${['all', '2', '3', '4'].map(n =>
       `<button type="button" data-players="${n}" aria-pressed="${n === 'all'}">${n}</button>`).join('')}</div>
     <div id="matchup-chart" data-deck-url="/DeckMatchups"></div>`,
    { runScripts: 'outside-only' });
  dom.window.eval(script);
  const { document } = dom.window;
  const all = selector => Array.from(document.querySelectorAll(selector));
  return {
    document,
    deckLabels: () => all('.matchup-deck').map(d => d.querySelector('.matchup-deck-label').textContent),
    links: () => all('.matchup-link'),
    choosePlayers(n) {
      document.querySelector(`[data-players="${n}"]`).click();
    },
    pressed: () => all('[data-players]').filter(b => b.getAttribute('aria-pressed') === 'true').map(b => b.dataset.players),
    setSlider(value) {
      const slider = document.getElementById('minGames');
      slider.value = String(value);
      slider.dispatchEvent(new dom.window.Event('input', { bubbles: true }));
    },
  };
}

test('shows the best matchup, the selected deck and the worst matchup in order', () => {
  const { deckLabels } = setup();

  assert.deepEqual(deckLabels(), ['Radha', 'Selected', 'Aziza']);
});

test('only decks with at least the minimum games are considered (default five)', () => {
  const { deckLabels, document } = setup();

  assert.equal(document.querySelector('.min-games-value').textContent, '5');
  assert.ok(!deckLabels().includes('Tiny'));
});

test('lowering the slider lets decks with fewer games become the best matchup', () => {
  const { deckLabels, setSlider, document } = setup();

  setSlider(1);

  assert.deepEqual(deckLabels(), ['Tiny', 'Selected', 'Aziza']);
  assert.equal(document.querySelector('.min-games-value').textContent, '1');
});

test('links are headed best and worst performance against', () => {
  const { links } = setup();

  assert.deepEqual(links().map(l => l.querySelector('.matchup-heading').textContent),
    ['Best performance against', 'Worst performance against']);
});

test('games played are shown above each bar and percentages below', () => {
  const { links } = setup();

  const [best, worst] = links();
  assert.equal(best.querySelector('.matchup-games').textContent, '10 games');
  assert.equal(worst.querySelector('.matchup-games').textContent, '8 games');
  assert.deepEqual(Array.from(best.querySelectorAll('.matchup-percent span')).map(s => s.textContent), ['70%', '20%']);
  assert.deepEqual(Array.from(worst.querySelectorAll('.matchup-percent span')).map(s => s.textContent), ['75%', '13%']);
});

test('best bar puts the selected deck wins (green) on top and the opponent wins (red) below', () => {
  const { links } = setup();

  const segments = Array.from(links()[0].querySelectorAll('.matchup-bar > div'));
  assert.deepEqual(segments.map(s => s.className), ['matchup-selected', 'matchup-other', 'matchup-opponent']);
  assert.deepEqual(segments.map(s => s.style.height), ['70%', '10%', '20%']);
});

test('worst bar puts the opponent wins (red) on top and the selected deck wins (green) below', () => {
  const { links } = setup();

  const segments = Array.from(links()[1].querySelectorAll('.matchup-bar > div'));
  assert.deepEqual(segments.map(s => s.className), ['matchup-opponent', 'matchup-other', 'matchup-selected']);
  assert.deepEqual(segments.map(s => s.style.height), ['75%', '12.5%', '12.5%']);
});

test('commander images come from Scryfall by exact name', () => {
  const { document } = setup();

  const first = document.querySelector('.matchup-deck img');
  assert.equal(first.getAttribute('src'),
    'https://api.scryfall.com/cards/named?format=image&version=normal&exact=Radha%2C%20Heart%20of%20Keld');
  assert.equal(first.getAttribute('alt'), 'Radha, Heart of Keld');
});

test('a deck with two commanders shows both images one above the other', () => {
  const { document, deckLabels } = setup({ selected, matchups: [matchups[3]] });

  assert.deepEqual(deckLabels(), ['Pair', 'Selected', 'Pair']);
  const images = Array.from(document.querySelector('.matchup-deck').querySelectorAll('img'));
  assert.deepEqual(images.map(i => i.getAttribute('alt')), ['Tymna', 'Thrasios']);
  assert.equal(document.querySelector('.matchup-deck-label').textContent, 'Pair');
});

test('decks the selected deck never beat are not shown as best matchup', () => {
  const { deckLabels, links } = setup({ selected, matchups: [opponent(3, 'Aziza', 8, 0, 6)] });

  assert.deepEqual(deckLabels(), ['Selected', 'Aziza']);
  assert.deepEqual(links().map(l => l.querySelector('.matchup-heading').textContent), ['Worst performance against']);
});

test('ties on win percentage favour the deck with more games', () => {
  const { deckLabels } = setup({
    selected,
    matchups: [opponent(2, 'Few', 6, 3, 0), opponent(3, 'Many', 10, 5, 0)],
  });

  assert.equal(deckLabels()[0], 'Many');
});

test('the colored bar sits between a block above it and a block below it', () => {
  const { links } = setup();

  links().forEach(link => {
    assert.deepEqual(Array.from(link.children).map(c => c.className), ['matchup-above', 'matchup-bar', 'matchup-below']);
  });
});

test('player count filter defaults to all games', () => {
  const { pressed } = setup();

  assert.deepEqual(pressed(), ['all']);
});

test('choosing a player count only counts games of that size', () => {
  const { choosePlayers, deckLabels, links, pressed, setSlider } = setup();
  setSlider(1);

  choosePlayers('3');

  // 3-player: Tiny wins 2/2 (100%), Aziza loses 6/6 (100%).
  assert.deepEqual(deckLabels(), ['Tiny', 'Selected', 'Aziza']);
  assert.deepEqual(pressed(), ['3']);
  assert.equal(links()[1].querySelector('.matchup-games').textContent, '6 games');
});

test('the minimum games applies to the filtered game count', () => {
  const { choosePlayers, deckLabels } = setup();

  choosePlayers('2');

  // Only Radha has five or more 2-player games.
  assert.deepEqual(deckLabels(), ['Radha', 'Selected', 'Radha']);
});

test('switching back to all restores every game', () => {
  const { choosePlayers, deckLabels } = setup();
  choosePlayers('4');

  choosePlayers('all');

  assert.deepEqual(deckLabels(), ['Radha', 'Selected', 'Aziza']);
});

test('the best and worst decks link to their own matchups page but the selected deck does not', () => {
  const { document } = setup();

  const decks = Array.from(document.querySelectorAll('.matchup-deck'));
  assert.deepEqual(decks.map(d => d.tagName), ['A', 'DIV', 'A']);
  assert.deepEqual([decks[0], decks[2]].map(d => d.getAttribute('href')),
    ['/DeckMatchups?id=2', '/DeckMatchups?id=3']);
  assert.equal(decks[1].getAttribute('href'), null);
});

test('shows a message when no deck meets the minimum games', () => {
  const { links, setSlider, document, deckLabels } = setup();

  setSlider(11);

  assert.equal(links().length, 0);
  assert.deepEqual(deckLabels(), ['Selected']);
  assert.match(document.getElementById('matchup-chart').textContent, /No decks with at least 11 games/);
});
