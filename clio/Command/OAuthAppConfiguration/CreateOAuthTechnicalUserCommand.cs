using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command;
using Clio.Command.IdentityServiceDeployment;
using Clio.Common;
using CommandLine;

namespace Clio.Command.OAuthAppConfiguration;

/// <summary>
/// CLI options for creating a Creatio technical user for a server-to-server OAuth app over REST.
/// </summary>
[Verb("create-oauth-technical-user",
	HelpText = "Create a Creatio technical user for a server-to-server OAuth app via OAuthConfigService REST")]
[FeatureToggle("deploy-identity")]
public sealed class CreateOAuthTechnicalUserOptions : RemoteCommandOptions
{
	/// <summary>
	/// Gets or sets the technical user name to create. Defaults to <c>clio_oauth_technical_user</c>.
	/// </summary>
	[Option("name", Required = false,
		HelpText = "Technical user name to create. Defaults to clio_oauth_technical_user")]
	public string Name { get; set; }
}

/// <summary>
/// Structured result of creating a Creatio technical user.
/// </summary>
/// <param name="SystemUserId">Identifier of the created technical user.</param>
/// <param name="Name">Name of the created technical user.</param>
/// <param name="RoleGranted">
/// Whether a Creatio role was granted to the new user. Always <see langword="false"/> for the remote
/// server-to-server path — see <c>RoleGrantDeferredNotice</c>.
/// </param>
/// <param name="RoleGrantNotice">Human-readable explanation of the deferred role grant.</param>
public sealed record CreateOAuthTechnicalUserResult(
	[property: JsonPropertyName("systemUserId")] string SystemUserId,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("roleGranted")] bool RoleGranted,
	[property: JsonPropertyName("roleGrantNotice")] string RoleGrantNotice);

/// <summary>
/// Creates a Creatio technical user through the platform <c>OAuthConfigService/CreateTechnicalUser</c>
/// endpoint (KnownRoute 47) over REST, for binding a subsequent server-to-server OAuth app.
/// </summary>
/// <remarks>
/// DEFERRED ROLE GRANT (server-to-server): the existing <c>deploy-identity</c> flow grants the
/// "System administrators" role to a freshly created technical user via a DIRECT database write
/// (Npgsql/SqlClient against <c>SysUserInRole</c> / <c>SysAdminUnitInRole</c>; see
/// <see cref="IdentityServiceRoleGrantService"/>). That path is impossible against a remote Creatio
/// where clio has no database access, and no clean REST endpoint for granting a role to a user is
/// available today (OAuthConfigService exposes only user/client creation; cliogate exposes no
/// role-grant endpoint). This command therefore creates the user WITHOUT granting any role and reports
/// the omission explicitly. Whether a role grant is even required for the server-to-server flow — or
/// whether <c>OAuthConfigService/CreateTechnicalUser</c> already provisions the necessary roles
/// server-side — is an OPEN QUESTION for the reviewer; do not assume either way. If a role is required,
/// grant it manually in Creatio or run the local <c>deploy-identity --create-tech-user</c> path against
/// an environment with database access.
/// </remarks>
public class CreateOAuthTechnicalUserCommand : Command<CreateOAuthTechnicalUserOptions>
{
	private const string DefaultTechnicalUserName = "clio_oauth_technical_user";

	internal const string RoleGrantDeferredNotice =
		"Role grant deferred: this REST-only flow does not assign a Creatio role to the new technical user. "
		+ "The deploy-identity role grant is database-direct and cannot run against a remote environment. "
		+ "If the server-to-server app requires elevated permissions, grant the role manually in Creatio "
		+ "or use deploy-identity --create-tech-user against an environment with database access.";

	private static readonly JsonSerializerOptions WriteIndentedOptions = new() {
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly IIdentityServiceCreatioClient _creatioClient;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="CreateOAuthTechnicalUserCommand"/> class.
	/// </summary>
	public CreateOAuthTechnicalUserCommand(IIdentityServiceCreatioClient creatioClient, ILogger logger) {
		_creatioClient = creatioClient;
		_logger = logger;
	}

	/// <inheritdoc />
	public override int Execute(CreateOAuthTechnicalUserOptions options) {
		try {
			CreateOAuthTechnicalUserResult result = CreateTechnicalUser(options);
			_logger.WriteInfo(JsonSerializer.Serialize(result, WriteIndentedOptions));
			_logger.WriteWarning(RoleGrantDeferredNotice);
			return 0;
		}
		catch (Exception exception) {
			_logger.WriteError(exception.Message);
			return 1;
		}
	}

	/// <summary>
	/// Creates a Creatio technical user over REST and returns its identifier. No Creatio role is granted —
	/// see the remarks on <see cref="CreateOAuthTechnicalUserCommand"/>.
	/// </summary>
	/// <param name="options">Creation criteria and environment settings.</param>
	/// <returns>The structured creation result.</returns>
	public virtual CreateOAuthTechnicalUserResult CreateTechnicalUser(CreateOAuthTechnicalUserOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		string userName = string.IsNullOrWhiteSpace(options.Name) ? DefaultTechnicalUserName : options.Name.Trim();
		string systemUserId = _creatioClient.CreateTechnicalUser(userName);
		return new CreateOAuthTechnicalUserResult(systemUserId, userName, false, RoleGrantDeferredNotice);
	}
}
