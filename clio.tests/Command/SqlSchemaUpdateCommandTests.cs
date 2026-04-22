namespace Clio.Tests.Command;

using System.IO;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class SqlSchemaUpdateCommandTests {
	private const string TestBase = "http://test";
	private const string SelectQueryUrl = TestBase + "/DataService/json/SyncReply/SelectQuery";
	private const string GetSchemaUrl = TestBase + "/ServiceModel/ScriptSchemaDesignerService.svc/GetSchema";
	private const string SaveSchemaUrl = TestBase + "/ServiceModel/ScriptSchemaDesignerService.svc/SaveSchema";
	private const string SchemaUId = "aa000000-0000-0000-0000-000000000001";

	private static string SchemaFoundJson =>
		$$$"""{"success": true, "rows": [{"UId": "{{{SchemaUId}}}"}]}""";

	private static string GetSchemaSuccessJson =>
		$$$"""{"success": true, "schema": {"uId": "{{{SchemaUId}}}", "name": "UsrSqlScript", "body": "-- old"}}""";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;
	private SqlSchemaUpdateCommand _command;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_serviceUrlBuilder.Build("ServiceModel/ScriptSchemaDesignerService.svc/GetSchema").Returns(GetSchemaUrl);
		_serviceUrlBuilder.Build("ServiceModel/ScriptSchemaDesignerService.svc/SaveSchema").Returns(SaveSchemaUrl);
		_command = new SqlSchemaUpdateCommand(_applicationClient, _serviceUrlBuilder, _logger);
	}

	[Test]
	public void TryUpdateSchema_Rejects_Missing_Schema_Name() {
		var options = new SqlSchemaUpdateOptions { Body = "SELECT 1;" };

		bool result = _command.TryUpdateSchema(options, out SqlSchemaUpdateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("schema-name");
	}

	[Test]
	public void TryUpdateSchema_Rejects_Missing_Body_And_BodyFile() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
		var options = new SqlSchemaUpdateOptions { SchemaName = "UsrSqlScript" };

		bool result = _command.TryUpdateSchema(options, out SqlSchemaUpdateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("body");
	}

	[Test]
	public void TryUpdateSchema_Fails_When_Schema_Not_Found() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns("""{"success": true, "rows": []}""");
		var options = new SqlSchemaUpdateOptions { SchemaName = "UsrMissing", Body = "SELECT 1;" };

		bool result = _command.TryUpdateSchema(options, out SqlSchemaUpdateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("UsrMissing").And.Contain("not found");
	}

	[Test]
	public void TryUpdateSchema_Reads_Body_From_File() {
		string tempFile = Path.GetTempFileName();
		try {
			File.WriteAllText(tempFile, "-- from file");
			_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
			_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>()).Returns(GetSchemaSuccessJson);
			_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>()).Returns("""{"success": true}""");
			var options = new SqlSchemaUpdateOptions { SchemaName = "UsrSqlScript", BodyFile = tempFile };

			bool result = _command.TryUpdateSchema(options, out SqlSchemaUpdateResponse response);

			result.Should().BeTrue();
			response.Success.Should().BeTrue();
			_applicationClient.Received(1).ExecutePostRequest(SaveSchemaUrl,
				Arg.Is<string>(s => s.Contains("from file")));
		}
		finally {
			File.Delete(tempFile);
		}
	}

	[Test]
	public void TryUpdateSchema_DryRun_Does_Not_Call_GetSchema_Or_SaveSchema() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
		var options = new SqlSchemaUpdateOptions {
			SchemaName = "UsrSqlScript",
			Body = "SELECT 1;",
			DryRun = true
		};

		bool result = _command.TryUpdateSchema(options, out SqlSchemaUpdateResponse response);

		result.Should().BeTrue();
		response.Success.Should().BeTrue();
		response.DryRun.Should().BeTrue();
		_applicationClient.DidNotReceive().ExecutePostRequest(GetSchemaUrl, Arg.Any<string>());
		_applicationClient.DidNotReceive().ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>());
	}

	[Test]
	public void TryUpdateSchema_Happy_Path_Calls_GetSchema_Then_SaveSchema() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
		_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>()).Returns(GetSchemaSuccessJson);
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>()).Returns("""{"success": true}""");
		var options = new SqlSchemaUpdateOptions {
			SchemaName = "UsrSqlScript",
			Body = "SELECT 2;"
		};

		bool result = _command.TryUpdateSchema(options, out SqlSchemaUpdateResponse response);

		result.Should().BeTrue();
		response.Success.Should().BeTrue();
		response.SchemaName.Should().Be("UsrSqlScript");
		response.BodyLength.Should().Be("SELECT 2;".Length);
		response.DryRun.Should().BeFalse();
		_applicationClient.Received(1).ExecutePostRequest(GetSchemaUrl,
			Arg.Is<string>(s => s.Contains(SchemaUId)));
		_applicationClient.Received(1).ExecutePostRequest(SaveSchemaUrl,
			Arg.Is<string>(s => s.Contains("SELECT 2;")));
	}

	[Test]
	public void TryUpdateSchema_Surfaces_SaveSchema_Error() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
		_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>()).Returns(GetSchemaSuccessJson);
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>())
			.Returns("""{"success": false, "errorInfo": {"message": "sql error"}}""");
		var options = new SqlSchemaUpdateOptions { SchemaName = "UsrSqlScript", Body = "BAD" };

		bool result = _command.TryUpdateSchema(options, out SqlSchemaUpdateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Be("sql error");
	}
}
