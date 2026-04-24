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

		/// <summary>
		/// Gets or sets the write mode. <c>replace</c> (default) saves the provided body verbatim.
		/// <c>append</c> merges the provided body fragment with the current schema body on the server.
		/// </summary>
		[Option("mode", Required = false, HelpText = "Write mode: 'replace' (default) or 'append' (merge with existing body)")]
		public string? Mode { get; set; }

		/// <summary>
		/// Optional explicit design package UId override. When set, bypasses
		/// <c>GetDesignPackageUId</c> and saves the replacing schema into the specified package.
		/// Use this when multiple apps replace the same platform page and the automatic resolution
		/// picks the wrong app.
		/// </summary>
		[Option("target-package-uid", Required = false, HelpText = "Explicit target package UId for the replacing schema (overrides automatic design-package resolution)")]
		public string? TargetPackageUId { get; set; }

		/// <summary>
		/// Optional explicit schema UId to save into directly. When set, bypasses hierarchy resolution
		/// entirely — <c>update-page</c> loads the schema by this UId, applies the body (or appends to
		/// it when <c>mode</c> is <c>append</c>), and saves. Use this when the page name is replaced by
		/// multiple apps and you already know the exact schema UId (for example, obtained via
		/// <c>list-pages</c>) of the replacement you want to modify.
		/// </summary>
		[Option("target-schema-uid", Required = false, HelpText = "Explicit schema UId to save into (bypasses hierarchy resolution)")]
		public string? TargetSchemaUId { get; set; }
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
				if (!TryLoadBodyFromFile(options, out response)) return false;
				PageUpdateResponse validationError = ValidateInput(options, out Dictionary<string, string> explicitResources);
				if (validationError != null) { response = validationError; return false; }
				if (!TryResolveContext(options, out EditableSchemaContext context, out response)) return false;
				if (options.DryRun) { response = CreateSuccessResponse(options, dryRun: true, registeredKeys: null); return true; }
				if (!TryLoadSchemaForSave(options.SchemaName, context, out JObject schemaToSave, out response)) return false;
				JArray parsedOptionalProperties = string.IsNullOrWhiteSpace(options.OptionalProperties)
					? null : JArray.Parse(options.OptionalProperties);
				if (!TryResolveBodyToWrite(schemaToSave, options, out string bodyToWrite, out response)) return false;
				List<string> registeredKeys = UpdateSchemaBody(schemaToSave, bodyToWrite, explicitResources, parsedOptionalProperties);
				if (!TrySaveSchema(schemaToSave, out response)) return false;
				response = CreateSuccessResponse(options, dryRun: false, registeredKeys);
				return true;
			} catch (Exception ex) {
				response = new PageUpdateResponse { Success = false, Error = ex.Message };
				return false;
			}
		}

		private static bool TryLoadBodyFromFile(PageUpdateOptions options, out PageUpdateResponse response) {
			response = null;
			if (!string.IsNullOrWhiteSpace(options.Body) || string.IsNullOrWhiteSpace(options.BodyFile)) return true;
			if (!File.Exists(options.BodyFile)) {
				response = new PageUpdateResponse { Success = false, Error = $"File not found: {options.BodyFile}" };
				return false;
			}
			options.Body = File.ReadAllText(options.BodyFile);
			return true;
		}

		private bool TryResolveContext(PageUpdateOptions options, out EditableSchemaContext context, out PageUpdateResponse response) {
			if (!string.IsNullOrWhiteSpace(options.TargetSchemaUId)) {
				context = new EditableSchemaContext { SchemaName = options.SchemaName, EditableSchemaUId = options.TargetSchemaUId, TemplateSchemaUId = options.TargetSchemaUId, IsCreateReplacing = false };
				response = null;
				return true;
			}
			return TryResolveEditableSchemaContext(options.SchemaName, options.TargetPackageUId, out context, out response);
		}

		private static bool TryResolveBodyToWrite(JObject schemaToSave, PageUpdateOptions options, out string bodyToWrite, out PageUpdateResponse response) {
			bodyToWrite = options.Body;
			response = null;
			if (!string.Equals(options.Mode, "append", StringComparison.OrdinalIgnoreCase)) return true;
			string currentBody = schemaToSave["body"]?.ToString();
			if (string.IsNullOrWhiteSpace(currentBody)) return true;
			try {
				bodyToWrite = PageBodyMerger.Merge(currentBody, options.Body);
				return true;
			} catch (Exception ex) {
				response = new PageUpdateResponse { Success = false, Error = $"Append merge failed: {ex.Message} [hint: the incoming body must contain valid marker pairs with new viewConfigDiff/handlers operations. See docs://mcp/guides/page-modification.]" };
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

		private string TargetPackageUIdOverride { get; set; }

		private bool TryResolveEditableSchemaContext(string schemaName, string targetPackageUIdOverride, out EditableSchemaContext context, out PageUpdateResponse response) {
			TargetPackageUIdOverride = targetPackageUIdOverride;
			context = null;
			var (metadata, queryError) = PageSchemaMetadataHelper.QuerySysSchemaRow(_applicationClient, _serviceUrlBuilder, schemaName, ("UId", "UId"));
			if (metadata == null) { response = new PageUpdateResponse { Success = false, Error = queryError }; return false; }
			string rawSchemaUId = metadata["UId"]?.ToString();
			if (string.IsNullOrWhiteSpace(rawSchemaUId)) { response = new PageUpdateResponse { Success = false, Error = $"Schema '{schemaName}' metadata is missing UId" }; return false; }
			if (_hierarchyClient == null) {
				context = new EditableSchemaContext { SchemaName = schemaName, EditableSchemaUId = rawSchemaUId, TemplateSchemaUId = rawSchemaUId, IsCreateReplacing = false };
				response = null;
				return true;
			}
			if (!TryGetDesignPackageUId(rawSchemaUId, schemaName, out string designPackageUId, out response)) return false;
			if (!TryGetHierarchy(rawSchemaUId, designPackageUId, schemaName, out IReadOnlyList<PageDesignerHierarchySchema> hierarchy, out response)) return false;
			PageDesignerHierarchySchema head = hierarchy[0];
			string rootUId = FindRootSchemaUId(hierarchy, schemaName);
			PageDesignerHierarchySchema root = !string.IsNullOrWhiteSpace(rootUId)
				? hierarchy.FirstOrDefault(s => string.Equals(s.UId, rootUId, StringComparison.OrdinalIgnoreCase)) ?? head : head;
			(string editableUId, bool isCreateReplacing) = ResolveEditableUId(head, schemaName, designPackageUId);
			context = new EditableSchemaContext {
				SchemaName = schemaName, EditableSchemaUId = editableUId, DesignPackageUId = designPackageUId,
				IsCreateReplacing = isCreateReplacing, ParentSchemaUId = isCreateReplacing ? root.UId : null,
				ParentSchemaName = root.Name, TemplateSchemaUId = isCreateReplacing ? root.UId : editableUId
			};
			response = null;
			return true;
		}

		private bool TryGetDesignPackageUId(string rawSchemaUId, string schemaName, out string designPackageUId, out PageUpdateResponse response) {
			if (!string.IsNullOrWhiteSpace(TargetPackageUIdOverride)) {
				designPackageUId = TargetPackageUIdOverride;
				response = null;
				return true;
			}
			try {
				designPackageUId = _hierarchyClient.GetDesignPackageUId(rawSchemaUId);
			} catch (Exception ex) {
				designPackageUId = null;
				response = new PageUpdateResponse { Success = false, Error = $"Failed to resolve design package for '{schemaName}': {ex.Message}" };
				return false;
			}
			if (!string.IsNullOrWhiteSpace(designPackageUId)) { response = null; return true; }
			response = new PageUpdateResponse { Success = false, Error = $"Failed to resolve design package for '{schemaName}': no package returned" };
			return false;
		}

		private bool TryGetHierarchy(string rawSchemaUId, string designPackageUId, string schemaName, out IReadOnlyList<PageDesignerHierarchySchema> hierarchy, out PageUpdateResponse response) {
			try {
				hierarchy = _hierarchyClient.GetParentSchemas(rawSchemaUId, designPackageUId);
			} catch (Exception ex) {
				hierarchy = null;
				response = new PageUpdateResponse { Success = false, Error = $"Failed to load hierarchy for '{schemaName}': {ex.Message}" };
				return false;
			}
			if (hierarchy != null && hierarchy.Count > 0) { response = null; return true; }
			response = new PageUpdateResponse { Success = false, Error = $"Schema '{schemaName}' hierarchy is empty" };
			return false;
		}

		private (string editableUId, bool isCreateReplacing) ResolveEditableUId(PageDesignerHierarchySchema head, string schemaName, string designPackageUId) {
			if (string.Equals(head.PackageUId, designPackageUId, StringComparison.OrdinalIgnoreCase))
				return (head.UId, false);
			string existingInPkg = PageSchemaMetadataHelper.FindExistingSchemaInPackage(_applicationClient, _serviceUrlBuilder, schemaName, designPackageUId);
			return string.IsNullOrWhiteSpace(existingInPkg) ? (Guid.NewGuid().ToString(), true) : (existingInPkg, false);
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

		private static string FindRootSchemaUId(IReadOnlyList<PageDesignerHierarchySchema> hierarchy, string schemaName) {
			for (int i = hierarchy.Count - 1; i >= 0; i--) {
				if (string.Equals(hierarchy[i].Name, schemaName, StringComparison.OrdinalIgnoreCase)) {
					return hierarchy[i].UId;
				}
			}
			return null;
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

		private bool TrySaveSchema(JObject schemaToSave, out PageUpdateResponse response) {
			string saveUrl = _serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema");
			string saveJson = _applicationClient.ExecutePostRequest(saveUrl, schemaToSave.ToString(Formatting.None));
			var saveResponse = JObject.Parse(saveJson);
			if (saveResponse["success"]?.Value<bool>() ?? false) {
				TryResetScriptCache();
				response = null;
				return true;
			}
			response = new PageUpdateResponse {
				Success = false,
				Error = BuildSaveErrorMessage(saveResponse)
			};
			return false;
		}

		/// <summary>
		/// Mirrors the frontend PageDesigner post-save behaviour by invalidating the workplace
		/// script cache via <c>/rest/WorkplaceService/ResetScriptCache</c>. Without this call,
		/// Creatio serves stale bundle JSON after a schema save — runtime pages omit fresh
		/// replacing schemas and the next <c>GetParentSchemas</c> returns the pre-save hierarchy,
		/// which tricks subsequent update-page calls into the CREATE branch and spawns duplicate
		/// replacing schemas in the design package.
		/// </summary>
		private void TryResetScriptCache() {
			try {
				string resetUrl = _serviceUrlBuilder.Build("/rest/WorkplaceService/ResetScriptCache");
				_applicationClient.ExecutePostRequest(resetUrl, string.Empty);
			} catch {
				// Cache reset is best-effort; never block a successful save on it.
			}
		}

		private static string BuildSaveErrorMessage(JObject saveResponse) =>
			AppendActionableHint(PageSchemaMetadataHelper.ParseSaveErrorMessage(saveResponse, "Failed to save page schema"));

		private static string AppendActionableHint(string serverError) {
			if (string.IsNullOrEmpty(serverError)) {
				return serverError;
			}
			if (serverError.Contains("requires an element of type 'Object'", StringComparison.OrdinalIgnoreCase) &&
				serverError.Contains("type 'Array'", StringComparison.OrdinalIgnoreCase)) {
				return serverError + " [hint: this typically happens when re-sending the full get-page raw.body — " +
					"backend re-applies existing merges that now conflict with parent hierarchy. " +
					"Send only NEW viewConfigDiff/handlers operations (the new component insert + matching handler), " +
					"not the entire inherited body. See docs://mcp/guides/page-modification for the minimal-diff pattern.]";
			}
			if (serverError.Contains("Item with name", StringComparison.OrdinalIgnoreCase) &&
				serverError.Contains("not found", StringComparison.OrdinalIgnoreCase)) {
				return serverError + " [hint: the schema manager cache may be holding a stale phantom replacing schema " +
					"from an earlier failed save. Restart Creatio to clear the cache, or verify the schema UId via list-pages.]";
			}
			if (serverError.Contains("third-party publisher", StringComparison.OrdinalIgnoreCase) ||
				serverError.Contains("installed from the file archive", StringComparison.OrdinalIgnoreCase)) {
				return serverError + " [hint: the schema is owned by a package whose maintainer differs from the " +
					"current workspace maintainer, so Creatio blocks direct in-place edits. " +
					"Fix by saving into a replacing schema in your design package: call update-page with " +
					"mode=append (auto-detects design package and creates a replacement there) or pass " +
					"target-package-uid explicitly to the app's design package. See " +
					"docs://mcp/guides/page-modification section 'multi-app replacements'.]";
			}
			return serverError;
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
			bool isAppendModeValidation = string.Equals(options.Mode, "append", StringComparison.OrdinalIgnoreCase);
			if (!isAppendModeValidation) {
				var integrityResult = SchemaValidationService.ValidateMarkerIntegrity(options.Body);
				if (!integrityResult.IsValid) {
					return new PageUpdateResponse {
						Success = false,
						Error = $"Body is missing required marker pairs: {string.Join("; ", integrityResult.Errors)}"
					};
				}
			}
			var syntaxResult = SchemaValidationService.ValidateJsSyntax(options.Body);
			if (!syntaxResult.IsValid) {
				return new PageUpdateResponse {
					Success = false,
					Error = $"Body contains invalid JavaScript syntax: {string.Join("; ", syntaxResult.Errors)}"
				};
			}
			var handlerResult = SchemaValidationService.ValidateHandlerStructure(options.Body);
			if (!handlerResult.IsValid) {
				return new PageUpdateResponse {
					Success = false,
					Error = $"Body contains invalid handlers: {string.Join("; ", handlerResult.Errors)}"
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
			var validatorPlacementResult = SchemaValidationService.ValidateValidatorBindingPlacement(options.Body);
			if (!validatorPlacementResult.IsValid) {
				return new PageUpdateResponse {
					Success = false,
					Error = $"Body contains invalid validator bindings: {string.Join("; ", validatorPlacementResult.Errors)}"
				};
			}
			return null;
		}
	}
}
