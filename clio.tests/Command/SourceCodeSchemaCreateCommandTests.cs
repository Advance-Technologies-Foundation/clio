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
public sealed class SourceCodeSchemaCreateCommandTests {
	private const string TestBase = "http://test";
	private const string SelectQueryUrl = TestBase + "/DataService/json/SyncReply/SelectQuery";
	private const string CreateNewSchemaUrl = TestBase + "/ServiceModel/SourceCodeSchemaDesignerService.svc/CreateNewSchema";
	private const string SaveSchemaUrl = TestBase + "/ServiceModel/SourceCodeSchemaDesignerService.svc/SaveSchema";
	private const string PackageUId = "aa000000-0000-0000-0000-000000000001";
	private const string GeneratedSchemaUId = "bb000000-0000-0000-0000-000000000002";

	private static string SchemaPayloadJson =>
		"""{"success": true, "schema": {"uId": "SCHEMA_UID", "name": "UsrSourceCodeSchema1", "body": " ", "caption": [], "description": []}}"""
		.Replace("SCHEMA_UID", GeneratedSchemaUId);

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;
	private SourceCodeSchemaCreateCommand _command;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_serviceUrlBuilder.Build("ServiceModel/SourceCodeSchemaDesignerService.svc/CreateNewSchema")
			.Returns(CreateNewSchemaUrl);
		_serviceUrlBuilder.Build("ServiceModel/SourceCodeSchemaDesignerService.svc/SaveSchema")
			.Returns(SaveSchemaUrl);
		_command = new SourceCodeSchemaCreateCommand(_applicationClient, _serviceUrlBuilder, _logger);
	}

	[Test]
	public void TryCreate_Rejects_Missing_Schema_Name() {
		var options = new SourceCodeSchemaCreateOptions { PackageName = "Custom" };

		bool result = _command.TryCreate(options, out SourceCodeSchemaCreateResponse response);

		result.Should().BeFalse();
		response.Success.Should().BeFalse();
		response.Error.Should().Contain("schema-name");
	}

	[Test]
	public void TryCreate_Rejects_Malformed_Schema_Name() {
		var options = new SourceCodeSchemaCreateOptions { SchemaName = "1Invalid", PackageName = "Custom" };

		bool result = _command.TryCreate(options, out SourceCodeSchemaCreateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("schema-name must start with a letter");
	}

	[Test]
	public void TryCreate_Rejects_Missing_Package_Name() {
		var options = new SourceCodeSchemaCreateOptions { SchemaName = "UsrMyHelper" };

		bool result = _command.TryCreate(options, out SourceCodeSchemaCreateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("package-name");
	}

	[Test]
	public void TryCreate_Rejects_Missing_Package() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns("""{"success": true, "rows": []}""");
		var options = new SourceCodeSchemaCreateOptions { SchemaName = "UsrMyHelper", PackageName = "DoesNotExist" };

		bool result = _command.TryCreate(options, out SourceCodeSchemaCreateResponse response);

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
		var options = new SourceCodeSchemaCreateOptions { SchemaName = "UsrExisting", PackageName = "Custom" };

		bool result = _command.TryCreate(options, out SourceCodeSchemaCreateResponse response);

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
		_applicationClient.ExecutePostRequest(CreateNewSchemaUrl, Arg.Any<string>())
			.Returns(SchemaPayloadJson);
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>())
			.Returns("""{"success": true}""");
		var options = new SourceCodeSchemaCreateOptions {
			SchemaName = "UsrMyHelper",
			PackageName = "Custom",
			Caption = "My Helper"
		};

		bool result = _command.TryCreate(options, out SourceCodeSchemaCreateResponse response);

		result.Should().BeTrue();
		response.Success.Should().BeTrue();
		response.SchemaName.Should().Be("UsrMyHelper");
		response.SchemaUId.Should().Be(GeneratedSchemaUId);
		response.PackageUId.Should().Be(PackageUId);
		response.Caption.Should().Be("My Helper");
		_applicationClient.Received(1).ExecutePostRequest(CreateNewSchemaUrl,
			Arg.Is<string>(s => s.Contains(PackageUId)));
		_applicationClient.Received(1).ExecutePostRequest(SaveSchemaUrl,
			Arg.Is<string>(s => s.Contains("UsrMyHelper") && s.Contains("My Helper")));
	}

	[Test]
	public void TryCreate_Uses_SchemaName_As_Caption_When_Caption_Omitted() {
		var selectResponses = new Queue<string>([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": []}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		_applicationClient.ExecutePostRequest(CreateNewSchemaUrl, Arg.Any<string>())
			.Returns(SchemaPayloadJson);
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>())
			.Returns("""{"success": true}""");
		var options = new SourceCodeSchemaCreateOptions { SchemaName = "UsrMyHelper", PackageName = "Custom" };

		_command.TryCreate(options, out SourceCodeSchemaCreateResponse response);

		response.Caption.Should().Be("UsrMyHelper");
		_applicationClient.Received(1).ExecutePostRequest(SaveSchemaUrl,
			Arg.Is<string>(s => s.Contains("UsrMyHelper")));
	}

	[Test]
	public void TryCreate_Surfaces_SaveSchema_Error() {
		var selectResponses = new Queue<string>([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": []}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		_applicationClient.ExecutePostRequest(CreateNewSchemaUrl, Arg.Any<string>())
			.Returns(SchemaPayloadJson);
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>())
			.Returns("""{"success": false, "errorInfo": {"message": "schema name conflict"}}""");
		var options = new SourceCodeSchemaCreateOptions { SchemaName = "UsrMyHelper", PackageName = "Custom" };

		bool result = _command.TryCreate(options, out SourceCodeSchemaCreateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Be("schema name conflict");
	}

	[Test]
	public void TryCreate_Fails_When_CreateNewSchema_Returns_No_Schema() {
		var selectResponses = new Queue<string>([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": []}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		_applicationClient.ExecutePostRequest(CreateNewSchemaUrl, Arg.Any<string>())
			.Returns("""{"success": true}""");
		var options = new SourceCodeSchemaCreateOptions { SchemaName = "UsrMyHelper", PackageName = "Custom" };

		bool result = _command.TryCreate(options, out SourceCodeSchemaCreateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("schema payload");
		_applicationClient.DidNotReceive().ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>());
	}

	[Test]
	public void TryCreate_Sets_Description_When_Provided() {
		var selectResponses = new Queue<string>([
			$$"""{"success": true, "rows": [{"UId": "{{PackageUId}}"}]}""",
			"""{"success": true, "rows": []}"""
		]);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns(_ => selectResponses.Dequeue());
		_applicationClient.ExecutePostRequest(CreateNewSchemaUrl, Arg.Any<string>())
			.Returns(SchemaPayloadJson);
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>())
			.Returns("""{"success": true}""");
		var options = new SourceCodeSchemaCreateOptions {
			SchemaName = "UsrMyHelper",
			PackageName = "Custom",
			Description = "Helper utilities"
		};

		_command.TryCreate(options, out _);

		_applicationClient.Received(1).ExecutePostRequest(SaveSchemaUrl,
			Arg.Is<string>(s => s.Contains("Helper utilities")));
	}
}
