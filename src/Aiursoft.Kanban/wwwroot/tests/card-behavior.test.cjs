const { test, after } = require('node:test');
const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const { mkdtempSync, readFileSync, rmSync } = require('node:fs');
const { tmpdir } = require('node:os');
const path = require('node:path');

// Compile the real pure modules; no browser or additional test dependency required.
const output = mkdtempSync(path.join(tmpdir(), 'kanban-card-tests-'));
after(() => rmSync(output, { recursive: true }));
execFileSync(process.execPath, [require.resolve('typescript/bin/tsc'),
  'src/kanban-board/card-dates.ts', 'src/kanban-board/filter-state.ts', 'src/gantt-chart/types.ts',
  '--outDir', output, '--rootDir', 'src', '--module', 'commonjs', '--moduleResolution', 'node',
  '--target', 'ES2020', '--strict', '--skipLibCheck',
], { cwd: path.resolve(__dirname, '..'), stdio: 'pipe' });
const { resolveDefaultCardDates } = require(path.join(output, 'kanban-board/card-dates.js'));
const { matchesCardFilters, filterBoardData } = require(path.join(output, 'kanban-board/filter-state.js'));
const { resolveItemDates, parseDate } = require(path.join(output, 'gantt-chart/types.js'));
const cardDetailSource = readFileSync(path.resolve(__dirname, '../src/card-detail-page/index.ts'), 'utf8');
const cardDetailView = readFileSync(path.resolve(__dirname, '../../Views/Cards/Detail.cshtml'), 'utf8');
const planned = { plannedStartDate: '2026-08-01', dueDate: '2026-08-10' };
const actual = { actualStartDate: '2026-08-03T09:00:00Z', actualEndDate: '2026-08-09T12:00:00Z' };

test('all schedule fields auto-save without an actual-times save button', () => {
  assert.doesNotMatch(cardDetailView, /id="saveActualTimes"/);
  assert.match(cardDetailView, /Changes save automatically\./);
  assert.match(cardDetailSource, /input\.addEventListener\('change'/);
  assert.match(cardDetailSource, /saveActualTimes\(\)\.catch/);
  assert.ok(cardDetailSource.includes(
    'updatePriority(parseInt(badge.dataset.priority, 10)).then(() => showSavedToast())'));
  assert.match(cardDetailSource, /plannedStartInput\?\.addEventListener\('change',[\s\S]{0,200}saveCardDetails\(\)\.then\(\(\) => showSavedToast\(\)\)/);
});

for (const [name, card, source, start, end] of [
  ['complete actual dates take priority', { ...planned, ...actual }, 'actual', actual.actualStartDate, actual.actualEndDate],
  ['incomplete actual dates fall back to complete planned dates', { ...planned, actualStartDate: actual.actualStartDate }, 'planned', planned.plannedStartDate, planned.dueDate],
  ['planned dates work without actual dates', planned, 'planned', planned.plannedStartDate, planned.dueDate],
]) {
  test(name, () => {
    assert.deepEqual(resolveDefaultCardDates(card), { source, start, end });
    assert.deepEqual(resolveItemDates(card, 'default'), [parseDate(start), parseDate(end)]);
  });
}

test('incomplete pairs never combine actual start with planned end', () => {
  const card = { actualStartDate: actual.actualStartDate, dueDate: planned.dueDate };
  assert.deepEqual(resolveDefaultCardDates(card), { source: 'actual', start: actual.actualStartDate, end: undefined });
  assert.equal(resolveItemDates(card, 'default'), null);
  assert.deepEqual(resolveDefaultCardDates({ dueDate: planned.dueDate }), { source: 'planned', start: undefined, end: planned.dueDate });
});

test('missing or invalid dates remain missing and do not produce fake ranges', () => {
  assert.deepEqual(resolveDefaultCardDates({}), { source: 'planned', start: undefined, end: undefined });
  assert.equal(resolveItemDates({ actualStartDate: 'invalid', actualEndDate: actual.actualEndDate }, 'default'), null);
  assert.equal(resolveDefaultCardDates({ ...planned, actualStartDate: 'invalid', actualEndDate: actual.actualEndDate }).source, 'planned');
});

test('date-only plans keep the same calendar day in western and eastern timezones', () => {
  const previous = process.env.TZ;
  try {
    for (const zone of ['America/Los_Angeles', 'Asia/Shanghai']) {
      process.env.TZ = zone;
      const date = parseDate('2026-08-01');
      assert.deepEqual([date.getFullYear(), date.getMonth(), date.getDate()], [2026, 7, 1]);
      assert.equal(parseDate('2026-08-01T09:00:00').toISOString(), '2026-08-01T09:00:00.000Z');
    }
  } finally {
    if (previous === undefined) delete process.env.TZ; else process.env.TZ = previous;
  }
});

const first = { id: 1, title: 'Client REVIEW', description: 'Contract milestone', priority: 'High', assignee: { userId: 'alice', displayName: 'Alice' }, ...planned };
const second = { id: 2, title: 'Draft', description: 'Client review preparation', priority: 'Low', assignee: { userId: 'bob', displayName: 'Bob' } };
const empty = { searchText: '', priorities: [], assigneeIds: [] };

test('search matches title or description, ignoring case and surrounding whitespace', () => {
  for (const card of [first, second]) assert.equal(matchesCardFilters(card, { ...empty, searchText: '  CLIENT review  ' }), true);
  assert.equal(matchesCardFilters(first, { ...empty, searchText: 'missing' }), false);
});

test('filter dimensions intersect and choices within a dimension are alternatives', () => {
  const state = { searchText: 'review', priorities: ['High', 'Medium'], assigneeIds: ['alice', 'bob'] };
  assert.equal(matchesCardFilters(first, state), true);
  assert.equal(matchesCardFilters(second, state), false);
  assert.equal(matchesCardFilters(first, { ...state, assigneeIds: ['bob'] }), false);
  assert.equal(matchesCardFilters({ ...first, assignee: undefined }, state), false);
});

test('Gantt filtering also includes undated matches, is reversible, and leaves source data untouched', () => {
  const data = { id: 1, columns: [{ id: 1, cards: [first, second] }] };
  assert.deepEqual(filterBoardData(data, { ...empty, assigneeIds: ['bob'] }).columns[0].cards, [second]);
  assert.deepEqual(filterBoardData(data, { ...empty, searchText: 'no match' }).columns[0].cards, []);
  assert.deepEqual(filterBoardData(data, empty).columns[0].cards, [first, second]);
  assert.equal(data.columns[0].cards.length, 2);
});
