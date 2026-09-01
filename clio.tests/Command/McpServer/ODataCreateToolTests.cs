using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Net.Http;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
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
public sealed class ODataCreateToolTests {
	[Test]
	[Category("Unit")]
	[Description("Reads a large create payload from rows-file and posts its rows without requiring an inline rows array.")]
	public void Create_Should_Read_Rows_From_File() {
		// Arrange
		MockFileSystem fileSystem = new();
		string rowsFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"odata-create-{System.Guid.NewGuid():N}.json");
		fileSystem.AddFile(rowsFile, new MockFileData("[{\"Name\":\"Acme\"}]", Encoding.UTF8));
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Account");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Id\":\"11111111-1111-1111-1111-111111111111\"}");
		ODataCreateTool tool = new(resolver, new ODataFileContract(fileSystem));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", RowsFile = rowsFile
		});

		// Assert
		response.Created.Should().Be(1, because: "the row loaded from disk should be posted and reported as created");
		client.Received(1).ExecutePostRequest("http://creatio/odata/Account", "{\"Name\":\"Acme\"}", 30_000, 1, 1);
	}
	[Test]
	[Category("Unit")]
	[Description("Rejects a rows-file larger than the 10 MiB payload bound with the byte-count diagnostic, before the environment is resolved and before any POST.")]
	public void Create_Should_Reject_A_RowsFile_Over_The_Payload_Bound_Before_Any_Post() {
		// Arrange
		MockFileSystem fileSystem = new();
		string rowsFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"odata-create-huge-{System.Guid.NewGuid():N}.json");
		//One byte past the bound: the check has to be the limit itself, not a round approximation of it.
		long overLimit = ODataFileContract.MaxPayloadBytes + 1;
		string padding = new('x', (int)(overLimit - "[{\"Name\":\"\"}]".Length));
		fileSystem.AddFile(rowsFile, new MockFileData($"[{{\"Name\":\"{padding}\"}}]", Encoding.UTF8));
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		ODataCreateTool tool = new(resolver, new ODataFileContract(fileSystem));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", RowsFile = rowsFile
		});

		// Assert
		response.Created.Should().Be(0,
			because: "reading, parsing and cloning an unbounded payload is the memory-exhaustion lever the bound exists to close");
		response.Error.Should().Contain(ODataFileContract.MaxPayloadBytes.ToString(),
			because: "the diagnostic has to name the limit so the caller knows what to split the payload against");
		resolver.DidNotReceive().Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>());
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a misspelled rows_file key instead of dropping it and posting the inline rows, which would execute an ambiguous request.")]
	public void Create_Should_Reject_An_Unbound_File_Source_Alias_Before_Any_Post() {
		// Arrange
		MockFileSystem fileSystem = new();
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		ODataCreateTool tool = new(resolver, new ODataFileContract(fileSystem));
		ODataCreateArgs args = JsonSerializer.Deserialize<ODataCreateArgs>(
			"""{"environment-name":"dev","entity":"Account","rows":[{"Name":"Inline"}],"rows_file":"C:/payload.json"}""")!;

		// Act
		ODataCreateBatchResponse response = tool.Create(args);

		// Assert
		response.Created.Should().Be(0,
			because: "a request naming two payload sources is ambiguous, even when one of them is misspelled");
		response.Error.Should().Contain("rows-file",
			because: "the caller has to be told the canonical spelling of the key that was dropped");
		resolver.DidNotReceive().Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>());
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects an entirely unknown argument before the environment is resolved, so a typo cannot execute a partially understood request.")]
	public void Create_Should_Reject_An_Unknown_Argument_Before_Any_Post() {
		// Arrange
		MockFileSystem fileSystem = new();
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		ODataCreateTool tool = new(resolver, new ODataFileContract(fileSystem));
		ODataCreateArgs args = JsonSerializer.Deserialize<ODataCreateArgs>(
			"""{"environment-name":"dev","entity":"Account","rows":[{"Name":"Inline"}],"stopOnErrors":true}""")!;

		// Act
		ODataCreateBatchResponse response = tool.Create(args);

		// Assert
		response.Created.Should().Be(0,
			because: "an unbound argument means the request was not understood as written");
		response.Error.Should().Contain("stopOnErrors",
			because: "the caller has to see which key was not accepted");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects rows and rows-file together instead of silently preferring one, so a caller never sends a payload it did not mean to.")]
	public void Create_Should_Reject_Rows_And_RowsFile_Together() {
		// Arrange
		MockFileSystem fileSystem = new();
		string rowsFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"odata-create-{System.Guid.NewGuid():N}.json");
		fileSystem.AddFile(rowsFile, new MockFileData("[{\"Name\":\"FromFile\"}]", Encoding.UTF8));
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		ODataCreateTool tool = new(resolver, new ODataFileContract(fileSystem));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"Inline\"}]"), RowsFile = rowsFile
		});

		// Assert
		response.Created.Should().Be(0,
			because: "two payload sources are ambiguous and picking one silently would send data the caller did not choose");
		response.Error.Should().Contain("not both",
			because: "the caller has to be told which argument to drop");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Reports a missing rows-file as a structured request error rather than letting the file exception escape as a protocol failure.")]
	public void Create_Should_Report_Missing_RowsFile() {
		// Arrange
		MockFileSystem fileSystem = new();
		string rowsFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"odata-create-absent-{System.Guid.NewGuid():N}.json");
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		ODataCreateTool tool = new(resolver, new ODataFileContract(fileSystem));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", RowsFile = rowsFile
		});

		// Assert
		response.Created.Should().Be(0,
			because: "an absent payload file is a request error, not a transport failure");
		response.Error.Should().Contain("was not found",
			because: "the caller has to know the path did not resolve to a file");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Reports malformed rows-file JSON as a structured request error instead of surfacing a raw parser exception.")]
	public void Create_Should_Report_Invalid_RowsFile_Json() {
		// Arrange
		MockFileSystem fileSystem = new();
		string rowsFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"odata-create-bad-{System.Guid.NewGuid():N}.json");
		fileSystem.AddFile(rowsFile, new MockFileData("{ not json", Encoding.UTF8));
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		ODataCreateTool tool = new(resolver, new ODataFileContract(fileSystem));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", RowsFile = rowsFile
		});

		// Assert
		response.Created.Should().Be(0,
			because: "an unparseable payload must fail the request, not the MCP protocol frame");
		response.Error.Should().Contain("must contain valid JSON",
			because: "the caller has to know the file content is at fault, not the request shape");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a rows-file holding a JSON object: create takes an array of rows, and a lone object would otherwise fall through the array check with a confusing message.")]
	public void Create_Should_Reject_RowsFile_That_Is_Not_An_Array() {
		// Arrange
		MockFileSystem fileSystem = new();
		string rowsFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"odata-create-obj-{System.Guid.NewGuid():N}.json");
		fileSystem.AddFile(rowsFile, new MockFileData("{\"Name\":\"Acme\"}", Encoding.UTF8));
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		ODataCreateTool tool = new(resolver, new ODataFileContract(fileSystem));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", RowsFile = rowsFile
		});

		// Assert
		response.Created.Should().Be(0,
			because: "a single object is not a batch of rows and must not be posted as one");
		response.Error.Should().Contain("non-empty array",
			because: "the caller has to know the expected payload shape");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a rows-file outside the workspace and the OS temp directory: without confinement the argument is an arbitrary file reader that forwards whatever it reads to the OData endpoint.")]
	public void Create_Should_Reject_RowsFile_Outside_The_Allowed_Locations() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		ODataCreateTool tool = new(resolver, new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>()));
		string outsidePath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "clio-odata-confinement-probe.json");

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", RowsFile = outsidePath
		});

		// Assert
		response.Created.Should().Be(0,
			because: "a path outside the allowed locations must never be opened, whether or not it exists");
		response.Error.Should().Contain("allowed locations",
			because: "the caller has to be told the path was refused by confinement, not simply missing");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts a rows-file holding exactly the 1000-row ceiling, so the limit is inclusive rather than off by one.")]
	public void Create_Should_Accept_A_RowsFile_At_The_Row_Ceiling() {
		// Arrange
		MockFileSystem fileSystem = new();
		string rowsFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(),
			$"odata-create-ceiling-{System.Guid.NewGuid():N}.json");
		string rows = "[" + string.Join(",", Enumerable.Range(0, ODataCreateTool.MaxRowCount)
			.Select(index => $"{{\"Name\":\"Row{index}\"}}")) + "]";
		fileSystem.AddFile(rowsFile, new MockFileData(rows, Encoding.UTF8));
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Account");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Id\":\"11111111-1111-1111-1111-111111111111\"}");
		ODataCreateTool tool = new(resolver, new ODataFileContract(fileSystem));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", RowsFile = rowsFile
		});

		// Assert
		response.Created.Should().Be(ODataCreateTool.MaxRowCount,
			because: "the published ceiling is the largest accepted batch, not the first rejected one");
		client.Received(ODataCreateTool.MaxRowCount).ExecutePostRequest(
			"http://creatio/odata/Account", Arg.Any<string>(), 30_000, 1, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects one row over the ceiling before the environment is resolved and before any POST, so a caller can batch without discovering the limit through a partial write.")]
	public void Create_Should_Reject_A_RowsFile_Over_The_Row_Ceiling_Before_Resolving_Or_Posting() {
		// Arrange
		MockFileSystem fileSystem = new();
		string rowsFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(),
			$"odata-create-over-{System.Guid.NewGuid():N}.json");
		string rows = "[" + string.Join(",", Enumerable.Range(0, ODataCreateTool.MaxRowCount + 1)
			.Select(index => $"{{\"Name\":\"Row{index}\"}}")) + "]";
		fileSystem.AddFile(rowsFile, new MockFileData(rows, Encoding.UTF8));
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		ODataCreateTool tool = new(resolver, new ODataFileContract(fileSystem));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", RowsFile = rowsFile
		});

		// Assert
		response.Created.Should().Be(0);
		response.Error.Should().Contain($"{ODataCreateTool.MaxRowCount + 1} entries",
			because: "the caller has to be told how far over the limit the payload was");
		response.Error.Should().Contain($"{ODataCreateTool.MaxRowCount}-row limit");
		resolver.DidNotReceiveWithAnyArgs().Resolve<IApplicationClient>(null);
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses a rows-file that is not valid UTF-8 and issues no POST, instead of decoding the invalid bytes to U+FFFD and sending silently altered data.")]
	public void Create_Should_Reject_A_RowsFile_That_Is_Not_Valid_Utf8_Without_Posting() {
		// Arrange - 0xC3 opens a two-byte sequence that 0x28 cannot continue, so strict UTF-8 rejects it.
		MockFileSystem fileSystem = new();
		string rowsFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(),
			$"odata-create-utf8-{System.Guid.NewGuid():N}.json");
		byte[] invalidUtf8 = [
			(byte)'[', (byte)'{', (byte)'"', (byte)'N', (byte)'"', (byte)':', (byte)'"',
			0xC3, 0x28,
			(byte)'"', (byte)'}', (byte)']'
		];
		fileSystem.AddFile(rowsFile, new MockFileData(invalidUtf8));
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		ODataCreateTool tool = new(resolver, new ODataFileContract(fileSystem));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", RowsFile = rowsFile
		});

		// Assert
		response.Created.Should().Be(0);
		response.Error.Should().Contain("not valid UTF-8",
			because: "a corrupted payload is a caller-facing input error, not a transport failure");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses a UTF-16LE rows-file and issues no POST: the BOM must not select an encoding, or the UTF-8-only contract is decided by the file rather than by the contract.")]
	public void Create_Should_Reject_A_Utf16_RowsFile_Without_Posting() {
		// Arrange - a real UTF-16LE payload WITH its BOM. ReadAllText's BOM detection would decode this fine.
		MockFileSystem fileSystem = new();
		string rowsFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(),
			$"odata-create-utf16-{System.Guid.NewGuid():N}.json");
		byte[] utf16WithBom = new UnicodeEncoding(bigEndian: false, byteOrderMark: true)
			.GetPreamble()
			.Concat(Encoding.Unicode.GetBytes("[{\"Name\":\"A\"}]"))
			.ToArray();
		fileSystem.AddFile(rowsFile, new MockFileData(utf16WithBom));
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		ODataCreateTool tool = new(resolver, new ODataFileContract(fileSystem));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", RowsFile = rowsFile
		});

		// Assert
		response.Created.Should().Be(0);
		response.Error.Should().Contain("not valid UTF-8",
			because: "0xFF/0xFE cannot start a UTF-8 sequence, so a UTF-16 file must be reported as the input error it is rather than decoded by BOM detection");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts a UTF-8 rows-file that carries a BOM: it is legal UTF-8, and the BOM must be stripped before JSON parsing rather than reaching the parser as U+FEFF.")]
	public void Create_Should_Accept_A_Utf8_RowsFile_With_A_Bom() {
		// Arrange
		MockFileSystem fileSystem = new();
		string rowsFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(),
			$"odata-create-utf8bom-{System.Guid.NewGuid():N}.json");
		byte[] utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
			.GetPreamble()
			.Concat(Encoding.UTF8.GetBytes("[{\"Name\":\"A\"}]"))
			.ToArray();
		fileSystem.AddFile(rowsFile, new MockFileData(utf8WithBom));
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Id\":\"00000000-0000-0000-0000-000000000001\"}");
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		ODataCreateTool tool = new(resolver, new ODataFileContract(fileSystem));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", RowsFile = rowsFile
		});

		// Assert
		response.Error.Should().BeNull(
			because: "a UTF-8 BOM is legal UTF-8; rejecting it would make the strictness about the BOM rather than about the encoding");
		response.Created.Should().Be(1,
			because: "the row behind the BOM must still be posted");
	}

	[Test]
	[Category("Unit")]
	[Description("Counts the same bare array identically whether it arrives inline or through rows-file, so the summary does not depend on how the payload was supplied.")]
	public void Create_Should_Report_The_Same_Counts_For_Inline_And_File_Rows() {
		// Arrange
		const string bareArray = "[{\"Name\":\"A\"},{\"Name\":\"B\"},{\"Name\":\"C\"}]";
		MockFileSystem fileSystem = new();
		string rowsFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(),
			$"odata-create-parity-{System.Guid.NewGuid():N}.json");
		fileSystem.AddFile(rowsFile, new MockFileData(bareArray, Encoding.UTF8));

		// Act
		ODataCreateBatchResponse inlineResponse = CreateWithRows(fileSystem,
			args => args with { Rows = Arr(bareArray) });
		ODataCreateBatchResponse fileResponse = CreateWithRows(fileSystem,
			args => args with { RowsFile = rowsFile });

		// Assert
		fileResponse.Created.Should().Be(inlineResponse.Created,
			because: "rows-file is a delivery mechanism for the same array, not a different contract");
		fileResponse.Failed.Should().Be(inlineResponse.Failed);
		fileResponse.Results.Should().HaveSameCount(inlineResponse.Results);
		inlineResponse.Created.Should().Be(3, because: "all three rows of the bare array are posted");
	}

	private static ODataCreateBatchResponse CreateWithRows(
		MockFileSystem fileSystem, Func<ODataCreateArgs, ODataCreateArgs> configure) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Account");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Id\":\"11111111-1111-1111-1111-111111111111\"}");
		ODataCreateTool tool = new(resolver, new ODataFileContract(fileSystem));
		return tool.Create(configure(new ODataCreateArgs { EnvironmentName = "dev", Entity = "Account" }));
	}

	private static JsonElement Arr(string json) => JsonDocument.Parse(json).RootElement.Clone();

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable, non-read-only, non-destructive, non-idempotent MCP tool name for odata-create.")]
	public void Create_Should_Advertise_Stable_Tool_Name() {
		// Arrange
		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ODataCreateTool)
			.GetMethod(nameof(ODataCreateTool.Create))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(ODataCreateTool.ToolName, because: "the tool name is part of the stable MCP contract");
		attribute.ReadOnly.Should().BeFalse(because: "odata-create mutates state by inserting records");
		attribute.Destructive.Should().BeFalse(because: "creating a record does not destroy existing state");
		attribute.Idempotent.Should().BeFalse(because: "repeating a create inserts another record");
	}

	[Test]
	[Category("Unit")]
	[Description("Posts each row to the entity set URL and returns the created records with their Ids.")]
	public void Create_Should_Post_Rows_And_Return_Created_Records() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(
				"{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"Acme\"}",
				"{\"Id\":\"22222222-2222-2222-2222-222222222222\",\"Name\":\"Globex\"}");
		ODataCreateTool tool = new(resolver, new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>()));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev",
			Entity = "Account",
			Rows = Arr("[{\"Name\":\"Acme\"},{\"Name\":\"Globex\"}]")
		});

		// Assert
		response.Created.Should().Be(2, because: "both rows insert successfully");
		response.Failed.Should().Be(0, because: "no row failed");
		response.Results[0].Index.Should().Be(0, because: "per-row results preserve input order");
		response.Results[0].Id.Should().Be("11111111-1111-1111-1111-111111111111", because: "the first created record Id is reported");
		response.Results[1].Index.Should().Be(1, because: "per-row results preserve input order");
		response.Results[1].Id.Should().Be("22222222-2222-2222-2222-222222222222", because: "the second created record Id is reported");
		urlBuilder.Received(1).Build("odata/Account");
		client.Received(2).ExecutePostRequest("http://creatio/odata/Account", Arg.Any<string>(), 30_000, 1, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves environment-scoped dependencies for the provided environment name.")]
	public void Create_Should_Resolve_Environment_Scoped_Client() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://env/odata/Account");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Id\":\"11111111-1111-1111-1111-111111111111\"}");
		ODataCreateTool tool = new(resolver, new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>()));

		// Act
		tool.Create(new ODataCreateArgs { EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"A\"}]") });

		// Assert
		resolver.Received(1).Resolve<IApplicationClient>(Arg.Is<EnvironmentOptions>(o => o.Environment == "dev"));
		resolver.Received(1).Resolve<IServiceUrlBuilder>(Arg.Is<EnvironmentOptions>(o => o.Environment == "dev"));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a request-level failure without any remote call when entity is missing.")]
	public void Create_Should_Fail_When_Entity_Missing() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		ODataCreateTool tool = new(resolver, new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>()));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = " ", Rows = Arr("[{\"Name\":\"A\"}]")
		});

		// Assert
		response.Error.Should().Be("entity is required.", because: "a blank entity is a request-level error");
		resolver.DidNotReceive().Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a request-level failure without any remote call when rows is empty.")]
	public void Create_Should_Fail_When_Rows_Empty() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		ODataCreateTool tool = new(resolver, new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>()));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[]")
		});

		// Assert
		response.Error.Should().Contain("rows is required", because: "an empty rows array is a request-level error");
		resolver.DidNotReceive().Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("An OData error body on a row is surfaced as a structured per-row failure.")]
	public void Create_Should_Surface_ODataError_As_Failure() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Account");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"error\":{\"code\":\"\",\"message\":\"Column Name is required\"}}");
		ODataCreateTool tool = new(resolver, new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>()));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"X\":1}]")
		});

		// Assert
		response.Failed.Should().Be(1, because: "the single row failed with an OData error");
		response.Results.Single().Success.Should().BeFalse(because: "an OData error body is not a successful create");
		response.Results.Single().Error.Should().Be("Column Name is required", because: "the OData error message is surfaced verbatim");
	}

	[Test]
	[Category("Unit")]
	[Description("An ASP.NET server error body returned with a non-failing status is reported as a per-row failure, not a created record.")]
	public void Create_Should_Surface_AspNet_ServerError_As_Failure() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/AddressType");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Message\":\"An error has occurred.\",\"ExceptionMessage\":\"Object reference not set to an instance of an object.\",\"ExceptionType\":\"System.NullReferenceException\"}");
		ODataCreateTool tool = new(resolver, new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>()));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "AddressType", Rows = Arr("[{\"Name\":\"Office\"}]")
		});

		// Assert
		response.Results.Single().Success.Should().BeFalse(because: "a server error body must never be reported as a successful create");
		response.Results.Single().Error.Should().Contain("Object reference", because: "the ASP.NET exception message is surfaced");
		response.Results.Single().Id.Should().BeNull(because: "no record was created");
	}

	[Test]
	[Category("Unit")]
	[Description("A Web API routing error body ({Message, MessageDetail}) for an unregistered controller is reported as a per-row failure, not a created record.")]
	public void Create_Should_Surface_Routing_Error_As_Failure() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/0/odata/UsrCustomerStatus");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Message\":\"No HTTP resource was found that matches the request URI '.../0/odata/UsrCustomerStatus'.\",\"MessageDetail\":\"No type was found that matches the controller named 'UsrCustomerStatus'.\"}");
		ODataCreateTool tool = new(resolver, new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>()));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "UsrCustomerStatus", Rows = Arr("[{\"Name\":\"Active\"}]")
		});

		// Assert
		response.Failed.Should().Be(1, because: "the single row failed with a routing error");
		response.Created.Should().Be(0, because: "no record was created against an unregistered entity set");
		response.Results.Single().Success.Should().BeFalse(because: "a {Message, MessageDetail} routing body must never be reported as a successful create");
		response.Results.Single().Error.Should().Contain("controller named 'UsrCustomerStatus'", because: "the MessageDetail identifies the unregistered controller");
		response.Results.Single().Error.Should().Contain(ODataResponseError.UnregisteredEntityHint, because: "the create path funnels through the same shared TryDetect and must surface the identical hint (asserted via the constant to avoid drift)");
		response.Results.Single().Id.Should().BeNull(because: "no record was created against an unregistered entity set");
	}

	[Test]
	[Category("Unit")]
	[Description("A bare {Message} body without MessageDetail is a per-row failure without the unregistered-entity hint, mirroring the read-side boundary on the shared detector.")]
	public void Create_Should_Surface_Bare_Message_Body_As_Failure_Without_Registration_Hint() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Account");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Message\":\"Authorization has been denied for this request.\"}");
		ODataCreateTool tool = new(resolver, new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>()));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"Office\"}]")
		});

		// Assert
		response.Failed.Should().Be(1, because: "a bare {Message} body is an error, not a created record");
		response.Results.Single().Success.Should().BeFalse(because: "a bare {Message} body with no entity members is not a successful create");
		response.Results.Single().Error.Should().Contain("Authorization has been denied", because: "the Message text is surfaced verbatim");
		response.Results.Single().Error.Should().NotContain(ODataResponseError.UnregisteredEntityHint, because: "without MessageDetail the failure is not identifiable as a routing error, so the registration hint must not be appended");
	}

	[Test]
	[Category("Unit")]
	[Description("A created-record echo that legitimately carries a Message column is reported as a successful create, proving the routing-error heuristic does not misfire on genuine create responses.")]
	public void Create_Should_Not_Misclassify_Created_Entity_With_Message_Column() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/EmailMessageData");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"@odata.context\":\"http://creatio/odata/$metadata#EmailMessageData/$entity\",\"Id\":\"22222222-2222-2222-2222-222222222222\",\"Message\":\"Hello there\"}");
		ODataCreateTool tool = new(resolver, new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>()));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "EmailMessageData", Rows = Arr("[{\"Message\":\"Hello there\"}]")
		});

		// Assert
		response.Created.Should().Be(1, because: "a create echo carrying @odata.context + Id is a real created record, even with a Message column");
		response.Results.Single().Success.Should().BeTrue(because: "the routing-error detection must not swallow a genuine created-record echo");
		response.Results.Single().Id.Should().Be("22222222-2222-2222-2222-222222222222", because: "the created record's Id must be surfaced");
	}

	[Test]
	[Category("Unit")]
	[Description("The absolute request URI carried by a bare-Message routing body is redacted on the create path too, mirroring the read-side guard so a silent removal of the Redact call would fail a test.")]
	public void Create_Should_Redact_Server_Uri_In_Routing_Error() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://secret-host:88/prod-app/0/odata/UsrCustomerStatus");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Message\":\"No HTTP resource was found that matches the request URI 'http://secret-host:88/prod-app/0/odata/UsrCustomerStatus'.\"}");
		ODataCreateTool tool = new(resolver, new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>()));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "UsrCustomerStatus", Rows = Arr("[{\"Name\":\"Active\"}]")
		});

		// Assert
		response.Results.Single().Success.Should().BeFalse(because: "a bare-Message routing body is still a per-row failure on the create path");
		response.Results.Single().Error.Should().NotContain("secret-host", because: "the environment host embedded in the routing Message must be redacted on the create path exactly as on the read path");
	}

	[Test]
	[Category("Unit")]
	[Description("An empty {Message} (or empty {Message, MessageDetail}) body is surfaced as a per-row failure with an explicit contentless-response message and no registration hint, verifying the empty-body fallback branch.")]
	public void Create_Should_Surface_Empty_Message_Body_As_Failure_With_Explicit_Text() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Account");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Message\":\"\",\"MessageDetail\":\"\"}");
		ODataCreateTool tool = new(resolver, new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>()));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"Office\"}]")
		});

		// Assert
		response.Results.Single().Success.Should().BeFalse(because: "a body whose only members are empty Message/MessageDetail is an error, not a created record");
		response.Results.Single().Error.Should().Be("Creatio returned an empty error response.", because: "an empty error body must degrade to an explicit contentless message rather than an empty or leading-space string");
		response.Results.Single().Error.Should().NotContain(ODataResponseError.UnregisteredEntityHint, because: "an empty body carries no MessageDetail, so it is not identifiable as a routing error and must not get the registration hint");
	}

	[Test]
	[Category("Unit")]
	[Description("An unrecognized error body (not one of TryDetect's shapes) that reaches the id-missing fallback is redacted, so response host detail cannot leak through the create path's catch-all failure branch.")]
	public void Create_Should_Redact_Unrecognized_Error_Body_In_Id_Missing_Fallback() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Account");
		// A ModelState validation body carries a member beyond Message/MessageDetail, so TryDetect does
		// not recognize it; with no Id it falls through to the id-missing fallback branch.
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Message\":\"The request is invalid.\",\"ModelState\":{\"row\":[\"failed calling http://secret-host:88/prod-app/0/odata/Account\"]}}");
		ODataCreateTool tool = new(resolver, new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>()));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"Office\"}]")
		});

		// Assert
		response.Results.Single().Success.Should().BeFalse(because: "a body with no created-record Id is a per-row failure");
		response.Results.Single().Error.Should().NotContain("secret-host", because: "the id-missing fallback embeds raw response text and must redact host detail to keep parity with the other error branches");
	}

	[Test]
	[Category("Unit")]
	[Description("A numeric primary key in the response body is accepted as a created record rather than reported as a missing Id.")]
	public void Create_Should_Accept_Numeric_Id() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/NumberKeyed");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Id\":42,\"Name\":\"Office\"}");
		ODataCreateTool tool = new(resolver, new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>()));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "NumberKeyed", Rows = Arr("[{\"Name\":\"Office\"}]")
		});

		// Assert
		response.Created.Should().Be(1, because: "a numeric Id still identifies a created record");
		response.Results.Single().Success.Should().BeTrue(because: "a non-string key must not be misreported as a failure");
		response.Results.Single().Id.Should().Be("42", because: "the numeric key is surfaced as its raw value");
	}

	[Test]
	[Category("Unit")]
	[Description("A success-status body without an Id is treated as a per-row failure, since a real OData create always echoes the record Id.")]
	public void Create_Should_Fail_When_Response_Has_No_Id() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/AddressType");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Name\":\"Office\"}");
		ODataCreateTool tool = new(resolver, new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>()));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "AddressType", Rows = Arr("[{\"Name\":\"Office\"}]")
		});

		// Assert
		response.Results.Single().Success.Should().BeFalse(because: "a body without an Id is not a created record");
		response.Results.Single().Error.Should().Contain("did not return a record Id", because: "the missing-Id reason is reported");
	}

	[Test]
	[Category("Unit")]
	[Description("By default a failed row does not abort the batch: remaining rows are still inserted and reported.")]
	public void Create_Should_Continue_After_Row_Failure_By_Default() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Account");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(
				"{\"error\":{\"code\":\"\",\"message\":\"bad row\"}}",
				"{\"Id\":\"22222222-2222-2222-2222-222222222222\"}");
		ODataCreateTool tool = new(resolver, new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>()));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"Bad\"},{\"Name\":\"Good\"}]")
		});

		// Assert
		response.Created.Should().Be(1, because: "the second row inserts even though the first failed");
		response.Failed.Should().Be(1, because: "the first row failed");
		response.Results.Should().HaveCount(2, because: "continue-on-error attempts every row");
		client.Received(2).ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30_000, 1, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("With stop-on-error the batch aborts after the first failed row and does not attempt later rows.")]
	public void Create_Should_Stop_After_Row_Failure_When_Requested() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Account");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"error\":{\"code\":\"\",\"message\":\"bad row\"}}");
		ODataCreateTool tool = new(resolver, new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>()));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", StopOnError = true,
			Rows = Arr("[{\"Name\":\"Bad\"},{\"Name\":\"NeverTried\"}]")
		});

		// Assert
		response.Failed.Should().Be(1, because: "the first row failed and aborted the batch");
		response.Results.Should().HaveCount(1, because: "stop-on-error aborts before the second row");
		client.Received(1).ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30_000, 1, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("Stops the batch when the caller cancels it: no further row is POSTed, and the first unattempted row is reported as definitely not created.")]
	public void Create_Should_Stop_Posting_When_The_Caller_Cancels() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Account");
		using CancellationTokenSource cancellation = new();
		//Cancel from inside the first POST, so the guard is exercised BETWEEN rows exactly as a caller
		//disconnecting mid-batch would trigger it.
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => {
				cancellation.Cancel();
				return "{\"Id\":\"11111111-1111-1111-1111-111111111111\"}";
			});
		ODataCreateTool tool = new(resolver, new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>()));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev",
			Entity = "Account",
			Rows = Arr("[{\"Name\":\"First\"},{\"Name\":\"Second\"},{\"Name\":\"Third\"}]")
		}, cancellation.Token);

		// Assert
		client.Received(1).ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		response.Created.Should().Be(1, because: "only the row already in flight completes");
		response.Results.Should().HaveCount(2,
			because: "the first row that was not attempted is reported, and nothing after it is sent");
		response.Results[1].RecordCreated.Should().BeFalse(
			because: "a row that was never sent definitely did not insert, so the caller can re-send it safely");
		response.Results[1].Error.Should().Contain("cancelled",
			because: "the caller has to know the batch stopped early and why");
	}

	[Test]
	[Category("Unit")]
	[Description("Caps each row's request timeout to what is left of the batch budget, so the last row cannot overshoot the aggregate deadline by a further full timeout.")]
	public void Create_Should_Cap_Row_Timeout_To_The_Batch_Budget() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/Account");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Id\":\"11111111-1111-1111-1111-111111111111\"}");
		ODataCreateTool tool = new(resolver, new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>()));

		// Act
		tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"Acme\"}]")
		});

		// Assert
		client.Received(1).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Any<string>(),
			Arg.Is<int>(timeout => timeout > 0 && timeout <= ODataCreateTool.RowRequestTimeoutMs),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps the row-ceiling wording and the enforced ceiling derived from one another, so an agent cannot read one limit off a description and be rejected by another.")]
	public void Create_Should_Publish_The_Enforced_Row_Ceiling() {
		// Arrange

		// Act
		string limitText = ODataCreateTool.RowCountLimitDescription;

		// Assert
		ODataCreateTool.MaxRowCountText.Should().Be(ODataCreateTool.MaxRowCount.ToString(CultureInfo.InvariantCulture),
			because: "the text form of the ceiling is what every description is built from, so it must equal the enforced value");
		limitText.Should().Contain(ODataCreateTool.MaxRowCountText,
			because: "the shared wording has to state the number it enforces");
	}

	#region record-created side-effect state

	private static ODataCreateTool BuildTool(IApplicationClient client) {
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		return new ODataCreateTool(resolver, new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>()));
	}

	[Test]
	[Category("Unit")]
	[Description("The advertised odata-create description documents the record-created side-effect state and the no-blind-retry rule, so a consumer learns the contract from the tool surface rather than from clio source.")]
	public void Create_Should_Advertise_The_RecordCreated_Contract() {
		// Arrange
		// Act
		// Fully qualified: NUnit ships its own DescriptionAttribute, and this assertion is about the
		// System.ComponentModel one the MCP surface actually advertises.
		System.ComponentModel.DescriptionAttribute description =
			(System.ComponentModel.DescriptionAttribute)typeof(ODataCreateTool)
				.GetMethod(nameof(ODataCreateTool.Create))!
				.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
				.Single();

		// Assert
		// Asserts the FIELD NAME only. It is a contract identifier and cannot change without a consumer
		// noticing; the surrounding prose can be reworded freely, and pinning it here would fail CI for a
		// rewrite rather than for a defect.
		description.Description.Should().Contain("record-created",
			because: "the side-effect state is only actionable if the advertised contract names it");
	}

	[Test]
	[Category("Unit")]
	[Description("A row echoed back with its Id reports record-created true, so a caller can act on the side effect without inferring it from the success flag.")]
	public void Create_Should_Report_RecordCreated_True_On_Success() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Id\":\"11111111-1111-1111-1111-111111111111\"}");
		ODataCreateTool tool = BuildTool(client);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"Acme\"}]")
		});

		// Assert
		response.Results[0].RecordCreated.Should().BeTrue(because: "the server echoed the created record");
		response.Results[0].RetryGuidance.Should().BeNull(because: "a verified insert needs no retry advice");
		response.Unverified.Should().Be(0, because: "nothing is in an unknown state");
	}

	[Test]
	[Category("Unit")]
	[Description("A server error payload leaves record-created unknown rather than false, because Creatio can fail a POST after the row is already written - reporting not-inserted would invite a duplicating retry.")]
	public void Create_Should_Report_RecordCreated_Unknown_On_Server_Error() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"error\":{\"code\":\"\",\"message\":\"An error has occurred.\"}}");
		ODataCreateTool tool = BuildTool(client);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "MailboxSyncSettings", Rows = Arr("[{\"UserName\":\"probe\"}]")
		});

		// Assert
		response.Results[0].Success.Should().BeFalse(because: "the server reported the call as failed");
		response.Results[0].RecordCreated.Should().BeNull(
			because: "a post-insert handler can throw after the record persists, so not-inserted is NOT known");
		response.Results[0].RetryGuidance.Should().NotBeNullOrWhiteSpace(
			because: "an unknown side effect must tell the caller to verify instead of retrying");
		response.Unverified.Should().Be(1, because: "the batch must surface how many rows are unverified");
	}

	[Test]
	[Description("A row rejected locally for its shape reports record-created false, because no request ever left clio - the caller can fix and re-send safely.")]
	[Category("Unit")]
	public void Create_Should_Report_RecordCreated_False_When_Row_Rejected_Locally() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		ODataCreateTool tool = BuildTool(client);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{}]")
		});

		// Assert
		response.Results[0].RecordCreated.Should().BeFalse(
			because: "the row never reached the server, so not-inserted is verified");
		response.Unverified.Should().Be(0, because: "a locally rejected row is not an unknown outcome");
		client.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("A non-JSON response body (an IIS/proxy error page instead of Creatio's OData pipeline) leaves record-created unknown rather than reporting a successful create — the request never reached Creatio intact, so the row's side effect cannot be assumed.")]
	public void Create_Should_Report_RecordCreated_Unknown_On_Non_Json_Response() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<html><head><title>404 - File or directory not found.</title></head></html>");
		ODataCreateTool tool = BuildTool(client);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"Acme\"}]")
		});

		// Assert
		response.Results[0].Success.Should().BeFalse(because: "an HTML error page proves the request never reached Creatio's OData pipeline");
		response.Results[0].RecordCreated.Should().BeNull(
			because: "the request did not reach Creatio intact, so whether a post-insert handler already wrote the row is unknown");
		response.Results[0].RetryGuidance.Should().NotBeNullOrWhiteSpace(
			because: "an unknown side effect must tell the caller to verify instead of retrying");
		response.Results[0].Error.Should().Contain("was not JSON",
			because: "the diagnostic must point at the transport layer, not the request's OData/ESQ shape");
		response.Unverified.Should().Be(1, because: "the batch must surface how many rows are unverified");
	}

	[Test]
	[Category("Unit")]
	[Description("A transport failure leaves record-created unknown, because the request may have been applied before the error surfaced on the client side.")]
	public void Create_Should_Report_RecordCreated_Unknown_On_Transport_Failure() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => throw new HttpRequestException("connection reset"));
		ODataCreateTool tool = BuildTool(client);

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = Arr("[{\"Name\":\"Acme\"}]")
		});

		// Assert
		response.Results[0].RecordCreated.Should().BeNull(
			because: "a client-side transport error cannot prove the server did not apply the insert");
		response.Results[0].RetryGuidance.Should().NotBeNullOrWhiteSpace(
			because: "the caller must verify before re-sending");
	}

	#endregion
}
