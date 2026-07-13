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
		HelpText = "Comma-separated platform API key(s). When at least one is set (here or via the "
			+ "CLIO_MCP_HTTP_PLATFORM_API_KEY environment variable), per-request credential "
			+ "passthrough is enabled and requests must present a matching 'Authorization: Bearer <key>'.")]
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

		// Per-request credential-passthrough leg (Story 5/4). Always wired: it is gated SOLELY by the
		// platform API-key gate, which fail-closes when no key is configured. With no key set (the
		// default) the gate publishes PassthroughEnabledItemKey=false, the credential-capture middleware
		// ignores the credential header, and the verb / stdio / -e <env> behave exactly as 8.1.0.72 — so
		// wiring the middleware unconditionally does NOT expose passthrough by default. A key is what
		// turns it on. (The former incubation feature flag was removed: mcp-http is not yet used in prod,
		// so the second gate was redundant given the fail-closed api-key gate.)
		//
		// The edge API-key gate runs BEFORE the credential-capture middleware so a credential header is
		// treated as trusted only after this gate authorizes the request.
		IPlatformApiKeyGate platformApiKeyGate =
			app.Services.GetRequiredService<IPlatformApiKeyGate>();
		app.Use((context, next) => EnforcePlatformApiKeyGate(
			context, next, platformApiKeyGate, options.CredentialsHeaderName));

		ICredentialHeaderParser credentialHeaderParser =
			app.Services.GetRequiredService<ICredentialHeaderParser>();
		ICredentialContextAccessor credentialContextAccessor =
			app.Services.GetRequiredService<ICredentialContextAccessor>();
		app.Use((context, next) => CaptureCredentialContext(
			context, next, credentialHeaderParser, credentialContextAccessor,
			options.CredentialsHeaderName));

		app.MapMcp(options.Path);

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
	internal static async Task EnforcePlatformApiKeyGate(
		HttpContext context,
		RequestDelegate next,
		IPlatformApiKeyGate gate,
		string credentialHeaderName) {
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
	internal static async Task CaptureCredentialContext(
		HttpContext context,
		RequestDelegate next,
		ICredentialHeaderParser parser,
		ICredentialContextAccessor accessor,
		string headerName) {
		bool passthroughEnabled =
			context.Items.TryGetValue(PassthroughEnabledItemKey, out object flag)
			&& flag is true;

		if (!passthroughEnabled) {
			// Not a trusted passthrough request — ignore the credential header (AC-02).
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

	private static bool IsTruthyEnvironmentFlag(string variableName) {
		string value = Environment.GetEnvironmentVariable(variableName);
		return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";
	}
}
