using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
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
	private static readonly ConcurrentDictionary<string, BuildReservation> _configurationBuildInFlight = new();

	/// <summary>
	/// One held configuration-build reservation: who holds it, and since when.
	/// </summary>
	/// <param name="Token">
	/// Ownership, from a monotonic counter. NOT derived from the clock: <c>Environment.TickCount64</c> has a
	/// ~15.6 ms resolution on Windows, so two reservations taken inside one tick would share a stamp and
	/// ownership would stop being decidable. Production could not hit that today — a reclaim needs an entry
	/// older than the ceiling, so the two stamps are tens of minutes apart — but correctness that rests on the
	/// clock being coarse enough is not correctness, and a test constructing the collision found it at once.
	/// </param>
	/// <param name="StartedAtMs">
	/// When the reservation was taken, in monotonic ticks, for the ceiling comparison only.
	/// </param>
	internal readonly record struct BuildReservation(long Token, long StartedAtMs);

	private static long _reservationTokenSource;

	/// <summary>
	/// How long a configuration-build reservation may be held in THIS PROCESS before another caller may
	/// reclaim it.
	/// </summary>
	/// <remarks>
	/// A backstop against a reservation that can never be released — not a timeout on the build, and not a
	/// distributed lease: another clio process on another machine is unaffected either way. See
	/// <see cref="TryReserveConfigurationBuild"/> for what does serialize that case, why a reservation can
	/// become unreleasable, and why this value is generous.
	/// </remarks>
	private static readonly TimeSpan ConfigurationBuildReservationCeiling = TimeSpan.FromMinutes(30);

	/// <summary>
	/// The ceiling in the units the reservation stamps are measured in.
	/// </summary>
	private static long ConfigurationBuildReservationCeilingMs =>
		(long)ConfigurationBuildReservationCeiling.TotalMilliseconds;

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
	/// Attempts to reserve a configuration build on <paramref name="cacheKey"/>. Returns
	/// <see langword="true"/> when none is currently in flight for this tenant (the caller may proceed and
	/// MUST balance it with <see cref="ReleaseConfigurationBuild"/> when the work — including its detached,
	/// past-deadline continuation — finishes), or <see langword="false"/> when one is already running (the
	/// caller should fail fast rather than start a second build the Creatio core would reject anyway).
	/// Atomic (single-flight) so two concurrent same-tenant builds cannot both win.
	/// </summary>
	/// <remarks>
	/// One reservation covers every tool that makes the TARGET rebuild its configuration, not just
	/// <c>compile-creatio</c>: <c>install-process-builder</c> ships a source-only package the target compiles
	/// during installation, so it must exclude, and be excluded by, a concurrent compile on the same tenant.
	/// It is deliberately narrow — it does NOT serialize unrelated same-tenant tools the way the per-tenant
	/// execution monitor would (review Blocker).
	/// <para>
	/// <b>No longer the authoritative exclusion once the tool runs in a worker (ENG-95262 story 7).</b> This
	/// dictionary is a <see langword="static"/> in whichever PROCESS ran the tool, so in a worker it excludes
	/// only that worker's own calls — of which there is one. The cross-process, cross-principal exclusion now
	/// lives in the parent as
	/// <see cref="Clio.Command.McpServer.Relay.ISharedResourceReservation"/>, keyed by normalised target +
	/// resource rather than by the tenant key, and taken by the dispatcher BEFORE the worker is spawned. This
	/// one is kept because <c>compile-creatio</c> and <c>install-process-builder</c> still run in-process on
	/// every host that does not route them to a worker (any non-stdio transport, and a worker's own
	/// <c>clio-run</c>), where it remains the only guard there is. It retires with the cohort at Stage 10.
	/// </para>
	/// </remarks>
	internal static bool TryReserveConfigurationBuild(string cacheKey, out BuildReservation reservation) {
		string key = Normalize(cacheKey);
		// Environment.TickCount64, NOT DateTime.UtcNow: this is an ELAPSED-time measurement, and the wall clock
		// is not monotonic. A forward step of more than the ceiling — an NTP phase correction, a VM snapshot
		// restore, a laptop resuming with a corrected RTC, an operator setting the clock — would otherwise
		// reclaim a reservation whose build started seconds ago, which is the one thing the generous ceiling is
		// supposed to prevent. Ticks since boot cannot move backwards and do not wrap in any realistic uptime.
		long now = Environment.TickCount64;
		reservation = new BuildReservation(Interlocked.Increment(ref _reservationTokenSource), now);
		if (_configurationBuildInFlight.TryAdd(key, reservation)) {
			return true;
		}
		// A held reservation past its ceiling is RECLAIMED rather than honoured, because without that this
		// dictionary has a permanent-wedge state. The reservation is released by the `finally` of the work
		// delegate, which past the MCP response deadline runs detached — so that `finally` is the only release
		// there is, and the install POST it wraps goes out with Timeout.Infinite (BasePackageInstaller). A
		// target that accepts the request and then never answers — a hung app pool mid-configuration-build, a
		// proxy holding the connection open, a deliberately stalling instance — therefore leaves the entry in
		// place for the life of the MCP server process, and every later install-process-builder AND
		// compile-creatio on that tenant is refused with no in-band recovery. MCP servers are long-lived, so
		// "restart clio to unstick it" is not an answer a user should need.
		//
		// SCOPE, because the wording here used to overclaim it. This dictionary is a static in ONE process:
		// it does not, and never did, exclude a second clio on another machine. Two users hitting the same
		// environment are two processes that know nothing about each other. What serializes THAT is the
		// platform — WorkspaceBuilder rejects a concurrent compilation on the node with
		// "AnotherCompilationIsInProgress" — and that is the real invariant. This reservation is a local
		// fast-fail: it stops one process from sending a doomed second request, and from starting a second
		// detached install on top of its own first one.
		//
		// So the ceiling is not a timeout on the work: nothing is cancelled, and a build still running past it
		// keeps running. It only stops a reservation nobody will ever release from outliving the work it was
		// protecting. It is generous for the modest reason — not to take the slot away from a build that is
		// honestly still running IN THIS PROCESS — rather than because early reclaim would break the
		// cross-machine invariant; the reservation was never what upheld that.
		//
		// The root cause on the install path is the unbounded POST, and bounding it would remove the need for
		// a ceiling THERE. It lives in BasePackageInstaller, shared with install-gate and every other package
		// install, so it carries its own blast radius and is deliberately a separate change. It would not retire
		// the ceiling, though: compile-creatio takes the same reservation and its long call does not go through
		// BasePackageInstaller, so that subject would remain.
		if (_configurationBuildInFlight.TryGetValue(key, out BuildReservation held)
			&& now - held.StartedAtMs > ConfigurationBuildReservationCeilingMs) {
			// TryUpdate, not Remove-then-Add: if two callers race here only one may take the reclaimed slot,
			// and the comparison value makes that decidable without a lock.
			return _configurationBuildInFlight.TryUpdate(key, reservation, held);
		}
		return false;
	}

	/// <summary>
	/// Releases the reservation taken by <see cref="TryReserveConfigurationBuild"/>. Must be called from the
	/// point where the actual work completes (its detached continuation past the MCP response deadline), not
	/// where the tool method returns, so the reservation spans the real build duration.
	/// </summary>
	/// <param name="cacheKey">The tenant key the reservation was taken for.</param>
	/// <param name="reservation">
	/// The token <see cref="TryReserveConfigurationBuild"/> handed out. Releasing is a no-op unless it still
	/// matches, which is what makes a superseded holder harmless.
	/// </param>
	/// <remarks>
	/// OWNERSHIP-AWARE, and it has to be once reclaiming exists. Before the ceiling there was exactly one
	/// logical owner per key at any time, so an unconditional remove was correct. With reclaiming there can be
	/// two: the original holder, whose work is still out there, and the caller that reclaimed the slot after
	/// the ceiling. An unconditional remove let the ORIGINAL delete the RECLAIMER's live reservation when it
	/// finally returned — and then any third caller reserved successfully and started a configuration build
	/// alongside the reclaimer's, with no refusal. So the guard would switch itself off for that tenant after
	/// a single reclaim: precisely the "second install on top of a live one" this reservation exists to stop,
	/// arrived at by way of the fix for the wedge.
	/// </remarks>
	internal static void ReleaseConfigurationBuild(string cacheKey, BuildReservation reservation) =>
		_configurationBuildInFlight.TryRemove(
			new KeyValuePair<string, BuildReservation>(Normalize(cacheKey), reservation));

	// Test-only: clears the process-global reservations so detached work started by one test cannot
	// fast-fail the next one (the release runs on the detached continuation, which may outlive the test
	// method). No production caller.
	internal static void ResetConfigurationBuildReservationsForTests() =>
		_configurationBuildInFlight.Clear();

	// Test-only: ages an existing reservation so the ceiling in TryReserveConfigurationBuild can be exercised
	// without waiting it out. Deliberately manipulates the same dictionary rather than making the ceiling
	// itself settable — a configurable ceiling is production state a test could leave wrong for the next one,
	// whereas a back-dated entry is cleared by ResetConfigurationBuildReservationsForTests like any other.
	// Returns false when there is no reservation to age, so a test cannot silently assert nothing.
	internal static bool BackdateConfigurationBuildReservationForTests(string cacheKey, TimeSpan age) {
		string key = Normalize(cacheKey);
		return _configurationBuildInFlight.TryGetValue(key, out BuildReservation held)
			&& _configurationBuildInFlight.TryUpdate(
				key,
				held with { StartedAtMs = held.StartedAtMs - (long)age.TotalMilliseconds },
				held);
	}

	// Test-only: the ceiling, so a test can express "older than the ceiling" without restating the number and
	// silently passing if production changes it.
	internal static TimeSpan ConfigurationBuildReservationCeilingForTests =>
		ConfigurationBuildReservationCeiling;

	// Null/blank normalizes to the single shared fallback key so GetLock, MarkInUse, and MarkAvailable
	// all key the same lock-provider entry for an environment-less / test-double call.
	private static string Normalize(string cacheKey) =>
		string.IsNullOrWhiteSpace(cacheKey) ? SharedFallbackKey : cacheKey;

	private static bool IsFallback(string cacheKey) =>
		string.IsNullOrWhiteSpace(cacheKey) || cacheKey == SharedFallbackKey;
}
