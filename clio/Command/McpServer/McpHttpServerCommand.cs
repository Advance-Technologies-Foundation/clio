using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using AspNetWebApplication = Microsoft.AspNetCore.Builder.WebApplication;
using AspNetWebApplicationBuilder = Microsoft.AspNetCore.Builder.WebApplicationBuilder;

namespace Clio.Command.McpServer;

[Verb("mcp-http", HelpText = "Starts MCP server in HTTP transport mode")]
public class McpHttpServerCommandOptions : BaseCommandOptions
{
	[Option("port", Default = 8005, Required = false, HelpText = "Port to listen on")]
	public int Port { get; set; }

	[Option("host", Default = "127.0.0.1", Required = false, HelpText = "Host address to bind to")]
	public string Host { get; set; }

	[Option("path", Default = "/mcp", Required = false, HelpText = "MCP endpoint path")]
	public string Path { get; set; }

	[Option("credentials-header-name", Default = "X-Integration-Credentials", Required = false,
		HelpText = "Name of the request header carrying base64-encoded JSON per-request credentials")]
	public string CredentialsHeaderName { get; set; }

	[Option("platform-api-key", Required = false,
		HelpText = "Non-OAuth dev/offline fallback for per-request credential passthrough. "
			+ "Comma-separated platform API key(s) (here or via CLIO_MCP_HTTP_PLATFORM_API_KEY); "
			+ "requests must present a matching 'Authorization: Bearer <key>'. IGNORED entirely "
			+ "when --auth-authority is configured -- standard OAuth authorization is then the only "
			+ "front door (it cannot be combined with this key on the same Authorization header).")]
	public string PlatformApiKey { get; set; }

	[Option("allowed-base-urls", Required = false,
		HelpText = "Comma-separated allowlist of origins (scheme+host+port) that a per-request "
			+ "passthrough target url may target. When set, a target whose origin is not on the "
			+ "list is rejected before any outbound call. When unset, only the always-on baseline "
			+ "blocks (link-local / cloud-metadata / loopback) apply.")]
	public string AllowedBaseUrls { get; set; }

	[Option("session-idle-ttl", Default = "5m", Required = false,
		HelpText = "Idle time after which an unused per-session container is evicted. Accepts a "
			+ "suffixed duration ('90s', '5m', '1h', '1d'), a bare number of seconds ('300'), or a "
			+ "TimeSpan ('00:05:00'). Defaults to 5 minutes.")]
	public string SessionIdleTtl { get; set; }

	[Option("max-sessions", Default = 50, Required = false,
		HelpText = "Maximum number of per-session containers kept in memory. When exceeded, the "
			+ "least-recently-used container is evicted. Defaults to 50.")]
	public int MaxSessions { get; set; }

	[Option("auth-authority", Required = false,
		HelpText = "OIDC authority (discovery/JWKS base URL) of the OAuth 2.1 Authorization Server "
			+ "whose access tokens this edge accepts. Setting it (here or via CLIO_MCP_HTTP_AUTH_AUTHORITY) "
			+ "ENABLES standard bearer-JWT authorization on the whole endpoint. Unset (default) => "
			+ "authorization is off and the edge behaves exactly as before.")]
	public string AuthAuthority { get; set; }

	[Option("auth-audience", Required = false,
		HelpText = "Comma-separated accepted audience(s) the token must be issued for (also read from "
			+ "CLIO_MCP_HTTP_AUTH_AUDIENCE). Validated against the token 'aud' claim.")]
	public string AuthAudience { get; set; }

	[Option("auth-required-scopes", Required = false,
		HelpText = "Comma-separated scope(s) every request must carry, all required (also read from "
			+ "CLIO_MCP_HTTP_AUTH_REQUIRED_SCOPES). Checked against the token 'scope'/'scp' claim.")]
	public string AuthRequiredScopes { get; set; }

	[Option("auth-issuer", Required = false,
		HelpText = "Comma-separated accepted issuer(s) for the token 'iss' claim (also read from "
			+ "CLIO_MCP_HTTP_AUTH_ISSUER). Optional: when unset, the issuer is validated against the "
			+ "discovery document's issuer. Use it when the token 'iss' (public authority) differs from "
			+ "--auth-authority (internal discovery URL).")]
	public string AuthIssuer { get; set; }

	[Option("auth-allow-insecure-metadata", Required = false,
		HelpText = "Allow OIDC metadata/JWKS to be fetched over plain HTTP (also enabled by a truthy "
			+ "CLIO_MCP_HTTP_AUTH_ALLOW_INSECURE_METADATA). Default is HTTPS-only; set this only for an "
			+ "internal-DNS HTTP authority on a trusted network.")]
	public bool AuthAllowInsecureMetadata { get; set; }

	[Option("auth-resource", Required = false,
		HelpText = "Explicit Protected Resource Metadata 'resource' (canonical MCP endpoint URI) override "
			+ "(also CLIO_MCP_HTTP_AUTH_RESOURCE). Unset (default) => derived per-request from the "
			+ "incoming scheme/host/path, which is correct behind any ingress that forwards the Host "
			+ "header. Set only when auto-derivation is wrong for this deployment.")]
	public string AuthResource { get; set; }

	[Option("allow-insecure-public", Required = false,
		HelpText = "Allow starting with a public/wildcard --host (e.g. 0.0.0.0) while authorization is "
			+ "OFF (no --auth-authority configured) (also a truthy CLIO_MCP_HTTP_ALLOW_INSECURE_PUBLIC). "
			+ "Without this flag, clio REFUSES TO START in that combination: an unauthenticated public "
			+ "bind exposes every registered environment's stored credentials to anyone who can reach "
			+ "the port. Not recommended outside a fully trusted network.")]
	public bool AllowInsecurePublic { get; set; }

	[Option("auth-allow-any-audience", Required = false,
		HelpText = "Allow enabling standard OAuth authorization (--auth-authority) with NEITHER "
			+ "--auth-audience NOR --auth-required-scopes configured (also a truthy "
			+ "CLIO_MCP_HTTP_AUTH_ALLOW_ANY_AUDIENCE). Without this flag, clio REFUSES TO START in that "
			+ "combination: with no audience or scope restriction, the endpoint would accept ANY token "
			+ "the configured --auth-authority ever mints for ANY client/resource, not just this one -- a "
			+ "confused-deputy risk on a shared identity platform. Configure --auth-audience and/or "
			+ "--auth-required-scopes instead wherever possible; this flag is a documented escape hatch, "
			+ "not a recommended posture.")]
	public bool AuthAllowAnyAudience { get; set; }
}

/// <summary>
/// Starts the MCP server using HTTP (Streamable HTTP) transport, backed by clio's full DI graph.
/// All tools registered for the stdio transport are available over HTTP as well.
/// </summary>
public class McpHttpServerCommand : Command<McpHttpServerCommandOptions>
{
	public override int Execute(McpHttpServerCommandOptions options) => Run(options);

	private static readonly string[] LoopbackHostAliases = ["localhost", "127.0.0.1", "[::1]", "::1"];

	internal static int Run(McpHttpServerCommandOptions options) {
		if (!IsTruthyEnvironmentFlag("CLIO_MCP_RESPECT_AMBIENT_PROXY")) {
			System.Net.Http.HttpClient.DefaultProxy = new System.Net.WebProxy();
		}

		AspNetWebApplicationBuilder builder = AspNetWebApplication.CreateBuilder();
		builder.WebHost.UseUrls($"http://{options.Host}:{options.Port}");

		// Validate the whole DI graph at build time so a scope/lifetime or missing-registration
		// mistake fails fast at startup instead of surfacing as a 500 on the first request. The root
		// provider of a WebApplicationBuilder is built by the host's DefaultServiceProviderFactory,
		// which reads its options from UseDefaultServiceProvider — NOT from a ServiceProviderOptions
		// resolved out of the collection it is about to build — so Configure<ServiceProviderOptions>
		// would have no effect here.
		builder.Host.UseDefaultServiceProvider(o => {
			o.ValidateOnBuild = true;
			o.ValidateScopes = true;
		});

		ConfigureHostFiltering(builder.Services, options.Host);

		BindingsModule bindingsModule = new();
		ISettingsRepository settingsRepository =
			bindingsModule.RegisterInto(builder.Services);
		BindingsModule.RegisterMcpServer(builder.Services, settingsRepository)
			.WithHttpTransport(ConfigureHttpTransport);

		// Per-request credential-passthrough seam (Story 4). Registered in the HTTP host,
		// NOT the shared BindingsModule, so IHttpContextAccessor is not pulled into the
		// stdio graph. STORY 7 RESOLUTION: the shared BindingsModule.RegisterInto registers
		// null-object defaults (NullCredentialContextAccessor / NullTargetUrlValidator) so the
		// credential resolver's ctor deps resolve in BOTH hosts; the REAL accessor + validator
		// are registered here, AFTER the shared build, so last-registration-wins gives the real
		// ones in HTTP and the null objects in stdio / ephemeral containers.
		builder.Services.AddHttpContextAccessor();
		builder.Services.AddSingleton<ICredentialHeaderParser, CredentialHeaderParser>();
		builder.Services.AddSingleton<ICredentialContextAccessor, CredentialContextAccessor>();

		// Edge API-key gate (Story 5, FR-09/FR-10). Built from the CLI flag plus the
		// CLIO_MCP_HTTP_PLATFORM_API_KEY env var at Run time and registered as an instance,
		// so the key set is fixed for the lifetime of this host. HTTP-host-scoped like the
		// parser/accessor above; see the BindingsModule skip-list note.
		IReadOnlyList<string> platformApiKeys = PlatformApiKeyConfiguration.Resolve(
			options.PlatformApiKey,
			Environment.GetEnvironmentVariable(PlatformApiKeyConfiguration.EnvironmentVariableName));
		builder.Services.AddSingleton<IPlatformApiKeyGate>(new PlatformApiKeyGate(platformApiKeys));

		// SSRF / egress guard (Story 6, FR-17). Built at Run time from the bound host plus the
		// parsed --allowed-base-urls flag and registered as an instance, so its policy is fixed
		// for the lifetime of this host. HTTP-host-scoped like the gate above; Story 7 resolves
		// it from the resolution path (which runs in the HTTP host) before client construction.
		// See the BindingsModule skip-list note.
		IReadOnlyList<string> allowedBaseUrls =
			AllowedBaseUrlsConfiguration.Resolve(options.AllowedBaseUrls);
		builder.Services.AddSingleton<ITargetUrlValidator>(
			new TargetUrlValidator(options.Host, allowedBaseUrls));

		// Bounded, evictable per-session container cache (Story 8, FR-08). Configured at Run time from
		// --session-idle-ttl / --max-sessions and registered as an instance AFTER the shared build, so
		// last-registration-wins gives this configured cache in HTTP while stdio / ephemeral containers
		// keep the shared default. HTTP-host-scoped like the gate/validator above; see the
		// BindingsModule skip-list note.
		TimeSpan sessionIdleTtl = SessionContainerCacheDefaults.ResolveIdleTtl(options.SessionIdleTtl);
		int maxSessions = options.MaxSessions > 0
			? options.MaxSessions
			: SessionContainerCacheDefaults.MaxSessions;
		builder.Services.AddSingleton<ISessionContainerCache>(
			new SessionContainerCache(sessionIdleTtl, maxSessions));

		// Standard OAuth 2.1 Resource-Server authorization (ENG-93386, Story 2/3). Resolved from the
		// --auth-* flags plus the CLIO_MCP_HTTP_AUTH_* env vars. Enabled iff an authority is configured;
		// when disabled we register NOTHING here and add no authN/authZ middleware below — calling
		// ASP.NET's UseAuthentication with no registered scheme throws, so skipping the whole block is
		// required, not just cosmetic; the edge then behaves exactly as before.
		AuthConfiguration authConfiguration =
			AuthConfiguration.Resolve(options, AuthEnvironment.FromProcessEnvironment());
		if (authConfiguration.Enabled) {
			McpHttpAuthentication.ConfigureServices(builder.Services, authConfiguration);
		}

		// ENG-93386 Story 7 (FR-12/D-6): --platform-api-key is retired as the default front door
		// but retained as a dev/offline fallback for when OAuth is not configured. Once OAuth IS
		// configured, the key is silently bypassed (see EnforcePlatformApiKeyGate) rather than
		// combined with it — flag the now-inert configuration loudly so it is never a silent
		// misconfiguration.
		if (ShouldWarnPlatformApiKeyIgnored(authConfiguration.Enabled, platformApiKeys.Count)) {
			ConsoleLogger.Instance.WriteWarning(
				"Both --auth-authority and --platform-api-key are configured; --platform-api-key is "
				+ "IGNORED while standard OAuth authorization is enabled — it cannot be combined with "
				+ "OAuth on the same Authorization header (FR-12). Remove --platform-api-key / unset "
				+ "CLIO_MCP_HTTP_PLATFORM_API_KEY if it is no longer needed.");
		}

		// Public-bind guard (Story 5, OQ-A: REFUSE, not just warn — security-first default). A wildcard
		// --host with authorization OFF means every registered environment's stored credentials are
		// reachable to anyone who can route to this port; that combination must not silently start.
		bool allowInsecurePublic = options.AllowInsecurePublic
			|| IsTruthyEnvironmentFlag("CLIO_MCP_HTTP_ALLOW_INSECURE_PUBLIC");
		PublicBindGuardOutcome guardOutcome = EvaluatePublicBindGuard(
			IsPublicBind(options.Host), authConfiguration.Enabled, allowInsecurePublic);
		switch (guardOutcome) {
			case PublicBindGuardOutcome.Refuse:
				ConsoleLogger.Instance.WriteError(
					$"Refusing to start: --host {options.Host} is a public/wildcard bind with no "
					+ "authorization configured (no --auth-authority). This would expose every "
					+ "registered environment's stored credentials to anyone who can reach the port. "
					+ "Configure --auth-authority, or pass --allow-insecure-public to start anyway "
					+ "(not recommended).");
				return 1;
			case PublicBindGuardOutcome.Warn:
				ConsoleLogger.Instance.WriteWarning(
					$"mcp-http is bound to {options.Host} (public/wildcard) with NO authorization "
					+ "configured; --allow-insecure-public was set. Every registered environment's "
					+ "stored credentials are reachable to anyone who can reach this port. Use only on "
					+ "a fully trusted network.");
				break;
		}

		// Audience/scope guard (ENG-93386, Story 8 final-review fix; REFUSE, not just warn —
		// security-first default, same posture as the public-bind guard above). Enabling authorization
		// requires only --auth-authority; with NEITHER --auth-audience NOR --auth-required-scopes also
		// configured, the JWT bearer handler accepts ANY token the shared issuer ever mints for ANY
		// client/resource (ValidateAudience becomes false and the scope check is vacuously satisfied by
		// an empty required-scope set) — a confused-deputy risk on a shared identity platform.
		bool allowAnyAudience = options.AuthAllowAnyAudience
			|| IsTruthyEnvironmentFlag("CLIO_MCP_HTTP_AUTH_ALLOW_ANY_AUDIENCE");
		PublicBindGuardOutcome audienceGuardOutcome = EvaluateAudienceScopeGuard(
			authConfiguration.Enabled, authConfiguration.Audiences.Count, authConfiguration.RequiredScopes.Count,
			allowAnyAudience);
		switch (audienceGuardOutcome) {
			case PublicBindGuardOutcome.Refuse:
				ConsoleLogger.Instance.WriteError(
					"Refusing to start: --auth-authority is configured but neither --auth-audience nor "
					+ "--auth-required-scopes is set. The endpoint would accept any token the configured "
					+ "authority ever mints for any client or resource, not just this one. Configure "
					+ "--auth-audience and/or --auth-required-scopes, or pass --auth-allow-any-audience "
					+ "to start anyway (not recommended).");
				return 1;
			case PublicBindGuardOutcome.Warn:
				ConsoleLogger.Instance.WriteWarning(
					"Standard OAuth authorization is enabled with NEITHER --auth-audience NOR "
					+ "--auth-required-scopes configured; --auth-allow-any-audience was set. The endpoint "
					+ "accepts any token the configured authority mints for any client or resource. Use "
					+ "only when the authority is exclusively dedicated to this deployment.");
				break;
		}

		AspNetWebApplication app = builder.Build();

		// FR-05/FR-08 (ENG-93208): wire the tool-execution-lock facade to this host's DI-registered
		// per-tenant lock provider and the run-time-configured session-container cache, so per-tenant
		// serialization and the in-flight eviction guard operate on the SAME instances the resolution
		// path (ToolCommandResolver) uses in the HTTP host.
		McpToolExecutionLock.Configure(
			app.Services.GetRequiredService<ITenantExecutionLockProvider>(),
			app.Services.GetRequiredService<ISessionContainerCache>());

		// DNS-rebinding / cross-origin protection. The MCP spec makes Origin/Host validation the
		// host's responsibility (ModelContextProtocol.AspNetCore does not do it automatically), and
		// every registered tool acts with the operator's stored credentials — so loopback binding is
		// not a mitigation on its own. UseHostFiltering rejects unexpected Host headers; the Origin
		// check rejects browser requests from non-allowlisted origins. Native MCP clients send no
		// Origin header and pass through unaffected.
		app.UseHostFiltering();
		app.Use((context, next) => ValidateOrigin(context, next, options.Host));

		// Standard OAuth 2.1 authentication/authorization (ENG-93386, Story 3). Added ONLY when
		// authorization is enabled (an authority is configured); adding UseAuthentication with no
		// registered scheme would throw. Endpoint enforcement (RequireAuthorization) + the fail-safe /
		// public-bind guard land in Story 5 — here the pipeline only authenticates and authorizes so the
		// principal is available to the credential-capture middleware below (Story 6 gates on it).
		if (authConfiguration.Enabled) {
			app.UseAuthentication();
			app.UseAuthorization();
		}

		// Per-request credential-passthrough leg (Story 5/4). Always wired. When OAuth is NOT
		// configured (default, dev/offline), gated SOLELY by the platform API-key gate, which
		// fail-closes when no key is configured — unchanged pre-ENG-93386 behavior. When OAuth IS
		// configured, the legacy key gate is bypassed (FR-12/D-6, Story 7): passthrough eligibility
		// comes solely from the authenticated principal RequireAuthorization already guaranteed.
		//
		// The gate runs BEFORE the credential-capture middleware so a credential header is treated
		// as trusted only after this gate authorizes the request.
		IPlatformApiKeyGate platformApiKeyGate =
			app.Services.GetRequiredService<IPlatformApiKeyGate>();
		app.Use((context, next) => EnforcePlatformApiKeyGate(
			context, next, platformApiKeyGate, options.CredentialsHeaderName, authConfiguration.Enabled));

		ICredentialHeaderParser credentialHeaderParser =
			app.Services.GetRequiredService<ICredentialHeaderParser>();
		ICredentialContextAccessor credentialContextAccessor =
			app.Services.GetRequiredService<ICredentialContextAccessor>();
		app.Use((context, next) => CaptureCredentialContext(
			context, next, credentialHeaderParser, credentialContextAccessor,
			options.CredentialsHeaderName, authConfiguration.Enabled));

		// Whole-endpoint enforcement (ENG-93386, Story 5): when authorization is enabled, EVERY request
		// to the MCP endpoint — passthrough AND pre-registered -e <env> / stored-credential access alike
		// — requires a valid token. This closes the "gates only passthrough" gap the bespoke platform-API-
		// key gate left open. When disabled, MapMcp carries no authorization requirement (today's behavior).
		IEndpointConventionBuilder mcpEndpoint = app.MapMcp(options.Path);
		if (authConfiguration.Enabled) {
			mcpEndpoint.RequireAuthorization(McpHttpAuthentication.PolicyName);
		}

		ConsoleLogger.Instance.WriteInfo(
			$"MCP HTTP server listening on http://{options.Host}:{options.Port}{options.Path}");
		app.Run();
		return 0;
	}

	/// <summary>
	/// Pins the MCP HTTP transport options that the credential-passthrough edge depends on, so a future
	/// SDK default change cannot silently drift them (ADR RISK #1, Story 15e). The single shared lambda is
	/// applied both here (via <c>WithHttpTransport</c>) and in
	/// <c>McpHttpTransportDefaultsTests</c>, so the assertion and the production wiring can never diverge.
	/// <list type="bullet">
	/// <item><description><see cref="HttpServerTransportOptions.PerSessionExecutionContext"/> = <see langword="false"/>:
	/// tool handlers run on the REQUEST's <see cref="System.Threading.ExecutionContext"/>, which is what
	/// lets the per-request credential context set by the capture middleware flow into the handler. If this
	/// flipped to <see langword="true"/> the handler would run on the session's captured context and
	/// passthrough would silently break.</description></item>
	/// <item><description><see cref="HttpServerTransportOptions.EnableLegacySse"/> = <see langword="false"/>:
	/// only the modern Streamable HTTP endpoint is exposed; the legacy request/response-split SSE endpoints
	/// (/sse, /message) are not mapped.</description></item>
	/// <item><description><see cref="HttpServerTransportOptions.Stateless"/> = <see langword="false"/>:
	/// the server tracks per-session state (the per-session container cache keys off it).</description></item>
	/// </list>
	/// </summary>
	/// <param name="options">The transport options instance the SDK is configuring.</param>
	internal static void ConfigureHttpTransport(HttpServerTransportOptions options) {
		// MCP9004: EnableLegacySse is [Obsolete] because ENABLING legacy SSE is unsafe. We are pinning it
		// to the SAFE value (false) precisely to keep the legacy endpoints disabled and guard against a
		// future SDK default flip — the obsolete member is used only to assert that safe state, never to
		// enable it. Suppression is scoped to this single assignment.
#pragma warning disable MCP9004
		options.EnableLegacySse = false;
#pragma warning restore MCP9004
		options.PerSessionExecutionContext = false;
		options.Stateless = false;
	}

	private static void ConfigureHostFiltering(IServiceCollection services, string boundHost) {
		List<string> allowedHosts = BuildAllowedHosts(boundHost);
		services.Configure<HostFilteringOptions>(o => {
			o.AllowedHosts = allowedHosts;
			o.AllowEmptyHosts = false;
			o.IncludeFailureMessage = false;
		});
	}

	internal static List<string> BuildAllowedHosts(string boundHost) {
		if (IsWildcardHost(boundHost)) {
			// A wildcard bind (e.g. --host 0.0.0.0) has no single legitimate Host value to restrict
			// to; Origin validation remains the effective anti-rebinding control for browser clients.
			return ["*"];
		}

		List<string> hosts = [boundHost];
		if (IsLoopbackAlias(boundHost)) {
			hosts.AddRange(LoopbackHostAliases);
		}

		return hosts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	/// <summary>
	/// Item key used by Story 5's API-key gate middleware (which runs earlier) to signal
	/// whether per-request credential passthrough is enabled. Absent ⇒ <see langword="false"/>.
	/// </summary>
	internal const string PassthroughEnabledItemKey = "clio.mcp.passthrough-enabled";

	// Story 5 edge API-key gate. Fail-closed and strictly additive: when no platform API
	// key is configured (default) the request behaves exactly as 8.1.0.72 — the credential
	// header is never treated as trusted (AC-02). When passthrough is enabled, a request
	// carrying the credential header must present a matching Authorization Bearer key. A
	// missing or mismatched key short-circuits with HTTP 401 and no secret (AC-03/AC-ERR).
	// The decision is published to the pipeline via PassthroughEnabledItemKey, which the
	// credential-capture middleware honors.
	//
	// ENG-93386 Story 7 (FR-12/D-6): once standard OAuth is configured, this legacy gate is
	// BYPASSED entirely rather than combined with it — the two schemes cannot coexist on the
	// same 'Authorization' header (JwtBearerHandler already claims it for the JWT once OAuth is
	// enabled), so an AND/OR combination is not even meaningful. Passthrough eligibility then
	// comes solely from the authenticated principal that RequireAuthorization (Story 5) already
	// guaranteed for this endpoint; CaptureCredentialContext's own authenticated-principal check
	// (Story 6) independently re-verifies it. When OAuth is not configured (default, dev/offline),
	// this gate behaves exactly as before ENG-93386 — --platform-api-key's retained fallback role.
	internal static async Task EnforcePlatformApiKeyGate(
		HttpContext context,
		RequestDelegate next,
		IPlatformApiKeyGate gate,
		string credentialHeaderName,
		bool authorizationEnabled) {
		if (authorizationEnabled) {
			context.Items[PassthroughEnabledItemKey] = context.User?.Identity?.IsAuthenticated == true;
			await next(context);
			return;
		}

		if (!gate.PassthroughEnabled) {
			// No key configured: fail-closed default. Downstream ignores the credential header.
			context.Items[PassthroughEnabledItemKey] = false;
			await next(context);
			return;
		}

		// Passthrough is enabled ⇒ this endpoint is a network edge, so the API key is required on EVERY
		// request, not only credential-bearing ones (review #1, ENG-93208). Otherwise a request that omits
		// the credential header would reach the full MCP tool surface — including registered-environment
		// tools and reg-web-app — completely unauthenticated on a non-loopback bind. Fail closed on the
		// whole surface: authenticate first, then decide whether THIS request also carries passthrough
		// credentials. Default (no key configured, branch above) stays exactly 8.1.0.72.
		string authorization = context.Request.Headers.Authorization.ToString();
		if (!gate.IsAuthorized(authorization)) {
			context.Items[PassthroughEnabledItemKey] = false;
			context.Response.StatusCode = StatusCodes.Status401Unauthorized;
			context.Response.ContentType = "application/json";
			// No secret (key or credentials) is echoed (FR-11).
			await context.Response.WriteAsJsonAsync(
				new { error = "Error: platform API key missing or invalid" });
			return;
		}

		// Authenticated. Passthrough is honored for this request only when it actually carries the
		// credential header; an authenticated no-header request is a legitimate pre-registered -e call and
		// runs with passthrough OFF (exactly 8.1.0.72 resolution), but it is no longer unauthenticated.
		context.Items[PassthroughEnabledItemKey] = context.Request.Headers.ContainsKey(credentialHeaderName);
		await next(context);
	}

	// Reads the configured credential header, parses it into a per-request CredentialContext,
	// and publishes it via ICredentialContextAccessor. Only acts when the earlier API-key gate
	// enabled passthrough for this request (PassthroughEnabledItemKey == true). When the item is
	// false/absent the credential header is ignored entirely — no parse, no 400 — so an
	// untrusted/no-key request behaves exactly as 8.1.0.72 (AC-02). Parse failure inside the
	// trusted path ⇒ HTTP 400 with a JSON body naming the defect (no secret).
	//
	// ENG-93386 Story 6 (FR-07 header-strip): when standard OAuth authorization is configured
	// (requireAuthenticatedPrincipal == true), the header is honored ONLY on an authenticated
	// request. Story 5's RequireAuthorization on the /mcp endpoint already makes this true for any
	// request reaching this middleware via that endpoint — this check makes the invariant explicit
	// and independently testable rather than an emergent property of middleware order, and also
	// covers a request that reaches this globally-wired middleware via a path other than /mcp
	// (where RequireAuthorization never ran). Additive: when authorization is not configured
	// (requireAuthenticatedPrincipal == false), passthrough keeps working via the platform-API-key
	// gate alone — unchanged, pre-ENG-93386 behavior; Story 7 decides that gate's ultimate fate.
	internal static async Task CaptureCredentialContext(
		HttpContext context,
		RequestDelegate next,
		ICredentialHeaderParser parser,
		ICredentialContextAccessor accessor,
		string headerName,
		bool requireAuthenticatedPrincipal) {
		bool passthroughEnabled =
			context.Items.TryGetValue(PassthroughEnabledItemKey, out object flag)
			&& flag is true;

		if (!passthroughEnabled) {
			// Not a trusted passthrough request — ignore the credential header (AC-02).
			await next(context);
			return;
		}

		if (requireAuthenticatedPrincipal && context.User?.Identity?.IsAuthenticated != true) {
			// FR-07: standard OAuth is configured but this request carries no valid principal —
			// the credential header is stripped/ignored, not merely deferred (AC-03).
			await next(context);
			return;
		}

		if (!context.Request.Headers.TryGetValue(headerName, out var headerValues)) {
			// No credential header — nothing to capture; context stays null.
			await next(context);
			return;
		}

		if (!parser.TryParse(headerValues.ToString(), out CredentialParseResult parsed, out string error)) {
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			context.Response.ContentType = "application/json";
			// error is defect-only and never carries a secret value (FR-11).
			await context.Response.WriteAsJsonAsync(new { error = $"Error: {error}" });
			return;
		}

		accessor.Current = new CredentialContext(
			parsed.Url, parsed.Auth, McpTransport.Http, passthroughEnabled);

		await next(context);
	}

	private static Task ValidateOrigin(HttpContext context, RequestDelegate next, string boundHost) {
		if (!context.Request.Headers.TryGetValue("Origin", out var originValues)) {
			// No Origin header — a non-browser (native MCP transport) client; nothing to validate.
			return next(context);
		}

		if (IsAllowedOrigin(originValues.ToString(), boundHost)) {
			return next(context);
		}

		context.Response.StatusCode = StatusCodes.Status403Forbidden;
		return Task.CompletedTask;
	}

	internal static bool IsAllowedOrigin(string origin, string boundHost) {
		if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri originUri)) {
			return false;
		}

		string originHost = originUri.Host;
		if (IsLoopbackAlias(originHost)) {
			return true;
		}

		return !IsWildcardHost(boundHost)
			&& string.Equals(originHost, boundHost, StringComparison.OrdinalIgnoreCase);
	}

	// Membership test against the fixed loopback ALIAS literals (localhost / 127.0.0.1 / ::1).
	// Deliberately NARROWER than TargetUrlValidator.IsLoopbackIpOrLocalhost (which parses any
	// 127.0.0.0/8 address as loopback): this gates Host-header allow-listing and Origin filtering,
	// where only the exact bound/alias hostnames are legitimate — e.g. 127.0.0.2 is NOT a valid
	// Host/Origin here. The two predicates guard different controls and are intentionally divergent.
	private static bool IsLoopbackAlias(string host) =>
		LoopbackHostAliases.Contains(host, StringComparer.OrdinalIgnoreCase);

	private static bool IsWildcardHost(string host) =>
		host is "0.0.0.0" or "*" or "::" or "[::]";

	/// <summary>The public-bind guard's decision (ENG-93386, Story 5, OQ-A).</summary>
	internal enum PublicBindGuardOutcome
	{
		/// <summary>No conflict: either the bind is not public, or authorization is enabled.</summary>
		Ok,

		/// <summary>Public bind with authorization off, but the operator explicitly opted in; start with a loud warning.</summary>
		Warn,

		/// <summary>Public bind with authorization off and no explicit opt-in; refuse to start.</summary>
		Refuse
	}

	/// <summary>
	/// Decides whether a public/reachable <c>--host</c> combined with authorization being off is allowed
	/// to start. Security-first default: REFUSE, not merely warn — an unauthenticated reachable bind
	/// exposes every registered environment's stored credentials to any caller that can reach the port.
	/// The operator can explicitly override via <c>--allow-insecure-public</c> (or the matching env var).
	/// </summary>
	/// <param name="isPublicBind">Whether <c>--host</c> is reachable by more than just this machine's own
	/// loopback interface — a wildcard bind (<c>0.0.0.0</c>) or any concrete non-loopback address/hostname
	/// (see <see cref="IsPublicBind"/>). Final-review fix (Story 8): originally this only covered the four
	/// literal wildcard spellings, silently missing a bind to a concrete LAN/public IP or DNS name — the
	/// exact "gates only the literal wildcard" gap this guard exists to close.</param>
	/// <param name="authorizationEnabled">Whether OAuth Resource-Server authorization is configured.</param>
	/// <param name="allowInsecurePublic">Whether the operator explicitly opted into the insecure combination.</param>
	/// <returns>The guard's decision.</returns>
	internal static PublicBindGuardOutcome EvaluatePublicBindGuard(
		bool isPublicBind, bool authorizationEnabled, bool allowInsecurePublic) {
		if (!isPublicBind || authorizationEnabled) {
			return PublicBindGuardOutcome.Ok;
		}
		return allowInsecurePublic ? PublicBindGuardOutcome.Warn : PublicBindGuardOutcome.Refuse;
	}

	/// <summary>
	/// ENG-93386 Story 8 (final-review fix). Decides whether standard OAuth authorization being enabled
	/// with NEITHER an accepted audience NOR a required scope configured is allowed to start. Security-first
	/// default: REFUSE, not merely warn — with both empty, <see cref="McpHttpAuthentication.BuildTokenValidationParameters"/>
	/// disables audience validation and the scope check is vacuously satisfied, so the endpoint accepts any
	/// token the configured authority ever mints for any client or resource (a confused-deputy risk on a
	/// shared identity platform). The operator can explicitly override via <c>--auth-allow-any-audience</c>
	/// (or the matching env var). Reuses <see cref="PublicBindGuardOutcome"/> — an identical Ok/Warn/Refuse
	/// shape for a distinct startup precondition.
	/// </summary>
	/// <param name="authorizationEnabled">Whether OAuth Resource-Server authorization is configured.</param>
	/// <param name="audienceCount">Number of configured accepted audiences.</param>
	/// <param name="requiredScopeCount">Number of configured required scopes.</param>
	/// <param name="allowAnyAudience">Whether the operator explicitly opted into the unscoped combination.</param>
	/// <returns>The guard's decision.</returns>
	internal static PublicBindGuardOutcome EvaluateAudienceScopeGuard(
		bool authorizationEnabled, int audienceCount, int requiredScopeCount, bool allowAnyAudience) {
		if (!authorizationEnabled || audienceCount > 0 || requiredScopeCount > 0) {
			return PublicBindGuardOutcome.Ok;
		}
		return allowAnyAudience ? PublicBindGuardOutcome.Warn : PublicBindGuardOutcome.Refuse;
	}

	/// <summary>
	/// ENG-93386 Story 8 (final-review fix): <see langword="true"/> for any host reachable by more than
	/// this machine's own loopback interface. Broader than <see cref="IsWildcardHost"/> (which only
	/// recognizes the literal wildcard spellings and is used for Host-header allow-listing, a distinct
	/// concern): a bind to a concrete LAN/public IP or DNS hostname is just as reachable to a remote
	/// caller as <c>0.0.0.0</c>, so the public-bind guard must treat it the same way. Deliberately reuses
	/// <see cref="TargetUrlValidator.IsLoopbackIpOrLocalhost"/> (parses the FULL 127.0.0.0/8 / <c>::1</c>
	/// loopback range), not the narrower <see cref="IsLoopbackAlias"/> — a second-pass adversarial-review
	/// fix caught that the alias-only check misclassified a legitimate loopback address like
	/// <c>127.0.0.2</c> as "public" and would have refused a harmless local bind.
	/// </summary>
	/// <param name="host">The <c>--host</c> value.</param>
	/// <returns><see langword="true"/> when the bind is not loopback.</returns>
	internal static bool IsPublicBind(string host) => !TargetUrlValidator.IsLoopbackIpOrLocalhost(host);

	/// <summary>
	/// ENG-93386 Story 7 (FR-12/D-6): <see langword="true"/> when both standard OAuth authorization
	/// and at least one legacy platform API key are configured together — a combination that is not
	/// a security problem (OAuth supersedes the key entirely; see <see cref="EnforcePlatformApiKeyGate"/>)
	/// but is worth a loud startup warning so an operator never assumes the key is still enforced.
	/// </summary>
	/// <param name="authorizationEnabled">Whether OAuth Resource-Server authorization is configured.</param>
	/// <param name="platformApiKeyCount">Number of configured platform API keys.</param>
	/// <returns><see langword="true"/> when the configured key is now inert and should be flagged.</returns>
	internal static bool ShouldWarnPlatformApiKeyIgnored(bool authorizationEnabled, int platformApiKeyCount) =>
		authorizationEnabled && platformApiKeyCount > 0;

	// ENG-93386 Story 8 (final-review fix): delegates the actual truthy-parsing rule to
	// AuthConfiguration.IsTruthy so every mcp-http boolean env-var override (this one included) trims
	// whitespace the same way — a review found this copy previously did not trim, unlike the other.
	private static bool IsTruthyEnvironmentFlag(string variableName) =>
		AuthConfiguration.IsTruthy(Environment.GetEnvironmentVariable(variableName));
}
