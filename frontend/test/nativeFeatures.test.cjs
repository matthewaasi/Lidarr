const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const babel = require('@babel/core');
const React = require('react');

function load(file, api) {
  const filename = path.resolve(__dirname, '../src', file);
  const { code } = babel.transformSync(fs.readFileSync(filename, 'utf8'), {
    filename, babelrc: false, configFile: false,
    presets: [['@babel/preset-env', { targets: { node: 'current' }, modules: 'commonjs' }], '@babel/preset-react']
  });
  const module = { exports: {} };
  const imports = {
    'Components/Alert': 'alert', 'Components/Link/Link': 'link',
    'Components/Loading/LoadingIndicator': 'loading', 'Components/Page/PageContent': 'page',
    'Components/Page/PageContentBody': 'body', 'Helpers/Props': { kinds: { DANGER: 'danger', WARNING: 'warning', SUCCESS: 'success' } },
    'Settings/FeatureSettings': { featureError: (xhr) => xhr.responseJSON?.message || 'Request failed' },
    'Utilities/createAjaxRequest': api,
    './FeatureSettings.css': {}, './SinglesReview.css': {}
  };
  vm.runInNewContext(code, { module, exports: module.exports, require: (id) => Object.hasOwn(imports, id) ? imports[id] : require(id), setTimeout, clearTimeout, console }, { filename });
  return module.exports.default;
}

function harness(file, props = {}) {
  const calls = [];
  const api = (options) => {
    let resolve;
    let reject;
    const request = new Promise((yes, no) => { resolve = yes; reject = no; });
    const call = { options, resolve, reject, aborted: false };
    calls.push(call);
    return { request, abortRequest: () => { call.aborted = true; } };
  };
  const Component = load(file, api);
  const instance = new Component(props);
  instance.setState = (update, callback) => {
    Object.assign(instance.state, typeof update === 'function' ? update(instance.state) : update);
    if (callback) callback();
  };
  return { instance, calls };
}

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

const settle = () => new Promise((resolve) => setImmediate(resolve));
const row = { id: 42, status: 'pending', autoUnmonitored: false, updated: '2026-11-09T12:00:00Z', evidence: {
  artist: 'Artist', single: 'Single', reason: 'Duration is uncertain', fingerprint: 'snapshot', canCleanup: true, singleFileIds: [11], tracks: [
    { trackId: 1, title: 'Song', durationMs: 180000, recordingId: 'recording', sourceTitle: 'Song', sourceAlbum: 'Album', sourceDurationMs: 182000, sourceRecordingId: 'other', reason: 'Title only' }
  ]
} };

test('identification settings save selected engine and distinct native thresholds', async () => {
  const { instance, calls } = harness('Settings/FeatureSettings.js', { feature: 'identification' });
  instance.componentDidMount();
  assert.equal(calls[0].options.url, '/config/identification');
  calls[0].resolve({ matchingEngine: 'LmStudio', originalAlbumMatchPercent: 80, originalTrackMatchPercent: 60 });
  await settle();
  instance.onChange({ target: { name: 'matchingEngine', value: 'Original', type: 'select-one' } });
  instance.onChange({ target: { name: 'originalAlbumMatchPercent', value: '85', type: 'number' } });
  instance.onSave({ preventDefault() {} });
  const body = JSON.parse(calls[1].options.data);
  assert.equal(body.matchingEngine, 'Original');
  assert.equal(body.originalAlbumMatchPercent, 85);
  assert.equal(body.originalTrackMatchPercent, 60);
  instance.onSave({ preventDefault() {} });
  assert.equal(calls.length, 2, 'duplicate save is guarded');
  calls[1].resolve({});
  await settle();
  assert.equal(instance.state.saved, true);
});

test('Unique Singles has an independent enable toggle and a retryable save error', async () => {
  const { instance, calls } = harness('Settings/FeatureSettings.js', { feature: 'uniquesingles' });
  instance.componentDidMount();
  calls[0].resolve({ uniqueSinglesEnabled: false, uniqueSinglesTitleDuration: true, uniqueSinglesToleranceMs: 3000 });
  await settle();
  instance.onChange({ target: { name: 'uniqueSinglesEnabled', type: 'checkbox', checked: true } });
  instance.onSave({ preventDefault() {} });
  assert.equal(calls[1].options.url, '/config/uniquesingles/1');
  assert.equal(JSON.parse(calls[1].options.data).uniqueSinglesEnabled, true);
  assert.equal(Object.hasOwn(JSON.parse(calls[1].options.data), 'matchingEngine'), false);
  calls[1].reject({ responseJSON: { message: 'Invalid duration' } });
  await settle();
  assert.equal(instance.state.busy, false);
  assert.equal(instance.state.error, 'Invalid duration');
});

test('Singles Review displays both tracks and requires an explicit confirmation before cleanup', () => {
  const { instance, calls } = harness('Activity/Singles/SinglesReview.js');
  Object.assign(instance.state, { enabled: true, loading: false, records: [row], total: 1 });
  let tree = instance.render();
  assert.match(text(tree), /Artist.*Single/);
  assert.match(text(tree), /Album/);
  const approve = nodes(tree, (n) => n.type === 'button' && text(n) === 'Approve cleanup…')[0];
  approve.props.onClick();
  assert.equal(calls.length, 0);
  tree = instance.render();
  const confirm = nodes(tree, (n) => n.type === 'button' && text(n) === 'Confirm cleanup')[0];
  confirm.props.onClick();
  assert.equal(calls.length, 1);
  assert.equal(calls[0].options.url, '/singlesreview/42');
  assert.deepEqual(JSON.parse(calls[0].options.data), { action: 'approve', fingerprint: 'snapshot' });
});

test('disabled feature and incomplete evidence cannot expose an enabled approval', () => {
  const { instance } = harness('Activity/Singles/SinglesReview.js');
  Object.assign(instance.state, { loading: false, records: [row], total: 1 });
  let approve = nodes(instance.render(), (n) => n.type === 'button' && text(n) === 'Approve cleanup…')[0];
  assert.equal(approve.props.disabled, true);
  instance.state.enabled = true;
  instance.state.records = [{ ...row, evidence: { ...row.evidence, canCleanup: false } }];
  approve = nodes(instance.render(), (n) => n.type === 'button' && text(n) === 'Approve cleanup…')[0];
  assert.equal(approve.props.disabled, true);
});

test('stale approval error reloads evidence without retrying deletion', async () => {
  const { instance, calls } = harness('Activity/Singles/SinglesReview.js');
  instance.onDecide(row, 'approve');
  calls[0].reject({ responseJSON: { message: 'Evidence changed' } });
  await settle();
  assert.equal(instance.state.error, 'Evidence changed');
  assert.equal(instance.state.busy, false);
  assert.equal(calls.length, 2);
  assert.match(calls[1].options.url, /^\/singlesreview\?/);
  assert.equal(calls[1].options.method, undefined);
});

test('scan progress follows the actual command and duplicate submissions are guarded', async () => {
  const { instance, calls } = harness('Activity/Singles/SinglesReview.js');
  instance.mounted = true;
  instance.state.enabled = true;
  instance.onScan();
  instance.onScan();
  assert.equal(calls.length, 1);
  assert.equal(JSON.parse(calls[0].options.data).name, 'ScanUniqueSingles');
  calls[0].resolve({ id: 7 });
  await settle();
  assert.equal(calls[1].options.url, '/command/7');
  calls[1].resolve({ status: 'completed' });
  await settle();
  assert.equal(instance.state.scanning, false);
  assert.match(calls[2].options.url, /^\/singlesreview\?/);
});

test('review pagination and filters are sent to the server and requests abort on navigation', () => {
  const { instance, calls } = harness('Activity/Singles/SinglesReview.js');
  instance.onPage(1);
  assert.match(calls[0].options.url, /page=2/);
  instance.onFilter({ target: { value: 'kept' } });
  assert.match(calls[1].options.url, /status=kept&page=1/);
  instance.componentWillUnmount();
  assert.equal(calls.every((call) => call.aborted), true);
});


test('hybrid matching can be selected and saved with required AI configuration', async () => {
  const { instance, calls } = harness('Settings/FeatureSettings.js', { feature: 'identification' });
  instance.componentDidMount();
  calls[0].resolve({ matchingEngine: 'Original', originalAlbumMatchPercent: 80, originalTrackMatchPercent: 60, lmStudioBaseUrl: 'http://localhost:1234/v1', lmStudioModel: 'model', lmStudioMode: 'Review', lmStudioConfidencePercent: 95 });
  await settle();
  instance.onChange({ target: { name: 'matchingEngine', value: 'Hybrid', type: 'select-one' } });
  const tree = instance.render();
  assert.equal(nodes(tree, (n) => n.type === 'option' && n.props.value === 'Hybrid').length, 1);
  assert.equal(nodes(tree, (n) => n.type === 'input' && n.props.name === 'lmStudioModel')[0].props.required, true);
  assert.match(text(tree), /80% album by default/);
  instance.onSave({ preventDefault() {} });
  const body = JSON.parse(calls[1].options.data);
  assert.equal(body.matchingEngine, 'Hybrid');
  assert.equal(body.originalAlbumMatchPercent, 80);
  assert.equal(body.lmStudioMode, 'Review');
  calls[1].resolve({});
  await settle();
});
