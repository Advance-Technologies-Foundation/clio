using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ExecuteEsqToFileToolTests {

	private const string TwoRowsResponse =
		"{\"success\":true,\"rows\":[{\"Id\":\"a1\",\"Name\":\"Alpha\"},{\"Id\":\"b2\",\"Name\":\"Beta\"}]}";

	private const string ContactQuery =
		"{\"rootSchemaName\":\"Contact\",\"columns\":{\"items\":{\"Id\":{},\"Name\":{}}}}";

	private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

	private static (ExecuteEsqToFileTool tool, IApplicationClient client, MockFileSystem fileSystem) BuildTool(
		string responseJson, MockFileSystem? fileSystem = null) {
		MockFileSystem fs = fileSystem ?? new MockFileSystem();
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>())
			.Returns("http://creatio/DataService/json/SyncReply/SelectQuery");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(responseJson);
		ExecuteEsqToFileTool tool = new(commandResolver, new McpOutputFileWriter(fs, new MockConfinedFileAccess(fs)));
		return (tool, client, fs);
	}

	private static string TempPath(MockFileSystem fileSystem, string prefix) =>
		fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.json");

	[Test]
	[Description("Writes the rows array execute-esq returns inline to output-file and returns the path and the row count without the rows.")]
	public void ExecuteToFile_ShouldWriteRowsToOutputFile() {
		// Arrange
		(ExecuteEsqToFileTool tool, _, MockFileSystem fileSystem) = BuildTool(TwoRowsResponse);
		string outputFile = TempPath(fileSystem, "esq-rows");

		// Act
		ExecuteEsqResponse response = tool.ExecuteToFile(new ExecuteEsqToFileArgs {
			EnvironmentName = "dev", Query = Json(ContactQuery), OutputFile = outputFile
		});

		// Assert
		response.Success.Should().BeTrue(because: "the query succeeded and the file was written");
		response.Rows.Should().BeNull(because: "the rows live in the file, not in the MCP result");
		response.Count.Should().Be(2, because: "the response carries the number of rows written");
		response.OutputFile.Should().Be(fileSystem.Path.GetFullPath(outputFile), because: "the caller needs the resolved path");
		fileSystem.File.ReadAllText(outputFile).Should().Be(
			"[{\"Id\":\"a1\",\"Name\":\"Alpha\"},{\"Id\":\"b2\",\"Name\":\"Beta\"}]",
			because: "the file holds exactly the rows array the inline tool returns");
		McpResponseBaseline.Serialize(response).Should().Be(
			$"{{\"success\":true,\"count\":2,\"output-file\":{JsonSerializer.Serialize(response.OutputFile)}}}",
			because: "the wire response carries only the success flag, the row count and the path");
	}

	[Test]
	[Description("The inline execute-esq and the file twin return the same row count and the same rows for one query.")]
	public void ExecuteToFile_ShouldMatchInlineTool_ForTheSameQuery() {
		// Arrange
		(ExecuteEsqToFileTool fileTool, _, MockFileSystem fileSystem) = BuildTool(TwoRowsResponse);
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(TwoRowsResponse);
		string outputFile = TempPath(fileSystem, "esq-same");

		// Act
		ExecuteEsqResponse inline = new ExecuteEsqTool(resolver).Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev", Query = Json(ContactQuery)
		});
		ExecuteEsqResponse toFile = fileTool.ExecuteToFile(new ExecuteEsqToFileArgs {
			EnvironmentName = "dev", Query = Json(ContactQuery), OutputFile = outputFile
		});

		// Assert
		toFile.Count.Should().Be(inline.Count, because: "both tools count the same rows");
		fileSystem.File.ReadAllText(outputFile).Should().Be(inline.Rows!.Value.GetRawText(),
			because: "the file carries the same rows the inline tool returns, so no information is lost");
	}

	[Test]
	[Description("A successful response without a rows array is written to output-file whole, and the response carries the path without a count, as the tool description promises.")]
	public void ExecuteToFile_ShouldWriteTheWholeBody_WhenTheResponseHasNoRowsArray() {
		// Arrange
		const string projectionResponse = "{\"success\":true,\"total\":5}";
		(ExecuteEsqToFileTool tool, _, MockFileSystem fileSystem) = BuildTool(projectionResponse);
		string outputFile = TempPath(fileSystem, "esq-no-rows");

		// Act
		ExecuteEsqResponse response = tool.ExecuteToFile(new ExecuteEsqToFileArgs {
			EnvironmentName = "dev", Query = Json(ContactQuery), OutputFile = outputFile
		});

		// Assert
		response.Success.Should().BeTrue(because: "success:true without rows is a valid non-row projection");
		response.OutputFile.Should().Be(outputFile, because: "the body went to the file the caller named");
		response.Count.Should().BeNull(because: "there is no rows array to count");
		response.Rows.Should().BeNull(because: "the body lives in the file, not in the MCP result");
		fileSystem.File.ReadAllText(outputFile).Should().Be(projectionResponse,
			because: "without a rows array the file holds the whole response body, as the inline tool returns it");
	}

	[Test]
	[Description("A requested column the query did not return fails the call as it does inline, and no file is written.")]
	public void ExecuteToFile_ShouldFailWithoutWriting_WhenARequestedColumnIsMissing() {
		// Arrange
		(ExecuteEsqToFileTool tool, _, MockFileSystem fileSystem) = BuildTool(
			"{\"success\":true,\"rows\":[{\"Id\":\"a1\"}]}");
		string outputFile = TempPath(fileSystem, "esq-missing");

		// Act
		ExecuteEsqResponse response = tool.ExecuteToFile(new ExecuteEsqToFileArgs {
			EnvironmentName = "dev", Query = Json(ContactQuery), OutputFile = outputFile
		});

		// Assert
		response.Success.Should().BeFalse(because: "the Name column did not resolve");
		response.Error.Should().Contain("'Name'", because: "the failure names the missing column as the inline tool does");
		fileSystem.File.Exists(outputFile).Should().BeFalse(because: "a failed query must not leave a file behind");
	}

	[Test]
	[Description("A DataService failure is returned as is and no file is written.")]
	public void ExecuteToFile_ShouldFailWithoutWriting_WhenDataServiceFails() {
		// Arrange
		(ExecuteEsqToFileTool tool, _, MockFileSystem fileSystem) = BuildTool(
			"{\"success\":false,\"responseStatus\":{\"ErrorCode\":\"Bad\",\"Message\":\"boom\"}}");
		string outputFile = TempPath(fileSystem, "esq-failed");

		// Act
		ExecuteEsqResponse response = tool.ExecuteToFile(new ExecuteEsqToFileArgs {
			EnvironmentName = "dev", Query = Json(ContactQuery), OutputFile = outputFile
		});

		// Assert
		response.Success.Should().BeFalse(because: "the server reported a failure");
		response.Error.Should().Contain("boom", because: "the server failure reaches the caller");
		fileSystem.File.Exists(outputFile).Should().BeFalse(because: "an error body is never written as rows");
	}

	[Test]
	[Description("A response above the inline 200 KB budget is written to the file, because the file mode is bounded only by the file-mode ceiling.")]
	public void ExecuteToFile_ShouldWriteResponse_LargerThanTheInlineBudget() {
		// Arrange
		string bigName = new('x', ExecuteEsqTool.MaxResponseSizeBytes);
		string bigResponse = "{\"success\":true,\"rows\":[{\"Id\":\"a1\",\"Name\":\"" + bigName + "\"}]}";
		(ExecuteEsqToFileTool tool, _, MockFileSystem fileSystem) = BuildTool(bigResponse);
		string outputFile = TempPath(fileSystem, "esq-big");

		// Act
		ExecuteEsqResponse response = tool.ExecuteToFile(new ExecuteEsqToFileArgs {
			EnvironmentName = "dev", Query = Json(ContactQuery), OutputFile = outputFile
		});

		// Assert
		response.Success.Should().BeTrue(because: "the inline budget does not apply to the file mode");
		fileSystem.File.ReadAllText(outputFile).Should().Contain(bigName, because: "the whole row is written");
	}

	[Test]
	[Description("A response above the file-mode ceiling fails with result-too-large, writes nothing, and does not point the caller at the tool it already called.")]
	public void ExecuteToFile_ShouldReportResultTooLarge_WithoutPointingAtItself() {
		// Arrange
		(ExecuteEsqToFileTool tool, _, MockFileSystem fileSystem) = BuildTool(TwoRowsResponse);
		string outputFile = TempPath(fileSystem, "esq-too-large");
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		IApplicationClient client = Substitute.For<IApplicationClient>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(Substitute.For<IServiceUrlBuilder>());
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(TwoRowsResponse);

		// Act
		ExecuteEsqResponse response = ExecuteEsqTool.Run(resolver, new ExecuteEsqArgs {
			EnvironmentName = "dev", Query = Json(ContactQuery)
		}, maxResponseBytes: 1, suggestFileTwin: false);

		// Assert
		response.ErrorClass.Should().Be(ExecuteEsqTool.ResultTooLargeErrorClass, because: "the response is over the ceiling");
		response.Error.Should().NotContain(ExecuteEsqToFileTool.ToolName,
			because: "the file path must not tell the caller to switch to the tool it is already using");
		fileSystem.File.Exists(outputFile).Should().BeFalse(because: "nothing is written for a rejected response");
	}

	[Test]
	[Description("Refuses an output-file that already exists, before any Creatio request.")]
	public void ExecuteToFile_ShouldRejectExistingOutputFile_BeforeQuerying() {
		// Arrange
		MockFileSystem fileSystem = new();
		string outputFile = TempPath(fileSystem, "esq-existing");
		fileSystem.AddFile(outputFile, new MockFileData("{}", Encoding.UTF8));
		(ExecuteEsqToFileTool tool, IApplicationClient client, _) = BuildTool(TwoRowsResponse, fileSystem);

		// Act
		ExecuteEsqResponse response = tool.ExecuteToFile(new ExecuteEsqToFileArgs {
			EnvironmentName = "dev", Query = Json(ContactQuery), OutputFile = outputFile
		});

		// Assert
		response.Success.Should().BeFalse(because: "an existing file is never overwritten");
		response.Error.Should().Contain("already exists", because: "the caller has to choose a different path");
		client.ReceivedCalls().Should().BeEmpty(because: "a refused path must not cost a query first");
		fileSystem.File.ReadAllText(outputFile).Should().Be("{}", because: "the existing file is left untouched");
	}

	[Test]
	[Description("Refuses an output-file outside the workspace and the OS temp directory, before any Creatio request.")]
	public void ExecuteToFile_ShouldRejectOutputFile_OutsideTheAllowedLocations() {
		// Arrange
		(ExecuteEsqToFileTool tool, IApplicationClient client, MockFileSystem fileSystem) = BuildTool(TwoRowsResponse);
		string outsidePath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			$"clio-esq-output-probe-{Guid.NewGuid():N}.json");

		// Act
		ExecuteEsqResponse response = tool.ExecuteToFile(new ExecuteEsqToFileArgs {
			EnvironmentName = "dev", Query = Json(ContactQuery), OutputFile = outsidePath
		});

		// Assert
		response.Success.Should().BeFalse(because: "a path outside the allowed locations must never be written");
		response.Error.Should().Contain("allowed locations", because: "the caller is told confinement refused the path");
		client.ReceivedCalls().Should().BeEmpty(because: "the path is checked before the query");
		fileSystem.File.Exists(outsidePath).Should().BeFalse(because: "nothing is created on the file system the tool writes to");
	}

	[Test]
	[Description("A missing output-file is refused and names execute-esq as the tool for inline rows, before any Creatio request.")]
	public void ExecuteToFile_ShouldRequireOutputFile() {
		// Arrange
		(ExecuteEsqToFileTool tool, IApplicationClient client, _) = BuildTool(TwoRowsResponse);

		// Act
		ExecuteEsqResponse response = tool.ExecuteToFile(new ExecuteEsqToFileArgs {
			EnvironmentName = "dev", Query = Json(ContactQuery), OutputFile = "  "
		});

		// Assert
		response.Success.Should().BeFalse(because: "the file destination is the whole point of this tool");
		response.Error.Should().Contain("output-file is required", because: "the caller learns which argument is missing");
		response.Error.Should().Contain("execute-esq", because: "the caller is sent to the inline tool");
		client.ReceivedCalls().Should().BeEmpty(because: "nothing is queried without a destination");
	}

	[Test]
	[Description("Advertises a stable tool name and the write-capable annotations a local file write needs.")]
	public void ExecuteToFile_ShouldAdvertiseStableNameAndWriteCapableAnnotations() {
		// Arrange

		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ExecuteEsqToFileTool)
			.GetMethod(nameof(ExecuteEsqToFileTool.ExecuteToFile))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be("execute-esq-to-file", because: "the tool name is part of the MCP contract");
		attribute.ReadOnly.Should().BeFalse(because: "the tool creates a local file");
		attribute.Idempotent.Should().BeFalse(because: "a second call to the same output-file is refused");
		attribute.Destructive.Should().BeFalse(because: "the tool reads remote data and only adds a local file");
	}
}
