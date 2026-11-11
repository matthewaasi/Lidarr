const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const babel = require('@babel/core');

const filename = path.resolve(__dirname, '../src/InteractiveImport/Track/SelectTrackModalContentConnector.js');
const { code } = babel.transformSync(fs.readFileSync(filename, 'utf8'), {
  filename, babelrc: false, configFile: false,
  presets: [['@babel/preset-env', { targets: { node: 'current' }, modules: 'commonjs' }], '@babel/preset-react']
});
const moduleUnderTest = { exports: {} };
const stubs = {
  'react-redux': { connect: () => (component) => component },
  'Store/Actions/interactiveImportActions': {},
  'Store/Actions/trackActions': {},
  'Store/Selectors/createClientSideCollectionSelector': () => () => ({}),
  './SelectTrackModalContent': 'content'
};
vm.runInNewContext(code, {
  module: moduleUnderTest, exports: moduleUnderTest.exports,
  require: (id) => Object.hasOwn(stubs, id) ? stubs[id] : require(id)
}, { filename });
const Connector = moduleUnderTest.exports.default;

for (const release of [0, undefined, null, -1]) {
  test('missing release ' + release + ' fetches album tracks without a release filter', () => {
    let query;
    new Connector({ artistId: 1, albumId: 2, albumReleaseId: release, fetchTracks: (value) => { query = value; } }).componentDidMount();
    assert.equal(query.artistId, 1);
    assert.equal(query.albumId, 2);
    assert.equal(query.albumReleaseId, undefined);
  });
}

test('an identified release retains its exact track list', () => {
  let query;
  new Connector({ artistId: 1, albumId: 2, albumReleaseId: 42, fetchTracks: (value) => { query = value; } }).componentDidMount();
  assert.equal(query.albumReleaseId, 42);
});

test('manual track selection carries the release into the import payload', () => {
  let updated;
  let closed;
  const instance = new Connector({
    id: 9, albumReleaseId: 0,
    items: [{ id: 10, albumReleaseId: 42, trackNumber: '1' }, { id: 11, albumReleaseId: 42, trackNumber: '2' }],
    updateInteractiveImportItem: (value) => { updated = value; },
    onModalClose: (changed) => { closed = changed; }
  });
  instance.onTracksSelect([11]);
  assert.equal(updated.id, 9);
  assert.equal(updated.albumReleaseId, 42);
  assert.deepEqual(Array.from(updated.tracks, (track) => track.id), [11]);
  assert.equal(closed, true);
});
