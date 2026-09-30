using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
/// Sandbox-tier proof that the object-rights tools talk to the REAL RightManagementService.svc — the routes, the
/// request field, the administratedObject envelope, the ExtendParent-ordered SysSchema lookup and the grantee
/// lookup — without writing anything. It reads the OOTB Contact object and runs set-object-rights only as a preview, so unlike <see cref="ObjectRightsSandboxE2ETests"/>
/// (which publishes schemas and stays developer-local) it is safe on the shared stand and runs automatically.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(SetObjectRightsTool.ToolName)]
[NonParallelizable]
public sealed class ObjectRightsReadOnlySandboxE2ETests : McpContractFixtureBase {

	private const string AllEmployees = "a29a3ba5-4b0d-de11-9a51-005056c00008";

	[Test]
	[Description("On a real stand, get-object-rights reads Contact's operation permissions, a grantee filter is honoured, and a name that does not exist fails.")]
	[AllureTag(GetObjectRightsTool.ToolName)]
	[AllureName("get-object-rights reads an OOTB object through the real RightManagementService")]
	[AllureDescription("Calls get-object-rights on Contact (all roles, then filtered to All employees) and on a name that does not exist, through the real MCP server against the configured sandbox. Nothing is written.")]
	public async Task GetObjectRights_Should_Read_Ootb_Object_On_A_Real_Stand() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();

		// Act
		ObjectRightsToolResponse all = await CallAsync(context, GetObjectRightsTool.ToolName, new() {
			["environment-name"] = context.EnvironmentName, ["entity-schema-name"] = "Contact"
		});
		ObjectRightsToolResponse filtered = await CallAsync(context, GetObjectRightsTool.ToolName, new() {
			["environment-name"] = context.EnvironmentName, ["entity-schema-name"] = "Contact", ["grantee"] = AllEmployees
		});
		ObjectRightsToolResponse missing = await CallAsync(context, GetObjectRightsTool.ToolName, new() {
			["environment-name"] = context.EnvironmentName, ["entity-schema-name"] = $"UsrNoSuchObject{Guid.NewGuid():N}"
		});

		// Assert
		all.Success.Should().BeTrue(because: $"Contact's rights must be readable on a real stand. Error: {all.Error}");
		all.Output.Should().Contain("Contact", because: "the result names the object it read");
		filtered.Success.Should().BeTrue(because: $"a grantee filter is a read too. Error: {filtered.Error}");
		filtered.Output.Should().Contain(AllEmployees,
			because: "the filtered read reports the All employees row, or that it has none");
		missing.Success.Should().BeFalse(because: "a name that resolves to no schema fails instead of reporting access");
	}

	[Test]
	[Description("On a real stand, a set-object-rights preview reads the object, plans the change and writes nothing.")]
	[AllureTag(SetObjectRightsTool.ToolName)]
	[AllureName("set-object-rights preview against the real RightManagementService")]
	[AllureDescription("Previews a read grant for All employees on Contact (with enable-operation-permissions, so the preview is allowed whether or not Contact uses operation permissions), then checks Contact's rights are unchanged. Nothing is written.")]
	public async Task SetObjectRights_Should_Preview_Without_Writing_On_A_Real_Stand() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		Dictionary<string, object?> readContact = new() {
			["environment-name"] = context.EnvironmentName, ["entity-schema-name"] = "Contact"
		};
		ObjectRightsToolResponse before = await CallAsync(context, GetObjectRightsTool.ToolName, readContact);
		Dictionary<string, object?> grant = new() {
			["environment-name"] = context.EnvironmentName,
			["entity-schema-name"] = "Contact",
			["grantee"] = AllEmployees,
			["operations"] = "read",
			["enable-operation-permissions"] = true,
			["preview"] = true
		};

		// Act
		ObjectRightsToolResponse preview = await CallAsync(context, SetObjectRightsTool.ToolName, grant);
		ObjectRightsToolResponse after = await CallAsync(context, GetObjectRightsTool.ToolName, readContact);

		// Assert
		preview.Success.Should().BeTrue(because: $"a preview of an allowed change is not a failure. Error: {preview.Error}");
		(preview.Output ?? string.Empty).Should().Match(output => output.Contains("PREVIEW — nothing was changed")
				|| output.Contains("(no change)"),
			because: "a preview either shows the planned change or says which row already holds it — and writes nothing");
		after.Output.Should().Be(before.Output, because: "the preview must leave Contact's rights exactly as they were");
		before.Output.Should().Contain("priority order", because: "every listing states the priority rule its rows follow");
	}

	private static async Task<ObjectRightsToolResponse> CallAsync(ArrangeContext context, string toolName,
		Dictionary<string, object?> args) {
		CallToolResult callResult = await context.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> { ["args"] = args },
			context.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(because: $"{toolName} must return a structured result, not a protocol error");
		return EntitySchemaStructuredResultParser.Extract<ObjectRightsToolResponse>(callResult);
	}

	private async Task<ArrangeContext> ArrangeAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName (a reachable stand) to run the object-rights read-only MCP E2E.");
		}
		if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName!)) {
			Assert.Ignore($"The object-rights read-only MCP E2E requires a reachable sandbox. '{environmentName}' was not reachable.");
		}
		return new ArrangeContext(Session, new CancellationTokenSource(TimeSpan.FromMinutes(4)), environmentName!);
	}

	private new sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string EnvironmentName) : IAsyncDisposable {
		public ValueTask DisposeAsync() {
			CancellationTokenSource.Dispose();
			return ValueTask.CompletedTask;
		}
	}
}
