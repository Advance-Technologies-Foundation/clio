---
name: clio-local-e2e
description: Provision or safely reuse an exclusive local Creatio environment, install ClioGate and the real AutoTest fixtures, then run the complete clio MCP E2E suite. Use when preparing a local Creatio stand for clio.mcp.e2e or when proving the full E2E suite locally; do not use for shared stands or teardown.
---

# Clio Local E2E

Produce one exclusively owned Creatio stand with the real E2E fixtures and a zero-failure full-suite result.

## Safety boundary

- Require exclusive ownership before deploying, changing FSM, installing packages, compiling, or running destructive tests.
- Use a dedicated environment and workspace under `F:\Projects\Issue-Workspaces`; never mutate a shared stand.
- Reuse an existing environment only after `clio get-info -e <environment>` succeeds and ownership is confirmed.
- The runner accepts only `issue-*` environments backed by a Phase 1 workspace, requires the registered URL to match exactly, and holds an atomic endpoint lock for the full run.
- Do not uninstall the environment when the run finishes unless the user explicitly requests teardown.

## Deploy Creatio

1. Use the `clio-creatio-phase-1` skill when it is available. Otherwise follow its operator-maintained inputs directly:
   - create or reuse `F:\Projects\Issue-Workspaces\issue-<id>`;
   - select the configured build from `F:\Projects\Issue-Workspaces\tie.toml`;
   - copy and render `F:\Projects\Issue-Workspaces\Phase1.yaml`;
   - run `clio run --file-name .\Phase1.yaml` from the workspace.
2. Require successful ordered stages: `deploy-creatio`, `install-gate`, `turn-fsm`, `pkg-to-file-system`, and `compile-configuration`. Inspect stage output; the scenario exit code alone is insufficient proof.
3. For the known FSM-readiness race only, require successful compilation and `clio get-info`, verify `fileDesignMode=true` and `UseStaticFileContent=false`, then retry `clio pkg-to-file-system -e <environment>` exactly once.
4. Verify the stand before seeding:

   ```powershell
   clio get-info -e <environment>
   clio list-packages -e <environment>
   ```

   `ClioGate` must be installed and reachable.

## Install the real E2E fixtures

The fixtures are not stored in Clio. Pull the known-good installed packages from a reachable donor environment that already contains `AutoTest` and `AutoTestClioMcp`; a synthetic application or a source-only compressed archive is invalid for this run.

1. From the Clio repository root, build Clio and capture its executable path:

   ```powershell
   dotnet build .\clio\clio.csproj -c Debug -f net8.0
   if ($LASTEXITCODE -ne 0) { throw "Clio build failed." }
   $clioDll = (Resolve-Path .\clio\bin\Debug\net8.0\clio.dll).Path
   $workspace = "F:\Projects\Issue-Workspaces\issue-<id>"

   $donorJson = & dotnet $clioDll list-packages -e <fixture-donor-environment> --Json true
   if ($LASTEXITCODE -ne 0) { throw "Could not inspect fixture donor packages." }
   $donorPackages = ($donorJson | Out-String | ConvertFrom-Json).data
   $expectedAutoTest = @($donorPackages | Where-Object { $_.Descriptor.Name -ceq "AutoTest" })[0].Descriptor
   $expectedAutoTestClioMcp = @($donorPackages | Where-Object { $_.Descriptor.Name -ceq "AutoTestClioMcp" })[0].Descriptor
   if (-not $expectedAutoTest -or -not $expectedAutoTestClioMcp) {
       throw "The donor does not contain both required fixture packages."
   }

   function Assert-PackageIdentity($Expected, $InstalledPackages) {
       $actual = @($InstalledPackages | Where-Object { $_.Descriptor.Name -ceq $Expected.Name })[0].Descriptor
       if (-not $actual `
           -or $actual.UId -ne $Expected.UId `
           -or $actual.Name -cne $Expected.Name `
           -or $actual.PackageVersion -ne $Expected.PackageVersion) {
           throw "Installed package identity does not match donor package '$($Expected.Name)'."
       }
   }
   ```

2. Pull the packages from the donor into the issue workspace, preserving dependency order:

   ```powershell
   Push-Location $workspace
   if ((Test-Path .\AutoTest.zip) -or (Test-Path .\AutoTestClioMcp.zip)) {
       throw "Refusing to reuse existing fixture ZIPs; move or remove them after verifying their ownership."
   }
   dotnet $clioDll pull-pkg AutoTest -e <fixture-donor-environment>
   if ($LASTEXITCODE -ne 0) { throw "AutoTest pull failed." }
   dotnet $clioDll pull-pkg AutoTestClioMcp -e <fixture-donor-environment>
   if ($LASTEXITCODE -ne 0) { throw "AutoTestClioMcp pull failed." }
   Pop-Location
   ```

   Require `AutoTest.zip` and `AutoTestClioMcp.zip` to exist and be nonempty.

3. Install `AutoTest` first. After every `push-pkg`, require three consecutive successful `get-info` probes within five minutes before continuing, then verify the installed package with `list-packages`:

   ```powershell
   function Wait-CreatioReady([string] $EnvironmentName) {
       $deadline = [DateTimeOffset]::UtcNow.AddMinutes(5)
       $consecutive = 0
       while ([DateTimeOffset]::UtcNow -lt $deadline -and $consecutive -lt 3) {
           & dotnet $clioDll get-info -e $EnvironmentName
           $consecutive = if ($LASTEXITCODE -eq 0) { $consecutive + 1 } else { 0 }
           if ($consecutive -lt 3) { Start-Sleep -Seconds 10 }
       }
       if ($consecutive -lt 3) { throw "Creatio did not reach three consecutive readiness probes." }
   }

   dotnet $clioDll push-pkg "$workspace\AutoTest.zip" -e <environment>
   if ($LASTEXITCODE -ne 0) { throw "AutoTest install failed." }
   Wait-CreatioReady <environment>
   $installedJson = & dotnet $clioDll list-packages -e <environment> --Json true
   if ($LASTEXITCODE -ne 0) { throw "Could not verify installed packages." }
   Assert-PackageIdentity $expectedAutoTest (($installedJson | Out-String | ConvertFrom-Json).data)

   dotnet $clioDll push-pkg "$workspace\AutoTestClioMcp.zip" -e <environment>
   if ($LASTEXITCODE -ne 0) { throw "AutoTestClioMcp install failed." }
   Wait-CreatioReady <environment>
   $installedJson = & dotnet $clioDll list-packages -e <environment> --Json true
   if ($LASTEXITCODE -ne 0) { throw "Could not verify installed packages." }
   Assert-PackageIdentity $expectedAutoTestClioMcp (($installedJson | Out-String | ConvertFrom-Json).data)
   ```

4. Verify `AutoTest`, `AutoTestClioMcp`, and `ClioGate` with `list-packages`. Confirm the installed application code `AutoTestClioMcp` is discoverable before interpreting fixture-dependent skips.

## Run the complete suite

From the Clio repository, invoke the bundled runner:

```powershell
.\.codex\skills\clio-local-e2e\scripts\run-clio-e2e.ps1 `
  -EnvironmentName <environment> `
  -EnvironmentUrl https://host:port `
  -SeedKeyPrefix LOCAL-<unique-run-id>
```

The runner performs its fail-closed workspace, registration, URL, HTTPS, and endpoint-lock preflight before it sets the destructive opt-in. It then builds `clio.mcp.e2e` for `net8.0` and runs the entire project with a TRX logger. Pass `-DatabaseProvider <name>` to opt into database-provider tests; otherwise the runner clears ambient provider configuration and those tests may skip.

## Acceptance and handoff

- Require zero failed tests in the full, unfiltered `clio.mcp.e2e` run.
- Report passed, skipped, failed, total, duration, TRX path, environment name and URL, package verification, and any single bounded FSM recovery.
- Explain configuration- or topology-driven skips. Never report a filtered run or a process exit code alone as complete E2E proof.
