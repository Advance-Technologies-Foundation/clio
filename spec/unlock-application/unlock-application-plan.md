# Unlock Application Plan

## Objective
Extend `unlock-package` so it can unlock all packages in a target environment by maintainer supplied through CLI.

Target command:

```bash
clio unlock-package -m Creatio -e dev_env_n8
```

## Functional Requirements
1. Add/enable a maintainer argument for `unlock-package` (`-m`, `--maintainer`).
2. For command execution, set system setting `Maintainer` to the maintainer argument value.
3. Unlock all packages in the selected environment that match the maintainer context.
4. Do not modify package maintainer metadata in package records.

## Implementation Plan
1. Locate current `unlock-package` command handler and argument parser.
2. Add maintainer option (`-m`, `--maintainer`) and validate it as required for this flow.
3. Implement/update service call that sets system setting `Maintainer` before unlock starts.
4. Execute unlock workflow for all applicable packages in the environment.
5. Ensure no code path updates package maintainer fields directly.
6. Add clear command output:
   - maintainer value used
   - environment used
   - number of packages unlocked
   - failures (if any)

## Error Handling
1. Missing maintainer argument: fail fast with actionable message.
2. Missing/invalid environment: fail with existing environment validation behavior.
3. Failed `Maintainer` update: stop unlock and return error.
4. Partial unlock failures: report failed packages and return non-success exit code.

## Tests
1. Argument parsing test for `-m`/`--maintainer`.
2. Unit test: `Maintainer` setting is updated before unlock execution.
3. Unit/integration test: unlock processes multiple packages.
4. Guard test: package maintainer field is not mutated.
5. Failure-path tests for setting update and unlock errors.

## Documentation Updates
1. Update `clio\Commands.md` with maintainer usage for `unlock-package`.
2. Update `clio\help\en\unlock-package.txt` to include new option and example.
3. Update `clio\docs\commands\unlock-package.md` with behavior and sample command.

## Acceptance Criteria
1. `clio unlock-package -m Creatio -e dev_env_n8` updates `Maintainer` to `Creatio` and unlocks matching packages.
2. Package maintainer metadata remains unchanged.
3. Command returns clear summary and proper error code on failures.
