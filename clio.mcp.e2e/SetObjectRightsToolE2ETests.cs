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

	protected override string UnknownArgumentName => "revok";

	protected override Dictionary<string, object?> InvalidEnvironmentArgs(string environmentName) => new() {
		["environment-name"] = environmentName,
		["entity-schema-name"] = "Contact",
		["grantee"] = "720b771c-e7a7-4f31-9cfb-52cd21c3739f",
		["operations"] = "read"
	};

	[Test]
	[AllureTag(SetObjectRightsTool.ToolName)]
	[AllureName("set-object-rights binds the disable-operation-permissions opt-in")]
	[AllureDescription("The disable-operation-permissions argument binds through the real MCP server and still fails on the missing environment, not as an unknown argument.")]
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

	[Test]
	[AllureTag(SetObjectRightsTool.ToolName)]
	[AllureName("set-object-rights refuses confirm without a confirmation-code")]
	[AllureDescription("confirm=true without the confirmation-code from a preview is refused through the real MCP server before the environment is resolved, so no write can skip the preview.")]
	[Description("Refuses confirm=true without a confirmation-code before the environment is resolved: the only way to write is through a preview.")]
	public async Task Tool_Should_Refuse_Confirm_Without_ConfirmationCode() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-{ToolName}-confirm-env-{Guid.NewGuid():N}";
		Dictionary<string, object?> args = InvalidEnvironmentArgs(invalidEnvironmentName);
		args["confirm"] = true;

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["args"] = args },
			arrangeContext.CancellationTokenSource.Token);
		ObjectRightsToolResponse response = EntitySchemaStructuredResultParser.Extract<ObjectRightsToolResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "the refusal is a structured tool result, not a protocol error");
		response.Success.Should().BeFalse(because: "a confirmed write must carry the code from a preview");
		response.Error.Should().Contain("confirmation-code", because: "the refusal names what is missing");
		response.Error.Should().NotContain(invalidEnvironmentName,
			because: "the refusal happens before the environment is resolved");
	}

	[Test]
	[AllureTag(SetObjectRightsTool.ToolName)]
	[AllureName("set-object-rights binds confirm, confirmation-code and allow-security-object")]
	[AllureDescription("The two-step and security opt-in arguments bind through the real MCP server and the call still fails on the missing environment, not as an unknown argument.")]
	[Description("Binds confirm, confirmation-code and allow-security-object through the real MCP server.")]
	public async Task Tool_Should_Bind_Confirmation_And_SecurityOptIn() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-{ToolName}-code-env-{Guid.NewGuid():N}";
		Dictionary<string, object?> args = InvalidEnvironmentArgs(invalidEnvironmentName);
		args["confirm"] = true;
		args["confirmation-code"] = "0123456789abcdef";
		args["allow-security-object"] = true;

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["args"] = args },
			arrangeContext.CancellationTokenSource.Token);
		ObjectRightsToolResponse response = EntitySchemaStructuredResultParser.Extract<ObjectRightsToolResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "these arguments are part of the tool contract");
		response.Error.Should().Contain(invalidEnvironmentName,
			because: "the call must fail on the missing environment rather than on an unknown argument");
	}
}
