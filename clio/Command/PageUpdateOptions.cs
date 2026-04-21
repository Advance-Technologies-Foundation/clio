namespace Clio.Command {
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using Clio.Common;
	using CommandLine;
	using Newtonsoft.Json;
	using Newtonsoft.Json.Linq;

	/// <summary>
	/// Options for the <c>update-page</c> command.
	/// </summary>
	[Verb("update-page", Aliases = ["page-update"], HelpText = "Update Freedom UI page schema body")]
	public class PageUpdateOptions : EnvironmentOptions {
		/// <summary>
		/// Gets or sets the page schema name to update.
		/// </summary>
		[Option("schema-name", Required = true, HelpText = "Page schema name")]
		public string SchemaName { get; set; }

		/// <summary>
		/// Gets or sets the full raw JavaScript body to save.
		/// </summary>
		[Option("body", Required = false, HelpText = "New JSON body content (inline)")]
		public string Body { get; set; }

		/// <summary>
		/// Gets or sets path to a file containing the new body. Alternative to --body.
		/// </summary>
		[Option("body-file", Required = false, HelpText = "Path to a file containing the new body. Alternative to --body.")]
		public string? BodyFile { get; set; }

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

		/// <summary>
		/// Gets or sets the optional properties to merge into the schema, as a JSON array of {key, value} objects.
		/// </summary>
		[Option("optional-properties", Required = false, HelpText = "JSON array of {key, value} objects to merge into schema optionalProperties")]
		public string? OptionalProperties { get; set; }
	}

	/// <summary>
	/// Validates and saves raw Freedom UI page bodies.
	/// </summary>
	public class PageUpdateCommand : Command<PageUpdateOptions> {
		private readonly IApplicationClient _applicationClient;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly ILogger _logger;
		private readonly IPageDesignerHierarchyClient _hierarchyClient;

		/// <summary>
		/// Initializes a new instance of the <see cref="PageUpdateCommand"/> class.
		/// </summary>
		/// <param name="applicationClient">Remote Creatio client.</param>
		/// <param name="serviceUrlBuilder">Service URL builder.</param>
		/// <param name="logger">Logger used for CLI output.</param>
		/// <param name="hierarchyClient">Designer hierarchy client used to resolve replacing schemas.</param>
		public PageUpdateCommand(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			ILogger logger,
			IPageDesignerHierarchyClient hierarchyClient = null) {
			_applicationClient = applicationClient;
			_serviceUrlBuilder = serviceUrlBuilder;
			_logger = logger;
			_hierarchyClient = hierarchyClient;
		}

		/// <summary>
		/// Attempts to validate and save the requested raw page body.
		/// </summary>
		/// <param name="options">Command options.</param>
		/// <param name="response">Structured command response.</param>
		/// <returns><c>true</c> when the page was updated successfully; otherwise <c>false</c>.</returns>
		public bool TryUpdatePage(PageUpdateOptions options, out PageUpdateResponse response) {
			try {
				if (string.IsNullOrWhiteSpace(options.Body) && !string.IsNullOrWhiteSpace(options.BodyFile)) {
					if (!File.Exists(options.BodyFile)) {
						response = new PageUpdateResponse { Success = false, Error = $"File not found: {options.BodyFile}" };
						return false;
					}
					options.Body = File.ReadAllText(options.BodyFile);
				}
				PageUpdateResponse validationError = ValidateInput(options, out Dictionary<string, string> explicitResources);
				if (validationError != null) {
					response = validationError;
					return false;
				}
				if (!TryResolveEditableSchemaContext(options.SchemaName, out EditableSchemaContext context, out response)) {
					return false;
				}
				if (options.DryRun) {
					response = CreateSuccessResponse(options, dryRun: true, registeredKeys: null);
					return true;
				}
				if (!TryLoadSchemaForSave(options.SchemaName, context, out JObject schemaToSave, out response)) {
					return false;
				}
				JArray parsedOptionalProperties = null;
				if (!string.IsNullOrWhiteSpace(options.OptionalProperties)) {
					parsedOptionalProperties = JArray.Parse(options.OptionalProperties);
				}
				List<string> registeredKeys = UpdateSchemaBody(schemaToSave, options.Body, explicitResources, parsedOptionalProperties);
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

		private bool TryResolveEditableSchemaContext(
				string schemaName,
				out EditableSchemaContext context,
				out PageUpdateResponse response) {
			context = null;
			var (metadata, queryError) = PageSchemaMetadataHelper.QuerySysSchemaRow(
				_applicationClient,
				_serviceUrlBuilder,
				schemaName,
				("UId", "UId"));
			if (metadata == null) {
				response = new PageUpdateResponse { Success = false, Error = queryError };
				return false;
			}
			string rawSchemaUId = metadata["UId"]?.ToString();
			if (string.IsNullOrWhiteSpace(rawSchemaUId)) {
				response = new PageUpdateResponse { Success = false, Error = $"Schema '{schemaName}' metadata is missing UId" };
				return false;
			}
			if (_hierarchyClient == null) {
				context = new EditableSchemaContext {
					SchemaName = schemaName,
					EditableSchemaUId = rawSchemaUId,
					TemplateSchemaUId = rawSchemaUId,
					IsCreateReplacing = false
				};
				response = null;
				return true;
			}
			string designPackageUId;
			try {
				designPackageUId = _hierarchyClient.GetDesignPackageUId(rawSchemaUId);
			} catch (Exception ex) {
				response = new PageUpdateResponse { Success = false, Error = $"Failed to resolve design package for '{schemaName}': {ex.Message}" };
				return false;
			}
			if (string.IsNullOrWhiteSpace(designPackageUId)) {
				response = new PageUpdateResponse { Success = false, Error = $"Failed to resolve design package for '{schemaName}': no package returned" };
				return false;
			}
			IReadOnlyList<PageDesignerHierarchySchema> hierarchy;
			try {
				hierarchy = _hierarchyClient.GetParentSchemas(rawSchemaUId, designPackageUId);
			} catch (Exception ex) {
				response = new PageUpdateResponse { Success = false, Error = $"Failed to load hierarchy for '{schemaName}': {ex.Message}" };
				return false;
			}
			if (hierarchy == null || hierarchy.Count == 0) {
				response = new PageUpdateResponse { Success = false, Error = $"Schema '{schemaName}' hierarchy is empty" };
				return false;
			}
			PageDesignerHierarchySchema head = hierarchy[0];
			bool headInDesignPackage = string.Equals(head.PackageUId, designPackageUId, StringComparison.OrdinalIgnoreCase);
			context = new EditableSchemaContext {
				SchemaName = schemaName,
				EditableSchemaUId = headInDesignPackage ? head.UId : Guid.NewGuid().ToString(),
				DesignPackageUId = designPackageUId,
				IsCreateReplacing = !headInDesignPackage,
				ParentSchemaUId = headInDesignPackage ? null : head.UId,
				ParentSchemaName = head.Name,
				TemplateSchemaUId = head.UId
			};
			response = null;
			return true;
		}

		private static List<string> UpdateSchemaBody(JObject schemaToSave, string body, Dictionary<string, string> explicitResources, JArray optionalProperties = null) {
			schemaToSave["body"] = body;
			if (optionalProperties != null) {
				MergeOptionalProperties(schemaToSave, optionalProperties);
			}
			var bodyKeys = ResourceStringHelper.ExtractKeys(body);
			var existingStrings = schemaToSave["localizableStrings"] as JArray;
			var (cleaned, registered) = ResourceStringHelper.CleanAndMerge(existingStrings, explicitResources, bodyKeys);
			schemaToSave["localizableStrings"] = cleaned;
			return registered.Count > 0 ? registered : null;
		}

		private static void MergeOptionalProperties(JObject schemaToSave, JArray incoming) {
			var existing = schemaToSave["optionalProperties"] as JArray ?? new JArray();
			var merged = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
			foreach (JToken item in existing) {
				string key = item["key"]?.ToString();
				if (!string.IsNullOrWhiteSpace(key)) {
					merged[key] = item;
				}
			}
			foreach (JToken item in incoming) {
				string key = item["key"]?.ToString();
				if (!string.IsNullOrWhiteSpace(key)) {
					merged[key] = item;
				}
			}
			schemaToSave["optionalProperties"] = new JArray(merged.Values);
		}

		private bool TryLoadSchemaForSave(
				string schemaName,
				EditableSchemaContext context,
				out JObject schemaToSave,
				out PageUpdateResponse response) {
			if (!TryGetSchema(context.TemplateSchemaUId, out JObject template, out string loadError)) {
				schemaToSave = null;
				response = new PageUpdateResponse {
					Success = false,
					Error = loadError ?? $"Failed to load schema '{schemaName}'"
				};
				return false;
			}
			schemaToSave = context.IsCreateReplacing
				? BuildNewReplacingSchemaDto(template, context)
				: template;
			response = null;
			return true;
		}

		private bool TryGetSchema(string schemaUId, out JObject schema, out string error) {
			var request = new JObject {
				["schemaUId"] = schemaUId,
				["useFullHierarchy"] = false
			};
			string url = _serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema");
			string json = _applicationClient.ExecutePostRequest(url, request.ToString(Formatting.None));
			var response = JObject.Parse(json);
			if (!(response["success"]?.Value<bool>() ?? false) || response["schema"] is not JObject loaded) {
				schema = null;
				error = response["errorInfo"]?["message"]?.ToString() ?? $"Failed to load schema '{schemaUId}'";
				return false;
			}
			schema = loaded;
			error = null;
			return true;
		}

		private static JObject BuildNewReplacingSchemaDto(JObject template, EditableSchemaContext context) {
			string originalName = template["name"]?.ToString() ?? context.SchemaName;
			JObject dto = (JObject)template.DeepClone();
			dto["uId"] = context.EditableSchemaUId;
			dto["name"] = originalName;
			dto["isReadOnly"] = false;
			dto["extendParent"] = true;
			dto["caption"] = null;
			dto["localizableStrings"] = null;
			dto["package"] = new JObject {
				["uId"] = context.DesignPackageUId,
				["name"] = string.Empty
			};
			dto["parent"] = new JObject {
				["uId"] = context.ParentSchemaUId,
				["name"] = context.ParentSchemaName ?? originalName
			};
			dto["body"] = BuildEmptyReplacingBody(originalName);
			return dto;
		}

		private static string BuildEmptyReplacingBody(string schemaName) {
			return "define(\"" + schemaName + "\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {\n" +
				"\treturn {\n" +
				"\t\tviewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/,\n" +
				"\t\tviewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,\n" +
				"\t\tmodelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,\n" +
				"\t\thandlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,\n" +
				"\t\tconverters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,\n" +
				"\t\tvalidators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/\n" +
				"\t};\n" +
				"});";
		}

		internal sealed class EditableSchemaContext {
			public string SchemaName { get; set; }
			public string EditableSchemaUId { get; set; }
			public string DesignPackageUId { get; set; }
			public bool IsCreateReplacing { get; set; }
			public string ParentSchemaUId { get; set; }
			public string ParentSchemaName { get; set; }
			public string TemplateSchemaUId { get; set; }
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
				return new PageUpdateResponse {
					Success = false,
					Error = "body is required and must not be empty. Reuse get-page raw.body instead of bundle or viewConfig fragments."
				};
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
			if (!string.IsNullOrWhiteSpace(options.OptionalProperties)) {
				try {
					JArray.Parse(options.OptionalProperties);
				} catch {
					return new PageUpdateResponse {
						Success = false,
						Error = "optional-properties must be a valid JSON array of {key, value} objects"
					};
				}
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
