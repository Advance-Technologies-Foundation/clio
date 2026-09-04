using System;
using System.Reflection;
using Clio.Command.McpServer;
using FluentAssertions;
using ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Name-resolution guard for <see cref="IMcpToolExecutionMetadataReader"/> (ENG-95262 Stage 1, TC-U-104).
/// Routing keyed on the wrong name is the specific miss ADR §9 warns about: the long-running tools are
/// non-resident and agents reach them as <c>clio-run {"command": "compile-creatio", …}</c>, so keying on
/// the OUTER name would send every long-running call to the executor's own row, and keying on an
/// unresolved deprecated alias would miss entirely.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpToolExecutionMetadataReaderTests {

	private const string ClioRunToolName = "clio-run";
	private const string ClioRunDestructiveToolName = "clio-run-destructive";

	private static IMcpToolExecutionMetadataReader BuildReaderOverProductionCatalog() {
		return new McpToolExecutionMetadataReader(
			typeof(McpFeatureToggleFilter).Assembly, new McpToolCompatibilityCatalog());
	}

	private static IMcpToolExecutionMetadataReader BuildReaderOverTestCatalog() {
		return new McpToolExecutionMetadataReader(
			Assembly.GetExecutingAssembly(), new McpToolCompatibilityCatalog());
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-104 (AC-05): both generic executors are unwrapped — the routing key of a clio-run call is the " +
		"INNER command, not the executor name, so a long-running tool reached through the executor is classified as " +
		"itself rather than as clio-run.")]
	public void ResolveRoutingKey_ShouldReturnTheInnerCommand_WhenCalledThroughAnExecutor() {
		// Arrange
		IMcpToolExecutionMetadataReader reader = BuildReaderOverProductionCatalog();

		// Act
		string viaClioRun = reader.ResolveRoutingKey(ClioRunToolName, "compile-creatio");
		string viaClioRunDestructive = reader.ResolveRoutingKey(ClioRunDestructiveToolName, "compile-creatio");

		// Assert
		viaClioRun.Should().Be("compile-creatio",
			because: "routing on the outer executor name would send every long-running call to the same place");
		viaClioRunDestructive.Should().Be("compile-creatio",
			because: "the deprecated executor alias accepts the same calls, so it must unwrap identically — otherwise " +
				"one executor routes and the other does not");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-104 (AC-05): the reader returns the INNER tool's declared metadata for an executor call, not the " +
		"executor's own row — asserted on real declared values, so a reader that silently keyed on the outer name would " +
		"fail rather than return a plausible-looking answer.")]
	public void TryGetMetadata_ShouldReturnTheInnerToolsMetadata_WhenCalledThroughAnExecutor() {
		// Arrange — the synthetic inner tool is declared in this assembly with known metadata.
		IMcpToolExecutionMetadataReader reader = BuildReaderOverTestCatalog();

		// Act
		bool viaExecutor = reader.TryGetMetadata(
			ClioRunToolName, InnerCommandFixtureTool.ToolName, out McpToolExecutionMetadata metadata);
		bool directCall = reader.TryGetMetadata(
			InnerCommandFixtureTool.ToolName, innerCommand: null, out McpToolExecutionMetadata direct);

		// Assert
		viaExecutor.Should().BeTrue(
			because: "the inner command carries execution metadata, so the executor call must resolve it");
		metadata.Should().Be(direct,
			because: "reaching a tool through clio-run must classify it exactly as reaching it directly does");
		directCall.Should().BeTrue(because: "the synthetic inner tool is annotated, so the direct lookup must hit too");
		metadata.Location.Should().Be(McpToolExecutionLocation.Worker,
			because: "the inner tool declares Worker; the executors themselves are in-process, so a reader keyed on the " +
				"outer name could never produce this value");
		metadata.OperationFamily.Should().Be(McpToolOperationFamily.ConfigurationBuild,
			because: "the inner tool's own operation family must survive the unwrap");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-104 (AC-05): a whitespace-padded inner command is trimmed, matching how the executor recovers the " +
		"command from both the direct and the wrapped call shapes before any routing decision is made.")]
	public void ResolveRoutingKey_ShouldTrimTheInnerCommand_WhenItCarriesWhitespace() {
		// Arrange
		IMcpToolExecutionMetadataReader reader = BuildReaderOverProductionCatalog();

		// Act
		string resolved = reader.ResolveRoutingKey($"  {ClioRunToolName} ", "  compile-creatio\t");

		// Assert
		resolved.Should().Be("compile-creatio",
			because: "the routing key must be the canonical tool name, not a whitespace variant that would miss the map");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-104 (AC-05): an executor call with NO inner command resolves to the executor's own name — there is " +
		"nothing else to key on, and the executor itself runs in the host process.")]
	public void ResolveRoutingKey_ShouldReturnTheExecutorName_WhenNoInnerCommandIsSupplied() {
		// Arrange
		IMcpToolExecutionMetadataReader reader = BuildReaderOverProductionCatalog();

		// Act
		string resolved = reader.ResolveRoutingKey(ClioRunToolName, innerCommand: null);

		// Assert
		resolved.Should().Be(ClioRunToolName,
			because: "an argument-less executor call never reaches a target tool, so it must not resolve to some other name");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-104 (AC-05): a deprecated alias is canonicalised through the compatibility catalog before the " +
		"lookup, both on a direct call and on a call routed through an executor — routing on an unresolved alias is the " +
		"miss ADR §9 names.")]
	public void ResolveRoutingKey_ShouldCanonicaliseADeprecatedAlias_OnBothDirectAndExecutorCalls() {
		// Arrange
		IMcpToolExecutionMetadataReader reader = BuildReaderOverProductionCatalog();

		// Act
		string direct = reader.ResolveRoutingKey("restart-by-environmentName", innerCommand: null);
		string viaExecutor = reader.ResolveRoutingKey(ClioRunToolName, "restart-by-environmentName");

		// Assert
		direct.Should().Be("restart-by-environment-name",
			because: "the compatibility catalog declares the camelCase spelling as a deprecated alias, and a router that " +
				"keyed on the alias would find no metadata at all");
		viaExecutor.Should().Be("restart-by-environment-name",
			because: "the unwrap and the alias resolution must compose — an alias supplied as an executor's inner command " +
				"is the combination both dispatch seams can produce");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolution is case-insensitive on the tool name, matching McpToolInvokerRegistry and the compatibility " +
		"catalog, so a differently-cased spelling cannot silently become an unclassified tool.")]
	public void TryGetMetadata_ShouldBeCaseInsensitive_OnTheToolName() {
		// Arrange
		IMcpToolExecutionMetadataReader reader = BuildReaderOverTestCatalog();

		// Act
		bool found = reader.TryGetMetadata(
			InnerCommandFixtureTool.ToolName.ToUpperInvariant(), innerCommand: null,
			out McpToolExecutionMetadata metadata);

		// Assert
		found.Should().BeTrue(
			because: "the registry and the compatibility catalog both compare tool names case-insensitively, so this " +
				"reader must agree with them");
		metadata.Should().NotBeNull(because: "a hit must carry the declared metadata");
	}

	[Test]
	[Category("Unit")]
	[Description("An unknown or blank tool name is a miss, not a throw: the reader answers a routing question and must " +
		"never take down a call it cannot classify.")]
	public void TryGetMetadata_ShouldMissWithoutThrowing_WhenTheToolIsUnknownOrBlank() {
		// Arrange
		IMcpToolExecutionMetadataReader reader = BuildReaderOverProductionCatalog();

		// Act
		bool unknown = reader.TryGetMetadata("definitely-not-a-real-tool-xyz", innerCommand: null,
			out McpToolExecutionMetadata unknownMetadata);
		bool blank = reader.TryGetMetadata("   ", innerCommand: null, out McpToolExecutionMetadata blankMetadata);
		string blankKey = reader.ResolveRoutingKey(null, innerCommand: null);

		// Assert
		unknown.Should().BeFalse(because: "an unknown tool simply has no declared metadata row");
		unknownMetadata.Should().BeNull(because: "a miss must not hand back a fabricated classification");
		blank.Should().BeFalse(because: "a blank name cannot be classified");
		blankMetadata.Should().BeNull(because: "a blank name must not resolve to some other tool's row");
		blankKey.Should().BeNull(because: "there is no routing key for a missing tool name");
	}

	[Test]
	[Category("Unit")]
	[Description("Constructor arguments are validated so a mis-wired reader fails at construction rather than answering " +
		"routing questions from an empty map.")]
	public void Constructor_ShouldThrow_WhenAnArgumentIsNull() {
		// Arrange
		Assembly assembly = Assembly.GetExecutingAssembly();

		// Act
		Action withoutCatalog = () => _ = new McpToolExecutionMetadataReader(assembly, compatibilityCatalog: null);
		Action withoutAssembly = () => _ = new McpToolExecutionMetadataReader(
			assembly: null, new McpToolCompatibilityCatalog());

		// Assert
		withoutCatalog.Should().Throw<ArgumentNullException>(
			because: "without the compatibility catalog a deprecated alias would silently be treated as an unknown tool");
		withoutAssembly.Should().Throw<ArgumentNullException>(
			because: "without an assembly the reader would classify nothing while reporting success");
	}

	// A synthetic tool declared in the TEST assembly, used as the INNER command of an executor call. Its
	// metadata values are deliberately ones the executors themselves could never carry (Worker +
	// ConfigurationBuild), so a reader that keyed on the outer name cannot accidentally pass.
	[McpServerToolType]
	private static class InnerCommandFixtureTool {
		internal const string ToolName = "zz-reader-inner-command-tool";

		[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true)]
		[System.ComponentModel.Description("Synthetic inner command reached through clio-run.")]
		[McpToolExecution(
			Location = McpToolExecutionLocation.Worker,
			Lifetime = McpToolExecutionLifetime.Sticky,
			OperationFamily = McpToolOperationFamily.ConfigurationBuild,
			BudgetPolicy = McpToolBudgetPolicy.ParentKillExtended,
			RequiresClientRequests = McpToolClientRequests.Progress,
			SharedFileResource = McpToolSharedFileResource.ConfigurationBuild)]
		public static string Run() => "inner";
	}
}
