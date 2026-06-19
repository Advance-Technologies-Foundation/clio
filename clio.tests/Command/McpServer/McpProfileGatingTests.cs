using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Story 1 (opt-in lazy MCP profile): the <c>mcp-lazy-tools</c> feature switches the registered MCP
/// tool surface between the full flat catalog (OFF, default) and the lazy core profile plus the
/// always-on executor / contract tools (ON). Gating happens at the single
/// <see cref="McpFeatureToggleFilter"/> seam; these tests pin the type-selection contract.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpProfileGatingTests
{
	// Lazy mode keeps the core set + the 3 always-on lazy types. 18 core types +
	// {ClioRunTool, ClioRunDestructiveTool} (ToolContractGetTool is already a core member) = 20
	// distinct flat tool TYPES. Per-TYPE granularity (ADR resolved decision #5) keeps the WHOLE class,
	// and several core classes declare more than one [McpServerTool] (DataForgeTool declares 8 — only
	// 3 are "core" but all 8 ride along; SysSettings/Application/Entity classes a few each), so the
	// registered TOOL count (what lands in tools/list) is ~27, higher than the 20 TYPE count. The
	// budget asserts on the registered tool count and is set to 30 to leave a small headroom over the
	// current 27 while still catching a regression that would re-grow the surface toward the ~124-tool
	// full catalog.
	private const int MaxLazyToolCount = 30;

	// tools/list budget ceiling for lazy mode. ADR target is ~5-8k tokens (~32k bytes at ~4 bytes/tok)
	// for the clio surface. We measure the serialized ProtocolTool set (name + description + input
	// schema) of the lazy tools as a proxy for the tools/list payload and ratchet it below 48k bytes
	// to catch silent core-set bloat while leaving headroom for the verbose core descriptions.
	private const int MaxLazyToolsSerializedBytes = 48 * 1024;

	private static Assembly ClioAssembly => typeof(McpFeatureToggleFilter).Assembly;

	private static Type[] EnabledToolTypes() =>
		McpFeatureToggleFilter.GetEnabledTypes(
			ClioAssembly, typeof(McpServerToolTypeAttribute), _ => true);

	[Test]
	[Category("Unit")]
	[Description("Returns the full enabled tool-type set unchanged when the lazy-tools feature is OFF (default), so existing consumers see zero change.")]
	public void SelectToolTypes_ShouldReturnFullSet_WhenLazyToolsDisabled() {
		// Arrange
		Type[] enabled = EnabledToolTypes();

		// Act
		Type[] selected = McpFeatureToggleFilter.SelectToolTypes(enabled, lazyToolsEnabled: false).ToArray();

		// Assert
		selected.Should().BeEquivalentTo(enabled,
			because: "with mcp-lazy-tools OFF the registered tool surface must be the unchanged full flat catalog");
		selected.Should().Contain(typeof(PageUpdateTool),
			because: "a long-tail tool type stays registered flat when lazy mode is off");
	}

	[Test]
	[Category("Unit")]
	[Description("Drops long-tail tool types and keeps only the core profile plus the always-on executor/contract types when the lazy-tools feature is ON.")]
	public void SelectToolTypes_ShouldReturnCorePlusExecutors_WhenLazyToolsEnabled() {
		// Arrange
		Type[] enabled = EnabledToolTypes();

		// Act
		Type[] selected = McpFeatureToggleFilter.SelectToolTypes(enabled, lazyToolsEnabled: true).ToArray();

		// Assert
		selected.Should().NotContain(typeof(PageUpdateTool),
			because: "a long-tail tool type must drop out of tools/list in lazy mode");
		selected.Should().Contain(typeof(ClioRunTool),
			because: "the safe executor stays flat in lazy mode so the long tail is reachable");
		selected.Should().Contain(typeof(ClioRunDestructiveTool),
			because: "the destructive executor stays flat in lazy mode so destructive long-tail commands are reachable");
		selected.Should().Contain(typeof(ToolContractGetTool),
			because: "the lazy-schema describe tool stays flat so the long tail is discoverable");
		selected.Should().Contain(typeof(DataForgeTool),
			because: "a core profile tool type stays flat in lazy mode");
	}

	[Test]
	[Category("Unit")]
	[Description("Every type the lazy mode selects is either a core profile type or an always-on lazy type, with no other tool types leaking through.")]
	public void SelectToolTypes_ShouldSelectOnlyCoreAndAlwaysOnTypes_WhenLazyToolsEnabled() {
		// Arrange
		Type[] enabled = EnabledToolTypes();
		HashSet<Type> allowed = new(McpCoreToolProfile.CoreToolTypes);
		allowed.UnionWith(McpCoreToolProfile.AlwaysOnLazyToolTypes);

		// Act
		Type[] selected = McpFeatureToggleFilter.SelectToolTypes(enabled, lazyToolsEnabled: true).ToArray();

		// Assert
		selected.Should().OnlyContain(type => allowed.Contains(type),
			because: "lazy mode registers exactly the core profile unioned with the always-on executor/contract types");
		selected.Should().HaveCountLessThan(enabled.Length,
			because: "lazy mode must register strictly fewer tool types than the full catalog");
	}

	[Test]
	[Category("Unit")]
	[Description("Confirms the lazy mode selection is strictly smaller than the full catalog and the full catalog is non-trivial, proving the reduction is real.")]
	public void SelectToolTypes_ShouldReduceTheCatalog_WhenComparingModes() {
		// Arrange
		Type[] enabled = EnabledToolTypes();

		// Act
		int fullCount = McpFeatureToggleFilter.SelectToolTypes(enabled, lazyToolsEnabled: false).Count();
		int lazyCount = McpFeatureToggleFilter.SelectToolTypes(enabled, lazyToolsEnabled: true).Count();

		// Assert
		fullCount.Should().BeGreaterThan(100,
			because: "clio ships well over a hundred MCP tool types in the full flat catalog");
		lazyCount.Should().BeLessThan(fullCount,
			because: "lazy mode is a strict subset of the full catalog");
	}

	[Test]
	[Category("Unit")]
	[Description("The throwaway spike env var CLIO_MCP_TOOL_TYPES is no longer consulted by the production selection path.")]
	public void SelectToolTypes_ShouldIgnoreSpikeEnvVar_WhenSet() {
		// Arrange
		const string spikeEnvVar = "CLIO_MCP_TOOL_TYPES";
		string original = Environment.GetEnvironmentVariable(spikeEnvVar);
		Type[] enabled = EnabledToolTypes();
		try {
			Environment.SetEnvironmentVariable(spikeEnvVar, "DataForgeTool");

			// Act
			Type[] selectedOff = McpFeatureToggleFilter.SelectToolTypes(enabled, lazyToolsEnabled: false).ToArray();
			Type[] selectedOn = McpFeatureToggleFilter.SelectToolTypes(enabled, lazyToolsEnabled: true).ToArray();

			// Assert
			selectedOff.Should().BeEquivalentTo(enabled,
				because: "the removed spike env var must not narrow the OFF (full catalog) selection");
			selectedOn.Should().Contain(typeof(PageListTool),
				because: "the selection is driven only by the lazy-tools flag, not by CLIO_MCP_TOOL_TYPES");
		} finally {
			Environment.SetEnvironmentVariable(spikeEnvVar, original);
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Registering with lazy mode ON yields a tools/list whose tool count is within the budget cap, guarding against silent core-set bloat.")]
	public void RegisterEnabledPrimitives_ShouldKeepLazyToolCountWithinBudget_WhenLazyToolsEnabled() {
		// Arrange
		ServiceCollection services = new();
		IMcpServerBuilder builder = services.AddMcpServer();

		// Act
		McpFeatureToggleFilter.RegisterEnabledPrimitives(
			builder, ClioAssembly, _ => true, JsonSerializerOptions.Default, lazyToolsEnabled: true);
		int lazyToolCount = services.Count(descriptor => descriptor.ServiceType == typeof(McpServerTool));

		// Assert
		lazyToolCount.Should().BeGreaterThan(0,
			because: "lazy mode still registers the core + executor tools");
		lazyToolCount.Should().BeLessThanOrEqualTo(MaxLazyToolCount,
			because: $"the lazy tools/list must stay within the {MaxLazyToolCount}-tool budget so it cannot silently bloat back toward the full catalog");
	}

	[Test]
	[Category("Unit")]
	[Description("The serialized tools/list payload for lazy mode stays within the byte budget, ratcheting the context cost of the core set.")]
	public void RegisterEnabledPrimitives_ShouldKeepLazyToolsSerializedSizeWithinBudget_WhenLazyToolsEnabled() {
		// Arrange
		ServiceCollection services = new();
		IMcpServerBuilder builder = services.AddMcpServer();
		McpFeatureToggleFilter.RegisterEnabledPrimitives(
			builder, ClioAssembly, _ => true, JsonSerializerOptions.Default, lazyToolsEnabled: true);
		using ServiceProvider provider = services.BuildServiceProvider();

		// Act
		IEnumerable<McpServerTool> tools = provider.GetServices<McpServerTool>();
		object[] protocolTools = tools.Select(tool => (object)tool.ProtocolTool).ToArray();
		string payload = JsonSerializer.Serialize(protocolTools);
		int payloadBytes = System.Text.Encoding.UTF8.GetByteCount(payload);

		// Assert
		protocolTools.Should().NotBeEmpty(
			because: "lazy mode advertises a non-empty tools/list");
		payloadBytes.Should().BeLessThanOrEqualTo(MaxLazyToolsSerializedBytes,
			because: $"the lazy tools/list payload must stay under {MaxLazyToolsSerializedBytes} bytes (~ADR token budget) to deliver the context-reduction goal");
	}
}
