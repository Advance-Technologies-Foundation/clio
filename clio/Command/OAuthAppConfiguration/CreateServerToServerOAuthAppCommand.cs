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
/// Creates a server-to-server (<c>client_credentials</c>) OAuth app in Creatio through the platform
/// <c>OAuthConfigService/AddClient</c> endpoint (KnownRoute 48) over REST, binding it to a system user.
/// The returned client credentials are surfaced ONLY in the structured result; the secret is never
/// logged and is NOT persisted to clio appsettings by this primitive (that is the responsibility of the
/// higher-level <c>deploy-identity</c> flow).
/// </summary>
public class CreateServerToServerOAuthAppCommand : Command<CreateServerToServerOAuthAppOptions>
{
	private const string DefaultSystemUser = "Supervisor";
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
			// The secret is intentionally omitted from the log: only the client id is echoed to the CLI.
			_logger.WriteInfo($"OAuth client id: {result.ClientId}");
			_logger.WriteInfo($"Bound system user id: {result.SystemUserId}");
			_logger.WriteInfo("OAuth client secret: <returned in structured result only>");
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
			ClientName = string.IsNullOrWhiteSpace(options.ClientName) ? "clio s2s" : options.ClientName,
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
