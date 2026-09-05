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
public sealed class SqlSchemaGetCommandTests {
	private const string TestBase = "http://test";
	private const string SelectQueryUrl = TestBase + "/DataService/json/SyncReply/SelectQuery";
	private const string GetSchemaUrl = TestBase + "/ServiceModel/ScriptSchemaDesignerService.svc/GetSchema";
	private const string SchemaUId = "aa000000-0000-0000-0000-000000000001";

	private static string SchemaFoundJson =>
		$$$"""{"success": true, "rows": [{"UId": "{{{SchemaUId}}}"}]}""";

	private static string GetSchemaSuccessJson =>
		$$$"""
		{
		  "success": true,
		  "schema": {
		    "uId": "{{{SchemaUId}}}",
		    "name": "UsrSqlScript",
		    "body": "SELECT 1;",
		    "caption": [{"cultureName": "en-US", "value": "Usr SQL Script"}],
		    "package": {"name": "Custom"}
		  }
		}
		""";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;
	private SqlSchemaGetCommand _command;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_serviceUrlBuilder.Build("ServiceModel/ScriptSchemaDesignerService.svc/GetSchema").Returns(GetSchemaUrl);
		_command = new SqlSchemaGetCommand(
			_applicationClient, _serviceUrlBuilder, new System.IO.Abstractions.FileSystem(), _logger);
	}

	[Test]
	public void TryGetSchema_Rejects_Missing_Schema_Name() {
		var options = new SqlSchemaGetOptions();

		bool result = _command.TryGetSchema(options, out SqlSchemaGetResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("schema-name");
	}

	[Test]
	public void TryGetSchema_Fails_When_Schema_Not_Found() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns("""{"success": true, "rows": []}""");
		var options = new SqlSchemaGetOptions { SchemaName = "UsrMissing" };

		bool result = _command.TryGetSchema(options, out SqlSchemaGetResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("UsrMissing").And.Contain("not found");
	}

	[Test]
	public void TryGetSchema_Returns_Body_And_Metadata_On_Success() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
		_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>()).Returns(GetSchemaSuccessJson);
		var options = new SqlSchemaGetOptions { SchemaName = "UsrSqlScript" };

		bool result = _command.TryGetSchema(options, out SqlSchemaGetResponse response);

		result.Should().BeTrue();
		response.Success.Should().BeTrue();
		response.SchemaName.Should().Be("UsrSqlScript");
		response.SchemaUId.Should().Be(SchemaUId);
		response.PackageName.Should().Be("Custom");
		response.Caption.Should().Be("Usr SQL Script");
		response.Body.Should().Be("SELECT 1;");
		response.BodyLength.Should().Be("SELECT 1;".Length);
	}

	[Test]
	public void TryGetSchema_Writes_Body_To_File_When_OutputFile_Provided() {
		// A fresh, non-existent path under the OS temp root: the confined writer is additive and refuses to
		// overwrite an existing file, so GetTempFileName() (which creates the file) can no longer be used here.
		string tempFile = Path.Combine(Path.GetTempPath(), "sqlget-" + System.Guid.NewGuid().ToString("N") + ".sql");
		try {
			_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
			_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>()).Returns(GetSchemaSuccessJson);
			var options = new SqlSchemaGetOptions { SchemaName = "UsrSqlScript", OutputFile = tempFile };

			bool result = _command.TryGetSchema(options, out SqlSchemaGetResponse response);

			result.Should().BeTrue();
			response.Body.Should().BeNull();
			response.BodyLength.Should().Be("SELECT 1;".Length);
			File.ReadAllText(tempFile).Should().Be("SELECT 1;");
		}
		finally {
			File.Delete(tempFile);
		}
	}

	[Test]
	public void TryGetSchema_Fails_When_GetSchema_Returns_No_Schema_Object() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
		_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>())
			.Returns("""{"success": false}""");
		var options = new SqlSchemaGetOptions { SchemaName = "UsrSqlScript" };

		bool result = _command.TryGetSchema(options, out SqlSchemaGetResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("UsrSqlScript").And.Contain("ScriptSchemaDesignerService");
	}

	[Test]
	[Description("An empty GetSchema body is reported as a classified, service-named failure rather than the raw Newtonsoft parser message (issue #1322).")]
	public void TryGetSchema_ShouldReportClassifiedFailure_WhenGetSchemaReturnsEmptyBody() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
		_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>()).Returns(string.Empty);
		var options = new SqlSchemaGetOptions { SchemaName = "UsrSqlScript" };

		// Act
		bool result = _command.TryGetSchema(options, out SqlSchemaGetResponse response);

		// Assert
		result.Should().BeFalse("an empty designer answer carries no schema");
		response.Error.Should().Contain("ScriptSchemaDesignerService GetSchema",
				"the caller must learn which service and operation answered with nothing")
			.And.Contain(GetSchemaUrl, "the endpoint URL is what makes a missing route diagnosable")
			.And.NotContain("Error reading JObject",
				"the bare Newtonsoft parser message is exactly what issue #1322 reported as unactionable");
	}

	[Test]
	[Description("An HTML page from GetSchema is classified as such, names the service and URL, and its markup is never echoed back.")]
	public void TryGetSchema_ShouldNotEchoMarkup_WhenGetSchemaReturnsHtmlPage() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
		_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>())
			.Returns("<!DOCTYPE html><html><body>Login<input value=\"topsecret\"/></body></html>");
		var options = new SqlSchemaGetOptions { SchemaName = "UsrSqlScript" };

		// Act
		bool result = _command.TryGetSchema(options, out SqlSchemaGetResponse response);

		// Assert
		result.Should().BeFalse("an HTML page is not a designer payload");
		response.Error.Should().Contain("ScriptSchemaDesignerService GetSchema",
				"the caller must learn which service and operation answered with a page")
			.And.Contain(GetSchemaUrl, "the endpoint URL is what makes a redirected request diagnosable")
			.And.Contain("HTML page instead of JSON", "the cause must be classified, not guessed at")
			.And.NotContain("topsecret",
				"a login or error page can carry session tokens, so the body is never echoed back")
			.And.NotContain("Error reading JObject",
				"the bare Newtonsoft parser message is exactly what issue #1322 reported as unactionable");
	}
}
