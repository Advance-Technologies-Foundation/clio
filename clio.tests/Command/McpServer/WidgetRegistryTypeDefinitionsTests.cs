using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NSubstitute;
using NSubstitute.Core;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit tests for <see cref="WidgetRegistryTypeDefinitions"/> — the registry access the chart and gauge
/// validators share. Two contracts live here: version resolution (normalise, degrade to <c>latest</c>,
/// fail open) and the merge precedence that is the whole reason each widget keeps its OWN merged bag
/// rather than one shared across components.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class WidgetRegistryTypeDefinitionsTests {

	private const string LatestVersion = "latest";
	private const string GaugeComponentType = "crt.GaugeWidget";
	private const string ChartComponentType = "crt.ChartWidget";

	private static JsonElement Marker(string value) {
		using JsonDocument document = JsonDocument.Parse($"{{\"marker\":\"{value}\"}}");
		return document.RootElement.GetProperty("marker").Clone();
	}

	private static ComponentCatalogState CatalogState(
		IReadOnlyDictionary<string, JsonElement>? globalTypes,
		params (string ComponentType, string TypeName, string Marker)[] perComponent) {
		List<ComponentRegistryEntry> entries = perComponent
			.GroupBy(item => item.ComponentType)
			.Select(group => new ComponentRegistryEntry {
				ComponentType = group.Key,
				References = new ComponentReferences {
					TypeDefinitions = group.ToDictionary(item => item.TypeName, item => Marker(item.Marker))
				}
			})
			.ToList();
		return new ComponentCatalogState(
			entries,
			entries.ToDictionary(entry => entry.ComponentType, entry => entry),
			LatestVersion,
			ComponentRegistrySource.FileCache,
			globalTypes is null ? null : new RegistryGlobalReferences { TypeDefinitions = globalTypes });
	}

	private static IComponentInfoCatalog CatalogReturning(ComponentCatalogState state) {
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();
		catalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(state);
		return catalog;
	}

	private static string CapturedLoadVersion(IComponentInfoCatalog catalog) {
		ICall call = catalog.ReceivedCalls()
			.Single(c => c.GetMethodInfo().Name == nameof(IComponentInfoCatalog.LoadAsync));
		return (string)call.GetArguments()[0];
	}

	private static string MarkerOf(IReadOnlyDictionary<string, JsonElement> definitions, string typeName) =>
		definitions[typeName].GetString()!;

	[Test]
	[Description("Loads the 'latest' catalog when no version is supplied, preserving the historical default.")]
	public async Task ResolveAsync_ShouldLoadLatestCatalog_WhenVersionIsNull() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();

		// Act
		await WidgetRegistryTypeDefinitions.ResolveAsync(catalog, GaugeComponentType, null, CancellationToken.None);

		// Assert
		CapturedLoadVersion(catalog).Should().Be(LatestVersion,
			because: "a null version must keep the safe superset behaviour");
	}

	[Test]
	[Description("Treats a whitespace-only version as 'no version supplied'.")]
	public async Task ResolveAsync_ShouldLoadLatestCatalog_WhenVersionIsWhitespace() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();

		// Act
		await WidgetRegistryTypeDefinitions.ResolveAsync(catalog, GaugeComponentType, "   ", CancellationToken.None);

		// Assert
		CapturedLoadVersion(catalog).Should().Be(LatestVersion,
			because: "a blank version carries no scoping information");
	}

	[Test]
	[Description("Scopes the catalog to an exact 3-part semver when one is supplied.")]
	public async Task ResolveAsync_ShouldLoadExactVersion_WhenVersionIsThreePartSemver() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();

		// Act
		await WidgetRegistryTypeDefinitions.ResolveAsync(catalog, GaugeComponentType, "8.3.3", CancellationToken.None);

		// Assert
		CapturedLoadVersion(catalog).Should().Be("8.3.3",
			because: "a well-formed version must scope the catalog to the environment's component set");
	}

	[Test]
	[Description("Drops the build component of a 4-part core version so the CDN filename stays well-formed.")]
	public async Task ResolveAsync_ShouldNormaliseToThreeParts_WhenVersionIsFourPartSemver() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();

		// Act
		await WidgetRegistryTypeDefinitions.ResolveAsync(catalog, GaugeComponentType, "8.3.3.1234", CancellationToken.None);

		// Assert
		CapturedLoadVersion(catalog).Should().Be("8.3.3",
			because: "agents pass a 4-part CoreVersion verbatim and the CDN publishes 3-part filenames");
	}

	[Test]
	[Description("Degrades an unparseable version to 'latest' rather than erroring.")]
	public async Task ResolveAsync_ShouldFallBackToLatest_WhenVersionIsUnparseable() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();

		// Act
		await WidgetRegistryTypeDefinitions.ResolveAsync(catalog, GaugeComponentType, "dev-build", CancellationToken.None);

		// Assert
		CapturedLoadVersion(catalog).Should().Be(LatestVersion,
			because: "a validator must never block a save because a version string was malformed");
	}

	[Test]
	[Description("Returns null when the registry is unavailable, so the caller can fail open.")]
	public async Task ResolveAsync_ShouldReturnNull_WhenRegistryUnavailable() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();
		catalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Throws(new ComponentRegistryUnavailableException("8.3.3", "https://academy.creatio.com/api/mcp/"));

		// Act
		IReadOnlyDictionary<string, JsonElement> result = await WidgetRegistryTypeDefinitions
			.ResolveAsync(catalog, GaugeComponentType, "8.3.3", CancellationToken.None);

		// Assert
		result.Should().BeNull(because: "an offline registry must skip the walk, not block the save");
	}

	[Test]
	[Description("Returns null without touching the catalog when none is supplied.")]
	public async Task ResolveAsync_ShouldReturnNull_WhenCatalogIsNull() {
		// Arrange

		// Act
		IReadOnlyDictionary<string, JsonElement> result = await WidgetRegistryTypeDefinitions
			.ResolveAsync(null, GaugeComponentType, "8.3.3", CancellationToken.None);

		// Assert
		result.Should().BeNull(because: "a missing catalog yields no type definitions");
	}

	[Test]
	[Description("Merges the requested component's own bag and NOT another component's — the constant each bridge passes is what selects it.")]
	public async Task ResolveAsync_ShouldMergeOnlyTheRequestedComponentBag_WhenCatalogCarriesSeveral() {
		// Arrange — two widgets whose per-component type definitions are distinguishable.
		ComponentCatalogState state = CatalogState(
			globalTypes: null,
			(GaugeComponentType, "GaugeWidgetConfig", "gauge"),
			(ChartComponentType, "ChartWidgetConfig", "chart"));
		IComponentInfoCatalog catalog = CatalogReturning(state);

		// Act
		IReadOnlyDictionary<string, JsonElement> result = await WidgetRegistryTypeDefinitions
			.ResolveAsync(catalog, GaugeComponentType, "8.3.3", CancellationToken.None);

		// Assert
		result.Should().ContainKey("GaugeWidgetConfig",
			because: "the gauge bridge must look its own root type up by crt.GaugeWidget");
		result.Should().NotContainKey("ChartWidgetConfig",
			because: "reading another widget's bag would silently disable the required-field walk with every test still green");
	}

	[Test]
	[Description("Folds the document-level type definitions in alongside the per-component ones.")]
	public async Task ResolveAsync_ShouldIncludeGlobalTypeDefinitions_WhenBothBagsExist() {
		// Arrange
		ComponentCatalogState state = CatalogState(
			globalTypes: new Dictionary<string, JsonElement> { ["WidgetDataProvidingConfig"] = Marker("global") },
			(GaugeComponentType, "GaugeWidgetConfig", "gauge"));
		IComponentInfoCatalog catalog = CatalogReturning(state);

		// Act
		IReadOnlyDictionary<string, JsonElement> result = await WidgetRegistryTypeDefinitions
			.ResolveAsync(catalog, GaugeComponentType, "8.3.3", CancellationToken.None);

		// Assert
		result.Should().ContainKey("WidgetDataProvidingConfig",
			because: "the shared data-providing type lives at document level and the walk must reach it");
		result.Should().ContainKey("GaugeWidgetConfig",
			because: "the per-component root type must survive the merge");
	}

	[Test]
	[Description("A per-component definition wins over a document-level one with the same name — the precedence that forces a separate bag per widget.")]
	public async Task ResolveAsync_ShouldPreferPerComponentDefinition_WhenBothBagsDefineTheSameType() {
		// Arrange
		ComponentCatalogState state = CatalogState(
			globalTypes: new Dictionary<string, JsonElement> { ["WidgetDataProvidingConfig"] = Marker("global") },
			(GaugeComponentType, "WidgetDataProvidingConfig", "per-component"));
		IComponentInfoCatalog catalog = CatalogReturning(state);

		// Act
		IReadOnlyDictionary<string, JsonElement> result = await WidgetRegistryTypeDefinitions
			.ResolveAsync(catalog, GaugeComponentType, "8.3.3", CancellationToken.None);

		// Assert
		MarkerOf(result, "WidgetDataProvidingConfig").Should().Be("per-component",
			because: "a component's own definition overrides the shared one — which is exactly why folding two components into one bag could let a type shadow the other's");
	}

	[Test]
	[Description("Returns the global definitions alone when the requested component has no bag of its own.")]
	public async Task ResolveAsync_ShouldReturnGlobalsOnly_WhenComponentIsAbsentFromTheCatalog() {
		// Arrange
		ComponentCatalogState state = CatalogState(
			globalTypes: new Dictionary<string, JsonElement> { ["WidgetDataProvidingConfig"] = Marker("global") },
			(ChartComponentType, "ChartWidgetConfig", "chart"));
		IComponentInfoCatalog catalog = CatalogReturning(state);

		// Act
		IReadOnlyDictionary<string, JsonElement> result = await WidgetRegistryTypeDefinitions
			.ResolveAsync(catalog, GaugeComponentType, "8.3.3", CancellationToken.None);

		// Assert
		result.Should().ContainKey("WidgetDataProvidingConfig",
			because: "the document-level bag is still usable when the component is missing from this catalog version");
		result.Should().NotContainKey("ChartWidgetConfig",
			because: "an unrelated component's types must never leak into the requested bag");
	}

	[Test]
	[Description("Returns null when neither bag exists, which the validators read as 'registry unavailable'.")]
	public async Task ResolveAsync_ShouldReturnNull_WhenCatalogDescribesNoTypes() {
		// Arrange
		ComponentCatalogState state = CatalogState(globalTypes: null);
		IComponentInfoCatalog catalog = CatalogReturning(state);

		// Act
		IReadOnlyDictionary<string, JsonElement> result = await WidgetRegistryTypeDefinitions
			.ResolveAsync(catalog, GaugeComponentType, "8.3.3", CancellationToken.None);

		// Assert
		result.Should().BeNull(
			because: "a catalog that describes no types is indistinguishable from an unavailable one for the walk");
	}
}
