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
/// The registered MCP tool surface is the lazy core profile plus the always-on executor / contract
/// tools — the only surface clio's MCP server exposes (the long-tail flat schemas are reached via
/// <c>clio-run</c> / <c>clio-run-destructive</c> and discovered via <c>get-tool-contract</c>).
/// Selection happens at the single <see cref="McpFeatureToggleFilter"/> seam; these tests pin the
/// type-selection contract and ratchet the tool count / serialized size of the surface.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpProfileGatingTests
{
	// The surface keeps the core set + the 3 always-on lazy types. 18 core types +
	// {ClioRunTool, ClioRunDestructiveTool} (ToolContractGetTool is already a core member) = 20
	// distinct flat tool TYPES. Per-TYPE granularity (ADR resolved decision #5) keeps the WHOLE class,
	// and several core classes declare more than one [McpServerTool] (DataForgeTool declares 8 — only
	// 3 are "core" but all 8 ride along; SysSettings/Application/Entity classes a few each), so the
	// registered TOOL count (what lands in tools/list) is ~27, higher than the 20 TYPE count. The
	// budget asserts on the registered tool count and is set to 30 to leave a small headroom over the
	// current 27 while still catching a regression that would re-grow the surface toward the ~124-tool
	// full catalog.
	private const int MaxLazyToolCount = 30;

	// tools/list budget ceiling. ADR target is ~5-8k tokens (~32k bytes at ~4 bytes/tok) for the clio
	// surface. We measure the serialized ProtocolTool set (name + description + input schema) as a
	// proxy for the tools/list payload. Story 2 slimmed the core descriptions (and the ubiquitous
	// environment-name/uri/login/password params), dropping the payload from ~37.4k to ~30.1k bytes;
	// the remaining bulk is the input-schema bodies, which Story 2 does not touch. The ratchet is
	// tightened to 34k bytes — below the post-slim measurement with headroom for master's growth
	// (validate-page version param + composites data) — to lock in the win and catch any silent re-growth.
	private const int MaxLazyToolsSerializedBytes = 34 * 1024;

	private static Assembly ClioAssembly => typeof(McpFeatureToggleFilter).Assembly;

	private static Type[] EnabledToolTypes() =>
		McpFeatureToggleFilter.GetEnabledTypes(
			ClioAssembly, typeof(McpServerToolTypeAttribute), _ => true);

	[Test]
	[Category("Unit")]
	[Description("Drops long-tail tool types and keeps only the core profile plus the always-on executor/contract types.")]
	public void SelectToolTypes_ShouldReturnCorePlusExecutorsAndDropLongTail_WhenCalled() {
		// Arrange
		Type[] enabled = EnabledToolTypes();

		// Act
		Type[] selected = McpFeatureToggleFilter.SelectToolTypes(enabled).ToArray();

		// Assert
		selected.Should().NotContain(typeof(PageUpdateTool),
			because: "a long-tail tool type must not sit flat in tools/list");
		selected.Should().Contain(typeof(ClioRunTool),
			because: "the safe executor stays flat so the long tail is reachable");
		selected.Should().Contain(typeof(ClioRunDestructiveTool),
			because: "the destructive executor stays flat so destructive long-tail commands are reachable");
		selected.Should().Contain(typeof(ToolContractGetTool),
			because: "the schema-describe tool stays flat so the long tail is discoverable");
		selected.Should().Contain(typeof(DataForgeTool),
			because: "a core profile tool type stays flat");
	}

	[Test]
	[Category("Unit")]
	[Description("Every selected type is either a core profile type or an always-on lazy type, with no other tool types leaking through.")]
	public void SelectToolTypes_ShouldSelectOnlyCoreAndAlwaysOnTypes_WhenCalled() {
		// Arrange
		Type[] enabled = EnabledToolTypes();
		HashSet<Type> allowed = new(McpCoreToolProfile.CoreToolTypes);
		allowed.UnionWith(McpCoreToolProfile.AlwaysOnLazyToolTypes);

		// Act
		Type[] selected = McpFeatureToggleFilter.SelectToolTypes(enabled).ToArray();

		// Assert
		selected.Should().OnlyContain(type => allowed.Contains(type),
			because: "the surface registers exactly the core profile unioned with the always-on executor/contract types");
		selected.Should().HaveCountLessThan(enabled.Length,
			because: "the surface must register strictly fewer tool types than the full discovered catalog");
	}

	[Test]
	[Category("Unit")]
	[Description("Confirms the selected surface is strictly smaller than the full discovered catalog and the full catalog is non-trivial, proving the reduction is real.")]
	public void SelectToolTypes_ShouldReturnStrictSubsetOfFullCatalog_WhenCalled() {
		// Arrange
		Type[] enabled = EnabledToolTypes();

		// Act
		int fullCount = enabled.Length;
		int lazyCount = McpFeatureToggleFilter.SelectToolTypes(enabled).Count();

		// Assert
		fullCount.Should().BeGreaterThan(100,
			because: "clio ships well over a hundred MCP tool types in the full discovered catalog");
		lazyCount.Should().BeLessThan(fullCount,
			because: "the registered surface is a strict subset of the full discovered catalog");
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
			Type[] selected = McpFeatureToggleFilter.SelectToolTypes(enabled).ToArray();

			// Assert
			selected.Should().Contain(typeof(PageListTool),
				because: "the selection is driven only by the core profile, not by CLIO_MCP_TOOL_TYPES");
			selected.Should().NotContain(typeof(PageUpdateTool),
				because: "the removed spike env var must not widen the surface to include long-tail types");
		} finally {
			Environment.SetEnvironmentVariable(spikeEnvVar, original);
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Registering yields a tools/list whose tool count is within the budget cap, guarding against silent core-set bloat.")]
	public void RegisterEnabledPrimitives_ShouldKeepToolCountWithinBudget_WhenCalled() {
		// Arrange
		ServiceCollection services = new();
		IMcpServerBuilder builder = services.AddMcpServer();

		// Act
		McpFeatureToggleFilter.RegisterEnabledPrimitives(
			builder, ClioAssembly, _ => true, JsonSerializerOptions.Default);
		int lazyToolCount = services.Count(descriptor => descriptor.ServiceType == typeof(McpServerTool));

		// Assert
		lazyToolCount.Should().BeGreaterThan(0,
			because: "the surface still registers the core + executor tools");
		lazyToolCount.Should().BeLessThanOrEqualTo(MaxLazyToolCount,
			because: $"the tools/list must stay within the {MaxLazyToolCount}-tool budget so it cannot silently bloat back toward the full catalog");
	}

	[Test]
	[Category("Unit")]
	[Description("The serialized tools/list payload stays within the byte budget, ratcheting the context cost of the core set.")]
	public void RegisterEnabledPrimitives_ShouldKeepToolsSerializedSizeWithinBudget_WhenCalled() {
		// Arrange
		ServiceCollection services = new();
		IMcpServerBuilder builder = services.AddMcpServer();
		McpFeatureToggleFilter.RegisterEnabledPrimitives(
			builder, ClioAssembly, _ => true, JsonSerializerOptions.Default);
		using ServiceProvider provider = services.BuildServiceProvider();

		// Act
		IEnumerable<McpServerTool> tools = provider.GetServices<McpServerTool>();
		object[] protocolTools = tools.Select(tool => (object)tool.ProtocolTool).ToArray();
		string payload = JsonSerializer.Serialize(protocolTools);
		int payloadBytes = System.Text.Encoding.UTF8.GetByteCount(payload);

		// Assert
		protocolTools.Should().NotBeEmpty(
			because: "the surface advertises a non-empty tools/list");
		payloadBytes.Should().BeLessThanOrEqualTo(MaxLazyToolsSerializedBytes,
			because: $"the tools/list payload must stay under {MaxLazyToolsSerializedBytes} bytes (~ADR token budget) to deliver the context-reduction goal");
	}
}
