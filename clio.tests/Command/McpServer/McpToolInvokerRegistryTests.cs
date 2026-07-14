using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Protocol;
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
	[Description("The registry's tool names equal the SDK's own WithTools discovery over the full catalog, so no tool is reachable via tools/call yet missing from clio-run dispatch (guards the BindingFlags parity, incl. inherited [McpServerTool] methods the SDK keeps and DeclaredOnly would drop).")]
	public void ToolNames_ShouldEqualSdkDiscoveredNames_OverFullCatalog() {
		// Arrange
		McpToolInvokerRegistry registry = BuildRegistryOverFullCatalog();
		// SDK truth, verified against ModelContextProtocol 1.4.0 McpServerBuilderExtensions.WithTools:
		// toolType.GetMethods(Instance | Static | Public | NonPublic) — NO DeclaredOnly. Encoded here
		// independently of the registry's own constant so a drift in the registry fails this test.
		HashSet<string> expected = DiscoverSdkToolNamesOverFullCatalog();

		// Act
		HashSet<string> actual = registry.ToolNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

		// Assert
		expected.Should().NotBeEmpty(
			because: "the production catalog contains MCP tools, so the SDK-discovered set must be non-empty");
		actual.Should().BeEquivalentTo(expected,
			because: "every SDK-registered tool must be reachable via clio-run dispatch, and vice versa — no parity gap");
	}

	// Mirrors the SDK's WithTools(IEnumerable<Type>) method enumeration over the same feature-enabled
	// [McpServerToolType] set the registry scans, then derives each name through the SDK's own
	// McpServerTool.Create — so the ONLY variable this comparison isolates is the BindingFlags set.
	private static HashSet<string> DiscoverSdkToolNamesOverFullCatalog() {
		const BindingFlags sdkFlags = BindingFlags.Instance | BindingFlags.Static |
			BindingFlags.Public | BindingFlags.NonPublic;
		McpServerToolCreateOptions createOptions = new() {
			Services = Substitute.For<IServiceProvider>(),
			SerializerOptions = JsonSerializerOptions.Default
		};
		Type[] enabledToolTypes = McpFeatureToggleFilter.GetEnabledTypes(
			typeof(SchemaSyncTool).Assembly, typeof(McpServerToolTypeAttribute), _ => true);

		HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
		foreach (Type toolType in enabledToolTypes) {
			foreach (MethodInfo method in toolType.GetMethods(sdkFlags)
				.Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)) {
				// createTargetFunc is never invoked for name resolution; instance methods reject target:null.
				McpServerTool tool = method.IsStatic
					? McpServerTool.Create(method, target: null, createOptions)
					: McpServerTool.Create(method, (Func<RequestContext<CallToolRequestParams>, object>)(_ => null!), createOptions);
				string name = tool.ProtocolTool.Name;
				if (!string.IsNullOrWhiteSpace(name)) {
					names.Add(name);
				}
			}
		}
		return names;
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

	// Two deliberately colliding tool types for the duplicate-name guard test below. They live in the
	// TEST assembly, so the production catalog stays collision-free while the guard is still provable.
	[McpServerToolType]
	private static class DuplicateNameToolTypeA {
		[McpServerTool(Name = "zz-duplicate-name-tool", Destructive = false)]
		[System.ComponentModel.Description("First declaration of a deliberately duplicated tool name.")]
		public static string RunA() => "a";
	}

	[McpServerToolType]
	private static class DuplicateNameToolTypeB {
		[McpServerTool(Name = "zz-duplicate-name-tool", Destructive = false)]
		[System.ComponentModel.Description("Second declaration of a deliberately duplicated tool name.")]
		public static string RunB() => "b";
	}

	[Test]
	[Category("Unit")]
	[Description("Throws on a duplicate MCP tool NAME instead of silently keeping the first declaration, so a rename can never ship as a second method (TC-U-05).")]
	public void Constructor_ShouldThrow_WhenTwoToolMethodsDeclareTheSameName() {
		// Arrange — scanning the TEST assembly picks up the two colliding fixture types above.
		IServiceProvider provider = Substitute.For<IServiceProvider>();
		IFeatureToggleService featureToggle = Substitute.For<IFeatureToggleService>();
		featureToggle.IsEnabled(Arg.Any<Type>()).Returns(true);

		// Act
		Action act = () => _ = new McpToolInvokerRegistry(
			provider,
			typeof(McpToolInvokerRegistryTests).Assembly,
			featureToggle,
			JsonSerializerOptions.Default);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a duplicate tool name makes dispatch ambiguous and must fail fast at registry construction")
			.WithMessage("*Duplicate MCP tool name*");
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
