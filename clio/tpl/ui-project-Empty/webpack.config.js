const { withModuleFederationPlugin } = require('@angular-architects/module-federation/webpack');

const config = withModuleFederationPlugin({
  name: '<%projectName%>',
  filename: 'remoteModuleEntry.js',
  exposes: {
    './Main': './src/main.ts',
  },
  shared: {},
  sharedMappings: [],
});

config.resolve.alias.lodash = 'lodash-es';
config.output.uniqueName = '<%projectName%>';
config.optimization.splitChunks = {
  ...config.optimization.splitChunks,
  chunks: 'all'
};

module.exports = config;
