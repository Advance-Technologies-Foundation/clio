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
/// The gap is narrow and expensive: the tool must thread the LOADED catalog's <c>references.baseInputs</c>
/// into the analysis, because that single field is both half the membership union and the whole enablement
/// gate. A tool that dropped it would either disable the prune everywhere or prune without the inherited
/// surface, stripping <c>visible</c> and <c>layoutConfig</c> from every element — and the pure-analysis
/// fixture cannot see either mistake, since it hand-builds the record the tool is responsible for composing.
/// <para>
/// The other half of this fixture's job is a NEGATIVE one: version resolution must not reach the gate at
/// all. Each platform version's registry describes the mobile runtime that version runs, so the file the
/// chain served is the authority and the stand's version number adds nothing. The cases below drive a
/// degraded probe, a successful probe and a caller-pinned <c>latest</c> through the real tool and assert
/// the prune behaves identically — which is what a reintroduced version condition would break.
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

	/// <summary>What a SUCCESSFUL probe returns: a semver. A probe never returns an alias.</summary>
	private const string ResolvedVersion = "8.3.4";

	[Test]
	[Description("A FAILED version probe still prunes. The resolver returns the literal 'latest' for every failure class and the chain then serves the latest catalog — which is a runtime-derived statement of what mobile supports, whoever asked for it. Withholding the prune here would leave a stand whose cliogate cannot answer shipping every web-only property to the device, for a reason that says nothing about the page.")]
	public async Task GetGuide_WhenVersionProbeDegraded_ShouldStillPrune() {
		// Arrange
		var tool = new StubbedTool(VersionResolutionSource.LatestFallback, "latest");

		// Act
		MobilePageConversionGuideResponse response = await tool.GetMobilePageConversionGuide(Args());

		// Assert
		response.Success.Should().BeTrue(
			because: "a degraded probe is reported through resolvedFrom/versionWarning, not by failing the conversion");
		Values(response, "UsrName").Should().NotContainKey(UndeclaredProperty,
			because: "the served catalog declares what mobile accepts, and the probe's outcome does not change that");
		response.Guide!.PropertyPruneApplied.Should().BeTrue(
			because: "version resolution must not reach the gate — the loaded payload is the only input to it");
	}

	[Test]
	[Description("A version READ FROM THE ENVIRONMENT prunes as well — including an OLD one. 8.3.4 is deliberately below where the removed floor sat: the registry published for that version describes the mobile runtime 8.3.4 runs, so membership in it is a correct support test for that stand, and refusing to prune there would leave exactly the stands the ticket was reported on untouched.")]
	public async Task GetGuide_WhenVersionResolvedFromEnvironment_ShouldPrune() {
		// Arrange
		var tool = new StubbedTool(VersionResolutionSource.Environment, ResolvedVersion);

		// Act
		MobilePageConversionGuideResponse response = await tool.GetMobilePageConversionGuide(Args());

		// Assert
		response.Success.Should().BeTrue(
			because: "the conversion must succeed for the prune assertion below to mean anything");
		Values(response, "UsrName").Should().NotContainKey(UndeclaredProperty,
			because: "the stubbed mobile crt.Input does not declare it, and the catalog served for this version is "
				+ "what decides — not how old the version is");
		response.Guide!.PropertyPruneApplied.Should().BeTrue(
			because: "the gate opened, and the caller-facing flag is what reports that");
	}

	[Test]
	[Description("An explicit `version: latest` prunes too. ResolveVersionAsync short-circuits on an explicit version, so the alias reaches the chain instead of the probed value — and it selects a catalog, which is precisely what the gate reads. Together with the two cases above this pins the negative: all three version-resolution outcomes produce the same prune, so a reintroduced version condition fails here rather than silently on a customer stand.")]
	public async Task GetGuide_WhenCallerPinsTheLatestAlias_ShouldStillPrune() {
		// Arrange — an explicit argument that bypasses the probe entirely.
		var tool = new StubbedTool(VersionResolutionSource.Environment, ResolvedVersion);

		// Act
		MobilePageConversionGuideResponse response = await tool.GetMobilePageConversionGuide(
			new MobilePageConversionGuideArgs(
				SourcePage, TargetSchemaName: null, Version: "latest", EnvironmentName: "unit-test-env"));

		// Assert
		response.Success.Should().BeTrue(
			because: "pinning a catalog is legal and changes only which registry the conversion is measured against");
		Values(response, "UsrName").Should().NotContainKey(UndeclaredProperty,
			because: "the catalog that was served is runtime-derived, and that is the whole question the gate asks");
		response.Guide!.PropertyPruneApplied.Should().BeTrue(
			because: "how the version was chosen is not an input to the gate, and this is where that is pinned");
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
