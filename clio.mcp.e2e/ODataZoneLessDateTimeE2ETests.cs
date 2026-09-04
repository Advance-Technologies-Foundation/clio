using System.Text.Encodings.Web;
using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Creatio;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the zone-less date-time guard added for GitHub issue #1369, driven through the
/// real MCP server process against a stub Creatio that records every request it serves.
/// <para>
/// Only the real server can prove the part that matters: that NO write request leaves the process when the
/// payload carries a date-time literal without a UTC designator or offset - the shape the platform either
/// rejects opaquely or silently stores as DateTime.MinValue while answering success.
/// </para>
/// </summary>
[TestFixture]
//A loopback stub answers every request in this fixture - no Creatio sandbox is involved.
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ODataUpdateTool.ToolName)]
[NonParallelizable]
public sealed class ODataZoneLessDateTimeE2ETests {
	private const string Entity = "Contact";
	private const string RecordId = "00000000-0000-0000-0000-000000000001";
	private const string ZoneLessValue = "2024-01-01T04:00:00.000";
	private const string ZonedValue = "2024-01-01T04:00:00.000Z";

	[Test]
	[AllureTag(ODataUpdateTool.ToolName)]
	[AllureName("odata-update refuses a zone-less date-time and issues no PATCH")]
	[AllureDescription("Calls odata-update with a date-time value carrying no UTC designator or offset against a stub whose CSDL declares the column as Edm.DateTimeOffset, and verifies the tool refuses with a message naming the field and the accepted forms while the stub records no PATCH.")]
	[Description("odata-update rejects a zone-less date-time before the write, names the field and both accepted forms, and sends no PATCH.")]
	public async Task ODataUpdate_Should_Refuse_A_ZoneLess_DateTime_Without_Writing() {
		// Arrange
		await using ODataPreWriteStand stand = await ODataPreWriteStand.StartAsync(
			RuntimeDetectionStubServer.ODataPreWriteMetadata, Entity);

		// Act
		CallToolResult callResult = await stand.UpdateAsync(Entity, RecordId, new Dictionary<string, object?> {
			["DueDate"] = ZoneLessValue
		});

		// Assert
		string surfacedText = SerializeSurfacedText(callResult);
		surfacedText.Should().Contain("DueDate",
			because: "the caller can only fix the payload when the refusal names the offending field");
		surfacedText.Should().Contain("time-zone offset",
			because: "the refusal must say WHY the literal cannot be sent, not merely that the call failed");
		surfacedText.Should().Contain("Nothing was written",
			because: "the caller must learn the record is untouched so it can safely re-send a corrected value");

		IReadOnlyList<RecordedStubRequest> requests = await stand.GetRecordedRequestsAsync();
		requests.Should().NotContain(request => request.Method == "PATCH",
			because: "the stub acks a PATCH, so only the absence of the request proves clio refused instead of "
				+ "letting the platform silently store DateTime.MinValue - the regression of issue #1369");
	}

	[Test]
	[AllureTag(ODataUpdateTool.ToolName)]
	[AllureName("odata-update writes a date-time that carries a UTC designator")]
	[AllureDescription("Calls odata-update with the same instant expressed with a trailing Z and verifies the PATCH reaches the stub unchanged, so the guard rejects only the ambiguous form.")]
	[Description("A zoned date-time passes the guard and reaches the service as a PATCH.")]
	public async Task ODataUpdate_Should_Write_A_Zoned_DateTime() {
		// Arrange
		await using ODataPreWriteStand stand = await ODataPreWriteStand.StartAsync(
			RuntimeDetectionStubServer.ODataPreWriteMetadata, Entity);

		// Act
		CallToolResult callResult = await stand.UpdateAsync(Entity, RecordId, new Dictionary<string, object?> {
			["DueDate"] = ZonedValue
		});

		// Assert
		SerializeSurfacedText(callResult).Should().Contain("success\\\":true",
			because: "an explicitly zoned instant is exactly what the OData endpoint accepts");

		IReadOnlyList<RecordedStubRequest> requests = await stand.GetRecordedRequestsAsync();
		requests.Should().Contain(request => request.Method == "PATCH",
			because: "the guard must not turn the documented workaround into a refusal");
	}

	[Test]
	[AllureTag(ODataCreateTool.ToolName)]
	[AllureName("odata-create refuses a zone-less date-time row and issues no POST")]
	[AllureDescription("Calls odata-create with a row whose date-time value carries no zone and verifies the row fails locally, is reported as definitely not created, and that the stub records no POST.")]
	[Description("odata-create fails a zone-less date-time row before any POST and reports record-created false.")]
	public async Task ODataCreate_Should_Refuse_A_ZoneLess_DateTime_Row_Without_Posting() {
		// Arrange
		await using ODataPreWriteStand stand = await ODataPreWriteStand.StartAsync(
			RuntimeDetectionStubServer.ODataPreWriteMetadata, Entity);

		// Act
		CallToolResult callResult = await stand.CreateAsync(Entity, new Dictionary<string, object?> {
			["Name"] = "e2e",
			["DueDate"] = ZoneLessValue
		});

		// Assert
		string surfacedText = SerializeSurfacedText(callResult);
		surfacedText.Should().Contain("DueDate",
			because: "the per-row error must name the field the caller has to correct");
		surfacedText.Should().Contain("record-created\\\":false",
			because: "a row rejected locally has a KNOWN side effect - the caller may fix and re-send it safely");

		IReadOnlyList<RecordedStubRequest> requests = await stand.GetRecordedRequestsAsync();
		//Scoped to the entity-set URL: authentication itself POSTs, so an unqualified "no POST" would
		//never hold on a stand the server had to log into.
		requests.Should().NotContain(
			request => request.Method == "POST" && request.Url.EndsWith("/odata/" + Entity, StringComparison.Ordinal),
			because: "the stub acks a create, so only the absence of the collection POST proves the row never left clio");
	}

	[Test]
	[AllureTag(ODataCreateTool.ToolName)]
	[AllureName("odata-create inserts a date-shaped value on a text column")]
	[AllureDescription("Calls odata-create with a date-shaped string on the Edm.String column declared by the stub's CSDL and verifies the row is inserted, proving the guard keys on the declared Edm type rather than the literal's shape.")]
	[Description("A date-shaped string bound to an Edm.String column is inserted, so the guard does not make text columns unwritable.")]
	public async Task ODataCreate_Should_Insert_A_Date_Shaped_Text_Value() {
		// Arrange
		await using ODataPreWriteStand stand = await ODataPreWriteStand.StartAsync(
			RuntimeDetectionStubServer.ODataPreWriteMetadata, Entity);

		// Act
		CallToolResult callResult = await stand.CreateAsync(Entity, new Dictionary<string, object?> {
			["Name"] = ZoneLessValue
		});

		// Assert
		SerializeSurfacedText(callResult).Should().Contain("created\\\":1",
			because: "the stub declares Name as Edm.String, so a date-shaped text value is a legitimate insert");

		IReadOnlyList<RecordedStubRequest> requests = await stand.GetRecordedRequestsAsync();
		requests.Should().Contain(
			request => request.Method == "POST" && request.Url.EndsWith("/odata/" + Entity, StringComparison.Ordinal),
			because: "a type-gated guard must leave text columns writable, which only the collection POST proves");
	}

	/// <summary>
	/// Flattens everything the tool result carries (content blocks plus structured content) into one string,
	/// so the assertions cover whatever channel the outcome was surfaced on.
	/// </summary>
	/// <param name="callResult">Tool result returned by the MCP server.</param>
	/// <returns>Serialized text of the whole result payload.</returns>
	private static string SerializeSurfacedText(CallToolResult callResult) =>
		//The relaxed encoder is deliberate: the default one escapes the quotes inside the tool's own JSON
		//payload as \u0022, so an assertion written against the payload's literal text never matches.
		JsonSerializer.Serialize(callResult.Content, RelaxedJson)
		+ JsonSerializer.Serialize(callResult.StructuredContent, RelaxedJson);

	/// <summary>Serializer options that leave quotes unescaped, so assertions can read the tool's payload.</summary>
	private static readonly JsonSerializerOptions RelaxedJson =
		new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}
