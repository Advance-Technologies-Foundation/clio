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
			var hierarchy = _hierarchyClient.GetParentSchemas(schemaUId, designPackageUId);
			if (hierarchy.Count == 0) {
				response = new PageGetResponse {
					Success = false,
					Error = $"Schema '{options.SchemaName}' hierarchy is empty"
				};
				return false;
			}

			PageDesignerHierarchySchema currentSchema = hierarchy[0];

			var parts = hierarchy
				.Where(schema => !string.IsNullOrWhiteSpace(schema.Body))
				.Select(schema => new PageSchemaBundlePart(schema, _bodyParser.Parse(schema.Body)))
				.ToList();
			PageBundleInfo bundle = _bundleBuilder.Build(parts);
			PageOwnBodySummary ownBodySummary = BuildOwnBodySummary(currentSchema, _bodyParser);
			response = new PageGetResponse {
				Success = true,
				Page = new PageMetadataInfo {
					SchemaName = currentSchema.Name,
					SchemaUId = currentSchema.UId,
					PackageName = currentSchema.PackageName,
					PackageUId = currentSchema.PackageUId,
					ParentSchemaName = metadata["ParentSchemaName"]?.ToString(),
					OwnBodySummary = ownBodySummary
				},
				Bundle = bundle,
				Raw = new PageRawInfo {
					Body = currentSchema.Body
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

	private static PageOwnBodySummary BuildOwnBodySummary(PageDesignerHierarchySchema schema, IPageSchemaBodyParser parser) {
		if (schema == null || string.IsNullOrWhiteSpace(schema.Body)) {
			return new PageOwnBodySummary { BodyLength = 0 };
		}
		PageParsedSchemaBody parsed = parser.Parse(schema.Body);
		int handlerCount = 0;
		var handlerRequests = new System.Collections.Generic.List<string>();
		string handlers = parsed.Handlers?.Trim();
		if (!string.IsNullOrEmpty(handlers) && handlers != "[]") {
			int depth = 0;
			foreach (char ch in handlers) {
				if (ch == '{') {
					if (depth == 0) handlerCount++;
					depth++;
				} else if (ch == '}') {
					depth--;
				}
			}
			var requestRegex = new System.Text.RegularExpressions.Regex(
				@"request\s*:\s*[""']([^""']+)[""']",
				System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);
			foreach (System.Text.RegularExpressions.Match m in requestRegex.Matches(handlers)) {
				handlerRequests.Add(m.Groups[1].Value);
			}
		}
		var ops = new System.Collections.Generic.List<PageOperationInfo>();
		if (parsed.ViewConfigDiff is Newtonsoft.Json.Linq.JArray viewDiff) {
			foreach (Newtonsoft.Json.Linq.JToken item in viewDiff) {
				if (item is not Newtonsoft.Json.Linq.JObject obj) {
					continue;
				}
				ops.Add(new PageOperationInfo {
					Operation = obj["operation"]?.ToString(),
					Name = obj["name"]?.ToString(),
					Type = obj["values"]?["type"]?.ToString(),
					ParentName = obj["parentName"]?.ToString()
				});
			}
		}
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
}
