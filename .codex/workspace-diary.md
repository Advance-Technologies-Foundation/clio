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
Files: Clio.Analyzers/DirectProcessUsageAnalyzer.cs, Clio.Analyzers.Tests/DirectProcessUsageAnalyzerTests.cs, clio/Common/ProcessExecutor.cs, clio/Command/CreatioInstallCommand/CreatioInstallerService.cs, clio.tests/CreatioInstallerServiceTests.cs, .codex/workspace-diary.md
Impact: CLIO004 policy is now enforced, targeted service migration is verified, and future cleanup can proceed incrementally from analyzer diagnostics.

## 2026-02-23 - Expand CLIO002 to detect Console.Out and Console.OutputEncoding usage
Context: User reported ConsoleOutputAnalyzer missed Console.Out-based async writes and output stream/encoding direct access.
Decision: Extended ConsoleOutputAnalyzer to inspect member access and invocation chains so it now reports CLIO002 for Console.Out, Console.OutputEncoding, and Console.Out.* invocations (e.g., WriteAsync), while preserving test-assembly and Clio.ConsoleLogger exclusions.
Discovery: Invocation-only matching on methods declared by System.Console misses Console.Out.WriteAsync because the method is declared on TextWriter.
Files: Clio.Analyzers/ConsoleOutputAnalyzer.cs, Clio.Analyzers.Tests/ConsoleOutputAnalyzerTests.cs, .codex/workspace-diary.md
Impact: CLIO002 now enforces logger-only output policy for additional real-world console patterns previously slipping through.

## 2026-02-23 - Remove CLIO004 ProcessStartInfo usage in RestoreDb
Context: User requested addressing CLIO004 warning for direct ProcessStartInfo usage in RestoreDb.cs.
Decision: Injected IProcessExecutor into RestoreDbCommand and replaced ExecutePgRestoreCommand low-level Process/ProcessStartInfo logic with ProcessExecutionOptions + ExecuteWithRealtimeOutputAsync, preserving debug and throttled progress logging behavior.
Discovery: Constructor-based tests for RestoreDb required DI argument updates to include IProcessExecutor substitute.
Files: clio/Command/RestoreDb.cs, clio.tests/Command/RestoreDb.LocalServer.Tests.cs, clio.tests/Command/RestoreDb.Tests.cs, .codex/workspace-diary.md
Impact: RestoreDb no longer triggers CLIO004 in this file and pg_restore execution now follows shared process abstraction.

## 2026-02-23 - Clear CLIO warnings in RegisterCommand.cs
Context: User requested resolving all CLIO diagnostics reported in RegisterCommand.cs.
Decision: Reworked RegisterCommand/UnregisterCommand to use ILogger + IProcessExecutor + System.IO.Abstractions IFileSystem, removed direct Console/Process/System.IO usage, switched Program verb mapping from CreateCommand<> to Resolve<> for DI-backed construction, and replaced CreatioEnvironment manual construction with AppContext.BaseDirectory path resolution.
Discovery: ICreatioEnvironment is internal, so it cannot appear in a public constructor signature; AppContext.BaseDirectory keeps behavior and avoids CLIO001.
Files: clio/Command/RegisterCommand.cs, clio/Program.cs, .codex/workspace-diary.md
Impact: RegisterCommand.cs no longer emits CLIO001/002/003/004 diagnostics while preserving register/unregister command behavior.

## 2026-02-23 - Add tests for RegisterCommand/UnregisterCommand after abstraction migration
Context: User requested test coverage for RegisterCommand after replacing direct Console/Process/System.IO usage with abstractions.
Decision: Added RegisterCommandTests with cross-platform-safe cases: non-Windows register behavior, unregister success path (verifying process commands), and unregister exception handling.
Discovery: RegisterCommand OS-branch behavior is platform-dependent; non-Windows test is intentionally skipped on Windows to keep suite executable across OSes.
Files: clio.tests/Command/RegisterCommand.Tests.cs, .codex/workspace-diary.md
Impact: Command now has direct unit coverage validating logger and process-executor interactions introduced by abstraction refactor.

## 2026-02-23 - Register RegisterCommand tests use DI-resolved SUT and teardown call reset
Context: User requested RegisterCommand tests to register test doubles through AdditionalRegistrations, resolve system-under-test from DI, and clear mock interactions between tests.
Decision: Added explicit DI registrations for RegisterCommand and UnregisterCommand in test AdditionalRegistrations so setup resolves both commands from the container; kept logger/process-executor substitutes registered as singletons; retained TearDown call reset with ClearReceivedCalls on both substitutes.
Discovery: Base test module does not auto-register concrete command types without interface mapping, so container resolution fails unless commands are explicitly added for this test fixture.
Files: clio.tests/Command/RegisterCommand.Tests.cs, .codex/workspace-diary.md
Impact: RegisterCommand test fixture now matches repository DI testing style and avoids cross-test interference from stale substitute invocations.

## 2026-02-23 - Align command-test conventions in RegisterCommand tests and AGENTS
Context: User requested removing redundant UnitTests category from a BaseCommandTests-based fixture and documenting command test conventions.
Decision: Removed `[Category("UnitTests")]` from `RegisterCommandTests` and added a new `## Command tests` section in `AGENTS.md` defining BaseCommandTests usage, DI registration via `AdditionalRegistrations`, container-resolved SUT setup, and teardown call clearing.
Discovery: `BaseCommandTests<TOptions>` already provides command-test categorization context, so explicit UnitTests category is unnecessary and was intentionally removed.
Files: clio.tests/Command/RegisterCommand.Tests.cs, AGENTS.md, .codex/workspace-diary.md
Impact: Test fixture now follows the agreed command-test style and repository guidance now explicitly captures the expected DI-based testing pattern.

## 2026-02-23 - Enforce process exit-code checks in register/unregister commands
Context: User requested register and unregister commands to fail with non-zero exit code and clear error messaging when process execution fails.
Decision: Replaced fire-and-forget string-only process calls with `IProcessExecutor.ExecuteAndCaptureAsync` checks in both RegisterCommand and UnregisterCommand, validating `Started` and `ExitCode == 0` before continuing.
Discovery: Existing unregister help text claimed idempotent success when registry keys are missing; this no longer matches behavior once non-zero exit codes are treated as failures.
Files: clio/Command/RegisterCommand.cs, clio.tests/Command/RegisterCommand.Tests.cs, clio/help/en/register.txt, clio/help/en/unregister.txt, clio/Commands.md, .codex/workspace-diary.md
Impact: Registry command failures are now surfaced to users immediately with error logs and non-zero command exit codes; tests and docs now reflect enforced failure semantics.

## 2026-02-23 - Add dedicated UnregisterCommand test fixture
Context: User requested explicit unit tests for UnregisterCommand.
Decision: Split unregister scenarios out of RegisterCommandTests and created `UnregisterCommandTests : BaseCommandTests<UnregisterOptions>` with DI-based setup, AdditionalRegistrations, and teardown call clearing.
Discovery: Because `UnregisterOptions` is internal, the test fixture class must be internal to avoid CS0060 inconsistent accessibility.
Files: clio.tests/Command/RegisterCommand.Tests.cs, clio.tests/Command/UnregisterCommand.Tests.cs, .codex/workspace-diary.md
Impact: Unregister behavior now has dedicated, focused coverage for success, exception, and non-zero exit code paths while keeping RegisterCommand tests scoped to register behavior.

## 2026-02-23 - Restore RegisterCommand test coverage via OS abstraction
Context: User reported RegisterCommandTests coverage regressed to a single non-Windows case after splitting unregister tests.
Decision: Introduced `IOperationSystem` and injected it into RegisterCommand so tests can deterministically cover Windows/admin and registry import result paths without relying on host OS or privileges.
Discovery: Static `OperationSystem.Current` checks prevented practical unit coverage for success and admin-path failures; abstraction eliminated environment coupling.
Files: clio/Common/System.cs, clio/Command/RegisterCommand.cs, clio.tests/Command/RegisterCommand.Tests.cs, .codex/workspace-diary.md
Impact: RegisterCommand tests now cover non-Windows, no-admin, first import failure, and successful Windows registration paths while keeping tests cross-platform executable.

## 2026-02-23 - Fix full-suite regressions from CreatioPackage logger null and test CWD leakage
Context: User reported 10 failing tests after recent command/test refactors.
Decision: Added logger fallback in CreatioPackage process execution (`_logger ?? ConsoleLogger.Instance`) and hardened `CreatioPkgTests` by wrapping `Environment.CurrentDirectory` and `PATH` mutations in `try/finally` with guaranteed restoration.
Discovery: A null logger in `CreatioPackage.ExecuteDotnetCommand` caused package creation failures, and failing integration tests left process CWD altered, cascading into unrelated YAML scenario test failures.
Files: clio/Package/CreatioPackage.cs, clio.tests/CreatioPkgTests.cs, .codex/workspace-diary.md
Impact: Full `clio.tests` suite now passes again with no cascading environment-state pollution between tests.

## 2026-02-23 - Fix CreateInfrastructure CLIO002/CLIO003 and align create-k8-files docs
Context: User requested clearing specific CLIO002 and CLIO003 diagnostics in CreateInfrastructure.cs.
Decision: Replaced the four flagged deployment-instruction Console.WriteLine calls with ConsoleLogger.Instance.WriteLine, and replaced System.IO.Path usage with Clio.Common.IFileSystem abstraction (GetFilesInfos(...).DirectoryName + NormalizeFilePathByPlatform).
Discovery: Command docs for create-k8-files were missing the existing -p/--path option, so required doc surfaces needed alignment even though runtime behavior did not change.
Files: clio/Command/CreateInfrastructure.cs, clio/help/en/create-k8-files.txt, clio/docs/commands/CreateK8FilesCommand.md, clio/Commands.md, .codex/workspace-diary.md
Impact: Requested CreateInfrastructure CLIO002/CLIO003 warnings are removed and command documentation now reflects current options consistently across help/index/detail docs.


## 2026-02-23 - Fix CLIO003 in InfrastructurePathProvider
Context: User requested removal of CLIO003 warning in InfrastructurePathProvider.cs.
Decision: Removed System.IO.Path usage and switched default infrastructure path composition to a separator-safe string interpolation ({SettingsRepository.AppSettingsFolderPath}/infrastructure).
Discovery: This was a localized CLIO003 issue; no command options or behavior contract changed.
Files: clio/Common/InfrastructurePathProvider.cs, .codex/workspace-diary.md
Impact: InfrastructurePathProvider no longer emits CLIO003 while preserving resolved default path behavior.


## 2026-02-23 - Use abstraction Path.Join in InfrastructurePathProvider
Context: User requested using Path.Join (cross-platform) instead of manual string concatenation in InfrastructurePathProvider.
Decision: Refactored InfrastructurePathProvider to use System.IO.Abstractions.IFileSystem.Path.Join, switched dependent constructors to DI-only usage (removed fallback 
ew InfrastructurePathProvider()), and updated Create/Deploy infrastructure tests accordingly.
Discovery: DeployInfrastructureCommand and related tests required constructor wiring updates when CreateInfrastructureCommand stopped using optional provider fallback; command behavior/docs remained unchanged.
Files: clio/Common/InfrastructurePathProvider.cs, clio/Command/CreateInfrastructure.cs, clio/Command/OpenInfrastructureCommand.cs, clio/Command/DeployInfrastructureCommand.cs, clio.tests/Common/InfrastructurePathProviderTests.cs, clio.tests/Command/CreateInfrastructureCommand.Tests.cs, clio.tests/Command/DeployInfrastructureCommandTests.cs, .codex/workspace-diary.md
Impact: InfrastructurePathProvider now uses cross-platform Path.Join through abstraction and no longer emits CLIO003 for this file, with DI/test graph kept compilable.


## 2026-02-23 - Performance audit hotspots in clio runtime paths
Context: User requested a targeted performance review focused on CPU/memory hotspots, allocation pressure, I/O latency, async contention, and benchmark coverage.
Decision: Performed static hotspot analysis across command/runtime paths and prioritized issues by runtime risk (busy waits, sync-over-async blocking, unbounded parallelism, and high-allocation full-materialization patterns).
Discovery: High-impact risks are concentrated in SafeDeleteDirectory busy-wait loop, deploy/install retry loops using Thread.Sleep, sync-over-async call chains in updater/NuGet/K8/IIS paths, and unbounded Parallel.ForEach in downloader; no benchmark harness references were found.
Files: clio/Common/FileSystem.cs, clio/Command/DeployInfrastructureCommand.cs, clio/Command/CreatioInstallCommand/CreatioInstallerService.cs, clio/AppUpdater.cs, clio/Package/NuGet/NuGetManager.cs, clio/Package/NuGet/NugetPackageRestorer.cs, clio/WebApplication/Downloader.cs, clio/ComposableApplication/ComposableApplicationManager.cs, clio/Common/K8/k8Commands.cs, clio/Common/IIS/WindowsIISAppPoolManager.cs, .codex/workspace-diary.md
Impact: Future optimization work can start from ranked hotspots with concrete remediation paths and benchmark-gap visibility.

## 2026-02-23 - Reviewer B performance regression audit for f3d29c87..HEAD
Context: User requested a performance-focused review of refactor changes between f3d29c87 and HEAD.
Decision: Prgsioritized findin where refactoring introduced measurable algorithmic/resource risks (output buffering growth, sync-over-async blocking, avoidable allocations).
Discovery: ProcessExecutor now unconditionally accumulates all stdout/stderr lines, which increases memory pressure for verbose long-running commands (pg_restore call paths in RestoreDb and CreatioInstallerService). NuGet refactor introduced .Result blocking in manager/restorer call chains and extra immutable materialization in version parsing.
Files: clio/Common/ProcessExecutor.cs, clio/Command/RestoreDb.cs, clio/Command/CreatioInstallCommand/CreatioInstallerService.cs, clio/Package/NuGet/NuGetManager.cs, clio/Package/NuGet/NugetPackageRestorer.cs, clio/Package/NuGet/NugetPackagesProvider.cs, .codex/workspace-diary.md
Impact: Provides targeted remediation points to reduce memory growth during restore operations and remove blocking/allocation overhead in NuGet version resolution.
## 2026-02-23 - Security review of refactor range f3d29c87..HEAD
Context: User requested reviewer-C style security-focused review of refactoring changes between f3d29c87 and HEAD.
Decision: Performed diff-driven analysis centered on process execution, shell invocation, DB restore command construction, and secret handling paths.
Discovery: No critical/high-confidence RCE was introduced; identified medium risk from string-based argument composition in centralized ProcessExecutor usage and low risk from fire-and-forget launch failures being swallowed.
Files: clio/Common/ProcessExecutor.cs, clio/Command/RestoreDb.cs, clio/Command/CreatioInstallCommand/CreatioInstallerService.cs, clio/Command/RegisterCommand.cs, clio/Command/UnregisterCommand.cs, .codex/workspace-diary.md
Impact: Captures concrete security hardening priorities for process execution API and command argument handling in future refactors.

## 2026-02-23 - Refactor review from f3d29c87
Context: User requested a multi-angle review of all committed changes from f3d29c87..HEAD, focused on correctness, maintainability, performance regressions, and security implications.
Decision: Performed parallel agent review (correctness/maintainability, performance, security) and validated findings with local source inspection and test execution.
Discovery: High-confidence regressions are concentrated around process execution semantics (fire-and-forget/start failure handling), kubectl preflight reliability, and stderr routing impact on existing tests; performance risk observed from unconditional process output buffering and sync-over-async NuGet calls; security risk observed in string-concatenated process arguments for pg_restore.
Files: clio/Common/ProcessExecutor.cs, clio/Command/DeployInfrastructureCommand.cs, clio/Command/OpenAppCommand.cs, clio/Common/ConsoleLogger.cs, clio/Package/NuGet/NuGetManager.cs, clio/Package/NuGet/NugetPackageRestorer.cs, clio/Command/RestoreDb.cs, clio/Command/CreatioInstallCommand/CreatioInstallerService.cs, clio.tests/Common/ConsoleLoggerTests.cs, clio.tests/Command/LastCompilationLogCommandTestFixture.cs, .codex/workspace-diary.md
Impact: Provides a prioritized defect/risk list for follow-up fixes after the large refactor and records validated hotspots to accelerate remediation.

Findings

1. High: process-start failures are silently swallowed in the new executor compatibility API, breaking preflight correctness.
   ProcessExecutor.cs:225 (/C:/Projects/clio/clio/Common/ProcessExecutor.cs:225) ignores FireAndForgetAsync result and returns success-like output; ProcessExecutor.cs:286 (/C:/Projects/clio/clio/Common/ProcessExecutor.cs:286) returns Started=false instead of throwing on start failure.
   DeployInfrastructureCommand still relies on exception flow in DeployInfrastructureCommand.cs:96 (/C:/Projects/clio/clio/Command/DeployInfrastructureCommand.cs:96), DeployInfrastructureCommand.cs:99 (/C:/Projects/clio/clio/Command/DeployInfrastructureCommand.cs:99), so missing kubectl can be reported as installed.
2. High: false-success path in browser launch on macOS.
   OpenAppCommand.cs:39 (/C:/Projects/clio/clio/Command/OpenAppCommand.cs:39) uses waitForExit:false; with current executor behavior, launch failure is not propagated, and command can still return 0.
3. Medium: regression in logging stream behavior breaks existing command/test expectations.
   Errors were moved to stderr in ConsoleLogger.cs:93 (/C:/Projects/clio/clio/Common/ConsoleLogger.cs:93), ConsoleLogger.cs:95 (/C:/Projects/clio/clio/Common/ConsoleLogger.cs:95).
   Current suite shows failing assertions tied to this behavior change: ConsoleLoggerTests.cs:19 (/C:/Projects/clio/clio.tests/Common/ConsoleLoggerTests.cs:19), ConsoleLoggerTests.cs:58 (/C:/Projects/clio/clio.tests/Common/ConsoleLoggerTests.cs:58), LastCompilationLogCommandTestFixture.cs:76 (/C:/Projects/clio/clio.tests/Command/
   LastCompilationLogCommandTestFixture.cs:76).
4. Medium: negative performance risk from unconditional full output buffering for long-running processes.
   ProcessExecutor.cs:371 (/C:/Projects/clio/clio/Common/ProcessExecutor.cs:371) appends every output line to in-memory buffers even for realtime flows used by restore/deploy paths: RestoreDb.cs:414 (/C:/Projects/clio/clio/Command/RestoreDb.cs:414), CreatioInstallerService.cs:496 (/C:/Projects/clio/clio/Command/CreatioInstallCommand/
   CreatioInstallerService.cs:496).
   This can create large allocation/GC pressure on verbose pg_restore output.
5. Medium: sync-over-async introduced in NuGet path after async refactor.
   Blocking .Result calls at NuGetManager.cs:209 (/C:/Projects/clio/clio/Package/NuGet/NuGetManager.cs:209) and NugetPackageRestorer.cs:72 (/C:/Projects/clio/clio/Package/NuGet/NugetPackageRestorer.cs:72) can reduce throughput and raise deadlock risk in future hosted/sync-context scenarios.
6. Medium (security): argument injection surface in command-line construction for pg_restore.
   RestoreDb.cs:391 (/C:/Projects/clio/clio/Command/RestoreDb.cs:391) builds raw argument string from config/user-derived fields (Hostname, Username, dbName) and passes through generic string-based executor API (ProcessExecutor.cs:278 (/C:/Projects/clio/clio/Common/ProcessExecutor.cs:278)).
   Use tokenized argument APIs (ArgumentList) and stricter input validation to reduce injection risk.

## 2026-02-26 21:27 – Проектный аудит качества/перф/безопасности
Context: Пользователь запросил общий анализ кода проекта с акцентом на практические риски.
Decision: Выполнен статический обзор по трем направлениям (code quality, performance, security), плюс `dotnet build` и `dotnet test` для подтверждения фактического состояния.
Discovery: Обнаружены 8 падающих тестов в `StartCommand`/`StopCommand`, риск утечки секрета в `NuGetManager.Push` (API key в аргументах процесса), а также системный техдолг по CLIO-анализаторам (952 warning при сборке).
Files: clio/Command/StartCommand.cs, clio/Command/StopCommand.cs, clio/Common/ProcessExecutor.cs, clio/Command/RestoreDb.cs, clio/Package/NuGet/NuGetManager.cs, clio/Package/NuGet/NugetPackageRestorer.cs, clio/Common/FileSystem.cs, clio.tests/Command/StartCommand.Tests.cs, clio.tests/Command/StopCommand.Tests.cs, .codex/workspace-diary.md
Impact: Зафиксирован приоритетный бэклог исправлений для надежности команд запуска/остановки, безопасности вызовов внешних утилит и снижения технического долга по архитектурным правилам.

## 2026-02-28 01:28 – Review skill docs for .github/skills/clio
Context: User requested a review of `.github/skills/clio` with focus on practical defects.
Decision: Compared skill/reference markdown examples and aliases against actual command metadata in `[Verb(...)]` attributes and command option requirements.
Discovery: Found concrete mismatches that can break usage (`env-ui` alias documented as `ui`, invalid minimal examples for `publish-app` and `new-ui-project`), plus a committed `.DS_Store` artifact and plaintext-secret examples without safety note.
Files: .github/skills/clio/SKILL.md, .github/skills/clio/references/commands-reference.md, .github/skills/clio/.DS_Store, clio/Command/EnvManageUiCommand.cs, clio/Command/CreateUiProjectCommand.cs, clio/Command/PublishWorkspaceCommand.cs
Impact: Future updates can prioritize correcting broken examples/aliases first to reduce failed command attempts and avoid accidental secret exposure in copied templates.

## 2026-02-28 01:54 – Apply fixes for .github/skills/clio review findings
Context: User asked to implement fixes after review of `.github/skills/clio`.
Decision: Updated command reference examples/aliases to match current CLI metadata, replaced plaintext credential examples with placeholders plus explicit security note, and removed macOS `.DS_Store` artifact from the skill folder.
Discovery: `publish-app` and `new-ui-project` examples were incomplete for required parameters; `env-ui` alias in reference diverged from actual verb aliases (`gui`, `far`).
Files: .github/skills/clio/references/commands-reference.md, .github/skills/clio/.DS_Store, .codex/workspace-diary.md
Impact: Skill docs now avoid known false examples, reduce credential leakage risk in copy-paste scenarios, and keep repository contents cleaner across platforms.
