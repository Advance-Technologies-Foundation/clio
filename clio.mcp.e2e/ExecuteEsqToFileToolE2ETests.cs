using System.Text.Json;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the <c>execute-esq-to-file</c> MCP tool.
/// </summary>
/// <remarks>
/// Every test runs the real clio MCP process against a loopback DataService stub, so the rows written to disk
/// are compared byte-for-byte with the rows the stub returned, and the stub's SelectQuery counter shows whether a
/// refused output path was rejected before the query was sent.
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ExecuteEsqToFileTool.ToolName)]
[NonParallelizable]
public sealed class ExecuteEsqToFileToolE2ETests {

	private const string ToolName = ExecuteEsqToFileTool.ToolName;
	private const string EnvironmentName = "execute-esq-to-file-e2e";

	// The file holds the raw text of the response's "rows" array, so the stub body is kept free of whitespace
	// and the expected file content is exactly that array.
	private const string StubRows =
		"[{\"Id\":\"0b6f5b1e-5d2a-4d3f-9a51-6d1c1a0e7a11\",\"Name\":\"Alpha\"}," +
		"{\"Id\":\"2c3e0f7a-8b4d-4e9f-b1a2-3c4d5e6f7a82\",\"Name\":\"Beta\"}]";
	private const string StubSelectQueryBody = "{\"rows\":" + StubRows + ",\"success\":true}";

	[Test]
	[Description("Exposes execute-esq-to-file as a discoverable, non-destructive tool through the get-tool-contract compact index on the lazy MCP surface.")]
	[AllureTag(ToolName)]
	[AllureName("execute-esq-to-file is discoverable on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server and verifies execute-esq-to-file is reachable and carries one non-destructive entry in the compact discovery index.")]
	public async Task ExecuteEsqToFile_Should_Be_Discoverable_On_Lazy_Surface() {
		await RunAgainstStubAsync(async (session, _, cancellationToken) => {
			// Act
			IReadOnlyCollection<string> toolNames = await AllureApi.Step(
				"Act by listing the reachable tool names",
				async () => await session.ListReachableToolNamesAsync(cancellationToken));
			IReadOnlyList<ToolContractIndexEntry> index = await AllureApi.Step(
				"Act by reading the compact discovery index",
				async () => await session.GetToolContractIndexAsync(cancellationToken));

			// Assert
			AllureApi.Step("Assert the tool is reachable", () =>
				toolNames.Should().Contain(ToolName,
					because: "execute-esq-to-file must be reachable on the lazy surface through clio-run"));
			ToolContractIndexEntry entry = AllureApi.Step("Assert the index has one entry for the tool", () =>
				index.Should().ContainSingle(item => item.Name == ToolName,
					because: "the compact discovery index must carry exactly one entry for execute-esq-to-file").Which);
			AllureApi.Step("Assert the tool is not flagged destructive", () =>
				entry.Destructive.Should().NotBe(true,
					because: "the tool only creates a new local file and refuses an existing one, so it is not destructive"));
		});
	}

	[Test]
	[Description("Writes the SelectQuery rows to the requested file byte-for-byte and returns the row count and the resolved output-file instead of the rows.")]
	[AllureTag(ToolName)]
	[AllureName("execute-esq-to-file writes the rows array and returns only its path and count")]
	[AllureDescription("Runs execute-esq-to-file against a loopback DataService stub and verifies the file holds exactly the rows array the stub returned, while the response carries success, count and output-file but no rows.")]
	public async Task ExecuteEsqToFile_Should_Write_Rows_And_Return_Path_Without_Rows() {
		// Arrange
		string outputDirectory = ScratchDirectory.CreateUnderPhysicalTemp("clio-esq-to-file-out");
		string outputFile = Path.Combine(outputDirectory, "rows.json");
		try {
			await RunAgainstStubAsync(async (session, stub, cancellationToken) => {
				// Act
				CallToolResult callResult = await AllureApi.Step(
					"Act by invoking execute-esq-to-file",
					async () => await CallAsync(session, outputFile, cancellationToken));
				ExecuteEsqResponse response = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(callResult);
				JsonElement raw = EntitySchemaStructuredResultParser.Extract<JsonElement>(callResult);

				// Assert
				AllureApi.Step("Assert a structured tool result", () =>
					callResult.IsError.Should().NotBeTrue(
						because: "a valid file-mode call must return a structured tool response, not a protocol error"));
				AllureApi.Step("Assert success", () =>
					response.Success.Should().BeTrue(
						because: $"the stub answered the SelectQuery with two rows: {response.Error}"));
				AllureApi.Step("Assert the row count", () =>
					response.Count.Should().Be(2,
						because: "the count must describe the rows written to the file"));
				AllureApi.Step("Assert the response names the resolved file", () =>
					response.OutputFile.Should().Be(outputFile,
						because: "the output directory is already a physical path, so the resolved path is the requested one"));
				AllureApi.Step("Assert the response carries no rows", () =>
					raw.TryGetProperty("rows", out _).Should().BeFalse(
						because: "the file destination exists to keep the rows out of the MCP result"));
				AllureApi.Step("Assert the file holds the exact rows array", () =>
					File.ReadAllText(outputFile).Should().Be(StubRows,
						because: "the file must hold the rows array exactly as DataService returned it"));
				AllureApi.Step("Assert one SelectQuery was sent", () =>
					stub.SelectQueryRequests.Should().Be(1,
						because: "one call runs the query once"));
			});
		} finally {
			ScratchDirectory.TryDelete(outputDirectory);
		}
	}

	[Test]
	[Description("Refuses an output-file that already exists, leaves its content unchanged and does not send the SelectQuery.")]
	[AllureTag(ToolName)]
	[AllureName("execute-esq-to-file refuses an existing output file before querying")]
	[AllureDescription("Pre-creates the output file with a sentinel, calls execute-esq-to-file on that path and verifies the call fails, the sentinel is intact, and the DataService stub received no SelectQuery.")]
	public async Task ExecuteEsqToFile_Should_Refuse_Existing_Output_File_Before_Querying() {
		// Arrange
		const string sentinel = "existing-content-must-survive";
		string outputDirectory = ScratchDirectory.CreateUnderPhysicalTemp("clio-esq-to-file-existing");
		string outputFile = Path.Combine(outputDirectory, "rows.json");
		File.WriteAllText(outputFile, sentinel);
		try {
			await RunAgainstStubAsync(async (session, stub, cancellationToken) => {
				// Act
				CallToolResult callResult = await AllureApi.Step(
					"Act by invoking execute-esq-to-file on an existing file",
					async () => await CallAsync(session, outputFile, cancellationToken));
				ExecuteEsqResponse response = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(callResult);

				// Assert
				AllureApi.Step("Assert failure", () =>
					response.Success.Should().BeFalse(
						because: "an explicit output-file must never overwrite an existing file"));
				AllureApi.Step("Assert the error names the existing file", () =>
					response.Error.Should().Contain("already exists",
						because: "the caller must be told why the path was refused"));
				AllureApi.Step("Assert the existing content is unchanged", () =>
					File.ReadAllText(outputFile).Should().Be(sentinel,
						because: "a refused call must leave the existing file untouched"));
				AllureApi.Step("Assert no SelectQuery was sent", () =>
					stub.SelectQueryRequests.Should().Be(0,
						because: "the output path is resolved before the query, so a refused path costs no query"));
			});
		} finally {
			ScratchDirectory.TryDelete(outputDirectory);
		}
	}

	[Test]
	[Description("Refuses an output-file outside the workspace and the OS temp directory, writes nothing and does not send the SelectQuery.")]
	[AllureTag(ToolName)]
	[AllureName("execute-esq-to-file refuses an output file outside the allowed locations")]
	[AllureDescription("Calls execute-esq-to-file with a path at the root of the temp directory's volume and verifies the call fails with the confinement error, no file appears, and the DataService stub received no SelectQuery.")]
	public async Task ExecuteEsqToFile_Should_Refuse_Output_File_Outside_Allowed_Locations() {
		// Arrange
		string outputFile = Path.Combine(
			Path.GetPathRoot(Path.GetTempPath())!,
			$"clio-esq-to-file-outside-{Guid.NewGuid():N}.json");
		await RunAgainstStubAsync(async (session, stub, cancellationToken) => {
			// Act
			CallToolResult callResult = await AllureApi.Step(
				"Act by invoking execute-esq-to-file with a path outside the allowed locations",
				async () => await CallAsync(session, outputFile, cancellationToken));
			ExecuteEsqResponse response = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(callResult);

			// Assert
			AllureApi.Step("Assert failure", () =>
				response.Success.Should().BeFalse(
					because: "output-file is confined to the workspace or the OS temp directory"));
			AllureApi.Step("Assert the confinement error", () =>
				response.Error.Should().Contain("outside the allowed locations",
					because: "the caller must be told where the file may be written"));
			AllureApi.Step("Assert no file was written", () =>
				File.Exists(outputFile).Should().BeFalse(
					because: "a refused path must never produce a file"));
			AllureApi.Step("Assert no SelectQuery was sent", () =>
				stub.SelectQueryRequests.Should().Be(0,
					because: "the output path is resolved before the query, so a refused path costs no query"));
		});
	}

	private static Task<CallToolResult> CallAsync(
		McpServerSession session,
		string outputFile,
		CancellationToken cancellationToken) =>
		session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = EnvironmentName,
					["output-file"] = outputFile,
					["query"] = new Dictionary<string, object?> {
						["rootSchemaName"] = "Contact",
						["allColumns"] = true,
						["rowCount"] = 10
					}
				}
			},
			cancellationToken);

	private static async Task RunAgainstStubAsync(
		Func<McpServerSession, SelectQueryStubServer, CancellationToken, Task> act) {
		await using SelectQueryStubServer stub = SelectQueryStubServer.Start(StubSelectQueryBody);
		// The redirected home lives beside the output directories, never around them: clio refuses an
		// output-file inside its own configuration directory.
		string tempHome = ScratchDirectory.CreateUnderPhysicalTemp("clio-esq-to-file-home");
		string envVarName = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		settings.ProcessEnvironmentVariables[envVarName] = tempHome;
		try {
			using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
				$$"""
				{
				  "ActiveEnvironmentKey": "{{EnvironmentName}}",
				  "Autoupdate": false,
				  "Environments": {
				    "{{EnvironmentName}}": {
				      "Uri": "{{stub.ApplicationUri}}",
				      "Login": "Supervisor",
				      "Password": "Supervisor",
				      "IsNetCore": false
				    }
				  }
				}
				""",
				settings.ClioProcessPath,
				settings.ProcessEnvironmentVariables);
			using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
			await using McpServerSession session = await AllureApi.Step(
				"Arrange the real MCP process against the loopback DataService stub",
				async () => await McpServerSession.StartAsync(settings, cancellationTokenSource.Token));
			await act(session, stub, cancellationTokenSource.Token);
		} finally {
			ScratchDirectory.TryDelete(tempHome);
		}
	}
}
