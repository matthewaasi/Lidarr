const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const babel = require('@babel/core');
const React = require('react');
function load(file, imports, extras = {}) {
  const filename = path.resolve(__dirname, '../src', file);
  const { code } = babel.transformSync(fs.readFileSync(filename, 'utf8'), { filename, babelrc: false, configFile: false,
    presets: [['@babel/preset-env', { targets: { node: 'current' }, modules: 'commonjs' }], '@babel/preset-react', '@babel/preset-typescript'] });
  const module = { exports: {} };
  vm.runInNewContext(code, { module, exports: module.exports, require: (id) => Object.hasOwn(imports, id) ? imports[id] : require(id), console, setTimeout, clearTimeout, ...extras }, { filename });
  return module.exports;
}
function nodes(element, predicate) {
  if (!element || typeof element !== 'object') return [];
  const result = predicate(element) ? [element] : [];
  React.Children.forEach(element.props?.children, (child) => result.push(...nodes(child, predicate)));
  return result;
}
function row(extra) {
  const actions = [];
  let closed = false;
  const hooks = { ...React, useCallback: (fn) => fn, useState: (value) => [value, () => {}], useRef: () => ({ current: null }), useEffect: () => {} };
  const Row = load('System/Tasks/Queued/QueuedTaskRow.tsx', {
    react: hooks, 'react-redux': { useDispatch: () => (action) => actions.push(action), useSelector: () => ({ longDateFormat: '', shortDateFormat: '', showRelativeDates: false, timeFormat: '' }) },
    'Components/Icon': 'icon', 'Components/Link/IconButton': 'icon-button', 'Components/Modal/ConfirmModal': 'confirm',
    'Components/Table/Cells/TableRowCell': 'td', 'Components/Table/TableRow': 'tr',
    'Helpers/Hooks/useModalOpenState': () => [false, () => {}, () => { closed = true; }],
    'Helpers/Props': { icons: { REMOVE: 'remove' }, kinds: {} },
    'Store/Actions/commandActions': { cancelCommand: (payload) => ({ type: 'cancel', payload }) },
    'Store/Selectors/createUISettingsSelector': () => () => ({}),
    'Utilities/Date/formatDate': () => '', 'Utilities/Date/formatDateTime': () => '', 'Utilities/Date/formatTimeSpan': () => '',
    'Utilities/String/titleCase': (text) => text, 'Utilities/String/translate': (key) => key,
    './QueuedTaskRowNameCell': 'name', './QueuedTaskRow.css': {}
  }).default;
  const tree = Row({ id: 12, trigger: 'manual', commandName: 'Album Search', queued: '2026-11-15T12:00:00Z', status: 'started', body: {}, ...extra });
  return { tree, actions, get closed() { return closed; }, cancel: nodes(tree, (node) => node.type === 'icon-button')[0] };
}

test('supported running task offers cancellation and confirmation preserves completed work', () => {
  const view = row({ canCancel: true });
  assert.equal(view.cancel.props.isDisabled, false);
  assert.equal(view.cancel.props.title, 'Cancel task');
  const modal = nodes(view.tree, (node) => node.type === 'confirm')[0];
  assert.match(modal.props.message, /Completed work and already queued downloads remain/);
  modal.props.onConfirm();
  assert.equal(view.actions[0].payload.id, 12);
  assert.equal(view.closed, true);
});

test('unsupported or finishing running task has a disabled cancel button', () => {
  const view = row({ canCancel: false });
  assert.equal(view.cancel.props.isDisabled, true);
  assert.match(view.cancel.props.title, /does not support cancellation or is finishing/);
});

test('pending cancellation remains visible and cannot be submitted again', () => {
  const view = row({ canCancel: false, cancellationRequested: true });
  assert.equal(view.cancel.props.isDisabled, true);
  assert.equal(view.cancel.props.title, 'Cancelling…');
  assert.equal(nodes(view.tree, (node) => node.type === 'name')[0].props.cancellationRequested, true);
});

test('queued cancellation remains available and finished tasks have no cancel action', () => {
  assert.equal(row({ status: 'queued' }).cancel.props.isDisabled, false);
  assert.equal(row({ status: 'cancelled' }).cancel, undefined);
  assert.equal(row({ status: 'completed' }).cancel, undefined);
});

test('cancel action updates the server-returned running row instead of removing it', async () => {
  const actions = [];
  const timers = [];
  const api = { request: { then: (done) => done({ id: 12, status: 'started', cancellationRequested: true }) } };
  const action = (type) => (payload) => ({ type, payload });
  const exports = load('Store/Actions/commandActions.js', {
    'redux-actions': { createAction: action }, 'redux-batched-actions': { batchActions: (list) => ({ type: 'batch', list }) },
    'Helpers/Props': { messageTypes: {} }, 'Store/thunks': { createThunk: (name) => name, handleThunks: (handlers) => handlers },
    'Utilities/Command': { isSameCommand: () => false }, 'Utilities/createAjaxRequest': (options) => { assert.equal(options.url, '/command/12'); assert.equal(options.method, 'DELETE'); return api; },
    './appActions': { hideMessage: action('hide'), showMessage: action('show') },
    './baseActions': { removeItem: action('remove'), updateItem: action('update'), set: action('set') },
    './Creators/createFetchHandler': () => () => {}, './Creators/createHandleActions': () => ({})
  }, { setTimeout: (fn) => { timers.push(fn); return timers.length; }, clearTimeout: () => {} });
  exports.actionHandlers[exports.CANCEL_COMMAND](() => ({}), { id: 12 }, (event) => actions.push(event));
  assert.equal(actions[0].type, 'update');
  assert.equal(actions[0].payload.status, 'started');
  assert.equal(actions[0].payload.cancellationRequested, true);
  assert.equal(actions.some((event) => event.type === 'remove'), false);
  exports.actionHandlers[exports.UPDATE_COMMAND](() => ({}), { id: 12, status: 'started', body: { sendUpdatesToClient: false } }, (event) => actions.push(event));
  assert.equal(timers.length, 0, 'running rows are not aged out while work is still running');
  actions.length = 0;
  exports.actionHandlers[exports.CANCEL_COMMAND](() => ({ commands: { items: [{ id: 12, status: 'cancelled' }] } }), { id: 12 }, (event) => actions.push(event));
  assert.equal(actions.some((event) => event.type === 'update'), false, 'a late cancellation response cannot resurrect a task already marked cancelled');
});
