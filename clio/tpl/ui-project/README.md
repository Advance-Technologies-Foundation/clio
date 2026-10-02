# Example
This project is example of Creatio remote module project.

## Build
Execute command
`npm run build`.
Rspack builds the remote module with `rspack.config.js` into the package file content folder set in `OUTPUT_PATH`.

## Test
Execute command
`npm run test`. This runs Jest with `jest-preset-angular`; `setup-jest.ts` initializes Angular's zone-based test environment.
A new project contains no specs, so this command exits non-zero with `No tests found` until you add a `*.spec.ts` file.

## Update Angular
Execute command
`ng update`. The build, the dev server and the tests run through npm scripts, so `angular.json` has no `build`, `serve` or `test` targets. The `ng-update` and `ng-update-test` targets only point the `ng update` migrations to `tsconfig.app.json` and `tsconfig.spec.json`; do not run them.

## How to develop and consume remote module
You can find different examples by following https://academy.creatio.com link.


default
