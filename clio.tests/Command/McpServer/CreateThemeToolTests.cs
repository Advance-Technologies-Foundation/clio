using System.Linq;
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
	[TestCase(nameof(CreateThemeTool.CreateThemeByName))]
	[TestCase(nameof(CreateThemeTool.CreateThemeByCredentials))]
	[Description("Declares the FR-12 safety flags on both create-theme tool methods: a write that is not destructive, not idempotent, and closed-world.")]
	public void CreateThemeTool_Should_DeclareCreateSafetyFlags_WhenInspectingMcpServerToolAttribute(string methodName) {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(CreateThemeTool)
			.GetMethod(methodName)!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.ReadOnly.Should().BeFalse(because: "creating a theme writes to the environment");
		attribute.Destructive.Should().BeFalse(because: "create adds a new theme without destroying existing state");
		attribute.Idempotent.Should().BeFalse(because: "a repeated create yields a different theme (new auto-id) rather than the same end state");
		attribute.OpenWorld.Should().BeFalse(because: "the tool only touches the addressed Creatio environment");
	}

	[Test]
	[Description("Resolves the environment-name create-theme MCP tool, forwards the theme fields, and returns the created id as a structured success result.")]
	[Category("Unit")]
	public void CreateThemeByEnvironment_Should_Resolve_Command_And_Return_CreatedId() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(createdId: "generated-id");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateThemeByName("docker_fix2", cssClassName: "ocean-theme", cssContent: ".ocean-theme{}", caption: "Ocean");

		// Assert
		result.Success.Should().BeTrue(because: "a created theme must report success");
		result.Id.Should().Be("generated-id", because: "the effective (possibly auto-generated) id must be surfaced for follow-up calls");
		commandResolver.Received(1).Resolve<CreateThemeCommand>(Arg.Is<CreateThemeOptions>(options =>
			options.Environment == "docker_fix2" &&
			options.Caption == "Ocean" &&
			options.CssClassName == "ocean-theme" &&
			options.CssContent == ".ocean-theme{}"));
		resolvedCommand.CapturedOptions.Should().NotBeNull(because: "the resolved command instance should create the theme");
		defaultCommand.CapturedOptions.Should().BeNull(because: "the environment-aware tool path must use the resolved command instance");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure without resolving a command when the environment name is empty.")]
	[Category("Unit")]
	public void CreateThemeByEnvironment_Should_Return_Failure_When_Environment_Name_Is_Empty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateThemeByName("   ", cssClassName: "ocean-theme", cssContent: ".ocean-theme{}", caption: "Ocean");

		// Assert
		result.Success.Should().BeFalse(because: "an empty environment name is an invalid request");
		result.Error.Should().NotBeNullOrWhiteSpace(because: "the failure must carry a diagnostic");
		commandResolver.DidNotReceive().Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Surfaces the command failure message as a structured failure when the resolved command reports failure.")]
	[Category("Unit")]
	public void CreateThemeByEnvironment_Should_Return_Failure_When_Command_Reports_Failure() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(success: false, error: "id already exists");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateThemeByName("docker_fix2", cssClassName: "ocean-theme", cssContent: ".ocean-theme{}", caption: "Ocean");

		// Assert
		result.Success.Should().BeFalse(because: "a command failure must surface as a tool failure");
		result.Error.Should().Contain("id already exists", because: "the server-provided message must be forwarded");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Resolves the credentials create-theme MCP tool, forwards the credentials and optional id/package, and preserves the default false isNetCore.")]
	[Category("Unit")]
	public void CreateThemeByCredentials_Should_Forward_Payload_And_Default_IsNetCore() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(createdId: "explicit-id");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateThemeByCredentials(
			"http://localhost:5000", "Supervisor", "Supervisor",
			cssClassName: "ocean-theme", cssContent: ".ocean-theme{}", caption: "Ocean",
			id: "explicit-id", packageName: "UsrBranding");

		// Assert
		result.Success.Should().BeTrue(because: "the credentials tool should forward a valid create-theme payload");
		commandResolver.Received(1).Resolve<CreateThemeCommand>(Arg.Is<CreateThemeOptions>(options =>
			options.Uri == "http://localhost:5000" &&
			options.Login == "Supervisor" &&
			options.Password == "Supervisor" &&
			options.IsNetCore == false &&
			options.Id == "explicit-id" &&
			options.PackageName == "UsrBranding"));
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure carrying the missing-field message when a required credential argument is empty.")]
	[Category("Unit")]
	public void CreateThemeByCredentials_Should_Return_Failure_When_Url_Is_Empty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateThemeByCredentials("  ", "Supervisor", "Supervisor",
			cssClassName: "ocean-theme", cssContent: ".ocean-theme{}", caption: "Ocean");

		// Assert
		result.Success.Should().BeFalse(because: "an empty url is an invalid request and must not resolve a command");
		result.Error.Should().Contain("url", because: "the failure must point at the missing field");
		commandResolver.DidNotReceive().Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Forwards a null caption when caption is omitted, leaving the command to derive it from cssClassName.")]
	[Category("Unit")]
	public void CreateThemeByEnvironment_Should_ForwardNullCaption_WhenCaptionOmitted() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(createdId: "ocean");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateThemeByName("docker_fix2", cssClassName: "ocean-theme", cssContent: ".ocean-theme{}");

		// Assert
		result.Success.Should().BeTrue(because: "caption is optional at the MCP surface");
		commandResolver.Received(1).Resolve<CreateThemeCommand>(Arg.Is<CreateThemeOptions>(options =>
			options.Caption == null && options.CssClassName == "ocean-theme"));
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
				Substitute.For<IServiceUrlBuilder>()) {
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
