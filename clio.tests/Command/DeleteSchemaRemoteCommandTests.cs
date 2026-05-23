namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Common;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class DeleteSchemaRemoteCommandTests {
	private const string TestBase = "http://test";
	private const string SelectQueryUrl = TestBase + "/DataService/json/SyncReply/SelectQuery";
	private const string DeleteUrl = TestBase + "/ServiceModel/WorkspaceExplorerService.svc/Delete";
	private const string SchemaId = "11111111-1111-1111-1111-111111111111";
	private const string SchemaUId = "22222222-2222-2222-2222-222222222222";
	private const string PackageUId = "33333333-3333-3333-3333-333333333333";

	private static string SchemaFoundJson =>
		$$$"""
		{
		  "success": true,
		  "rows": [
		    {
		      "Id": "{{{SchemaId}}}",
		      "UId": "{{{SchemaUId}}}",
		      "ManagerName": "SourceCodeSchemaManager",
		      "PackageUId": "{{{PackageUId}}}",
		      "PackageName": "Custom"
		    }
		  ]
		}
		""";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private DeleteSchemaCommand _command;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectQueryUrl);
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DeleteWorkspaceItem).Returns(DeleteUrl);
		_command = new DeleteSchemaCommand(
			_applicationClient,
			new EnvironmentSettings { Uri = TestBase, Login = "u", Password = "p", IsNetCore = true },
			_serviceUrlBuilder,
			Substitute.For<IWorkspacePathBuilder>(),
			Substitute.For<IJsonConverter>(),
			Substitute.For<IFileSystem>()) {
			ApplicationClient = _applicationClient
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
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true, "rows": []}""");

		bool result = _command.TryDeleteRemote("UsrMissing", out DeleteSchemaRemoteResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("UsrMissing").And.Contain("not found");
	}

	[Test]
	public void TryDeleteRemote_Happy_Path_Calls_Delete_Endpoint() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(SchemaFoundJson);
		_applicationClient.ExecutePostRequest(DeleteUrl, Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true, "rowsAffected": 1}""");

		bool result = _command.TryDeleteRemote("UsrSchema", out DeleteSchemaRemoteResponse response);

		result.Should().BeTrue();
		response.Success.Should().BeTrue();
		response.SchemaName.Should().Be("UsrSchema");
		response.SchemaUId.Should().Be(SchemaUId);
		response.PackageName.Should().Be("Custom");
		response.ManagerName.Should().Be("SourceCodeSchemaManager");
		_applicationClient.Received(1).ExecutePostRequest(DeleteUrl,
			Arg.Is<string>(s => s.Contains(SchemaUId) && s.Contains("Custom")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	public void TryDeleteRemote_Surfaces_Delete_Error() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(SchemaFoundJson);
		_applicationClient.ExecutePostRequest(DeleteUrl, Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": false, "rowsAffected": 0, "errorInfo": {"message": "locked"}}""");

		bool result = _command.TryDeleteRemote("UsrSchema", out DeleteSchemaRemoteResponse response);

		result.Should().BeFalse();
		response.Error.Should().Be("locked");
		response.SchemaUId.Should().Be(SchemaUId);
	}
}
