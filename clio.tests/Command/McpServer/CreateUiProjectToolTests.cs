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
public class CreateUiProjectToolTests {

	private string _workspaceDirectory;

	[SetUp]
	public void SetUp() {
		_workspaceDirectory = Path.Combine(Path.GetTempPath(), "clio-ui-tool-tests-" + Path.GetRandomFileName());
		Directory.CreateDirectory(Path.Combine(_workspaceDirectory, ".clio"));
		File.WriteAllText(Path.Combine(_workspaceDirectory, ".clio", "workspaceSettings.json"), "{}");
		// Resolve any symlinks (e.g. /var → /private/var on macOS) so path comparisons
		// that use Directory.GetCurrentDirectory() are stable across platforms.
		string savedCwd = Directory.GetCurrentDirectory();
		Directory.SetCurrentDirectory(_workspaceDirectory);
		_workspaceDirectory = Directory.GetCurrentDirectory();
		Directory.SetCurrentDirectory(savedCwd);
	}

	[TearDown]
	public void TearDown() {
		if (!string.IsNullOrEmpty(_workspaceDirectory) && Directory.Exists(_workspaceDirectory)) {
			Directory.Delete(_workspaceDirectory, recursive: true);
		}
	}

	[Test]
	[Description("Maps the new-ui-project MCP arguments into create-ui-project command options and resolves the command without startup-time environment registration.")]
	[Category("Unit")]
	public void CreateUiProject_Should_Map_All_Arguments() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateUiProjectCommand command = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.ResolveWithoutEnvironment<CreateUiProjectCommand>(Arg.Any<CreateUiProjectOptions>())
			.Returns(command);
		CreateUiProjectTool tool = new(ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateUiProject(
			new CreateUiProjectArgs(
				WorkspaceDirectory: _workspaceDirectory,
				ProjectName: "my_module",
				PackageName: "UsrCustomPkg",
				VendorPrefix: "usr",
				Empty: true,
				CreatioVersion: "8.1.2"));

		// Assert
		result.ExitCode.Should().Be(0, because: "the MCP tool should forward a valid new-ui-project payload");
		commandResolver.Received(1).ResolveWithoutEnvironment<CreateUiProjectCommand>(
			Arg.Is<EnvironmentOptions>(options =>
				options.GetType() == typeof(CreateUiProjectOptions)
				&& ((CreateUiProjectOptions)options).ProjectName == "my_module"
				&& ((CreateUiProjectOptions)options).PackageName == "UsrCustomPkg"
				&& ((CreateUiProjectOptions)options).VendorPrefix == "usr"
				&& ((CreateUiProjectOptions)options).IsEmpty
				&& ((CreateUiProjectOptions)options).CreatioVersion == "8.1.2"
				&& ((CreateUiProjectOptions)options).IsSilent));
		command.CapturedOptions.Should().NotBeNull(because: "the resolved command should receive the mapped options");
		command.CapturedOptions!.ProjectName.Should().Be("my_module");
		command.CapturedOptions.PackageName.Should().Be("UsrCustomPkg");
		command.CapturedOptions.VendorPrefix.Should().Be("usr");
		command.CapturedOptions.IsEmpty.Should().BeTrue();
		command.CapturedOptions.CreatioVersion.Should().Be("8.1.2");
		command.CapturedOptions.IsSilent.Should().BeTrue(
			because: "the MCP tool must bypass the interactive 'download package?' prompt");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Defaults optional new-ui-project MCP arguments so callers only need to supply the required identity fields.")]
	[Category("Unit")]
	public void CreateUiProject_Should_Default_Optional_Arguments() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateUiProjectCommand command = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.ResolveWithoutEnvironment<CreateUiProjectCommand>(Arg.Any<CreateUiProjectOptions>())
			.Returns(command);
		CreateUiProjectTool tool = new(ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateUiProject(
			new CreateUiProjectArgs(
				WorkspaceDirectory: _workspaceDirectory,
				ProjectName: "my_module",
				PackageName: "UsrCustomPkg",
				VendorPrefix: "usr"));

		// Assert
		result.ExitCode.Should().Be(0);
		command.CapturedOptions.Should().NotBeNull();
		command.CapturedOptions!.IsEmpty.Should().BeFalse();
		command.CapturedOptions.CreatioVersion.Should().BeEmpty();
		command.CapturedOptions.IsSilent.Should().BeTrue();
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Pins the process working directory to the requested workspace while the command runs, then restores it.")]
	[Category("Unit")]
	public void CreateUiProject_Should_Switch_Working_Directory_For_The_Command() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string previousCwd = Directory.GetCurrentDirectory();
		string observedCwd = null;
		FakeCreateUiProjectCommand command = new(options => observedCwd = Directory.GetCurrentDirectory());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.ResolveWithoutEnvironment<CreateUiProjectCommand>(Arg.Any<CreateUiProjectOptions>())
			.Returns(command);
		CreateUiProjectTool tool = new(ConsoleLogger.Instance, commandResolver);

		try {
			// Act
			tool.CreateUiProject(
				new CreateUiProjectArgs(
					WorkspaceDirectory: _workspaceDirectory,
					ProjectName: "my_module",
					PackageName: "UsrCustomPkg",
					VendorPrefix: "usr"));

			// Assert
			Path.GetFullPath(observedCwd!).Should().Be(Path.GetFullPath(_workspaceDirectory),
				because: "the tool must pin cwd to the supplied workspace so packages/projects are scaffolded under it");
			Directory.GetCurrentDirectory().Should().Be(previousCwd,
				because: "the tool must restore the previous working directory after the command completes");
		} finally {
			Directory.SetCurrentDirectory(previousCwd);
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Description("Rejects new-ui-project calls when the workspace directory is missing.")]
	[Category("Unit")]
	public void CreateUiProject_Should_Reject_Missing_Workspace_Directory() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateUiProjectTool tool = new(ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateUiProject(
			new CreateUiProjectArgs(
				WorkspaceDirectory: "",
				ProjectName: "my_module",
				PackageName: "UsrCustomPkg",
				VendorPrefix: "usr"));

		// Assert
		result.ExitCode.Should().Be(1);
		result.Output.Should().ContainSingle()
			.Which.Value.ToString().Should().Contain("workspaceDirectory is required");
		commandResolver.DidNotReceive().ResolveWithoutEnvironment<CreateUiProjectCommand>(Arg.Any<EnvironmentOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Rejects new-ui-project calls when the workspace directory exists but is not a clio workspace.")]
	[Category("Unit")]
	public void CreateUiProject_Should_Reject_Non_Workspace_Directory() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string nonWorkspace = Path.Combine(Path.GetTempPath(), "clio-ui-tool-not-a-ws-" + Path.GetRandomFileName());
		Directory.CreateDirectory(nonWorkspace);
		try {
			IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
			CreateUiProjectTool tool = new(ConsoleLogger.Instance, commandResolver);

			// Act
			CommandExecutionResult result = tool.CreateUiProject(
				new CreateUiProjectArgs(
					WorkspaceDirectory: nonWorkspace,
					ProjectName: "my_module",
					PackageName: "UsrCustomPkg",
					VendorPrefix: "usr"));

			// Assert
			result.ExitCode.Should().Be(1);
			result.Output.Should().ContainSingle()
				.Which.Value.ToString().Should().Contain("not a clio workspace");
			commandResolver.DidNotReceive().ResolveWithoutEnvironment<CreateUiProjectCommand>(Arg.Any<EnvironmentOptions>());
		} finally {
			Directory.Delete(nonWorkspace, recursive: true);
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Description("Rejects drive-relative or root-relative workspace paths that would otherwise pass IsPathRooted.")]
	[Category("Unit")]
	public void CreateUiProject_Should_Reject_Drive_Relative_Workspace_Directory() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateUiProjectTool tool = new(ConsoleLogger.Instance, commandResolver);

		// Act — "C:ws" is path-rooted but NOT fully qualified; resolves against the
		// process's current directory on the named drive, not an absolute location.
		CommandExecutionResult result = tool.CreateUiProject(
			new CreateUiProjectArgs(
				WorkspaceDirectory: "C:ws",
				ProjectName: "my_module",
				PackageName: "UsrCustomPkg",
				VendorPrefix: "usr"));

		// Assert
		result.ExitCode.Should().Be(1);
		result.Output.Should().ContainSingle()
			.Which.Value.ToString().Should().Contain("fully-qualified");
		commandResolver.DidNotReceive().ResolveWithoutEnvironment<CreateUiProjectCommand>(Arg.Any<EnvironmentOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[TestCase("../escape")]
	[TestCase("..\\escape")]
	[TestCase("packages/sub")]
	[TestCase("packages\\sub")]
	[TestCase("C:\\absolute")]
	[Description("Rejects packageName values that contain path separators, parent-directory references, or absolute paths so scaffolding cannot escape the workspace.")]
	[Category("Unit")]
	public void CreateUiProject_Should_Reject_PackageName_With_Path_Characters(string packageName) {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateUiProjectTool tool = new(ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateUiProject(
			new CreateUiProjectArgs(
				WorkspaceDirectory: _workspaceDirectory,
				ProjectName: "my_module",
				PackageName: packageName,
				VendorPrefix: "usr"));

		// Assert
		result.ExitCode.Should().Be(1);
		result.Output.Should().ContainSingle()
			.Which.Value.ToString().Should().Contain("packageName");
		commandResolver.DidNotReceive().ResolveWithoutEnvironment<CreateUiProjectCommand>(Arg.Any<EnvironmentOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Exposes the supported new-ui-project arguments and requires the structured args payload.")]
	[Category("Unit")]
	public void CreateUiProject_Should_Expose_Required_Args_Payload() {
		// Arrange
		System.Reflection.ParameterInfo argsParameter = typeof(CreateUiProjectTool)
			.GetMethod(nameof(CreateUiProjectTool.CreateUiProject))!
			.GetParameters()
			.Single(parameter => parameter.Name == "args");

		// Act
		object[] requiredAttributes = argsParameter.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), inherit: false);

		// Assert
		requiredAttributes.Should().ContainSingle(
			because: "the new-ui-project MCP tool should require its structured args payload");
		typeof(CreateUiProjectArgs).GetProperties().Select(property => property.Name).Should().BeEquivalentTo(
			["WorkspaceDirectory", "ProjectName", "PackageName", "VendorPrefix", "Empty", "CreatioVersion"],
			because: "the new-ui-project MCP payload should only expose the supported UI project arguments");
	}

	private sealed class FakeCreateUiProjectCommand : CreateUiProjectCommand {
		private readonly System.Action<CreateUiProjectOptions> _onExecute;
		public CreateUiProjectOptions CapturedOptions { get; private set; }

		public FakeCreateUiProjectCommand(System.Action<CreateUiProjectOptions> onExecute = null)
			: base(
				Substitute.For<IUiProjectCreator>(),
				Substitute.For<IValidator<CreateUiProjectOptions>>(),
				ConsoleLogger.Instance) {
			_onExecute = onExecute;
		}

		public override int Execute(CreateUiProjectOptions options) {
			CapturedOptions = options;
			_onExecute?.Invoke(options);
			return 0;
		}
	}
}
