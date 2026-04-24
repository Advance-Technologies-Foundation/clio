namespace Clio.Command;

using System;
using System.Linq;
using System.Text.Json;
using Clio.Common;
using CommandLine;

/// <summary>
/// Options for the <c>get-page</c> command.
/// </summary>
[Verb("get-page", Aliases = ["page-get"], HelpText = "Get a Freedom UI page bundle and raw schema body")]
public class PageGetOptions : EnvironmentOptions {
	/// <summary>
	/// Gets or sets the schema name to fetch.
	/// </summary>
	[Option("schema-name", Required = true, HelpText = "Schema name to fetch")]
	public string SchemaName { get; set; }
}

/// <summary>
/// Reads Freedom UI pages as merged bundle-first envelopes.
/// </summary>
public class PageGetCommand : Command<PageGetOptions> {
	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;
	private readonly IPageDesignerHierarchyClient _hierarchyClient;
	private readonly IPageSchemaBodyParser _bodyParser;
	private readonly IPageBundleBuilder _bundleBuilder;

	/// <summary>
	/// Initializes a new instance of the <see cref="PageGetCommand"/> class.
	/// </summary>
	/// <param name="applicationClient">Remote Creatio client.</param>
	/// <param name="serviceUrlBuilder">Service URL builder.</param>
	/// <param name="logger">Logger used for CLI output.</param>
	/// <param name="hierarchyClient">Hierarchy client for designer schemas.</param>
	/// <param name="bodyParser">Parser for schema body markers.</param>
	/// <param name="bundleBuilder">Bundle builder that mirrors frontend merge logic.</param>
	public PageGetCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger,
		IPageDesignerHierarchyClient hierarchyClient,
		IPageSchemaBodyParser bodyParser,
		IPageBundleBuilder bundleBuilder) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
		_hierarchyClient = hierarchyClient;
		_bodyParser = bodyParser;
		_bundleBuilder = bundleBuilder;
	}

	/// <summary>
	/// Attempts to read the requested page as a merged bundle plus raw body envelope.
	/// </summary>
	/// <param name="options">Command options.</param>
	/// <param name="response">Response envelope.</param>
	/// <returns><c>true</c> when the page was read successfully; otherwise <c>false</c>.</returns>
	public bool TryGetPage(PageGetOptions options, out PageGetResponse response) {
		if (string.IsNullOrWhiteSpace(options.SchemaName)) {
			response = new PageGetResponse {
				Success = false,
				Error = "schemaName is required"
			};
			return false;
		}
		try {
			var (metadata, error) = PageSchemaMetadataHelper.QuerySysSchemaRow(
				_applicationClient,
				_serviceUrlBuilder,
				options.SchemaName,
				("Name", "Name"),
				("UId", "UId"),
				("PackageName", "SysPackage.Name"),
				("PackageUId", "SysPackage.UId"),
				("ParentSchemaName", "[SysSchema:Id:Parent].Name"));
			if (metadata is null) {
				response = new PageGetResponse {
					Success = false,
					Error = error
				};
				return false;
			}

			string schemaUId = metadata["UId"]?.ToString();
			string packageUId = metadata["PackageUId"]?.ToString();
			if (string.IsNullOrWhiteSpace(schemaUId) || string.IsNullOrWhiteSpace(packageUId)) {
				response = new PageGetResponse {
					Success = false,
					Error = $"Schema '{options.SchemaName}' metadata is missing package or schema identifiers"
				};
				return false;
			}

			string designPackageUId = null;
			try {
				designPackageUId = _hierarchyClient.GetDesignPackageUId(schemaUId);
			} catch {
				designPackageUId = null;
			}
			if (string.IsNullOrWhiteSpace(designPackageUId)) {
				designPackageUId = packageUId;
			}
			var initialHierarchy = _hierarchyClient.GetParentSchemas(schemaUId, designPackageUId);
			if (initialHierarchy.Count == 0) {
				response = new PageGetResponse {
					Success = false,
					Error = $"Schema '{options.SchemaName}' hierarchy is empty"
				};
				return false;
			}

			string rootSchemaUId = FindRootSchemaUId(initialHierarchy, options.SchemaName) ?? schemaUId;
			System.Collections.Generic.IReadOnlyList<PageDesignerHierarchySchema> hierarchy;
			if (!string.Equals(rootSchemaUId, schemaUId, StringComparison.OrdinalIgnoreCase)) {
				var fullHierarchy = _hierarchyClient.GetParentSchemas(rootSchemaUId, designPackageUId);
				hierarchy = fullHierarchy.Count > 0 ? fullHierarchy : initialHierarchy;
			} else {
				hierarchy = initialHierarchy;
			}

			PageDesignerHierarchySchema currentSchema = hierarchy[0];

			var parts = hierarchy
				.Where(schema => schema.Body != null)
				.Select(schema => new PageSchemaBundlePart(schema, _bodyParser.Parse(schema.Body)))
				.ToList();
			PageBundleInfo bundle = _bundleBuilder.Build(parts);
			var schemaChain = hierarchy
				.Select(s => new PageSchemaChainEntry {
					SchemaUId = s.UId,
					SchemaName = s.Name,
					PackageUId = s.PackageUId,
					PackageName = s.PackageName,
					HasBody = s.Body != null
				})
				.ToList();
			string designPackageName = PageSchemaMetadataHelper.QueryPackageName(
				_applicationClient, _serviceUrlBuilder, designPackageUId);
			PageDesignerHierarchySchema editableSchema = hierarchy.FirstOrDefault(
				s => string.Equals(s.PackageUId, designPackageUId, StringComparison.OrdinalIgnoreCase));
			if (editableSchema is null && !string.IsNullOrWhiteSpace(designPackageUId)) {
				editableSchema = LoadReplacingSchemaInDesignPackage(options.SchemaName, designPackageUId);
			}
			bool willCreateReplacing = editableSchema is null
				&& !string.IsNullOrWhiteSpace(designPackageUId)
				&& !string.Equals(designPackageUId, currentSchema.PackageUId, StringComparison.OrdinalIgnoreCase);
			string editableBody = editableSchema?.Body ?? BuildEmptyBody(options.SchemaName);
			PageOwnBodySummary ownBodySummary = BuildOwnBodySummary(editableSchema ?? currentSchema, _bodyParser);
			response = new PageGetResponse {
				Success = true,
				Page = new PageMetadataInfo {
					SchemaName = currentSchema.Name,
					SchemaUId = currentSchema.UId,
					PackageName = currentSchema.PackageName,
					PackageUId = currentSchema.PackageUId,
					ParentSchemaName = metadata["ParentSchemaName"]?.ToString(),
					OwnBodySummary = ownBodySummary,
					DesignPackageUId = designPackageUId,
					DesignPackageName = designPackageName,
					RootSchemaUId = rootSchemaUId,
					WillCreateReplacingInDesignPackage = willCreateReplacing
				},
				Bundle = new PageBundleInfo {
					Name = bundle.Name,
					ViewConfig = bundle.ViewConfig,
					ViewModelConfig = bundle.ViewModelConfig,
					ModelConfig = bundle.ModelConfig,
					Resources = bundle.Resources,
					Handlers = bundle.Handlers,
					Converters = bundle.Converters,
					Validators = bundle.Validators,
					Parameters = bundle.Parameters,
					Deps = bundle.Deps,
					Args = bundle.Args,
					OptionalProperties = bundle.OptionalProperties,
					Containers = bundle.Containers,
					Schemas = schemaChain
				},
				Raw = new PageRawInfo {
					Body = editableBody
				},
				Error = null
			};
			return true;
		}
		catch (Exception ex) {
			response = new PageGetResponse {
				Success = false,
				Error = ex.Message
			};
			return false;
		}
	}

	/// <inheritdoc />
	public override int Execute(PageGetOptions options) {
		bool success = TryGetPage(options, out PageGetResponse response);
		_logger.WriteInfo(JsonSerializer.Serialize(response));
		return success ? 0 : 1;
	}

	private PageDesignerHierarchySchema LoadReplacingSchemaInDesignPackage(string schemaName, string designPackageUId) {
		string replacingUId = PageSchemaMetadataHelper.FindExistingSchemaInPackage(
			_applicationClient, _serviceUrlBuilder, schemaName, designPackageUId);
		if (string.IsNullOrWhiteSpace(replacingUId)) {
			return null;
		}
		try {
			System.Collections.Generic.IReadOnlyList<PageDesignerHierarchySchema> replacingHierarchy =
				_hierarchyClient.GetParentSchemas(replacingUId, designPackageUId);
			return replacingHierarchy.FirstOrDefault(
				s => string.Equals(s.UId, replacingUId, StringComparison.OrdinalIgnoreCase));
		} catch {
			return null;
		}
	}

	private static string FindRootSchemaUId(System.Collections.Generic.IReadOnlyList<PageDesignerHierarchySchema> hierarchy, string schemaName) {
		for (int i = hierarchy.Count - 1; i >= 0; i--) {
			if (string.Equals(hierarchy[i].Name, schemaName, StringComparison.OrdinalIgnoreCase)) {
				return hierarchy[i].UId;
			}
		}
		return null;
	}

	private static string BuildEmptyBody(string schemaName) {
		return "define(\"" + schemaName + "\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {\n\treturn {\n\t\tviewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/,\n\t\tviewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,\n\t\tmodelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,\n\t\thandlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,\n\t\tconverters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,\n\t\tvalidators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/\n\t};\n});";
	}

	private static PageOwnBodySummary BuildOwnBodySummary(PageDesignerHierarchySchema schema, IPageSchemaBodyParser parser) {
		if (schema == null || string.IsNullOrWhiteSpace(schema.Body))
			return new PageOwnBodySummary { BodyLength = 0 };
		PageParsedSchemaBody parsed = parser.Parse(schema.Body);
		string handlers = parsed.Handlers?.Trim();
		(int handlerCount, var handlerRequests) = ExtractHandlerInfo(handlers);
		var ops = ExtractViewConfigOps(parsed);
		return new PageOwnBodySummary {
			BodyLength = schema.Body.Length,
			ViewConfigDiffOperations = (parsed.ViewConfigDiff as Newtonsoft.Json.Linq.JArray)?.Count ?? 0,
			ViewModelConfigDiffOperations = (parsed.ViewModelConfigDiff as Newtonsoft.Json.Linq.JArray)?.Count ?? 0,
			ModelConfigDiffOperations = (parsed.ModelConfigDiff as Newtonsoft.Json.Linq.JArray)?.Count ?? 0,
			HandlerEntries = handlerCount,
			ViewConfigDiffOps = ops,
			HandlerRequests = handlerRequests
		};
	}

	private static (int count, System.Collections.Generic.List<string> requests) ExtractHandlerInfo(string handlers) {
		int count = 0;
		var requests = new System.Collections.Generic.List<string>();
		if (string.IsNullOrEmpty(handlers) || handlers == "[]")
			return (count, requests);
		int depth = 0;
		foreach (char ch in handlers) {
			if (ch == '{') {
				if (depth == 0) count++;
				depth++;
			} else if (ch == '}') {
				depth--;
			}
		}
		var requestRegex = new System.Text.RegularExpressions.Regex(
			@"request\s*:\s*[""']([^""']+)[""']",
			System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled,
			System.TimeSpan.FromSeconds(1));
		foreach (System.Text.RegularExpressions.Match m in requestRegex.Matches(handlers))
			requests.Add(m.Groups[1].Value);
		return (count, requests);
	}

	private static System.Collections.Generic.List<PageOperationInfo> ExtractViewConfigOps(PageParsedSchemaBody parsed) {
		var ops = new System.Collections.Generic.List<PageOperationInfo>();
		if (parsed.ViewConfigDiff is not Newtonsoft.Json.Linq.JArray viewDiff)
			return ops;
		foreach (Newtonsoft.Json.Linq.JToken item in viewDiff) {
			if (item is not Newtonsoft.Json.Linq.JObject obj) continue;
			ops.Add(new PageOperationInfo {
				Operation = obj["operation"]?.ToString(),
				Name = obj["name"]?.ToString(),
				Type = obj["values"]?["type"]?.ToString(),
				ParentName = obj["parentName"]?.ToString()
			});
		}
		return ops;
	}
}
