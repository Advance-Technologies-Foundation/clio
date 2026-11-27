using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the read-back after an OData write (ENG-107611 / clio#1995): Creatio can accept a
/// write and store a different value because an entity rule replaced it, and the write tools must say so.
/// A loopback stub plays that rule - every read answers <c>Name</c> with a fixed value - so these tests prove,
/// through the real MCP server, that the read-back is sent after the write and that its divergence reaches the
/// caller in the structured response.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ODataUpdateTool.ToolName)]
[NonParallelizable]
public sealed class ODataWriteVerificationE2ETests {
	private const string Entity = "Contact";
	private const string RecordId = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";
	private const string SentName = "Sent by the caller";

	[Test]
	[AllureTag(ODataUpdateTool.ToolName)]
	[AllureName("odata-update reports a value an entity rule replaced")]
	[AllureDescription("Runs odata-update against a stub that acks the PATCH but stores another Name, and verifies the response keeps success=true and lists Name in diverged with the sent and the stored value.")]
	[Description("odata-update reads the written field back after the PATCH and reports the replaced value in diverged.")]
	public async Task ODataUpdate_Should_Report_A_Replaced_Value() {
		// Arrange
		await using ODataPreWriteStand stand = await ODataPreWriteStand.StartAsync(
			RuntimeDetectionStubServer.ODataWriteDivergence, Entity);

		// Act
		CallToolResult callResult = await stand.UpdateAsync(Entity, RecordId,
			new Dictionary<string, object?> { ["Name"] = SentName });
		ODataWriteResponse response = EntitySchemaStructuredResultParser.Extract<ODataWriteResponse>(callResult);

		// Assert
		response.Success.Should().BeTrue(because: $"the PATCH was accepted: {response.Error}");
		response.Verified.Should().BeTrue(because: "the stub answered the read-back with the record");
		response.Diverged.Should().ContainSingle(because: "the rule replaced Name")
			.Which.Field.Should().Be("Name", because: "the divergence names the field as sent");
		response.Diverged![0].Sent.GetString().Should().Be(SentName, because: "the caller sees what it sent");
		response.Diverged[0].Stored.GetString().Should().Be(RuntimeDetectionStubServer.ODataWriteDivergenceStoredName,
			because: "the caller sees what Creatio stored");
		response.Warnings.Should().Contain(warning => warning.Contains("'Name'", StringComparison.Ordinal),
			because: "the divergence is called out as a warning");

		IReadOnlyList<RecordedStubRequest> requests = await stand.GetRecordedRequestsAsync();
		int patch = requests.ToList().FindIndex(request => request.Method == "PATCH");
		int readBack = requests.ToList().FindLastIndex(request => request.Method == "GET"
			&& request.Url.Contains($"/odata/{Entity}({RecordId})?$select=Name", StringComparison.Ordinal));
		patch.Should().BeGreaterThanOrEqualTo(0, because: "the write was sent");
		readBack.Should().BeGreaterThan(patch, because: "the stored value is read after the write, not before");
	}

	[Test]
	[AllureTag(ODataCreateTool.ToolName)]
	[AllureName("odata-create reports a value an entity rule replaced on a created row")]
	[AllureDescription("Runs odata-create against a stub that creates the row but stores another Name, and verifies the row stays created, carries diverged, and the summary counts it.")]
	[Description("odata-create reads the created row back by Id and reports the replaced value on the row and in the summary.")]
	public async Task ODataCreate_Should_Report_A_Replaced_Value() {
		// Arrange
		await using ODataPreWriteStand stand = await ODataPreWriteStand.StartAsync(
			RuntimeDetectionStubServer.ODataWriteDivergence, Entity);

		// Act
		CallToolResult callResult = await stand.CreateAsync(Entity, new Dictionary<string, object?> { ["Name"] = SentName });
		ODataCreateBatchResponse response = EntitySchemaStructuredResultParser.Extract<ODataCreateBatchResponse>(callResult);

		// Assert
		response.Created.Should().Be(1, because: "the row was created; a replaced value does not undo the insert");
		response.Diverged.Should().Be(1, because: "the created row was stored differently");
		response.Results.Should().ContainSingle(because: "one row was sent")
			.Which.Diverged.Should().ContainSingle(because: "the rule replaced Name")
			.Which.Stored.GetString().Should().Be(RuntimeDetectionStubServer.ODataWriteDivergenceStoredName,
				because: "the caller sees what Creatio stored");

		IReadOnlyList<RecordedStubRequest> requests = await stand.GetRecordedRequestsAsync();
		requests.Should().Contain(request => request.Method == "GET"
				&& request.Url.Contains($"/odata/{Entity}?$filter=", StringComparison.Ordinal),
			because: "the created row is read back by its Id");
	}

	[Test]
	[AllureTag(ODataUpdateTool.ToolName)]
	[AllureName("odata-update with verify=false sends no read-back")]
	[AllureDescription("Runs odata-update with verify=false against the divergence stub and verifies no keyed read follows the PATCH and the response carries no verification members.")]
	[Description("verify=false turns the read-back off: no GET of the record follows the PATCH.")]
	public async Task ODataUpdate_Should_Not_Read_Back_When_Verify_Is_False() {
		// Arrange
		await using ODataPreWriteStand stand = await ODataPreWriteStand.StartAsync(
			RuntimeDetectionStubServer.ODataWriteDivergence, Entity);

		// Act
		CallToolResult callResult = await stand.Session.CallToolAsync(
			ODataUpdateTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = stand.EnvironmentName,
					["entity"] = Entity,
					["id"] = RecordId,
					["data"] = new Dictionary<string, object?> { ["Name"] = SentName },
					["confirm"] = true,
					["verify"] = false
				}
			},
			stand.CancellationToken);
		ODataWriteResponse response = EntitySchemaStructuredResultParser.Extract<ODataWriteResponse>(callResult);

		// Assert
		response.Success.Should().BeTrue(because: $"the PATCH was accepted: {response.Error}");
		response.Verified.Should().BeNull(because: "no read-back ran");
		response.Diverged.Should().BeNull(because: "no read-back ran");
		IReadOnlyList<RecordedStubRequest> requests = await stand.GetRecordedRequestsAsync();
		requests.Should().NotContain(request => request.Method == "GET"
				&& request.Url.Contains($"/odata/{Entity}({RecordId})", StringComparison.Ordinal),
			because: "verify=false must not read the record");
	}
	[Test]
	[AllureTag(ODataCreateTool.ToolName)]
	[AllureName("odata-create with verify=false sends no read-back")]
	[AllureDescription("Runs odata-create with verify=false against the divergence stub and verifies no collection read by Id follows the POST and the response carries no verification members.")]
	[Description("verify=false turns the create read-back off: no $filter read follows the POST.")]
	public async Task ODataCreate_Should_Not_Read_Back_When_Verify_Is_False() {
		// Arrange
		await using ODataPreWriteStand stand = await ODataPreWriteStand.StartAsync(
			RuntimeDetectionStubServer.ODataWriteDivergence, Entity);

		// Act
		CallToolResult callResult = await stand.Session.CallToolAsync(
			ODataCreateTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = stand.EnvironmentName,
					["entity"] = Entity,
					["rows"] = new List<Dictionary<string, object?>> { new() { ["Name"] = SentName } },
					["verify"] = false
				}
			},
			stand.CancellationToken);
		ODataCreateBatchResponse response = EntitySchemaStructuredResultParser.Extract<ODataCreateBatchResponse>(callResult);

		// Assert
		response.Created.Should().Be(1, because: $"the row was created: {response.Error}");
		response.Diverged.Should().BeNull(because: "no read-back ran");
		response.Results.Should().ContainSingle(because: "one row was sent")
			.Which.Verified.Should().BeNull(because: "no read-back ran");
		IReadOnlyList<RecordedStubRequest> requests = await stand.GetRecordedRequestsAsync();
		requests.Should().NotContain(request => request.Method == "GET"
				&& request.Url.Contains($"/odata/{Entity}?$filter=", StringComparison.Ordinal),
			because: "verify=false must not read the created row");
	}

}
