using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Creatio;

/// <summary>
/// Isolated stand for the <c>odata-update</c> pre-write validation: a <see cref="RuntimeDetectionStubServer"/>
/// configured for one OData pre-write mode, an MCP server session pointed at an isolated clio home, and an
/// environment registered against the stub. The stub records every request it serves, so a test can assert
/// which URL the validation actually requested and that no PATCH was issued.
/// </summary>
internal sealed class ODataPreWriteStand : IAsyncDisposable {
	private const string RegisterToolName = "reg-web-app";

	private readonly string _tempHome;
	private readonly TemporaryClioSettingsOverride _settingsOverride;
	private readonly CancellationTokenSource _cancellationTokenSource;

	private ODataPreWriteStand(
		string tempHome,
		TemporaryClioSettingsOverride settingsOverride,
		CancellationTokenSource cancellationTokenSource,
		RuntimeDetectionStubServer stub,
		McpServerSession session,
		string environmentName) {
		_tempHome = tempHome;
		_settingsOverride = settingsOverride;
		_cancellationTokenSource = cancellationTokenSource;
		Stub = stub;
		Session = session;
		EnvironmentName = environmentName;
	}

	/// <summary>Stub Creatio the environment is registered against.</summary>
	public RuntimeDetectionStubServer Stub { get; }

	/// <summary>Live MCP server session talking to a real <c>clio mcp-server</c> child process.</summary>
	public McpServerSession Session { get; }

	/// <summary>Name of the environment registered against <see cref="Stub"/>.</summary>
	public string EnvironmentName { get; }

	/// <summary>Token bounding the whole stand's lifetime.</summary>
	public CancellationToken CancellationToken => _cancellationTokenSource.Token;

	/// <summary>
	/// Starts a stub in <paramref name="preWriteMode"/>, an MCP session against an isolated clio home, and
	/// registers an environment pointing at the stub.
	/// </summary>
	/// <param name="preWriteMode">
	/// <see cref="RuntimeDetectionStubServer.ODataPreWriteMetadata"/> or
	/// <see cref="RuntimeDetectionStubServer.ODataPreWriteUnverified"/>.
	/// </param>
	/// <param name="entity">OData entity set the stub serves.</param>
	/// <returns>The started stand; dispose it to tear everything down.</returns>
	public static async Task<ODataPreWriteStand> StartAsync(string preWriteMode, string entity) {
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-odata-prewrite-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		string envVarName = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		settings.ProcessEnvironmentVariables[envVarName] = tempHome;
		TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
			"""
			{
			  "ActiveEnvironmentKey": null,
			  "Environments": {}
			}
			""",
			settings.ClioProcessPath,
			settings.ProcessEnvironmentVariables);
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		RuntimeDetectionStubServer stub = RuntimeDetectionStubServer.Start(
			new RuntimeDetectionStubServerConfiguration(
				NetCoreHealthEnabled: false,
				NetFrameworkHealthEnabled: true,
				NetCoreServiceEnabled: false,
				NetFrameworkServiceEnabled: true,
				NetCoreUiMarkerEnabled: false,
				NetFrameworkUiMarkerEnabled: true,
				ODataEntity: entity,
				ODataPreWriteMode: preWriteMode));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string environmentName = $"odata-prewrite-{Guid.NewGuid():N}";
		await RegisterEnvironmentAsync(session, environmentName, stub.BaseUrl, cancellationTokenSource.Token);
		return new ODataPreWriteStand(
			tempHome, settingsOverride, cancellationTokenSource, stub, session, environmentName);
	}

	/// <summary>
	/// Calls <c>odata-update</c> against the registered stub environment.
	/// </summary>
	/// <param name="entity">Entity set to update.</param>
	/// <param name="id">Record GUID to address.</param>
	/// <param name="data">Field/value map sent as the <c>data</c> argument.</param>
	/// <returns>The raw MCP tool result.</returns>
	public Task<CallToolResult> UpdateAsync(string entity, string id, Dictionary<string, object?> data) =>
		Session.CallToolAsync(
			Clio.Command.McpServer.Tools.ODataUpdateTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = EnvironmentName,
					["entity"] = entity,
					["id"] = id,
					["data"] = data,
					["confirm"] = true
				}
			},
			CancellationToken);

	/// <summary>Requests the stub recorded, oldest first.</summary>
	/// <returns>Every request the stub served during this stand's lifetime.</returns>
	public Task<IReadOnlyList<RecordedStubRequest>> GetRecordedRequestsAsync() =>
		Stub.GetRecordedRequestsAsync(CancellationToken);

	public async ValueTask DisposeAsync() {
		await Session.DisposeAsync();
		await Stub.DisposeAsync();
		_settingsOverride.Dispose();
		_cancellationTokenSource.Dispose();
		TryDeleteDirectory(_tempHome);
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
			because: $"the {RegisterToolName} MCP tool must be discoverable before the stub environment can be registered");

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
			because: "the stub environment must register successfully before odata-update can be exercised against it");
	}
}
