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

		AspNetWebApplication app = builder.Build();

		// DNS-rebinding / cross-origin protection. The MCP spec makes Origin/Host validation the
		// host's responsibility (ModelContextProtocol.AspNetCore does not do it automatically), and
		// every registered tool acts with the operator's stored credentials — so loopback binding is
		// not a mitigation on its own. UseHostFiltering rejects unexpected Host headers; the Origin
		// check rejects browser requests from non-allowlisted origins. Native MCP clients send no
		// Origin header and pass through unaffected.
		app.UseHostFiltering();
		app.Use((context, next) => ValidateOrigin(context, next, options.Host));

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

	// Reads the configured credential header, parses it into a per-request CredentialContext,
	// and publishes it via ICredentialContextAccessor. Absent header ⇒ no context (stdio /
	// no-header path). Parse failure ⇒ HTTP 400 with a JSON body naming the defect (no secret).
	private static async Task CaptureCredentialContext(
		HttpContext context,
		RequestDelegate next,
		ICredentialHeaderParser parser,
		ICredentialContextAccessor accessor,
		string headerName) {
		if (!context.Request.Headers.TryGetValue(headerName, out var headerValues)) {
			// No credential header — nothing to capture; context stays null (AC-05).
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

		// PassthroughModeEnabled is forward-compatible: the authoritative gate is Story 5
		// (FR-09), which sets PassthroughEnabledItemKey in an earlier middleware. This
		// middleware only carries the flag into the context; until Story 5 ships it is false.
		bool passthroughEnabled =
			context.Items.TryGetValue(PassthroughEnabledItemKey, out object flag)
			&& flag is true;

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
