using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
			.WithHttpTransport();

		// Per-request credential-passthrough seam (Story 4). Registered in the HTTP host,
		// NOT the shared BindingsModule, so IHttpContextAccessor is not pulled into the
		// stdio graph. FLAG FOR STORY 7: when the credential resolver consumes
		// ICredentialContextAccessor it must resolve in BOTH hosts — either move these
		// registrations (plus AddHttpContextAccessor) into BindingsModule, or make the
		// stdio path tolerate a null accessor.
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

		AspNetWebApplication app = builder.Build();

		// DNS-rebinding / cross-origin protection. The MCP spec makes Origin/Host validation the
		// host's responsibility (ModelContextProtocol.AspNetCore does not do it automatically), and
		// every registered tool acts with the operator's stored credentials — so loopback binding is
		// not a mitigation on its own. UseHostFiltering rejects unexpected Host headers; the Origin
		// check rejects browser requests from non-allowlisted origins. Native MCP clients send no
		// Origin header and pass through unaffected.
		app.UseHostFiltering();
		app.Use((context, next) => ValidateOrigin(context, next, options.Host));

		// Edge API-key gate (Story 5). Runs BEFORE the credential-capture middleware so a
		// credential header is treated as trusted only after this gate authorizes the request.
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
		if (IsLoopbackHost(boundHost)) {
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
	// carrying the credential header must present a matching 'Authorization: Bearer <key>';
	// a missing/mismatched key short-circuits with HTTP 401 and no secret (AC-03/AC-ERR).
	// The decision is published to the pipeline via PassthroughEnabledItemKey, which the
	// credential-capture middleware honors.
	private static async Task EnforcePlatformApiKeyGate(
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

		if (!context.Request.Headers.ContainsKey(credentialHeaderName)) {
			// Passthrough-capable server, but this request is not using passthrough
			// (e.g. pre-registered -e). Still exactly 8.1.0.72 behavior.
			context.Items[PassthroughEnabledItemKey] = false;
			await next(context);
			return;
		}

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

		context.Items[PassthroughEnabledItemKey] = true;
		await next(context);
	}

	// Reads the configured credential header, parses it into a per-request CredentialContext,
	// and publishes it via ICredentialContextAccessor. Only acts when the earlier API-key gate
	// enabled passthrough for this request (PassthroughEnabledItemKey == true). When the item is
	// false/absent the credential header is ignored entirely — no parse, no 400 — so an
	// untrusted/no-key request behaves exactly as 8.1.0.72 (AC-02). Parse failure inside the
	// trusted path ⇒ HTTP 400 with a JSON body naming the defect (no secret).
	private static async Task CaptureCredentialContext(
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
		if (IsLoopbackHost(originHost)) {
			return true;
		}

		return !IsWildcardHost(boundHost)
			&& string.Equals(originHost, boundHost, StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsLoopbackHost(string host) =>
		LoopbackHostAliases.Contains(host, StringComparer.OrdinalIgnoreCase);

	private static bool IsWildcardHost(string host) =>
		host is "0.0.0.0" or "*" or "::" or "[::]";

	private static bool IsTruthyEnvironmentFlag(string variableName) {
		string value = Environment.GetEnvironmentVariable(variableName);
		return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";
	}
}
