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

	private const string SectionSchema = "ContactSectionV2";

	[Test]
	[Description("Invokes get-classic-list-columns through the real stdio MCP server against the configured sandbox and returns statically parsed schema columns.")]
	[AllureTag(GetClassicListColumnsTool.ToolName)]
	[AllureName("get-classic-list-columns resolves a sandbox Classic section as schema-default")]
	[AllureDescription("Uses the configured sandbox and the standard Contact section to prove the real read-only MCP path parses static list columns out of live Classic bodies, not just hand-written ones.")]
	public async Task Resolve_ShouldReturnSchemaDefault_WhenConfiguredSandboxIsAvailable() {
		// Arrange
		ArrangeContext arrangeContext = await AllureApi.Step("Arrange configured sandbox MCP session", async () => {
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			return await ArrangeAsync(settings, TimeSpan.FromMinutes(3));
		});
		await using (arrangeContext) {

			// Act
			CallToolResult callResult = await AllureApi.Step("Act by resolving ContactSectionV2 through stdio MCP", async () =>
				await arrangeContext.Session.CallToolAsync(
					GetClassicListColumnsTool.ToolName,
					new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["schema-name"] = SectionSchema,
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
					because: $"the standard Contact section should resolve successfully. Error: {response.Error}");
				return Task.CompletedTask;
			});
			await AllureApi.Step("Assert response is bound to the requested section and entity", () => {
				response.SectionSchema.Should().Be(SectionSchema,
					because: "the response must preserve the exact inspected Classic section");
				response.Entity.Should().Be("Contact", because: "ContactSectionV2 is bound to the Contact entity");
				return Task.CompletedTask;
			});
			await AllureApi.Step("Assert the live body resolved through the static-parse branch", () => {
				response.Source.Should().Be("schema-default",
					because: "ContactSectionV2 declares its list columns statically, so the live path must prove the "
						+ "parser works against a real Classic body rather than only hand-written ones");
				response.Columns.Should().NotBeEmpty(because: "a non-none source must carry at least one default column");
				response.Columns.Select(column => column.Name).Should().Contain("Name",
					because: "the Contact section list shows the contact name");
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
	[AllureDescription("The source discriminator is the load-bearing part of the contract, so a section with no static list columns must return exactly entity-default while the reconfigured section returns schema-default.")]
	public async Task Resolve_ShouldReturnEntityDefault_WhenSectionDeclaresNoStaticColumns() {
		// Arrange
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
