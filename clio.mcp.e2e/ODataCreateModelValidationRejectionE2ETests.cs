using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the <c>odata-create</c> model-binding rejection (ENG-104665), driven through the
/// real MCP server process against a stub Creatio that records every request it serves.
/// <para>
/// Creatio answers a row that fails model binding with "The input was not valid." (ASP.NET Core) and names
/// no column. That answer comes before any write, so the row is a confirmed no-write. Only the real server
/// shows that the tool reads <c>$metadata</c> once, AFTER the rejected POST, to name the binary column and
/// point the caller to <c>upload-file</c>.
/// </para>
/// </summary>
[TestFixture]
//A loopback stub answers every request in this fixture - no Creatio sandbox is involved.
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ODataCreateTool.ToolName)]
[NonParallelizable]
public sealed class ODataCreateModelValidationRejectionE2ETests {
	private const string Entity = "KnowledgeBaseFile";

	[Test]
	[AllureTag(ODataCreateTool.ToolName)]
	[AllureName("odata-create reports a binary-column row that Creatio rejects in model binding as not inserted")]
	[AllureDescription("Registers an environment against a stub whose $metadata declares Data as Edm.Stream and whose collection POST answers HTTP 400 'The input was not valid.'. Calls odata-create with a value for Data and verifies the row reports record-created=false, side-effect none, a 'Not inserted' retry-guidance, and an error that names 'Data' and upload-file. The stub must record exactly one POST and one $metadata read, in that order.")]
	[Description("odata-create turns Creatio's model-binding rejection of a binary column into a confirmed no-write that names the column and points to upload-file, and reads $metadata once, after the POST.")]
	public async Task ODataCreate_Should_Report_Binary_Column_Rejection_As_Not_Inserted() {
		// Arrange
		await using ODataPreWriteStand stand = await ODataPreWriteStand.StartAsync(
			RuntimeDetectionStubServer.ODataCreateModelValidationRejected, Entity);

		// Act
		CallToolResult callResult = await stand.CreateAsync(Entity, new Dictionary<string, object?> {
			["Name"] = "e2e.txt",
			["Data"] = "AQID"
		});
		ODataCreateBatchResponse response = EntitySchemaStructuredResultParser.Extract<ODataCreateBatchResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a row Creatio rejects is a structured per-row failure, not a protocol error");
		ODataRowResult row = response.Results.Should().ContainSingle(
			because: "one row was sent and it was attempted").Subject;
		row.Success.Should().BeFalse(
			because: "the stub rejected the row in model binding");
		row.RecordCreated.Should().BeFalse(
			because: "model binding runs before the controller action, so Creatio wrote nothing and the caller may safely re-send a corrected row");
		row.RetryGuidance.Should().Contain("Not inserted",
			because: "the next step must say the row was not inserted, so the agent does not read the entity back for nothing");
		row.Diagnostic.Should().NotBeNull(
			because: "write failures carry additive diagnostic context");
		row.Diagnostic!.SideEffect.Should().Be("none",
			because: "the server's answer proves the write was not applied");
		row.Diagnostic.TransportOutcome.Should().Be("response-received",
			because: "the POST reached the stub and an answer came back");
		row.Error.Should().Contain("'Data'",
			because: "the answer names no column, so the tool must name the binary column it found in $metadata");
		row.Error.Should().Contain("binary",
			because: "the caller must learn why the column cannot be written through OData");
		row.Error.Should().Contain("upload-file",
			because: "the caller must be pointed to the tool that can write a file entity's Data column");

		IReadOnlyList<RecordedStubRequest> requests = await stand.GetRecordedRequestsAsync();
		List<int> postIndexes = IndexesOf(requests, request =>
			request.Method == "POST" && request.Url.EndsWith($"/odata/{Entity}", StringComparison.Ordinal));
		List<int> metadataIndexes = IndexesOf(requests, request =>
			request.Method == "GET" && request.Url.EndsWith("/odata/$metadata", StringComparison.Ordinal));
		postIndexes.Should().ContainSingle(
			because: "the row was posted once and a model-binding rejection is never retried");
		metadataIndexes.Should().ContainSingle(
			because: "the multi-megabyte CSDL is read at most once per batch");
		metadataIndexes[0].Should().BeGreaterThan(postIndexes[0],
			because: "a row without a date-time literal must not pay for the CSDL before the POST; it is read only after the rejection, to name the column");
	}

	private static List<int> IndexesOf(IReadOnlyList<RecordedStubRequest> requests,
		Func<RecordedStubRequest, bool> predicate) =>
		requests.Select((request, index) => (request, index))
			.Where(item => predicate(item.request))
			.Select(item => item.index)
			.ToList();
}
