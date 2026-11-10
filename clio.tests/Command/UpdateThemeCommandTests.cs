namespace Clio.Tests.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.Branding;
using Clio.Command.Theming;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public sealed class UpdateThemeCommandTests : BaseCommandTests<UpdateThemeOptions>
{
	private const string ThemeId = "d53a816b-65ed-4b5b-ad3b-f739280add45";

	private const string ImageId = "6d2e4b1f-93a8-4c57-b0e1-2f7a9c3d5e81";

	private static readonly BackgroundGalleryImage GalleryImage = new(Guid.Parse(ImageId),
		Guid.Parse("8b1c2d3e-4f50-4a6b-9c7d-e8f9a0b1c2d3"), BrandingBackgroundService.ShellBackgroundTagId);

	private IApplicationClient _applicationClient;
	private IThemeBackgroundWriter _backgroundWriter;
	private IThemeCatalog _themeCatalog;
	private ILogger _logger;
	private UpdateThemeCommand _command;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<UpdateThemeCommand>();
		_logger = Substitute.For<ILogger>();
		_command.Logger = _logger;
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_backgroundWriter = Substitute.For<IThemeBackgroundWriter>();
		ArrangeGallery(added: true);
		_backgroundWriter.TurnOffPanelIconBackground(default, default).ReturnsForAnyArgs(PanelIconBackgroundOutcome.None);
		_backgroundWriter.BindImageIntoThemePackage(default, default, default)
			.ReturnsForAnyArgs(ThemeImageBindingOutcome.None);
		_themeCatalog = Substitute.For<IThemeCatalog>();
		containerBuilder.AddTransient<IApplicationClient>(_ => _applicationClient);
		containerBuilder.AddTransient(_ => _backgroundWriter);
		containerBuilder.AddTransient(_ => _themeCatalog);
	}

	private void ArrangeGallery(bool added) {
		_backgroundWriter.TryAddImageToBackgroundGallery(Arg.Any<ThemeBackground>(), out Arg.Any<BackgroundGalleryImage>(),
				out Arg.Any<string>())
			.Returns(call => {
				call[1] = added ? GalleryImage : null;
				return added;
			});
	}

	private static UpdateThemeOptions ValidOptions() => new() {
		Id = "d53a816b-65ed-4b5b-ad3b-f739280add45",
		Caption = "Ocean",
		CssClassName = "ocean-theme",
		CssContent = ".ocean-theme{--crt-x:2}"
	};

	private void StubUpdateThemeSuccess() {
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("UpdateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true}");
	}

	[Test, Category("Unit")]
	[Description("Posts UpdateTheme to the WebApp-prefixed ThemeService path when the environment runs under .NET Framework.")]
	public void UpdateTheme_ShouldFormCorrectApplicationRequest_WhenApplicationRunsUnderNetFramework() {
		// Arrange
		EnvironmentSettings.IsNetCore = false;
		StubUpdateThemeSuccess();

		// Act
		_command.Execute(ValidOptions());

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(u => u == "http://localhost/0/ServiceModel/ThemeService.svc/UpdateTheme"),
			Arg.Any<string>(), 100_000, 3, 1);
	}

	[Test, Category("Unit")]
	[Description("Posts UpdateTheme without the WebApp prefix when the environment runs under .NET Core.")]
	public void UpdateTheme_ShouldFormCorrectApplicationRequest_WhenApplicationRunsUnderNetCore() {
		// Arrange
		EnvironmentSettings.IsNetCore = true;
		StubUpdateThemeSuccess();

		// Act
		_command.Execute(ValidOptions());

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(u => u == "http://localhost/ServiceModel/ThemeService.svc/UpdateTheme"),
			Arg.Any<string>(), 100_000, 3, 1);
	}

	[Test, Category("Unit")]
	[Description("Sends a camelCase full-overwrite body and omits the packageUId key (UpdateTheme cannot re-home a theme).")]
	public void UpdateTheme_ShouldSendBodyWithoutPackageUId_WhenExecuted() {
		// Arrange
		string capturedBody = null;
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("UpdateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => { capturedBody = ci.ArgAt<string>(1); return "{\"success\":true}"; });

		// Act
		int exitCode = _command.Execute(ValidOptions());

		// Assert
		exitCode.Should().Be(0, because: "a success=true response means the theme was overwritten");
		capturedBody.Should().Contain("\"id\":\"d53a816b-65ed-4b5b-ad3b-f739280add45\"", because: "the target id must be sent");
		capturedBody.Should().Contain("\"cssClassName\":\"ocean-theme\"", because: "the css class name must be sent in camelCase");
		capturedBody.Should().NotContain("packageUId", because: "UpdateTheme has no package parameter — a theme cannot be re-homed");
	}

	[Test, Category("Unit")]
	[Description("Fails fast without any HTTP call when css-class-name violates the format rule.")]
	public void UpdateTheme_ShouldFailFastWithoutHttp_WhenCssClassNameInvalid() {
		// Arrange
		UpdateThemeOptions options = ValidOptions();
		options.CssClassName = "1-bad";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an invalid css-class-name is rejected before any service call");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("css-class-name")));
	}

	[Test, Category("Unit")]
	[Description("Returns failure and surfaces the ThemeService error message when the response reports success=false.")]
	public void UpdateTheme_ShouldReturnFailureAndLogErrorInfoMessage_WhenResponseReportsFailure() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("UpdateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":false,\"errorInfo\":{\"errorCode\":\"InvalidOperationException\",\"message\":\"theme not found\"}}");

		// Act
		int exitCode = _command.Execute(ValidOptions());

		// Assert
		exitCode.Should().Be(1, because: "an explicit success=false must surface as a command failure");
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("theme not found")));
	}

	[Test, Category("Unit")]
	[Description("Fails the command when the response body is a non-empty non-JSON payload (e.g. an auth redirect): ThemeService always answers with JSON, so a non-JSON body means the update never reached the service.")]
	public void UpdateTheme_ShouldReturnFailureAndLogError_WhenResponseBodyIsNotParseableJson() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("UpdateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("OK");

		// Act
		int exitCode = _command.Execute(ValidOptions());

		// Assert
		exitCode.Should().Be(1, because: "a non-JSON body signals the update did not reach ThemeService and must surface as a failure");
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("Unexpected response from server")));
	}

	[Test, Category("Unit")]
	[Description("Returns success when the response body is empty — an empty body is the ThemeService contract default for a successful update.")]
	public void UpdateTheme_ShouldReturnSuccess_WhenResponseBodyIsEmpty() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("UpdateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);

		// Act
		int exitCode = _command.Execute(ValidOptions());

		// Assert
		exitCode.Should().Be(0, because: "an empty body is the contract default for a successful update");
	}

	[TestCase(null, "#1A2B3C", null, "{\"mode\":\"Color\",\"color\":\"#1A2B3C\"}")]
	[TestCase(null, "null", null, "{}")]
	[TestCase("null", null, null, "{}")]
	[TestCase(null, "#1A2B3C", "", "{\"mode\":\"Color\",\"color\":\"#1A2B3C\"}")]
	[Category("Unit")]
	[Description("Sends only the id and the background when no content field is given or a given one is empty, so the stored caption, class name and CSS are kept; null in either background argument sends an empty background, which removes it.")]
	public void Execute_ShouldSendOnlyTheIdAndTheBackground_WhenNoContentFieldIsGiven(string imageId, string color,
		string content, string expectedBackground) {
		string body = null;
		_applicationClient.ExecutePostRequest(Arg.Is<string>(u => u.Contains("UpdateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => {
				body = call.ArgAt<string>(1);
				return "{\"success\":true}";
			});

		_command.Execute(new UpdateThemeOptions {
			Id = ThemeId, BackgroundImageId = imageId, BackgroundColor = color,
			Caption = content, CssClassName = content, CssContent = content
		});

		body.Should().Be($"{{\"id\":\"{ThemeId}\",\"background\":{expectedBackground}}}",
			because: "null content fields are kept by the platform, null keeps the background, and {} removes it");
	}

	[TestCase(ThemeId, "", true, 1, 0)]
	[TestCase(ThemeId, "/", true, 1, 0)]
	[TestCase("0f5b6a1c-2d3e-4f50-8a9b-c1d2e3f4a5b6", "", true, 0, 1)]
	[TestCase(ThemeId, "", false, 0, 0)]
	[Category("Unit")]
	[Description("Binds an image into the package read from the theme's catalog cssFilePath, with or without a leading slash, and writes nothing for a theme the catalog does not list, which it names as missing, or an image the environment does not have.")]
	public void Execute_ShouldBindIntoTheCatalogPackage_OnlyForAListedTheme(string catalogThemeId, string pathPrefix,
		bool imageFound, int expectedCalls, int expectedMissingThemeErrors) {
		ArrangeGallery(imageFound);
		StubCatalog(true, new ThemeDescriptor {
			Id = catalogThemeId,
			CssFilePath = $"{pathPrefix}Terrasoft.Configuration/Pkg/UsrBranding/Files/themes/{catalogThemeId}/theme.css?hash=abc"
		});
		StubUpdateThemeSuccess();

		_command.Execute(new UpdateThemeOptions { Id = ThemeId, BackgroundImageId = ImageId });

		_applicationClient.Received(expectedCalls).ExecutePostRequest(Arg.Is<string>(u => u.Contains("UpdateTheme")),
			Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_backgroundWriter.Received(expectedCalls).TurnOffPanelIconBackground(Arg.Any<ThemeBackground>(), false);
		_backgroundWriter.Received(expectedCalls).BindImageIntoThemePackage(GalleryImage, ThemeId, "UsrBranding");
		_logger.Received(expectedMissingThemeErrors)
			.WriteError(Arg.Is<string>(m => m != null && m.StartsWith($"No theme with id '{ThemeId}'")));
	}

	[Test, Category("Unit")]
	[Description("Refuses a full overwrite that has no caption before any HTTP call.")]
	public void UpdateTheme_ShouldFailFastWithoutHttp_WhenCaptionIsMissingWithoutABackground() {
		UpdateThemeOptions options = ValidOptions();
		options.Caption = null;

		int exitCode = _command.Execute(options);

		exitCode.Should().Be(1, because: "without a background argument the update is a full overwrite, which needs a caption");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default, default, default, default);
		_logger.Received(1).WriteError("Theme caption is required.");
	}

	[Test, Category("Unit")]
	[Description("Still checks a given css-class-name in a background update, before any HTTP call.")]
	public void UpdateTheme_ShouldFailFastWithoutHttp_WhenABackgroundUpdateGivesAnInvalidCssClassName() {
		int exitCode = _command.Execute(new UpdateThemeOptions {
			Id = ThemeId, BackgroundColor = "#1A2B3C", CssClassName = "1bad"
		});

		exitCode.Should().Be(1, because: "a content field given with a background is written, so it must be valid");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default, default, default, default);
		_logger.Received(1).WriteError(Arg.Is<string>(m => m != null && m.StartsWith("css-class-name must match")));
	}

	[TestCase(false, 0, null, "GetAvailableThemes failed: catalog unavailable")]
	[TestCase(true, 0, null, "No theme with id '" + ThemeId + "' is on the environment (see list-themes); nothing was changed. "
		+ ThemeCatalogMessages.EmptyCatalogLicenseCaveat)]
	[TestCase(true, 1, "themes/" + ThemeId + "/theme.css", "Theme '" + ThemeId + "' has no package in its cssFilePath")]
	[TestCase(true, 2, "Terrasoft.Configuration/Pkg/UsrBranding/Files/themes/" + ThemeId + "/theme.css",
		"Theme id '" + ThemeId + "' matches more than one theme")]
	[Category("Unit")]
	[Description("Refuses an image background before any write when the theme's package cannot be read from the catalog: the catalog read fails, the catalog is empty, the cssFilePath names no package, or the id matches more than one theme.")]
	public void Execute_ShouldNotPostUpdateTheme_WhenTheThemePackageCannotBeResolved(bool catalogRead, int listedCopies,
		string cssFilePath, string expectedError) {
		StubCatalog(catalogRead, Enumerable.Repeat(new ThemeDescriptor { Id = ThemeId, CssFilePath = cssFilePath },
			listedCopies).ToArray());

		int exitCode = _command.Execute(new UpdateThemeOptions { Id = ThemeId, BackgroundImageId = ImageId });

		exitCode.Should().Be(1, because: "an image needs the theme's package, so the theme is not written without one");
		_applicationClient.DidNotReceive().ExecutePostRequest(Arg.Is<string>(u => u.Contains("UpdateTheme")),
			Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_logger.Received(1).WriteError(Arg.Is<string>(m => m != null && m.StartsWith(expectedError)));
	}

	[TestCase("null", null)]
	[TestCase(null, "null")]
	[Category("Unit")]
	[Description("Logs the removal and hands the empty background to the writer when either background argument is null, without reading the theme catalog.")]
	public void Execute_ShouldReportTheRemoval_WhenABackgroundArgumentIsNull(string imageId, string color) {
		StubUpdateThemeSuccess();

		int exitCode = _command.Execute(new UpdateThemeOptions {
			Id = ThemeId, BackgroundImageId = imageId, BackgroundColor = color, KeepIconBackground = true
		});

		exitCode.Should().Be(0, because: "null alone is a valid background update");
		_logger.Received(1).WriteInfo("The theme has no background now.");
		_backgroundWriter.Received(1).TurnOffPanelIconBackground(
			Arg.Is<ThemeBackground>(background => background.RemovesBackground), true);
		_themeCatalog.DidNotReceiveWithAnyArgs().TryGetAvailableThemes(default, out _, out _);
	}

	[Test, Category("Unit")]
	[Description("Refuses null together with the other background argument before any HTTP call.")]
	public void Execute_ShouldFailFastWithoutHttp_WhenNullIsCombinedWithAnotherBackgroundArgument() {
		int exitCode = _command.Execute(new UpdateThemeOptions {
			Id = ThemeId, BackgroundImageId = "null", BackgroundColor = "#1A2B3C"
		});

		exitCode.Should().Be(1, because: "a call carries at most one background argument, and null is one");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default, default, default, default);
		_logger.Received(1).WriteError(
			"background-image-id and background-color cannot be combined: pass at most one background argument.");
	}

	private void StubCatalog(bool read, params ThemeDescriptor[] themes) {
		_themeCatalog.TryGetAvailableThemes(Arg.Any<ListThemesOptions>(), out Arg.Any<IReadOnlyList<ThemeDescriptor>>(),
				out Arg.Any<string>())
			.Returns(call => {
				call[1] = themes;
				call[2] = read ? null : "catalog unavailable";
				return read;
			});
	}
}
