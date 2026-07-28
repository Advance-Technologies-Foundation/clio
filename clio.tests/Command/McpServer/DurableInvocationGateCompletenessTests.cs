using System;
using System.Collections.Generic;
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
/// state). Issue #953. The fix moved the gate rather than the annotations, which turns the previous
/// hand-maintained "reviewed silent write-capable tools" baseline into a structural invariant: the
/// silently-executable set must contain NO write-capable tool at all, so a newly added write tool can no
/// longer slip into silent execution and no list has to be curated to notice.
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

	[Test]
	[Category("Unit")]
	[Description("No write-capable tool is silently executable on the durable path: the gate's silently-executable set contains only tools that explicitly declare ReadOnly=true (issue #953).")]
	public void SilentlyExecutableTools_ShouldContainNoWriteCapableTool_OverFullCatalog() {
		// Arrange
		McpToolInvokerRegistry registry = BuildRegistryOverFullCatalog();

		// Act — classify every tool exactly as the durable handler does: IsReadOnly(name)==true executes
		// silently, everything else is gated. The write-capable members of the silent set are the leak.
		List<string> silentWriteCapable = [];
		foreach (string name in registry.ToolNames) {
			if (!registry.IsReadOnly(name)) {
				continue;
			}
			registry.TryGetTool(name, out McpServerTool tool);
			if (tool.ProtocolTool.Annotations?.ReadOnlyHint != true) {
				silentWriteCapable.Add(name);
			}
		}

		// Assert
		silentWriteCapable.Should().BeEmpty(
			because: "the durable gate keys on write-capability, so a tool that can mutate anything must be " +
				"answered with confirmation-required rather than executed without a host prompt");
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
