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
/// GH-1699 / ENG-101507: a write the database rejects for a foreign key reaches the MCP caller with clio's
/// foreign-key hint, measured against a live Creatio stand for odata-create, odata-update and odata-delete.
/// </summary>
/// <remarks>
/// The unit tests in <c>clio.tests</c> pin the PostgreSQL 23503 and SQL Server 547 wordings, but only a real
/// stand shows whether Creatio lets the database report the violation at all or rejects the write earlier
/// with a wording of its own. This fixture is that measurement: it inserts a Contact whose AccountId is a
/// fresh GUID that no Account row can carry, re-points a test Contact at such a GUID, and deletes a test
/// Account that a test Contact still references.
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

	[Test]
	[AllureTag(ODataUpdateTool.ToolName)]
	[AllureName("odata-update names the foreign key a missing lookup Id violates")]
	[AllureDescription("Creates a test Contact, patches its AccountId to a GUID no Account row carries and verifies the error carries clio's validated FK hint while the side effect stays unknown.")]
	[Description("odata-update that re-points a lookup at a non-existent Id returns clio's FK hint in the error, and the side effect stays unknown.")]
	public async Task ODataUpdate_Should_Name_The_Foreign_Key_A_Missing_Lookup_Id_Violates() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run odata-update against the sandbox stand.");
		}
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrange = Arrange(TimeSpan.FromMinutes(5));
		string missingAccountId = Guid.NewGuid().ToString();
		string? contactId = null;

		try {
			contactId = await CreateRecordAsync(arrange, environmentName, "Contact",
				new Dictionary<string, object?> { ["Name"] = $"clio-e2e-fk-update-{Guid.NewGuid():N}" });

			// Act
			CallToolResult callResult = await arrange.Session.CallToolAsync(
				ODataUpdateTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["entity"] = "Contact",
						["id"] = contactId,
						["data"] = new Dictionary<string, object?> { ["AccountId"] = missingAccountId },
						["confirm"] = true
					}
				},
				arrange.CancellationTokenSource.Token);
			ODataWriteResponse response = EntitySchemaStructuredResultParser.Extract<ODataWriteResponse>(callResult);

			// Assert
			callResult.IsError.Should().NotBeTrue(
				because: "a write the database rejects is a structured failure, not a protocol error");
			TestContext.Out.WriteLine($"Measured odata-update success={response.Success} error: {response.Error}");
			response.Success.Should().BeFalse(
				because: $"no Account carries {missingAccountId}, so the update must be refused");
			response.Error.Should().Contain(HintPrefix,
				because: "the live FK wording must be one the extractor recognizes, otherwise the caller is back to the bare headline GH-1699 reported");
			response.Error.Should().MatchRegex("a referenced record is missing|has no matching record",
				because: "odata-update shares the keyed write path, so it describes the missing referenced record like odata-create");
			response.Error.Should().Contain("follow retry-guidance",
				because: "a more descriptive FK error must not override unknown write side effects");
			response.Diagnostic.Should().NotBeNull(
				because: "a server-reported write failure carries the bounded write diagnostic");
			response.Diagnostic!.SideEffect.Should().Be("unknown",
				because: "the hint adds the cause only; a server-reported failure keeps its side effect unknown");
		} finally {
			await DeleteRecordAsync(arrange, environmentName, "Contact", contactId);
		}
	}

	[Test]
	[AllureTag(ODataDeleteTool.ToolName)]
	[AllureName("odata-delete names the table that still references the record")]
	[AllureDescription("Creates a test Account and a test Contact referencing it, deletes the Account and verifies the error carries clio's validated 'record is still referenced' hint naming Contact.")]
	[Description("odata-delete of an Account a Contact still references returns clio's 'record is still referenced' hint naming the referencing table.")]
	public async Task ODataDelete_Should_Name_The_Table_That_Still_References_The_Record() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run odata-delete against the sandbox stand.");
		}
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrange = Arrange(TimeSpan.FromMinutes(5));
		string? accountId = null;
		string? contactId = null;

		try {
			accountId = await CreateRecordAsync(arrange, environmentName, "Account",
				new Dictionary<string, object?> { ["Name"] = $"clio-e2e-fk-delete-{Guid.NewGuid():N}" });
			contactId = await CreateRecordAsync(arrange, environmentName, "Contact",
				new Dictionary<string, object?> {
					["Name"] = $"clio-e2e-fk-delete-{Guid.NewGuid():N}", ["AccountId"] = accountId
				});

			// Act
			CallToolResult callResult = await arrange.Session.CallToolAsync(
				ODataDeleteTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["entity"] = "Account",
						["id"] = accountId,
						["confirm"] = true
					}
				},
				arrange.CancellationTokenSource.Token);
			ODataWriteResponse response = EntitySchemaStructuredResultParser.Extract<ODataWriteResponse>(callResult);

			// Assert
			callResult.IsError.Should().NotBeTrue(
				because: "a delete the database rejects is a structured failure, not a protocol error");
			TestContext.Out.WriteLine($"Measured odata-delete success={response.Success} error: {response.Error}");
			response.Success.Should().BeFalse(
				because: "a Contact still references the Account, so the database must refuse the delete");
			response.Error.Should().Contain(HintPrefix,
				because: "the live FK wording must be one the extractor recognizes");
			response.Error.Should().Contain("the record is still referenced",
				because: "the delete is blocked by rows that still reference the record, so an unchanged retry fails the same way");
			response.Error.Should().Contain("'Contact'",
				because: "the referencing table is where the rows that block the delete live");
			response.Error.Should().Contain("Do not delete or re-point records without authorization",
				because: "diagnosing a relationship must not authorize destructive changes to dependent records");
			response.Diagnostic!.SideEffect.Should().Be("unknown",
				because: "the hint adds the cause only; a server-reported failure keeps its side effect unknown");
		} finally {
			// Contact first: while it references the Account, the Account delete is exactly what this test
			// expects the database to refuse. A delete of an already-deleted row is a harmless failure.
			await DeleteRecordAsync(arrange, environmentName, "Contact", contactId);
			await DeleteRecordAsync(arrange, environmentName, "Account", accountId);
		}
	}

	private static async Task<string> CreateRecordAsync(ArrangeContext arrange, string environmentName, string entity,
			Dictionary<string, object?> row) {
		CallToolResult callResult = await arrange.Session.CallToolAsync(
			ODataCreateTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["entity"] = entity,
					["rows"] = new object[] { row }
				}
			},
			arrange.CancellationTokenSource.Token);
		ODataCreateBatchResponse response = EntitySchemaStructuredResultParser.Extract<ODataCreateBatchResponse>(callResult);
		ODataRowResult created = response.Results.Should().ContainSingle(
			because: $"one {entity} row was sent to arrange the test").Which;
		created.Success.Should().BeTrue(because: $"the test {entity} must exist before the write under test: {created.Error}");
		return created.Id!;
	}

	private static async Task DeleteRecordAsync(ArrangeContext arrange, string environmentName, string entity,
			string? id) {
		if (id is null) {
			return;
		}
		await arrange.Session.CallToolAsync(
			ODataDeleteTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["entity"] = entity,
					["id"] = id,
					["confirm"] = true
				}
			},
			arrange.CancellationTokenSource.Token);
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) =>
		// Destructive fixture: configured-only. The AllowDestructiveMcpTests opt-in authorizes writes to
		// the disposable stand named in settings, never to a fallback environment that merely answers.
		await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(
			settings,
			$"OData write FK-hint MCP E2E requires the configured sandbox environment '{settings.Sandbox.EnvironmentName}' to be set and reachable.");
}
