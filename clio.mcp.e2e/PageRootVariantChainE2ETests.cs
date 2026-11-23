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
/// End-to-end proof on a disposable sandbox that <c>update-page</c> reads a replaced page's chain from its root
/// (base) variant and so agrees with <c>get-page</c> (ENG-106817).
/// </summary>
/// <remarks>
/// <para>
/// The page name has one <c>SysSchema</c> row per package that holds a variant, and the name lookup can return any
/// of them. <c>update-page</c> now asks for every row with <c>ExtendParent</c>, reads the chain once from the row
/// marked <c>ExtendParent = false</c>, and runs its package-choice check on that same chain. This fixture pins the
/// two facts that only a real stand can show: the stand marks exactly the base variant with
/// <c>ExtendParent = false</c>, and an untargeted write decides the same way <c>get-page</c> reports.
/// </para>
/// <para>
/// The fixture builds its own world: a base package holding a fresh page, and a layer package that depends on it
/// with one replacing layer of that page. Every name is unique to the run; the cleanup deletes every schema of the
/// page name wherever the writes put it, then both packages.
/// </para>
/// </remarks>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(PageUpdateTool.ToolName)]
[NonParallelizable]
public sealed class PageRootVariantChainE2ETests : McpContractFixtureBase {

	private const string RequiredMarker = "package-choice-required";
	// The platform package that holds BlankPageTemplate: the base package must depend on it to hold a page built
	// from that template.
	private const string TemplatePackageName = "CrtUIPlatform";
	// A deleted name's other copies stay: at most one per package, two packages plus the default target.
	private const int MaxSchemaDeletesPerName = 4;

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
	[AllureTag(PageGetTool.ToolName)]
	[AllureName("update-page reads a replaced page from its root variant and decides as get-page reports")]
	[AllureDescription("Seeds a page in a base package with one replacing layer in a second package, then verifies over the real MCP transport: SysSchema marks exactly the base variant with ExtendParent = false; an untargeted update-page either refuses with package-choice-required and the layers get-page lists, or, when get-page needs no choice, saves into the default target only. Deletes every schema and package it created.")]
	[Description("ENG-106817: on a real stand, the root variant update-page reads the chain from is the ExtendParent = false row, and the untargeted write agrees with get-page's packageChoice.")]
	public async Task UpdatePage_Should_Read_A_Replaced_Page_From_Its_Root_And_Agree_With_GetPage() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(10));
		string suffix = Guid.NewGuid().ToString("N")[..10];
		string pageName = "UsrRootChain" + suffix;
		List<string> packages = [];
		string workDirectory = CreateFixtureDirectory("root-variant-chain");
		try {
			(string basePackageUId, string layerPackageUId) = await AllureApi.Step(
				"Arrange: seed a page with a replacing layer in a second package",
				() => SeedAsync(context, suffix, pageName, packages, workDirectory));

			// Act
			IReadOnlyList<VariantRow> variants = await AllureApi.Step(
				"Act: read the stored variants of the page from SysSchema", () => ReadVariantRowsAsync(pageName));
			PageGetResponse seeded = await AllureApi.Step(
				"Act: read the page with get-page", () => GetPageAsync(context, pageName, workDirectory));
			PageUpdateResponse written = await AllureApi.Step(
				"Act: write the page with update-page, naming no target package",
				() => UpdatePageAsync(context, pageName, BuildFragment(pageName, "UsrClioE2ERootChain"), workDirectory));
			IReadOnlyList<VariantRow> variantsAfter = await AllureApi.Step(
				"Act: read the stored variants again after the write", () => ReadVariantRowsAsync(pageName));

			// Assert
			TestContext.Progress.WriteLine("Stored variants: " + string.Join("; ", variants));
			AllureApi.Step("Assert the base variant is stored with ExtendParent = false", () =>
				variants.Should().Contain(row => row.PackageUId == basePackageUId && !row.ExtendParent,
					because: "the page created in the base package is the variant update-page reads the chain from"));
			AllureApi.Step("Assert the layer variant is stored with ExtendParent = true", () =>
				variants.Should().Contain(row => row.PackageUId == layerPackageUId && row.ExtendParent,
					because: "the layer written into the second package replaces the base variant"));
			AllureApi.Step("Assert exactly one variant is marked as the root", () =>
				variants.Count(row => !row.ExtendParent).Should().Be(1,
					because: "update-page takes the one ExtendParent = false row as the root variant"));
			AllureApi.Step("Assert get-page read the seeded page", () =>
				seeded.Success.Should().BeTrue(because: $"get-page must read the seeded page. Error: {seeded.Error}"));
			PagePackageChoice choice = seeded.Page!.PackageChoice;
			AllureApi.Step("Assert get-page reports packageChoice", () =>
				choice.Should().NotBeNull(because: "packageChoice must survive the MCP serialization of get-page"));
			if (choice.Required) {
				AllureApi.Step("Assert the untargeted write is refused", () =>
					written.Success.Should().BeFalse(because: "get-page asks for a choice, so update-page must too"));
				AllureApi.Step("Assert the refusal is package-choice-required", () =>
					written.Error.Should().StartWith(RequiredMarker,
						because: "both commands read the same root-variant chain"));
				AllureApi.Step("Assert the refusal lists the layers get-page lists", () =>
					written.PackageChoice!.Layers.Select(layer => layer.PackageUId.ToLowerInvariant())
						.Should().BeEquivalentTo(choice.Layers.Select(layer => layer.PackageUId.ToLowerInvariant()),
							because: "update-page and get-page must list the same layers for the same page"));
				AllureApi.Step("Assert the refused write saved nothing", () =>
					variantsAfter.Should().BeEquivalentTo(variants, because: "a refused write saves nothing"));
			}
			else {
				AllureApi.Step("Assert the untargeted write succeeded", () =>
					written.Success.Should().BeTrue(
						because: $"get-page needs no choice, so update-page must not refuse. Error: {written.Error}"));
				AllureApi.Step("Assert the default target variant changed", () => {
					VariantRow changed = variantsAfter.Single(row => SameUId(row.PackageUId, choice.DefaultTargetPackageUId));
					variants.Should().NotContain(changed, because: "the change is stored in the default target");
				});
				AllureApi.Step("Assert no other variant changed", () =>
					variantsAfter.Where(row => !SameUId(row.PackageUId, choice.DefaultTargetPackageUId))
						.Should().BeEquivalentTo(
							variants.Where(row => !SameUId(row.PackageUId, choice.DefaultTargetPackageUId)),
							because: "no other variant of the page may change"));
			}
		}
		finally {
			await CleanUpAsync(pageName, packages);
		}
	}

	private static bool SameUId(string left, string right) =>
		string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Reads every stored variant of the page straight from <c>SysSchema</c> over OData: its package, its
	/// <c>ExtendParent</c> flag and its checksum, an independent view of what the name lookup sees.
	/// </summary>
	private async Task<IReadOnlyList<VariantRow>> ReadVariantRowsAsync(string schemaName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(_settings, [
			"call-service", "--method", "GET", "-e", EnvironmentName, "--service-path",
			$"odata/SysSchema?$filter=Name%20eq%20'{schemaName}'&$select=ExtendParent,Checksum,ModifiedOn"
				+ "&$expand=SysPackage($select=UId)"
		]);
		result.ExitCode.Should().Be(0, because: $"SysSchema must be readable over OData. {result.StandardError}");
		string output = result.StandardOutput;
		using JsonDocument document = JsonDocument.Parse(output[output.IndexOf('{')..]);
		return document.RootElement.GetProperty("value").EnumerateArray()
			.Select(row => new VariantRow(
				row.GetProperty("SysPackage").GetProperty("UId").GetString()!.ToLowerInvariant(),
				row.GetProperty("ExtendParent").GetBoolean(),
				row.GetProperty("Checksum").GetString()!,
				row.GetProperty("ModifiedOn").GetString()!))
			.ToList();
	}

	private async Task<(string BasePackageUId, string LayerPackageUId)> SeedAsync(ArrangeContext context,
		string suffix, string pageName, List<string> packages, string workDirectory) {
		CreatePackageResponse basePackage = await CreatePackageAsync(context, "ClioE2ERcBase" + suffix,
			[TemplatePackageName], packages);
		CreatePackageResponse layerPackage = await CreatePackageAsync(context, "ClioE2ERcLayer" + suffix,
			[basePackage.PackageName!], packages);
		await CreatePageAsync(context, pageName, basePackage.PackageName!);
		PageUpdateResponse layered = await UpdatePageAsync(context, pageName,
			BuildFragment(pageName, "UsrClioE2ERootChainLayer"), workDirectory, layerPackage.PackageUId);
		layered.Success.Should().BeTrue(
			because: $"a target package with no layer of the page gets a new replacing layer. Error: {layered.Error}");
		return (basePackage.PackageUId!.ToLowerInvariant(), layerPackage.PackageUId!.ToLowerInvariant());
	}

	private async Task<CreatePackageResponse> CreatePackageAsync(ArrangeContext context, string packageName,
		string[] dependencies, List<string> created) {
		CallToolResult callResult = await context.Session.CallDestructiveAsync(CreatePackageTool.CreatePackageToolName,
			new Dictionary<string, object?> {
				["environment-name"] = EnvironmentName,
				["package-name"] = packageName,
				["description"] = "clio MCP e2e root variant chain",
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
					["output-directory"] = Directory.CreateDirectory(
						Path.Combine(workDirectory, "get-page-" + Guid.NewGuid().ToString("N")[..8])).FullName
				}
			},
			context.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(because: "get-page returns a structured payload, not a transport error");
		return EntitySchemaStructuredResultParser.Extract<PageGetResponse>(callResult);
	}

	private async Task<PageUpdateResponse> UpdatePageAsync(ArrangeContext context, string schemaName, string body,
		string workDirectory, string? targetPackageUId = null) {
		Dictionary<string, object?> args = new() {
			["schema-name"] = schemaName,
			["body"] = body,
			["mode"] = "append",
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

	/// <summary>
	/// Deletes every schema of the page name, wherever the writes put it, then the packages in reverse creation
	/// order. Never throws: a failed cleanup is written to the progress log, as <see cref="BoundedCleanup"/> does
	/// for the other sandbox fixtures.
	/// </summary>
	private async Task CleanUpAsync(string schemaName, List<string> packages) {
		for (int attempt = 0; attempt < MaxSchemaDeletesPerName; attempt++) {
			ClioCliCommandResult? deleted = null;
			string? error = await BoundedCleanup.RunAsync(async token => {
					deleted = await ClioCliCommandRunner.RunAsync(_settings,
						["delete-schema", schemaName, "--remote", "-e", EnvironmentName], cancellationToken: token);
					// A non-zero exit is the normal end (no schema of this name is left), not a cleanup failure.
					return 0;
				},
				TimeSpan.FromMinutes(2), $"Deleting schema {schemaName}");
			if (error is not null) {
				TestContext.Progress.WriteLine(error);
				break;
			}
			if (deleted is null || deleted.ExitCode != 0) {
				break;
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

	/// <summary>One stored variant of the page: its package, its <c>ExtendParent</c> flag and its checksum.</summary>
	private sealed record VariantRow(string PackageUId, bool ExtendParent, string Checksum, string ModifiedOn);
}
