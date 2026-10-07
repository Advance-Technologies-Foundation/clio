using System;
using System.Linq;
using System.Reflection;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Package;
using CommandLine;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Covers the file system development mode warning of <see cref="PackageDataBindingWriter"/>: the platform's
/// binding endpoints write to the database only, so in that mode a binding the writer saved is missing from
/// the package folder that gets committed (GitHub #1747).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PackageDataBindingWriterFileDesignModeTests {
	private const string SaveSchemaUrl = "http://localhost/0/ServiceModel/SchemaDataDesignerService.svc/SaveSchema";
	private const string DeleteBindingUrl = "http://localhost/0/DataService/json/SyncReply/DeletePackageSchemaDataRequest";
	private const string RefusedResponse = """{"success":false,"errorInfo":{"message":"refused"}}""";
	private const string RowId = "4f41bcc2-7ed0-45e8-a1fd-474918966d15";
	private static readonly PackageRef Package = new(Guid.Parse("1d07fd0e-2ca4-4d20-93b4-eb5a795ea03f"), "UsrPkg");

	private IApplicationClient _applicationClient = null!;
	private IFileDesignModeStateReader _stateReader = null!;
	private ILogger _logger = null!;
	private PackageDataBindingWriter _sut = null!;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success":true}""");
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveSchemaData).Returns(SaveSchemaUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DeletePackageSchemaData).Returns(DeleteBindingUrl);
		_stateReader = Substitute.For<IFileDesignModeStateReader>();
		_logger = Substitute.For<ILogger>();
		_sut = new PackageDataBindingWriter(
			_applicationClient,
			serviceUrlBuilder,
			Substitute.For<IPackageTargetResolver>(),
			Substitute.For<IDataBindingSchemaClient>(),
			_stateReader,
			_logger);
	}

	[Test]
	[Description("Reads the file system development mode state once per writer and warns once per saved binding, so a command that saves several bindings names each one without asking the environment again.")]
	public void SaveBinding_Should_Read_Mode_Once_And_Warn_Per_Binding_When_File_Design_Mode_Is_Enabled() {
		// Arrange
		_stateReader.GetIsFileDesignModeEnabled().Returns(true);

		// Act
		_sut.SaveBinding(Package, "UsrStatus", "UsrStatus", BuildSchema(), [RowId]);
		_sut.SaveBinding(Package, "Lookup_UsrStatus", "Lookup", BuildSchema(), [RowId]);

		// Assert
		_stateReader.Received(1).GetIsFileDesignModeEnabled();
		_logger.Received(2).WriteWarning(Arg.Any<string>());
		_logger.Received(1).WriteWarning(Arg.Is<string>(message => message.Contains("'UsrStatus'")
			&& message.Contains("Pkg/UsrPkg/Data/UsrStatus")));
		_logger.Received(1).WriteWarning(Arg.Is<string>(message => message.Contains("Pkg/UsrPkg/Data/Lookup_UsrStatus")));
	}

	[Test]
	[Description("Says a refreshed binding was re-saved and that a commit carries it as the last export wrote it, instead of claiming the package ships without the binding, because the folder may already hold it.")]
	public void SaveBinding_Should_Describe_A_Refresh_As_A_Change_When_The_Binding_Already_Existed() {
		// Arrange
		_stateReader.GetIsFileDesignModeEnabled().Returns(true);

		// Act
		_sut.SaveBinding(Package, "UsrStatus", "UsrStatus", BuildSchema(), [RowId],
			existingBindingUId: Guid.Parse("c653d44c-9c7c-125d-e269-b9257b353ff9"));

		// Assert
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("re-saved in the database only")
			&& message.Contains("carries the binding as the last export wrote it")
			&& !message.Contains("without this binding")));
	}

	[Test]
	[Description("Keeps an unreadable state cached for the writer, so a stalled state read is not repeated before every following binding, and warns conditionally for each binding.")]
	public void SaveBinding_Should_Read_Unknown_State_Once_And_Warn_Conditionally_Per_Binding() {
		// Arrange
		_stateReader.GetIsFileDesignModeEnabled().Returns((bool?)null);

		// Act
		_sut.SaveBinding(Package, "UsrStatus", "UsrStatus", BuildSchema(), [RowId]);
		_sut.SaveBinding(Package, "Lookup_UsrStatus", "Lookup", BuildSchema(), [RowId]);

		// Assert
		_stateReader.Received(1).GetIsFileDesignModeEnabled();
		_logger.Received(2).WriteWarning(Arg.Is<string>(message =>
			message.Contains("could not read whether file system development mode is enabled")));
	}

	[Test]
	[Description("Keeps a committed save successful when the state reader throws a non-HTTP exception, and warns that the state is unknown, because a failed save would make sync-schemas seed-data replay a non-idempotent insert.")]
	public void SaveBinding_Should_Succeed_And_Warn_Conditionally_When_The_State_Reader_Throws() {
		// Arrange
		_stateReader.GetIsFileDesignModeEnabled().Returns(_ => throw new NullReferenceException("converter defect"));

		// Act
		Action act = () => _sut.SaveBinding(Package, "UsrStatus", "UsrStatus", BuildSchema(), [RowId]);

		// Assert
		act.Should().NotThrow(
			because: "the binding is already saved, and reporting it as failed invites a duplicate-row replay");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("could not read whether file system development mode is enabled")
			&& message.Contains("Pkg/UsrPkg/Data/UsrStatus")));
	}

	[Test]
	[Description("Does not read the mode or warn when SaveSchema is refused, because nothing changed in the database and the caller already reports the failure.")]
	public void SaveBinding_Should_Not_Warn_When_The_Save_Is_Refused() {
		// Arrange
		_stateReader.GetIsFileDesignModeEnabled().Returns(true);
		_applicationClient
			.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(RefusedResponse);

		// Act
		Action act = () => _sut.SaveBinding(Package, "UsrStatus", "UsrStatus", BuildSchema(), [RowId]);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a refused save is still a failure");
		_stateReader.DidNotReceive().GetIsFileDesignModeEnabled();
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test]
	[Description("Does not read the mode or warn when the delete is refused, because the binding is still in the database.")]
	public void DeleteBinding_Should_Not_Warn_When_The_Delete_Is_Refused() {
		// Arrange
		_stateReader.GetIsFileDesignModeEnabled().Returns(true);
		_applicationClient
			.ExecutePostRequest(DeleteBindingUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(RefusedResponse);

		// Act
		Action act = () => _sut.DeleteBinding(Package, "UsrStatus");

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a refused delete is still a failure");
		_stateReader.DidNotReceive().GetIsFileDesignModeEnabled();
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test]
	[Description("Warns after DeleteBinding in file system development mode that the package folder on disk was not updated and still carries the removed binding.")]
	public void DeleteBinding_Should_Warn_That_Package_Folder_Was_Not_Updated_When_File_Design_Mode_Is_Enabled() {
		// Arrange
		_stateReader.GetIsFileDesignModeEnabled().Returns(true);

		// Act
		_sut.DeleteBinding(Package, "UsrStatus");

		// Assert
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("removed from the database only")
			&& message.Contains("(Pkg/UsrPkg/Data/UsrStatus) was not updated")
			&& message.Contains("still ships the removed binding")
			&& message.Contains("pkg-to-file-system")));
	}

	[Test]
	[Description("Uses the removal wording in the conditional warning when the state cannot be read after a delete.")]
	public void DeleteBinding_Should_Warn_Conditionally_When_File_Design_Mode_State_Is_Unknown() {
		// Arrange
		_stateReader.GetIsFileDesignModeEnabled().Returns((bool?)null);

		// Act
		_sut.DeleteBinding(Package, "UsrStatus");

		// Assert
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("was removed from the database, but clio could not read")
			&& message.Contains("Pkg/UsrPkg/Data/UsrStatus")));
	}

	[Test]
	[Description("Does not warn after a save or a delete when the environment reports file system development mode as disabled, and reads that state once for both.")]
	public void SaveAndDeleteBinding_Should_Not_Warn_When_File_Design_Mode_Is_Disabled() {
		// Arrange
		_stateReader.GetIsFileDesignModeEnabled().Returns(false);

		// Act
		_sut.SaveBinding(Package, "UsrStatus", "UsrStatus", BuildSchema(), [RowId]);
		_sut.DeleteBinding(Package, "UsrStatus");

		// Assert
		_stateReader.Received(1).GetIsFileDesignModeEnabled();
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test]
	[Description("The commands the warning names are the real CLI verbs and MCP tools, and the MCP route goes through clio-run, so the advice cannot drift to a command an agent cannot run.")]
	public void Warning_Should_Name_Existing_Cli_Verbs_And_Mcp_Tools() {
		// Arrange
		VerbAttribute exportVerb = typeof(LoadPackagesToFileSystemOptions).GetCustomAttribute<VerbAttribute>()!;
		VerbAttribute importVerb = typeof(LoadPackagesToDbOptions).GetCustomAttribute<VerbAttribute>()!;

		// Act
		string warning = PackageDataBindingWriter.BuildPackageFolderWarning(
			true, Package.Name, "UsrStatus", PackageDataBindingWriter.BindingChange.Created);

		// Assert
		PackageDataBindingWriter.FileSystemExportCommandName.Should().Be(exportVerb.Name,
			because: "the CLI form of the advice must name an existing verb");
		PackageDataBindingWriter.FileSystemExportCommandName.Should().Be(LoadPackagesTool.LoadPackagesToFileSystemToolName,
			because: "MCP callers are told to run the tool of the same name");
		PackageDataBindingWriter.FileSystemImportCommandName.Should().Be(importVerb.Name,
			because: "the warning names pkg-to-db as the step that must not come first");
		PackageDataBindingWriter.FileSystemImportCommandName.Should().Be(LoadPackagesTool.LoadPackagesToDbToolName,
			because: "the import is the same name on both surfaces");
		warning.Should().Contain($"{ClioRunTool.ToolName} with command {LoadPackagesTool.LoadPackagesToFileSystemToolName}",
			because: "the export tool is not resident, so an MCP caller reaches it through clio-run");
	}

	[Test]
	[Description("The created-binding warning states what happens without action, what the advised command does to the disk, and orders it before pkg-to-db, which would make the database match the disk.")]
	public void BuildPackageFolderWarning_Should_State_What_The_Advised_Command_Does() {
		// Arrange
		const string bindingName = "UsrStatus";

		// Act
		string warning = PackageDataBindingWriter.BuildPackageFolderWarning(
			true, Package.Name, bindingName, PackageDataBindingWriter.BindingChange.Created);

		// Assert
		warning.Should().Contain("ships it without this binding",
			because: "the reader must learn what happens if nothing is done");
		warning.Should().Contain("rewrites every package folder on disk from the database, except client modules and C# source code",
			because: "the advised command exports every package, and the reader must know that before running it");
		warning.Should().Contain("removes on-disk items the database does not have",
			because: "uncommitted on-disk work is lost if the reader runs the export without saving it first");
		warning.Should().Contain("before the next pkg-to-db",
			because: "pkg-to-db makes the database match the disk and a database-only binding does not survive it");
	}

	private static DataBindingDbSchema BuildSchema() {
		DataBindingSchemaColumn[] columns = [
			new(Guid.Parse("ae0e45ca-c495-4fe7-a39d-3ab7278e1617"), "Id", 0, null),
			new(Guid.Parse("736c30a7-c0ec-4fa9-b034-2552b319b633"), "Name", 1, null)
		];
		return new DataBindingDbSchema(
			Guid.Parse("2d07fd0e-2ca4-4d20-93b4-eb5a795ea03f"), "UsrStatus",
			columns.Select(column => column.Name).ToList(), columns);
	}
}
