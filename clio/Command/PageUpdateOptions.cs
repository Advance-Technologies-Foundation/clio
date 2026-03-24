namespace Clio.Command {
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Clio.Common;
	using CommandLine;
	using Newtonsoft.Json;
	using Newtonsoft.Json.Linq;

	[Verb("page-update", HelpText = "Update Freedom UI page schema body")]
	public class PageUpdateOptions : EnvironmentOptions {
		[Option("schema-name", Required = true, HelpText = "Page schema name")]
		public string SchemaName { get; set; }

		[Option("body", Required = true, HelpText = "New JSON body content")]
		public string Body { get; set; }

		[Option("dry-run", Required = false, HelpText = "Validate only, don't save")]
		public bool DryRun { get; set; }

		[Option("resources", Required = false, HelpText = "JSON object of resource key-value pairs for #ResourceString(key)# macros")]
		public string Resources { get; set; }
	}

	public class PageUpdateCommand : Command<PageUpdateOptions> {

		private readonly IApplicationClient _applicationClient;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly ILogger _logger;

		public PageUpdateCommand(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			ILogger logger) {
			_applicationClient = applicationClient;
			_serviceUrlBuilder = serviceUrlBuilder;
			_logger = logger;
		}

		public bool TryUpdatePage(PageUpdateOptions options, out PageUpdateResponse response) {
			try {
				PageUpdateResponse validationError = ValidateInput(options);
				if (validationError != null) {
					response = validationError;
					return false;
				}
				var (metadata, queryError) = PageSchemaMetadataHelper.QuerySysSchemaRow(
					_applicationClient, _serviceUrlBuilder, options.SchemaName,
					("UId", "UId"));
				if (metadata == null) {
					response = new PageUpdateResponse { Success = false, Error = queryError };
					return false;
				}
				string schemaUId = metadata["UId"]?.ToString();
				if (options.DryRun) {
					response = new PageUpdateResponse {
						Success = true,
						SchemaName = options.SchemaName,
						BodyLength = options.Body.Length,
						DryRun = true
					};
					return true;
				}
				var getSchemaRequest = new JObject {
					["schemaUId"] = schemaUId,
					["useFullHierarchy"] = false
				};
				string designerUrl = _serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema");
				string getSchemaJson = _applicationClient.ExecutePostRequest(designerUrl, getSchemaRequest.ToString(Formatting.None));
				var getSchemaResponse = JObject.Parse(getSchemaJson);
				if (!(getSchemaResponse["success"]?.Value<bool>() ?? false) || getSchemaResponse["schema"] == null) {
					response = new PageUpdateResponse { Success = false, Error = $"Failed to load schema '{options.SchemaName}'" };
					return false;
				}
				var schemaToSave = getSchemaResponse["schema"] as JObject;
				schemaToSave["body"] = options.Body;
				List<string> registeredKeys = null;
				var bodyKeys = ResourceStringHelper.ExtractKeys(options.Body);
				var existingStrings = schemaToSave["localizableStrings"] as JArray;
				Dictionary<string, string> explicitResources = null;
				if (!string.IsNullOrWhiteSpace(options.Resources)) {
					try {
						explicitResources = JsonConvert.DeserializeObject<Dictionary<string, string>>(options.Resources);
					} catch {
					}
				}
				var (cleaned, registered) = ResourceStringHelper.CleanAndMerge(
					existingStrings, explicitResources, bodyKeys);
				schemaToSave["localizableStrings"] = cleaned;
				if (registered.Count > 0) {
					registeredKeys = registered;
				}
				string saveUrl = _serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema");
				string saveJson = _applicationClient.ExecutePostRequest(saveUrl, schemaToSave.ToString(Formatting.None));
				var saveResponse = JObject.Parse(saveJson);
				if (!(saveResponse["success"]?.Value<bool>() ?? false)) {
					string errorMessage = "Failed to save page schema";
					var errorInfo = saveResponse["errorInfo"] as JObject;
					if (errorInfo != null) {
						string infoMessage = errorInfo["message"]?.ToString();
						if (!string.IsNullOrWhiteSpace(infoMessage)) {
							errorMessage = infoMessage;
						}
					}
					var validationErrors = saveResponse["validationErrors"] as JArray;
					if (validationErrors != null && validationErrors.Count > 0) {
						var messages = validationErrors
							.Select(e => e["message"]?.ToString() ?? e["caption"]?.ToString())
							.Where(m => !string.IsNullOrWhiteSpace(m));
						errorMessage = string.Join("; ", messages);
					}
					var addonsErrors = saveResponse["addonsErrors"] as JArray;
					if (addonsErrors != null && addonsErrors.Count > 0) {
						errorMessage = string.Join("; ", addonsErrors.Select(e => e.ToString()));
					}
					response = new PageUpdateResponse { Success = false, Error = errorMessage };
					return false;
				}
				response = new PageUpdateResponse {
					Success = true,
					SchemaName = options.SchemaName,
					BodyLength = options.Body.Length,
					DryRun = false,
					ResourcesRegistered = registeredKeys?.Count ?? 0,
					RegisteredResourceKeys = registeredKeys
				};
				return true;
			}
			catch (Exception ex) {
				response = new PageUpdateResponse { Success = false, Error = ex.Message };
				return false;
			}
		}

		public override int Execute(PageUpdateOptions options) {
			bool success = TryUpdatePage(options, out PageUpdateResponse response);
			_logger.WriteInfo(JsonConvert.SerializeObject(response));
			return success ? 0 : 1;
		}

		private static PageUpdateResponse ValidateInput(PageUpdateOptions options) {
			if (string.IsNullOrWhiteSpace(options.SchemaName)) {
				return new PageUpdateResponse { Success = false, Error = "schemaName is required" };
			}
			if (string.IsNullOrWhiteSpace(options.Body)) {
				return new PageUpdateResponse { Success = false, Error = "body is required and must not be empty" };
			}
			var integrityResult = SchemaValidationService.ValidateMarkerIntegrity(options.Body);
			if (!integrityResult.IsValid) {
				return new PageUpdateResponse {
					Success = false,
					Error = $"Body is missing required marker pairs: {string.Join("; ", integrityResult.Errors)}"
				};
			}
			var syntaxResult = SchemaValidationService.ValidateJsSyntax(options.Body);
			if (!syntaxResult.IsValid) {
				return new PageUpdateResponse {
					Success = false,
					Error = $"Body contains invalid JavaScript syntax: {string.Join("; ", syntaxResult.Errors)}"
				};
			}
			return null;
		}
	}
}
