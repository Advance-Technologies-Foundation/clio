const { withModuleFederationPlugin } = require('@angular-architects/module-federation/webpack');
const { set } = require('lodash');

const mfConfig = {
  name: '<%projectName%>',
  filename: 'remoteModuleEntry.js',
  exposes: {
    './Main': './src/main.ts',
  },
  shared: {},
  sharedMappings: [],
};

const config = withModuleFederationPlugin(mfConfig);
set(config, 'resolve.alias.lodash', 'lodash-es');
set(config, 'output.uniqueName', '<%projectName%>');
set(config, 'optimization.splitChunks.chunks', 'async');

module.exports = config;
