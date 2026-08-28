using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Creatio;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the <c>odata-update</c> pre-write field validation added for issue #1212, driven
/// through the real MCP server process against a stub Creatio that records every request it serves.
/// <para>
/// These are the failure envelopes the unit tests in <c>clio.tests</c> cannot prove, because only the real
/// server can show WHICH URL the validation requests and that no PATCH leaves the process:
/// </para>
/// <list type="bullet">
/// <item>an unknown field is rejected before the write, and the stub sees no PATCH;</item>
/// <item>the CSDL document is fetched from the SERVICE-ROOT <c>odata/$metadata</c>, not the
/// non-existent per-entity <c>odata/{entity}/$metadata</c> route;</item>
/// <item>a non-JSON pre-write body yields an "unverified" refusal that leaks no response body.</item>
/// </list>
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(ODataUpdateTool.ToolName)]
[NonParallelizable]
public sealed class ODataUpdatePreWriteValidationE2ETests {
	private const string Entity = "Contact";
	private const string RecordId = "00000000-0000-0000-0000-000000000001";
	private const string UnknownField = "labColor";

	[Test]
	[AllureTag(ODataUpdateTool.ToolName)]
	[AllureName("odata-update rejects an unknown data field before writing")]
	[AllureDescription("Registers an environment against a stub whose service-root $metadata declares only Id and Name, calls odata-update with an extra field, and verifies the tool refuses before the PATCH and the stub records no PATCH at all.")]
	[Description("odata-update names the data field missing from the entity's CSDL type, says nothing was written, and issues no PATCH.")]
	public async Task ODataUpdate_Should_Reject_Unknown_Field_Before_Writing() {
		// Arrange
		await using ODataPreWriteStand stand = await ODataPreWriteStand.StartAsync(
			RuntimeDetectionStubServer.ODataPreWriteMetadata, Entity);

		// Act
		CallToolResult callResult = await stand.UpdateAsync(Entity, RecordId, new Dictionary<string, object?> {
			["Name"] = "e2e",
			[UnknownField] = "#fff"
		});

		// Assert
		string surfacedText = SerializeSurfacedText(callResult);
		surfacedText.Should().Contain(UnknownField,
			because: "the caller can only fix the payload if the refusal names the offending field");
		surfacedText.Should().Contain("do not exist on the OData type",
			because: "the refusal must say the field is absent from the entity's type, not merely that the call failed");
		surfacedText.Should().Contain("nothing was written",
			because: "the caller must learn no partial write happened, which is the whole point of issue #1212");

		IReadOnlyList<RecordedStubRequest> requests = await stand.GetRecordedRequestsAsync();
		requests.Should().NotContain(request => request.Method == "PATCH",
			because: "a payload rejected before the write must never reach the service - the stub proves the "
				+ "absence of the side effect, which no unit test can");
	}

	[Test]
	[AllureTag(ODataUpdateTool.ToolName)]
	[AllureName("odata-update fetches CSDL from the service-root $metadata")]
	[AllureDescription("Verifies the pre-write validation GETs serviceRoot/odata/$metadata - the only $metadata resource OData v4 and ASP.NET Web API OData define - and never the per-entity route, then lets a payload of known fields through to the PATCH.")]
	[Description("The pre-write CSDL fetch addresses the service-root odata/$metadata, not odata/{entity}/$metadata, and a payload whose fields all exist is written.")]
	public async Task ODataUpdate_Should_Fetch_Metadata_From_Service_Root() {
		// Arrange
		await using ODataPreWriteStand stand = await ODataPreWriteStand.StartAsync(
			RuntimeDetectionStubServer.ODataPreWriteMetadata, Entity);

		// Act
		CallToolResult callResult = await stand.UpdateAsync(Entity, RecordId, new Dictionary<string, object?> {
			["Name"] = "e2e"
		});

		// Assert
		IReadOnlyList<RecordedStubRequest> requests = await stand.GetRecordedRequestsAsync();
		requests.Should().Contain(
			request => request.Method == "GET" && request.Url.EndsWith("/odata/$metadata", StringComparison.Ordinal),
			because: "in OData v4 $metadata is a service-root resource and ASP.NET Web API OData maps only "
				+ "~/$metadata, so this is the only route that can resolve on a live environment");
		requests.Should().NotContain(
			request => request.Url.Contains($"/odata/{Entity}/$metadata", StringComparison.Ordinal),
			because: "the per-entity $metadata route is not a defined OData resource path and would 404 into a "
				+ "routing-error body, leaving the CSDL validator silently dead in production");

		string surfacedText = SerializeSurfacedText(callResult);
		surfacedText.Should().NotContain("do not exist on the OData type",
			because: "Name is declared by the stub's CSDL, so validation must pass it through");
		requests.Should().Contain(
			request => request.Method == "PATCH" && request.Url.Contains($"/odata/{Entity}(", StringComparison.Ordinal),
			because: "a payload the CSDL confirms must actually be written to the addressed record");
	}

	[Test]
	[AllureTag(ODataUpdateTool.ToolName)]
	[AllureName("odata-update refuses to write on an unverified pre-write response")]
	[AllureDescription("Points both pre-write reads at a non-JSON IIS-style body carrying a credential URI, then verifies odata-update reports the payload as unverified, performs no PATCH, keeps the body prefix as diagnostics, and scrubs the credential and internal host.")]
	[Description("When $metadata and the $select probe both answer with a non-JSON body, odata-update reports the payload as unverified, writes nothing, surfaces the body prefix as diagnostics, and redacts the credential URI it carries.")]
	public async Task ODataUpdate_Should_Refuse_When_PreWrite_Response_Is_Unverified() {
		// Arrange
		await using ODataPreWriteStand stand = await ODataPreWriteStand.StartAsync(
			RuntimeDetectionStubServer.ODataPreWriteUnverified, Entity);

		// Act
		CallToolResult callResult = await stand.UpdateAsync(Entity, RecordId, new Dictionary<string, object?> {
			["Name"] = "e2e"
		});

		// Assert
		string surfacedText = SerializeSurfacedText(callResult);
		surfacedText.Should().Contain("could not be verified",
			because: "an outcome the tool could neither confirm nor refute must read as unverified, never as success");
		surfacedText.Should().Contain("No write was performed",
			because: "the caller must be told the record is untouched so it can safely retry");
		surfacedText.Should().Contain(RuntimeDetectionStubServer.ODataPreWriteUnverifiedBodyMarker,
			because: "a prefix of the unrecognized body is deliberately surfaced as diagnostics - the same "
				+ "contract as ODataKeyedWrite.ValidateWriteResponse - so the agent can see what answered");
		surfacedText.Should().NotContain(RuntimeDetectionStubServer.ODataPreWriteUnverifiedSecret,
			because: "a non-JSON pre-write body is the IIS/proxy or SSO page - a realistic carrier of "
				+ "credentials and redirect tokens - so the redactor must scrub them from the surfaced prefix");
		surfacedText.Should().NotContain(RuntimeDetectionStubServer.ODataPreWriteUnverifiedHost,
			because: "internal hostnames in that same body must not reach the MCP transcript either");
		surfacedText.Should().Contain("[redacted-uri]",
			because: "the scrubbed URI must leave a visible placeholder so the agent knows a URI was elided");
		surfacedText.Should().NotContain("is an invalid start of a value",
			because: "the raw System.Text.Json parser message must never cross the MCP boundary");

		IReadOnlyList<RecordedStubRequest> requests = await stand.GetRecordedRequestsAsync();
		requests.Should().NotContain(request => request.Method == "PATCH",
			because: "the stub acks a PATCH, so only the absence of the request itself proves the tool refused "
				+ "to write rather than writing and reporting a failure");
	}

	/// <summary>
	/// Flattens everything the tool result carries (content blocks plus structured content) into one string,
	/// so the assertions cover whatever channel the failure was surfaced on.
	/// </summary>
	/// <param name="callResult">Tool result returned by the MCP server.</param>
	/// <returns>Serialized text of the whole result payload.</returns>
	private static string SerializeSurfacedText(CallToolResult callResult) =>
		JsonSerializer.Serialize(callResult.Content) + JsonSerializer.Serialize(callResult.StructuredContent);
}
