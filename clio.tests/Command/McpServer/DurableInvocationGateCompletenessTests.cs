using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Completeness guard for the durable-invocation authorization gate (ADR D3 / FR-3a). The forgiving
/// handler silently executes a registry tool only when it is annotated <c>ReadOnly=true</c>; every
/// write-capable tool is answered with a <c>confirmation-required</c> retry shape instead.
/// </summary>
/// <remarks>
/// The gate used to key on <c>Destructive</c>, which let every additive-only write run unprompted on this
/// path — <c>odata-create</c> inserted durable rows with no confirmation, while being correctly annotated
/// <c>Destructive=false</c> (the MCP contract reserves that flag for updates which can destroy existing
/// state). Issue #953. The fix moved the gate rather than the annotations, so after the move the
/// silently-executable set IS the <c>ReadOnly=true</c> set. That set therefore has to be pinned against an
/// independent, reviewed baseline — comparing <c>IsReadOnly</c> to <c>ReadOnlyHint</c> would only compare
/// the annotation with itself and could never fail. The baseline below is the moved-over equivalent of the
/// old hand-maintained "reviewed silent write-capable tools" list: a newly added tool annotated
/// <c>ReadOnly=true</c> fails this test until a reviewer confirms it really mutates nothing (PR #984 review).
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class DurableInvocationGateCompletenessTests {

	private static McpToolInvokerRegistry BuildRegistryOverFullCatalog() {
		IServiceProvider provider = Substitute.For<IServiceProvider>();
		IFeatureToggleService featureToggle = Substitute.For<IFeatureToggleService>();
		featureToggle.IsEnabled(Arg.Any<Type>()).Returns(true);
		return new McpToolInvokerRegistry(
			provider,
			typeof(SchemaSyncTool).Assembly,
			featureToggle,
			JsonSerializerOptions.Default);
	}

	/// <summary>
	/// The reviewed set of tools allowed to execute silently on the durable path. Every entry has been
	/// checked to mutate nothing — no remote write, no local file write, no setting toggle. This is the
	/// oracle the gate is measured against; it is deliberately NOT derived from the annotations, because a
	/// set derived from <c>ReadOnlyHint</c> would agree with <c>IsReadOnly</c> by construction.
	/// </summary>
	/// <remarks>
	/// Adding a tool here is a review decision. If this test fails on a new tool, the question to answer is
	/// "does it really mutate nothing?" — if it writes anything, drop its <c>ReadOnly=true</c> annotation
	/// instead of extending this list.
	/// </remarks>
	private static readonly HashSet<string> ReviewedSilentlyExecutableTools = new(StringComparer.Ordinal) {
		"advise-theme-palette",
		"assert-infrastructure",
		"check-auth-code-flow",
		"check-settings-health",
		"check-theming-access",
		"compile-status",
		"dataforge-context",
		"dataforge-find-lookups",
		"dataforge-find-tables",
		"dataforge-get-relations",
		"dataforge-get-table-columns",
		"dataforge-status",
		"describe-business-process",
		"describe-environment",
		"execute-esq",
		"find-app",
		"find-empty-iis-port",
		"find-entity-schema",
		"get-app-info",
		"get-component-info",
		"get-entity-schema-column-properties",
		"get-entity-schema-properties",
		"get-fsm-mode",
		"get-guidance",
		"get-identity-public-jwk",
		"get-identity-service-config",
		"get-mobile-page-conversion-guide",
		"get-page-hierarchy",
		"get-process-signature",
		"get-record-rights",
		"get-related-page-addon",
		"get-request-info",
		"get-schema-name-prefix",
		"get-sys-setting",
		"get-telemetry-consent",
		"get-tool-contract",
		"get-user-culture",
		"list-app-sections",
		"list-apps",
		"list-creatio-builds",
		"list-entity-client-schemas",
		"list-environments",
		"list-packages",
		"list-page-templates",
		"list-pages",
		"list-printables",
		"list-sys-settings",
		"list-themes",
		"list-user-tasks",
		"odata-read",
		"read-entity-business-rules",
		"read-page-business-rules",
		"resolve-oauth-system-user",
		"restart-status",
		"show-passing-infrastructure",
		"validate-page",
		"validate-process-graph",
		"verify-oauth-app",
		"watch-compilation"
	};

	[Test]
	[Category("Unit")]
	[Description("The durable path's silently-executable set equals the reviewed read-only baseline, so a newly added tool annotated ReadOnly=true cannot slip into silent execution unnoticed (issue #953, PR #984 review).")]
	public void SilentlyExecutableTools_ShouldEqualReviewedBaseline_OverFullCatalog() {
		// Arrange
		McpToolInvokerRegistry registry = BuildRegistryOverFullCatalog();

		// Act — classify every tool exactly as the durable handler does: IsReadOnly(name)==true executes
		// silently, everything else is answered with confirmation-required.
		string[] silentlyExecutable = registry.ToolNames
			.Where(registry.IsReadOnly)
			.ToArray();

		// Assert — anti-vacuity first: an empty catalog would satisfy any set comparison.
		registry.ToolNames.Should().NotBeEmpty(
			because: "the assertions below are meaningless over an empty catalog");
		silentlyExecutable.Should().NotBeEmpty(
			because: "the durable path is supposed to run read-only tools without a prompt, so an empty " +
				"silent set would mean the gate had swallowed the whole catalog");
		silentlyExecutable.Should().BeEquivalentTo(ReviewedSilentlyExecutableTools,
			because: "the silently-executable set is measured against a reviewed baseline, not against the " +
				"same ReadOnlyHint annotation the gate reads — a write-capable tool mis-annotated " +
				"ReadOnly=true must break this test instead of executing with no host prompt");
	}

	[Test]
	[Category("Unit")]
	[Description("Every name in the reviewed silently-executable baseline still resolves to a registered tool, so a rename or removal cannot leave the baseline quietly stale (PR #984 review).")]
	public void ReviewedSilentlyExecutableBaseline_ShouldContainOnlyRegisteredTools() {
		// Arrange
		McpToolInvokerRegistry registry = BuildRegistryOverFullCatalog();

		// Act & Assert
		foreach (string name in ReviewedSilentlyExecutableTools) {
			registry.TryGetTool(name, out McpServerTool tool).Should().BeTrue(
				because: $"'{name}' is in the reviewed baseline, so it must still exist in the catalog — " +
					"IsReadOnly fails closed to false for an unknown name, which would silently weaken the " +
					"set comparison after a rename");
			tool.Should().NotBeNull(because: $"a registered tool must resolve to an instance for '{name}'");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("An additive-only remote write such as odata-create is gated by the durable handler even though its destructiveHint is correctly false — the regression #953 reported.")]
	public void AdditiveOnlyRemoteWrites_ShouldNotBeSilentlyExecutable() {
		// Arrange
		McpToolInvokerRegistry registry = BuildRegistryOverFullCatalog();
		string[] additiveOnlyRemoteWrites = [
			"odata-create",
			"create-client-unit-schema",
			"create-schema",
			"create-sql-schema",
			"create-theme",
			"upload-image"
		];

		// Act & Assert
		foreach (string name in additiveOnlyRemoteWrites) {
			registry.TryGetTool(name, out _).Should().BeTrue(
				because: $"'{name}' must still be a registered tool — both IsReadOnly and IsDestructive fail " +
					"closed for an unknown name, so a rename would make the assertions below pass vacuously");
			registry.IsDestructive(name).Should().BeFalse(
				because: $"'{name}' performs only additive updates, so its destructiveHint is spec-conformant " +
					"and this test would be meaningless if the annotation had been flipped instead of the gate");
			registry.IsReadOnly(name).Should().BeFalse(
				because: $"'{name}' writes durable state, so the durable gate must refuse to run it silently");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("The executor wrapper tools themselves are destructive-flagged, so the durable gate can never silently execute a nested executor.")]
	public void ExecutorWrappers_ShouldBeDestructive_SoTheGateNeverSilentlyRunsThem() {
		// Arrange
		McpToolInvokerRegistry registry = BuildRegistryOverFullCatalog();

		// Act & Assert
		registry.IsDestructive("clio-run").Should().BeTrue(
			because: "the generic executor must stay host-gated");
		registry.IsDestructive("clio-run-destructive").Should().BeTrue(
			because: "the destructive executor must stay host-gated");
	}

	[Test]
	[Category("Unit")]
	[Description("set-user-theme is classified destructive, so the durable gate never silently runs it — it overwrites an existing profile value and must be host-confirmed (ENG-93302 PR #895 review #3).")]
	public void SetUserTheme_ShouldBeDestructive_SoTheGateNeverSilentlyRunsIt() {
		// Arrange
		McpToolInvokerRegistry registry = BuildRegistryOverFullCatalog();

		// Act & Assert
		registry.IsDestructive("set-user-theme").Should().BeTrue(
			because: "applying a theme overwrites (or clears) the profile's existing Theme value, so it must be " +
				"host-gated rather than silently executable — consistent with update-theme/delete-theme");
	}

	[Test]
	[Category("Unit")]
	[Description("set-background-image is classified destructive, so the durable gate never silently runs it — it replaces the environment-wide background for all users and must be host-confirmed, which is also why it is absent from the ReviewedSilentlyExecutableTools baseline (PR #984 flipped that baseline to hold ReadOnly=true tools only, so ANY write-capable tool — ReadOnly=false, destructive or not, e.g. get-page / get-schema / get-theme / upload-image — is correctly absent from it and needs no entry).")]
	public void SetBackgroundImage_ShouldBeDestructive_SoTheGateNeverSilentlyRunsIt() {
		// Arrange
		McpToolInvokerRegistry registry = BuildRegistryOverFullCatalog();

		// Act & Assert
		registry.IsDestructive("set-background-image").Should().BeTrue(
			because: "setting the background replaces the currently configured one for every user, so it must be " +
				"host-gated rather than silently executable — consistent with set-user-theme");
	}
}
