using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.ModelBuilder;
using Clio.Project;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using ModelContextProtocol.Server;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class AddItemModelToolTests {

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for add-item-model so prompts, unit tests, and E2E tests share one identifier.")]
	public void AddItemModelTool_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		string toolName = AddItemModelTool.AddItemModelToolName;

		// Assert
		toolName.Should().Be("add-item-model",
			because: "the MCP tool name must remain stable for callers and tests");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the add-item-model MCP method as destructive because it writes and can overwrite generated model files.")]
	public void AddItemModel_Should_Be_Marked_As_Destructive() {
		// Arrange
		System.Reflection.MethodInfo method = typeof(AddItemModelTool).GetMethod(nameof(AddItemModelTool.AddItemModel))!;
		McpServerToolAttribute attribute = method
			.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false)
			.Cast<McpServerToolAttribute>()
			.Single();

		// Act
		bool destructive = attribute.Destructive;

		// Assert
		destructive.Should().BeTrue(
			because: "add-item-model writes generated source files into the requested folder");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the environment-aware add-item command and maps the MCP namespace, folder, and environment-name arguments into add-item model options.")]
	public void AddItemModel_Should_Resolve_Command_And_Map_Arguments() {
		// Arrange
		MockFileSystem fileSystem = new(new Dictionary<string, MockFileData>(), @"C:\");
		fileSystem.AddDirectory(@"C:\Models");
		FakeAddItemCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<AddItemCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		AddItemModelTool tool = new(ConsoleLogger.Instance, commandResolver, fileSystem);

		// Act
		CommandExecutionResult result = tool.AddItemModel(new AddItemModelArgs(
			"Contoso.Models",
			@"C:\Models",
			"dev"));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a valid add-item-model request should execute the resolved environment-aware command");
		commandResolver.Received(1).Resolve<AddItemCommand>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the mapped add-item options");
		resolvedCommand.CapturedOptions!.ItemType.Should().Be("model",
			because: "the MCP tool only exposes add-item model generation");
		resolvedCommand.CapturedOptions.CreateAll.Should().BeTrue(
			because: "the first MCP slice intentionally covers only all-model generation");
		resolvedCommand.CapturedOptions.Namespace.Should().Be("Contoso.Models",
			because: "the requested namespace must be forwarded to add-item model generation");
		resolvedCommand.CapturedOptions.DestinationPath.Should().Be(@"C:\Models",
			because: "the requested folder must become the add-item destination path");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev",
			because: "the requested environment name must be preserved for schema loading");
		resolvedCommand.CapturedOptions.Culture.Should().Be("en-US",
			because: "the MCP tool should rely on the command's default culture when the argument is omitted");
		resolvedCommand.CapturedOptions.ItemName.Should().BeNull(
			because: "single-entity generation is out of scope for this MCP slice");
		resolvedCommand.CapturedOptions.Fields.Should().BeNull(
			because: "single-entity field selection is out of scope for this MCP slice");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a missing folder before command execution so add-item-model does not rely on the server working directory.")]
	public void AddItemModel_Should_Reject_Missing_Folder() {
		// Arrange
		MockFileSystem fileSystem = new(new Dictionary<string, MockFileData>(), @"C:\");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		AddItemModelTool tool = new(ConsoleLogger.Instance, commandResolver, fileSystem);

		// Act
		CommandExecutionResult result = tool.AddItemModel(new AddItemModelArgs(
			"Contoso.Models",
			"",
			"dev"));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "the tool should fail fast when the caller omits the target folder");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, "Folder is required."),
			because: "the validation failure should explain that folder is mandatory");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<AddItemCommand>(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects relative folders before command execution so add-item-model always uses an explicit absolute destination.")]
	public void AddItemModel_Should_Reject_Relative_Folder() {
		// Arrange
		MockFileSystem fileSystem = new(new Dictionary<string, MockFileData>(), @"C:\");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		AddItemModelTool tool = new(ConsoleLogger.Instance, commandResolver, fileSystem);

		// Act
		CommandExecutionResult result = tool.AddItemModel(new AddItemModelArgs(
			"Contoso.Models",
			@"relative\models",
			"dev"));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "the tool should fail fast when the caller provides a relative folder");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, @"Folder path must be absolute: relative\models"),
			because: "the validation failure should explain that only absolute folders are allowed");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<AddItemCommand>(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects UNC folders before command execution so add-item-model does not write generated models to network shares.")]
	public void AddItemModel_Should_Reject_Network_Folder() {
		// Arrange
		MockFileSystem fileSystem = new(new Dictionary<string, MockFileData>(), @"C:\");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		AddItemModelTool tool = new(ConsoleLogger.Instance, commandResolver, fileSystem);

		// Act
		CommandExecutionResult result = tool.AddItemModel(new AddItemModelArgs(
			"Contoso.Models",
			@"\\server\share\models",
			"dev"));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "the tool should fail fast when the caller points add-item-model at a network share");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, @"Folder path must be a local absolute path: \\server\share\models"),
			because: "the validation failure should explain that network folders are not supported");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<AddItemCommand>(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects nonexistent folders before command execution so add-item-model does not silently create an unexpected path.")]
	public void AddItemModel_Should_Reject_Nonexistent_Folder() {
		// Arrange
		MockFileSystem fileSystem = new(new Dictionary<string, MockFileData>(), @"C:\");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		AddItemModelTool tool = new(ConsoleLogger.Instance, commandResolver, fileSystem);

		// Act
		CommandExecutionResult result = tool.AddItemModel(new AddItemModelArgs(
			"Contoso.Models",
			@"C:\Missing\Models",
			"dev"));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "the tool should fail fast when the requested folder does not exist");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, @"Folder path not found: C:\Missing\Models"),
			because: "the validation failure should explain that the target folder was not found");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<AddItemCommand>(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Prompt guidance for add-item-model references the exact tool name and keeps namespace, folder, and environment-name visible.")]
	public void AddItemModelPrompt_Should_Mention_Tool_Name_And_Arguments() {
		// Arrange

		// Act
		string prompt = AddItemModelPrompt.AddItemModel(
			"Contoso.Models",
			@"C:\Models",
			"dev");

		// Assert
		prompt.Should().Contain(AddItemModelTool.AddItemModelToolName,
			because: "the prompt should reference the exact production tool name");
		prompt.Should().Contain("`namespace`",
			because: "the prompt should keep the namespace argument visible to callers");
		prompt.Should().Contain("`folder`",
			because: "the prompt should keep the folder argument visible to callers");
		prompt.Should().Contain("`environment-name`",
			because: "the prompt should keep the environment-name argument visible to callers");
		prompt.Should().Contain("existing local absolute directory",
			because: "the prompt should explain the folder validation rule");
	}

	private sealed class FakeAddItemCommand : AddItemCommand {
		public AddItemOptions? CapturedOptions { get; private set; }

		public FakeAddItemCommand()
			: base(
				Substitute.For<IApplicationClient>(),
				Substitute.For<IServiceUrlBuilder>(),
				new AddItemOptionsValidator(),
				Substitute.For<IVsProjectFactory>(),
				Substitute.For<ILogger>(),
				new MockFileSystem(new Dictionary<string, MockFileData>(), @"C:\"), 
				Substitute.For<IModelBuilder>()) {
		}

		public override int Execute(AddItemOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
