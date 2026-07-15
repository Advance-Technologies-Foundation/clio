using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ClioRing.Ipc;

/// <summary>
/// A long-lived client for a single clio MCP child process spoken to over stdio (JSON-RPC 2.0).
/// One child per client for the whole app session: the child is spawned lazily on first use, the
/// <c>initialize</c> handshake is performed once, and subsequent calls reuse the warm process.
/// All members are asynchronous and must never block a UI thread.
/// </summary>
public interface IClioIpcClient : IAsyncDisposable {
	/// <summary>True once the child is spawned and the handshake has completed successfully.</summary>
	bool IsConnected { get; }

	/// <summary>The negotiated handshake (server name/version/capabilities), or null before connecting.</summary>
	ClioServerHandshake? Handshake { get; }

	/// <summary>
	/// The self-identifying launch target — the connected clio dll path (or the raw command when no dll
	/// argument is present). Used with the server version to show the connection state.
	/// </summary>
	string TargetPath { get; }

	/// <summary>
	/// True when the last <see cref="GetCatalogAsync"/> parsed the modern compact <c>index</c> (with
	/// destructive/resident safety flags). False when the catalog was empty or came back in the old
	/// <c>tools</c> shape — signalling an incompatible clio build to the UI.
	/// </summary>
	bool LastCatalogIsModern { get; }

	/// <summary>Raised when the child process exits unexpectedly (marks the client disconnected).</summary>
	event EventHandler? Disconnected;

	/// <summary>
	/// Ensures the child is spawned and the <c>initialize</c> handshake has completed, returning the
	/// negotiated <see cref="ClioServerHandshake"/>. Idempotent: a second call while already connected
	/// returns the cached handshake without respawning.
	/// </summary>
	Task<ClioServerHandshake> ConnectAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Fetches the full command catalog via <c>get-tool-contract</c> with empty arguments (the complete
	/// ~140-tool surface, not the resident <c>tools/list</c> subset). Connects first if needed.
	/// </summary>
	Task<IReadOnlyList<ClioCatalogEntry>> GetCatalogAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Fetches a single tool's full contract/schema via <c>get-tool-contract {name}</c>, returned as the
	/// raw JSON payload. Connects first if needed.
	/// </summary>
	Task<ClioToolCallResult> GetToolContractAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>
	/// Invokes an MCP tool by name with the given JSON arguments object (for example <c>{}</c> or
	/// <c>{"name":"ve"}</c>) and returns the parsed result. Connects first if needed. READ-ONLY use in
	/// the proof — destructive tools are intentionally not wired here.
	/// </summary>
	/// <param name="name">The flat tool name (for example <c>list-environments</c>).</param>
	/// <param name="argumentsJson">A JSON object literal for the tool arguments; null/empty means <c>{}</c>.</param>
	/// <param name="cancellationToken">Cancels the call.</param>
	Task<ClioToolCallResult> CallToolAsync(string name, string? argumentsJson, CancellationToken cancellationToken = default);

	/// <summary>
	/// Same as <see cref="CallToolAsync(string, string?, CancellationToken)"/> but forwards the server's
	/// <c>notifications/progress</c> to <paramref name="progress"/> as human-readable lines. NOTE: these
	/// are keep-alive HEARTBEAT/activity beats (liveness for a long op), NOT incremental command stdout —
	/// the tool's actual payload arrives once, in the final result. Cancelling requests cancellation, but
	/// the underlying clio operation is DETACHED — it cannot be truly aborted mid-flight; cancellation
	/// only stops the ring from waiting.
	/// </summary>
	Task<ClioToolCallResult> CallToolAsync(string name, string? argumentsJson, IProgress<string>? progress, CancellationToken cancellationToken = default);

	/// <summary>
	/// Same as <see cref="CallToolAsync(string, string?, CancellationToken)"/> but consumes the server's
	/// structured stage stream: it issues an explicit <c>progressToken</c>, registers a raw
	/// <c>notifications/progress</c> handler for the call duration, reads the typed envelope from
	/// <c>params._meta.clioStageEvent</c> (which the SDK's <c>IProgress&lt;ProgressNotificationValue&gt;</c>
	/// path drops — ADR fact 6), and reports each event to <paramref name="stageProgress"/>. Notifications
	/// for a foreign run are ignored by token; duplicate/out-of-order <c>sequence</c> per <c>runId</c> is
	/// dropped; unknown fields and a malformed envelope are tolerated (never throws on a bad beat). This is
	/// additive — the existing <see cref="CallToolAsync(string, string?, IProgress{string}?, CancellationToken)"/>
	/// string overload is unchanged.
	/// </summary>
	/// <param name="name">The flat tool name (for example <c>deploy-creatio</c>).</param>
	/// <param name="argumentsJson">A JSON object literal for the tool arguments; null/empty means <c>{}</c>.</param>
	/// <param name="stageProgress">Sink for decoded, de-duplicated typed stage events.</param>
	/// <param name="cancellationToken">Cancels the call.</param>
	Task<ClioToolCallResult> CallToolAsync(string name, string? argumentsJson, IProgress<ClioStageEvent>? stageProgress, CancellationToken cancellationToken = default);

	/// <summary>
	/// Kills any existing child and spawns a fresh one, performing the handshake again. Used to recover
	/// after the child dies (or on demand). Returns the new handshake.
	/// </summary>
	Task<ClioServerHandshake> RestartAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Sends a bare MCP <c>ping</c> round-trip on the warm channel (no server-side work). This is the
	/// steady-state protocol round-trip time — the closest measurable proxy for the <c>initialize</c>
	/// handshake RTT, which the stdio transport fuses with process spawn and cannot report alone.
	/// Connects first if needed.
	/// </summary>
	Task PingAsync(CancellationToken cancellationToken = default);
}
