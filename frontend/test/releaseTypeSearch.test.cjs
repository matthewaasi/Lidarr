const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const babel = require('@babel/core');
const React = require('react');

// Load the real components/selectors without booting the browser-wide Redux store.
function load(relativePath, imports = {}) {
  const filename = path.resolve(__dirname, '../src', relativePath);
  const { code } = babel.transformSync(fs.readFileSync(filename, 'utf8'), {
    filename,
    babelrc: false,
    configFile: false,
    presets: [['@babel/preset-env', { targets: { node: 'current' }, modules: 'commonjs' }], '@babel/preset-react']
  });
  const module = { exports: {} };
  vm.runInNewContext(code, {
    module,
    exports: module.exports,
    require: (id) => Object.hasOwn(imports, id) ? imports[id] : require(id),
    Set,
    console
  }, { filename });
  return module.exports;
}

const names = load('Commands/commandNames.js');
const executing = load('Utilities/Command/isCommandExecuting.js').default;
const connector = load('Artist/Details/ArtistDetailsSeasonConnector.js', {
  'Commands/commandNames': names,
  'Store/Actions/albumActions': {},
  'Store/Actions/commandActions': {},
  'Store/Selectors/createArtistSelector': () => (state) => state.artist,
  'Store/Selectors/createClientSideCollectionSelector': () => (state) => state.albums,
  'Store/Selectors/createCommandsSelector': () => (state) => state.commands,
  'Store/Selectors/createDimensionsSelector': () => (state) => state.dimensions,
  'Store/Selectors/createUISettingsSelector': () => (state) => state.uiSettings,
  'Utilities/Command': { isCommandExecuting: executing },
  './ArtistDetailsSeason': () => null
});

function state(commands = []) {
  return {
    artist: { id: 1, monitored: true },
    albums: { items: [
      { id: 10, artistId: 1, albumType: 'Album', monitored: true, statistics: { trackFileCount: 10, totalTrackCount: 10 } },
      { id: 11, artistId: 1, albumType: 'Album', monitored: false },
      { id: 12, artistId: 1, albumType: 'Single', monitored: true },
      { id: 13, artistId: 2, albumType: 'Album', monitored: true },
      { id: 14, artistId: 1, albumType: 'Album', monitored: true, secondaryTypes: ['Live', 'Compilation'] }
    ], columns: [] },
    commands,
    dimensions: { isSmallScreen: false },
    uiSettings: {}
  };
}

function select(commands, label = 'Album') {
  return connector.createMapStateToProps()(state(commands), { label, artistId: 1 });
}

function instance(extra = {}) {
  const component = new connector.ArtistDetailsSeasonConnector({
    artistId: 1,
    searchAlbumIds: [10, 14],
    isSearching: false,
    queueAlbumSearch: async () => {},
    ...extra
  });
  component.setState = (update) => Object.assign(component.state, update);
  component.componentDidMount();
  return component;
}

test('targets only monitored releases in this artist section, including completed albums and secondary types', () => {
  assert.deepEqual(Array.from(select([]).searchAlbumIds), [10, 14]);
  assert.deepEqual(Array.from(select([], 'Single').searchAlbumIds), [12]);
  assert.deepEqual(Array.from(select([], 'EP').searchAlbumIds), []);
  assert.equal(select([]).items.length, 3);
});

for (const status of ['queued', 'started']) {
  test(`${status} overlapping album searches and this artist's search show busy`, () => {
    assert.equal(select([{ name: names.ALBUM_SEARCH, status, body: { albumIds: [14] } }]).isSearching, true);
    assert.equal(select([{ name: names.ARTIST_SEARCH, status, body: { artistId: 1 } }]).isSearching, true);
  });
}

test('other artists, other sections, and finished/failed searches do not lock this section', () => {
  for (const command of [
    { name: names.ALBUM_SEARCH, status: 'started', body: { albumIds: [12] } },
    { name: names.ARTIST_SEARCH, status: 'started', body: { artistId: 2 } },
    { name: names.ALBUM_SEARCH, status: 'completed', body: { albumIds: [10] } },
    { name: names.ALBUM_SEARCH, status: 'failed', body: { albumIds: [10] } },
    { name: names.ALBUM_SEARCH, status: 'started', body: {} }
  ]) {
    assert.equal(Boolean(select([command]).isSearching), false);
  }
});

test('single click sends one native AlbumSearch command; rapid double clicks cannot duplicate it', async () => {
  let finish;
  const payloads = [];
  const component = instance({ queueAlbumSearch: (payload) => {
    payloads.push(payload);
    return new Promise((resolve) => { finish = resolve; });
  } });
  const first = component.onSearchPress();
  await component.onSearchPress();
  assert.equal(payloads.length, 1);
  assert.equal(payloads[0].name, names.ALBUM_SEARCH);
  assert.deepEqual(Array.from(payloads[0].albumIds), [10, 14]);
  assert.equal(component.state.isQueueingSearch, true);
  finish();
  await first;
  assert.equal(component.state.isQueueingSearch, false);
});

test('empty sections and already-running searches cannot submit', async () => {
  let calls = 0;
  const queueAlbumSearch = () => { calls++; };
  await instance({ searchAlbumIds: [], queueAlbumSearch }).onSearchPress();
  await instance({ isSearching: true, queueAlbumSearch }).onSearchPress();
  assert.equal(calls, 0);
});

test('queue failure is visible and releases the click guard so the user can retry', async () => {
  let fail = true;
  const component = instance({ queueAlbumSearch: async () => {
    if (fail) throw Error('HTTP 503');
  } });
  await component.onSearchPress();
  assert.equal(component.state.searchError, true);
  assert.equal(component.state.isQueueingSearch, false);
  fail = false;
  await component.onSearchPress();
  assert.equal(component.state.searchError, false);
});

test('late completion from an old artist cannot clear a new artist search', async () => {
  const finishes = [];
  const component = instance({ queueAlbumSearch: () => new Promise((resolve) => finishes.push(resolve)) });
  const first = component.onSearchPress();
  const previous = component.props;
  component.props = { ...previous, artistId: 2, searchAlbumIds: [13] };
  component.componentDidUpdate(previous);
  const second = component.onSearchPress();
  finishes[0]();
  await first;
  assert.equal(component.state.isQueueingSearch, true);
  finishes[1]();
  await second;
  assert.equal(component.state.isQueueingSearch, false);
});

test('unmounted headers do not receive state updates when a request finishes', async () => {
  let finish;
  const component = instance({ queueAlbumSearch: () => new Promise((resolve) => { finish = resolve; }) });
  const request = component.onSearchPress();
  component.componentWillUnmount();
  component.setState = () => assert.fail('setState after unmount');
  finish();
  await request;
});

const stub = () => null;
const spinner = () => null;
const nativeProps = { icons: { SEARCH: {}, EXPAND: {}, COLLAPSE: {} }, kinds: {}, sizes: {}, sortDirections: { all: ['ascending', 'descending'] }, tooltipPositions: {} };
const ui = load('Artist/Details/ArtistDetailsSeason.js', {
  'Album/AlbumFormats': stub,
  'Components/Icon': stub,
  'Components/Label': stub,
  'Components/Link/IconButton': stub,
  'Components/Link/Link': stub,
  'Components/Link/SpinnerIconButton': spinner,
  'Components/MonitorToggleButton': stub,
  'Components/Table/Table': stub,
  'Components/Table/TableBody': stub,
  'Components/Tooltip/Popover': stub,
  'Helpers/Props': nativeProps,
  'Organize/OrganizePreviewModalConnector': stub,
  'TrackFile/Editor/TrackFileEditorModal': stub,
  'Utilities/Date/isBefore': () => false,
  'Utilities/Number/formatBytes': String,
  'Utilities/String/translate': (key) => key,
  'Utilities/Table/getToggledRange': stub,
  './AlbumGroupInfo': stub,
  './AlbumRowConnector': stub,
  './ArtistDetailsSeason.css': new Proxy({}, { get: (_, key) => String(key) })
}).default;

function nodes(element) {
  if (!React.isValidElement(element)) return [];
  return [element, ...React.Children.toArray(element.props.children).flatMap(nodes)];
}

test('search button remains available when the section is collapsed, and exposes an accessible label', () => {
  const component = new ui({ items: [], columns: [], searchAlbumIds: [10], isExpanded: false, isSearching: false, searchError: false, label: 'Album' });
  const button = nodes(component.render()).find((element) => element.type === spinner);
  assert.ok(button);
  assert.equal(button.props.isDisabled, false);
  assert.equal(button.props['aria-label'], 'SearchMonitoredReleaseType');
});

test('UI disables empty/busy sections and renders queue failures as an alert', () => {
  for (const extra of [{ searchAlbumIds: [] }, { isSearching: true }]) {
    const component = new ui({ items: [], columns: [], searchAlbumIds: [10], isSearching: false, searchError: true, label: 'Album', ...extra });
    const rendered = nodes(component.render());
    assert.equal(rendered.find((element) => element.type === spinner).props.isDisabled, true);
    assert.ok(rendered.find((element) => element.props.role === 'alert'));
  }
});

function loadQueueConnector() {
  return load('Activity/Queue/QueueConnector.js', {
    'react-redux': { connect: () => (component) => component },
    'Commands/commandNames': names,
    'Components/withCurrentPage': (component) => component,
    'Store/Actions/albumActions': {},
    'Store/Actions/commandActions': {},
    'Store/Actions/queueActions': {},
    'Store/Selectors/createClientSideCollectionSelector': {},
    'Store/Selectors/createCommandExecutingSelector': stub,
    'Utilities/Object/hasDifferentItems': stub,
    'Utilities/Object/selectUniqueIds': stub,
    'Utilities/pagePopulator': {},
    './Queue': stub
  }).default;
}

test('Queue Refresh only refreshes list state without requesting AI retry', () => {
  const QueueConnector = loadQueueConnector();
  let sent;
  new QueueConnector({ executeCommand: (payload) => { sent = payload; } }).onRefreshPress();
  assert.equal(sent.name, names.REFRESH_MONITORED_DOWNLOADS);
  assert.equal(sent.queueOnly, true);
  assert.equal(sent.retryAiMatching, undefined);
});

function queueInstance(queueRetryAi, running = false) {
  const QueueConnector = loadQueueConnector();
  const instance = new QueueConnector({ queueRetryAi, isRetryAiMatchesExecuting: running });
  instance._isMounted = true;
  instance.setState = (update) => Object.assign(instance.state, update);
  return instance;
}

test('Retry AI sends a dedicated command and immediately guards against duplicate clicks', async () => {
  let finish;
  const payloads = [];
  const instance = queueInstance((payload) => {
    payloads.push(payload);
    return new Promise((resolve) => { finish = resolve; });
  });
  const pending = instance.onRetryAiPress();
  await instance.onRetryAiPress();
  assert.equal(payloads.length, 1);
  assert.equal(payloads[0].name, names.RETRY_AI_MATCHES);
  assert.equal(instance.state.isQueueingAiRetry, true);
  finish();
  await pending;
  assert.equal(instance.state.isQueueingAiRetry, false);
});

test('running Retry AI cannot be queued again; failed submissions show a retryable error', async () => {
  let calls = 0;
  await queueInstance(() => { calls++; }, true).onRetryAiPress();
  assert.equal(calls, 0);
  const instance = queueInstance(async () => { throw Error('HTTP 503'); });
  await instance.onRetryAiPress();
  assert.equal(instance.state.aiRetryError, true);
  assert.equal(instance.state.isQueueingAiRetry, false);
  instance.props.queueRetryAi = async () => {};
  await instance.onRetryAiPress();
  assert.equal(instance.state.aiRetryError, false);
});

test('Queue toolbar places Retry AI immediately after Refresh and tracks its own spinner', () => {
  const toolbarButton = () => null;
  const queue = load('Activity/Queue/Queue.js', {
    'Components/Alert': stub,
    'Components/Loading/LoadingIndicator': stub,
    'Components/Menu/FilterMenu': stub,
    'Components/Page/PageContent': stub,
    'Components/Page/PageContentBody': stub,
    'Components/Page/Toolbar/PageToolbar': stub,
    'Components/Page/Toolbar/PageToolbarButton': toolbarButton,
    'Components/Page/Toolbar/PageToolbarSection': stub,
    'Components/Page/Toolbar/PageToolbarSeparator': stub,
    'Components/Table/Table': stub,
    'Components/Table/TableBody': stub,
    'Components/Table/TableOptions/TableOptionsModalWrapper': stub,
    'Components/Table/TablePager': stub,
    'Helpers/Props': { ...nativeProps, align: {} },
    'Utilities/Object/getRemovedItems': stub,
    'Utilities/Object/hasDifferentItems': stub,
    'Utilities/String/translate': (key) => key,
    'Utilities/Table/getSelectedIds': () => [],
    'Utilities/Table/removeOldSelectedState': stub,
    'Utilities/Table/selectAll': stub,
    'Utilities/Table/toggleSelected': stub,
    './QueueFilterModal': stub,
    './QueueOptionsConnector': stub,
    './QueueRowConnector': stub,
    './RemoveQueueItemModal': stub
  }).default;
  const buttons = nodes(new queue({
    items: [], count: 2, totalRecords: 2, isPopulated: true,
    isArtistPopulated: true, isAlbumsPopulated: true, isRetryAiMatchesExecuting: true
  }).render()).filter((node) => node.type === toolbarButton);
  assert.deepEqual(buttons.slice(0, 2).map((button) => button.props.label), ['Refresh', 'RetryAi']);
  assert.equal(buttons[1].props.isSpinning, true);
  assert.equal(Boolean(buttons[0].props.isSpinning), false);
});

test('Queue status renders true import errors red and review cautions yellow', () => {
  const StatusCell = load('Activity/Queue/QueueStatusCell.js', {
    'Components/Icon': stub,
    'Components/Table/Cells/TableRowCell': stub,
    'Components/Tooltip/Popover': stub,
    'Helpers/Props': { icons: {}, kinds: { DANGER: 'red', WARNING: 'yellow' }, tooltipPositions: {} },
    'Utilities/String/translate': (key) => key,
    './QueueStatusCell.css': {}
  }).default;
  for (const [status, color] of [['error', 'red'], ['warning', 'yellow']]) {
    const cell = StatusCell({ status: 'completed', trackedDownloadState: 'importFailed', trackedDownloadStatus: status, statusMessages: [] });
    assert.equal(cell.props.children.props.anchor.props.kind, color);
  }
});

test('Compact queue details use the same red/yellow distinction', () => {
  const Details = load('Activity/Queue/QueueDetails.js', {
    'Components/Icon': stub,
    'Helpers/Props': { icons: {}, kinds: { DANGER: 'red', WARNING: 'yellow' } },
    'Utilities/String/translate': (key) => key
  }).default;
  for (const [status, color] of [['error', 'red'], ['warning', 'yellow']]) {
    assert.equal(Details({ status: 'completed', trackedDownloadStatus: status, size: 1, sizeleft: 0 }).props.kind, color);
  }
});

test('Manual Import remains available for red errors as well as yellow review holds', () => {
  const button = () => null;
  const interactive = {};
  const imports = Object.fromEntries([
    'Activity/Queue/ProtocolLabel', 'Album/AlbumFormats', 'Album/AlbumTitleLink', 'Album/TrackQuality', 'Artist/ArtistNameLink',
    'Components/Icon', 'Components/Link/SpinnerIconButton', 'Components/ProgressBar', 'Components/Table/Cells/RelativeDateCellConnector',
    'Components/Table/Cells/TableRowCell', 'Components/Table/Cells/TableSelectCell', 'Components/Table/TableRow', 'Components/Tooltip/Popover',
    'Components/Tooltip/Tooltip', 'InteractiveImport/InteractiveImportModal', './QueueStatusCell', './RemoveQueueItemModal', './TimeleftCell'
  ].map((name) => [name, stub]));
  const Row = load('Activity/Queue/QueueRow.js', {
    ...imports,
    'Components/Link/IconButton': button,
    'Helpers/Props': { icons: { INTERACTIVE: interactive }, kinds: {}, tooltipPositions: {} },
    'Utilities/Number/formatBytes': String,
    'Utilities/Number/formatCustomFormatScore': String,
    'Utilities/String/translate': (key) => key,
    './QueueRow.css': {}
  }).default;
  for (const status of ['error', 'warning']) {
    const row = new Row({ status: 'completed', trackedDownloadStatus: status, columns: [{ name: 'actions', isVisible: true }], size: 1, sizeleft: 0 });
    assert.ok(nodes(row.render()).some((node) => node.type === button && node.props.name === interactive));
  }
});

test('Manual Import does not preselect a matched track when its release has a true error', () => {
  const filename = 'InteractiveImport/Interactive/InteractiveImportRow.js';
  const source = fs.readFileSync(path.resolve(__dirname, '../src', filename), 'utf8');
  const imports = {};
  for (const match of source.matchAll(/from '([^']+)'/g)) {
    if (!['react', 'prop-types'].includes(match[1])) imports[match[1]] = stub;
  }
  imports['Helpers/Props'] = nativeProps;
  const Row = load(filename, imports).default;
  for (const [aiMatch, expected] of [
    [{ accepted: true, isError: true }, 0],
    [{ accepted: false, isError: false }, 0],
    [{ accepted: true, isError: false }, 1]
  ]) {
    let selected = 0;
    const row = new Row({ id: 1, artist: {}, album: {}, tracks: [{}], quality: {}, size: 1, aiMatch, onSelectedChange: () => { selected++; } });
    row.componentDidMount();
    assert.equal(selected, expected);
  }
});
