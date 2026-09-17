namespace Clio.Command;

using System;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>Options for the package SQL create command.</summary>
[Verb("create-sql-schema", Aliases = ["sql-schema-create"],
	HelpText = "Create a new SQL script schema on a remote Creatio environment")]
public class SqlSchemaCreateOptions : EnvironmentOptions {

	/// <summary>Unique name of the package SQL script.</summary>
	[Option("schema-name", Required = true, HelpText = "New schema name, e.g. 'UsrMySqlScript'")]
	public string SchemaName { get; set; }

	/// <summary>Name of the owning package.</summary>
	[Option("package-name", Required = true, HelpText = "Target package name that will own the new schema")]
	public string PackageName { get; set; }

	/// <summary>Optional native database engine: 0 MSSql, 1 Oracle, 2 PostgreSql; otherwise detect it.</summary>
	[Option("db-engine-type", Required = false, HelpText = "Database engine: 0 MSSql, 1 Oracle, 2 PostgreSql. Defaults to the target engine.")]
	public int? DbEngineType { get; set; }

	/// <summary>Package installation phase, defaulting to after package.</summary>
	[Option("install-type", Required = false, Default = 1, HelpText = "Installation phase: 0 before package, 1 after package, 2 after schema data, 3 uninstall app.")]
	public int InstallType { get; set; } = 1;

	/// <summary>Legacy display-name field; SQL scripts have no localized caption.</summary>
	[Option("caption", Required = false, HelpText = "Legacy option: nonempty values are rejected; SQL scripts use schema-name.")]
	public string Caption { get; set; }

	/// <summary>Legacy description option, rejected when nonempty.</summary>
	[Option("description", Required = false, HelpText = "Legacy option: nonempty values are rejected; SQL scripts have no description.")]
	public string Description { get; set; }

	/// <summary>Legacy caption culture option, rejected when nonempty.</summary>
	[Option("caption-culture", Required = false, HelpText = "Legacy option: nonempty values are rejected; SQL scripts have no caption culture.")]
	public string? CaptionCulture { get; set; }
}

/// <summary>Result of the package SQL create operation.</summary>
public sealed class SqlSchemaCreateResponse {

	/// <summary>Whether the operation completed successfully.</summary>
	[JsonProperty("success")]
	[System.Text.Json.Serialization.JsonPropertyName("success")]
	public bool Success { get; set; }

	/// <summary>Unique name of the package SQL script.</summary>
	[JsonProperty("schemaName")]
	[System.Text.Json.Serialization.JsonPropertyName("schemaName")]
	public string SchemaName { get; set; }

	/// <summary>Stable identity of the package SQL script.</summary>
	[JsonProperty("schemaUId")]
	[System.Text.Json.Serialization.JsonPropertyName("schemaUId")]
	public string SchemaUId { get; set; }

	/// <summary>Name of the owning package.</summary>
	[JsonProperty("packageName")]
	[System.Text.Json.Serialization.JsonPropertyName("packageName")]
	public string PackageName { get; set; }

	/// <summary>Stable identity of the owning package.</summary>
	[JsonProperty("packageUId")]
	[System.Text.Json.Serialization.JsonPropertyName("packageUId")]
	public string PackageUId { get; set; }

	/// <summary>Legacy display-name field; SQL scripts have no localized caption.</summary>
	[JsonProperty("caption")]
	[System.Text.Json.Serialization.JsonPropertyName("caption")]
	public string Caption { get; set; }

	/// <summary>Failure diagnostic, or null on success.</summary>
	[JsonProperty("error")]
	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }
}

/// <summary>Runs the native package SQL create operation.</summary>
public class SqlSchemaCreateCommand : Command<SqlSchemaCreateOptions> {

	private static readonly SchemaDesignerKind Kind = SchemaDesignerKind.SqlScript;

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	/// <summary>Initializes the command with its environment-scoped dependencies.</summary>
	public SqlSchemaCreateCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	/// <summary>Validates input and performs the native package SQL create operation.</summary>
	public virtual bool TryCreate(SqlSchemaCreateOptions options, out SqlSchemaCreateResponse response) {
		try {
			if (options is null) {
				response = new SqlSchemaCreateResponse { Success = false, Error = "options is required" };
				return false;
			}
			string validationError = SchemaDesignerHelper.ValidateCreateInput(options.SchemaName, options.PackageName);
			if (validationError != null) {
				response = new SqlSchemaCreateResponse { Success = false, Error = validationError };
				return false;
			}
			if (!string.IsNullOrWhiteSpace(options.Caption) || !string.IsNullOrWhiteSpace(options.Description)
				|| !string.IsNullOrWhiteSpace(options.CaptionCulture)) {
				response = new SqlSchemaCreateResponse { Success = false,
					Error = "Package SQL scripts have no caption, description or caption culture. Omit these legacy options; schema-name is the display name." };
				return false;
			}
			if (options.DbEngineType is < 0 or > 2 || options.InstallType is < 0 or > 3) {
				response = new SqlSchemaCreateResponse { Success = false, Error = "db-engine-type must be 0..2 and install-type must be 0..3." };
				return false;
			}
			(string packageUId, string packageError) = PageSchemaMetadataHelper.QueryPackageUId(
				_applicationClient, _serviceUrlBuilder, options.PackageName);
			if (packageError != null) {
				response = new SqlSchemaCreateResponse { Success = false, Error = packageError };
				return false;
			}
			SqlSchemaCreateResponse duplicateFailure = CheckSchemaIsAbsent(options.SchemaName);
			if (duplicateFailure != null) {
				response = duplicateFailure;
				return false;
			}
			int? engine = options.DbEngineType;
			if (engine is null) {
				string infoUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetSystemEnvironmentInfo);
				string infoJson = _applicationClient.ExecutePostRequest(infoUrl, "{}");
				(JObject info, string infoError) = SchemaDesignerHelper.ParseServiceResponse("GetSystemEnvironmentInfo", infoUrl, infoJson);
				engine = info?["success"]?.Value<bool>() == true ? info["dbEngineType"]?.ToString() switch {
					"MSSql" => 0, "Oracle" => 1, "PostgreSql" => 2, _ => null
				} : null;
				if (engine is null) {
					response = new SqlSchemaCreateResponse { Success = false,
						Error = $"Could not detect the database engine. Supply db-engine-type explicitly. {infoError ?? (info?["errorInfo"] as JObject)?["message"]?.ToString()}" };
					return false;
				}
			}
			string caption = options.SchemaName;
			JObject schema = new() {
				["uId"] = Guid.NewGuid().ToString(), ["name"] = options.SchemaName,
				["package"] = new JObject { ["uId"] = packageUId, ["name"] = options.PackageName },
				["body"] = " ", ["dbEngineType"] = engine.Value, ["installType"] = options.InstallType,
				["dependOnSqlScripts"] = new JArray(), ["backwardCompatibilityConfirmed"] = false
			};
			string saveError = SchemaDesignerHelper.SaveSchema(
				_applicationClient, _serviceUrlBuilder, schema, Kind, out bool outcomeUnknown);
			if (saveError != null) {
				if (!outcomeUnknown) {
					response = new SqlSchemaCreateResponse { Success = false, Error = saveError };
					return false;
				}
				response = VerifyUnknownSaveOutcome(options, saveError, packageUId, caption, schema["uId"]!.ToString());
				return response.Success;
			}
			response = BuildSuccess(options, schema["uId"]?.ToString(), packageUId, caption);
			return true;
		}
		catch (Exception ex) {
			response = new SqlSchemaCreateResponse { Success = false, Error = ex.Message };
			return false;
		}
	}

	/// <summary>
	/// Checks that the target schema name is free, returning the failure response when it is taken or when
	/// the check could not be answered.
	/// </summary>
	/// <remarks>
	/// Branches on the discriminated resolve outcome, not on the error text: only an answered "there is no
	/// such schema" licenses the create. Anything unanswerable (a transport failure, a DataService failure
	/// envelope, a row with no UId) aborts, or a create runs over a schema that may already be there.
	/// </remarks>
	/// <param name="schemaName">Schema name the create would take.</param>
	/// <returns>The failure response, or <see langword="null"/> when the name is free.</returns>
	private SqlSchemaCreateResponse CheckSchemaIsAbsent(string schemaName) {
		SchemaResolveResult existing = SchemaDesignerHelper.ResolveSchemaUId(
			_applicationClient, _serviceUrlBuilder, schemaName, Kind);
		if (existing.IsResolved) {
			return new SqlSchemaCreateResponse {
				Success = false,
				Error = $"Schema '{schemaName}' already exists in this environment."
			};
		}
		if (existing.IsNotFound) {
			return null;
		}
		return new SqlSchemaCreateResponse {
			Success = false,
			Error = $"Could not check whether schema '{schemaName}' already exists: {existing.Error}"
		};
	}

	/// <summary>
	/// Reads the schema back after a save whose answer was unusable, so the command reports what the
	/// environment actually holds instead of a failure it never observed.
	/// </summary>
	/// <remarks>
	/// Reporting a failure for a schema that WAS created leaves the caller retrying a create that can only
	/// fail as "already exists"; reporting success for one that was not created is worse still. When the
	/// read-back itself cannot be answered, the outcome is reported as unverified.
	/// </remarks>
	/// <param name="options">The create request being reported on.</param>
	/// <param name="saveError">The classified save failure whose outcome is unknown.</param>
	/// <param name="packageUId">UId of the package that would own the schema.</param>
	/// <param name="caption">Legacy display name returned to the caller.</param>
	/// <param name="createdUId">Identity generated for this create attempt.</param>
	/// <returns>The response to surface, successful only when the read-back found the schema.</returns>
	private SqlSchemaCreateResponse VerifyUnknownSaveOutcome(
		SqlSchemaCreateOptions options, string saveError, string packageUId, string caption, string createdUId) {
		SchemaResolveResult readBack;
		try {
			readBack = SchemaDesignerHelper.ResolveSchemaUId(
				_applicationClient, _serviceUrlBuilder, options.SchemaName, Kind);
		} catch (Exception ex) when (SchemaDesignerHelper.IsTransportFailure(ex)) {
			readBack = SchemaResolveResult.Unanswerable("Readback transport failed.");
		}
		if (!readBack.IsResolved && !readBack.IsNotFound) {
			return new SqlSchemaCreateResponse {
				Success = false,
				Error = $"{saveError} The result could not be verified either: {readBack.Error} "
					+ $"Check whether schema '{options.SchemaName}' exists before retrying."
			};
		}
		return readBack.IsResolved && Guid.TryParse(readBack.UId, out Guid actualUId) && actualUId == Guid.Parse(createdUId)
			? BuildSuccess(options, readBack.UId, packageUId, caption)
			: new SqlSchemaCreateResponse { Success = false, Error = saveError };
	}

	private static SqlSchemaCreateResponse BuildSuccess(
		SqlSchemaCreateOptions options, string schemaUId, string packageUId, string caption) =>
		new() {
			Success = true,
			SchemaName = options.SchemaName,
			SchemaUId = schemaUId,
			PackageName = options.PackageName,
			PackageUId = packageUId,
			Caption = caption
		};

	/// <inheritdoc />
	public override int Execute(SqlSchemaCreateOptions options) {
		bool success = TryCreate(options, out SqlSchemaCreateResponse response);
		_logger.WriteInfo(JsonConvert.SerializeObject(response));
		return success ? 0 : 1;
	}
}
