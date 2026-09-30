using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// GH-1699 / ENG-101507: a write whose lookup Id is missing from the referenced table reaches the MCP caller
/// with clio's foreign-key hint, measured against a live Creatio stand.
/// </summary>
/// <remarks>
/// The unit tests in <c>clio.tests</c> pin the PostgreSQL 23503 and SQL Server 547 wordings, but only a real
/// stand shows whether Creatio lets the database report the violation at all or rejects the missing lookup
/// earlier with a wording of its own. This fixture is that measurement: it inserts a Contact whose AccountId
/// is a fresh GUID that no Account row can carry.
/// </remarks>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(ODataCreateTool.ToolName)]
[NonParallelizable]
public sealed class ODataWriteForeignKeyHintE2ETests : McpContractFixtureBase {

	private const string HintPrefix = "From the error payload (validated identifiers only): ";

	[Test]
	[AllureTag(ODataCreateTool.ToolName)]
	[AllureName("odata-create names the foreign key a missing lookup Id violates")]
	[AllureDescription("Inserts a Contact with an AccountId no Account row carries and verifies the row error carries clio's validated FK hint while record-created stays unknown.")]
	[Description("odata-create with a non-existent lookup Id returns clio's FK hint in the row error, and the side-effect fields are unchanged.")]
	public async Task ODataCreate_Should_Name_The_Foreign_Key_A_Missing_Lookup_Id_Violates() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run odata-create against the sandbox stand.");
		}
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrange = Arrange(TimeSpan.FromMinutes(5));
		string missingAccountId = Guid.NewGuid().ToString();
		string contactName = $"clio-e2e-fk-{Guid.NewGuid():N}";
		ODataCreateBatchResponse? response = null;

		try {
			// Act
			CallToolResult callResult = await arrange.Session.CallToolAsync(
				ODataCreateTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["entity"] = "Contact",
						["rows"] = new object[] {
							new Dictionary<string, object?> { ["Name"] = contactName, ["AccountId"] = missingAccountId }
						}
					}
				},
				arrange.CancellationTokenSource.Token);
			response = EntitySchemaStructuredResultParser.Extract<ODataCreateBatchResponse>(callResult);

			// Assert
			callResult.IsError.Should().NotBeTrue(
				because: "a row the database rejects is a structured per-row failure, not a protocol error");
			ODataRowResult row = response.Results.Should().ContainSingle(
				because: "one row was sent and it was attempted").Which;
			row.Success.Should().BeFalse(
				because: $"no Account carries {missingAccountId}, so the insert must be refused");
			TestContext.Out.WriteLine($"Measured odata-create row error: {row.Error}");
			row.Error.Should().Contain(HintPrefix,
				because: "the live FK wording must be one the extractor recognizes, otherwise the caller is back to the bare headline GH-1699 reported");
			row.Error.Should().MatchRegex("a referenced record is missing|has no matching record",
				because: "the hint describes the FK failure without assuming whether the caller or an event handler wrote the rejected row");
			row.Error.Should().Contain("referenced table",
				because: "the hint names the referenced table when known and explicitly reports when it is unknown");
			row.Error.Should().Contain("get-entity-schema-properties",
				because: "the agent needs an actionable metadata inspection step when diagnosing the relationship");
			row.Error.Should().Contain("follow retry-guidance",
				because: "a more descriptive FK error must not override unknown write side effects");
			if (string.Equals(settings.Sandbox.DatabaseProvider, "postgresql", StringComparison.OrdinalIgnoreCase)
				&& !row.Error.Contains("a value in column", StringComparison.Ordinal)) {
				row.Error.Should().Contain("does not identify the foreign-key column or referenced table",
					because: "a PostgreSQL response without DETAIL must disclose the diagnostic limit rather than invent a field");
			}
			row.RecordCreated.Should().BeNull(
				because: "the hint adds the cause only; a server-reported failure keeps its side effect unknown");
			row.RetryGuidance.Should().NotBeNullOrWhiteSpace(
				because: "the unknown side effect still tells the caller to verify before re-sending");
		} finally {
			// A green insert here would be a defect, but it would also leave a Contact behind on the stand.
			string? createdId = response?.Results?.FirstOrDefault(result => result.Success)?.Id;
			if (createdId is not null) {
				await arrange.Session.CallToolAsync(
					ODataDeleteTool.ToolName,
					new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = environmentName,
							["entity"] = "Contact",
							["id"] = createdId,
							["confirm"] = true
						}
					},
					arrange.CancellationTokenSource.Token);
			}
		}
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) =>
		// Destructive fixture: configured-only. The AllowDestructiveMcpTests opt-in authorizes writes to
		// the disposable stand named in settings, never to a fallback environment that merely answers.
		await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(
			settings,
			$"odata-create FK-hint MCP E2E requires the configured sandbox environment '{settings.Sandbox.EnvironmentName}' to be set and reachable.");
}
