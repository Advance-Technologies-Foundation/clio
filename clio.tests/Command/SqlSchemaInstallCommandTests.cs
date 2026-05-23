namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class SqlSchemaInstallCommandTests {
	private const string TestBase = "http://test";
	private const string SelectQueryUrl = TestBase + "/DataService/json/SyncReply/SelectQuery";
	private const string ExecuteScriptUrl = TestBase + "/ServiceModel/ScriptSchemaDesignerService.svc/ExecuteScript";
	private const string SchemaUId = "aa000000-0000-0000-0000-000000000001";

	private static string SchemaFoundJson =>
		$$$"""{"success": true, "rows": [{"UId": "{{{SchemaUId}}}"}]}""";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;
	private SqlSchemaInstallCommand _command;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_serviceUrlBuilder.Build("ServiceModel/ScriptSchemaDesignerService.svc/ExecuteScript").Returns(ExecuteScriptUrl);
		_command = new SqlSchemaInstallCommand(_applicationClient, _serviceUrlBuilder, _logger);
	}

	[Test]
	public void TryInstall_Rejects_Missing_Schema_Name() {
		var options = new SqlSchemaInstallOptions();

		bool result = _command.TryInstall(options, out SqlSchemaInstallResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("schema-name");
	}

	[Test]
	public void TryInstall_Fails_When_Schema_Not_Found() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns("""{"success": true, "rows": []}""");
		var options = new SqlSchemaInstallOptions { SchemaName = "UsrMissing" };

		bool result = _command.TryInstall(options, out SqlSchemaInstallResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("UsrMissing").And.Contain("not found");
	}

	[Test]
	public void TryInstall_Happy_Path_Calls_ExecuteScript() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
		_applicationClient.ExecutePostRequest(ExecuteScriptUrl, Arg.Any<string>())
			.Returns("""{"success": true}""");
		var options = new SqlSchemaInstallOptions { SchemaName = "UsrSqlScript" };

		bool result = _command.TryInstall(options, out SqlSchemaInstallResponse response);

		result.Should().BeTrue();
		response.Success.Should().BeTrue();
		response.SchemaName.Should().Be("UsrSqlScript");
		response.SchemaUId.Should().Be(SchemaUId);
		_applicationClient.Received(1).ExecutePostRequest(ExecuteScriptUrl,
			Arg.Is<string>(s => s.Contains(SchemaUId)));
	}

	[Test]
	public void TryInstall_Surfaces_ExecuteScript_Error() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
		_applicationClient.ExecutePostRequest(ExecuteScriptUrl, Arg.Any<string>())
			.Returns("""{"success": false, "errorInfo": {"message": "db failure"}}""");
		var options = new SqlSchemaInstallOptions { SchemaName = "UsrSqlScript" };

		bool result = _command.TryInstall(options, out SqlSchemaInstallResponse response);

		result.Should().BeFalse();
		response.Error.Should().Be("db failure");
		response.SchemaUId.Should().Be(SchemaUId);
	}
}
