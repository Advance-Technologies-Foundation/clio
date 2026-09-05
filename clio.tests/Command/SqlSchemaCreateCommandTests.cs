namespace Clio.Tests.Command;

using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class SqlSchemaCreateCommandTests {
	private const string TestBase = "http://test";
	private const string SelectQueryUrl = TestBase + "/DataService/json/SyncReply/SelectQuery";
	private const string CreateNewSchemaUrl = TestBase + "/ServiceModel/ScriptSchemaDesignerService.svc/CreateNewSchema";
	private const string SaveSchemaUrl = TestBase + "/ServiceModel/ScriptSchemaDesignerService.svc/SaveSchema";
	private const string PackageUId = "aa000000-0000-0000-0000-000000000001";
	private const string GeneratedSchemaUId = "bb000000-0000-0000-0000-000000000002";

	private static string SchemaPayloadJson =>
		"""{"success": true, "schema": {"uId": "SCHEMA_UID", "name": "UsrSqlScript1", "body": " ", "caption": [], "description": []}}"""
		.Replace("SCHEMA_UID", GeneratedSchemaUId);

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;
	private SqlSchemaCreateCommand _command;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_serviceUrlBuilder.Build("ServiceModel/ScriptSchemaDesignerService.svc/CreateNewSchema").Returns(CreateNewSchemaUrl);
		_serviceUrlBuilder.Build("ServiceModel/ScriptSchemaDesignerService.svc/SaveSchema").Returns(SaveSchemaUrl);
		_command = new SqlSchemaCreateCommand(_applicationClient, _serviceUrlBuilder, _logger,
			Substitute.For<Clio.Command.EntitySchemaDesigner.ICaptionCultureResolver>());
	}

	[Test]
	public void TryCreate_Rejects_Missing_Schema_Name() {
		var options = new SqlSchemaCreateOptions { PackageName = "Custom" };

		bool result = _command.TryCreate(options, out SqlSchemaCreateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("schema-name");
	}

	[Test]
	public void TryCreate_Rejects_Malformed_Schema_Name() {
		var options = new SqlSchemaCreateOptions { SchemaName = "1Invalid", PackageName = "Custom" };

		bool result = _command.TryCreate(options, out SqlSchemaCreateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("schema-name must start with a letter");
	}

	[Test]
	public void TryCreate_Rejects_Missing_Package_Name() {
		var options = new SqlSchemaCreateOptions { SchemaName = "UsrMySqlScript" };

		bool result = _command.TryCreate(options, out SqlSchemaCreateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("package-name");
	}

	[Test]
	public void TryCreate_Rejects_Missing_Package() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns("""{"success": true, "rows": []}""");
		var options = new SqlSchemaCreateOptions { SchemaName = "UsrMySqlScript", PackageName = "DoesNotExist" };

		bool result = _command.TryCreate(options, out SqlSchemaCreateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("DoesNotExist").And.Contain("not found");
	}

	[Test]
	public void TryCreate_Rejects_Duplicate_Schema_Name() {
		var selectResponses = new Queue<string>([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": [{"UId": "11111111-2222-3333-4444-555555555555"}]}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		var options = new SqlSchemaCreateOptions { SchemaName = "UsrExisting", PackageName = "Custom" };

		bool result = _command.TryCreate(options, out SqlSchemaCreateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("already exists");
	}

	[Test]
	public void TryCreate_Happy_Path_Calls_CreateNewSchema_Then_SaveSchema() {
		var selectResponses = new Queue<string>([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": []}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		_applicationClient.ExecutePostRequest(CreateNewSchemaUrl, Arg.Any<string>()).Returns(SchemaPayloadJson);
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>()).Returns("""{"success": true}""");
		var options = new SqlSchemaCreateOptions {
			SchemaName = "UsrMySqlScript",
			PackageName = "Custom",
			Caption = "My SQL Script"
		};

		bool result = _command.TryCreate(options, out SqlSchemaCreateResponse response);

		result.Should().BeTrue();
		response.Success.Should().BeTrue();
		response.SchemaName.Should().Be("UsrMySqlScript");
		response.SchemaUId.Should().Be(GeneratedSchemaUId);
		response.PackageUId.Should().Be(PackageUId);
		response.Caption.Should().Be("My SQL Script");
		_applicationClient.Received(1).ExecutePostRequest(CreateNewSchemaUrl,
			Arg.Is<string>(s => s.Contains(PackageUId)));
		_applicationClient.Received(1).ExecutePostRequest(SaveSchemaUrl,
			Arg.Is<string>(s => s.Contains("UsrMySqlScript") && s.Contains("My SQL Script")));
	}

	[Test]
	public void TryCreate_Surfaces_SaveSchema_Error() {
		var selectResponses = new Queue<string>([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": []}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		_applicationClient.ExecutePostRequest(CreateNewSchemaUrl, Arg.Any<string>()).Returns(SchemaPayloadJson);
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>())
			.Returns("""{"success": false, "errorInfo": {"message": "script conflict"}}""");
		var options = new SqlSchemaCreateOptions { SchemaName = "UsrMySqlScript", PackageName = "Custom" };

		bool result = _command.TryCreate(options, out SqlSchemaCreateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Be("script conflict");
	}

	[Test]
	[Description("An empty CreateNewSchema body is reported as a named service failure, not as the raw Newtonsoft parser message (issue #1322).")]
	public void TryCreate_Reports_Empty_CreateNewSchema_Response_With_Service_And_Url() {
		// Arrange
		var selectResponses = new Queue<string>([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": []}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		_applicationClient.ExecutePostRequest(CreateNewSchemaUrl, Arg.Any<string>()).Returns(string.Empty);
		var options = new SqlSchemaCreateOptions { SchemaName = "UsrMySqlScript", PackageName = "Custom" };

		// Act
		bool result = _command.TryCreate(options, out SqlSchemaCreateResponse response);

		// Assert
		result.Should().BeFalse("an empty designer response means the schema was not created");
		response.Error.Should().Contain("ScriptSchemaDesignerService CreateNewSchema",
				"the caller must learn which service and operation answered with nothing")
			.And.Contain(CreateNewSchemaUrl, "the endpoint URL is what makes a missing route diagnosable")
			.And.Contain("unlocked", "the message must carry an actionable hint")
			.And.NotContain("Error reading JObject",
				"the bare Newtonsoft parser message is exactly what issue #1322 reported as unactionable");
	}

	[Test]
	[Description("An HTML login/error page from CreateNewSchema is classified without echoing the markup.")]
	public void TryCreate_Reports_Html_CreateNewSchema_Response_Without_Echoing_Markup() {
		// Arrange
		var selectResponses = new Queue<string>([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": []}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		_applicationClient.ExecutePostRequest(CreateNewSchemaUrl, Arg.Any<string>())
			.Returns("<!DOCTYPE html><html><body>Login<input value=\"topsecret\"/></body></html>");
		var options = new SqlSchemaCreateOptions { SchemaName = "UsrMySqlScript", PackageName = "Custom" };

		// Act
		bool result = _command.TryCreate(options, out SqlSchemaCreateResponse response);

		// Assert
		result.Should().BeFalse("an HTML page is not a successful designer response");
		response.Error.Should().Contain("HTML page instead of JSON",
				"the caller must be told the request did not reach the designer service")
			.And.NotContain("topsecret",
				"a login or error page can carry session tokens, so the body is never echoed back");
	}

	[Test]
	[Description("An unusable SaveSchema response is verified by reading the schema back, and reports success when the schema exists.")]
	public void TryCreate_Verifies_Unknown_Save_Outcome_And_Reports_Success_When_Schema_Exists() {
		// Arrange
		var selectResponses = new Queue<string>([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": []}""",
			$$"""{"success": true, "rows": [{"UId": "{{GeneratedSchemaUId}}"}]}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		_applicationClient.ExecutePostRequest(CreateNewSchemaUrl, Arg.Any<string>()).Returns(SchemaPayloadJson);
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>()).Returns(string.Empty);
		var options = new SqlSchemaCreateOptions { SchemaName = "UsrMySqlScript", PackageName = "Custom" };

		// Act
		bool result = _command.TryCreate(options, out SqlSchemaCreateResponse response);

		// Assert
		result.Should().BeTrue("the read-back proves the save was applied despite the lost answer");
		response.SchemaUId.Should().Be(GeneratedSchemaUId,
			"the verified UId is the one the environment actually holds");
	}

	[Test]
	[Description("An unusable SaveSchema response whose read-back finds no schema is reported as a failure.")]
	public void TryCreate_Reports_Failure_When_Unknown_Save_Outcome_Reads_Back_Missing() {
		// Arrange
		var selectResponses = new Queue<string>([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": []}""",
			"""{"success": true, "rows": []}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		_applicationClient.ExecutePostRequest(CreateNewSchemaUrl, Arg.Any<string>()).Returns(SchemaPayloadJson);
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>()).Returns(string.Empty);
		var options = new SqlSchemaCreateOptions { SchemaName = "UsrMySqlScript", PackageName = "Custom" };

		// Act
		bool result = _command.TryCreate(options, out SqlSchemaCreateResponse response);

		// Assert
		result.Should().BeFalse("the read-back shows the schema was not written");
		response.Error.Should().Contain("ScriptSchemaDesignerService SaveSchema",
			"the failure must still name the service whose answer was unusable");
	}

	[Test]
	[Description("When the read-back after an unusable SaveSchema response itself fails, the outcome is reported as unverified rather than as a failure.")]
	public void TryCreate_Reports_Unverified_When_Read_Back_Itself_Fails() {
		// Arrange
		var selectResponses = new Queue<string>([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": []}""",
			string.Empty
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		_applicationClient.ExecutePostRequest(CreateNewSchemaUrl, Arg.Any<string>()).Returns(SchemaPayloadJson);
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>()).Returns(string.Empty);
		var options = new SqlSchemaCreateOptions { SchemaName = "UsrMySqlScript", PackageName = "Custom" };

		// Act
		bool result = _command.TryCreate(options, out SqlSchemaCreateResponse response);

		// Assert
		result.Should().BeFalse("an unverified outcome must not be reported as a success");
		response.Error.Should().Contain("could not be verified",
				"the caller must be told the result is unknown rather than observed to have failed")
			.And.Contain("UsrMySqlScript", "the caller needs the schema name to check the environment manually");
	}

	[Test]
	[Description("A failed duplicate-name check aborts instead of proceeding to create, so a transport failure is never read as 'the schema does not exist'.")]
	public void TryCreate_Aborts_When_Duplicate_Check_Cannot_Be_Answered() {
		// Arrange
		var selectResponses = new Queue<string>([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			string.Empty
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		var options = new SqlSchemaCreateOptions { SchemaName = "UsrMySqlScript", PackageName = "Custom" };

		// Act
		bool result = _command.TryCreate(options, out SqlSchemaCreateResponse response);

		// Assert
		result.Should().BeFalse("an unanswerable duplicate check is a failure, not a licence to create");
		response.Error.Should().Contain("SelectQuery",
			"the caller must learn which request could not be answered");
		_applicationClient.Received(0).ExecutePostRequest(CreateNewSchemaUrl, Arg.Any<string>());
	}
}
