namespace Clio.Tests.Command;

using Clio.Command;
using Newtonsoft.Json.Linq;
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
	private const string ExecuteScriptUrl = TestBase + "/ServiceModel/WorkspaceExplorerService.svc/InstallSqlScripts";
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
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectQueryUrl);
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.InstallSqlScripts).Returns(ExecuteScriptUrl);
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
		_applicationClient.ExecuteNonReplayablePostRequest(ExecuteScriptUrl, Arg.Any<string>())
			.Returns("""{"success": true}""");
		var options = new SqlSchemaInstallOptions { SchemaName = "UsrSqlScript" };

		bool result = _command.TryInstall(options, out SqlSchemaInstallResponse response);

		result.Should().BeTrue();
		response.Success.Should().BeTrue();
		response.SchemaName.Should().Be("UsrSqlScript");
		response.SchemaUId.Should().Be(SchemaUId);
		_applicationClient.Received(1).ExecuteNonReplayablePostRequest(ExecuteScriptUrl,
			Arg.Is<string>(s => JToken.DeepEquals(JToken.Parse(s), new JArray(SchemaUId))));
	}

	[Test]
	public void TryInstall_Surfaces_ExecuteScript_Error() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
		_applicationClient.ExecuteNonReplayablePostRequest(ExecuteScriptUrl, Arg.Any<string>())
			.Returns("""{"success": false, "errorInfo": {"message": "db failure"}}""");
		var options = new SqlSchemaInstallOptions { SchemaName = "UsrSqlScript" };

		bool result = _command.TryInstall(options, out SqlSchemaInstallResponse response);

		result.Should().BeFalse();
		response.Error.Should().Be("db failure");
		response.SchemaUId.Should().Be(SchemaUId);
	}
	[Test]
	[Description("A lost execution response must warn against blindly repeating non-idempotent SQL.")]
	public void TryInstall_ShouldReportUnknownOutcome_WhenTransportThrows() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
		_applicationClient.ExecuteNonReplayablePostRequest(ExecuteScriptUrl, Arg.Any<string>())
			.Returns(_ => throw new System.Net.Http.HttpRequestException("connection reset"));
		// Act
		bool result = _command.TryInstall(new() { SchemaName = "UsrSql" }, out SqlSchemaInstallResponse response);
		// Assert
		result.Should().BeFalse(because: "a lost response does not establish execution success");
		response.Error.Should().Contain("outcome is unknown", because: "the server may have committed before the connection reset");
		response.Error.Should().Contain("verify database effects before retrying", because: "manual replay may duplicate a non-idempotent write");
		_applicationClient.Received(1).ExecuteNonReplayablePostRequest(ExecuteScriptUrl, Arg.Any<string>());
	}

}
