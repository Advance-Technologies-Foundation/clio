using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Exercises the published <c>create-user-task</c> contract through the real offline MCP server.
/// </summary>
/// <remarks>
/// Hermetic, so it carries <c>McpE2E.NoEnvironment</c> rather than <c>McpE2E.ProcessDesigner</c>: the
/// latter is excluded by the TeamCity base filter AND by every exclusion the hosted job has to keep, so a
/// hermetic fixture tagged with it runs in neither lane and guards nothing.
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(CreateUserTaskTool.CreateUserTaskToolName)]
public sealed class CreateUserTaskToolE2ETests : McpContractFixtureBase {

	private const string ToolName = CreateUserTaskTool.CreateUserTaskToolName;

	[Test]
	[Description("Starts the real clio MCP server and verifies create-user-task is discoverable via the get-tool-contract compact index.")]
	[AllureTag(ToolName)]
	[AllureName("create-user-task is discoverable on the lazy surface of the clio MCP server")]
	public async Task CreateUserTask_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext context = Arrange();

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: $"the {ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
	}

	[Test]
	[Description("Verifies the published create-user-task contract describes the code argument in terms of the environment's SchemaNamePrefix rather than a hardcoded Usr prefix (ENG-104560).")]
	[AllureTag(ToolName)]
	[AllureName("create-user-task publishes a SchemaNamePrefix-aware code argument")]
	public async Task CreateUserTask_Should_Publish_SchemaNamePrefix_Aware_Code_Argument() {
		// Arrange
		await using ArrangeContext context = Arrange();

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["tool-names"] = new[] { ToolName },
					["detail"] = "full"
				}
			},
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "get-tool-contract should return a normal MCP tool result envelope for a known tool name");
		ToolContractGetResponse response =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(callResult);
		response.Tools.Should().NotBeNull(
			because: $"a full-detail contract request for {ToolName} must return its definition");
		string codeDescription = response.Tools!
			.Should().ContainSingle(definition => definition.Name == ToolName,
				because: $"the contract request named only {ToolName}")
			.Which.InputSchema.Properties
			.Should().ContainSingle(property => property.Name == "code",
				because: "the user task code is published as a wire property of the args record")
			.Which.Description;
		codeDescription.Should().Contain("SchemaNamePrefix",
			because: "Creatio validates a schema code against the environment's SchemaNamePrefix, so the published contract must not leave an agent to guess the prefix");
		codeDescription.Should().NotContain("Usr",
			because: "naming the out-of-the-box prefix in the contract is what made agents send 'Usr' to environments configured otherwise (ENG-104560)");
		codeDescription.Should().Contain("correct that environment's system setting",
			because: "an unusable SchemaNamePrefix is the environment's defect, and an agent that is not told so answers the refusal by retrying codes that cannot work either");
	}
}
