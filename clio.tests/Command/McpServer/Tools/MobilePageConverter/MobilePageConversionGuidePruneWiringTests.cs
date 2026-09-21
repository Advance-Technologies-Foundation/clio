using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter;

/// <summary>
/// Drives <see cref="MobilePageConversionGuideTool.GetMobilePageConversionGuide"/> end to end to pin the
/// WIRING of the ENG-96589 property prune — the one thing the pure-analysis fixture
/// (<c>WebToMobilePropertyPruneTests</c>) cannot reach, because it hand-builds the
/// <c>MobileRegistryGeneration</c> that the TOOL is responsible for composing.
/// </summary>
/// <remarks>
/// The gap is narrow and expensive, and it has two axes. <c>VersionKnown</c> is derived from
/// <c>versionResolution.Source == VersionResolutionSource.Environment</c>, and the platform-version
/// resolver returns the literal string <c>"latest"</c> for EVERY failure class. Hard-coding that
/// derivation to <see langword="true"/> leaves the whole analysis fixture green while a stand whose
/// version probe failed is pruned against a runtime it does not run — the single failure the version
/// floor exists to prevent. The other axis is the string: <c>ResolveVersionAsync</c> short-circuits on an
/// explicit <c>version</c> argument and reports it as Environment-sourced, so a caller-pinned
/// <c>latest</c> arrives indistinguishable from a real probe unless the gate refuses the alias itself.
/// <para>
/// Most tests here pass NO explicit version, so resolution actually reaches the injected resolver instead
/// of short-circuiting; the alias test is the one that deliberately does the opposite.
/// </para>
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class MobilePageConversionGuidePruneWiringTests {

	private const string SourcePage = "UsrLead_FormPage";
	private const string WebTemplate = "WebTemplate";
	private const string MobileTemplate = "MobileTemplate";

	/// <summary>A property the stubbed mobile <c>crt.Input</c> does not declare — the prune's subject.</summary>
	private const string UndeclaredProperty = "tooltip";

	/// <summary>What a SUCCESSFUL probe returns: a semver, above the floor. A probe never returns an alias.</summary>
	private const string ResolvedVersion = "10.1.0";

	[Test]
	[Description("A version that came from a FAILED probe does not enable the prune, even though the string it carries is 'latest'. The resolver returns that literal for every failure class, so the tool must decide on the resolution SOURCE and never on the string.")]
	public async Task GetGuide_WhenVersionProbeDegraded_ShouldNotPrune() {
		// Arrange
		var tool = new StubbedTool(VersionResolutionSource.LatestFallback, "latest");

		// Act
		MobilePageConversionGuideResponse response = await tool.GetMobilePageConversionGuide(Args());

		// Assert
		response.Success.Should().BeTrue(
			because: "the conversion itself must still succeed on a degraded probe — only the prune is withheld");
		Values(response, "UsrName").Should().ContainKey(UndeclaredProperty,
			because: "a failed version probe says nothing about how new the stand is, so it cannot authorise pruning");
		response.Guide!.PropertyPruneApplied.Should().BeFalse(
			because: "the caller must be able to see that the prune did not run on this stand");
	}

	[Test]
	[Description("The control case: a version READ FROM THE ENVIRONMENT does enable the prune. Without it the degraded-probe test would pass on a tool that never prunes at all — the same class of vacuity it exists to close.")]
	public async Task GetGuide_WhenVersionResolvedFromEnvironment_ShouldPrune() {
		// Arrange
		var tool = new StubbedTool(VersionResolutionSource.Environment, ResolvedVersion);

		// Act
		MobilePageConversionGuideResponse response = await tool.GetMobilePageConversionGuide(Args());

		// Assert
		response.Success.Should().BeTrue(
			because: "the conversion must succeed for the prune assertion below to mean anything");
		Values(response, "UsrName").Should().NotContainKey(UndeclaredProperty,
			because: "the stubbed mobile crt.Input does not declare it and the version is a positive statement about the target");
		response.Guide!.PropertyPruneApplied.Should().BeTrue(
			because: "the gate opened, and the caller-facing flag is what reports that");
	}

	[Test]
	[Description("A degraded probe does not enable the prune even when the version string it carries WOULD pass the floor. Every other refusal here pairs a failed probe with the literal 'latest', which the version half of the gate refuses on its own — so without this case, hard-coding VersionKnown to true at the composition site leaves both fixtures green. The resolver cannot produce this pairing today; the gate is not allowed to depend on that.")]
	public async Task GetGuide_WhenProbeDegradedButVersionWouldPass_ShouldNotPrune() {
		// Arrange
		var tool = new StubbedTool(VersionResolutionSource.LatestFallback, ResolvedVersion);

		// Act
		MobilePageConversionGuideResponse response = await tool.GetMobilePageConversionGuide(Args());

		// Assert
		response.Success.Should().BeTrue(
			because: "a degraded probe withholds the prune, not the conversion");
		Values(response, "UsrName").Should().ContainKey(UndeclaredProperty,
			because: "the version was never read from the stand, so the string alone cannot authorise measuring it");
		response.Guide!.PropertyPruneApplied.Should().BeFalse(
			because: "this is the case that proves the SOURCE half of the gate is load-bearing on its own");
	}

	[Test]
	[Description("An explicit `version: latest` argument does not enable the prune, even on a stand whose probe would have succeeded. This is the wiring half of the alias rule: ResolveVersionAsync SHORT-CIRCUITS on an explicit version and reports it as Environment-sourced, so the alias arrives carrying the same provenance a real probe would have. `environment-name` is required to read the source page and therefore cannot be made mutually exclusive with `version` the way the sibling tools do, which leaves this the only guard.")]
	public async Task GetGuide_WhenCallerPinsTheLatestAlias_ShouldNotPrune() {
		// Arrange — the resolver would answer with a prunable semver; the explicit argument must win anyway.
		var tool = new StubbedTool(VersionResolutionSource.Environment, ResolvedVersion);

		// Act
		MobilePageConversionGuideResponse response = await tool.GetMobilePageConversionGuide(
			new MobilePageConversionGuideArgs(
				SourcePage, TargetSchemaName: null, Version: "latest", EnvironmentName: "unit-test-env"));

		// Assert
		response.Success.Should().BeTrue(
			because: "pinning the alias is legal — it selects a catalog, and only the prune is withheld");
		Values(response, "UsrName").Should().ContainKey(UndeclaredProperty,
			because: "the stand's own version was never read, so nothing authorises measuring it against the newest runtime");
		response.Guide!.PropertyPruneApplied.Should().BeFalse(
			because: "the refusal has to be visible in the response rather than inferred from an unchanged page");
	}

	[Test]
	[Description("The tool threads the loaded catalog's inherited input surface into the analysis. baseInputs is BOTH half the membership union and how the runtime-derived generation is recognised, so a tool that failed to pass it would either disable the prune or prune without it.")]
	public async Task GetGuide_ShouldCarryTheCatalogsInheritedSurfaceIntoTheAnalysis() {
		// Arrange — the stubbed catalog declares `visible` ONLY in baseInputs, on no component.
		var tool = new StubbedTool(VersionResolutionSource.Environment, ResolvedVersion);

		// Act
		MobilePageConversionGuideResponse response = await tool.GetMobilePageConversionGuide(Args());

		// Assert
		JsonObject values = Values(response, "UsrName");
		values.Should().ContainKey("visible",
			because: "`visible` survives ONLY because the tool carried references.baseInputs from the loaded state");
		values.Should().NotContainKey(UndeclaredProperty,
			because: "the prune still ran — otherwise `visible` would survive for the trivial reason that nothing was pruned");
	}

	// ---------------------------------------------------------------- helpers

	/// <summary>No explicit version: this is what makes the tool consult the injected resolver.</summary>
	private static MobilePageConversionGuideArgs Args() =>
		new(SourcePage, TargetSchemaName: null, Version: null, EnvironmentName: "unit-test-env");

	private static JsonObject Values(MobilePageConversionGuideResponse response, string name) {
		response.Guide.Should().NotBeNull(because: "every assertion that calls this reads the guide");
		return response.Guide!.ViewConfigDiff.Single(o => o.Name == name).Values!.AsObject();
	}

	/// <summary>The tool with every page read answered in-memory and one stubbed version resolution.</summary>
	private sealed class StubbedTool : MobilePageConversionGuideTool {

		internal StubbedTool(VersionResolutionSource source, string version)
			: base(Substitute.For<IToolCommandResolver>(), Substitute.For<ILogger>(),
				MobileCatalog(), WebCatalog(), RulesCatalog(),
				VersionResolverFactory(source, version), SettingsRepository()) { }

		internal override PageGetResponse ReadPageUnderTenantLock(PageGetOptions options) =>
			string.Equals(options.SchemaName, SourcePage, StringComparison.OrdinalIgnoreCase)
				? SourcePageResponse()
				: TemplateResponse();
	}

	private static ISettingsRepository SettingsRepository() {
		ISettingsRepository repository = Substitute.For<ISettingsRepository>();
		repository.GetEnvironment(Arg.Any<EnvironmentOptions>()).Returns(new EnvironmentSettings());
		return repository;
	}

	private static IPlatformVersionResolverFactory VersionResolverFactory(
		VersionResolutionSource source, string version) {
		IPlatformVersionResolver resolver = Substitute.For<IPlatformVersionResolver>();
		// The string and the source are set INDEPENDENTLY on purpose: a degraded probe reports "latest" with
		// LatestFallback, a successful one reports a semver with Environment, and the tool must be pinned on
		// both axes — neither the string nor the source decides on its own.
		resolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new PlatformVersionResolution(version, source)));
		IPlatformVersionResolverFactory factory = Substitute.For<IPlatformVersionResolverFactory>();
		factory.Create(Arg.Any<EnvironmentSettings>()).Returns(resolver);
		return factory;
	}

	private static PageGetResponse SourcePageResponse() => new() {
		Success = true,
		Page = new PageMetadataInfo { SchemaName = SourcePage, SchemaType = "web", ParentSchemaName = WebTemplate },
		Bundle = new PageBundleInfo {
			ViewConfig = JsonNode.Parse($$"""
				[ { "name": "MainContainer", "type": "crt.FlexContainer", "items": [
					{ "name": "UsrName", "type": "crt.Input", "label": "Name",
					  "visible": "$IsVisible", "{{UndeclaredProperty}}": "hint" } ] } ]
				""")!.AsArray(),
			ViewModelConfig = new JsonObject(),
			ModelConfig = new JsonObject(),
			Resources = new PageResourceInfo()
		}
	};

	private static PageGetResponse TemplateResponse() => new() {
		Success = true,
		Page = new PageMetadataInfo { SchemaName = MobileTemplate, SchemaType = "web" },
		Bundle = new PageBundleInfo {
			ViewConfig = JsonNode.Parse("""
				[ { "name": "MainContainer", "type": "crt.GridContainer", "items": [] } ]
				""")!.AsArray(),
			ViewModelConfig = new JsonObject(),
			ModelConfig = new JsonObject(),
			Resources = new PageResourceInfo()
		}
	};

	private static IMobileComponentInfoCatalog MobileCatalog() {
		IMobileComponentInfoCatalog catalog = Substitute.For<IMobileComponentInfoCatalog>();
		catalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(MobileState()));
		return catalog;
	}

	private static IComponentInfoCatalog WebCatalog() {
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();
		catalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(State(
				Entries(("crt.FlexContainer", new[] { "items" }), ("crt.Input", new[] { "label" })), null)));
		return catalog;
	}

	private static IWebToMobilePageConversionRulesCatalog RulesCatalog() {
		IWebToMobilePageConversionRulesCatalog catalog = Substitute.For<IWebToMobilePageConversionRulesCatalog>();
		catalog.GetRulesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new WebToMobilePageConversionRules {
				Templates = [new TemplateMappingRule { Web = WebTemplate, Mobile = MobileTemplate }]
			}));
		return catalog;
	}

	/// <summary>
	/// A runtime-derived-SHAPED mobile catalog: components declaring a limited surface of their own, plus
	/// the Flutter inherited surface (`layoutConfig` + `visible`) the generation check requires.
	/// </summary>
	private static ComponentCatalogState MobileState() =>
		State(
			Entries(
				("crt.FlexContainer", new[] { "items" }),
				("crt.GridContainer", new[] { "items", "columns" }),
				("crt.Input", new[] { "control", "label", "placeholder", "readonly" })),
			new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase) {
				["layoutConfig"] = JsonSerializer.SerializeToElement(new { type = "GridLayoutConfig" }),
				["visible"] = JsonSerializer.SerializeToElement(new { type = "boolean" }),
				["name"] = JsonSerializer.SerializeToElement(new { type = "string" }),
				["type"] = JsonSerializer.SerializeToElement(new { type = "string" })
			});

	private static ComponentCatalogState State(
		IReadOnlyList<ComponentRegistryEntry> entries, IReadOnlyDictionary<string, JsonElement> baseInputs) {
		var lookup = new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase);
		foreach (ComponentRegistryEntry entry in entries) {
			lookup[entry.ComponentType] = entry;
		}
		return new ComponentCatalogState(
			entries, lookup, "latest", ComponentRegistrySource.FileCache,
			baseInputs is null ? null : new RegistryGlobalReferences { BaseInputs = baseInputs });
	}

	private static IReadOnlyList<ComponentRegistryEntry> Entries(params (string Type, string[] Inputs)[] types) {
		var entries = new List<ComponentRegistryEntry>();
		foreach ((string type, string[] inputs) in types) {
			var declared = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
			foreach (string input in inputs) {
				declared[input] = JsonSerializer.SerializeToElement(new { type = "unknown" });
			}
			entries.Add(new ComponentRegistryEntry {
				ComponentType = type,
				Container = inputs.Contains("items"),
				Inputs = declared
			});
		}
		return entries;
	}
}
