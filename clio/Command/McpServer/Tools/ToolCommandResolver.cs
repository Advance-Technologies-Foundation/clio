using System;
using System.Security.Cryptography;
using System.Text;
using Clio;
using Clio.Command.McpServer;
using Clio.Common;
using Clio.UserEnvironment;
using Microsoft.Extensions.DependencyInjection;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Resolves environment-aware command instances for MCP tools.
/// </summary>
public interface IToolCommandResolver {
	/// <summary>
	/// Resolves a command instance for the provided environment options.
	/// </summary>
	/// <typeparam name="TCommand">The command type to resolve.</typeparam>
	/// <param name="options">Environment options that identify the execution target.</param>
	/// <returns>A command instance configured for the requested target.</returns>
	TCommand Resolve<TCommand>(EnvironmentOptions options);
	TCommand ResolveWithoutEnvironment<TCommand>(EnvironmentOptions options);

	/// <summary>
	/// The cache key the MOST RECENT <see cref="Resolve{TCommand}"/> call on the CURRENT async-flow
	/// cached its container under. The BaseTool execution path reads this immediately after resolving a
	/// command so it can lock / mark-in-use on the SAME key without recomputing it via
	/// <see cref="GetTenantKey"/> (M2, ENG-93208 — eliminates the divergence window and the second
	/// <c>settings.Fill</c> per invocation). Flow-local so concurrent tenants never read each other's
	/// value; <see langword="null"/> before any <see cref="Resolve{TCommand}"/> call on the flow.
	/// </summary>
	string LastResolvedTenantKey { get; }

	/// <summary>
	/// Computes the credential-discriminating cache key for <paramref name="options"/> WITHOUT building
	/// or acquiring a container. Runs the SAME branch as <see cref="Resolve{TCommand}"/> (per-request
	/// credential context → passthrough key; otherwise the registry/URI key), so the returned value is
	/// the SAME key <see cref="Resolve{TCommand}"/> caches the container under. Used by the per-tenant
	/// execution lock (FR-05) and the session-container in-flight guard so both key off the exact
	/// identity the command resolves into. Never throws for a bad environment: a resolution failure
	/// yields a stable fallback key instead (the command itself will fail, and there is no shared
	/// session to protect).
	/// </summary>
	/// <param name="options">Environment options that identify the execution target.</param>
	/// <returns>The cache key the command for these options resolves under.</returns>
	string GetTenantKey(EnvironmentOptions options);

	/// <summary>
	/// Computes the NORMALISED TARGET key for <paramref name="options"/> — the environment a call is
	/// about to act on, and nothing else. Never throws.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Deliberately a DIFFERENT cardinality from <see cref="GetTenantKey"/>, and conflating the two
	/// fails either way</b> (ENG-95262 story 7, AC-03; cross-call-state inventory §3).
	/// <see cref="GetTenantKey"/> answers "whose session is this" and therefore carries the credential
	/// fingerprint; it is the right key for the compile/restart STATUS registries and for the sticky
	/// worker. This answers "which server am I about to recompile", and Creatio's configuration build is
	/// SERVER-WIDE: keying the exclusion by the tenant key would let two principals on one environment
	/// compile concurrently and corrupt each other's package compilation state.
	/// </para>
	/// <para>
	/// The converse cost is stated rather than hidden: keyed by target alone, one principal's stuck build
	/// denies every other principal on that environment — which is why the reservation's reclaim
	/// ceiling is its MAXIMUM HOLD TIME rather than an incidental number.
	/// </para>
	/// </remarks>
	/// <param name="options">Environment options that identify the execution target.</param>
	/// <returns>The normalised target key; a stable unresolved fallback when the target cannot be resolved.</returns>
	string GetTargetKey(EnvironmentOptions options);
}

/// <summary>
/// Creates isolated command instances for MCP tool execution targets.
/// Caches <see cref="IServiceProvider"/> per environment key so that a single
/// <see cref="IApplicationClient"/> (and its authenticated HTTP session) is reused
/// across successive tool calls targeting the same Creatio instance.
/// </summary>
public class ToolCommandResolver(
	ISettingsRepository settingsRepository,
	ISettingsBootstrapService settingsBootstrapService,
	ICredentialContextAccessor credentialContextAccessor,
	ITargetUrlValidator targetUrlValidator,
	ISessionContainerCache sessionContainerCache,
	ISessionTargetNormalizer sessionTargetNormalizer) : IToolCommandResolver {

	/// <summary>
	/// Prefix of every credential-passthrough cache key (see <see cref="BuildPassthroughCacheKey"/>).
	/// A resolved tenant key that starts with this prefix identifies a passthrough request — the signal
	/// BaseTool uses to scope log redaction to the public multi-tenant edge (FIX 2, ENG-93208).
	/// </summary>
	internal const string PassthroughKeyPrefix = "passthrough:";

	// Placeholder identity/login used when no explicit environment, URI, or login is available: the
	// unresolved cache-key fallback (GetTenantKey) and the environment-less default settings.
	private const string DefaultIdentifier = "default";

	/// <summary>
	/// Resolves a command against an explicit environment or URI-based target.
	/// </summary>
	/// <typeparam name="TCommand">The command type to resolve.</typeparam>
	/// <param name="options">Environment options that identify the execution target.</param>
	/// <returns>A command instance configured for the requested target.</returns>
	// M2 (ENG-93208): the key the last Resolve on this flow cached under, exposed via
	// LastResolvedTenantKey so the BaseTool execution path can lock on the exact same key without
	// recomputing it (a second settings.Fill) via GetTenantKey. AsyncLocal so concurrent tenants never
	// read each other's value; the resolve + the immediately-following read are on the same flow.
	private readonly System.Threading.AsyncLocal<string> _lastResolvedTenantKey = new();

	/// <inheritdoc />
	public string LastResolvedTenantKey => _lastResolvedTenantKey.Value;

	/// <inheritdoc />
	public TCommand Resolve<TCommand>(EnvironmentOptions options) {
		ArgumentNullException.ThrowIfNull(options);

		// Credential-passthrough branch (FR-03/FR-04/FR-12). A per-request CredentialContext, when
		// present, is authoritative: build an EPHEMERAL EnvironmentSettings straight from the header
		// credentials — no settings repo, no env-name match, no interactive Fill, nothing persisted.
		// Read Current FIRST so this path has zero coupling to the settings bootstrap / registry. In
		// the stdio host and the per-environment ephemeral containers the accessor is the null object
		// (Current == null), so this branch is only ever taken in the mcp-http host on a passthrough
		// request; every other caller falls through to the unchanged registry / explicit-URI path.
		CredentialContext credentialContext = credentialContextAccessor.Current;
		if (credentialContext is not null) {
			// FR-19 (ENG-93208): on the multi-tenant HTTP edge with passthrough mode ON, the credential
			// arrives EXCLUSIVELY in the X-Integration-Credentials header (which produced this context).
			// Plaintext credential/environment tool-args on that edge are a smuggling vector: they would be
			// SILENTLY dropped (the passthrough branch below ignores `options` entirely and resolves from the
			// header). Reject them explicitly so the caller learns the correct channel instead of having
			// their input quietly ignored. The check is mode-scoped — it reads Transport + PassthroughModeEnabled
			// from the per-request context, so stdio and default-HTTP (Current == null, unreachable here) keep
			// honoring args exactly as 8.1.0.72 (AC-02/AC-03). The message names NO supplied value (AC-ERR/FR-11).
			if (credentialContext.Transport == McpTransport.Http
				&& credentialContext.PassthroughModeEnabled
				&& HasExplicitCredentialArgs(options)) {
				throw new EnvironmentResolutionException(
					"Explicit credential or environment arguments (uri/login/password/client-id/client-secret/environment) "
					+ "are not accepted when credential passthrough is enabled over HTTP. Supply the target environment "
					+ "and credentials via the X-Integration-Credentials header, not tool arguments.");
			}
			return ResolvePassthrough<TCommand>(credentialContext);
		}

		(EnvironmentSettings settings, string cacheKey) = ResolveSettingsAndKey(options);
		_lastResolvedTenantKey.Value = cacheKey;
		IServiceProvider container = sessionContainerCache.Acquire(cacheKey,
			() => new BindingsModule().Register(settings, NonInteractiveConsole.ForceInContainer));
		return container.GetRequiredService<TCommand>();
	}

	/// <inheritdoc />
	public string GetTenantKey(EnvironmentOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		// Read Current FIRST so this mirrors Resolve's branch selection exactly: a per-request
		// credential context yields the same passthrough key Resolve caches the container under.
		CredentialContext credentialContext = credentialContextAccessor.Current;
		if (credentialContext is not null) {
			return BuildPassthroughCacheKey(credentialContext);
		}
		try {
			return ResolveSettingsAndKey(options).CacheKey;
		}
		catch (EnvironmentResolutionException) {
			// The key only selects an execution lock / cache marker; when the environment cannot be
			// resolved the command itself fails and there is no shared authenticated session to protect.
			// Return a stable fallback derived from the requested identity so same-target failing calls
			// still serialize and different targets do not — and no exception escapes the lock-key path.
			return $"unresolved:{options.Environment ?? options.Uri ?? DefaultIdentifier}";
		}
	}

	/// <inheritdoc />
	public string GetTargetKey(EnvironmentOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		// Same branch order as GetTenantKey / Resolve, so the target this returns is the target the call
		// actually acts on. The passthrough branch is unreachable on the shipped worker path (stdio only,
		// ADR §5 / OQ-9) but is answered rather than left to fall through, so a future mcp-http revival
		// does not silently key every passthrough call under one shared target.
		CredentialContext credentialContext = credentialContextAccessor.Current;
		if (credentialContext is not null) {
			return NormalizeOrUnresolved(credentialContext.Url);
		}
		try {
			(EnvironmentSettings settings, _) = ResolveSettingsAndKey(options);
			return BuildTargetIdentity(options, settings);
		}
		catch (EnvironmentResolutionException) {
			// NEVER throws, for the same reason GetTenantKey does not: an exclusion key is consulted on the
			// dispatch path, and a target that cannot be resolved must fail the CALL (which it does, in the
			// command) rather than the reservation lookup. The fallback is derived from the requested
			// identity so two calls naming the same unresolvable target still exclude each other.
			return $"unresolved:{options.Environment ?? options.Uri ?? DefaultIdentifier}";
		}
	}

	// A target that the normaliser rejects (userinfo, query, fragment — threat model T-5) is NOT folded
	// into some neighbouring target: it keeps an unresolved, raw-identity key of its own, which is
	// strictly MORE distinguishing and can therefore only cost an extra reservation, never merge two
	// environments. The rejected value is not echoed — T-6 forbids putting userinfo in a message, and a
	// key derived from the raw url would carry it into a dictionary the same call later reads back.
	private string NormalizeOrUnresolved(string url) {
		try {
			return sessionTargetNormalizer.Normalize(url);
		}
		catch (EnvironmentResolutionException) {
			return $"unresolved:{HashSecretMaterial(url ?? string.Empty)}";
		}
	}

	// Shared settings-resolution + cache-key builder used by BOTH Resolve (which then acquires the
	// container) and GetTenantKey (which returns only the key). Keeping them on one path guarantees the
	// per-tenant lock keys off the exact identity the container is cached under. The four throws are
	// EXPECTED, caller-actionable resolution failures (unknown environment, missing URI, broken settings
	// bootstrap) → EnvironmentResolutionException, which BaseTool maps to exit code 1. Unexpected
	// failures (settings.Fill, BindingsModule.Register, GetRequiredService) stay plain exceptions →
	// exit code -1, so a real DI/wiring bug remains distinguishable from a bad environment name.
	private (EnvironmentSettings Settings, string CacheKey) ResolveSettingsAndKey(EnvironmentOptions options) {
		// Re-read the settings file before matching the environment name. The host is long-lived, so the
		// repository's constructor-time snapshot would otherwise reject an environment registered after
		// process start (ENG-94529) — and it would keep routing to the OLD uri of an environment that was
		// re-pointed since. This costs no extra file read: the bootstrap report below was already produced
		// by a full disk read on every resolve, and the reload returns that same report. A file that
		// cannot be read keeps the previous snapshot and reports status "broken" through the report, which
		// the CanExecuteEnvTools checks below already handle.
		SettingsReloadResult reload = settingsRepository.Reload();
		SettingsBootstrapReport bootstrapReport = reload?.Report ?? settingsBootstrapService.GetReport();
		EnvironmentSettings settings;
		if (!string.IsNullOrWhiteSpace(options.Environment)) {
			if (!bootstrapReport.CanExecuteEnvTools) {
				throw new EnvironmentResolutionException(
					$"clio settings bootstrap is broken. Repair {bootstrapReport.SettingsFilePath}. Explicit uri/login/password remains available only as an emergency fallback.");
			}
			if (!settingsRepository.IsEnvironmentExists(options.Environment)) {
				throw new EnvironmentResolutionException(BuildEnvironmentNotFoundError(options.Environment));
			}
			settings = settingsRepository.FindEnvironment(options.Environment)
				?? throw new EnvironmentResolutionException(BuildEnvironmentNotFoundError(options.Environment));
			// MCP command resolution is NEVER interactive (see ForceNonInteractiveConsole): Fill must use a
		// non-interactive console too, or a Safe-flagged environment resolved over an mcp-http host started
		// at a TTY would still deadlock on Console.ReadKey inside Fill (review RC-5). NonInteractiveConsole
		// fails closed, so a Safe environment surfaces SafeEnvironmentConfirmationRequiredException instead.
		settings = settings.Fill(options, NonInteractiveConsole.Shared);
		}
		else {
			// Non-interactive for the same reason as the other Fill sites (review RC-5).
			settings = new EnvironmentSettings().Fill(options, NonInteractiveConsole.Shared);
			if (string.IsNullOrWhiteSpace(settings.Uri)) {
				if (!bootstrapReport.CanExecuteEnvTools) {
					throw new EnvironmentResolutionException(
						$"clio settings bootstrap is broken. Repair {bootstrapReport.SettingsFilePath}. Explicit uri/login/password remains available only as an emergency fallback.");
				}
				throw new EnvironmentResolutionException(
					"Either a configured environment name or an explicit URI is required for MCP command execution. Prefer a registered environment name; use explicit URI credentials only as a bootstrap or emergency fallback.");
			}
		}
		return (settings, BuildCacheKey(options, settings));
	}

	// Resolves a command from a per-request credential context. The SSRF/egress guard runs FIRST
	// (AC-04) so a hostile url cannot be used as a credential-redirection lever, then the ephemeral
	// settings are built and cached under a credential-discriminating key. This method is linear:
	// EnsureAllowed precedes every settings/container/client construction by construction.
	private TCommand ResolvePassthrough<TCommand>(CredentialContext context) {
		// AC-04 / FR-17: validate the caller-influenced target url BEFORE building any settings,
		// container, or client. A rejection is caller-actionable (fix your input), so surface it as an
		// EnvironmentResolutionException — consistent with the cookie / missing-auth / non-Bearer
		// rejections below — so BaseTool maps it to exit code 1, not -1 ("clio bug"). The validator's
		// message names the reason and never carries a secret (FR-11); it is preserved verbatim.
		try {
			targetUrlValidator.EnsureAllowed(context.Url);
		}
		catch (TargetUrlNotAllowedException ex) {
			throw new EnvironmentResolutionException(ex.Message, ex);
		}

		// FR-12 / AC-05: name the real missing piece. Cookie auth is caller-actionable (exit code 1),
		// so intercept it here as an EnvironmentResolutionException rather than letting the deep
		// ApplicationClientFactory NotSupportedException surface as an unexpected wiring failure.
		if (context.Auth?.Kind == CredentialKind.Cookie) {
			throw new EnvironmentResolutionException(
				"Cookie-based authentication is not supported for credential passthrough in v1; supply an access token.");
		}
		if (!HasUsableAuth(context.Auth)) {
			throw new EnvironmentResolutionException(
				"Authentication material (an access token or a login/password pair) is required for credential-passthrough command execution.");
		}
		// Same footgun class as cookie: a non-Bearer access-token type would otherwise trip
		// ApplicationClientFactory.GuardBearerSettings deep in command resolution (exit -1). Surface it
		// here as a caller-actionable EnvironmentResolutionException (exit 1). The scheme name is not a
		// secret. The parser (Story 4) forwards the caller-supplied type verbatim, so this is reachable.
		if (context.Auth?.Kind == CredentialKind.AccessToken
			&& !string.IsNullOrWhiteSpace(context.Auth.AccessTokenType)
			&& !string.Equals(context.Auth.AccessTokenType, AuthenticationScheme.Bearer, StringComparison.OrdinalIgnoreCase)) {
			throw new EnvironmentResolutionException(
				$"Access-token type '{context.Auth.AccessTokenType}' is not supported for credential passthrough; only 'Bearer' is supported.");
		}

		EnvironmentSettings settings = BuildEphemeralSettings(context);
		string cacheKey = BuildPassthroughCacheKey(context);
		_lastResolvedTenantKey.Value = cacheKey;
		// Nothing is persisted (AC-03): the ephemeral settings never touch the settings repository,
		// disk, session, or appsettings.json — only this in-memory container cache.
		// Skip per-build ValidateOnBuild/ValidateScopes on this rotating-token hot path (review): the
		// EnvironmentScoped graph SHAPE is invariant across tenants and is validated once at mcp-http host
		// startup (BindingsModule.ValidateEnvironmentScopedGraph), so re-validating the full ~455-registration
		// graph on every near-continuous rotating-token cache miss is pure startup-grade cost.
		IServiceProvider container = sessionContainerCache.Acquire(cacheKey,
			() => new BindingsModule().Register(settings, NonInteractiveConsole.ForceInContainer, validateGraph: false));
		return container.GetRequiredService<TCommand>();
	}

	/// <summary>
	/// Builds an ephemeral, non-persisted <see cref="EnvironmentSettings"/> directly from a
	/// per-request <see cref="CredentialContext"/>. Never consults the settings repository, matches
	/// an environment name, or runs the interactive <c>Fill</c> path — the header-built environment
	/// carries no Safe flag and stays non-interactive / fail-closed (A-06).
	/// </summary>
	/// <param name="context">The per-request credential context (url, runtime, and precedence-resolved auth).</param>
	/// <returns>An in-memory <see cref="EnvironmentSettings"/> carrying the target url, runtime, and credential material.</returns>
	internal static EnvironmentSettings BuildEphemeralSettings(CredentialContext context) {
		ArgumentNullException.ThrowIfNull(context);
		CredentialMaterial auth = context.Auth;
		EnvironmentSettings settings = new() {
			Uri = context.Url,
			IsNetCore = context.IsNetCore
		};
		switch (auth?.Kind) {
			case CredentialKind.AccessToken:
				settings.AccessToken = auth.AccessToken;
				if (!string.IsNullOrWhiteSpace(auth.AccessTokenType)) {
					settings.AccessTokenType = auth.AccessTokenType;
				}
				break;
			case CredentialKind.Cookie:
				settings.Cookie = auth.Cookie;
				break;
			case CredentialKind.LoginPassword:
				settings.Login = auth.Login;
				settings.Password = auth.Password;
				break;
			default:
				break;
		}
		return settings;
	}

	// FR-19: true when the caller supplied any EXPLICIT credential/environment argument that the
	// passthrough path would otherwise drop silently. The five auth args (uri/login/password/client-id/
	// client-secret) are the Creatio-auth axis passthrough replaces; the environment NAME is included
	// because on the passthrough branch it is ignored today (the header context wins), so leaving it
	// unflagged would let a caller believe a named environment took effect when it did not. Chained
	// booleans (no nested ternary, S3358); the values themselves are never read into any message.
	private static bool HasExplicitCredentialArgs(EnvironmentOptions options) =>
		!string.IsNullOrWhiteSpace(options.Uri)
		|| !string.IsNullOrWhiteSpace(options.Login)
		|| !string.IsNullOrWhiteSpace(options.Password)
		|| !string.IsNullOrWhiteSpace(options.ClientId)
		|| !string.IsNullOrWhiteSpace(options.ClientSecret)
		|| !string.IsNullOrWhiteSpace(options.Environment);

	private static bool HasUsableAuth(CredentialMaterial auth) =>
		auth?.Kind switch {
			CredentialKind.AccessToken => !string.IsNullOrWhiteSpace(auth.AccessToken),
			CredentialKind.Cookie => !string.IsNullOrWhiteSpace(auth.Cookie),
			CredentialKind.LoginPassword => !string.IsNullOrWhiteSpace(auth.Login)
				&& !string.IsNullOrWhiteSpace(auth.Password),
			_ => false
		};

	// Dedicated passthrough cache key that INCLUDES the runtime and credential discriminator (kind + token /
	// cookie / login+password), unlike BuildCacheKey which hashes only Login|Password|ClientId|IsNetCore
	// and would collide two different bearer tokens on the same url (cross-tenant reuse). The secret
	// material is SHA-256 hashed, never placed raw in the key. The FULL hash is used (not a 64-bit
	// truncation): on this feature "same url, different token" is the norm, so a truncation collision
	// would be a credential crossover. Full FR-07 key unification and FR-08 TTL / eviction are
	// Story 8 — this only prevents the cross-tenant collision.
	// AC-00 (ENG-95262 story 7) deliberately stops short of this branch: session-target normalisation is
	// scoped to STDIO, and the passthrough branch is only ever taken in the mcp-http host, which is
	// deferred with story 5 (ADR §5, OQ-9). Normalising context.Url here would also change a
	// credential-discriminating key on a surface whose principal component does not exist yet.
	internal static string BuildPassthroughCacheKey(CredentialContext context) {
		CredentialMaterial auth = context.Auth;
		string material = string.Concat(
			context.IsNetCore ? "1" : "0", "|",
			((int)(auth?.Kind ?? CredentialKind.AccessToken)).ToString(), "|",
			auth?.AccessToken ?? string.Empty, "|",
			auth?.AccessTokenType ?? string.Empty, "|",
			auth?.Cookie ?? string.Empty, "|",
			auth?.Login ?? string.Empty, "|",
			auth?.Password ?? string.Empty);
		return $"{PassthroughKeyPrefix}{context.Url}:{HashSecretMaterial(material)}";
	}

	// Single secret-hashing helper shared by both cache keys (FR-07/FR-11): the credential material is
	// SHA-256 hashed so the discriminator is secret-free before it is placed in a key. The FULL hash is
	// returned (never truncated): on this feature "same url, different token" is the norm, so a
	// truncated-prefix collision would be a cross-tenant credential crossover.
	private static string HashSecretMaterial(string material) {
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
		return Convert.ToHexString(hash);
	}

	public TCommand ResolveWithoutEnvironment<TCommand>(EnvironmentOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		EnvironmentSettings settings = string.IsNullOrWhiteSpace(options.Environment)
			? new EnvironmentSettings {
				Login = DefaultIdentifier
			}
			: settingsRepository.FindEnvironment(options.Environment)
				?? new EnvironmentSettings {
					Login = DefaultIdentifier
				};
		// MCP command resolution is NEVER interactive (see ForceNonInteractiveConsole): Fill must use a
		// non-interactive console too, or a Safe-flagged environment resolved over an mcp-http host started
		// at a TTY would still deadlock on Console.ReadKey inside Fill (review RC-5). NonInteractiveConsole
		// fails closed, so a Safe environment surfaces SafeEnvironmentConfirmationRequiredException instead.
		settings = settings.Fill(options, NonInteractiveConsole.Shared);
		IServiceProvider container = new BindingsModule().Register(settings, NonInteractiveConsole.ForceInContainer);
		return container.GetRequiredService<TCommand>();
	}

	// FR-07: the hashed credential string now also includes AccessToken / AccessTokenType / Cookie so
	// two requests to the SAME url/environment with DISTINCT tokens (empty login/password) resolve to
	// distinct containers instead of colliding on a shared authenticated session. Secret material is
	// SHA-256 hashed via the shared helper and never placed raw in the key (FR-11).
	internal string BuildCacheKey(EnvironmentOptions options, EnvironmentSettings settings) {
		string identity = BuildTargetIdentity(options, settings);
		// ClientSecret is here, and AuthAppUri deliberately is NOT. Both were absent while the identity
		// still carried the ENVIRONMENT NAME, which distinguished two registrations on its own; AC-00
		// replaced the name with the normalised target, so two environments on one uri sharing a ClientId
		// — or both leaving it empty — would otherwise produce an IDENTICAL discriminator despite
		// different secrets, and the container cache and sticky registry would hand the second one the
		// first's authenticated client. That is the credential crossover T-5 exists to prevent.
		//
		// AuthAppUri is excluded because it is a DERIVED property, not stored state: when unset it returns
		// Uri lowercased with ".creatio.com" replaced by "-is.creatio.com/connect/token"
		// (ConfigurationOptions.cs). Hashing it would therefore re-split the very key AC-00 converged —
		// and worse, that string replacement is not stable across equivalent uris, since a uri carrying an
		// explicit :443 produces a mangled result the normaliser cannot fold back. A field whose default
		// is a function of the target belongs in the target half or nowhere; it is already in the target
		// half.
		//
		// Residual, stated rather than hidden: two environments identical in uri, ClientId and
		// ClientSecret but with different EXPLICIT AuthAppUri values still share a key. That is R-5's
		// per-principal credential fingerprint, which belongs to the deferred story 5 (ADR §5, OQ-9), not
		// to a value derived from the uri.
		string credentials = string.Concat(
			settings.Login ?? string.Empty, "|",
			settings.Password ?? string.Empty, "|",
			settings.ClientId ?? string.Empty, "|",
			settings.ClientSecret ?? string.Empty, "|",
			settings.AccessToken ?? string.Empty, "|",
			settings.AccessTokenType ?? string.Empty, "|",
			settings.Cookie ?? string.Empty, "|",
			settings.IsNetCore.ToString());
		return $"{identity}:{HashSecretMaterial(credentials)}";
	}

	/// <summary>
	/// Builds the TARGET component of the session key (ENG-95262 story 7, AC-00).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The identity is the NORMALISED TARGET and nothing else, so one resolved target produces one key
	/// whether it was reached by registered environment name or by explicit URI. It previously read
	/// <c>options.Environment ?? "default"</c> + <c>"|"</c> + <c>settings.Uri</c>, which yielded TWO keys
	/// for one target — <c>myenv|http://x</c> through the name branch and <c>default|http://x</c> through
	/// the URI branch. ENG-94529 put the uri into the identity (so a re-pointed environment stops handing
	/// back a client bound to its OLD uri, which the fold below preserves — the uri is still what the key
	/// is made of) but left the two branches divergent. Every registry stage 7 moves to the parent is keyed
	/// by tenant, so a split key would put <c>compile-creatio</c> invoked by name and <c>compile-status</c>
	/// polled by uri in different buckets and make the poll answer "no such operation" for a running compile.
	/// </para>
	/// <para>
	/// SCOPE — stdio only, and this method is the seam that keeps it extensible. R-5's sticky key has three
	/// components: principal + normalised target + credential fingerprint. On stdio the target IS the whole
	/// key, because the child reads <c>appsettings.json</c> itself and no credential crosses the boundary;
	/// the other two belong to the deferred story 5 (ADR §5, OQ-9). When <c>mcp-http</c> is revived they are
	/// composed in HERE, alongside the normalised target — the normaliser itself stays a pure target→target
	/// fold and does not need to change. (The credential hash appended by <see cref="BuildCacheKey"/> is the
	/// pre-existing FR-07 container discriminator, not R-5's per-process credential fingerprint.)
	/// </para>
	/// <para>
	/// A target that cannot be reached at all — a registered environment with no uri — keeps a name-derived
	/// identity. That is strictly MORE distinguishing than a normalised target, so it can only ever cost an
	/// extra container, never merge two targets. There is no uri branch to converge with in that case.
	/// </para>
	/// </remarks>
	private string BuildTargetIdentity(EnvironmentOptions options, EnvironmentSettings settings) =>
		string.IsNullOrWhiteSpace(settings.Uri)
			? $"env:{options.Environment ?? DefaultIdentifier}"
			: sessionTargetNormalizer.Normalize(settings.Uri);

	private string BuildEnvironmentNotFoundError(string missingEnvironmentName) =>
		EnvironmentNotFoundError.Build(missingEnvironmentName, settingsRepository);
}
