namespace Clio.Command;

using System;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Verb("create-sql-schema", Aliases = ["sql-schema-create"],
	HelpText = "Create a new SQL script schema on a remote Creatio environment")]
public class SqlSchemaCreateOptions : EnvironmentOptions {

	[Option("schema-name", Required = true, HelpText = "New schema name, e.g. 'UsrMySqlScript'")]
	public string SchemaName { get; set; }

	[Option("package-name", Required = true, HelpText = "Target package name that will own the new schema")]
	public string PackageName { get; set; }

	[Option("caption", Required = false, HelpText = "Optional display caption; defaults to schema-name")]
	public string Caption { get; set; }

	[Option("description", Required = false, HelpText = "Optional schema description")]
	public string Description { get; set; }

	[Option("caption-culture", Required = false, HelpText = "Override the culture used for the generated schema caption (e.g. en-US, uk-UA). Precedence: this override > the connected user's profile culture > en-US. Supplying it skips the profile-culture lookup.")]
	public string? CaptionCulture { get; set; }
}

public sealed class SqlSchemaCreateResponse {

	[JsonProperty("success")]
	[System.Text.Json.Serialization.JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonProperty("schemaName")]
	[System.Text.Json.Serialization.JsonPropertyName("schemaName")]
	public string SchemaName { get; set; }

	[JsonProperty("schemaUId")]
	[System.Text.Json.Serialization.JsonPropertyName("schemaUId")]
	public string SchemaUId { get; set; }

	[JsonProperty("packageName")]
	[System.Text.Json.Serialization.JsonPropertyName("packageName")]
	public string PackageName { get; set; }

	[JsonProperty("packageUId")]
	[System.Text.Json.Serialization.JsonPropertyName("packageUId")]
	public string PackageUId { get; set; }

	[JsonProperty("caption")]
	[System.Text.Json.Serialization.JsonPropertyName("caption")]
	public string Caption { get; set; }

	[JsonProperty("error")]
	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }
}

public class SqlSchemaCreateCommand : Command<SqlSchemaCreateOptions> {

	private static readonly SchemaDesignerKind Kind = SchemaDesignerKind.SqlScript;

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;
	private readonly Clio.Command.EntitySchemaDesigner.ICaptionCultureResolver _captionCultureResolver;

	public SqlSchemaCreateCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger,
		Clio.Command.EntitySchemaDesigner.ICaptionCultureResolver captionCultureResolver) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
		_captionCultureResolver = captionCultureResolver;
	}

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
			string caption = string.IsNullOrWhiteSpace(options.Caption) ? options.SchemaName : options.Caption.Trim();
			(JObject schema, string createError) = SchemaDesignerHelper.CreateNewSchema(
				_applicationClient, _serviceUrlBuilder, packageUId, Kind);
			if (createError != null) {
				response = new SqlSchemaCreateResponse { Success = false, Error = createError };
				return false;
			}
			string captionCulture = _captionCultureResolver.Resolve(options, options.CaptionCulture);
			SchemaDesignerHelper.ApplySchemaMetadata(schema, options.SchemaName, caption, options.Description, captionCulture);
			string saveError = SchemaDesignerHelper.SaveSchema(
				_applicationClient, _serviceUrlBuilder, schema, Kind, out bool outcomeUnknown);
			if (saveError != null) {
				if (!outcomeUnknown) {
					response = new SqlSchemaCreateResponse { Success = false, Error = saveError };
					return false;
				}
				response = VerifyUnknownSaveOutcome(options, saveError, packageUId, caption);
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
	/// <param name="caption">Caption applied to the schema.</param>
	/// <returns>The response to surface, successful only when the read-back found the schema.</returns>
	private SqlSchemaCreateResponse VerifyUnknownSaveOutcome(
		SqlSchemaCreateOptions options, string saveError, string packageUId, string caption) {
		SchemaResolveResult readBack = SchemaDesignerHelper.ResolveSchemaUId(
			_applicationClient, _serviceUrlBuilder, options.SchemaName, Kind);
		if (!readBack.IsResolved && !readBack.IsNotFound) {
			return new SqlSchemaCreateResponse {
				Success = false,
				Error = $"{saveError} The result could not be verified either: {readBack.Error} "
					+ $"Check whether schema '{options.SchemaName}' exists before retrying."
			};
		}
		return readBack.IsResolved
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

	public override int Execute(SqlSchemaCreateOptions options) {
		bool success = TryCreate(options, out SqlSchemaCreateResponse response);
		_logger.WriteInfo(JsonConvert.SerializeObject(response));
		return success ? 0 : 1;
	}
}
