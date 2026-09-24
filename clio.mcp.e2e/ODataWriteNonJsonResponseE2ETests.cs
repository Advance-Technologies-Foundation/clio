using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for ENG-95971: a session against an environment whose odata endpoint answers
/// with an IIS-style HTML error page (never JSON, never one of the shapes <see cref="Clio.Common.CreatioResponseError"/>
/// recognizes) showed odata-read correctly failing while odata-update reported success:true unconditionally
/// — the write tools ignored the transport response entirely. odata-update, odata-delete, and odata-create
/// must all report the same underlying failure instead of masking it as a successful write.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(ODataUpdateTool.ToolName)]
[NonParallelizable]
public sealed class ODataWriteNonJsonResponseE2ETests {
	private const string StubbedEntity = "labClientStatus";
	private const string RecordId = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";

	[Test]
	[AllureTag(ODataUpdateTool.ToolName)]
	[AllureName("odata-update reports an HTML odata response as a structured failure")]
	[AllureDescription("Registers an environment against a stub whose odata endpoint answers PATCH with an HTML error page, then verifies odata-update returns success:false instead of the unconditional success it used to report.")]
	[Description("odata-update against an odata endpoint that answers with HTML (never JSON) returns success:false naming the transport-layer cause, not a masked success.")]
	public async Task ODataUpdate_Should_Report_NonJson_Response_As_Failure() {
		await RunAgainstNonJsonStubAsync(async (session, environmentName, cancellationToken) => {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				ODataUpdateTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["entity"] = StubbedEntity,
						["id"] = RecordId,
						["data"] = new Dictionary<string, object?> { ["Name"] = "New" },
						["confirm"] = true
					}
				},
				cancellationToken);
			ODataWriteResponse response = EntitySchemaStructuredResultParser.Extract<ODataWriteResponse>(callResult);

			// Assert
			callResult.IsError.Should().NotBeTrue(
				because: "a bindable odata-update payload should return a structured tool response, not a protocol error");
			response.Success.Should().BeFalse(
				because: "an HTML odata response must never be reported as a successful update - the request never reached a real OData controller");
			response.Diagnostic.Should().NotBeNull(because: "write failures carry additive diagnostic context");
			response.Diagnostic!.SideEffect.Should().Be("not-attempted", because: "the metadata refusal prevents PATCH submission");
			response.Diagnostic.TransportOutcome.Should().Be("not-attempted", because: "read-only preflight failed before the write");
			response.Error.Should().Contain("was not JSON",
				because: "the diagnostic must point at the transport layer, not the request's OData/ESQ shape");
			response.Error.Should().Contain("HTTP 404",
				because: "the status the stub's error page states must survive the whole mcp-server round trip, "
					+ "which is the only place the write path can learn it - the transport exposes none");
		});
	}

	[Test]
	[AllureTag(ODataDeleteTool.ToolName)]
	[AllureName("odata-delete reports an HTML odata response as a structured failure")]
	[AllureDescription("Registers an environment against a stub whose odata endpoint answers DELETE with an HTML error page, then verifies odata-delete returns success:false instead of the unconditional success it used to report.")]
	[Description("odata-delete against an odata endpoint that answers with HTML (never JSON) returns success:false naming the transport-layer cause, not a masked success.")]
	public async Task ODataDelete_Should_Report_NonJson_Response_As_Failure() {
		await RunAgainstNonJsonStubAsync(async (session, environmentName, cancellationToken) => {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				ODataDeleteTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["entity"] = StubbedEntity,
						["id"] = RecordId,
						["confirm"] = true
					}
				},
				cancellationToken);
			ODataWriteResponse response = EntitySchemaStructuredResultParser.Extract<ODataWriteResponse>(callResult);

			// Assert
			callResult.IsError.Should().NotBeTrue(
				because: "a bindable odata-delete payload should return a structured tool response, not a protocol error");
			response.Success.Should().BeFalse(
				because: "an HTML odata response must never be reported as a successful delete - the request never reached a real OData controller");
			response.Diagnostic.Should().NotBeNull(because: "write failures carry additive diagnostic context");
			response.Diagnostic!.SideEffect.Should().Be("unknown", because: "an HTML response cannot prove the write had no effect");
			response.Diagnostic.TransportOutcome.Should().Be("response-received", because: "the stub returned a body after the write attempt");
			response.Error.Should().Contain("was not JSON",
				because: "the diagnostic must point at the transport layer, not the request's OData/ESQ shape");
		});
	}

	[Test]
	[AllureTag(ODataCreateTool.ToolName)]
	[AllureName("odata-create reports an HTML odata response as a structured per-row failure")]
	[AllureDescription("Registers an environment against a stub whose odata endpoint answers POST with an HTML error page, then verifies odata-create reports the row as failed with record-created unknown, instead of the unconditional success it used to report for a non-JSON body.")]
	[Description("odata-create against an odata endpoint that answers with HTML (never JSON) reports the row as failed with record-created unknown, not a masked created-record success.")]
	public async Task ODataCreate_Should_Report_NonJson_Response_As_Failure() {
		await RunAgainstNonJsonStubAsync(async (session, environmentName, cancellationToken) => {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				ODataCreateTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["entity"] = StubbedEntity,
						["rows"] = new object[] { new Dictionary<string, object?> { ["Name"] = "Active" } }
					}
				},
				cancellationToken);
			ODataCreateBatchResponse response = EntitySchemaStructuredResultParser.Extract<ODataCreateBatchResponse>(callResult);

			// Assert
			callResult.IsError.Should().NotBeTrue(
				because: "a bindable odata-create payload should return a structured tool response, not a protocol error");
			response.Failed.Should().Be(1,
				because: "the single row targets an endpoint that answered with HTML and must be reported as failed, not created");
			ODataRowResult row = response.Results.Should().ContainSingle(
				because: "the one-row batch should report exactly one per-row result").Subject;
			row.Diagnostic!.SideEffect.Should().Be("unknown", because: "a failed create can have committed before its response failed");
			row.Success.Should().BeFalse(
				because: "an HTML odata response must never be reported as a successful create");
			row.RecordCreated.Should().BeNull(
				because: "the request never reached Creatio intact, so whether a post-insert handler already wrote the row is unknown");
			row.Error.Should().Contain("was not JSON",
				because: "the diagnostic must point at the transport layer, not the request's OData/ESQ shape");
		});
	}

	/// <summary>
	/// Runs <paramref name="act"/> against a session whose registered environment points at a stub answering
	/// every odata request for <see cref="StubbedEntity"/> with an HTML error page.
	/// </summary>
	private static Task RunAgainstNonJsonStubAsync(
		Func<McpServerSession, string, CancellationToken, Task> act) =>
		StubEnvironmentStand.RunAsync(
			"clio-odata-write-nonjson-e2e",
			new RuntimeDetectionStubServerConfiguration(
				NetCoreHealthEnabled: true,
				NetFrameworkHealthEnabled: true,
				NetCoreServiceEnabled: false,
				NetFrameworkServiceEnabled: true,
				NetCoreUiMarkerEnabled: false,
				NetFrameworkUiMarkerEnabled: true,
				ODataNonJsonEntity: StubbedEntity),
			(session, environmentName, _, cancellationToken) => act(session, environmentName, cancellationToken));
}
