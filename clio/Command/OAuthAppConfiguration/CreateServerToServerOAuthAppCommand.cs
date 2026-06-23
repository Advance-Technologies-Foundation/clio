using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command;
using Clio.Command.IdentityServiceDeployment;
using Clio.Common;
using CommandLine;

namespace Clio.Command.OAuthAppConfiguration;

/// <summary>
/// CLI options for creating a server-to-server (<c>client_credentials</c>) OAuth app in Creatio over REST.
/// </summary>
[Verb("create-server-to-server-oauth-app",
	HelpText = "Create a server-to-server (client_credentials) OAuth app in Creatio via OAuthConfigService REST")]
[FeatureToggle("deploy-identity")]
public sealed class CreateServerToServerOAuthAppOptions : RemoteCommandOptions
{
	/// <summary>
	/// Gets or sets the system user identifier the OAuth app is bound to. When empty the app is bound to
	/// the system user resolved from <c>--system-user</c> (defaulting to <c>Supervisor</c>).
	/// </summary>
	[Option("system-user-id", Required = false,
		HelpText = "System user id to bind the OAuth app to. Resolve it first with resolve-oauth-system-user or create-oauth-technical-user")]
	public string SystemUserId { get; set; }

	/// <summary>
	/// Gets or sets the system user name to resolve when <c>--system-user-id</c> is not supplied.
	/// Defaults to <c>Supervisor</c>.
	/// </summary>
	[Option("system-user", Required = false,
		HelpText = "System user name to bind the OAuth app to when --system-user-id is omitted. Defaults to Supervisor")]
	public string SystemUser { get; set; }

	/// <summary>
	/// Gets or sets the OAuth client display name.
	/// </summary>
	[Option("client-name", Required = false, Default = "clio s2s",
		HelpText = "OAuth client display name")]
	public string ClientName { get; set; }

	/// <summary>
	/// Gets or sets the OAuth client application URL.
	/// </summary>
	[Option("client-application-url", Required = false,
		Default = "https://github.com/Advance-Technologies-Foundation/clio.git",
		HelpText = "OAuth client application URL")]
	public string ClientApplicationUrl { get; set; }

	/// <summary>
	/// Gets or sets the OAuth client description.
	/// </summary>
	[Option("client-description", Required = false, Default = "server-to-server integration for clio cli",
		HelpText = "OAuth client description")]
	public string ClientDescription { get; set; }
}

/// <summary>
/// Structured result of creating a server-to-server OAuth app. The <see cref="ClientSecret"/> is returned
/// ONLY in this structured result and is never written to logs.
/// </summary>
/// <param name="ClientId">The created OAuth client identifier.</param>
/// <param name="ClientSecret">The created OAuth client secret. Never logged; surfaced only here.</param>
/// <param name="SystemUserId">The system user the OAuth app is bound to.</param>
/// <param name="GrantType">The OAuth grant type (always <c>client_credentials</c>).</param>
public sealed record CreateServerToServerOAuthAppResult(
	[property: JsonPropertyName("clientId")] string ClientId,
	[property: JsonPropertyName("clientSecret")] string ClientSecret,
	[property: JsonPropertyName("systemUserId")] string SystemUserId,
	[property: JsonPropertyName("grantType")] string GrantType);

/// <summary>
/// CLI one-time reveal of the created OAuth credentials. The client secret is generated once by Creatio
/// and cannot be retrieved later, so the CLI must hand it to the user who explicitly ran the command.
/// </summary>
/// <param name="ClientId">The created OAuth client identifier.</param>
/// <param name="ClientSecret">The created OAuth client secret. Emitted to STDOUT only; never logged.</param>
/// <param name="SystemUserId">The system user the OAuth app is bound to.</param>
/// <param name="Name">The OAuth client display name.</param>
public sealed record CreateServerToServerOAuthAppCredentials(
	[property: JsonPropertyName("clientId")] string ClientId,
	[property: JsonPropertyName("clientSecret")] string ClientSecret,
	[property: JsonPropertyName("systemUserId")] string SystemUserId,
	[property: JsonPropertyName("name")] string Name);

/// <summary>
/// Creates a server-to-server (<c>client_credentials</c>) OAuth app in Creatio through the platform
/// <c>OAuthConfigService/AddClient</c> endpoint (KnownRoute 48) over REST, binding it to a system user.
/// The returned client credentials are surfaced ONLY in the structured result; the secret is never
/// logged and is NOT persisted to clio appsettings by this primitive (that is the responsibility of the
/// higher-level <c>deploy-identity</c> flow).
/// </summary>
public class CreateServerToServerOAuthAppCommand : Command<CreateServerToServerOAuthAppOptions>
{
	private const string DefaultSystemUser = "Supervisor";
	private const string DefaultClientName = "clio s2s";
	private const string ClientCredentialsGrantType = "client_credentials";

	private static readonly JsonSerializerOptions WriteIndentedOptions = new() {
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly IIdentityServiceCreatioClient _creatioClient;
	private readonly ResolveOAuthSystemUserCommand _systemUserResolver;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="CreateServerToServerOAuthAppCommand"/> class.
	/// </summary>
	public CreateServerToServerOAuthAppCommand(
		IIdentityServiceCreatioClient creatioClient,
		ResolveOAuthSystemUserCommand systemUserResolver,
		ILogger logger) {
		_creatioClient = creatioClient;
		_systemUserResolver = systemUserResolver;
		_logger = logger;
	}

	/// <inheritdoc />
	public override int Execute(CreateServerToServerOAuthAppOptions options) {
		try {
			CreateServerToServerOAuthAppResult result = CreateApp(options);

			// One-time secret reveal. Creatio generates the client secret once and never returns it again,
			// so the CLI MUST hand it to the user who ran the command. The logger (ILogger.WriteInfo) is NOT
			// used for the secret: every ConsoleLogger method also persists to the rotating log file and any
			// additional sinks. Instead the credentials are written as a pure-JSON object directly to STDOUT
			// (bypassing the logger's file sinks) so the output stays both secret-safe and machine-parseable
			// (e.g. `clio create-server-to-server-oauth-app -e env | jq`). The capture warning goes to STDERR
			// to keep STDOUT a clean JSON document. The MCP tool path does NOT use Execute() — it calls
			// CreateApp() and returns the secret in its structured record — so this console write is CLI-only.
			CreateServerToServerOAuthAppCredentials credentials = new(
				result.ClientId,
				result.ClientSecret,
				result.SystemUserId,
				ResolveClientName(options));
			// CLIO002: direct Console use is intentional and must NOT be routed through ILogger here. Every
			// ConsoleLogger method also persists to the rotating log file / additional sinks, which would
			// leak the one-time secret to disk. Writing the secret straight to STDOUT (and the warning to
			// STDERR) keeps it off every log sink and keeps STDOUT a clean JSON document. Mirrors the
			// machine-readable console-output pattern in ShowAppListCommand.
#pragma warning disable CLIO002
			Console.Error.WriteLine(
				"WARNING: the client secret below is shown ONCE and cannot be retrieved later. Capture it now.");
			Console.Out.WriteLine(JsonSerializer.Serialize(credentials, WriteIndentedOptions));
#pragma warning restore CLIO002
			return 0;
		}
		catch (Exception exception) {
			_logger.WriteError(exception.Message);
			return 1;
		}
	}

	/// <summary>
	/// Creates the server-to-server OAuth app and returns its credentials. The system user is resolved by
	/// id when supplied, otherwise by name (defaulting to <c>Supervisor</c>).
	/// </summary>
	/// <param name="options">Creation criteria and environment settings.</param>
	/// <returns>The structured creation result, including the client secret.</returns>
	public virtual CreateServerToServerOAuthAppResult CreateApp(CreateServerToServerOAuthAppOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		string systemUserId = ResolveSystemUserId(options);

		DeployIdentityOptions clientOptions = new() {
			ClientName = ResolveClientName(options),
			ClientApplicationUrl = string.IsNullOrWhiteSpace(options.ClientApplicationUrl)
				? "https://github.com/Advance-Technologies-Foundation/clio.git"
				: options.ClientApplicationUrl,
			ClientDescription = string.IsNullOrWhiteSpace(options.ClientDescription)
				? "server-to-server integration for clio cli"
				: options.ClientDescription
		};

		OAuthClientCredentials credentials = _creatioClient.CreateClioClient(clientOptions, systemUserId);
		return new CreateServerToServerOAuthAppResult(
			credentials.ClientId,
			credentials.ClientSecret,
			systemUserId,
			ClientCredentialsGrantType);
	}

	private static string ResolveClientName(CreateServerToServerOAuthAppOptions options) =>
		string.IsNullOrWhiteSpace(options.ClientName) ? DefaultClientName : options.ClientName;

	private string ResolveSystemUserId(CreateServerToServerOAuthAppOptions options) {
		if (!string.IsNullOrWhiteSpace(options.SystemUserId)) {
			return options.SystemUserId.Trim();
		}
		string userName = string.IsNullOrWhiteSpace(options.SystemUser) ? DefaultSystemUser : options.SystemUser.Trim();
		ResolveOAuthSystemUserResult resolved = _systemUserResolver.ResolveSystemUser(
			new ResolveOAuthSystemUserOptions {
				Environment = options.Environment,
				Uri = options.Uri,
				Login = options.Login,
				Password = options.Password,
				Name = userName
			});
		if (!resolved.Found || string.IsNullOrWhiteSpace(resolved.SystemUserId)) {
			throw new InvalidOperationException(
				$"System user '{userName}' was not found. Pass --system-user-id explicitly or create one with create-oauth-technical-user.");
		}
		return resolved.SystemUserId;
	}
}
