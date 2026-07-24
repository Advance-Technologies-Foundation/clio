namespace Clio.Tests.Command;

using System;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public sealed class UploadImageCommandTests : BaseCommandTests<UploadImageOptions>
{
	private static readonly Guid UploadedImageId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

	private ISysImageUploader _uploader;
	private ILogger _logger;
	private UploadImageCommand _command;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<UploadImageCommand>();
		_logger = Substitute.For<ILogger>();
		_command.Logger = _logger;
	}

	public override void TearDown() {
		_uploader.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_uploader = Substitute.For<ISysImageUploader>();
		containerBuilder.AddTransient<ISysImageUploader>(_ => _uploader);
	}

	[Test, Category("Unit")]
	[Description("Reports success and prints the created SysImage id when the uploader succeeds, so the caller can chain the id into SysImageInTag / CrtBackgroundConfig.")]
	public void Execute_ShouldReportImageId_WhenUploadSucceeds() {
		// Arrange
		_uploader.UploadAsync("C:/brand/background.png", Arg.Any<System.Threading.CancellationToken>())
			.Returns(SysImageUploadResult.Successful(UploadedImageId));
		UploadImageOptions options = new() { File = "C:/brand/background.png" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a verified upload is a success");
		_logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains(UploadedImageId.ToString())));
	}

	[Test, Category("Unit")]
	[Description("Reports failure with the uploader's diagnostic message when the upload fails.")]
	public void Execute_ShouldReportError_WhenUploadFails() {
		// Arrange
		_uploader.UploadAsync(Arg.Any<string>(), Arg.Any<System.Threading.CancellationToken>())
			.Returns(SysImageUploadResult.Failure("File not found: 'C:/missing.png'."));
		UploadImageOptions options = new() { File = "C:/missing.png" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a failed upload must surface as a non-zero exit code");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("File not found")));
	}
}
