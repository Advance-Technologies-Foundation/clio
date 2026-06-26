using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical MCP resources generated from Creatio composable-app skill documentation.
/// </summary>
[McpServerResourceType]
public sealed class ComposableAppSkillGuidanceResources {
	/// <summary>
	/// Returns canonical MCP guidance for Implement or review Creatio data access with ATF.Repository when code needs model-based queries, inserts, updates, or deletes and has access to local Creatio runtime services or to a remote Creatio instance through repository providers. Use when configuring `IDataProvider` or writing CRUD logic through `IAppDataContext`. If the task creates or changes repository models or their members, also use `atf-repository-model-management`.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/guides/atf-repository-dev", Name = "atf-repository-dev-guidance")]
	[Description("Returns canonical MCP guidance for Implement or review Creatio data access with ATF.Repository when code needs model-based queries, inserts, updates, or deletes and has access to local Creatio runtime services or to a remote Creatio instance through repository providers. Use when configuring `IDataProvider` or writing CRUD logic through `IAppDataContext`. If the task creates or changes repository models or their members, also use `atf-repository-model-management`.")]
	public ResourceContents GetAtfRepositoryDevGuide() => ComposableAppSkillResourceCatalog.Get("docs://mcp/guides/atf-repository-dev");

	/// <summary>
	/// Returns the models-and-relations reference for atf-repository-dev.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/atf-repository-dev/models-and-relations", Name = "atf-repository-dev-models-and-relations-reference")]
	[Description("Returns the models-and-relations reference for atf-repository-dev.")]
	public ResourceContents GetAtfRepositoryDevModelsAndRelationsReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/atf-repository-dev/models-and-relations");

	/// <summary>
	/// Returns the package-and-version reference for atf-repository-dev.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/atf-repository-dev/package-and-version", Name = "atf-repository-dev-package-and-version-reference")]
	[Description("Returns the package-and-version reference for atf-repository-dev.")]
	public ResourceContents GetAtfRepositoryDevPackageAndVersionReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/atf-repository-dev/package-and-version");

	/// <summary>
	/// Returns the provider-and-context reference for atf-repository-dev.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/atf-repository-dev/provider-and-context", Name = "atf-repository-dev-provider-and-context-reference")]
	[Description("Returns the provider-and-context reference for atf-repository-dev.")]
	public ResourceContents GetAtfRepositoryDevProviderAndContextReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/atf-repository-dev/provider-and-context");

	/// <summary>
	/// Returns the query-patterns reference for atf-repository-dev.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/atf-repository-dev/query-patterns", Name = "atf-repository-dev-query-patterns-reference")]
	[Description("Returns the query-patterns reference for atf-repository-dev.")]
	public ResourceContents GetAtfRepositoryDevQueryPatternsReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/atf-repository-dev/query-patterns");

	/// <summary>
	/// Returns the write-operations reference for atf-repository-dev.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/atf-repository-dev/write-operations", Name = "atf-repository-dev-write-operations-reference")]
	[Description("Returns the write-operations reference for atf-repository-dev.")]
	public ResourceContents GetAtfRepositoryDevWriteOperationsReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/atf-repository-dev/write-operations");

	/// <summary>
	/// Returns canonical MCP guidance for Manage ATF.Repository model classes when a task names the needed entities, columns, or nested relations and must find existing models, reuse or extend project models, inspect generated model sets, or generate source models with clio. This skill must be used whenever a task creates, extends, merges, or selects ATF.Repository models or their members. Use for requests like `Add models Account (Name, Owner, Owner.DecisionRole, Owner.DecisionRole.Name)` or `Add Account model (Name, Owner, Owner.DecisionRole, Owner.DecisionRole.Name), Order (Number, Account, Opportunity.Title)`.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/guides/atf-repository-model-management", Name = "atf-repository-model-management-guidance")]
	[Description("Returns canonical MCP guidance for Manage ATF.Repository model classes when a task names the needed entities, columns, or nested relations and must find existing models, reuse or extend project models, inspect generated model sets, or generate source models with clio. This skill must be used whenever a task creates, extends, merges, or selects ATF.Repository models or their members. Use for requests like `Add models Account (Name, Owner, Owner.DecisionRole, Owner.DecisionRole.Name)` or `Add Account model (Name, Owner, Owner.DecisionRole, Owner.DecisionRole.Name), Order (Number, Account, Opportunity.Title)`.")]
	public ResourceContents GetAtfRepositoryModelManagementGuide() => ComposableAppSkillResourceCatalog.Get("docs://mcp/guides/atf-repository-model-management");

	/// <summary>
	/// Returns the collision-and-cleanup reference for atf-repository-model-management.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/atf-repository-model-management/collision-and-cleanup", Name = "atf-repository-model-management-collision-and-cleanup-reference")]
	[Description("Returns the collision-and-cleanup reference for atf-repository-model-management.")]
	public ResourceContents GetAtfRepositoryModelManagementCollisionAndCleanupReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/atf-repository-model-management/collision-and-cleanup");

	/// <summary>
	/// Returns the generation-workflow reference for atf-repository-model-management.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/atf-repository-model-management/generation-workflow", Name = "atf-repository-model-management-generation-workflow-reference")]
	[Description("Returns the generation-workflow reference for atf-repository-model-management.")]
	public ResourceContents GetAtfRepositoryModelManagementGenerationWorkflowReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/atf-repository-model-management/generation-workflow");

	/// <summary>
	/// Returns the model-graph-selection reference for atf-repository-model-management.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/atf-repository-model-management/model-graph-selection", Name = "atf-repository-model-management-model-graph-selection-reference")]
	[Description("Returns the model-graph-selection reference for atf-repository-model-management.")]
	public ResourceContents GetAtfRepositoryModelManagementModelGraphSelectionReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/atf-repository-model-management/model-graph-selection");

	/// <summary>
	/// Returns canonical MCP guidance for Write, update, or review tests for functionality built on ATF.Repository, especially when tests should use `MemoryDataProviderMock`, `IDataStore` setup, `AppDataContextFactory.GetAppDataContext(...)`, model-schema registration, seeded in-memory data, and repository assertions after query or save operations. If the task creates or changes repository models or their members, also use `atf-repository-model-management`.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/guides/atf-repository-tests", Name = "atf-repository-tests-guidance")]
	[Description("Returns canonical MCP guidance for Write, update, or review tests for functionality built on ATF.Repository, especially when tests should use `MemoryDataProviderMock`, `IDataStore` setup, `AppDataContextFactory.GetAppDataContext(...)`, model-schema registration, seeded in-memory data, and repository assertions after query or save operations. If the task creates or changes repository models or their members, also use `atf-repository-model-management`.")]
	public ResourceContents GetAtfRepositoryTestsGuide() => ComposableAppSkillResourceCatalog.Get("docs://mcp/guides/atf-repository-tests");

	/// <summary>
	/// Returns the assertion-patterns reference for atf-repository-tests.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/atf-repository-tests/assertion-patterns", Name = "atf-repository-tests-assertion-patterns-reference")]
	[Description("Returns the assertion-patterns reference for atf-repository-tests.")]
	public ResourceContents GetAtfRepositoryTestsAssertionPatternsReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/atf-repository-tests/assertion-patterns");

	/// <summary>
	/// Returns the build-and-verify reference for atf-repository-tests.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/atf-repository-tests/build-and-verify", Name = "atf-repository-tests-build-and-verify-reference")]
	[Description("Returns the build-and-verify reference for atf-repository-tests.")]
	public ResourceContents GetAtfRepositoryTestsBuildAndVerifyReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/atf-repository-tests/build-and-verify");

	/// <summary>
	/// Returns the data-seeding-patterns reference for atf-repository-tests.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/atf-repository-tests/data-seeding-patterns", Name = "atf-repository-tests-data-seeding-patterns-reference")]
	[Description("Returns the data-seeding-patterns reference for atf-repository-tests.")]
	public ResourceContents GetAtfRepositoryTestsDataSeedingPatternsReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/atf-repository-tests/data-seeding-patterns");

	/// <summary>
	/// Returns the memory-provider-setup reference for atf-repository-tests.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/atf-repository-tests/memory-provider-setup", Name = "atf-repository-tests-memory-provider-setup-reference")]
	[Description("Returns the memory-provider-setup reference for atf-repository-tests.")]
	public ResourceContents GetAtfRepositoryTestsMemoryProviderSetupReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/atf-repository-tests/memory-provider-setup");

	/// <summary>
	/// Returns the minimal-test-setup reference for atf-repository-tests.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/atf-repository-tests/minimal-test-setup", Name = "atf-repository-tests-minimal-test-setup-reference")]
	[Description("Returns the minimal-test-setup reference for atf-repository-tests.")]
	public ResourceContents GetAtfRepositoryTestsMinimalTestSetupReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/atf-repository-tests/minimal-test-setup");

	/// <summary>
	/// Returns canonical MCP guidance for "Deliver composable app E2E tests."
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/guides/composable-app-e2e-test-implementation", Name = "composable-app-e2e-test-implementation-guidance")]
	[Description("Returns canonical MCP guidance for \"Deliver composable app E2E tests.\"")]
	public ResourceContents GetComposableAppE2eTestImplementationGuide() => ComposableAppSkillResourceCatalog.Get("docs://mcp/guides/composable-app-e2e-test-implementation");

	/// <summary>
	/// Returns the environment-readiness reference for composable-app-e2e-test-implementation.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/composable-app-e2e-test-implementation/environment-readiness", Name = "composable-app-e2e-test-implementation-environment-readiness-reference")]
	[Description("Returns the environment-readiness reference for composable-app-e2e-test-implementation.")]
	public ResourceContents GetComposableAppE2eTestImplementationEnvironmentReadinessReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/composable-app-e2e-test-implementation/environment-readiness");

	/// <summary>
	/// Returns the flow reference for composable-app-e2e-test-implementation.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/composable-app-e2e-test-implementation/flow", Name = "composable-app-e2e-test-implementation-flow-reference")]
	[Description("Returns the flow reference for composable-app-e2e-test-implementation.")]
	public ResourceContents GetComposableAppE2eTestImplementationFlowReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/composable-app-e2e-test-implementation/flow");

	/// <summary>
	/// Returns the sources reference for composable-app-e2e-test-implementation.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/composable-app-e2e-test-implementation/sources", Name = "composable-app-e2e-test-implementation-sources-reference")]
	[Description("Returns the sources reference for composable-app-e2e-test-implementation.")]
	public ResourceContents GetComposableAppE2eTestImplementationSourcesReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/composable-app-e2e-test-implementation/sources");

	/// <summary>
	/// Returns canonical MCP guidance for Initialize a fresh repository created from this composable-app starter kit for a concrete app/module/slug combination. Use when Codex needs to bootstrap repo scaffolding, root templates, and local environment files for a new composable Creatio application.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/guides/composable-app-repo-bootstrap", Name = "composable-app-repo-bootstrap-guidance")]
	[Description("Returns canonical MCP guidance for Initialize a fresh repository created from this composable-app starter kit for a concrete app/module/slug combination. Use when Codex needs to bootstrap repo scaffolding, root templates, and local environment files for a new composable Creatio application.")]
	public ResourceContents GetComposableAppRepoBootstrapGuide() => ComposableAppSkillResourceCatalog.Get("docs://mcp/guides/composable-app-repo-bootstrap");

	/// <summary>
	/// Returns canonical MCP guidance for Create or review Creatio configuration entity event listeners implemented as `Terrasoft.Core.Entities.Events.BaseEntityEventListener` with `[EntityEventListener(SchemaName = "...")]`. Use when adding or changing entity lifecycle handling such as `OnSaving`, `OnSaved`, `OnInserting`, `OnInserted`, `OnUpdating`, `OnUpdated`, `OnDeleting`, `OnDeleted`, or direct event subscriptions like `entity.Validating` for a specific schema.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/guides/configuration-entity-event-listener", Name = "configuration-entity-event-listener-guidance")]
	[Description("Returns canonical MCP guidance for Create or review Creatio configuration entity event listeners implemented as `Terrasoft.Core.Entities.Events.BaseEntityEventListener` with `[EntityEventListener(SchemaName = \"...\")]`. Use when adding or changing entity lifecycle handling such as `OnSaving`, `OnSaved`, `OnInserting`, `OnInserted`, `OnUpdating`, `OnUpdated`, `OnDeleting`, `OnDeleted`, or direct event subscriptions like `entity.Validating` for a specific schema.")]
	public ResourceContents GetConfigurationEntityEventListenerGuide() => ComposableAppSkillResourceCatalog.Get("docs://mcp/guides/configuration-entity-event-listener");

	/// <summary>
	/// Returns the listener-patterns reference for configuration-entity-event-listener.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/configuration-entity-event-listener/listener-patterns", Name = "configuration-entity-event-listener-listener-patterns-reference")]
	[Description("Returns the listener-patterns reference for configuration-entity-event-listener.")]
	public ResourceContents GetConfigurationEntityEventListenerListenerPatternsReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/configuration-entity-event-listener/listener-patterns");

	/// <summary>
	/// Returns the review-checklist reference for configuration-entity-event-listener.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/configuration-entity-event-listener/review-checklist", Name = "configuration-entity-event-listener-review-checklist-reference")]
	[Description("Returns the review-checklist reference for configuration-entity-event-listener.")]
	public ResourceContents GetConfigurationEntityEventListenerReviewChecklistReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/configuration-entity-event-listener/review-checklist");

	/// <summary>
	/// Returns the validation-patterns reference for configuration-entity-event-listener.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/configuration-entity-event-listener/validation-patterns", Name = "configuration-entity-event-listener-validation-patterns-reference")]
	[Description("Returns the validation-patterns reference for configuration-entity-event-listener.")]
	public ResourceContents GetConfigurationEntityEventListenerValidationPatternsReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/configuration-entity-event-listener/validation-patterns");

	/// <summary>
	/// Returns canonical MCP guidance for Write, update, or review tests for Creatio configuration entity event listeners built on `BaseEntityEventListener`. Use when creating tests that instantiate the listened entity schema, prepare minimal entity data, create a fresh listener instance, invoke the required lifecycle methods such as `OnSaving`, `OnSaved`, `OnInserting`, `OnUpdating`, `OnDeleting`, or validation hooks in the correct order, and verify helper calls or validation results.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/guides/configuration-entity-event-listener-tests", Name = "configuration-entity-event-listener-tests-guidance")]
	[Description("Returns canonical MCP guidance for Write, update, or review tests for Creatio configuration entity event listeners built on `BaseEntityEventListener`. Use when creating tests that instantiate the listened entity schema, prepare minimal entity data, create a fresh listener instance, invoke the required lifecycle methods such as `OnSaving`, `OnSaved`, `OnInserting`, `OnUpdating`, `OnDeleting`, or validation hooks in the correct order, and verify helper calls or validation results.")]
	public ResourceContents GetConfigurationEntityEventListenerTestsGuide() => ComposableAppSkillResourceCatalog.Get("docs://mcp/guides/configuration-entity-event-listener-tests");

	/// <summary>
	/// Returns the review-checklist reference for configuration-entity-event-listener-tests.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/configuration-entity-event-listener-tests/review-checklist", Name = "configuration-entity-event-listener-tests-review-checklist-reference")]
	[Description("Returns the review-checklist reference for configuration-entity-event-listener-tests.")]
	public ResourceContents GetConfigurationEntityEventListenerTestsReviewChecklistReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/configuration-entity-event-listener-tests/review-checklist");

	/// <summary>
	/// Returns the test-patterns reference for configuration-entity-event-listener-tests.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/configuration-entity-event-listener-tests/test-patterns", Name = "configuration-entity-event-listener-tests-test-patterns-reference")]
	[Description("Returns the test-patterns reference for configuration-entity-event-listener-tests.")]
	public ResourceContents GetConfigurationEntityEventListenerTestsTestPatternsReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/configuration-entity-event-listener-tests/test-patterns");

	/// <summary>
	/// Returns the validation-test-patterns reference for configuration-entity-event-listener-tests.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/configuration-entity-event-listener-tests/validation-test-patterns", Name = "configuration-entity-event-listener-tests-validation-test-patterns-reference")]
	[Description("Returns the validation-test-patterns reference for configuration-entity-event-listener-tests.")]
	public ResourceContents GetConfigurationEntityEventListenerTestsValidationTestPatternsReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/configuration-entity-event-listener-tests/validation-test-patterns");

	/// <summary>
	/// Returns canonical MCP guidance for Guidance for composable app development on the Creatio platform. Use when Codex needs to create or modify Creatio packages, Freedom UI pages, client schemas, custom components, data sources, page handlers, or CLIO-based package workflows in local workspaces or source-controlled repositories.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/guides/creatio-composable-app-development", Name = "creatio-composable-app-development-guidance")]
	[Description("Returns canonical MCP guidance for Guidance for composable app development on the Creatio platform. Use when Codex needs to create or modify Creatio packages, Freedom UI pages, client schemas, custom components, data sources, page handlers, or CLIO-based package workflows in local workspaces or source-controlled repositories.")]
	public ResourceContents GetCreatioComposableAppDevelopmentGuide() => ComposableAppSkillResourceCatalog.Get("docs://mcp/guides/creatio-composable-app-development");

	/// <summary>
	/// Returns the official-docs reference for creatio-composable-app-development.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/creatio-composable-app-development/official-docs", Name = "creatio-composable-app-development-official-docs-reference")]
	[Description("Returns the official-docs reference for creatio-composable-app-development.")]
	public ResourceContents GetCreatioComposableAppDevelopmentOfficialDocsReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/creatio-composable-app-development/official-docs");

	/// <summary>
	/// Returns canonical MCP guidance for Create and validate a Creatio Freedom UI section that only hosts an external UI in a single crt.IFrame. Use for composable-app shells (React/SPA UI served by REST endpoint) and reusable section bootstrap in package data.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/guides/creatio-freedom-iframe-section", Name = "creatio-freedom-iframe-section-guidance")]
	[Description("Returns canonical MCP guidance for Create and validate a Creatio Freedom UI section that only hosts an external UI in a single crt.IFrame. Use for composable-app shells (React/SPA UI served by REST endpoint) and reusable section bootstrap in package data.")]
	public ResourceContents GetCreatioFreedomIframeSectionGuide() => ComposableAppSkillResourceCatalog.Get("docs://mcp/guides/creatio-freedom-iframe-section");

	/// <summary>
	/// Returns the creatio-iframe-section-template reference for creatio-freedom-iframe-section.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/creatio-freedom-iframe-section/creatio-iframe-section-template", Name = "creatio-freedom-iframe-section-creatio-iframe-section-template-reference")]
	[Description("Returns the creatio-iframe-section-template reference for creatio-freedom-iframe-section.")]
	public ResourceContents GetCreatioFreedomIframeSectionCreatioIframeSectionTemplateReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/creatio-freedom-iframe-section/creatio-iframe-section-template");

	/// <summary>
	/// Returns canonical MCP guidance for Implement or review Creatio feature-toggle checks in C# package code such as packages/PkgOne/Files/src/cs or packages/PkgOne/src/cs. Use when gating behavior on a Creatio feature flag, adding `Creatio.FeatureToggling.Features.GetIsEnabled(...)`, or moving feature names into `Constants.cs`. If the task includes unit tests, also use feature-toggle-tests.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/guides/feature-toggle", Name = "feature-toggle-guidance")]
	[Description("Returns canonical MCP guidance for Implement or review Creatio feature-toggle checks in C# package code such as packages/PkgOne/Files/src/cs or packages/PkgOne/src/cs. Use when gating behavior on a Creatio feature flag, adding `Creatio.FeatureToggling.Features.GetIsEnabled(...)`, or moving feature names into `Constants.cs`. If the task includes unit tests, also use feature-toggle-tests.")]
	public ResourceContents GetFeatureToggleGuide() => ComposableAppSkillResourceCatalog.Get("docs://mcp/guides/feature-toggle");

	/// <summary>
	/// Returns the constants-pattern reference for feature-toggle.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/feature-toggle/constants-pattern", Name = "feature-toggle-constants-pattern-reference")]
	[Description("Returns the constants-pattern reference for feature-toggle.")]
	public ResourceContents GetFeatureToggleConstantsPatternReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/feature-toggle/constants-pattern");

	/// <summary>
	/// Returns the implementation-patterns reference for feature-toggle.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/feature-toggle/implementation-patterns", Name = "feature-toggle-implementation-patterns-reference")]
	[Description("Returns the implementation-patterns reference for feature-toggle.")]
	public ResourceContents GetFeatureToggleImplementationPatternsReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/feature-toggle/implementation-patterns");

	/// <summary>
	/// Returns the runtime-behavior reference for feature-toggle.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/feature-toggle/runtime-behavior", Name = "feature-toggle-runtime-behavior-reference")]
	[Description("Returns the runtime-behavior reference for feature-toggle.")]
	public ResourceContents GetFeatureToggleRuntimeBehaviorReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/feature-toggle/runtime-behavior");

	/// <summary>
	/// Returns canonical MCP guidance for Write, update, or review unit tests for Creatio feature-toggle behavior in package test projects such as tests/PkgOne or package-local test folders. Use when mocking a feature state with `FeatureRequest` and `Creatio.FeatureToggling.TestKit.FeatureStub.Setup(...)`, adding enabled and disabled coverage, or moving feature names into `Constants.cs`. Pair with feature-toggle when production code changes.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/guides/feature-toggle-tests", Name = "feature-toggle-tests-guidance")]
	[Description("Returns canonical MCP guidance for Write, update, or review unit tests for Creatio feature-toggle behavior in package test projects such as tests/PkgOne or package-local test folders. Use when mocking a feature state with `FeatureRequest` and `Creatio.FeatureToggling.TestKit.FeatureStub.Setup(...)`, adding enabled and disabled coverage, or moving feature names into `Constants.cs`. Pair with feature-toggle when production code changes.")]
	public ResourceContents GetFeatureToggleTestsGuide() => ComposableAppSkillResourceCatalog.Get("docs://mcp/guides/feature-toggle-tests");

	/// <summary>
	/// Returns the constants-and-fixture-pattern reference for feature-toggle-tests.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/feature-toggle-tests/constants-and-fixture-pattern", Name = "feature-toggle-tests-constants-and-fixture-pattern-reference")]
	[Description("Returns the constants-and-fixture-pattern reference for feature-toggle-tests.")]
	public ResourceContents GetFeatureToggleTestsConstantsAndFixturePatternReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/feature-toggle-tests/constants-and-fixture-pattern");

	/// <summary>
	/// Returns the feature-stub-pattern reference for feature-toggle-tests.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/feature-toggle-tests/feature-stub-pattern", Name = "feature-toggle-tests-feature-stub-pattern-reference")]
	[Description("Returns the feature-stub-pattern reference for feature-toggle-tests.")]
	public ResourceContents GetFeatureToggleTestsFeatureStubPatternReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/feature-toggle-tests/feature-stub-pattern");

	/// <summary>
	/// Returns the test-coverage-checklist reference for feature-toggle-tests.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/feature-toggle-tests/test-coverage-checklist", Name = "feature-toggle-tests-test-coverage-checklist-reference")]
	[Description("Returns the test-coverage-checklist reference for feature-toggle-tests.")]
	public ResourceContents GetFeatureToggleTestsTestCoverageChecklistReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/feature-toggle-tests/test-coverage-checklist");

	/// <summary>
	/// Returns canonical MCP guidance for Implement or review Creatio system-setting access in C# package code such as packages/PkgOne/Files/src/cs or packages/PkgOne/src/cs. Use when reading a system setting with `Terrasoft.Core.Configuration.SysSettings.TryGetValue(...)`, `GetValue(...)`, or `GetDefValue(...)`, introducing or reusing `Constants.SysSettingCodes.&lt;SettingName&gt;`, or replacing inline sys-setting code strings with package constants. If the task includes unit tests, also use sys-setting-tests.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/guides/sys-setting", Name = "sys-setting-guidance")]
	[Description("Returns canonical MCP guidance for Implement or review Creatio system-setting access in C# package code such as packages/PkgOne/Files/src/cs or packages/PkgOne/src/cs. Use when reading a system setting with `Terrasoft.Core.Configuration.SysSettings.TryGetValue(...)`, `GetValue(...)`, or `GetDefValue(...)`, introducing or reusing `Constants.SysSettingCodes.<SettingName>`, or replacing inline sys-setting code strings with package constants. If the task includes unit tests, also use sys-setting-tests.")]
	public ResourceContents GetSysSettingGuide() => ComposableAppSkillResourceCatalog.Get("docs://mcp/guides/sys-setting");

	/// <summary>
	/// Returns the access-patterns reference for sys-setting.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/sys-setting/access-patterns", Name = "sys-setting-access-patterns-reference")]
	[Description("Returns the access-patterns reference for sys-setting.")]
	public ResourceContents GetSysSettingAccessPatternsReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/sys-setting/access-patterns");

	/// <summary>
	/// Returns the constants-pattern reference for sys-setting.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/sys-setting/constants-pattern", Name = "sys-setting-constants-pattern-reference")]
	[Description("Returns the constants-pattern reference for sys-setting.")]
	public ResourceContents GetSysSettingConstantsPatternReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/sys-setting/constants-pattern");

	/// <summary>
	/// Returns the review-checklist reference for sys-setting.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/sys-setting/review-checklist", Name = "sys-setting-review-checklist-reference")]
	[Description("Returns the review-checklist reference for sys-setting.")]
	public ResourceContents GetSysSettingReviewChecklistReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/sys-setting/review-checklist");

	/// <summary>
	/// Returns canonical MCP guidance for Write, update, or review unit tests for Creatio system-setting behavior in package test projects such as tests/PkgOne or package-local test folders. Use when decorating a test class with `[MockSettings(RequireMock.All)]`, overriding `SetupSysSettings()`, configuring `FakeSysSettings`, mocking `FakeSysSettingsEngine` for `GetValue`, `GetDefValue`, or `TryGetValue`, or replacing inline sys-setting code strings with `Constants.SysSettingCodes`. Pair with sys-setting when production code changes.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/guides/sys-setting-tests", Name = "sys-setting-tests-guidance")]
	[Description("Returns canonical MCP guidance for Write, update, or review unit tests for Creatio system-setting behavior in package test projects such as tests/PkgOne or package-local test folders. Use when decorating a test class with `[MockSettings(RequireMock.All)]`, overriding `SetupSysSettings()`, configuring `FakeSysSettings`, mocking `FakeSysSettingsEngine` for `GetValue`, `GetDefValue`, or `TryGetValue`, or replacing inline sys-setting code strings with `Constants.SysSettingCodes`. Pair with sys-setting when production code changes.")]
	public ResourceContents GetSysSettingTestsGuide() => ComposableAppSkillResourceCatalog.Get("docs://mcp/guides/sys-setting-tests");

	/// <summary>
	/// Returns the coverage-checklist reference for sys-setting-tests.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/sys-setting-tests/coverage-checklist", Name = "sys-setting-tests-coverage-checklist-reference")]
	[Description("Returns the coverage-checklist reference for sys-setting-tests.")]
	public ResourceContents GetSysSettingTestsCoverageChecklistReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/sys-setting-tests/coverage-checklist");

	/// <summary>
	/// Returns the mock-settings-attribute reference for sys-setting-tests.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/sys-setting-tests/mock-settings-attribute", Name = "sys-setting-tests-mock-settings-attribute-reference")]
	[Description("Returns the mock-settings-attribute reference for sys-setting-tests.")]
	public ResourceContents GetSysSettingTestsMockSettingsAttributeReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/sys-setting-tests/mock-settings-attribute");

	/// <summary>
	/// Returns the setup-sys-settings-pattern reference for sys-setting-tests.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/references/sys-setting-tests/setup-sys-settings-pattern", Name = "sys-setting-tests-setup-sys-settings-pattern-reference")]
	[Description("Returns the setup-sys-settings-pattern reference for sys-setting-tests.")]
	public ResourceContents GetSysSettingTestsSetupSysSettingsPatternReference() => ComposableAppSkillResourceCatalog.Get("docs://mcp/references/sys-setting-tests/setup-sys-settings-pattern");

}

/// <summary>
/// In-memory catalog for Creatio composable-app skill MCP resources.
/// </summary>
internal static class ComposableAppSkillResourceCatalog {
	private static readonly IReadOnlyDictionary<string, ComposableAppSkillResourceEntry> EntriesByUri = CreateEntries()
		.ToDictionary(entry => entry.Article.Uri, StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Returns every generated composable-app skill MCP resource entry.
	/// </summary>
	internal static IReadOnlyList<ComposableAppSkillResourceEntry> GetEntries() => EntriesByUri.Values
		.OrderBy(entry => entry.Article.Uri, StringComparer.OrdinalIgnoreCase)
		.ToArray();

	/// <summary>
	/// Returns top-level guide resources that should be exposed through get-guidance.
	/// </summary>
	internal static IReadOnlyList<ComposableAppSkillResourceEntry> GetGuides() => GetEntries()
		.Where(entry => entry.IsGuide)
		.ToArray();

	/// <summary>
	/// Resolves one generated resource by URI.
	/// </summary>
	internal static TextResourceContents Get(string uri) => EntriesByUri[uri].Article;

	private static IReadOnlyList<ComposableAppSkillResourceEntry> CreateEntries() => [
		Create(
			"atf-repository-dev",
			null,
			"docs://mcp/guides/atf-repository-dev",
			"Implement or review Creatio data access with ATF.Repository when code needs model-based queries, inserts, updates, or deletes and has access to local Creatio runtime services or to a remote Creatio instance through repository providers. Use when configuring `IDataProvider` or writing CRUD logic through `IAppDataContext`. If the task creates or changes repository models or their members, also use `atf-repository-model-management`.",
			true,
			"""
# ATF.Repository Development

## Use This Skill When

- You need to read data with ATF.Repository model queries.
- You need to create, update, or delete records through repository models.
- You have `UserConnection` available directly, or you can register repository services in the package container.
- You need to access Creatio remotely through `RemoteDataProvider`.
- You need to write repository code against existing project models.

## Non-Negotiable Rules

- Every repository model must inherit `ATF.Repository.BaseModel`.
- Every repository model must be marked with `[Schema("<EntitySchemaName>")]`.
- Every mapped scalar column must use `[SchemaProperty("<ColumnName>")]`.
- Property CLR types must match the connected Creatio column types.
- Do not use `Nullable<T>` or `T?` in repository models. Nullable model properties can cause runtime errors.
- Navigation properties for lookups and details must be `virtual`.
- Prefer `IAppDataContext` as the primary application-facing API because it creates tracked models, exposes `Models<T>()`, and applies changes through `Save()`.
- If a task creates, extends, or selects repository models, apply `atf-repository-model-management` first instead of handcrafting models here.
- Keep repository creation close to composition root boundaries. Business logic should depend on injected abstractions, not construct remote providers ad hoc inside deep domain code.
- Match the provider pattern to the host application. Package code, tests, and external utilities may construct or inject providers differently, but feature logic should still depend on repository abstractions rather than on ad hoc transport details.
- Stay within the supported `Models<T>()` query surface documented in `references/query-patterns.md`.
- `RemoteDataProvider` is not `IDisposable`. Do not wrap it in `using` or `await using`.
- For small remote read-only reports, prefer tiny hand-authored models over broad generated model sets when the task only needs a few scalar fields or one clearly defined reverse relation.
- When the feature reads child rows from an already loaded master model, prefer a `DetailProperty` relation over issuing a second top-level query and grouping client-side.

## Standard Workflow

1. Confirm whether existing models are sufficient; if not, apply `atf-repository-model-management`.
2. Create or review the model class and its attributes.
3. Decide whether the feature needs local Creatio access (`LocalDataProvider`) or remote access (`RemoteDataProvider`).
4. Create or inject `IDataProvider` at the infrastructure boundary.
5. Create `IAppDataContext` from the current `IDataProvider` close to the operation that uses it.
6. Query with `Models<T>()` or load a single tracked model.
7. If the task is a console/reporting utility, keep models minimal and favor relation traversal from the loaded model graph.
8. Apply create, update, or delete changes to models.
9. Persist changes with `Save()` and check the result.

## References

Read only what you need:

- `references/package-and-version.md`: package acquisition rules, version checks, and validating the current `IAppDataContext` API surface
- `references/models-and-relations.md`: minimal model pattern plus direct, reverse, and many-to-many relation mapping
- `references/provider-and-context.md`: local provider creation, remote provider creation, DI guidance, and standalone console-app patterns
- `references/query-patterns.md`: querying with `Models<T>()`, allowed LINQ surface, filtering, ordering, paging, and loading tracked models
- `references/write-operations.md`: insert, update, delete, and save-result handling

Suggested loading order:

1. `references/models-and-relations.md` when you need to create or review model mappings.
2. `references/provider-and-context.md` when you need local DI setup or remote utility/console setup.
3. `references/query-patterns.md` or `references/write-operations.md` only for the operation style you are implementing.
4. `references/package-and-version.md` only when package acquisition or API-surface validation is relevant.

## Review Checklist

1. Model inherits `BaseModel` and has `[Schema(...)]`.
2. Every mapped property uses the correct repository attribute.
3. Lookup/detail navigation properties are `virtual`.
4. Scalar property types match schema column types.
5. Nullable model property types are not used.
6. `IDataProvider` creation matches the execution context: local vs remote.
7. DI registers `IDataProvider`, not `IAppDataContext`.
8. `IAppDataContext` is created from `IDataProvider` close to the usage boundary and reused through the operation.
9. Query code stays within the allowed `Models<T>()` LINQ surface.
10. Reverse relations use `DetailProperty(nameof(DetailModel.ForeignKeyProperty))`, not the raw schema column name.
11. Console/reporting code does not issue avoidable second top-level queries for data already reachable through a modeled relation.
12. Insert/update/delete logic calls `Save()` and checks the result when `IAppDataContext` is used.
13. Generated model namespaces or type names do not create unresolved ambiguity with framework or project types.

## What To Report Back

- Files changed, with one-line reason per file
- Which models or relations were added or updated
- How `IDataProvider` and `IAppDataContext` were created or injected
- Which query or CRUD pattern was used
- Tests added or updated, or the exact reason tests were not changed
- Build/test commands run, or the precise blocker if they were not run
"""
		),
		Create(
			"atf-repository-dev",
			"models-and-relations",
			"docs://mcp/references/atf-repository-dev/models-and-relations",
			"Reference resource for atf-repository-dev: models-and-relations.",
			false,
			"""
# Models And Relations

## What A Model Is

A repository model is a C# class that maps one Creatio entity schema to a tracked object graph.

Rules:

- Inherit `BaseModel`
- Mark the class with `[Schema("<EntitySchemaName>")]`
- Map scalar columns with `[SchemaProperty("<ColumnName>")]`
- Keep CLR property types aligned with Creatio column types
- Do not use `Nullable<T>` or `T?` in model properties

Example:

```csharp
using System;
using System.Collections.Generic;
using ATF.Repository;
using ATF.Repository.Attributes;

[Schema("Contact")]
public class ContactModel : BaseModel {
	[SchemaProperty("Name")]
	public string Name { get; set; }

	[SchemaProperty("Account")]
	public Guid AccountId { get; set; }

	[LookupProperty("Account")]
	public virtual AccountModel Account { get; set; }

	[DetailProperty(nameof(ContactAddressModel.ContactId))]
	public virtual List<ContactAddressModel> Addresses { get; set; }
}

[Schema("Account")]
public class AccountModel : BaseModel {
	[SchemaProperty("Name")]
	public string Name { get; set; }
}

[Schema("ContactAddress")]
public class ContactAddressModel : BaseModel {
	[SchemaProperty("Contact")]
	public Guid ContactId { get; set; }

	[SchemaProperty("Address")]
	public string Address { get; set; }
}
```

## Direct Relation

Use `[LookupProperty("<LookupColumnName>")]` on a `virtual` property of another model type.

This is the standard choice for:

- many-to-one navigation
- one-to-one style navigation where the link is stored as a lookup column

Pattern:

```csharp
[SchemaProperty("Owner")]
public Guid OwnerId { get; set; }

[LookupProperty("Owner")]
public virtual ContactModel Owner { get; set; }
```

## Reverse Relation

Use `[DetailProperty("<DetailModelPropertyName>")]` on a `virtual List<TDetail>` property in the master model.

This is the standard choice for:

- one-to-many collections
- traversing detail rows from the master model
- point the attribute to the detail model property name marked by `[SchemaProperty(...)]`

Pattern:

```csharp
[DetailProperty(nameof(OrderLineModel.OrderId))]
public virtual List<OrderLineModel> Lines { get; set; }
```

Where the detail model contains the actual mapped foreign key:

```csharp
[SchemaProperty("Order")]
public Guid OrderId { get; set; }
```

Reporting example with `Contact -> Account by Owner`:

```csharp
[Schema("Contact")]
public class ContactModel : BaseModel {
	[SchemaProperty("Name")]
	public string Name { get; set; }

	[DetailProperty(nameof(AccountModel.OwnerId))]
	public virtual List<AccountModel> OwnedAccounts { get; set; }
}

[Schema("Account")]
public class AccountModel : BaseModel {
	[SchemaProperty("Name")]
	public string Name { get; set; }

	[SchemaProperty("Owner")]
	public Guid OwnerId { get; set; }
}
```

## Many-To-Many

ATF.Repository works best when the link entity is modeled explicitly.

Recommended approach:

1. Create a model for the link schema.
2. Expose a detail collection from the master model to the link model.
3. Add lookup navigation from the link model to each side.

Pattern:

```csharp
[Schema("UsrContactRole")]
public class ContactRoleLinkModel : BaseModel {
	[SchemaProperty("Contact")]
	public Guid ContactId { get; set; }

	[LookupProperty("Contact")]
	public virtual ContactModel Contact { get; set; }

	[SchemaProperty("Role")]
	public Guid RoleId { get; set; }

	[LookupProperty("Role")]
	public virtual RoleModel Role { get; set; }
}
```

This keeps repository mapping explicit and avoids hiding the link schema.

## DetailProperty Rule

For `DetailProperty`, do not pass the schema column name directly.

Use the name of the detail-model property that is marked with `[SchemaProperty(...)]`, preferably through `nameof(...)`.

Pattern:

```csharp
[Schema("Account")]
public class Account: BaseModel {

	[SchemaProperty("Name")]
	public string Name { get; set; }

	[DetailProperty(nameof(AccountAddress.AccountId))]
	public virtual List<AccountAddress> CollectionOfAccountAddressByAccount { get; set; }
}

[Schema("AccountAddress")]
public class AccountAddress: BaseModel {

	[SchemaProperty("Name")]
	public string Name { get; set; }

	[SchemaProperty("Account")]
	public Guid AccountId { get; set; }
}
```

## Practical Modeling Guidance

- Keep model names business-readable even if they differ from schema names.
- Do not add navigation properties unless the feature actually needs them.
- For write-heavy scenarios, always keep the scalar foreign key property alongside the navigation property.
- For detail collections, point `DetailProperty` to the detail model property name, preferably via `nameof(...)`.
- For read-only reporting tasks, prefer traversing a modeled detail collection from the master row over running a separate top-level query and regrouping in memory.
"""
		),
		Create(
			"atf-repository-dev",
			"package-and-version",
			"docs://mcp/references/atf-repository-dev/package-and-version",
			"Reference resource for atf-repository-dev: package-and-version.",
			false,
			"""
# Package And Version

## Package Acquisition

Use this only when the project does not already reference `ATF.Repository` or when the existing package version looks uncertain.

- For standalone apps, external utilities, and projects that do not already reference `ATF.Repository`, use NuGet as the default acquisition path.
- Verify the latest stable `ATF.Repository` version on NuGet at task time, then install that exact version instead of hunting for local DLLs, package caches, or unrelated project references.
- Prefer a normal package install such as `dotnet add <project> package ATF.Repository --version <latest-stable>` or an explicit `<PackageReference />` with the verified latest stable version.
- If the user explicitly asks to use a local repository checkout, a bundled DLL, or an existing solution reference, follow that instead and state the reason.

## Version Check

Confirm the actual `IAppDataContext` surface available in the current codebase or package version before relying on helper methods.

Common members include:

- `CreateModel<T>()`
- `GetModel<T>(Guid id)`
- `DeleteModel(T model)`
- `Models<T>()`
- `Save()`

Use the verified API surface exposed by the referenced `ATF.Repository` version.
"""
		),
		Create(
			"atf-repository-dev",
			"provider-and-context",
			"docs://mcp/references/atf-repository-dev/provider-and-context",
			"Reference resource for atf-repository-dev: provider-and-context.",
			false,
			"""
# Provider And Context

## Roles

`IDataProvider` is the low-level transport for repository operations.

Its job is to talk to the actual data source:

- local Creatio runtime through `UserConnection`
- remote Creatio instance through HTTP-based provider implementations

`IAppDataContext` is the higher-level repository work surface.

Its job is to:

- create tracked models
- expose queryable model sets through `Models<T>()`
- load single models
- mark models for deletion
- persist accumulated changes through `Save()`

Short version:

- `IDataProvider` = how repository talks to data
- `IAppDataContext` = how application code works with models

## Creating A Local Provider

Use this when the code runs inside Creatio and already has `UserConnection`.

```csharp
using ATF.Repository;
using ATF.Repository.Providers;

IDataProvider dataProvider = new LocalDataProvider(userConnection);
IAppDataContext appDataContext = AppDataContextFactory.GetAppDataContext(dataProvider);
```

## Creating A Remote Provider

Use this when code must talk to another Creatio instance instead of the current runtime.

`RemoteDataProvider` is not `IDisposable`. Do not wrap it in `using` or `await using`.

Cookie-based auth:

```csharp
IDataProvider dataProvider =
	new RemoteDataProvider(applicationUrl, username, password, isNetCore);
```

Credentials-based auth:

```csharp
IDataProvider dataProvider =
	new RemoteDataProvider(applicationUrl, credentials, isNetCore);
```

OAuth-based auth:

```csharp
IDataProvider dataProvider =
	new RemoteDataProvider(applicationUrl, authApp, clientId, clientSecret, isNetCore);
```

Then:

```csharp
IAppDataContext appDataContext = AppDataContextFactory.GetAppDataContext(dataProvider);
```

## Standalone Console App Pattern

Use this for small external utilities that read or write against a remote Creatio instance.

```csharp
using ATF.Repository;
using ATF.Repository.Attributes;
using ATF.Repository.Providers;

[Schema("Contact")]
public class ContactModel : BaseModel {
	[SchemaProperty("Name")]
	public string Name { get; set; }

	[SchemaProperty("Surname")]
	public string Surname { get; set; }

	[SchemaProperty("GivenName")]
	public string GivenName { get; set; }
}

const string url = "http://localhost:5000";
const string userName = "Supervisor";
const string password = "Supervisor";
const bool isNetCore = true;

RemoteDataProvider dataProvider = new(url, userName, password, isNetCore);
IAppDataContext appDataContext = AppDataContextFactory.GetAppDataContext(dataProvider);

foreach (ContactModel contact in appDataContext.Models<ContactModel>()
		.OrderBy(item => item.Surname)
		.ThenBy(item => item.GivenName)
		.ThenBy(item => item.Name)) {
	Console.WriteLine($"{contact.Surname}, {contact.GivenName} | {contact.Name}");
}
```

Before writing the app, install the current latest stable `ATF.Repository` package from NuGet for the target project.

## Minimal Remote Reporting Pattern

Use this when the task is a small report or console app and only needs a few scalar fields plus one reverse relation.

- Hand-author only the scalar properties the report prints.
- Add a `DetailProperty` collection when the report needs child rows from the already loaded master rows.
- Avoid a second top-level `Models<T>()` query just to regroup child data client-side when the relationship can be modeled directly.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using ATF.Repository;
using ATF.Repository.Attributes;
using ATF.Repository.Providers;

[Schema("Contact")]
public class ContactModel : BaseModel {
	[SchemaProperty("Name")]
	public string Name { get; set; }

	[DetailProperty(nameof(AccountModel.OwnerId))]
	public virtual List<AccountModel> OwnedAccounts { get; set; }
}

[Schema("Account")]
public class AccountModel : BaseModel {
	[SchemaProperty("Name")]
	public string Name { get; set; }

	[SchemaProperty("Owner")]
	public Guid OwnerId { get; set; }
}

RemoteDataProvider dataProvider = new(url, userName, password, isNetCore);
IAppDataContext appDataContext = AppDataContextFactory.GetAppDataContext(dataProvider);

foreach (ContactModel contact in appDataContext.Models<ContactModel>().OrderBy(x => x.Name)) {
	int ownedAccountsCount = contact.OwnedAccounts.Count;
	string singleAccountName = ownedAccountsCount == 1
		? contact.OwnedAccounts[0].Name
		: string.Empty;

	Console.WriteLine($"{contact.Name} | Owned accounts: {ownedAccountsCount} {singleAccountName}");
}
```

## DI Registration

Preferred local pattern in Creatio package code:

```csharp
// UserConnection is owned by the Creatio platform. Inject it through a Func accessor so the
// DI container never tracks or disposes the per-request connection.
serviceCollection.AddTransient<Func<UserConnection>>(sp => () => UserConnection);
serviceCollection.AddScoped<IDataProvider>(sp =>
	new LocalDataProvider(sp.GetRequiredService<Func<UserConnection>>()()));
```

Guidance:

- Never register `UserConnection` as scoped or transient: the container would dispose the
  platform-owned connection when the scope closes. Inject `Func<UserConnection>` and call it.
- Register `IDataProvider` as scoped.
- Do not register `IAppDataContext` in DI.
- Create `IAppDataContext` from the current `IDataProvider` close to the operation that uses it.
- Resolve `IDataProvider` from application services rather than newing it up deep inside business logic methods.
"""
		),
		Create(
			"atf-repository-dev",
			"query-patterns",
			"docs://mcp/references/atf-repository-dev/query-patterns",
			"Reference resource for atf-repository-dev: query-patterns.",
			false,
			"""
# Query Patterns

## Querying Through Models

Primary pattern:

```csharp
var contacts = appDataContext.Models<ContactModel>()
	.Where(x => x.Name.Contains("Alex"))
	.OrderBy(x => x.Name)
	.ToList();
```

Repository queries are model-based, so filter, sort, and project against model properties rather than raw schema column strings whenever possible.

## Allowed LINQ Members

When querying from `Models<T>()`, use only:

- `Skip`
- `Take`
- `FirstOrDefault`
- `First`
- `Where`
- `OrderBy`
- `OrderByDescending`
- `ThenBy`
- `ThenByDescending`
- `Max`
- `Min`
- `Average`
- `Sum`
- `Count`
- `Any`
- `Select`
- `GroupBy`

Calling other LINQ methods can lead to runtime errors.

## Common Filters

Boolean:

```csharp
var activeContacts = appDataContext.Models<ContactModel>()
	.Where(x => x.Active)
	.ToList();
```

Numeric or date comparisons:

```csharp
var matureContacts = appDataContext.Models<ContactModel>()
	.Where(x => x.Age >= 50)
	.ToList();
```

Combined conditions:

```csharp
var filtered = appDataContext.Models<ContactModel>()
	.Where(x => x.Age > 10)
	.Where(x => x.TypeId == contactTypeId)
	.ToList();
```

Paging and sorting:

```csharp
var page = appDataContext.Models<ContactModel>()
	.Where(x => x.Name.Contains("Abc"))
	.OrderBy(x => x.CreatedOn)
	.Skip(20)
	.Take(10)
	.ToList();
```

## Navigating Relations

Direct relation:

```csharp
var accountName = contact.Account.Name;
```

Reverse relation:

```csharp
var primaryAddresses = contact.Addresses
	.Where(x => x.IsPrimary)
	.ToList();
```

## Loading A Single Existing Model

`IAppDataContext` pattern in this workspace:

```csharp
ContactModel model = appDataContext.GetModel<ContactModel>(contactId);
```

## Practical Query Guidance

- Start with `Models<T>()` when you need filtering.
- Use `GetModel<T>(id)` when the identifier is already known and the operation is about one record.
- Keep query expressions simple and readable; split very complex filtering into named intermediate steps when needed.
- Model only the relations you actually need to navigate in the current feature.

## Allowed Aggregations

For aggregation queries, use only:

- `Average`
- `Count`
- `Max`
- `Min`
- `Sum`
"""
		),
		Create(
			"atf-repository-dev",
			"write-operations",
			"docs://mcp/references/atf-repository-dev/write-operations",
			"Reference resource for atf-repository-dev: write-operations.",
			false,
			"""
# Write Operations

## Insert

Preferred `IAppDataContext` pattern in this workspace:

```csharp
ContactModel model = appDataContext.CreateModel<ContactModel>();
model.Name = "New contact";
model.AccountId = accountId;

var saveResult = appDataContext.Save();
if (!saveResult.Success) {
	// Map the failure to the workspace error-as-value pattern.
}
```

## Update

Load the existing model, change mapped properties, then save:

```csharp
ContactModel model = appDataContext.GetModel<ContactModel>(contactId);
model.Name = "Updated name";
model.AccountId = newAccountId;

var saveResult = appDataContext.Save();
```

## Delete

Mark the tracked model for deletion, then save:

```csharp
ContactModel model = appDataContext.GetModel<ContactModel>(contactId);
appDataContext.DeleteModel(model);

var saveResult = appDataContext.Save();
```

Equivalent repository style:

## Save Result Handling

In this workspace DLL, `IAppDataContext.Save()` returns `ISaveResult` with:

- `Success`
- `RowsAffected`
- `ErrorMessage`

Use that result to make write behavior explicit:

```csharp
var saveResult = appDataContext.Save();
if (!saveResult.Success) {
	// In production code, map this to the workspace error-handling pattern.
}
```

## Write Guidance

- Create new models only through repository APIs so defaults are applied correctly.
- For updates, load first, then mutate properties, then save once.
- For deletes, call repository delete API instead of trying to hack `Entity` state manually.
- Prefer one `Save()` per logical unit of work.
- If application logic expects business-validation failures, map them to the project error-as-value pattern rather than throwing for expected flow.
"""
		),
		Create(
			"atf-repository-model-management",
			null,
			"docs://mcp/guides/atf-repository-model-management",
			"Manage ATF.Repository model classes when a task names the needed entities, columns, or nested relations and must find existing models, reuse or extend project models, inspect generated model sets, or generate source models with clio. This skill must be used whenever a task creates, extends, merges, or selects ATF.Repository models or their members. Use for requests like `Add models Account (Name, Owner, Owner.DecisionRole, Owner.DecisionRole.Name)` or `Add Account model (Name, Owner, Owner.DecisionRole, Owner.DecisionRole.Name), Order (Number, Account, Opportunity.Title)`.",
			true,
			"""
# ATF.Repository Model Management

## Use This Skill When

- A task needs one or more ATF.Repository models and names the required schemas or members.
- You need to decide whether to reuse existing models, extend them, or generate new ones.
- Generated models may already exist, but you need to verify whether they are suitable for the current task.
- A task depends on lookup or detail navigation, and you need to identify the minimal model graph that supports it.

## Core Rules

- Reuse existing project models before generating new ones.
- For trivial read-only tasks, prefer hand-authoring a minimal model instead of generating a broad model set.
- Generate models with `add-item-model` (`mcp__clio__add-item-model`) when source models are missing or clearly incomplete for the task.
- If the MCP server is unavailable, use `clio add-item model` in the terminal as the fallback.
- Generate into a local absolute staging folder, not a mixed source folder.
- Treat generation as potentially broad, verify the requested schemas after generation, and keep only the models the task actually needs.
- Treat generated code as project code: review names, relations, and collisions before using it in feature code.
- If a generated staging set already exists, mine it for the exact relation/property names you need before deciding to generate again.

## Standard Workflow

1. Identify the exact schemas and members the task needs.
2. Inspect the current project for existing matching models.
3. Reuse or extend existing models if they already satisfy the task.
4. If the task is a small report or utility and only needs a few scalar fields or one obvious relation, hand-author the minimal model set instead of generating.
5. If models are still missing, call `add-item-model` with a local absolute output folder dedicated to generated output.
6. Review the generated result and confirm the requested models are present.
7. Check for side effects such as duplicate models, unexpectedly broad output, type mismatches, or naming collisions.
8. Keep or reference only the models needed for the task, and avoid broad namespace imports when they create ambiguity.

## References

Read only what you need:

- `references/model-graph-selection.md`: choosing the minimal schema and member graph for the task
- `references/generation-workflow.md`: `add-item-model` usage, folder constraints, fallback behavior, and broad-generation checks
- `references/collision-and-cleanup.md`: duplicate models, naming collisions, namespace ambiguity, and cleanup decisions

## What To Report Back

- which models were reused, extended, or generated
- the output folder used for generation
- whether generation produced only the requested models or a broader set
- any collisions, duplication, or cleanup decisions that affected implementation
"""
		),
		Create(
			"atf-repository-model-management",
			"collision-and-cleanup",
			"docs://mcp/references/atf-repository-model-management/collision-and-cleanup",
			"Reference resource for atf-repository-model-management: collision-and-cleanup.",
			false,
			"""
# Collision And Cleanup

## What To Check

Review generated or reused models for:

- duplicate or overlapping models
- type mismatches
- broad namespace imports that create ambiguity
- naming collisions such as `File`, `Task`, `Environment`, or other common framework names

## Collision Handling

When a generated model name collides with framework or project types:

- prefer aliases or explicit qualification
- prefer narrower `using` directives
- avoid renaming unrelated code just to accommodate one generated type

## Cleanup Rule

If generation produces more files than the task needs, do not treat that as proof that every generated model should now be used.

Keep the smallest set that supports the task and report:

- what was kept
- what was ignored or left staged
- any cleanup or qualification decisions that affected implementation
"""
		),
		Create(
			"atf-repository-model-management",
			"generation-workflow",
			"docs://mcp/references/atf-repository-model-management/generation-workflow",
			"Reference resource for atf-repository-model-management: generation-workflow.",
			false,
			"""
# Generation Workflow

## Preferred Tool Path

Use `add-item-model` through the CLIO MCP server when models are missing or clearly incomplete for the task.

Do not generate by default for trivial read-only tasks such as:

- a console app that prints a few scalar fields
- a report that needs one lookup navigation
- a report that needs one reverse relation such as `Contact -> Account by Owner`

In those cases, hand-author the minimal models unless the exact relation shape is unclear.

Fallback only if MCP is unavailable:

- `clio add-item model`

## Folder Rules

The `folder` argument for `add-item-model` must be:

- a local absolute path
- a staging folder dedicated to generated output

Do not use:

- relative paths
- UNC or network paths
- mixed source folders that already contain hand-maintained models

The output folder does not need to exist before calling the MCP tool; the MCP layer is expected to create it when possible.

## Broad Generation Warning

Treat `add-item-model` as potentially broad generation. It may generate the full environment model set rather than only the schema you currently care about.

After generation:

- verify the requested schemas are present
- check whether extra models were produced
- keep, copy, or reference only the models the task actually needs
- if the generated set reveals the exact relation/property name you needed, copy only that minimal shape into the hand-maintained model instead of adopting the full generated file

If generation is broader than expected, report that explicitly.
"""
		),
		Create(
			"atf-repository-model-management",
			"model-graph-selection",
			"docs://mcp/references/atf-repository-model-management/model-graph-selection",
			"Reference resource for atf-repository-model-management: model-graph-selection.",
			false,
			"""
# Model Graph Selection

## Start Small

Identify the smallest useful model graph that satisfies the task before deciding to generate anything.

- Start from the exact schemas and members named in the request.
- Add lookup or detail relations only when the feature or test needs to navigate them.
- Prefer reusing an existing project model even if it exposes a few extra members, as long as it does not create ambiguity or conflict.

## Minimal Graph Rule

Do not assume the task needs the full schema neighborhood.

Example request shapes:

- `Account (Name, Owner, Owner.DecisionRole, Owner.DecisionRole.Name)`
- `Order (Number, Account, Opportunity.Title)`

For requests like these, choose the minimal set of models and members that supports the required traversal.

For small reporting tasks, the minimal graph is often just:

- one master model with the printed scalar fields
- one detail model with the lookup foreign key and any printed scalar fields

Example:

- `Contact (Name, Email, OwnedAccounts.Count, OwnedAccounts.Name when count == 1)`

This does not justify generating the entire environment model set.

## Reuse Before Generation

Before generating:

- inspect existing project models
- inspect previously generated models already checked into the workspace
- prefer extending a nearby model over introducing a parallel duplicate
- if a staged generated model already shows the exact reverse relation name, reuse that mapping pattern and hand-author the minimal production model

Report any tradeoff where reuse pulls in a slightly broader model than the task strictly needs.
"""
		),
		Create(
			"atf-repository-tests",
			null,
			"docs://mcp/guides/atf-repository-tests",
			"Write, update, or review tests for functionality built on ATF.Repository, especially when tests should use `MemoryDataProviderMock`, `IDataStore` setup, `AppDataContextFactory.GetAppDataContext(...)`, model-schema registration, seeded in-memory data, and repository assertions after query or save operations. If the task creates or changes repository models or their members, also use `atf-repository-model-management`.",
			true,
			"""
# ATF.Repository Tests

## Use This Skill When

- Production code uses `ATF.Repository` and needs unit or component-style tests.
- Tests should run against `MemoryDataProviderMock` instead of a real `UserConnection`.
- You need to seed in-memory records for repository queries or write operations.
- You need to register repository models and relation graphs before exercising code under test.
- You need to verify repository-driven read, insert, update, or delete behavior.

## Package Acquisition

- For test projects that do not already reference the repository test tooling, install `ATF.Repository.Mock` from NuGet as the default package for repository tests.
- Verify the latest stable `ATF.Repository.Mock` version on NuGet at task time, then install that exact version instead of searching for local DLLs or copying binaries from other projects.
- Prefer `dotnet add <test-project> package ATF.Repository.Mock --version <latest-stable>` or an explicit `<PackageReference />` with the verified latest stable version.
- `ATF.Repository.Mock` brings a compatible `ATF.Repository` dependency, but if the solution already pins `ATF.Repository`, align the versions deliberately and report the choice.

## Non-Negotiable Rules

- Add or update tests when repository-based production code changes unless the user explicitly says not to.
- Use `MemoryDataProviderMock` as the default repository test double when repository behavior itself is under test.
- Build `IAppDataContext` from the mock provider with `AppDataContextFactory.GetAppDataContext(...)`.
- Register every model involved in the tested query or relation graph in `DataStore` before seeding or querying.
- Prefer `AddModelRawData(...)` when seeding relation-heavy scenarios or reverse relations because it makes the foreign-key links explicit and avoids relying on mock helper overloads that may vary by package version.
- If a task requires creating or changing models for the test scenario, apply `atf-repository-model-management` before writing the tests.
- Structure every test method with explicit `// Arrange`, `// Act`, and `// Assert`.
- Decorate every test with `[Description("...")]`.
- Use assertion messages or FluentAssertions `because` reasons so failures explain intent.
- Prefer asserting observable repository outcomes by re-querying the in-memory context after `Save()` rather than inspecting implementation details.
- If tests require models that do not yet exist in project code, apply `atf-repository-model-management` first.

## Core Workflow

1. Identify the repository models and relation graph required by the production behavior.
2. Create `MemoryDataProviderMock`.
3. Register all required models in `mock.DataStore`.
4. Seed data with `AddModel(...)`, `AddModel(recordId, ...)`, or `AddModelRawData(...)`.
5. For reverse relations, lookup chains, or version-sensitive mock APIs, prefer `AddModelRawData(...)`.
6. Create `IAppDataContext` with `AppDataContextFactory.GetAppDataContext(mock)`.
7. Execute the production logic.
8. Assert returned values, save result, and final repository state by querying the in-memory context.

## References

Read only what you need:

- `references/minimal-test-setup.md`: a compact end-to-end test example with `MemoryDataProviderMock`
- `references/memory-provider-setup.md`: creating `MemoryDataProviderMock`, registering schemas, and building `IAppDataContext`
- `references/data-seeding-patterns.md`: `AddModel`, `AddModelRawData`, fixed ids, defaults, and relation graph setup
- `references/assertion-patterns.md`: AAA layout, local assertion style, and how to verify query and save outcomes
- `references/build-and-verify.md`: build and test command patterns for repository test work

## Build And Verify

Use `references/build-and-verify.md` when you need concrete command patterns.

Use the configuration that matches the current target framework and repository setup, and run build and test sequentially unless the current environment has a documented reason to do otherwise.

## What To Report Back

- test files changed, with one-line reason per file
- which repository models were registered in the mock data store
- how the test data was seeded
- what repository behavior each test proves
- build/test commands run, or the exact blocker if not run
"""
		),
		Create(
			"atf-repository-tests",
			"assertion-patterns",
			"docs://mcp/references/atf-repository-tests/assertion-patterns",
			"Reference resource for atf-repository-tests: assertion-patterns.",
			false,
			"""
# Assertion Patterns

## Test Layout

Use explicit AAA sections:

```csharp
// Arrange
// Act
// Assert
```

Decorate every test with:

```csharp
[Description("...")]
```

## Query Assertions

Assert through the query result:

```csharp
result.Should().HaveCount(1, because: "only one seeded record matches the filter");
result.Single().Name.Should().Be("Alex", because: "the matching seeded record should be returned");
```

## Save Assertions

For insert/update/delete, assert both save result and final state:

```csharp
var saveResult = appDataContext.Save();
saveResult.Success.Should().BeTrue(because: "the in-memory save should succeed");

List<Contact> contacts = appDataContext.Models<Contact>().ToList();
contacts.Should().ContainSingle(x => x.Name == "Updated", because: "the updated record should be persisted in memory");
```

Delete example:

```csharp
var saveResult = appDataContext.Save();
saveResult.Success.Should().BeTrue(because: "delete should complete successfully");

appDataContext.Models<Contact>()
	.Any(x => x.Id == contactId)
	.Should()
	.BeFalse(because: "deleted records should no longer be returned");
```

## Recommended Assertions

Prefer asserting:

- returned collections
- selected scalar values
- relation navigation results
- `Save()` success and final in-memory state

Avoid asserting:

- private implementation details
- internal intermediate collections that are not part of the observable behavior

## Workspace Style

Use:

- NUnit `[Test]` and `[Description]`
- explicit Arrange/Act/Assert comments
- FluentAssertions with `because`

Match the existing local test tone unless a target test file already follows a stronger convention.
"""
		),
		Create(
			"atf-repository-tests",
			"build-and-verify",
			"docs://mcp/references/atf-repository-tests/build-and-verify",
			"Reference resource for atf-repository-tests: build-and-verify.",
			false,
			"""
# Build And Verify

Use the configuration that matches the current target framework and repository setup.

Typical commands:

```powershell
dotnet build <solution-or-project> -c <configuration>
dotnet test <test-project> -c <configuration> --no-build
```

Run build and test sequentially unless the current environment has a documented reason to do otherwise.
"""
		),
		Create(
			"atf-repository-tests",
			"data-seeding-patterns",
			"docs://mcp/references/atf-repository-tests/data-seeding-patterns",
			"Reference resource for atf-repository-tests: data-seeding-patterns.",
			false,
			"""
# Data Seeding Patterns

## Typed Seed

Use typed seeding when the model class already exists and typed setup is clearer.

```csharp
dataProvider.AddModel<Contact>(model => {
	model.Id = contactId;
	model.Name = "Alex";
	model.AccountId = accountId;
});
```

Fixed id overload:

```csharp
dataProvider.AddModel<Contact>(contactId, model => {
	model.Name = "Alex";
	model.AccountId = accountId;
});
```

## Raw Seed

Use raw seed dictionaries when you need a quick table-style setup or when constructing typed models is noisy.

Prefer raw seed by default when:

- the test is about a reverse relation such as `Contact -> Account by Owner`
- the test needs to make foreign-key links visually obvious
- the available `ATF.Repository.Mock` version may not expose the same `AddModel(...)` helper overloads
- table-style setup is easier to review than a sequence of typed lambdas

```csharp
dataProvider.DataStore.RegisterModelSchema<Contact>();
dataProvider.DataStore.AddModelRawData("Contact", new List<Dictionary<string, object>> {
	new Dictionary<string, object> {
		["Id"] = contactId,
		["Name"] = "Alex",
		["Account"] = accountId
	}
});
```

Reverse-relation example:

```csharp
dataProvider.DataStore.RegisterModelSchema(typeof(ContactModel), typeof(AccountModel));
dataProvider.DataStore.AddModelRawData("Contact", new List<Dictionary<string, object>> {
	new Dictionary<string, object> {
		["Id"] = contactId,
		["Name"] = "Alex Carter",
		["Surname"] = "Carter",
		["GivenName"] = "Alex"
	}
});
dataProvider.DataStore.AddModelRawData("Account", new List<Dictionary<string, object>> {
	new Dictionary<string, object> {
		["Id"] = accountId,
		["Name"] = "Alpha",
		["Owner"] = contactId
	}
});
```

Prefer typed seeding when it is clearly supported in the current mock package and the test data shape stays simple.

## Relation Graph Seeding

When testing nested lookups, seed the full chain:

- root record
- related lookup record
- nested lookup record if assertions or filters depend on it

Example:

- `Account.Owner`
- `Owner.DecisionRole`
- `Owner.DecisionRole.Name`

Requires seeding:

- `Account`
- `Contact`
- `ContactDecisionRole`

For reverse relations, seed:

- the master rows
- the detail rows
- the foreign-key column that links the detail rows back to the master

## Test Data Rule

Seed only the rows needed to prove the behavior.

Use:

- one matching record
- one non-matching record when filtering matters
- the minimum related rows needed for navigation assertions
"""
		),
		Create(
			"atf-repository-tests",
			"memory-provider-setup",
			"docs://mcp/references/atf-repository-tests/memory-provider-setup",
			"Reference resource for atf-repository-tests: memory-provider-setup.",
			false,
			"""
# Memory Provider Setup

## Default Test Double

Use `MemoryDataProviderMock` as the default in-memory data provider for ATF.Repository tests.

Pattern:

```csharp
MemoryDataProviderMock dataProvider = new MemoryDataProviderMock();
IAppDataContext appDataContext = AppDataContextFactory.GetAppDataContext(dataProvider);
```

## Register Models Before Use

Register every model that participates in the test:

```csharp
dataProvider.DataStore.RegisterModelSchema<Contact>();
```

Or register a graph:

```csharp
dataProvider.DataStore.RegisterModelSchema(typeof(Account), typeof(Contact), typeof(ContactDecisionRole));
```

Register:

- the root model under test
- related lookup models
- related detail models
- nested related models used by filters or assertions

## Default Values

When production logic depends on default values, configure them explicitly:

```csharp
dataProvider.DataStore.SetDefaultValues(defaults => {
	defaults.Add("StatusId", someStatusId);
});
```

## Additional Mock Features

`MemoryDataProviderMock` also supports:

- `MockSysSettingValue(...)`
- `MockFeatureEnable(...)`
- `MockExecuteProcess(...)`

Use these when repository code depends on sys settings, feature flags, or process execution.
"""
		),
		Create(
			"atf-repository-tests",
			"minimal-test-setup",
			"docs://mcp/references/atf-repository-tests/minimal-test-setup",
			"Reference resource for atf-repository-tests: minimal-test-setup.",
			false,
			"""
# Minimal Test Setup

Use this as the smallest complete example when the skill needs to remember the wiring pattern for repository tests.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using ATF.Repository;
using ATF.Repository.Mock;
using FluentAssertions;
using NUnit.Framework;

[Test]
[Description("Returns matching contacts for an account")]
public void Execute_ReturnsMatchingContacts() {
	// Arrange
	Guid accountId = Guid.NewGuid();
	MemoryDataProviderMock dataProvider = new MemoryDataProviderMock();
	dataProvider.DataStore.RegisterModelSchema<Contact>();
	dataProvider.AddModel<Contact>(model => {
		model.Id = Guid.NewGuid();
		model.Name = "Alex";
		model.AccountId = accountId;
	});
	IAppDataContext appDataContext = AppDataContextFactory.GetAppDataContext(dataProvider);

	// Act
	List<Contact> result = appDataContext.Models<Contact>()
		.Where(x => x.AccountId == accountId)
		.ToList();

	// Assert
	result.Should().HaveCount(1, because: "one seeded contact belongs to the requested account");
result[0].Name.Should().Be("Alex", because: "the seeded contact should be returned");
}
```

When the test depends on reverse relations or the `ATF.Repository.Mock` helper surface is uncertain, switch this example to `AddModelRawData(...)` instead of assuming `AddModel(...)` is available in the current package version.
"""
		),
		Create(
			"composable-app-e2e-test-implementation",
			null,
			"docs://mcp/guides/composable-app-e2e-test-implementation",
			"""Deliver composable app E2E tests.""",
			true,
			"""
# Cross-Repo E2E Delivery (Composable App)

## Goal

Execute a full, repeatable cross-repo E2E delivery flow from feature analysis to test delivery, with mandatory environment readiness checks and composable app workspace deployment.

This is the Composable App variant -- it pushes the composable app workspace to stand before testing (`PUSH_APP_TO_STAND=true`). This ensures the stand has the latest feature code from the composable app repository before running E2E tests.

## Required Inputs

Collect these before implementation:

- Source repository path (default: current working directory -- expected to be a Composable App repo).
- Target E2E repository URL or name.
- Jira issue URL or Jira issue key.
- If Jira URL/key is unavailable, explicit tested-case description from user (business flow, expected behavior, affected pages/entities/events).
- Delivery depth: `patch only` | `branch + commit` | `branch + push + PR` (default).
- Target stand URL from `.env` (`CREATIO_APP_URL` preferred; legacy `APP_URL` / `CLIO_APP_URL` accepted).
- Credentials and environment alias for Clio/SVN when needed.
- Local env file path (default: skill-local `.env`).

If target repo URL is not provided, default to `https://creatio.ghe.com/engineering/creatio-playwright-tests.git`.

## Workflow Overview

The detailed step-by-step flow with exact commands lives in `references/flow.md`. The readiness check command matrix lives in `references/environment-readiness.md`. Read those files when executing each step.

### 0. Application testability preflight

Run `python scripts/validate_app_testability.py` to verify local tools, stand components, and composable app sync.

Key gates:
- **0.0 Local system components** -- verifies `git`, `ssh`, `gh`, `clio`, `svn`, `node`, `npm`, `npx` with auto-install on macOS/Windows.
- **0.1 Stand components** -- cliogate availability, required test packages (default: `AutoTest`) with auto-install.
- **0.2 Composable app sync** -- pushes current workspace to stand via `clio pushw` (`PUSH_APP_TO_STAND=true` by default in this variant). This ensures the tested composable app code is deployed to the stand.

Do not start any parallel analysis/implementation work before Step 0 passes.

### 1. Analyze source repository

Resolve context source (Jira URL/key or manual tested-case description). Inspect ticket/commits/diffs to extract testable behavior. Build implementation targets. See `references/flow.md` for details.

### 2. Bootstrap target E2E repository

Search one level above source repo. Clone or pull + setup. Verify TestKit docs exist. See `references/flow.md` for exact commands.

### 3. Test scope selection

Default: `Extended`. Override only if user explicitly requested `Basic` or `Full`.

### 4. Stand readiness gate

Use stand URL from `.env`. Run readiness checks via Clio and SQL probes per `references/environment-readiness.md`. If readiness fails, stop and emit blocker details -- do not auto-install licenses or test users.

### 5. Implement scenarios and tests

Translate validated feature behavior into scope-driven scenarios. Reuse target repo conventions and existing page objects first. Add/modify minimal required files only.

### 6. Execute agreed run scope

Run lint/type checks, then tests with `PW_USE_DYNAMIC_USERS=false`. On failures: fix, rerun, capture final status. If still failing, stop before git delivery and ask user.

### 7. Deliver results

Default delivery: `branch + push + PR` (only after successful checks/tests). If checks/tests fail, ask user whether to continue with patch only, commit without PR, or additional fixes.

Return a structured report covering: context source, repository bootstrap, readiness results, test implementation, execution results, git delivery state, and any blockers/risks.

## Mandatory Rules

- Do not start parallel work before Step 0 passes.
- Resolve analysis context only in Step 1; do not proceed without valid context.
- Always run with `PW_USE_DYNAMIC_USERS=false` unless user explicitly requested otherwise.
- If tests fail, do not push and do not open PR automatically.
- Always spawn subagents for parallelizable independent work when safe and tool-allowed.
- Default test scope is `Extended` -- use without asking.

## Reference Files

- `references/flow.md` -- detailed step-by-step workflow with commands
- `references/environment-readiness.md` -- readiness check command matrix
- `references/sources.md` -- fixed artifact sources
- `.env.example` -- environment configuration template
- `scripts/validate_app_testability.py` -- preflight validation script
"""
		),
		Create(
			"composable-app-e2e-test-implementation",
			"environment-readiness",
			"docs://mcp/references/composable-app-e2e-test-implementation/environment-readiness",
			"Reference resource for composable-app-e2e-test-implementation: environment-readiness.",
			false,
			"""
# Environment Readiness

## Goal

Confirm the stand is usable for E2E execution before test implementation and run.

## Prerequisites

- Local tools are available (or auto-installable): `git`, `ssh`, `gh`, `clio`, `svn`, `node`, `npm`, `npx`.
- Clio environment credentials are available (direct args or registered env alias).
- Skill-local `.env` is configured (default path: `../.env` relative to this references folder).

Recommended:

1. Copy `.env.example` to `.env`.
2. Fill all required keys (`SVN_USERNAME`, `SVN_PASSWORD`) and repo host values.
3. Set either `CLIO_ENV_NAME` or `CREATIO_APP_URL`.
4. If URL-based auto-registration is needed, provide `CREATIO_LOGIN` and `CREATIO_PASSWORD` (or ensure active clio env credentials are valid).
5. Use built-in default artifact URLs from `references/sources.md` unless override is needed in `.env`.
6. Run `python scripts/validate_app_testability.py` before readiness checks.
7. Do not continue to any Jira/repo/test implementation steps until Step 0 passes.
8. Optional: tune SSH check timeout with `SSH_CONNECT_TIMEOUT_SECONDS` (default `15`).
9. Repository access checks are SSH-first with HTTPS fallback (using SVN credentials for HTTPS auth).

## 0. Local System Components Gate

Preflight starts with local component verification before any stand/repo actions.

Defaults:

- `AUTO_INSTALL_LOCAL_COMPONENTS=true`
- `LOCAL_COMPONENTS_REQUIRED=git,ssh,gh,clio,svn,node,npm,npx`
- `LOCAL_COMPONENTS_ALLOW_ELEVATION=false`
- `WINDOWS_PM_ORDER=winget,choco`

Behavior:

- On macOS/Windows, preflight attempts auto-install for missing local components.
- If installation requires elevated privileges, preflight stops and returns manual command blocker.
- If component remains unavailable after attempts, preflight fails immediately.

## 1. Stand Components Gate

### 1.1 Clio Connectivity and Registration

Check registered environments:

```bash
clio show-web-app-list
clio show-web-app-list <ENV_NAME>
```

If environment is missing, register it:

```bash
clio reg-web-app <ENV_NAME> -u <STAND_URL> -l <LOGIN> -p <PASSWORD>
```

If `CLIO_ENV_NAME` is empty and app URL is provided, preflight auto-resolves and persists environment alias.
If `CLIO_ENV_NAME` is filled but points to a different stand than app URL, preflight normalizes it to URL-resolved alias.

Verify `cliogate` availability for SQL-based checks:

```bash
clio get-info -e <ENV_NAME>
```

If it reports missing/outdated cliogate, install/update:

```bash
clio install-gate -e <ENV_NAME>
```

Skill preflight attempts this installation automatically by default.
Disable auto-install only if needed:

```bash
AUTO_INSTALL_CLIOGATE=false
```

### 1.2 Required Package Checks

Use `clio sql` (alias of `execute-sql-script`).

```bash
clio sql "SELECT Id FROM SysSettings WHERE Code = 'CustomPackageId'" -e <ENV_NAME>
```

#### Package presence check (example)

```sql
SELECT Name
FROM SysPackage
WHERE Name IN ('AutoTest');
```

For this skill, these test packages are mandatory:

- `AutoTest`

You can override the default list with `.env` key:

- `REQUIRED_TEST_PACKAGES=AutoTest`

Auto-install behavior:

- Preflight tries to install missing packages when `AUTO_INSTALL_TEST_PACKAGES=true`.
- Provide package artifacts location:
  - `TEST_PACKAGES_DIR=/absolute/path/to/test-package-artifacts`
- Repo-based fallback (no `.env` override needed):
  - skill-local `artifacts/test-packages`
- SVN fallback:
  - `AUTO_FETCH_TEST_PACKAGES_FROM_SVN=true` (default)
  - `SVN_TEST_PACKAGE_REPOS` (comma-separated SVN repo URLs) used for artifact discovery/export into local cache
  - Default package repo (if not overridden): `http://tscore-svn.tscrm.com:8050/svn/ts5conf/PackageStore/`
  - `SVN_TEST_PACKAGE_CACHE_DIR` optional cache directory (default: skill-local `artifacts/svn-package-repos`)
  - Preflight auto-cleans legacy full-checkout cache folders (directories containing `.svn`) inside cache root.
- Expected artifact names:
  - `<PackageName>.gz` or `<PackageName>.zip` or directory `<PackageName>`
  - On installation failure, full `clio push-pkg` output is saved to:
    - skill-local `artifacts/install-logs/<Package>-install.log`

Notes:

- This skill does not auto-install licenses.
- This skill does not auto-create/import test users.
- For SVN CLI operations, credentials are passed via `--username/--password` flags (tool behavior).

## 2. Composable App Sync Gate (workspace -> stand)

This step is executed only when `PUSH_APP_TO_STAND=true` (default depends on skill variant).

After successful stand components gate (1.1 + 1.2), preflight runs:

```bash
clio pushw -e <ENV_NAME>
```

This ensures current repository workspace code is pushed to the stand.

Control via `.env`:

- `PUSH_APP_TO_STAND=true` (composable app variant) or `false` (universal variant)
- `CLIO_PUSHW_WORKSPACE_DIR` (optional; default = current working directory)
- `CLIO_PUSHW_USE_APPLICATION_INSTALLER=false` (optional flag)

If push fails, preflight stops and stores full output in:

- skill-local `artifacts/install-logs/push-workspace.log`

## 3. Failure Handling

When readiness fails, stop and return blocker report with:

- failed step,
- exact command,
- exact stderr/stdout summary,
- missing access details,
- required user action.
"""
		),
		Create(
			"composable-app-e2e-test-implementation",
			"flow",
			"docs://mcp/references/composable-app-e2e-test-implementation/flow",
			"Reference resource for composable-app-e2e-test-implementation: flow.",
			false,
			"""
# Cross-Repo E2E Flow

## 0. Parallel Execution Policy

- Use spawned subagents for independent tasks whenever possible and allowed by tools.
- Keep parent agent as coordinator: assign scoped ownership, gather results, resolve conflicts.
- Do not parallelize stateful dependent operations (single branch writes, sequential setup/apply/re-check, final delivery).
- Do not start any parallel work (Jira/source analysis, target repo bootstrap, drafting tests) until Step 0 credential preflight has passed.

## 1. Preflight Gates

### 1.0 Local system components gate

- Verify local tools: `git`, `ssh`, `gh`, `clio`, `svn`, `node`, `npm`, `npx`.
- If missing, attempt auto-install on macOS/Windows.
- Fail-fast before any repo/bootstrap/test action if unresolved.
- Verify repository endpoint access in SSH-first mode with HTTPS fallback (SVN credentials for HTTPS auth).

### 1.1 Stand components gate

- Validate clio environment reachability.
- Validate/install `cliogate`.
- Validate required test packages (default `AutoTest`) with auto-install/re-check.

### 1.2 Composable app sync gate

- Run `clio pushw` only after 1.1 passes.
- This step runs only when `PUSH_APP_TO_STAND=true` (default depends on skill variant).
- When `PUSH_APP_TO_STAND=false`, this step is skipped entirely.

## 2. Source Context

1. Open source repo in current working directory.
2. Extract behavior under test from task context and source code.
3. Build concise test objective list tied to ticket id (or Jira URL-resolved key).

## 3. Target Repo Discovery and Setup

Only search one level above source repo.

Default repo URL when none provided by user:

- `https://creatio.ghe.com/engineering/creatio-playwright-tests.git`

```bash
source_repo="$(pwd)"
parent_dir="$(dirname "$source_repo")"
repo_name="creatio-playwright-tests"
target_repo="$parent_dir/$repo_name"
```

### If target repo exists

```bash
git -C "$target_repo" status --short
# if local changes are risky, ask user before pull
git -C "$target_repo" pull
cd "$target_repo"
npm install
npx playwright install chromium webkit --with-deps
```

### If target repo does not exist

```bash
git clone https://creatio.ghe.com/engineering/creatio-playwright-tests.git "$target_repo"
cd "$target_repo"
npm install
npx playwright install chromium webkit --with-deps
```

### Required post-check

```bash
test -d "$target_repo/node_modules/@creatio/playwright-testkit/dist/docs"
```

If missing, stop and report setup blocker.

## 4. Scope Decision

Use default scope `Extended` without asking.
If user explicitly requested `Basic` or `Full`, use that explicit requirement.

## 5. Stand URL Gate

Use stand URL from `.env` (`CREATIO_APP_URL`, fallback `APP_URL` / `CLIO_APP_URL`).
If URL is missing in `.env` and not provided by user, request it and wait.

## 6. Readiness Gate

Run readiness checks with Clio before implementation and execution.
Use `references/environment-readiness.md` command matrix.

## 7. Readiness Failure Handling

If readiness fails:

1. Stop and return blocker report with exact command/output and required next action.
2. Do not auto-install licenses.
3. Do not auto-create/import test users.

## 8. Implement and Execute

1. Implement test files in target E2E repo according to selected scope.
2. Run required checks and tests with `PW_USE_DYNAMIC_USERS=false` unless user explicitly requested a different value.
3. Iterate on failures until pass or hard blocker.

## 9. Delivery

If delivery depth is missing, default to:

- `branch + push + PR` (only after successful checks/tests)

Provide:

- changed files,
- run commands and outcomes,
- branch/commit,
- push/PR links when requested,
- blockers/risks.
"""
		),
		Create(
			"composable-app-e2e-test-implementation",
			"sources",
			"docs://mcp/references/composable-app-e2e-test-implementation/sources",
			"Reference resource for composable-app-e2e-test-implementation: sources.",
			false,
			"""
# Fixed Artifact Sources

## Test Packages Source

- Repository root:
  - `http://tscore-svn.tscrm.com:8050/svn/ts5conf/PackageStore/`
- Required packages:
  - `AutoTest`

Skill preflight lists/export artifacts into a local cache and searches for package artifacts
(`*.gz` / `*.zip`) when local artifacts are not found.

## Scope Note

This skill no longer auto-installs licenses or test users from SVN sources.

## Auth Note

SVN endpoints may require credentials.
If unauthenticated requests fail, provide credentials via svn auth options or stored credentials.
Current implementation uses SVN CLI `--username/--password` flags (credentials are not echoed by the script).
"""
		),
		Create(
			"composable-app-repo-bootstrap",
			null,
			"docs://mcp/guides/composable-app-repo-bootstrap",
			"Initialize a fresh repository created from this composable-app starter kit for a concrete app/module/slug combination. Use when Codex needs to bootstrap repo scaffolding, root templates, and local environment files for a new composable Creatio application.",
			true,
			"""
# Composable App Repo Bootstrap

## Goal

Turn a fresh repository created from this starter template into an app-specific working repository without overwriting local files that were already customized.

## Required Inputs

- `AppName` in PascalCase, for example `Pulse`
- `ModuleName` in PascalCase, for example `JobHealth`
- `module-slug` in lowercase kebab-case, for example `job-health`

## Workflow

1. Confirm the current repository still looks like an uninitialized starter-kit clone:
   - root `AGENTS.md`
   - root `README.md`
   - `.agents/skills/`
2. Run the bundled helper with the platform-appropriate entrypoint:

```text
macOS/Linux: python3 .agents/skills/composable-app-repo-bootstrap/scripts/bootstrap_repo.py <AppName> <ModuleName> <module-slug>
Windows: py -3 .agents/skills/composable-app-repo-bootstrap/scripts/bootstrap_repo.py <AppName> <ModuleName> <module-slug>
```

3. Read the summary from the helper and continue only after it reports successful validation.

## What The Helper Does

- Validate the local toolchain before changing files:
  - `python 3.9+`
  - `clio`
  - `node.js 22+`
- Generate docs/spec/UI/package scaffold files and root templates from the skill `assets/` directory.
- Create `.env.example`, create `.env`, and auto-fill any safely discoverable values from local environment variables, a single registered `clio` web app, or an unambiguous local `PackageStore` path.
- Render root `README.md` from the bundled README template when the root README is still the generic starter-kit README.
- Render the root `AGENTS.md` from the bundled `assets/AGENTS.md.template` when it still contains starter placeholders, and remove duplicate `AGENTS.*.md` files on rerun so the repo ends with exactly one root `AGENTS.md`.
- Validate the expected bootstrap outputs and verify that app-specific generated files no longer contain `<AppName>`, `<ModuleName>`, or `<module-slug>`.

## Operating Rules

- Preserve an existing `.env`; never overwrite it unless the user explicitly asks.
- Preserve a user-customized root `README.md`; the helper skips it when it no longer matches the starter-kit marker.
- If a customized `AGENTS.md` conflicts with a duplicate `AGENTS.*.md` file, stop and report the blocker instead of leaving two AGENTS files in the root.
- Treat reruns as normal. Existing generated files are expected, and the helper should remain safe to run again with the same arguments.
- Keep generated documentation in English.
- Prefer the Python entrypoints in automated flows; keep `.sh` and `.cmd` files as thin convenience wrappers for shell-specific environments.
- If the toolchain check fails, stop before any bootstrap changes and report the missing or outdated utility explicitly.
"""
		),
		Create(
			"configuration-entity-event-listener",
			null,
			"docs://mcp/guides/configuration-entity-event-listener",
			"Create or review Creatio configuration entity event listeners implemented as `Terrasoft.Core.Entities.Events.BaseEntityEventListener` with `[EntityEventListener(SchemaName = \"...\")]`. Use when adding or changing entity lifecycle handling such as `OnSaving`, `OnSaved`, `OnInserting`, `OnInserted`, `OnUpdating`, `OnUpdated`, `OnDeleting`, `OnDeleted`, or direct event subscriptions like `entity.Validating` for a specific schema.",
			true,
			"""
# Configuration Entity Event Listener

## Non-Negotiable Rules

- Inherit `Terrasoft.Core.Entities.Events.BaseEntityEventListener`.
- Add `[EntityEventListener(SchemaName = "<EntitySchemaName>")]` with the exact schema code.
- Name the class `<EntitySchemaName>EntityEventListener` unless the package already uses another convention.
- Keep the listener thin. Move business rules, side effects, and heavy queries into helper or service classes.
- Use `sender as Entity` or `(Entity)sender` and work through `entity.UserConnection`.
- Do not throw for expected business or validation flow when the workspace already uses value-based error handling.
- For validation, prefer `entity.Validating += ...` and add `EntityValidationMessage` entries instead of throwing.

## Required Location And Shape

Source path:
- `packages/<PACKAGE_NAME>/Files/src/cs/EntryPoints/EntityEventListeners/<EntitySchemaName>EntityEventListener.cs`

Namespace:
- `<PackageNamespace>.EntryPoints.EntityEventListeners`

Minimal skeleton:

```csharp
using Terrasoft.Core;
using Terrasoft.Core.Entities;
using Terrasoft.Core.Entities.Events;

namespace <PackageNamespace>.EntryPoints.EntityEventListeners {
	[EntityEventListener(SchemaName = "<EntitySchemaName>")]
	public class <EntitySchemaName>EntityEventListener : BaseEntityEventListener {
		public override void OnSaving(object sender, EntityBeforeEventArgs e) {
			base.OnSaving(sender, e);
			Entity entity = (Entity)sender;
			UserConnection userConnection = entity.UserConnection;
		}
	}
}
```

## Default Workflow

1. Confirm the exact entity schema code and package namespace.
2. Create or update the listener under `Files/src/cs/EntityEventListeners/`.
3. Choose the smallest event surface that solves the task.
4. Capture any state needed across events in private fields only when the same listener instance will reuse it during one operation.
5. Delegate business logic to a helper or application service.
6. Add or update unit tests when production behavior changes.
7. Build and run relevant tests.

## Event Order You Must Respect

Create flow:
1. `OnSaving`
2. `OnInserting`
3. `OnInserted`
4. `OnSaved`

Update flow:
1. `OnSaving`
2. `OnUpdating`
3. `OnUpdated`
4. `OnSaved`

Delete flow:
1. `OnDeleting`
2. `OnDeleted`

## State Across Events

- Treat one listener instance as reusable during the same entity operation.
- Store only operation-scoped state in private fields, for example `UserConnection`, flags, or values captured before save and used after save.
- Initialize lazily when helper construction depends on `UserConnection`.
- Do not let the listener become a service locator for unrelated logic.

## Validation Pattern

- Subscribe to `entity.Validating` from `OnSaving` when entity validation must happen in the save pipeline.
- Add one or more `EntityValidationMessage` items to `entity.ValidationMessages`.
- Point the message to the failing column when possible.
- Keep validation text user-facing and specific.

## Read These References

- `references/listener-patterns.md`: class structure, naming, event selection, and thin-listener patterns
- `references/validation-patterns.md`: validation subscription and `EntityValidationMessage` examples
- `references/review-checklist.md`: review points and common defects

## What To Report Back

- Which entity schema and event hooks were used
- Whether logic stayed inside the listener or moved into a helper, and why
- Tests added or updated, or the reason tests were not changed
- Build and test commands run, or the exact blocker if they were not run
"""
		),
		Create(
			"configuration-entity-event-listener",
			"listener-patterns",
			"docs://mcp/references/configuration-entity-event-listener/listener-patterns",
			"Reference resource for configuration-entity-event-listener: listener-patterns.",
			false,
			"""
# Listener Patterns

## Core Shape

Use this base structure:

```csharp
using Terrasoft.Core;
using Terrasoft.Core.Entities;
using Terrasoft.Core.Entities.Events;

namespace <PackageNamespace>.EntryPoints.EntityEventListeners {
	[EntityEventListener(SchemaName = "AccountAnniversary")]
	public class AccountAnniversaryEntityEventListener : BaseEntityEventListener {
		private UserConnection _userConnection;
		private IAccountAnniversaryHelper _helper;

		private IAccountAnniversaryHelper Helper => _helper ??=
			ClassFactory.Get<IAccountAnniversaryHelper>(
				new ConstructorArgument("userConnection", _userConnection));

		public override void OnSaving(object sender, EntityBeforeEventArgs e) {
			base.OnSaving(sender, e);
			Entity entity = (Entity)sender;
			_userConnection = entity.UserConnection;
		}

		public override void OnSaved(object sender, EntityAfterEventArgs e) {
			base.OnSaved(sender, e);
			Helper.CalculateRegistrationsAndBookedValue();
		}
	}
}
```

## Choose The Right Event

- Use `OnSaving` for logic shared by both insert and update.
- Use `OnInserting` and `OnInserted` only for create-specific behavior.
- Use `OnUpdating` and `OnUpdated` only for update-specific behavior.
- Use `OnDeleting` and `OnDeleted` only for delete-specific behavior.
- Use `OnSaved` when work must happen after both insert and update are completed in the entity pipeline.

## Private State Between Events

Valid uses:

- cache `UserConnection`
- cache old or derived values captured before save and reused after save
- lazy-create a helper that depends on `UserConnection`

Avoid:

- long-lived mutable caches unrelated to one operation
- direct heavy business logic in every override
- subscribing to unrelated events without a clear reason

## Thin Listener Pattern

Prefer:

1. Extract entity and `UserConnection`.
2. Gather only the small amount of context required for later steps.
3. Call a helper, domain service, or application service.

Avoid:

1. Duplicating business rules across multiple overrides.
2. Hiding complex SQL or repository logic directly in the listener.
3. Letting one listener orchestrate many unrelated responsibilities.

## Common Review Defects

- Wrong `base.*` call, for example `base.OnSaving` inside `OnSaved`
- Missing `SchemaName` or wrong schema code in `[EntityEventListener]`
- Listener class name does not match workspace convention
- Heavy logic embedded directly in overrides instead of a helper
- Using a more specific event when `OnSaving` or `OnSaved` would be clearer
- Forgetting that create and update have different event sequences
"""
		),
		Create(
			"configuration-entity-event-listener",
			"review-checklist",
			"docs://mcp/references/configuration-entity-event-listener/review-checklist",
			"Reference resource for configuration-entity-event-listener: review-checklist.",
			false,
			"""
# Review Checklist

## Listener Contract

1. Class inherits `BaseEntityEventListener`.
2. `[EntityEventListener(SchemaName = "...")]` matches the real entity schema code.
3. File path and namespace match package conventions.
4. Class name follows `<EntitySchemaName>EntityEventListener` unless a package convention overrides it.

## Event Choice

1. Selected overrides match the business requirement.
2. Create, update, and delete flows respect the real event order.
3. Shared logic is not duplicated across insert and update without need.

## Implementation Quality

1. Listener stays thin and delegates business logic.
2. `Entity` and `UserConnection` extraction is safe and clear.
3. Any private state stored between events is operation-scoped and justified.
4. Validation uses `ValidationMessages` rather than exceptions for expected failures.
5. `base.On...` calls match the override being implemented.

## Tests And Validation

1. Unit tests or other automated coverage were added or updated when behavior changed.
2. Build and relevant tests were run, or a blocker is documented.
3. If the listener affects user-visible validation, tests or evidence cover the failing and passing cases.
"""
		),
		Create(
			"configuration-entity-event-listener",
			"validation-patterns",
			"docs://mcp/references/configuration-entity-event-listener/validation-patterns",
			"Reference resource for configuration-entity-event-listener: validation-patterns.",
			false,
			"""
# Validation Patterns

## Subscribe From `OnSaving`

Validation runs immediately after `OnSaving`, so subscribe there:

```csharp
using Terrasoft.Common;
using Terrasoft.Core.Entities;
using Terrasoft.Core.Entities.Events;

[EntityEventListener(SchemaName = "AccountAnniversary")]
public class AccountAnniversaryEntityEventListener : BaseEntityEventListener {
	public override void OnSaving(object sender, EntityBeforeEventArgs e) {
		base.OnSaving(sender, e);
		Entity entity = (Entity)sender;
		entity.Validating += OnValidating;
	}

	private void OnValidating(object sender, EntityValidationEventArgs e) {
		Entity entity = (Entity)sender;
		if (CheckIsEntityValid(entity, out string invalidColumn, out string invalidMessage)) {
			return;
		}

		entity.ValidationMessages.Add(new EntityValidationMessage {
			MessageType = MessageType.Error,
			Column = entity.Schema.Columns.FindByName(invalidColumn),
			Text = $"Validation failed for column: {invalidColumn}, due to {invalidMessage}"
		});
	}
}
```

## Validation Guidance

- Keep the validation function deterministic and side-effect free.
- Return the failing column code when possible so Creatio can point the user to the exact field.
- Prefer one clear message over many vague messages.
- If multiple checks are required, add multiple validation messages only when the UI benefits from seeing all of them at once.
- Keep validation in a helper when the same rule is reused elsewhere.

## Review Notes

- Use `MessageType`, not misspelled property names.
- Resolve the column through `entity.Schema.Columns.FindByName(...)`.
- Keep the handler private unless tests or shared infrastructure require wider scope.
- If repeated saves may attach duplicate handlers in the same flow, inspect the surrounding code and prevent duplicate subscription when needed.
"""
		),
		Create(
			"configuration-entity-event-listener-tests",
			null,
			"docs://mcp/guides/configuration-entity-event-listener-tests",
			"Write, update, or review tests for Creatio configuration entity event listeners built on `BaseEntityEventListener`. Use when creating tests that instantiate the listened entity schema, prepare minimal entity data, create a fresh listener instance, invoke the required lifecycle methods such as `OnSaving`, `OnSaved`, `OnInserting`, `OnUpdating`, `OnDeleting`, or validation hooks in the correct order, and verify helper calls or validation results.",
			true,
			"""
# Configuration Entity Event Listener Tests

## Non-Negotiable Rules

- Add or update tests when entity-listener production behavior changes unless the user explicitly says not to.
- Create a fresh listener instance per test when the listener stores private operation state such as `UserConnection`.
- Instantiate the real entity schema through `UserConnection.EntitySchemaManager.GetInstanceByName(...)`.
- Fill only the minimal entity data needed for the scenario, typically with `CreateEntity(UserConnection)` and `SetDefColumnValues()`.
- Invoke only the minimal set of listener events required for the behavior under test, but keep the real event order whenever logic spans multiple events.
- Mock helper or service dependencies instead of testing heavy business logic through the listener itself.
- Pair this skill with `configuration-entity-event-listener` when production listener code also changes.

## Required Test Shape

Typical test location:
- `tests/<PACKAGE_NAME>/EntityEventListeners/<EntitySchemaName>EntityEventListenerTests.cs`

Typical fixture shape:

```csharp
[TestFixture]
[MockSettings(RequireMock.DBEngine)]
public class AccountAnniversaryEntityEventListenerTests : BaseConfigurationTestFixture {
	private AccountAnniversaryEntityEventListener _listener;
	private IAccountAnniversaryHelper _accountAnniversaryHelper;

	protected override void SetUp() {
		base.SetUp();
		_accountAnniversaryHelper = Substitute.For<IAccountAnniversaryHelper>();
		ClassFactory.RebindWithFactoryMethod(() => _accountAnniversaryHelper);
		_listener = new AccountAnniversaryEntityEventListener();
	}
}
```

## Workflow

1. Inspect which listener events actually contain or enable the behavior under test.
2. Create or reuse the entity schema instance with only the needed default and explicit field values.
3. Rebind helper dependencies through `ClassFactory` or the local composition mechanism used by the listener.
4. Create a fresh listener instance in `SetUp()` or the arrange section.
5. Invoke the minimal event chain in the real lifecycle order.
6. Assert the observable behavior: helper calls, validation messages, entity changes, or side effects.
7. Build and run the relevant tests sequentially.

## Event Invocation Rules

- If the listener logic lives only in `OnDeleting`, call only `OnDeleting`.
- If `OnSaved` depends on data captured in `OnSaving`, call `OnSaving` and then `OnSaved`.
- If the listener logic spans create or update specific hooks, preserve the real order from the Creatio pipeline.
- For validation handlers added in `OnSaving`, call `OnSaving` before raising or inspecting validation behavior.

## Coverage Checklist

1. Tests cover the exact listener method or event chain that enables the behavior.
2. Each test uses a fresh listener instance when private state matters.
3. Entity setup is minimal and readable.
4. Helper or service dependencies are mocked and verified directly.
5. Validation scenarios assert `ValidationMessages` rather than exception flow for expected failures.
6. Multi-event scenarios call methods in the correct order.

## References

Read only what you need:
- `references/test-patterns.md`: fixture shape, entity creation, helper rebinding, and event invocation examples
- `references/validation-test-patterns.md`: how to test `entity.Validating` handlers and validation messages
- `references/review-checklist.md`: review points and common listener-test defects

## Build And Verify

Typical commands:

```powershell
dotnet build .\MainSolution.slnx -c dev-n8 -v d
dotnet test .\tests\<PACKAGE_NAME>\<PACKAGE_NAME>.Tests.csproj -c dev-n8 --no-build
```

Use the matching `dev-nf` configuration for `net472` targets.

Run build and test sequentially in this workspace. Parallel `dotnet build` and `dotnet test` can lock package outputs under `obj`.

## What To Report Back

- Test files changed, with one-line reason per file
- Which listener event or event chain each test covers
- Which helper or service dependency was mocked and verified
- Build/test commands run, or the exact blocker if they were not run
"""
		),
		Create(
			"configuration-entity-event-listener-tests",
			"review-checklist",
			"docs://mcp/references/configuration-entity-event-listener-tests/review-checklist",
			"Reference resource for configuration-entity-event-listener-tests: review-checklist.",
			false,
			"""
# Review Checklist

## Fixture And Setup

1. Test fixture uses the workspace base class expected for configuration tests.
2. Listener instance is fresh per test when private state is involved.
3. Helper dependencies are substituted and rebound before the listener is exercised.
4. Entity schema is instantiated through `UserConnection.EntitySchemaManager`.

## Behavioral Coverage

1. Tests call the minimal event set required for the scenario.
2. Multi-event scenarios preserve the real lifecycle order.
3. Assertions focus on observable listener behavior: helper call, validation message, or entity mutation.
4. Validation tests verify `ValidationMessages` for expected failures.

## Common Defects

- Calling `OnSaved` without first calling `OnSaving` when the listener caches `UserConnection`
- Reusing one listener instance across multiple tests
- Over-mocking the entity instead of using the real schema instance
- Testing helper internals rather than the listener-to-helper collaboration
- Using the wrong event order for create or update scenarios
"""
		),
		Create(
			"configuration-entity-event-listener-tests",
			"test-patterns",
			"docs://mcp/references/configuration-entity-event-listener-tests/test-patterns",
			"Reference resource for configuration-entity-event-listener-tests: test-patterns.",
			false,
			"""
# Test Patterns

## Minimal Helper-Call Test

Use the smallest event chain that enables the listener logic:

```csharp
[TestFixture]
[MockSettings(RequireMock.DBEngine)]
public class AccountAnniversaryEntityEventListenerTests : BaseConfigurationTestFixture {
	private AccountAnniversaryEntityEventListener _listener;
	private IAccountAnniversaryHelper _accountAnniversaryHelper;

	protected override void SetUp() {
		base.SetUp();
		_accountAnniversaryHelper = Substitute.For<IAccountAnniversaryHelper>();
		ClassFactory.RebindWithFactoryMethod(() => _accountAnniversaryHelper);
		_listener = new AccountAnniversaryEntityEventListener();
	}

	[Test]
	public void OnSaved_Should_Call_CalculateRegistrationsAndBookedValue() {
		EntitySchema schema =
			UserConnection.EntitySchemaManager.GetInstanceByName("AccountAnniversary");
		Entity entity = schema.CreateEntity(UserConnection);
		entity.SetDefColumnValues();

		_listener.OnSaving(entity, null);
		_listener.OnSaved(entity, null);

		_accountAnniversaryHelper.Received(1).CalculateRegistrationsAndBookedValue();
	}
}
```

## Core Guidance

- Create the real schema instance through `EntitySchemaManager`.
- Prefer `SetDefColumnValues()` before adding scenario-specific values.
- Rebind helper dependencies before creating or invoking the listener.
- Use one listener instance per test when it caches state across events.

## Event Chain Selection

- `OnSaving` only: test shared before-save logic.
- `OnSaving` + `OnSaved`: test logic that captures state before save and uses it after save.
- `OnInserting` or `OnInserted`: test create-specific logic only.
- `OnUpdating` or `OnUpdated`: test update-specific logic only.
- `OnDeleting` or `OnDeleted`: test delete-specific logic only.

## What To Avoid

- Replaying the full lifecycle when one method is enough.
- Creating unnecessary records or unrelated mocks.
- Reusing a listener instance across tests.
- Verifying helper internals instead of the listener's observable collaboration.
"""
		),
		Create(
			"configuration-entity-event-listener-tests",
			"validation-test-patterns",
			"docs://mcp/references/configuration-entity-event-listener-tests/validation-test-patterns",
			"Reference resource for configuration-entity-event-listener-tests: validation-test-patterns.",
			false,
			"""
# Validation Test Patterns

## Testing Validation Subscription

When the listener subscribes to `entity.Validating` from `OnSaving`, first call `OnSaving`, then trigger validation through the entity flow available in the workspace test base.

Example pattern:

```csharp
[Test]
public void OnSaving_Should_Add_ValidationMessage_When_Entity_Is_Invalid() {
	EntitySchema schema =
		UserConnection.EntitySchemaManager.GetInstanceByName("AccountAnniversary");
	Entity entity = schema.CreateEntity(UserConnection);
	entity.SetDefColumnValues();

	_listener.OnSaving(entity, null);

	entity.Validate();

	entity.ValidationMessages.Count.Should().BeGreaterThan(0);
	entity.ValidationMessages[0].Text.Should().Contain("Validation failed");
}
```

## Validation Assertions

- Assert that validation messages were added.
- Assert the failing column when the listener sets it.
- Assert the visible message text or a stable part of it.
- Prefer testing the user-visible validation outcome over private helper implementation.

## If Direct Validation Trigger Differs

Some workspace fixtures expose validation differently. If `entity.Validate()` is not the right trigger in the local package tests:

1. Inspect existing entity-validation tests in the package.
2. Reuse the local trigger pattern.
3. Keep the skill output explicit about which trigger path was chosen and why.
"""
		),
		Create(
			"creatio-composable-app-development",
			null,
			"docs://mcp/guides/creatio-composable-app-development",
			"Guidance for composable app development on the Creatio platform. Use when Codex needs to create or modify Creatio packages, Freedom UI pages, client schemas, custom components, data sources, page handlers, or CLIO-based package workflows in local workspaces or source-controlled repositories.",
			true,
			"""
# Creatio Composable App Development

Use this skill for source-level Creatio work, especially when the task involves packages, Freedom UI, page schemas, or CLIO-based delivery.

## Operating Rules

- Prefer package-safe customization. Extend through custom packages instead of assuming direct core edits are acceptable.
- Treat Creatio artifacts as package-scoped. Identify the package, schema, and object ownership before editing.
- Prefer existing workspace conventions over generic samples. If the repository already has naming, packaging, or schema patterns, follow them.
- Use CLIO command names exactly when you mention them. The local command set in this environment includes `new-pkg`, `push-pkg`, and `pull-pkg`.
- For niche Creatio APIs or Freedom UI patterns, consult [references/official-docs.md](references/official-docs.md) before inventing a structure.

## Workflow

1. Identify the artifact type.
   Package work usually starts in a package directory, package manifest, schema folder, or CLIO workflow.
   Freedom UI work usually starts in a page schema, client module schema, view model config, or handler chain.

2. Locate the owning package and schema.
   Determine which package should contain the change and whether the requested behavior belongs in an existing schema, a replacement schema, or a new custom schema.

3. Inspect the current implementation before designing the change.
   For Freedom UI pages, look for existing handlers, converters, validators, data source config, and request chains.
   For custom components, inspect the module declaration, package dependencies, and any existing usage of `@creatio-devkit/common`.

4. Choose the smallest extension surface that solves the task.
   Prefer page handlers, schema sections, and package-contained modules before introducing broader architectural changes.
   If no-code designer settings are sufficient, note that explicitly instead of forcing code.

5. Keep the delivery path explicit.
   If the task changes a package, mention the CLIO path that normally validates or ships that package.
   Use local docs or command help when command options are not obvious.

## Common Task Map

### Package Lifecycle

- Create a new package: `clio new-pkg <PACKAGE_NAME>`
- Pull a package from an environment: `clio pull-pkg <PACKAGE_NAME>`
- Push or install a package: `clio push-pkg <PACKAGE_NAME>`

Use package lifecycle commands when the user is creating a new customization package, synchronizing a local package, or deploying a finished change to an environment.

### Freedom UI Pages

- Start by finding the page schema and the surrounding client schema sections.
- Prefer handler-based business logic for page events, data load/save hooks, and request interception.
- Check whether the behavior belongs in page visibility, validation, filtering, data querying, navigation, or save/load flows before adding custom code.

### Custom Components

- Confirm whether a standard Freedom UI component plus page logic is enough before creating a custom component.
- If a custom component is required, keep it package-local and verify module dependencies first.
- When the component is based on a classic page element, inspect the AMD module declaration and dependency list carefully.

### Review and Refactor Work

- Validate package boundaries first.
- Look for hard-coded object names, duplicated request logic, and handler chains that mix unrelated responsibilities.
- Flag risky changes that affect shared schemas or out-of-the-box record pages across multiple apps.

## What to Inspect

- Package directory structure and manifest files
- Schema names and prefixes
- Existing client module handlers, converters, and validators
- Data source declarations and save/load request chains
- Existing CLIO docs or command help when deployment steps matter

## Reference Set

Read [references/official-docs.md](references/official-docs.md) when you need:

- Official Creatio documentation links for app architecture, Freedom UI pages, and custom components
- Local CLIO command references for package lifecycle commands
- Guardrails about package ownership, page customization boundaries, and component implementation choices
"""
		),
		Create(
			"creatio-composable-app-development",
			"official-docs",
			"docs://mcp/references/creatio-composable-app-development/official-docs",
			"Reference resource for creatio-composable-app-development: official-docs.",
			false,
			"""
# Official Docs And Local References

Use these references when you need confirmation for Creatio-specific concepts, especially when package boundaries, Freedom UI behavior, or CLIO delivery details matter.

## Official Creatio Docs

### Application architecture and package model

- Creating applications on Creatio platform:
  https://academy.creatio.com/docs/developer/architecture/development_in_creatio/creating_applications_on_creatio_platform/overview
- Use this reference for:
  - package boundaries
  - application structure
  - why delivery happens through packages instead of core edits

### Freedom UI page customization

- Freedom UI page customization basics:
  https://academy.creatio.com/docs/sites/academy_en/files/pdf/guide/1407/Freedom_UI_page_customization_basics_8.0.pdf
- Use this reference for:
  - handlers, validators, and converters
  - page load and save customization
  - deciding whether the behavior belongs in page source code

### Custom Freedom UI components

- Custom UI component based on a classic Creatio page element:
  https://academy.creatio.com/docs/sites/academy_en/files/pdf/guide/1403/Implement_a_custom_component_8.0.pdf
- Use this reference for:
  - `@creatio-devkit/common`
  - AMD module shape
  - custom component implementation boundaries

## Local CLIO References

These are machine-local references verified from the workspace on 2026-03-30.

- Command index:
  `C:\Projects\clio\clio\Commands.md`
- Package creation:
  `C:\Projects\clio\clio\docs\commands\new-pkg.md`
- Package pull:
  `C:\Projects\clio\clio\docs\commands\pull-pkg.md`
- Package push:
  `C:\Projects\clio\clio\docs\commands\push-pkg.md`

## Guidance

- Prefer the official Creatio docs for product concepts and framework behavior.
- Prefer the local CLIO docs for exact command names and examples in this environment.
- If the official docs mention an older version label, still use them for structure and terminology, but verify the current repository conventions before copying code verbatim.
"""
		),
		Create(
			"creatio-freedom-iframe-section",
			null,
			"docs://mcp/guides/creatio-freedom-iframe-section",
			"Create and validate a Creatio Freedom UI section that only hosts an external UI in a single crt.IFrame. Use for composable-app shells (React/SPA UI served by REST endpoint) and reusable section bootstrap in package data.",
			true,
			"""
# Creatio Freedom IFrame Section

## Overview

Use this skill when you need a Creatio section that works as a shell for external UI (for example React build under package `Files/ui`) and should contain only one `crt.IFrame`.

This skill is optimized for composable applications like Radar.

## Target Result

1. A section is visible in Creatio navigation.
2. Section page is based on `BlankPageTemplate`.
3. Page contains only one `crt.IFrame` with runtime-aware URL:
   - net8: `/rest/<UiService>/ui`
   - .NET Framework: `/0/rest/<UiService>/ui`
4. IFrame runs without sandbox restrictions required for same-origin assets:
   - `isSandbox: false`
   - `sandbox: null`
5. Section registration data is valid (`SysModule`, `SysModuleEntity`, `SysModuleInWorkplace`).

## Required Inputs

- `PACKAGE_NAME` (for example `Radar`)
- `SECTION_CODE` (for example `RADAR`)
- `SECTION_SCHEMA_NAME` (for example `RADAR_ListPage`)
- `UI_URL`
  - net8 example: `/rest/RadarUiService/ui`
  - .NET Framework example: `/0/rest/RadarUiService/ui`
- `WORKPLACE_ID` or workplace lookup key (usually Studio for dev)
- Operation code for access policy from module spec (do not invent)

If access mapping is missing in spec/doc, ask developer before implementing endpoint changes.

## File Locations

- Section schema JS:
  - `packages/<PACKAGE_NAME>/Schemas/<SECTION_SCHEMA_NAME>/<SECTION_SCHEMA_NAME>.js`
- Section registration data:
  - `packages/<PACKAGE_NAME>/Data/SysModule_<SECTION_CODE>/data.json`
  - `packages/<PACKAGE_NAME>/Data/SysModuleEntity_<SECTION_CODE>/data.json`
  - `packages/<PACKAGE_NAME>/Data/SysModuleInWorkplace_<SECTION_CODE>/data.json`

## Implementation Steps

1. Create/Update section page schema (`<SECTION_SCHEMA_NAME>.js`):
- Parent template must be `BlankPageTemplate`.
- Add single `crt.GridContainer` + single `crt.IFrame`.
- No `PDS`, no list datasets, no extra visual controls.

2. Configure iframe node:
- `type: "crt.IFrame"`
- `isSandbox: false`
- `sandbox: null`
- `contentType: "url"`
- `urlContent: "<UI_URL>"`

3. Register section in package data:
- `SysModule`: code/caption/section schema binding.
- `SysModuleEntity`: connect section module to target entity (or app-specific placeholder mapping).
- `SysModuleInWorkplace`: put section into target workplace.

4. Data integrity rules:
- Never store literal string `"null"` in image/icon lookup fields.
- If old `SysModuleInWorkplace` row has zero GUID links, create a new row with new `Id`.

5. Build/deploy loop (repo-specific):
- If UI changed: `cd src/ui/<UI_APP> && npm run build:creatio`
- C# build: `dotnet build MainSolution.slnx -c dev-n8`
- Install package: `clio push-pkg packages/<PACKAGE_NAME> -e "$CLIO_ENV" --Safe false`

## Mandatory Validation

### A. HTTP checks

1. Runtime-correct UI URL returns `200` for authenticated session:
   - net8: `GET /rest/<UiService>/ui`
   - .NET Framework: `GET /0/rest/<UiService>/ui`
2. Referenced JS/CSS assets from returned HTML return `200`.

### B. OData checks (section metadata)

Verify rows exist and links are non-empty:

- `SysModule` row for `<SECTION_CODE>`
- `SysModuleInWorkplace` row for module/workplace
- `SysModuleInWorkplace.SysWorkplaceId != 00000000-0000-0000-0000-000000000000`
- `SysModuleInWorkplace.SysModuleId != 00000000-0000-0000-0000-000000000000`

### C. Browser DOM checks

On section page:

- `#iframeRegular` is visible
- `#iframeSandboxed` is hidden
- Network has successful load for the runtime-correct `/rest` or `/0/rest` UI URL and main assets

## Troubleshooting Order

1. Reproduce with terminal (`AuthService.svc/Login` + `curl`) first.
2. Check latest `Error.log`.
3. Inspect browser console/network/DOM.
4. Then patch.

Do not start from manual refresh-only debugging.

## Output Requirements

When using this skill, provide:

1. Files changed.
2. Section registration summary (module/workplace/schema ids or keys).
3. Validation evidence:
- UI URL + asset status codes,
- OData presence checks,
- one log exception line or explicit "no blocking section errors found".

## References

- `references/creatio-iframe-section-template.md`
- Radar working example:
  - `packages/Radar/Schemas/RADAR_ListPage/RADAR_ListPage.js`
  - `packages/Radar/Data/SysModule_RADAR/data.json`
  - `packages/Radar/Data/SysModuleEntity_RADAR/data.json`
  - `packages/Radar/Data/SysModuleInWorkplace_RADAR/data.json`
"""
		),
		Create(
			"creatio-freedom-iframe-section",
			"creatio-iframe-section-template",
			"docs://mcp/references/creatio-freedom-iframe-section/creatio-iframe-section-template",
			"Reference resource for creatio-freedom-iframe-section: creatio-iframe-section-template.",
			false,
			"""
---
interface:
  display_name: "Creatio Freedom IFrame Section"
  short_description: "Scaffold a Freedom UI section shell with a single iframe-bound external UI."
  default_prompt: "Create a Creatio Freedom UI section based on BlankPageTemplate with one crt.IFrame and validate registration + runtime checks."
---
# Creatio Freedom IFrame Section Template

Use this template when creating a new section that embeds an external UI (React/SPA) via runtime-aware UI route:
- net8: `/rest/<UiService>/ui`
- .NET Framework: `/0/rest/<UiService>/ui`

## Inputs

- `PACKAGE_NAME`
- `SECTION_CODE`
- `SECTION_SCHEMA_NAME`
- `SECTION_CAPTION`
- `UI_URL`
  - net8 example: `/rest/RadarUiService/ui`
  - .NET Framework example: `/0/rest/RadarUiService/ui`
- `WORKPLACE_ID` (or resolved Studio workplace id)

## 1) Section Page Schema

Path:
- `packages/<PACKAGE_NAME>/Schemas/<SECTION_SCHEMA_NAME>/<SECTION_SCHEMA_NAME>.js`

Skeleton:

```js
define("<SECTION_SCHEMA_NAME>", [], function() {
  return {
    viewConfigDiff: [
      {
        operation: "insert",
        name: "MainContainer",
        values: {
          type: "crt.GridContainer",
          rows: "minmax(0, 1fr)",
          columns: ["minmax(0, 1fr)"],
          gap: { columnGap: "none", rowGap: "none" },
          items: []
        },
        parentName: "CardContentContainer",
        propertyName: "items",
        index: 0
      },
      {
        operation: "insert",
        name: "RadarIFrame",
        values: {
          type: "crt.IFrame",
          contentType: "url",
          urlContent: "<UI_URL>",
          isSandbox: false,
          sandbox: null,
          fitContentToViewport: true,
          scrollType: "none"
        },
        parentName: "MainContainer",
        propertyName: "items",
        index: 0
      }
    ]
  };
});
```

Requirements:
- Parent template must be `BlankPageTemplate`.
- Keep only one iframe control.
- Do not add `PDS`/lists/data-source components.

## 2) Data Registration Files

- `packages/<PACKAGE_NAME>/Data/SysModule_<SECTION_CODE>/data.json`
- `packages/<PACKAGE_NAME>/Data/SysModuleEntity_<SECTION_CODE>/data.json`
- `packages/<PACKAGE_NAME>/Data/SysModuleInWorkplace_<SECTION_CODE>/data.json`

Rules:
- Avoid string literal `"null"` in icon/image fields.
- If `SysModuleInWorkplace` has zero-GUID links, add new valid row with a new `Id`.

## 3) Build and Deploy

```bash
source .env
cd src/ui/<UI_APP_NAME> && npm run build:creatio
cd /path/to/repo
dotnet build MainSolution.slnx -c dev-n8
clio push-pkg packages/<PACKAGE_NAME> -e "$CLIO_ENV" --Safe false
```

## 4) Runtime Validation

1. UI endpoint:
- net8: `GET <CREATIO_URL>/rest/<UiService>/ui` -> `200`
- .NET Framework: `GET <CREATIO_URL>/0/rest/<UiService>/ui` -> `200`

2. Assets:
- JS/CSS asset URLs from HTML -> `200`

3. OData registration checks:
- `SysModule` exists for `<SECTION_CODE>`
- `SysModuleInWorkplace` exists with non-zero `SysModuleId` and `SysWorkplaceId`

4. Browser DOM checks:
- `#iframeRegular` visible
- `#iframeSandboxed` hidden

5. Logs:
- Check latest `Error.log` and report first blocking backend error (if any).

## 5) Access Policy

- Enforce operation-level authorization for related backend endpoints.
- Use operation codes from module spec/doc.
- If mapping is missing, request it from developer before implementation.
"""
		),
		Create(
			"feature-toggle",
			null,
			"docs://mcp/guides/feature-toggle",
			"Implement or review Creatio feature-toggle checks in C# package code such as packages/PkgOne/Files/src/cs or packages/PkgOne/src/cs. Use when gating behavior on a Creatio feature flag, adding `Creatio.FeatureToggling.Features.GetIsEnabled(...)`, or moving feature names into `Constants.cs`. If the task includes unit tests, also use feature-toggle-tests.",
			true,
			"""
# Feature Toggle

## Non-Negotiable Rules

- Prefer `const string` feature names in `Constants.cs`. Do not introduce new inline string literals in production code when a package constants file exists or should exist.
- Use `Creatio.FeatureToggling.Features.GetIsEnabled(...)` for the decision point.
- Evaluate the feature once per logical branch and store the result in a local variable when the value is reused.
- Keep both enabled and disabled paths explicit. Do not hide the fallback behavior.
- If production code changes under a feature flag, also apply `feature-toggle-tests`.

## Required Pattern

Use this shape unless the local package already has a stronger convention:

```csharp
var isFeatureEnabled =
	Creatio.FeatureToggling.Features.GetIsEnabled(Constants.FeatureName);
```

Behavior reminder:
- `true` means the feature exists and is enabled globally, for the current user, or for a user group the current user belongs to.
- `false` means the feature is missing or not enabled for the current execution context.

## Constants Pattern

Prefer a package-level constants holder such as:

```csharp
public static class Constants {
	public const string FeatureName = "FeatureName";
}
```

Rules:
- Reuse an existing `Constants.cs` file when present.
- Add a narrowly named constant for each feature flag instead of reusing unrelated constants.
- Reference the constant from production code and tests.
- Do not duplicate the raw feature name in multiple files.

## Implementation Workflow

1. Find the package `Constants.cs` file. Add a feature-name constant there if one does not already exist.
2. Add the feature check close to the behavior it gates.
3. Keep the non-feature path readable and unchanged unless the feature requires a deliberate fallback.
4. If the method has multiple gated branches, compute the feature state once and reuse the variable.
5. If tests are required, apply `feature-toggle-tests`.
6. Build and run the relevant tests after production changes.

## Review Checklist

1. Feature name comes from `Constants.cs`.
2. The code uses `Creatio.FeatureToggling.Features.GetIsEnabled(...)`.
3. The feature state is not recomputed unnecessarily.
4. Enabled and disabled behavior are both understandable from the code.
5. The gated logic does not silently change unrelated behavior.
6. Tests cover both enabled and disabled outcomes when the behavior affects observable results.

## References

Read only what you need:
- `references/constants-pattern.md`: where to keep feature-name constants and how to reference them
- `references/implementation-patterns.md`: example usage in methods and review guidance
- `references/runtime-behavior.md`: what `GetIsEnabled` means for global, user, and group assignments

## Build And Verify

Typical commands:

```powershell
dotnet build .\MainSolution.slnx -c dev-n8 -v d
dotnet test .\tests\<PACKAGE_NAME>\<PACKAGE_NAME>.Tests.csproj -c dev-n8 --no-build
```

Use the matching `dev-nf` configuration for `net472` targets.

Run build and test sequentially in this workspace. Parallel `dotnet build` and `dotnet test` can lock package outputs under `obj`.

## What To Report Back

- Files changed, with one-line reason per file
- Which feature flag constant was added or reused
- Where the feature check was added and what behavior it gates
- Tests added or updated, or the reason tests were not changed
- Build/test commands run, or the exact blocker if not run
"""
		),
		Create(
			"feature-toggle",
			"constants-pattern",
			"docs://mcp/references/feature-toggle/constants-pattern",
			"Reference resource for feature-toggle: constants-pattern.",
			false,
			"""
# Constants Pattern

Keep feature names in a package-level `Constants.cs` file and reuse them from both production code and tests.

## Example

```csharp
namespace PkgOne {
	public static class Constants {
		public const string FeatureName = "FeatureName";
	}
}
```

## Rules

- Prefer extending an existing `Constants.cs` file over introducing a second constants holder.
- Use a specific constant name that communicates the gated behavior.
- Reference the constant everywhere the feature name is needed.
- Do not mix raw string literals and constants for the same feature.
"""
		),
		Create(
			"feature-toggle",
			"implementation-patterns",
			"docs://mcp/references/feature-toggle/implementation-patterns",
			"Reference resource for feature-toggle: implementation-patterns.",
			false,
			"""
# Implementation Patterns

Reuse the local package style when it already contains feature-toggle usage. Otherwise add a small, explicit gate around the affected behavior.

## Basic Usage

```csharp
public CalculationResult Calculate(CalculationRequest request) {
	var isFeatureEnabled =
		Creatio.FeatureToggling.Features.GetIsEnabled(Constants.FeatureName);

	if (!isFeatureEnabled) {
		return CalculateLegacy(request);
	}

	return CalculateWithFeature(request);
}
```

## Review Guidance

- Compute the feature state once when multiple branches depend on it.
- Keep the feature check near the behavior it controls.
- Leave the fallback path easy to read.
- Prefer feature-gating orchestration code, not low-level helper lines that make the behavior harder to trace.
"""
		),
		Create(
			"feature-toggle",
			"runtime-behavior",
			"docs://mcp/references/feature-toggle/runtime-behavior",
			"Reference resource for feature-toggle: runtime-behavior.",
			false,
			"""
# Runtime Behavior

`Creatio.FeatureToggling.Features.GetIsEnabled(...)` returns:

- `true` when the feature exists and is enabled globally
- `true` when the feature is enabled for the current user
- `true` when the feature is enabled for a user group the current user belongs to
- `false` otherwise

Treat `false` as the default and keep the fallback behavior explicit in code and tests.
"""
		),
		Create(
			"feature-toggle-tests",
			null,
			"docs://mcp/guides/feature-toggle-tests",
			"Write, update, or review unit tests for Creatio feature-toggle behavior in package test projects such as tests/PkgOne or package-local test folders. Use when mocking a feature state with `FeatureRequest` and `Creatio.FeatureToggling.TestKit.FeatureStub.Setup(...)`, adding enabled and disabled coverage, or moving feature names into `Constants.cs`. Pair with feature-toggle when production code changes.",
			true,
			"""
# Feature Toggle Tests

## Non-Negotiable Rules

- Add or update tests for production feature-toggle changes unless the user explicitly says not to.
- Reuse the same feature-name constant from `Constants.cs`. Do not duplicate raw feature-name literals in tests.
- Prefer setting the default feature state in shared fixture or setup code when most tests use the same value.
- Add at least one enabled-path test and one disabled-path test when the feature changes observable behavior.
- Pair this skill with `feature-toggle` when production code also changes.

## Required Stub Pattern

Disabled:

```csharp
FeatureRequest request =
	new Creatio.FeatureToggling.Providers.FeatureRequest(Constants.FeatureName);
Creatio.FeatureToggling.TestKit.FeatureStub.Setup(request, false);
```

Enabled:

```csharp
FeatureRequest request =
	new Creatio.FeatureToggling.Providers.FeatureRequest(Constants.FeatureName);
Creatio.FeatureToggling.TestKit.FeatureStub.Setup(request, true);
```

Use `true` for enabled coverage and `false` for disabled coverage.

## Workflow

1. Reuse or add the feature-name constant in `Constants.cs`.
2. Set the default feature state in shared fixture or setup code when that keeps the test suite simpler.
3. Cover the observable enabled behavior.
4. Cover the observable disabled or fallback behavior.
5. Override the stub in the arrange section only for tests that need a different feature state.
6. Build and run the relevant tests sequentially.

## Coverage Checklist

1. Feature enabled path returns or triggers the expected result.
2. Feature disabled path preserves the fallback behavior.
3. Tests reference `Constants.cs` instead of repeating the raw feature name.
4. Feature stub setup is obvious from the fixture setup or an explicit per-test override.
5. Assertions focus on the gated behavior, not unrelated implementation details.

## References

Read only what you need:
- `references/feature-stub-pattern.md`: exact `FeatureRequest` and `FeatureStub.Setup(...)` usage
- `references/constants-and-fixture-pattern.md`: how to keep constants shared across production code and tests
- `references/test-coverage-checklist.md`: minimum coverage expectations for feature-gated logic

## Build And Verify

Typical commands:

```powershell
dotnet build .\MainSolution.slnx -c dev-n8 -v d
dotnet test .\tests\<PACKAGE_NAME>\<PACKAGE_NAME>.Tests.csproj -c dev-n8 --no-build
```

Use the matching `dev-nf` configuration for `net472` targets.

Run build and test sequentially in this workspace. Parallel `dotnet build` and `dotnet test` can lock package outputs under `obj`.

## What To Report Back

- Test files changed, with one-line reason per file
- Which feature constant was added or reused
- Which enabled and disabled behaviors are covered
- Build/test commands run, or the exact blocker if not run
"""
		),
		Create(
			"feature-toggle-tests",
			"constants-and-fixture-pattern",
			"docs://mcp/references/feature-toggle-tests/constants-and-fixture-pattern",
			"Reference resource for feature-toggle-tests: constants-and-fixture-pattern.",
			false,
			"""
# Constants And Fixture Pattern

Reuse the same feature constant from production code and configure the stub in the local fixture style.

## Example

```csharp
protected override void SetUp() {
	base.SetUp();

	FeatureRequest request =
		new Creatio.FeatureToggling.Providers.FeatureRequest(Constants.FeatureName);
	Creatio.FeatureToggling.TestKit.FeatureStub.Setup(request, false);
}
```

## Rules

- Prefer `Constants.cs` over raw feature-name literals.
- Reuse the local fixture style from the package test project when one already exists.
- If a test changes the feature state from the fixture default, make that override explicit in the arrange section.
"""
		),
		Create(
			"feature-toggle-tests",
			"feature-stub-pattern",
			"docs://mcp/references/feature-toggle-tests/feature-stub-pattern",
			"Reference resource for feature-toggle-tests: feature-stub-pattern.",
			false,
			"""
# Feature Stub Pattern

Use `FeatureRequest` plus `FeatureStub.Setup(...)` to control the feature state in tests.

## Disabled

```csharp
FeatureRequest request =
	new Creatio.FeatureToggling.Providers.FeatureRequest(Constants.FeatureName);
Creatio.FeatureToggling.TestKit.FeatureStub.Setup(request, false);
```

## Enabled

```csharp
FeatureRequest request =
	new Creatio.FeatureToggling.Providers.FeatureRequest(Constants.FeatureName);
Creatio.FeatureToggling.TestKit.FeatureStub.Setup(request, true);
```

## Guidance

- Build the request from the shared constant.
- Prefer shared fixture or setup code for the default feature state.
- Override the stub in the arrange phase only when a test needs a different state.
- Add separate tests when enabled and disabled behavior differ.
"""
		),
		Create(
			"feature-toggle-tests",
			"test-coverage-checklist",
			"docs://mcp/references/feature-toggle-tests/test-coverage-checklist",
			"Reference resource for feature-toggle-tests: test-coverage-checklist.",
			false,
			"""
# Test Coverage Checklist

Minimum coverage for feature-gated code:

1. Feature enabled path.
2. Feature disabled path.
3. Shared constant usage from `Constants.cs`.
4. Any observable output or branch difference caused by the feature state.

Prefer focused tests that prove the gated behavior instead of reproducing unrelated setup noise.
"""
		),
		Create(
			"sys-setting",
			null,
			"docs://mcp/guides/sys-setting",
			"Implement or review Creatio system-setting access in C# package code such as packages/PkgOne/Files/src/cs or packages/PkgOne/src/cs. Use when reading a system setting with `Terrasoft.Core.Configuration.SysSettings.TryGetValue(...)`, `GetValue(...)`, or `GetDefValue(...)`, introducing or reusing `Constants.SysSettingCodes.<SettingName>`, or replacing inline sys-setting code strings with package constants. If the task includes unit tests, also use sys-setting-tests.",
			true,
			"""
# Sys Setting

## Non-Negotiable Rules

- Keep every sys-setting code string in `Constants.cs`. Prefer a nested holder such as `Constants.SysSettingCodes`, but extend the existing package constants layout when one is already established instead of introducing a competing holder.
- Reuse the same constant from production code and tests. Do not repeat the raw sys-setting code in multiple files.
- Use `Terrasoft.Core.Configuration.SysSettings` directly for reads.
- Choose the accessor that matches the behavior: `TryGetValue(...)` for optional reads, `GetValue(...)` for current value reads, `GetDefValue(...)` for default-value reads.
- If production sys-setting code changes, also apply `sys-setting-tests`.

## Required Access Patterns

Optional read:

```csharp
bool isSettingValue =
	Terrasoft.Core.Configuration.SysSettings.TryGetValue(
		UserConnection,
		Constants.SysSettingCodes.MySetting,
		out object settingValue);
```

Current value:

```csharp
var value = Terrasoft.Core.Configuration.SysSettings.GetValue(
	UserConnection,
	Constants.SysSettingCodes.MySetting);
```

Default value:

```csharp
var defValue = Terrasoft.Core.Configuration.SysSettings.GetDefValue(
	UserConnection,
	Constants.SysSettingCodes.MySetting);
```

## Constants Pattern

Prefer this shape unless the package already has a stronger convention:

```csharp
public static class Constants {
	public static class SysSettingCodes {
		public const string MySetting = "MySetting";
	}
}
```

Rules:
- Extend an existing `Constants.cs` file when present.
- Prefer `Constants.SysSettingCodes`, but keep the existing package constants layout if one already exists.
- Add a narrowly named constant for each sys setting.
- Reference the constant everywhere the code is needed.

## Workflow

1. Find the package `Constants.cs` file and add or reuse `Constants.SysSettingCodes.<SettingName>`.
2. Identify whether the code needs `TryGetValue(...)`, `GetValue(...)`, or `GetDefValue(...)`.
3. Place the sys-setting read close to the behavior that depends on it.
4. Reuse the returned value instead of re-reading the same setting inside one logical branch.
5. Keep fallback behavior explicit when `TryGetValue(...)` can return `false`.
6. If tests are required, apply `sys-setting-tests`.
7. Build and run the relevant tests after production changes.

## Review Checklist

1. Sys-setting code string comes from `Constants.SysSettingCodes`.
2. The accessor matches the intent: optional, current value, or default value.
3. The same setting is not re-read unnecessarily inside the same flow.
4. `TryGetValue(...)` failure handling is explicit and readable.
5. Tests cover the observable behavior that depends on the setting when behavior changes.

## References

Read only what you need:
- `references/constants-pattern.md`: where to keep sys-setting code constants
- `references/access-patterns.md`: exact `TryGetValue(...)`, `GetValue(...)`, and `GetDefValue(...)` usage
- `references/review-checklist.md`: implementation and review reminders for sys-setting reads

## Build And Verify

Typical commands:

```powershell
dotnet build .\MainSolution.slnx -c dev-n8 -v d
dotnet test .\tests\<PACKAGE_NAME>\<PACKAGE_NAME>.Tests.csproj -c dev-n8 --no-build
```

Use the matching `dev-nf` configuration for `net472` targets.

Run build and test sequentially in this workspace. Parallel `dotnet build` and `dotnet test` can lock package outputs under `obj`.

## What To Report Back

- Files changed, with one-line reason per file
- Which sys-setting constant was added or reused
- Which accessor was chosen and why
- Tests added or updated, or the reason tests were not changed
- Build/test commands run, or the exact blocker if not run
"""
		),
		Create(
			"sys-setting",
			"access-patterns",
			"docs://mcp/references/sys-setting/access-patterns",
			"Reference resource for sys-setting: access-patterns.",
			false,
			"""
# Access Patterns

Use the `Terrasoft.Core.Configuration.SysSettings` accessor that matches the intended behavior.

## `TryGetValue(...)`

Use when the code can proceed differently if the setting is missing or unavailable.

```csharp
bool isSettingValue = Terrasoft.Core.Configuration.SysSettings.TryGetValue(
	UserConnection,
	Constants.SysSettingCodes.MySetting,
	out object settingValue);
```

## `GetValue(...)`

Use when the current effective setting value is required.

```csharp
var value = Terrasoft.Core.Configuration.SysSettings.GetValue(
	UserConnection,
	Constants.SysSettingCodes.MySetting);
```

## `GetDefValue(...)`

Use when the default setting value is required instead of the current resolved value.

```csharp
var defValue = Terrasoft.Core.Configuration.SysSettings.GetDefValue(
	UserConnection,
	Constants.SysSettingCodes.MySetting);
```

## Rules

- Pass `UserConnection` to `TryGetValue(...)` and `GetValue(...)`.
- Keep the sys-setting code in `Constants.SysSettingCodes`.
- Reuse a local variable when later code needs the same result more than once.
"""
		),
		Create(
			"sys-setting",
			"constants-pattern",
			"docs://mcp/references/sys-setting/constants-pattern",
			"Reference resource for sys-setting: constants-pattern.",
			false,
			"""
# Constants Pattern

Keep sys-setting code strings in a package-level `Constants.cs` file and prefer a nested `SysSettingCodes` holder when the package does not already have an established constants layout.

## Example

```csharp
namespace PkgOneApp {
	public static class Constants {
		public static class SysSettingCodes {
			public const string MySetting = "MySetting";
		}
	}
}
```

## Rules

- Reuse an existing `Constants.cs` file when present.
- Prefer `Constants.SysSettingCodes`, but do not introduce a second constants pattern if the package already uses another established layout.
- Add one narrowly named constant per system setting.
- Do not mix raw string literals and constants for the same sys setting.
"""
		),
		Create(
			"sys-setting",
			"review-checklist",
			"docs://mcp/references/sys-setting/review-checklist",
			"Reference resource for sys-setting: review-checklist.",
			false,
			"""
# Review Checklist

Use this list when reviewing sys-setting access changes.

1. The sys-setting code comes from `Constants.SysSettingCodes`.
2. The chosen accessor matches the behavior under test or implementation.
3. `TryGetValue(...)` callers handle the `false` path explicitly.
4. The same setting is not re-read unnecessarily in the same logical branch.
5. Tests were updated when the setting changes observable behavior.
"""
		),
		Create(
			"sys-setting-tests",
			null,
			"docs://mcp/guides/sys-setting-tests",
			"Write, update, or review unit tests for Creatio system-setting behavior in package test projects such as tests/PkgOne or package-local test folders. Use when decorating a test class with `[MockSettings(RequireMock.All)]`, overriding `SetupSysSettings()`, configuring `FakeSysSettings`, mocking `FakeSysSettingsEngine` for `GetValue`, `GetDefValue`, or `TryGetValue`, or replacing inline sys-setting code strings with `Constants.SysSettingCodes`. Pair with sys-setting when production code changes.",
			true,
			"""
# Sys Setting Tests

## Non-Negotiable Rules

- Decorate the test class with `[MockSettings(RequireMock.All)]`.
- Reuse the same sys-setting code constant from `Constants.SysSettingCodes`. Do not repeat raw sys-setting strings in tests.
- Override `SetupSysSettings()` when the test fixture reads sys settings.
- Register every required setting with `FakeSysSettings.Setup(...)`.
- Mock `FakeSysSettingsEngine` for each accessor the production code uses: `GetDefaultSettingsValue(...)`, `GetSettingsValue(...)`, or `TryGetSettingsValue(...)`.
- Pair this skill with `sys-setting` when production code also changes.

## Required Fixture Pattern

Mock only the accessor or accessors used by the production code under test. The example below shows all three for reference.

```csharp
[MockSettings(RequireMock.All)]
public class MyTests : MyTestsBase {
	protected override void SetupSysSettings() {
		base.SetupSysSettings();

		FakeSysSettings mySetting = new FakeSysSettings {
			Code = PkgOneApp.Constants.SysSettingCodes.MySetting,
		};

		FakeSysSettings.Setup(new[] { mySetting });
		FakeSysSettingsEngine engine = Substitute.For<FakeSysSettingsEngine>();
		FakeSysSettingsEngine.Setup(engine);
		const string mockValue = "mock value";

		engine.GetDefaultSettingsValue(Arg.Is(PkgOneApp.Constants.SysSettingCodes.MySetting))
			.Returns(mockValue + "GetDefValue");

		engine.GetSettingsValue(PkgOneApp.Constants.SysSettingCodes.MySetting, Arg.Any<Guid>())
			.Returns(_ => mockValue + "GetSettingsValue");

		engine.TryGetSettingsValue(
				PkgOneApp.Constants.SysSettingCodes.MySetting,
				Arg.Any<Guid>(),
				out Arg.Any<object>())
			.Returns(callInfo => {
				callInfo[2] = mockValue + "TryGetSettingsValue";
				return true;
			});
	}
}
```

## Workflow

1. Add `[MockSettings(RequireMock.All)]` to the test class.
2. Reuse or add the setting code under `Constants.SysSettingCodes`.
3. Override `SetupSysSettings()` and call `base.SetupSysSettings()`.
4. Register the needed `FakeSysSettings` entries with the same constants used by production code.
5. Set up `FakeSysSettingsEngine` responses for the sys-setting accessor used in the production path under test.
6. Add focused assertions for the observable behavior driven by the mocked setting values.
7. Build and run tests sequentially.

## Coverage Checklist

1. The test class has `[MockSettings(RequireMock.All)]`.
2. `SetupSysSettings()` registers every setting the test subject reads.
3. `GetDefaultSettingsValue(...)` is mocked when production code calls `GetDefValue(...)`.
4. `GetSettingsValue(...)` is mocked when production code calls `GetValue(...)`.
5. `TryGetSettingsValue(...)` is mocked when production code calls `TryGetValue(...)`.
6. Tests use `Constants.SysSettingCodes` instead of raw strings.
7. Assertions cover the behavior affected by the sys-setting value.

## References

Read only what you need:
- `references/mock-settings-attribute.md`: when and why to require `[MockSettings(RequireMock.All)]`
- `references/setup-sys-settings-pattern.md`: exact `SetupSysSettings()` and `FakeSysSettingsEngine` pattern
- `references/coverage-checklist.md`: minimum expectations for sys-setting test coverage

## Build And Verify

Typical commands:

```powershell
dotnet build .\MainSolution.slnx -c dev-n8 -v d
dotnet test .\tests\<PACKAGE_NAME>\<PACKAGE_NAME>.Tests.csproj -c dev-n8 --no-build
```

Use the matching `dev-nf` configuration for `net472` targets.

Run build and test sequentially in this workspace. Parallel `dotnet build` and `dotnet test` can lock package outputs under `obj`.

## What To Report Back

- Test files changed, with one-line reason per file
- Which sys-setting constant was added or reused
- Which sys-setting accessor mocks were configured
- Build/test commands run, or the exact blocker if not run
"""
		),
		Create(
			"sys-setting-tests",
			"coverage-checklist",
			"docs://mcp/references/sys-setting-tests/coverage-checklist",
			"Reference resource for sys-setting-tests: coverage-checklist.",
			false,
			"""
# Coverage Checklist

Use this checklist when reviewing sys-setting unit tests.

1. The class is decorated with `[MockSettings(RequireMock.All)]`.
2. `SetupSysSettings()` calls `base.SetupSysSettings()`.
3. `FakeSysSettings` uses `Constants.SysSettingCodes` for the setting code.
4. `FakeSysSettingsEngine` mocks the same accessor the production code calls.
5. Tests assert the behavior caused by the mocked setting value.
6. Raw sys-setting code strings do not appear in test code.
"""
		),
		Create(
			"sys-setting-tests",
			"mock-settings-attribute",
			"docs://mcp/references/sys-setting-tests/mock-settings-attribute",
			"Reference resource for sys-setting-tests: mock-settings-attribute.",
			false,
			"""
# Mock Settings Attribute

Decorate sys-setting test classes with `[MockSettings(RequireMock.All)]` when the fixture uses fake sys-setting infrastructure.

## Example

```csharp
[MockSettings(RequireMock.All)]
public class MyTests : MyTestsBase {
}
```

## Rules

- Apply the attribute at the test-class level.
- Keep it in place for suites that depend on `FakeSysSettings` or `FakeSysSettingsEngine`.
- Do not rely on partially mocked sys settings when `RequireMock.All` is the workspace rule.
"""
		),
		Create(
			"sys-setting-tests",
			"setup-sys-settings-pattern",
			"docs://mcp/references/sys-setting-tests/setup-sys-settings-pattern",
			"Reference resource for sys-setting-tests: setup-sys-settings-pattern.",
			false,
			"""
# Setup Sys Settings Pattern

Override `SetupSysSettings()` to register fake settings and configure the fake engine for the accessors used by the production code.

## Example

Mock only the accessor or accessors used by the production code under test. The example below shows all three for reference.

```csharp
protected override void SetupSysSettings() {
	base.SetupSysSettings();

	FakeSysSettings mySetting = new FakeSysSettings {
		Code = PkgOneApp.Constants.SysSettingCodes.MySetting,
	};

	FakeSysSettings.Setup(new[] { mySetting });
	FakeSysSettingsEngine engine = Substitute.For<FakeSysSettingsEngine>();
	FakeSysSettingsEngine.Setup(engine);
	const string mockValue = "mock value";

	engine.GetDefaultSettingsValue(Arg.Is(PkgOneApp.Constants.SysSettingCodes.MySetting))
		.Returns(mockValue + "GetDefValue");

	engine.GetSettingsValue(PkgOneApp.Constants.SysSettingCodes.MySetting, Arg.Any<Guid>())
		.Returns(_ => mockValue + "GetSettingsValue");

	engine.TryGetSettingsValue(
			PkgOneApp.Constants.SysSettingCodes.MySetting,
			Arg.Any<Guid>(),
			out Arg.Any<object>())
		.Returns(callInfo => {
			callInfo[2] = mockValue + "TryGetSettingsValue";
			return true;
		});
}
```

## Rules

- Always call `base.SetupSysSettings()` first.
- Register the required `FakeSysSettings` entries before setting up the engine.
- Configure only the accessors the production code actually uses, unless the shared fixture benefits from setting all three.
- Keep the mocked values obvious so tests can assert the behavior they drive.
"""
		),
	];

	private static ComposableAppSkillResourceEntry Create(
		string skill,
		string? reference,
		string uri,
		string description,
		bool isGuide,
		string text,
		string mimeType = "text/markdown") => new(
			Skill: skill,
			Reference: reference,
			Description: description,
			IsGuide: isGuide,
			Article: new TextResourceContents {
				Uri = uri,
				MimeType = mimeType,
				Text = text
			});
}

/// <summary>
/// Metadata and content for one generated composable-app skill MCP resource.
/// </summary>
internal sealed record ComposableAppSkillResourceEntry(
	string Skill,
	string? Reference,
	string Description,
	bool IsGuide,
	TextResourceContents Article
);
