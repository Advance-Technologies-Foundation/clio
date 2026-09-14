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

/// <summary>Proves bounded real license capacity and asynchronous role redistribution on disposable identities.</summary>
[TestFixture, Category("McpE2E.Sandbox"), NonParallelizable, AllureNUnit]
[AllureFeature("Administration")]
public sealed class AdministrationLicenseE2ETests : McpContractFixtureBase {
	private const string PasswordVariable = "CLIO_ADMIN_PASSWORD_LICENSE_E2E";
	private readonly string _password = "C!9a" + Guid.NewGuid().ToString("N");

	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) =>
		settings.ProcessEnvironmentVariables[PasswordVariable] = _password;

	[Test]
	[Description("Exhausts a reserved small license package, rejects overflow, and proves role assignment/removal with explicit manual preservation.")]
	[AllureName("Licensed administration capacity and redistribution")]
	public async Task Administration_Licenses_PreserveCapacityAndManualAssignments() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests || string.IsNullOrWhiteSpace(settings.Sandbox.EnvironmentName)
			|| settings.Sandbox.AdministrationLicensePackageId is null) {
			Assert.Ignore("Configure an authorized sandbox and AdministrationLicensePackageId for an unused 2..8-seat test package.");
		}
		await using var context = Arrange(TimeSpan.FromMinutes(15));
		CancellationToken requestToken = context.CancellationTokenSource.Token;
		string environment = settings.Sandbox.EnvironmentName!;
		Guid package = settings.Sandbox.AdministrationLicensePackageId!.Value;
		Guid role = Guid.NewGuid();
		List<Guid> users = [], contacts = [];
		bool roleAttempted = false, completed = false;
		try {
			await CreateUser();
			JsonElement initial = await License(users[0]);
			int capacity = initial.GetProperty("PaidCount").GetInt32();
			capacity.Should().BeInRange(2, 8, because: "the fixture must never consume an unbounded number of licenses");
			initial.GetProperty("UsedCount").GetInt32().Should().Be(0, because: "this package must be reserved and unused before testing");
			initial.GetProperty("AvailableCount").GetInt32().Should().Be(capacity, because: "the entire small package must be available");
			while (users.Count <= capacity) { await CreateUser(); }

			// Act: direct capacity and removal through the real tool.
			foreach (Guid user in users.Take(capacity)) { await Assign(user, false); }
			JsonElement full = await License(users[^1]);
			full.GetProperty("AvailableCount").GetInt32().Should().Be(0, because: "every reserved seat is assigned to a disposable account");
			CommandExecutionEnvelope overflow = await Command(ManageLicenseTool.ToolName, new() {
				["action"] = "user-assign", ["user-id"] = users[^1], ["package-id"] = package
			});
			overflow.ExitCode.Should().Be(1, because: "the account beyond capacity must be rejected");
			overflow.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Error, because: "capacity failure must be actionable");
			(await License(users[^1])).GetProperty("Checked").GetBoolean().Should().BeFalse(because: "a failed assignment must not consume a seat");
			foreach (Guid user in users.Take(capacity)) { await Assign(user, true); }
			(await License(users[0])).GetProperty("UsedCount").GetInt32().Should().Be(0, because: "direct removals must return all reserved seats");

			roleAttempted = true;
			await Success(ManageRoleTool.ToolName, new() {
				["action"] = "create", ["id"] = role, ["name"] = "CLIO license E2E " + role.ToString("N"),
				["type"] = 6, ["parent-id"] = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008")
			});
			foreach (Guid user in users.Take(2)) {
				await Success(ManageRoleTool.ToolName, new() { ["action"] = "add-member", ["id"] = role, ["user-id"] = user });
			}
			await Assign(users[0], false);
			await RoleLicense(false);
			await Redistribute(false);
			await WaitChecked(users[1], true);
			(await License(users[0])).GetProperty("SysLicSource").GetString().Should().Be("Manually", because: "default redistribution must preserve the manually assigned license source");
			await RoleLicense(true);
			await Redistribute(false);
			await WaitChecked(users[1], false);
			(await License(users[0])).GetProperty("Checked").GetBoolean().Should().BeTrue(because: "removing a role entitlement must preserve a manual assignment by default");
			await Redistribute(true);
			await WaitChecked(users[0], false);

			// Assert
			(await License(users[0])).GetProperty("UsedCount").GetInt32().Should().Be(0, because: "explicit manual redistribution must release the final fixture seat");
			completed = true;
		} finally {
			using CancellationTokenSource cleanupBudget = new(TimeSpan.FromMinutes(3));
			requestToken = cleanupBudget.Token;
			List<string> failures = [];
			foreach (Guid user in users) { await Clean(() => Success(ManageUserTool.ToolName, new() { ["action"] = "delete", ["id"] = user }), "user/" + user); }
			if (roleAttempted) { await Clean(() => Success(ManageRoleTool.ToolName, new() { ["action"] = "delete", ["id"] = role }), "role/" + role); }
			foreach (Guid contact in contacts) {
				await Clean(async () => {
					CallToolResult call = await Call(ODataDeleteTool.ToolName, new() { ["entity"] = "Contact", ["id"] = contact.ToString(), ["confirm"] = true });
					EntitySchemaStructuredResultParser.Extract<ODataWriteResponse>(call).Success.Should().BeTrue(because: "the disposable contact must be deleted");
				}, "contact/" + contact);
			}
			if (completed) { failures.Should().BeEmpty(because: "all disposable licensed fixtures must be removed"); }
			else if (failures.Count != 0) { TestContext.Error.WriteLine("Additional cleanup failures: " + string.Join(", ", failures)); }
			async Task Clean(Func<Task> cleanup, string identity) {
				try { await cleanup(); } catch (Exception) { failures.Add(identity); }
			}
		}

		async Task CreateUser() {
			Guid contact = Guid.NewGuid(), user = Guid.NewGuid();
			string login = "clio-license-e2e-" + user.ToString("N");
			contacts.Add(contact);
			TestContext.Progress.WriteLine($"License fixture contact={contact}, user={user}, role={role}");
			CallToolResult created = await Call(ODataCreateTool.ToolName, new() {
				["entity"] = "Contact", ["rows"] = new[] { new { Id = contact, Name = login } }
			});
			EntitySchemaStructuredResultParser.Extract<ODataCreateBatchResponse>(created).Created.Should().Be(1, because: "each account needs its own disposable contact");
			users.Add(user);
			await Success(ManageUserTool.ToolName, new() {
				["action"] = "create", ["id"] = user, ["user-login"] = login, ["contact-id"] = contact,
				["password-env"] = PasswordVariable, ["force-change-password"] = false
			});
		}

		async Task<JsonElement> License(Guid user) {
			JsonElement licenses = await Success(ManageLicenseTool.InspectToolName, new() { ["action"] = "user-list", ["user-id"] = user });
			return licenses.EnumerateArray().Single(item => item.GetProperty("Id").GetGuid() == package).Clone();
		}
		Task<JsonElement> Assign(Guid user, bool remove) => Success(ManageLicenseTool.ToolName, new() {
			["action"] = remove ? "user-remove" : "user-assign", ["user-id"] = user, ["package-id"] = package
		});
		Task<JsonElement> RoleLicense(bool remove) => Success(ManageLicenseTool.ToolName, new() {
			["action"] = remove ? "role-remove" : "role-assign", ["role-id"] = role, ["package-id"] = package
		});
		async Task Redistribute(bool includeManual) {
			JsonElement receipt = await Success(ManageLicenseTool.ToolName, new() {
				["action"] = "role-redistribute", ["role-id"] = role, ["include-manual"] = includeManual
			});
			receipt.GetProperty("redistributionScheduled").GetBoolean().Should().BeTrue(because: "the native scheduling process must complete successfully");
			receipt.GetProperty("userAssignmentsVerified").GetBoolean().Should().BeFalse(because: "the receipt must not claim the asynchronous result");
		}
		async Task WaitChecked(Guid user, bool expected) {
			DateTime deadline = DateTime.UtcNow.AddMinutes(3);
			while (DateTime.UtcNow < deadline) {
				if ((await License(user)).GetProperty("Checked").GetBoolean() == expected) { return; }
				await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationTokenSource.Token);
			}
			Assert.Fail("The scheduled redistribution did not reach the expected assignment state within three minutes.");
		}
		async Task<CallToolResult> Call(string tool, Dictionary<string, object?> args) {
			args["environment-name"] = environment;
			CallToolResult result = await context.Session.CallToolAsync(tool, new Dictionary<string, object?> { ["args"] = args }, requestToken);
			string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
			text.Contains(_password, StringComparison.Ordinal).Should().BeFalse(because: "the password must never appear in MCP output");
			result.IsError.Should().NotBeTrue(because: "the real tool arguments must bind");
			return result;
		}
		async Task<CommandExecutionEnvelope> Command(string tool, Dictionary<string, object?> args) => McpCommandExecutionParser.Extract(await Call(tool, args));
		async Task<JsonElement> Success(string tool, Dictionary<string, object?> args) {
			CommandExecutionEnvelope result = await Command(tool, args);
			result.ExitCode.Should().Be(0, because: tool + "/" + args["action"] + " must succeed: " + string.Join(" ", result.Output?.Select(message => message.Value) ?? []));
			CommandLogMessageEnvelope info = result.Output.Should().ContainSingle(message => message.MessageType == LogDecoratorType.Info,
				because: "a successful administration action emits one structured result").Which;
			using JsonDocument json = JsonDocument.Parse(info.Value!);
			return json.RootElement.Clone();
		}
	}
}
