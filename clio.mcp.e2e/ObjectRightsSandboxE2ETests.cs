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
/// untouched, the All employees row the server adds when operation permissions are turned on, the last-grant
/// refusal and the disable path — are otherwise tested only against mocks.
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
	[Description("On a real stand: a read grant with include-connected turns operation permissions on for a disposable object and its lookup, the server-added All employees row is reported, record and column administration stay untouched, a last-grant revoke is refused, and the disable opt-in turns permissions off again.")]
	[AllureTag(SetObjectRightsTool.ToolName)]
	[AllureTag(GetObjectRightsTool.ToolName)]
	[AllureName("object-rights read-modify-write round-trips on a real Creatio stand")]
	[AllureDescription("Creates a disposable lookup and an object referencing it, grants All external users read with include-connected, reads it back, checks record/column administration is untouched, then proves the last-grant refusal and the disable-operation-permissions path. The fixture package is deleted in teardown.")]
	public async Task ObjectRights_Should_RoundTrip_On_A_Real_Stand() {
		TeamCityRunGuard.IgnoreIfRunningUnderTeamCityOrGitHubActions(
			"create-entity-schema publishes configuration and starts the global OData rebuild, which makes every "
			+ "concurrent test on the shared stand fail. Run this scenario by hand against a leased sandbox.");
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: true);
		string suffix = System.Guid.NewGuid().ToString("N").Substring(0, 10);
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

		// Act — preview, then grant read to the external audience on the root and its own lookup with the token
		ObjectRightsToolResponse grant = await PreviewThenConfirmAsync(arrangeContext,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["entity-schema-name"] = rootName,
				["grantee"] = ExternalUsers,
				["operations"] = "read",
				["include-connected"] = true
			});

		// Assert — both objects were turned on, and the result names the roles that hold rights afterwards
		grant.Success.Should().BeTrue(because: $"the grant must apply on a real stand. Error: {grant.Error}");
		grant.Output.Should().Contain($"{rootName}: granted [read]", because: "the root result line is reported");
		grant.Output.Should().Contain($"{lookupName}: granted [read]", because: "the connected lookup is granted read");
		grant.Output.Should().Contain("turned ON", because: "both objects were not administered before the grant");

		// Act — read the rights back
		ObjectRightsToolResponse read = await CallRightsAsync(arrangeContext, GetObjectRightsTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["entity-schema-name"] = rootName,
				["include-connected"] = true
			});

		// Assert — the read-back shows the grant on both objects
		read.Success.Should().BeTrue(because: $"the read-back must succeed. Error: {read.Error}");
		read.Output.Should().Contain("All external users", because: "the grantee's row was saved on the stand");
		read.Output.Should().NotContain($"{lookupName}: not administered",
			because: "the fan-out turned operation permissions on for the lookup too");

		// Assert — the save left record and column administration untouched (they were sent as null)
		JsonElement rootProperties = await ReadSchemaPropertiesAsync(arrangeContext, rootName);
		rootProperties.GetProperty("administrated-by-records").GetBoolean().Should().BeFalse(
			because: "the object-rights save must not touch record administration");
		rootProperties.GetProperty("administrated-by-columns").GetBoolean().Should().BeFalse(
			because: "the object-rights save must not touch column administration");

		// Act — remove the grantee, then try to remove the last remaining grant without the opt-in
		ObjectRightsToolResponse revokeExternal = await RevokeAllAsync(arrangeContext, rootName, ExternalUsers, disable: false);
		ObjectRightsToolResponse refused = await RevokeAllAsync(arrangeContext, rootName, AllEmployees, disable: false);

		// Assert — the grantee revoke applies; the last-grant revoke is refused
		revokeExternal.Success.Should().BeTrue(because: $"another role still holds rights. Error: {revokeExternal.Error}");
		refused.Success.Should().BeFalse(because: "removing the last effective grant is refused without the opt-in");
		refused.Error.Should().Contain("LAST effective grant", because: "the refusal names the access widening it avoided");

		// Act — the explicit opt-in turns operation permissions off again
		ObjectRightsToolResponse disabled = await RevokeAllAsync(arrangeContext, rootName, AllEmployees, disable: true);

		// Assert
		disabled.Success.Should().BeTrue(because: $"the caller asked for the widening. Error: {disabled.Error}");
		disabled.Output.Should().Contain("now OFF", because: "the object is available to all internal users again");
	}

	// The MCP write is two-step: a call without confirm returns a preview and a confirmation-code and writes
	// nothing; the confirmed call must carry that token.
	private static async Task<ObjectRightsToolResponse> PreviewThenConfirmAsync(
		DataBindingDbArrangeContext arrangeContext, Dictionary<string, object?> args) {
		ObjectRightsToolResponse preview = await CallRightsAsync(arrangeContext, SetObjectRightsTool.ToolName, args);
		preview.Success.Should().BeTrue(because: $"the preview must succeed. Error: {preview.Error}");
		System.Text.RegularExpressions.Match token = System.Text.RegularExpressions.Regex.Match(
			preview.Output ?? string.Empty, @"confirmation-code: (?<token>[0-9a-f]+)");
		token.Success.Should().BeTrue(because: $"the preview must print a confirmation code. Output: {preview.Output}");
		preview.Output.Should().Contain("PREVIEW — nothing was changed", because: "a preview writes nothing");
		Dictionary<string, object?> confirmed = new(args) {
			["confirm"] = true,
			["confirmation-code"] = token.Groups["token"].Value
		};
		return await CallRightsAsync(arrangeContext, SetObjectRightsTool.ToolName, confirmed);
	}

	private static Task<ObjectRightsToolResponse> RevokeAllAsync(DataBindingDbArrangeContext arrangeContext,
		string schemaName, string grantee, bool disable) =>
		PreviewThenConfirmAsync(arrangeContext, new Dictionary<string, object?> {
			["environment-name"] = arrangeContext.EnvironmentName,
			["entity-schema-name"] = schemaName,
			["grantee"] = grantee,
			["operations"] = "read,create,edit,delete",
			["revoke"] = true,
			["disable-operation-permissions"] = disable
		});

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
