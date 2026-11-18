using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end proof on a disposable sandbox of the ENG-105680 package-choice check: a page customized in two
/// unlocked packages is refused by <c>update-page</c> and <c>sync-pages</c> until the write names its target,
/// and a chosen target is honored (ENG-106818).
/// </summary>
/// <remarks>
/// The fixture builds its own world and never touches the shared <c>ClioMcp_BlankPageToSave</c> page: a base
/// package holds two fresh pages, and two layer packages that depend on it each get one replacing layer of the
/// first page. Every schema name is unique to the run, so the cleanup deletes every schema of those names
/// wherever the writes put them (the default target is the platform's choice and can lie outside the
/// disposable packages), then the three packages.
/// </remarks>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(PageUpdateTool.ToolName)]
[NonParallelizable]
public sealed class PagePackageChoiceE2ETests : McpContractFixtureBase {

	private const string RequiredMarker = "package-choice-required";
	private const string ReplaceRefusedMarker = PageUpdateCommand.PackageChoiceReplaceRefusedMarker;
	// The platform package that holds BlankPageTemplate: the base package must depend on it to hold a page
	// built from that template.
	private const string TemplatePackageName = "CrtUIPlatform";
	// A replacing schema keeps a deleted name's other copies: at most one per package, three packages plus the
	// default target outside them.
	private const int MaxSchemaDeletesPerName = 5;

	private McpE2ESettings _settings = null!;

	private string EnvironmentName => _settings.Sandbox.EnvironmentName!;

	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		if (!settings.AllowDestructiveMcpTests || string.IsNullOrWhiteSpace(settings.Sandbox.EnvironmentName)) {
			Assert.Ignore("Configure an explicitly disposable sandbox and AllowDestructiveMcpTests=true.");
		}
		_settings = settings;
	}

	[Test]
	[AllureTag(PageUpdateTool.ToolName)]
	[AllureTag(PageSyncTool.ToolName)]
	[AllureTag(PageGetTool.ToolName)]
	[AllureName("update-page and sync-pages refuse a page customized in two packages until the target is chosen")]
	[AllureDescription("Seeds a page with replacing layers in two unlocked disposable packages, then verifies over the real MCP transport: get-page reports packageChoice.required with both layers; update-page without target-package-uid is refused with package-choice-required and saves nothing; sync-pages refuses only that page while the other page of the batch saves; a retry with the default target saves there; an append into the other layer saves into that layer; a replace into it is refused with package-choice-replace-refused. Deletes every schema and package it created.")]
	[Description("ENG-106818: the package-choice-required refusal and the chosen-target retries of update-page and sync-pages, against a page with replacing layers in two unlocked packages on a real stand.")]
	public async Task PackageChoice_Should_Refuse_Untargeted_Writes_And_Honor_The_Chosen_Target() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(15));
		string suffix = Guid.NewGuid().ToString("N")[..10];
		string pageName = "UsrPkgChoice" + suffix;
		string companionName = "UsrPkgChoiceSync" + suffix;
		List<string> packages = [];
		string workDirectory = CreateFixtureDirectory("package-choice");
		try {
			PackageChoiceWorld world = await AllureApi.Step("Arrange: seed a page with layers in two packages",
				() => SeedAsync(context, suffix, pageName, companionName, packages, workDirectory));

			// Act
			PageGetResponse seeded = await GetPageAsync(context, pageName, workDirectory);
			string bodyBeforeRefusal = await File.ReadAllTextAsync(seeded.Files!.BodyFile);
			IReadOnlyList<StoredRow> rowsBeforeRefusals = await ReadStoredRowsAsync(pageName);
			PageUpdateResponse refused = await UpdatePageAsync(context, pageName,
				BuildFragment(pageName, "UsrClioE2EUntargeted"), workDirectory, "append");
			PageSyncResponse synced = await SyncPagesAsync(context, workDirectory,
				(pageName, BuildFragment(pageName, "UsrClioE2ESyncRefused")),
				(companionName, BuildFragment(companionName, "UsrClioE2ESyncSaved")));
			string bodyAfterRefusals = await ReadBodyAsync(context, pageName, workDirectory);
			string companionBody = await ReadBodyAsync(context, companionName, workDirectory);
			IReadOnlyList<StoredRow> rowsAfterRefusals = await ReadStoredRowsAsync(pageName);

			// Assert
			PagePackageChoice choice = seeded.Page!.PackageChoice;
			TestContext.Progress.WriteLine($"Default target {seeded.Page!.PackageChoice?.DefaultTargetPackageUId}; stored rows: "
				+ string.Join("; ", rowsBeforeRefusals));
			AllureApi.Step("Assert the stand holds the base page and one layer in each layer package", () =>
				rowsBeforeRefusals.Select(row => row.PackageUId).Should().BeEquivalentTo(
					new[] { world.BasePackageUId, world.FirstLayerPackageUId, world.SecondLayerPackageUId },
					because: "the seed wrote the base page and exactly one replacing layer into each layer package"));
			AllureApi.Step("Assert get-page asks for a package choice and lists both layers", () => {
				seeded.Success.Should().BeTrue(because: $"get-page must read the seeded page. Error: {seeded.Error}");
				choice.Should().NotBeNull(because: "packageChoice must survive the MCP serialization of get-page");
				choice.Required.Should().BeTrue(
					because: "two unlocked packages customize the page, so a write must name its target");
				LayerPackageUIds(choice).Should().Contain(
					[world.FirstLayerPackageUId, world.SecondLayerPackageUId],
					because: "both replacing layers the test wrote must be listed for the user to choose from");
				choice.Layers.Where(layer => SameUId(layer.PackageUId, world.FirstLayerPackageUId)
						|| SameUId(layer.PackageUId, world.SecondLayerPackageUId))
					.Should().OnlyContain(layer => layer.PackageLocked == false,
						because: "packages created by create-package are editable");
			});
			AllureApi.Step("Assert update-page without a target is refused and saves nothing", () => {
				refused.Success.Should().BeFalse(because: "a write that names no target must not pick a package itself");
				refused.Error.Should().StartWith(RequiredMarker,
					because: "the marker is what the agent keys on to ask the user");
				refused.PackageChoice.Should().NotBeNull(because: "the refusal returns the layers as structured data");
				LayerPackageUIds(refused.PackageChoice!).Should().Contain(
					[world.FirstLayerPackageUId, world.SecondLayerPackageUId],
					because: "the refusal lists the same layers get-page does");
			});
			AllureApi.Step("Assert sync-pages refuses only the page that needs a choice", () => {
				synced.Pages.Should().HaveCount(2, because: "two pages were sent in one batch");
				PageSyncPageResult refusedPage = synced.Pages.Single(page => page.SchemaName == pageName);
				PageSyncPageResult savedPage = synced.Pages.Single(page => page.SchemaName == companionName);
				refusedPage.Success.Should().BeFalse(because: "sync-pages has no target argument, so the page is refused");
				refusedPage.Error.Should().StartWith(RequiredMarker,
					because: "sync-pages relays the same refusal and names update-page as the way to choose");
				savedPage.Success.Should().BeTrue(
					because: $"a page customized in one package only is not held back by the refusal of another. Error: {savedPage.Error}");
				companionBody.Should().Contain("UsrClioE2ESyncSaved", because: "the other page of the batch was saved");
			});
			AllureApi.Step("Assert the refused writes left the page unchanged", () => {
				bodyAfterRefusals.Should().Be(bodyBeforeRefusal,
					because: "neither update-page nor sync-pages may save a page they refused");
				rowsAfterRefusals.Should().BeEquivalentTo(rowsBeforeRefusals,
					because: "no stored schema of the page, in any package, may change on a refused write");
			});

			IReadOnlyList<StoredRow> rowsAfterDefault =
				await AssertDefaultTargetRetrySavesAsync(context, pageName, choice, rowsAfterRefusals, workDirectory);
			await AssertOtherLayerRetriesAsync(context, pageName, choice, rowsAfterDefault, workDirectory);
		}
		finally {
			await CleanUpAsync([pageName, companionName], packages);
		}
	}

	private async Task<IReadOnlyList<StoredRow>> AssertDefaultTargetRetrySavesAsync(ArrangeContext context,
		string pageName, PagePackageChoice choice, IReadOnlyList<StoredRow> rowsBefore, string workDirectory) {
		// Act
		PageUpdateResponse saved = await UpdatePageAsync(context, pageName,
			BuildFragment(pageName, "UsrClioE2EDefaultTarget"), workDirectory, "append",
			choice.DefaultTargetPackageUId);
		string body = await ReadBodyAsync(context, pageName, workDirectory);
		IReadOnlyList<StoredRow> rowsAfter = await ReadStoredRowsAsync(pageName);

		// Assert
		AllureApi.Step("Assert a retry with the default target's packageUId saves there only", () => {
			saved.Success.Should().BeTrue(
				because: $"the default target's packageUId is the user's choice to keep the default. Error: {saved.Error}");
			body.Should().Contain("UsrClioE2EDefaultTarget",
				because: "get-page returns the default target's body, which now holds the change");
			AssertOnlyPackageChanged(rowsBefore, rowsAfter, choice.DefaultTargetPackageUId,
				"the save goes to the default target and to no other package");
		});
		return rowsAfter;
	}

	private async Task AssertOtherLayerRetriesAsync(ArrangeContext context, string pageName,
		PagePackageChoice choice, IReadOnlyList<StoredRow> rowsBefore, string workDirectory) {
		// Arrange
		PagePackageLayer other = choice.Layers.First(layer => !layer.IsDefaultTarget && layer.PackageLocked == false);

		// Act
		PageUpdateResponse appended = await UpdatePageAsync(context, pageName,
			BuildFragment(pageName, "UsrClioE2EOtherLayer"), workDirectory, "append", other.PackageUId);
		PagePackageLayer afterAppend = await ReadLayerAsync(context, pageName, other.PackageUId, workDirectory);
		IReadOnlyList<StoredRow> rowsAfterAppend = await ReadStoredRowsAsync(pageName);
		PageUpdateResponse replaced = await UpdatePageAsync(context, pageName,
			BuildFragment(pageName, "UsrClioE2EReplaceRefused"), workDirectory, "replace", other.PackageUId);
		IReadOnlyList<StoredRow> rowsAfterReplace = await ReadStoredRowsAsync(pageName);

		// Assert
		AllureApi.Step("Assert an append retry into another layer saves into that layer", () => {
			appended.Success.Should().BeTrue(
				because: $"append keeps the chosen layer's own changes, so it is allowed. Error: {appended.Error}");
			afterAppend.Changes.Should().Contain("insert UsrClioE2EOtherLayer",
				because: "the change must land in the chosen layer, not in the default target");
			AssertOnlyPackageChanged(rowsBefore, rowsAfterAppend, other.PackageUId,
				"the append is stored in the chosen layer's package and in no other");
		});
		AllureApi.Step("Assert a replace retry into that layer is refused and saves nothing", () => {
			replaced.Success.Should().BeFalse(
				because: "a replace would overwrite the layer's own changes with the default target's body");
			replaced.Error.Should().StartWith(ReplaceRefusedMarker,
				because: "the marker tells the agent to send only its change with mode append");
			rowsAfterReplace.Should().BeEquivalentTo(rowsAfterAppend,
				because: "no stored schema of the page, in any package, may change on a refused replace");
		});
	}

	private static void AssertOnlyPackageChanged(IReadOnlyList<StoredRow> before, IReadOnlyList<StoredRow> after,
		string changedPackageUId, string because) {
		StoredRow changed = after.Single(row => SameUId(row.PackageUId, changedPackageUId));
		before.Should().NotContain(changed, because: because);
		after.Where(row => !SameUId(row.PackageUId, changedPackageUId)).Should().BeEquivalentTo(
			before.Where(row => !SameUId(row.PackageUId, changedPackageUId)), because: because);
	}

	private static bool SameUId(string left, string right) =>
		string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Reads every stored schema of the page, in every package, straight from <c>SysSchema</c> over OData: an
	/// independent view of what the writes saved, which <c>get-page</c> alone cannot give (it returns the default
	/// target's body only).
	/// </summary>
	private async Task<IReadOnlyList<StoredRow>> ReadStoredRowsAsync(string schemaName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(_settings, [
			"call-service", "--method", "GET", "-e", EnvironmentName, "--service-path",
			$"odata/SysSchema?$filter=Name%20eq%20'{schemaName}'&$select=Checksum,ModifiedOn"
				+ "&$expand=SysPackage($select=UId,Name)"
		]);
		result.ExitCode.Should().Be(0, because: $"SysSchema must be readable over OData. {result.StandardError}");
		string output = result.StandardOutput;
		using JsonDocument document = JsonDocument.Parse(output[output.IndexOf('{')..]);
		return document.RootElement.GetProperty("value").EnumerateArray()
			.Select(row => new StoredRow(
				row.GetProperty("SysPackage").GetProperty("UId").GetString()!.ToLowerInvariant(),
				row.GetProperty("SysPackage").GetProperty("Name").GetString()!,
				row.GetProperty("Checksum").GetString()!,
				row.GetProperty("ModifiedOn").GetString()!))
			.ToList();
	}

	private async Task<PackageChoiceWorld> SeedAsync(ArrangeContext context, string suffix, string pageName,
		string companionName, List<string> packages, string workDirectory) {
		CreatePackageResponse basePackage = await CreatePackageAsync(context, "ClioE2EPcBase" + suffix,
			[TemplatePackageName], packages);
		CreatePackageResponse firstLayer = await CreatePackageAsync(context, "ClioE2EPcLayerA" + suffix,
			[basePackage.PackageName!], packages);
		CreatePackageResponse secondLayer = await CreatePackageAsync(context, "ClioE2EPcLayerB" + suffix,
			[basePackage.PackageName!, firstLayer.PackageName!], packages);
		await CreatePageAsync(context, pageName, basePackage.PackageName!);
		await CreatePageAsync(context, companionName, basePackage.PackageName!);
		foreach ((CreatePackageResponse layer, string element) in new[] {
			(firstLayer, "UsrClioE2ELayerA"), (secondLayer, "UsrClioE2ELayerB")
		}) {
			PageUpdateResponse written = await UpdatePageAsync(context, pageName, BuildFragment(pageName, element),
				workDirectory, "append", layer.PackageUId);
			written.Success.Should().BeTrue(
				because: $"a target package with no layer of the page gets a new replacing layer. Error: {written.Error}");
		}
		return new PackageChoiceWorld(basePackage.PackageUId!.ToLowerInvariant(),
			firstLayer.PackageUId!.ToLowerInvariant(), secondLayer.PackageUId!.ToLowerInvariant());
	}

	private async Task<CreatePackageResponse> CreatePackageAsync(ArrangeContext context, string packageName,
		string[] dependencies, List<string> created) {
		CallToolResult callResult = await context.Session.CallDestructiveAsync(CreatePackageTool.CreatePackageToolName,
			new Dictionary<string, object?> {
				["environment-name"] = EnvironmentName,
				["package-name"] = packageName,
				["description"] = "clio MCP e2e package choice",
				["dependencies"] = dependencies
			},
			context.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(because: "create-package returns a structured result");
		CreatePackageResponse response = EntitySchemaStructuredResultParser.Extract<CreatePackageResponse>(callResult);
		if (response.PackageName is not null) {
			created.Add(response.PackageName);
		}
		response.Success.Should().BeTrue(because: response.Error ?? "a disposable package is created");
		return response;
	}

	private async Task CreatePageAsync(ArrangeContext context, string schemaName, string packageName) {
		CallToolResult callResult = await context.Session.CallToolAsync(PageCreateTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["template"] = "BlankPageTemplate",
					["package-name"] = packageName,
					["environment-name"] = EnvironmentName
				}
			},
			context.CancellationTokenSource.Token);
		PageCreateResponse response = EntitySchemaStructuredResultParser.Extract<PageCreateResponse>(callResult);
		response.Success.Should().BeTrue(because: $"the base page is created in the base package. Error: {response.Error}");
	}

	private async Task<PageGetResponse> GetPageAsync(ArrangeContext context, string schemaName, string workDirectory) {
		CallToolResult callResult = await context.Session.CallToolAsync(PageGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["environment-name"] = EnvironmentName,
					// A fresh folder per read, so a later read never shows the body file of an earlier one.
					["output-directory"] = Directory.CreateDirectory(
						Path.Combine(workDirectory, "get-page-" + Guid.NewGuid().ToString("N")[..8])).FullName
				}
			},
			context.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(because: "get-page returns a structured payload, not a transport error");
		return EntitySchemaStructuredResultParser.Extract<PageGetResponse>(callResult);
	}

	private async Task<PageGetResponse> GetReadablePageAsync(ArrangeContext context, string schemaName,
		string workDirectory) {
		PageGetResponse response = await GetPageAsync(context, schemaName, workDirectory);
		response.Success.Should().BeTrue(because: $"the page must stay readable. Error: {response.Error}");
		return response;
	}

	private async Task<string> ReadBodyAsync(ArrangeContext context, string schemaName, string workDirectory) =>
		await File.ReadAllTextAsync((await GetReadablePageAsync(context, schemaName, workDirectory)).Files!.BodyFile);

	private async Task<PagePackageLayer> ReadLayerAsync(ArrangeContext context, string schemaName,
		string packageUId, string workDirectory) =>
		(await GetReadablePageAsync(context, schemaName, workDirectory)).Page!.PackageChoice.Layers
			.Single(layer => SameUId(layer.PackageUId, packageUId));

	private static IEnumerable<string> LayerPackageUIds(PagePackageChoice choice) =>
		choice.Layers.Select(layer => layer.PackageUId.ToLowerInvariant());

	private async Task<PageUpdateResponse> UpdatePageAsync(ArrangeContext context, string schemaName, string body,
		string workDirectory, string mode, string? targetPackageUId = null) {
		Dictionary<string, object?> args = new() {
			["schema-name"] = schemaName,
			["body"] = body,
			["mode"] = mode,
			["environment-name"] = EnvironmentName,
			["output-directory"] = workDirectory
		};
		if (targetPackageUId is not null) {
			args["target-package-uid"] = targetPackageUId;
		}
		CallToolResult callResult = await context.Session.CallToolAsync(PageUpdateTool.ToolName,
			new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(because: "update-page returns a structured payload, not a transport error");
		return EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);
	}

	private async Task<PageSyncResponse> SyncPagesAsync(ArrangeContext context, string workDirectory,
		params (string SchemaName, string Body)[] pages) {
		CallToolResult callResult = await context.Session.CallToolAsync(PageSyncTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = EnvironmentName,
					["output-directory"] = workDirectory,
					["pages"] = pages.Select(page => new Dictionary<string, object?> {
						["schema-name"] = page.SchemaName,
						["body"] = page.Body
					}).ToArray()
				}
			},
			context.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(because: "sync-pages returns a structured payload, not a transport error");
		return EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(callResult);
	}

	/// <summary>
	/// Deletes every schema with the run's unique names, wherever the writes put them, then the packages in
	/// reverse creation order (a layer package before the base it depends on). Never throws: a failed cleanup is
	/// written to the progress log, as <see cref="BoundedCleanup"/> does for the other sandbox fixtures.
	/// </summary>
	private async Task CleanUpAsync(IEnumerable<string> schemaNames, List<string> packages) {
		foreach (string schemaName in schemaNames) {
			for (int attempt = 0; attempt < MaxSchemaDeletesPerName; attempt++) {
				ClioCliCommandResult? deleted = null;
				string? error = await BoundedCleanup.RunAsync(async token => {
						deleted = await ClioCliCommandRunner.RunAsync(_settings,
							["delete-schema", schemaName, "--remote", "-e", EnvironmentName], cancellationToken: token);
						// A non-zero exit is the normal end (no schema of this name is left), not a cleanup
						// failure, so BoundedCleanup only reports a timeout or an exception here.
						return 0;
					},
					TimeSpan.FromMinutes(2), $"Deleting schema {schemaName}");
				if (error is not null) {
					TestContext.Progress.WriteLine(error);
					break;
				}
				if (deleted is null || deleted.ExitCode != 0) {
					// No schema of this name is left.
					break;
				}
			}
		}
		foreach (string packageName in Enumerable.Reverse(packages)) {
			string? error = await BoundedCleanup.RunAsync(
				async token => (await ClioCliCommandRunner.RunAsync(_settings,
					["delete-pkg-remote", packageName, "-e", EnvironmentName], cancellationToken: token)).ExitCode,
				TimeSpan.FromMinutes(2), $"Deleting package {packageName}");
			if (error is not null) {
				TestContext.Progress.WriteLine(error);
			}
		}
	}

	/// <summary>Builds a diff-form append fragment that inserts one container with the given name.</summary>
	private static string BuildFragment(string schemaName, string elementName) =>
		"define(\"" + schemaName + "\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"" + elementName + "\"," +
		"\"parentName\":\"MainContainer\",\"propertyName\":\"items\",\"index\":0," +
		"\"values\":{\"type\":\"crt.FlexContainer\",\"direction\":\"column\",\"items\":[]}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private sealed record PackageChoiceWorld(string BasePackageUId, string FirstLayerPackageUId,
		string SecondLayerPackageUId);

	/// <summary>One stored schema of the page: its package, checksum and modification time.</summary>
	private sealed record StoredRow(string PackageUId, string PackageName, string Checksum, string ModifiedOn);
}
