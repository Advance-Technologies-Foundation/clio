using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>Exercises disposable user, role and access lifecycles through the real MCP server.</summary>
[TestFixture, Category("McpE2E.Sandbox"), NonParallelizable, AllureNUnit]
[AllureFeature("Administration")]
public sealed class AdministrationToolE2ETests : McpContractFixtureBase {
	private const string PasswordVariable = "CLIO_ADMIN_E2E_PASSWORD";
	private const string NextPasswordVariable = "CLIO_ADMIN_E2E_NEXT_PASSWORD";
	private readonly string _password = "C!9a" + Guid.NewGuid().ToString("N");
	private readonly string _nextPassword = "C!9b" + Guid.NewGuid().ToString("N");

	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		settings.ProcessEnvironmentVariables[PasswordVariable] = _password;
		settings.ProcessEnvironmentVariables[NextPasswordVariable] = _nextPassword;
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("Creates a disposable account and organizational roles, verifies management and access effects, then removes its owned records.")]
	[AllureName("Agent administration lifecycle against an exclusive sandbox")]
	public async Task Administration_Lifecycle(bool external) {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests || string.IsNullOrWhiteSpace(settings.Sandbox.EnvironmentName)
			|| settings.Sandbox.AdministrationContactId is null) {
			Assert.Ignore("Enable destructive tests and configure Sandbox.EnvironmentName and a dedicated AdministrationContactId.");
		}
		await using var context = Arrange(TimeSpan.FromMinutes(12));
		string environment = settings.Sandbox.EnvironmentName!;
		Guid user = Guid.NewGuid(), division = Guid.NewGuid(), functional = Guid.NewGuid(), ip = Guid.NewGuid();
		Guid manager = Guid.Empty;
		string login = "clio-admin-e2e-" + user.ToString("N");
		Guid root = Guid.Parse(external ? "720b771c-e7a7-4f31-9cfb-52cd21c3739f" : "a29a3ba5-4b0d-de11-9a51-005056c00008");
		bool userCreated = false, divisionCreated = false, functionalCreated = false;
		try {
			// Act
			userCreated = true; // Attempt cleanup even when persistence succeeds but the response fails.
			JsonElement created = await Success(ManageUserTool.ToolName, new() {
				["action"] = "create", ["id"] = user, ["user-login"] = login,
				["contact-id"] = settings.Sandbox.AdministrationContactId, ["password-env"] = PasswordVariable,
				["force-change-password"] = false, ["external"] = external
			});
			divisionCreated = true;
			await Success(ManageRoleTool.ToolName, new() {
				["action"] = "create", ["id"] = division, ["name"] = login + " division", ["type"] = 1,
				["parent-id"] = root
			});
			JsonElement chiefs = await Success(ManageRoleTool.ToolName, new() { ["action"] = "ensure-manager", ["parent-id"] = division });
			manager = chiefs.GetProperty("Id").GetGuid();
			JsonElement chiefsAgain = await Success(ManageRoleTool.ToolName, new() { ["action"] = "ensure-manager", ["parent-id"] = division });
			functionalCreated = true;
			await Success(ManageRoleTool.ToolName, new() {
				["action"] = "create", ["id"] = functional, ["name"] = login + " functional", ["type"] = 6,
				["parent-id"] = root
			});
			await Success(ManageRoleTool.ToolName, new() { ["action"] = "add-member", ["id"] = manager, ["user-id"] = user });
			JsonElement managerMembers = await Success(ManageRoleTool.InspectToolName, new() { ["action"] = "members", ["id"] = manager });
			JsonElement effectiveManagerMembers = await Success(ManageRoleTool.InspectToolName, new() {
				["action"] = "members", ["id"] = manager, ["effective"] = true
			});
			JsonElement effective = await Success(ManageRoleTool.InspectToolName, new() {
				["action"] = "memberships", ["user-id"] = user, ["effective"] = true
			});
			await Success(ManageRoleTool.ToolName, new() {
				["action"] = "add-functional", ["id"] = division, ["functional-id"] = functional
			});
			JsonElement managerInherited = await Success(ManageRoleTool.InspectToolName, new() {
				["action"] = "memberships", ["user-id"] = user, ["effective"] = true
			});
			await Success(ManageRoleTool.ToolName, new() {
				["action"] = "remove-functional", ["id"] = division, ["functional-id"] = functional
			});
			JsonElement managerAfterRemoval = await Success(ManageRoleTool.InspectToolName, new() {
				["action"] = "memberships", ["user-id"] = user, ["effective"] = true
			});
			if (!external) {
				Guid operation = Guid.Parse("3533c7dc-893d-4545-8bd4-96a8aa7a3777");
				JsonElement grant = await Success(ManageAccessTool.ToolName, new() {
					["action"] = "deny-operation", ["operation-id"] = operation, ["unit-id"] = user
				});
				JsonElement priority = await Success(ManageAccessTool.ToolName, new() {
					["action"] = "operation-position", ["id"] = grant[0].GetProperty("Id").GetGuid(), ["position"] = 0
				});
				priority.GetProperty("Position").GetInt32().Should().Be(0,
					because: "the real MCP path must execute the version-gated priority and cache bridge");
				await Success(ManageAccessTool.ToolName, new() {
					["action"] = "revoke-operation", ["operation-id"] = operation, ["unit-id"] = user
				});
			}
			JsonElement disabled = await Success(ManageUserTool.ToolName, new() { ["action"] = "update", ["id"] = user, ["active"] = false });
			JsonElement unlocked = await Success(ManageUserTool.ToolName, new() { ["action"] = "unlock", ["id"] = user });
			await Success(ManageUserTool.ToolName, new() { ["action"] = "update", ["id"] = user, ["active"] = true });
			JsonElement password = await Success(ManageUserTool.ToolName, new() {
				["action"] = "password", ["id"] = user, ["password-env"] = NextPasswordVariable, ["force-change-password"] = false
			});
			await Success(ManageAccessTool.ToolName, new() {
				["action"] = "ip-create", ["id"] = ip, ["unit-id"] = user, ["begin-ip"] = "0.0.0.0", ["end-ip"] = "255.255.255.255"
			});
			JsonElement ranges = await Success(ManageAccessTool.InspectToolName, new() { ["action"] = "ip-list", ["unit-id"] = user });
			await Success(ManageAccessTool.ToolName, new() { ["action"] = "ip-delete", ["id"] = ip, ["unit-id"] = user });
			await Success(ManageAccessTool.ToolName, new() { ["action"] = "delegate", ["grantor-id"] = functional, ["unit-id"] = user });
			JsonElement delegated = await Success(ManageRoleTool.InspectToolName, new() {
				["action"] = "memberships", ["user-id"] = user, ["effective"] = true
			});
			await Success(ManageAccessTool.ToolName, new() { ["action"] = "revoke-delegation", ["grantor-id"] = functional, ["unit-id"] = user });
			JsonElement afterRevoke = await Success(ManageRoleTool.InspectToolName, new() {
				["action"] = "memberships", ["user-id"] = user, ["effective"] = true
			});
			JsonElement licenses = await Success(ManageLicenseTool.InspectToolName, new() { ["action"] = "user-list", ["user-id"] = user });
			// Assert
			created.GetProperty("Name").GetString().Should().Be(login, because: "the account must persist the requested login");
			created.GetProperty("ConnectionType").GetInt32().Should().Be(external ? 1 : 0,
				because: "internal and external users must retain their requested connection type");
			chiefs.GetProperty("ConnectionType").GetInt32().Should().Be(external ? 1 : 0,
				because: "the manager group must belong to the same connection domain as its organizational parent");
			chiefsAgain.GetProperty("Id").GetGuid().Should().Be(manager, because: "ensuring managers must reuse the automatically created manager role");
			managerMembers.EnumerateArray().Select(row => row.GetProperty("SysUser").GetProperty("value").GetGuid()).Should()
				.Contain(user, because: "an agent must be able to inspect the selected manager group's users directly");
			effectiveManagerMembers.EnumerateArray().Select(row => row.GetProperty("SysAdminUnit").GetProperty("value").GetGuid()).Should()
				.Contain(user, because: "the effective role-centric query must resolve user identities through the native relationship");
			effective.EnumerateArray().Select(row => row.GetProperty("SysAdminUnitRoleId").GetGuid()).Should()
				.Contain(manager, because: "direct manager assignment must become effective");
			managerInherited.EnumerateArray().Select(row => row.GetProperty("SysAdminUnitRoleId").GetGuid()).Should()
				.Contain(functional, because: "a manager must inherit functional roles associated with the organizational parent");
			managerAfterRemoval.EnumerateArray().Select(row => row.GetProperty("SysAdminUnitRoleId").GetGuid()).Should()
				.NotContain(functional, because: "the guarded removal must refresh the manager's effective access");
			disabled.GetProperty("Active").GetBoolean().Should().BeFalse(because: "deactivation must persist");
			unlocked.GetProperty("Active").GetBoolean().Should().BeFalse(because: "unlock must not activate a disabled account");
			password.GetProperty("passwordAccepted").GetBoolean().Should().BeTrue(because: "the host must resolve the secret reference and reach the password endpoint");
			ranges.EnumerateArray().Select(row => row.GetProperty("Id").GetGuid()).Should().Contain(ip, because: "the access rule must exist before its explicit removal");
			delegated.EnumerateArray().Select(row => row.GetProperty("SysAdminUnitRoleId").GetGuid()).Should().Contain(functional,
				because: "delegation must confer the grantor's effective role");
			afterRevoke.EnumerateArray().Select(row => row.GetProperty("SysAdminUnitRoleId").GetGuid()).Should().NotContain(functional,
				because: "revocation must remove the isolated delegated membership");
			licenses.ValueKind.Should().Be(JsonValueKind.Array, because: "license inspection must work even on an unlicensed lab");
		} finally {
			List<string> cleanupFailures = [];
			if (userCreated) { await Cleanup(ManageUserTool.ToolName, ManageUserTool.InspectToolName, user); }
			if (manager != Guid.Empty) { await Cleanup(ManageRoleTool.ToolName, ManageRoleTool.InspectToolName, manager); }
			if (divisionCreated) { await Cleanup(ManageRoleTool.ToolName, ManageRoleTool.InspectToolName, division); }
			if (functionalCreated) { await Cleanup(ManageRoleTool.ToolName, ManageRoleTool.InspectToolName, functional); }
			cleanupFailures.Should().BeEmpty(because: "every attempted fixture must be removed; listed IDs need explicit inspection");

			async Task Cleanup(string manageTool, string inspectTool, Guid id) {
				try {
					JsonElement rows = await Success(inspectTool, new() { ["action"] = "list", ["id"] = id });
					if (rows.GetArrayLength() != 0) {
						await Success(manageTool, new() { ["action"] = "delete", ["id"] = id });
					}
				} catch (Exception) {
					cleanupFailures.Add(manageTool + "/" + id);
				}
			}
		}

		[AllureStep("Call administration tool and verify its execution envelope")]
		async Task<JsonElement> Success(string toolName, Dictionary<string, object?> args) {
			args["environment-name"] = environment;
			CallToolResult call = await context.Session.CallToolAsync(toolName, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
			string allText = string.Join(" ", call.Content.OfType<TextContentBlock>().Select(block => block.Text));
			allText.Contains(_password, StringComparison.Ordinal).Should().BeFalse(because: "the first password must never appear in MCP content");
			allText.Contains(_nextPassword, StringComparison.Ordinal).Should().BeFalse(because: "the replacement password must never appear in MCP content");
			call.IsError.Should().NotBeTrue(because: "the administration payload must bind correctly");
			CommandExecutionEnvelope envelope = McpCommandExecutionParser.Extract(call);
			envelope.ExitCode.Should().Be(0, because: toolName + "/" + args["action"] + " must succeed: " + string.Join(" ", envelope.Output?.Select(message => message.Value) ?? []));
			CommandLogMessageEnvelope info = envelope.Output.Should().ContainSingle(message => message.MessageType == LogDecoratorType.Info,
				because: "successful administration emits one structured state result").Which;
			using JsonDocument parsed = JsonDocument.Parse(info.Value!);
			return parsed.RootElement.Clone();
		}
	}
}
