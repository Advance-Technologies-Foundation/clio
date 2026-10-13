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
/// ENG-103020: odata-create accepts payload/values/body for rows and odata-update accepts values/payload/body
/// for data, reporting the accepted synonym in warnings, measured against a live Creatio stand. The throwaway
/// Contact is removed afterwards.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(ODataCreateTool.ToolName)]
[NonParallelizable]
public sealed class ODataWriteArgumentAliasesE2ETests : McpContractFixtureBase {

	[Test]
	[AllureTag(ODataCreateTool.ToolName)]
	[AllureTag(ODataUpdateTool.ToolName)]
	[AllureName("odata-create and odata-update accept payload aliases with warnings")]
	[AllureDescription("Creates a Contact with the payload alias, renames it with the values alias and verifies the records changed and the warnings name the canonical arguments; the Contact is deleted.")]
	[Description("odata-create payload (single object) and odata-update values execute against the stand, each returning a warning naming the canonical argument.")]
	public async Task ODataWrite_Should_Accept_Payload_Aliases_And_Warn() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run odata-create/update against the sandbox stand.");
		}
		string environmentName = await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(settings,
			$"OData write alias MCP E2E requires the configured sandbox environment '{settings.Sandbox.EnvironmentName}' to be set and reachable.");
		await using var arrange = Arrange(TimeSpan.FromMinutes(5));
		string originalName = $"clio-e2e-alias-{Guid.NewGuid():N}";
		string renamed = originalName + "-renamed";
		string? contactId = null;

		try {
			// Act
			CallToolResult createResult = await arrange.Session.CallToolAsync(
				ODataCreateTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["entity"] = "Contact",
						["payload"] = new Dictionary<string, object?> { ["Name"] = originalName }
					}
				},
				arrange.CancellationTokenSource.Token);
			ODataCreateBatchResponse created = EntitySchemaStructuredResultParser.Extract<ODataCreateBatchResponse>(createResult);
			contactId = created.Results.FirstOrDefault(row => row.Success)?.Id;

			// Assert
			createResult.IsError.Should().NotBeTrue(because: "the alias is accepted, not a protocol error");
			created.Results.Should().ContainSingle(row => row.Success,
				because: $"a single payload object becomes one created row: {created.Error}");
			created.Warnings.Should().ContainSingle(warning => warning.Contains("'payload'") && warning.Contains("'rows'"),
				because: "the caller learns the canonical argument name");
			contactId.Should().NotBeNullOrWhiteSpace(because: "the created row carries the new record's Id");

			// Act
			CallToolResult updateResult = await arrange.Session.CallToolAsync(
				ODataUpdateTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["entity"] = "Contact",
						["id"] = contactId,
						["values"] = new Dictionary<string, object?> { ["Name"] = renamed },
						["confirm"] = true
					}
				},
				arrange.CancellationTokenSource.Token);
			ODataWriteResponse updated = EntitySchemaStructuredResultParser.Extract<ODataWriteResponse>(updateResult);

			// Assert
			updated.Success.Should().BeTrue(because: $"the values alias is accepted as data: {updated.Error}");
			updated.Warnings.Should().ContainSingle(warning => warning.Contains("'values'") && warning.Contains("'data'"),
				because: "the caller learns the canonical argument name");

			// Act
			CallToolResult readResult = await arrange.Session.CallToolAsync(
				ODataReadTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["entity"] = "Contact",
						["select"] = new[] { "Id", "Name" },
						["filters"] = new Dictionary<string, object?> {
							["all"] = new object[] {
								new Dictionary<string, object?> { ["field"] = "Id", ["value"] = contactId }
							}
						},
						["top"] = 1
					}
				},
				arrange.CancellationTokenSource.Token);

			// Assert
			readResult.IsError.Should().NotBeTrue(because: "the read-back is a normal structured call");
			string text = string.Concat(readResult.Content.OfType<TextContentBlock>().Select(block => block.Text));
			text.Should().Contain(renamed, because: "the values alias must have written the new Name, not just warned");
		} finally {
			if (contactId is not null) {
				await arrange.Session.CallToolAsync(
					ODataDeleteTool.ToolName,
					new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = environmentName,
							["entity"] = "Contact",
							["id"] = contactId,
							["confirm"] = true
						}
					},
					arrange.CancellationTokenSource.Token);
			}
		}
	}
}
