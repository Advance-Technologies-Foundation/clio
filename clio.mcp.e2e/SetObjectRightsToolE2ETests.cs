using System.Collections.Generic;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>End-to-end tests for the destructive set-object-rights MCP tool (see the shared base).</summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(SetObjectRightsTool.ToolName)]
[NonParallelizable]
public sealed class SetObjectRightsToolE2ETests : ObjectRightsToolE2ETestsBase {

	protected override string ToolName => SetObjectRightsTool.ToolName;

	protected override bool ExpectedDestructive => true;

	protected override Dictionary<string, object?> InvalidEnvironmentArgs(string environmentName) => new() {
		["environment-name"] = environmentName,
		["entity-schema-name"] = "Contact",
		["grantee"] = "720b771c-e7a7-4f31-9cfb-52cd21c3739f",
		["operations"] = "read"
	};

	[Test]
	[Description("Binds the disable-operation-permissions opt-in through the real MCP server, so an agent can request the last-row revoke that a revoke never performs implicitly.")]
	public async Task Tool_Should_Bind_DisableOperationPermissions_OptIn() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-{ToolName}-optin-env-{Guid.NewGuid():N}";
		Dictionary<string, object?> args = InvalidEnvironmentArgs(invalidEnvironmentName);
		args["revoke"] = true;
		args["disable-operation-permissions"] = true;

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["args"] = args },
			arrangeContext.CancellationTokenSource.Token);
		ObjectRightsToolResponse response = EntitySchemaStructuredResultParser.Extract<ObjectRightsToolResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "disable-operation-permissions is part of the tool contract and must bind like any other argument");
		response.Error.Should().Contain(invalidEnvironmentName,
			because: "the opt-in must still fail on the missing environment rather than being rejected as an unknown argument");
	}
}
