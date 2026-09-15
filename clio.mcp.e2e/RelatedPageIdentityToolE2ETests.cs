using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>Proves related-page identity and persistence on an explicitly selected disposable Marketing sandbox.</summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[Category("McpE2E.Manual")]
[Category("LocalOnly")]
[Explicit("Clears and restores BulkEmail bindings and rebuilds static configuration; requires an exclusive Marketing sandbox.")]
[NonParallelizable]
// [AllureNUnit] is intentionally omitted. Its lifecycle hooks can deadlock fixtures with many sequential awaits.
[AllureFeature(GetRelatedPageAddonTool.ToolName)]
public sealed class RelatedPageIdentityToolE2ETests {
	[TestCase("web")]
	[TestCase("mobile")]
	[Description("Keeps root identity stable across inherited/own-package reads, clear, restore, and missing-object errors.")]
	[AllureTag(GetRelatedPageAddonTool.ToolName)]
	[AllureTag(CreateRelatedPageAddonTool.ToolName)]
	[AllureName("Related-page responses retain the persisted entity identity")]
	[AllureDescription("Uses a SysSchema base-row oracle, exercises real MCP read/write, and restores the original semantic page set.")]
	public async Task RelatedPages_ShouldKeepPersistedIdentity_WhenDesignerUsesReplacement(string schemaType) {
		// Arrange
		TeamCityRunGuard.IgnoreIfRunningUnderTeamCityOrGitHubActions("Related-page identity requires a disposable local sandbox.");
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E__AllowDestructiveMcpTests=true for an exclusive disposable sandbox.");
		}
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(5));
		var root = await AllureApi.Step("Read persisted base identity independently", () =>
			RelatedPageIdentityReadback.ReadRootAsync(settings, "BulkEmail", deadline.Token));
		string expected = root["UId"]!.GetValue<string>();
		await using McpServerSession session = await McpServerSession.StartAsync(settings, deadline.Token);
		var args = new Dictionary<string, object?> {
			["environment-name"] = settings.Sandbox.EnvironmentName,
			["entity-schema-name"] = "BulkEmail", ["package-name"] = "Custom", ["schema-type"] = schemaType
		};
		var original = await ReadAsync(session, args, deadline.Token);
		original.Success.Should().BeTrue(because: $"the existing binding must be captured before a write: {original.Error}");
		original.Pages.Should().OnlyContain(page => string.IsNullOrWhiteSpace(page.Role)
			|| page.RoleName == "All employees" || page.RoleName == "All external users",
			because: "the fixture must be able to restore every original role before making any mutation");
		// Use an existing page reference for a metadata persistence probe, including when mobile starts empty.
		// This does not test page rendering; the disposable binding is restored below.
		var web = schemaType == "web" ? original : await ReadAsync(session,
			new Dictionary<string, object?>(args) { ["schema-type"] = "web" }, deadline.Token);
		web.Success.Should().BeTrue(because: "the probe needs an existing page reference");
		web.Pages.Should().NotBeEmpty(because: "the Marketing sandbox must contain a seeded BulkEmail web page");
		string probePageUId = web.Pages[0].PageSchemaUId;

		// Act / Assert: each stage has a separate result so a success envelope cannot hide a failed readback.
		await AllureApi.Step("Repeated inherited-package reads equal the persisted root", async () => {
			var repeated = await ReadAsync(session, args, deadline.Token);
			repeated.Success.Should().BeTrue(because: "the unchanged read must succeed");
			new[] { original.EntitySchemaUId, repeated.EntitySchemaUId }.Should().OnlyContain(
				id => id == expected, because: "temporary designer IDs must never become the public identity");
		});
		await AllureApi.Step("Own-package read uses the same root", async () => {
			var ownArgs = new Dictionary<string, object?>(args) { ["package-name"] = root["PackageName"]!.GetValue<string>() };
			var own = await ReadAsync(session, ownArgs, deadline.Token);
			own.Success.Should().BeTrue(because: "the entity's own package must resolve");
			own.EntitySchemaUId.Should().Be(expected, because: "package context does not change the base identity");
		});
		try {
			await AllureApi.Step("Write and independently read a nonempty page set", async () => {
				var seed = new Dictionary<string, object?>(args) {
					["pages"] = new[] { new Dictionary<string, object?> {
						["page-schema-uid"] = probePageUId, ["is-default"] = true, ["is-add"] = true
					} }
				};
				var seeded = await WriteAsync(session, seed, deadline.Token);
				seeded.Success.Should().BeTrue(because: $"a nonempty binding must be saved: {seeded.Error}");
				seeded.EntitySchemaUId.Should().Be(expected, because: "a nonempty write must report the persisted target");
				var persisted = await ReadAsync(session, args, deadline.Token);
				persisted.Success.Should().BeTrue(because: "the saved metadata must survive another request");
				persisted.Pages.Should().ContainSingle(because: "a no-op save must not pass on an empty or multi-page baseline");
				persisted.Pages[0].PageSchemaUId.Should().Be(probePageUId, because: "the requested page reference must persist");
				persisted.Pages[0].IsDefault.Should().BeTrue(because: "the default flag must persist");
				persisted.Pages[0].IsAdd.Should().BeTrue(because: "the add flag must persist");
			});
			await AllureApi.Step("Clear bindings and verify persisted empty set", async () => {
				var clear = new Dictionary<string, object?>(args) { ["pages"] = Array.Empty<object>() };
				var written = await WriteAsync(session, clear, deadline.Token);
				written.Success.Should().BeTrue(because: $"the clear must save successfully: {written.Error}");
				written.EntitySchemaUId.Should().Be(expected, because: "create reports the same resolved identity as get");
				var cleared = await ReadAsync(session, args, deadline.Token);
				cleared.Success.Should().BeTrue(because: "the persisted clear must be readable");
				cleared.Pages.Should().BeEmpty(because: "the empty set must actually persist");
				cleared.EntitySchemaUId.Should().Be(expected, because: "clearing pages cannot change entity identity");
			});
		} finally {
			// A separate deadline lets restoration run even when the assertion deadline expired.
			using CancellationTokenSource restoreDeadline = new(TimeSpan.FromMinutes(3));
			var restore = new Dictionary<string, object?>(args) {
				["type-column-uid"] = original.TypeColumnUId,
				["pages"] = original.Pages.Select(page => new Dictionary<string, object?> {
					["page-schema-uid"] = page.PageSchemaUId, ["is-default"] = page.IsDefault,
					["is-add"] = page.IsAdd, ["is-ssp-default"] = page.IsSspDefault,
					["role"] = page.Role, ["type-column-value"] = page.TypeColumnValue
				}).ToArray()
			};
			await AllureApi.Step("Restore and verify original binding semantics", async () => {
				var restored = await WriteAsync(session, restore, restoreDeadline.Token);
				restored.Success.Should().BeTrue(because: $"the original configuration must be restored: {restored.Error}");
				restored.EntitySchemaUId.Should().Be(expected, because: "restore is another create using the same root");
				var after = await ReadAsync(session, args, restoreDeadline.Token);
				after.Success.Should().BeTrue(because: "the restoration must survive readback");
				after.Pages.Should().BeEquivalentTo(original.Pages, because: "the reporting fix must preserve page binding behavior");
				after.TypeColumnUId.Should().Be(original.TypeColumnUId, because: "restoration must preserve the type discriminator");
			});
		}
		await AllureApi.Step("Missing object fails without changing the real binding", async () => {
			var missing = new Dictionary<string, object?>(args) { ["entity-schema-name"] = "UsrMissing" + Guid.NewGuid().ToString("N") };
			var failed = await ReadAsync(session, missing, deadline.Token);
			failed.Success.Should().BeFalse(because: "a missing entity must not resolve to a parent's binding");
			failed.Error.Should().Contain("not found", because: "the error must explain the missing object");
			var unchanged = await ReadAsync(session, args, deadline.Token);
			unchanged.Pages.Should().BeEquivalentTo(original.Pages, because: "the failed lookup must not affect an existing binding");
		});
	}

	private static async Task<GetRelatedPageAddonResponse> ReadAsync(McpServerSession session, Dictionary<string, object?> args, CancellationToken token) {
		var result = await session.CallToolAsync(GetRelatedPageAddonTool.ToolName,
			new Dictionary<string, object?> { ["args"] = args }, token);
		result.IsError.Should().NotBeTrue(because: "the read must use the structured command result");
		return EntitySchemaStructuredResultParser.Extract<GetRelatedPageAddonResponse>(result);
	}

	private static async Task<CreateRelatedPageAddonResponse> WriteAsync(McpServerSession session, Dictionary<string, object?> args, CancellationToken token) {
		var result = await session.CallToolAsync(CreateRelatedPageAddonTool.ToolName,
			new Dictionary<string, object?> { ["args"] = args }, token);
		result.IsError.Should().NotBeTrue(because: "the write must use the structured command result");
		return EntitySchemaStructuredResultParser.Extract<CreateRelatedPageAddonResponse>(result);
	}
}
