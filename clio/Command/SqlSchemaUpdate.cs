namespace Clio.Command;

using System;
using System.Linq;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Verb("update-sql-schema", Aliases = ["sql-schema-update"],
	HelpText = "Update the body of a SQL script schema on a remote Creatio environment")]
public class SqlSchemaUpdateOptions : EnvironmentOptions {

	[Option("schema-name", Required = true, HelpText = "SQL script schema name")]
	public string SchemaName { get; set; }

	[Option("body", Required = false, HelpText = "New SQL body to save. Use body-file for large bodies.")]
	public string Body { get; set; }

	[Option("body-file", Required = false,
		HelpText = "Absolute path to a file whose contents are used as the new schema body. Takes precedence over --body when both are provided.")]
	public string BodyFile { get; set; }

	[Option("dry-run", Required = false, HelpText = "Validate and resolve the schema without saving")]
	public bool DryRun { get; set; }
}

public sealed class SqlSchemaUpdateResponse {

	[JsonProperty("success")]
	[System.Text.Json.Serialization.JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonProperty("schemaName")]
	[System.Text.Json.Serialization.JsonPropertyName("schemaName")]
	public string SchemaName { get; set; }

	[JsonProperty("bodyLength")]
	[System.Text.Json.Serialization.JsonPropertyName("bodyLength")]
	public int BodyLength { get; set; }

	[JsonProperty("dryRun")]
	[System.Text.Json.Serialization.JsonPropertyName("dryRun")]
	public bool DryRun { get; set; }

	[JsonProperty("error")]
	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }
}

public class SqlSchemaUpdateCommand : Command<SqlSchemaUpdateOptions> {

	private const string SelectQueryRoute = "/DataService/json/SyncReply/SelectQuery";
	private const string GetSchemaRoute = "ServiceModel/ScriptSchemaDesignerService.svc/GetSchema";
	private const string SaveSchemaRoute = "ServiceModel/ScriptSchemaDesignerService.svc/SaveSchema";
	private const string ScriptSchemaManagerName = "ScriptSchemaManager";

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	public SqlSchemaUpdateCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	public virtual bool TryUpdateSchema(SqlSchemaUpdateOptions options, out SqlSchemaUpdateResponse response) {
		try {
			if (string.IsNullOrWhiteSpace(options.SchemaName)) {
				response = new SqlSchemaUpdateResponse { Success = false, Error = "schema-name is required" };
				return false;
			}
			if (!string.IsNullOrWhiteSpace(options.BodyFile)) {
				if (!System.IO.File.Exists(options.BodyFile)) {
					response = new SqlSchemaUpdateResponse {
						Success = false,
						Error = $"body-file not found: '{options.BodyFile}'"
					};
					return false;
				}
				options.Body = System.IO.File.ReadAllText(options.BodyFile);
			}
			if (string.IsNullOrWhiteSpace(options.Body)) {
				response = new SqlSchemaUpdateResponse {
					Success = false,
					Error = "body (or body-file) is required and must not be empty"
				};
				return false;
			}
			if (!TryResolveSchemaUId(options.SchemaName, out string schemaUId, out response)) {
				return false;
			}
			if (options.DryRun) {
				response = CreateSuccessResponse(options, dryRun: true);
				return true;
			}
			if (!TryLoadSchemaForSave(options.SchemaName, schemaUId, out JObject schemaToSave, out response)) {
				return false;
			}
			schemaToSave["body"] = options.Body;
			if (!TrySaveSchema(schemaToSave, out response)) {
				return false;
			}
			response = CreateSuccessResponse(options, dryRun: false);
			return true;
		}
		catch (Exception ex) {
			response = new SqlSchemaUpdateResponse { Success = false, Error = ex.Message };
			return false;
		}
	}

	public override int Execute(SqlSchemaUpdateOptions options) {
		bool success = TryUpdateSchema(options, out SqlSchemaUpdateResponse response);
		_logger.WriteInfo(JsonConvert.SerializeObject(response));
		return success ? 0 : 1;
	}

	private bool TryResolveSchemaUId(
		string schemaName,
		out string schemaUId,
		out SqlSchemaUpdateResponse response) {
		var query = SqlSchemaQueries.BuildSelectUIdByName(schemaName, ScriptSchemaManagerName);
		string url = _serviceUrlBuilder.Build(SelectQueryRoute);
		string responseJson = _applicationClient.ExecutePostRequest(url, query.ToString(Formatting.None));
		JObject selectResponse = JObject.Parse(responseJson);
		var rows = selectResponse["rows"] as JArray ?? [];
		if (rows.Count == 0) {
			schemaUId = null;
			response = new SqlSchemaUpdateResponse {
				Success = false,
				Error = $"Schema '{schemaName}' not found (ManagerName='{ScriptSchemaManagerName}')"
			};
			return false;
		}
		schemaUId = rows[0]["UId"]?.ToString();
		if (string.IsNullOrWhiteSpace(schemaUId)) {
			response = new SqlSchemaUpdateResponse {
				Success = false,
				Error = $"Schema '{schemaName}' metadata is missing UId"
			};
			return false;
		}
		response = null;
		return true;
	}

	private bool TryLoadSchemaForSave(
		string schemaName,
		string schemaUId,
		out JObject schemaToSave,
		out SqlSchemaUpdateResponse response) {
		var getSchemaRequest = new JObject {
			["schemaUId"] = schemaUId,
			["useFullHierarchy"] = false
		};
		string designerUrl = _serviceUrlBuilder.Build(GetSchemaRoute);
		string getSchemaJson = _applicationClient.ExecutePostRequest(
			designerUrl,
			getSchemaRequest.ToString(Formatting.None));
		var getSchemaResponse = JObject.Parse(getSchemaJson);
		if (!(getSchemaResponse["success"]?.Value<bool>() ?? false) || getSchemaResponse["schema"] is not JObject schema) {
			schemaToSave = null;
			response = new SqlSchemaUpdateResponse {
				Success = false,
				Error = $"Failed to load schema '{schemaName}' via ScriptSchemaDesignerService"
			};
			return false;
		}
		schemaToSave = schema;
		response = null;
		return true;
	}

	private bool TrySaveSchema(JObject schemaToSave, out SqlSchemaUpdateResponse response) {
		string saveUrl = _serviceUrlBuilder.Build(SaveSchemaRoute);
		string saveJson = _applicationClient.ExecutePostRequest(
			saveUrl,
			schemaToSave.ToString(Formatting.None));
		var saveResponse = JObject.Parse(saveJson);
		if (saveResponse["success"]?.Value<bool>() ?? false) {
			response = null;
			return true;
		}
		response = new SqlSchemaUpdateResponse {
			Success = false,
			Error = BuildSaveErrorMessage(saveResponse)
		};
		return false;
	}

	private static string BuildSaveErrorMessage(JObject saveResponse) {
		string errorMessage = "Failed to save schema";
		if (saveResponse["errorInfo"] is JObject errorInfo) {
			string infoMessage = errorInfo["message"]?.ToString();
			if (!string.IsNullOrWhiteSpace(infoMessage)) {
				errorMessage = infoMessage;
			}
		}
		if (saveResponse["validationErrors"] is JArray validationErrors && validationErrors.Count > 0) {
			var messages = validationErrors
				.Select(e => e["message"]?.ToString() ?? e["caption"]?.ToString())
				.Where(m => !string.IsNullOrWhiteSpace(m));
			errorMessage = string.Join("; ", messages);
		}
		if (saveResponse["addonsErrors"] is JArray addonsErrors && addonsErrors.Count > 0) {
			errorMessage = string.Join("; ", addonsErrors.Select(e => e.ToString()));
		}
		return errorMessage;
	}

	private static SqlSchemaUpdateResponse CreateSuccessResponse(SqlSchemaUpdateOptions options, bool dryRun) {
		return new SqlSchemaUpdateResponse {
			Success = true,
			SchemaName = options.SchemaName,
			BodyLength = options.Body.Length,
			DryRun = dryRun
		};
	}
}
