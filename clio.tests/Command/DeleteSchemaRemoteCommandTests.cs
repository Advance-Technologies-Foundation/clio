namespace Clio.Tests.Command;

using Clio.Command;
using System;
using System.Collections.Generic;
using Clio.Common;
using Clio.Package;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class DeleteSchemaRemoteCommandTests {
	private const string TestBase = "http://test";
	private const string GetWorkspaceItemsUrl = TestBase + "/ServiceModel/WorkspaceExplorerService.svc/GetWorkspaceItems";
	private const string DeleteUrl = TestBase + "/ServiceModel/WorkspaceExplorerService.svc/Delete";
	private const string FileDesignModeUrl = TestBase + "/ServiceModel/WorkspaceExplorerService.svc/GetIsFileDesignMode";
	private const string SchemaId = "11111111-1111-1111-1111-111111111111";
	private const string SchemaUId = "22222222-2222-2222-2222-222222222222";
	private const string PackageUId = "33333333-3333-3333-3333-333333333333";

	private static string GetWorkspaceItemsJson(int type = 4) =>
		$$$"""
		{
		  "items": [
		    {
		      "id": "{{{SchemaId}}}",
		      "uId": "{{{SchemaUId}}}",
		      "name": "UsrSchema",
		      "title": "UsrSchema",
		      "packageUId": "{{{PackageUId}}}",
		      "packageName": "Custom",
		      "type": {{{type}}},
		      "isChanged": false,
		      "isLocked": false,
		      "isReadOnly": false
		    },
		    {
		      "id": "00000000-0000-0000-0000-000000000099",
		      "uId": "00000000-0000-0000-0000-000000000099",
		      "name": "OtherSchema",
		      "packageUId": "00000000-0000-0000-0000-000000000099",
		      "packageName": "OtherPackage",
		      "type": 1
		    }
		  ]
		}
		""";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private IDeletedItemFileCleaner _cleaner;
	private ILogger _logger;
	private DeleteSchemaCommand _command;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetWorkspaceItems).Returns(GetWorkspaceItemsUrl);
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DeleteWorkspaceItem).Returns(DeleteUrl);
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetIsFileDesignMode).Returns(FileDesignModeUrl);
		_cleaner = Substitute.For<IDeletedItemFileCleaner>();
		_logger = Substitute.For<ILogger>();
		_command = new DeleteSchemaCommand(
			_applicationClient,
			new EnvironmentSettings { Uri = TestBase, Login = "u", Password = "p", IsNetCore = true },
			_serviceUrlBuilder,
			Substitute.For<IWorkspacePathBuilder>(),
			Substitute.For<IJsonConverter>(),
			Substitute.For<IFileSystem>(),
			_cleaner) {
			ApplicationClient = _applicationClient,
			Logger = _logger
		};
	}

	[Test]
	public void TryDeleteRemote_Rejects_Missing_Schema_Name() {
		bool result = _command.TryDeleteRemote(string.Empty, out DeleteSchemaRemoteResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("schema-name");
	}

	[Test]
	public void TryDeleteRemote_Fails_When_Schema_Not_Found() {
		_applicationClient.ExecutePostRequest(GetWorkspaceItemsUrl, Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"items": []}""");

		bool result = _command.TryDeleteRemote("UsrMissing", out DeleteSchemaRemoteResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("UsrMissing").And.Contain("not found");
	}

	[Test]
	public void TryDeleteRemote_Happy_Path_Ships_Platform_Type_To_Delete_Endpoint() {
		// type=4 is the value GetWorkspaceItems returns for a Freedom UI page on classic Creatio;
		// shipping anything else (e.g. clio's previous default of 0 from the old SysSchema query)
		// makes the platform resolve a wrong SchemaManager and silently delete nothing.
		_applicationClient.ExecutePostRequest(GetWorkspaceItemsUrl, Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(GetWorkspaceItemsJson(type: 4));
		_applicationClient.ExecutePostRequest(DeleteUrl, Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true, "rowsAffected": 1}""");

		bool result = _command.TryDeleteRemote("UsrSchema", out DeleteSchemaRemoteResponse response);

		result.Should().BeTrue();
		response.Success.Should().BeTrue();
		response.SchemaName.Should().Be("UsrSchema");
		response.SchemaUId.Should().Be(SchemaUId);
		response.PackageName.Should().Be("Custom");
		_applicationClient.Received(1).ExecutePostRequest(DeleteUrl,
			Arg.Is<string>(s => s.Contains(SchemaUId)
				&& s.Contains("Custom")
				&& s.Contains("\"type\":4")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	public void TryDeleteRemote_Surfaces_Delete_Error() {
		_applicationClient.ExecutePostRequest(GetWorkspaceItemsUrl, Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(GetWorkspaceItemsJson());
		_applicationClient.ExecutePostRequest(DeleteUrl, Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": false, "rowsAffected": 0, "errorInfo": {"message": "locked"}}""");

		bool result = _command.TryDeleteRemote("UsrSchema", out DeleteSchemaRemoteResponse response);

		result.Should().BeFalse();
		response.Error.Should().Be($"locked (endpoint={DeleteUrl})");
		response.SchemaUId.Should().Be(SchemaUId);
	}

	[Test]
	public void TryDeleteRemote_Reports_Endpoint_And_Body_When_Platform_Returns_Silent_NoOp() {
		_applicationClient.ExecutePostRequest(GetWorkspaceItemsUrl, Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(GetWorkspaceItemsJson());
		const string noOpResponse = """{"success": true, "rowsAffected": 0, "errorInfo": null}""";
		_applicationClient.ExecutePostRequest(DeleteUrl, Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(noOpResponse);

		bool result = _command.TryDeleteRemote("UsrSchema", out DeleteSchemaRemoteResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain(DeleteUrl)
			.And.Contain("rowsAffected=0")
			.And.Contain("success=True")
			.And.Contain("UsrSchema");
	}

	[Test]
	public void TryDeleteRemote_Wraps_Exception_With_Type_Name() {
		_applicationClient.ExecutePostRequest(GetWorkspaceItemsUrl, Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new System.Net.WebException("boom"));

		bool result = _command.TryDeleteRemote("UsrSchema", out DeleteSchemaRemoteResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("[WebException]").And.Contain("boom");
	}

	[Test]
	[Description("A remote delete of an entity schema says that the database table and its data stay, so the caller does not take the delete as complete.")]
	public void Execute_ShouldSayTheTableIsNotDropped_WhenRemoteDeleteRemovesAnEntitySchema() {
		// Arrange
		ArrangeSuccessfulDelete(itemType: 3);
		ArrangeFileDesignMode(isOn: false);

		// Act
		int exitCode = _command.Execute(RemoteOptions());

		// Assert
		exitCode.Should().Be(0, because: "the platform deleted the schema");
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("Deleted schema 'UsrSchema'")
			&& message.Contains("database table, its columns and its data are not dropped")));
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test]
	[Description("A non-entity delete does not claim anything about database tables.")]
	public void Execute_ShouldNotMentionTables_WhenRemoteDeleteRemovesAClientUnitSchema() {
		// Arrange
		ArrangeSuccessfulDelete(itemType: 4);
		ArrangeFileDesignMode(isOn: false);

		// Act
		int exitCode = _command.Execute(RemoteOptions());

		// Assert
		exitCode.Should().Be(0, because: "the platform deleted the schema");
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(message => message.Contains("table")));
	}

	[Test]
	[Description("Outside file system mode the package files are not touched: the cleanup is never called and nothing is reported about files.")]
	public void Execute_ShouldLeaveFilesAlone_WhenFileDesignModeIsOff() {
		// Arrange
		ArrangeSuccessfulDelete(itemType: 3);
		ArrangeFileDesignMode(isOn: false);

		// Act
		_command.Execute(RemoteOptions());

		// Assert
		_cleaner.DidNotReceive().Clean(Arg.Any<DeletedItemFileCleanupRequest>());
		_applicationClient.Received(1).ExecutePostRequest(FileDesignModeUrl, string.Empty, Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("In file system mode the deleted item, its package, its type, the environment and --ep are handed to the file cleanup.")]
	public void Execute_ShouldHandDeletedItemToFileCleanup_WhenFileDesignModeIsOn() {
		// Arrange
		ArrangeSuccessfulDelete(itemType: 3);
		ArrangeFileDesignMode(isOn: true);
		ArrangeCleanup(Result(DeletedItemFileCleanupStatus.Cleaned, packageFolder: "/site/Pkg/Custom"));
		DeleteSchemaOptions options = RemoteOptions();
		options.EnvironmentPath = "/sites/dev";

		// Act
		_command.Execute(options);

		// Assert
		_cleaner.Received(1).Clean(Arg.Is<DeletedItemFileCleanupRequest>(request =>
			request.EnvironmentName == "dev"
			&& request.EnvironmentPathOverride == "/sites/dev"
			&& request.PackageName == "Custom"
			&& request.ItemName == "UsrSchema"
			&& request.ItemType == 3));
	}

	[Test]
	[Description("A call made with a bare URI has no registered environment, so the cleanup is not given the active environment's name.")]
	public void Execute_ShouldNotPassActiveEnvironmentName_WhenCallUsesBareUri() {
		// Arrange
		ArrangeSuccessfulDelete(itemType: 3);
		ArrangeFileDesignMode(isOn: true);
		ArrangeCleanup(Result(DeletedItemFileCleanupStatus.NotCleaned, problem: "no site folder is known"));
		_command.EnvironmentSettings.EnvironmentName = "active-but-unrelated";
		DeleteSchemaOptions options = new() { SchemaName = "UsrSchema", Remote = true, Uri = TestBase };

		// Act
		_command.Execute(options);

		// Assert
		_cleaner.Received(1).Clean(Arg.Is<DeletedItemFileCleanupRequest>(request => request.EnvironmentName == null));
	}

	[Test]
	[Description("A call that names a registered environment but overrides its URI targets another site, so the cleanup is not given that environment's name.")]
	public void Execute_ShouldNotPassEnvironmentName_WhenUriOverridesRegisteredEnvironment() {
		// Arrange
		ArrangeSuccessfulDelete(itemType: 3);
		ArrangeFileDesignMode(isOn: true);
		ArrangeCleanup(Result(DeletedItemFileCleanupStatus.NotCleaned, problem: "no site folder is known"));
		DeleteSchemaOptions options = RemoteOptions();
		options.Uri = "https://another-site";

		// Act
		_command.Execute(options);

		// Assert
		_cleaner.Received(1).Clean(Arg.Is<DeletedItemFileCleanupRequest>(request => request.EnvironmentName == null));
	}

	[Test]
	[Description("A cleanup that throws after the database delete is reported as a warning and does not turn the delete into a failure.")]
	public void Execute_ShouldWarnAndSucceed_WhenFileCleanupThrows() {
		// Arrange
		ArrangeSuccessfulDelete(itemType: 3);
		ArrangeFileDesignMode(isOn: true);
		_cleaner.Clean(Arg.Any<DeletedItemFileCleanupRequest>()).Throws(new InvalidOperationException("settings unreadable"));

		// Act
		int exitCode = _command.Execute(RemoteOptions());

		// Assert
		exitCode.Should().Be(0, because: "the schema is already deleted and a retry could only fail with 'not found'");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("settings unreadable") && message.Contains("Schemas/UsrSchema/")));
	}

	[Test]
	[Description("When clio removed the folders, the result lists the removed folders and no warning.")]
	public void Execute_ShouldListRemovedFolders_WhenCleanupRemovedThem() {
		// Arrange
		ArrangeSuccessfulDelete(itemType: 3);
		ArrangeFileDesignMode(isOn: true);
		ArrangeCleanup(Result(DeletedItemFileCleanupStatus.Cleaned, packageFolder: "/site/Pkg/Custom",
			removed: ["Schemas/UsrSchema/", "Resources/UsrSchema.Entity/"]));

		// Act
		int exitCode = _command.Execute(RemoteOptions());

		// Assert
		exitCode.Should().Be(0, because: "both the platform delete and the file cleanup succeeded");
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("/site/Pkg/Custom")
			&& message.Contains("Schemas/UsrSchema/")
			&& message.Contains("Resources/UsrSchema.Entity/")));
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test]
	[Description("When the package folder holds no folders of the item, the result says where it searched.")]
	public void Execute_ShouldNameSearchedFolders_WhenNothingWasFound() {
		// Arrange
		ArrangeSuccessfulDelete(itemType: 3);
		ArrangeFileDesignMode(isOn: true);
		ArrangeCleanup(Result(DeletedItemFileCleanupStatus.Cleaned, packageFolder: "/site/Pkg/Custom"));

		// Act
		_command.Execute(RemoteOptions());

		// Assert
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("No folders of 'UsrSchema'") && message.Contains("Resources/UsrSchema.*/")));
	}

	[Test]
	[Description("In file system mode with an unreachable package folder, the result warns and names every folder left behind.")]
	public void Execute_ShouldWarnWithLeftoverFolders_WhenPackageFolderIsUnreachable() {
		// Arrange
		ArrangeSuccessfulDelete(itemType: 3);
		ArrangeFileDesignMode(isOn: true);
		ArrangeCleanup(Result(DeletedItemFileCleanupStatus.NotCleaned, problem: "no site folder is registered"));

		// Act
		int exitCode = _command.Execute(RemoteOptions());

		// Assert
		exitCode.Should().Be(0,
			because: "the database delete cannot be undone, so a non-zero code would only invite a retry that fails");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("file system mode")
			&& message.Contains("no site folder is registered")
			&& message.Contains("Schemas/UsrSchema/")
			&& message.Contains("Resources/UsrSchema.*/")
			&& message.Contains("pkg-to-db")));
	}

	[TestCase("{\"success\": false, \"errorInfo\": {\"message\": \"probe refused\"}}", "probe refused")]
	[TestCase("<html>login</html>", "GetIsFileDesignMode")]
	[Description("When the file design mode cannot be read, no file is touched and the result warns with the folders to check.")]
	public void Execute_ShouldWarnWithFoldersToCheck_WhenFileDesignModeIsUnknown(string probeResponse, string reason) {
		// Arrange
		ArrangeSuccessfulDelete(itemType: 3);
		_applicationClient.ExecutePostRequest(FileDesignModeUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(probeResponse);

		// Act
		int exitCode = _command.Execute(RemoteOptions());

		// Assert
		exitCode.Should().Be(0, because: "an unreadable mode never fails a completed delete");
		_cleaner.DidNotReceive().Clean(Arg.Any<DeletedItemFileCleanupRequest>());
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("Could not check whether the environment is in file system mode")
			&& message.Contains(reason)
			&& message.Contains("Schemas/UsrSchema/")));
	}

	[Test]
	[Description("The mode probe uses the delete's own connection with the caller's timeout and retry settings.")]
	public void Execute_ShouldProbeWithCommandTimeouts_WhenCheckingFileDesignMode() {
		// Arrange
		ArrangeSuccessfulDelete(itemType: 3);
		ArrangeFileDesignMode(isOn: false);
		DeleteSchemaOptions options = RemoteOptions();
		options.TimeOut = 4321;

		// Act
		_command.Execute(options);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(FileDesignModeUrl, string.Empty, 4321, Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Description("Folders kept on purpose because another item still uses them are reported as information, never as folders to remove by hand.")]
	public void Execute_ShouldReportKeptFoldersWithoutWarning_WhenCleanupKeptSharedResources() {
		// Arrange
		ArrangeSuccessfulDelete(itemType: 13);
		ArrangeFileDesignMode(isOn: true);
		ArrangeCleanup(new DeletedItemFileCleanupResult(DeletedItemFileCleanupStatus.Cleaned, "/site/Pkg/Custom",
			["Resources/UsrSchema.*/"], [], [], null, ["Resources/UsrSchema.*/ (the package also holds schema 'UsrSchema')"]));

		// Act
		_command.Execute(RemoteOptions());

		// Assert
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("Kept in package folder") && message.Contains("Resources/UsrSchema.*/")));
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test]
	[Description("Folders found but not removable are named in a warning next to the ones that were removed.")]
	public void Execute_ShouldWarnAboutRemainingFolders_WhenSomeFoldersCouldNotBeRemoved() {
		// Arrange
		ArrangeSuccessfulDelete(itemType: 3);
		ArrangeFileDesignMode(isOn: true);
		ArrangeCleanup(Result(DeletedItemFileCleanupStatus.Cleaned, packageFolder: "/site/Pkg/Custom",
			removed: ["Schemas/UsrSchema/"], remaining: ["Resources/UsrSchema.Entity/ (access denied)"]));

		// Act
		_command.Execute(RemoteOptions());

		// Assert
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("Resources/UsrSchema.Entity/ (access denied)") && message.Contains("pkg-to-db")));
	}

	[Test]
	[Description("A failed platform delete never reaches the file cleanup, so no file is removed for a schema that still exists.")]
	public void Execute_ShouldNotCleanFiles_WhenPlatformDeleteFails() {
		// Arrange
		ArrangeFileDesignMode(isOn: true);
		_applicationClient.ExecutePostRequest(GetWorkspaceItemsUrl, Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(GetWorkspaceItemsJson(type: 3));
		_applicationClient.ExecutePostRequest(DeleteUrl, Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": false, "rowsAffected": 0, "errorInfo": {"message": "has dependent objects"}}""");

		// Act
		int exitCode = _command.Execute(RemoteOptions());

		// Assert
		exitCode.Should().Be(1, because: "the platform refused the delete");
		_cleaner.DidNotReceive().Clean(Arg.Any<DeletedItemFileCleanupRequest>());
	}

	private static DeleteSchemaOptions RemoteOptions() => new() {
		SchemaName = "UsrSchema", Remote = true, Environment = "dev"
	};

	private void ArrangeSuccessfulDelete(int itemType) {
		_applicationClient.ExecutePostRequest(GetWorkspaceItemsUrl, Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(GetWorkspaceItemsJson(type: itemType));
		_applicationClient.ExecutePostRequest(DeleteUrl, Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true, "rowsAffected": 1}""");
	}

	private void ArrangeFileDesignMode(bool isOn) {
		_applicationClient.ExecutePostRequest(FileDesignModeUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(isOn ? """{"success": true, "value": true}""" : """{"success": true, "value": false}""");
	}

	private void ArrangeCleanup(DeletedItemFileCleanupResult result) {
		_cleaner.Clean(Arg.Any<DeletedItemFileCleanupRequest>()).Returns(result);
	}

	private static DeletedItemFileCleanupResult Result(DeletedItemFileCleanupStatus status,
		string packageFolder = null, string problem = null, List<string> removed = null,
		List<string> remaining = null) =>
		new(status, packageFolder, ["Schemas/UsrSchema/", "Resources/UsrSchema.*/"], removed ?? [], remaining ?? [],
			problem);
}
