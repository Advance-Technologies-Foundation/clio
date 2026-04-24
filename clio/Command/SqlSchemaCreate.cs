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

	public SqlSchemaCreateCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
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
			if (SchemaDesignerHelper.SchemaNameExists(_applicationClient, _serviceUrlBuilder, options.SchemaName, Kind)) {
				response = new SqlSchemaCreateResponse {
					Success = false,
					Error = $"Schema '{options.SchemaName}' already exists in this environment."
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
			SchemaDesignerHelper.ApplySchemaMetadata(schema, options.SchemaName, caption, options.Description);
			string saveError = SchemaDesignerHelper.SaveSchema(_applicationClient, _serviceUrlBuilder, schema, Kind);
			if (saveError != null) {
				response = new SqlSchemaCreateResponse { Success = false, Error = saveError };
				return false;
			}
			response = new SqlSchemaCreateResponse {
				Success = true,
				SchemaName = options.SchemaName,
				SchemaUId = schema["uId"]?.ToString(),
				PackageName = options.PackageName,
				PackageUId = packageUId,
				Caption = caption
			};
			return true;
		}
		catch (Exception ex) {
			response = new SqlSchemaCreateResponse { Success = false, Error = ex.Message };
			return false;
		}
	}

	public override int Execute(SqlSchemaCreateOptions options) {
		bool success = TryCreate(options, out SqlSchemaCreateResponse response);
		_logger.WriteInfo(JsonConvert.SerializeObject(response));
		return success ? 0 : 1;
	}
}
