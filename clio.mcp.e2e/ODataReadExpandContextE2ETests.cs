using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Pins the <c>@odata.context</c> shape a real Creatio service returned for an expanded read -
/// <c>Contact?$select=Id,Name,AccountId&amp;$expand=Account&amp;$top=1</c> answering with the fragment
/// <c>#Contact(Id,Name,AccountId,Account())</c> - through the REAL stdio MCP server, not only through
/// the in-process parser unit tests. <c>odata-read</c> is a long-tail tool, so a call to it travels
/// <c>McpDurableCallToolHandler</c> -> <c>IClioRunExecutor.InvokeResolvedAsync</c> -> the same
/// <c>DispatchAsync</c> that serves a nested <c>clio-run</c> call.
/// <para>
/// The unit tests over <c>ODataReadTool</c>'s context parser cannot see the argument binding,
/// serialization and dispatch the live call actually travels through, so a change on that path could
/// turn an accepted body into a failure with every unit test still green.
/// </para>
/// </summary>
[TestFixture]
//A loopback stub answers every request in this fixture - no Creatio sandbox is involved.
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ODataReadTool.ToolName)]
[NonParallelizable]
public sealed class ODataReadExpandContextE2ETests {
	private const string Entity = "Contact";
	private const string NavigationProperty = "Account";

	[Test]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read accepts the live expanded context fragment over stdio")]
	[AllureDescription("Sends select plus expand through the real stdio MCP server against a stub answering with the live-proven #Contact(Id,Name,AccountId,Account()) context, and verifies the read is accepted as a top-level read of the requested set with the expanded record in the payload.")]
	[Description("odata-read with expand is answered with the live-proven parenthesized context fragment and reports success with the expanded record, proving the fragment parser is reached and satisfied over the real MCP dispatch path.")]
	public async Task ODataRead_Should_Accept_The_Expanded_Context_Shape_Through_TheLongTailDispatch() {
		// Arrange
		await using ODataPreWriteStand stand = await ODataPreWriteStand.StartAsync(
			RuntimeDetectionStubServer.ODataExpandRead, Entity);

		// Act
		CallToolResult callResult = await stand.Session.CallToolAsync(
			ODataReadTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = stand.EnvironmentName,
					["entity"] = Entity,
					["select"] = new[] { "Id", "Name", "AccountId" },
					["expand"] = new[] { NavigationProperty },
					["top"] = 1
				}
			},
			stand.CancellationToken);
		ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a bindable odata-read payload must return a structured tool response, not a protocol error");
		response.Success.Should().BeTrue(
			because: "the parenthesized projection fragment is what a real Creatio service answers to an expanded "
				+ "read, so it must be accepted as a top-level read of the requested set");
		response.Count.Should().Be(1,
			because: "the stub answered the one requested record and the read must report it as returned data");
		response.Value.Should().NotBeNull(
			because: "an accepted expanded read must carry the records, not only a success flag");
		response.Value!.Value.EnumerateArray().Should().NotBeEmpty(
			because: "reading the first record below would otherwise throw an unhelpful sequence error "
				+ "instead of reporting that the read returned nothing");
		response.Value.Value.EnumerateArray().First().TryGetProperty(NavigationProperty, out JsonElement expanded)
			.Should().BeTrue(
				because: "the expanded navigation property is the payload the caller asked for by sending expand");
		expanded.ValueKind.Should().Be(JsonValueKind.Object,
			because: "an expanded single-valued navigation property arrives as the nested entity object");

		IReadOnlyList<RecordedStubRequest> requests = await stand.GetRecordedRequestsAsync();
		requests.Should().Contain(request => request.Url.Contains("$expand=Account", StringComparison.Ordinal),
			because: "asserting the answer alone would still pass if expand were dropped before the request left "
				+ "the process, so the recorded query is what proves it was sent");
	}
}
