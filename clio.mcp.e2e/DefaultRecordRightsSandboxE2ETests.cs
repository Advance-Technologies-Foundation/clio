using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// Developer-local end-to-end proof of the record layer against a REAL Creatio: the facts set-default-record-rights and
/// apply-default-record-rights rest on — the full-list save, rules applied to a new record on insert, the actualization
/// process that replaces default-origin rights on existing records, and the switch — are otherwise tested only against
/// mocks.
/// </summary>
/// <remarks>
/// It publishes a schema into a fixture-owned package, so it belongs to the destructive LocalOnly sub-tier for the reason
/// recorded on <see cref="DataBindingDbColorSchemaE2ETests"/>. The package — and with it the schema, its rules and its
/// records — is deleted by the fixture teardown on every outcome.
/// </remarks>
[TestFixture]
[Category("McpE2E.Sandbox")]
[Category("McpE2E.Manual")]
[Category("LocalOnly")]
[Explicit("Publishes a schema, changes its record permissions and runs the record-rights update on the stand; run it by hand against a leased sandbox.")]
// [AllureNUnit] is intentionally omitted, for the reason recorded on EntitySchemaToolE2ETests.
[AllureFeature(SetDefaultRecordRightsTool.ToolName)]
[NonParallelizable]
public sealed class DefaultRecordRightsSandboxE2ETests : DataBindingDbFixtureBase {

	private const string AllEmployees = "a29a3ba5-4b0d-de11-9a51-005056c00008";
	private const string AllExternalUsers = "720b771c-e7a7-4f31-9cfb-52cd21c3739f";

	[Test]
	[Description("On a real stand: a record created while off gets no rights; an unnamed enable is refused; enable + grant (with do-not-apply-for-manager) and a second rule are saved and read back; a record inserted afterwards gets the rule's rights and apply gives them to the early record; a revoke that empties the rule removes it and keeps the other rule; apply then removes the default-origin right; a disable keeps the stored rule, apply is then refused, and a repeated disable changes nothing.")]
	[AllureTag(SetDefaultRecordRightsTool.ToolName)]
	[AllureTag(ApplyDefaultRecordRightsTool.ToolName)]
	[AllureTag(GetObjectRightsTool.ToolName)]
	[AllureName("default record rights round-trip on a real Creatio stand")]
	[AllureDescription("insert while off → enable + grant (do-not-apply-for-manager) + second rule → insert → apply (adds) → revoke → apply (removes) → disable on a disposable object; the fixture package is deleted in teardown.")]
	public async Task DefaultRecordRights_Should_RoundTrip_On_A_Real_Stand() {
		TeamCityRunGuard.IgnoreIfRunningUnderTeamCityOrGitHubActions(
			"create-entity-schema publishes configuration and starts the global OData rebuild, which makes every "
			+ "concurrent test on the shared stand fail. Run this scenario by hand against a leased sandbox.");
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: true);
		string objectName = $"UsrRecRt{Guid.NewGuid().ToString("N").Substring(0, 10)}";
		CommandExecutionActResult created = await ActCommandAsync(arrangeContext, CreateEntitySchemaToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["package-name"] = arrangeContext.PackageName,
				["schema-name"] = objectName,
				["title-localizations"] = new Dictionary<string, string> { ["en-US"] = "Record rights e2e" },
				["columns"] = new[] { new Dictionary<string, object?> { ["column-name"] = "UsrName", ["type"] = "Text" } }
			});
		AssertCommandExitCode(created, 0, "the disposable object must exist before its record rights are changed");

		Dictionary<string, object?> Rule(string operations, bool revoke = false, bool enable = false) {
			Dictionary<string, object?> args = new() {
				["environment-name"] = arrangeContext.EnvironmentName,
				["entity-schema-name"] = objectName,
				["author"] = AllEmployees,
				["grantee"] = AllEmployees,
				["operations"] = operations,
				["revoke"] = revoke,
				["enable-record-permissions"] = enable
			};
			if (!revoke) {
				args["do-not-apply-for-manager"] = true;
			}
			return args;
		}
		Dictionary<string, object?> Switch(bool enable) => new() {
			["environment-name"] = arrangeContext.EnvironmentName,
			["entity-schema-name"] = objectName,
			[enable ? "enable-record-permissions" : "disable-record-permissions"] = true
		};
		Dictionary<string, object?> Apply() => new() {
			["environment-name"] = arrangeContext.EnvironmentName,
			["entity-schema-name"] = objectName,
			["timeout-seconds"] = 60
		};

		Dictionary<string, object?> ExternalRule() => new() {
			["environment-name"] = arrangeContext.EnvironmentName,
			["entity-schema-name"] = objectName,
			["author"] = AllExternalUsers,
			["grantee"] = AllEmployees,
			["operations"] = "read"
		};

		// Act — a record created while record permissions are off gets no record rights
		string earlyRecord = await AllureApi.Step("Insert a record while record permissions are off",
			() => InsertAsync(arrangeContext, objectName));

		// Act — a grant on an object whose record permissions are off, without naming the enable
		ObjectRightsToolResponse unnamed = await AllureApi.Step("Grant without naming the enable",
			() => CallAsync(arrangeContext, SetDefaultRecordRightsTool.ToolName, Rule("read")));

		// Assert
		AllureApi.Step("Verify the unnamed enable is refused", () => {
			unnamed.Success.Should().BeFalse(because: "turning record permissions on must be named in the arguments");
			unnamed.Error.Should().Contain("enable-record-permissions", because: "the refusal names the flag");
		});

		// Act — enable and grant in one call, add a second, unrelated rule, then read the object back
		ObjectRightsToolResponse granted = await AllureApi.Step("Enable and grant in one call",
			() => CallAsync(arrangeContext, SetDefaultRecordRightsTool.ToolName, Rule("read,edit", enable: true)));
		ObjectRightsToolResponse second = await AllureApi.Step("Add a second rule in its own call",
			() => CallAsync(arrangeContext, SetDefaultRecordRightsTool.ToolName, ExternalRule()));
		ObjectRightsToolResponse read = await AllureApi.Step("Read the object back",
			() => CallAsync(arrangeContext, GetObjectRightsTool.ToolName, ReadArgs()));

		// Assert
		AllureApi.Step("Verify the enable and the first rule", () => {
			granted.Success.Should().BeTrue(because: $"the named enable and grant must apply. Error: {granted.Error}");
			granted.Output.Should().Contain("A rule is added", because: "the result reports the rule");
			granted.Output.Should().Contain("existing record(s)", because: "the result reports the record count for the apply decision");
		});
		AllureApi.Step("Verify the second rule is added", () => second.Success.Should().BeTrue(
			because: $"a second rule is added in its own call. Error: {second.Error}"));
		AllureApi.Step("Verify the read-back shows the switch and both rules", () => {
			read.Output.Should().Contain("Record permissions: ON", because: "the switch was turned on");
			read.Output.Should().Contain("read granted, edit granted, delete -, do not apply for manager: true",
				because: "the first rule and its manager flag were saved and read back");
			read.Output.Should().Contain($"All external users ({AllExternalUsers}) → All employees ({AllEmployees}): read granted",
				because: "the second rule was saved without touching the first");
		});

		// Act — a record created now gets the rule's rights on insert; apply gives them to the early record too
		string lateRecord = await AllureApi.Step("Insert a record after the enable",
			() => InsertAsync(arrangeContext, objectName));
		string lateRights = await AllureApi.Step("Read the late record's rights",
			() => ReadRecordRightsAsync(arrangeContext, objectName, lateRecord));
		string earlyBeforeApply = await AllureApi.Step("Read the early record's rights before apply",
			() => ReadRecordRightsAsync(arrangeContext, objectName, earlyRecord));
		ObjectRightsToolResponse firstApply = await AllureApi.Step("Apply the rules to existing records",
			() => CallAsync(arrangeContext, ApplyDefaultRecordRightsTool.ToolName, Apply()));
		string earlyAfterApply = await AllureApi.Step("Read the early record's rights after apply",
			() => ReadRecordRightsAsync(arrangeContext, objectName, earlyRecord));

		// Assert
		AllureApi.Step("Verify a record inserted after the rule gets its rights", () => lateRights.Should().Contain(
			"granted -> All employees", because: "a default rule applies to a record created after it"));
		AllureApi.Step("Verify the enable gave the early record no rights", () => earlyBeforeApply.Should().NotContain(
			"-> All employees", because: "an enable gives existing records no rights"));
		AllureApi.Step("Verify the first apply completed", () => {
			firstApply.Success.Should().BeTrue(because: $"the record-rights update must complete. Error: {firstApply.Error}");
			firstApply.Output.Should().Contain("completed", because: "a small table finishes well within the wait");
			firstApply.Output.Should().NotContain("could not check whether a record-rights update is already running",
				because: "the running-update check must work on a real stand, not only fail quietly");
		});
		AllureApi.Step("Verify apply gave the early record the rule's rights", () => earlyAfterApply.Should().Contain(
			"granted -> All employees", because: "the update applies the current rules to a record that had none"));

		// Act — revoke the first rule's last rights (it is removed), then apply again
		ObjectRightsToolResponse revoked = await AllureApi.Step("Revoke the first rule's last rights",
			() => CallAsync(arrangeContext, SetDefaultRecordRightsTool.ToolName, Rule("read,edit", revoke: true)));
		ObjectRightsToolResponse afterRevoke = await AllureApi.Step("Read the object after the revoke",
			() => CallAsync(arrangeContext, GetObjectRightsTool.ToolName, ReadArgs()));
		ObjectRightsToolResponse secondApply = await AllureApi.Step("Apply again after the revoke",
			() => CallAsync(arrangeContext, ApplyDefaultRecordRightsTool.ToolName, Apply()));
		string lateAfterApply = await AllureApi.Step("Read the late record's rights after the second apply",
			() => ReadRecordRightsAsync(arrangeContext, objectName, lateRecord));

		// Assert
		AllureApi.Step("Verify the revoke removed the rule", () => {
			revoked.Success.Should().BeTrue(because: $"the revoke must apply. Error: {revoked.Error}");
			revoked.Output.Should().Contain("is removed", because: "a rule left with no right is removed");
		});
		AllureApi.Step("Verify the full-list save kept the other rule", () => afterRevoke.Output.Should().Contain(
			$"All external users ({AllExternalUsers}) → All employees ({AllEmployees}): read granted",
			because: "the full-list save kept the rule the call did not name"));
		AllureApi.Step("Verify the second apply completed", () => {
			secondApply.Success.Should().BeTrue(because: $"the record-rights update must complete. Error: {secondApply.Error}");
			secondApply.Output.Should().Contain("completed", because: "a small table finishes well within the wait");
			secondApply.Output.Should().NotContain("could not check whether a record-rights update is already running",
				because: "the running-update check must work on a real stand");
		});
		AllureApi.Step("Verify the second apply removed the default-origin right", () => {
			lateAfterApply.Should().Contain("Supervisor", because: "the record's rights were read: its author keeps its own right");
			lateAfterApply.Should().NotContain("-> All employees",
				because: "the update removes the rights that came from a rule that no longer exists");
		});

		// Act — turn record permissions off with a rule still stored, then try to apply, then disable again
		ObjectRightsToolResponse disabled = await AllureApi.Step("Disable record permissions",
			() => CallAsync(arrangeContext, SetDefaultRecordRightsTool.ToolName, Switch(false)));
		ObjectRightsToolResponse afterDisable = await AllureApi.Step("Read the object after the disable",
			() => CallAsync(arrangeContext, GetObjectRightsTool.ToolName, ReadArgs()));
		ObjectRightsToolResponse applyWhileOff = await AllureApi.Step("Apply while record permissions are off",
			() => CallAsync(arrangeContext, ApplyDefaultRecordRightsTool.ToolName, Apply()));
		ObjectRightsToolResponse disabledAgain = await AllureApi.Step("Disable again",
			() => CallAsync(arrangeContext, SetDefaultRecordRightsTool.ToolName, Switch(false)));

		// Assert
		AllureApi.Step("Verify the disable applied", () => {
			disabled.Success.Should().BeTrue(because: $"the named disable must apply. Error: {disabled.Error}");
			disabled.Output.Should().Contain("turned OFF", because: "the result states the switch");
		});
		AllureApi.Step("Verify the disable kept the stored rule", () => {
			afterDisable.Output.Should().Contain("not in effect while record permissions are off",
				because: "a switch-only save keeps the stored rules");
			afterDisable.Output.Should().Contain("All external users", because: "the stored rule is still listed");
		});
		AllureApi.Step("Verify apply is refused while off", () => {
			applyWhileOff.Success.Should().BeFalse(because: "there is nothing to apply while record rights are not evaluated");
			applyWhileOff.Error.Should().Contain("are OFF", because: "the refusal says why");
		});
		AllureApi.Step("Verify a repeated disable changes nothing", () => disabledAgain.Output.Should().Contain(
			"(no change)", because: "a repeated call changes nothing"));

		// Act — remove the last stored rule while record permissions are off (the save sends an empty list)
		ObjectRightsToolResponse lastRemoved = await AllureApi.Step("Revoke the last stored rule while off",
			() => CallAsync(arrangeContext, SetDefaultRecordRightsTool.ToolName, new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["entity-schema-name"] = objectName,
				["author"] = AllExternalUsers,
				["grantee"] = AllEmployees,
				["operations"] = "read",
				["revoke"] = true
			}));
		ObjectRightsToolResponse noRules = await AllureApi.Step("Read the object with no rule left",
			() => CallAsync(arrangeContext, GetObjectRightsTool.ToolName, ReadArgs()));

		// Assert
		AllureApi.Step("Verify the last rule is removed", () => {
			lastRemoved.Success.Should().BeTrue(because: $"the platform must accept an empty rule list. Error: {lastRemoved.Error}");
			lastRemoved.Output.Should().Contain("is removed", because: "the last rule is removed");
		});
		AllureApi.Step("Verify the empty list was saved", () => noRules.Output.Should().Contain(
			"Record permissions: OFF, no default record rules", because: "the empty list was saved and read back"));

		Dictionary<string, object?> ReadArgs() => new() {
			["environment-name"] = arrangeContext.EnvironmentName, ["entity-schema-name"] = objectName
		};
	}

	private static async Task<string> InsertAsync(DataBindingDbArrangeContext arrangeContext, string objectName) {
		string recordId = Guid.NewGuid().ToString();
		CallToolResult inserted = await arrangeContext.Session.CallToolAsync("execute-dataservice-batch",
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = arrangeContext.EnvironmentName,
					["operations"] = new[] {
						new Dictionary<string, object?> {
							["operation"] = "insert",
							["schema-name"] = objectName,
							["record-id"] = recordId,
							["values"] = new Dictionary<string, object?> {
								["UsrName"] = new Dictionary<string, object?> { ["data-value-type"] = 1, ["value"] = "probe" }
							}
						}
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		inserted.IsError.Should().NotBeTrue(because: "the probe record must be inserted");
		return recordId;
	}

	private static async Task<ObjectRightsToolResponse> CallAsync(DataBindingDbArrangeContext arrangeContext,
		string toolName, Dictionary<string, object?> args) {
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> { ["args"] = args },
			arrangeContext.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(because: $"{toolName} must return a structured result, not a protocol error");
		return EntitySchemaStructuredResultParser.Extract<ObjectRightsToolResponse>(callResult);
	}

	private static async Task<string> ReadRecordRightsAsync(DataBindingDbArrangeContext arrangeContext, string objectName,
		string recordId) {
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			"get-record-rights",
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = arrangeContext.EnvironmentName,
					["entity"] = objectName,
					["record-id"] = recordId
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(because: "the record's rights must be readable");
		JsonElement response = EntitySchemaStructuredResultParser.Extract<JsonElement>(callResult);
		response.TryGetProperty("output", out JsonElement output).Should().BeTrue(
			because: $"the record's rights must be read, not an error: {response.GetRawText()}");
		return output.GetString() ?? "";
	}
}
