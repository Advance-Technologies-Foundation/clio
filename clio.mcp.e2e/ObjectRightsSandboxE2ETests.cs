using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// Developer-local end-to-end proof of the object-rights read-modify-write against a REAL Creatio: the platform
/// facts the tools' safety rests on — the save envelope, that the untouched record/column collections stay
/// untouched, that the save keeps the All employees row an unadministered object is read with, that a revoke keeps
/// its row, the refusals of unnamed transitions and the disable path — are otherwise tested only against mocks.
/// </summary>
/// <remarks>
/// It publishes two schemas into a fixture-owned package, so it belongs to the destructive LocalOnly sub-tier
/// for the reason recorded on <see cref="DataBindingDbColorSchemaE2ETests"/>: create-entity-schema starts the
/// asynchronous global OData rebuild, which breaks concurrent tests on the same stand. The package — and with it
/// both schemas and their rights — is deleted by the fixture teardown on every outcome.
/// </remarks>
[TestFixture]
[Category("McpE2E.Sandbox")]
[Category("McpE2E.Manual")]
[Category("LocalOnly")]
[Explicit("Publishes schemas and changes their object permissions on the stand; run it by hand against a leased sandbox.")]
// [AllureNUnit] is intentionally omitted, for the reason recorded on EntitySchemaToolE2ETests: the adapter's
// lifecycle hooks deadlock a fixture with many sequential awaited MCP calls, and a hang would stop the
// `await using` teardown that deletes the fixture package. The [Allure*] metadata attributes are unaffected.
[AllureFeature(SetObjectRightsTool.ToolName)]
[NonParallelizable]
public sealed class ObjectRightsSandboxE2ETests : DataBindingDbFixtureBase {

	private const string ExternalUsers = "720b771c-e7a7-4f31-9cfb-52cd21c3739f";
	private const string AllEmployees = "a29a3ba5-4b0d-de11-9a51-005056c00008";

	[Test]
	[Description("On a real stand, one object per call: an unnamed enable is refused and writes nothing; a named enable grants read on a disposable object and, in its own call, on its lookup, keeping All employees; record and column administration stay untouched; a revoke keeps the row; the last granting row is refused without the disable and turned off with it.")]
	[AllureTag(SetObjectRightsTool.ToolName)]
	[AllureTag(GetObjectRightsTool.ToolName)]
	[AllureName("object-rights read-modify-write round-trips on a real Creatio stand")]
	[AllureDescription("Creates a disposable lookup and an object referencing it, shows an unnamed enable is refused, grants All external users read on each object in its own call with enable-operation-permissions, reads them back, checks record/column administration is untouched, revokes the grantee (its row stays), then proves the no-granting-row refusal and the disable-operation-permissions path. The fixture package is deleted in teardown.")]
	public async Task ObjectRights_Should_RoundTrip_On_A_Real_Stand() {
		TeamCityRunGuard.IgnoreIfRunningUnderTeamCityOrGitHubActions(
			"create-entity-schema publishes configuration and starts the global OData rebuild, which makes every "
			+ "concurrent test on the shared stand fail. Run this scenario by hand against a leased sandbox.");
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: true);
		string suffix = Guid.NewGuid().ToString("N").Substring(0, 10);
		string lookupName = $"UsrOrLkp{suffix}";
		string rootName = $"UsrOrRoot{suffix}";

		CommandExecutionActResult lookupResult = await ActCommandAsync(arrangeContext, "create-lookup",
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["package-name"] = arrangeContext.PackageName,
				["schema-name"] = lookupName,
				["title-localizations"] = new Dictionary<string, string> { ["en-US"] = "Object rights e2e lookup" }
			});
		AssertCommandExitCode(lookupResult, 0, "the disposable lookup must exist before the object references it");
		CommandExecutionActResult rootResult = await ActCommandAsync(arrangeContext, CreateEntitySchemaToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["package-name"] = arrangeContext.PackageName,
				["schema-name"] = rootName,
				["title-localizations"] = new Dictionary<string, string> { ["en-US"] = "Object rights e2e root" },
				["columns"] = new[] {
					new Dictionary<string, object?> { ["column-name"] = "UsrName", ["type"] = "Text" },
					new Dictionary<string, object?> {
						["column-name"] = "UsrLookup", ["type"] = "Lookup", ["reference-schema-name"] = lookupName
					}
				}
			});
		AssertCommandExitCode(rootResult, 0, "the disposable root object must exist before its rights are changed");

		Dictionary<string, object?> Args(string schema, string grantee, string operations, bool revoke = false,
			bool enable = false, bool disable = false) => new() {
			["environment-name"] = arrangeContext.EnvironmentName,
			["entity-schema-name"] = schema,
			["grantee"] = grantee,
			["operations"] = operations,
			["revoke"] = revoke,
			["enable-operation-permissions"] = enable,
			["disable-operation-permissions"] = disable
		};
		Dictionary<string, object?> Read(string schema, string? grantee = null, bool includeConnected = false) {
			Dictionary<string, object?> args = new() {
				["environment-name"] = arrangeContext.EnvironmentName,
				["entity-schema-name"] = schema,
				["include-connected"] = includeConnected
			};
			if (grantee is not null) {
				args["grantee"] = grantee;
			}
			return args;
		}

		// Act — a grant that would turn operation permissions on, without naming that transition
		ObjectRightsToolResponse unnamed = await CallRightsAsync(arrangeContext, SetObjectRightsTool.ToolName,
			Args(rootName, ExternalUsers, "read"));
		ObjectRightsToolResponse untouched = await CallRightsAsync(arrangeContext, GetObjectRightsTool.ToolName,
			Read(rootName));

		// Assert — refused, and nothing was written
		unnamed.Success.Should().BeFalse(because: "turning operation permissions on must be named in the arguments");
		unnamed.Error.Should().Contain("enable-operation-permissions", because: "the refusal names the flag to pass");
		untouched.Output.Should().Contain("not administered", because: "a refused call writes nothing");

		// Act — the same grant with the transition named, then the lookup in its own call
		ObjectRightsToolResponse grantRoot = await CallRightsAsync(arrangeContext, SetObjectRightsTool.ToolName,
			Args(rootName, ExternalUsers, "read", enable: true));
		ObjectRightsToolResponse grantLookup = await CallRightsAsync(arrangeContext, SetObjectRightsTool.ToolName,
			Args(lookupName, ExternalUsers, "read", enable: true));

		// Assert — each object was turned on and read back
		grantRoot.Success.Should().BeTrue(because: $"the grant must apply on a real stand. Error: {grantRoot.Error}");
		grantRoot.Output.Should().Contain($"'{rootName}': granted [read]", because: "the result line names the object");
		grantRoot.Output.Should().Contain("turned ON", because: "the root was not administered before the grant");
		grantLookup.Success.Should().BeTrue(because: $"the lookup is granted in its own call. Error: {grantLookup.Error}");

		// Act — read both objects back
		ObjectRightsToolResponse read = await CallRightsAsync(arrangeContext, GetObjectRightsTool.ToolName,
			Read(rootName, includeConnected: true));

		// Assert — the read-back lists the rows in priority order on both objects
		read.Success.Should().BeTrue(because: $"the read-back must succeed. Error: {read.Error}");
		read.Output.Should().Contain("priority order", because: "every listing states the priority rule");
		read.Output.Should().Contain($"[0] All employees ({AllEmployees}): read/create/edit/delete",
			because: "the enabling save stored the All employees row the read synthesized, at position 0");
		read.Output.Should().Contain($"[1] All external users ({ExternalUsers}): read",
			because: "the grantee's row was saved below it, at the lowest priority");
		read.Output.Should().Contain($"{lookupName}: administered by operation permissions",
			because: "the lookup's own call turned operation permissions on for it");

		// Assert — the save left record and column administration untouched (they were sent as null)
		JsonElement rootProperties = await ReadSchemaPropertiesAsync(arrangeContext, rootName);
		rootProperties.GetProperty("administrated-by-records").GetBoolean().Should().BeFalse(
			because: "the object-rights save must not touch record administration");
		rootProperties.GetProperty("administrated-by-columns").GetBoolean().Should().BeFalse(
			because: "the object-rights save must not touch column administration");

		// Act — revoke the grantee: its row must stay, as an explicit deny
		ObjectRightsToolResponse revoke = await CallRightsAsync(arrangeContext, SetObjectRightsTool.ToolName,
			Args(rootName, ExternalUsers, "read", revoke: true));
		ObjectRightsToolResponse revokedRow = await CallRightsAsync(arrangeContext, GetObjectRightsTool.ToolName,
			Read(rootName, grantee: ExternalUsers));

		// Assert
		revoke.Success.Should().BeTrue(because: $"All employees still grants something. Error: {revoke.Error}");
		revokedRow.Output.Should().Contain("All external users",
			because: "a revoke keeps the row; removing it would let a lower row decide");
		revokedRow.Output.Should().Contain($"[1] All external users ({ExternalUsers}): no operations",
			because: "the row is kept at its position with read cleared");

		// Act — take away the last granting row, first without and then with the explicit disable
		ObjectRightsToolResponse lastRow = await CallRightsAsync(arrangeContext, SetObjectRightsTool.ToolName,
			Args(rootName, AllEmployees, "read,create,edit,delete", revoke: true));
		ObjectRightsToolResponse disabled = await CallRightsAsync(arrangeContext, SetObjectRightsTool.ToolName,
			Args(rootName, AllEmployees, "read,create,edit,delete", revoke: true, disable: true));

		// Assert
		lastRow.Success.Should().BeFalse(because: "an administered object is never left with no granting row");
		lastRow.Error.Should().Contain("disable-operation-permissions", because: "the refusal names the explicit way out");
		disabled.Success.Should().BeTrue(because: $"the caller named the transition. Error: {disabled.Error}");
		disabled.Output.Should().Contain("turned OFF", because: "the object is available to all internal users again");
		disabled.Output.Should().NotContain("Differs from the plan",
			because: "the read-back matches the plan: the disable wrote the cleared row and the switch, nothing else");

		// Act — read the object back after the disable, then run the same disable again
		ObjectRightsToolResponse afterDisable = await CallRightsAsync(arrangeContext, GetObjectRightsTool.ToolName,
			Read(rootName));
		ObjectRightsToolResponse disabledAgain = await CallRightsAsync(arrangeContext, SetObjectRightsTool.ToolName,
			Args(rootName, AllEmployees, "read,create,edit,delete", revoke: true, disable: true));

		// Assert — the stored rows are kept for a later re-enable, and the re-run changes nothing
		afterDisable.Output.Should().Contain($"{rootName}: not administered", because: "the switch is off");
		afterDisable.Output.Should().Contain($"[0] All employees ({AllEmployees}): no operations",
			because: "a disable keeps the stored rows: All employees stays, with its operations cleared");
		afterDisable.Output.Should().Contain($"[1] All external users ({ExternalUsers}): no operations",
			because: "the revoked grantee's row is kept at its position too");
		disabledAgain.Success.Should().BeTrue(because: $"a re-run of a call that landed is safe. Error: {disabledAgain.Error}");
		disabledAgain.Output.Should().Contain("(no change)", because: "the switch is already off and the row already cleared");
	}

	private static async Task<ObjectRightsToolResponse> CallRightsAsync(DataBindingDbArrangeContext arrangeContext,
		string toolName, Dictionary<string, object?> args) {
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> { ["args"] = args },
			arrangeContext.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(because: $"{toolName} must return a structured result, not a protocol error");
		return EntitySchemaStructuredResultParser.Extract<ObjectRightsToolResponse>(callResult);
	}

	private static async Task<JsonElement> ReadSchemaPropertiesAsync(DataBindingDbArrangeContext arrangeContext,
		string schemaName) {
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			"get-entity-schema-properties",
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = arrangeContext.EnvironmentName,
					["schema-name"] = schemaName,
					["package-name"] = arrangeContext.PackageName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(because: "the schema properties must be readable after the grant");
		return EntitySchemaStructuredResultParser.Extract<JsonElement>(callResult);
	}
}
