const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(
  path.join(__dirname, '..', '..', 'src', 'wwwroot', 'js', 'deck-network.js'),
  'utf8');

const data = {
  nodes: [
    { deckId: 1, label: 'Atraxa', games: 5 },
    { deckId: 2, label: 'Meren', games: 4 },
    { deckId: 3, label: 'Korvold', games: 3 },
  ],
  edges: [
    { source: 1, target: 2, games: 3 },
    { source: 1, target: 3, games: 2 },
    { source: 2, target: 3, games: 1 },
  ],
};

function setup(payload = data, focusDeck = null) {
  const dom = new JSDOM(
    `<script type="application/json" id="deck-network-data">${JSON.stringify(payload)}</script>
     <div id="network-filter">
       <input type="range" id="minDeckGames" min="1" max="1" value="1" />
       <output class="min-games-value">1</output>
     </div>
     <div id="deck-network" data-deck-url="/DeckMatchups"${focusDeck === null ? '' : ` data-focus-deck="${focusDeck}"`}></div>`,
    { runScripts: 'outside-only' });
  dom.window.eval(script);
  const { document } = dom.window;
  const all = selector => Array.from(document.querySelectorAll(selector));
  return {
    document,
    slider: () => document.getElementById('minDeckGames'),
    filterHidden: () => document.getElementById('network-filter').hidden,
    // Returns true when the click's default navigation was cancelled.
    click(label) {
      const node = all('.network-node').find(n => n.querySelector('text').textContent === label);
      const event = new dom.window.MouseEvent('click', { bubbles: true, cancelable: true });
      node.dispatchEvent(event);
      return event.defaultPrevented;
    },
    setSlider(value) {
      const slider = document.getElementById('minDeckGames');
      slider.value = String(value);
      slider.dispatchEvent(new dom.window.Event('input', { bubbles: true }));
    },
    labels: () => all('.network-node text').map(t => t.textContent),
    shownLabels: () => all('.network-node text:not(.network-label-hidden)').map(t => t.textContent),
    hiddenLabels: () => all('text.network-label-hidden').map(t => t.textContent),
    edges: () => all('.network-edge'),
    nodes: () => all('.network-node'),
    edgeBetween: (a, b) => document.querySelector(`.network-edge[data-source="${a}"][data-target="${b}"]`),
  };
}

test('draws one node per deck, labelled and linked to its matchups', () => {
  const { nodes } = setup();

  assert.deepEqual(nodes().map(n => n.querySelector('text').textContent), ['Atraxa', 'Meren', 'Korvold']);
  assert.deepEqual(nodes().map(n => n.getAttribute('href')),
    ['/DeckMatchups?id=1', '/DeckMatchups?id=2', '/DeckMatchups?id=3']);
});

test('draws one edge per pair of decks', () => {
  const { edges } = setup();

  assert.equal(edges().length, 3);
});

test('edge width grows with the number of games', () => {
  const { edgeBetween } = setup();
  const width = (a, b) => parseFloat(edgeBetween(a, b).getAttribute('stroke-width'));

  assert.ok(width(1, 2) > width(1, 3));
  assert.ok(width(1, 3) > width(2, 3));
  assert.ok(width(2, 3) > 0);
});

test('edge width is proportional to games', () => {
  const { edgeBetween } = setup({
    nodes: data.nodes,
    edges: [{ source: 1, target: 2, games: 10 }, { source: 1, target: 3, games: 5 }],
  });
  const width = (a, b) => parseFloat(edgeBetween(a, b).getAttribute('stroke-width'));

  assert.equal(width(1, 2), width(1, 3) * 2);
});

test('edges describe their games in a tooltip', () => {
  const { edgeBetween } = setup();

  assert.equal(edgeBetween(1, 2).querySelector('title').textContent, 'Atraxa vs Meren: 3 games');
  assert.equal(edgeBetween(2, 3).querySelector('title').textContent, 'Meren vs Korvold: 1 game');
});

test('edges run between the positions of their two nodes', () => {
  const { edgeBetween, nodes } = setup();
  const position = node => {
    const circle = node.querySelector('circle');
    return [circle.getAttribute('cx'), circle.getAttribute('cy')];
  };
  const edge = edgeBetween(1, 2);
  const [first, second] = nodes();

  assert.deepEqual([edge.getAttribute('x1'), edge.getAttribute('y1')], position(first));
  assert.deepEqual([edge.getAttribute('x2'), edge.getAttribute('y2')], position(second));
});

test('layout is finite and keeps nodes apart', () => {
  const { nodes } = setup();
  const points = nodes().map(n => {
    const c = n.querySelector('circle');
    return [parseFloat(c.getAttribute('cx')), parseFloat(c.getAttribute('cy'))];
  });

  points.forEach(p => p.forEach(v => assert.ok(Number.isFinite(v))));
  for (let i = 0; i < points.length; i++) {
    for (let j = i + 1; j < points.length; j++) {
      assert.ok(Math.hypot(points[i][0] - points[j][0], points[i][1] - points[j][1]) > 10);
    }
  }
});

// Deck n (1-based) has n games, linked in a chain so every deck has an edge.
function chain(count) {
  const nodes = Array.from({ length: count }, (_, i) => ({ deckId: i + 1, label: `Deck ${i + 1}`, games: i + 1 }));
  const edges = Array.from({ length: count - 1 }, (_, i) => ({ source: i + 1, target: i + 2, games: 1 }));
  return { nodes, edges };
}

test('by default only about twenty of the most played decks are shown', () => {
  const { nodes, slider, document } = setup(chain(25));

  assert.equal(nodes().length, 20);
  assert.equal(slider().value, '6');
  assert.equal(document.querySelector('.min-games-value').textContent, '6');
});

test('every deck is shown by default when there are fewer than twenty', () => {
  const { nodes, slider } = setup(chain(10));

  assert.equal(nodes().length, 10);
  assert.equal(slider().value, '1');
});

test('the slider ranges up to the most games played by any deck', () => {
  const { slider } = setup(chain(25));

  assert.equal(slider().min, '1');
  assert.equal(slider().max, '25');
});

test('raising the slider hides decks with fewer games and their edges', () => {
  const { nodes, edges, labels, setSlider, document } = setup(chain(25));

  setSlider(23);

  assert.deepEqual(labels(), ['Deck 23', 'Deck 24', 'Deck 25']);
  assert.equal(edges().length, 2);
  assert.equal(document.querySelector('.min-games-value').textContent, '23');
  assert.equal(nodes().length, 3);
});

test('layout stretches to fill the available drawing area', () => {
  // Every deck played every other, the case where the layout otherwise collapses into a clump.
  const nodes = Array.from({ length: 15 }, (_, i) => ({ deckId: i + 1, label: `Deck ${i + 1}`, games: 14 }));
  const edges = [];
  for (let a = 1; a <= 15; a++) for (let b = a + 1; b <= 15; b++) edges.push({ source: a, target: b, games: 1 });
  const { document } = setup({ nodes, edges });
  const circles = Array.from(document.querySelectorAll('.network-node circle'));
  const xs = circles.map(c => parseFloat(c.getAttribute('cx')));
  const ys = circles.map(c => parseFloat(c.getAttribute('cy')));
  const [, , width, height] = document.querySelector('svg').getAttribute('viewBox').split(' ').map(Number);

  assert.ok(Math.min(...xs) <= 0.05 * width);
  assert.ok(Math.max(...xs) >= 0.6 * width);
  assert.ok(Math.min(...ys) <= 0.1 * height);
  assert.ok(Math.max(...ys) >= 0.9 * height);
});

test('clicking a node redraws with only that deck and the decks it has played, whatever the slider says', () => {
  const { click, labels, edges, filterHidden } = setup(chain(25));

  const prevented = click('Deck 6');

  assert.ok(prevented);
  assert.deepEqual(labels().sort(), ['Deck 5', 'Deck 6', 'Deck 7']);
  assert.equal(edges().length, 2);
  assert.equal(filterHidden(), true);
});

test('a focused deck is marked and links to its matchups', () => {
  const { click, document } = setup(chain(25));

  click('Deck 6');

  assert.deepEqual(Array.from(document.querySelectorAll('.network-focused text')).map(t => t.textContent), ['Deck 6']);
  assert.equal(document.querySelector('.network-matchups').getAttribute('href'), '/DeckMatchups?id=6');
  assert.match(document.querySelector('.network-focus').textContent, /Deck 6/);
});

test('a deck named by data-focus-deck is focused on load', () => {
  const { labels, edges, filterHidden } = setup(chain(25), 6);

  assert.deepEqual(labels().sort(), ['Deck 5', 'Deck 6', 'Deck 7']);
  assert.equal(edges().length, 2);
  assert.equal(filterHidden(), true);
});

test('a focus deck that is not in the network is ignored', () => {
  const { nodes, filterHidden } = setup(chain(25), 999);

  assert.equal(nodes().length, 20);
  assert.equal(filterHidden(), false);
});

test('clicking a node in the focused view moves the focus to it', () => {
  const { click, labels } = setup(chain(25));

  click('Deck 6');
  click('Deck 7');

  assert.deepEqual(labels().sort(), ['Deck 6', 'Deck 7', 'Deck 8']);
});

test('show all decks restores the full graph and the slider', () => {
  const { click, labels, nodes, filterHidden, document } = setup(chain(25));

  click('Deck 6');
  document.querySelector('.network-reset').click();

  assert.equal(nodes().length, 20);
  assert.ok(!labels().includes('Deck 5'));
  assert.equal(filterHidden(), false);
  assert.equal(document.querySelector('.network-reset'), null);
});

// Hub played Opp n exactly n times, so Opp 9 down to Opp 5 are its five most played opponents.
function hub() {
  const opponents = Array.from({ length: 8 }, (_, i) => ({ deckId: i + 2, label: `Opp ${i + 2}`, games: i + 2 }));
  return {
    nodes: [{ deckId: 1, label: 'Hub', games: 40 }, ...opponents],
    edges: opponents.map(o => ({ source: 1, target: o.deckId, games: o.deckId })),
  };
}

test('every deck name is shown when nothing is focused', () => {
  const { hiddenLabels } = setup(hub());

  assert.deepEqual(hiddenLabels(), []);
});

test('the focused view names only the focused deck and its five most played opponents', () => {
  const { click, shownLabels, hiddenLabels, labels } = setup(hub());

  click('Hub');

  assert.deepEqual(shownLabels().sort(), ['Hub', 'Opp 5', 'Opp 6', 'Opp 7', 'Opp 8', 'Opp 9']);
  assert.deepEqual(hiddenLabels(), ['Opp 2', 'Opp 3', 'Opp 4']);
  assert.equal(labels().length, 9);
});

test('the focused view names every opponent when there are five or fewer', () => {
  const { click, hiddenLabels } = setup(hub());

  click('Hub');
  click('Opp 2');

  assert.deepEqual(hiddenLabels(), []);
});

test('leaving the focused view names every deck again', () => {
  const { click, hiddenLabels, document } = setup(hub());

  click('Hub');
  document.querySelector('.network-reset').click();

  assert.deepEqual(hiddenLabels(), []);
});

test('every deck label has a lozenge behind it that reaches left past its node', () => {
  const { nodes } = setup();

  nodes().forEach(node => {
    const rect = node.querySelector('rect.network-label-bg');
    const circle = node.querySelector('circle');
    const text = node.querySelector('text');
    const cx = parseFloat(circle.getAttribute('cx'));
    const cy = parseFloat(circle.getAttribute('cy'));
    const x = parseFloat(rect.getAttribute('x'));
    const width = parseFloat(rect.getAttribute('width'));
    const height = parseFloat(rect.getAttribute('height'));

    assert.ok(x < cx - parseFloat(circle.getAttribute('r')), 'extends left of the node circle');
    assert.equal(parseFloat(rect.getAttribute('y')) + height / 2, cy, 'vertically centred on the node');
    assert.equal(parseFloat(rect.getAttribute('rx')), height / 2, 'fully rounded ends');
    assert.ok(x + width > parseFloat(text.getAttribute('x')), 'extends past the start of the label');
  });
});

test('the node circle is drawn above the lozenge and the label', () => {
  const { nodes } = setup();

  nodes().forEach(node => {
    const order = Array.from(node.children).map(child => child.tagName.toLowerCase());
    assert.ok(order.indexOf('rect') < order.indexOf('circle'));
    assert.ok(order.indexOf('rect') < order.indexOf('text'));
  });
});

test('a hidden label hides its lozenge too', () => {
  const { click, document } = setup(hub());

  click('Hub');

  const hiddenNodes = Array.from(document.querySelectorAll('.network-node'))
    .filter(node => node.querySelector('text').classList.contains('network-label-hidden'));
  assert.equal(hiddenNodes.length, 3);
  hiddenNodes.forEach(node => assert.ok(node.querySelector('rect').classList.contains('network-label-hidden')));
});

test('named decks are drawn in front of decks whose names are hidden, with the focused deck on top', () => {
  const { click, nodes } = setup(hub());

  click('Hub');

  const drawn = nodes().map(n => n.querySelector('text'));
  assert.deepEqual(drawn.map(t => t.classList.contains('network-label-hidden')),
    [true, true, true, false, false, false, false, false, false]);
  assert.equal(drawn[drawn.length - 1].textContent, 'Hub');
});

test('hovering a deck brings it in front of everything else', () => {
  const { click, nodes, document } = setup(hub());
  click('Hub');
  const hidden = nodes().find(n => n.querySelector('text').textContent === 'Opp 2');

  hidden.dispatchEvent(new document.defaultView.MouseEvent('mouseenter'));

  assert.equal(nodes()[nodes().length - 1], hidden);
  assert.equal(nodes().length, 9);
});

test('shows a message when there are no games between decks', () => {
  const { document, nodes } = setup({ nodes: [], edges: [] });

  assert.equal(nodes().length, 0);
  assert.match(document.getElementById('deck-network').textContent, /No games/);
});
