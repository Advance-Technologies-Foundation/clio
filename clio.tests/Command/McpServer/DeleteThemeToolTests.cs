using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.Theming;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class DeleteThemeToolTests {

	[Test]
	[Category("Unit")]
	[Description("Declares the FR-12 safety flags on the delete-theme tool method: a destructive, non-idempotent write that is closed-world.")]
	public void DeleteThemeTool_ShouldDeclareDeleteSafetyFlags_WhenInspectingMcpServerToolAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(DeleteThemeTool)
			.GetMethod(nameof(DeleteThemeTool.DeleteTheme))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(DeleteThemeTool.ToolName, because: "the tool must be published under its canonical kebab-case name");
		attribute.ReadOnly.Should().BeFalse(because: "deleting a theme writes to the environment");
		attribute.Destructive.Should().BeTrue(because: "delete removes an existing theme from the environment");
		attribute.Idempotent.Should().BeFalse(because: "deleting an unknown id is reported as a failure rather than the same end state");
		attribute.OpenWorld.Should().BeFalse(because: "the tool only touches the addressed Creatio environment");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the single args wrapper as required at the MCP schema level, so a call that omits args fails with a structured error instead of an opaque binding failure.")]
	public void DeleteThemeTool_ShouldRequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] requiredAttributes = typeof(DeleteThemeTool)
			.GetMethod(nameof(DeleteThemeTool.DeleteTheme))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		requiredAttributes.Should().NotBeEmpty(
			because: "the args wrapper must be schema-required so an omitted args object fails with a structured error, not an opaque MCP binding failure");
	}

	[Test]
	[Description("Resolves the delete-theme MCP tool for the requested environment and forwards the id.")]
	[Category("Unit")]
	public void DeleteTheme_ShouldResolveCommandForRequestedEnvironment() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeDeleteThemeCommand defaultCommand = new();
		FakeDeleteThemeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<DeleteThemeCommand>(Arg.Any<DeleteThemeOptions>()).Returns(resolvedCommand);
		DeleteThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.DeleteTheme(new DeleteThemeArgs(
			EnvironmentName: "docker_fix2", Id: "ocean-theme"));

		// Assert
		result.ExitCode.Should().Be(0, because: "the tool should forward a valid delete-theme payload");
		commandResolver.Received(1).Resolve<DeleteThemeCommand>(Arg.Is<DeleteThemeOptions>(options =>
			options.Environment == "docker_fix2" &&
			options.Id == "ocean-theme"));
		resolvedCommand.CapturedOptions.Should().NotBeNull(because: "the resolved command should delete the theme");
		defaultCommand.CapturedOptions.Should().BeNull(because: "the environment-aware tool path should use the resolved command instance");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure naming id without resolving a command when the id is empty.")]
	[Category("Unit")]
	public void DeleteTheme_ShouldReturnError_WhenIdIsEmpty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeDeleteThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		DeleteThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.DeleteTheme(new DeleteThemeArgs(
			EnvironmentName: "docker_fix2", Id: "  "));

		// Assert
		result.ExitCode.Should().Be(1, because: "an empty id is an expected, caller-actionable validation error");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, "id is required and cannot be empty."),
			because: "the failure must name the exact kebab-case field the caller has to fix");
		commandResolver.DidNotReceive().Resolve<DeleteThemeCommand>(Arg.Any<DeleteThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure naming environment-name when the required environment name is omitted.")]
	[Category("Unit")]
	public void DeleteTheme_ShouldReturnFailure_WhenEnvironmentNameIsMissing() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeDeleteThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		DeleteThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.DeleteTheme(new DeleteThemeArgs(Id: "ocean-theme"));

		// Assert
		result.ExitCode.Should().Be(1, because: "a missing environment name is an expected, caller-actionable validation error");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, "environment-name is required and cannot be empty."),
			because: "the failure must name the exact kebab-case field the caller has to add");
		commandResolver.DidNotReceive().Resolve<DeleteThemeCommand>(Arg.Any<DeleteThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure naming environment-name without resolving a command when the environment name is empty.")]
	[Category("Unit")]
	public void DeleteTheme_ShouldReturnFailure_WhenEnvironmentNameIsEmpty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeDeleteThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		DeleteThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.DeleteTheme(new DeleteThemeArgs(
			EnvironmentName: "   ", Id: "ocean-theme"));

		// Assert
		result.ExitCode.Should().Be(1, because: "an empty environment name is an expected, caller-actionable validation error");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, "environment-name is required and cannot be empty."),
			because: "the failure must name the exact kebab-case field the caller has to fix");
		commandResolver.DidNotReceive().Resolve<DeleteThemeCommand>(Arg.Any<DeleteThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns an actionable rename hint instead of silently ignoring a camelCase alias of a kebab-case argument.")]
	[Category("Unit")]
	public void DeleteTheme_ShouldReturnRenameHint_WhenCamelCaseAliasIsPassed() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeDeleteThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		DeleteThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);
		DeleteThemeArgs args = new(Id: "ocean-theme") {
			ExtensionData = new Dictionary<string, JsonElement> {
				["environmentName"] = JsonSerializer.SerializeToElement("docker_fix2")
			}
		};

		// Act
		CommandExecutionResult result = tool.DeleteTheme(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "a rejected alias is an expected, caller-actionable validation error");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			message.Value.ToString().Contains("'environmentName' -> 'environment-name'"),
			because: "the failure must tell the caller the exact rename that fixes the call");
		commandResolver.DidNotReceive().Resolve<DeleteThemeCommand>(Arg.Any<DeleteThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Binds the delete-theme argument record from kebab-case JSON using the real MCP serializer options, and routes camelCase spellings into the overflow bag — the exact JSON->record binding the MCP host performs, which direct method calls bypass.")]
	[Category("Unit")]
	public void DeleteThemeArgs_ShouldBindKebabCaseAndRouteCamelCaseToExtensionData() {
		// Arrange
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();

		// Act
		DeleteThemeArgs kebab = JsonSerializer.Deserialize<DeleteThemeArgs>(
			"""{"environment-name":"docker_fix2","id":"ocean-theme"}""", options)!;
		DeleteThemeArgs camel = JsonSerializer.Deserialize<DeleteThemeArgs>(
			"""{"environmentName":"docker_fix2"}""", options)!;

		// Assert
		kebab.EnvironmentName.Should().Be("docker_fix2", because: "the advertised kebab-case environment-name field must bind");
		kebab.Id.Should().Be("ocean-theme", because: "the advertised id field must bind");
		(kebab.ExtensionData is null || kebab.ExtensionData.Count == 0).Should().BeTrue(
			because: "every kebab field binds to a declared parameter, so nothing overflows");
		camel.EnvironmentName.Should().BeNull(
			because: "environmentName is not a declared wire name, so it must not bind");
		camel.ExtensionData.Should().ContainKey("environmentName",
			because: "the unbound camelCase spelling must land in the overflow bag so the tool can return a rename hint");
	}

	private sealed class FakeDeleteThemeCommand : DeleteThemeCommand {
		public DeleteThemeOptions CapturedOptions { get; private set; }

		public FakeDeleteThemeCommand()
			: base(
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>()) {
		}

		public override int Execute(DeleteThemeOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
