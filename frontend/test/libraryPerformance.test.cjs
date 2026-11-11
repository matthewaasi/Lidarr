const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const babel = require('@babel/core');
const lodash = require('lodash');

function load(file, imports = {}) {
  const filename = path.resolve(__dirname, '../src', file);
  const { code } = babel.transformSync(fs.readFileSync(filename, 'utf8'), {
    filename, babelrc: false, configFile: false,
    presets: [['@babel/preset-env', { targets: { node: 'current' }, modules: 'commonjs' }], '@babel/preset-react']
  });
  const module = { exports: {} };
  vm.runInNewContext(code, { module, exports: module.exports,
    require: (id) => Object.hasOwn(imports, id) ? imports[id] : require(id)
  }, { filename });
  return module.exports;
}

const createCollectionSelector = load('Store/Selectors/createArtistClientSideCollectionItemsSelector.js', {
  './createClientSideCollectionSelector': () => (state) => state.collection
}).default;

function collection(extra = {}) {
  return { items: [{ id: 1, sortName: 'a' }, { id: 2, sortName: 'b' }], isFetching: false, isPopulated: true, totalItems: 2, ...extra };
}

test('artist collection reuses its result when non-displayed metadata changes', () => {
  const select = createCollectionSelector('artistIndex');
  const first = select({ collection: collection() });
  const second = select({ collection: collection({ items: [{ id: 1, sortName: 'a', overview: 'changed' }, { id: 2, sortName: 'b' }] }) });
  assert.equal(second, first);
});

test('artist collection does not cache across loading, error, filter or order changes', () => {
  const select = createCollectionSelector('artistIndex');
  const source = collection();
  const first = select({ collection: source });
  for (const changed of [
    { isFetching: true }, { isPopulated: false }, { error: { status: 500 } },
    { selectedFilterKey: 'monitored' }, { totalItems: 3 },
    { items: [...source.items].reverse() }, { items: [{ id: 1, sortName: 'z' }, source.items[1]] }
  ]) {
    const next = select({ collection: { ...source, ...changed } });
    assert.notEqual(next, first);
    assert.equal(next.isFetching, changed.isFetching ?? source.isFetching);
  }
});

function connector(file, component) {
  const sorts = [];
  const measured = { ...lodash, orderBy: (...args) => { sorts.push(args[1]); return lodash.orderBy(...args); } };
  const imports = {
    lodash: measured,
    'react-redux': { connect: () => (Component) => Component },
    'Commands/commandNames': {},
    'Store/Actions/albumActions': {}, 'Store/Actions/artistActions': {},
    'Store/Actions/commandActions': {}, 'Store/Actions/queueActions': {},
    'Store/Actions/trackActions': {}, 'Store/Actions/trackFileActions': {},
    'Store/Selectors/createAllArtistSelector': () => (state) => state.artist.items,
    'Store/Selectors/createCommandsSelector': () => (state) => state.commands.items,
    'Store/Selectors/createUISettingsSelector': () => (state) => state.settings.ui.item,
    'Utilities/Command': { findCommand: () => undefined, isCommandExecuting: () => false },
    'Utilities/pagePopulator': {},
    [`./${component}`]: () => null
  };
  return { select: load(file, imports).createMapStateToProps(), sorts };
}

function state() {
  return {
    artist: { items: [{ id: 1, foreignArtistId: 'artist', sortName: 'Artist', alternateTitles: [] }] },
    albums: { items: [{ id: 10, artistId: 1, foreignAlbumId: 'album', albumType: 'Album', releaseDate: '2020-01-01' }], isPopulated: true, isFetching: false },
    tracks: { items: [], isPopulated: false, isFetching: true },
    trackFiles: { items: [], isFetching: true, isPopulated: false },
    commands: { items: [] }, settings: { ui: { item: {} } }
  };
}

test('artist album list is available before track files finish loading, and preserves file errors', () => {
  const { select } = connector('Artist/Details/ArtistDetailsConnector.js', 'ArtistDetails');
  const source = state();
  const result = select(source, { foreignArtistId: 'artist' });
  assert.equal(result.isPopulated, true);
  assert.equal(result.isFetching, false);
  assert.equal(result.hasTrackFiles, false);
  const error = { status: 500 };
  assert.equal(select({ ...source, trackFiles: { ...source.trackFiles, error } }, { foreignArtistId: 'artist' }).trackFilesError, error);
});

test('artist navigation is not re-sorted for command or track-file updates', () => {
  const { select, sorts } = connector('Artist/Details/ArtistDetailsConnector.js', 'ArtistDetails');
  const source = state();
  const props = { foreignArtistId: 'artist' };
  select(source, props);
  select({ ...source, commands: { items: [{}] } }, props);
  select({ ...source, trackFiles: { ...source.trackFiles, isFetching: false, isPopulated: true } }, props);
  assert.equal(sorts.filter((key) => key === 'sortName').length, 1);
  select({ ...source, artist: { items: [...source.artist.items] } }, props);
  assert.equal(sorts.filter((key) => key === 'sortName').length, 2);
});

test('release navigation is not re-sorted for track or command updates', () => {
  const { select, sorts } = connector('Album/Details/AlbumDetailsConnector.js', 'AlbumDetails');
  const source = state();
  const props = { foreignAlbumId: 'album' };
  select(source, props);
  select({ ...source, commands: { items: [{}] } }, props);
  select({ ...source, tracks: { ...source.tracks, isFetching: false } }, props);
  assert.equal(sorts.filter((key) => key === 'releaseDate').length, 1);
  select({ ...source, albums: { ...source.albums, items: [...source.albums.items] } }, props);
  assert.equal(sorts.filter((key) => key === 'releaseDate').length, 2);
});

test('release selector safely handles the requested album disappearing', () => {
  const { select } = connector('Album/Details/AlbumDetailsConnector.js', 'AlbumDetails');
  assert.equal(Object.keys(select(state(), { foreignAlbumId: 'missing' })).length, 0);
});
