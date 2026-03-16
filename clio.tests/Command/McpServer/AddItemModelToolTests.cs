using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.ModelBuilder;
using Clio.Project;
using ConsoleTables;
using FluentValidation.Results;
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
	[Description("Creates a missing absolute local folder before command execution so add-item-model stays aligned with the wrapped model-generation command.")]
	public void AddItemModel_Should_Create_Missing_Absolute_Folder() {
		// Arrange
		MockFileSystem fileSystem = new(new Dictionary<string, MockFileData>(), @"C:\");
		TestLogger logger = new();
		FakeAddItemCommand resolvedCommand = new(logger, fileSystem);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<AddItemCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		AddItemModelTool tool = new(logger, commandResolver, fileSystem);
		string missingFolder = @"C:\Missing\Models";

		// Act
		CommandExecutionResult result = tool.AddItemModel(new AddItemModelArgs(
			"Contoso.Models",
			missingFolder,
			"dev"));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the tool should create a missing absolute folder before executing add-item model generation");
		fileSystem.Directory.Exists(missingFolder).Should().BeTrue(
			because: "the missing folder should be created before command execution");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should still receive add-item options after folder creation");
		resolvedCommand.CapturedOptions!.DestinationPath.Should().Be(missingFolder,
			because: "the created absolute folder should become the destination path passed to the command");
	}

	[Test]
	[Category("Unit")]
	[Description("Compacts repeated per-model progress output into one MCP summary line while keeping non-progress messages visible.")]
	public void AddItemModel_Should_Compact_Progress_Into_Summary_Message() {
		// Arrange
		MockFileSystem fileSystem = new(new Dictionary<string, MockFileData>(), @"C:\");
		fileSystem.AddDirectory(@"C:\Models");
		TestLogger logger = new();
		FakeAddItemCommand resolvedCommand = new(
			logger,
			fileSystem,
			onExecute: _ => {
				logger.WriteInfo("Generating models...");
				logger.WriteLine(@"Models will be generated in directory: C:\Models");
				logger.Write("Generated: 1 models from 3\r");
				logger.Write("Generated: 2 models from 3\r");
				logger.Write("Generated: 3 models from 3\r");
				logger.WriteLine();
			});
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<AddItemCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		AddItemModelTool tool = new(logger, commandResolver, fileSystem);

		// Act
		CommandExecutionResult result = tool.AddItemModel(new AddItemModelArgs(
			"Contoso.Models",
			@"C:\Models",
			"dev"));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "successful generation should still report a successful command result after compaction");
		result.Output.Should().Contain(message =>
			message.LogDecoratorType == LogDecoratorType.Info &&
			Equals(message.Value, "Generating models..."),
			because: "non-progress info messages should remain visible after compaction");
		result.Output.Should().Contain(message =>
			message.LogDecoratorType == LogDecoratorType.None &&
			Equals(message.Value, @"Models will be generated in directory: C:\Models"),
			because: "non-progress undecorated messages should remain visible after compaction");
		result.Output.Should().Contain(message =>
			message.LogDecoratorType == LogDecoratorType.Info &&
			Equals(message.Value, "Generated 3 models; requested filter: none."),
			because: "the MCP result should append one compact summary derived from the final progress message");
		result.Output.Should().NotContain(message =>
			Equals(message.Value, "Generated: 1 models from 3\r") ||
			Equals(message.Value, "Generated: 2 models from 3\r") ||
			Equals(message.Value, "Generated: 3 models from 3\r"),
			because: "repeated per-model progress messages should be removed from the MCP result");
	}

	[Test]
	[Category("Unit")]
	[Description("Falls back to counting generated model files when the command output does not include a final progress line.")]
	public void AddItemModel_Should_Fallback_To_File_Count_For_Summary() {
		// Arrange
		MockFileSystem fileSystem = new(new Dictionary<string, MockFileData>(), @"C:\");
		fileSystem.AddDirectory(@"C:\Models");
		TestLogger logger = new();
		FakeAddItemCommand resolvedCommand = new(
			logger,
			fileSystem,
			onExecute: options => {
				fileSystem.AddFile(Path.Combine(options.DestinationPath, "Account.cs"), new MockFileData("// model"));
				fileSystem.AddFile(Path.Combine(options.DestinationPath, "Contact.cs"), new MockFileData("// model"));
				fileSystem.AddFile(Path.Combine(options.DestinationPath, "BaseModelExtensions.cs"), new MockFileData("// helper"));
			});
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<AddItemCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		AddItemModelTool tool = new(logger, commandResolver, fileSystem);

		// Act
		CommandExecutionResult result = tool.AddItemModel(new AddItemModelArgs(
			"Contoso.Models",
			@"C:\Models",
			"dev"));

		// Assert
		result.Output.Should().Contain(message =>
			message.LogDecoratorType == LogDecoratorType.Info &&
			Equals(message.Value, "Generated 2 models; requested filter: none."),
			because: "the MCP result should count generated model files when progress output is unavailable");
	}

	[Test]
	[Category("Unit")]
	[Description("Preserves warnings and errors while still removing repeated progress noise from failed or partial command output.")]
	public void AddItemModel_Should_Preserve_Warnings_And_Errors_During_Compaction() {
		// Arrange
		MockFileSystem fileSystem = new(new Dictionary<string, MockFileData>(), @"C:\");
		fileSystem.AddDirectory(@"C:\Models");
		TestLogger logger = new();
		FakeAddItemCommand resolvedCommand = new(
			logger,
			fileSystem,
			onExecute: _ => {
				logger.WriteLine(@"Models will be generated in directory: C:\Models");
				logger.Write("Generated: 1 models from 3\r");
				logger.WriteWarning("One schema was skipped.");
				logger.WriteError("Model generation failed.");
			},
			exitCode: 1);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<AddItemCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		AddItemModelTool tool = new(logger, commandResolver, fileSystem);

		// Act
		CommandExecutionResult result = tool.AddItemModel(new AddItemModelArgs(
			"Contoso.Models",
			@"C:\Models",
			"dev"));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "the command failure should still be surfaced after output compaction");
		result.Output.Should().Contain(message =>
			message.LogDecoratorType == LogDecoratorType.Warning &&
			Equals(message.Value, "One schema was skipped."),
			because: "warning messages should be preserved during output compaction");
		result.Output.Should().Contain(message =>
			message.LogDecoratorType == LogDecoratorType.Error &&
			Equals(message.Value, "Model generation failed."),
			because: "error messages should be preserved during output compaction");
		result.Output.Should().NotContain(message =>
			Equals(message.Value, "Generated: 1 models from 3\r"),
			because: "progress noise should still be removed on failure");
		result.Output.Should().NotContain(message =>
			message.LogDecoratorType == LogDecoratorType.Info &&
			Equals(message.Value, "Generated 1 models; requested filter: none."),
			because: "the MCP tool should not append a success summary when generation fails");
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
		prompt.Should().Contain("created if it does not exist",
			because: "the prompt should explain that the tool creates the output folder when needed");
	}

	private sealed class FakeAddItemCommand : AddItemCommand {
		private readonly Action<AddItemOptions>? _onExecute;
		private readonly int _exitCode;

		public AddItemOptions? CapturedOptions { get; private set; }

		public FakeAddItemCommand(
			ILogger logger = null!,
			System.IO.Abstractions.IFileSystem fileSystem = null!,
			Action<AddItemOptions>? onExecute = null,
			int exitCode = 0)
			: base(
				Substitute.For<IApplicationClient>(),
				Substitute.For<IServiceUrlBuilder>(),
				new AddItemOptionsValidator(),
				Substitute.For<IVsProjectFactory>(),
				logger ?? Substitute.For<ILogger>(),
				fileSystem ?? new MockFileSystem(new Dictionary<string, MockFileData>(), @"C:\"),
				Substitute.For<IModelBuilder>()) {
			_onExecute = onExecute;
			_exitCode = exitCode;
		}

		public override int Execute(AddItemOptions options) {
			CapturedOptions = options;
			_onExecute?.Invoke(options);
			return _exitCode;
		}
	}

	private sealed class TestLogger : ILogger {
		List<LogMessage> ILogger.LogMessages => LogMessages;
		bool ILogger.PreserveMessages { get; set; }
		internal List<LogMessage> LogMessages { get; } = [];

		public void ClearMessages() => LogMessages.Clear();
		public IDisposable BeginScopedFileSink(string logFilePath) => Substitute.For<IDisposable>();
		public void Start(string logFilePath = "") { }
		public void SetCreatioLogStreamer(ILogStreamer creatioLogStreamer) { }
		public void StartWithStream() { }
		public void Stop() { }
		public void Write(string value) => LogMessages.Add(new UndecoratedMessage(value));
		public void WriteLine() => LogMessages.Add(new UndecoratedMessage(string.Empty));
		public void WriteLine(string value) => LogMessages.Add(new UndecoratedMessage(value));
		public void WriteWarning(string value) => LogMessages.Add(new WarningMessage(value));
		public void WriteError(string value) => LogMessages.Add(new ErrorMessage(value));
		public void WriteInfo(string value) => LogMessages.Add(new InfoMessage(value));
		public void WriteDebug(string value) => LogMessages.Add(new DebugMessage(value));
		public void PrintTable(ConsoleTable table) => LogMessages.Add(new TableMessage(table));
		public void PrintValidationFailureErrors(IEnumerable<ValidationFailure> errors) { }
	}
}
