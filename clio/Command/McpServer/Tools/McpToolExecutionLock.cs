using Clio.Command.McpServer;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Composition facade over the per-tenant execution lock (FR-05) and the session-container in-flight
/// guard (FR-08 wiring, Story 9). Replaces the former single global <c>SyncRoot</c> object: callers now
/// acquire a lock keyed by the credential-discriminating cache key (from
/// <see cref="IToolCommandResolver.GetTenantKey"/>), so different tenants no longer serialize while the
/// same tenant still does.
/// </summary>
/// <remarks>
/// A static facade (not constructor injection) is used deliberately: <c>BaseTool&lt;T&gt;</c> is the
/// base of ~60 MCP tool types and <c>ExecuteWithCleanLog</c> has no options in scope, so threading two
/// extra services through every subclass constructor would be high-churn for no behavioral gain. The
/// lock provider is the process-wide <see cref="TenantExecutionLockProvider.Shared"/> (also the
/// DI-registered singleton), so it needs no runtime configuration. The session cache IS host-specific
/// (stdio default vs the mcp-http run-time-configured instance) and is wired once at host startup via
/// <see cref="Configure"/>; before that (e.g. in unit tests that new-up tools directly) the mark
/// operations are safe no-ops.
/// </remarks>
internal static class McpToolExecutionLock {

	// Stable lock key for tool executions that carry no per-tenant identity (environment-less commands
	// and the env-insensitive injected-command path). These share no authenticated Creatio session, so
	// a single shared lock is correct and never serializes DIFFERENT real tenants (which use their own
	// credential-derived keys).
	internal const string SharedFallbackKey = "__mcp_shared_fallback__";

	/// <summary>
	/// Single process-global lock guarding every region that READS or WRITES the process working
	/// directory (H1/H2, ENG-93208). The current directory is process-global state, so once different
	/// tenants run concurrently (per-tenant lock) a workspace tool that pins cwd could otherwise place
	/// another tenant's output under the pinned workspace, and two writers could corrupt each other's
	/// save/restore. Every cwd reader (page-anchor resolution) and writer (workspace pin) acquires THIS
	/// one lock around its cwd-sensitive region. It is deliberately NOT a per-tenant lock: cwd is
	/// process-global and these are local-workspace ops, not the multi-tenant hot path, so serializing
	/// them is acceptable.
	/// <para>
	/// <b>Deadlock ordering (single global order): per-tenant lock → CwdLock, NEVER the reverse.</b> A
	/// tool that already holds its per-tenant lock (a command running under
	/// <c>ExecuteUnderTenantLock</c> / <c>InternalExecute</c>, or the page-sync batch) may then take
	/// <see cref="CwdLock"/>. No path may take <see cref="CwdLock"/> and THEN acquire a per-tenant lock
	/// for a different key. Workspace writers therefore acquire their per-tenant lock first (with the
	/// same key their inner command resolves under, so the inner acquire is a reentrant no-op) and only
	/// then take <see cref="CwdLock"/> around the pin/execute/restore region.
	/// </para>
	/// </summary>
	internal static readonly object CwdLock = new();

	private static ITenantExecutionLockProvider _lockProvider = TenantExecutionLockProvider.Shared;
	private static ISessionContainerCache _sessionContainerCache;

	/// <summary>
	/// Wires the facade to the host's DI-registered lock provider and session cache. Called once at MCP
	/// host startup (stdio and mcp-http). Passing <see langword="null"/> for either argument leaves the
	/// current value in place.
	/// </summary>
	/// <param name="lockProvider">The DI-registered tenant execution lock provider.</param>
	/// <param name="sessionContainerCache">The session-container cache whose entries must be marked in-use during a call.</param>
	internal static void Configure(
		ITenantExecutionLockProvider lockProvider, ISessionContainerCache sessionContainerCache) {
		if (lockProvider is not null) {
			_lockProvider = lockProvider;
		}
		if (sessionContainerCache is not null) {
			_sessionContainerCache = sessionContainerCache;
		}
	}

	/// <summary>
	/// Returns the per-tenant execution lock for <paramref name="cacheKey"/>. A null/blank key
	/// (e.g. an environment-less command, or a test double whose resolver returns no key) normalizes to
	/// the single shared fallback lock rather than throwing.
	/// </summary>
	internal static object GetLock(string cacheKey) =>
		_lockProvider.GetLock(Normalize(cacheKey));

	/// <summary>
	/// Marks <paramref name="cacheKey"/> as in-flight: on the lock provider (so a held lock mapping is
	/// never evicted — bounds the lock map without breaking mutual exclusion, M1) and, for real tenants,
	/// on the session-container cache (so eviction cannot dispose the container mid-call).
	/// </summary>
	internal static void MarkInUse(string cacheKey) {
		string key = Normalize(cacheKey);
		_lockProvider.MarkInUse(key);
		if (!IsFallback(key)) {
			_sessionContainerCache?.MarkInUse(key);
		}
	}

	/// <summary>Clears the in-flight markers set by <see cref="MarkInUse"/> for <paramref name="cacheKey"/>.</summary>
	internal static void MarkAvailable(string cacheKey) {
		string key = Normalize(cacheKey);
		_lockProvider.MarkAvailable(key);
		if (!IsFallback(key)) {
			_sessionContainerCache?.MarkAvailable(key);
		}
	}

	// Null/blank normalizes to the single shared fallback key so GetLock, MarkInUse, and MarkAvailable
	// all key the same lock-provider entry for an environment-less / test-double call.
	private static string Normalize(string cacheKey) =>
		string.IsNullOrWhiteSpace(cacheKey) ? SharedFallbackKey : cacheKey;

	private static bool IsFallback(string cacheKey) =>
		string.IsNullOrWhiteSpace(cacheKey) || cacheKey == SharedFallbackKey;
}
