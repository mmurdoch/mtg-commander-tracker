const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(
  path.join(__dirname, '..', '..', 'src', 'wwwroot', 'js', 'deck-plays-chart.js'),
  'utf8');

function setup() {
  const dom = new JSDOM(
    `<div data-chart="deck-plays">
       <svg>
         <g class="line-chart-series" data-deck="1"><path class="line-chart-hit"></path><circle></circle></g>
         <g class="line-chart-series" data-deck="2"><path class="line-chart-hit"></path></g>
       </svg>
       <ul><li data-deck="1"><a href="#">Atraxa</a></li><li data-deck="2"><a href="#">Breya</a></li></ul>
     </div>`,
    { runScripts: 'outside-only' });
  dom.window.eval(script);
  const { document, MouseEvent, FocusEvent } = dom.window;
  const q = selector => document.querySelector(selector);
  return {
    hover: element => element.dispatchEvent(new MouseEvent('mouseover', { bubbles: true })),
    leave: () => q('[data-chart="deck-plays"]').dispatchEvent(new MouseEvent('mouseleave')),
    focus: element => element.dispatchEvent(new FocusEvent('focusin', { bubbles: true })),
    blur: element => element.dispatchEvent(new FocusEvent('focusout', { bubbles: true })),
    q,
    order: () => Array.from(document.querySelectorAll('g[data-deck]')).map(el => el.getAttribute('data-deck')),
    dimmed: () => Array.from(document.querySelectorAll('[data-deck].line-chart-dimmed'))
      .map(el => `${el.tagName.toLowerCase()}:${el.getAttribute('data-deck')}`),
  };
}

test('hovering a line dims the other lines and legend entries', () => {
  const page = setup();

  page.hover(page.q('g[data-deck="1"] path'));

  assert.deepEqual(page.dimmed(), ['g:2', 'li:2']);
});

test('hovering a legend entry highlights its line and dims everything else', () => {
  const page = setup();

  page.hover(page.q('li[data-deck="2"] a'));

  assert.deepEqual(page.dimmed(), ['g:1', 'li:1']);
});

test('moving to another deck switches the highlight', () => {
  const page = setup();

  page.hover(page.q('g[data-deck="1"] circle'));
  page.hover(page.q('li[data-deck="2"]'));

  assert.deepEqual(page.dimmed(), ['g:1', 'li:1']);
});

test('hovering the chart outside any deck clears the highlight', () => {
  const page = setup();
  page.hover(page.q('g[data-deck="1"] path'));

  page.hover(page.q('svg'));

  assert.deepEqual(page.dimmed(), []);
});

test('leaving the chart clears the highlight', () => {
  const page = setup();
  page.hover(page.q('g[data-deck="1"] path'));

  page.leave();

  assert.deepEqual(page.dimmed(), []);
});

test('the highlighted line is drawn on top and the original order returns afterwards', () => {
  const page = setup();
  assert.deepEqual(page.order(), ['1', '2']);

  page.hover(page.q('g[data-deck="1"] path'));
  assert.deepEqual(page.order(), ['2', '1']);

  page.leave();
  assert.deepEqual(page.order(), ['1', '2']);
});

test('keyboard focus on a legend link highlights its deck until blur', () => {
  const page = setup();
  const link = page.q('li[data-deck="1"] a');

  page.focus(link);
  assert.deepEqual(page.dimmed(), ['g:2', 'li:2']);

  page.blur(link);
  assert.deepEqual(page.dimmed(), []);
});
