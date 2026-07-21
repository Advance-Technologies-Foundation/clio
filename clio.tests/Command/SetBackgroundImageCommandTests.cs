namespace Clio.Tests.Command;

using System;
using Clio.Command.Branding;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public sealed class SetBackgroundImageCommandTests : BaseCommandTests<SetBackgroundImageOptions>
{
	private static readonly Guid ImageId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
	private static readonly Guid CustomTagId = Guid.Parse("11111111-2222-3333-4444-555555555555");

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ISysSettingsManager _sysSettingsManager;
	private ILogger _logger;
	private SetBackgroundImageCommand _command;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<SetBackgroundImageCommand>();
		_logger = Substitute.For<ILogger>();
		_command.Logger = _logger;
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_sysSettingsManager.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build(Arg.Any<string>()).Returns(callInfo => callInfo.Arg<string>());
		_sysSettingsManager = Substitute.For<ISysSettingsManager>();
		containerBuilder.AddTransient<IApplicationClient>(_ => _applicationClient);
		containerBuilder.AddTransient<IServiceUrlBuilder>(_ => _serviceUrlBuilder);
		containerBuilder.AddTransient<ISysSettingsManager>(_ => _sysSettingsManager);
	}

	private void ArrangeImageExists(bool exists = true) {
		string rows = exists ? $"[{{\"Id\":\"{ImageId}\"}}]" : "[]";
		_applicationClient.ExecuteGetRequest(
				Arg.Is<string>(url => url.StartsWith("odata/SysImage?")))
			.Returns($"{{\"value\":{rows}}}");
	}

	private void ArrangeGalleryState(bool alreadyRegistered) {
		string rows = alreadyRegistered ? $"[{{\"Id\":\"{Guid.NewGuid()}\"}}]" : "[]";
		// The membership filter must use navigation paths (Entity/Id, Tag/Id): flat EntityId/TagId
		// names in $filter fail on the platform with "Column by path ... not found" (verified live).
		_applicationClient.ExecuteGetRequest(
				Arg.Is<string>(url => url.StartsWith("odata/SysImageInTag?$filter=Entity/Id eq ")
					&& url.Contains(" and Tag/Id eq ")))
			.Returns($"{{\"value\":{rows}}}");
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
		ArrangeGalleryState(alreadyRegistered: false);
		_applicationClient.ExecuteGetRequest(
				Arg.Is<string>(url => url.StartsWith("odata/SysImageTag?")))
			.Returns($"{{\"value\":[{{\"Id\":\"{CustomTagId}\"}}]}}");
		_applicationClient.ExecutePostRequest("odata/SysImageInTag",
				Arg.Is<string>(body => body.Contains(SetBackgroundImageCommand.ShellBackgroundTagId.ToString())))
			.Returns("{\"error\":{\"message\":\"FK violation\"}}");
		_applicationClient.ExecutePostRequest("odata/SysImageInTag",
				Arg.Is<string>(body => body.Contains(CustomTagId.ToString())))
			.Returns($"{{\"Id\":\"{Guid.NewGuid()}\"}}");
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
}
