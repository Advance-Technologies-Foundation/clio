using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Clio.Command;
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
	// The surface keeps the core set + the 3 always-on lazy types. ENG-92761 removed DataForgeTool (the
	// only resident class declaring more than one [McpServerTool] — 8 methods) from the resident profile,
	// and SysSettingGetTool / SysSettingsListTool (single-method, no ride-along) also moved to the long
	// tail. With DataForgeTool gone, every remaining resident type declares exactly one tool, so the
	// registered TOOL count (what lands in tools/list) equals the TYPE count. Issue #1183 added the
	// resident merge-creatio-artifact tool to the default surface. The
	// budget is set to 20 to leave a small headroom while still catching a regression that would re-grow
	// the surface toward the ~124-tool full catalog.
	private const int MaxLazyToolCount = 20;

	// tools/list budget ceiling. ADR target is ~5-8k tokens (~32k bytes at ~4 bytes/tok) for the clio
	// surface. We measure the serialized ProtocolTool set (name + description + input schema) as a
	// proxy for the tools/list payload. Story 2 slimmed the core descriptions (and the ubiquitous
	// environment-name/uri/login/password params), dropping the payload from ~37.4k to ~30.1k bytes; the
	// remaining bulk is the input-schema bodies, which Story 2 does not touch. The ceiling grew to 35*1024
	// when origin/master added resident tools (desktop-page, related-page-binding, business-rule CRUD,
	// ...), and to 39*1024 when get-request-info joined the resident core tools. ENG-92761 then dropped
	// DataForgeTool's 8-method schema block, and moving get-sys-setting / list-sys-settings to the long
	// tail dropped 2 more single-method schemas, bringing the measured payload down to 30233 bytes.
	// Issue #1183 adds the semantic merge boundary to the default resident surface. The ratchet remains
	// the deliberate guard against later default-surface growth.
	// Issue #965 re-baselined it to 33*1024: master measured 32741 bytes against the previous 32*1024
	// ceiling, i.e. 27 bytes of headroom, so ANY resident schema change was already blocked. Relaxing the
	// 13 genuinely-optional resident record parameters (list-pages, get-page, get-entity-schema-properties)
	// costs `,"default":null` — 15 bytes each, emitted by the SDK for every defaulted parameter — which
	// outweighs the 163 bytes of `required` entries the same change removes; measured 32773 after. The
	// 15-byte annotation could be stripped, but only by mirroring the SDK's per-method DI-factory
	// registration (WithTools(IEnumerable<Type>) exposes no SchemaCreateOptions); see
	// docs/knowledge/McpServer/relaxing-a-record-parameter-costs-default-null-in-tools-list.md.
	private const int MaxLazyToolsSerializedBytes = 33 * 1024;

	private static Assembly ClioAssembly => typeof(McpFeatureToggleFilter).Assembly;

	/// <summary>
	/// The default-surface predicate for the budget ratchets: every ungated type is enabled,
	/// every <see cref="FeatureToggleAttribute"/>-gated type is disabled — exactly the
	/// fail-closed default of <c>IFeatureToggleService</c> in a fresh install. Gated
	/// experimental CORE tools do not ship in the default <c>tools/list</c>, so they must
	/// not consume the byte budget; removing a <c>[FeatureToggle]</c> lands the tool's cost
	/// on the ratchet at exactly the moment the deliberate budget decision is due.
	/// </summary>
	private static bool DefaultSurfaceEnabled(Type type) =>
		type.GetCustomAttribute<FeatureToggleAttribute>() is null;

	private static Type[] EnabledToolTypes() =>
		McpFeatureToggleFilter.GetEnabledTypes(
			ClioAssembly, typeof(McpServerToolTypeAttribute), _ => true);

	[Test]
	[Category("Unit")]
	[Description("Keeps the Creatio merge tool in the default MCP tools/list surface.")]
	public void SelectToolTypes_ShouldIncludeCreatioArtifactMerge_WhenFeaturesAreDisabledByDefault() {
		// Arrange
		Type[] enabled = McpFeatureToggleFilter.GetEnabledTypes(
			ClioAssembly, typeof(McpServerToolTypeAttribute), DefaultSurfaceEnabled);

		// Act
		Type[] selected = McpFeatureToggleFilter.SelectToolTypes(enabled).ToArray();

		// Assert
		selected.Should().Contain(typeof(CreatioArtifactMergeTool),
			because: "the merge functionality must be discoverable without local feature configuration");
	}

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
		selected.Should().Contain(typeof(PageListTool),
			because: "a core profile tool type stays flat");
		selected.Should().Contain(typeof(CreatioArtifactMergeTool),
			because: "agents need direct discovery of the supported semantic merge boundary");
		selected.Should().NotContain(typeof(DataForgeTool),
			because: "DataForgeTool was moved to the long tail (ENG-92761); it is reachable via clio-run / get-tool-contract, not flat in tools/list");
		selected.Should().NotContain(typeof(SysSettingGetTool),
			because: "get-sys-setting was moved to the long tail; it is reachable via clio-run / get-tool-contract, not flat in tools/list");
		selected.Should().NotContain(typeof(SysSettingsListTool),
			because: "list-sys-settings was moved to the long tail; it is reachable via clio-run / get-tool-contract, not flat in tools/list");
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
	[Description("Registering the DEFAULT surface (feature-gated types off, matching a fresh install) yields a tools/list whose tool count is within the budget cap, guarding against silent core-set bloat.")]
	public void RegisterEnabledPrimitives_ShouldKeepToolCountWithinBudget_WhenCalled() {
		// Arrange
		ServiceCollection services = new();
		IMcpServerBuilder builder = services.AddMcpServer();

		// Act
		McpFeatureToggleFilter.RegisterEnabledPrimitives(
			builder, ClioAssembly, DefaultSurfaceEnabled, JsonSerializerOptions.Default);
		int lazyToolCount = services.Count(descriptor => descriptor.ServiceType == typeof(McpServerTool));

		// Assert
		lazyToolCount.Should().BeGreaterThan(0,
			because: "the surface still registers the core + executor tools");
		lazyToolCount.Should().BeLessThanOrEqualTo(MaxLazyToolCount,
			because: $"the tools/list must stay within the {MaxLazyToolCount}-tool budget so it cannot silently bloat back toward the full catalog");
	}

	[Test]
	[Category("Unit")]
	[Description("The serialized DEFAULT-surface tools/list payload (feature-gated types off, matching a fresh install) stays within the byte budget, ratcheting the context cost of the core set.")]
	public void RegisterEnabledPrimitives_ShouldKeepToolsSerializedSizeWithinBudget_WhenCalled() {
		// Arrange
		ServiceCollection services = new();
		IMcpServerBuilder builder = services.AddMcpServer();
		McpFeatureToggleFilter.RegisterEnabledPrimitives(
			builder, ClioAssembly, DefaultSurfaceEnabled, JsonSerializerOptions.Default);
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
