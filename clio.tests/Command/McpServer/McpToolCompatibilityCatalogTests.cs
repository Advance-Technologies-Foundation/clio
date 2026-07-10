using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// The compatibility catalog is the single source of truth mapping legacy/renamed MCP tool names to
/// their canonical name. It must resolve declared aliases case-insensitively, miss cleanly on unknown
/// names, fail fast on any collision at construction (so a malformed catalog cannot boot under
/// ValidateOnBuild), and only ever point at real registered tools.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpToolCompatibilityCatalogTests {

	private static McpToolCompatibilityEntry Entry(
		string canonical,
		params string[] aliases) =>
		new(
			CanonicalName: canonical,
			Aliases: aliases,
			Kind: McpToolCompatibilityKind.DeprecatedAlias,
			DeprecatedSince: null,
			Replacement: null,
			Owner: McpToolSurfaceOwner.Clio);

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
	[Description("Resolves the deprecated restart alias to its canonical tool name, so guidance using the old camelCase name keeps working.")]
	public void TryResolveAlias_ShouldReturnCanonical_WhenNameIsDeclaredAlias() {
		// Arrange
		IMcpToolCompatibilityCatalog catalog = new McpToolCompatibilityCatalog();

		// Act
		bool resolved = catalog.TryResolveAlias("restart-by-environmentName", out string canonical, out McpToolCompatibilityEntry entry);

		// Assert
		resolved.Should().BeTrue(because: "restart-by-environmentName is a declared deprecated alias");
		canonical.Should().Be("restart-by-environment-name",
			because: "the alias must resolve to the current canonical tool name");
		entry.Kind.Should().Be(McpToolCompatibilityKind.DeprecatedAlias,
			because: "the restart alias is a deprecated-alias mapping");
	}

	[Test]
	[Category("Unit")]
	[Description("Alias resolution is case-insensitive, matching the invoker registry's OrdinalIgnoreCase lookup.")]
	public void TryResolveAlias_ShouldBeCaseInsensitive_WhenAliasCasingDiffers() {
		// Arrange
		IMcpToolCompatibilityCatalog catalog = new McpToolCompatibilityCatalog();

		// Act
		bool resolved = catalog.TryResolveAlias("RESTART-BY-ENVIRONMENTNAME", out string canonical, out _);

		// Assert
		resolved.Should().BeTrue(because: "alias lookup must be case-insensitive");
		canonical.Should().Be("restart-by-environment-name",
			because: "the canonical name is emitted in its declared casing regardless of the request casing");
	}

	[Test]
	[Category("Unit")]
	[Description("An unknown name is a clean miss, not a throw, so callers can fall through to direct registry lookup.")]
	public void TryResolveAlias_ShouldReturnMiss_WhenNameIsUnknown() {
		// Arrange
		IMcpToolCompatibilityCatalog catalog = new McpToolCompatibilityCatalog();

		// Act
		bool resolved = catalog.TryResolveAlias("definitely-not-an-alias", out string canonical, out McpToolCompatibilityEntry entry);

		// Assert
		resolved.Should().BeFalse(because: "an unknown name is not a declared alias");
		canonical.Should().BeNull(because: "no canonical resolves for an unknown name");
		entry.Should().BeNull(because: "no entry resolves for an unknown name");
	}

	[Test]
	[Category("Unit")]
	[Description("A canonical name is not itself an alias, so it misses and the caller uses it directly against the registry.")]
	public void TryResolveAlias_ShouldReturnMiss_WhenNameIsCanonical() {
		// Arrange
		IMcpToolCompatibilityCatalog catalog = new McpToolCompatibilityCatalog();

		// Act
		bool resolved = catalog.TryResolveAlias("restart-by-environment-name", out _, out _);

		// Assert
		resolved.Should().BeFalse(
			because: "a canonical name is not a declared alias; only legacy names resolve through the alias index");
	}

	[Test]
	[Category("Unit")]
	[Description("A duplicate canonical name in the catalog throws at construction (fail-fast under ValidateOnBuild).")]
	public void Constructor_ShouldThrow_WhenCanonicalIsDuplicated() {
		// Arrange
		List<McpToolCompatibilityEntry> entries = [
			Entry("tool-a", "alias-a"),
			Entry("tool-a", "alias-b")
		];

		// Act
		Action act = () => _ = new McpToolCompatibilityCatalog(entries);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a duplicate canonical name makes the catalog ambiguous and must fail fast at construction");
	}

	[Test]
	[Category("Unit")]
	[Description("An alias that collides with a canonical name throws at construction.")]
	public void Constructor_ShouldThrow_WhenAliasCollidesWithCanonical() {
		// Arrange
		List<McpToolCompatibilityEntry> entries = [
			Entry("tool-a", "alias-a"),
			Entry("tool-b", "tool-a")
		];

		// Act
		Action act = () => _ = new McpToolCompatibilityCatalog(entries);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a name cannot be both a canonical tool and an alias — resolution would be ambiguous");
	}

	[Test]
	[Category("Unit")]
	[Description("The same alias declared on two entries throws at construction.")]
	public void Constructor_ShouldThrow_WhenAliasIsDuplicatedAcrossEntries() {
		// Arrange
		List<McpToolCompatibilityEntry> entries = [
			Entry("tool-a", "shared-alias"),
			Entry("tool-b", "shared-alias")
		];

		// Act
		Action act = () => _ = new McpToolCompatibilityCatalog(entries);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "an alias may resolve to exactly one canonical tool");
	}

	[Test]
	[Category("Unit")]
	[Description("An entry whose canonical name targets a generic executor throws at construction — an alias resolving to clio-run would bypass the executor's self-dispatch guard (recursion DoS).")]
	public void Constructor_ShouldThrow_WhenCanonicalTargetsExecutor() {
		// Arrange
		List<McpToolCompatibilityEntry> entries = [
			Entry("clio-run", "legacy-run")
		];

		// Act
		Action act = () => _ = new McpToolCompatibilityCatalog(entries);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "the executors must never be a compatibility target — a resolved alias would re-enter dispatch recursively");
	}

	[Test]
	[Category("Unit")]
	[Description("An entry declaring an executor name as an alias throws at construction.")]
	public void Constructor_ShouldThrow_WhenAliasIsExecutorName() {
		// Arrange
		List<McpToolCompatibilityEntry> entries = [
			Entry("tool-a", "clio-run-destructive")
		];

		// Act
		Action act = () => _ = new McpToolCompatibilityCatalog(entries);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "an executor name declared as an alias would shadow the executor on the resolution path");
	}

	[Test]
	[Category("Unit")]
	[Description("The production catalog builds without throwing, proving the shipped seed is internally consistent.")]
	public void Constructor_ShouldBuildProductionSeed_WithoutThrowing() {
		// Arrange & Act
		Action act = () => _ = new McpToolCompatibilityCatalog();

		// Assert
		act.Should().NotThrow(because: "the shipped compatibility seed must be internally consistent");
	}

	[Test]
	[Category("Unit")]
	[Description("Every canonical name in the shipped catalog is a real registered MCP tool, so an alias never points at a non-existent tool.")]
	public void ProductionCatalog_ShouldOnlyReferenceRealTools_ForEveryCanonical() {
		// Arrange
		IMcpToolCompatibilityCatalog catalog = new McpToolCompatibilityCatalog();
		McpToolInvokerRegistry registry = BuildRegistryOverFullCatalog();
		HashSet<string> toolNames = registry.ToolNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

		// Act
		List<string> danglingCanonicals = catalog.Entries
			.Where(entry => entry.Kind == McpToolCompatibilityKind.DeprecatedAlias)
			.Select(entry => entry.CanonicalName)
			.Where(canonical => !toolNames.Contains(canonical))
			.ToList();

		// Assert
		danglingCanonicals.Should().BeEmpty(
			because: "a deprecated-alias entry must point at a real registered tool, otherwise the alias resolves to nothing");
	}
}
