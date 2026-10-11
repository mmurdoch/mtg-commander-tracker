const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(
  path.join(__dirname, '..', '..', 'src', 'wwwroot', 'js', 'table-sort.js'),
  'utf8');

// Name, win % value, display text; '' means no data.
const rows = [
  ['Alice', '66.6667', '67% (2/3)'],
  ['Bob', '', '-'],
  ['Carol', '100', '100% (1/1)'],
  ['Dave', '0', '0% (0/1)'],
];

function setup() {
  const body = rows
    .map(([name, value, text]) => `<tr><td>${name}</td><td data-sort-value="${value}">${text}</td></tr>`)
    .join('');
  const dom = new JSDOM(
    `<table data-sortable>
       <thead><tr>
         <th aria-sort="none"><button type="button" data-sort-type="text">Name</button></th>
         <th aria-sort="none"><button type="button" data-sort-type="number">Win %</button></th>
       </tr></thead>
       <tbody>${body}</tbody>
     </table>`,
    { runScripts: 'outside-only' });
  dom.window.eval(script);
  const { document } = dom.window;
  const [nameButton, winButton] = document.querySelectorAll('th button');
  return {
    nameButton,
    winButton,
    nameHeader: nameButton.parentElement,
    winHeader: winButton.parentElement,
    names: () => Array.from(document.querySelectorAll('tbody tr')).map(r => r.cells[0].textContent),
  };
}

test('number column sorts ascending on first click with empty values last', () => {
  const t = setup();
  t.winButton.click();
  assert.deepEqual(t.names(), ['Dave', 'Alice', 'Carol', 'Bob']);
  assert.equal(t.winHeader.getAttribute('aria-sort'), 'ascending');
});

test('second click sorts descending and keeps empty values last', () => {
  const t = setup();
  t.winButton.click();
  t.winButton.click();
  assert.deepEqual(t.names(), ['Carol', 'Alice', 'Dave', 'Bob']);
  assert.equal(t.winHeader.getAttribute('aria-sort'), 'descending');
});

test('third click toggles back to ascending', () => {
  const t = setup();
  t.winButton.click();
  t.winButton.click();
  t.winButton.click();
  assert.deepEqual(t.names(), ['Dave', 'Alice', 'Carol', 'Bob']);
});

test('text column sorts alphabetically', () => {
  const t = setup();
  t.nameButton.click();
  t.nameButton.click();
  assert.deepEqual(t.names(), ['Dave', 'Carol', 'Bob', 'Alice']);
  t.nameButton.click();
  assert.deepEqual(t.names(), ['Alice', 'Bob', 'Carol', 'Dave']);
});

test('sorting another column resets the previous header', () => {
  const t = setup();
  t.nameButton.click();
  t.winButton.click();
  assert.equal(t.nameHeader.getAttribute('aria-sort'), 'none');
  assert.equal(t.winHeader.getAttribute('aria-sort'), 'ascending');
});
