using System;
using System.Collections.Concurrent;
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
	IInteractiveConsole interactiveConsole,
	ICredentialContextAccessor credentialContextAccessor,
	ITargetUrlValidator targetUrlValidator) : IToolCommandResolver {

	private static readonly ConcurrentDictionary<string, IServiceProvider> ContainerCache = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Resolves a command against an explicit environment or URI-based target.
	/// </summary>
	/// <typeparam name="TCommand">The command type to resolve.</typeparam>
	/// <param name="options">Environment options that identify the execution target.</param>
	/// <returns>A command instance configured for the requested target.</returns>
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
			return ResolvePassthrough<TCommand>(credentialContext);
		}

		SettingsBootstrapReport bootstrapReport = settingsBootstrapService.GetReport();
		EnvironmentSettings settings;
		// These four throws are EXPECTED, caller-actionable resolution failures (unknown environment,
		// missing URI, broken settings bootstrap) → EnvironmentResolutionException, which BaseTool maps
		// to exit code 1. Unexpected failures below (settings.Fill, BindingsModule.Register,
		// GetRequiredService) stay plain exceptions → exit code -1, so a real DI/wiring bug remains
		// distinguishable from a bad environment name.
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
			settings = settings.Fill(options, interactiveConsole);
		}
		else {
			settings = new EnvironmentSettings().Fill(options, interactiveConsole);
			if (string.IsNullOrWhiteSpace(settings.Uri)) {
				if (!bootstrapReport.CanExecuteEnvTools) {
					throw new EnvironmentResolutionException(
						$"clio settings bootstrap is broken. Repair {bootstrapReport.SettingsFilePath}. Explicit uri/login/password remains available only as an emergency fallback.");
				}
				throw new EnvironmentResolutionException(
					"Either a configured environment name or an explicit URI is required for MCP command execution. Prefer a registered environment name; use explicit URI credentials only as a bootstrap or emergency fallback.");
			}
		}
		string cacheKey = BuildCacheKey(options, settings);
		IServiceProvider container = ContainerCache.GetOrAdd(cacheKey,
			_ => new BindingsModule().Register(settings));
		return container.GetRequiredService<TCommand>();
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
		// Nothing is persisted (AC-03): the ephemeral settings never touch the settings repository,
		// disk, session, or appsettings.json — only this in-memory container cache.
		IServiceProvider container = ContainerCache.GetOrAdd(cacheKey,
			_ => new BindingsModule().Register(settings));
		return container.GetRequiredService<TCommand>();
	}

	/// <summary>
	/// Builds an ephemeral, non-persisted <see cref="EnvironmentSettings"/> directly from a
	/// per-request <see cref="CredentialContext"/>. Never consults the settings repository, matches
	/// an environment name, or runs the interactive <c>Fill</c> path — the header-built environment
	/// carries no Safe flag and stays non-interactive / fail-closed (A-06).
	/// </summary>
	/// <param name="context">The per-request credential context (url + precedence-resolved auth).</param>
	/// <returns>An in-memory <see cref="EnvironmentSettings"/> carrying the target url and credential material.</returns>
	internal static EnvironmentSettings BuildEphemeralSettings(CredentialContext context) {
		ArgumentNullException.ThrowIfNull(context);
		CredentialMaterial auth = context.Auth;
		EnvironmentSettings settings = new() {
			Uri = context.Url
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

	private static bool HasUsableAuth(CredentialMaterial auth) =>
		auth?.Kind switch {
			CredentialKind.AccessToken => !string.IsNullOrWhiteSpace(auth.AccessToken),
			CredentialKind.Cookie => !string.IsNullOrWhiteSpace(auth.Cookie),
			CredentialKind.LoginPassword => !string.IsNullOrWhiteSpace(auth.Login),
			_ => false
		};

	// Dedicated passthrough cache key that INCLUDES the credential discriminator (kind + token /
	// cookie / login+password), unlike BuildCacheKey which hashes only Login|Password|ClientId|IsNetCore
	// and would collide two different bearer tokens on the same url (cross-tenant reuse). The secret
	// material is SHA-256 hashed, never placed raw in the key. The FULL hash is used (not a 64-bit
	// truncation): on this feature "same url, different token" is the norm, so a truncation collision
	// would be a credential crossover. Full FR-07 key unification and FR-08 TTL / eviction are
	// Story 8 — this only prevents the cross-tenant collision.
	internal static string BuildPassthroughCacheKey(CredentialContext context) {
		CredentialMaterial auth = context.Auth;
		string material = string.Concat(
			((int)(auth?.Kind ?? CredentialKind.AccessToken)).ToString(), "|",
			auth?.AccessToken ?? string.Empty, "|",
			auth?.AccessTokenType ?? string.Empty, "|",
			auth?.Cookie ?? string.Empty, "|",
			auth?.Login ?? string.Empty, "|",
			auth?.Password ?? string.Empty);
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
		return $"passthrough:{context.Url}:{Convert.ToHexString(hash)}";
	}

	public TCommand ResolveWithoutEnvironment<TCommand>(EnvironmentOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		EnvironmentSettings settings = string.IsNullOrWhiteSpace(options.Environment)
			? new EnvironmentSettings {
				Login = "default"
			}
			: settingsRepository.FindEnvironment(options.Environment)
				?? new EnvironmentSettings {
					Login = "default"
				};
		settings = settings.Fill(options, interactiveConsole);
		IServiceProvider container = new BindingsModule().Register(settings);
		return container.GetRequiredService<TCommand>();
	}

	private static string BuildCacheKey(EnvironmentOptions options, EnvironmentSettings settings) {
		string identity = options.Environment
			?? settings.Uri
			?? "default";
		string credentials = string.Concat(
			settings.Login ?? string.Empty, "|",
			settings.Password ?? string.Empty, "|",
			settings.ClientId ?? string.Empty, "|",
			settings.IsNetCore.ToString());
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(credentials));
		return $"{identity}:{Convert.ToHexString(hash)[..16]}";
	}

	private string BuildEnvironmentNotFoundError(string missingEnvironmentName) =>
		EnvironmentNotFoundError.Build(missingEnvironmentName, settingsRepository);
}
