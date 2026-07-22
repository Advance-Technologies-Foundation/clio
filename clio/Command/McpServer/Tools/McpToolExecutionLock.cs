using System.Collections.Concurrent;
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
	/// Single process-global lock for regions that pin or restore the process working directory
	/// (H2, ENG-93208). The current directory is process-global state, so once different tenants run
	/// concurrently (per-tenant lock) a tool that pins cwd could otherwise place another tenant's output
	/// under the pinned workspace, and two writers could corrupt each other's save/restore. It is
	/// deliberately NOT a per-tenant lock: cwd is process-global and these are local, infrequent ops, not
	/// the multi-tenant hot path, so serializing them is acceptable.
	/// <para>
	/// <b>ENG-93208 systemic fix (review #4 follow-up) — <c>WorkspaceSyncTool</c> no longer takes this
	/// lock.</b> <c>push-workspace</c>/<c>restore-workspace</c> used to pin process cwd around the
	/// network-bound install/restore, which serialized them against every other <see cref="CwdLock"/>
	/// user for the duration of that network call — including the page-sync hot path
	/// (<c>PageSyncTool.WriteVerifiedBodyFile</c>), causing cross-tenant head-of-line blocking.
	/// <c>WorkspaceCommandToolBase.ExecuteInWorkspace</c> now threads the explicit workspace root through
	/// <c>IWorkspacePathBuilder.RootPath</c> (resolved per-tenant session container, the same seam
	/// <c>Workspace.PublishToFile</c>/<c>PublishToFolder</c> already used) instead of mutating process
	/// cwd, so it needs no process-wide lock at all — only the per-tenant lock (already held via
	/// <c>ExecuteUnderTenantLock</c>) guards against the SAME tenant racing two concurrent calls on its
	/// own <see cref="IWorkspacePathBuilder"/> instance.
	/// </para>
	/// <para>
	/// <b>Scope limitation (review #4, ENG-93208 — NOT a full guarantee).</b> Only the tools that take
	/// <see cref="CwdLock"/> EXPLICITLY are mutually excluded: <c>CreateUiProjectTool</c>,
	/// <c>DownloadConfigurationTool</c>, <c>PageSyncTool</c>, <c>PageFileWriter</c>,
	/// <c>PageBaselineGuard</c>. The much larger set of TRANSITIVE / direct
	/// <c>Environment.CurrentDirectory</c> readers reached through <c>command.Execute</c> — e.g.
	/// <c>PackageArchiver</c>, <c>WorkingDirectoriesProvider.CurrentDirectory</c>,
	/// <c>FileSystem</c>'s <c>GetCurrentDirectory</c> calls, <c>ModelBuilder</c>, <c>PackageCreator</c>,
	/// and any command that defaults an output path to the current directory (compress, download-package)
	/// — do NOT take this lock and run under only the per-tenant lock. So while tenant A holds
	/// <see cref="CwdLock"/> across a cwd pin, tenant B's cwd-defaulting command can still read A's pinned
	/// cwd. This is a KNOWN residual, tolerated because the multi-tenant passthrough edge is an incubation
	/// feature that is OFF by default (no concurrent tenants on the shipped default). The systemic fix —
	/// thread an explicit working directory instead of mutating/reading process cwd — has landed for the
	/// workspace push/restore path (above); the remaining tools in this list are tracked as a follow-up.
	/// Do not rely on this lock for cross-tenant cwd isolation of the transitive readers.
	/// </para>
	/// <para>
	/// <b>Deadlock ordering (single global order): per-tenant lock → CwdLock, NEVER the reverse.</b> A
	/// tool that already holds its per-tenant lock (a command running under
	/// <c>ExecuteUnderTenantLock</c> / <c>InternalExecute</c>, or the page-sync batch) may then take
	/// <see cref="CwdLock"/>. No path may take <see cref="CwdLock"/> and THEN acquire a per-tenant lock
	/// for a different key.
	/// </para>
	/// </summary>
	internal static readonly object CwdLock = new();

	private static ITenantExecutionLockProvider _lockProvider = TenantExecutionLockProvider.Shared;
	private static ISessionContainerCache _sessionContainerCache;

	// Per-tenant "compilation in flight" reservation (ENG-91315, review Blocker). Compilation is the one
	// env-bound MCP operation the Creatio core itself serializes: WorkspaceBuilder rejects a second
	// concurrent compilation on the node with "AnotherCompilationIsInProgress" (verified in core trunk,
	// Terrasoft.Core/Packages/WorkspaceBuilder.cs). Editing/saving schemas, data, and other tools are NOT
	// blocked by a running compile — so serializing them behind it (which the broad per-tenant execution
	// monitor did) is over-broad. Worse, past the MCP response deadline the compile detaches and keeps
	// running for minutes; holding the broad monitor across that left every OTHER same-tenant tool silently
	// blocked past the caller's client ceiling. The compile path now takes only this narrow compile-scoped
	// reservation instead: a second same-tenant compile fails fast (mirroring the core's own reject), and
	// non-compile tools are not blocked at all. Process-global by necessity — concurrent MCP calls share the
	// process, and tool instances do not — matching why the lock provider itself is a static facade.
	private static readonly ConcurrentDictionary<string, byte> _compileInFlight = new();

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
	/// Returns the per-tenant execution lock for <paramref name="cacheKey"/> and pins its lock-provider
	/// mapping in-use (review #3), so the mapping cannot be evicted between hand-out and the caller taking
	/// the monitor. Balanced by <see cref="MarkAvailable"/>. A null/blank key (e.g. an environment-less
	/// command, or a test double whose resolver returns no key) normalizes to the single shared fallback
	/// lock rather than throwing.
	/// </summary>
	internal static object GetLock(string cacheKey) =>
		_lockProvider.GetLock(Normalize(cacheKey));

	/// <summary>
	/// Marks <paramref name="cacheKey"/> as in-flight on the session-container cache (so eviction cannot
	/// dispose the container mid-call), for real tenants only. The lock-provider mapping is already pinned
	/// by <see cref="GetLock"/> (review #3), so it is not pinned again here.
	/// </summary>
	internal static void MarkInUse(string cacheKey) {
		string key = Normalize(cacheKey);
		if (!IsFallback(key)) {
			_sessionContainerCache?.MarkInUse(key);
		}
	}

	/// <summary>
	/// Releases the in-flight markers for <paramref name="cacheKey"/>: the lock-provider pin taken by
	/// <see cref="GetLock"/> and, for real tenants, the session-container marker set by <see cref="MarkInUse"/>.
	/// </summary>
	internal static void MarkAvailable(string cacheKey) {
		string key = Normalize(cacheKey);
		_lockProvider.MarkAvailable(key);
		if (!IsFallback(key)) {
			_sessionContainerCache?.MarkAvailable(key);
		}
	}

	/// <summary>
	/// Releases ONLY the session-container in-flight marker for <paramref name="cacheKey"/> — the lock-free
	/// counterpart to <see cref="MarkAvailable"/>. For paths that pinned the session container via
	/// <see cref="MarkInUse"/> WITHOUT ever taking <see cref="GetLock"/> (e.g. the restart readiness wait,
	/// which deliberately runs lock-free). It skips <c>_lockProvider.MarkAvailable</c> on purpose: that call
	/// decrements the lock-provider in-use count which only <see cref="GetLock"/> increments, so calling the
	/// full <see cref="MarkAvailable"/> from a GetLock-free path would stray-decrement a DIFFERENT in-flight
	/// holder's count, reopening the eviction/mutual-exclusion window <see cref="TenantExecutionLockProvider"/>
	/// guarantees against (review Finding 2, ENG-91315).
	/// </summary>
	internal static void MarkSessionContainerAvailable(string cacheKey) {
		string key = Normalize(cacheKey);
		if (!IsFallback(key)) {
			_sessionContainerCache?.MarkAvailable(key);
		}
	}

	/// <summary>
	/// Attempts to reserve compilation for <paramref name="cacheKey"/>. Returns <see langword="true"/> when
	/// no compile is currently in flight for this tenant (the caller may proceed and MUST balance it with
	/// <see cref="ReleaseCompile"/> when the compile — including its detached, past-deadline continuation —
	/// finishes), or <see langword="false"/> when one is already running (the caller should fail fast rather
	/// than start a second compile the Creatio core would reject anyway). Atomic (single-flight) so two
	/// concurrent same-tenant compiles cannot both win.
	/// </summary>
	internal static bool TryReserveCompile(string cacheKey) =>
		_compileInFlight.TryAdd(Normalize(cacheKey), 0);

	/// <summary>
	/// Releases the compile reservation taken by <see cref="TryReserveCompile"/>. Must be called from the
	/// point where the actual compile work completes (its detached continuation past the MCP response
	/// deadline), not where the tool method returns, so the reservation spans the real compile duration.
	/// </summary>
	internal static void ReleaseCompile(string cacheKey) =>
		_compileInFlight.TryRemove(Normalize(cacheKey), out _);

	// Test-only: clears the process-global compile reservations so a detached compile started by one test
	// cannot fast-fail a compile in the next (the reservation release runs on the detached continuation,
	// which may outlive the test method). No production caller.
	internal static void ResetCompileReservationsForTests() =>
		_compileInFlight.Clear();

	// Null/blank normalizes to the single shared fallback key so GetLock, MarkInUse, and MarkAvailable
	// all key the same lock-provider entry for an environment-less / test-double call.
	private static string Normalize(string cacheKey) =>
		string.IsNullOrWhiteSpace(cacheKey) ? SharedFallbackKey : cacheKey;

	private static bool IsFallback(string cacheKey) =>
		string.IsNullOrWhiteSpace(cacheKey) || cacheKey == SharedFallbackKey;
}
