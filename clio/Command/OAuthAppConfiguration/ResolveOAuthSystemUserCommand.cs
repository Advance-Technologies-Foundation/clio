using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command;
using Clio.Common;
using CommandLine;
using static Clio.Package.SelectQueryHelper;

namespace Clio.Command.OAuthAppConfiguration;

/// <summary>
/// CLI options for resolving a Creatio system user (<c>SysAdminUnit</c>) over DataService REST, used to
/// pick the <c>systemUserId</c> a server-to-server OAuth app will be bound to.
/// </summary>
[Verb("resolve-oauth-system-user",
	HelpText = "Resolve a Creatio system user (SysAdminUnit) by name or id over DataService REST")]
[FeatureToggle("deploy-identity")]
public sealed class ResolveOAuthSystemUserOptions : RemoteCommandOptions
{
	/// <summary>
	/// Gets or sets the system user name to resolve. Defaults to <c>Supervisor</c> when neither
	/// <c>--name</c> nor <c>--id</c> is supplied.
	/// </summary>
	[Option("name", Required = false,
		HelpText = "System user (SysAdminUnit) name to resolve. Defaults to Supervisor")]
	public string Name { get; set; }

	/// <summary>
	/// Gets or sets the system user identifier to resolve. When supplied, it takes precedence over
	/// <c>--name</c>.
	/// </summary>
	[Option("id", Required = false,
		HelpText = "System user (SysAdminUnit) id to resolve. Takes precedence over --name when supplied")]
	public string Id { get; set; }
}

/// <summary>
/// Structured result of resolving a Creatio system user.
/// </summary>
/// <param name="SystemUserId">Resolved system user identifier, or null when not found.</param>
/// <param name="Name">Resolved system user name, or null when not found.</param>
/// <param name="Found">Whether a matching system user was found.</param>
public sealed record ResolveOAuthSystemUserResult(
	[property: JsonPropertyName("systemUserId")] string SystemUserId,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("found")] bool Found);

/// <summary>
/// Resolves a Creatio system user (<c>SysAdminUnit</c>) by name or id using a DataService SelectQuery
/// issued through <see cref="IApplicationClient"/>. This is the REST-only replacement for the DB-direct
/// resolver used by <c>deploy-identity</c>, so it works against remote Creatio instances where clio has
/// no filesystem or database access.
/// </summary>
public class ResolveOAuthSystemUserCommand : Command<ResolveOAuthSystemUserOptions>
{
	private const string DefaultSystemUser = "Supervisor";

	private static readonly IReadOnlyList<SelectQueryColumnDefinition> UserColumns =
	[
		new("Id", "Id"),
		new("Name", "Name")
	];

	private static readonly JsonSerializerOptions WriteIndentedOptions = new() {
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="ResolveOAuthSystemUserCommand"/> class.
	/// </summary>
	public ResolveOAuthSystemUserCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	/// <inheritdoc />
	public override int Execute(ResolveOAuthSystemUserOptions options) {
		try {
			ResolveOAuthSystemUserResult result = ResolveSystemUser(options);
			_logger.WriteInfo(JsonSerializer.Serialize(result, WriteIndentedOptions));
			return result.Found ? 0 : 1;
		}
		catch (Exception exception) {
			_logger.WriteError(exception.Message);
			return 1;
		}
	}

	/// <summary>
	/// Resolves a Creatio system user by id (preferred when supplied) or by name (defaulting to
	/// <c>Supervisor</c>) over DataService REST.
	/// </summary>
	/// <param name="options">Resolution criteria and environment settings.</param>
	/// <returns>The structured resolution result.</returns>
	public virtual ResolveOAuthSystemUserResult ResolveSystemUser(ResolveOAuthSystemUserOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		string id = string.IsNullOrWhiteSpace(options.Id) ? null : options.Id.Trim();
		string name = string.IsNullOrWhiteSpace(options.Name) ? null : options.Name.Trim();

		SelectQueryFilterDefinition filter = id is not null
			? new SelectQueryFilterDefinition("Id", id, GuidDataValueType)
			: new SelectQueryFilterDefinition("Name", name ?? DefaultSystemUser, TextDataValueType);

		SysAdminUnitResponse response = ExecuteSelectQuery<SysAdminUnitResponse>(
			_applicationClient,
			_serviceUrlBuilder,
			BuildSelectQuery("SysAdminUnit", UserColumns, [filter], rowCount: 1));

		SysAdminUnitRowDto row = response.Rows.FirstOrDefault();
		return row is null
			? new ResolveOAuthSystemUserResult(null, null, false)
			: new ResolveOAuthSystemUserResult(row.Id ?? string.Empty, row.Name ?? string.Empty, true);
	}

	private sealed class SysAdminUnitResponse : SelectQueryResponseBaseDto
	{
		[JsonPropertyName("rows")] public List<SysAdminUnitRowDto> Rows { get; set; } = [];
	}

	private sealed class SysAdminUnitRowDto
	{
		[JsonPropertyName("Id")] public string Id { get; set; }
		[JsonPropertyName("Name")] public string Name { get; set; }
	}
}
