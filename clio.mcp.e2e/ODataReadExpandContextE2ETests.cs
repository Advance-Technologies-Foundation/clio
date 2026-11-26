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

	[Test]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read sends a nested $select in expand over stdio")]
	[AllureDescription("Sends expand in the comma-separated string form with a nested select through the real stdio MCP server, and verifies the request URL carries the canonical $expand=Account($select=Name),Owner and the expanded record is returned (ENG-107610).")]
	[Description("A nested $select inside expand travels the real stdio dispatch into the request URL in canonical form, and the string form keeps the nested comma with its entry.")]
	public async Task ODataRead_Should_Send_A_Nested_Select_In_Expand_Through_TheLongTailDispatch() {
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
					["select"] = new[] { "Id", "Name" },
					["expand"] = "Account($select=Id, Name),Owner",
					["top"] = 1
				}
			},
			stand.CancellationToken);
		ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a nested $select in expand is an accepted argument, not a protocol error");
		response.Success.Should().BeTrue(
			because: "odata-read accepts a validated nested $select in expand again (ENG-107610)");
		IReadOnlyList<RecordedStubRequest> requests = await stand.GetRecordedRequestsAsync();
		requests.Select(request => Uri.UnescapeDataString(request.Url)).Should()
			.Contain(url => url.Contains("$expand=Account($select=Id,Name),Owner", StringComparison.Ordinal),
				because: "the nested select must reach the request URL rebuilt from the parsed names, with the "
					+ "string form split only on the top-level comma");
		response.Value!.Value.EnumerateArray().First().GetProperty(NavigationProperty)
			.TryGetProperty("Name", out _).Should().BeTrue(
				because: "the stub answers the nested select with the selected column of the expanded record");
	}

	[Test]
	[AllureTag(ODataReadToFileTool.ToolName)]
	[AllureName("odata-read-to-file sends a nested $select in expand over stdio and writes the projection")]
	[AllureDescription("Sends a nested select in expand to odata-read-to-file through the real stdio MCP server, and verifies the request URL carries the canonical $expand=Account($select=Name) and the file on disk holds the expanded record projected to the selected column (ENG-107610).")]
	[Description("odata-read-to-file sends a nested $select inside expand over the real stdio dispatch and writes the projected expanded record to disk.")]
	public async Task ODataReadToFile_Should_Send_A_Nested_Select_In_Expand_And_Write_The_Projection() {
		// Arrange
		string outputFile = Path.Combine(Path.GetTempPath(), $"odata-read-to-file-expand-e2e-{Guid.NewGuid():N}.json");
		await using ODataPreWriteStand stand = await ODataPreWriteStand.StartAsync(
			RuntimeDetectionStubServer.ODataExpandRead, Entity);
		try {
			// Act
			CallToolResult callResult = await stand.Session.CallToolAsync(
				ODataReadToFileTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = stand.EnvironmentName,
						["entity"] = Entity,
						["select"] = new[] { "Id" },
						["expand"] = new[] { "Account($select=Name)" },
						["top"] = 1,
						["output-file"] = outputFile
					}
				},
				stand.CancellationToken);
			ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);

			// Assert
			callResult.IsError.Should().NotBeTrue(
				because: "a nested $select in expand is an accepted argument of the file tool, not a protocol error");
			response.Success.Should().BeTrue(
				because: $"odata-read-to-file accepts a validated nested $select in expand (ENG-107610): {response.Error}");
			IReadOnlyList<RecordedStubRequest> requests = await stand.GetRecordedRequestsAsync();
			requests.Select(request => Uri.UnescapeDataString(request.Url)).Should()
				.Contain(url => url.Contains("$expand=Account($select=Name)", StringComparison.Ordinal),
					because: "the file tool must send the nested select rebuilt from the parsed names, like odata-read");
			using JsonDocument written = JsonDocument.Parse(File.ReadAllText(response.OutputFile!));
			JsonElement account = written.RootElement.GetProperty("value").EnumerateArray().First()
				.GetProperty(NavigationProperty);
			account.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
				["Id", "Name"],
				because: "the file holds the expanded record projected to the nested select (plus its key)");
		} finally {
			if (File.Exists(outputFile)) {
				File.Delete(outputFile);
			}
		}
	}
}
