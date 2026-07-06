using System.Linq;
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
/// Unit tests for the version-scoping behaviour of <see cref="ChartWidgetValidation"/>: the requested
/// platform version must be threaded into the catalog load (instead of the hardcoded <c>latest</c>),
/// normalised to the 3-part CDN form, and degrade safely to <c>latest</c> on blank/unparseable input.
/// The fail-open contract (registry unavailable ⇒ no chart error) is also covered.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ChartWidgetValidationTests {

	private const string LatestVersion = "latest";

	// A minimal AMD web body; ValidateAsync reaches the catalog load for any non-empty body
	// because the registry-driven chart walk runs regardless of whether a chart is present.
	private const string WebBody = "define(\"X\", [], function() { return {}; });";

	// Reads back the version string passed to the single IComponentInfoCatalog.LoadAsync call so the
	// test can assert it with a FluentAssertions `because`, instead of a bare NSubstitute Received() check.
	private static string CapturedLoadVersion(IComponentInfoCatalog catalog) {
		ICall call = catalog.ReceivedCalls()
			.Single(c => c.GetMethodInfo().Name == nameof(IComponentInfoCatalog.LoadAsync));
		return (string)call.GetArguments()[0];
	}

	[Test]
	[Description("Loads the 'latest' catalog when no version is supplied (preserves the pre-feature default).")]
	public async Task ResolveTypeDefinitionsAsync_ShouldLoadLatestCatalog_WhenVersionIsNull() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();

		// Act
		await ChartWidgetValidation.ResolveTypeDefinitionsAsync(catalog, null, CancellationToken.None);

		// Assert
		CapturedLoadVersion(catalog).Should().Be(LatestVersion,
			because: "a null version must keep the historical 'latest' behaviour");
	}

	[Test]
	[Description("Loads the 'latest' catalog when the version is blank/whitespace.")]
	public async Task ResolveTypeDefinitionsAsync_ShouldLoadLatestCatalog_WhenVersionIsWhitespace() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();

		// Act
		await ChartWidgetValidation.ResolveTypeDefinitionsAsync(catalog, "   ", CancellationToken.None);

		// Assert
		CapturedLoadVersion(catalog).Should().Be(LatestVersion,
			because: "a whitespace-only version is treated as 'no version supplied' ⇒ 'latest'");
	}

	[Test]
	[Description("Loads the exact requested catalog version when a clean 3-part semver is supplied.")]
	public async Task ResolveTypeDefinitionsAsync_ShouldLoadExactVersion_WhenVersionIsThreePartSemver() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();

		// Act
		await ChartWidgetValidation.ResolveTypeDefinitionsAsync(catalog, "8.3.3", CancellationToken.None);

		// Assert
		CapturedLoadVersion(catalog).Should().Be("8.3.3",
			because: "a well-formed 3-part version must scope the catalog to that exact version");
	}

	[Test]
	[Description("Normalises a 4-part core version to the 3-part Major.Minor.Patch CDN filename form.")]
	public async Task ResolveTypeDefinitionsAsync_ShouldNormaliseToThreeParts_WhenVersionIsFourPartSemver() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();

		// Act — agents may pass a 4-part CoreVersion (e.g. from GetSysInfo) verbatim.
		await ChartWidgetValidation.ResolveTypeDefinitionsAsync(catalog, "8.3.3.1234", CancellationToken.None);

		// Assert
		CapturedLoadVersion(catalog).Should().Be("8.3.3",
			because: "the build component must be dropped so the CDN per-version filename is well-formed");
	}

	[Test]
	[Description("Falls back to 'latest' when the supplied version is not a parseable semver (fail-open, never blocks a save).")]
	public async Task ResolveTypeDefinitionsAsync_ShouldFallBackToLatest_WhenVersionIsUnparseable() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();

		// Act
		await ChartWidgetValidation.ResolveTypeDefinitionsAsync(catalog, "dev-build", CancellationToken.None);

		// Assert
		CapturedLoadVersion(catalog).Should().Be(LatestVersion,
			because: "a malformed version must degrade to the safe 'latest' superset rather than erroring");
	}

	[Test]
	[Description("Returns null (fail-open) when the registry is unavailable, regardless of the requested version.")]
	public async Task ResolveTypeDefinitionsAsync_ShouldReturnNull_WhenRegistryUnavailable() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();
		catalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Throws(new ComponentRegistryUnavailableException("8.3.3", "https://academy.creatio.com/api/mcp/"));

		// Act
		var result = await ChartWidgetValidation.ResolveTypeDefinitionsAsync(catalog, "8.3.3", CancellationToken.None);

		// Assert
		result.Should().BeNull(
			because: "an unavailable registry must not block the save — chart validation is skipped (fail-open)");
	}

	[Test]
	[Description("Returns null without touching the catalog when no catalog is provided.")]
	public async Task ResolveTypeDefinitionsAsync_ShouldReturnNull_WhenCatalogIsNull() {
		// Arrange

		// Act
		var result = await ChartWidgetValidation.ResolveTypeDefinitionsAsync(null, "8.3.3", CancellationToken.None);

		// Assert
		result.Should().BeNull(because: "a missing catalog yields no type definitions (fail-open)");
	}

	[Test]
	[Description("ValidateAsync threads the requested version through to the catalog load for a non-empty body.")]
	public async Task ValidateAsync_ShouldLoadRequestedVersion_WhenBodyHasContent() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();

		// Act
		await ChartWidgetValidation.ValidateAsync(WebBody, catalog, "8.3.3", CancellationToken.None);

		// Assert
		CapturedLoadVersion(catalog).Should().Be("8.3.3",
			because: "ValidateAsync must scope the chart-widget catalog to the requested version");
	}

	[Test]
	[Description("ValidateAsync passes (valid) and never loads the catalog when the body is empty.")]
	public async Task ValidateAsync_ShouldReturnValidWithoutLoading_WhenBodyIsEmpty() {
		// Arrange
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();

		// Act
		SchemaValidationResult result =
			await ChartWidgetValidation.ValidateAsync(string.Empty, catalog, "8.3.3", CancellationToken.None);

		// Assert
		result.IsValid.Should().BeTrue(because: "an empty body has nothing to validate");
		await catalog.DidNotReceive().LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}
}
