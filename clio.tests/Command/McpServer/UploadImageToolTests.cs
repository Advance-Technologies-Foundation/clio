using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Clio.Command.Branding;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class UploadImageToolTests {

	private static readonly Guid UploadedImageId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

	[Test]
	[Category("Unit")]
	[Description("Declares the safety flags on the upload-image tool method: a non-destructive write (a new SysImage record is created on every call, nothing existing is overwritten), non-idempotent, closed-world.")]
	public void UploadImageTool_ShouldDeclareAdditiveWriteSafetyFlags_WhenInspectingMcpServerToolAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(UploadImageTool)
			.GetMethod(nameof(UploadImageTool.UploadImage))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(UploadImageTool.ToolName,
			because: "the tool must be published under its canonical kebab-case name");
		attribute.ReadOnly.Should().BeFalse(because: "uploading an image writes a SysImage record");
		attribute.Destructive.Should().BeFalse(
			because: "the upload is additive-only — every call creates a new SysImage record and never overwrites existing data");
		attribute.Idempotent.Should().BeFalse(
			because: "repeating the call creates another SysImage record with a new id");
		attribute.OpenWorld.Should().BeFalse(because: "the tool only touches the addressed Creatio environment");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the single args wrapper as required at the MCP schema level, so a call that omits args fails with a structured error instead of an opaque binding failure.")]
	public void UploadImageTool_ShouldRequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] requiredAttributes = typeof(UploadImageTool)
			.GetMethod(nameof(UploadImageTool.UploadImage))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		requiredAttributes.Should().NotBeEmpty(
			because: "the args wrapper must be schema-required so an omitted args object fails with a structured error, not an opaque MCP binding failure");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the environment-scoped upload-image command, forwards the file path, and returns the created SysImage id as a structured success result.")]
	public void UploadImage_ShouldResolveCommandAndReturnImageId() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeUploadImageCommand defaultCommand = new();
		FakeUploadImageCommand resolvedCommand = new(SysImageUploadResult.Successful(UploadedImageId));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UploadImageCommand>(Arg.Any<UploadImageOptions>()).Returns(resolvedCommand);
		UploadImageTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		UploadImageResult result = tool.UploadImage(new UploadImageArgs(
			EnvironmentName: "docker_fix2", File: "C:/brand/background.svg"));

		// Assert
		result.Success.Should().BeTrue(because: "a verified upload must report success");
		result.ImageId.Should().Be(UploadedImageId.ToString(),
			because: "the created SysImage id is the value the caller needs for SysImageInTag and CrtBackgroundConfig");
		commandResolver.Received(1).Resolve<UploadImageCommand>(Arg.Is<UploadImageOptions>(options =>
			options.Environment == "docker_fix2" && options.File == "C:/brand/background.svg"));
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command instance should perform the upload");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware tool path must use the resolved command instance, not the startup-time one");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured failure naming environment-name when the required environment name is omitted, without resolving a command.")]
	public void UploadImage_ShouldReturnFailure_WhenEnvironmentNameIsMissing() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeUploadImageCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		UploadImageTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		UploadImageResult result = tool.UploadImage(new UploadImageArgs(File: "C:/brand/background.svg"));

		// Assert
		result.Success.Should().BeFalse(because: "a request without an environment name is invalid");
		result.Error.Should().Contain("environment-name",
			because: "the failure must name the exact kebab-case field the caller has to add");
		commandResolver.DidNotReceive().Resolve<UploadImageCommand>(Arg.Any<UploadImageOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured failure naming file when the required file path is omitted, without resolving a command.")]
	public void UploadImage_ShouldReturnFailure_WhenFileIsMissing() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeUploadImageCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		UploadImageTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		UploadImageResult result = tool.UploadImage(new UploadImageArgs(EnvironmentName: "docker_fix2"));

		// Assert
		result.Success.Should().BeFalse(because: "a request without a file path has nothing to upload");
		result.Error.Should().Contain("file",
			because: "the failure must name the exact field the caller has to add");
		commandResolver.DidNotReceive().Resolve<UploadImageCommand>(Arg.Any<UploadImageOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces the command's failure message as a structured error result when the upload fails.")]
	public void UploadImage_ShouldReturnFailure_WhenCommandReportsFailure() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeUploadImageCommand defaultCommand = new();
		FakeUploadImageCommand resolvedCommand = new(SysImageUploadResult.Failure("File not found: 'C:/missing.png'."));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UploadImageCommand>(Arg.Any<UploadImageOptions>()).Returns(resolvedCommand);
		UploadImageTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		UploadImageResult result = tool.UploadImage(new UploadImageArgs(
			EnvironmentName: "docker_fix2", File: "C:/missing.png"));

		// Assert
		result.Success.Should().BeFalse(because: "a failed upload must not report success");
		result.Error.Should().Contain("File not found",
			because: "the caller needs the actionable failure reason surfaced from the command");
		result.ImageId.Should().BeNull(because: "no SysImage record was created");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeUploadImageCommand : UploadImageCommand {
		private readonly SysImageUploadResult _result;

		public UploadImageOptions CapturedOptions { get; private set; }

		public FakeUploadImageCommand(SysImageUploadResult result = null)
			: base(new EnvironmentSettings(), Substitute.For<ISysImageUploader>()) {
			_result = result ?? SysImageUploadResult.Successful(UploadedImageId);
		}

		public override SysImageUploadResult UploadImage(UploadImageOptions options) {
			CapturedOptions = options;
			return _result;
		}
	}
}
