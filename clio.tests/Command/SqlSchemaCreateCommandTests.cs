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
		_command = new SqlSchemaCreateCommand(_applicationClient, _serviceUrlBuilder, _logger);
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
}
