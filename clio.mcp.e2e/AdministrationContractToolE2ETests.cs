using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>Verifies administration discovery and error envelopes through an external MCP process.</summary>
[TestFixture, Category("McpE2E.NoEnvironment"), AllureNUnit, NonParallelizable]
[AllureFeature("Administration")]
public sealed class AdministrationContractToolE2ETests : McpContractFixtureBase {
	[TestCase(ManageUserTool.InspectToolName, "create")]
	[TestCase(ManageRoleTool.InspectToolName, "remove-functional")]
	[TestCase(ManageAccessTool.InspectToolName, "delegate")]
	[TestCase(ManageLicenseTool.InspectToolName, "role-redistribute")]
	[Description("Read-only administration tools reject mutation actions before resolving an environment.")]
	public async Task Inspection_RejectsMutation(string toolName, string action) {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		// Act
		CallToolResult call = await context.Session.CallToolAsync(toolName, new Dictionary<string, object?> {
			["args"] = new Dictionary<string, object?> { ["environment-name"] = "missing-" + Guid.NewGuid(), ["action"] = action }
		}, context.CancellationTokenSource.Token);
		// Assert
		CommandExecutionEnvelope response = McpCommandExecutionParser.Extract(call);
		response.ExitCode.Should().Be(1, because: "a mutation is invalid on a read-only tool");
		response.Output.Should().Contain(message => message.Value != null && message.Value.Contains("inspection tool accepts only"),
			because: "the action guard must run before environment resolution or network access");
	}

	[TestCase(ManageUserTool.InspectToolName, false, "list")]
	[TestCase(ManageUserTool.ToolName, true, "list")]
	[TestCase(ManageRoleTool.InspectToolName, false, "list")]
	[TestCase(ManageRoleTool.ToolName, true, "list")]
	[TestCase(ManageAccessTool.InspectToolName, false, "ip-list")]
	[TestCase(ManageAccessTool.ToolName, true, "ip-list")]
	[TestCase(ManageLicenseTool.InspectToolName, false, "user-list")]
	[TestCase(ManageLicenseTool.ToolName, true, "user-list")]
	[Description("Discovers each administration tool and binds an unknown environment without performing mutations.")]
	[AllureName("Administration tools expose classification and safe missing-environment errors")]
	public async Task Tool_DiscoveryAndMissingEnvironment(string toolName, bool destructive, string action) {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		string missing = "missing-admin-" + Guid.NewGuid().ToString("N");
		// Act
		IReadOnlyList<ToolContractIndexEntry> index = await context.Session.GetToolContractIndexAsync(context.CancellationTokenSource.Token);
		CallToolResult call = await context.Session.CallToolAsync(toolName, new Dictionary<string, object?> {
			["args"] = new Dictionary<string, object?> {
			["environment-name"] = missing, ["action"] = action
			}
		}, context.CancellationTokenSource.Token);
		// Assert
		index.Should().ContainSingle(entry => entry.Name == toolName && entry.Destructive == destructive,
			because: "agents must discover each administration capability with its correct mutation classification");
		call.IsError.Should().NotBeTrue(because: "valid arguments must bind to a structured command response: " + string.Join(" ", call.Content.OfType<TextContentBlock>().Select(block => block.Text)));
		CommandExecutionEnvelope response = McpCommandExecutionParser.Extract(call);
		response.ExitCode.Should().NotBe(0, because: "an unknown environment must fail before contacting Creatio");
		response.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Error,
			because: "the agent needs a readable failure rather than an empty result");
	}

	[TestCase("../../etc/passwd", "password-file must be the name of a file")]
	[TestCase("Pa55 word!", "password-file must be the name of a file")]
	[Description("manage-user refuses a password-file that is not a plain name before resolving an environment, without repeating the value, which may be a secret pasted by mistake.")]
	[AllureName("manage-user refuses a password-file that is a path or a pasted secret")]
	public async Task ManageUser_RefusesAPasswordFileThatIsNotAPlainName(string value, string expected) {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		// Act
		CallToolResult call = await context.Session.CallToolAsync(ManageUserTool.ToolName, new Dictionary<string, object?> {
			["args"] = new Dictionary<string, object?> {
				["environment-name"] = "missing-" + Guid.NewGuid(), ["action"] = "password", ["id"] = Guid.NewGuid(),
				["password-file"] = value
			}
		}, context.CancellationTokenSource.Token);
		// Assert
		CommandExecutionEnvelope response = McpCommandExecutionParser.Extract(call);
		string text = string.Join(" ", (response.Output ?? []).Select(message => message.Value));
		response.ExitCode.Should().Be(1, because: "only a plain name the host gave is a valid password-file");
		text.Should().Contain(expected, because: "the refusal must say what a valid reference is, before any environment is resolved");
		text.Should().NotContain(value, because: "the refused value may be a secret and must not be echoed back to the agent");
	}

	[Test]
	[Description("manage-user refuses a call that names both password-file and password-env before resolving an environment.")]
	[AllureName("manage-user refuses two password references")]
	public async Task ManageUser_RefusesBothPasswordReferences() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		// Act
		CallToolResult call = await context.Session.CallToolAsync(ManageUserTool.ToolName, new Dictionary<string, object?> {
			["args"] = new Dictionary<string, object?> {
				["environment-name"] = "missing-" + Guid.NewGuid(), ["action"] = "create", ["id"] = Guid.NewGuid(),
				["password-file"] = "demo-users", ["password-env"] = "CLIO_ADMIN_PASSWORD_DEMO"
			}
		}, context.CancellationTokenSource.Token);
		// Assert
		CommandExecutionEnvelope response = McpCommandExecutionParser.Extract(call);
		response.ExitCode.Should().Be(1, because: "two references leave it ambiguous which password the caller meant");
		response.Output.Should().Contain(message => message.Value != null && message.Value.Contains("not both"),
			because: "the refusal must say to supply one reference");
	}
}
