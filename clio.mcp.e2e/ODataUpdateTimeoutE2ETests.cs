using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for issue #1851: a PATCH that gets no answer within odata-update's client-side timeout
/// used to fail with creatio.client's "One or more errors occurred. (A task was canceled.)". Only the real server
/// process against a stub that holds the PATCH open proves the production exception shape reaches the new
/// message: a unit test can only throw the shape it assumes.
/// </summary>
[TestFixture]
//A loopback stub answers every request, so no Creatio sandbox is involved.
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ODataUpdateTool.ToolName)]
[NonParallelizable]
public sealed class ODataUpdateTimeoutE2ETests {
	private const string Entity = "Contact";
	private const string RecordId = "00000000-0000-0000-0000-000000000001";

	[Test]
	[AllureTag(ODataUpdateTool.ToolName)]
	[AllureName("odata-update names a PATCH timeout and advises one re-send")]
	[AllureDescription("Registers an environment against a stub that never answers the PATCH, waits for the 30 s client-side timeout, and verifies odata-update reports the timeout, keeps side-effect unknown and advises a re-send.")]
	[Description("odata-update against a PATCH that never gets an answer reports the client-side timeout instead of 'A task was canceled', with side-effect unknown and re-send advice.")]
	public async Task ODataUpdate_Should_Report_Timeout_When_Patch_Gets_No_Answer() {
		// Arrange
		await using ODataPreWriteStand stand = await ODataPreWriteStand.StartAsync(
			RuntimeDetectionStubServer.ODataPreWriteStallPatch, Entity);

		// Act
		CallToolResult callResult = await stand.UpdateAsync(Entity, RecordId, new Dictionary<string, object?> {
			["Name"] = "e2e"
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a timed-out write is a structured tool result, not a protocol-level failure");
		ODataWriteResponse response = EntitySchemaStructuredResultParser.Extract<ODataWriteResponse>(callResult);
		response.Success.Should().BeFalse(because: "a PATCH without an answer is not a confirmed update");
		response.Error.Should().Be(ODataUpdateTool.PatchTimedOutError,
			because: "the real creatio.client cancellation must be recognized as the client-side timeout");
		response.Diagnostic.Should().NotBeNull(because: "write failures carry the diagnostic context");
		response.Diagnostic!.WriteAttempted.Should().BeTrue(because: "the PATCH was sent before the timeout");
		response.Diagnostic.SideEffect.Should().Be("unknown",
			because: "the server can still commit a PATCH the client stopped waiting for");
		response.Diagnostic.RetryAdvice.Should().Be(ODataUpdateTool.PatchTimedOutRetryAdvice,
			because: "a single re-send of the same literal values is the next step, with a read first when processes react to the record");

		IReadOnlyList<RecordedStubRequest> requests = await stand.Stub.GetRecordedRequestsAsync(stand.CancellationToken);
		requests.Should().Contain(request => request.Method == "PATCH",
			because: "the stub must prove the PATCH left the process, which is why the outcome is unknown");
	}
}
