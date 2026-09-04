using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(SchemaNamePrefixTool.GetSchemaNamePrefixToolName)]
[NonParallelizable]
public sealed class SchemaNamePrefixToolE2ETests {

	private const string ToolName = SchemaNamePrefixTool.GetSchemaNamePrefixToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes get-schema-name-prefix against the configured sandbox environment, and verifies the structured response returns the active SchemaNamePrefix system setting.")]
	[AllureTag(ToolName)]
	[AllureName("Get schema name prefix returns structured SchemaNamePrefix payload")]
	[AllureDescription("Uses the real clio MCP server to call get-schema-name-prefix against the configured reachable sandbox environment and verifies that the structured response reports success and returns a non-null prefix value.")]
	public async Task GetSchemaNamePrefix_Should_Return_Structured_Prefix_Response() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3), requireReachableEnvironment: true);

		// Act
		CallToolResult callResult = await CallToolAsync(
			arrangeContext,
			ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName
			});
		SchemaNamePrefixResult response = EntitySchemaStructuredResultParser.Extract<SchemaNamePrefixResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "get-schema-name-prefix should return a structured success payload for a reachable configured environment");
		response.Success.Should().BeTrue(
			because: "reading SchemaNamePrefix should succeed when the environment is reachable and the system setting is accessible");
		response.SchemaNamePrefix.Should().NotBeNullOrWhiteSpace(
			because: "the sandbox environment should have SchemaNamePrefix configured to a non-empty value");
		response.Error.Should().BeNull(
			because: "no error message should be present when the tool call succeeds");
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("get-schema-name-prefix reports rejected credentials instead of an empty prefix")]
	[AllureDescription("Registers an environment against a stub whose authenticated SelectQuery answers with the Creatio login page, then verifies get-schema-name-prefix returns success:false with an error rather than a successful empty prefix.")]
	[Description("get-schema-name-prefix against an environment whose credentials Creatio rejects returns success:false with a diagnostic, not a valid-looking empty prefix. The tool's own [Description] now promises that behaviour and AGENTS.md makes MCP e2e coverage mandatory for a changed MCP tool, so the failure shape is pinned here and not only in the unit tests.")]
	public async Task GetSchemaNamePrefix_Should_Report_Authentication_Failure() {
		await CredentialRejectionStubHarness.RunAsync("schemanameprefix-auth",
			async (session, environmentName, cancellationToken) => {
				// Act
				CallToolResult callResult = await session.CallToolAsync(
					ToolName,
					new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = environmentName
						}
					},
					cancellationToken);
				SchemaNamePrefixResult response =
					EntitySchemaStructuredResultParser.Extract<SchemaNamePrefixResult>(callResult);

				// Assert
				response.Success.Should().BeFalse(
					because: "a rejected credential must not be reported as a successful prefix read - the empty "
					+ "prefix that used to come back is indistinguishable from an environment that has none configured");
				response.Error.Should().NotBeNullOrWhiteSpace(
					because: "the failure envelope has to carry a diagnostic the caller can act on");
				response.SchemaNamePrefix.Should().BeNullOrEmpty(
					because: "nothing was read, so no prefix may be advertised alongside the failure");
			});
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("get-schema-name-prefix returns the full classified failure envelope on a rejected session")]
	[AllureDescription("Drives get-schema-name-prefix over the real mcp-server against a stub whose authenticated SelectQuery answers with the Creatio login page, and asserts the whole failure envelope: success:false, the read path's ProviderFailure error-category, a cause, a recovery action and a correlation ID.")]
	[Description("The tool lost five hand-written catch arms and gained four envelope fields, and nothing over the real MCP path asserted them. This pins the classified failure shape end to end: the ProviderFailure category the READ path can actually prove (the same one get-sys-setting reports on this stub), non-empty cause and recovery-action, a correlation ID, and no prefix advertised alongside the failure.")]
	public async Task GetSchemaNamePrefix_Should_Return_The_Classified_Failure_Envelope() {
		await CredentialRejectionStubHarness.RunAsync("schemanameprefix-auth",
			async (session, environmentName, cancellationToken) => {
				// Act
				SchemaNamePrefixResult response = await CallForResultAsync(session, environmentName, cancellationToken);

				// Assert
				response.Success.Should().BeFalse(
					because: "a rejected credential must not be reported as a successful prefix read - an empty "
					+ "prefix on success:true is indistinguishable from an environment that has none configured");
				response.ErrorCategory.Should().Be(SysSettingErrorCategories.ProviderFailure,
					because: "this is the READ path, where ATF keeps only the parser message and never the body - "
					+ "so a login page and a gateway error page are indistinguishable and the envelope reports the "
					+ "provider verdict rather than claiming a credential rejection, exactly as "
					+ "SysSettingsAuthenticationFailureE2ETests asserts for get-sys-setting. Agreeing with the "
					+ "sys-setting tools is the point; Authentication is the WRITE path's definite verdict, and "
					+ "this tool has no write path");
				response.Error.Should().NotBeNullOrWhiteSpace(
					because: "ProviderFailure is one of the three categories whose message IS the diagnosis, so it "
					+ "is promoted into the headline rather than replaced by the generic label");
				response.Cause.Should().NotBeNullOrWhiteSpace(
					because: "the cause is what tells the caller WHY, and it is the field the no-promotion rule "
					+ "keeps the actionable text in");
				response.RecoveryAction.Should().NotBeNullOrWhiteSpace(
					because: "the envelope promises an action the caller can take, not just a verdict");
				response.CorrelationId.Should().NotBeNullOrWhiteSpace(
					because: "the recovery text asks the caller to quote an ID, so one has to be there");
				response.SchemaNamePrefix.Should().BeNullOrEmpty(
					because: "nothing was read, so no prefix may be advertised alongside the failure");
			});
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("get-schema-name-prefix does not promote a Configuration failure message into the error field")]
	[AllureDescription("Calls get-schema-name-prefix with an environment name that is not registered and asserts the Configuration category keeps its message in cause while the error field stays the tool's generic label.")]
	[Description("The no-promotion rule is an allow-list: only Authentication, Network and ProviderFailure put their message in the headline. A Configuration failure - an unregistered environment, nothing sent anywhere - must keep the generic label in error and carry the actionable text in cause, next to a recovery action and a correlation ID.")]
	public async Task GetSchemaNamePrefix_Should_Not_Promote_A_Configuration_Failure_Message() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3), requireReachableEnvironment: false);

		// Act
		SchemaNamePrefixResult response = await CallForResultAsync(
			arrangeContext.Session, $"clio-unregistered-{Guid.NewGuid():N}", arrangeContext.CancellationTokenSource.Token);

		// Assert
		response.Success.Should().BeFalse(
			because: "an environment that is not registered cannot be read from");
		response.ErrorCategory.Should().Be(SysSettingErrorCategories.Configuration,
			because: "nothing was sent anywhere - this is clio's own state, not the environment's");
		response.Error.Should().Be(SchemaNamePrefixTool.GenericReadFailure,
			because: "a Configuration failure is not on the promotion allow-list, so its message must not become "
			+ "the headline - that rule predates the shared classifier and a category added later must not "
			+ "silently start promoting");
		response.Cause.Should().NotBeNullOrWhiteSpace(
			because: "the actionable text is not lost, it moves to the cause field");
		response.RecoveryAction.Should().NotBeNullOrWhiteSpace(
			because: "an unregistered environment has a concrete remedy and the envelope must name it");
		response.CorrelationId.Should().NotBeNullOrWhiteSpace(
			because: "every classified failure mints an ID, whichever category it fell into");
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("get-schema-name-prefix forwards the correlation ID to the client log pane")]
	[AllureDescription("Captures notifications/message while get-schema-name-prefix fails against the credential-rejection stub, and asserts the envelope's correlation ID arrives in a forwarded log notification under the clio.tool.{correlationId} category.")]
	[Description("Running as an MCP server every other sink is closed: the console is suppressed under MCP server mode and the log file exists only with --log. So notifications/message is the ONLY place the classified line can land, and without it the correlation-id the recovery text tells the caller to quote resolves to nothing.")]
	public async Task GetSchemaNamePrefix_Should_Forward_The_Correlation_Id_To_The_Client() {
		await CredentialRejectionStubHarness.RunAsync("schemanameprefix-log",
			async (session, environmentName, cancellationToken) => {
				// Arrange
				session.StartCapturingLogNotifications();

				// Act
				SchemaNamePrefixResult response = await CallForResultAsync(session, environmentName, cancellationToken);

				// Assert
				response.CorrelationId.Should().NotBeNullOrWhiteSpace(
					because: "the forwarded line is identified by the ID, so the envelope has to carry one");
				bool forwarded = await session.WaitForCapturedLogAsync(
					node => (node["logger"]?.GetValue<string>() ?? string.Empty)
							.Contains(response.CorrelationId!, StringComparison.Ordinal)
						|| (node["data"]?.ToJsonString() ?? string.Empty)
							.Contains(response.CorrelationId!, StringComparison.Ordinal),
					TimeSpan.FromSeconds(20),
					cancellationToken);
				forwarded.Should().BeTrue(
					because: "the correlation ID must be resolvable somewhere the caller can actually look - "
					+ "notifications/message is the only sink open on the MCP path");
			});
	}

	private static async Task<SchemaNamePrefixResult> CallForResultAsync(
		McpServerSession session,
		string? environmentName,
		CancellationToken cancellationToken) {
		CallToolResult callResult = await session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName
				}
			},
			cancellationToken);
		return EntitySchemaStructuredResultParser.Extract<SchemaNamePrefixResult>(callResult);
	}

	private static async Task<CallToolResult> CallToolAsync(
		ArrangeContext arrangeContext,
		string toolName,
		Dictionary<string, object?> args) {
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
		toolNames.Should().Contain(toolName,
			because: "the get-schema-name-prefix tool must be discoverable via the get-tool-contract compact index before the end-to-end call can be executed through the lazy surface");
		return await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["args"] = args
			},
			arrangeContext.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync(
		McpE2ESettings settings,
		TimeSpan timeout,
		bool requireReachableEnvironment) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string? environmentName = requireReachableEnvironment
			? await ResolveReachableEnvironmentAsync(settings)
			: settings.Sandbox.EnvironmentName;
		return new ArrangeContext(session, cancellationTokenSource, environmentName);
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(configuredEnvironmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to run SchemaNamePrefix MCP E2E tests.");
		}

		if (!await CanReachEnvironmentAsync(settings, configuredEnvironmentName!)) {
			Assert.Ignore($"SchemaNamePrefix MCP E2E requires a reachable sandbox environment. '{configuredEnvironmentName}' was not reachable.");
		}

		return configuredEnvironmentName!;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName]);
		return result.ExitCode == 0;
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string? EnvironmentName) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
