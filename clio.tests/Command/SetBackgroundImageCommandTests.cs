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
	/// <summary>The package the substituted delivery target reports back as the resolved delivery target.</summary>
	private const string TestPackageName = "UsrBrandingPkg";

	private static readonly Guid ImageId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
	private static readonly Guid CustomTagId = Guid.Parse("11111111-2222-3333-4444-555555555555");

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ISysSettingsManager _sysSettingsManager;
	private ISysImageUploader _sysImageUploader;
	private IPanelIconBackgroundFeatureManager _panelIconBackgroundFeature;
	private IPackageDataBinder _packageDataBinder;
	private ILogger _logger;
	private SetBackgroundImageCommand _command;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<SetBackgroundImageCommand>();
		_logger = Substitute.For<ILogger>();
		_command.Logger = _logger;
		_packageDataBinder.UsePackage(Arg.Any<string>()).Returns(TestPackageName);
		_packageDataBinder
			.BindSysSettingsValue(Arg.Any<string>(), Arg.Any<bool>())
			.Returns(PackageDataBindingOutcome.Success());
		_packageDataBinder.BindRow(
				Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<System.Collections.Generic.IReadOnlyList<string>>(), Arg.Any<Guid>())
			.Returns(PackageDataBindingOutcome.Success());
		_packageDataBinder.BindFeatureOffState(Arg.Any<string>())
			.Returns(PackageDataBindingOutcome.Success());
		_packageDataBinder.RemoveBinding(Arg.Any<string>(), Arg.Any<string>())
			.Returns([]);
		_packageDataBinder.RemoveSysSettingsValue(Arg.Any<string>(), Arg.Any<bool>())
			.Returns([]);
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_serviceUrlBuilder.ClearReceivedCalls();
		_sysSettingsManager.ClearReceivedCalls();
		_sysImageUploader.ClearReceivedCalls();
		_panelIconBackgroundFeature.ClearReceivedCalls();
		_packageDataBinder.ClearReceivedCalls();
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
		_packageDataBinder = Substitute.For<IPackageDataBinder>();
		containerBuilder.AddTransient<IApplicationClient>(_ => _applicationClient);
		containerBuilder.AddTransient<IServiceUrlBuilder>(_ => _serviceUrlBuilder);
		containerBuilder.AddTransient<ISysSettingsManager>(_ => _sysSettingsManager);
		containerBuilder.AddTransient<ISysImageUploader>(_ => _sysImageUploader);
		containerBuilder.AddTransient<IPanelIconBackgroundFeatureManager>(_ => _panelIconBackgroundFeature);
		containerBuilder.AddTransient<IPackageDataBinder>(_ => _packageDataBinder);
	}

	private void ArrangeImageExists(bool exists = true) {
		string rows = exists ? $"[{{\"Id\":\"{ImageId}\"}}]" : "[]";
		_applicationClient.ExecuteGetRequest(
				Arg.Is<string>(url => url.StartsWith("odata/SysImage?")))
			.Returns($"{{\"value\":{rows}}}");
	}

	private static string Rows(bool withRow) =>
		withRow ? $"{{\"value\":[{{\"Id\":\"{Guid.NewGuid()}\"}}]}}" : "{\"value\":[]}";

	/// <summary>
	/// Answers the consecutive gallery-membership GETs with one response per element of
	/// <paramref name="sequence"/>. The command verifies a registration by reading back AFTER the insert POST, so
	/// the number of elements is the number of membership reads that run — not an arbitrary length.
	/// </summary>
	/// <remarks>
	/// The membership filter must use navigation paths (<c>Entity/Id</c>, <c>Tag/Id</c>): the flat
	/// <c>EntityId</c>/<c>TagId</c> names fail on the platform in <c>$filter</c> with
	/// "Column by path ... not found" (verified live), which is why this matcher pins the navigation form.
	/// </remarks>
	/// <param name="sequence">Whether each successive membership read answers with a row.</param>
	private void ArrangeGalleryReads(params bool[] sequence) {
		string[] responses = System.Linq.Enumerable.ToArray(
			System.Linq.Enumerable.Select(sequence, withRow => Rows(withRow)));
		_applicationClient.ExecuteGetRequest(
				Arg.Is<string>(url => url.StartsWith("odata/SysImageInTag?$filter=Entity/Id eq ")
					&& url.Contains(" and Tag/Id eq ")))
			.Returns(responses[0], System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Skip(responses, 1)));
	}

	/// <summary>
	/// Arranges the gallery reads for the two states the command distinguishes: already registered answers the
	/// single pre-check with a row, while not-yet-registered answers the pre-check empty and the post-insert
	/// read-back with the new row.
	/// </summary>
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
		// Seeded tag: pre-check empty, read-back still empty (the insert was rejected);
		// resolved tag: pre-check empty, read-back confirms the row.
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
		// Pre-check and read-back stay empty for both the seeded and the resolved tag.
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
	[Description("Carries a failed feature turn-off on the result's warnings, not only in the log, so a non-CLI caller such as the MCP tool learns the panel may still hide the background.")]
	public void SetBackground_ShouldCarryTheFeatureTurnOffFailure_OnTheResultWarnings() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		_panelIconBackgroundFeature.When(feature => feature.DisableForAllUsers())
			.Do(_ => throw new InvalidOperationException("feature service unavailable"));
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		SetBackgroundResult result = _command.SetBackground(options);

		// Assert
		result.Warnings.Should().Contain(warning => warning.Contains(PanelIconBackgroundFeatureManager.FeatureCode),
			because: "this caveat used to be logged straight out, so an MCP caller was told the background was applied with no hint that the panel can still hide it — the result is the only channel both surfaces read");
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
		_packageDataBinder.Received(1).UsePackage(
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
		_packageDataBinder.UsePackage("UsrMyApp").Returns("UsrMyApp");
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString(), PackageName = "UsrMyApp" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a user-named package must be honored");
		_packageDataBinder.Received(1).UsePackage("UsrMyApp");
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
		_packageDataBinder.UsePackage(Arg.Any<string>())
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
		_packageDataBinder.DidNotReceiveWithAnyArgs().UsePackage(default);
	}

	[Test, Category("Unit")]
	[Description("Relays the binding reconcile's warnings in the run output at warning level, because they are the only place a delivery gap is reported and info level would give a gap the same weight as a success line.")]
	public void Execute_ShouldRelayTheBindingWarnings_AtWarningLevel() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		_packageDataBinder.BindFeatureOffState(Arg.Any<string>())
			.Returns(PackageDataBindingOutcome.Refused(
				["UsePanelIconBackground: no All-Users feature state on this environment"]));
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		_command.Execute(options);

		// Assert
		_logger.Received(1).WriteWarning(Arg.Is<string>(message => message.Contains("UsePanelIconBackground")));
	}

	[Test, Category("Unit")]
	[Description("Delivers the background configuration together with its definition, because clio creates that setting itself and an install target may not have it.")]
	public void Execute_ShouldDeliverTheConfigTogetherWithItsDefinition() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		_command.Execute(options);

		// Assert
		_packageDataBinder.Received(1).BindSysSettingsValue(
			SetBackgroundImageCommand.BackgroundConfigCode, includeDefinition: true);
	}

	[Test, Category("Unit")]
	[Description("Delivers exactly the image this run applied, by its id, into the image folder.")]
	public void Execute_ShouldDeliverTheAppliedImage() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		_command.Execute(options);

		// Assert
		_packageDataBinder.Received(1).BindRow(
			"SysImage", "ShellBackground",
			Arg.Any<System.Collections.Generic.IReadOnlyList<string>>(), ImageId);
	}

	[Test, Category("Unit")]
	[Description("Delivers the gallery membership row the apply confirmed, so the image stays selectable in the gallery on the install target.")]
	public void Execute_ShouldDeliverTheGalleryMembership() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		_command.Execute(options);

		// Assert
		_packageDataBinder.Received(1).BindRow(
			"SysImageInTag", "ShellBackground",
			Arg.Any<System.Collections.Generic.IReadOnlyList<string>>(), Arg.Any<Guid>());
	}

	[Test, Category("Unit")]
	[Description("Withholds the gallery membership registered under a customized tag id — that id would not resolve on an install target — and stops shipping any earlier membership folder.")]
	public void Execute_ShouldWithholdTheGalleryMembership_ForACustomizedTag() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryReads(false, false, true);
		_applicationClient.ExecuteGetRequest(
				Arg.Is<string>(url => url.StartsWith("odata/SysImageTag?")))
			.Returns($"{{\"value\":[{{\"Id\":\"{CustomTagId}\"}}]}}");
		_applicationClient.ExecutePostRequest(Arg.Is<string>(url => url == "odata/SysImageInTag"), Arg.Any<string>())
			.Returns("{}");
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		_command.Execute(options);

		// Assert
		_packageDataBinder.DidNotReceive().BindRow(
			"SysImageInTag", "ShellBackground",
			Arg.Any<System.Collections.Generic.IReadOnlyList<string>>(), Arg.Any<Guid>());
		_packageDataBinder.Received(1).RemoveBinding("SysImageInTag_ShellBackground", "SysImageInTag");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message => message.Contains("customized")));
	}

	[Test, Category("Unit")]
	[Description("Names the parts that were already bound when a later delivery throws, so the caller is not told the whole binding failed while the package already carries some of it.")]
	public void Execute_ShouldNameTheAlreadyBoundParts_WhenALaterDeliveryFails() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		_packageDataBinder.BindFeatureOffState(Arg.Any<string>())
			.Throws(new InvalidOperationException("SaveSchema rejected the binding"));
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the user asked for a background that ships with the package, and part of the delivery failed");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Already bound and left in place: image, gallery-membership, background-config")
			&& message.Contains("SaveSchema rejected the binding")));
	}

	[Test, Category("Unit")]
	[Description("Omits the already-bound note when the delivery failed before anything landed, so the message never implies package changes that did not happen.")]
	public void Execute_ShouldOmitTheAlreadyBoundNote_WhenNothingWasBound() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		_packageDataBinder.BindRow(
				Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<System.Collections.Generic.IReadOnlyList<string>>(), Arg.Any<Guid>())
			.Throws(new InvalidOperationException("package is locked"));
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		_command.Execute(options);

		// Assert
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("package is locked") && !message.Contains("Already bound")));
	}

	[Test, Category("Unit")]
	[Description("Delivers the image before the configuration that names it, so a delivery that stops partway can never leave the package shipping a configuration whose image is missing.")]
	public void Execute_ShouldDeliverTheImage_BeforeTheConfigurationThatNamesIt() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the delivery order only matters on a run that reached the package");
		Received.InOrder(() => {
			_packageDataBinder.BindRow(
				"SysImage", "ShellBackground",
				Arg.Any<System.Collections.Generic.IReadOnlyList<string>>(), ImageId);
			_packageDataBinder.BindSysSettingsValue(
				SetBackgroundImageCommand.BackgroundConfigCode, includeDefinition: true);
		});
	}

	[Test, Category("Unit")]
	[Description("Withholds the background configuration when the image row was not bound, because a configuration naming an image the package does not ship installs a background the target cannot render.")]
	public void Execute_ShouldWithholdTheConfiguration_WhenTheImageIsNotBound() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		_packageDataBinder.BindRow(
				"SysImage", Arg.Any<string>(),
				Arg.Any<System.Collections.Generic.IReadOnlyList<string>>(), Arg.Any<Guid>())
			.Returns(PackageDataBindingOutcome.Refused(["SysImage_ShellBackground: row not found"]));
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		_command.Execute(options);

		// Assert
		_packageDataBinder.DidNotReceive().BindSysSettingsValue(
			SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<bool>());
		_packageDataBinder.Received(1).RemoveSysSettingsValue(
			SetBackgroundImageCommand.BackgroundConfigCode, includeDefinition: true);
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains(SetBackgroundImageCommand.BackgroundConfigCode)
			&& message.Contains("the image row was not bound")));
	}

	[Test, Category("Unit")]
	[Description("Says nothing was bound on a successful run whose every delivery was refused, so the package line never claims a delivery the warnings beside it contradict.")]
	public void Execute_ShouldSayNothingWasBound_WhenEveryDeliveryIsRefused() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		_packageDataBinder.BindRow(
				Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<System.Collections.Generic.IReadOnlyList<string>>(), Arg.Any<Guid>())
			.Returns(PackageDataBindingOutcome.Refused(["SysImage_ShellBackground: row not found"]));
		_packageDataBinder.BindFeatureOffState(Arg.Any<string>())
			.Returns(PackageDataBindingOutcome.Refused(["UsePanelIconBackground: not confirmed off"]));
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "the background was applied — a delivery gap is a warning channel, not an apply failure");
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains($"No background data could be bound into package '{TestPackageName}'")));
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(message =>
			message.Contains("Background data bound into package")));
	}

	[Test, Category("Unit")]
	[Description("Names the parts the delivery landed on a fully successful run, so the package line reports what the package actually carries.")]
	public void Execute_ShouldNameTheBoundParts_WhenEveryDeliverySucceeds() {
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
			message.Contains($"Background data bound into package '{TestPackageName}'")
			&& message.Contains("image, gallery-membership, background-config, panel-icon-off-state")));
	}

	[Test, Category("Unit")]
	[Description("Reports the withheld gallery membership as a warning when the image row was not bound, so no delivery gap leaves the run output without a line naming it.")]
	public void Execute_ShouldReportTheWithheldGalleryMembership_WhenTheImageIsNotBound() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		_packageDataBinder.BindRow(
				"SysImage", Arg.Any<string>(),
				Arg.Any<System.Collections.Generic.IReadOnlyList<string>>(), Arg.Any<Guid>())
			.Returns(PackageDataBindingOutcome.Refused(["SysImage_ShellBackground: row not found"]));
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		_command.Execute(options);

		// Assert
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("background gallery membership")
			&& message.Contains("the image row was not bound")));
	}

	[Test, Category("Unit")]
	[Description("Carries the resolved package when binding fails partway, so a structured caller learns where the parts that landed went without parsing the message.")]
	public void SetBackground_ShouldCarryThePackage_WhenBindingFailsPartway() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		_packageDataBinder.BindFeatureOffState(Arg.Any<string>())
			.Throws(new InvalidOperationException("SaveSchema rejected the binding"));
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		SetBackgroundResult result = _command.SetBackground(options);

		// Assert
		result.Success.Should().BeFalse(because: "part of the delivery the caller asked for did not land");
		result.Package.Should().Be(TestPackageName,
			because: "the parts bound before the failure are in it, so the field its own contract describes must name it");
	}

	[Test, Category("Unit")]
	[Description("Leaves the package unnamed when the run failed before a delivery target was resolved, so the result never points at a package it never touched.")]
	public void SetBackground_ShouldLeaveThePackageUnnamed_WhenNoTargetWasResolved() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		_packageDataBinder.UsePackage(Arg.Any<string>())
			.Throws(new InvalidOperationException("package is locked"));
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		SetBackgroundResult result = _command.SetBackground(options);

		// Assert
		result.Package.Should().BeNull(
			because: "nothing was bound anywhere, so naming a package would invent a change that never happened");
	}

	[Test, Category("Unit")]
	[Description("Carries the bound parts when binding fails partway, so the field its own contract describes is not empty while the package already carries them.")]
	public void SetBackground_ShouldCarryTheBoundParts_WhenBindingFailsPartway() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		_packageDataBinder.BindFeatureOffState(Arg.Any<string>())
			.Throws(new InvalidOperationException("SaveSchema rejected the binding"));
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString() };

		// Act
		SetBackgroundResult result = _command.SetBackground(options);

		// Assert
		result.Success.Should().BeFalse(because: "part of the delivery did not land");
		result.Bound.Should().Contain("image",
			because: "an empty Bound must mean the package carries nothing from this run, so a failure that did bind must say so");
	}

	[Test, Category("Unit")]
	[Description("Always delivers the panel-icon off-state through its verify-inside delivery, even when the turn-off was skipped — the delivery itself refuses a state that is not confirmed off.")]
	public void Execute_ShouldAlwaysAskForTheFeatureOffStateDelivery() {
		// Arrange
		ArrangeImageExists();
		ArrangeGalleryState(alreadyRegistered: true);
		_sysSettingsManager.UpdateSysSetting(SetBackgroundImageCommand.BackgroundConfigCode, Arg.Any<object>())
			.Returns(true);
		SetBackgroundImageOptions options = new() { ImageId = ImageId.ToString(), KeepIconBackground = true };

		// Act
		_command.Execute(options);

		// Assert
		_packageDataBinder.Received(1).BindFeatureOffState("UsePanelIconBackground");
	}
}
