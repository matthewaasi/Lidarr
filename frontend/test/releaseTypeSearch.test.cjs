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
