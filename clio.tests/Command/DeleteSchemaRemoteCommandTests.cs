namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Common;
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
	private DeleteSchemaCommand _command;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetWorkspaceItems).Returns(GetWorkspaceItemsUrl);
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
}
