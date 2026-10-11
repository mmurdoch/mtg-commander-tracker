const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(
  path.join(__dirname, '..', '..', 'src', 'wwwroot', 'js', 'home-charts.js'),
  'utf8');

const entry = (label, games, wins) => ({ label, games, wins });

const data = {
  people: {
    2: [entry('Low', 4, 1), entry('High', 2, 2), entry('Mid', 10, 5)],
    3: [],
    4: [],
  },
  decks: {
    2: [
      entry('A', 10, 5),   // 50%
      entry('B', 6, 4),    // 67%
      entry('C', 5, 5),    // 100%
      entry('D', 20, 12),  // 60%
      entry('E', 8, 2),    // 25%
      entry('F', 12, 9),   // 75%
      entry('Tiny', 1, 1), // 100% but only 1 game
    ],
    3: [entry('Only', 3, 1)],
    4: [],
  },
};

function setup(chartData = data) {
  const sliders = [2, 3, 4].map(n =>
    `<div data-chart="decks" data-players="${n}">
       <input type="range" class="min-games-slider" min="0" max="40" value="5" />
       <output class="min-games-value">5</output>
       <div class="chart-bars"></div>
     </div>`).join('');
  const people = [2, 3, 4].map(n =>
    `<div data-chart="people" data-players="${n}"><div class="chart-bars"></div></div>`).join('');
  const dom = new JSDOM(
    `<script type="application/json" id="home-charts-data">${JSON.stringify(chartData)}</script>${people}${sliders}`,
    { runScripts: 'outside-only' });
  dom.window.eval(script);
  const { document } = dom.window;
  const chart = (kind, size) => document.querySelector(`[data-chart="${kind}"][data-players="${size}"]`);
  return {
    document,
    window: dom.window,
    chart,
    labels: (kind, size) => Array.from(chart(kind, size).querySelectorAll('.chart-label')).map(e => e.textContent),
    values: (kind, size) => Array.from(chart(kind, size).querySelectorAll('.chart-value')).map(e => e.textContent),
    setSlider(size, value) {
      const slider = chart('decks', size).querySelector('input[type="range"]');
      slider.value = String(value);
      slider.dispatchEvent(new dom.window.Event('input', { bubbles: true }));
    },
  };
}

test('people chart lists entries ordered by win percentage', () => {
  const { labels, values } = setup();

  assert.deepEqual(labels('people', 2), ['High', 'Mid', 'Low']);
  assert.deepEqual(values('people', 2), ['100% (2/2)', '50% (5/10)', '25% (1/4)']);
});

test('entries with equal displayed win percentages are ordered by games played descending', () => {
  const { labels, values } = setup({
    people: { 2: [entry('Few', 2, 1), entry('Many', 100, 50), entry('Some', 10, 5), entry('Top', 3, 3)], 3: [], 4: [] },
    decks: {
      2: [entry('A', 52, 35), entry('B', 100, 67), entry('C', 6, 4)], // 67.3%, 67%, 66.7% all display as 67%
      3: [],
      4: [],
    },
  });

  assert.deepEqual(labels('people', 2), ['Top', 'Many', 'Some', 'Few']);
  assert.deepEqual(labels('decks', 2), ['B', 'A', 'C']);
  assert.deepEqual(values('decks', 2), ['67% (67/100)', '67% (35/52)', '67% (4/6)']);
});

test('chart labels expose the full name as a hover tooltip', () => {
  const { chart } = setup({
    people: { 2: [entry('A very long person name', 1, 1)], 3: [], 4: [] },
    decks: { 2: [entry('A very long commander name (Deck)', 5, 3)], 3: [], 4: [] },
  });

  assert.equal(chart('people', 2).querySelector('.chart-label').title, 'A very long person name');
  assert.equal(chart('decks', 2).querySelector('.chart-label').title, 'A very long commander name (Deck)');
});

test('people chart sizes bars by win percentage', () => {
  const { chart } = setup();

  const widths = Array.from(chart('people', 2).querySelectorAll('.chart-bar')).map(b => b.style.width);
  assert.deepEqual(widths, ['100%', '50%', '25%']);
});

test('bars are green above the expected win percentage and red at or below it', () => {
  const { chart } = setup({
    people: {
      2: [entry('Above', 3, 2), entry('Equal', 2, 1), entry('Below', 3, 1)],
      3: [entry('Above', 3, 2), entry('Equal', 3, 1), entry('Below', 4, 1)],
      4: [entry('Above', 3, 2), entry('Equal', 4, 1), entry('Below', 5, 1)],
    },
    decks: { 2: [entry('Deck', 10, 6)], 3: [entry('Deck', 10, 2)], 4: [entry('Deck', 10, 3)] },
  });

  const classes = (kind, size) => Array.from(chart(kind, size).querySelectorAll('.chart-bar'))
    .map(b => b.classList.contains('chart-bar-above') ? 'above'
      : b.classList.contains('chart-bar-below') ? 'below' : 'none');

  assert.deepEqual(classes('people', 2), ['above', 'below', 'below']);
  assert.deepEqual(classes('people', 3), ['above', 'below', 'below']);
  assert.deepEqual(classes('people', 4), ['above', 'below', 'below']);
  assert.deepEqual(classes('decks', 2), ['above']);
  assert.deepEqual(classes('decks', 3), ['below']);
  assert.deepEqual(classes('decks', 4), ['above']);
});

test('each chart has one expected win percentage line spanning all rows', () => {
  const { chart } = setup({
    people: {
      2: [entry('P', 4, 1), entry('Q', 4, 2), entry('R', 4, 3)],
      3: [entry('P', 4, 1)],
      4: [entry('P', 4, 1)],
    },
    decks: { 2: [entry('D', 10, 5)], 3: [], 4: [] },
  });

  const lines = (kind, size) => chart(kind, size).querySelectorAll('.chart-expected');
  const left = (kind, size) => lines(kind, size)[0].style.left;
  assert.equal(lines('people', 2).length, 1);
  assert.equal(left('people', 2), '50%');
  assert.equal(left('people', 4), '25%');
  assert.equal(left('decks', 2), '50%');
  assert.match(left('people', 3), /^33\.3+\d*%$/);
  assert.equal(lines('people', 2)[0].style.gridRow, '1 / span 3');
  assert.equal(chart('people', 2).querySelectorAll('.chart-track').length, 3);
});

test('every item in a row is placed on that row of the chart grid', () => {
  const { chart } = setup();

  const rows = Array.from(chart('people', 2).querySelectorAll('.chart-row'));
  rows.forEach((row, i) => {
    assert.equal(row.children.length, 4);
    Array.from(row.children).forEach(item => assert.equal(item.style.gridRow, String(i + 1)));
  });
});

test('no expected line is drawn when a chart is empty', () => {
  const { chart } = setup();

  assert.equal(chart('people', 3).querySelector('.chart-expected'), null);
});

test('empty chart shows a message', () => {
  const { chart } = setup();

  assert.match(chart('people', 3).textContent, /No games/);
});

test('deck chart defaults to decks with at least five games and shows the top five by win percentage', () => {
  const { labels } = setup();

  assert.deepEqual(labels('decks', 2), ['C', 'F', 'B', 'D', 'A']);
});

test('lowering the slider lets decks with fewer games into the top five', () => {
  const { labels, setSlider, chart } = setup();

  setSlider(2, 0);

  assert.deepEqual(labels('decks', 2), ['C', 'Tiny', 'F', 'B', 'D']);
  assert.equal(chart('decks', 2).querySelector('.min-games-value').textContent, '0');
});

test('raising the slider excludes decks below the minimum', () => {
  const { labels, setSlider } = setup();

  setSlider(2, 10);

  assert.deepEqual(labels('decks', 2), ['F', 'D', 'A']);
});

test('deck charts have independent sliders and show a message when nothing qualifies', () => {
  const { labels, chart, setSlider } = setup();

  assert.match(chart('decks', 3).textContent, /No decks/);
  setSlider(3, 1);

  assert.deepEqual(labels('decks', 3), ['Only']);
  assert.deepEqual(labels('decks', 2), ['C', 'F', 'B', 'D', 'A']);
});

test('labels are rendered as text, not markup', () => {
  const { chart } = setup({ people: { 2: [entry('<b>x</b>', 1, 1)], 3: [], 4: [] }, decks: { 2: [], 3: [], 4: [] } });

  assert.equal(chart('people', 2).querySelector('b'), null);
  assert.equal(chart('people', 2).querySelector('.chart-label').textContent, '<b>x</b>');
});
