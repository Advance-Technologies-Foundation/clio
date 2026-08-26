using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NSubstitute;
using NSubstitute.Core;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit tests for <see cref="GaugeWidgetValidation"/>: the requested platform version must be threaded
/// into the catalog load and normalised the same way the chart bridge does it, and — the behaviour that
/// distinguishes this bridge — an unavailable registry must still leave the SCALE rules enforced, because
/// <c>min</c>/<c>max</c>/<c>thresholds</c> are decidable from the page body alone.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class GaugeWidgetValidationTests {

	private const string LatestVersion = "latest";

	// A minimal AMD web body with no widgets; the bridge still reaches the catalog load for any
	// non-empty body because the walk runs regardless of whether a gauge is present.
	private const string EmptyWebBody = "define(\"X\", [], function() { return {}; });";

	// A gauge whose scale is invalid (min >= max) — the case the widget itself never reports.
	private static string BodyWithInvalidScale() =>
		"define(\"X\", [], function() { return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[" +
		"{\"operation\":\"insert\",\"name\":\"GaugeWidget_a\",\"parentName\":\"Main\",\"propertyName\":\"items\"," +
		"\"values\":{\"type\":\"crt.GaugeWidget\",\"config\":{\"min\":10,\"max\":5}}}" +
		"]/**SCHEMA_VIEW_CONFIG_DIFF*/ }; });";

	// Reads back the version string passed to the single IComponentInfoCatalog.LoadAsync call so the
	// test can assert it with a FluentAssertions `because`, instead of a bare NSubstitute Received() check.
	private static string CapturedLoadVersion(IComponentInfoCatalog catalog) {
		ICall call = catalog.ReceivedCalls()
			.Single(c => c.GetMethodInfo().Name == nameof(IComponentInfoCatalog.LoadAsync));
		return (string)call.GetArguments()[0];
	}

	[Test]
	[Description("Loads the 'latest' catalog when no version is supplied.")]
	public async Task ResolveTypeDefinitionsAsync_ShouldLoadLatestCatalog_WhenVersionIsNull() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();

		// Act
		await GaugeWidgetValidation.ResolveTypeDefinitionsAsync(catalog, null, CancellationToken.None);

		// Assert
		CapturedLoadVersion(catalog).Should().Be(LatestVersion,
			because: "a null version must resolve to the safe 'latest' superset, matching the chart bridge");
	}

	[Test]
	[Description("Normalises a 4-part core version to the 3-part Major.Minor.Patch CDN filename form.")]
	public async Task ResolveTypeDefinitionsAsync_ShouldNormaliseToThreeParts_WhenVersionIsFourPartSemver() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();

		// Act — agents may pass a 4-part CoreVersion (e.g. from GetSysInfo) verbatim.
		await GaugeWidgetValidation.ResolveTypeDefinitionsAsync(catalog, "8.3.3.1234", CancellationToken.None);

		// Assert
		CapturedLoadVersion(catalog).Should().Be("8.3.3",
			because: "the build component must be dropped so the CDN per-version filename is well-formed");
	}

	[Test]
	[Description("Falls back to 'latest' when the supplied version is not a parseable semver.")]
	public async Task ResolveTypeDefinitionsAsync_ShouldFallBackToLatest_WhenVersionIsUnparseable() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();

		// Act
		await GaugeWidgetValidation.ResolveTypeDefinitionsAsync(catalog, "dev-build", CancellationToken.None);

		// Assert
		CapturedLoadVersion(catalog).Should().Be(LatestVersion,
			because: "a malformed version must degrade to 'latest' rather than erroring");
	}

	[Test]
	[Description("Returns null (fail-open on the registry layer) when the registry is unavailable.")]
	public async Task ResolveTypeDefinitionsAsync_ShouldReturnNull_WhenRegistryUnavailable() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();
		catalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Throws(new ComponentRegistryUnavailableException("8.3.3", "https://academy.creatio.com/api/mcp/"));

		// Act
		IReadOnlyDictionary<string, JsonElement> result =
			await GaugeWidgetValidation.ResolveTypeDefinitionsAsync(catalog, "8.3.3", CancellationToken.None);

		// Assert
		result.Should().BeNull(
			because: "an unavailable registry must skip the required-field walk instead of blocking the save");
	}

	[Test]
	[Description("Returns null without touching the catalog when no catalog is provided.")]
	public async Task ResolveTypeDefinitionsAsync_ShouldReturnNull_WhenCatalogIsNull() {
		// Arrange

		// Act
		IReadOnlyDictionary<string, JsonElement> result =
			await GaugeWidgetValidation.ResolveTypeDefinitionsAsync(null, "8.3.3", CancellationToken.None);

		// Assert
		result.Should().BeNull(because: "a missing catalog yields no type definitions");
	}

	[Test]
	[Description("ValidateAsync threads the requested version through to the catalog load for a non-empty body.")]
	public async Task ValidateAsync_ShouldLoadRequestedVersion_WhenBodyHasContent() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();

		// Act
		await GaugeWidgetValidation.ValidateAsync(EmptyWebBody, catalog, "8.3.4", CancellationToken.None);

		// Assert
		CapturedLoadVersion(catalog).Should().Be("8.3.4",
			because: "the environment's resolved platform version must scope the gauge catalog load");
	}

	[Test]
	[Description("ValidateAsync passes an empty body without touching the catalog.")]
	public async Task ValidateAsync_ShouldReturnValid_WhenBodyIsEmpty() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();

		// Act
		SchemaValidationResult result =
			await GaugeWidgetValidation.ValidateAsync(string.Empty, catalog, "8.3.4", CancellationToken.None);

		// Assert
		result.IsValid.Should().BeTrue(because: "there is nothing to validate in an empty body");
		catalog.ReceivedCalls().Should().BeEmpty(
			because: "an empty body must not trigger a catalog load at all");
	}

	[Test]
	[Description("The scale rules still fail a save when the registry is unavailable — they need no type definitions.")]
	public async Task ValidateAsync_ShouldStillRejectInvalidScale_WhenRegistryUnavailable() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();
		catalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Throws(new ComponentRegistryUnavailableException("8.3.3", "https://academy.creatio.com/api/mcp/"));

		// Act
		SchemaValidationResult result =
			await GaugeWidgetValidation.ValidateAsync(BodyWithInvalidScale(), catalog, "8.3.3", CancellationToken.None);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "min >= max is decidable from the body alone, so an offline registry must not turn the check off");
		result.Errors.Should().ContainSingle(error => error.Contains("must be less than max"),
			because: "the reported error must name the violated scale rule");
	}

	[Test]
	[Description("The scale rules are enforced with no catalog at all, so a caller without a registry still gets them.")]
	public async Task ValidateAsync_ShouldRejectInvalidScale_WhenCatalogIsNull() {
		// Arrange

		// Act
		SchemaValidationResult result =
			await GaugeWidgetValidation.ValidateAsync(BodyWithInvalidScale(), null, null, CancellationToken.None);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "the scale layer is registry-independent, unlike the chart bridge which no-ops without a catalog");
	}
}
