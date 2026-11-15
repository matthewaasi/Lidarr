const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const babel = require('@babel/core');
const React = require('react');

function load(file, imports) {
  const filename = path.resolve(__dirname, '../src', file);
  const { code } = babel.transformSync(fs.readFileSync(filename, 'utf8'), {
    filename, babelrc: false, configFile: false,
    presets: [['@babel/preset-env', { targets: { node: 'current' }, modules: 'commonjs' }], '@babel/preset-react']
  });
  const module = { exports: {} };
  vm.runInNewContext(code, { module, exports: module.exports, require: (id) => Object.hasOwn(imports, id) ? imports[id] : require(id), console }, { filename });
  return module.exports.default;
}

function harness() {
  let options;
  let done;
  let fail;
  let aborted = false;
  let received = '';
  const updates = [];
  const errors = [];
  const createSearch = load('Utilities/createProgressiveReleaseSearch.js', {
    'Utilities/createAjaxRequest': (settings) => {
      options = settings;
      return { request: { done: (fn) => { done = fn; }, fail: (fn) => { fail = fn; } }, abortRequest: () => { aborted = true; fail({ aborted: true }); } };
    }
  });
  const cancel = createSearch({ albumId: 1 }, (result) => updates.push(result), (error) => errors.push(error));
  return {
    updates, errors, cancel,
    get aborted() { return aborted; },
    get options() { return options; },
    append(text) {
      received += text;
      options.xhrFields.onprogress({ target: { status: 200, responseText: received } });
    },
    frame(message) { this.append(JSON.stringify(message) + '\n'); },
    finish() { done(received); },
    fail(error) { fail(error); }
  };
}

const batch = (records, processed = records.length, total = 15000) => ({ type: 'batch', processed, total, records });

test('stream parses fragmented frames and displays checked batches before completion', () => {
  const search = harness();
  assert.equal(search.options.url, '/release/stream');
  const line = JSON.stringify(batch([{ guid: 'a', title: 'Beyoncé' }], 100)) + '\n';
  search.append(line.slice(0, 15));
  assert.equal(search.updates.length, 0);
  search.append(line.slice(15));
  assert.equal(search.updates[0].items[0].title, 'Beyoncé');
  assert.equal(search.updates[0].phase, 'processing');
  search.frame({ type: 'complete', records: [{ guid: 'a', title: 'Beyoncé' }] });
  search.finish();
  assert.equal(search.updates.at(-1).phase, 'complete');
  assert.equal(search.errors.length, 0);
});

test('15000 records are retained and final server ranking replaces provisional ordering', () => {
  const search = harness();
  const records = Array.from({ length: 15000 }, (_, id) => ({ guid: String(id), indexerId: 1, rejections: id % 3 ? [] : ['Rejected'] }));
  for (let start = 0; start < records.length; start += 100) {
    search.frame(batch(records.slice(start, start + 100), start + 100));
  }
  assert.equal(search.updates.at(-1).items.length, 15000);
  search.frame({ type: 'complete', records: records.slice().reverse() });
  assert.equal(search.updates.at(-1).items[0].guid, '14999');
  assert.equal(search.updates.at(-1).items.at(-1).guid, '0');
  assert.equal(search.updates.at(-1).items.length, 15000);
});

test('duplicate winner replacement never creates another displayed row', () => {
  const search = harness();
  search.frame(batch([{ guid: 'same', indexerId: 1, rejections: ['Rejected'] }]));
  search.frame(batch([{ guid: 'same', indexerId: 2, rejections: [] }], 2));
  assert.equal(search.updates.at(-1).items.length, 1);
  assert.equal(search.updates.at(-1).items[0].indexerId, 2);
});

test('closing aborts the request and ignores late progress, completion and errors', () => {
  const search = harness();
  search.frame(batch([{ guid: 'a' }]));
  search.cancel();
  search.frame({ type: 'complete', records: [{ guid: 'late' }] });
  search.finish();
  search.fail({ responseJSON: { message: 'late error' } });
  assert.equal(search.aborted, true);
  assert.equal(search.updates.length, 1);
  assert.equal(search.errors.length, 0);
});

test('truncated or malformed streams fail without marking partial results complete', () => {
  const search = harness();
  search.frame(batch([{ guid: 'a' }]));
  search.append('{"type":"complete"');
  search.finish();
  assert.equal(search.errors.length, 1);
  assert.match(search.errors[0].responseJSON.message, /before completion/);
  assert.equal(search.updates.at(-1).phase, 'processing');
  assert.equal(search.aborted, true);
  const malformed = harness();
  malformed.append('not json\n');
  assert.equal(malformed.errors.length, 1);
});

test('server errors retain partial results and stop the request', () => {
  const search = harness();
  search.frame(batch([{ guid: 'a' }]));
  search.frame({ type: 'error', message: 'Search did not finish' });
  assert.equal(search.errors[0].responseJSON.message, 'Search did not finish');
  assert.equal(search.updates.at(-1).items[0].guid, 'a');
  assert.equal(search.aborted, true);
});

const componentImports = {
  'Components/Alert': 'alert', 'Components/Icon': 'icon', 'Components/Loading/LoadingIndicator': 'loading',
  'Components/Menu/FilterMenu': 'filter', 'Components/Menu/PageMenuButton': 'button', 'Components/Table/Table': 'table',
  'Components/Table/TableBody': 'tbody', 'Helpers/Props': { align: {}, icons: {}, kinds: {}, sortDirections: {} },
  'Utilities/String/translate': (key) => key, './InteractiveSearchFilterModalConnector': 'filtermodal', './InteractiveSearchRow': 'release-row', './InteractiveSearch.css': {}
};
const Search = load('InteractiveSearch/InteractiveSearch.js', componentImports);
function nodes(element, predicate) {
  if (!element || typeof element !== 'object') return [];
  const result = predicate(element) ? [element] : [];
  React.Children.forEach(element.props?.children, (child) => result.push(...nodes(child, predicate)));
  return result;
}
function text(element) {
  if (typeof element === 'string' || typeof element === 'number') return String(element);
  return React.Children.toArray(element?.props?.children).map(text).join('');
}
function component() {
  const instance = new Search({ items: Array.from({ length: 15000 }, (_, id) => ({ guid: String(id), indexerId: 1 })), isPopulated: true, isFetching: true, totalReleasesCount: 15000, searchProgress: { phase: 'processing', processed: 500, total: 15000 } });
  instance.setState = (update) => Object.assign(instance.state, update);
  return instance;
}

test('large result lists render 100 rows while preserving access to every result', () => {
  const instance = component();
  let rows = nodes(instance.render(), (node) => node.type === 'release-row');
  assert.equal(rows.length, 100);
  assert.equal(rows[0].props.guid, '0');
  assert.match(text(instance.render()), /Checked 500 of 15000/);
  instance.setState({ page: 150 });
  rows = nodes(instance.render(), (node) => node.type === 'release-row');
  assert.equal(rows.length, 100);
  assert.equal(rows[0].props.guid, '14900');
  assert.equal(rows.at(-1).props.guid, '14999');
  assert.match(text(instance.render()), /Page 150 of 150/);
});

test('filter or sort changes reset pagination and partial errors remain visible', () => {
  const instance = component();
  instance.setState({ page: 100 });
  instance.componentDidUpdate({ ...instance.props, sortKey: 'old' });
  assert.equal(instance.state.page, 1);
  instance.props = { ...instance.props, isFetching: false, error: { responseJSON: { message: 'Partial results are incomplete' } } };
  assert.match(text(instance.render()), /Partial results are incomplete/);
  assert.equal(nodes(instance.render(), (node) => node.type === 'release-row').length, 100);
});
