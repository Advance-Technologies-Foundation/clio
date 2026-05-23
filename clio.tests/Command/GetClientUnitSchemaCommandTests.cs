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
public sealed class GetClientUnitSchemaCommandTests {
	private const string TestBase = "http://test";
	private const string SelectQueryUrl = TestBase + "/DataService/json/SyncReply/SelectQuery";
	private const string GetSchemaUrl = TestBase + "/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema";
	private const string SchemaUId = "aa000000-0000-0000-0000-000000000001";

	private static string SchemaFoundJson =>
		$$$"""{"success": true, "rows": [{"UId": "{{{SchemaUId}}}"}]}""";

	private static string GetSchemaSuccessJson =>
		$$$"""
		{
		  "success": true,
		  "schema": {
		    "uId": "{{{SchemaUId}}}",
		    "name": "UsrHelper",
		    "body": "define('UsrHelper', []);",
		    "caption": [{"cultureName": "en-US", "value": "Usr Helper"}],
		    "package": {"name": "Custom"}
		  }
		}
		""";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;
	private GetClientUnitSchemaCommand _command;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema").Returns(GetSchemaUrl);
		_command = new GetClientUnitSchemaCommand(_applicationClient, _serviceUrlBuilder, _logger);
	}

	[Test]
	public void TryGetSchema_Rejects_Missing_Schema_Name() {
		var options = new GetClientUnitSchemaOptions();

		bool result = _command.TryGetSchema(options, out GetClientUnitSchemaResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("schema-name");
	}

	[Test]
	public void TryGetSchema_Fails_When_Schema_Not_Found() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns("""{"success": true, "rows": []}""");
		var options = new GetClientUnitSchemaOptions { SchemaName = "UsrMissing" };

		bool result = _command.TryGetSchema(options, out GetClientUnitSchemaResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("UsrMissing").And.Contain("not found");
	}

	[Test]
	public void TryGetSchema_Returns_Body_And_Metadata_On_Success() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
		_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>()).Returns(GetSchemaSuccessJson);
		var options = new GetClientUnitSchemaOptions { SchemaName = "UsrHelper" };

		bool result = _command.TryGetSchema(options, out GetClientUnitSchemaResponse response);

		result.Should().BeTrue();
		response.Success.Should().BeTrue();
		response.SchemaName.Should().Be("UsrHelper");
		response.SchemaUId.Should().Be(SchemaUId);
		response.PackageName.Should().Be("Custom");
		response.Caption.Should().Be("Usr Helper");
		response.Body.Should().Be("define('UsrHelper', []);");
		response.BodyLength.Should().Be("define('UsrHelper', []);".Length);
	}

	[Test]
	public void TryGetSchema_Writes_Body_To_File_When_OutputFile_Provided() {
		string tempFile = Path.GetTempFileName();
		try {
			_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
			_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>()).Returns(GetSchemaSuccessJson);
			var options = new GetClientUnitSchemaOptions { SchemaName = "UsrHelper", OutputFile = tempFile };

			bool result = _command.TryGetSchema(options, out GetClientUnitSchemaResponse response);

			result.Should().BeTrue();
			response.Body.Should().BeNull();
			response.BodyLength.Should().Be("define('UsrHelper', []);".Length);
			File.ReadAllText(tempFile).Should().Be("define('UsrHelper', []);");
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
		var options = new GetClientUnitSchemaOptions { SchemaName = "UsrHelper" };

		bool result = _command.TryGetSchema(options, out GetClientUnitSchemaResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("UsrHelper").And.Contain("ClientUnitSchemaDesignerService");
	}
}
