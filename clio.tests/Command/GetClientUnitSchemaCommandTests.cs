namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json.Linq;
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

	private static string GetSchemaFullHierarchyJson =>
		$$$"""
		{
		  "success": true,
		  "schema": {
		    "uId": "{{{SchemaUId}}}",
		    "name": "UsrHelper",
		    "body": "define('UsrHelper', []);",
		    "caption": [{"cultureName": "en-US", "value": "Usr Helper"}],
		    "package": {"name": "Custom"},
		    "localizableStrings": [
		      {"name": "Cap1", "parentSchemaUId": "p1", "uId": "ls1",
		       "values": [{"cultureName": "en-US", "value": "Caption One"}]}
		    ]
		  }
		}
		""";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private IFileSystem _fileSystem;
	private ILogger _logger;
	private GetClientUnitSchemaCommand _command;
	private string _writtenPath;
	private string _writtenContent;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_fileSystem = Substitute.For<IFileSystem>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema").Returns(GetSchemaUrl);
		_writtenPath = null;
		_writtenContent = null;
		_fileSystem.When(fs => fs.WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>()))
			.Do(ci => { _writtenPath = ci.ArgAt<string>(0); _writtenContent = ci.ArgAt<string>(1); });
		_command = new GetClientUnitSchemaCommand(_applicationClient, _serviceUrlBuilder, _fileSystem, _logger);
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
	[Description("TryGetSchema writes only the body to the output file and omits it from the response when --output-file is set.")]
	public void TryGetSchema_Writes_Body_To_File_When_OutputFile_Provided() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
		_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>()).Returns(GetSchemaSuccessJson);
		var options = new GetClientUnitSchemaOptions { SchemaName = "UsrHelper", OutputFile = "/out/UsrHelper.js" };

		// Act
		bool result = _command.TryGetSchema(options, out GetClientUnitSchemaResponse response);

		// Assert
		result.Should().BeTrue(because: "an existing schema is fetched and written");
		response.Body.Should().BeNull(because: "with an output file the body goes to disk, not the response");
		response.BodyLength.Should().Be("define('UsrHelper', []);".Length, because: "the length is still reported");
		_writtenPath.Should().Be("/out/UsrHelper.js", because: "the body is written to the requested path");
		_writtenContent.Should().Be("define('UsrHelper', []);", because: "the raw body is written verbatim");
	}

	[Test]
	[Description("TryGetSchema with --full-hierarchy returns the merged localizable strings and their count in the response.")]
	public void TryGetSchema_ReturnsMergedLocalizableStrings_WhenFullHierarchy() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
		_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>()).Returns(GetSchemaFullHierarchyJson);
		var options = new GetClientUnitSchemaOptions { SchemaName = "UsrHelper", FullHierarchy = true };

		// Act
		bool result = _command.TryGetSchema(options, out GetClientUnitSchemaResponse response);

		// Assert
		result.Should().BeTrue(because: "a full-hierarchy read of an existing schema succeeds");
		response.FullHierarchy.Should().BeTrue(because: "the response echoes the full-hierarchy mode");
		response.LocalizableStringCount.Should().Be(1, because: "the merged schema carries one localizable string");
		response.LocalizableStrings.Should().ContainSingle(
				because: "the merged strings are surfaced in the response when no output file is set")
			.Which.Name.Should().Be("Cap1", because: "the string key is read from the merged schema");
	}

	[Test]
	[Description("TryGetSchema with --full-hierarchy and --output-file writes the documented localizable-strings contract, not a raw DTO dump.")]
	public void TryGetSchema_WritesLocalizableStringsContract_WhenFullHierarchyAndOutputFile() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(SchemaFoundJson);
		_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>()).Returns(GetSchemaFullHierarchyJson);
		var options = new GetClientUnitSchemaOptions {
			SchemaName = "UsrHelper", FullHierarchy = true, OutputFile = "/out/UsrHelper.fh.json"
		};

		// Act
		bool result = _command.TryGetSchema(options, out GetClientUnitSchemaResponse response);

		// Assert
		result.Should().BeTrue(because: "the schema resolves and the contract is written");
		response.LocalizableStrings.Should().BeNull(
			because: "with an output file the strings go to disk, not the response");
		_writtenPath.Should().Be("/out/UsrHelper.fh.json", because: "the contract is written to the requested path");
		JObject written = JObject.Parse(_writtenContent);
		written["schemaName"]!.ToString().Should().Be("UsrHelper", because: "the contract documents the schema name");
		written["fullHierarchy"]!.Value<bool>().Should().BeTrue(because: "the contract records the full-hierarchy mode");
		written["body"]!.ToString().Should().Be("define('UsrHelper', []);",
			because: "the single top-layer body is preserved (the view is not folded)");
		JArray strings = (JArray)written["localizableStrings"]!;
		strings.Should().ContainSingle(because: "the merged localizable strings are the contract's payload");
		strings[0]!["name"]!.ToString().Should().Be("Cap1", because: "each entry carries its key");
		strings[0]!["parentSchemaUId"]!.ToString().Should().Be("p1",
			because: "parentSchemaUId provenance is preserved in the contract");
	}

	[Test]
	[Description("TryGetSchema with --schema-uid loads that exact UId directly and issues no name-resolution SelectQuery.")]
	public void TryGetSchema_BypassesNameResolution_WhenSchemaUidProvided() {
		// Arrange — only the GetSchema call is stubbed; a SelectQuery, if issued, would return null and fail
		_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>()).Returns(GetSchemaSuccessJson);
		var options = new GetClientUnitSchemaOptions { SchemaUId = SchemaUId };

		// Act
		bool result = _command.TryGetSchema(options, out GetClientUnitSchemaResponse response);

		// Assert
		result.Should().BeTrue(because: "an explicit schema-uid resolves directly to a body");
		response.SchemaUId.Should().Be(SchemaUId, because: "the provided UId is used verbatim");
		_applicationClient.DidNotReceive().ExecutePostRequest(SelectQueryUrl, Arg.Any<string>());
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
