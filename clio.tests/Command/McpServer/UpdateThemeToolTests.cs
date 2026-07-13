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
public class UpdateThemeToolTests {

	[Test]
	[Category("Unit")]
	[Description("Declares the FR-12 safety flags on the update-theme tool method: a destructive, idempotent write (full overwrite by id), and closed-world.")]
	public void UpdateThemeTool_ShouldDeclareUpdateSafetyFlags_WhenInspectingMcpServerToolAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(UpdateThemeTool)
			.GetMethod(nameof(UpdateThemeTool.UpdateTheme))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(UpdateThemeTool.ToolName, because: "the tool must be published under its canonical kebab-case name");
		attribute.ReadOnly.Should().BeFalse(because: "updating a theme writes to the environment");
		attribute.Destructive.Should().BeTrue(because: "a full overwrite by id destroys the theme's previous content, so confirmation-seeking MCP clients must prompt");
		attribute.Idempotent.Should().BeTrue(because: "a full overwrite by id reaches the same end state when repeated");
		attribute.OpenWorld.Should().BeFalse(because: "the tool only touches the addressed Creatio environment");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the single args wrapper as required at the MCP schema level, so a call that omits args fails with a structured error instead of an opaque binding failure.")]
	public void UpdateThemeTool_ShouldRequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] requiredAttributes = typeof(UpdateThemeTool)
			.GetMethod(nameof(UpdateThemeTool.UpdateTheme))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		requiredAttributes.Should().NotBeEmpty(
			because: "the args wrapper must be schema-required so an omitted args object fails with a structured error, not an opaque MCP binding failure");
	}

	[Test]
	[Description("Resolves the update-theme MCP tool for the requested environment and forwards the full-overwrite payload.")]
	[Category("Unit")]
	public void UpdateTheme_ShouldResolveCommandForRequestedEnvironment() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeUpdateThemeCommand defaultCommand = new();
		FakeUpdateThemeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateThemeCommand>(Arg.Any<UpdateThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		UpdateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.UpdateTheme(new UpdateThemeArgs(
			EnvironmentName: "docker_fix2", Id: "ocean-theme", Caption: "Ocean",
			CssClassName: "ocean-theme", CssContent: ".ocean-theme{}"));

		// Assert
		result.ExitCode.Should().Be(0, because: "the tool should forward a valid update-theme payload");
		commandResolver.Received(1).Resolve<UpdateThemeCommand>(Arg.Is<UpdateThemeOptions>(options =>
			options.Environment == "docker_fix2" &&
			options.Id == "ocean-theme" &&
			options.Caption == "Ocean" &&
			options.CssClassName == "ocean-theme" &&
			options.CssContent == ".ocean-theme{}"));
		resolvedCommand.CapturedOptions.Should().NotBeNull(because: "the resolved command should overwrite the theme");
		defaultCommand.CapturedOptions.Should().BeNull(because: "the environment-aware tool path should use the resolved command instance");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure naming id without resolving a command when the id is empty.")]
	[Category("Unit")]
	public void UpdateTheme_ShouldReturnError_WhenIdIsEmpty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeUpdateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		UpdateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.UpdateTheme(new UpdateThemeArgs(
			EnvironmentName: "docker_fix2", Id: "   ", Caption: "Ocean",
			CssClassName: "ocean-theme", CssContent: ".ocean-theme{}"));

		// Assert
		result.ExitCode.Should().Be(1, because: "an empty id is an expected, caller-actionable validation error");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, "id is required and cannot be empty."),
			because: "the failure must name the exact kebab-case field the caller has to fix");
		commandResolver.DidNotReceive().Resolve<UpdateThemeCommand>(Arg.Any<UpdateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure naming environment-name without resolving a command when the required environment name is omitted.")]
	[Category("Unit")]
	public void UpdateTheme_ShouldReturnFailure_WhenEnvironmentNameIsMissing() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeUpdateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		UpdateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.UpdateTheme(new UpdateThemeArgs(
			Id: "ocean-theme", Caption: "Ocean", CssClassName: "ocean-theme", CssContent: ".ocean-theme{}"));

		// Assert
		result.ExitCode.Should().Be(1, because: "a missing environment name is an expected, caller-actionable validation error");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, "environment-name is required and cannot be empty."),
			because: "the failure must name the exact kebab-case field the caller has to add");
		commandResolver.DidNotReceive().Resolve<UpdateThemeCommand>(Arg.Any<UpdateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure naming environment-name without resolving a command when the environment name is empty.")]
	[Category("Unit")]
	public void UpdateTheme_ShouldReturnFailure_WhenEnvironmentNameIsEmpty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeUpdateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		UpdateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.UpdateTheme(new UpdateThemeArgs(
			EnvironmentName: "   ", Id: "ocean-theme", Caption: "Ocean",
			CssClassName: "ocean-theme", CssContent: ".ocean-theme{}"));

		// Assert
		result.ExitCode.Should().Be(1, because: "an empty environment name is an expected, caller-actionable validation error");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, "environment-name is required and cannot be empty."),
			because: "the failure must name the exact kebab-case field the caller has to fix");
		commandResolver.DidNotReceive().Resolve<UpdateThemeCommand>(Arg.Any<UpdateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure naming caption without resolving a command when the required caption is omitted.")]
	[Category("Unit")]
	public void UpdateTheme_ShouldReturnFailure_WhenCaptionIsMissing() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeUpdateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		UpdateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.UpdateTheme(new UpdateThemeArgs(
			EnvironmentName: "docker_fix2", Id: "ocean-theme", CssClassName: "ocean-theme", CssContent: ".ocean-theme{}"));

		// Assert
		result.ExitCode.Should().Be(1, because: "a missing caption is an expected, caller-actionable validation error");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, "caption is required and cannot be empty."),
			because: "the failure must name the exact kebab-case field the caller has to add");
		commandResolver.DidNotReceive().Resolve<UpdateThemeCommand>(Arg.Any<UpdateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure naming css-class-name without resolving a command when the required CSS class name is omitted.")]
	[Category("Unit")]
	public void UpdateTheme_ShouldReturnFailure_WhenCssClassNameIsMissing() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeUpdateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		UpdateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.UpdateTheme(new UpdateThemeArgs(
			EnvironmentName: "docker_fix2", Id: "ocean-theme", Caption: "Ocean", CssContent: ".ocean-theme{}"));

		// Assert
		result.ExitCode.Should().Be(1, because: "a missing CSS class name is an expected, caller-actionable validation error");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, "css-class-name is required and cannot be empty."),
			because: "the failure must name the exact kebab-case field the caller has to add");
		commandResolver.DidNotReceive().Resolve<UpdateThemeCommand>(Arg.Any<UpdateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure naming css-content when the required CSS payload is omitted.")]
	[Category("Unit")]
	public void UpdateTheme_ShouldReturnFailure_WhenCssContentIsMissing() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeUpdateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		UpdateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.UpdateTheme(new UpdateThemeArgs(
			EnvironmentName: "docker_fix2", Id: "ocean-theme", Caption: "Ocean",
			CssClassName: "ocean-theme"));

		// Assert
		result.ExitCode.Should().Be(1, because: "a missing CSS payload is an expected, caller-actionable validation error");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, "css-content is required and cannot be empty."),
			because: "the failure must name the exact kebab-case field the caller has to add");
		commandResolver.DidNotReceive().Resolve<UpdateThemeCommand>(Arg.Any<UpdateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns an actionable rename hint instead of silently ignoring a camelCase alias of a kebab-case argument.")]
	[Category("Unit")]
	public void UpdateTheme_ShouldReturnRenameHint_WhenCamelCaseAliasIsPassed() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeUpdateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		UpdateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);
		UpdateThemeArgs args = new(Id: "ocean-theme", Caption: "Ocean",
			CssClassName: "ocean-theme", CssContent: ".ocean-theme{}") {
			ExtensionData = new Dictionary<string, JsonElement> {
				["environmentName"] = JsonSerializer.SerializeToElement("docker_fix2")
			}
		};

		// Act
		CommandExecutionResult result = tool.UpdateTheme(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "a rejected alias is an expected, caller-actionable validation error");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			message.Value.ToString().Contains("'environmentName' -> 'environment-name'"),
			because: "the failure must tell the caller the exact rename that fixes the call");
		commandResolver.DidNotReceive().Resolve<UpdateThemeCommand>(Arg.Any<UpdateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Binds the update-theme argument record from kebab-case JSON using the real MCP serializer options, and routes camelCase spellings into the overflow bag — the exact JSON->record binding the MCP host performs, which direct method calls bypass.")]
	[Category("Unit")]
	public void UpdateThemeArgs_ShouldBindKebabCaseAndRouteCamelCaseToExtensionData() {
		// Arrange
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();

		// Act
		UpdateThemeArgs kebab = JsonSerializer.Deserialize<UpdateThemeArgs>(
			"""{"environment-name":"docker_fix2","id":"ocean-theme","caption":"Ocean","css-class-name":"ocean-theme","css-content":".ocean-theme{}"}""",
			options)!;
		UpdateThemeArgs camel = JsonSerializer.Deserialize<UpdateThemeArgs>(
			"""{"cssClassName":"ocean-theme"}""", options)!;

		// Assert
		kebab.EnvironmentName.Should().Be("docker_fix2", because: "the advertised kebab-case environment-name field must bind");
		kebab.Id.Should().Be("ocean-theme", because: "the advertised id field must bind");
		kebab.Caption.Should().Be("Ocean", because: "the advertised caption field must bind");
		kebab.CssClassName.Should().Be("ocean-theme", because: "the advertised kebab-case css-class-name field must bind");
		kebab.CssContent.Should().Be(".ocean-theme{}", because: "the advertised kebab-case css-content field must bind");
		(kebab.ExtensionData is null || kebab.ExtensionData.Count == 0).Should().BeTrue(
			because: "every kebab field binds to a declared parameter, so nothing overflows");
		camel.CssClassName.Should().BeNull(
			because: "cssClassName is not a declared wire name, so it must not bind");
		camel.ExtensionData.Should().ContainKey("cssClassName",
			because: "the unbound camelCase spelling must land in the overflow bag so the tool can return a rename hint");
	}

	[Test]
	[Description("Returns the distinct version exit code with the version-requirement message and never overwrites the theme when the target environment does not satisfy the Creatio version floor.")]
	[Category("Unit")]
	public void UpdateTheme_ShouldReturnVersionExitCode_WhenCreatioVersionRequirementIsUnmet() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeUpdateThemeCommand defaultCommand = new();
		FakeUpdateThemeCommand resolvedCommand = new();
		ICreatioVersionChecker versionChecker = Substitute.For<ICreatioVersionChecker>();
		versionChecker
			.When(c => c.EnsureRequirements(Arg.Any<object>()))
			.Do(_ => throw new CreatioVersionRequirementException(
				"This command requires Creatio 10.0.0 or later. The target environment runs 8.1.5. Update Creatio and retry.",
				CreatioVersionRequirementException.VersionTooOldCode));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateThemeCommand>(Arg.Any<UpdateThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>()).Returns(versionChecker);
		UpdateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.UpdateTheme(new UpdateThemeArgs(
			EnvironmentName: "docker_fix2", Id: "ocean-theme", Caption: "Ocean",
			CssClassName: "ocean-theme", CssContent: ".ocean-theme{}"));
		string[] messageValues = result.Output.Select(message => message.Value?.ToString() ?? string.Empty).ToArray();

		// Assert
		result.ExitCode.Should().Be(Clio.Program.CreatioVersionRequirementExitCode,
			because: "an unmet Creatio version requirement must refuse the overwrite with the distinct version exit code, mirroring the CLI gate");
		messageValues.Should().Contain(value => value.Contains("requires Creatio 10.0.0 or later"),
			because: "the version-requirement message must be surfaced to the MCP caller");
		resolvedCommand.CapturedOptions.Should().BeNull(
			because: "the theme must never be overwritten when the environment does not satisfy the version floor");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeUpdateThemeCommand : UpdateThemeCommand {
		public UpdateThemeOptions CapturedOptions { get; private set; }

		public FakeUpdateThemeCommand()
			: base(
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>(),
				Substitute.For<IFileSystem>()) {
		}

		public override int Execute(UpdateThemeOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
