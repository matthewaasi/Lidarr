const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const babel = require('@babel/core');
const React = require('react');

test('queue interval renders saved seconds and sends edits using the seconds API field', () => {
  const filename = path.resolve(__dirname, '../src/Settings/DownloadClients/Options/DownloadClientOptions.js');
  const { code } = babel.transformSync(fs.readFileSync(filename, 'utf8'), {
    filename, babelrc: false, configFile: false,
    presets: [['@babel/preset-env', { targets: { node: 'current' }, modules: 'commonjs' }], '@babel/preset-react']
  });
  const module = { exports: {} };
  vm.runInNewContext(code, {
    module, exports: module.exports,
    require: (id) => {
      if (id === 'react' || id === 'prop-types') return require(id);
      if (id === 'Helpers/Props') return { inputTypes: { NUMBER: 'number' }, kinds: {}, sizes: {} };
      if (id === 'Utilities/String/translate') return (key) => key;
      return id;
    }
  }, { filename });
  const Component = module.exports.default;
  function find(element) {
    if (!element || typeof element !== 'object') return null;
    if (element.props?.name === 'queueRefreshIntervalSeconds') return element;
    return React.Children.toArray(element.props?.children).map(find).find(Boolean);
  }
  for (const value of [1, 15, 30, 60, 300, 86400]) {
    let change;
    const input = find(Component({
      advancedSettings: false, isFetching: false, hasSettings: true,
      settings: { queueRefreshIntervalSeconds: { value } },
      onInputChange: (event) => { change = event; }
    }));
    assert.ok(input, 'field is available without advanced settings');
    assert.equal(input.props.value, value);
    assert.equal(input.props.min, 1);
    assert.equal(input.props.max, 86400);
    input.props.onChange({ name: input.props.name, value: 15 });
    assert.deepEqual(change, { name: 'queueRefreshIntervalSeconds', value: 15 });
  }
});
