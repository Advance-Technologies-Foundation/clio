namespace Clio.Command;

using System;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>Options for the package SQL install command.</summary>
[Verb("install-sql-schema", Aliases = ["sql-schema-install", "execute-sql-schema"],
	HelpText = "Execute a SQL script schema on a remote Creatio environment (runs raw SQL directly on the database)")]
public class SqlSchemaInstallOptions : EnvironmentOptions {

	/// <summary>Unique name of the package SQL script.</summary>
	[Option("schema-name", Required = true, HelpText = "SQL script schema name to execute")]
	public string SchemaName { get; set; }
}

/// <summary>Result of the package SQL install operation.</summary>
public sealed class SqlSchemaInstallResponse {

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

	/// <summary>Failure diagnostic, or null on success.</summary>
	[JsonProperty("error")]
	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }
}

/// <summary>Runs the native package SQL install operation.</summary>
public class SqlSchemaInstallCommand : Command<SqlSchemaInstallOptions> {

	private static readonly SchemaDesignerKind Kind = SchemaDesignerKind.SqlScript;

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	/// <summary>Initializes the command with its environment-scoped dependencies.</summary>
	public SqlSchemaInstallCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	/// <summary>Validates input and performs the native package SQL install operation.</summary>
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
			var executeRequest = new JArray(schemaUId);
			string executeUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.InstallSqlScripts);
			string executeResponseJson;
			try {
				executeResponseJson = _applicationClient.ExecuteNonReplayablePostRequest(
					executeUrl, executeRequest.ToString(Formatting.None));
			} catch (Exception ex) when (SchemaDesignerHelper.IsTransportFailure(ex)) {
				response = new SqlSchemaInstallResponse { Success = false, SchemaName = options.SchemaName,
					SchemaUId = schemaUId, Error = "InstallSqlScripts transport failed. Execution outcome is unknown; verify database effects before retrying." };
				return false;
			}
			(JObject executeResponse, string parseError) = SchemaDesignerHelper.ParseServiceResponse(
				"WorkspaceExplorerService InstallSqlScripts", executeUrl, executeResponseJson);
			if (parseError != null) {
				response = new SqlSchemaInstallResponse { Success = false, SchemaName = options.SchemaName,
					SchemaUId = schemaUId, Error = $"{parseError} Execution outcome is unknown; verify database effects before retrying." };
				return false;
			}
			if (!(executeResponse["success"]?.Value<bool>() ?? false)) {
				response = new SqlSchemaInstallResponse {
					Success = false,
					SchemaName = options.SchemaName,
					SchemaUId = schemaUId,
					Error = executeResponse["errorInfo"]?["message"]?.ToString() ?? "InstallSqlScripts failed"
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

	/// <inheritdoc />
	public override int Execute(SqlSchemaInstallOptions options) {
		bool success = TryInstall(options, out SqlSchemaInstallResponse response);
		_logger.WriteInfo(JsonConvert.SerializeObject(response));
		return success ? 0 : 1;
	}
}
