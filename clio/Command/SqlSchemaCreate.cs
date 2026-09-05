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
			// Resolve directly instead of through SchemaNameExists: that helper discards the resolve error,
			// so a SelectQuery that failed for a transport reason would read as "the schema does not exist"
			// and the command would go on to create a schema that may already be there.
			(string existingUId, string existsError) = SchemaDesignerHelper.ResolveSchemaUId(
				_applicationClient, _serviceUrlBuilder, options.SchemaName, Kind);
			if (existingUId != null) {
				response = new SqlSchemaCreateResponse {
					Success = false,
					Error = $"Schema '{options.SchemaName}' already exists in this environment."
				};
				return false;
			}
			if (existsError != null && !SchemaDesignerHelper.IsSchemaNotFound(existsError)) {
				response = new SqlSchemaCreateResponse {
					Success = false,
					Error = $"Could not check whether schema '{options.SchemaName}' already exists: {existsError}"
				};
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
				// The save answer was unusable, so the write was neither observed to succeed nor to fail.
				// Read the schema back before claiming either: reporting a failure for a schema that WAS
				// created leaves the caller retrying a create that can only fail as "already exists".
				(string createdUId, string resolveError) = SchemaDesignerHelper.ResolveSchemaUId(
					_applicationClient, _serviceUrlBuilder, options.SchemaName, Kind);
				if (resolveError != null && !SchemaDesignerHelper.IsSchemaNotFound(resolveError)) {
					// The verification itself failed, so the outcome stays unknown - say so rather than
					// turning a failed read-back into a claim about the schema.
					response = new SqlSchemaCreateResponse {
						Success = false,
						Error = $"{saveError} The result could not be verified either: {resolveError} "
							+ $"Check whether schema '{options.SchemaName}' exists before retrying."
					};
					return false;
				}
				if (createdUId == null) {
					response = new SqlSchemaCreateResponse { Success = false, Error = saveError };
					return false;
				}
				response = BuildSuccess(options, createdUId, packageUId, caption);
				return true;
			}
			response = BuildSuccess(options, schema["uId"]?.ToString(), packageUId, caption);
			return true;
		}
		catch (Exception ex) {
			response = new SqlSchemaCreateResponse { Success = false, Error = ex.Message };
			return false;
		}
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
