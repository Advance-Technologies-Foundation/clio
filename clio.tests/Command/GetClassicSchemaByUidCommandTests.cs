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
public sealed class GetClassicSchemaByUidCommandTests {
	private const string SchemaUId = "948080fc-031e-4d88-9239-47bcedaa92bc";
	private const string GetSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema";

	private static string GetSchemaSuccessJson =>
		$$$"""
		{
		  "success": true,
		  "schema": {
		    "uId": "{{{SchemaUId}}}",
		    "name": "ContactPageV2",
		    "body": "define('ContactPageV2', []);",
		    "caption": [{"cultureName": "en-US", "value": "Contact page"}],
		    "package": {"name": "Custom"}
		  }
		}
		""";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;
	private GetClassicSchemaByUidCommand _command;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build(Arg.Any<string>()).Returns(GetSchemaUrl);
		_command = new GetClassicSchemaByUidCommand(_applicationClient, _serviceUrlBuilder, _logger);
	}

	[Test]
	[Description("TryGetSchema writes only the schema body to an absolute output file and omits body from the response.")]
	public void TryGetSchema_Should_Write_Body_To_File_When_OutputFile_Provided() {
		// Arrange
		_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>()).Returns(GetSchemaSuccessJson);
		string outputFile = Path.Combine(Path.GetTempPath(), $"clio-{Path.GetRandomFileName()}.js");
		try {
			var options = new GetClassicSchemaByUidOptions { SchemaUId = SchemaUId, OutputFile = outputFile };

			// Act
			bool result = _command.TryGetSchema(options, out GetClassicSchemaByUidResponse response);

			// Assert
			result.Should().BeTrue(because: "an existing schema should be loaded and written");
			response.Success.Should().BeTrue(because: "the command completed successfully");
			response.Body.Should().BeNull(because: "with output-file the body is written to disk, not returned");
			response.BodyLength.Should().Be("define('ContactPageV2', []);".Length,
				because: "the response still reports body length for callers");
			File.ReadAllText(outputFile).Should().Be("define('ContactPageV2', []);",
				because: "the raw schema body is written verbatim");
		}
		finally {
			if (File.Exists(outputFile)) {
				File.Delete(outputFile);
			}
		}
	}

	[Test]
	[Description("TryGetSchema rejects relative output-file values before loading the schema.")]
	public void TryGetSchema_Should_Fail_When_OutputFile_Is_Relative() {
		// Arrange
		var options = new GetClassicSchemaByUidOptions {
			SchemaUId = SchemaUId,
			OutputFile = Path.Combine("relative", "schema.js")
		};

		// Act
		bool result = _command.TryGetSchema(options, out GetClassicSchemaByUidResponse response);

		// Assert
		result.Should().BeFalse(because: "relative output paths are ambiguous under MCP server working directories");
		response.Success.Should().BeFalse(because: "the command should reject invalid output-file input");
		response.Error.Should().Contain("absolute path", because: "the user needs a corrective path requirement");
		_applicationClient.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>());
	}
}
