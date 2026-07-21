using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class SetBackgroundImageToolTests {

	private static readonly Guid ImageId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

	[Test]
	[Category("Unit")]
	[Description("Declares the safety flags on the set-background-image tool method: a destructive write (the currently configured background is replaced for all users), idempotent (re-applying the same image converges to the same state), closed-world.")]
	public void SetBackgroundImageTool_ShouldDeclareDestructiveWriteSafetyFlags_WhenInspectingMcpServerToolAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(SetBackgroundImageTool)
			.GetMethod(nameof(SetBackgroundImageTool.SetBackgroundImage))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(SetBackgroundImageTool.ToolName,
			because: "the tool must be published under its canonical kebab-case name");
		attribute.ReadOnly.Should().BeFalse(because: "setting the background writes to the environment");
		attribute.Destructive.Should().BeTrue(
			because: "the currently configured background is replaced for all users, so the MCP host must prompt before running it");
		attribute.Idempotent.Should().BeTrue(
			because: "re-applying the same image converges to the same background state");
		attribute.OpenWorld.Should().BeFalse(because: "the tool only touches the addressed Creatio environment");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the single args wrapper as required at the MCP schema level, so a call that omits args fails with a structured error instead of an opaque binding failure.")]
	public void SetBackgroundImageTool_ShouldRequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] requiredAttributes = typeof(SetBackgroundImageTool)
			.GetMethod(nameof(SetBackgroundImageTool.SetBackgroundImage))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		requiredAttributes.Should().NotBeEmpty(
			because: "the args wrapper must be schema-required so an omitted args object fails with a structured error, not an opaque MCP binding failure");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the environment-scoped set-background-image command, forwards the image id, and returns a structured success result.")]
	public void SetBackgroundImage_ShouldResolveCommandAndReturnSuccess() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetBackgroundImageCommand defaultCommand = new();
		FakeSetBackgroundImageCommand resolvedCommand = new(SetBackgroundResult.Successful());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SetBackgroundImageCommand>(Arg.Any<SetBackgroundImageOptions>())
			.Returns(resolvedCommand);
		SetBackgroundImageTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		SetBackgroundImageResult result = tool.SetBackgroundImage(new SetBackgroundImageArgs(
			EnvironmentName: "docker_fix2", ImageId: ImageId.ToString()));

		// Assert
		result.Success.Should().BeTrue(because: "an applied background must report success");
		commandResolver.Received(1).Resolve<SetBackgroundImageCommand>(Arg.Is<SetBackgroundImageOptions>(options =>
			options.Environment == "docker_fix2" && options.ImageId == ImageId.ToString()));
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command instance should apply the background");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware tool path must use the resolved command instance, not the startup-time one");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured failure naming environment-name when the required environment name is omitted, without resolving a command.")]
	public void SetBackgroundImage_ShouldReturnFailure_WhenEnvironmentNameIsMissing() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetBackgroundImageCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SetBackgroundImageTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		SetBackgroundImageResult result = tool.SetBackgroundImage(
			new SetBackgroundImageArgs(ImageId: ImageId.ToString()));

		// Assert
		result.Success.Should().BeFalse(because: "a request without an environment name is invalid");
		result.Error.Should().Contain("environment-name",
			because: "the failure must name the exact kebab-case field the caller has to add");
		commandResolver.DidNotReceive().Resolve<SetBackgroundImageCommand>(Arg.Any<SetBackgroundImageOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured failure naming image-id when the required image id is omitted, without resolving a command.")]
	public void SetBackgroundImage_ShouldReturnFailure_WhenImageIdIsMissing() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetBackgroundImageCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SetBackgroundImageTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		SetBackgroundImageResult result = tool.SetBackgroundImage(
			new SetBackgroundImageArgs(EnvironmentName: "docker_fix2"));

		// Assert
		result.Success.Should().BeFalse(because: "a request without an image id has nothing to apply");
		result.Error.Should().Contain("image-id",
			because: "the failure must name the exact field the caller has to add");
		commandResolver.DidNotReceive().Resolve<SetBackgroundImageCommand>(Arg.Any<SetBackgroundImageOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces the command's failure message as a structured error result when applying the background fails.")]
	public void SetBackgroundImage_ShouldReturnFailure_WhenCommandReportsFailure() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetBackgroundImageCommand defaultCommand = new();
		FakeSetBackgroundImageCommand resolvedCommand = new(SetBackgroundResult.Failure(
			$"No uploaded image with id '{ImageId}' was found in the environment."));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SetBackgroundImageCommand>(Arg.Any<SetBackgroundImageOptions>())
			.Returns(resolvedCommand);
		SetBackgroundImageTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		SetBackgroundImageResult result = tool.SetBackgroundImage(new SetBackgroundImageArgs(
			EnvironmentName: "docker_fix2", ImageId: ImageId.ToString()));

		// Assert
		result.Success.Should().BeFalse(because: "a failed apply must not report success");
		result.Error.Should().Contain("No uploaded image",
			because: "the caller needs the actionable failure reason surfaced from the command");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Redacts sensitive tokens (a full request URI carrying the target host and embedded credentials) out of the command's failure message before it crosses into the MCP transcript, while keeping the human-readable reason intact.")]
	public void SetBackgroundImage_ShouldRedactSensitiveErrorText_WhenCommandFailsWithSensitiveMessage() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		const string sensitiveMessage =
			"Registering the image failed: POST https://admin:s3cr3t@stand.creatio.com/0/odata/SysImageInTag returned 500.";
		FakeSetBackgroundImageCommand defaultCommand = new();
		FakeSetBackgroundImageCommand resolvedCommand = new(SetBackgroundResult.Failure(sensitiveMessage));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SetBackgroundImageCommand>(Arg.Any<SetBackgroundImageOptions>())
			.Returns(resolvedCommand);
		SetBackgroundImageTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		SetBackgroundImageResult result = tool.SetBackgroundImage(new SetBackgroundImageArgs(
			EnvironmentName: "docker_fix2", ImageId: ImageId.ToString()));

		// Assert
		result.Success.Should().BeFalse(because: "a failed apply must not report success");
		result.Error.Should().NotContain("s3cr3t",
			because: "the credential embedded in the request URI must never reach the MCP transcript");
		result.Error.Should().NotContain("stand.creatio.com",
			because: "the target host must be scrubbed from the surfaced error");
		result.Error.Should().Contain("[redacted-uri]",
			because: "the sensitive URI must be replaced by the stable redaction placeholder");
		result.Error.Should().Contain("Registering the image failed",
			because: "the human-readable reason must survive redaction so the agent can self-correct");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeSetBackgroundImageCommand : SetBackgroundImageCommand {
		private readonly SetBackgroundResult _result;

		public SetBackgroundImageOptions CapturedOptions { get; private set; }

		public FakeSetBackgroundImageCommand(SetBackgroundResult result = null)
			: base(Substitute.For<IApplicationClient>(), new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>(), Substitute.For<ISysSettingsManager>()) {
			_result = result ?? SetBackgroundResult.Successful();
		}

		public override SetBackgroundResult SetBackground(SetBackgroundImageOptions options) {
			CapturedOptions = options;
			return _result;
		}
	}
}
