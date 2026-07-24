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
/// handler silently executes any registry tool whose <c>Destructive</c> annotation is <c>false</c> —
/// reproducing the pre-lazy host behavior (those tools ran without a destructive prompt when they were
/// advertised, too). That makes each tool's own annotation the security boundary, so the WRITE-capable
/// subset of the silently-executable set (<c>ReadOnly=false, Destructive=false</c>) is pinned here as
/// an explicit, reviewed baseline: adding a new write-capable tool (or flipping an annotation) fails
/// this test until the change is consciously re-reviewed against the gate.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class DurableInvocationGateCompletenessTests {

	// The reviewed baseline: every tool the durable handler will execute WITHOUT a confirmation
	// round-trip even though it can write (ReadOnly=false) — because its own annotation says
	// Destructive=false, exactly as the host treated it pre-#743. Reviewed 2026-07-10 (ENG-93370,
	// decision: reproduce the pre-#743 per-tool gate as-is). If this test fails, either the new tool's
	// annotation is wrong (a genuinely destructive tool must declare Destructive=true) or the baseline
	// must be consciously extended in the same PR that adds the tool.
	// GH-953: odata-create was intentionally REMOVED from this baseline — it is now Destructive=true, so
	// the durable gate host-confirms it instead of silently executing (the intended mutation-gating change).
	private static readonly IReadOnlyCollection<string> ReviewedSilentWriteCapableTools = new HashSet<string>(
		StringComparer.OrdinalIgnoreCase) {
		"add-package",
		"add-package-dependency",
		"build-theme",
		"clear-themes-cache",
		"create-business-process",
		"create-client-unit-schema",
		"create-schema",
		"create-sql-schema",
		"create-theme",
		"create-user-task",
		"create-workspace",
		"download-configuration-by-build",
		"download-configuration-by-environment",
		"experimental",
		"finish-hotfix",
		"generate-source-code",
		"get-browser-session",
		"get-identity-assertion",
		"get-page",
		"install-gate",
		"install-toolkit",
		"new-integration-test-project",
		"new-test-project",
		"new-ui-project",
		"reg-web-app",
		"send-telemetry",
		"start-creatio",
		"unlock-for-hotfix",
		"update-toolkit",
		"upload-image"
	};

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

	[Test]
	[Category("Unit")]
	[Description("Every registry tool is classifiable by the durable gate, and the write-capable silently-executable subset exactly matches the reviewed baseline — a new or re-annotated write tool cannot slip into silent execution unreviewed.")]
	public void SilentlyExecutableWriteCapableTools_ShouldMatchReviewedBaseline_OverFullCatalog() {
		// Arrange
		McpToolInvokerRegistry registry = BuildRegistryOverFullCatalog();

		// Act — classify every tool exactly as the durable handler does: IsDestructive(name)==false
		// executes silently; the write-capable subset is the security-relevant one.
		List<string> silentWriteCapable = [];
		foreach (string name in registry.ToolNames) {
			if (registry.IsDestructive(name)) {
				continue;
			}
			registry.TryGetTool(name, out McpServerTool tool);
			bool readOnly = tool.ProtocolTool.Annotations?.ReadOnlyHint ?? false;
			if (!readOnly) {
				silentWriteCapable.Add(name);
			}
		}

		// Assert
		silentWriteCapable.Should().BeEquivalentTo(ReviewedSilentWriteCapableTools,
			because: "the set of write-capable tools the forgiving handler executes without confirmation " +
				"is a security boundary and must only change through a conscious review of this baseline");
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
	[Description("set-background-image is classified destructive, so the durable gate never silently runs it — it replaces the environment-wide background for all users and must be host-confirmed. This is why the tool is intentionally absent from the silently-executable ReviewedSilentWriteCapableTools baseline (that list holds Destructive=false write tools only; upload-image is there because it is additive-only).")]
	public void SetBackgroundImage_ShouldBeDestructive_SoTheGateNeverSilentlyRunsIt() {
		// Arrange
		McpToolInvokerRegistry registry = BuildRegistryOverFullCatalog();

		// Act & Assert
		registry.IsDestructive("set-background-image").Should().BeTrue(
			because: "setting the background replaces the currently configured one for every user, so it must be " +
				"host-gated rather than silently executable — consistent with set-user-theme");
	}
}
