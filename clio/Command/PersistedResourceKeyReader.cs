using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Clio.Command.McpServer;

namespace Clio.Command;

/// <summary>
/// The outcome of ONE persisted-resource-key read: the resource keys already stored on the target
/// schema's <c>localizableStrings</c>, and — when the read produced none — the reason it failed.
/// </summary>
/// <param name="Keys">
/// The keys read from the schema. EMPTY when the read failed, which restores the stricter
/// label-resource verdict instead of letting an unvalidated body through.
/// </param>
/// <param name="FailureWarning">
/// The caller-facing reason the read produced no keys, or <see langword="null"/> when it did not fail.
/// A failed read never changes the verdict, so this is always a WARNING, never an error — but it must
/// be visible: without it a 401, an unreachable environment or a failed hierarchy resolution reaches
/// the caller as "resource 'X' is neither auto-provided ... nor registered", i.e. exactly the
/// misleading cause issue #1320 opened with, one layer down.
/// </param>
public sealed record PersistedResourceKeyRead(IReadOnlySet<string> Keys, string FailureWarning) {

	/// <summary>The empty key set (Sonar S1168): nothing was read, so the stricter verdict stands.</summary>
	private static readonly IReadOnlySet<string> NoKeys = new HashSet<string>(StringComparer.Ordinal);

	/// <summary>A completed read that produced no keys and no failure (e.g. a create-replacing target).</summary>
	public static readonly PersistedResourceKeyRead None = new(NoKeys, null);

	/// <summary><see langword="true"/> when this read failed and left the stricter verdict standing.</summary>
	public bool Failed => !string.IsNullOrWhiteSpace(FailureWarning);

	/// <summary>Wraps a successful read.</summary>
	/// <param name="keys">The keys read from the schema; <see langword="null"/> is normalised to empty.</param>
	/// <returns>A successful read result.</returns>
	public static PersistedResourceKeyRead FromKeys(IReadOnlySet<string> keys) =>
		keys is { Count: > 0 } ? new PersistedResourceKeyRead(keys, null) : None;

	/// <summary>Wraps a failed read, wording the caller-facing warning.</summary>
	/// <param name="detail">The underlying reason; blank falls back to a generic sentence.</param>
	/// <returns>A failed read result carrying the warning.</returns>
	public static PersistedResourceKeyRead Failure(string detail) =>
		new(NoKeys, BuildWarning(detail));

	/// <summary>
	/// Single source of truth for the persisted-resource-key failure wording, shared by every surface
	/// that can produce one (the command's own read, the MCP tools' command resolution) so they cannot
	/// drift.
	/// </summary>
	/// <param name="detail">The underlying reason; blank falls back to a generic sentence.</param>
	/// <returns>The redacted, caller-facing warning sentence.</returns>
	public static string BuildWarning(string detail) =>
		SensitiveErrorTextRedactor.Redact(
			"Persisted resource keys could not be read; the stricter label-resource verdict stands. "
			+ (string.IsNullOrWhiteSpace(detail)
				? "The target schema context could not be resolved."
				: detail));
}

/// <summary>
/// Owns the persisted-resource-key read for the duration of ONE logical page write, so the schema
/// hierarchy behind it is resolved once per target instead of once per validation gate.
/// </summary>
/// <remarks>
/// The label-resource rescue (issue #1320) runs at up to THREE gates in one logical save — the MCP
/// pre-execution gate, the per-page gate <c>sync-pages</c> runs again inside its batch loop, and the
/// command-level gate — and every gate that resolves the hierarchy itself costs about three extra
/// Creatio round trips on what the rescue defines as the NORMAL path. Memoizing on the request DTO
/// (the shape this module replaces) keyed the cache on options-INSTANCE identity, which
/// <c>sync-pages</c> does not have: it builds a fresh <see cref="PageUpdateOptions"/> per page and its
/// first gate runs before any options exist at all. Keying on the target being read is what actually
/// describes the thing cached.
/// <para>
/// The scope is flow-local and the store is a STATIC field, deliberately. The page tools resolve from
/// the MCP bootstrap container while <c>PageUpdateCommand</c> is resolved out of a separate per-tenant
/// container built by <c>new BindingsModule().Register</c> — which does not share the root container's
/// singletons — so two instances of this class exist within one call whatever DI lifetime is
/// registered. A static store is what makes them agree; an instance-level one would give each gate its
/// own cache and silently restore the duplicate reads this module exists to remove. Flow-local matches
/// the established <c>IToolCommandResolver.LastResolvedTenantKey</c> precedent and keeps concurrent
/// tenants from reading each other's keys.
/// </para>
/// </remarks>
public interface IPersistedResourceKeyReader {

	/// <summary>
	/// Opens the caching scope for one logical page write. Open it at the TOP of the entry point so
	/// every gate runs inside one scope. Dispose restores the enclosing scope (nesting is safe).
	/// </summary>
	/// <returns>A disposable that closes the scope.</returns>
	/// <remarks>
	/// The scope value flows DOWN to awaited callees, never back UP to the caller — an
	/// <see cref="AsyncLocal{T}"/> write made inside an async method is invisible to whoever awaited it.
	/// That is why the scope belongs at the entry point rather than beside the first gate that needs it:
	/// a scope opened inside a nested async helper covers that helper and nothing else.
	/// <para>
	/// With NO scope open every <see cref="Read"/> simply performs the read — correct, just uncached.
	/// That is the plain-CLI and unit-test shape, so a missing scope degrades cost, never behaviour.
	/// </para>
	/// </remarks>
	IDisposable BeginRequestScope();

	/// <summary>
	/// Returns the persisted resource keys for the schema <paramref name="options"/> targets, running
	/// <paramref name="read"/> at most once per target for the sequential gates of one logical save.
	/// </summary>
	/// <param name="options">The pending write request identifying the environment, schema and redirect.</param>
	/// <param name="read">
	/// Performs the actual read. It must not throw: report a failure as
	/// <see cref="PersistedResourceKeyRead.Failure"/> so the reason is reachable through
	/// <see cref="GetFailureWarning"/> instead of aborting the save.
	/// </param>
	/// <returns>The read result — never <see langword="null"/>.</returns>
	/// <remarks>
	/// "At most once" is a statement about the SEQUENTIAL gates of one save, which is how every caller
	/// uses this: two gates racing on the same target may both run <paramref name="read"/> and one
	/// result wins. That costs a duplicate round trip, never a wrong verdict.
	/// <para>
	/// A FAILED read is deliberately NOT cached. One transient 401 or timeout in the out-of-lock
	/// pre-pass would otherwise stand in for the rest of the call and reject a page whose key IS
	/// persisted — even though the in-lock command gate that runs moments later already holds a resolved
	/// schema context and would have read it successfully.
	/// </para>
	/// </remarks>
	PersistedResourceKeyRead Read(PageUpdateOptions options, Func<PersistedResourceKeyRead> read);

	/// <summary>
	/// Drops the cached read for the schema <paramref name="options"/> targets.
	/// </summary>
	/// <param name="options">The write request whose target identifies the cache entry.</param>
	/// <remarks>
	/// MUST be called after a successful save, because a save REGISTERS keys: inside one
	/// <c>sync-pages</c> batch a second page on the same schema would otherwise be validated against the
	/// key set read before the first page's save and be rejected for a key that now exists. This is the
	/// same hazard the batch's own pre-pass avoids by keying its entries per page index rather than per
	/// schema name.
	/// </remarks>
	void Invalidate(PageUpdateOptions options);

	/// <summary>
	/// Returns the failure warning recorded for the schema <paramref name="options"/> targets during the
	/// current scope, or <see langword="null"/> when no read failed (or none ran).
	/// </summary>
	/// <param name="options">The write request whose target identifies the recorded failure.</param>
	/// <returns>The warning to surface on the response's warning channel, or <see langword="null"/>.</returns>
	/// <remarks>
	/// The reason cannot travel on the logger alone: the page-write tools answer with typed responses
	/// that have no log member, and the MCP tool wraps execution in <c>ExecuteWithCleanLog</c>, which
	/// discards the capture buffer. Recorded separately from the key cache precisely BECAUSE a failed
	/// read is not cached — the reason must outlive the retry that a later gate is free to make.
	/// </remarks>
	string GetFailureWarning(PageUpdateOptions options);
}

/// <inheritdoc />
public sealed class PersistedResourceKeyReader : IPersistedResourceKeyReader {

	// STATIC, not instance: see the interface remarks — the tools and the command are resolved from two
	// different containers within one call, so an instance store would never be shared.
	private static readonly AsyncLocal<PersistedResourceKeyScopeState> CurrentScope = new();

	/// <inheritdoc />
	public IDisposable BeginRequestScope() {
		PersistedResourceKeyScopeState previous = CurrentScope.Value;
		CurrentScope.Value = new PersistedResourceKeyScopeState();
		return new RequestScope(previous);
	}

	/// <inheritdoc />
	public PersistedResourceKeyRead Read(PageUpdateOptions options, Func<PersistedResourceKeyRead> read) {
		ArgumentNullException.ThrowIfNull(read);
		PersistedResourceKeyScopeState scope = CurrentScope.Value;
		if (scope is null) {
			return read() ?? PersistedResourceKeyRead.None;
		}
		PersistedResourceKeyTarget target = ResolveTarget(options);
		if (scope.Keys.TryGetValue(target, out PersistedResourceKeyRead cached)) {
			return cached;
		}
		PersistedResourceKeyRead result = read() ?? PersistedResourceKeyRead.None;
		if (result.Failed) {
			// Not cached - see the interface remarks. The REASON is still recorded, so the caller learns
			// why the verdict stayed strict even though a later gate is free to retry the read.
			scope.Failures[target] = result.FailureWarning;
			return result;
		}
		scope.Keys[target] = result;
		return result;
	}

	/// <inheritdoc />
	public void Invalidate(PageUpdateOptions options) {
		PersistedResourceKeyScopeState scope = CurrentScope.Value;
		scope?.Keys.TryRemove(ResolveTarget(options), out _);
	}

	/// <inheritdoc />
	public string GetFailureWarning(PageUpdateOptions options) {
		PersistedResourceKeyScopeState scope = CurrentScope.Value;
		return scope is not null && scope.Failures.TryGetValue(ResolveTarget(options), out string warning)
			? warning
			: null;
	}

	// The key names what is actually READ, not merely which page is being saved:
	//  - the redirect fields decide which schema the read resolves to (TryResolveContext takes
	//    TemplateSchemaUId from TargetSchemaUId when it is set), so two saves of one schema name that
	//    redirect differently read DIFFERENT schemas;
	//  - the login discriminates two profiles pointed at one URL with different users, whose visible
	//    localizableStrings can differ.
	// Under authorized credential passthrough both Environment and Uri are blank by design (the header
	// carries the tenant). A literal marker keeps the key non-empty and readable; it cannot collide
	// across tenants because the cache lives in the per-call scope, and one call is one tenant.
	private const string CredentialPassthroughMarker = "(credential-passthrough)";

	private static PersistedResourceKeyTarget ResolveTarget(PageUpdateOptions options) {
		if (options is null) {
			return new PersistedResourceKeyTarget(CredentialPassthroughMarker, string.Empty, string.Empty, string.Empty, string.Empty);
		}
		string environment = FirstNonBlank(options.Environment, options.Uri) ?? CredentialPassthroughMarker;
		return new PersistedResourceKeyTarget(
			environment,
			options.Login ?? string.Empty,
			options.TargetPackageUId ?? string.Empty,
			options.TargetSchemaUId ?? string.Empty,
			options.SchemaName ?? string.Empty);
	}

	private static string FirstNonBlank(string first, string second) {
		if (!string.IsNullOrWhiteSpace(first)) {
			return first;
		}
		return string.IsNullOrWhiteSpace(second) ? null : second;
	}

	/// <summary>The cache key: one schema, on one environment, as one user, through one redirect.</summary>
	private readonly record struct PersistedResourceKeyTarget(
		string Environment, string Login, string TargetPackageUId, string TargetSchemaUId, string SchemaName);

	/// <summary>
	/// One scope's two stores. The failure reasons are kept SEPARATELY from the keys so that not caching
	/// a failed read does not also throw away the explanation the caller has to be given.
	/// </summary>
	private sealed class PersistedResourceKeyScopeState {

		public ConcurrentDictionary<PersistedResourceKeyTarget, PersistedResourceKeyRead> Keys { get; } = new();

		public ConcurrentDictionary<PersistedResourceKeyTarget, string> Failures { get; } = new();
	}

	// Restores the enclosing scope rather than clearing it. On the synchronous CLI path the assignment
	// in BeginRequestScope is visible to the caller's ExecutionContext, so without this a later
	// TryUpdatePage on the same thread would inherit a stale, already-closed cache.
	private sealed class RequestScope(PersistedResourceKeyScopeState previous) : IDisposable {

		public void Dispose() => CurrentScope.Value = previous;
	}
}
