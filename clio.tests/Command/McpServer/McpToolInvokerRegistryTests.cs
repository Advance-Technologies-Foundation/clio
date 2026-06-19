using System;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// The invoker registry maps every feature-enabled MCP tool NAME (including the long tail hidden from
/// tools/list in lazy mode and the MCP-only tools with no CLI [Verb]) to an invokable
/// <see cref="McpServerTool"/>, so clio-run / clio-run-destructive can reach any tool by name.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpToolInvokerRegistryTests {

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
	[Description("Resolves the MCP-only sync-schemas tool by name, proving a tool with no CLI [Verb] is now reachable for clio-run dispatch.")]
	public void TryGetTool_ShouldResolveMcpOnlyTool_WhenToolHasNoCliVerb() {
		// Arrange
		McpToolInvokerRegistry registry = BuildRegistryOverFullCatalog();

		// Act
		bool found = registry.TryGetTool(SchemaSyncTool.ToolName, out McpServerTool tool);

		// Assert
		found.Should().BeTrue(
			because: "sync-schemas is an MCP-only tool and must be reachable via the invoker registry");
		tool.Should().NotBeNull(because: "a resolved tool must be invokable");
		tool.ProtocolTool.Name.Should().Be(SchemaSyncTool.ToolName,
			because: "the resolved tool is the real sync-schemas tool");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves additional MCP-only long-tail tools (execute-esq, odata-read) by name, confirming the whole long tail is reachable, not just one tool.")]
	public void TryGetTool_ShouldResolveLongTailTools_WhenRequestedByName() {
		// Arrange
		McpToolInvokerRegistry registry = BuildRegistryOverFullCatalog();

		// Act
		bool foundEsq = registry.TryGetTool(ExecuteEsqTool.ToolName, out _);
		bool foundODataRead = registry.TryGetTool(ODataReadTool.ToolName, out _);

		// Assert
		foundEsq.Should().BeTrue(because: "execute-esq is an MCP-only long-tail tool that must be reachable");
		foundODataRead.Should().BeTrue(because: "odata-read is an MCP-only long-tail tool that must be reachable");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports an unknown tool name as a miss without throwing.")]
	public void TryGetTool_ShouldReturnMiss_WhenToolIsUnknown() {
		// Arrange
		McpToolInvokerRegistry registry = BuildRegistryOverFullCatalog();

		// Act
		bool found = registry.TryGetTool("definitely-not-a-tool", out McpServerTool tool);

		// Assert
		found.Should().BeFalse(because: "an unknown tool name must be a miss, not a throw");
		tool.Should().BeNull(because: "no tool resolves for an unknown name");
	}

	[Test]
	[Category("Unit")]
	[Description("Derives destructiveness from the tool's [McpServerTool(Destructive=...)] annotation: sync-schemas is destructive, list-pages is not.")]
	public void IsDestructive_ShouldReflectToolAnnotation_WhenToolIsRegistered() {
		// Arrange
		McpToolInvokerRegistry registry = BuildRegistryOverFullCatalog();

		// Act
		bool syncSchemasDestructive = registry.IsDestructive(SchemaSyncTool.ToolName);
		bool listPagesDestructive = registry.IsDestructive(PageListTool.ToolName);

		// Assert
		syncSchemasDestructive.Should().BeTrue(
			because: "sync-schemas is annotated Destructive = true");
		listPagesDestructive.Should().BeFalse(
			because: "list-pages is a read-only discovery tool and is not destructive");
	}

	[Test]
	[Category("Unit")]
	[Description("Fails closed for an unknown tool so the safe clio-run surface refuses it.")]
	public void IsDestructive_ShouldFailClosed_WhenToolIsUnknown() {
		// Arrange
		McpToolInvokerRegistry registry = BuildRegistryOverFullCatalog();

		// Act
		bool result = registry.IsDestructive("definitely-not-a-tool");

		// Assert
		result.Should().BeTrue(
			because: "an unknown tool must be treated as destructive so the safe surface refuses it");
	}
}
