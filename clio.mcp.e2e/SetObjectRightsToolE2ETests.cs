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

	private async Task<(CallToolResult Result, ObjectRightsToolResponse Response)> CallAsync(
		Dictionary<string, object?> args) {
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["args"] = args },
			arrangeContext.CancellationTokenSource.Token);
		return (callResult, EntitySchemaStructuredResultParser.Extract<ObjectRightsToolResponse>(callResult));
	}

	[Test]
	[AllureTag(SetObjectRightsTool.ToolName)]
	[AllureName("set-object-rights binds the explicit transition flags")]
	[AllureDescription("enable-operation-permissions on a grant, disable-operation-permissions alone (no grantee, no operations) and preview bind through the real MCP server, and each call still fails on the missing environment, not on an unknown or missing argument.")]
	[Description("Binds every explicit transition flag and the dry-run flag through the real MCP server: enable on a grant, and a disable alone, whose call names neither grantee nor operations.")]
	public async Task Tool_Should_Bind_Transition_And_Preview_Flags() {
		// Arrange
		string grantEnvironment = $"missing-{ToolName}-enable-env-{Guid.NewGuid():N}";
		Dictionary<string, object?> grant = InvalidEnvironmentArgs(grantEnvironment);
		grant["enable-operation-permissions"] = true;
		grant["preview"] = true;
		string disableEnvironment = $"missing-{ToolName}-disable-env-{Guid.NewGuid():N}";
		Dictionary<string, object?> disable = new() {
			["environment-name"] = disableEnvironment,
			["entity-schema-name"] = "Contact",
			["disable-operation-permissions"] = true
		};

		// Act
		(CallToolResult grantResult, ObjectRightsToolResponse grantResponse) = await CallAsync(grant);
		(CallToolResult disableResult, ObjectRightsToolResponse disableResponse) = await CallAsync(disable);

		// Assert
		grantResult.IsError.Should().NotBeTrue(because: "the flags are part of the tool contract and bind like any argument");
		grantResponse.Error.Should().Contain(grantEnvironment,
			because: "the call must fail on the missing environment rather than on an unknown argument");
		disableResult.IsError.Should().NotBeTrue(
			because: "a disable alone is part of the contract: grantee and operations are not required for it");
		disableResponse.Error.Should().Contain(disableEnvironment,
			because: "the call must fail on the missing environment rather than on a missing grantee or operations");
	}

	[TestCase("confirm", true)]
	[TestCase("confirmation-code", "0123456789abcdef")]
	[TestCase("include-connected", true)]
	[TestCase("connected-operations", "read")]
	[TestCase("allow-security-object", true)]
	[AllureTag(SetObjectRightsTool.ToolName)]
	[AllureName("set-object-rights refuses the retired arguments")]
	[AllureDescription("An argument of the retired two-step / fan-out contract is refused through the real MCP server before the environment is resolved, so a caller on the old contract is told so instead of being half-understood.")]
	[Description("Refuses an argument of the retired contract (confirm, confirmation-code, include-connected, connected-operations, allow-security-object) before the environment is resolved.")]
	public async Task Tool_Should_Refuse_Retired_Argument(string argument, object value) {
		// Arrange
		string invalidEnvironmentName = $"missing-{ToolName}-retired-env-{Guid.NewGuid():N}";
		Dictionary<string, object?> args = InvalidEnvironmentArgs(invalidEnvironmentName);
		args[argument] = value;

		// Act
		(CallToolResult callResult, ObjectRightsToolResponse response) = await CallAsync(args);

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "the refusal is a structured tool result, not a protocol error");
		response.Success.Should().BeFalse(because: "a retired argument must be refused, not silently dropped");
		response.Error.Should().Contain(argument, because: "the refusal names the offending key");
		response.Error.Should().NotContain(invalidEnvironmentName,
			because: "the refusal happens before the environment is resolved");
	}
}
