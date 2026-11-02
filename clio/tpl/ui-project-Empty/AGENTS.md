## Remote Module Reference Guides

**MANDATORY:** Read and follow these guides before working on any task related to Creatio Freedom UI integrations with remote modules:

Runtime components: node_modules\@creatio-devkit\common\AI_GUIDES_INDEX.md
Design-time components (if relevant): node_modules\@creatio\interface-designer\AI_GUIDES_INDEX.md

## Zoneless change detection (required)

Creatio loads only the remote entry of this project, never its `polyfills` bundle, and the
Creatio page can run without zone.js. The project therefore bootstraps with
`provideZonelessChangeDetection()` (`src/app/remote-app-context.ts`) and must work the same
whether the Creatio page loads zone.js or not.

Rules:

- Never add `zone.js` to `package.json`, to `polyfills` in `angular.json`, or as an import.
- Do not rely on `NgZone` to render: `NgZone.run`, `runOutsideAngular`, `onStable`,
  `onMicrotaskEmpty` and `isStable` do nothing without zone.js.
- Angular renders a component when a signal its template reads changes, an input changes
  (the Creatio binding or `setInput`), a template event handler such as `(click)` runs, the
  `async` pipe emits, or `ChangeDetectorRef.markForCheck()` is called. State that changes
  anywhere else (`setTimeout`, `setInterval`, a promise or `subscribe` callback, a listener added
  with `addEventListener`, a third-party library callback, a request handler that updates a
  component) must be a signal or be followed by `markForCheck()`.
- Prefer signals (`signal`, `computed`, `input`) and `ChangeDetectionStrategy.OnPush`.
- Do not call `ChangeDetectorRef.detectChanges()` as the routine way to render.
- Tests: `ng test` (`@angular-builders/jest`) runs zoneless by default; never set
  `zoneless: false`. Use `await fixture.whenStable()` and Jest fake timers, not `fakeAsync`,
  `tick` or `waitForAsync`, which need zone.js.
- Check every change in Creatio with zoneless mode on and off: the browser console shows no
  `NG0908` error and the component renders and updates.

### Migrating an existing remote module project

1. Move to the Angular version of this template (see `package.json`): `@angular/*`, `@angular/cli`
   and `@angular-devkit/build-angular` 22.0.8, `@angular-architects/module-federation` ^22,
   `ngx-build-plus` ^20, TypeScript ~6.0, Node.js 22.22.3 or later. Pin `webpack` to the version
   `@angular-devkit/build-angular` depends on (5.106.2): a second webpack copy breaks the Module
   Federation plugin. TypeScript 6 rejects `baseUrl` and `downlevelIteration` in `tsconfig.json`;
   remove them.
2. Remove `zone.js` from `package.json`, set `"polyfills": []` for the build and test targets in
   `angular.json`, and delete `src/polyfills.ts`.
3. Bootstrap the root module with
   `platformBrowser().bootstrapModule(AppModule, { applicationProviders: [provideZonelessChangeDetection()] })`
   instead of `platformBrowserDynamic().bootstrapModule(AppModule)`.
4. Apply the Angular 19+ defaults: `standalone: false` on components declared in an `NgModule`,
   `buildTarget` instead of `browserTarget` in `angular.json`, `provideAppInitializer` instead of
   `APP_INITIALIZER`.
5. In `webpack.config.js`, share `@creatio-devkit/common` with `requiredVersion: 'auto'` instead of a
   pinned version.
6. Apply the rules above to every component and service: find each callback that changes state a
   template reads and make that state a signal or call `markForCheck()` after it.
7. Build, run the tests, deploy the package, and check it in Creatio with zoneless mode on and off.

## Creating package test projects (required)

Clio is the only approved way to scaffold package unit-test and integration-test projects.
Do not hand-create test `.csproj`, `.sln`, or `.slnx` files, run `dotnet new` test templates,
or invent a separate test harness. Write scenario-specific test cases inside the Clio-generated
projects, using their existing frameworks, base fixtures, and configuration conventions.
This rule concerns package C# tests; retain the existing Angular test tooling for UI tests.

Run these commands from the Clio workspace root (the directory containing `.clio`):

```shell
clio unit-test --package <Package_Name>
clio integration-test --package <Package_Name>
```

For example, for package `UsrOrders`:

```shell
clio unit-test --package UsrOrders
clio integration-test --package UsrOrders --target-framework net8.0
```

Integration tests default to `net10.0`; select `--target-framework` to match the intended test runtime.
Before adding integration scenarios, read `get-guidance name=integration-testing`.
For MCP, discover contracts with `get-tool-contract`, then invoke the existing tools through `clio-run`:

```json
{"command":"new-test-project","args":{"package-name":"UsrOrders","workspace-path":"<absolute-workspace-path>","environment-name":"<registered-environment>"}}
```

```json
{"command":"new-integration-test-project","args":{"package-name":"UsrOrders","workspace-path":"<absolute-workspace-path>","target-framework":"net8.0"}}
```

Use the workspace root even when working inside a nested UI project. Unit-test MCP scaffolding
requires a registered environment name; integration-test scaffolding does not.
Verify the command's exit code and the resulting solution membership before reporting success:

| Scaffold | Generated test project | Test solution | Workspace solution |
|---|---|---|---|
| Unit | `tests/UsrOrders/UsrOrders.Tests.csproj` | `tests/UnitTests.slnx` | `MainSolution.slnx` |
| Integration | `tests/UsrOrders.IntegrationTests/UsrOrders.IntegrationTests.csproj` | `tests/IntegrationTests.slnx` | `MainSolution.slnx` |

Both solutions must contain the generated test project. UnitTests.slnx also contains the package
project under test. Rerunning the unit scaffold repairs missing registrations and preserves existing
project/fixture files. The integration scaffold refuses an existing project directory to protect
customizations. If Clio reports an error, report the diagnostic and fix the Clio workflow; do not
bypass it by inventing another test project. Scaffolding alone does not prove the tests pass.
