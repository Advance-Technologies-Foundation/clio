namespace Clio.Command;

using System;
using System.IO;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Verb("get-client-unit-schema", Aliases = ["client-unit-schema-get"],
	HelpText = "Read the body and metadata of a client unit (JavaScript) schema from a remote Creatio environment")]
public class GetClientUnitSchemaOptions : EnvironmentOptions {

	[Option("schema-name", Required = true, HelpText = "Client unit schema name")]
	public string SchemaName { get; set; }

	[Option("output-file", Required = false,
		HelpText = "Absolute path to write the schema body to. When set, body is omitted from the response.")]
	public string OutputFile { get; set; }
}

public sealed class GetClientUnitSchemaResponse {

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

	[JsonProperty("caption")]
	[System.Text.Json.Serialization.JsonPropertyName("caption")]
	public string Caption { get; set; }

	[JsonProperty("body")]
	[System.Text.Json.Serialization.JsonPropertyName("body")]
	public string Body { get; set; }

	[JsonProperty("bodyLength")]
	[System.Text.Json.Serialization.JsonPropertyName("bodyLength")]
	public int BodyLength { get; set; }

	[JsonProperty("error")]
	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }
}

public class GetClientUnitSchemaCommand : Command<GetClientUnitSchemaOptions> {

	private const string SelectQueryRoute = "/DataService/json/SyncReply/SelectQuery";
	private const string GetSchemaRoute = "/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema";

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	public GetClientUnitSchemaCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	public virtual bool TryGetSchema(GetClientUnitSchemaOptions options, out GetClientUnitSchemaResponse response) {
		try {
			if (string.IsNullOrWhiteSpace(options.SchemaName)) {
				response = new GetClientUnitSchemaResponse {
					Success = false,
					Error = "schema-name is required"
				};
				return false;
			}
			if (!TryResolveSchemaUId(options.SchemaName, out string schemaUId, out response)) {
				return false;
			}
			if (!TryLoadSchema(options.SchemaName, schemaUId, out JObject schema, out response)) {
				return false;
			}
			string body = schema["body"]?.ToString() ?? string.Empty;
			string caption = ExtractCaption(schema);
			string packageName = schema["package"]?["name"]?.ToString();
			string schemaName = schema["name"]?.ToString() ?? options.SchemaName;
			response = new GetClientUnitSchemaResponse {
				Success = true,
				SchemaName = schemaName,
				SchemaUId = schemaUId,
				PackageName = packageName,
				Caption = caption,
				BodyLength = body.Length
			};
			if (!string.IsNullOrWhiteSpace(options.OutputFile)) {
				File.WriteAllText(options.OutputFile, body);
			} else {
				response.Body = body;
			}
			return true;
		}
		catch (Exception ex) {
			response = new GetClientUnitSchemaResponse { Success = false, Error = ex.Message };
			return false;
		}
	}

	public override int Execute(GetClientUnitSchemaOptions options) {
		bool success = TryGetSchema(options, out GetClientUnitSchemaResponse response);
		_logger.WriteInfo(JsonConvert.SerializeObject(response));
		return success ? 0 : 1;
	}

	private bool TryResolveSchemaUId(
		string schemaName,
		out string schemaUId,
		out GetClientUnitSchemaResponse response) {
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
					}
				}
			},
			["rowCount"] = 1
		};
		string url = _serviceUrlBuilder.Build(SelectQueryRoute);
		string responseJson = _applicationClient.ExecutePostRequest(url, query.ToString(Formatting.None));
		JObject selectResponse = JObject.Parse(responseJson);
		var rows = selectResponse["rows"] as JArray ?? [];
		if (rows.Count == 0) {
			schemaUId = null;
			response = new GetClientUnitSchemaResponse {
				Success = false,
				Error = $"Schema '{schemaName}' not found"
			};
			return false;
		}
		schemaUId = rows[0]["UId"]?.ToString();
		if (string.IsNullOrWhiteSpace(schemaUId)) {
			response = new GetClientUnitSchemaResponse {
				Success = false,
				Error = $"Schema '{schemaName}' metadata is missing UId"
			};
			return false;
		}
		response = null;
		return true;
	}

	private bool TryLoadSchema(
		string schemaName,
		string schemaUId,
		out JObject schema,
		out GetClientUnitSchemaResponse response) {
		var getSchemaRequest = new JObject {
			["schemaUId"] = schemaUId,
			["useFullHierarchy"] = false
		};
		string designerUrl = _serviceUrlBuilder.Build(GetSchemaRoute);
		string getSchemaJson = _applicationClient.ExecutePostRequest(
			designerUrl,
			getSchemaRequest.ToString(Formatting.None));
		JObject getSchemaResponse = JObject.Parse(getSchemaJson);
		if (getSchemaResponse["schema"] is not JObject loaded) {
			schema = null;
			response = new GetClientUnitSchemaResponse {
				Success = false,
				Error = $"Failed to load schema '{schemaName}' via ClientUnitSchemaDesignerService"
			};
			return false;
		}
		schema = loaded;
		response = null;
		return true;
	}

	private static string ExtractCaption(JObject schema) {
		if (schema["caption"] is JArray captions && captions.Count > 0) {
			return captions[0]?["value"]?.ToString();
		}
		return schema["caption"]?.ToString();
	}
}
