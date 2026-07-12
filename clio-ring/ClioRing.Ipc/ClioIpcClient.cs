using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ClioRing.Ipc;

/// <summary>
/// Default <see cref="IClioIpcClient"/>. Spawns one clio MCP child over stdio using the official
/// <see cref="StdioClientTransport"/> + <see cref="McpClient"/>, keeps it warm for the app session,
/// and exposes read-only catalog/contract/tool calls. All SDK usage lives here (a non-AOT project)
/// so the PublishAot app never sees the SDK's reflection-based trim/AOT warnings.
/// </summary>
public sealed class ClioIpcClient : IClioIpcClient {
	// Bounded graceful-shutdown window: close stdin, wait at most this long for the child to exit on
	// its own (~200ms in practice), then force-terminate the owned child. Also the SDK's own
	// ShutdownTimeout, so a Ring exit is never blocked longer than this.
	private static readonly TimeSpan ShutdownGrace = TimeSpan.FromMilliseconds(750);

	private readonly ClioIpcSettings _settings;
	private readonly Action<string>? _log;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly Dictionary<string, ClioToolCallResult> _contractCache = new(StringComparer.OrdinalIgnoreCase);
	private McpClient? _client;
	private Process? _child;
	private ClioServerHandshake? _handshake;
	private string? _cacheKey;
	private IReadOnlyList<ClioCatalogEntry>? _catalogCache;
	private volatile bool _disposed;

	/// <summary>
	/// Creates a client bound to the given launch settings. The child is not spawned until first use.
	/// </summary>
	/// <param name="settings">How to launch the clio MCP child.</param>
	/// <param name="log">Optional sink for lifecycle diagnostics (for example the app's startup log).</param>
	public ClioIpcClient(ClioIpcSettings settings, Action<string>? log = null) {
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		_log = log;
	}

	/// <inheritdoc />
	public bool IsConnected => _client is not null && _child is { HasExited: false };

	/// <inheritdoc />
	public ClioServerHandshake? Handshake => _handshake;

	/// <inheritdoc />
	public string TargetPath => ResolveTargetPath(_settings);

	/// <inheritdoc />
	public bool LastCatalogIsModern { get; private set; }

	// The most meaningful launch identity: the clio dll argument if present, else the raw command.
	private static string ResolveTargetPath(ClioIpcSettings settings) {
		string? dll = settings.Args.FirstOrDefault(a => a.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
		return dll ?? settings.Command;
	}

	/// <inheritdoc />
	public event EventHandler? Disconnected;

	/// <inheritdoc />
	public async Task<ClioServerHandshake> ConnectAsync(CancellationToken cancellationToken = default) {
		ObjectDisposedException.ThrowIf(_disposed, this);
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try {
			if (_client is not null && _child is { HasExited: false }) {
				return _handshake!;
			}
			await SpawnAndHandshakeLockedAsync(cancellationToken).ConfigureAwait(false);
			return _handshake!;
		}
		finally {
			_gate.Release();
		}
	}

	/// <inheritdoc />
	public async Task<ClioServerHandshake> RestartAsync(CancellationToken cancellationToken = default) {
		ObjectDisposedException.ThrowIf(_disposed, this);
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try {
			await TeardownLockedAsync().ConfigureAwait(false);
			await SpawnAndHandshakeLockedAsync(cancellationToken).ConfigureAwait(false);
			return _handshake!;
		}
		finally {
			_gate.Release();
		}
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<ClioCatalogEntry>> GetCatalogAsync(CancellationToken cancellationToken = default) {
		await ConnectAsync(cancellationToken).ConfigureAwait(false);
		// Cache keyed by (executable path + server version): re-opening the view is instant, and a
		// version change (a different dll/build after RestartAsync) invalidates it at connect time.
		if (_catalogCache is not null) {
			return _catalogCache;
		}

		// Newer clio returns the compact index for bare {}; older builds require the {"args":{}} wrapper.
		// Try the documented shape first, then fall back so the proof works across clio versions.
		ClioToolCallResult result = await CallToolAsync("get-tool-contract", "{}", cancellationToken).ConfigureAwait(false);
		(IReadOnlyList<ClioCatalogEntry> entries, bool modern) = ParseCatalog(result.RawText);
		if (entries.Count == 0) {
			result = await CallToolAsync("get-tool-contract", "{\"args\":{}}", cancellationToken).ConfigureAwait(false);
			(entries, modern) = ParseCatalog(result.RawText);
		}
		LastCatalogIsModern = modern;
		_catalogCache = entries;
		return entries;
	}

	/// <inheritdoc />
	public async Task<ClioToolCallResult> GetToolContractAsync(string name, CancellationToken cancellationToken = default) {
		if (string.IsNullOrWhiteSpace(name)) {
			throw new ArgumentException("Tool name is required.", nameof(name));
		}
		await ConnectAsync(cancellationToken).ConfigureAwait(false);
		if (_contractCache.TryGetValue(name, out ClioToolCallResult? cached)) {
			return cached;
		}
		// get-tool-contract requires parameters wrapped in an "args" object; a single tool's full
		// contract is requested via "tool-names":[<name>]. Built as an escaped literal (not
		// JsonSerializer.Serialize) so it is safe under the host's AOT-disabled reflection serializer.
		string escaped = JsonEncodedText.Encode(name).ToString();
		string args = $"{{\"args\":{{\"tool-names\":[\"{escaped}\"]}}}}";
		ClioToolCallResult result = await CallToolAsync("get-tool-contract", args, cancellationToken).ConfigureAwait(false);
		if (!result.IsError) {
			_contractCache[name] = result;
		}
		return result;
	}

	/// <inheritdoc />
	public Task<ClioToolCallResult> CallToolAsync(string name, string? argumentsJson, CancellationToken cancellationToken = default) =>
		CallToolAsync(name, argumentsJson, progress: null, cancellationToken);

	/// <inheritdoc />
	public async Task<ClioToolCallResult> CallToolAsync(string name, string? argumentsJson, IProgress<string>? progress, CancellationToken cancellationToken = default) {
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (string.IsNullOrWhiteSpace(name)) {
			throw new ArgumentException("Tool name is required.", nameof(name));
		}

		await ConnectAsync(cancellationToken).ConfigureAwait(false);
		McpClient client = _client ?? throw new InvalidOperationException("clio MCP client is not connected.");
		IReadOnlyDictionary<string, object?> arguments = ParseArguments(argumentsJson);

		IProgress<ProgressNotificationValue>? sdkProgress = progress is null
			? null
			: new ProgressAdapter(progress);

		try {
			CallToolResult result = await client.CallToolAsync(name, arguments, progress: sdkProgress, cancellationToken: cancellationToken)
				.ConfigureAwait(false);
			return ToResult(result);
		}
		catch (Exception ex) when (ex is not OperationCanceledException) {
			// A dead child surfaces here; mark disconnected so the UI/harness can offer a restart.
			MarkDisconnected();
			throw;
		}
	}

	/// <inheritdoc />
	public async Task<ClioToolCallResult> CallToolAsync(string name, string? argumentsJson, IProgress<ClioStageEvent>? stageProgress, CancellationToken cancellationToken = default) {
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (string.IsNullOrWhiteSpace(name)) {
			throw new ArgumentException("Tool name is required.", nameof(name));
		}

		await ConnectAsync(cancellationToken).ConfigureAwait(false);
		McpClient client = _client ?? throw new InvalidOperationException("clio MCP client is not connected.");
		IReadOnlyDictionary<string, object?> arguments = ParseArguments(argumentsJson);

		// No typed sink -> nothing to correlate; fall back to a plain call (no progressToken issued).
		if (stageProgress is null) {
			return await InvokeToolAsync(client, name, arguments, sdkProgress: null, options: null, cancellationToken).ConfigureAwait(false);
		}

		// Issue our OWN progressToken so the raw handler can correlate this call's notifications and
		// reject foreign/concurrent runs (AC-05). Reading params._meta requires the raw handler because
		// the SDK's IProgress<ProgressNotificationValue> path drops _meta (ADR fact 6).
		string token = $"clio-ring-{Guid.NewGuid():N}";
		var adapter = new ClioStageEventAdapter(stageProgress, token);
		var options = new RequestOptions { ProgressToken = new ProgressToken(token) };

		IAsyncDisposable registration = client.RegisterNotificationHandler(
			ProgressNotificationMethod,
			(JsonRpcNotification notification, CancellationToken _) => {
				// The adapter is total: it swallows a missing/malformed _meta and never throws, so a bad
				// beat can never surface as an unobserved exception on the SDK's dispatch loop.
				adapter.Consume(notification.Params);
				return ValueTask.CompletedTask;
			});

		await using (registration.ConfigureAwait(false)) {
			return await InvokeToolAsync(client, name, arguments, sdkProgress: null, options, cancellationToken).ConfigureAwait(false);
		}
	}

	// Single call site for the SDK CallToolAsync + shared dead-child handling.
	private async Task<ClioToolCallResult> InvokeToolAsync(
		McpClient client,
		string name,
		IReadOnlyDictionary<string, object?> arguments,
		IProgress<ProgressNotificationValue>? sdkProgress,
		RequestOptions? options,
		CancellationToken cancellationToken) {
		try {
			CallToolResult result = await client.CallToolAsync(name, arguments, progress: sdkProgress, options: options, cancellationToken: cancellationToken)
				.ConfigureAwait(false);
			return ToResult(result);
		}
		catch (Exception ex) when (ex is not OperationCanceledException) {
			// A dead child surfaces here; mark disconnected so the UI/harness can offer a restart.
			MarkDisconnected();
			throw;
		}
	}

	// JSON-RPC method for server->client progress notifications (MCP spec / SDK NotificationMethods).
	private const string ProgressNotificationMethod = "notifications/progress";

	// Maps the SDK's structured progress notifications to human-readable lines for the UI/harness.
	private sealed class ProgressAdapter(IProgress<string> sink) : IProgress<ProgressNotificationValue> {
		public void Report(ProgressNotificationValue value) {
			string message = string.IsNullOrWhiteSpace(value.Message) ? "working…" : value.Message;
			string line = value.Total is > 0
				? $"[{value.Progress:0}/{value.Total:0}] {message}"
				: message;
			sink.Report(line);
		}
	}

	/// <inheritdoc />
	public async Task PingAsync(CancellationToken cancellationToken = default) {
		ObjectDisposedException.ThrowIf(_disposed, this);
		await ConnectAsync(cancellationToken).ConfigureAwait(false);
		McpClient client = _client ?? throw new InvalidOperationException("clio MCP client is not connected.");
		try {
			await client.PingAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is not OperationCanceledException) {
			MarkDisconnected();
			throw;
		}
	}

	/// <summary>
	/// Proof-harness only: forcibly kills the child process tree to simulate an unexpected crash, without
	/// tearing down the client state (so the next call/restart observes the death the way it would in the
	/// wild). Returns the killed child's PID, or null if there was no live child to kill.
	/// </summary>
	internal int? SimulateChildCrash() {
		Process? child = _child;
		if (child is null || child.HasExited) {
			return null;
		}
		int pid = child.Id;
		try {
			child.Kill(entireProcessTree: true);
			child.WaitForExit(5000);
		}
		catch (Exception) {
			// Race: it may have exited on its own.
		}
		return pid;
	}

	// ---- internals (all hold the gate unless noted) ----

	private async Task SpawnAndHandshakeLockedAsync(CancellationToken cancellationToken) {
		HashSet<int> before = SnapshotCandidatePids();

		var transport = new StdioClientTransport(new StdioClientTransportOptions {
			Command = _settings.Command,
			Arguments = _settings.Args.ToArray(),
			WorkingDirectory = _settings.WorkingDirectory,
			Name = "clio-ring-ipc",
			// Keep the SDK's own force-terminate window small; the owned-child bounded shutdown below is
			// the primary control, but this guarantees the SDK never blocks a Ring exit beyond the grace.
			ShutdownTimeout = ShutdownGrace
		}, NullLoggerFactory.Instance);

		var options = new McpClientOptions {
			ClientInfo = new Implementation { Name = "clio-ring", Version = "0.1.0-spike" }
		};

		McpClient client = await McpClient.CreateAsync(transport, options, NullLoggerFactory.Instance, cancellationToken)
			.ConfigureAwait(false);

		_client = client;
		_child = ResolveChildProcess(before);
		HookChildExit(_child);

		IReadOnlyList<string> caps = DescribeCapabilities(client.ServerCapabilities);
		string version = client.ServerInfo?.Version ?? "unknown";
		_handshake = new ClioServerHandshake {
			ServerName = client.ServerInfo?.Name ?? "unknown",
			ServerVersion = version,
			ProtocolVersion = client.NegotiatedProtocolVersion,
			Capabilities = caps,
			Instructions = client.ServerInstructions
		};

		// Invalidate the contract/catalog caches when the (executable path + version) key changes — for
		// example after a RestartAsync that picked up a newer clio build.
		string key = $"{TargetPath}|{version}";
		if (!string.Equals(key, _cacheKey, StringComparison.Ordinal)) {
			_cacheKey = key;
			_catalogCache = null;
			_contractCache.Clear();
			LastCatalogIsModern = false;
		}
	}

	// Bounded, non-blocking teardown: start the SDK dispose (closes stdin -> graceful EOF), wait at most
	// the grace window for the OWNED child to exit on its own, then force-terminate only that child.
	// Records the outcome (graceful vs forced) + elapsed to the log sink. A Ring exit never blocks past
	// the grace window.
	private async Task TeardownLockedAsync() {
		McpClient? client = _client;
		Process? child = _child;
		_client = null;
		_child = null;
		_handshake = null;

		if (client is null && child is null) {
			return;
		}

		var sw = Stopwatch.StartNew();
		int? pid = TryGetPid(child);

		// Fire the dispose (closes stdin); observe its exception without blocking on its full window.
		if (client is not null) {
			Task disposeTask = client.DisposeAsync().AsTask();
			_ = disposeTask.ContinueWith(static t => { _ = t.Exception; }, TaskScheduler.Default);
		}

		bool graceful = false;
		if (child is not null) {
			try {
				await Task.WhenAny(child.WaitForExitAsync(), Task.Delay(ShutdownGrace)).ConfigureAwait(false);
			}
			catch (Exception) {
				// WaitForExitAsync can throw if the handle is gone; treated as exited below.
			}
			graceful = SafeHasExited(child);
		}
		else if (client is not null) {
			// No owned child handle: bound the dispose wait directly.
			graceful = true;
		}

		bool forced = false;
		if (child is not null && !SafeHasExited(child)) {
			KillProcessTree(child);
			forced = true;
		}
		else {
			child?.Dispose();
		}

		sw.Stop();
		_log?.Invoke($"ipc shutdown: outcome={(forced ? "forced" : "graceful")} elapsedMs={sw.ElapsedMilliseconds} pid={pid?.ToString() ?? "n/a"}");
	}

	private static int? TryGetPid(Process? p) {
		try {
			return p?.Id;
		}
		catch (Exception) {
			return null;
		}
	}

	private static bool SafeHasExited(Process p) {
		try {
			return p.HasExited;
		}
		catch (Exception) {
			return true;
		}
	}

	private void MarkDisconnected() {
		if (_child is { HasExited: true } || _client is null) {
			Disconnected?.Invoke(this, EventArgs.Empty);
		}
	}

	private void HookChildExit(Process? child) {
		if (child is null) {
			return;
		}
		try {
			child.EnableRaisingEvents = true;
			child.Exited += (_, _) => Disconnected?.Invoke(this, EventArgs.Empty);
		}
		catch (Exception) {
			// The child may have exited between resolve and hook; call-time detection still covers it.
		}
	}

	// Best-effort child PID resolution: diff the candidate-process snapshot taken before spawn against
	// after. StdioClientTransport owns the process and exposes no handle, so this is how we get one for
	// exit hooking / simulated-kill in the proof harness. Returns null if it cannot be isolated.
	private HashSet<int> SnapshotCandidatePids() {
		string procName = CandidateProcessName();
		try {
			return Process.GetProcessesByName(procName).Select(p => p.Id).ToHashSet();
		}
		catch (Exception) {
			return new HashSet<int>();
		}
	}

	private Process? ResolveChildProcess(HashSet<int> before) {
		string procName = CandidateProcessName();
		try {
			Process[] now = Process.GetProcessesByName(procName);
			Process? candidate = now
				.Where(p => !before.Contains(p.Id))
				.OrderBy(p => SafeStartTime(p))
				.FirstOrDefault();
			foreach (Process p in now) {
				if (!ReferenceEquals(p, candidate)) {
					p.Dispose();
				}
			}
			return candidate;
		}
		catch (Exception) {
			return null;
		}
	}

	private string CandidateProcessName() {
		string command = _settings.Command;
		string baseName = System.IO.Path.GetFileNameWithoutExtension(command);
		return string.IsNullOrEmpty(baseName) ? command : baseName;
	}

	private static DateTime SafeStartTime(Process p) {
		try {
			return p.StartTime;
		}
		catch (Exception) {
			return DateTime.MaxValue;
		}
	}

	private static void KillProcessTree(Process? child) {
		if (child is null) {
			return;
		}
		try {
			if (!child.HasExited) {
				child.Kill(entireProcessTree: true);
				child.WaitForExit(5000);
			}
		}
		catch (Exception) {
			// Already exited / access race; nothing more to do.
		}
		finally {
			child.Dispose();
		}
	}

	private static IReadOnlyList<string> DescribeCapabilities(ServerCapabilities? capabilities) {
		if (capabilities is null) {
			return Array.Empty<string>();
		}
		var caps = new List<string>();
		if (capabilities.Tools is not null) { caps.Add("tools"); }
		if (capabilities.Resources is not null) { caps.Add("resources"); }
		if (capabilities.Prompts is not null) { caps.Add("prompts"); }
		if (capabilities.Logging is not null) { caps.Add("logging"); }
		if (capabilities.Completions is not null) { caps.Add("completions"); }
		return caps;
	}

	private static IReadOnlyDictionary<string, object?> ParseArguments(string? argumentsJson) {
		if (string.IsNullOrWhiteSpace(argumentsJson)) {
			return new Dictionary<string, object?>();
		}
		using JsonDocument doc = JsonDocument.Parse(argumentsJson);
		if (doc.RootElement.ValueKind != JsonValueKind.Object) {
			throw new ArgumentException("Tool arguments must be a JSON object.", nameof(argumentsJson));
		}
		var map = new Dictionary<string, object?>(StringComparer.Ordinal);
		foreach (JsonProperty prop in doc.RootElement.EnumerateObject()) {
			// Clone so the value outlives the disposed JsonDocument; the SDK re-serializes it.
			map[prop.Name] = prop.Value.Clone();
		}
		return map;
	}

	private static ClioToolCallResult ToResult(CallToolResult result) {
		string text = string.Join(
			Environment.NewLine,
			result.Content.OfType<TextContentBlock>().Select(block => block.Text));

		string? compactJson = null;
		string trimmed = text.TrimStart();
		if (trimmed.StartsWith('{') || trimmed.StartsWith('[')) {
			try {
				using JsonDocument doc = JsonDocument.Parse(text);
				compactJson = doc.RootElement.GetRawText();
			}
			catch (JsonException) {
				compactJson = null;
			}
		}

		return new ClioToolCallResult {
			RawText = text,
			Json = compactJson,
			IsError = result.IsError ?? false,
			HasStructuredContent = result.StructuredContent is not null
		};
	}

	// Returns the parsed entries and whether they came from the modern compact "index" shape (with
	// safety flags). The old "tools" shape (or an empty/unparseable payload) is reported as not-modern
	// so the UI can surface an "incompatible clio" state instead of a silent partial catalog.
	private static (IReadOnlyList<ClioCatalogEntry> Entries, bool Modern) ParseCatalog(string rawText) {
		if (string.IsNullOrWhiteSpace(rawText)) {
			return (Array.Empty<ClioCatalogEntry>(), false);
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(rawText);
			JsonElement root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object) {
				return (Array.Empty<ClioCatalogEntry>(), false);
			}
			// Newer clio: { "index": [ {name, purpose, contract-available, resident, destructive} ] }.
			// Older clio: { "tools": [ {name, description, ...} ] } (full contracts, no safety flags).
			bool fromIndex = root.TryGetProperty("index", out JsonElement array) && array.ValueKind == JsonValueKind.Array;
			if (!fromIndex && (!root.TryGetProperty("tools", out array) || array.ValueKind != JsonValueKind.Array)) {
				return (Array.Empty<ClioCatalogEntry>(), false);
			}
			var entries = new List<ClioCatalogEntry>(array.GetArrayLength());
			foreach (JsonElement item in array.EnumerateArray()) {
				if (item.ValueKind != JsonValueKind.Object) {
					continue;
				}
				entries.Add(new ClioCatalogEntry {
					Name = ReadString(item, "name") ?? string.Empty,
					Purpose = ReadString(item, "purpose") ?? ReadString(item, "description") ?? string.Empty,
					ContractAvailable = ReadBool(item, "contract-available", "contractAvailable"),
					Resident = ReadBool(item, "resident"),
					Destructive = ReadBool(item, "destructive")
				});
			}
			return (entries, fromIndex);
		}
		catch (JsonException) {
			return (Array.Empty<ClioCatalogEntry>(), false);
		}
	}

	private static string? ReadString(JsonElement obj, string name) =>
		obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

	private static bool ReadBool(JsonElement obj, params string[] names) {
		foreach (string name in names) {
			if (obj.TryGetProperty(name, out JsonElement el)) {
				if (el.ValueKind == JsonValueKind.True) { return true; }
				if (el.ValueKind == JsonValueKind.False) { return false; }
			}
		}
		return false;
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync() {
		if (_disposed) {
			return;
		}
		_disposed = true;
		await _gate.WaitAsync().ConfigureAwait(false);
		try {
			await TeardownLockedAsync().ConfigureAwait(false);
		}
		finally {
			_gate.Release();
			_gate.Dispose();
		}
	}
}
