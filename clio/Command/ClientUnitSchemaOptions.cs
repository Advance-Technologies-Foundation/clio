namespace Clio.Command;

using System;
using System.Linq;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Options for the <c>update-client-unit-schema</c> command.
/// </summary>
[Verb("update-client-unit-schema",
	Aliases = ["client-unit-schema-update"],
	HelpText = "Update the raw body of any client unit schema (classic 7x or Freedom UI) without Freedom UI bundle/marker validation")]
public class ClientUnitSchemaUpdateOptions : EnvironmentOptions {

	[Option("schema-name", Required = true, HelpText = "Client unit schema name")]
	public string SchemaName { get; set; }

	[Option("body", Required = false, HelpText = "New raw JavaScript body to save. Use body-file for large bodies.")]
	public string Body { get; set; }

	[Option("body-file", Required = false, HelpText = "Absolute path to a file whose contents are used as the new schema body. Takes precedence over --body when both are provided.")]
	public string BodyFile { get; set; }

	[Option("dry-run", Required = false, HelpText = "Validate and resolve the schema without saving")]
	public bool DryRun { get; set; }
}

/// <summary>
/// Saves raw client unit schema bodies (classic 7x mixins, utilities, modules, Freedom UI, etc.)
/// through <c>ClientUnitSchemaDesignerService</c>, bypassing the Freedom UI-specific marker
/// validation, field-binding checks, and bundle merging performed by <see cref="PageUpdateCommand"/>.
/// </summary>
public class ClientUnitSchemaUpdateCommand : Command<ClientUnitSchemaUpdateOptions> {

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	public ClientUnitSchemaUpdateCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	public bool TryUpdateSchema(
		ClientUnitSchemaUpdateOptions options,
		out ClientUnitSchemaUpdateResponse response) {
		try {
			if (string.IsNullOrWhiteSpace(options.SchemaName)) {
				response = new ClientUnitSchemaUpdateResponse {
					Success = false,
					Error = "schema-name is required"
				};
				return false;
			}
			if (!string.IsNullOrWhiteSpace(options.BodyFile)) {
				if (!System.IO.File.Exists(options.BodyFile)) {
					response = new ClientUnitSchemaUpdateResponse {
						Success = false,
						Error = $"body-file not found: '{options.BodyFile}'"
					};
					return false;
				}
				options.Body = System.IO.File.ReadAllText(options.BodyFile);
			}
			if (string.IsNullOrWhiteSpace(options.Body)) {
				response = new ClientUnitSchemaUpdateResponse {
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
			response = new ClientUnitSchemaUpdateResponse {
				Success = false,
				Error = ex.Message
			};
			return false;
		}
	}

	public override int Execute(ClientUnitSchemaUpdateOptions options) {
		bool success = TryUpdateSchema(options, out ClientUnitSchemaUpdateResponse response);
		_logger.WriteInfo(JsonConvert.SerializeObject(response));
		return success ? 0 : 1;
	}

	private bool TryResolveSchemaUId(
		string schemaName,
		out string schemaUId,
		out ClientUnitSchemaUpdateResponse response) {
		var (metadata, queryError) = PageSchemaMetadataHelper.QuerySysSchemaRow(
			_applicationClient,
			_serviceUrlBuilder,
			schemaName,
			("UId", "UId"));
		if (metadata == null) {
			schemaUId = null;
			response = new ClientUnitSchemaUpdateResponse {
				Success = false,
				Error = queryError
			};
			return false;
		}
		schemaUId = metadata["UId"]?.ToString();
		if (string.IsNullOrWhiteSpace(schemaUId)) {
			response = new ClientUnitSchemaUpdateResponse {
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
		out ClientUnitSchemaUpdateResponse response) {
		var getSchemaRequest = new JObject {
			["schemaUId"] = schemaUId,
			["useFullHierarchy"] = false
		};
		string designerUrl = _serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema");
		string getSchemaJson = _applicationClient.ExecutePostRequest(
			designerUrl,
			getSchemaRequest.ToString(Formatting.None));
		var getSchemaResponse = JObject.Parse(getSchemaJson);
		if (!(getSchemaResponse["success"]?.Value<bool>() ?? false) || getSchemaResponse["schema"] is not JObject schema) {
			schemaToSave = null;
			response = new ClientUnitSchemaUpdateResponse {
				Success = false,
				Error = $"Failed to load schema '{schemaName}' via ClientUnitSchemaDesignerService"
			};
			return false;
		}
		schemaToSave = schema;
		response = null;
		return true;
	}

	private bool TrySaveSchema(
		JObject schemaToSave,
		out ClientUnitSchemaUpdateResponse response) {
		string saveUrl = _serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema");
		string saveJson = _applicationClient.ExecutePostRequest(
			saveUrl,
			schemaToSave.ToString(Formatting.None));
		var saveResponse = JObject.Parse(saveJson);
		if (saveResponse["success"]?.Value<bool>() ?? false) {
			response = null;
			return true;
		}
		response = new ClientUnitSchemaUpdateResponse {
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

	private static ClientUnitSchemaUpdateResponse CreateSuccessResponse(
		ClientUnitSchemaUpdateOptions options,
		bool dryRun) {
		return new ClientUnitSchemaUpdateResponse {
			Success = true,
			SchemaName = options.SchemaName,
			BodyLength = options.Body.Length,
			DryRun = dryRun
		};
	}
}

/// <summary>Response envelope for the <c>update-client-unit-schema</c> command.</summary>
public sealed class ClientUnitSchemaUpdateResponse {

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
