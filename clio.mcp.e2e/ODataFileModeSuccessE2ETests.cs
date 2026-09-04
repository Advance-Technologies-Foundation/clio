using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
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

	/// <summary>
	/// Body size the stub is told to stream for the oversized-response test: far past
	/// <see cref="ODataFileContract.MaxResponseBytes"/>, so "cut off near the limit" and "drained the whole
	/// body" are separated by hundreds of megabytes rather than by a margin socket buffering can cross.
	/// </summary>
	private const int OversizedResponseBytes = 512 * 1024 * 1024;

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
	[AllureTag(ODataReadToFileTool.ToolName)]
	[AllureName("odata-read-to-file refuses an oversized response and writes nothing")]
	[AllureDescription("Points odata-read-to-file at a loopback endpoint that streams a body past clio's ceiling with no Content-Length, and verifies the call fails, no file is written, and the MCP session still serves the next call.")]
	[Description("An oversized response is refused as the bytes arrive: no file is written and the session stays usable.")]
	public async Task ODataReadToFile_Should_Refuse_An_Oversized_Response_And_Keep_The_Session_Usable() {
		string outputFile = Path.Combine(Path.GetTempPath(), $"odata-oversized-{Guid.NewGuid():N}.json");
		try {
			await StubEnvironmentStand.RunAsync(
				"clio-odata-oversized-e2e",
				// Far more than the ceiling, so "the server was cut off near the limit" and "the client drained
				// everything" are numerically unmistakable.
				EchoStubConfiguration(oversizedBytes: OversizedResponseBytes),
				async (session, environmentName, stubServer, cancellationToken) => {
					// Act
					CallToolResult callResult = await session.CallToolAsync(
						ODataReadToFileTool.ToolName,
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
						because: "a response past the ceiling must be refused rather than summarized and written");
					response.Error.Should().Contain("exceeds",
						because: "the caller has to be told the response was too large and how to narrow it");
					File.Exists(outputFile).Should().BeFalse(
						because: "nothing may be published for a body that was refused");

					// Assert - the CLIENT stopped reading within one copy buffer of the ceiling. This is the
					// assertion that separates a byte bound from a time bound, and it is machine-independent:
					// the ceiling is tested before each write inside the transport's copy loop, so the count
					// the caller is told about can exceed the limit by at most one 80 KiB read. The previous
					// implementation could only watch the growing staging file from another task, and that
					// count reached 134,676,480 bytes against this same 64 MiB ceiling on a CI agent - twice
					// the limit, and unbounded in principle, because the producer is not scheduled in step
					// with the observer.
					long reportedBytes = ExtractReportedByteCount(response.Error!);
					reportedBytes.Should().BeInRange(
						ODataFileContract.MaxResponseBytes, ODataFileContract.MaxResponseBytes + CopyBufferBytes,
						because: "the ceiling is enforced before each write in the transport copy loop, so the "
							+ "overshoot is exactly one read buffer regardless of host load - a poll-based "
							+ $"bound reports an arbitrary figure instead (reported {reportedBytes} bytes)");

					// Assert - the PRODUCER was cut off near the limit, not after the whole body arrived.
					// This is what separates a real streaming bound from one applied to an already-buffered
					// response: with the latter the server drains all 512 MiB before anything is rejected.
					long sent = await stubServer.GetODataSentBytesAsync(cancellationToken);
					// The server-side figure is the one that stays machine-dependent: the overshoot past the
					// ceiling is whatever the socket buffers had already accepted, which locally is ~72 MiB.
					// It is pinned to twice the ceiling rather than to half the body, which is tight enough to
					// catch the 140-173 MiB the poll-based version let through while still leaving room for
					// socket buffering.
					sent.Should().BeLessThan(2 * ODataFileContract.MaxResponseBytes,
						because: "the transfer must be abandoned close to the ceiling; a figure at or past "
							+ $"twice the limit means it ran too late (server sent {sent} bytes)");

					// Assert - the session survives the refusal and still answers the next call.
					CallToolResult followUp = await session.CallToolAsync(
						ODataReadTool.ToolName,
						new Dictionary<string, object?> {
							["args"] = new Dictionary<string, object?> {
								["environment-name"] = environmentName,
								["entity"] = EchoEntity,
								["output-file"] = "should-be-rejected.json"
							}
						},
						cancellationToken);
					EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(followUp).Error.Should()
						.Contain(ODataReadToFileTool.ToolName,
							because: "the MCP session must still be serving tools after the oversized call was refused");
				},
				TimeSpan.FromMinutes(3));
		} finally {
			if (File.Exists(outputFile)) {
				File.Delete(outputFile);
			}
		}
	}

	/// <summary>
	/// Copy-buffer size of the transport's bounded download loop, which is the whole overshoot the ceiling
	/// can ever have. Pinned as a literal because it is a property of the transport rather than of clio, and
	/// a change to it should make this assertion fail and be re-read rather than adapt silently.
	/// </summary>
	private const long CopyBufferBytes = 81920;

	/// <summary>
	/// Pulls the byte count out of the "Response is at least N bytes, which exceeds the M-byte limit."
	/// message, so the assertion reads the number the caller was actually given rather than trusting a
	/// separate measurement of the same thing.
	/// </summary>
	private static long ExtractReportedByteCount(string error) {
		Match match = Regex.Match(error, @"at least (\d+) bytes");
		match.Success.Should().BeTrue(
			because: $"the oversize error must report the observed byte count; got '{error}'");
		return long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
	}

	private static RuntimeDetectionStubServerConfiguration EchoStubConfiguration(int oversizedBytes = 0) =>
		new(
			NetCoreHealthEnabled: true,
			NetFrameworkHealthEnabled: true,
			NetCoreServiceEnabled: false,
			NetFrameworkServiceEnabled: true,
			NetCoreUiMarkerEnabled: false,
			NetFrameworkUiMarkerEnabled: true,
			ODataEchoEntity: EchoEntity,
			ODataWriteRequiredMarker: PatchMarker,
			ODataOversizedBytes: oversizedBytes);

	private static Task RunAgainstEchoStubAsync(
		Func<Support.Mcp.McpServerSession, string, CancellationToken, Task> act) =>
		StubEnvironmentStand.RunAsync(
			"clio-odata-file-mode-e2e",
			EchoStubConfiguration(),
			(session, environmentName, _, cancellationToken) => act(session, environmentName, cancellationToken));
}
