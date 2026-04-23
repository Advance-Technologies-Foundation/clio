namespace Clio.Command;

using System;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Verb("install-sql-schema", Aliases = ["sql-schema-install", "execute-sql-schema"],
	HelpText = "Execute a SQL script schema on a remote Creatio environment (runs raw SQL directly on the database)")]
public class SqlSchemaInstallOptions : EnvironmentOptions {

	[Option("schema-name", Required = true, HelpText = "SQL script schema name to execute")]
	public string SchemaName { get; set; }
}

public sealed class SqlSchemaInstallResponse {

	[JsonProperty("success")]
	[System.Text.Json.Serialization.JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonProperty("schemaName")]
	[System.Text.Json.Serialization.JsonPropertyName("schemaName")]
	public string SchemaName { get; set; }

	[JsonProperty("schemaUId")]
	[System.Text.Json.Serialization.JsonPropertyName("schemaUId")]
	public string SchemaUId { get; set; }

	[JsonProperty("error")]
	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }
}

public class SqlSchemaInstallCommand : Command<SqlSchemaInstallOptions> {

	private static readonly SchemaDesignerKind Kind = SchemaDesignerKind.SqlScript;
	private const string ExecuteScriptRoute = "ServiceModel/ScriptSchemaDesignerService.svc/ExecuteScript";

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	public SqlSchemaInstallCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	public virtual bool TryInstall(SqlSchemaInstallOptions options, out SqlSchemaInstallResponse response) {
		try {
			if (string.IsNullOrWhiteSpace(options.SchemaName)) {
				response = new SqlSchemaInstallResponse { Success = false, Error = "schema-name is required" };
				return false;
			}
			(string schemaUId, string resolveError) = SchemaDesignerHelper.ResolveSchemaUId(
				_applicationClient, _serviceUrlBuilder, options.SchemaName, Kind);
			if (resolveError != null) {
				response = new SqlSchemaInstallResponse { Success = false, Error = resolveError };
				return false;
			}
			var executeRequest = new JObject { ["schemaUId"] = schemaUId };
			string executeUrl = _serviceUrlBuilder.Build(ExecuteScriptRoute);
			string executeResponseJson = _applicationClient.ExecutePostRequest(
				executeUrl, executeRequest.ToString(Formatting.None));
			JObject executeResponse = JObject.Parse(executeResponseJson);
			if (!(executeResponse["success"]?.Value<bool>() ?? false)) {
				response = new SqlSchemaInstallResponse {
					Success = false,
					SchemaName = options.SchemaName,
					SchemaUId = schemaUId,
					Error = executeResponse["errorInfo"]?["message"]?.ToString() ?? "ExecuteScript failed"
				};
				return false;
			}
			response = new SqlSchemaInstallResponse {
				Success = true,
				SchemaName = options.SchemaName,
				SchemaUId = schemaUId
			};
			return true;
		}
		catch (Exception ex) {
			response = new SqlSchemaInstallResponse { Success = false, Error = ex.Message };
			return false;
		}
	}

	public override int Execute(SqlSchemaInstallOptions options) {
		bool success = TryInstall(options, out SqlSchemaInstallResponse response);
		_logger.WriteInfo(JsonConvert.SerializeObject(response));
		return success ? 0 : 1;
	}
}
