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
    './LibraryAudit.css': {}, './FeatureSettings.css': {}, './SinglesReview.css': {}
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


test('automatic and manual engines can be saved independently with required AI configuration', async () => {
  const { instance, calls } = harness('Settings/FeatureSettings.js', { feature: 'identification' });
  instance.componentDidMount();
  calls[0].resolve({ matchingEngine: 'Original', originalAlbumMatchPercent: 80, originalTrackMatchPercent: 60, lmStudioBaseUrl: 'http://localhost:1234/v1', lmStudioModel: 'model', lmStudioMode: 'Review', lmStudioConfidencePercent: 95 });
  await settle();
  instance.onChange({ target: { name: 'matchingEngine', value: 'Hybrid', type: 'select-one' } });
  instance.onChange({ target: { name: 'manualMatchingEngine', value: 'Original', type: 'select-one' } });
  const tree = instance.render();
  assert.equal(nodes(tree, (n) => n.type === 'select' && n.props.name === 'manualMatchingEngine')[0].props.value, 'Original');
  assert.equal(nodes(tree, (n) => n.type === 'input' && n.props.name === 'lmStudioModel')[0].props.required, true);
  assert.match(text(tree), /80% album by default/);
  instance.onSave({ preventDefault() {} });
  const body = JSON.parse(calls[1].options.data);
  assert.equal(body.matchingEngine, 'Hybrid');
  assert.equal(body.manualMatchingEngine, 'Original');
  assert.equal(body.originalAlbumMatchPercent, 80);
  assert.equal(body.lmStudioMode, 'Review');
  calls[1].resolve({});
  await settle();
});


test('audit deletes only findings, with explicit all and selected scopes', async () => {
  const { instance, calls } = harness('Library/Audit/LibraryAudit.js');
  instance.mounted = true;
  instance.onDelete();
  assert.equal(calls.length, 0, 'confirmation is required');
  instance.onSelect(12, true);
  instance.onSelect(15, true);
  instance.setState({ deleteTarget: 'selected' });
  instance.onDelete();
  assert.equal(calls[0].options.url, '/libraryaudit/bulk');
  assert.equal(calls[0].options.method, 'DELETE');
  assert.deepEqual(JSON.parse(calls[0].options.data), [12, 15]);
  instance.onDelete();
  assert.equal(calls.length, 1, 'duplicate deletion is guarded');
  calls[0].reject({ responseJSON: { message: 'Try again' } });
  await settle();
  assert.equal(instance.state.error, 'Try again');
  assert.equal(instance.state.selectedIds.length, 2, 'failed delete keeps selection');
  instance.setState({ deleteTarget: 'all', status: 'ignored', kind: 'version', page: 3 });
  instance.onDelete();
  assert.equal(calls[1].options.url, '/libraryaudit/all', 'all ignores filters and pagination');
  assert.equal(calls[1].options.data, undefined);
  calls[1].resolve({});
  await settle();
  assert.equal(instance.state.selectedIds.length, 0);
  assert.equal(instance.state.deleteTarget, null);
  assert.match(calls[2].options.url, /^\/libraryaudit\?/);
});

test('audit individual delete and scan guard', () => {
  const { instance, calls } = harness('Library/Audit/LibraryAudit.js');
  instance.setState({ deleteTarget: 42, scanning: true });
  instance.onDelete();
  assert.equal(calls.length, 0);
  instance.setState({ scanning: false });
  instance.onDelete();
  assert.equal(calls[0].options.url, '/libraryaudit/42');
  assert.equal(calls[0].options.method, 'DELETE');
});


test('audit selection controls require confirmation and reset on navigation', () => {
  const { instance, calls } = harness('Library/Audit/LibraryAudit.js');
  instance.setState({ loading: false, records: [{ id: 7, evidence: { files: [], candidates: [] } }], total: 1 });
  const findButton = (label) => nodes(instance.render(), (node) => node.type === 'button' && text(node).startsWith(label))[0];
  assert.equal(findButton('Delete selected').props.disabled, true);
  nodes(instance.render(), (node) => node.props?.['aria-label'] === 'Select all findings on this page')[0].props.onChange({ target: { checked: true } });
  assert.equal(instance.state.selectedIds[0], 7);
  findButton('Delete selected').props.onClick();
  assert.equal(calls.length, 0);
  assert.match(text(instance.render()), /Your library, music files, tags and monitoring are unchanged/);
  findButton('Cancel').props.onClick();
  assert.equal(instance.state.deleteTarget, null);
  instance.onLoad();
  assert.equal(instance.state.selectedIds.length, 0);
});


test('audit file removal is opt in, selects exact copies, and resets with scope', () => {
  const { instance, calls } = harness('Library/Audit/LibraryAudit.js');
  const evidence = { files: [{ id: 11, path: '/music/keep.flac' }, { id: 12, path: '/music/remove.flac' }], candidates: [] };
  instance.setState({ records: [{ id: 7, evidenceKey: 'current', evidence }], loading: true });
  instance.onConfirmDelete(7);
  assert.equal(instance.state.deleteFileIds.length, 0);
  const fileCheckbox = nodes(instance.render(), (node) => node.type === 'input' && text(node).length === 0 && node.props.type === 'checkbox').at(-1);
  fileCheckbox.props.onChange({ target: { checked: true } });
  assert.equal(instance.state.deleteFileIds[0], 12);
  assert.match(text(instance.render()), /Also delete 1 selected music files/);
  instance.onDelete();
  assert.equal(calls[0].options.url, '/libraryaudit/deletefiles/preview');
  assert.equal(calls[0].options.method, 'POST');
  assert.deepEqual(JSON.parse(calls[0].options.data), { findings: [{ id: 7, evidenceKey: 'current' }], fileIds: [12] });
  instance.onConfirmDelete('all');
  assert.equal(instance.state.deleteFileIds.length, 0);
  assert.doesNotMatch(text(instance.render()), /Also delete file:/);
});

test('audit bulk file deletion sends only selected findings and chosen files', () => {
  const { instance, calls } = harness('Library/Audit/LibraryAudit.js');
  instance.setState({ selectedIds: [7, 8], records: [7, 8, 9].map((id) => ({ id, evidenceKey: `key${id}`, evidence: { files: [], candidates: [] } })) });
  instance.onConfirmDelete('selected');
  instance.setState({ deleteFileIds: [12, 13] });
  instance.onDelete();
  assert.deepEqual(JSON.parse(calls[0].options.data), { findings: [{ id: 7, evidenceKey: 'key7' }, { id: 8, evidenceKey: 'key8' }], fileIds: [12, 13] });
});


test('audit preview lists exact paths and sizes and requires separate all-copy confirmation', async () => {
  const { instance, calls } = harness('Library/Audit/LibraryAudit.js');
  instance.mounted = true;
  instance.setState({ loading: true, records: [{ id: 7, evidenceKey: 'current', evidence: { files: [{ id: 11 }, { id: 12 }], candidates: [] } }] });
  instance.onConfirmDelete(7);
  instance.onChangeDeleteFile(11, true);
  instance.onChangeDeleteFile(12, true);
  instance.onDelete();
  const files = [{ id: 11, path: '/music/a.flac', size: 1048576 }, { id: 12, path: '/music/b.flac', size: 1048576 }];
  const preview = { files, totalBytes: 2097152, previewKey: 'preview-key', duplicateGroups: [{ findingId: 99, files }] };
  calls[0].resolve(preview);
  await settle();
  assert.equal(calls.length, 1, 'preview never starts deletion');
  const output = text(instance.render());
  assert.match(output, /2 files selected/);
  assert.match(output, /2,097,152 bytes \(2.00 MiB\)/);
  assert.match(output, /every audited copy/);
  assert.match(output, /music\/a.flac/);
  assert.match(output, /music\/b.flac/);
  const confirm = nodes(instance.render(), (node) => node.type === 'button' && text(node) === 'Confirm delete findings and selected files')[0];
  assert.equal(confirm.props.disabled, true);
  instance.onDelete();
  assert.equal(calls.length, 1, 'all-copy guard also applies in the handler');
  const checkbox = nodes(instance.render(), (node) => node.type === 'input' && node.props.type === 'checkbox').at(-1);
  checkbox.props.onChange({ target: { checked: true } });
  instance.onDelete();
  assert.equal(calls[1].options.url, '/libraryaudit/deletefiles');
  assert.deepEqual(JSON.parse(calls[1].options.data), { findings: [{ id: 7, evidenceKey: 'current' }], fileIds: [11, 12], previewKey: 'preview-key', confirmAllCopies: true });
});

test('changing file selection invalidates the preview and all-copy acknowledgement', () => {
  const { instance } = harness('Library/Audit/LibraryAudit.js');
  instance.setState({ deleteFileIds: [11, 12], deletionPreview: { previewKey: 'old' }, confirmAllCopies: true });
  instance.onChangeDeleteFile(12, false);
  assert.equal(instance.state.deletionPreview, null);
  assert.equal(instance.state.confirmAllCopies, false);
  assert.deepEqual(Array.from(instance.state.deleteFileIds), [11]);
});

test('failed audit preview keeps selection and never enables deletion', async () => {
  const { instance, calls } = harness('Library/Audit/LibraryAudit.js');
  instance.mounted = true;
  instance.setState({ records: [{ id: 7, evidenceKey: 'current', evidence: { files: [], candidates: [] } }] });
  instance.onConfirmDelete(7);
  instance.onChangeDeleteFile(11, true);
  instance.onDelete();
  calls[0].reject({ responseJSON: { message: 'File changed' } });
  await settle();
  assert.equal(instance.state.deletionPreview, null);
  assert.equal(instance.state.error, 'File changed');
  assert.equal(instance.deleteButtonLabel(), 'Preview file deletion');
  assert.equal(instance.state.deleteFileIds[0], 11);
});


test('ordinary file deletion reviews preview first and resets it after server rejection', async () => {
  const { instance, calls } = harness('Library/Audit/LibraryAudit.js');
  instance.mounted = true;
  instance.setState({ records: [{ id: 7, evidenceKey: 'current', evidence: { files: [], candidates: [] } }] });
  instance.onConfirmDelete(7);
  instance.onChangeDeleteFile(11, true);
  instance.onDelete();
  calls[0].resolve({ files: [{ id: 11, path: '/music/remove.flac', size: 100 }], totalBytes: 100, previewKey: 'reviewed', duplicateGroups: [] });
  await settle();
  assert.equal(instance.deleteButtonLabel(), 'Confirm delete findings and selected files');
  instance.onDelete();
  assert.equal(calls[1].options.url, '/libraryaudit/deletefiles');
  assert.equal(JSON.parse(calls[1].options.data).previewKey, 'reviewed');
  assert.equal(JSON.parse(calls[1].options.data).confirmAllCopies, false);
  calls[1].reject({ responseJSON: { message: 'Preview changed' } });
  await settle();
  assert.equal(instance.state.deletionPreview, null);
  assert.equal(instance.state.confirmAllCopies, false);
  assert.equal(instance.deleteButtonLabel(), 'Preview file deletion');
});


test('audit phase percentage and completed file counts are visible and accessible', () => {
  const { instance } = harness('Library/Audit/LibraryAudit.js');
  instance.setState({ scanning: true, scanStatus: 'started', scanProgress: { phase: 'inspection', processed: 74366, total: 228816, percentage: 32.5, stage: 1, stages: 3, activity: 'Fingerprint lookup', updated: '2026-11-15T12:00:00Z' } });
  const output = text(instance.renderProgress());
  assert.match(output, /Inspecting files.*32.5%/);
  assert.match(output, /74,366 \/ 228,816 files inspected/);
  assert.match(output, /Stage 1 of 3/);
  assert.match(output, /Fingerprint lookup/);
  assert.equal(nodes(instance.renderProgress(), (node) => node.type === 'progress')[0].props.value, 32.5);
});

test('audit reopening attaches to a started scan ahead of queued scans', async () => {
  const { instance, calls } = harness('Library/Audit/LibraryAudit.js');
  instance.componentDidMount();
  calls[1].resolve([{ id: 8, name: 'ScanLibraryAudit', status: 'queued' }, { id: 9, name: 'ScanLibraryAudit', status: 'started', body: { progress: { phase: 'inspection', processed: 1, total: 2, percentage: 50, stage: 1, stages: 3 } } }]);
  await settle();
  assert.equal(calls[2].options.url, '/command/9');
  assert.equal(instance.state.scanProgress.percentage, 50);
  instance.componentWillUnmount();
});

test('audit saving is indeterminate and completion requires the command to finish', async () => {
  const { instance, calls } = harness('Library/Audit/LibraryAudit.js');
  instance.mounted = true;
  instance.setState({ scanning: true, scanStatus: 'started', scanProgress: { phase: 'saving', processed: 0, total: null, percentage: null, stage: 3, stages: 3 } });
  assert.equal(nodes(instance.renderProgress(), (node) => node.type === 'progress')[0].props.value, undefined);
  instance.setState({ scanProgress: { phase: 'completed', percentage: 100, stage: 3, stages: 3 } });
  assert.doesNotMatch(text(instance.renderProgress()), /Audit completed/);
  instance.poll(9);
  calls[0].resolve({ status: 'completed', body: { progress: { phase: 'completed', percentage: 100, stage: 3, stages: 3 } } });
  await settle();
  assert.equal(instance.state.scanning, false);
  assert.match(text(instance.renderProgress()), /Audit completed.*100.0%/);
  instance.componentWillUnmount();
});

test('cancelled audit retains phase progress without claiming completion', async () => {
  const { instance, calls } = harness('Library/Audit/LibraryAudit.js');
  instance.mounted = true;
  instance.setState({ scanning: true });
  instance.poll(9);
  calls[0].resolve({ status: 'cancelled', body: { progress: { phase: 'inspection', processed: 1, total: 2, percentage: 50, stage: 1, stages: 3 } } });
  await settle();
  assert.equal(instance.state.scanning, false);
  assert.match(text(instance.renderProgress()), /Cancelled/);
  assert.match(text(instance.renderProgress()), /50.0%/);
  assert.doesNotMatch(text(instance.renderProgress()), /Audit completed/);
  instance.componentWillUnmount();
});

test('failed progress polling keeps the running scan and labels the last received progress', async () => {
  const { instance, calls } = harness('Library/Audit/LibraryAudit.js');
  instance.mounted = true;
  instance.setState({ scanning: true, scanStatus: 'started', scanProgress: { phase: 'inspection', percentage: 50, processed: 1, total: 2 } });
  instance.poll(9);
  calls[0].reject({ status: 503 });
  await settle();
  assert.equal(instance.state.scanning, true);
  assert.match(text(instance.renderProgress()), /Last received progress/);
  assert.match(instance.state.progressError, /Retrying/);
  instance.poll(9);
  calls[1].resolve({ status: 'started', body: { progress: { phase: 'inspection', percentage: 75, processed: 3, total: 4 } } });
  await settle();
  assert.equal(instance.state.progressError, '');
  assert.equal(instance.state.scanProgress.percentage, 75);
  instance.componentWillUnmount();
});

test('new audit resets old progress and missing server counters stay indeterminate', () => {
  const { instance, calls } = harness('Library/Audit/LibraryAudit.js');
  instance.setState({ scanStatus: 'completed', scanProgress: { phase: 'completed', percentage: 100 } });
  instance.onScan();
  assert.equal(instance.state.scanProgress, null);
  assert.equal(instance.state.scanStatus, 'queued');
  assert.equal(nodes(instance.renderProgress(), (node) => node.type === 'progress')[0].props.value, undefined);
  assert.equal(calls[0].options.url, '/command');
});


test('failed audit submission does not leave a phantom queued progress indicator', async () => {
  const { instance, calls } = harness('Library/Audit/LibraryAudit.js');
  instance.mounted = true;
  instance.onScan();
  calls[0].reject({ responseJSON: { message: 'Cannot start scan' } });
  await settle();
  assert.equal(instance.state.scanning, false);
  assert.equal(instance.renderProgress(), null);
  assert.equal(instance.state.error, 'Cannot start scan');
  instance.componentWillUnmount();
});
