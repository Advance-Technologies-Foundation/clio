namespace Clio.Tests.Command;

using System;
using Clio.Command.Branding;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public sealed class SetBackgroundImageCommandTests : BaseCommandTests<SetBackgroundImageOptions>
{
	/// <summary>The package the substituted binding service reports back as the resolved delivery target.</summary>
	private const string TestPackageName = "UsrBrandingPkg";

	private static readonly Guid ImageId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
	private static readonly Guid CustomTagId = Guid.Parse("11111111-2222-3333-4444-555555555555");

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ISysSettingsManager _sysSettingsManager;
	private ISysImageUploader _sysImageUploader;
	private IPanelIconBackgroundFeatureManager _panelIconBackgroundFeature;
	private IBrandingBindingService _brandingBindingService;
	private ILogger _logger;
	private SetBackgroundImageCommand _command;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<SetBackgroundImageCommand>();
		_logger = Substitute.For<ILogger>();
		_command.Logger = _logger;
		_brandingBindingService.BindBackground(Arg.Any<string>())
			.Returns(new BrandingScopeReport(BrandingScope.Background, TestPackageName, [], [], false));
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_serviceUrlBuilder.ClearReceivedCalls();
		_sysSettingsManager.ClearReceivedCalls();
		_sysImageUploader.ClearReceivedCalls();
		_panelIconBackgroundFeature.ClearReceivedCalls();
		_brandingBindingService.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build(Arg.Any<string>()).Returns(callInfo => callInfo.Arg<string>());
		_sysSettingsManager = Substitute.For<ISysSettingsManager>();
		_sysImageUploader = Substitute.For<ISysImageUploader>();
		_panelIconBackgroundFeature = Substitute.For<IPanelIconBackgroundFeatureManager>();
		_brandingBindingService = Substitute.For<IBrandingBindingService>();
		containerBuilder.AddTransient<IApplicationClient>(_ => _applicationClient);
		containerBuilder.AddTransient<IServiceUrlBuilder>(_ => _serviceUrlBuilder);
		containerBuilder.AddTransient<ISysSettingsManager>(_ => _sysSettingsManager);
		containerBuilder.AddTransient<ISysImageUploader>(_ => _sysImageUploader);
		containerBuilder.AddTransient<IPanelIconBackgroundFeatureManager>(_ => _panelIconBackgroundFeature);
		containerBuilder.AddTransient<IBrandingBindingService>(_ => _brandingBindingService);
	}

	private void ArrangeImageExists(bool exists = true) {
		string rows = exists ? $"[{{\"Id\":\"{ImageId}\"}}]" : "[]";
		_applicationClient.ExecuteGetRequest(
				Arg.Is<string>(url => url.StartsWith("odata/SysImage?")))
			.Returns($"{{\"value\":{rows}}}");
	}

	private static string Rows(bool withRow) =>
		withRow ? $"{{\"value\":[{{\"Id\":\"{Guid.NewGuid()}\"}}]}}" : "{\"value\":[]}";

	private void ArrangeGalleryReads(params bool[] sequence) {
		string[] responses = System.Linq.Enumerable.ToArray(
			System.Linq.Enumerable.Select(sequence, withRow => Rows(withRow)));
		_applicationClient.ExecuteGetRequest(
				Arg.Is<string>(url => url.StartsWith("odata/SysImageInTag?$filter=Entity/Id eq ")
					&& url.Contains(" and Tag/Id eq ")))
			.Returns(responses[0], System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Skip(responses, 1)));
	}

	private void ArrangeGalleryState(bool alreadyRegistered) {
		if (alreadyRegistered) {
			ArrangeGalleryReads(true);
		} else {
			ArrangeGalleryReads(false, true);
		}
	}

	[Test, Category("Unit")]
	[Description("Sets the background end to end: verifies the image, registers it in the background gallery, and points the background configuration at it.")]
	public void Execute_ShouldSetBackground_WhenImageExistsAndIsNotYetInGallery() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: false);
		_applicationClient.ExecutePostRequest(Arg.Is<string>(url => url == "odata/SysImageInTag"), Arg.Any<string>())
			.Returns($"{{\"Id\":\"{Guid.NewGuid()}\"}}");
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a verified image that was registered and configured is a success");
		_applicationClient.Received(1).ExecutePostRequest(
			"odata/SysImageInTag",
			Arg.Is<string>(body => body.Contains(ImageId.ToString())
				&& body.Contains(SetBackgroundImageCommand.ShellBackgroundTagId.ToString())));
		_sysSettingsManager.Received(1).UpdateSysSetting(
			SetBackgroundImageCommand.BackgroundConfigCode,
			Arg.Is<object>(value => value.ToString().Contains(ImageId.ToString())
				&& value.ToString().Contains("Image")));
	}

	[Test, Category("Unit")]
	[Description("Uploads the local file and sets the created image as the background when --file is passed, skipping the existence probe (the upload itself proves the image).")]
	public void Execute_ShouldUploadAndSetBackground_WhenFileIsPassed() {
		// Arrange
		_sysImageUploader.UploadAsync("C:/brand/background.png", Arg.Any<System.Threading.CancellationToken>())
			.Returns(SysImageUploadResult.Successful(ImageId));
		ArrangeGalleryState(alreadyRegistered: false);
		_applicationClient.ExecutePostRequest(Arg.Is<string>(url => url == "odata/SysImageInTag"), Arg.Any<string>())
			.Returns($"{{\"Id\":\"{Guid.NewGuid()}\"}}");
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		SetBackgroundImageOptions options = new() { File = "C:/brand/background.png" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a file source is uploaded first and then applied like an image id");
		_sysSettingsManager.Received(1).UpdateSysSetting(
			SetBackgroundImageCommand.BackgroundConfigCode,
			Arg.Is<object>(value => value.ToString().Contains(ImageId.ToString())));
		_applicationClient.DidNotReceive().ExecuteGetRequest(
			Arg.Is<string>(url => url.StartsWith("odata/SysImage?")));
	}

	[Test, Category("Unit")]
	[Description("Fails without touching the environment when both a file and an image id are passed — the sources are mutually exclusive.")]
	public void Execute_ShouldFail_WhenBothFileAndImageIdArePassed() {
		// Arrange
		SetBackgroundImageOptions options = new() {
			ImageId = ImageId.ToString(), File = "C:/brand/background.png"
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "two image sources are ambiguous and must be rejected");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("not both")));
		_applicationClient.DidNotReceiveWithAnyArgs().ExecuteGetRequest(default);
		_sysImageUploader.DidNotReceiveWithAnyArgs().UploadAsync(default);
	}

	[Test, Category("Unit")]
	[Description("Fails without touching the environment when neither a file nor an image id is passed.")]
	public void Execute_ShouldFail_WhenNoImageSourceIsPassed() {
		// Arrange
		SetBackgroundImageOptions options = new();

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "there is no image to apply");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("file") && message.Contains("image-id")));
		_applicationClient.DidNotReceiveWithAnyArgs().ExecuteGetRequest(default);
	}

	[Test, Category("Unit")]
	[Description("Surfaces the uploader's failure message when the --file upload fails, without writing anything.")]
	public void Execute_ShouldFail_WhenFileUploadFails() {
		// Arrange
		_sysImageUploader.UploadAsync(Arg.Any<string>(), Arg.Any<System.Threading.CancellationToken>())
			.Returns(SysImageUploadResult.Failure("File not found: 'C:/missing.png'."));
		SetBackgroundImageOptions options = new() { File = "C:/missing.png" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a failed upload leaves nothing to apply");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("File not found")));
		_sysSettingsManager.DidNotReceiveWithAnyArgs().UpdateSysSetting(default, default);
	}

	[Test, Category("Unit")]
	[Description("Fails with a message naming image-id when the value is not a valid id, without touching the environment.")]
	public void Execute_ShouldFail_WhenImageIdIsNotAGuid() {
		// Arrange
		SetBackgroundImageOptions options = new() { ImageId = "not-a-guid" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an unparsable image id cannot be applied");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("image-id")));
		_applicationClient.DidNotReceiveWithAnyArgs().ExecuteGetRequest(default);
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
	}

	[Test, Category("Unit")]
	[Description("Fails with an upload-image pointer when the environment answers the existence probe with an empty row set, without writing anything.")]
	public void Execute_ShouldFail_WhenImageDoesNotExist() {
		// Arrange
		ArrangeImageExists(exists: false);
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a missing image cannot be set as the background");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("upload-image")));
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
		_sysSettingsManager.DidNotReceiveWithAnyArgs().UpdateSysSetting(default, default);
	}

	[Test, Category("Unit")]
	[Description("Fails with a could-not-check message — not the misleading upload-image pointer — when the existence probe itself fails (transport or auth), without writing anything.")]
	public void Execute_ShouldFail_WithoutUploadPointer_WhenExistenceProbeFails() {
		// Arrange
		_applicationClient.ExecuteGetRequest(Arg.Any<string>())
			.Throws(new InvalidOperationException("connection refused"));
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an unanswered existence probe cannot prove anything");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Could not check the image") && !message.Contains("upload-image")));
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
		_sysSettingsManager.DidNotReceiveWithAnyArgs().UpdateSysSetting(default, default);
	}

	[Test, Category("Unit")]
	[Description("Aborts without inserting a gallery row when the gallery-membership read fails, so a transient read failure cannot create duplicate registrations.")]
	public void Execute_ShouldAbortWithoutInsert_WhenGalleryReadFails() {
		// Arrange
		ArrangeImageExists();
		_applicationClient.ExecuteGetRequest(
				Arg.Is<string>(url => url.StartsWith("odata/SysImageInTag?")))
			.Returns("{\"error\":{\"message\":\"boom\"}}");
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an unreadable gallery must abort the flow");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Could not check the background gallery")));
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
		_sysSettingsManager.DidNotReceiveWithAnyArgs().UpdateSysSetting(default, default);
	}

	[Test, Category("Unit")]
	[Description("Skips the gallery insert when the image is already registered, and still points the background configuration at it (idempotent re-run).")]
	public void Execute_ShouldSkipGalleryInsert_WhenImageIsAlreadyRegistered() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "re-applying an already-registered image is a valid, idempotent request");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
		_sysSettingsManager.Received(1).UpdateSysSetting(
			SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>());
	}

	[Test, Category("Unit")]
	[Description("Re-resolves the gallery tag by name and retries the registration when the platform-seeded tag id is rejected (customized installation).")]
	public void Execute_ShouldRetryWithResolvedTagId_WhenSeededTagIdIsRejected() {
		// Arrange
		ArrangeImageExists();
		_applicationClient.ExecuteGetRequest(
				Arg.Is<string>(url => url.StartsWith("odata/SysImageTag?")))
			.Returns($"{{\"value\":[{{\"Id\":\"{CustomTagId}\"}}]}}");
		_applicationClient.ExecutePostRequest("odata/SysImageInTag",
				Arg.Is<string>(body => body.Contains(SetBackgroundImageCommand.ShellBackgroundTagId.ToString())))
			.Returns("{\"error\":{\"message\":\"FK violation\"}}");
		_applicationClient.ExecutePostRequest("odata/SysImageInTag",
				Arg.Is<string>(body => body.Contains(CustomTagId.ToString())))
			.Returns($"{{\"Id\":\"{Guid.NewGuid()}\"}}");
		ArrangeGalleryReads(false, false, false, true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the by-name tag lookup must recover from a deviating seeded tag id");
		_applicationClient.Received(1).ExecutePostRequest("odata/SysImageInTag",
			Arg.Is<string>(body => body.Contains(CustomTagId.ToString())));
	}

	[Test, Category("Unit")]
	[Description("Does not trust the insert response body: a non-JSON 2xx POST body (e.g. a login page) still counts as registered when the authoritative read-back confirms the membership row.")]
	public void Execute_ShouldSucceed_WhenPostBodyIsNotJsonButReadBackConfirmsRow() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryReads(false, true);
		_applicationClient.ExecutePostRequest(Arg.Is<string>(url => url == "odata/SysImageInTag"), Arg.Any<string>())
			.Returns("<html>login page</html>");
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the read-back, not the POST body, is the authoritative registration proof");
	}

	[Test, Category("Unit")]
	[Description("Does not trust the insert response body: a success-looking POST body must NOT report success when the read-back shows the row never materialized (for the seeded and the by-name-resolved tag alike).")]
	public void Execute_ShouldFail_WhenPostLooksSuccessfulButReadBackShowsNoRow() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryReads(false, false, false, false);
		_applicationClient.ExecuteGetRequest(
				Arg.Is<string>(url => url.StartsWith("odata/SysImageTag?")))
			.Returns($"{{\"value\":[{{\"Id\":\"{CustomTagId}\"}}]}}");
		_applicationClient.ExecutePostRequest(Arg.Is<string>(url => url == "odata/SysImageInTag"), Arg.Any<string>())
			.Returns($"{{\"Id\":\"{Guid.NewGuid()}\"}}");
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an unconfirmed registration must not report success");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Registering the image in the background gallery failed")));
		_sysSettingsManager.DidNotReceiveWithAnyArgs().UpdateSysSetting(default, default);
	}

	[Test, Category("Unit")]
	[Description("Fails with a message naming the background configuration setting when the gallery registration succeeded but the setting write failed.")]
	public void Execute_ShouldFail_WhenSettingWriteFails() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(false);
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the background is not applied until the configuration write succeeds");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains(SetBackgroundImageCommand.BackgroundConfigCode)));
	}

	[Test, Category("Unit")]
	[Description("Turns the UsePanelIconBackground feature off for everyone after the background is applied, so the panel does not hide the shell background.")]
	public void Execute_ShouldDisablePanelIconBackground_AfterApplyingBackground() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the background was applied and the feature was turned off");
		_panelIconBackgroundFeature.Received(1).DisableForAllUsers();
	}

	[Test, Category("Unit")]
	[Description("Does not touch the panel-icon-background feature when the background is not applied (the configuration write failed).")]
	public void Execute_ShouldNotDisablePanelIconBackground_WhenBackgroundNotApplied() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(false);
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		_command.Execute(options);

		// Assert
		_panelIconBackgroundFeature.DidNotReceive().DisableForAllUsers();
	}

	[Test, Category("Unit")]
	[Description("Still reports success when the feature turn-off fails: the background is already applied, so the failure is a warning, not a command failure.")]
	public void Execute_ShouldSucceedWithWarning_WhenFeatureTurnOffFails() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		_panelIconBackgroundFeature.When(feature => feature.DisableForAllUsers())
			.Do(_ => throw new InvalidOperationException("feature service unavailable"));
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the background is applied and cannot be cleanly rolled back, so a failed feature toggle is only a warning");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains(PanelIconBackgroundFeatureManager.FeatureCode)));
	}

	[Test, Category("Unit")]
	[Description("Leaves the UsePanelIconBackground feature untouched when keep-icon-background is passed, so the caller can opt out of the turn-off.")]
	public void Execute_ShouldNotDisablePanelIconBackground_WhenKeepIconBackgroundIsPassed() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString(), KeepIconBackground = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "keeping the feature as is must not fail the apply");
		_panelIconBackgroundFeature.DidNotReceive().DisableForAllUsers();
	}

	[Test, Category("Unit")]
	[Description("Leaves the package unset on the binding call when the caller names none, so the environment's CurrentPackageId decides where the data lands.")]
	public void Execute_ShouldLeaveThePackageUnset_WhenNoPackageIsNamed() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the apply and the bind both succeeded");
		_brandingBindingService.Received(1).BindBackground(
			Arg.Is<string>(package => string.IsNullOrWhiteSpace(package)));
	}

	[Test, Category("Unit")]
	[Description("Binds the background into the caller-named package instead of the default.")]
	public void Execute_ShouldBindBackgroundIntoNamedPackage_WhenPackageIsPassed() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		_brandingBindingService.BindBackground("UsrMyApp")
			.Returns(new BrandingScopeReport(BrandingScope.Background, TestPackageName, ["background image"], [], false));
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString(), PackageName = "UsrMyApp" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a user-named package must be honored");
		_brandingBindingService.Received(1).BindBackground("UsrMyApp");
	}

	[Test, Category("Unit")]
	[Description("Reports the bound package in the run output so the user learns where the background data landed.")]
	public void Execute_ShouldNameTheBoundPackage_InTheRunOutput() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		_command.Execute(options);

		// Assert
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains($"bound into package '{TestPackageName}'")));
	}

	[Test, Category("Unit")]
	[Description("Fails with a message naming the applied image and the package when the apply succeeded but the binding failed, so a delivery failure is never silent.")]
	public void Execute_ShouldFailNamingTheAppliedImage_WhenBindingFails() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		_brandingBindingService.BindBackground(Arg.Any<string>())
			.Throws(new InvalidOperationException("package is locked"));
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the user asked for a background that ships with the package, and the package part failed");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains(ImageId.ToString()) && message.Contains("package is locked")));
	}

	[Test, Category("Unit")]
	[Description("Never runs the binding when the background apply itself failed, so a broken apply cannot ship stale package data.")]
	public void Execute_ShouldNotBind_WhenApplyFails() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(false);
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		_command.Execute(options);

		// Assert
		_brandingBindingService.DidNotReceiveWithAnyArgs().BindBackground(default);
	}

	[Test, Category("Unit")]
	[Description("Relays the binding reconcile's skipped entries in the run output, because they are the only place a delivery gap is reported.")]
	public void Execute_ShouldRelayTheSkippedEntries_FromTheBindingReport() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		_brandingBindingService.BindBackground(Arg.Any<string>())
			.Returns(new BrandingScopeReport(BrandingScope.Background, TestPackageName, ["background image"],
				["UsePanelIconBackground: no All-Users feature state on this environment"], false));
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		_command.Execute(options);

		// Assert
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("Skipped:") && message.Contains("UsePanelIconBackground")));
	}
}
