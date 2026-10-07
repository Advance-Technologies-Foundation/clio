namespace Clio.Tests.Command;

using Clio.Command;
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
		ArrangeCleanup(Result(DeletedItemFileCleanupStatus.FileSystemModeOff));

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
		ArrangeCleanup(Result(DeletedItemFileCleanupStatus.FileSystemModeOff));

		// Act
		int exitCode = _command.Execute(RemoteOptions());

		// Assert
		exitCode.Should().Be(0, because: "the platform deleted the schema");
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(message => message.Contains("table")));
	}

	[Test]
	[Description("After a successful remote delete the deleted item, its package, its type and the environment are handed to the file cleanup.")]
	public void Execute_ShouldHandDeletedItemToFileCleanup_WhenRemoteDeleteSucceeds() {
		// Arrange
		ArrangeSuccessfulDelete(itemType: 3);
		ArrangeCleanup(Result(DeletedItemFileCleanupStatus.FileSystemModeOff));
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
		ArrangeCleanup(Result(DeletedItemFileCleanupStatus.FileSystemModeUnknown, problem: "no registered environment"));
		_command.EnvironmentSettings.EnvironmentName = "active-but-unrelated";
		DeleteSchemaOptions options = new() { SchemaName = "UsrSchema", Remote = true, Uri = TestBase };

		// Act
		_command.Execute(options);

		// Assert
		_cleaner.Received(1).Clean(Arg.Is<DeletedItemFileCleanupRequest>(request => request.EnvironmentName == null));
	}

	[Test]
	[Description("When file system mode is on and clio removed the folders, the result lists the removed folders and no warning.")]
	public void Execute_ShouldListRemovedFolders_WhenCleanupRemovedThem() {
		// Arrange
		ArrangeSuccessfulDelete(itemType: 3);
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
	[Description("When file system mode is on but the package folder is unreachable, the result warns and names every folder left behind.")]
	public void Execute_ShouldWarnWithLeftoverFolders_WhenPackageFolderIsUnreachable() {
		// Arrange
		ArrangeSuccessfulDelete(itemType: 3);
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

	[Test]
	[Description("When the file system mode cannot be read, the result warns and names the folders to check.")]
	public void Execute_ShouldWarnWithFoldersToCheck_WhenFileSystemModeIsUnknown() {
		// Arrange
		ArrangeSuccessfulDelete(itemType: 3);
		ArrangeCleanup(Result(DeletedItemFileCleanupStatus.FileSystemModeUnknown, problem: "probe timed out"));

		// Act
		_command.Execute(RemoteOptions());

		// Assert
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("Could not check whether the environment is in file system mode")
			&& message.Contains("probe timed out")
			&& message.Contains("Schemas/UsrSchema/")));
	}

	[Test]
	[Description("Folders found but not removable are named in a warning next to the ones that were removed.")]
	public void Execute_ShouldWarnAboutRemainingFolders_WhenSomeFoldersCouldNotBeRemoved() {
		// Arrange
		ArrangeSuccessfulDelete(itemType: 3);
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

	private void ArrangeCleanup(DeletedItemFileCleanupResult result) {
		_cleaner.Clean(Arg.Any<DeletedItemFileCleanupRequest>()).Returns(result);
	}

	private static DeletedItemFileCleanupResult Result(DeletedItemFileCleanupStatus status,
		string packageFolder = null, string problem = null, List<string> removed = null,
		List<string> remaining = null) =>
		new(status, packageFolder, ["Schemas/UsrSchema/", "Resources/UsrSchema.*/"], removed ?? [], remaining ?? [],
			problem);
}
