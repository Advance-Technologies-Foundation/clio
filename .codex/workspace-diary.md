## 2026-02-22 - Canonical command-doc instructions
Context: Align duplicated command documentation guidance used by Codex skills and GitHub Copilot prompts.
Decision: Introduce a single canonical instruction file and convert both agent-specific files into thin wrappers with Canonical-Source and Canonical-Version markers.
Discovery: A lightweight PowerShell verifier can enforce wrapper-to-canonical sync and prevent silent drift.
Files: docs/agent-instructions/document-command.md, .codex/skills/document-command/SKILL.md, .github/prompts/doc-command.prompt.md, scripts/verify-agent-docs.ps1, .codex/workspace-diary.md
Impact: Future updates to command documentation guidance can be made once in canonical docs and validated automatically for both agent surfaces.
## 2026-02-22 - Document verifier inline help
Context: Improve maintainability of agent-doc drift checker by documenting script purpose and parameters.
Decision: Add PowerShell comment-based help and concise inline comments in verify-agent-docs script.
Discovery: Script remains CI-friendly; exit behavior and validation output unchanged after documentation updates.
Files: scripts/verify-agent-docs.ps1, .codex/workspace-diary.md
Impact: Team members can discover usage via Get-Help and understand script intent without reading full implementation.
## 2026-02-22 - Add DI instance-creation policy
Context: Establish repository-wide guidance for service instantiation and dependency injection consistency.
Decision: Add AGENTS.md directive to resolve behavior classes from DI, require interfaces for behavior classes, and allow `new` only for simple DTO/value carriers (prefer records).
Discovery: Clear exceptions are needed to avoid over-constraining value objects and data-only models.
Files: AGENTS.md, .codex/workspace-diary.md
Impact: Reduces ad-hoc object creation, improves testability, and enforces consistent dependency management.
## 2026-02-22 - Add CLIO001 DI manual-construction analyzer
Context: Enforce repository policy to avoid `new` for DI-registered behavior classes.
Decision: Create `Clio.Analyzers` project with `CLIO001` analyzer, wire it as Analyzer reference in `clio` and `cliogate`, and add analyzer project to `clio.slnx`.
Discovery: Analyzer infers registered implementations from Autofac and Microsoft.Extensions.DependencyInjection registration calls, and intentionally skips record types and test assemblies.
Files: Clio.Analyzers/Clio.Analyzers.csproj, Clio.Analyzers/DependencyInjectionManualConstructionAnalyzer.cs, clio/clio.csproj, cliogate/cliogate.csproj, clio.slnx, .codex/workspace-diary.md
Impact: Builds now flag manual `new` construction of DI-registered types with diagnostic `CLIO001`.
## 2026-02-22 - Migrate clio DI and JSON stack
Context: Replace Autofac with Microsoft.Extensions.DependencyInjection and move off System.Json in clio and clio.tests.
Decision: Ported the existing remove-autofac migration with targeted reconciliation on current branch, then completed remaining DI conversion callsites manually.
Discovery: Keeping BuildServiceProvider validation with ValidateOnBuild=false avoids premature DI graph failures while preserving runtime resolution behavior; test fixture overrides map cleanly to IServiceCollection + GetRequiredService patterns.
Files: clio/BindingsModule.cs, clio/Program.cs, clio/Command/CloneEnvironmentCommand.cs, clio/Command/ShowDiffEnvironmentsCommand.cs, clio/Command/ScenarioRunnerCommand.cs, clio/AppUpdater.cs, clio/ComposableApplication/ComposableApplicationManager.cs, clio/Package/PackageConverter.cs, clio/ExceptionReadableMessageExtension.cs, clio/clio.csproj, clio.tests/Command/BaseClioModuleTests.cs, clio.tests/GlobalUsings.cs, clio.tests/clio.tests.csproj
Impact: clio now uses Microsoft DI container APIs end-to-end, System.Json dependency is removed, and full clio.tests suite passes on net8.
## 2026-02-22 - Retarget CLIO001 analyzer to Microsoft DI
Context: After migrating clio away from Autofac, analyzer registration detection still included Autofac methods/namespaces.
Decision: Restrict CLIO001 registration-source detection to Microsoft.Extensions.DependencyInjection APIs and add support for TryAdd*/Replace method names.
Discovery: Existing type extraction logic for Add* generic overloads still works for IServiceCollection patterns; no Autofac-specific extraction paths are needed.
Files: Clio.Analyzers/DependencyInjectionManualConstructionAnalyzer.cs, .codex/workspace-diary.md
Impact: CLIO001 now reflects the current DI stack and avoids Autofac-based assumptions/false positives.
## 2026-02-22 - Stabilize CLIO001 matches after MS DI migration
Context: Manual `new` in `Command/AddSchema.cs` was not reported even though CLIO001 analyzer assembly was loaded and executed.
Decision: Extended CLIO001 registration matching from symbol-only to symbol+name fallback, preserving registration discovery from Microsoft DI methods while handling cases where type binding is not symbol-resolvable.
Discovery: `dotnet build` with analyzer reporting confirmed analyzer execution; diagnostics were missed by symbol-only matching in this code path and restored by name fallback.
Files: Clio.Analyzers/DependencyInjectionManualConstructionAnalyzer.cs, .codex/workspace-diary.md
Impact: CLIO001 now reports manual construction in AddSchema and other DI-registered usages more reliably under current MS DI setup.
## 2026-02-22 - Validate CLIO001 on RfsEnvironment
Context: User requested validation of CLIO001 warnings in `Command/RfsEnvironment.cs` after analyzer retargeting to MS DI.
Decision: Classified warnings by exact object-creation site: outer `new Clio.Common.FileSystem(...)` is a true positive, inner `new System.IO.Abstractions.FileSystem()` is a false positive.
Discovery: Name-based fallback in CLIO001 can collide on short type names (`FileSystem`) across namespaces, producing duplicate warnings on the same line/statement.
Files: clio/Command/RfsEnvironment.cs, Clio.Analyzers/DependencyInjectionManualConstructionAnalyzer.cs, .codex/workspace-diary.md
Impact: Future analyzer tightening should prefer fully-qualified type matching (or short-name matching only for unbound symbols) to avoid namespace-collision false positives.
## 2026-02-22 - Tighten CLIO001 matching and validate RfsEnvironment warnings
Context: User asked to proceed with analyzer tightening and validate CLIO001 findings in `Command/RfsEnvironment.cs`.
Decision: Updated CLIO001 to prioritize symbol/FQN matching, limit simple-name fallback to unresolved simple syntax, and include anonymous-method factory extraction; also switched registration collection to non-cancelled pass for stability.
Discovery: `BindingsModule` aliases `FileSystem` to `System.IO.Abstractions.FileSystem` and registers it (`AddTransient<IFileSystem, FileSystem>`), so inner `new System.IO.Abstractions.FileSystem()` warnings in RfsEnvironment are semantically consistent with current analyzer policy.
Files: Clio.Analyzers/DependencyInjectionManualConstructionAnalyzer.cs, clio/BindingsModule.cs, clio/Command/RfsEnvironment.cs, .codex/workspace-diary.md
Impact: CLIO001 behavior is more deterministic, but RfsEnvironment still reports four warnings because both concrete `FileSystem` types are currently DI-registered/treated as services.
## 2026-02-22 - Refactor RfsEnvironment to DI-managed filesystem
Context: User requested removing manual filesystem construction in `RfsEnvironment` and consuming `Clio.Common.FileSystem` through DI.
Decision: Converted `RfsEnvironment` to an injectable service with constructor dependencies `IFileSystem` and `IPackageUtilities`; replaced static `new FileSystem(...)`/`new PackageUtilities(...)` with injected services; updated link commands and wiring to resolve through DI.
Discovery: `Link2RepoCommand` was created via `CreateCommand` and had to be switched to `Resolve` in `Program` so constructor injection works.
Files: clio/Command/RfsEnvironment.cs, clio/Command/Link2RepoCommand.cs, clio/Command/Link4RepoCommand.cs, clio/Program.cs, clio/BindingsModule.cs, .codex/workspace-diary.md
Impact: `RfsEnvironment` no longer manually constructs filesystem helpers, and CLIO001 warnings for `RfsEnvironment.cs` are eliminated.
## 2026-02-22 - Add target-typed new support to CLIO001
Context: Analyzer reported `new EnvironmentSettings()` but missed `new()` for the same type.
Decision: Extended CLIO001 to analyze both `ObjectCreationExpression` and `ImplicitObjectCreationExpression` and resolve created type from semantic model for both forms.
Discovery: Enabling implicit object creation analysis surfaced additional existing manual-construction sites (many previously hidden due target-typed syntax).
Files: Clio.Analyzers/DependencyInjectionManualConstructionAnalyzer.cs, .codex/workspace-diary.md
Impact: CLIO001 now consistently detects DI manual construction for explicit and target-typed `new`.
## 2026-02-22 - Add CLIO001 attention directive to AGENTS
Context: User requested stronger repository guidance to prioritize DI and treat manual instance creation as last resort.
Decision: Added a dedicated `CLIO001 handling` section in `AGENTS.md` emphasizing DI-first, last-resort `new` usage, and justification for suppressions.
Discovery: Existing DI policy was broad; explicit CLIO001 guidance reduces ambiguity during warning triage.
Files: AGENTS.md, .codex/workspace-diary.md
Impact: Future work should consistently treat CLIO001 warnings as actionable and avoid normalizing manual construction patterns.
## 2026-02-22 - Add CLIO002 analyzer for Console output
Context: User requested a new analyzer to discourage direct `Console` output and encourage `ConsoleLogger` usage.
Decision: Added `CLIO002` analyzer that reports `System.Console.Write`/`WriteLine` invocations, excluding `Clio.*.ConsoleLogger` implementation.
Discovery: Running build immediately surfaces many existing direct console writes across command and utility classes, which confirms analyzer coverage.
Files: Clio.Analyzers/ConsoleOutputAnalyzer.cs, .codex/workspace-diary.md
Impact: Repository now has automatic enforcement pressure toward `ILogger`/`ConsoleLogger` instead of direct console printing.
## 2026-02-22 - Introduce CLIO severity policy in .editorconfig
Context: User requested scalable analyzer governance without documenting every rule in AGENTS.
Decision: Added `clio/.editorconfig` with CLIO severity tiers and explicit per-ID settings (`CLIO001` warning, `CLIO002` suggestion), and updated AGENTS to make `.editorconfig` the source of truth.
Discovery: Numeric ID ranges cannot be expressed directly in .editorconfig, so each new CLIO rule must be added explicitly while preserving the tier convention.
Files: clio/.editorconfig, AGENTS.md, .codex/workspace-diary.md
Impact: Analyzer severity management is centralized and scalable as CLIO analyzer set grows.

## 2026-02-22 - CLIO002 cleanup in LoadPackagesToFileSystemCommand
Context: User requested addressing CLIO002 in LoadPackagesToFileSystemCommand.
Decision: Replaced direct Console usage with injected ILogger in the command and updated the direct-construction test.
Discovery: Only one unit test instantiated this command directly (TurnFsmCommand login-retry test) and required constructor update.
Files: clio/Command/LoadPackagesToFileSystemCommand.cs, clio.tests/Command/TurnFsmCommand.LoginRetry.Tests.cs
Impact: CLIO002 is removed from this command while keeping behavior and tests intact.

## 2026-02-22 - Refactor NugetPackagesProvider logging, DI HttpClient, and JSON parser
Context: User requested fixing CLIO002 warnings in NugetPackagesProvider and migrating its HTTP/JSON flow to DI + System.Text.Json.
Decision: Replaced direct Console output with ILogger, added constructor injection for HttpClient/ILogger, switched versions parsing from Newtonsoft JObject/JToken to System.Text.Json model binding, and registered typed client via AddHttpClient<INugetPackagesProvider, NugetPackagesProvider>.
Discovery: Registering plain HttpClient globally introduced broad CLIO001 fallout, so typed HttpClient registration was used to scope DI creation to INugetPackagesProvider.
Files: clio/Package/NuGet/NugetPackagesProvider.cs, clio/BindingsModule.cs, clio/clio.csproj, .codex/workspace-diary.md
Impact: The targeted CLIO002 warnings are resolved, NugetPackagesProvider now follows DI for HttpClient, and Newtonsoft dependency is removed from this provider path.

## 2026-02-22 - Add NugetPackagesProvider tests with DelegatingHandler and DI wiring
Context: User requested coverage for NugetPackagesProvider with mocked DI and DelegatingHandler-based HTTP responses.
Decision: Added a dedicated NUnit fixture that registers INugetPackagesProvider via AddHttpClient and injects a custom DelegatingHandler from DI to control responses; covered success, no-versions warning, and HTTP-error logging paths.
Discovery: Existing PackageVersion ordering treats "-rc" as greater than empty suffix, so Last resolves to rc in current implementation and tests were aligned to current behavior.
Files: clio.tests/Package/NuGet/NugetPackagesProvider.Tests.cs, clio/Package/NuGet/NugetPackagesProvider.cs, .codex/workspace-diary.md
Impact: NugetPackagesProvider behavior is now regression-protected and demonstrates project-preferred DI+HttpClient testing approach.

## 2026-02-22 - Add test style directives to AGENTS
Context: User requested stricter test authoring rules for consistency and readability.
Decision: Added a dedicated Test style policy section requiring explicit AAA structure, `because` in every assertion, and `[Description]` on every test method.
Discovery: Existing AGENTS guidance covered DI/analyzers/docs but did not define shared test-writing conventions.
Files: AGENTS.md, .codex/workspace-diary.md
Impact: Future tests should follow uniform structure and clearer intent documentation.

## 2026-02-22 - Align NugetPackagesProvider tests with AGENTS test policy
Context: User requested applying new test style directives to NugetPackagesProvider tests.
Decision: Updated tests to explicit AAA sections, added Description attribute to each test, and ensured all assertions include because; replaced NSubstitute Received/DidNotReceive checks with counted call assertions supporting because.
Discovery: Log verification via ReceivedCalls allows assertion-style checks that comply with mandatory because rule.
Files: clio.tests/Package/NuGet/NugetPackagesProvider.Tests.cs, .codex/workspace-diary.md
Impact: NugetPackagesProvider tests now comply with repository testing conventions and remain green.

## 2026-02-22 - Refactor NugetPackagesProvider tests to BaseClioModuleTests DI flow
Context: User requested resolving INugetPackagesProvider from shared test container and registering StubDelegatingHandler + provider in DI.
Decision: Reworked NugetPackagesProviderTests to inherit BaseClioModuleTests, override AdditionalRegistrations to add singleton logger/handler and typed AddHttpClient registration, and resolve provider from Container in each test.
Discovery: Per-test container recreation via BaseClioModuleTests keeps handler/logger state isolated while enabling DI-first test composition.
Files: clio.tests/Package/NuGet/NugetPackagesProvider.Tests.cs, .codex/workspace-diary.md
Impact: Tests now follow repository DI testing style and validate the actual resolution path used by BindingsModule-based setups.

## 2026-02-22 - Remove Task covariance in NuGet provider
Context: Address compiler warning about co-variant array conversion in NugetPackagesProvider.
Decision: Replaced Task.WaitAll(Task<T>[]) usage with Task.WhenAll(tasks).GetAwaiter().GetResult() to keep strong typing.
Discovery: Task.WaitAll requires Task[], which triggers array covariance from Task<T>[]; Task.WhenAll avoids that path.
Files: clio/Package/NuGet/NugetPackagesProvider.cs
Impact: Removes potential runtime array type mismatch risk and clears analyzer/compiler warning.

## 2026-02-22 - Convert NuGet provider API to async/await
Context: User requested replacing blocking .GetAwaiter().GetResult() in NugetPackagesProvider with async/await.
Decision: Changed INugetPackagesProvider methods to Task-based signatures and updated provider implementation to wait Task.WhenAll(tasks).
Discovery: Synchronous call sites in NuGet manager/restorer now bridge via .Result until broader async propagation is done.
Files: clio/Package/NuGet/INugetPackagesProvider.cs, clio/Package/NuGet/NugetPackagesProvider.cs, clio/Package/NuGet/NuGetManager.cs, clio/Package/NuGet/NugetPackageRestorer.cs, clio.tests/Package/NuGet/NugetPackagesProvider.Tests.cs
Impact: Provider no longer blocks with GetAwaiter().GetResult() and keeps async flow where version aggregation happens.

## 2026-02-22 - Replace Console writes in RfsEnvironment
Context: User requested resolving CLIO002 warnings in RfsEnvironment.cs.
Decision: Injected ILogger into RfsEnvironment and replaced Console.WriteLine calls with _logger.WriteLine.
Discovery: RfsEnvironment is already created via DI (BindingsModule), so constructor injection required no registration changes.
Files: clio/Command/RfsEnvironment.cs
Impact: Targeted CLIO002 warnings in RfsEnvironment are removed while preserving output behavior.

## 2026-02-22 - Remove Console output in LinkCoreSrcCommand confirmation flow
Context: User requested addressing CLIO002 warning(s) in LinkCoreSrcCommand.cs.
Decision: Replaced all Console.WriteLine/Console.Write calls in RequestUserConfirmation with _logger.WriteLine/_logger.Write while keeping Console.ReadLine for user input.
Discovery: LinkCoreSrcCommand already has injected ILogger, so no DI registration changes were needed.
Files: clio/Command/LinkCoreSrcCommand.cs
Impact: CLIO002 warnings for Console output in LinkCoreSrcCommand are removed.

## 2026-02-22 - Remove CLIO002 console output in WindowsFeatureManager and UploadLicenseCommand
Context: User requested addressing specific CLIO002 warnings in two command classes.
Decision: Replaced Console.Write* calls with injected ILogger writes; added ILogger dependency to UploadLicenseCommand constructor.
Discovery: WindowsFeatureManager already had _logger, so only write-call replacements were needed there.
Files: clio/Command/WindowsFeatureManager.cs, clio/Command/UploadLicenseCommand.cs
Impact: Targeted CLIO002 warnings for these files are removed while keeping behavior intact.

## 2026-02-22 - Remove Console output in SetFsmConfigCommand
Context: User requested fixing CLIO002 warning in SetFsmConfigCommand.cs.
Decision: Added ILogger dependency to SetFsmConfigCommand and replaced Console.WriteLine calls with _logger.WriteLine.
Discovery: File had two console writes (missing config + table output), both now routed through logger.
Files: clio/Command/SetFsmConfigCommand.cs
Impact: CLIO002 warnings in SetFsmConfigCommand are cleared.

## 2026-02-22 - Fix SetFsmConfigCommand tests after ILogger constructor change
Context: Tests failed to compile after SetFsmConfigCommand started requiring ILogger.
Decision: Updated SetFsmConfigCommand.Tests setup to create and pass a substitute ILogger into command constructor.
Discovery: The test file initially referenced the old 2-arg constructor only.
Files: clio.tests/Command/SetFsmConfigCommand.Tests.cs
Impact: clio.tests compiles again with the updated command dependency graph.

## 2026-02-22 - Fix TurnFsmCommandLoginRetryTests after SetFsmConfigCommand ctor change
Context: Targeted test Execute_RetriesLogin_AfterRestart failed after SetFsmConfigCommand gained ILogger constructor dependency.
Decision: Updated partial substitute construction in login-retry test to pass ILogger.
Discovery: Failure was Castle proxy constructor mismatch in Substitute.ForPartsOf<SetFsmConfigCommand>.
Files: clio.tests/Command/TurnFsmCommand.LoginRetry.Tests.cs
Impact: TurnFsmCommandLoginRetryTests.Execute_RetriesLogin_AfterRestart passes again.

## 2026-02-23 - Add CLIO003 direct System.IO usage analyzer with test harness
Context: User requested analyzer enforcement to discourage direct System.IO usage and guide code toward System.IO.Abstractions for testability.
Decision: Added CLIO003 analyzer that reports direct usage of core System.IO types (File, Directory, Path, FileInfo, DirectoryInfo, DriveInfo, FileStream), excludes test assemblies, and avoids invocation/member-access double-reporting for the same expression; implemented diagnostics-only iteration without code fix.
Discovery: In-memory analyzer tests must avoid assembly names containing "test" due intentional analyzer exclusion logic; using a neutral default assembly name restores expected coverage.
Files: Clio.Analyzers/DirectSystemIoUsageAnalyzer.cs, Clio.Analyzers.Tests/Clio.Analyzers.Tests.csproj, Clio.Analyzers.Tests/AnalyzerTestRunner.cs, Clio.Analyzers.Tests/DirectSystemIoUsageAnalyzerTests.cs, clio.slnx, .codex/workspace-diary.md
Impact: Repository now enforces filesystem abstraction policy via CLIO003 and has a dedicated analyzer test project to prevent regressions.

## 2026-02-23 - Add CLIO001 and CLIO002 analyzer test coverage
Context: User requested missing tests for DependencyInjectionManualConstructionAnalyzer (CLIO001) and ConsoleOutputAnalyzer (CLIO002).
Decision: Added dedicated NUnit test suites in Clio.Analyzers.Tests using existing in-memory Roslyn runner; covered positive and negative scenarios including test-assembly exclusions and analyzer-specific exception paths.
Discovery: CLIO001 behavior can be validated with minimal in-source Microsoft.Extensions.DependencyInjection stubs, avoiding external analyzer-testing harness complexity while exercising semantic registration detection.
Files: Clio.Analyzers.Tests/ConsoleOutputAnalyzerTests.cs, Clio.Analyzers.Tests/DependencyInjectionManualConstructionAnalyzerTests.cs, .codex/workspace-diary.md
Impact: Analyzer regressions for CLIO001/CLIO002 are now protected by automated tests alongside existing CLIO003 coverage.

## 2026-02-23 - Replace Console output in CreatioInstallerService for CLIO002
Context: User requested addressing CLIO002 warning(s) in CreatioInstallerService.
Decision: Replaced direct Console.Write/Console.WriteLine calls in CreatioInstallerService with injected ILogger (_logger.Write/_logger.WriteLine) while keeping Console.ReadLine input handling intact.
Discovery: deploy-creatio command contract/options were unchanged; command docs were reviewed and no updates were required.
Files: clio/Command/CreatioInstallCommand/CreatioInstallerService.cs, .codex/workspace-diary.md
Impact: CLIO002 no longer reports for CreatioInstallerService write calls, preserving user-visible messaging flow through logger abstraction.

## 2026-02-23 - Add XML docs for InstallerCommand public API
Context: User reported AGENTS.md C# inline documentation policy violation after public API changes in InstallerCommand.cs.
Decision: Added XML documentation comments for public types, constructor, Execute method, and all public options properties in PfInstallerOptions.
Discovery: InstallerCommand.cs had existing public API surface without XML docs; policy requires explicit /// documentation when changing public members.
Files: clio/Command/CreatioInstallCommand/InstallerCommand.cs, .codex/workspace-diary.md
Impact: Installer command public API now aligns with repository XML documentation requirements and avoids future ambiguity for CLI option semantics.

## 2026-02-23 - Add XML docs to CreatioInstallerService public API
Context: User requested making CreatioInstallerService compliant with AGENTS.md C# inline documentation policy.
Decision: Added interface-first XML documentation to ICreatioInstallerService and used <inheritdoc /> on implemented members in CreatioInstallerService; documented public constructors and non-interface public methods.
Discovery: Public API surface included ICreatioInstallerService methods, CreatioInstallerService constructors, CreateDeployDirectory, Execute override, and browser/build helper methods.
Files: clio/Command/CreatioInstallCommand/CreatioInstallerService.cs, .codex/workspace-diary.md
Impact: Creatio installer public API is now self-documented and aligned with repository XML doc standards without behavior changes.

## 2026-02-23 - Remove CLIO003 usage from CreatioInstallerService via abstraction-first path flow
Context: User requested implementing the approved plan to address CLIO003 warnings in CreatioInstallerService.
Decision: Switched CreatioInstallerService filesystem operations to System.IO.Abstractions + Clio filesystem abstractions, migrated DoPgWork contract to path-based signature, added InstallerHelper path-based helpers, and updated deployment strategy contract to accept app directory path.
Discovery: Full removal inside CreatioInstallerService required avoiding DirectoryInfo/FileInfo flow through strategy/helper boundaries; path-based contracts kept behavior while removing direct System.IO usage in the target file.
Files: clio/Command/CreatioInstallCommand/CreatioInstallerService.cs, clio/Command/RestoreDb.cs, clio/Command/InstallerHelper.cs, clio/Common/DeploymentStrategies/IDeploymentStrategy.cs, clio/Common/DeploymentStrategies/IISDeploymentStrategy.cs, clio/Common/DeploymentStrategies/DotNetDeploymentStrategy.cs, .codex/workspace-diary.md
Impact: dotnet build shows zero CLIO003 diagnostics for CreatioInstallerService.cs; deploy/restore flows remain functionally intact with abstraction-friendly contracts.

## 2026-02-23 - Expand ProcessExecutor with launch/capture/realtime APIs
Context: User requested ProcessExecutor capabilities for fire-and-forget launch, output capture, and realtime output streaming.
Decision: Reworked ProcessExecutor to add async APIs (`FireAndForgetAsync`, `ExecuteAndCaptureAsync`, `ExecuteWithRealtimeOutputAsync`) with structured options/results, while preserving the legacy `Execute(...)` compatibility path.
Discovery: Realtime callback behavior is reliably validated via stdout-focused command execution; platform-specific mixed stdout/stderr shell syntax can be flaky in tests.
Files: clio/Common/ProcessExecutor.cs, clio/Common/IProcessExecutor.cs, clio.tests/Common/ProcessExecutorTests.cs
Impact: Process execution now supports explicit operational modes with timeout/cancellation metadata, and new unit tests cover the three required behaviors.

## 2026-02-23 - Enforce cross-platform test executability and remove shell-specific ProcessExecutor test commands
Context: User required ProcessExecutor tests to run on macOS, Linux, and Windows without shell-specific dependencies.
Decision: Replaced ProcessExecutorTests command helpers with `dotnet`-based invocations (`dotnet --info` / `dotnet --version`) and added AGENTS.md rule requiring cross-platform test executability unless validating OS-specific behavior.
Discovery: Using direct `dotnet` commands avoids `cmd`/`bash` branching and provides stable output for capture/realtime assertions across platforms.
Files: clio.tests/Common/ProcessExecutorTests.cs, AGENTS.md, .codex/workspace-diary.md
Impact: ProcessExecutor tests now avoid platform shell assumptions and repository policy explicitly enforces cross-OS compatibility for future tests.

## 2026-02-23 - Replace direct Process usage in CreatioInstallerService with IProcessExecutor
Context: User requested removing direct Process usage and updating tests to rely on DI + IProcessExecutor.
Decision: Injected IProcessExecutor into CreatioInstallerService, replaced pg_restore invocation and browser launch logic to use process executor APIs, and extended ProcessExecutionOptions with environment-variable support for pg_restore password handling.
Discovery: Running parallel test commands can intermittently lock build outputs; sequential rerun validated the same test sets successfully.
Files: clio/Command/CreatioInstallCommand/CreatioInstallerService.cs, clio/Common/ProcessExecutor.cs, clio.tests/CreatioInstallerServiceTests.cs, .codex/workspace-diary.md
Impact: CreatioInstallerService no longer starts processes directly, and tests now verify browser launch behavior through DI-resolved IProcessExecutor.

## 2026-02-23 - Implement CLIO004 analyzer and validate process-executor migration scope
Context: User requested implementing the plan to discourage direct System.Diagnostics.Process usage and steer code to IProcessExecutor.
Decision: Added CLIO004 analyzer + tests, kept ProcessExecutor as the explicit low-level exception via scoped pragma with justification, and validated CreatioInstallerService no longer directly references Process APIs.
Discovery: Repository has many existing CLIO001/002/003/004 warnings outside the targeted scope; sequential build avoids transient file-lock errors seen during parallel build/test runs.
## 2026-03-19 00:00 – Fix l4r relative env package path on Unix
Context: User reported that `link-from-repository` treated a relative `--envPkgPath` as an environment name on macOS and failed with a Windows-only error.
Decision: Added `Link4RepoCommand.TryResolveDirectoryPath(...)` and switched `--envPkgPath` / environment path handling to recognize relative paths, rooted paths, file URIs, separator-containing values, and existing directories as direct filesystem paths.
Discovery: The previous `Uri.TryCreate(..., UriKind.Absolute)` check was too narrow for CLI path semantics; targeted unit tests pass after covering relative path resolution and plain environment-name fallback.
Files: clio/Command/Link4RepoCommand.cs, clio/Commands.md, clio.tests/Command/Link4RepoCommandTests.cs, .codex/workspace-diary.md
Impact: `clio l4r` now accepts relative package-directory paths on macOS/Linux instead of incorrectly requiring Windows environment-name resolution.
Files: Clio.Analyzers/DirectProcessUsageAnalyzer.cs, Clio.Analyzers.Tests/DirectProcessUsageAnalyzerTests.cs, clio/Common/ProcessExecutor.cs, clio/Command/CreatioInstallCommand/CreatioInstallerService.cs, clio.tests/CreatioInstallerServiceTests.cs, .codex/workspace-diary.md
Impact: CLIO004 policy is now enforced, targeted service migration is verified, and future cleanup can proceed incrementally from analyzer diagnostics.

## 2026-02-23 - Expand CLIO002 to detect Console.Out and Console.OutputEncoding usage
Context: User reported ConsoleOutputAnalyzer missed Console.Out-based async writes and output stream/encoding direct access.
Decision: Extended ConsoleOutputAnalyzer to inspect member access and invocation chains so it now reports CLIO002 for Console.Out, Console.OutputEncoding, and Console.Out.* invocations (e.g., WriteAsync), while preserving test-assembly and Clio.ConsoleLogger exclusions.
Discovery: Invocation-only matching on methods declared by System.Console misses Console.Out.WriteAsync because the method is declared on TextWriter.
Files: Clio.Analyzers/ConsoleOutputAnalyzer.cs, Clio.Analyzers.Tests/ConsoleOutputAnalyzerTests.cs, .codex/workspace-diary.md
Impact: CLIO002 now enforces logger-only output policy for additional real-world console patterns previously slipping through.

## 2026-03-19 00:00 – Push workspace backup investigation
Context: Investigated why `pushw --Safe true` still performs package backup before install on a development environment.
Decision: No code change; documented the actual call chain and current behavior.
Discovery: `PushWorkspaceCommand` always routes through `WorkspaceInstaller` to `BasePackageInstaller.InstallPackedPackage`, which unconditionally calls `CreateBackupPackage` before installation. The `--Safe` option is not consulted there; current `Safe` handling in `EnvironmentSettings.Fill` only prompts based on the stored environment setting `this.Safe`, not the command-line `options.Safe` value.
Files: clio/Command/PushWorkspaceCommand.cs, clio/Workspace/WorkspaceInstaller.cs, clio/Package/BasePackageInstaller.cs, clio/Environment/ConfigurationOptions.cs
Impact: Future fixes should make backup behavior explicit/configurable and should not rely on `--Safe` for install-time backup control.

## 2026-03-17 00:00 – Create release 8.0.2.28
Context: User requested running the release flow from the release prompt instructions.
Decision: Verified `gh` installation and auth, confirmed latest tag `8.0.2.27`, reused already-updated `AssemblyVersion` `8.0.2.28`, then created and pushed tag `8.0.2.28` and published the GitHub release with `gh release create`.
Discovery: `clio/clio.csproj` was already aligned to the next release version before tagging, so no project-file edit was required during the release run.
Files: clio/clio.csproj, .codex/workspace-diary.md
Impact: Release `8.0.2.28` is published and CI/CD can now build, test, and publish artifacts from the release tag.

## 2026-03-19 00:00 – Add explicit skip-backup option for push commands
Context: User requested a separate parameter to disable package backup for `pushw` and `push-pkg`, while preserving existing behavior when the parameter is omitted.
Decision: Added nullable `skip-backup` options on `push-workspace` and `push-pkg`, propagated them through workspace/package/application installer flows as `createBackup`, and kept backup enabled unless the caller explicitly passes `true`.
Discovery: `push-workspace` already has MCP coverage, so the tool contract, prompt guidance, unit tests, and MCP E2E schema assertions also needed alignment; `push-pkg` currently has no MCP artifact, so no MCP addition was required there.
Files: clio/Command/PushWorkspaceCommand.cs, clio/Command/PushPackageCommand.cs, clio/Workspace/IWorkspace.cs, clio/Workspace/Workspace.cs, clio/Workspace/WorkspaceInstaller.cs, clio/Package/BasePackageInstaller.cs, clio/Package/IPackageInstaller.cs, clio/Package/PackageInstaller.cs, clio/Package/IApplicationInstaller.cs, clio/Package/ApplicationInstaller.cs, clio/Command/McpServer/Tools/WorkspaceSyncTool.cs, clio/Command/McpServer/Prompts/WorkspaceSyncPrompt.cs, clio/Commands.md, clio/help/en/push-pkg.txt, clio.tests/Command/PushPkgCommand.Tests.cs, clio.tests/Command/McpServer/WorkspaceSyncToolTests.cs, clio.mcp.e2e/WorkspaceSyncToolE2ETests.cs, .codex/workspace-diary.md
Impact: Dev and CI callers can now opt out of backup explicitly without changing the default install safety behavior for existing commands and integrations.

## 2026-03-19 00:00 – Create release 8.0.2.31
Context: User invoked the release prompt workflow and asked to follow the repository release instructions.
Decision: Verified `gh` installation/auth first, resolved latest tag `8.0.2.30`, updated `AssemblyVersion` in `clio/clio.csproj` to `8.0.2.31`, then created and pushed tag `8.0.2.31` and published the GitHub release via `gh release create`.
Discovery: The repository release scripts follow the same tag-first flow without an intermediate version-bump commit, so the run was kept consistent with the existing project practice.
Files: clio/clio.csproj, .github/prompts/release.prompt.md, create-release.sh, create-release.ps1, .codex/workspace-diary.md
Impact: Release `8.0.2.31` is published and the CI/CD workflow can now build, test, and publish artifacts from the new release tag.

## 2026-03-23 12:00 – Restore compile-package documentation surfaces
Context: User reported that `compile-package` was missing from `clio help` documentation and asked to ensure command-specific docs were available too.
Decision: Added the missing local help entry to `clio/help/en/help.txt`, created dedicated `clio/help/en/compile-package.txt` and `clio/docs/commands/compile-package.md`, and refreshed the `compile-package` section in `clio/Commands.md`.
Discovery: The command implementation already existed and `Commands.md` already had a basic section, but both the command-specific help file and detailed markdown doc were missing entirely.
Files: clio/help/en/help.txt, clio/help/en/compile-package.txt, clio/docs/commands/compile-package.md, clio/Commands.md, .codex/workspace-diary.md
Impact: `compile-package` now appears in general help, `clio compile-package -H` resolves locally, and existing links to `compile-package.md` now point to a real document.

## 2026-03-23 12:30 – Backfill local help coverage for Commands.md entries
Context: User asked to add local help for command entries documented in `clio/Commands.md` but missing in `clio/help/en`.
Decision: Added concise local help files for all previously uncovered command headings from `Commands.md`, including legacy/alias headings such as `ver`, `extract-package`, `lic`, `callservice`, and `create-manifest`.
Discovery: Most gaps were not missing source commands, but missing local help surfaces or legacy heading mismatches between `Commands.md` and canonical `[Verb]` names.
Files: clio/help/en/*.txt, .codex/workspace-diary.md
Impact: GitHub-documented command headings now resolve to local help files, improving `clio <command> -H` coverage without changing command behavior.

## 2026-03-23 13:00 – Expand backfilled local help pages to full format
Context: User chose the next step of turning the newly backfilled local help pages into more useful command help.
Decision: Expanded the new `clio/help/en/*.txt` pages with structured `OPTIONS`, `EXAMPLES`, and `SEE ALSO` sections, using canonical command contracts where available and alias/legacy redirect wording where the Commands.md heading differs from the actual verb name.
Discovery: Alias pages such as `ver`, `download-app`, `publish-workspace`, `lic`, and `run-scenario` benefit from local explanatory pages even when the runtime may also resolve through the canonical command help.
Files: clio/help/en/*.txt, .codex/workspace-diary.md
Impact: The backfilled local help set is now substantially more usable than bare placeholder pages while preserving existing command behavior.

## 2026-03-23 13:20 – Backfill docs/commands for the new local-help command set
Context: User selected the next step of adding GitHub markdown documentation for the same command set previously backfilled in local help.
Decision: Added markdown pages under `clio/docs/commands` for the previously uncovered package, application, workspace, manifest, infrastructure, alias, and legacy-heading commands, using concise standalone docs or redirect-style pages to canonical commands where appropriate.
Discovery: A large share of the missing GitHub docs surface was caused by filename mismatches and legacy headings in `Commands.md`, not by missing source commands.
Files: clio/docs/commands/*.md, .codex/workspace-diary.md
Impact: The specific command set previously missing local help now also has matching GitHub markdown documentation pages.

## 2026-02-23 - Remove CLIO004 ProcessStartInfo usage in RestoreDb
Context: User requested addressing CLIO004 warning for direct ProcessStartInfo usage in RestoreDb.cs.
Decision: Injected IProcessExecutor into RestoreDbCommand and replaced ExecutePgRestoreCommand low-level Process/ProcessStartInfo logic with ProcessExecutionOptions + ExecuteWithRealtimeOutputAsync, preserving debug and throttled progress logging behavior.
Discovery: Constructor-based tests for RestoreDb required DI argument updates to include IProcessExecutor substitute.
Files: clio/Command/RestoreDb.cs, clio.tests/Command/RestoreDb.LocalServer.Tests.cs, clio.tests/Command/RestoreDb.Tests.cs, .codex/workspace-diary.md
Impact: RestoreDb no longer triggers CLIO004 in this file and pg_restore execution now follows shared process abstraction.

## 2026-03-23 00:00 – Document prod Docker cache strategy guidance
Context: User asked whether the prod Dockerfile can "export everything up to line 19" to speed up image rebuilds.
Decision: Advised that Docker already caches lines 1-19 as stable layers when the daemon cache is preserved, and recommended either a named base stage / separately tagged intermediate image or BuildKit cache export/import for environments with ephemeral caches.
Discovery: `BuildDockerImageService.CreateBuildContext` always copies the template and `source/` into a temporary context, so repository-root `.dockerignore` rules do not control that staged context; the main invalidation point in the template is `COPY source/ ./`, not the apt/entrypoint layers.
Files: C:\Projects\clio\clio\tpl\docker-templates\prod\Dockerfile, C:\Projects\clio\clio\Command\BuildDockerImageService.cs, C:\Projects\clio\.codex\workspace-diary.md
Impact: Future Docker build-performance questions can start from the correct cache boundary in the prod template and distinguish between local layer reuse versus exported remote/intermediate cache strategies.

## 2026-03-23 16:45 – Reuse creatio-base stage in dev Docker template
Context: User requested aligning the dev Docker image with the prod cache strategy by reusing a shared `creatio-base` layer.
Decision: Split the dev Dockerfile into a `creatio-base` stage for the common .NET SDK and base OS dependencies, then kept SSH, code-server, and the global `clio` tool in the dev-only final stage.
Discovery: The dev template still carried the older fragile XML `sed` expression; switching it to the same delimiter-safe form as prod removed a latent build failure while preserving behavior.
Files: C:\Projects\clio\clio\tpl\docker-templates\dev\Dockerfile, C:\Projects\clio\.codex\workspace-diary.md
Impact: Dev and prod templates now share the same cached foundation, which reduces duplicated base setup work and keeps future dependency changes easier to reason about.

## 2026-03-23 18:15 – Add deep-link environment registration with login/password
Context: User needed a generic external-link flow to register a new environment with URI, login, password, runtime flags, and environment path.
Decision: Added a new `clio://RegisterEnvironment` handler that parses the requested query arguments and forwards them into the existing `RegAppCommand`/`RegAppOptions` flow instead of creating a separate registration path.
Discovery: `RegisterOAuthCredentials` was the only existing deep-link registration flow; `RegAppOptions` already supported `Uri`, `Login`, `Password`, `IsNetCore`, `Safe`, and `EnvironmentPath`, so the missing piece was only a generic external-link handler plus validator/test coverage. When no explicit environment name is supplied, deriving it from the URI host and port gives a stable default.
Files: C:\Projects\clio\clio\Requests\RegisterEnvironment.cs, C:\Projects\clio\clio.tests\Requests\RegisterEnvironmentHandlerTests.cs, C:\Projects\clio\clio.tests\Validators\ExternalLinkOptionsValidator.Tests.cs, C:\Projects\clio\.codex\workspace-diary.md
Impact: Future tooling can register local or password-based environments through a single deep link without relying on the OAuth-only registration path.

## 2026-03-23 18:35 – Add deep-link environment unregister by name
Context: User requested a matching external-link flow to remove a registered environment by its name.
Decision: Added `clio://UnregisterEnvironment?name=...` and mapped it directly to the existing `UnregAppCommand` so environment removal reuses the current repository deletion behavior instead of introducing a second unregister implementation.
Discovery: `ExternalLink` validation only needed the new request type name to exist plus a valid query shape; the simplest reliable test for the handler is asserting the underlying `ISettingsRepository.RemoveEnvironment(name)` side effect through the real `UnregAppCommand`.
Files: C:\Projects\clio\clio\Requests\UnregisterEnvironment.cs, C:\Projects\clio\clio.tests\Requests\UnregisterEnvironmentHandlerTests.cs, C:\Projects\clio\clio.tests\Validators\ExternalLinkOptionsValidator.Tests.cs, C:\Projects\clio\.codex\workspace-diary.md
Impact: Future tooling can both add and remove environments through deep links without shelling out to separate explicit CLI commands.

## 2026-03-23 18:50 – Fix MediatR container validation after adding unregister deep link
Context: After adding `UnregisterEnvironmentHandler`, unrelated tests such as `SetApplicationIconCommand_CallsComposableAppmanager` started failing during test fixture setup.
Decision: Registered `UnregAppCommand` in the main DI container so MediatR-discovered request handlers depending on it can be constructed during service-provider validation.
Discovery: The failures were not specific to the application-icon command; `BindingsModule` scans the whole assembly for MediatR handlers, and the new `UnregisterEnvironmentHandler` introduced a constructor dependency on `UnregAppCommand` that was never previously registered. Any test that built the full container failed before reaching its own assertions.
Files: C:\Projects\clio\clio\BindingsModule.cs, C:\Projects\clio\.codex\workspace-diary.md
Impact: Full-container tests are stable again, and future MediatR handler additions that depend on commands need corresponding DI registrations or interface-based dependencies.

## 2026-03-09 16:58 – Add clear-redis success log assertion and invalid-environment E2E coverage
Context: User asked to align the clear-redis MCP E2E tests with the new tool-name constants, assert that successful output contains an `Info` message type, and add a negative case for an invalid environment name.
Decision: Updated the clear-redis E2E fixture to use `ClearRedisTool.ClearRedisByEnvironmentName` as the tool tag/name, added a success-path assertion for `LogDecoratorType.Info`, added an invalid-environment test that asserts failure plus no Redis mutation, and replaced brittle direct JSON deserialization with a tolerant execution parser that can read structured payloads, content blocks, and top-level MCP error responses.
Discovery: On this MCP surface, successful tool calls may expose execution details via text content rather than directly deserializable structured content, and invalid environment lookups can surface as a generic MCP invocation error (`An error occurred invoking 'clear-redis-by-environment'.`) instead of the raw command diagnostic.
Files: clio.mcp.e2e/ClearRedisToolE2ETests.cs, clio.mcp.e2e/AGENTS.md, .codex/workspace-diary.md
Impact: The clear-redis E2E suite now covers both the happy path and a core failure mode while matching the real MCP wire contract closely enough to keep the Allure report stable and useful.

## 2026-03-09 17:08 – Add repository skills for creating and testing MCP tools
Context: User requested two local Codex skills that explain how to create MCP tools and how to test them in this repository.
Decision: Added `create-mcp-tool` and `test-mcp-tool` under `.codex/skills`, using the skill scaffolding workflow and concise repo-specific instructions that require production tool-name constants, full optional-argument coverage, and `Info`/`Error` message-type assertions in tests.
Discovery: The repo-local skill set was small enough that self-contained `SKILL.md` guidance was sufficient; no extra references or scripts were needed beyond the standard generated `agents/openai.yaml`.
Files: .codex/skills/create-mcp-tool/SKILL.md, .codex/skills/create-mcp-tool/agents/openai.yaml, .codex/skills/test-mcp-tool/SKILL.md, .codex/skills/test-mcp-tool/agents/openai.yaml, .codex/workspace-diary.md
Impact: Future MCP work can now be guided by explicit reusable instructions for both tool implementation and test coverage standards.

## 2026-03-09 17:12 – Wire MCP skills into repository AGENTS guidance
Context: User asked whether AGENTS guidance needed to be updated so the new MCP skills would be invoked consistently.
Decision: Updated the root `AGENTS.md` to explicitly require `create-mcp-tool` for MCP implementation work and `test-mcp-tool` for MCP testing work, and added local reminders in the MCP source and E2E test folders.
Discovery: The repo already had an explicit skill-trigger section for command documentation, so extending that same pattern was the lowest-friction way to make MCP skill usage deterministic for future agents.
Files: AGENTS.md, clio/Command/McpServer/AGENTS.md, clio.mcp.e2e/AGENTS.md, .codex/workspace-diary.md
Impact: Future Codex runs no longer need to rely on implicit skill-description matching for MCP work; the repository instructions now call out the MCP skills directly.

## 2026-03-09 19:02 – Add create-workspace MCP tool and workspaces-root fallback
Context: User requested an MCP surface for `clio create-workspace` that creates empty local workspaces, plus command support for an explicit base directory or a global configured workspaces root.
Decision: Added `Settings.workspaces-root`, extended `create-workspace --empty` with `--directory`, kept the MCP tool thin by mapping directly to command options, and covered the feature with command unit tests, MCP mapping tests, and MCP E2E tests. The MCP prompt was given a distinct name and the E2E client was aligned to the scalar MCP argument name `workspaceName`.
Discovery: `WorkspacePathes` belongs to environment settings and is not the right source for global empty-workspace creation; the real CLI path works with `--directory`, while the MCP client binds scalar tool arguments by C# parameter name rather than kebab-case.
Files: clio/Environment/ConfigurationOptions.cs, clio/Environment/ISettingsRepository.cs, clio/Command/CreateWorkspaceCommand.cs, clio/Command/McpServer/Tools/CreateWorkspaceTool.cs, clio/Command/McpServer/Prompts/CreateWorkspacePrompt.cs, clio/help/en/create-workspace.txt, clio/docs/commands/create-workspace.md, clio/Commands.md, clio.tests/Command/CreateWorkspaceCommand.Tests.cs, clio.tests/Command/McpServer/CreateWorkspaceToolTests.cs, clio.mcp.e2e/CreateWorkspaceToolE2ETests.cs, clio.mcp.e2e/Support/Results/McpCommandExecutionParser.cs, spec/create-workspace-mcp/plan.md, .codex/workspace-diary.md
Impact: Future MCP work for local filesystem commands can follow a proven pattern: keep command path resolution in the command layer, use explicit tool-name constants, and validate E2E argument names against the real MCP binding behavior.

## 2026-03-09 19:18 – Replace create-workspace path calls with filesystem abstraction
Context: User asked to address CLIO003 warnings in `CreateWorkspaceCommand` and add XML documentation for `ISettingsRepository`.
Decision: Extended `IFileSystem` with the minimal path operations needed by `CreateWorkspaceCommand` (`CombinePaths`, `GetFullPath`, `IsPathRooted`, `DirectorySeparatorChar`), switched the command to use those members instead of `System.IO.Path`, and documented the full `ISettingsRepository` interface with XML comments.
Discovery: The command tests needed explicit substitute behavior for the new path members because the workspace command now relies on the filesystem abstraction for rooted-path validation and normalization.
Files: clio/Common/IFileSystem.cs, clio/Common/FileSystem.cs, clio/Command/CreateWorkspaceCommand.cs, clio/Environment/ISettingsRepository.cs, clio.tests/Command/CreateWorkspaceCommand.Tests.cs, .codex/workspace-diary.md
Impact: `CreateWorkspaceCommand` now follows the repository’s filesystem-abstraction rule more closely, and the settings repository contract is clearer for future command and MCP work.

## 2026-03-09 12:21 – Fix create-workspace omitted-directory E2E settings override
Context: User noticed the `create-workspace` MCP E2E fixture covered the explicit `directory` path but the omitted-directory fallback path was failing after a new test was added.
Decision: Kept the omitted-directory E2E scenario and fixed the temporary settings override helper to edit clio's real `creatio\\clio\\appsettings.json` location based on the clio assembly metadata instead of deriving the path from the test host process.
Discovery: `SettingsRepository.AppSettingsFolderPath` depends on `Assembly.GetEntryAssembly()`, so using `new SettingsRepository()` inside the E2E test process pointed at the test host's settings location rather than the `clio mcp-server` child process location.
Files: clio.mcp.e2e/CreateWorkspaceToolE2ETests.cs, clio.mcp.e2e/Support/Configuration/TemporaryClioSettingsOverride.cs, .codex/workspace-diary.md
Impact: Future E2E tests that temporarily override clio global settings can target the same settings file the spawned `clio` process actually reads, which makes fallback-configuration scenarios reliable.

## 2026-03-09 12:30 – Report create-workspace destination path
Context: User requested `create-workspace` to report the directory where the workspace was created and asked for related unit and E2E coverage.
Decision: Updated `CreateWorkspaceCommand` to emit an explicit `Workspace created at: <full-path>` info message for both empty-workspace and environment-backed flows, extended command unit tests to assert the new message, and extended MCP E2E assertions to verify the path is surfaced through tool output.
Discovery: The `create-workspace` MCP tool contract did not need to change because the new behavior is purely command output; MCP reviewed, no update required beyond tests.
Files: clio/Command/CreateWorkspaceCommand.cs, clio.tests/Command/CreateWorkspaceCommand.Tests.cs, clio.mcp.e2e/CreateWorkspaceToolE2ETests.cs, clio/help/en/create-workspace.txt, clio/docs/commands/create-workspace.md, clio/Commands.md, .codex/workspace-diary.md
Impact: Users and MCP consumers now get the created workspace path directly in command output, which improves usability and gives tests a stable user-facing success signal to assert.

## 2026-03-09 12:47 – Add workspaces-root to generated settings schema
Context: User noticed the generated `appsettings.json` schema template still did not expose the new global `workspaces-root` setting.
Decision: Added `workspaces-root` to the JSON schema template with a description aligned to `create-workspace --empty` fallback behavior.
Discovery: The runtime `Settings` model and docs had already been updated, but `clio/tpl/jsonschema/schema.json.tpl` was still missing the corresponding property, so schema-driven editors would not suggest it.
Files: clio/tpl/jsonschema/schema.json.tpl, .codex/workspace-diary.md
Impact: Generated schema consumers now see `workspaces-root` as a first-class supported setting in `appsettings.json`.

## 2026-03-09 20:05 – Identify Windows register tool packaging mismatch
Context: User reported `clio register` failing on Windows with `DirectoryNotFoundException` for `.dotnet\tools\.store\...\tools\net8.0\any\img`.
Decision: Traced the failure to `RegisterCommand` assuming an `img` folder exists under `AppContext.BaseDirectory`, then verified the built `clio.8.0.2.16.nupkg` packs `reg` assets but not `img`.
Discovery: `clio/Command/RegisterCommand.cs` unconditionally calls `imgFolder.GetFiles()` on `<base>/img`, while `clio/clio.csproj` includes `reg`, `tpl`, `cliogate`, `Wiki`, and `help` as content but omits `img`; existing register tests manually add `img` to the mock filesystem, so they miss the packaged-tool layout.
Files: clio/Command/RegisterCommand.cs, clio/clio.csproj, clio.tests/Command/RegisterCommand.Tests.cs, .codex/workspace-diary.md
Impact: Future fixes should either pack `img` with the tool or make `register` tolerate a missing icon directory, and add a regression test that reflects the real .NET tool installation structure.

## 2026-03-09 21:02 – Expose link-from-repository as MCP tool
Context: User requested MCP coverage for the `link-from-repository` command alias `l4r`, mirroring both CLI target modes.
Decision: Added a thin MCP tool with separate methods for registered-environment and direct-environment-package-path modes, paired prompt helpers, and mapping-focused unit coverage without E2E because the command creates filesystem symlinks.
Discovery: `Link4RepoCommand` already owns both target-resolution paths, so the MCP adapter does not need `IToolCommandResolver`; isolated test output paths remain necessary in this repo because the default `clio.exe` debug output can be locked by a running process.
Files: clio/Command/McpServer/Tools/LinkFromRepositoryTool.cs, clio/Command/McpServer/Prompts/LinkFromRepositoryPrompt.cs, clio.tests/Command/McpServer/LinkFromRepositoryToolTests.cs, .codex/workspace-diary.md
Impact: MCP consumers can now invoke `l4r` safely through explicit mode-specific tools while future maintainers have a tested pattern for local filesystem-linking MCP adapters that should stay unit-tested only unless a symlink-safe sandbox is introduced.

## 2026-03-09 21:15 – Make MCP E2E coverage mandatory in repo guidance
Context: User requested that new or updated MCP tools always require end-to-end coverage by default, even when not explicitly mentioned in the task.
Decision: Tightened root AGENTS guidance, MCP-local AGENTS guidance, the E2E project guidance, and both MCP skills so MCP work now requires `clio.mcp.e2e` coverage as a standard deliverable rather than an optional follow-up.
Discovery: The prior guidance strongly encouraged E2E for MCP work but still left room to stop at unit mapping tests; making the requirement explicit in both repo policy and skills is the lowest-friction way to change future agent behavior.
Files: AGENTS.md, clio/Command/McpServer/AGENTS.md, clio.mcp.e2e/AGENTS.md, .codex/skills/create-mcp-tool/SKILL.md, .codex/skills/test-mcp-tool/SKILL.md, .codex/workspace-diary.md
Impact: Future MCP tool tasks should now include real `clio mcp-server` end-to-end coverage automatically, and agents should extend the harness instead of treating E2E as optional.

## 2026-03-23 19:10 – Create release 8.0.2.36
Context: User confirmed the `/release` flow for the current repository state.
Decision: Verified `gh` installation and authentication, took latest tag `8.0.2.35`, updated `AssemblyVersion` in `clio/clio.csproj` to `8.0.2.36`, then created and pushed tag `8.0.2.36` and published the GitHub release with `gh release create`.
Discovery: The repository release scripts still use the tag-based release flow without an intermediate version-bump commit, so the run stayed aligned with existing project practice.
Files: clio/clio.csproj, .codex/workspace-diary.md
Impact: Release `8.0.2.36` is published and CI/CD can now build, test, and publish artifacts from the new release tag.

## 2026-03-09 21:29 – Add link-from-repository MCP E2E coverage
Context: After exposing `link-from-repository` as an MCP tool, the user required real end-to-end coverage instead of unit mapping tests only.
Decision: Added a dedicated `clio.mcp.e2e` fixture covering direct-path success plus failure paths for both MCP methods, and ran it against the isolated `clio.dll` build via `McpE2E__ClioProcessPath` to avoid the locked default debug executable.
Discovery: A stable direct-path success case only needs a temporary repository with `packages/<PackageName>` and a temporary Creatio `Pkg` folder; on machines without directory-symlink capability, the success scenario should be skipped explicitly before invoking MCP.
Files: clio.mcp.e2e/LinkFromRepositoryToolE2ETests.cs, .codex/workspace-diary.md
Impact: The `l4r` MCP surface now has real stdio/server coverage, including symbolic-link side effects and failure diagnostics, and future local-filesystem MCP tests can reuse the same isolated-process-path approach.

## 2026-03-08 14:55 – RemoteEntitySchemaCreator parsing review
Context: User requested a correctness/regression review of RemoteEntitySchemaCreator with focus on ParseColumns and column parsing.
Decision: Reviewed implementation, command options/docs, and existing RemoteEntitySchemaCreator tests; validated baseline by running targeted tests.
Discovery: Existing tests cover happy-path and missing lookup reference schema but do not cover duplicate columns, non-lookup extra segment handling, or colon-containing column tokens.
Files: clio/Command/EntitySchemaDesigner/RemoteEntitySchemaCreator.cs, clio/Command/CreateEntitySchemaCommand.cs, clio.tests/Command/RemoteEntitySchemaCreatorTests.cs, clio/docs/commands/create-entity-schema.md, .codex/workspace-diary.md
Impact: Future fixes can prioritize parser strictness and duplicate-name validation with focused regression tests.

## 2026-03-09 10:45 – Add clear-redis-by-credentials MCP coverage
Context: User requested MCP test coverage for the `clear-redis-by-credentials` tool using the new repository MCP skills.
Decision: Added unit coverage for credentials argument mapping including the optional `isNetCore` combinations, extended the E2E sandbox resolver to read URL/login/password/IsNetCore from the real registered `clio` settings plus `ConnectionStrings.config`, and added successful and negative credentials-path E2E scenarios.
Discovery: On this machine the registered `clio` settings file contains the credentials needed for sandbox MCP tests, while the most reliable negative runtime path for `clear-redis-by-credentials` is an invalid URL rather than invalid credentials; successful runs emit `Info` messages and failed runs emit `Error` messages that should be asserted explicitly.
Files: clio.tests/Command/McpServer/ClearRedisToolTests.cs, clio.mcp.e2e/ClearRedisToolE2ETests.cs, clio.mcp.e2e/Support/Configuration/RegisteredClioEnvironmentSettingsResolver.cs, clio.mcp.e2e/Support/Configuration/SandboxEnvironmentContext.cs, clio.mcp.e2e/Support/Configuration/SandboxEnvironmentResolver.cs, clio/Command/McpServer/Prompts/ClearRedisPrompt.cs, clio.mcp.e2e/AGENTS.md, .codex/workspace-diary.md
Impact: MCP work on `clear-redis` now has both mapping-level and end-to-end credentials coverage, plus explicit guidance for future tools around optional-argument combinations and success/error message assertions.

## 2026-03-09 10:55 – Standardize command-based Allure features and manual E2E flow
Context: User requested that MCP E2E fixtures use the underlying `clio` command name as the Allure feature when possible and that the project guidance document the manual test execution flow.
Decision: Kept the clear-redis fixture on `[AllureFeature("clear-redis-db")]` and updated the E2E AGENTS guidance to prefer command-based feature names, require Allure Report 3, and describe the repository `run-e2e-tests.ps1` workflow.
Discovery: The local PowerShell runner assumes Allure CLI is already installed and available on `PATH`, using `allure generate` and `allure serve` directly after `dotnet test`.
Files: clio.mcp.e2e/AGENTS.md, clio.mcp.e2e/ClearRedisToolE2ETests.cs, .codex/workspace-diary.md
Impact: Future MCP E2E tests should produce more consistent Allure reporting and clearer local execution guidance.

## 2026-03-09 20:13 – Security/correctness review for LinkFromRepository MCP tool
Context: User requested a focused review of the newly added link-from-repository / l4r MCP tool, prompt, and unit tests.
Decision: Reviewed added MCP files against command execution flow (Link4RepoCommand and RfsEnvironment) to confirm security and behavioral alignment.
Discovery: The link-from-repository-by-environment tool currently allows absolute file-path input through environmentName, which reaches destructive filesystem operations; tool metadata also marks both methods as non-destructive despite delete-and-symlink behavior.
Files: clio/Command/McpServer/Tools/LinkFromRepositoryTool.cs, clio/Command/McpServer/Prompts/LinkFromRepositoryPrompt.cs, clio.tests/Command/McpServer/LinkFromRepositoryToolTests.cs, clio/Command/Link4RepoCommand.cs, clio/Command/RfsEnvironment.cs, .codex/workspace-diary.md
Impact: Future MCP hardening work should enforce strict environment-key validation on the environment-mode tool path and correct MCP destructive metadata so clients can apply appropriate safety gating.


## 2026-03-09 20:15 – Review LinkFromRepository MCP tool correctness/performance
Context: User requested a targeted review of the new LinkFromRepository MCP tool, prompt, and unit tests for correctness/performance concerns.
Decision: Reviewed the new MCP files against Link4RepoCommand behavior and executed targeted Release tests for LinkFromRepositoryToolTests to validate baseline mapping behavior.
Discovery: The tool currently executes a startup-resolved Link4RepoCommand instance directly, while that command contains mutable execution state; the prompt and tests also do not cover platform restrictions in environment-name flow.
Files: clio/Command/McpServer/Tools/LinkFromRepositoryTool.cs, clio/Command/McpServer/Prompts/LinkFromRepositoryPrompt.cs, clio.tests/Command/McpServer/LinkFromRepositoryToolTests.cs, clio/Command/Link4RepoCommand.cs, .codex/workspace-diary.md
Impact: Future MCP updates for link-from-repository should enforce mode/platform constraints and prefer isolated command execution to avoid cross-request correctness risks.


## 2026-03-09 20:17 – Review LinkFromRepository MCP additions
Context: User requested a focused code-quality/correctness review of the new `link-from-repository` MCP tool, prompt, and unit tests.
Decision: Validated added tests in `Release` mode (`dotnet test ... --filter FullyQualifiedName~LinkFromRepositoryToolTests`) and reproduced command behavior for relative `envPkgPath` with `dotnet run -c Release --no-build -- link-from-repository --envPkgPath RelativePkgPath --repoPath C:\Repo --packages PkgA`.
Discovery: The new MCP by-path contract and prompt describe `envPkgPath` as a path but do not constrain it to an absolute path, while command execution falls back to environment-name resolution for non-file values; current unit tests use a fake command that always returns success, so this mismatch is not detected.
Files: clio/Command/McpServer/Tools/LinkFromRepositoryTool.cs, clio/Command/McpServer/Prompts/LinkFromRepositoryPrompt.cs, clio.tests/Command/McpServer/LinkFromRepositoryToolTests.cs, clio/Command/Link4RepoCommand.cs, .codex/workspace-diary.md
Impact: Future MCP/tool tests should include at least one real-failure path assertion for `link-from-repository` (or a high-fidelity fake) so parameter-contract regressions are caught before release.

## 2026-03-09 21:43 – Expand l4r MCP E2E package selection coverage
Context: User requested additional `link-from-repository` MCP E2E tests for comma-separated package lists and `*` wildcard package selection.
Decision: Extended the existing direct-path E2E fixture with one test covering `PkgA,PkgB` and another covering `*`, and generalized the symlink assertion helpers to validate per-package results.
Discovery: Verifying these flows reliably required seeding two repository packages and two target package folders in the temporary Creatio package path before invoking the real `clio mcp-server` process.
Files: clio.mcp.e2e/LinkFromRepositoryToolE2ETests.cs, .codex/workspace-diary.md
Impact: The `l4r` MCP tool now has process-level coverage for the main package-selection modes users are expected to invoke through MCP.

## 2026-03-09 21:58 – Mark link-from-repository MCP tools as destructive
Context: User reported that the new link-from-repository MCP endpoints were annotated as non-destructive even though the command deletes existing package directories before replacing them with symbolic links.
Decision: Set both LinkFromRepository MCP methods to `Destructive = true`, added a unit test that reflects the attribute contract, and added an MCP E2E discovery assertion that checks the destructive hint from the real server.
Discovery: The E2E project default `appsettings.json` points at a Debug `clio.exe`, so validating newly changed MCP metadata locally required the existing `McpE2E__ClioProcessPath` override to target the freshly built Release binary.
Files: clio/Command/McpServer/Tools/LinkFromRepositoryTool.cs, clio.tests/Command/McpServer/LinkFromRepositoryToolTests.cs, clio.mcp.e2e/LinkFromRepositoryToolE2ETests.cs, .codex/workspace-diary.md
Impact: MCP clients can now apply destructive-operation guardrails consistently for both link-from-repository invocation modes, and future regressions in discovery metadata are covered at both unit and server levels.

## 2026-03-09 21:25 – Add aggregate assert-infrastructure MCP tool
Context: User requested one MCP call that runs the full infrastructure assert sweep and returns complete machine-readable results for Kubernetes, local infrastructure, filesystem, and deployment database candidates.
Decision: Added an MCP-only aggregation service and structured result contract, exposed it through a dedicated `assert-infrastructure` tool and prompt, introduced focused assertion interfaces for testable orchestration, and added both unit coverage and real `clio mcp-server` E2E coverage.
Discovery: The ModelContextProtocol server advertises non-`BaseTool` classes only when they carry `[McpServerToolType]`, and this tool’s structured payload is returned through a text content block rather than `StructuredContent`, so the E2E parser must validate wrappers before deserializing the actual JSON.
Files: clio/Common/Assertions/AssertInfrastructureAggregator.cs, clio/Common/Assertions/AssertInfrastructureResult.cs, clio/Command/McpServer/Tools/AssertInfrastructureTool.cs, clio/Command/McpServer/Prompts/AssertInfrastructurePrompt.cs, clio.tests/Common/Assertions/AssertInfrastructureAggregatorTests.cs, clio.tests/Command/McpServer/AssertInfrastructureToolTests.cs, clio.mcp.e2e/AssertInfrastructureToolE2ETests.cs, clio.mcp.e2e/Support/Results/AssertInfrastructureEnvelope.cs, clio/Command/AssertCommand.cs, clio/Common/Kubernetes/K8ContextValidator.cs, clio/Common/Kubernetes/K8DatabaseAssertion.cs, clio/Common/Kubernetes/K8RedisAssertion.cs, clio/Common/Assertions/FsPathAssertion.cs, clio/Common/Assertions/FsPermissionAssertion.cs, .codex/workspace-diary.md
Impact: Agents can now inspect one authoritative infrastructure snapshot before deployment decisions, while future MCP work has a tested pattern for aggregate read-only tools that continue across partial failures and still verify against the real stdio server.

## 2026-03-09 21:40 – Return structured unmasked show-webApp-list MCP payload
Context: User wanted the `show-webApp-list` MCP tool to stop returning raw logger output and instead expose the same structured shape as the command JSON output, but without masking sensitive values.
Decision: Kept the CLI command projection reusable by adding a structured environment DTO path on `ShowAppListCommand`, updated the MCP tool to return that structured result directly with `maskSensitiveData: false`, and tightened the E2E parser so it only accepts meaningful environment payloads instead of MCP wrapper objects.
Discovery: The real MCP server returned the tool payload inside wrapper content that could deserialize into an array of null-valued envelopes unless the parser validated at least one meaningful `name` or `uri`.
Files: clio/Command/ShowAppListCommand.cs, clio/Command/McpServer/Tools/ShowWebAppListTool.cs, clio.tests/Command/McpServer/ShowWebAppListToolTests.cs, clio.mcp.e2e/ShowWebAppListToolE2ETests.cs, clio.mcp.e2e/Support/Results/ShowWebAppListEnvelope.cs, .codex/workspace-diary.md
Impact: Agents now receive structured, unmasked web application settings from MCP without depending on raw log parsing, and the E2E harness is stricter about identifying actual JSON result payloads.

## 2026-03-09 22:18 – Finish deploy-creatio MCP flow and add passing infrastructure discovery
Context: User needed `deploy-creatio` to become a real MCP deployment entrypoint and needed a separate MCP tool that exposes only passing infrastructure choices and explicit deployment recommendations.
Decision: Replaced the `deploy-creatio` maintenance stub with real `InstallerCommand` execution, added preflight guidance that points agents to `assert-infrastructure` and `show-passing-infrastructure`, introduced `IPassingInfrastructureService` plus a structured `show-passing-infrastructure` MCP tool, and covered both tools with unit and real stdio E2E tests.
Discovery: The deployment selection flow needed a purpose-built passing-only contract instead of reusing the diagnostic assert payload, and the real MCP server returns these structured tool results through text content wrappers that the E2E parser must validate before deserializing.
Files: clio/Command/McpServer/Tools/InstallerCommandTool.cs, clio/Command/McpServer/Tools/ShowPassingInfrastructureTool.cs, clio/Command/McpServer/Prompts/DeployCreatioPrompt.cs, clio/Command/McpServer/Prompts/ShowPassingInfrastructurePrompt.cs, clio/Common/Assertions/PassingInfrastructureService.cs, clio/Common/Assertions/ShowPassingInfrastructureResult.cs, clio.tests/Command/McpServer/InstallerCommandToolTests.cs, clio.tests/Command/McpServer/ShowPassingInfrastructureToolTests.cs, clio.tests/Common/Assertions/PassingInfrastructureServiceTests.cs, clio.mcp.e2e/DeployCreatioToolE2ETests.cs, clio.mcp.e2e/ShowPassingInfrastructureToolE2ETests.cs, clio.mcp.e2e/Support/Results/ShowPassingInfrastructureEnvelope.cs, .codex/workspace-diary.md
Impact: MCP callers can now inspect failing infrastructure, inspect passing deployable infrastructure, and then call `deploy-creatio` with a concrete recommended bundle instead of guessing `dbServerName` and `redisDb`.

## 2026-03-09 23:07 – Reduce deploy-creatio MCP inputs to five fields
Context: User wanted the MCP-facing `deploy-creatio` tool to accept only `db-server-name`, `redis-server-name`, `ZipFile`, `SiteName`, and `SitePort`, with all other MCP arguments disabled.
Decision: Reduced `DeployCreatioArgs` to the five approved fields, kept `IsSilent = true` plus `RedisDb = -1` internally, and trimmed `show-passing-infrastructure` recommendation bundles so they only advertise MCP arguments that `deploy-creatio` still accepts.
Discovery: The underlying installer does not need an MCP `db` argument when `ZipFile` is supplied because it detects the database type from the unpacked build; the real MCP server advertises the deploy input schema under an `args` wrapper, so E2E validation must inspect nested properties rather than expecting flattened tool parameters.
Files: clio/Command/McpServer/Tools/InstallerCommandTool.cs, clio/Command/McpServer/Prompts/DeployCreatioPrompt.cs, clio/Common/Assertions/ShowPassingInfrastructureResult.cs, clio/Common/Assertions/PassingInfrastructureService.cs, clio.tests/Command/McpServer/InstallerCommandToolTests.cs, clio.tests/Command/McpServer/ShowPassingInfrastructureToolTests.cs, clio.tests/Common/Assertions/PassingInfrastructureServiceTests.cs, clio.mcp.e2e/DeployCreatioToolE2ETests.cs, clio.mcp.e2e/ShowPassingInfrastructureToolE2ETests.cs, clio.mcp.e2e/Support/Results/ShowPassingInfrastructureEnvelope.cs, .codex/workspace-diary.md
Impact: MCP callers now see a much narrower deploy contract, while the passing-infrastructure discovery payload remains aligned with the reduced tool input surface.

## 2026-03-09 23:20 – Fix deploy-creatio MCP AutoRun null regression
Context: A real deploy-creatio MCP request failed after successful unzip and PostgreSQL restore with `Nullable object must have a value.`
Decision: Set `AutoRun = true` in the deploy-creatio MCP wrapper and changed the installer service to use a null-safe `options.AutoRun == true` check.
Discovery: The reduced five-argument MCP wrapper manually constructs `PfInstallerOptions`, so CLI parser defaults are bypassed and nullable options must be assigned explicitly.
Files: clio/Command/McpServer/Tools/InstallerCommandTool.cs, clio/Command/CreatioInstallCommand/CreatioInstallerService.cs, clio.tests/Command/McpServer/InstallerCommandToolTests.cs, .codex/workspace-diary.md
Impact: deploy-creatio MCP calls no longer crash at the auto-launch step when using the reduced contract.

## 2026-03-09 23:55 – Add hardened find-empty-iis-port MCP tool
Context: User needed an MCP tool that helps agents choose a safe local IIS `sitePort` in the fixed `40000-42000` range before `deploy-creatio`.
Decision: Added a read-only MCP tool plus IIS port discovery services, then hardened the result contract to expose only the recommended port and occupancy counts while failing closed on scan errors.
Discovery: Returning full bound-port lists and raw exception text leaked unnecessary host topology, and cross-platform unit tests required injected platform detection because IIS discovery is Windows-only.
Files: clio/Common/IIS/FindAvailableIisPortResult.cs, clio/Common/IIS/AvailableIisPortService.cs, clio/Common/IIS/IPlatformDetector.cs, clio/Common/IIS/TcpPortReservationReader.cs, clio/Common/IIS/IIISSiteDetector.cs, clio/Common/IIS/WindowsIISSiteDetector.cs, clio/Command/McpServer/Tools/FindEmptyIisPortTool.cs, clio/Command/McpServer/Prompts/FindEmptyIisPortPrompt.cs, clio.tests/Common/IIS/AvailableIisPortServiceTests.cs, clio.tests/Command/McpServer/FindEmptyIisPortToolTests.cs, clio.mcp.e2e/FindEmptyIisPortToolE2ETests.cs, clio.mcp.e2e/Support/Results/FindAvailableIisPortEnvelope.cs, .codex/workspace-diary.md
Impact: Agents can now discover a safe local IIS deployment port through MCP without guessing, while the tool avoids leaking host network details and has both unit and real server E2E coverage.

## 2026-03-10 00:04 – Restore disable-reset-password default in deploy-creatio MCP
Context: User reported that password-reset disabling no longer worked through the reduced five-argument `deploy-creatio` MCP tool.
Decision: Set `DisableResetPassword = true` explicitly in the MCP wrapper so it matches the CLI parser default that is bypassed by manual `PfInstallerOptions` construction, and locked that behavior with a unit test.
Discovery: The CLI option default lives on `PfInstallerOptions`, but the MCP wrapper builds the options object directly, so any omitted boolean defaults silently fall back to `false` unless they are assigned in code.
Files: clio/Command/McpServer/Tools/InstallerCommandTool.cs, clio.tests/Command/McpServer/InstallerCommandToolTests.cs, .codex/workspace-diary.md
Impact: `deploy-creatio` MCP now preserves the existing CLI behavior for forced-password-reset disabling instead of silently skipping it.

## 2026-03-10 01:22 – Add dconf MCP tools for environment and build flows
Context: User needed MCP support for `dconf -e` and `dconf --build` so agents can download configuration into a chosen workspace.
Decision: Added separate `download-configuration-by-environment` and `download-configuration-by-build` MCP tools plus aligned prompt guidance, required absolute `workspace-path` for both modes, enforced absolute `build-path` for build mode, and serialized MCP command execution in `BaseTool` to avoid concurrent working-directory leakage.
Discovery: `DownloadConfigurationCommand` depends on the current working directory to target the workspace, Windows drive-relative paths can bypass a simple rooted-path check, and the extracted-build flow works in E2E when the workspace includes `.clio/workspaceSettings.json`.
Files: clio/Command/McpServer/Tools/BaseTool.cs, clio/Command/McpServer/Tools/DownloadConfigurationTool.cs, clio/Command/McpServer/Prompts/DownloadConfigurationPrompt.cs, clio.tests/Command/McpServer/DownloadConfigurationToolTests.cs, clio.mcp.e2e/DownloadConfigurationToolE2ETests.cs, .codex/workspace-diary.md
Impact: MCP clients can now invoke both supported `dconf` flows with a clear contract and passing unit/E2E coverage, while malformed paths and concurrent tool execution are handled more safely.

## 2026-03-10 15:17 – Review FSM/compile MCP correctness and coverage
Context: User requested a focused review of the new FSM status/toggle and compile MCP implementation and tests.
Decision: Re-ran targeted unit and E2E suites for FSM/compile tools and validated the FSM mode inference edge case with a throwaway runtime probe against `FsmModeStatusService`.
Discovery: `DetectMode` currently classifies `useStaticFileContent=true` with `staticFileContent={}` as `off`, which violates the intended fail-closed behavior for non-populated static content; existing unit/E2E tests do not cover this shape.
Files: clio/Common/FsmModeStatusService.cs, clio.tests/Command/McpServer/FsmModeToolTests.cs, clio.tests/Command/McpServer/CompileCreatioToolTests.cs, clio.mcp.e2e/FsmModeToolE2ETests.cs, clio.mcp.e2e/CompileCreatioToolE2ETests.cs, .codex/workspace-diary.md
Impact: Future fixes should tighten `staticFileContent` population validation and add explicit regression tests so ambiguous payloads fail closed as designed.

## 2026-03-10 15:09 – Add FSM mode and compile MCP tools
Context: User needed MCP support to detect FSM mode from `GetApplicationInfo`, toggle FSM mode by environment name, and compile Creatio fully or by a single package.
Decision: Added a new `IFsmModeStatusService` that derives FSM mode from a single unambiguous `GetApplicationInfo` payload, exposed `get-fsm-mode`, `set-fsm-mode`, and `compile-creatio` MCP tools with aligned prompts, and blocked comma-separated package lists at the MCP boundary so `compile-creatio` stays single-package only.
Discovery: The real MCP E2E runner was using a Debug `clio.exe` from `clio.mcp.e2e/appsettings.json`, so verifying new tool discovery required overriding `McpE2E__ClioProcessPath` to the fresh Release binary; the current stable E2E coverage for these tools is discovery plus invalid-environment failure paths.
Files: clio/Common/FsmModeStatusService.cs, clio/Command/McpServer/Tools/FsmModeTool.cs, clio/Command/McpServer/Tools/CompileCreatioTool.cs, clio/Command/McpServer/Prompts/FsmAndCompilePrompt.cs, clio.tests/Command/McpServer/FsmModeToolTests.cs, clio.tests/Command/McpServer/CompileCreatioToolTests.cs, clio.mcp.e2e/FsmModeToolE2ETests.cs, clio.mcp.e2e/CompileCreatioToolE2ETests.cs, clio.mcp.e2e/Support/Results/FsmModeStatusEnvelope.cs, clio/BindingsModule.cs, .codex/workspace-diary.md
Impact: Agents can now query FSM state, toggle FSM mode, and request full or single-package compilation through MCP with passing targeted unit and E2E coverage; docs were reviewed and remain accurate because CLI behavior did not change.

## 2026-03-10 16:08 – Add DB operation log artifacts for deploy and restore
Context: User requested always-on temp log artifacts for `deploy-creatio` and `restore-db` in both CLI and MCP flows.
Decision: Added per-invocation DB log sessions, surfaced `log-file-path` in MCP results, introduced restore-db MCP tools, and routed DB-native restore output plus scoped logger output into a shared synchronized artifact writer.
Discovery: MCP tool invocation for command-style tools requires the `args` wrapper in E2E calls; reopening the artifact file per line caused both corruption and a measurable performance regression, so a shared append writer registry was needed.
Files: clio/Common/DbOperationLogging.cs, clio/Common/SharedAppendFileSink.cs, clio/Common/ConsoleLogger.cs, clio/Command/RestoreDb.cs, clio/Command/CreatioInstallCommand/InstallerCommand.cs, clio/Command/CreatioInstallCommand/CreatioInstallerService.cs, clio/Command/McpServer/Tools/CommandExecutionResult.cs, clio/Command/McpServer/Tools/RestoreDbTool.cs, clio/Command/McpServer/Tools/InstallerCommandTool.cs, clio/Command/McpServer/Prompts/RestoreDbPrompt.cs, clio.tests/Command/RestoreDb.LogArtifact.Tests.cs, clio.tests/Command/McpServer/RestoreDbToolTests.cs, clio.mcp.e2e/RestoreDbToolE2ETests.cs, clio.mcp.e2e/DeployCreatioToolE2ETests.cs, .codex/workspace-diary.md
Impact: CLI users and MCP callers now consistently receive a persistent DB-operation log artifact path for troubleshooting, with restore/deploy DB-native output preserved and covered by unit and E2E tests.

## 2026-03-10 17:06 – Add remote entity schema column commands
Context: User needed Clio support to mutate remote entity schema columns and inspect column/schema properties using the existing `EntitySchemaDesignerService` contract.
Decision: Refactored the remote designer HTTP flow into a reusable client, expanded the Clio-side entity schema DTOs to cover column editing/read scenarios, added `modify-entity-schema-column`, `get-entity-schema-column-properties`, and `get-entity-schema-properties`, and documented the new commands without changing the MCP surface.
Discovery: `GetSchemaDesignItem` plus local DTO mutation is sufficient for v1 column work, inherited columns need an explicit read-only guard, and full `clio.tests` currently has unrelated setup failures because `SettingsRepository` writes `schema.json` under a missing `testhost` profile path on this machine.
Files: clio/Command/EntitySchemaDesigner/EntitySchemaDesignerDtos.cs, clio/Command/EntitySchemaDesigner/EntitySchemaDesignerSupport.cs, clio/Command/EntitySchemaDesigner/RemoteEntitySchemaDesignerClient.cs, clio/Command/EntitySchemaDesigner/RemoteEntitySchemaCreator.cs, clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs, clio/Command/ModifyEntitySchemaColumnCommand.cs, clio/Command/GetEntitySchemaColumnPropertiesCommand.cs, clio/Command/GetEntitySchemaPropertiesCommand.cs, clio/BindingsModule.cs, clio/Program.cs, clio/Commands.md, clio/help/en/modify-entity-schema-column.txt, clio/help/en/get-entity-schema-column-properties.txt, clio/help/en/get-entity-schema-properties.txt, clio/docs/commands/modify-entity-schema-column.md, clio/docs/commands/get-entity-schema-column-properties.md, clio/docs/commands/get-entity-schema-properties.md, clio.tests/Command/ModifyEntitySchemaColumnCommandTests.cs, clio.tests/Command/GetEntitySchemaColumnPropertiesCommandTests.cs, clio.tests/Command/GetEntitySchemaPropertiesCommandTests.cs, clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs, .codex/workspace-diary.md
Impact: Clio can now add/modify/remove own entity-schema columns remotely, print human-readable schema/column summaries, and the new command surface has targeted unit coverage and documentation aligned with the existing CLI help system.

## 2026-03-11 11:35 – Add push-workspace and restore-workspace MCP tools
Context: User requested new MCP tools for `pushw` and `restorew` with only environment name and workspace path inputs.
Decision: Added dedicated workspace-sync MCP tools plus prompt helpers, reused the download-configuration-style absolute workspace-path validation and current-directory switching pattern, and limited the MCP contract to `environment-name` and `workspace-path`.
Discovery: Because `BaseTool<T>` requires one concrete options type, exposing both commands cleanly required separate MCP tool classes sharing a common workspace execution base rather than one combined generic adapter.
Files: clio/Command/McpServer/Tools/WorkspaceSyncTool.cs, clio/Command/McpServer/Prompts/WorkspaceSyncPrompt.cs, clio.tests/Command/McpServer/WorkspaceSyncToolTests.cs, clio.mcp.e2e/WorkspaceSyncToolE2ETests.cs, .codex/workspace-diary.md
Impact: MCP clients can now invoke workspace push and restore flows with a narrow, explicit contract and regression coverage for path validation, command mapping, and invalid-environment failures.

## 2026-03-11 16:02 – Security review of workspace-sync MCP additions
Context: User requested a focused security/unsafe-behavior review for new workspace sync MCP files only.
Decision: Reviewed tool metadata, path-validation logic, prompt wording, and unit/E2E coverage for exploitable behaviors and safety-contract mismatches.
Discovery: Workspace path validation currently accepts UNC absolute paths and switches process working directory to them, and both mutating tools are marked Destructive=false, weakening client safety guardrails.
Files: clio/Command/McpServer/Tools/WorkspaceSyncTool.cs, clio/Command/McpServer/Prompts/WorkspaceSyncPrompt.cs, clio.tests/Command/McpServer/WorkspaceSyncToolTests.cs, clio.mcp.e2e/WorkspaceSyncToolE2ETests.cs, .codex/workspace-diary.md
Impact: Future MCP workspace sync hardening should explicitly block network/UNC workspace paths and align destructive metadata with real side effects.

## 2026-03-11 16:02 – Workspace sync MCP performance review
Context: User requested a focused performance/overhead review for new workspace MCP tool, prompt, unit tests, and E2E tests.
Decision: Performed static review of the four target files and validated runtime behavior by executing the `WorkspaceSyncToolE2ETests` suite.
Discovery: No concrete performance bottlenecks or unnecessary overhead were found in the reviewed additions; the E2E class completed quickly in the current environment.
Files: clio/Command/McpServer/Tools/WorkspaceSyncTool.cs, clio/Command/McpServer/Prompts/WorkspaceSyncPrompt.cs, clio.tests/Command/McpServer/WorkspaceSyncToolTests.cs, clio.mcp.e2e/WorkspaceSyncToolE2ETests.cs, .codex/workspace-diary.md
Impact: Future reviewers can treat this workspace-sync MCP slice as performance-clean unless command-level behavior outside these files changes.

## 2026-03-11 17:02 – Add get-pkg-list MCP tool and workspace happy-path coverage
Context: User asked to expose `get-pkg-list` through MCP and to add real happy-path push/restore coverage using arranged workspaces and packages.
Decision: Added a structured `get-pkg-list` MCP tool plus prompt and unit/E2E coverage, changed workspace E2E to arrange real packages with `create-workspace` and `add-package`, asserted push success through MCP `get-pkg-list`, and verified restore by deleting the local package directory then restoring into the same workspace so existing workspace settings drive the package download.
Discovery: `CallToolResult.Content` for structured non-command MCP tools arrives as text-content wrappers containing JSON strings, so E2E parsers must reject wrapper objects before deserializing payload rows; `install-gate` can fail even when cliogate is already present, so the harness now falls back to verifying `get-pkg-list --Json true` success instead of assuming install must always be the successful path.
Files: clio/Command/GetPkgListCommand.cs, clio/Command/McpServer/Tools/GetPkgListTool.cs, clio/Command/McpServer/Prompts/WorkspacePackagePrompt.cs, clio/Command/McpServer/Tools/BaseTool.cs, clio/Command/McpServer/Tools/WorkspaceSyncTool.cs, clio.tests/Command/McpServer/GetPkgListToolTests.cs, clio.tests/Command/McpServer/WorkspaceSyncToolTests.cs, clio.mcp.e2e/GetPkgListToolE2ETests.cs, clio.mcp.e2e/WorkspaceSyncToolE2ETests.cs, clio.mcp.e2e/Support/Configuration/ClioCliCommandRunner.cs, clio.mcp.e2e/Support/Configuration/TestConfiguration.cs, clio.mcp.e2e/Support/Results/GetPkgListEnvelope.cs, .codex/workspace-diary.md
Impact: MCP callers can now list environment packages with structured package metadata, and the workspace-sync MCP slice now has real end-to-end proof for push publication and restore reconstruction instead of only negative-path validation.

## 2026-03-11 17:25 – Research E2E test filtering options
Context: User asked whether the `clio.mcp.e2e/run-e2e-tests.ps1` filter input can be made more specific, ideally around `AllureFeature`.
Decision: Reviewed the local runner and test attributes, then checked official `.NET`, NUnit adapter, and Allure NUnit documentation to see which metadata is actually filterable during execution.
Discovery: In the current VSTest-based `dotnet test` flow, NUnit filtering is limited to `FullyQualifiedName`, `Name`, `Category`, and `Priority`; `AllureFeature` is reporting metadata and is not a native test-selection property. More precise selection would need either standardized NUnit categories or NUnit's `NUnit.Where` filtering against custom NUnit properties.
Files: clio.mcp.e2e/run-e2e-tests.ps1, clio.mcp.e2e/clio.mcp.e2e.csproj, Directory.Packages.props, .codex/workspace-diary.md
Impact: Future cleanup of the E2E runner should standardize on explicit NUnit metadata for test selection instead of overloading free-form filter input and class-name conventions.

## 2026-03-12 10:45 – Install gh-address-comments skill
Context: User requested installation of the `gh-address-comments` Codex skill.
Decision: Used the `skill-installer` helper script to install the curated skill from `openai/skills` into the local Codex skills directory.
Discovery: The curated installer script successfully placed the skill under `C:\Users\k.krylov\.codex\skills\gh-address-comments` without requiring fallback to git checkout.
Files: C:/Users/k.krylov/.codex/skills/.system/skill-installer/SKILL.md, .codex/workspace-diary.md
Impact: The `gh-address-comments` skill is now available in this Codex environment after restart.

## 2026-03-12 11:05 – Research product skill repository model
Context: User asked how to create a product-specific skills repository with an experience similar to the OpenAI `skill-installer` flow.
Decision: Ground the recommendation in the Codex skills documentation plus the installed `skill-installer` behavior instead of proposing a custom format.
Discovery: The cleanest path is to mirror the `openai/skills` repository conventions (`skills/.curated`, optional `skills/.experimental`, one folder per skill with `SKILL.md`) so an installer can list and install skills by repo path while Codex still discovers installed skills normally.
Files: C:/Users/k.krylov/.codex/skills/.system/skill-installer/SKILL.md, C:/Projects/clio/.codex/workspace-diary.md
Impact: Future guidance for internal skill catalogs can reuse the same repository shape and user flow without inventing a separate distribution model.

## 2026-03-13 12:55 – Add data-binding CLI and MCP flows
Context: User requested implementation of the planned data-binding command family with matching MCP tools, tests, and docs.
Decision: Added three flat verbs (`create-data-binding`, `add-data-binding-row`, `remove-data-binding-row`) over a shared DI-backed service layer, aligned MCP tools/prompts/tests to the same contract, and normalized `create-data-binding --environment` to the legacy `-e/--Environment` parser option in `Program.cs` so the CLI matches the documented plan without changing global environment handling.
Discovery: Real MCP E2E failures came from two separate issues: command-style MCP calls in this harness need the `args` wrapper, and `create-data-binding` can surface a top-level MCP invocation error when the configured sandbox environment is unresolved because environment-aware command resolution happens before `BaseTool` enters its exception-to-result path.
Files: clio/Command/DataBindingCommand.cs, clio/Command/McpServer/Tools/DataBindingTool.cs, clio/Command/McpServer/Prompts/DataBindingPrompt.cs, clio/BindingsModule.cs, clio/Program.cs, clio.tests/Command/DataBindingCommandTests.cs, clio.tests/Command/McpServer/DataBindingToolTests.cs, clio.mcp.e2e/DataBindingToolE2ETests.cs, clio/help/en/create-data-binding.txt, clio/help/en/add-data-binding-row.txt, clio/help/en/remove-data-binding-row.txt, clio/docs/commands/create-data-binding.md, clio/docs/commands/add-data-binding-row.md, clio/docs/commands/remove-data-binding-row.md, clio/Commands.md, .codex/workspace-diary.md
Impact: Future command/MCP work can reuse the data-binding service and E2E patterns, while the CLI now accepts the planned lowercase environment flag and the E2E suite automatically falls back to a reachable real environment (`d2`) when the configured sandbox alias is stale.

## 2026-03-15 10:20 – Change default data-binding folder name
Context: User wanted `create-data-binding` to default to `SysSettings` instead of `SysSettings_1` when no explicit binding name is provided.
Decision: Changed the default binding-name derivation from `<schema>_1` to `<schema>` and updated the matching MCP description text, docs, unit tests, and E2E expectations to keep the contract aligned.
Discovery: The code path that needed the behavior change was a single default-name branch in `DataBindingService.CreateBinding`; the rest of the work was contract maintenance across docs/tests, and parallel `dotnet test` runs against the same Debug output can produce transient `clio.dll` file-lock failures, so rerunning the targeted unit slices with `--no-build` was the stable verification path.
Files: clio/Command/DataBindingCommand.cs, clio/Command/McpServer/Tools/DataBindingTool.cs, clio.tests/Command/DataBindingCommandTests.cs, clio.tests/Command/McpServer/DataBindingToolTests.cs, clio.mcp.e2e/DataBindingToolE2ETests.cs, clio/help/en/create-data-binding.txt, clio/docs/commands/create-data-binding.md, clio/Commands.md, .codex/workspace-diary.md
Impact: `clio-dev create-data-binding -e d2 --package <pkg> --schema <schema>` now creates the binding under `<schema>` by default, and future changes to this command have an updated doc/test baseline that matches the new folder naming rule.

## 2026-03-15 10:40 – Make data-binding filter.json empty
Context: User wanted newly created data bindings to generate an empty `filter.json` file instead of `{}`.
Decision: Changed the filter writer in `DataBindingService.CreateBinding` to write an empty string and updated the command help plus detailed command docs and unit coverage to match.
Discovery: Direct CLI verification is stable when the workspace is arranged minimally on disk (`.clio/workspaceSettings.json` plus the target package folder) and the command is run sequentially; the resulting `filter.json` length is `0` against real environment `d2`.
Files: clio/Command/DataBindingCommand.cs, clio.tests/Command/DataBindingCommandTests.cs, clio/help/en/create-data-binding.txt, clio/docs/commands/create-data-binding.md, .codex/workspace-diary.md
Impact: Newly created bindings now match the expected empty-filter file layout and avoid spurious `{}` content diffs.

## 2026-03-15 11:05 – Auto-generate missing data-binding Id on create
Context: User wanted `create-data-binding` to accept row payloads without `Id` and generate the primary key automatically for schemas such as `SysSettings`.
Decision: Changed only the create flow to synthesize a GUID value when the binding primary column is GUID-based and omitted from `--values`; `add-data-binding-row` still requires an explicit primary key so upserts remain deterministic.
Discovery: The narrowest safe implementation point was `BuildRow`, with a create-only `autoGeneratePrimaryKey` switch; targeted unit tests, MCP E2E tests, and a direct CLI smoke against `d2` all confirmed that `--values "{\"Code\":\"UsrSetting\",\"Name\":\"Setting name\"}"` now writes a generated GUID `Id` into `data.json`.
Files: clio/Command/DataBindingCommand.cs, clio/Command/McpServer/Tools/DataBindingTool.cs, clio/Command/McpServer/Prompts/DataBindingPrompt.cs, clio.tests/Command/DataBindingCommandTests.cs, clio.tests/Command/McpServer/DataBindingToolTests.cs, clio.mcp.e2e/DataBindingToolE2ETests.cs, clio/help/en/create-data-binding.txt, clio/docs/commands/create-data-binding.md, clio/Commands.md, .codex/workspace-diary.md
Impact: Users and MCP callers can create data bindings from business-column payloads without manually supplying `Id`, while row-level update/remove operations keep explicit key semantics.

## 2026-03-15 11:25 – Auto-generate missing data-binding Id on add-row
Context: User wanted `add-data-binding-row` to mirror `create-data-binding` and generate `Id` automatically when the row payload omits it.
Decision: Extended the same GUID primary-key generation behavior to add-row, and tightened the helper so `Id: null` is treated as missing instead of producing an empty normalized key.
Discovery: The right fix was to treat primary-key presence as "present with a non-empty value" rather than just `ContainsKey`; targeted unit tests, MCP E2E tests, and a direct CLI smoke against `d2` confirmed that add-row now appends a new row with a generated GUID `Id`.
Files: clio/Command/DataBindingCommand.cs, clio/Command/McpServer/Tools/DataBindingTool.cs, clio/Command/McpServer/Prompts/DataBindingPrompt.cs, clio.tests/Command/DataBindingCommandTests.cs, clio.tests/Command/McpServer/DataBindingToolTests.cs, clio.mcp.e2e/DataBindingToolE2ETests.cs, clio/help/en/add-data-binding-row.txt, clio/docs/commands/add-data-binding-row.md, clio/help/en/create-data-binding.txt, clio/docs/commands/create-data-binding.md, clio/Commands.md, .codex/workspace-diary.md
Impact: Both create and add-row now accept business-column payloads without explicit `Id`, and null GUID keys no longer create malformed rows that are hard to remove later.

## 2026-03-15 12:15 – Add offline data-binding template catalog
Context: User wanted `create-data-binding` to work in isolated environments for stable schemas such as `SysSettings`, with MCP and docs aligned to the same offline behavior.
Decision: Added a built-in template catalog plus schema resolver so templated schemas bypass Creatio entirely, made MCP `create-data-binding` conditionally environment-free for templated schemas, and kept add/remove row strictly local-file operations.
Discovery: The safest public DI surface was a small public `IDataBindingTemplateCatalog` contract exposing only `HasTemplate`; the schema-returning part stayed internal to avoid leaking internal model types through the public MCP tool constructor path.
Files: clio/Command/DataBindingCommand.cs, clio/BindingsModule.cs, clio/Command/McpServer/Tools/DataBindingTool.cs, clio/Command/McpServer/Prompts/DataBindingPrompt.cs, clio.tests/Command/DataBindingCommandTests.cs, clio.tests/Command/McpServer/DataBindingToolTests.cs, clio.mcp.e2e/DataBindingToolE2ETests.cs, clio/help/en/create-data-binding.txt, clio/help/en/add-data-binding-row.txt, clio/help/en/remove-data-binding-row.txt, clio/docs/commands/create-data-binding.md, clio/docs/commands/add-data-binding-row.md, clio/docs/commands/remove-data-binding-row.md, clio/Commands.md, spec/data-binding/data-binding-template-catalog.md, .codex/workspace-diary.md
Impact: `SysSettings` bindings can now be created offline through both CLI and MCP, while future stable schemas can be added to the built-in catalog without changing command semantics.

## 2026-03-15 12:35 – Collapse data-binding type maps into DataValueTypeMap
Context: User called out that `DataBindingDataValueTypeMap.FromRuntimeValueType` duplicated GUID knowledge already present in `DataValueTypeMap`.
Decision: Added `DataValueTypeMap.FromRuntimeValueType(int)` to the existing shared map in `clio/Command/ProcessModel/Schema.cs` and removed the data-binding-specific switch table.
Discovery: `DataValueTypeMap` already had the needed GUID constants but only exposed GUID-to-CLR-type resolution; adding the reverse runtime-int-to-GUID method removed the duplication cleanly without changing data-binding behavior.
Files: clio/Command/ProcessModel/Schema.cs, clio/Command/DataBindingCommand.cs, .codex/workspace-diary.md
Impact: Data-binding and process-model code now share a single authoritative data-value-type GUID table, so future GUID changes only need to be made in one place.

## 2026-03-15 14:05 – Preserve workspace gitignore through dotnet-tool packaging
Context: User reported that `create-workspace` lost `.gitignore` after clio was packed and installed as a NuGet tool because dotfiles under `tpl/workspace` were not surviving packaging.
Decision: Kept the real template source `.gitignore`, added a surrogate `clio/tpl/workspace/gitignore.txt` that NuGet will carry, and normalized it back to `.gitignore` in `WorkspaceCreator.Create` immediately after copying the workspace template.
Discovery: Even when the generated `.nuspec` explicitly listed `tpl/workspace/.gitignore` in `tools/net8.0/any`, the final `.nupkg` still dropped that entry; a non-dot surrogate filename packs correctly and can be renamed safely during workspace creation.
Files: clio/clio.csproj, clio/tpl/workspace/gitignore.txt, clio/Workspace/WorkspaceCreator.cs, .codex/workspace-diary.md
Impact: Installed `clio` tool packages now preserve the workspace gitignore content via a packaged surrogate, and `create-workspace` produces `.gitignore` correctly in both local-dev and packaged-tool flows.

## 2026-03-15 14:40 – Add offline SysModule data-binding template
Context: User wanted a built-in offline template for `SysModule`, using the checked-in binding under `spec/data-binding/DataBindingPkg/Data/SysModule` as the source of truth and with special attention to `Image16`/`Image20`.
Decision: Extended template columns so offline templates can carry exact `DataTypeValueUId` overrides from checked-in descriptors, then added `SysModule` to `DataBindingTemplateCatalog` using the checked-in schema metadata instead of relying on runtime data-value-type integers.
Discovery: The provided `dbhub` MCP surface was not exposed in this session, and direct runtime creation of `SysModule` fails on unsupported runtime image types; using explicit template `DataTypeValueUId` values avoided broad runtime-schema changes while preserving the exact checked-in GUIDs for `Image16`, `Image20`, `Logo`, and `Image32`. `DataValueTypeMap.Resolve` still needed the two image GUIDs mapped as string-compatible so row operations can round-trip base64/null image values from the filesystem.
Files: clio/Command/DataBindingCommand.cs, clio/Command/ProcessModel/Schema.cs, clio.tests/Command/DataBindingCommandTests.cs, .codex/workspace-diary.md
Impact: `create-data-binding --schema SysModule` now works offline with descriptor types matching the checked-in binding, and image columns use the correct filesystem-oriented type GUIDs instead of being coerced through the runtime map.

## 2026-03-15 16:22 – SysModule template validation
Context: Validated the built-in SysModule data-binding template against the live d2 database and the checked-in sample binding.
Decision: Treat the checked-in binding as the filesystem baseline and compare both it and the code template to live column metadata from dbhub.
Discovery: SysModule DB metadata, sample descriptor, and template code are aligned on schema UId, key column, column UIds, and special image type handling; the binding intentionally omits five system columns (CreatedOn, CreatedById, ModifiedOn, ModifiedById, ProcessListeners).
Files: C:\Projects\clio\clio\Command\DataBindingCommand.cs, C:\Projects\clio\spec\data-binding\DataBindingPkg\Data\SysModule\descriptor.json, C:\Projects\clio\spec\data-binding\DataBindingPkg\Data\SysModule\data.json, C:\Projects\clio\.codex\workspace-diary.md
Impact: Future SysModule template changes can use the checked-in binding as a reliable offline source, with image columns preserved as base64 payloads and image-lookup columns preserved as lookup GUID fields.

## 2026-03-15 17:05 – Support image files in data-binding values
Context: User wanted image-content data-binding columns to accept local image files instead of requiring pre-encoded base64 strings, with docs and MCP contract updated.
Decision: Added image-content file-path support in the shared data-binding value converter so create/add commands and MCP tools can encode files automatically, but restricted file reads to paths inside the resolved workspace to avoid arbitrary local-file reads.
Discovery: The first implementation needed a workspace-boundary guard and consistent workspace-root resolution for localization values as well as main row values; after tightening that, targeted command tests and MCP E2E passed.
Files: C:\Projects\clio\clio\Command\DataBindingCommand.cs, C:\Projects\clio\clio\Command\ProcessModel\Schema.cs, C:\Projects\clio\clio\Command\McpServer\Tools\DataBindingTool.cs, C:\Projects\clio\clio\Command\McpServer\Prompts\DataBindingPrompt.cs, C:\Projects\clio\clio.tests\Command\DataBindingCommandTests.cs, C:\Projects\clio\clio.tests\Command\McpServer\DataBindingToolTests.cs, C:\Projects\clio\clio.mcp.e2e\DataBindingToolE2ETests.cs, C:\Projects\clio\clio\help\en\create-data-binding.txt, C:\Projects\clio\clio\help\en\add-data-binding-row.txt, C:\Projects\clio\clio\docs\commands\create-data-binding.md, C:\Projects\clio\clio\docs\commands\add-data-binding-row.md, C:\Projects\clio\clio\Commands.md, C:\Projects\clio\.codex\workspace-diary.md
Impact: Future data-binding work can rely on a single image-file encoding path across CLI and MCP, with workspace-scoped file resolution documented and covered by tests.

## 2026-03-15 17:08 – Enforce SysModule IconBackground palette
Context: User added an HTML reference with the 16 allowed SysModule colors and wanted data binding to reject any other IconBackground values.
Decision: Added a single enum-backed domain rule in data-binding row construction so both `create-data-binding` and `add-data-binding-row` validate and normalize `SysModule.IconBackground` consistently.
Discovery: Accepting palette values case-insensitively but persisting them in canonical uppercase hex form keeps bindings stable while remaining easy to use from CLI and MCP.
Files: C:\Projects\clio\clio\Command\DataBindingCommand.cs, C:\Projects\clio\clio\Command\McpServer\Tools\DataBindingTool.cs, C:\Projects\clio\clio\Command\McpServer\Prompts\DataBindingPrompt.cs, C:\Projects\clio\clio.tests\Command\DataBindingCommandTests.cs, C:\Projects\clio\clio.tests\Command\McpServer\DataBindingToolTests.cs, C:\Projects\clio\clio.mcp.e2e\DataBindingToolE2ETests.cs, C:\Projects\clio\clio\help\en\create-data-binding.txt, C:\Projects\clio\clio\help\en\add-data-binding-row.txt, C:\Projects\clio\clio\docs\commands\create-data-binding.md, C:\Projects\clio\clio\docs\commands\add-data-binding-row.md, C:\Projects\clio\clio\Commands.md, C:\Projects\clio\.codex\workspace-diary.md
Impact: SysModule bindings now enforce the same predefined color palette across CLI, MCP, tests, and generated filesystem data.

## 2026-03-16 09:58 – Add entity schema MCP read and column tools after PR 467 merge
Context: User wanted PR 467 merged first, then MCP support for the new entity schema column commands plus stricter CLIO warning handling for touched files.
Decision: Merged PR 467 before implementation, replaced the create-only MCP slice with a grouped entity-schema tool/prompt surface, added shared structured read models for schema and column properties, and updated `AGENTS.md` so touched files must be left free of relevant `CLIO*` diagnostics.
Discovery: The merged entity-schema DTOs introduced duplicate type names between `Clio.Command` and `Clio.Command.EntitySchemaDesigner`, so tests need explicit qualification when both namespaces are used; the sandbox in this environment could not provision `cliogate`, so the destructive entity-schema E2E flow now skips cleanly while invalid-environment coverage still runs.
Files: C:\Projects\clio\clio\Command\EntitySchemaDesigner\EntitySchemaReadModels.cs, C:\Projects\clio\clio\Command\EntitySchemaDesigner\RemoteEntitySchemaColumnManager.cs, C:\Projects\clio\clio\Command\GetEntitySchemaPropertiesCommand.cs, C:\Projects\clio\clio\Command\GetEntitySchemaColumnPropertiesCommand.cs, C:\Projects\clio\clio\Command\ModifyEntitySchemaColumnCommand.cs, C:\Projects\clio\clio\Command\McpServer\Tools\EntitySchemaTool.cs, C:\Projects\clio\clio\Command\McpServer\Prompts\EntitySchemaPrompt.cs, C:\Projects\clio\clio.tests\Command\McpServer\EntitySchemaToolTests.cs, C:\Projects\clio\clio.mcp.e2e\EntitySchemaToolE2ETests.cs, C:\Projects\clio\AGENTS.md, C:\Projects\clio\.codex\workspace-diary.md
Impact: Entity schema create/read/modify commands now have aligned MCP coverage with shared structured projections, and future agents are explicitly required to avoid introducing new `CLIO*` diagnostics and to fix relevant ones in touched files.

## 2026-03-16 10:01 – Remove PushWorkspace console logging
Context: User asked to address the remaining `CLIO002` warning in `PushWorkspaceCommand.cs`.
Decision: Replaced direct `Console.WriteLine` calls in `PushWorkspaceCommand` with injected `ILogger` writes and updated the workspace-sync test fake constructor to pass a logger dependency.
Discovery: `PushWorkspaceCommand` already had a narrow set of progress messages, so the fix was isolated to dependency injection and message routing without changing command behavior or MCP wiring.
Files: C:\Projects\clio\clio\Command\PushWorkspaceCommand.cs, C:\Projects\clio\clio.tests\Command\McpServer\WorkspaceSyncToolTests.cs, C:\Projects\clio\.codex\workspace-diary.md
Impact: `PushWorkspaceCommand` no longer emits `CLIO002` for console logging, and the workspace-sync MCP test fixture remains aligned with the command constructor.

## 2026-03-16 10:58 – Silence CommonProgramTest console spam
Context: User reported that `CommonProgramTest` printed real logger output to the console, making CI logs noisy and harder to read.
Decision: Marked the fixture non-parallel and redirected `Console.Out` and `Console.Error` to `TextWriter.Null` for the duration of each test, restoring the original writers in teardown.
Discovery: The fixture assertions do not depend on console text, so suppressing the global writers at fixture scope removes the noise without changing test intent; the global console redirect needs non-parallel execution to avoid cross-test interference.
Files: C:\Projects\clio\clio.tests\CommonProgramTest.cs, C:\Projects\clio\.codex\workspace-diary.md
Impact: `CommonProgramTest` no longer floods CI output with logger text while still exercising `Program.Main` behavior.

## 2026-03-16 12:04 – Add add-item-model MCP tool
Context: User requested a new MCP tool that exposes the `clio add-item model` generate-all-models flow with explicit namespace, folder, and environment-name arguments.
Decision: Added a dedicated `add-item-model` tool and prompt, resolved `AddItemCommand` through `IToolCommandResolver`, enforced strict local absolute folder validation in the MCP layer, and kept resources/docs unchanged because CLI behavior did not change.
Discovery: `AddItemOptions` needs an explicit `en-US` initializer when constructed outside the CLI parser, and the real MCP E2E path can still hit intermittent IIS `503` HTML responses during runtime schema reads even after MCP-safe logging, readiness polling, sequential MCP-mode schema loading, and request retries.
Files: C:\Projects\clio\clio\Command\McpServer\Tools\AddItemModelTool.cs, C:\Projects\clio\clio\Command\McpServer\Prompts\AddItemModelPrompt.cs, C:\Projects\clio\clio\Command\AddItemCommand.cs, C:\Projects\clio\clio\ModelBuilder\ModelBuilder.cs, C:\Projects\clio\clio\Common\ConsoleLogger.cs, C:\Projects\clio\clio\Program.cs, C:\Projects\clio\clio.tests\Command\McpServer\AddItemModelToolTests.cs, C:\Projects\clio\clio.tests\ModelBuilder\ModelBuilderTests.cs, C:\Projects\clio\clio.mcp.e2e\AddItemModelToolE2ETests.cs, C:\Projects\clio\clio.mcp.e2e\Support\Configuration\ClioCliCommandRunner.cs, C:\Projects\clio\.codex\workspace-diary.md
Impact: Future MCP tools for environment-sensitive generators can reuse the same pattern for stable tool naming, prompt alignment, MCP-side path validation, MCP-safe logging, and readiness-aware E2E setup.

## 2026-03-16 12:52 – Add generate-process-model MCP tool
Context: User requested MCP exposure for `generate-process-model` with the command’s existing arguments plus environment-name and matching test coverage.
Decision: Added a dedicated `generate-process-model` tool and prompt, kept destination-path behavior aligned with the current CLI command, added initializer-backed defaults for non-CLI option construction, and wrapped environment-resolution failures into normal command execution envelopes for MCP callers.
Discovery: The underlying process-model writer still resolves output with `Path.Join(Environment.CurrentDirectory, fileOrFolderPath)`, so absolute destination paths are not currently honored; the MCP E2E therefore verifies output through a relative destination path under the MCP server working directory and uses a configurable sandbox `ProcessCode`.
Files: C:\Projects\clio\clio\Command\GenerateProcessModelCommand.cs, C:\Projects\clio\clio\Command\McpServer\Tools\GenerateProcessModelTool.cs, C:\Projects\clio\clio\Command\McpServer\Prompts\GenerateProcessModelPrompt.cs, C:\Projects\clio\clio.tests\Command\GenerateProcessModelCommandTestFixture.cs, C:\Projects\clio\clio.tests\Command\McpServer\GenerateProcessModelToolTests.cs, C:\Projects\clio\clio.mcp.e2e\GenerateProcessModelToolE2ETests.cs, C:\Projects\clio\clio.mcp.e2e\Support\Configuration\McpE2ESettings.cs, C:\Projects\clio\clio.mcp.e2e\appsettings.example.json, C:\Projects\clio\clio.mcp.e2e\appsettings.json, C:\Projects\clio\.codex\workspace-diary.md
Impact: Future MCP adapters for generator-style commands can follow the same pattern for tool naming, default mapping, structured resolver-failure reporting, and config-driven E2E inputs without changing CLI behavior.

## 2026-03-16 13:28 – Respect explicit file paths in process-model writer
Context: User reported that `ProcessModelWriter` always treated `DestinationPath` as a folder, so `generate-process-model` could not write to an explicitly requested file name.
Decision: Split writer output resolution into file-vs-folder handling, preserving exact `.cs` file paths while keeping folder mode as `<Code>.cs`, updated the MCP prompt/E2E to cover explicit file targets, and added command docs for the previously undocumented command.
Discovery: The repo’s `CLIO003` analyzer also applies to small path checks in command helpers, so the writer fix must avoid direct `System.IO.Path` usage and stay within `IFileSystem` plus string-based separator parsing.
Files: C:\Projects\clio\clio\Command\ProcessModel\ProcessModelWriter.cs, C:\Projects\clio\clio\Command\GenerateProcessModelCommand.cs, C:\Projects\clio\clio\Command\McpServer\Tools\GenerateProcessModelTool.cs, C:\Projects\clio\clio\Command\McpServer\Prompts\GenerateProcessModelPrompt.cs, C:\Projects\clio\clio.tests\Command\ProcessModel\ProcessModelWriterTests.cs, C:\Projects\clio\clio.tests\Command\McpServer\GenerateProcessModelToolTests.cs, C:\Projects\clio\clio.mcp.e2e\GenerateProcessModelToolE2ETests.cs, C:\Projects\clio\clio\Commands.md, C:\Projects\clio\clio\help\en\generate-process-model.txt, C:\Projects\clio\clio\docs\commands\generate-process-model.md, C:\Projects\clio\.codex\workspace-diary.md
Impact: Future work on process-model generation can rely on explicit output-file naming, with MCP/tests/docs already aligned to that contract.

## 2026-03-16 14:12 – Refine add-item-model MCP folder handling and output
Context: User asked to implement reviewed agent feedback for `add-item-model`, specifically auto-creating the output folder in MCP mode and reducing noisy generation output.
Decision: Relaxed MCP validation to allow nonexistent absolute local folders, created the folder before executing `AddItemCommand`, preserved relative and UNC rejection, and compacted repeated model-progress messages into one MCP summary line while keeping warnings and errors intact.
Discovery: The underlying `add-item model` command already creates the destination directory, so the stricter behavior was MCP-only; the destructive MCP E2E success path still intermittently fails against the shared environment with runtime schema IIS `503` responses, while the new validation scenario remains stable.
Files: C:\Projects\clio\clio\Command\McpServer\Tools\AddItemModelTool.cs, C:\Projects\clio\clio\Command\McpServer\Prompts\AddItemModelPrompt.cs, C:\Projects\clio\clio.tests\Command\McpServer\AddItemModelToolTests.cs, C:\Projects\clio\clio.mcp.e2e\AddItemModelToolE2ETests.cs, C:\Projects\clio\.codex\workspace-diary.md
Impact: Future MCP generator tools can align more closely with wrapped CLI behavior while still returning agent-friendly summaries and keeping destructive E2E failures distinguishable from environment instability.

## 2026-03-16 14:41 – Review create-data-binding lookup/image-reference row shape
Context: User reported that `create-data-binding` should write `SchemaColumnUId`, `Value`, and required `DisplayValue` for lookup and image-reference columns.
Decision: Validated the report as a real bug in the current implementation rather than a caller error; no code changes yet because the request was code review only.
Discovery: `BuildRow` and template row generation only serialize `SchemaColumnUId` plus `Value`, `DataBindingRowValue` has no `DisplayValue` property, and the `--values` converter only accepts scalar JSON nodes for lookup/image-reference values, so callers cannot supply or validate the required display text today.
Files: C:\Projects\clio\clio\Command\DataBindingCommand.cs, C:\Projects\clio\clio\Command\ProcessModel\Schema.cs, C:\Projects\clio\spec\data-binding\DataBindingPkg\Data\SysModule\data.json, C:\Projects\clio\cliogate\Data\SysAdminOperationGrantee_Telemetry\data.json, C:\Projects\clio\.codex\workspace-diary.md
Impact: Future fixes to `create-data-binding` should treat lookup/image-reference values as structured binding row payloads instead of scalar values and add regression tests for both CLI and MCP surfaces.

## 2026-03-16 15:46 – Backfill data-binding DisplayValue for lookup and image-reference rows
Context: User asked to implement the validated `create-data-binding` bug fix and preferred a hybrid contract where caller-supplied `displayValue` wins, but `create-data-binding` should resolve it from Creatio when runtime lookup data is available.
Decision: Extended data-binding row serialization to support `DisplayValue`, accepted structured `{ value, displayValue }` payloads for lookup and image-reference columns, resolved missing lookup display text through `SelectQuery` during runtime-backed `create-data-binding`, and kept `add-data-binding-row` local-only by requiring explicit display text for non-null lookup/image-reference values.
Discovery: Built-in offline templates such as `SysModule` already model lookup/image-reference columns even without reference schema names, so offline callers can still generate valid rows by supplying explicit `displayValue`; MCP/data-binding docs had to be updated in parallel because the `values` JSON contract is now richer than the original scalar-only description.
Files: C:\Projects\clio\clio\Command\DataBindingCommand.cs, C:\Projects\clio\clio\Command\ProcessModel\Schema.cs, C:\Projects\clio\clio\BindingsModule.cs, C:\Projects\clio\clio\Command\McpServer\Tools\DataBindingTool.cs, C:\Projects\clio\clio\Command\McpServer\Prompts\DataBindingPrompt.cs, C:\Projects\clio\clio.tests\Command\DataBindingCommandTests.cs, C:\Projects\clio\clio.tests\Command\McpServer\DataBindingToolTests.cs, C:\Projects\clio\clio.mcp.e2e\DataBindingToolE2ETests.cs, C:\Projects\clio\clio\help\en\create-data-binding.txt, C:\Projects\clio\clio\help\en\add-data-binding-row.txt, C:\Projects\clio\clio\docs\commands\create-data-binding.md, C:\Projects\clio\clio\docs\commands\add-data-binding-row.md, C:\Projects\clio\clio\Commands.md, C:\Projects\clio\.codex\workspace-diary.md
Impact: Future data-binding work can rely on a stable row contract for lookup and image-reference values across CLI and MCP, with regression coverage for explicit display text, runtime backfill, and offline validation failures.

## 2026-03-16 16:34 – Add install-application MCP tool
Context: User requested MCP exposure for the `install-application` command with environment-based invocation, command-aligned docs, and coverage.
Decision: Added a dedicated `install-application` MCP tool and prompt, mapped `name`, `report-path`, `check-compilation-errors`, and `environment-name` into `InstallApplicationOptions`, and changed `InstallApplicationCommand` to log via `ILogger` instead of raw console writes so CLI and MCP both return structured success and failure messages.
Discovery: The local `clio` debug output is often file-locked by a running process, so focused validation is more reliable when tests are executed with isolated `OutDir` and `BaseIntermediateOutputPath` values; the success E2E can verify a real side effect by asserting the requested install report file is created.
Files: C:\Projects\clio\clio\Command\InstallApplicationOptions.cs, C:\Projects\clio\clio\Command\McpServer\Tools\InstallApplicationTool.cs, C:\Projects\clio\clio\Command\McpServer\Prompts\InstallApplicationPrompt.cs, C:\Projects\clio\clio.tests\Command\InstallApplicationCommandTests.cs, C:\Projects\clio\clio.tests\Command\McpServer\InstallApplicationToolTests.cs, C:\Projects\clio\clio.mcp.e2e\InstallApplicationToolE2ETests.cs, C:\Projects\clio\clio.mcp.e2e\Support\Configuration\McpE2ESettings.cs, C:\Projects\clio\clio.mcp.e2e\appsettings.example.json, C:\Projects\clio\clio.mcp.e2e\appsettings.json, C:\Projects\clio\clio\help\en\install-application.txt, C:\Projects\clio\clio\docs\commands\install-application.md, C:\Projects\clio\clio\Commands.md, C:\Projects\clio\.codex\workspace-diary.md
Impact: Future environment-sensitive MCP tools can reuse the same pattern for environment-only tool contracts, logger-backed command output, report-file side-effect assertions, and isolated-output test execution when local debug binaries are locked.

## 2026-03-18 00:00 – Remove cliogate analyzer project dependency
Context: User reported `NETSDK1005` when building `cliogate` because the project imported `Clio.Analyzers` as an analyzer reference even though the analyzer project does not target `netstandard2.0`.
Decision: Removed the analyzer `ProjectReference` from `cliogate/cliogate.csproj` so `cliogate` no longer depends on `Clio.Analyzers` during restore or build.
Discovery: `clio` already scopes its analyzer reference differently, while `cliogate` was the only production project directly coupling its restore graph to the analyzer target frameworks.
Files: C:\Projects\clio\cliogate\cliogate.csproj, C:\Projects\clio\.codex\workspace-diary.md
Impact: `cliogate` restore/build no longer fails on analyzer target mismatch; existing package compatibility/reference warnings remain unchanged and are unrelated to the removed analyzer dependency.

## 2026-03-20 17:35 – Add restore-db disable-reset-password option
Context: User asked for `restore-db` to support the same `--disable-reset-password` option and behavior already used by `deploy-creatio`.
Decision: Added hidden `RestoreDbCommandOptions.DisableResetPassword` with a real default initializer, exposed `ICreatioInstallerService.TryDisableForcedPasswordReset`, and invoked that shared helper after successful restore-db flows instead of duplicating the password-reset logic.
Discovery: CommandLine `[Option(Default = ...)]` alone does not cover programmatic option construction in tests or MCP wrappers, so `RestoreDbCommandOptions` needed `DisableResetPassword = true` to keep CLI, MCP, and in-process execution aligned.
Files: C:\Projects\clio\clio\Command\RestoreDb.cs, C:\Projects\clio\clio\Command\CreatioInstallCommand\CreatioInstallerService.cs, C:\Projects\clio\clio\Command\McpServer\Tools\RestoreDbTool.cs, C:\Projects\clio\clio\Command\McpServer\Prompts\RestoreDbPrompt.cs, C:\Projects\clio\clio.tests\Command\RestoreDb.LocalServer.Tests.cs, C:\Projects\clio\clio.tests\Command\RestoreDb.Tests.cs, C:\Projects\clio\clio.tests\Command\McpServer\RestoreDbToolTests.cs, C:\Projects\clio\clio.mcp.e2e\RestoreDbToolE2ETests.cs, C:\Projects\clio\clio\help\en\restore-db.txt, C:\Projects\clio\clio\docs\commands\restore-db.md, C:\Projects\clio\clio\Commands.md, C:\Projects\clio\.codex\workspace-diary.md
Impact: Future restore-db changes can reuse the existing deploy-creatio password-reset flow consistently across CLI, MCP, tests, and docs without reintroducing divergent defaults.

## 2026-03-20 15:35 – Make set-fsm-config cross-platform and remove direct System.IO usage
Context: User asked for `clio fsm` support on Windows, macOS, and Linux, specifically by changing `SetFsmConfigCommand.cs` and addressing the local `CLIO003` analyzer warning.
Decision: Injected `Clio.Common.IFileSystem` into `SetFsmConfigCommand`, replaced direct `System.IO` file/path access with the repo abstraction, resolved config files by candidate order (`Web.config` and `Terrasoft.WebHost.dll.config`) instead of hard-coding one filename, and let non-Windows flows use registered `EnvironmentPath` or direct `--physicalPath`.
Discovery: `turn-fsm` inherits this behavior because it delegates config changes to `SetFsmConfigCommand`, but the existing MCP FSM tool contract did not need changes because it already wraps `turn-fsm` rather than `set-fsm-config`; the stale docs/help were still advertising `--environmentName` even though the real option is `-e/--Environment`.
Files: C:\Projects\clio\clio\Command\SetFsmConfigCommand.cs, C:\Projects\clio\clio.tests\Command\SetFsmConfigCommand.Tests.cs, C:\Projects\clio\clio.tests\Command\TurnFsmCommand.LoginRetry.Tests.cs, C:\Projects\clio\clio.tests\Command\McpServer\FsmModeToolTests.cs, C:\Projects\clio\clio\help\en\set-fsm-config.txt, C:\Projects\clio\clio\help\en\turn-fsm.txt, C:\Projects\clio\clio\docs\commands\set-fsm-config.md, C:\Projects\clio\clio\docs\commands\turn-fsm.md, C:\Projects\clio\clio\Commands.md, C:\Projects\clio\.codex\workspace-diary.md
Impact: Future FSM work can rely on path-based local config resolution across all three OSes, keep `CLIO003` clear in the command implementation, and use the new markdown docs/help as the canonical CLI contract.

## 2026-03-20 18:12 – Make link-from-repository environment linking cross-platform
Context: User reported that `clio l4r -e` only handled `HandleLinkingByEnvName` correctly on Windows and needed equivalent behavior on Linux and macOS.
Decision: Changed `Link4RepoCommand` to resolve package directories from registered `EnvironmentPath` values on all platforms, preserved the legacy Windows IIS-based fallback for older registrations, and added focused tests around environment-based resolution instead of symlink side effects.
Discovery: The existing `LinkFromRepositoryTool` MCP surface already matched the command contract and did not require code changes, but this command was missing dedicated help and markdown documentation that now describe the cross-platform `-e/--Environment` behavior.
Files: C:\Projects\clio\clio\Command\Link4RepoCommand.cs, C:\Projects\clio\clio.tests\Command\Link4RepoCommand.Tests.cs, C:\Projects\clio\clio.tests\Command\McpServer\LinkFromRepositoryToolTests.cs, C:\Projects\clio\clio\help\en\link-from-repository.txt, C:\Projects\clio\clio\docs\commands\link-from-repository.md, C:\Projects\clio\clio\Commands.md, C:\Projects\clio\.codex\workspace-diary.md
Impact: Registered environments with a valid `EnvironmentPath` now support `clio l4r -e` across Windows, Linux, and macOS, while Windows users still retain compatibility with the previous host-discovery fallback.

## 2026-03-21 12:40 – Add restore-db ZIP/template restore flow
Context: User needed `clio rdb` to restore directly from PostgreSQL ZIP backups and to support `--as-template` so a ZIP can create only a reusable template database.
Decision: Added direct backup handling in `RestoreDbCommand` for PostgreSQL `.backup` files and ZIP packages, introduced `--as-template`, split template creation into a reusable `ICreatioInstallerService.EnsurePgTemplate` path, and kept local-server restore behavior aligned for both normal restores and template-only mode.
Discovery: The previous command path only understood ZIP backups inside the `--dbServerName` local-server branch and would dereference `env.DbServer.Uri` when a ZIP was passed without that option; MCP, tests, and command docs all needed updates because `dbName` is no longer required when `--as-template` is used.
Files: C:\Projects\clio\clio\Command\RestoreDb.cs, C:\Projects\clio\clio\Command\CreatioInstallCommand\CreatioInstallerService.cs, C:\Projects\clio\clio\Command\McpServer\Tools\RestoreDbTool.cs, C:\Projects\clio\clio\Command\McpServer\Prompts\RestoreDbPrompt.cs, C:\Projects\clio\clio.tests\Command\RestoreDb.Tests.cs, C:\Projects\clio\clio.tests\Command\RestoreDb.LocalServer.Tests.cs, C:\Projects\clio\clio.tests\Command\McpServer\RestoreDbToolTests.cs, C:\Projects\clio\clio.mcp.e2e\RestoreDbToolE2ETests.cs, C:\Projects\clio\clio\help\en\restore-db.txt, C:\Projects\clio\clio\docs\commands\restore-db.md, C:\Projects\clio\clio\Commands.md, C:\Projects\clio\.codex\workspace-diary.md
Impact: Future restore-db work can treat PostgreSQL ZIP packages as first-class inputs, create reusable templates without provisioning a target database, and rely on aligned CLI, MCP, and test coverage for that contract.

## 2026-03-21 13:05 – Apply password-reset flow to ZIP template restore
Context: After the ZIP/template restore work, user reported that the post-restore password-reset handling was not being applied consistently for ZIP-based template creation.
Decision: Changed restore-db template flows to resolve and log the effective template database name, exposed `ICreatioInstallerService.EnsurePgTemplateAndGetName`, and invoked `TryDisableForcedPasswordReset` against the actual template database for both direct ZIP and local PostgreSQL template restores.
Discovery: The original ZIP restore path already applied password-reset handling for concrete database restores, but template-only mode had no reliable way to know the created template database name, so the follow-up script was skipped entirely.
Files: C:\Projects\clio\clio\Command\RestoreDb.cs, C:\Projects\clio\clio\Command\CreatioInstallCommand\CreatioInstallerService.cs, C:\Projects\clio\clio.tests\Command\RestoreDb.Tests.cs, C:\Projects\clio\clio.tests\Command\RestoreDb.LocalServer.Tests.cs, C:\Projects\clio\.codex\workspace-diary.md
Impact: ZIP-backed template creation now reports the effective template name and runs the same password-reset follow-up logic as a normal restore, preventing another silent mismatch between restore modes.

## 2026-03-21 13:18 – Allow dropping PostgreSQL template databases
Context: User hit a restore-db failure when `--drop-if-exists --as-template` tried to replace an existing PostgreSQL template and PostgreSQL rejected dropping a template database directly.
Decision: Updated the shared `Postgres.DropDb` helper to clear `pg_database.datistemplate` before terminating sessions and issuing `DROP DATABASE`, so all template-replacement flows can reuse the same drop path.
Discovery: Both restore-db and installer service template-refresh flows already relied on `Postgres.DropDb`, so fixing the low-level helper removed the bug in every caller without needing MCP or CLI contract changes.
Files: C:\Projects\clio\clio\Common\db\Postgres.cs, C:\Projects\clio\.codex\workspace-diary.md
Impact: Existing PostgreSQL templates can now be replaced safely with `--drop-if-exists`, avoiding the previous `[ERROR] cannot drop a template database` failure during template refresh.

