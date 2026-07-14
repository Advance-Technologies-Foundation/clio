using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Results;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Mcp;

internal sealed class McpServerSession : IAsyncDisposable {
	private readonly StdioClientTransport _transport;
	private readonly ConcurrentQueue<JsonNode> _capturedProgressParams = new();
	private readonly SemaphoreSlim _progressCapturedSignal = new(0, int.MaxValue);
	private IAsyncDisposable? _progressCaptureRegistration;
	private bool _progressCaptureRegistered;
	private HashSet<string>? _advertisedToolNames;
	private IReadOnlyCollection<string>? _reachableToolNames;
	private IReadOnlyList<ToolContractIndexEntry>? _toolContractIndex;

	private McpServerSession(McpClient client, StdioClientTransport transport) {
		Client = client;
		_transport = transport;
	}

	public McpClient Client { get; }

	public static async Task<McpServerSession> StartAsync(McpE2ESettings settings, CancellationToken cancellationToken) =>
		await StartAsync(settings, elicitationHandler: null, cancellationToken);

	/// <summary>
	/// Starts a clio MCP server session. When <paramref name="elicitationHandler"/> is supplied the
	/// client advertises the elicitation capability and routes server elicitation requests to that
	/// handler — letting a test exercise the elicitation path (for example a client that never
	/// answers, simulating a headless agent). When it is <see langword="null"/> the client behaves as
	/// before and does not advertise elicitation.
	/// </summary>
	public static async Task<McpServerSession> StartAsync(
		McpE2ESettings settings,
		Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>>? elicitationHandler,
		CancellationToken cancellationToken) {
		ClioProcessDescriptor process = ClioExecutableResolver.Resolve(settings);
		StdioClientTransport transport = new(new StdioClientTransportOptions {
			Command = process.Command,
			Arguments = [.. process.Arguments],
			WorkingDirectory = process.WorkingDirectory,
			EnvironmentVariables = settings.ProcessEnvironmentVariables,
			Name = "clio-mcp-e2e",
			ShutdownTimeout = TimeSpan.FromSeconds(10)
		}, NullLoggerFactory.Instance);

		McpClientOptions options = new() {
			ClientInfo = new Implementation {
				Name = "clio.mcp.e2e",
				Version = "1.0.0"
			}
		};
		if (elicitationHandler is not null) {
			options.Capabilities = new ClientCapabilities { Elicitation = new ElicitationCapability() };
			options.Handlers = new McpClientHandlers { ElicitationHandler = elicitationHandler };
		}

		McpClient client = await McpClient.CreateAsync(
			transport,
			options,
			NullLoggerFactory.Instance,
			cancellationToken);

		return new McpServerSession(client, transport);
	}

	/// <summary>
	/// Registers a RAW <c>notifications/progress</c> handler that captures the full notification
	/// <c>params</c> node (including <c>_meta</c>). The typed <see cref="IProgress{T}"/> overload of
	/// <see cref="CallToolAsync(string, IReadOnlyDictionary{string, object?}, IProgress{ProgressNotificationValue}, CancellationToken)"/>
	/// deserializes into <see cref="ProgressNotificationValue"/> and DROPS <c>_meta</c>, so a test that
	/// needs the typed <c>_meta.clioStageEvent</c> envelope must read the raw params captured here.
	/// Idempotent — registering more than once per session is a no-op.
	/// </summary>
	public void StartCapturingProgressNotifications() {
		if (_progressCaptureRegistered) {
			return;
		}

		_progressCaptureRegistered = true;
		_progressCaptureRegistration = Client.RegisterNotificationHandler(
			NotificationMethods.ProgressNotification, (notification, _) => {
			JsonNode? paramsNode = notification.Params?.DeepClone();
			if (paramsNode is not null) {
				_capturedProgressParams.Enqueue(paramsNode);
				_progressCapturedSignal.Release();
			}

			return default;
		});
	}

	/// <summary>
	/// The raw <c>params</c> nodes of every <c>notifications/progress</c> captured since
	/// <see cref="StartCapturingProgressNotifications"/> was called, in arrival order.
	/// </summary>
	public IReadOnlyList<JsonNode> CapturedProgressParams => [.. _capturedProgressParams];

	/// <summary>
	/// Waits for asynchronously dispatched progress notifications to satisfy <paramref name="condition"/>,
	/// returning the snapshot that satisfied the condition. Tool completion and notification dispatch use
	/// independent SDK continuations, so a completed call does not guarantee that the raw notification handler
	/// has drained its queue yet. Throws a diagnostic <see cref="TimeoutException"/> when the condition remains
	/// unsatisfied after one final snapshot at the timeout boundary.
	/// </summary>
	public async Task<IReadOnlyList<JsonNode>> WaitForCapturedProgressAsync(
		Func<IReadOnlyList<JsonNode>, bool> condition,
		TimeSpan timeout,
		CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(condition);
		if (timeout <= TimeSpan.Zero) {
			throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Progress wait timeout must be positive.");
		}

		Stopwatch stopwatch = Stopwatch.StartNew();
		while (true) {
			cancellationToken.ThrowIfCancellationRequested();
			IReadOnlyList<JsonNode> snapshot = CapturedProgressParams;
			if (condition(snapshot)) {
				return snapshot;
			}

			TimeSpan remaining = timeout - stopwatch.Elapsed;
			if (remaining <= TimeSpan.Zero
				|| !await _progressCapturedSignal.WaitAsync(remaining, cancellationToken)) {
				IReadOnlyList<JsonNode> finalSnapshot = CapturedProgressParams;
				if (condition(finalSnapshot)) {
					return finalSnapshot;
				}

				throw new TimeoutException(BuildProgressTimeoutMessage(timeout, finalSnapshot));
			}
		}
	}

	private static string BuildProgressTimeoutMessage(TimeSpan timeout, IReadOnlyList<JsonNode> snapshot) {
		string events = snapshot.Count == 0
			? "none"
			: string.Join(", ", snapshot.Select(DescribeProgressNotification));
		return $"Timed out after {timeout.TotalSeconds:0.###} seconds waiting for MCP progress condition. "
			+ $"Captured {snapshot.Count} notification(s): {events}.";
	}

	private static string DescribeProgressNotification(JsonNode node) {
		JsonNode? stageEvent = node["_meta"]?["clioStageEvent"];
		if (stageEvent is null) {
			return "untyped";
		}

		string eventType = stageEvent["eventType"]?.ToString() ?? "missing-event-type";
		string runId = stageEvent["runId"]?.ToString() ?? "missing-run-id";
		string sequence = stageEvent["sequence"]?.ToString() ?? "missing-sequence";
		string stageId = stageEvent["stage"]?["stageId"]?.ToString() ?? "none";
		string status = stageEvent["stage"]?["status"]?.ToString() ?? "none";
		string outcome = stageEvent["runCompleted"]?["outcome"]?.ToString() ?? "none";
		return $"{eventType}(runId={runId}, sequence={sequence}, stage={stageId}, status={status}, outcome={outcome})";
	}

	public async Task<IList<McpClientTool>> ListToolsAsync(CancellationToken cancellationToken) =>
		await Client.ListToolsAsync(cancellationToken: cancellationToken);

	public async Task<IList<McpClientResource>> ListResourcesAsync(CancellationToken cancellationToken) =>
		await Client.ListResourcesAsync(cancellationToken: cancellationToken);

	public async Task<ReadResourceResult> ReadResourceAsync(string uri, CancellationToken cancellationToken) =>
		await Client.ReadResourceAsync(uri, cancellationToken: cancellationToken);

	/// <summary>
	/// Invokes an MCP tool by its flat name, transparently honouring the lazy tool surface: a tool
	/// advertised in <c>tools/list</c> (resident) is called natively, while a hidden long-tail tool is
	/// dispatched through the <c>clio-run</c> executor with the equivalent
	/// <c>{"command":&lt;name&gt;,"args":{…}}</c> call shape. <c>clio-run</c> returns the target tool's
	/// <see cref="CallToolResult"/> verbatim (plus an out-of-band <c>_meta</c> audit entry), so
	/// envelope parsers observe the same payload either way.
	/// </summary>
	public async Task<CallToolResult> CallToolAsync(
		string toolName,
		IReadOnlyDictionary<string, object?> arguments,
		CancellationToken cancellationToken) {
		if (await IsToolAdvertisedAsync(toolName, cancellationToken)) {
			return await Client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);
		}
		return await Client.CallToolAsync(
			ClioRunTool.ToolName,
			BuildClioRunArguments(toolName, arguments),
			cancellationToken: cancellationToken);
	}

	/// <summary>
	/// Invokes a tool while forwarding the server's <c>notifications/progress</c> to
	/// <paramref name="progress"/>. The SDK generates a progress token for the request, so this
	/// overload is the way E2E tests observe the long-running heartbeat (ENG-91274). Applies the same
	/// resident-vs-<c>clio-run</c> routing as
	/// <see cref="CallToolAsync(string, IReadOnlyDictionary{string, object?}, CancellationToken)"/>;
	/// the retargeted request context keeps the caller's progress token, so heartbeats flow for
	/// dispatched long-tail tools too.
	/// </summary>
	public async Task<CallToolResult> CallToolAsync(
		string toolName,
		IReadOnlyDictionary<string, object?> arguments,
		IProgress<ProgressNotificationValue> progress,
		CancellationToken cancellationToken) {
		if (await IsToolAdvertisedAsync(toolName, cancellationToken)) {
			return await Client.CallToolAsync(toolName, arguments, progress: progress, cancellationToken: cancellationToken);
		}
		return await Client.CallToolAsync(
			ClioRunTool.ToolName,
			BuildClioRunArguments(toolName, arguments),
			progress: progress,
			cancellationToken: cancellationToken);
	}

	/// <summary>
	/// Invokes a tool by its BARE name with NO resident-vs-<c>clio-run</c> routing — the raw wire call
	/// an agent following static guidance would make. This is the entry point for testing the durable
	/// (forgiving) unmatched-name handler (ENG-93370), which must observe the unrouted name itself; the
	/// routed <see cref="CallToolAsync(string, IReadOnlyDictionary{string, object?}, CancellationToken)"/>
	/// would mask it behind <c>clio-run</c>.
	/// </summary>
	public async Task<CallToolResult> CallToolRawAsync(
		string toolName,
		IReadOnlyDictionary<string, object?> arguments,
		CancellationToken cancellationToken) =>
		await Client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);

	/// <summary>
	/// Invokes a tool with an explicit progress token while leaving the raw progress-notification
	/// handler as the sole handler for <c>notifications/progress</c>. This mirrors ClioRing's call path
	/// and preserves notification <c>_meta</c>, which the SDK's typed <c>progress:</c> overload drops.
	/// </summary>
	public async Task<CallToolResult> CallToolWithRawProgressAsync(
		string toolName,
		IReadOnlyDictionary<string, object?> arguments,
		CancellationToken cancellationToken) {
		RequestOptions options = new() {
			ProgressToken = new ProgressToken($"clio-mcp-e2e-{Guid.NewGuid():N}")
		};
		if (await IsToolAdvertisedAsync(toolName, cancellationToken)) {
			return await Client.CallToolAsync(
				toolName, arguments, options: options, cancellationToken: cancellationToken);
		}
		return await Client.CallToolAsync(
			ClioRunTool.ToolName,
			BuildClioRunArguments(toolName, arguments),
			options: options,
			cancellationToken: cancellationToken);
	}

	/// <summary>
	/// True when <paramref name="toolName"/> is advertised in <c>tools/list</c> (a resident tool on the
	/// lazy surface). The advertised set is fetched once per session — tool registration is fixed at
	/// server-process start, so the cache cannot go stale.
	/// </summary>
	public async Task<bool> IsToolAdvertisedAsync(string toolName, CancellationToken cancellationToken) {
		_advertisedToolNames ??= (await Client.ListToolsAsync(cancellationToken: cancellationToken))
			.Select(tool => tool.Name)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		return _advertisedToolNames.Contains(toolName);
	}

	/// <summary>
	/// Every tool name reachable through this server: the resident <c>tools/list</c> names unioned with
	/// the compact discovery index from <c>get-tool-contract</c> (the long tail dispatched via
	/// <c>clio-run</c>). This is the lazy-surface replacement for asserting flat advertisement — a tool
	/// "advertised" to an agent is one discoverable in the index, not necessarily resident.
	/// </summary>
	public async Task<IReadOnlyCollection<string>> ListReachableToolNamesAsync(CancellationToken cancellationToken) {
		if (_reachableToolNames is not null) {
			return _reachableToolNames;
		}
		IList<McpClientTool> tools = await Client.ListToolsAsync(cancellationToken: cancellationToken);
		HashSet<string> names = tools
			.Select(tool => tool.Name)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (ToolContractIndexEntry entry in await GetToolContractIndexAsync(cancellationToken)) {
			names.Add(entry.Name);
		}
		_reachableToolNames = names;
		return names;
	}

	/// <summary>
	/// The compact discovery index from <c>get-tool-contract</c> (no args), fetched once per session.
	/// Use it to assert per-tool discovery metadata (for example the <c>destructive</c> flag) for
	/// long-tail tools that no longer surface an MCP tool annotation via <c>tools/list</c>.
	/// </summary>
	public async Task<IReadOnlyList<ToolContractIndexEntry>> GetToolContractIndexAsync(CancellationToken cancellationToken) {
		if (_toolContractIndex is not null) {
			return _toolContractIndex;
		}
		CallToolResult indexResult = await Client.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?>(),
			cancellationToken: cancellationToken);
		ToolContractGetResponse indexResponse =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(indexResult);
		_toolContractIndex = indexResponse.Index ?? [];
		return _toolContractIndex;
	}

	// Maps a flat-name call onto the clio-run call shape. The common clio tool takes ONE record
	// parameter named `args`, so tests send {"args":{…}}; clio-run expects the TARGET tool's args
	// object (it re-wraps under the record parameter itself), so that inner object is forwarded.
	// Any other shape (multi-scalar parameters, or a deliberately malformed payload) is forwarded
	// as-is so clio-run's own binding surfaces the equivalent failure.
	private static Dictionary<string, object?> BuildClioRunArguments(
		string toolName,
		IReadOnlyDictionary<string, object?> arguments) {
		Dictionary<string, object?> wrapped = new() { ["command"] = toolName };
		// A call with NO top-level keys at all means the caller omitted the args wrapper entirely
		// (the "missing required args wrapper" binding-failure tests) — omit "args" here too so
		// clio-run's own dispatch fails binding the target's args record inside the retargeted
		// context, preserving that native failure instead of silently defaulting it. A call that
		// DOES carry an "args" key — even {"args":{}}, a deliberate no-op call with no properties —
		// forwards that value verbatim, so an intentionally empty args object still reaches the
		// target tool as an empty (not missing) object.
		if (arguments.Count == 0) {
			return wrapped;
		}
		wrapped["args"] = arguments.Count == 1 && arguments.TryGetValue("args", out object? inner)
			? inner
			: arguments;
		return wrapped;
	}

	public async ValueTask DisposeAsync() {
		if (_progressCaptureRegistration is not null) {
			await _progressCaptureRegistration.DisposeAsync();
		}
		await Client.DisposeAsync();
		_progressCapturedSignal.Dispose();
	}
}
