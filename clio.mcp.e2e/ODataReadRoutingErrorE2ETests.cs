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
				CreatioResponseError.DescribeServerReportedReadError(includeUnregisteredEntityHint: true),
				because: "the read path reports the locally authored classification plus the hint, asserted via the shared builder to avoid literal drift");
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
			response.StatusCode.Should().Be(404,
				because: "issue #1325: the documented async-gap retry after create-entity-schema has to key off the "
					+ "status programmatically, and the transport exposes no status of its own - it is read out of "
					+ "the page title");
			response.Entity.Should().Be(NonODataEntity,
				because: "the structured failure must name the entity set it refers to");
			response.Error.Should().Contain(CreatioResponseError.UnregisteredEntityHint,
				because: "the HTML 404 and the JSON routing 404 are the same condition and must share the one hint");
		}, NonODataEntity);
	}

	[Test]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read accepts the comma-separated select form end to end")]
	[AllureDescription("Sends select as the comma-separated string OData itself uses, over a real mcp-server session, and verifies it binds instead of failing on JSON deserialization.")]
	[Description("odata-read with select passed as \"Id,Name\" reaches the tool and is answered by the tool contract, not by a System.String[] deserialization error from the MCP argument binder.")]
	public async Task ODataRead_Should_Accept_A_Comma_Separated_Select() {
		await RunAgainstRoutingErrorStubAsync(async (session, environmentName, cancellationToken) => {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				ODataReadTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["entity"] = NonODataEntity,
						["select"] = "Id,Name",
						["top"] = 10
					}
				},
				cancellationToken);

			// Assert
			callResult.IsError.Should().NotBeTrue(
				because: "issue #1327: the comma-separated form must bind, not be rejected by the argument binder "
					+ "with \"The JSON value could not be converted to System.String[]\"");
			ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);
			response.Error.Should().NotContain("System.String[]",
				because: "a .NET type name is a serializer message, never a statement about this tool\'s contract");
			response.StatusCode.Should().Be(404,
				because: "the request reached the stub and was answered by the tool contract, which proves select bound");
		}, NonODataEntity);
	}

	[Test]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read rejects an unsupported select shape with a contract message")]
	[AllureDescription("Sends select as a number over a real mcp-server session and verifies the failure states this tool\'s contract rather than a .NET type conversion.")]
	[Description("odata-read with a numeric select returns a contract message naming the two accepted forms, not a serializer message.")]
	public async Task ODataRead_Should_Reject_An_Unsupported_Select_Shape_With_A_Contract_Message() {
		await RunAgainstRoutingErrorStubAsync(async (session, environmentName, cancellationToken) => {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				ODataReadTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["entity"] = NonODataEntity,
						["select"] = 5,
						["top"] = 10
					}
				},
				cancellationToken);
			ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);

			// Assert
			response.Success.Should().BeFalse(
				because: "a shape the tool cannot use must be rejected before any Creatio request");
			response.Error.Should().Contain("comma-separated string",
				because: "the message must name both accepted forms so the caller can correct the call in one step");
			response.Error.Should().NotContain("System.String[]",
				because: "the serializer message was the defect reported in issue #1327");
		}, NonODataEntity);
	}

	/// <summary>
	/// Stands up the isolated clio home, the routing-error stub, a real mcp-server session, and a
	/// registered environment pointing at the stub, then runs <paramref name="act"/> against them.
	/// Centralizes the arrange so the read and create tests do not each re-implement it.
	/// </summary>
	private static async Task RunAgainstRoutingErrorStubAsync(
		Func<McpServerSession, string, CancellationToken, Task> act,
		string? nonJsonEntity = null) {
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
					ODataNonJsonEntity: nonJsonEntity));
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
