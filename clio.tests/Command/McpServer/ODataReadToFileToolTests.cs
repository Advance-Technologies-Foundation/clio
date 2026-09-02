using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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
[Property("Module", "McpServer")]
public sealed class ODataReadToFileToolTests {

	[Test]
	[Category("Unit")]
	[Description("Writes a successful OData response to output-file, omits the inline value, and returns row and per-column byte summaries.")]
	public void ReadToFile_Should_Write_Response_To_Output_File_When_Requested() {
		// Arrange
		MockFileSystem fileSystem = new();
		string outputFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"odata-read-{Guid.NewGuid():N}.json");
		ICreatioApplicationClient client = Substitute.For<ICreatioApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Contact?$top=25");
		client.ExecuteGetRequestBoundedAsync(
				Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(Encoding.UTF8.GetBytes("{\"value\":[{\"Id\":\"1\",\"Name\":\"John\"},{\"Id\":\"2\",\"Name\":\"Jane\"}]}")));
		ODataReadToFileTool tool = new(resolver, new ODataFileContract(fileSystem, new MockConfinedFileAccess(fileSystem)));

		// Act
		ODataReadResponse response = tool.ReadToFile(new ODataReadToFileArgs {
			EnvironmentName = "dev", Entity = "Contact", OutputFile = outputFile
		});

		// Assert
		response.Success.Should().BeTrue(because: "a successful OData response should be persisted when output-file is requested");
		response.Value.Should().BeNull(because: "large response values must not be duplicated into the MCP result");
		response.OutputFile.Should().Be(fileSystem.Path.GetFullPath(outputFile), because: "the caller needs the resolved file path");
		response.RowCount.Should().Be(2, because: "the summary should count returned object rows");
		response.ColumnSizes.Should().ContainKey("Name", because: "the summary should expose sizes for returned columns");
		fileSystem.File.ReadAllText(outputFile).Should().Contain("John", because: "the raw OData response must be written unchanged");
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses an output-file that already exists, and refuses it BEFORE the OData request so a rejected path never costs a full fetch first.")]
	public void ReadToFile_Should_Reject_Existing_Output_File_Before_Fetching() {
		// Arrange
		MockFileSystem fileSystem = new();
		string outputFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"odata-read-existing-{Guid.NewGuid():N}.json");
		fileSystem.AddFile(outputFile, new MockFileData("{}", Encoding.UTF8));
		ICreatioApplicationClient client = Substitute.For<ICreatioApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Contact?$top=25");
		ODataReadToFileTool tool = new(resolver, new ODataFileContract(fileSystem, new MockConfinedFileAccess(fileSystem)));

		// Act
		ODataReadResponse response = tool.ReadToFile(new ODataReadToFileArgs {
			EnvironmentName = "dev", Entity = "Contact", OutputFile = outputFile
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "an explicit output-file is additive and must never overwrite an existing file");
		response.Error.Should().Contain("already exists",
			because: "the caller has to know to choose a different path");
		client.DidNotReceiveWithAnyArgs().ExecuteGetRequestBoundedAsync(default, default, default, default);
		// because: a path that was already refused must not cost a full fetch first
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses an output-file outside the workspace and the OS temp directory, so an agent-supplied path cannot land a write on an arbitrary file.")]
	public void ReadToFile_Should_Reject_Output_File_Outside_The_Allowed_Locations() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Contact?$top=25");
		ODataReadToFileTool tool = new(resolver, new ODataFileContract(new MockFileSystem(), new MockConfinedFileAccess(new MockFileSystem())));
		string outsidePath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			$"clio-odata-output-probe-{Guid.NewGuid():N}.json");

		// Act
		ODataReadResponse response = tool.ReadToFile(new ODataReadToFileArgs {
			EnvironmentName = "dev", Entity = "Contact", OutputFile = outsidePath
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a path outside the allowed locations must never be written");
		response.Error.Should().Contain("allowed locations",
			because: "the caller has to be told the path was refused by confinement");
		File.Exists(outsidePath).Should().BeFalse(
			because: "the refusal must happen before anything is created on disk");
	}


	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for odata-read-to-file, and the write-capable safety annotations its local file write requires.")]
	public void ReadToFile_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ODataReadToFileTool)
			.GetMethod(nameof(ODataReadToFileTool.ReadToFile))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(ODataReadToFileTool.ToolName,
			because: "the MCP tool name must stay stable for callers and tests");
		attribute.ReadOnly.Should().BeFalse(
			because: "this tool writes a local file, and a ReadOnly annotation would make the MCP read-deadline "
				+ "pipeline treat the call as retry-safe - a deadline firing after the file landed would leave the "
				+ "agent with a retry the already-exists guard refuses");
		attribute.Idempotent.Should().BeFalse(
			because: "a second call to the same output-file is refused, not a no-op");
		attribute.Destructive.Should().BeFalse(
			because: "the tool reads remote data and only adds a local file");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a missing output-file and names the read-only tool to use instead, before any Creatio request.")]
	public void ReadToFile_Should_Require_Output_File() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		ODataReadToFileTool tool = new(resolver, new ODataFileContract(new MockFileSystem(), new MockConfinedFileAccess(new MockFileSystem())));

		// Act
		ODataReadResponse response = tool.ReadToFile(new ODataReadToFileArgs {
			EnvironmentName = "dev", Entity = "Contact", OutputFile = "   "
		});

		// Assert
		response.Success.Should().BeFalse(because: "the file destination is the whole point of this tool");
		response.Error.Should().Contain("odata-read",
			because: "a caller with no file destination should be sent to the read-only tool");
		client.ReceivedCalls().Should().BeEmpty(
			because: "the argument is rejected before any Creatio request");
	}

	[TestCase("null")]
	[TestCase("true")]
	[TestCase("42")]
	[TestCase("\"Unauthorized\"")]
	[Category("Unit")]
	[Description("Rejects a scalar JSON body instead of persisting it as a successful single-entity response, and leaves no output file behind.")]
	public void ReadToFile_Should_Reject_Scalar_Response_And_Write_Nothing(string scalarBody) {
		// Arrange
		MockFileSystem fileSystem = new();
		string outputFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"odata-scalar-{Guid.NewGuid():N}.json");
		ICreatioApplicationClient client = Substitute.For<ICreatioApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Contact?$top=25");
		client.ExecuteGetRequestBoundedAsync(
				Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(Encoding.UTF8.GetBytes(scalarBody)));
		ODataReadToFileTool tool = new(resolver, new ODataFileContract(fileSystem, new MockConfinedFileAccess(fileSystem)));

		// Act
		ODataReadResponse response = tool.ReadToFile(new ODataReadToFileArgs {
			EnvironmentName = "dev", Entity = "Contact", OutputFile = outputFile
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a scalar body is not OData content and must never be reported as one record");
		response.Error.Should().Contain("not a record or a collection",
			because: "the caller has to be told the endpoint did not answer with OData content");
		fileSystem.File.Exists(outputFile).Should().BeFalse(
			because: "nothing may be persisted for a body that was rejected");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports the record count and the paging annotations from the same single pass that builds the file summary, without returning the inline value.")]
	public void ReadToFile_Should_Report_Paging_Annotations_Without_Inline_Value() {
		// Arrange
		MockFileSystem fileSystem = new();
		string outputFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"odata-paged-{Guid.NewGuid():N}.json");
		ICreatioApplicationClient client = Substitute.For<ICreatioApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Contact?$top=25");
		client.ExecuteGetRequestBoundedAsync(
				Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(Encoding.UTF8.GetBytes("{\"@odata.count\":7,\"@odata.nextLink\":\"http://creatio/next\",\"value\":[{\"Id\":\"1\"}]}")));
		ODataReadToFileTool tool = new(resolver, new ODataFileContract(fileSystem, new MockConfinedFileAccess(fileSystem)));

		// Act
		ODataReadResponse response = tool.ReadToFile(new ODataReadToFileArgs {
			EnvironmentName = "dev", Entity = "Contact", OutputFile = outputFile, Count = true
		});

		// Assert
		response.Success.Should().BeTrue(because: "the response is a valid OData envelope");
		response.Count.Should().Be(1, because: "the page carried one record");
		response.TotalCount.Should().Be(7, because: "count=true must return the verified server total");
		response.NextLink.Should().Be("http://creatio/next", because: "the caller needs the paging continuation");
		response.Value.Should().BeNull(because: "the file destination exists to keep the value out of the MCP result");
	}

	[Test]
	[Category("Unit")]
	[Description("Fails a count=true call whose response carries no @odata.count, and writes nothing, so an unverifiable total never reaches disk as a successful read.")]
	public void ReadToFile_Should_Fail_When_Count_Requested_But_Server_Omits_It() {
		// Arrange
		MockFileSystem fileSystem = new();
		string outputFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"odata-nocount-{Guid.NewGuid():N}.json");
		ICreatioApplicationClient client = Substitute.For<ICreatioApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Contact?$top=25");
		client.ExecuteGetRequestBoundedAsync(
				Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(Encoding.UTF8.GetBytes("{\"value\":[{\"Id\":\"1\"}]}")));
		ODataReadToFileTool tool = new(resolver, new ODataFileContract(fileSystem, new MockConfinedFileAccess(fileSystem)));

		// Act
		ODataReadResponse response = tool.ReadToFile(new ODataReadToFileArgs {
			EnvironmentName = "dev", Entity = "Contact", OutputFile = outputFile, Count = true
		});

		// Assert
		response.Success.Should().BeFalse(because: "an unverified total count must not be reported as verified");
		fileSystem.File.Exists(outputFile).Should().BeFalse(
			because: "a call reported as failed must leave nothing on disk");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a server error body without writing a file, so a file named after a successful read never holds an error payload.")]
	public void ReadToFile_Should_Not_Write_A_Server_Error_Body() {
		// Arrange
		MockFileSystem fileSystem = new();
		string outputFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"odata-error-{Guid.NewGuid():N}.json");
		ICreatioApplicationClient client = Substitute.For<ICreatioApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Contact?$top=25");
		client.ExecuteGetRequestBoundedAsync(
				Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(Encoding.UTF8.GetBytes("{\"error\":{\"code\":\"500\",\"message\":\"boom\"}}")));
		ODataReadToFileTool tool = new(resolver, new ODataFileContract(fileSystem, new MockConfinedFileAccess(fileSystem)));

		// Act
		ODataReadResponse response = tool.ReadToFile(new ODataReadToFileArgs {
			EnvironmentName = "dev", Entity = "Contact", OutputFile = outputFile
		});

		// Assert
		response.Success.Should().BeFalse(because: "an OData error body is a failed read");
		fileSystem.File.Exists(outputFile).Should().BeFalse(
			because: "an error payload must not be persisted under a name that suggests a successful read");
	}

	[Test]
	[Category("Unit")]
	[Description("Stops reading and writes nothing when the response passes the byte ceiling, so one call cannot exhaust the server's memory behind a small top.")]
	public void ReadToFile_Should_Reject_A_Response_Past_The_Byte_Ceiling() {
		// Arrange
		MockFileSystem fileSystem = new();
		string outputFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"odata-huge-{Guid.NewGuid():N}.json");
		ICreatioApplicationClient client = Substitute.For<ICreatioApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Contact?$top=25");
		//The transport abandons the transfer at the ceiling and reports it as ResponseTooLargeException; the
		//tool has to turn that into an actionable caller-facing message rather than a transport error.
		client.ExecuteGetRequestBoundedAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns<Task<byte[]>>(_ => throw new ResponseTooLargeException(
				ODataFileContract.MaxResponseBytes + 1, ODataFileContract.MaxResponseBytes));
		ODataReadToFileTool tool = new(resolver, new ODataFileContract(fileSystem, new MockConfinedFileAccess(fileSystem)));

		// Act
		ODataReadResponse result = tool.ReadToFile(new ODataReadToFileArgs {
			EnvironmentName = "dev", Entity = "Contact", OutputFile = outputFile
		});

		// Assert
		result.Success.Should().BeFalse(
			because: "a response past the ceiling must be refused, not summarized and written");
		result.Error.Should().Contain("exceeds",
			because: "the caller has to be told the response was too large and how to narrow it");
		fileSystem.File.Exists(outputFile).Should().BeFalse(
			because: "nothing may be published for a body that was refused");
	}

	[Test]
	[Category("Unit")]
	[Description("Writes nothing and reports the cancellation when the caller abandons the call before the response arrives.")]
	public void ReadToFile_Should_Write_Nothing_When_The_Caller_Cancels() {
		// Arrange
		MockFileSystem fileSystem = new();
		string outputFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"odata-cancelled-{Guid.NewGuid():N}.json");
		ICreatioApplicationClient client = Substitute.For<ICreatioApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Contact?$top=25");
		using CancellationTokenSource cancellation = new();
		client.ExecuteGetRequestBoundedAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(_ => {
				cancellation.Cancel();
				return Task.FromResult(Encoding.UTF8.GetBytes("{\"value\":[]}"));
			});
		ODataReadToFileTool tool = new(resolver, new ODataFileContract(fileSystem, new MockConfinedFileAccess(fileSystem)));

		// Act
		ODataReadResponse result = tool.ReadToFile(new ODataReadToFileArgs {
			EnvironmentName = "dev", Entity = "Contact", OutputFile = outputFile
		}, cancellation.Token);

		// Assert
		result.Success.Should().BeFalse(because: "an abandoned call is not a successful read");
		result.Error.Should().Contain("cancelled",
			because: "the caller has to be able to tell cancellation apart from a server failure");
		fileSystem.File.Exists(outputFile).Should().BeFalse(
			because: "a cancelled call must leave nothing behind for the caller to trip over on a retry");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts the same query arguments as odata-read, so the split did not fork the query surface.")]
	public void ReadToFile_Should_Build_The_Same_Query_As_ODataRead() {
		// Arrange
		MockFileSystem fileSystem = new();
		string outputFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"odata-query-{Guid.NewGuid():N}.json");
		ICreatioApplicationClient client = Substitute.For<ICreatioApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		client.ExecuteGetRequestBoundedAsync(
				Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(Encoding.UTF8.GetBytes("{\"value\":[]}")));
		ODataReadToFileTool tool = new(resolver, new ODataFileContract(fileSystem, new MockConfinedFileAccess(fileSystem)));
		JsonElement nameValue = JsonDocument.Parse("\"John\"").RootElement.Clone();

		// Act
		tool.ReadToFile(new ODataReadToFileArgs {
			EnvironmentName = "dev",
			Entity = "Contact",
			OutputFile = outputFile,
			Select = ["Id", "Name"],
			Top = 10,
			Filters = new ODataFilters {
				All = [new ODataFilterCondition { Field = "Name", Op = "eq", Value = nameValue }]
			}
		});

		// Assert
		string builtPath = urlBuilder.ReceivedCalls()
			.Single(call => call.GetMethodInfo().Name == nameof(IServiceUrlBuilder.Build))
			.GetArguments()[0] as string;
		builtPath.Should().Contain("odata/Contact", because: "the entity set drives the request path")
			.And.Contain("$select=Id%2CName", because: "select must be escaped into the query string")
			.And.Contain("$top=10", because: "the file tool must build its query through the same shared builder as odata-read");
	}
}
