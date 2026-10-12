using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.RuntimeVerification;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// ENG-103024: odata-delete explains a refusal with the rows that still reference the record, plans a cascade
/// without deleting, and deletes a batch of ids - measured against the configured sandbox stand.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(ODataDeleteTool.ToolName)]
[NonParallelizable]
public sealed class ODataDeleteExplainE2ETests : McpContractFixtureBase {

	[Test]
	[AllureTag(ODataDeleteTool.ToolName)]
	[AllureName("odata-delete explains a refusal with the rows that reference the record, and dry-run deletes nothing")]
	[AllureDescription("Creates an Account and a Contact referencing it, deletes the Account and checks explanation and referencing, then runs a cascade dry-run and checks the Account still exists.")]
	[Description("A refused delete names the referencing Contact as a linked row in referencing and explanation; a cascade dry-run plans without deleting, and never plans to delete a linked row.")]
	public async Task ODataDelete_Should_Explain_A_Refusal_And_Plan_Without_Deleting() {
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
				new Dictionary<string, object?> { ["Name"] = $"clio-e2e-explain-{Guid.NewGuid():N}" });
			contactId = await CreateRecordAsync(arrange, environmentName, "Contact",
				new Dictionary<string, object?> { ["Name"] = $"clio-e2e-explain-{Guid.NewGuid():N}", ["AccountId"] = accountId });

			// Act
			ODataWriteResponse refused = await DeleteAsync(arrange, new Dictionary<string, object?> {
				["environment-name"] = environmentName, ["entity"] = "Account", ["id"] = accountId, ["confirm"] = true
			});
			ODataWriteResponse plan = await DeleteAsync(arrange, new Dictionary<string, object?> {
				["environment-name"] = environmentName, ["entity"] = "Account", ["id"] = accountId,
				["cascade"] = true, ["dry-run"] = true
			});

			// Assert
			refused.Success.Should().BeFalse(because: "a Contact still references the Account");
			refused.Referencing.Should().Contain(rows => rows.Entity == "Contact" && rows.Column == "Account"
					&& rows.Ids.Contains(contactId) && rows.Kind == ReferencingRows.LinkedKind,
				because: "the referencing Contact is found through the metadata and classified as an optional link");
			refused.Explanation.Should().Contain("Contact.Account",
				because: "the explanation names where the blocking row is");
			plan.Success.Should().BeTrue(because: "a dry run only plans");
			plan.Plan.Should().ContainSingle(because: "one id was asked about")
				.Which.CascadeWouldDelete.Should().NotContain(key => key.Id == contactId,
					because: "cascade never deletes a linked row");
			ODataWriteResponse stillThere = await DeleteAsync(arrange, new Dictionary<string, object?> {
				["environment-name"] = environmentName, ["entity"] = "Account", ["id"] = accountId, ["dry-run"] = true
			});
			stillThere.Plan!.Single().Visible.Should().BeTrue(because: "the dry run deleted nothing");
		} finally {
			await DeleteRecordAsync(arrange, environmentName, "Contact", contactId);
			await DeleteRecordAsync(arrange, environmentName, "Account", accountId);
		}
	}

	[Test]
	[AllureTag(ODataDeleteTool.ToolName)]
	[AllureName("odata-delete deletes a batch of ids and reports each")]
	[AllureDescription("Creates two Contacts and deletes both in one call with ids.")]
	[Description("ids deletes every record and reports one successful result per id.")]
	public async Task ODataDelete_Should_Delete_A_Batch_Of_Ids() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run odata-delete against the sandbox stand.");
		}
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrange = Arrange(TimeSpan.FromMinutes(5));
		string first = await CreateRecordAsync(arrange, environmentName, "Contact",
			new Dictionary<string, object?> { ["Name"] = $"clio-e2e-batch-{Guid.NewGuid():N}" });
		string second = await CreateRecordAsync(arrange, environmentName, "Contact",
			new Dictionary<string, object?> { ["Name"] = $"clio-e2e-batch-{Guid.NewGuid():N}" });

		// Act
		ODataWriteResponse response = await DeleteAsync(arrange, new Dictionary<string, object?> {
			["environment-name"] = environmentName, ["entity"] = "Contact", ["ids"] = new[] { first, second },
			["confirm"] = true
		});

		// Assert
		response.Success.Should().BeTrue(because: $"both contacts are deletable: {response.Error}");
		response.Results.Should().HaveCount(2, because: "every id gets its own outcome");
		response.Results!.Should().OnlyContain(result => result.Success, because: "both were deleted");
	}

	private static async Task<ODataWriteResponse> DeleteAsync(ArrangeContext arrange, Dictionary<string, object?> args) {
		CallToolResult callResult = await arrange.Session.CallToolAsync(ODataDeleteTool.ToolName,
			new Dictionary<string, object?> { ["args"] = args }, arrange.CancellationTokenSource.Token);
		return EntitySchemaStructuredResultParser.Extract<ODataWriteResponse>(callResult);
	}

	private static async Task<string> CreateRecordAsync(ArrangeContext arrange, string environmentName, string entity,
			Dictionary<string, object?> row) {
		CallToolResult callResult = await arrange.Session.CallToolAsync(
			ODataCreateTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName, ["entity"] = entity, ["rows"] = new object[] { row }
				}
			},
			arrange.CancellationTokenSource.Token);
		ODataCreateBatchResponse response = EntitySchemaStructuredResultParser.Extract<ODataCreateBatchResponse>(callResult);
		ODataRowResult created = response.Results.Should().ContainSingle(because: $"one {entity} row was sent").Which;
		created.Success.Should().BeTrue(because: $"the test {entity} must exist first: {created.Error}");
		return created.Id!;
	}

	private static async Task DeleteRecordAsync(ArrangeContext arrange, string environmentName, string entity, string? id) {
		if (id is null) {
			return;
		}
		await DeleteAsync(arrange, new Dictionary<string, object?> {
			["environment-name"] = environmentName, ["entity"] = entity, ["id"] = id, ["confirm"] = true
		});
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) =>
		await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(settings,
			$"odata-delete MCP E2E requires the configured sandbox environment '{settings.Sandbox.EnvironmentName}' to be set and reachable.");
}
