using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for ENG-93088: the odata-* tools must report a Web API routing error
/// (<c>{Message, MessageDetail}</c>, e.g. a 404 for an unregistered/uncompiled OData controller
/// returned with HTTP 200) as a structured failure instead of wrapping the error body as data.
/// Both the read and the create path funnel through the shared <see cref="Clio.Common.CreatioResponseError"/>
/// detection, so both are exercised here against the stubbed masked response.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(ODataReadTool.ToolName)]
[NonParallelizable]
public sealed class ODataReadRoutingErrorE2ETests {
	private const string RegisterToolName = "reg-web-app";
	private const string UnregisteredEntity = "UsrCustomerStatus";
	private const string NonODataEntity = "MessageType";
	private const string InvalidQueryEntity = "SysSettingsValue";

	[Test]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read reports a Web API routing error as a structured failure")]
	[AllureDescription("Registers an environment against a stub that returns the {Message, MessageDetail} routing body with HTTP 200 for an unregistered controller, then verifies odata-read returns success:false with the unregistered-entity hint.")]
	[Description("odata-read against an unregistered OData controller (a {Message, MessageDetail} 404 body served with HTTP 200) returns success:false with a clear message, not a masked single-entity success.")]
	public async Task ODataRead_Should_Report_Routing_Error_As_Failure() {
		await RunAgainstRoutingErrorStubAsync(async (session, environmentName, cancellationToken) => {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				ODataReadTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["entity"] = UnregisteredEntity,
						["select"] = new[] { "Id", "Name" },
						["top"] = 10
					}
				},
				cancellationToken);
			ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);

			// Assert
			callResult.IsError.Should().NotBeTrue(
				because: "a bindable odata-read payload should return a structured tool response, not a protocol error");
			response.Success.Should().BeFalse(
				because: "a {Message, MessageDetail} routing body must be surfaced as a failure, not wrapped as a single-entity success");
			response.Error.Should().Be(
				CreatioResponseError.DescribeServerReportedReadError(ODataErrorKind.UnregisteredEntity),
				because: "the read path reports the locally authored classification plus the hint, asserted via the shared builder to avoid literal drift");
			response.ErrorCode.Should().Be("entity-not-found",
				because: "a routing miss and an IIS 404 page are the same condition - an entity set that cannot be reached - so they must not hand the caller two different codes");
			response.CorrelationId.Should().NotBeNullOrWhiteSpace(
				because: "core-rules promises a correlation-id on every response, and it is the only bridge to the debug line carrying the server's own wording");
			response.Error.Should().NotContain($"controller named '{UnregisteredEntity}'",
				because: "the server's own MessageDetail must not be copied into an MCP transcript, which a model reads as trusted content");
			response.Error.Should().Contain(CreatioResponseError.UnregisteredEntityHint,
				because: "the unregistered-entity hint is locally authored, so it is the one piece of detail that still steers the agent to wait-and-retry rather than read this as a data gap");
		});
	}

	[Test]
	[AllureTag(ODataCreateTool.ToolName)]
	[AllureName("odata-create reports a Web API routing error as a structured per-row failure")]
	[AllureDescription("Registers an environment against a stub that returns the {Message, MessageDetail} routing body with HTTP 200 for a POST to an unregistered controller, then verifies odata-create reports a per-row failure with the unregistered-entity hint.")]
	[Description("odata-create against an unregistered OData controller (a {Message, MessageDetail} 404 body served with HTTP 200 on POST) reports the row as failed with a clear message, exercising the shared routing-error detection end to end on the write path.")]
	public async Task ODataCreate_Should_Report_Routing_Error_As_Failure() {
		await RunAgainstRoutingErrorStubAsync(async (session, environmentName, cancellationToken) => {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				ODataCreateTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["entity"] = UnregisteredEntity,
						["rows"] = new object[] { new Dictionary<string, object?> { ["Name"] = "Active" } }
					}
				},
				cancellationToken);
			ODataCreateBatchResponse response = EntitySchemaStructuredResultParser.Extract<ODataCreateBatchResponse>(callResult);

			// Assert
			callResult.IsError.Should().NotBeTrue(
				because: "a bindable odata-create payload should return a structured tool response, not a protocol error");
			response.Failed.Should().Be(1,
				because: "the single row targets an unregistered controller and must be reported as failed, not created");
			ODataRowResult row = response.Results.Should().ContainSingle(
				because: "the one-row batch should report exactly one per-row result").Subject;
			row.Success.Should().BeFalse(
				because: "a {Message, MessageDetail} routing body on POST must not be reported as a successful create");
			row.Error.Should().Contain(Clio.Common.CreatioResponseError.UnregisteredEntityHint,
				because: "the create path funnels through the same shared detection and must surface the identical unregistered-entity hint");
		});
	}

	[Test]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read classifies a 404-style HTML body as an unavailable entity set")]
	[AllureDescription("Registers an environment against a stub that returns a 404-style IIS HTML body with HTTP 200, then verifies odata-read explains the ESQ escape route without exposing the HTML parser failure.")]
	[Description("odata-read against a 404-style HTML body returns success:false naming the unavailable entity and the execute-esq escape route, not a raw JSON parser error or IIS page.")]
	public async Task ODataRead_Should_Classify_Iis_Html_404_As_Missing_Entity_Set() {
		await RunAgainstRoutingErrorStubAsync(async (session, environmentName, cancellationToken) => {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				ODataReadTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["entity"] = NonODataEntity,
						["select"] = new[] { "Id", "Name" },
						["top"] = 10
					}
				},
				cancellationToken);
			ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);

			// Assert
			callResult.IsError.Should().NotBeTrue(
				because: "a bindable odata-read payload should return a structured tool response, not a protocol error");
			response.Success.Should().BeFalse(
				because: "an IIS 404 means the entity set is unavailable, not that the OData JSON is malformed");
			response.Error.Should().Contain(NonODataEntity,
				because: "the failure must identify the requested entity set");
			response.Error.Should().Contain("execute-esq",
				because: "schemas without an OData entity set must have an actionable read alternative");
			response.Error.Should().NotContain("Failed to parse OData response",
				because: "the caller should not be sent down a serialization-debugging path");
			response.Error.Should().NotContain("404 - File or directory not found",
				because: "the IIS boilerplate is not an actionable diagnostic");
			response.ErrorCode.Should().Be("entity-not-found",
				because: "GH-1407 case 1 asks for a structured code on the 404 path instead of a wall of escaped IIS markup");
			response.CorrelationId.Should().NotBeNullOrWhiteSpace(
				because: "core-rules promises a correlation-id on every response, and this path carried none");
		}, NonODataEntity);
	}

	[Test]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read classifies an unknown filter property as invalid-query")]
	[AllureDescription("Serves the OData v4 error body Creatio returns for a property the entity type has not got, and verifies odata-read reports error-code invalid-query with a correlation-id and none of the server's own wording.")]
	[Description("GH-1407 case 4: a filter on a property the entity does not expose is reported as error-code invalid-query, naming the caller's own field, with a correlation-id and no server prose.")]
	public async Task ODataRead_Should_Classify_An_Unknown_Filter_Property_As_Invalid_Query() {
		await RunAgainstRoutingErrorStubAsync(async (session, environmentName, cancellationToken) => {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				ODataReadTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["entity"] = InvalidQueryEntity,
						["filters"] = new Dictionary<string, object?> {
							["all"] = new object[] {
								new Dictionary<string, object?> { ["field"] = "Nope", ["op"] = "eq", ["value"] = "x" }
							}
						},
						["top"] = 2
					}
				},
				cancellationToken);
			ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);

			// Assert
			response.Success.Should().BeFalse(
				because: "an OData error envelope is a failure, not a page of records");
			response.ErrorCode.Should().Be("invalid-query",
				because: "GH-1407 asks for a machine-readable code so a caller can tell a wrong query from an unreachable environment without parsing English");
			response.CorrelationId.Should().NotBeNullOrWhiteSpace(
				because: "core-rules promises a correlation-id on every response, and this failure carried none before GH-1407");
			response.Error.Should().Contain("Nope",
				because: "the caller's own field name is the only text that may be echoed, and without it the caller cannot tell which member was rejected");
			response.Error.Should().NotContain("Terrasoft.Configuration.OData",
				because: "the server's own wording must not reach a field a model reads as trusted content");
		}, invalidQueryEntity: InvalidQueryEntity);
	}

	[Test]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read answers a raw lookup-column filter with the navigation-path hint")]
	[AllureDescription("Serves the nested innererror body Creatio returns for a filter on a raw foreign-key column, and verifies odata-read classifies it as invalid-query and names the navigation path to filter on instead.")]
	[Description("GH-1407 case 2: a filter on a raw foreign-key column, whose cause the server hides two levels down under innererror, is reported as invalid-query with the SysSettings/Id navigation-path hint.")]
	public async Task ODataRead_Should_Hint_The_Navigation_Path_For_A_Raw_Lookup_Column() {
		await RunAgainstRoutingErrorStubAsync(async (session, environmentName, cancellationToken) => {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				ODataReadTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["entity"] = InvalidQueryEntity,
						["filters"] = new Dictionary<string, object?> {
							["all"] = new object[] {
								new Dictionary<string, object?> {
									["field"] = "SysSettingsId",
									["op"] = "eq",
									["value"] = "00000000-0000-0000-0000-000000000001"
								}
							}
						},
						["select"] = new[] { "Id", "TextValue" },
						["top"] = 5
					}
				},
				cancellationToken);
			ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);

			// Assert
			response.Success.Should().BeFalse(
				because: "the server rejected the query, so reporting anything but a failure would hide it");
			response.ErrorCode.Should().Be("invalid-query",
				because: "Creatio puts 'An error has occurred.' in the headline here and the real cause two levels down, so classifying on the headline alone left the caller with an unexplained server error");
			response.Error.Should().Contain("SysSettings/Id",
				because: "GH-1407 reports that the navigation path succeeds where the raw column fails, and the caller had no way to discover it");
			response.Error.Should().NotContain("Column by path",
				because: "the sentence the classification was derived from is the server's own and stays on the debug channel");
			response.CorrelationId.Should().NotBeNullOrWhiteSpace(
				because: "the correlation-id is the only bridge from this response to that debug line");
		}, invalidQueryEntity: InvalidQueryEntity);
	}

	/// <summary>
	/// Stands up the isolated clio home, the routing-error stub, a real mcp-server session, and a
	/// registered environment pointing at the stub, then runs <paramref name="act"/> against them.
	/// Centralizes the arrange so the read and create tests do not each re-implement it.
	/// </summary>
	private static async Task RunAgainstRoutingErrorStubAsync(
		Func<McpServerSession, string, CancellationToken, Task> act,
		string? nonJsonEntity = null,
		string? invalidQueryEntity = null) {
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-odata-routing-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		try {
			string envVarName = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			settings.ProcessEnvironmentVariables[envVarName] = tempHome;
			using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
				"""
				{
				  "ActiveEnvironmentKey": null,
				  "Environments": {}
				}
				""",
				settings.ClioProcessPath,
				settings.ProcessEnvironmentVariables);
			await using RuntimeDetectionStubServer stubServer = RuntimeDetectionStubServer.Start(
				new RuntimeDetectionStubServerConfiguration(
					NetCoreHealthEnabled: true,
					NetFrameworkHealthEnabled: true,
					NetCoreServiceEnabled: false,
					NetFrameworkServiceEnabled: true,
					NetCoreUiMarkerEnabled: false,
					NetFrameworkUiMarkerEnabled: true,
					ODataRoutingErrorEntity: UnregisteredEntity,
					ODataNonJsonEntity: nonJsonEntity,
					ODataInvalidQueryEntity: invalidQueryEntity));
			using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
			await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
			string environmentName = $"odata-routing-{Guid.NewGuid():N}";
			await RegisterEnvironmentAsync(session, environmentName, stubServer.BaseUrl, cancellationTokenSource.Token);

			await act(session, environmentName, cancellationTokenSource.Token);
		} finally {
			TryDeleteDirectory(tempHome);
		}
	}

	private static void TryDeleteDirectory(string path) {
		try {
			if (Directory.Exists(path)) {
				Directory.Delete(path, recursive: true);
			}
		} catch {
			// Best-effort cleanup of the isolated home directory; a leaked temp dir must not fail the test.
		}
	}

	private static async Task RegisterEnvironmentAsync(
		McpServerSession session,
		string environmentName,
		string baseUrl,
		CancellationToken cancellationToken) {
		IReadOnlyCollection<string> toolNames = await session.ListReachableToolNamesAsync(cancellationToken);
		toolNames.Should().Contain(RegisterToolName,
			because: $"the {RegisterToolName} MCP tool must be discoverable before the test can register the stub environment");

		CallToolResult registerResult = await session.CallToolAsync(
			RegisterToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["uri"] = baseUrl,
					["login"] = "Supervisor",
					["password"] = "Supervisor"
				}
			},
			cancellationToken);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(registerResult);
		execution.ExitCode.Should().Be(0,
			because: "the stub environment must register successfully before the odata-* tool can be exercised against it");
	}
}
