// Creatio loads `remoteModuleEntry.js` with `import()` and calls `get('./Main')`, so the container
// has to stay an ES module.
const path = require('path');
const rspack = require('@rspack/core');

// @nx/angular-rspack builds an Nx project graph and would cache it in a `.nx` folder next to this file.
process.env.NX_WORKSPACE_DATA_DIRECTORY ??= path.join(__dirname, 'node_modules/.cache/nx');

const { createConfig } = require('@nx/angular-rspack');

const REMOTE_NAME = '<%projectName%>';
const OUTPUT_PATH = '<%distPath%>';

// `rspack` only defaults NODE_ENV from `--node-env`, so an inherited NODE_ENV would pick the build mode,
// and a development build ships source maps into the package. @nx/angular-rspack reads NODE_ENV too.
const nodeEnvArg = process.argv.find((arg) => arg.startsWith('--node-env='));
if (nodeEnvArg) {
  process.env.NODE_ENV = nodeEnvArg.slice('--node-env='.length);
}
const isDev = process.env.NODE_ENV === 'development';
// Set by `rspack serve`.
const isServe = Boolean(process.env.RSPACK_SERVE);

function getBuildOptions() {
  const options = {
    root: __dirname,
    browser: './src/main.ts',
    index: './src/index.html',
    polyfills: ['zone.js'],
    tsConfig: './tsconfig.app.json',
    inlineStyleLanguage: 'scss',
    // Object form: outside an Nx workspace @nx/angular-rspack mis-resolves the source root for
    // string asset paths and rejects them.
    assets: [
      { glob: 'favicon.ico', input: './src', output: '.' },
      { glob: '**/*', input: './src/assets', output: 'assets' },
    ],
    styles: ['./src/styles.scss'],
    // Flat output; without `browser: '.'` the files land in a `browser/` subfolder.
    outputPath: { base: OUTPUT_PATH, browser: '.', media: '.' },
    outputHashing: 'none',
  };
  if (isServe) {
    // The dev server writes nothing to disk, so keep the last build. `publicHost`: the page is served
    // by Creatio, so live reload must connect back to this server.
    Object.assign(options, {
      devServer: { port: 4100, hmr: false, publicHost: 'http://localhost:4100' },
      deleteOutputPath: false,
    });
  }
  if (isDev) {
    return {
      ...options,
      optimization: false,
      // `sourceMap: true` would mean hidden maps plus parsing the source maps of every node_modules file.
      sourceMap: { scripts: true, styles: true, hidden: false, vendor: false },
      namedChunks: true,
      extractLicenses: false,
    };
  }
  return {
    ...options,
    budgets: [
      { type: 'initial', maximumWarning: '500kb', maximumError: '1mb' },
      { type: 'anyComponentStyle', maximumWarning: '6kb', maximumError: '9kb' },
    ],
  };
}

function applyRemoteModuleConfig(config) {
  // Chunks resolve against the container's own URL, not the Creatio page.
  config.output = { ...config.output, uniqueName: REMOTE_NAME, publicPath: 'auto' };
  // The container bootstraps itself; a separate runtime chunk would break it.
  config.optimization = { ...config.optimization, runtimeChunk: false };
  // `rspack serve` otherwise compiles `import()` targets on demand through a request to the page's
  // own origin, which is Creatio, so feature activation would never load.
  config.lazyCompilation = false;
  config.resolve = { ...config.resolve, alias: { ...config.resolve?.alias, lodash: 'lodash-es' } };
  // Creatio pages load RequireJS: without AMD parsing a bundled UMD module calls the page's
  // `define()` and RequireJS throws "Mismatched anonymous define()".
  config.amd = {};
  config.module.rules.push({
    // zone.js cannot track native async/await, so downlevel it as Angular's webpack build did.
    test: /\.[cm]?[jt]sx?$/,
    enforce: 'post',
    use: [
      {
        loader: 'builtin:swc-loader',
        options: {
          jsc: { parser: { syntax: 'ecmascript' } },
          env: {
            // Deliberately modern: `include` forces just these two transforms and nothing else.
            targets: 'chrome >= 120',
            include: ['transform-async-to-generator', 'transform-async-generator-functions'],
          },
        },
      },
    ],
  });
  config.module.rules.push({
    // Rspack 2.2 tree shaking emits invalid JavaScript for an unused class with a static private
    // method, e.g. `InterfaceDesignerSchemaService` in @creatio/interface-designer; compiling the
    // Creatio packages to ES2021 removes the `static #` syntax first. Reproduced on Rspack 2.2.2
    // to 2.2.7; drop this rule once the Empty template builds without it.
    test: /\.m?js$/,
    include: /[\\/]node_modules[\\/]@creatio(-devkit)?[\\/]/,
    enforce: 'post',
    use: [{ loader: 'builtin:swc-loader', options: { jsc: { target: 'es2021', parser: { syntax: 'ecmascript' } } } }],
  });
  config.plugins.push(
    // Module Federation 1 container: the Module Federation 2 runtime would add ~110 KB to every
    // entry, and the template shares nothing with the host.
    new rspack.container.ModuleFederationPluginV1({
      name: REMOTE_NAME,
      filename: 'remoteModuleEntry.js',
      exposes: { './Main': './src/main.ts' },
      shared: {},
      library: { type: 'module' },
      runtime: false,
    }),
  );
  return config;
}

module.exports = async () => {
  const [config] = await createConfig({ options: getBuildOptions() });
  return applyRemoteModuleConfig(config);
};
