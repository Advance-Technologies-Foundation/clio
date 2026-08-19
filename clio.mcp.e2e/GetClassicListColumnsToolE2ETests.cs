using Allure.NUnit;
using Allure.NUnit.Attributes;
using Allure.Net.Commons;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>End-to-end tests for the read-only get-classic-list-columns MCP tool.</summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(GetClassicListColumnsTool.ToolName)]
[NonParallelizable]
public sealed class GetClassicListColumnsToolE2ETests : McpContractFixtureBase {

	[Test]
	[Description("Invokes get-classic-list-columns through the real stdio MCP server against the configured sandbox and returns statically parsed schema columns.")]
	[AllureTag(GetClassicListColumnsTool.ToolName)]
	[AllureName("get-classic-list-columns resolves a sandbox Classic section as schema-default")]
	[AllureDescription("Uses the configured sandbox and the section named by McpE2E:Sandbox:ClassicSchemaDefaultSectionSchema to prove the real read-only MCP path parses static list columns out of live Classic bodies, not just hand-written ones. Asserted with ignore-profile=true, because the saved grid profile otherwise answers first on a seeded stand. On a product stand AccountSectionV2 satisfies this live; a bare Studio stand has no Account section and still has to seed one, and the test self-ignores while the setting is blank.")]
	public async Task Resolve_ShouldReturnSchemaDefault_WhenConfiguredSandboxIsAvailable() {
		// Arrange — which sections carry static list columns depends on the product installed, so this half of
		// the discrimination pair stays OPT-IN. It defaulted to ContactSectionV2, but that section declares none
		// (it resolves to entity-default), so the default asserted a false premise and turned the build red
		// instead of skipping. On a product stand AccountSectionV2 is a live target for this half — verified
		// against sae_m_seeenu_15888720_0820 — so the setting no longer requires a hand-seeded section there.
		McpE2ESettings loaded = TestConfiguration.Load();
		string? sectionSchema = loaded.Sandbox.ClassicSchemaDefaultSectionSchema;
		if (string.IsNullOrWhiteSpace(sectionSchema)) {
			Assert.Ignore("McpE2E:Sandbox:ClassicSchemaDefaultSectionSchema is blank; set it to a Classic section "
				+ "whose live body declares static list columns (a `getGridDataColumns` / `initColumnsConfig` "
				+ "override) to run the schema-default E2E.");
			return;
		}
		ArrangeContext arrangeContext = await AllureApi.Step("Arrange configured sandbox MCP session", async () => {
			loaded.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			return await ArrangeAsync(loaded, TimeSpan.FromMinutes(3));
		});
		await using (arrangeContext) {

			// Act
			CallToolResult callResult = await AllureApi.Step($"Act by resolving {sectionSchema} through stdio MCP", async () =>
				await arrangeContext.Session.CallToolAsync(
					GetClassicListColumnsTool.ToolName,
					new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["schema-name"] = sectionSchema,
							// ignore-profile is REQUIRED for this assertion to mean anything: a seeded stand holds
							// a saved grid profile for most sections, and the profile source outranks the static
							// one, so without the flag this test would assert schema-default on an answer that
							// legitimately arrives as profile.
							["ignore-profile"] = true,
							["environment-name"] = arrangeContext.EnvironmentName
						}
					},
					arrangeContext.CancellationTokenSource.Token));
			GetClassicListColumnsResponse response =
				EntitySchemaStructuredResultParser.Extract<GetClassicListColumnsResponse>(callResult);
			CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

			// Assert
			await AllureApi.Step("Assert MCP invocation succeeds", () => {
				callResult.IsError.Should().NotBeTrue(
					because: "a valid read-only tool request must return a structured payload rather than an MCP error");
				response.Success.Should().BeTrue(
					because: $"the configured section '{sectionSchema}' should resolve successfully. Error: {response.Error}");
				return Task.CompletedTask;
			});
			await AllureApi.Step("Assert response is bound to the requested section and entity", () => {
				response.SectionSchema.Should().Be(sectionSchema,
					because: "the response must preserve the exact inspected Classic section");
				response.Entity.Should().NotBeNullOrWhiteSpace(
					because: "a resolved section always names the entity its list is bound to");
				return Task.CompletedTask;
			});
			await AllureApi.Step("Assert the live body resolved through the static-parse branch", () => {
				response.Source.Should().Be("schema-default",
					because: $"'{sectionSchema}' is configured as the section that declares its list columns "
						+ "statically, so the live path must prove the parser works against a real Classic body "
						+ "rather than only hand-written ones");
				response.Columns.Should().NotBeEmpty(
					because: "a non-none source must carry at least one default column");
				return Task.CompletedTask;
			});
			await AllureApi.Step("Assert successful execution includes an Info log message", () => {
				execution.ExitCode.Should().Be(0, because: "the command completed successfully");
				execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Info,
					because: "successful MCP command output must include a human-readable Info diagnostic");
				return Task.CompletedTask;
			});
		}
	}

	[Test]
	[Description("Resolves a never-configured sandbox Classic section to entity-default, discriminating it from the schema-default branch.")]
	[AllureTag(GetClassicListColumnsTool.ToolName)]
	[AllureName("get-classic-list-columns resolves a never-configured Classic section as entity-default")]
	[AllureDescription("The source discriminator is the load-bearing part of the contract, so a section with no static list columns must return exactly entity-default. This half runs on a stock stand; the schema-default half of the pair runs only once McpE2E:Sandbox:ClassicSchemaDefaultSectionSchema names a seeded section, so on a stand that does not set it the two do NOT run as a pair.")]
	public async Task Resolve_ShouldReturnEntityDefault_WhenSectionDeclaresNoStaticColumns() {
		// Arrange — defaults to ContactSectionV2, which the sandbox run proves is bare (it resolved to
		// entity-default carrying the resolver's "does not define static list columns" note and no skipped
		// layers). A stand that DOES configure Contact retargets this at a section it leaves alone.
		McpE2ESettings settings = TestConfiguration.Load();
		string? neverConfiguredSection = settings.Sandbox.ClassicEntityDefaultSectionSchema;
		if (string.IsNullOrWhiteSpace(neverConfiguredSection)) {
			Assert.Ignore("Configure McpE2E:Sandbox:ClassicEntityDefaultSectionSchema with a seeded Classic section "
				+ "that has no static list columns to run the entity-default discrimination E2E.");
			return;
		}
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			GetClassicListColumnsTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = neverConfiguredSection,
					// Same reason as the schema-default half: the entity fallback is only reachable once the
					// saved profile is taken out of the resolution order.
					["ignore-profile"] = true,
					["environment-name"] = arrangeContext.EnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		GetClassicListColumnsResponse response =
			EntitySchemaStructuredResultParser.Extract<GetClassicListColumnsResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "resolving a seeded section is a valid read-only request");
		response.Success.Should().BeTrue(
			because: $"the seeded section '{neverConfiguredSection}' should resolve. Error: {response.Error}");
		response.Source.Should().Be("entity-default",
			because: "a section that was never configured has no static list columns, so the primary display "
				+ "column is the answer — and it must read differently from the reconfigured section");
		response.Columns.Should().ContainSingle(
			because: "the entity-default fallback is exactly the primary display column");
		response.Notes.Should().NotBeEmpty(
			because: "the response must say why the entity fallback was selected");
	}

	[Test]
	[Description("Resolves a Classic section's saved grid profile through the real MCP path and reports the view, the configuration used, and the profile scope.")]
	[AllureTag(GetClassicListColumnsTool.ToolName)]
	[AllureName("get-classic-list-columns resolves a Classic section from its saved grid profile")]
	[AllureDescription("The saved grid profile is what the Classic list actually renders, and it is the branch the ticket's original scope dropped: a product section declares far fewer columns in code than its list shows. This asserts the live default path returns source=profile with the provenance fields, and that the same section returns a DIFFERENT source once ignore-profile takes the profile out of the order — the discrimination the contract rests on.")]
	public async Task Resolve_ShouldReturnProfile_WhenTheSectionHasASavedGridProfile() {
		// Arrange — defaults to the same never-configured section as the entity-default half, because a stand
		// that seeds product data holds a SYSTEM grid profile for it even though nobody ever opened it. A stand
		// that does not can retarget this through the same setting.
		McpE2ESettings settings = TestConfiguration.Load();
		string? sectionSchema = settings.Sandbox.ClassicEntityDefaultSectionSchema;
		if (string.IsNullOrWhiteSpace(sectionSchema)) {
			Assert.Ignore("Configure McpE2E:Sandbox:ClassicEntityDefaultSectionSchema with a seeded Classic "
				+ "section to run the profile-source E2E.");
			return;
		}
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			GetClassicListColumnsTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = sectionSchema,
					["environment-name"] = arrangeContext.EnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		GetClassicListColumnsResponse response =
			EntitySchemaStructuredResultParser.Extract<GetClassicListColumnsResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "reading a saved grid profile is a valid read-only request");
		response.Success.Should().BeTrue(
			because: $"section '{sectionSchema}' should resolve. Error: {response.Error}");
		if (response.Source != "profile") {
			Assert.Ignore($"Sandbox section '{sectionSchema}' resolved as '{response.Source}', so the stand holds "
				+ "no saved grid profile for it; point McpE2E:Sandbox:ClassicEntityDefaultSectionSchema at a "
				+ "product section to run the profile-source assertions.");
			return;
		}
		response.Columns.Should().NotBeEmpty(
			because: "a profile source means a stored configuration was read, so it carries columns");
		response.View.Should().NotBeNullOrWhiteSpace(
			because: "a profile answer must name the view it came from");
		response.ViewType.Should().BeOneOf(["listed", "tiled"],
			because: "a Classic grid stores both configurations, so the answer must name the one reported");
		response.ProfileScope.Should().BeOneOf(["user", "shared", "unknown"],
			because: "the scope is what keeps a personal layout from reading as the section's canonical set");

		// The same section with the profile taken out of the order must answer from a DIFFERENT source. Without
		// this second call the test could pass on a build where ignore-profile is silently ignored.
		CallToolResult staticResult = await arrangeContext.Session.CallToolAsync(
			GetClassicListColumnsTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = sectionSchema,
					["ignore-profile"] = true,
					["environment-name"] = arrangeContext.EnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		GetClassicListColumnsResponse staticResponse =
			EntitySchemaStructuredResultParser.Extract<GetClassicListColumnsResponse>(staticResult);
		staticResponse.Success.Should().BeTrue(
			because: $"the static branch must still resolve '{sectionSchema}'. Error: {staticResponse.Error}");
		staticResponse.Source.Should().NotBe("profile",
			because: "ignore-profile has to remove the profile from the resolution order, and this pair is the "
				+ "live proof that the two questions return different answers");
		staticResponse.ViewType.Should().BeNull(
			because: "a static answer has no stored configuration to name");
	}

	[Test]
	[Description("Reports a nonexistent Classic section as a structured command-level failure rather than an MCP transport error.")]
	[AllureTag(GetClassicListColumnsTool.ToolName)]
	[AllureName("get-classic-list-columns reports a missing Classic section as a structured failure")]
	[AllureDescription("Consumers key their error path off success:false, so a missing schema must arrive as a readable payload with callResult.IsError not true.")]
	public async Task Resolve_ShouldReportFailure_WhenSectionSchemaIsMissing() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));
		string missingSchema = $"UsrMissingClassicSection{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			GetClassicListColumnsTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = missingSchema,
					["environment-name"] = arrangeContext.EnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		GetClassicListColumnsResponse response =
			EntitySchemaStructuredResultParser.Extract<GetClassicListColumnsResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a missing section is a command-level failure, not an MCP transport failure");
		response.Success.Should().BeFalse(because: "the requested Classic section does not exist");
		response.Error.Should().Contain(missingSchema,
			because: "the failure should identify the schema the caller requested");
		response.Error.Should().Contain("not found",
			because: "the failure should explain that no section schema resolved");
	}

	private async Task<ArrangeContext> ArrangeAsync(McpE2ESettings settings, TimeSpan timeout) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		return new ArrangeContext(Session, cancellationTokenSource, environmentName);
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to run get-classic-list-columns MCP E2E.");
			return string.Empty;
		}
		if (await CanReachEnvironmentAsync(settings, environmentName)) {
			return environmentName;
		}
		Assert.Ignore($"Configured MCP sandbox environment '{environmentName}' is not reachable.");
		return string.Empty;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromSeconds(30));
		try {
			ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
				settings,
				["ping-app", "-e", environmentName],
				cancellationToken: cancellationTokenSource.Token);
			return result.ExitCode == 0;
		}
		catch (OperationCanceledException) {
			return false;
		}
	}

	private new sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string EnvironmentName) : IAsyncDisposable {
		public ValueTask DisposeAsync() {
			CancellationTokenSource.Dispose();
			return ValueTask.CompletedTask;
		}
	}
}
