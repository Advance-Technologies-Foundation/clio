namespace Clio.Command;

using System;
using System.Linq;
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

	private const string SelectQueryRoute = "/DataService/json/SyncReply/SelectQuery";
	private const string CreateNewSchemaRoute = "ServiceModel/ScriptSchemaDesignerService.svc/CreateNewSchema";
	private const string SaveSchemaRoute = "ServiceModel/ScriptSchemaDesignerService.svc/SaveSchema";
	private const string ScriptSchemaManagerName = "ScriptSchemaManager";

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
			SqlSchemaCreateResponse validationError = ValidateInput(options);
			if (validationError != null) {
				response = validationError;
				return false;
			}
			if (!TryResolvePackageUId(options.PackageName, out string packageUId, out string packageError)) {
				response = new SqlSchemaCreateResponse { Success = false, Error = packageError };
				return false;
			}
			if (SchemaNameExists(options.SchemaName)) {
				response = new SqlSchemaCreateResponse {
					Success = false,
					Error = $"Schema '{options.SchemaName}' already exists in this environment."
				};
				return false;
			}
			string caption = string.IsNullOrWhiteSpace(options.Caption) ? options.SchemaName : options.Caption.Trim();
			if (!TryCreateAndSave(packageUId, options.SchemaName, caption, options.Description,
				out string schemaUId, out string createError)) {
				response = new SqlSchemaCreateResponse { Success = false, Error = createError };
				return false;
			}
			response = new SqlSchemaCreateResponse {
				Success = true,
				SchemaName = options.SchemaName,
				SchemaUId = schemaUId,
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

	private static SqlSchemaCreateResponse ValidateInput(SqlSchemaCreateOptions options) {
		if (options is null) {
			return new SqlSchemaCreateResponse { Success = false, Error = "options is required" };
		}
		if (string.IsNullOrWhiteSpace(options.SchemaName)) {
			return new SqlSchemaCreateResponse { Success = false, Error = "schema-name is required" };
		}
		if (!IsValidSchemaName(options.SchemaName)) {
			return new SqlSchemaCreateResponse {
				Success = false,
				Error = "schema-name must start with a letter and contain only letters, digits, or underscores"
			};
		}
		if (string.IsNullOrWhiteSpace(options.PackageName)) {
			return new SqlSchemaCreateResponse { Success = false, Error = "package-name is required" };
		}
		return null;
	}

	private static bool IsValidSchemaName(string name) {
		if (string.IsNullOrEmpty(name) || !char.IsLetter(name[0])) {
			return false;
		}
		return name.All(c => char.IsLetterOrDigit(c) || c == '_');
	}

	private bool SchemaNameExists(string schemaName) {
		var query = new JObject {
			["rootSchemaName"] = "SysSchema",
			["operationType"] = 0,
			["columns"] = new JObject {
				["items"] = new JObject {
					["UId"] = new JObject {
						["expression"] = new JObject { ["expressionType"] = 0, ["columnPath"] = "UId" }
					}
				}
			},
			["filters"] = new JObject {
				["filterType"] = 6,
				["logicalOperation"] = 0,
				["isEnabled"] = true,
				["items"] = new JObject {
					["byName"] = new JObject {
						["filterType"] = 1,
						["comparisonType"] = 3,
						["isEnabled"] = true,
						["leftExpression"] = new JObject { ["expressionType"] = 0, ["columnPath"] = "Name" },
						["rightExpression"] = new JObject {
							["expressionType"] = 2,
							["parameter"] = new JObject { ["dataValueType"] = 1, ["value"] = schemaName }
						}
					},
					["byManager"] = new JObject {
						["filterType"] = 1,
						["comparisonType"] = 3,
						["isEnabled"] = true,
						["leftExpression"] = new JObject { ["expressionType"] = 0, ["columnPath"] = "ManagerName" },
						["rightExpression"] = new JObject {
							["expressionType"] = 2,
							["parameter"] = new JObject { ["dataValueType"] = 1, ["value"] = ScriptSchemaManagerName }
						}
					}
				}
			},
			["rowCount"] = 1
		};
		string url = _serviceUrlBuilder.Build(SelectQueryRoute);
		string responseJson = _applicationClient.ExecutePostRequest(url, query.ToString(Formatting.None));
		JObject response = JObject.Parse(responseJson);
		var rows = response["rows"] as JArray ?? [];
		return rows.Count > 0;
	}

	private bool TryResolvePackageUId(string packageName, out string packageUId, out string error) {
		packageUId = null;
		error = null;
		var query = new JObject {
			["rootSchemaName"] = "SysPackage",
			["operationType"] = 0,
			["columns"] = new JObject {
				["items"] = new JObject {
					["UId"] = new JObject {
						["expression"] = new JObject { ["expressionType"] = 0, ["columnPath"] = "UId" }
					}
				}
			},
			["filters"] = new JObject {
				["filterType"] = 6,
				["logicalOperation"] = 0,
				["isEnabled"] = true,
				["items"] = new JObject {
					["filter0"] = new JObject {
						["filterType"] = 1,
						["comparisonType"] = 3,
						["isEnabled"] = true,
						["leftExpression"] = new JObject { ["expressionType"] = 0, ["columnPath"] = "Name" },
						["rightExpression"] = new JObject {
							["expressionType"] = 2,
							["parameter"] = new JObject { ["dataValueType"] = 1, ["value"] = packageName }
						}
					}
				}
			},
			["rowCount"] = 1
		};
		string url = _serviceUrlBuilder.Build(SelectQueryRoute);
		string responseJson = _applicationClient.ExecutePostRequest(url, query.ToString(Formatting.None));
		JObject response = JObject.Parse(responseJson);
		if (!(response["success"]?.Value<bool>() ?? false)) {
			error = "Failed to query SysPackage";
			return false;
		}
		var rows = response["rows"] as JArray ?? [];
		if (rows.Count == 0) {
			error = $"Package '{packageName}' not found in the target environment.";
			return false;
		}
		packageUId = rows[0]["UId"]?.ToString();
		if (string.IsNullOrWhiteSpace(packageUId)) {
			error = $"Package '{packageName}' has no UId in the SysPackage response.";
			return false;
		}
		return true;
	}

	private bool TryCreateAndSave(string packageUId, string schemaName, string caption, string description,
		out string schemaUId, out string error) {
		schemaUId = null;
		error = null;
		string createUrl = _serviceUrlBuilder.Build(CreateNewSchemaRoute);
		var createRequest = new JObject { ["packageUId"] = packageUId };
		string createResponseJson = _applicationClient.ExecutePostRequest(
			createUrl, createRequest.ToString(Formatting.None));
		JObject createResponse = JObject.Parse(createResponseJson);
		if (!(createResponse["success"]?.Value<bool>() ?? false)) {
			error = createResponse["errorInfo"]?["message"]?.ToString() ?? "CreateNewSchema failed";
			return false;
		}
		if (createResponse["schema"] is not JObject schema) {
			error = "CreateNewSchema did not return a schema payload.";
			return false;
		}
		schema["name"] = schemaName;
		schema["caption"] = new JArray(new JObject { ["cultureName"] = "en-US", ["value"] = caption });
		if (!string.IsNullOrWhiteSpace(description)) {
			schema["description"] = new JArray(
				new JObject { ["cultureName"] = "en-US", ["value"] = description });
		}
		string saveUrl = _serviceUrlBuilder.Build(SaveSchemaRoute);
		string saveResponseJson = _applicationClient.ExecutePostRequest(
			saveUrl, schema.ToString(Formatting.None));
		JObject saveResponse = JObject.Parse(saveResponseJson);
		if (!(saveResponse["success"]?.Value<bool>() ?? false)) {
			error = BuildSaveErrorMessage(saveResponse);
			return false;
		}
		schemaUId = schema["uId"]?.ToString();
		return true;
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
		return errorMessage;
	}
}
