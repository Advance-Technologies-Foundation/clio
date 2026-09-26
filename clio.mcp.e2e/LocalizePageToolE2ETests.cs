using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the <c>localize-page</c> MCP tool (story-page-localization-2, TC-E2E-01..05, and the
/// ADR D8 baseline refresh under <c>output-directory</c>).
/// </summary>
/// <remarks>
/// The write cases run on a page created by the test itself: a culture value written on the shared seeded
/// page could not be removed afterwards, and a save there would move the checksum the conflict-detection
/// fixtures rely on. The report-only case reads the seeded page, because it saves nothing.
/// </remarks>
[TestFixture]
[AllureNUnit]
[AllureFeature(LocalizePageTool.ToolName)]
[NonParallelizable]
public sealed class LocalizePageToolE2ETests : McpContractFixtureBase {
	private const string ToolName = LocalizePageTool.ToolName;
	private const string SeededPage = "ClioMcp_BlankPageToSave";
	private const string PackageName = "Custom";
	private const string LabelKey = "UsrE2eLabel_caption";
	private const string EnglishLabel = "E2E label";
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("get-tool-contract returns the localize-page contract with schema-name and culture as the only required fields.")]
	[AllureTag(ToolName)]
	[AllureName("localize-page contract is served by get-tool-contract")]
	[AllureDescription("Starts the real clio MCP server, requests the localize-page contract through get-tool-contract and checks the required fields and the optional resources/caption fields, so a long-tail caller can discover the tool without it being resident in tools/list.")]
	public async Task LocalizePage_Contract_Should_Be_Served_By_GetToolContract() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult result = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["tool-names"] = new[] { ToolName } }
			},
			context.CancellationTokenSource.Token);
		ToolContractGetResponse response = EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(result);

		// Assert
		response.Success.Should().BeTrue(because: "localize-page has a curated contract");
		ToolContractDefinition contract = response.Tools!.Single();
		contract.InputSchema.Required.Should().BeEquivalentTo(["schema-name", "culture"],
			because: "resources and caption are optional; omitting both is the report-only call");
		contract.InputSchema.Properties.Select(property => property.Name).Should().Contain(["resources", "caption", "output-directory"],
			because: "the translation map and the page title are the two values the tool writes");
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("A report-only call on the seeded page saves nothing and reports coverage over the same key set get-page shows.")]
	[AllureTag(ToolName)]
	[AllureName("localize-page report-only covers the get-page key set")]
	[AllureDescription("Reads the seeded page ClioMcp_BlankPageToSave with get-page, calls localize-page for es-ES without resources or caption, and asserts success, saved:false and coverage.keys equal to the number of resource keys in the get-page bundle.")]
	public async Task LocalizePage_ReportOnly_Should_Cover_GetPage_Keys() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ReachableSandboxEnvironment.ResolveOrIgnoreAsync(
			settings, "localize-page MCP E2E requires a reachable sandbox environment.");
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(5));
		string directory = CreateFixtureDirectory("localize-page-report");
		JsonObject strings = await ReadResourceStringsAsync(context, SeededPage, environmentName, directory);

		// Act
		LocalizePageResponse response = await LocalizeAsync(context, SeededPage, "es-ES", environmentName);

		// Assert
		AllureApi.Step("Report-only call succeeds without saving", () => {
			response.Success.Should().BeTrue(because: $"report-only must succeed on a readable page. Error: {response.Error}");
			response.Saved.Should().BeFalse(because: "a call without resources and caption never saves");
		});
		AllureApi.Step("Coverage counts the get-page key set", () => {
			response.Coverage.Should().NotBeNull(because: "report-only exists to return coverage");
			response.Coverage.Keys.Should().Be(strings.Count,
				because: "coverage is computed over the hierarchy key set get-page merges (ADR OQ-2)");
		});
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("TC-E2E-05, 01, 02, 03, 04 in order on a fresh page: report-only lists the untranslated key; es-ES is written with en-US unchanged; the identical re-run does not save; de-DE keeps es-ES; an absent culture fails with the Languages-section message.")]
	[AllureTag(ToolName)]
	[AllureName("localize-page writes one culture at a time and keeps the others")]
	[AllureDescription("Creates a page from BlankPageTemplate in Custom, registers one own resource key through update-page, then drives localize-page: report-only (TC-E2E-05), es-ES write of the key and the title (TC-E2E-01), identical re-run (TC-E2E-02), de-DE write (TC-E2E-03) and xx-XX (TC-E2E-04), checking the stored values with get-page after each write.")]
	public async Task LocalizePage_Should_Write_One_Culture_And_Keep_The_Others() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false — skipping localize-page writes.");
		}
		string environmentName = await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(
			settings,
			$"localize-page MCP E2E requires the configured sandbox environment '{settings.Sandbox.EnvironmentName}' to be set and reachable.");
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(10));
		string schemaName = "UsrE2eLocalize" + Guid.NewGuid().ToString("N")[..12];
		string directory = CreateFixtureDirectory("localize-page-write");
		try {
			await ArrangePageWithOwnKeyAsync(context, schemaName, environmentName, directory);

			// Act + Assert: TC-E2E-05 report-only before any translation
			LocalizePageResponse report = await LocalizeAsync(context, schemaName, "es-ES", environmentName);
			AllureApi.Step("TC-E2E-05: report-only lists the untranslated own key", () => {
				report.Success.Should().BeTrue(because: $"report-only must succeed. Error: {report.Error}");
				report.Saved.Should().BeFalse(because: "report-only never saves");
				report.Coverage.Missing.Should().Contain(LabelKey,
					because: "the key was registered with an en-US value only, so es-ES is missing");
			});

			// Act + Assert: TC-E2E-01 es-ES write
			LocalizePageResponse spanish = await LocalizeAsync(context, schemaName, "es-ES", environmentName,
				resources: $"{{\"{LabelKey}\":\"Etiqueta E2E\"}}", caption: "Pagina E2E");
			AllureApi.Step("TC-E2E-01: es-ES key and title are written", () => {
				spanish.Success.Should().BeTrue(because: $"es-ES is a SysCulture row on the stand. Error: {spanish.Error}");
				spanish.Saved.Should().BeTrue(because: "a new value was supplied");
				spanish.Written.Should().Contain(LabelKey, because: "the key value in es-ES changed");
				spanish.CaptionOutcome.Should().Be(LocalizePageResponse.CaptionWritten, because: "the page title in es-ES changed");
			});
			JsonObject afterSpanish = await ReadResourceStringsAsync(context, schemaName, environmentName, directory);
			AllureApi.Step("TC-E2E-01: get-page shows es-ES and the unchanged en-US value", () => {
				CultureValue(afterSpanish, LabelKey, "es-ES").Should().Be("Etiqueta E2E",
					because: "the readback must contain the written translation");
				CultureValue(afterSpanish, LabelKey, "en-US").Should().Be(EnglishLabel,
					because: "localize-page must never change the default culture");
			});

			// Act + Assert: TC-E2E-02 identical re-run
			LocalizePageResponse rerun = await LocalizeAsync(context, schemaName, "es-ES", environmentName,
				resources: $"{{\"{LabelKey}\":\"Etiqueta E2E\"}}", caption: "Pagina E2E");
			AllureApi.Step("TC-E2E-02: the identical re-run does not save", () => {
				rerun.Success.Should().BeTrue(because: $"a re-run is safe. Error: {rerun.Error}");
				rerun.Saved.Should().BeFalse(because: "every supplied value already equals the stored one");
				rerun.Unchanged.Should().Contain(LabelKey, because: "the key value was already stored");
				rerun.CaptionOutcome.Should().Be(LocalizePageResponse.CaptionUnchanged, because: "the title was already stored");
			});

			// Act + Assert: TC-E2E-03 de-DE after es-ES
			LocalizePageResponse german = await LocalizeAsync(context, schemaName, "de-DE", environmentName,
				resources: $"{{\"{LabelKey}\":\"E2E-Beschriftung\"}}");
			JsonObject afterGerman = await ReadResourceStringsAsync(context, schemaName, environmentName, directory);
			AllureApi.Step("TC-E2E-03: de-DE is written and es-ES and en-US are kept", () => {
				german.Success.Should().BeTrue(because: $"de-DE is a SysCulture row on the stand. Error: {german.Error}");
				CultureValue(afterGerman, LabelKey, "de-DE").Should().Be("E2E-Beschriftung",
					because: "the readback must contain the German translation");
				CultureValue(afterGerman, LabelKey, "es-ES").Should().Be("Etiqueta E2E",
					because: "adding a second culture must keep the first one");
				CultureValue(afterGerman, LabelKey, "en-US").Should().Be(EnglishLabel,
					because: "the default culture must stay unchanged");
			});

			// Act + Assert: TC-E2E-04 absent culture
			LocalizePageResponse absent = await LocalizeAsync(context, schemaName, "xx-XX", environmentName,
				resources: $"{{\"{LabelKey}\":\"xx\"}}");
			JsonObject afterAbsent = await ReadResourceStringsAsync(context, schemaName, environmentName, directory);
			AllureApi.Step("TC-E2E-04: an absent culture fails with the Languages-section message and stores nothing", () => {
				absent.Success.Should().BeFalse(because: "the platform would drop a culture that is not a SysCulture row");
				absent.Error.Should().Contain("Languages section", because: "the agent must be told where to add the culture");
				afterAbsent[LabelKey]!.ToJsonString().Should().Be(afterGerman[LabelKey]!.ToJsonString(),
					because: "a refused call must not change any stored value");
			});

			// Act + Assert: fix D8 - localize-page with get-page's output-directory refreshes the baseline, so the
			// caller's own next update-page is not refused as an external modification.
			PageGetResponse baselinePage = await GetPageAsync(context, schemaName, environmentName, directory);
			baselinePage.Success.Should().BeTrue(because: $"get-page writes the baseline. Error: {baselinePage.Error}");
			string unchangedBody = await File.ReadAllTextAsync(baselinePage.Files.BodyFile);
			LocalizePageResponse withAnchor = await LocalizeAsync(context, schemaName, "es-ES", environmentName,
				resources: $"{{\"{LabelKey}\":\"Etiqueta E2E v2\"}}", outputDirectory: directory);
			CallToolResult followUp = await context.Session.CallToolAsync(PageUpdateTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["schema-name"] = schemaName,
						["body"] = unchangedBody,
						["environment-name"] = environmentName,
						["output-directory"] = directory
					}
				}, context.CancellationTokenSource.Token);
			PageUpdateResponse followUpResponse = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(followUp);
			AllureApi.Step("D8: update-page after localize-page with the same output-directory is not a conflict", () => {
				withAnchor.Success.Should().BeTrue(because: $"es-ES is writable. Error: {withAnchor.Error}");
				withAnchor.Saved.Should().BeTrue(because: "a new es-ES value was supplied");
				followUpResponse.Success.Should().BeTrue(
					because: $"localize-page refreshed the baseline get-page wrote under output-directory. Error: {followUpResponse.Error}");
				(followUpResponse.Error ?? string.Empty).Should().NotContain("modified outside",
					because: "the only change since get-page is the caller's own localize-page save");
			});
		} finally {
			// Every run creates a new UsrE2eLocalize* page; remove it so the stand does not accumulate them.
			using CancellationTokenSource cleanupCts = new(TimeSpan.FromMinutes(2));
			await ClioCliCommandRunner.RunAsync(
				settings,
				["delete-schema", schemaName, "--remote", "-e", environmentName],
				cancellationToken: cleanupCts.Token);
		}
	}

	private async Task ArrangePageWithOwnKeyAsync(
		ArrangeContext context, string schemaName, string environmentName, string directory) {
		await AllureApi.Step("Arrange: create a fresh page and register one own resource key", async () => {
			CallToolResult created = await context.Session.CallToolAsync(PageCreateTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["schema-name"] = schemaName,
						["template"] = "BlankPageTemplate",
						["package-name"] = PackageName,
						["environment-name"] = environmentName
					}
				}, context.CancellationTokenSource.Token);
			PageCreateResponse createResponse = EntitySchemaStructuredResultParser.Extract<PageCreateResponse>(created);
			createResponse.Success.Should().BeTrue(
				because: $"a fresh page isolates this test from other fixtures. Error: {createResponse.Error}");

			PageGetResponse original = await GetPageAsync(context, schemaName, environmentName, directory);
			original.Success.Should().BeTrue(because: $"the new page must be readable. Error: {original.Error}");
			string originalBody = await File.ReadAllTextAsync(original.Files.BodyFile);
			string labelDiff = "[{\"operation\":\"insert\",\"name\":\"UsrE2eLabel\",\"parentName\":\"MainContainer\"," +
				"\"propertyName\":\"items\",\"index\":0,\"values\":{\"type\":\"crt.Label\"," +
				"\"caption\":\"#ResourceString(" + LabelKey + ")#\"}}]";
			string body = Regex.Replace(originalBody,
				@"/\*\*SCHEMA_VIEW_CONFIG_DIFF\*/[\s\S]*?/\*\*SCHEMA_VIEW_CONFIG_DIFF\*/",
				"/**SCHEMA_VIEW_CONFIG_DIFF*/" + labelDiff + "/**SCHEMA_VIEW_CONFIG_DIFF*/",
				RegexOptions.None, RegexTimeout);
			body.Should().NotBe(originalBody, because: "the arrange body must add the label that uses the key");

			CallToolResult updated = await context.Session.CallToolAsync(PageUpdateTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["schema-name"] = schemaName,
						["body"] = body,
						["resources"] = $"{{\"{LabelKey}\":\"{EnglishLabel}\"}}",
						["environment-name"] = environmentName,
						["output-directory"] = directory
					}
				}, context.CancellationTokenSource.Token);
			PageUpdateResponse updateResponse = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(updated);
			updateResponse.Success.Should().BeTrue(
				because: $"update-page must register the own key the test translates. Error: {updateResponse.Error}");
		});
	}

	private static async Task<LocalizePageResponse> LocalizeAsync(
		ArrangeContext context, string schemaName, string culture, string environmentName,
		string? resources = null, string? caption = null, string? outputDirectory = null) {
		Dictionary<string, object?> args = new() {
			["schema-name"] = schemaName,
			["culture"] = culture,
			["environment-name"] = environmentName
		};
		if (resources is not null) {
			args["resources"] = resources;
		}
		if (caption is not null) {
			args["caption"] = caption;
		}
		if (outputDirectory is not null) {
			args["output-directory"] = outputDirectory;
		}
		CallToolResult result = await context.Session.CallToolAsync(
			ToolName, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
		result.IsError.Should().NotBeTrue(
			because: "localize-page failures must stay inside the structured response, not surface as MCP errors");
		return EntitySchemaStructuredResultParser.Extract<LocalizePageResponse>(result);
	}

	private static async Task<PageGetResponse> GetPageAsync(
		ArrangeContext context, string schemaName, string environmentName, string directory) {
		CallToolResult result = await context.Session.CallToolAsync(
			PageGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["environment-name"] = environmentName,
					["output-directory"] = directory
				}
			},
			context.CancellationTokenSource.Token);
		result.IsError.Should().NotBeTrue(because: "get-page must return a structured payload");
		return EntitySchemaStructuredResultParser.Extract<PageGetResponse>(result);
	}

	// get-page writes the merged bundle to disk; its resource strings live under resources.strings,
	// keyed by resource key and then by culture (measured on stand eng90576).
	private static async Task<JsonObject> ReadResourceStringsAsync(
		ArrangeContext context, string schemaName, string environmentName, string directory) {
		PageGetResponse page = await GetPageAsync(context, schemaName, environmentName, directory);
		page.Success.Should().BeTrue(because: $"get-page is the readback source. Error: {page.Error}");
		JsonNode bundle = JsonNode.Parse(await File.ReadAllTextAsync(page.Files.BundleFile))!;
		JsonObject? strings = bundle["resources"]?["strings"]?.AsObject();
		strings.Should().NotBeNull(because: "the bundle must carry resources.strings for the readback to mean anything");
		return strings!;
	}

	private static string? CultureValue(JsonObject strings, string key, string culture) =>
		strings[key]?[culture]?.GetValue<string>();
}
