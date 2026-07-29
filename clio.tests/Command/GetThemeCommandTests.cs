namespace Clio.Tests.Command;

using System;
using Clio.Command;
using Clio.Command.Theming;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public sealed class GetThemeCommandTests : BaseCommandTests<GetThemeOptions> {

	private const string ThemeId = "fb73e945-fe28-4834-838b-3986693c1a3d";
	private const string CssFilePath = "Terrasoft.Configuration/Pkg/Custom/Files/themes/" + ThemeId + "/theme.css?hash=abc123";
	private const string CssContent = ".brand-dark { --crt-test: #112233; }";

	private static readonly string CatalogJson =
		"{\"success\":true,\"values\":[{\"id\":\"" + ThemeId + "\",\"caption\":\"Brand Dark\"," +
		"\"cssClassName\":\"brand-dark\",\"cssFilePath\":\"" + CssFilePath + "\"}]}";

	private IApplicationClient _applicationClient;
	private ILogger _logger;
	private GetThemeCommand _command;

	// An output-file under the OS temp root — one of the two locations OutputPathConfinement allows.
	private string AllowedOutput(string fileName) =>
		FileSystem.Path.Combine(FileSystem.Path.GetTempPath(), "get-theme-out", fileName);

	public override void Setup() {
		// The fixture instance (and its EnvironmentSettings) is shared across tests; the two URL-shape tests
		// mutate IsNetCore, so pin the default here to keep every other test order-independent.
		EnvironmentSettings.IsNetCore = false;
		base.Setup();
		// Resolve the SUT from the container so it is wired exactly as production (real IServiceUrlBuilder,
		// the real ListThemesCommand behind IThemeCatalog); only the I/O boundary (IApplicationClient) is faked.
		_command = Container.GetRequiredService<GetThemeCommand>();
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient<IApplicationClient>(_ => _applicationClient);
		containerBuilder.AddSingleton(_logger);
	}

	private void ArrangeCatalog(string catalogJson = null) {
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(catalogJson ?? CatalogJson);
	}

	private void ArrangeCss(string cssContent = CssContent) {
		_applicationClient.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(cssContent);
	}

	[Test, Category("Unit")]
	[Description("Resolves the theme through the WebApp-prefixed GetAvailableThemes catalog and fetches the CSS from the WebApp-prefixed cssFilePath when the environment runs under .NET Framework.")]
	public void GetTheme_ShouldFormCatalogAndCssRequests_WhenApplicationRunsUnderNetFramework() {
		// Arrange
		EnvironmentSettings.IsNetCore = false;
		ArrangeCatalog();
		ArrangeCss();

		// Act
		_command.TryGetTheme(new GetThemeOptions { Id = ThemeId }, out _);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			"http://localhost/0/ServiceModel/ThemeService.svc/GetAvailableThemes",
			"{}", 100_000, 3, 1);
		_applicationClient.Received(1).ExecuteGetRequest(
			"http://localhost/0/" + CssFilePath, 100_000, 3, 1);
	}

	[Test, Category("Unit")]
	[Description("Resolves the theme through the GetAvailableThemes catalog and fetches the CSS from the cssFilePath without the WebApp prefix when the environment runs under .NET Core.")]
	public void GetTheme_ShouldFormCatalogAndCssRequests_WhenApplicationRunsUnderNetCore() {
		// Arrange
		EnvironmentSettings.IsNetCore = true;
		ArrangeCatalog();
		ArrangeCss();

		// Act
		_command.TryGetTheme(new GetThemeOptions { Id = ThemeId }, out _);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			"http://localhost/ServiceModel/ThemeService.svc/GetAvailableThemes",
			"{}", 100_000, 3, 1);
		_applicationClient.Received(1).ExecuteGetRequest(
			"http://localhost/" + CssFilePath, 100_000, 3, 1);
	}

	[Test, Category("Unit")]
	[Description("Returns the full envelope — id, caption, cssClassName, cssFilePath, cssContent, and length — when the theme exists and its CSS is fetched.")]
	public void TryGetTheme_ShouldReturnMetadataAndContent_WhenThemeExists() {
		// Arrange
		ArrangeCatalog();
		ArrangeCss();

		// Act
		bool result = _command.TryGetTheme(new GetThemeOptions { Id = ThemeId }, out GetThemeResponse response);

		// Assert
		result.Should().BeTrue(because: "an existing theme with readable CSS is a successful read");
		response.Success.Should().BeTrue(because: "the envelope must report the successful read");
		response.Id.Should().Be(ThemeId, because: "the id must come from the catalog entry");
		response.Caption.Should().Be("Brand Dark",
			because: "the caption feeds update-theme verbatim and must be mapped from the catalog");
		response.CssClassName.Should().Be("brand-dark",
			because: "the cssClassName feeds update-theme verbatim and must be mapped from the catalog");
		response.CssFilePath.Should().Be(CssFilePath,
			because: "the served file path is part of the theme's identity on the environment");
		response.CssContent.Should().Be(CssContent,
			because: "the CSS must be returned byte-for-byte as served so the round-trip does not corrupt it");
		response.CssContentLength.Should().Be(CssContent.Length,
			because: "the length is reported alongside the content");
	}

	[Test, Category("Unit")]
	[Description("Matches the theme id case-insensitively, the same convention set-user-theme resolves ids with, so a server that normalizes GUID casing still resolves.")]
	public void TryGetTheme_ShouldMatchIdCaseInsensitively_WhenCatalogCasingDiffers() {
		// Arrange
		ArrangeCatalog();
		ArrangeCss();

		// Act
		bool result = _command.TryGetTheme(new GetThemeOptions { Id = ThemeId.ToUpperInvariant() },
			out GetThemeResponse response);

		// Assert
		result.Should().BeTrue(because: "theme ids are case-insensitive identifiers");
		response.Id.Should().Be(ThemeId,
			because: "the envelope must carry the catalog's canonical id, not the caller's casing");
	}

	[Test, Category("Unit")]
	[Description("Reports a clear not-found error pointing at list-themes when the id is absent from a non-empty catalog.")]
	public void TryGetTheme_ShouldReturnNotFound_WhenIdIsAbsentFromCatalog() {
		// Arrange
		ArrangeCatalog();

		// Act
		bool result = _command.TryGetTheme(new GetThemeOptions { Id = "missing-theme" },
			out GetThemeResponse response);

		// Assert
		result.Should().BeFalse(because: "an unknown theme id must not be reported as a successful read");
		response.Error.Should().Contain("missing-theme").And.Contain("list-themes",
			because: "the not-found error must name the id and point the caller at the catalog");
		_applicationClient.DidNotReceive().ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>());
	}

	[Test, Category("Unit")]
	[Description("Names the possibly-missing CanCustomizeBranding license in the not-found error when the whole catalog is empty, because an unlicensed caller sees an empty list rather than an error.")]
	public void TryGetTheme_ShouldMentionLicense_WhenCatalogIsEmpty() {
		// Arrange
		ArrangeCatalog("{\"success\":true,\"values\":[]}");

		// Act
		bool result = _command.TryGetTheme(new GetThemeOptions { Id = ThemeId }, out GetThemeResponse response);

		// Assert
		result.Should().BeFalse(because: "the theme cannot be read from an empty catalog");
		response.Error.Should().Contain("CanCustomizeBranding",
			because: "an empty catalog is ambiguous between no-themes and no-license, so both causes must be named");
	}

	[Test, Category("Unit")]
	[Description("Rejects an id that violates the shared theme-id rule before any network call.")]
	public void TryGetTheme_ShouldRejectInvalidId_WithoutAnyNetworkCall() {
		// Arrange
		GetThemeOptions options = new() { Id = "not a valid id!" };

		// Act
		bool result = _command.TryGetTheme(options, out GetThemeResponse response);

		// Assert
		result.Should().BeFalse(because: "an id violating the shared rule must be rejected client-side");
		response.Error.Should().Contain("Theme id",
			because: "the shared ThemeParameterValidator message explains the rule");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
		_applicationClient.DidNotReceiveWithAnyArgs().ExecuteGetRequest(default);
	}

	[Test, Category("Unit")]
	[Description("Surfaces the ThemeService error message when the catalog read reports success=false.")]
	public void TryGetTheme_ShouldSurfaceCatalogFailure_WhenGetAvailableThemesFails() {
		// Arrange
		ArrangeCatalog("{\"success\":false,\"errorInfo\":{\"errorCode\":\"SecurityException\",\"message\":\"no permission\"}}");

		// Act
		bool result = _command.TryGetTheme(new GetThemeOptions { Id = ThemeId }, out GetThemeResponse response);

		// Assert
		result.Should().BeFalse(because: "a failed catalog read means the theme cannot be resolved");
		response.Error.Should().Contain("no permission",
			because: "the server-provided errorInfo.message explains why the read failed");
	}

	[Test, Category("Unit")]
	[Description("Fails the read when the CSS fetch returns an HTML document (e.g. a login redirect or an error page) instead of the theme file.")]
	public void TryGetTheme_ShouldFail_WhenCssFetchReturnsHtml() {
		// Arrange
		ArrangeCatalog();
		ArrangeCss("<!DOCTYPE html><html><body>Endpoint not found.</body></html>");

		// Act
		bool result = _command.TryGetTheme(new GetThemeOptions { Id = ThemeId }, out GetThemeResponse response);

		// Assert
		result.Should().BeFalse(because: "an HTML body is never theme CSS and must not be returned as content");
		response.Error.Should().Contain("HTML",
			because: "the error must explain that an HTML page came back instead of the CSS file");
	}

	[Test, Category("Unit")]
	[Description("Returns success with empty content when the theme exists but its CSS file is empty — an empty theme is a theme to fill in, not an error.")]
	public void TryGetTheme_ShouldReturnEmptyContent_WhenCssFileIsEmpty() {
		// Arrange
		ArrangeCatalog();
		ArrangeCss(string.Empty);

		// Act
		bool result = _command.TryGetTheme(new GetThemeOptions { Id = ThemeId }, out GetThemeResponse response);

		// Assert
		result.Should().BeTrue(because: "a theme that exists with empty content is still a successful read");
		response.CssContent.Should().Be(string.Empty,
			because: "the empty content is the theme's actual state");
		response.CssContentLength.Should().Be(0, because: "the reported length must match the empty content");
	}

	[Test, Category("Unit")]
	[Description("Reports an inconsistent-catalog error when more than one catalog entry matches the id, instead of silently reading whichever the server listed first.")]
	public void TryGetTheme_ShouldReportInconsistentCatalog_WhenIdMatchesMoreThanOneTheme() {
		// Arrange — the same id twice (differing only in casing, which the id match ignores)
		ArrangeCatalog("{\"success\":true,\"values\":[" +
			"{\"id\":\"" + ThemeId + "\",\"caption\":\"First\",\"cssClassName\":\"first\",\"cssFilePath\":\"a/theme.css\"}," +
			"{\"id\":\"" + ThemeId.ToUpperInvariant() + "\",\"caption\":\"Second\",\"cssClassName\":\"second\",\"cssFilePath\":\"b/theme.css\"}]}");

		// Act
		bool result = _command.TryGetTheme(new GetThemeOptions { Id = ThemeId }, out GetThemeResponse response);

		// Assert
		result.Should().BeFalse(because: "an ambiguous id must not resolve to an arbitrary catalog entry");
		response.Error.Should().Contain("more than one theme",
			because: "the error must explain that the catalog is inconsistent for this id");
		_applicationClient.DidNotReceive().ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>());
	}

	[Test, Category("Unit")]
	[Description("Detects an HTML error page even when it is served with a leading UTF-8 BOM, which TrimStart() alone would not strip.")]
	public void TryGetTheme_ShouldFail_WhenCssFetchReturnsHtmlWithLeadingBom() {
		// Arrange
		ArrangeCatalog();
		ArrangeCss("\uFEFF<!DOCTYPE html><html><body>Session expired.</body></html>");

		// Act
		bool result = _command.TryGetTheme(new GetThemeOptions { Id = ThemeId }, out GetThemeResponse response);

		// Assert
		result.Should().BeFalse(
			because: "a BOM-prefixed HTML page must not slip past the sniff and be returned as theme CSS");
		response.Error.Should().Contain("HTML",
			because: "the error must explain that an HTML page came back instead of the CSS file");
	}

	[Test, Category("Unit")]
	[Description("Refuses a fetched body larger than the 1 MiB cssContent cap — anything bigger cannot be a clio-managed theme CSS, and returning it would balloon memory or flood an MCP transcript.")]
	public void TryGetTheme_ShouldFail_WhenCssContentExceedsSizeCap() {
		// Arrange
		ArrangeCatalog();
		ArrangeCss(new string('a', (1024 * 1024) + 1));

		// Act
		bool result = _command.TryGetTheme(new GetThemeOptions { Id = ThemeId }, out GetThemeResponse response);

		// Assert
		result.Should().BeFalse(because: "the read side must enforce the same 1 MiB cap the write side enforces");
		response.Error.Should().Contain("1 MiB",
			because: "the error must name the limit so the caller understands why the read was refused");
	}

	[Test, Category("Unit")]
	[Description("Fails the read when the theme's catalog entry carries no cssFilePath, because there is no file to fetch the content from.")]
	public void TryGetTheme_ShouldFail_WhenCatalogEntryHasNoCssFilePath() {
		// Arrange
		ArrangeCatalog("{\"success\":true,\"values\":[{\"id\":\"" + ThemeId + "\",\"caption\":\"Brand Dark\"," +
			"\"cssClassName\":\"brand-dark\"}]}");

		// Act
		bool result = _command.TryGetTheme(new GetThemeOptions { Id = ThemeId }, out GetThemeResponse response);

		// Assert
		result.Should().BeFalse(because: "without a cssFilePath there is nothing to read the content from");
		response.Error.Should().Contain("no CSS file path",
			because: "the error must explain why the content could not be read");
	}

	[Test, Category("Unit")]
	[Description("Writes the CSS to the output file and omits cssContent from the envelope (keeping cssContentLength) when --output-file is set.")]
	public void TryGetTheme_ShouldWriteCssToFile_WhenOutputFileIsProvided() {
		// Arrange
		ArrangeCatalog();
		ArrangeCss();
		string outputFile = AllowedOutput("theme.css");
		GetThemeOptions options = new() { Id = ThemeId, OutputFile = outputFile };

		// Act
		bool result = _command.TryGetTheme(options, out GetThemeResponse response);

		// Assert
		result.Should().BeTrue(because: "an allowed output path must not fail the read");
		response.CssContent.Should().BeNull(
			because: "with an output file the CSS goes to disk, not the envelope");
		response.CssContentLength.Should().Be(CssContent.Length,
			because: "the length is still reported so the caller can sanity-check the write");
		string writtenPath = FileSystem.Path.GetFullPath(outputFile);
		FileSystem.File.ReadAllText(writtenPath).Should().Be(CssContent,
			because: "the CSS is atomically written to the confined, resolved output path");
	}

	[Test, Category("Unit")]
	[Description("Rejects an output-file that escapes the workspace and OS temp dir before any network call, instead of overwriting an arbitrary file.")]
	public void TryGetTheme_ShouldRejectOutputFileOutsideAllowedZones_WithoutAnyNetworkCall() {
		// Arrange — output-file traverses out of temp to a sibling directory
		string tempRoot = FileSystem.Path.GetFullPath(FileSystem.Path.GetTempPath());
		string escape = FileSystem.Path.Combine(tempRoot, "..", "escape", "theme.css");
		GetThemeOptions options = new() { Id = ThemeId, OutputFile = escape };

		// Act
		bool result = _command.TryGetTheme(options, out GetThemeResponse response);

		// Assert
		result.Should().BeFalse(because: "an output-file escaping both allowed zones must not be written");
		response.Error.Should().Contain("output-file",
			because: "the confinement error names the offending argument");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
	}

	[Test, Category("Unit")]
	[Description("Refuses to overwrite an existing output-file, failing before any write so the additive Destructive=false contract stays honest.")]
	public void TryGetTheme_ShouldRefuseToOverwriteExistingOutputFile() {
		// Arrange — an allowed (temp) output-file that already exists on disk
		ArrangeCatalog();
		ArrangeCss();
		string outputFile = AllowedOutput("existing.css");
		FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(outputFile));
		FileSystem.File.WriteAllText(outputFile, "old content");
		GetThemeOptions options = new() { Id = ThemeId, OutputFile = outputFile };

		// Act
		bool result = _command.TryGetTheme(options, out GetThemeResponse response);

		// Assert
		result.Should().BeFalse(because: "an existing output-file must not be silently overwritten");
		FileSystem.File.ReadAllText(outputFile).Should().Be("old content",
			because: "the refused write must leave the existing file intact");
	}

	[Test, Category("Unit")]
	[Description("Maps a transport exception from the CSS fetch to a failure envelope instead of letting it escape the data method.")]
	public void TryGetTheme_ShouldReturnFailureEnvelope_WhenCssFetchThrows() {
		// Arrange
		ArrangeCatalog();
		_applicationClient.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => throw new InvalidOperationException("connection dropped"));

		// Act
		bool result = _command.TryGetTheme(new GetThemeOptions { Id = ThemeId }, out GetThemeResponse response);

		// Assert
		result.Should().BeFalse(because: "a transport failure must surface as a failed read");
		response.Error.Should().Contain("connection dropped",
			because: "the exception message explains what went wrong");
	}

	[Test, Category("Unit")]
	[Description("Prints the JSON envelope and returns exit code 0 on a successful read.")]
	public void Execute_ShouldPrintEnvelopeAndReturnZero_WhenReadSucceeds() {
		// Arrange
		ArrangeCatalog();
		ArrangeCss();

		// Act
		int exitCode = _command.Execute(new GetThemeOptions { Id = ThemeId });

		// Assert
		exitCode.Should().Be(0, because: "a successful read exits with 0");
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("\"success\":true") && message.Contains("cssContent")));
	}

	[Test, Category("Unit")]
	[Description("Prints the failure envelope and returns exit code 1 when the theme is not found.")]
	public void Execute_ShouldPrintFailureEnvelopeAndReturnOne_WhenThemeIsNotFound() {
		// Arrange
		ArrangeCatalog();

		// Act
		int exitCode = _command.Execute(new GetThemeOptions { Id = "missing-theme" });

		// Assert
		exitCode.Should().Be(1, because: "a failed read exits non-zero");
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("\"success\":false") && message.Contains("missing-theme")));
	}
}
