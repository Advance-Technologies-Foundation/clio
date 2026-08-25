using IoFileSystem = System.IO.Abstractions.IFileSystem;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.SchemaTransfer;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Argument-mapping coverage for the schema-transfer MCP tools.
/// </summary>
/// <remarks>
/// Both tools translate their args record into options by hand, and the flags they carry
/// (<c>dry-run</c>, <c>allow-new-layer</c>) are the safety switches that decide whether an import
/// writes at all and whether it may create a new layer in a foreign package. A silent mapping
/// mistake there is a destructive defect, so every field is asserted here.
/// Same shape as <see cref="DeleteSchemaToolTests"/>: an <see cref="IToolCommandResolver"/>
/// substitute plus a fake command that captures the options it was executed with.
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public class SchemaTransferToolTests {

	[Test]
	[Category("Unit")]
	public void ExportSchema_Should_Map_Every_Argument_To_Options() {
		ConsoleLogger.Instance.ClearMessages();
		FakeExportSchemaCommand defaultCommand = new();
		FakeExportSchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ExportSchemaCommand>(Arg.Any<ExportSchemaOptions>())
			.Returns(resolvedCommand);
		ExportSchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		CommandExecutionResult result = tool.ExportSchema(new ExportSchemaArgs(
			"UsrMyTask",
			"docker_fix2",
			"UsrCustomPackage",
			"AddonSchemaManager",
			"/tmp/bundles"));

		result.ExitCode.Should().Be(0);
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the tool must execute the resolved command, not the injected default one");
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.SchemaName.Should().Be("UsrMyTask");
		resolvedCommand.CapturedOptions.Environment.Should().Be("docker_fix2");
		resolvedCommand.CapturedOptions.PackageName.Should().Be("UsrCustomPackage");
		resolvedCommand.CapturedOptions.ManagerName.Should().Be("AddonSchemaManager");
		resolvedCommand.CapturedOptions.Destination.Should().Be("/tmp/bundles");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	public void ExportSchema_Should_Leave_Optional_Arguments_Unset() {
		ConsoleLogger.Instance.ClearMessages();
		FakeExportSchemaCommand defaultCommand = new();
		FakeExportSchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ExportSchemaCommand>(Arg.Any<ExportSchemaOptions>())
			.Returns(resolvedCommand);
		ExportSchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		CommandExecutionResult result = tool.ExportSchema(new ExportSchemaArgs(
			"UsrMyTask",
			"docker_fix2"));

		result.ExitCode.Should().Be(0);
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.SchemaName.Should().Be("UsrMyTask");
		resolvedCommand.CapturedOptions.PackageName.Should().BeNull(
			because: "an omitted package must stay unset so the gate can report an ambiguous name");
		resolvedCommand.CapturedOptions.ManagerName.Should().BeNull();
		resolvedCommand.CapturedOptions.Destination.Should().BeNull(
			because: "an omitted destination must fall through to the command's own default, not to an empty path");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	public void ImportSchema_Should_Map_Every_Argument_To_Options() {
		ConsoleLogger.Instance.ClearMessages();
		FakeImportSchemaCommand defaultCommand = new();
		FakeImportSchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ImportSchemaCommand>(Arg.Any<ImportSchemaOptions>())
			.Returns(resolvedCommand);
		ImportSchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		CommandExecutionResult result = tool.ImportSchema(new ImportSchemaArgs(
			"/tmp/bundles/UsrMyTask",
			"UsrCustomPackage",
			"docker_fix2",
			DryRun: true,
			AllowNewLayer: true));

		result.ExitCode.Should().Be(0);
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the tool must execute the resolved command, not the injected default one");
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.Path.Should().Be("/tmp/bundles/UsrMyTask");
		resolvedCommand.CapturedOptions.PackageName.Should().Be("UsrCustomPackage");
		resolvedCommand.CapturedOptions.Environment.Should().Be("docker_fix2");
		resolvedCommand.CapturedOptions.DryRun.Should().BeTrue();
		resolvedCommand.CapturedOptions.AllowNewLayer.Should().BeTrue();
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	public void ImportSchema_Should_Default_The_Safety_Flags_To_False_When_Omitted() {
		ConsoleLogger.Instance.ClearMessages();
		FakeImportSchemaCommand defaultCommand = new();
		FakeImportSchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ImportSchemaCommand>(Arg.Any<ImportSchemaOptions>())
			.Returns(resolvedCommand);
		ImportSchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		CommandExecutionResult result = tool.ImportSchema(new ImportSchemaArgs(
			"/tmp/bundles/UsrMyTask",
			"UsrCustomPackage",
			"docker_fix2"));

		result.ExitCode.Should().Be(0);
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		// The two flags are the destructive safety switches: an omitted dry-run must NOT silently
		// become a real write, and an omitted allow-new-layer must NOT silently permit a foreign
		// package to gain a layer.
		resolvedCommand.CapturedOptions.DryRun.Should().BeFalse();
		resolvedCommand.CapturedOptions.AllowNewLayer.Should().BeFalse();
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeExportSchemaCommand : ExportSchemaCommand {
		public ExportSchemaOptions CapturedOptions { get; private set; }

		public FakeExportSchemaCommand()
			: base(
				Substitute.For<ISchemaTransferClient>(),
				Substitute.For<ISchemaBundleStore>(),
				Substitute.For<IoFileSystem>(),
				new EnvironmentSettings(),
				Substitute.For<ILogger>()) {
		}

		public override int Execute(ExportSchemaOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}

	private sealed class FakeImportSchemaCommand : ImportSchemaCommand {
		public ImportSchemaOptions CapturedOptions { get; private set; }

		public FakeImportSchemaCommand()
			: base(
				Substitute.For<ISchemaTransferClient>(),
				Substitute.For<ISchemaBundleStore>(),
				Substitute.For<ILogger>()) {
		}

		public override int Execute(ImportSchemaOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
