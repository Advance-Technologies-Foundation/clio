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
public sealed class SourceCodeSchemaUpdateCommandTests {
	private const string TestBase = "http://test";
	private const string SelectQueryUrl = TestBase + "/DataService/json/SyncReply/SelectQuery";
	private const string GetSchemaUrl = TestBase + "/ServiceModel/SourceCodeSchemaDesignerService.svc/GetSchema";
	private const string SaveSchemaUrl = TestBase + "/ServiceModel/SourceCodeSchemaDesignerService.svc/SaveSchema";
	private const string SchemaUId = "aa000000-0000-0000-0000-000000000001";

	private static string SchemaFoundJson =>
		$$$"""{"success": true, "rows": [{"UId": "{{{SchemaUId}}}"}]}""";

	private static string GetSchemaSuccessJson =>
		$$$"""{"success": true, "schema": {"uId": "{{{SchemaUId}}}", "name": "UsrHelper", "body": "// old"}}""";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;
	private SourceCodeSchemaUpdateCommand _command;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_serviceUrlBuilder.Build("ServiceModel/SourceCodeSchemaDesignerService.svc/GetSchema").Returns(GetSchemaUrl);
		_serviceUrlBuilder.Build("ServiceModel/SourceCodeSchemaDesignerService.svc/SaveSchema").Returns(SaveSchemaUrl);
		_command = new SourceCodeSchemaUpdateCommand(_applicationClient, _serviceUrlBuilder, _logger);
	}

	[Test]
	public void TryUpdateSchema_Rejects_Missing_Schema_Name() {
		var options = new SourceCodeSchemaUpdateOptions { Body = "// code" };

		bool result = _command.TryUpdateSchema(options, out SourceCodeSchemaUpdateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("schema-name");
	}

	[Test]
	public void TryUpdateSchema_Rejects_Missing_Body_And_BodyFile() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
		var options = new SourceCodeSchemaUpdateOptions { SchemaName = "UsrHelper" };

		bool result = _command.TryUpdateSchema(options, out SourceCodeSchemaUpdateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("body");
	}

	[Test]
	public void TryUpdateSchema_Fails_When_Schema_Not_Found() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns("""{"success": true, "rows": []}""");
		var options = new SourceCodeSchemaUpdateOptions { SchemaName = "UsrMissing", Body = "// code" };

		bool result = _command.TryUpdateSchema(options, out SourceCodeSchemaUpdateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Contain("UsrMissing").And.Contain("not found");
	}

	[Test]
	public void TryUpdateSchema_Reads_Body_From_File() {
		string tempFile = Path.GetTempFileName();
		try {
			File.WriteAllText(tempFile, "// from file");
			_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
			_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>()).Returns(GetSchemaSuccessJson);
			_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>()).Returns("""{"success": true}""");
			var options = new SourceCodeSchemaUpdateOptions { SchemaName = "UsrHelper", BodyFile = tempFile };

			bool result = _command.TryUpdateSchema(options, out SourceCodeSchemaUpdateResponse response);

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
		var options = new SourceCodeSchemaUpdateOptions {
			SchemaName = "UsrHelper",
			Body = "// dry",
			DryRun = true
		};

		bool result = _command.TryUpdateSchema(options, out SourceCodeSchemaUpdateResponse response);

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
		var options = new SourceCodeSchemaUpdateOptions {
			SchemaName = "UsrHelper",
			Body = "// new code"
		};

		bool result = _command.TryUpdateSchema(options, out SourceCodeSchemaUpdateResponse response);

		result.Should().BeTrue();
		response.Success.Should().BeTrue();
		response.SchemaName.Should().Be("UsrHelper");
		response.BodyLength.Should().Be("// new code".Length);
		response.DryRun.Should().BeFalse();
		_applicationClient.Received(1).ExecutePostRequest(GetSchemaUrl,
			Arg.Is<string>(s => s.Contains(SchemaUId)));
		_applicationClient.Received(1).ExecutePostRequest(SaveSchemaUrl,
			Arg.Is<string>(s => s.Contains("new code")));
	}

	[Test]
	public void TryUpdateSchema_Surfaces_SaveSchema_Error() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
		_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>()).Returns(GetSchemaSuccessJson);
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>())
			.Returns("""{"success": false, "errorInfo": {"message": "compile error"}}""");
		var options = new SourceCodeSchemaUpdateOptions { SchemaName = "UsrHelper", Body = "// bad" };

		bool result = _command.TryUpdateSchema(options, out SourceCodeSchemaUpdateResponse response);

		result.Should().BeFalse();
		response.Error.Should().Be("compile error");
	}
}
