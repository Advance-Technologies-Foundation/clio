using System.IO;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class NewThemeToolTests {

	private string _workspaceDirectory;

	[SetUp]
	public void SetUp() {
		_workspaceDirectory = Path.Combine(Path.GetTempPath(), "clio-theme-tool-tests-" + Path.GetRandomFileName());
		Directory.CreateDirectory(Path.Combine(_workspaceDirectory, ".clio"));
		File.WriteAllText(Path.Combine(_workspaceDirectory, ".clio", "workspaceSettings.json"), "{}");
	}

	[TearDown]
	public void TearDown() {
		if (!string.IsNullOrEmpty(_workspaceDirectory) && Directory.Exists(_workspaceDirectory)) {
			Directory.Delete(_workspaceDirectory, recursive: true);
		}
	}

	[Test]
	[Description("Maps the new-theme MCP arguments into new-theme command options and resolves the command without startup-time environment registration.")]
	[Category("Unit")]
	public void NewTheme_Should_Map_All_Arguments() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeNewThemeCommand command = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.ResolveWithoutEnvironment<NewThemeCommand>(Arg.Any<NewThemeOptions>())
			.Returns(command);
		NewThemeTool tool = new(ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.NewTheme(
			new NewThemeArgs(
				WorkspaceDirectory: _workspaceDirectory,
				CssClassName: "acme-dark-theme",
				PackageName: "UsrThemes",
				Caption: "Acme Dark",
				Id: "AcmeDark"));

		// Assert
		result.ExitCode.Should().Be(0, because: "the MCP tool should forward a valid new-theme payload");
		command.CapturedOptions.Should().NotBeNull(because: "the resolved command should receive the mapped options");
		command.CapturedOptions!.CssClassName.Should().Be("acme-dark-theme");
		command.CapturedOptions.PackageName.Should().Be("UsrThemes");
		command.CapturedOptions.Caption.Should().Be("Acme Dark");
		command.CapturedOptions.Id.Should().Be("AcmeDark");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Leaves optional new-theme caption and id unset so the command derives them.")]
	[Category("Unit")]
	public void NewTheme_Should_Default_Optional_Arguments() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeNewThemeCommand command = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.ResolveWithoutEnvironment<NewThemeCommand>(Arg.Any<NewThemeOptions>())
			.Returns(command);
		NewThemeTool tool = new(ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.NewTheme(
			new NewThemeArgs(
				WorkspaceDirectory: _workspaceDirectory,
				CssClassName: "acme-dark-theme",
				PackageName: "UsrThemes"));

		// Assert
		result.ExitCode.Should().Be(0);
		command.CapturedOptions.Should().NotBeNull();
		command.CapturedOptions!.Caption.Should().BeNull(because: "an omitted caption is derived by the command, not the tool");
		command.CapturedOptions.Id.Should().BeNull(because: "an omitted id defaults to a UUID inside the command");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Pins the process working directory to the requested workspace while the command runs, then restores it.")]
	[Category("Unit")]
	public void NewTheme_Should_Switch_Working_Directory_For_The_Command() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string previousCwd = Directory.GetCurrentDirectory();
		string observedCwd = null;
		FakeNewThemeCommand command = new(_ => observedCwd = Directory.GetCurrentDirectory());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.ResolveWithoutEnvironment<NewThemeCommand>(Arg.Any<NewThemeOptions>())
			.Returns(command);
		NewThemeTool tool = new(ConsoleLogger.Instance, commandResolver);

		try {
			// Act
			tool.NewTheme(
				new NewThemeArgs(
					WorkspaceDirectory: _workspaceDirectory,
					CssClassName: "acme-dark-theme",
					PackageName: "UsrThemes"));

			// Assert
			Path.GetFullPath(observedCwd!).Should().Be(Path.GetFullPath(_workspaceDirectory),
				because: "the tool must pin cwd to the supplied workspace so the theme is scaffolded under it");
			Directory.GetCurrentDirectory().Should().Be(previousCwd,
				because: "the tool must restore the previous working directory after the command completes");
		} finally {
			Directory.SetCurrentDirectory(previousCwd);
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Description("Rejects new-theme calls when the workspace directory is missing.")]
	[Category("Unit")]
	public void NewTheme_Should_Reject_Missing_Workspace_Directory() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		NewThemeTool tool = new(ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.NewTheme(
			new NewThemeArgs(
				WorkspaceDirectory: "",
				CssClassName: "acme-dark-theme",
				PackageName: "UsrThemes"));

		// Assert
		result.ExitCode.Should().Be(1);
		result.Output.Should().ContainSingle()
			.Which.Value.ToString().Should().Contain("workspaceDirectory is required");
		commandResolver.DidNotReceive().ResolveWithoutEnvironment<NewThemeCommand>(Arg.Any<EnvironmentOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Rejects new-theme calls when the workspace directory exists but is not a clio workspace.")]
	[Category("Unit")]
	public void NewTheme_Should_Reject_Non_Workspace_Directory() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string nonWorkspace = Path.Combine(Path.GetTempPath(), "clio-theme-tool-not-a-ws-" + Path.GetRandomFileName());
		Directory.CreateDirectory(nonWorkspace);
		try {
			IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
			NewThemeTool tool = new(ConsoleLogger.Instance, commandResolver);

			// Act
			CommandExecutionResult result = tool.NewTheme(
				new NewThemeArgs(
					WorkspaceDirectory: nonWorkspace,
					CssClassName: "acme-dark-theme",
					PackageName: "UsrThemes"));

			// Assert
			result.ExitCode.Should().Be(1);
			result.Output.Should().ContainSingle()
				.Which.Value.ToString().Should().Contain("not a clio workspace");
			commandResolver.DidNotReceive().ResolveWithoutEnvironment<NewThemeCommand>(Arg.Any<EnvironmentOptions>());
		} finally {
			Directory.Delete(nonWorkspace, recursive: true);
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[TestCase("../escape")]
	[TestCase("packages/sub")]
	[TestCase("C:\\absolute")]
	[Description("Rejects packageName values that contain path separators, parent-directory references, or absolute paths so scaffolding cannot escape the workspace.")]
	[Category("Unit")]
	public void NewTheme_Should_Reject_PackageName_With_Path_Characters(string packageName) {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		NewThemeTool tool = new(ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.NewTheme(
			new NewThemeArgs(
				WorkspaceDirectory: _workspaceDirectory,
				CssClassName: "acme-dark-theme",
				PackageName: packageName));

		// Assert
		result.ExitCode.Should().Be(1);
		result.Output.Should().ContainSingle()
			.Which.Value.ToString().Should().Contain("packageName");
		commandResolver.DidNotReceive().ResolveWithoutEnvironment<NewThemeCommand>(Arg.Any<EnvironmentOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Exposes the supported new-theme arguments and requires the structured args payload.")]
	[Category("Unit")]
	public void NewTheme_Should_Expose_Required_Args_Payload() {
		// Arrange
		System.Reflection.ParameterInfo argsParameter = typeof(NewThemeTool)
			.GetMethod(nameof(NewThemeTool.NewTheme))!
			.GetParameters()
			.Single(parameter => parameter.Name == "args");

		// Act
		object[] requiredAttributes = argsParameter.GetCustomAttributes(
			typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), inherit: false);

		// Assert
		requiredAttributes.Should().ContainSingle(
			because: "the new-theme MCP tool should require its structured args payload");
		typeof(NewThemeArgs).GetProperties().Select(property => property.Name).Should().BeEquivalentTo(
			["WorkspaceDirectory", "CssClassName", "PackageName", "Caption", "Id"],
			because: "the new-theme MCP payload should only expose the supported theme arguments");
	}

	private sealed class FakeNewThemeCommand : NewThemeCommand {
		private readonly System.Action<NewThemeOptions> _onExecute;
		public NewThemeOptions CapturedOptions { get; private set; }

		public FakeNewThemeCommand(System.Action<NewThemeOptions> onExecute = null)
			: base(
				Substitute.For<IThemeCreator>(),
				Substitute.For<IValidator<NewThemeOptions>>(),
				ConsoleLogger.Instance) {
			_onExecute = onExecute;
		}

		public override int Execute(NewThemeOptions options) {
			CapturedOptions = options;
			_onExecute?.Invoke(options);
			return 0;
		}
	}
}
