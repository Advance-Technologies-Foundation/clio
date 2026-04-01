namespace Clio.Command {
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Clio.Common;
	using CommandLine;
	using Newtonsoft.Json;
	using Newtonsoft.Json.Linq;

	/// <summary>
	/// Options for the <c>page-update</c> command.
	/// </summary>
	[Verb("page-update", HelpText = "Update Freedom UI page schema body")]
	public class PageUpdateOptions : EnvironmentOptions {
		/// <summary>
		/// Gets or sets the page schema name to update.
		/// </summary>
		[Option("schema-name", Required = true, HelpText = "Page schema name")]
		public string SchemaName { get; set; }

		/// <summary>
		/// Gets or sets the full raw JavaScript body to save.
		/// </summary>
		[Option("body", Required = true, HelpText = "New JSON body content")]
		public string Body { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether the command should validate without saving.
		/// </summary>
		[Option("dry-run", Required = false, HelpText = "Validate only, don't save")]
		public bool DryRun { get; set; }

		/// <summary>
		/// Gets or sets the explicit resource captions used for <c>#ResourceString(key)#</c> macros.
		/// </summary>
		[Option("resources", Required = false, HelpText = "JSON object of resource key-value pairs for #ResourceString(key)# macros")]
		public string Resources { get; set; }
	}

	/// <summary>
	/// Validates and saves raw Freedom UI page bodies.
	/// </summary>
	public class PageUpdateCommand : Command<PageUpdateOptions> {
		private readonly IApplicationClient _applicationClient;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly ILogger _logger;

		/// <summary>
		/// Initializes a new instance of the <see cref="PageUpdateCommand"/> class.
		/// </summary>
		/// <param name="applicationClient">Remote Creatio client.</param>
		/// <param name="serviceUrlBuilder">Service URL builder.</param>
		/// <param name="logger">Logger used for CLI output.</param>
		public PageUpdateCommand(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			ILogger logger) {
			_applicationClient = applicationClient;
			_serviceUrlBuilder = serviceUrlBuilder;
			_logger = logger;
		}

		/// <summary>
		/// Attempts to validate and save the requested raw page body.
		/// </summary>
		/// <param name="options">Command options.</param>
		/// <param name="response">Structured command response.</param>
		/// <returns><c>true</c> when the page was updated successfully; otherwise <c>false</c>.</returns>
		public bool TryUpdatePage(PageUpdateOptions options, out PageUpdateResponse response) {
			try {
				PageUpdateResponse validationError = ValidateInput(options, out Dictionary<string, string> explicitResources);
				if (validationError != null) {
					response = validationError;
					return false;
				}
				if (!TryResolveSchemaUId(options.SchemaName, out string schemaUId, out response)) {
					return false;
				}
				if (options.DryRun) {
					response = CreateSuccessResponse(options, dryRun: true, registeredKeys: null);
					return true;
				}
				if (!TryLoadSchemaForSave(options.SchemaName, schemaUId, out JObject schemaToSave, out response)) {
					return false;
				}
				List<string> registeredKeys = UpdateSchemaBody(schemaToSave, options.Body, explicitResources);
				if (!TrySaveSchema(schemaToSave, out response)) {
					return false;
				}
				response = CreateSuccessResponse(options, dryRun: false, registeredKeys);
				return true;
			}
			catch (Exception ex) {
				response = new PageUpdateResponse { Success = false, Error = ex.Message };
				return false;
			}
		}

		/// <summary>
		/// Executes the command and writes the structured response to the CLI output.
		/// </summary>
		/// <param name="options">Command options.</param>
		/// <returns>Command exit code.</returns>
		public override int Execute(PageUpdateOptions options) {
			bool success = TryUpdatePage(options, out PageUpdateResponse response);
			_logger.WriteInfo(JsonConvert.SerializeObject(response));
			return success ? 0 : 1;
		}

		private bool TryResolveSchemaUId(string schemaName, out string schemaUId, out PageUpdateResponse response) {
			var (metadata, queryError) = PageSchemaMetadataHelper.QuerySysSchemaRow(
				_applicationClient,
				_serviceUrlBuilder,
				schemaName,
				("UId", "UId"));
			if (metadata == null) {
				schemaUId = null;
				response = new PageUpdateResponse { Success = false, Error = queryError };
				return false;
			}
			schemaUId = metadata["UId"]?.ToString();
			response = null;
			return true;
		}

		private bool TryLoadSchemaForSave(
			string schemaName,
			string schemaUId,
			out JObject schemaToSave,
			out PageUpdateResponse response) {
			var getSchemaRequest = new JObject {
				["schemaUId"] = schemaUId,
				["useFullHierarchy"] = false
			};
			string designerUrl = _serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema");
			string getSchemaJson = _applicationClient.ExecutePostRequest(designerUrl, getSchemaRequest.ToString(Formatting.None));
			var getSchemaResponse = JObject.Parse(getSchemaJson);
			if (!(getSchemaResponse["success"]?.Value<bool>() ?? false) || getSchemaResponse["schema"] is not JObject schema) {
				schemaToSave = null;
				response = new PageUpdateResponse { Success = false, Error = $"Failed to load schema '{schemaName}'" };
				return false;
			}
			schemaToSave = schema;
			response = null;
			return true;
		}

		private static List<string> UpdateSchemaBody(JObject schemaToSave, string body, Dictionary<string, string> explicitResources) {
			schemaToSave["body"] = body;
			var bodyKeys = ResourceStringHelper.ExtractKeys(body);
			var existingStrings = schemaToSave["localizableStrings"] as JArray;
			var (cleaned, registered) = ResourceStringHelper.CleanAndMerge(existingStrings, explicitResources, bodyKeys);
			schemaToSave["localizableStrings"] = cleaned;
			return registered.Count > 0 ? registered : null;
		}

		private bool TrySaveSchema(JObject schemaToSave, out PageUpdateResponse response) {
			string saveUrl = _serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema");
			string saveJson = _applicationClient.ExecutePostRequest(saveUrl, schemaToSave.ToString(Formatting.None));
			var saveResponse = JObject.Parse(saveJson);
			if (saveResponse["success"]?.Value<bool>() ?? false) {
				response = null;
				return true;
			}
			response = new PageUpdateResponse {
				Success = false,
				Error = BuildSaveErrorMessage(saveResponse)
			};
			return false;
		}

		private static string BuildSaveErrorMessage(JObject saveResponse) {
			string errorMessage = "Failed to save page schema";
			if (saveResponse["errorInfo"] is JObject errorInfo) {
				string infoMessage = errorInfo["message"]?.ToString();
				if (!string.IsNullOrWhiteSpace(infoMessage)) {
					errorMessage = infoMessage;
				}
			}
			if (saveResponse["validationErrors"] is JArray validationErrors && validationErrors.Count > 0) {
				IEnumerable<string> messages = validationErrors
					.Select(e => e["message"]?.ToString() ?? e["caption"]?.ToString())
					.Where(m => !string.IsNullOrWhiteSpace(m));
				errorMessage = string.Join("; ", messages);
			}
			if (saveResponse["addonsErrors"] is JArray addonsErrors && addonsErrors.Count > 0) {
				errorMessage = string.Join("; ", addonsErrors.Select(e => e.ToString()));
			}
			return errorMessage;
		}

		private static PageUpdateResponse CreateSuccessResponse(
			PageUpdateOptions options,
			bool dryRun,
			List<string> registeredKeys) {
			return new PageUpdateResponse {
				Success = true,
				SchemaName = options.SchemaName,
				BodyLength = options.Body.Length,
				DryRun = dryRun,
				ResourcesRegistered = registeredKeys?.Count ?? 0,
				RegisteredResourceKeys = registeredKeys
			};
		}

		private static PageUpdateResponse ValidateInput(PageUpdateOptions options, out Dictionary<string, string> explicitResources) {
			explicitResources = null;
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
			if (!SchemaValidationService.TryParseResources(options.Resources, out explicitResources, out _)) {
				return new PageUpdateResponse {
					Success = false,
					Error = "resources must be a valid JSON object string"
				};
			}
			var semanticResult = SchemaValidationService.ValidateStandardFieldBindings(options.Body, explicitResources);
			if (!semanticResult.IsValid) {
				return new PageUpdateResponse {
					Success = false,
					Error = $"Body contains invalid form field bindings: {string.Join("; ", semanticResult.Errors)}"
				};
			}
			return null;
		}
	}
}
