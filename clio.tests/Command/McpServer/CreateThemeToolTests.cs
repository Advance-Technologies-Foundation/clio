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
public class CreateThemeToolTests {

	[Test]
	[Category("Unit")]
	[Description("Declares the FR-12 safety flags on the create-theme tool method: a write that is not destructive, not idempotent, and closed-world.")]
	public void CreateThemeTool_ShouldDeclareCreateSafetyFlags_WhenInspectingMcpServerToolAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(CreateThemeTool)
			.GetMethod(nameof(CreateThemeTool.CreateTheme))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(CreateThemeTool.ToolName, because: "the tool must be published under its canonical kebab-case name");
		attribute.ReadOnly.Should().BeFalse(because: "creating a theme writes to the environment");
		attribute.Destructive.Should().BeFalse(because: "create adds a new theme without destroying existing state");
		attribute.Idempotent.Should().BeFalse(because: "a repeated create yields a different theme (new auto-id) rather than the same end state");
		attribute.OpenWorld.Should().BeFalse(because: "the tool only touches the addressed Creatio environment");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the single args wrapper as required at the MCP schema level, so a call that omits args fails with a structured error instead of an opaque binding failure.")]
	public void CreateThemeTool_ShouldRequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] requiredAttributes = typeof(CreateThemeTool)
			.GetMethod(nameof(CreateThemeTool.CreateTheme))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		requiredAttributes.Should().NotBeEmpty(
			because: "the args wrapper must be schema-required so an omitted args object fails with a structured error, not an opaque MCP binding failure");
	}

	[Test]
	[Description("Resolves the environment-name create-theme MCP tool, forwards the theme fields, and returns the created id as a structured success result.")]
	[Category("Unit")]
	public void CreateTheme_ShouldResolveCommandAndReturnCreatedId() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(createdId: "generated-id");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", CssClassName: "ocean-theme", CssContent: ".ocean-theme{}", Caption: "Ocean",
			Id: "explicit-id", PackageName: "UsrBranding"));

		// Assert
		result.Success.Should().BeTrue(because: "a created theme must report success");
		result.Id.Should().Be("generated-id", because: "the effective (possibly auto-generated) id must be surfaced for follow-up calls");
		commandResolver.Received(1).Resolve<CreateThemeCommand>(Arg.Is<CreateThemeOptions>(options =>
			options.Environment == "docker_fix2" &&
			options.Caption == "Ocean" &&
			options.CssClassName == "ocean-theme" &&
			options.CssContent == ".ocean-theme{}" &&
			options.Id == "explicit-id" &&
			options.PackageName == "UsrBranding"));
		resolvedCommand.CapturedOptions.Should().NotBeNull(because: "the resolved command instance should create the theme");
		defaultCommand.CapturedOptions.Should().BeNull(because: "the environment-aware tool path must use the resolved command instance");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure without resolving a command when the environment name is empty.")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnFailure_WhenEnvironmentNameIsEmpty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "   ", CssClassName: "ocean-theme", CssContent: ".ocean-theme{}", Caption: "Ocean"));

		// Assert
		result.Success.Should().BeFalse(because: "an empty environment name is an invalid request");
		result.Error.Should().Contain("environment-name",
			because: "the failure must name the exact kebab-case field the caller has to fix");
		commandResolver.DidNotReceive().Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure naming environment-name when the required environment name is omitted.")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnFailure_WhenEnvironmentNameIsMissing() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			CssClassName: "ocean-theme", CssContent: ".ocean-theme{}", Caption: "Ocean"));

		// Assert
		result.Success.Should().BeFalse(because: "a create request without an environment name is invalid");
		result.Error.Should().Contain("environment-name",
			because: "the failure must name the exact kebab-case field the caller has to add");
		commandResolver.DidNotReceive().Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Surfaces the command failure message as a structured failure when the resolved command reports failure.")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnFailure_WhenCommandReportsFailure() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(success: false, error: "id already exists");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", CssClassName: "ocean-theme", CssContent: ".ocean-theme{}", Caption: "Ocean"));

		// Assert
		result.Success.Should().BeFalse(because: "a command failure must surface as a tool failure");
		result.Error.Should().Contain("id already exists", because: "the server-provided message must be forwarded");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Redacts a sensitive ThemeService error body before it crosses into the MCP client transcript (review: b-horodyskyi — the TryCreateTheme errorMessage out-param bypassed ExecuteResolved's exception handling entirely).")]
	[Category("Unit")]
	public void CreateTheme_ShouldRedactSensitiveText_WhenCommandReportsFailure() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(success: false,
			error: "Unexpected response from server: https://internal-host.example/ThemeService?token=sekret123");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", CssClassName: "ocean-theme", CssContent: ".ocean-theme{}", Caption: "Ocean"));

		// Assert
		result.Success.Should().BeFalse(because: "a command failure must surface as a tool failure");
		result.Error.Should().NotContain("internal-host.example",
			because: "the server-provided errorMessage can carry a target host, so it must be redacted before crossing the MCP boundary");
		result.Error.Should().NotContain("sekret123",
			because: "the server-provided errorMessage can carry a credential value, so it must be redacted before crossing the MCP boundary");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure naming css-content when the required CSS payload is omitted.")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnFailure_WhenCssContentIsMissing() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", Caption: "Ocean"));

		// Assert
		result.Success.Should().BeFalse(because: "a create request without CSS content is invalid");
		result.Error.Should().Contain("css-content", because: "the failure must name the exact kebab-case field the caller has to add");
		commandResolver.DidNotReceive().Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure naming css-content when the CSS payload is explicitly empty.")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnFailure_WhenCssContentIsEmpty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", CssClassName: "ocean-theme", CssContent: "", Caption: "Ocean"));

		// Assert
		result.Success.Should().BeFalse(because: "an explicitly empty css-content is invalid");
		result.Error.Should().Contain("css-content", because: "the failure must name the exact kebab-case field the caller has to fix");
		commandResolver.DidNotReceive().Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns an actionable rename hint instead of silently ignoring a camelCase alias of a kebab-case argument.")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnRenameHint_WhenCamelCaseAliasIsPassed() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);
		CreateThemeArgs args = new(CssContent: ".ocean-theme{}") {
			ExtensionData = new Dictionary<string, JsonElement> {
				["environmentName"] = JsonSerializer.SerializeToElement("docker_fix2")
			}
		};

		// Act
		CreateThemeResult result = tool.CreateTheme(args);

		// Assert
		result.Success.Should().BeFalse(because: "a camelCase alias must be rejected, not silently dropped");
		result.Error.Should().Contain("'environmentName' -> 'environment-name'",
			because: "the failure must tell the caller the exact rename that fixes the call");
		commandResolver.DidNotReceive().Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Forwards a null caption when caption is omitted, leaving the command to derive it from cssClassName.")]
	[Category("Unit")]
	public void CreateTheme_Should_ForwardNullCaption_WhenCaptionOmitted() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(createdId: "ocean");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", CssClassName: "ocean-theme", CssContent: ".ocean-theme{}"));

		// Assert
		result.Success.Should().BeTrue(because: "caption is optional at the MCP surface");
		commandResolver.Received(1).Resolve<CreateThemeCommand>(Arg.Is<CreateThemeOptions>(options =>
			options.Caption == null && options.CssClassName == "ocean-theme"));
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Binds the create-theme argument record from kebab-case JSON using the real MCP serializer options, and routes camelCase spellings into the overflow bag — the exact JSON->record binding the MCP host performs, which direct method calls bypass.")]
	[Category("Unit")]
	public void CreateThemeArgs_ShouldBindKebabCaseAndRouteCamelCaseToExtensionData() {
		// Arrange
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();

		// Act
		CreateThemeArgs kebab = JsonSerializer.Deserialize<CreateThemeArgs>(
			"""{"environment-name":"docker_fix2","css-content":".ocean-theme{}","css-class-name":"ocean-theme","caption":"Ocean","id":"ocean","package-name":"UsrBranding"}""",
			options)!;
		CreateThemeArgs camel = JsonSerializer.Deserialize<CreateThemeArgs>(
			"""{"cssContent":".ocean-theme{}"}""", options)!;

		// Assert
		kebab.EnvironmentName.Should().Be("docker_fix2", because: "the advertised kebab-case environment-name field must bind");
		kebab.CssContent.Should().Be(".ocean-theme{}", because: "the advertised kebab-case css-content field must bind");
		kebab.CssClassName.Should().Be("ocean-theme", because: "the advertised kebab-case css-class-name field must bind");
		kebab.Caption.Should().Be("Ocean", because: "the advertised caption field must bind");
		kebab.Id.Should().Be("ocean", because: "the advertised id field must bind");
		kebab.PackageName.Should().Be("UsrBranding", because: "the advertised kebab-case package-name field must bind");
		(kebab.ExtensionData is null || kebab.ExtensionData.Count == 0).Should().BeTrue(
			because: "every kebab field binds to a declared parameter, so nothing overflows");
		camel.CssContent.Should().BeNull(
			because: "cssContent is not a declared wire name, so it must not bind");
		camel.ExtensionData.Should().ContainKey("cssContent",
			because: "the unbound camelCase spelling must land in the overflow bag so the tool can return a rename hint");
	}

	[Test]
	[Description("Returns a structured failure carrying the version-requirement message and never creates the theme when the target environment does not satisfy the Creatio version floor.")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnFailure_WhenCreatioVersionRequirementIsUnmet() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(createdId: "generated-id");
		ICreatioVersionChecker versionChecker = Substitute.For<ICreatioVersionChecker>();
		versionChecker
			.When(c => c.EnsureRequirements(Arg.Any<object>()))
			.Do(_ => throw new CreatioVersionRequirementException(
				"This command requires Creatio 10.0.0 or later. The target environment runs 8.1.5. Update Creatio and retry.",
				CreatioVersionRequirementException.VersionTooOldCode));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>()).Returns(versionChecker);
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", CssClassName: "ocean-theme", CssContent: ".ocean-theme{}", Caption: "Ocean"));

		// Assert
		result.Success.Should().BeFalse(
			because: "an unmet Creatio version requirement must refuse the create on the MCP surface exactly as the CLI gate does");
		result.Error.Should().Contain("requires Creatio 10.0.0 or later",
			because: "the version-requirement message must be surfaced to the MCP caller");
		result.Error.Should().Contain($"[{CreatioVersionRequirementException.VersionTooOldCode}]",
			because: "the typed result carries no exit code, so the stable machine-readable ErrorCode must travel in the error message");
		resolvedCommand.CapturedOptions.Should().BeNull(
			because: "the theme must never be created when the environment does not satisfy the version floor");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeCreateThemeCommand : CreateThemeCommand {
		private readonly bool _success;
		private readonly string _createdId;
		private readonly string _error;

		public CreateThemeOptions CapturedOptions { get; private set; }

		public FakeCreateThemeCommand(string createdId = "auto-id", bool success = true, string error = null)
			: base(
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>(),
				Substitute.For<IFileSystem>()) {
			_createdId = createdId;
			_success = success;
			_error = error;
		}

		public override bool TryCreateTheme(CreateThemeOptions options, out string createdId, out string errorMessage) {
			CapturedOptions = options;
			createdId = _success ? _createdId : null;
			errorMessage = _error;
			return _success;
		}
	}
}
