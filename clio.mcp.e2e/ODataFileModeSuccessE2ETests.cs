using System.Linq;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the file-backed OData payloads on the SUCCESS path.
/// </summary>
/// <remarks>
/// The other OData fixtures deliberately stop at missing-environment resolution, so nothing there ever
/// fetches a response, writes a file, or POSTs a file-backed payload - the contract that matters most is the
/// one they cannot reach. These tests run against a loopback OData stub instead: they pin the exact bytes
/// odata-read-to-file leaves on disk together with its summary fields, and they prove a rows-file payload
/// actually reaches POST and PATCH rather than being dropped on the way.
/// </remarks>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(ODataReadToFileTool.ToolName)]
[NonParallelizable]
public sealed class ODataFileModeSuccessE2ETests {

	private const string EchoEntity = "labClientStatus";
	private const string PatchMarker = "e2e-patch-marker";
	private const string RecordId = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";

	[Test]
	[AllureTag(ODataReadToFileTool.ToolName)]
	[AllureName("odata-read-to-file writes the exact response bytes and summarizes them")]
	[AllureDescription("Runs odata-read-to-file against a loopback OData endpoint and verifies the persisted file is byte-for-byte the response body, with row-count, column-sizes and the paging annotations reported from the same pass.")]
	[Description("odata-read-to-file writes the raw response unchanged and returns a summary matching it, with no inline value.")]
	public async Task ODataReadToFile_Should_Write_Exact_Response_Bytes() {
		string outputFile = Path.Combine(Path.GetTempPath(), $"odata-read-to-file-e2e-{Guid.NewGuid():N}.json");
		try {
			await RunAgainstEchoStubAsync(async (session, environmentName, cancellationToken) => {
				// Act
				CallToolResult callResult = await session.CallToolAsync(
					ODataReadToFileTool.ToolName,
					new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = environmentName,
							["entity"] = EchoEntity,
							["output-file"] = outputFile,
							["count"] = true
						}
					},
					cancellationToken);
				ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);

				// Assert
				callResult.IsError.Should().NotBeTrue(
					because: "a valid file-mode call must return a structured tool response, not a protocol error");
				response.Success.Should().BeTrue(
					because: $"the stub answered a valid OData collection: {response.Error}");
				response.Value.Should().BeNull(
					because: "the file destination exists precisely to keep the response value out of the MCP result");
				response.OutputFile.Should().NotBeNullOrWhiteSpace(
					because: "the caller needs the resolved path of the file it was told was written");
				File.ReadAllText(response.OutputFile!).Should().Be(
					RuntimeDetectionStubServer.ODataEchoCollectionBody,
					because: "the persisted file must be the raw response byte-for-byte, not a re-serialization of it");
				response.RowCount.Should().Be(2,
					because: "the stub collection carries two object rows");
				response.Count.Should().Be(2,
					because: "the record count must match the rows the response carried");
				response.TotalCount.Should().Be(2,
					because: "count=true must report the server's verified @odata.count");
				response.ColumnSizes.Should().ContainKey("Name",
					because: "the summary is what replaces the inline value, so it must name the columns on disk");
			});
		} finally {
			if (File.Exists(outputFile)) {
				File.Delete(outputFile);
			}
		}
	}

	[Test]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read rejects output-file")]
	[AllureDescription("Verifies the read-only odata-read tool rejects an output-file argument through the real MCP dispatch and points the caller at odata-read-to-file.")]
	[Description("odata-read rejects output-file over the real stdio path, so the file destination cannot re-enter the read-only tool.")]
	public async Task ODataRead_Should_Reject_Output_File_Argument() {
		string outputFile = Path.Combine(Path.GetTempPath(), $"odata-read-rejected-{Guid.NewGuid():N}.json");
		await RunAgainstEchoStubAsync(async (session, environmentName, cancellationToken) => {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				ODataReadTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["entity"] = EchoEntity,
						["output-file"] = outputFile
					}
				},
				cancellationToken);
			ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);

			// Assert
			response.Success.Should().BeFalse(
				because: "output-file is not an argument of the read-only tool");
			response.Error.Should().Contain(ODataReadToFileTool.ToolName,
				because: "the caller must be pointed at the tool that does take a file destination");
			File.Exists(outputFile).Should().BeFalse(
				because: "a rejected argument must never produce a file");
		});
	}

	[Test]
	[AllureTag(ODataCreateTool.ToolName)]
	[AllureName("odata-create sends a rows-file payload to POST")]
	[AllureDescription("Writes a rows-file, runs odata-create against a loopback OData endpoint that echoes the posted row's Name back as the created record Id, and verifies the Id came from the file contents.")]
	[Description("A rows-file payload reaches the POST body: the stub echoes the file's field value back as the created record Id.")]
	public async Task ODataCreate_Should_Send_The_File_Payload_To_Post() {
		string rowName = $"file-payload-{Guid.NewGuid():N}";
		string rowsFile = Path.Combine(Path.GetTempPath(), $"odata-create-payload-{Guid.NewGuid():N}.json");
		File.WriteAllText(rowsFile, $"[{{\"Name\":\"{rowName}\"}}]");
		try {
			await RunAgainstEchoStubAsync(async (session, environmentName, cancellationToken) => {
				// Act
				CallToolResult callResult = await session.CallToolAsync(
					ODataCreateTool.ToolName,
					new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = environmentName,
							["entity"] = EchoEntity,
							["rows-file"] = rowsFile
						}
					},
					cancellationToken);
				ODataCreateBatchResponse response = EntitySchemaStructuredResultParser.Extract<ODataCreateBatchResponse>(callResult);

				// Assert
				response.Created.Should().Be(1,
					because: $"the single file-backed row must be POSTed and reported as created: {response.Error}");
				response.Results.Should().ContainSingle().Which.Id.Should().Be(rowName,
					because: "the stub echoes the POSTed row's Name back as the record Id, so a matching Id proves the file contents reached the request body");
			});
		} finally {
			if (File.Exists(rowsFile)) {
				File.Delete(rowsFile);
			}
		}
	}

	[Test]
	[AllureTag(ODataUpdateTool.ToolName)]
	[AllureName("odata-update sends a rows-file payload to PATCH")]
	[AllureDescription("Writes a rows-file, runs odata-update against a loopback OData endpoint that succeeds only when the PATCH body carries the expected marker, and verifies the update succeeded.")]
	[Description("A rows-file payload reaches the PATCH body: the stub fails the request unless the file's marker is present.")]
	public async Task ODataUpdate_Should_Send_The_File_Payload_To_Patch() {
		string rowsFile = Path.Combine(Path.GetTempPath(), $"odata-update-payload-{Guid.NewGuid():N}.json");
		File.WriteAllText(rowsFile, $"{{\"Name\":\"{PatchMarker}\"}}");
		try {
			await RunAgainstEchoStubAsync(async (session, environmentName, cancellationToken) => {
				// Act
				CallToolResult callResult = await session.CallToolAsync(
					ODataUpdateTool.ToolName,
					new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = environmentName,
							["entity"] = EchoEntity,
							["id"] = RecordId,
							["rows-file"] = rowsFile,
							["confirm"] = true
						}
					},
					cancellationToken);
				ODataWriteResponse response = EntitySchemaStructuredResultParser.Extract<ODataWriteResponse>(callResult);

				// Assert
				response.Success.Should().BeTrue(
					because: $"the stub answers PATCH successfully only when the body carries the file's marker: {response.Error}");
			});
		} finally {
			if (File.Exists(rowsFile)) {
				File.Delete(rowsFile);
			}
		}
	}

	[Test]
	[AllureTag(ODataCreateTool.ToolName)]
	[AllureName("odata-create stops POSTing when the caller cancels the request")]
	[AllureDescription("Runs a slow multi-row odata-create against a loopback OData endpoint, cancels the MCP call mid-batch, and verifies the stub received fewer POSTs than the batch carried - proving the host's cancellation reaches the row loop over the real transport.")]
	[Description("Cancelling the MCP call stops subsequent POSTs: the stub sees fewer requests than the batch had rows.")]
	public async Task ODataCreate_Should_Stop_Posting_When_The_Transport_Call_Is_Cancelled() {
		const int rowCount = 6;
		const int postDelayMs = 1200;
		await StubEnvironmentStand.RunAsync(
			"clio-odata-cancel-e2e",
			new RuntimeDetectionStubServerConfiguration(
				NetCoreHealthEnabled: true,
				NetFrameworkHealthEnabled: true,
				NetCoreServiceEnabled: false,
				NetFrameworkServiceEnabled: true,
				NetCoreUiMarkerEnabled: false,
				NetFrameworkUiMarkerEnabled: true,
				ODataEchoEntity: EchoEntity,
				ODataWriteRequiredMarker: PatchMarker,
				ODataEchoPostDelayMs: postDelayMs),
			async (session, environmentName, stubServer, cancellationToken) => {
				// Arrange - a batch long enough that cancelling after two rows leaves several unsent.
				object[] rows = Enumerable.Range(0, rowCount)
					.Select(object (index) => new Dictionary<string, object?> { ["Name"] = $"row-{index}" })
					.ToArray();
				using CancellationTokenSource callCancellation =
					CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				callCancellation.CancelAfter(TimeSpan.FromMilliseconds(postDelayMs * 2.5));

				// Act
				try {
					await session.CallToolAsync(
						ODataCreateTool.ToolName,
						new Dictionary<string, object?> {
							["args"] = new Dictionary<string, object?> {
								["environment-name"] = environmentName,
								["entity"] = EchoEntity,
								["rows"] = rows
							}
						},
						callCancellation.Token);
				} catch (OperationCanceledException) {
					// Expected: the client stops waiting once it cancels the request.
				}
				// The row already in flight is allowed to finish, so give it more than one delay to land
				// before counting - otherwise the assertion could pass for the wrong reason.
				await Task.Delay(TimeSpan.FromMilliseconds(postDelayMs * 2), cancellationToken);

				// Assert
				int posted = await stubServer.GetODataPostCountAsync(cancellationToken);
				posted.Should().BeGreaterThan(0,
					because: "the batch must actually start before cancellation can be shown to stop it");
				posted.Should().BeLessThan(rowCount,
					because: "cancelling the MCP call must stop the row loop, leaving the remaining rows unsent");
			},
			TimeSpan.FromMinutes(3));
	}

	private static Task RunAgainstEchoStubAsync(
		Func<Support.Mcp.McpServerSession, string, CancellationToken, Task> act) =>
		StubEnvironmentStand.RunAsync(
			"clio-odata-file-mode-e2e",
			new RuntimeDetectionStubServerConfiguration(
				NetCoreHealthEnabled: true,
				NetFrameworkHealthEnabled: true,
				NetCoreServiceEnabled: false,
				NetFrameworkServiceEnabled: true,
				NetCoreUiMarkerEnabled: false,
				NetFrameworkUiMarkerEnabled: true,
				ODataEchoEntity: EchoEntity,
				ODataWriteRequiredMarker: PatchMarker),
			(session, environmentName, _, cancellationToken) => act(session, environmentName, cancellationToken));
}
