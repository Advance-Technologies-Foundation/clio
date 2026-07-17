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
	private readonly object _activeCallsSync = new();
	private readonly Dictionary<string, ClioToolCallResult> _contractCache = new(StringComparer.OrdinalIgnoreCase);
	private McpClient? _client;
	private ClioServerHandshake? _handshake;
	private string? _cacheKey;
	private IReadOnlyList<ClioCatalogEntry>? _catalogCache;
	private TaskCompletionSource<bool>? _activeCallsDrained;
	private int _activeCalls;
	private bool _lifecycleChanging;
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
	public bool IsConnected => _client is not null && _handshake is not null;

	/// <inheritdoc />
	public ClioServerHandshake? Handshake => _handshake;

	/// <inheritdoc />
	public string TargetPath => ResolveTargetPath(_settings);

	/// <inheritdoc />
	public bool LastCatalogIsModern { get; private set; }

	// The most meaningful launch identity: the clio dll argument if present, else the raw command.
	private static string ResolveTargetPath(ClioIpcSettings settings) {
		string? dll = settings.Args.FirstOrDefault(a => a?.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) == true);
		return dll ?? settings.Command;
	}

	/// <inheritdoc />
	public event EventHandler? Disconnected;

	/// <inheritdoc />
	public event EventHandler? ConnectionChanged;

	/// <inheritdoc />
	public async Task<ClioServerHandshake> ConnectAsync(CancellationToken cancellationToken = default) {
		ObjectDisposedException.ThrowIf(_disposed, this);
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try {
			if (_client is not null && _handshake is not null) {
				return _handshake!;
			}
			if (_client is not null) {
				await TeardownLockedAsync().ConfigureAwait(false);
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
		lock (_activeCallsSync) {
			if (_activeCalls != 0) {
				throw new InvalidOperationException("clio MCP cannot restart while a tool call is active.");
			}
			_lifecycleChanging = true;
		}
		bool gateHeld = false;
		try {
			await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
			gateHeld = true;
			await TeardownLockedAsync().ConfigureAwait(false);
			await SpawnAndHandshakeLockedAsync(cancellationToken).ConfigureAwait(false);
			return _handshake!;
		}
		finally {
			if (gateHeld) {
				_gate.Release();
			}
			lock (_activeCallsSync) {
				_lifecycleChanging = false;
			}
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

		BeginActiveCall();
		try {
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
		finally {
			EndActiveCall();
		}
	}

	/// <inheritdoc />
	public async Task<ClioToolCallResult> CallToolAsync(string name, string? argumentsJson, IProgress<ClioStageEvent>? stageProgress, CancellationToken cancellationToken = default) {
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (string.IsNullOrWhiteSpace(name)) {
			throw new ArgumentException("Tool name is required.", nameof(name));
		}

		BeginActiveCall();
		try {
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
		finally {
			EndActiveCall();
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
		BeginActiveCall();
		try {
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
		finally {
			EndActiveCall();
		}
	}

	// ---- internals (all hold the gate unless noted) ----

	private async Task SpawnAndHandshakeLockedAsync(CancellationToken cancellationToken) {
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

		IReadOnlyList<string> caps = DescribeCapabilities(client.ServerCapabilities);
		string version = client.ServerInfo?.Version ?? "unknown";
		_handshake = new ClioServerHandshake {
			ServerName = client.ServerInfo?.Name ?? "unknown",
			ServerVersion = version,
			ProtocolVersion = client.NegotiatedProtocolVersion,
			Capabilities = caps,
			Instructions = client.ServerInstructions
		};
		ConnectionChanged?.Invoke(this, EventArgs.Empty);

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

	// The transport owns the exact child it created. Awaiting client disposal delegates graceful shutdown
	// and its bounded force-termination fallback to that owner; Ring must never infer ownership by process name.
	private async Task TeardownLockedAsync() {
		McpClient? client = _client;
		_client = null;
		bool hadHandshake = _handshake is not null;
		_handshake = null;
		if (hadHandshake) {
			ConnectionChanged?.Invoke(this, EventArgs.Empty);
		}

		if (client is null) {
			return;
		}

		var sw = Stopwatch.StartNew();
		await client.DisposeAsync().ConfigureAwait(false);
		sw.Stop();
		_log?.Invoke($"ipc shutdown: transport-owned elapsedMs={sw.ElapsedMilliseconds}");
	}

	private void MarkDisconnected() {
		_handshake = null;
		ConnectionChanged?.Invoke(this, EventArgs.Empty);
		Disconnected?.Invoke(this, EventArgs.Empty);
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

	private void BeginActiveCall() {
		lock (_activeCallsSync) {
			if (_lifecycleChanging || _disposed) {
				throw new InvalidOperationException("clio MCP is restarting or shutting down.");
			}
			if (_activeCalls == 0) {
				_activeCallsDrained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			}
			_activeCalls++;
		}
	}

	private void EndActiveCall() {
		TaskCompletionSource<bool>? drained = null;
		lock (_activeCallsSync) {
			_activeCalls--;
			if (_activeCalls == 0) {
				drained = _activeCallsDrained;
				_activeCallsDrained = null;
			}
		}
		drained?.TrySetResult(true);
	}

	private Task WaitForActiveCallsAsync() {
		lock (_activeCallsSync) {
			return _activeCalls == 0 ? Task.CompletedTask : _activeCallsDrained!.Task;
		}
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync() {
		if (_disposed) {
			return;
		}
		_disposed = true;
		lock (_activeCallsSync) {
			_lifecycleChanging = true;
		}
		await WaitForActiveCallsAsync().ConfigureAwait(false);
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
