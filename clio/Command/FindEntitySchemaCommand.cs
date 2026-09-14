using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using Clio.Common;
using Clio.Package;
using CommandLine;
using static Clio.Package.SelectQueryHelper;

namespace Clio.Command;

/// <summary>
/// CLI options for finding remote entity schemas by name, name pattern, or UId.
/// </summary>
[Verb("find-entity-schema", HelpText = "Find entity schemas in a Creatio environment by name, pattern, or UId")]
public class FindEntitySchemaOptions : RemoteCommandOptions
{
	[Option("schema-name", Required = false, HelpText = "Exact entity schema name to find")]
	public string? SchemaName { get; set; }

	[Option("name", Required = false, Hidden = true, HelpText = "Alias for --schema-name")]
	public string? SchemaNameAlias {
		get => SchemaName;
		set { if (!string.IsNullOrEmpty(value)) SchemaName = value; }
	}

	[Option("search-pattern", Required = false, HelpText = "Case-insensitive substring to search in entity schema names")]
	public string? SearchPattern { get; set; }

	[Option("pattern", Required = false, Hidden = true, HelpText = "Alias for --search-pattern")]
	public string? SearchPatternAlias {
		get => SearchPattern;
		set { if (!string.IsNullOrEmpty(value)) SearchPattern = value; }
	}

	[Option("uid", Required = false, HelpText = "Entity schema UId (Guid) to find")]
	public string? Uid { get; set; }
}

/// <summary>
/// Structured result item returned by <see cref="FindEntitySchemaCommand"/>.
/// </summary>
public sealed record EntitySchemaSearchResult(
	[property: JsonPropertyName("schema-name")] string SchemaName,
	[property: JsonPropertyName("package-name")] string PackageName,
	[property: JsonPropertyName("package-maintainer")] string PackageMaintainer,
	[property: JsonPropertyName("parent-schema-name")] string? ParentSchemaName
);

/// <summary>
/// Finds entity schemas in a Creatio environment using DataService queries on SysSchema.
/// Accepts exact name, case-insensitive substring pattern, or UId as search criteria. An empty
/// substring result is cross-checked once with a broader query before absence is reported.
/// </summary>
public class FindEntitySchemaCommand : Command<FindEntitySchemaOptions>
{
	private const string EntitySchemaManagerName = "EntitySchemaManager";
	private const int ContainsComparisonType = 11;
	private const int EmptyFilterFallbackRowCount = 10000;

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	private static readonly IReadOnlyList<SelectQueryColumnDefinition> SchemaColumns =
	[
		new("Name", "Name"),
		new("UId", "UId"),
		new("SysPackage.Name", "PackageName"),
		new("SysPackage.Maintainer", "PackageMaintainer"),
		new("[SysSchema:Id:Parent].Name", "ParentSchemaName")
	];

	public FindEntitySchemaCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	/// <inheritdoc/>
	public override int Execute(FindEntitySchemaOptions options) {
		try {
			IReadOnlyList<EntitySchemaSearchResult> results = FindSchemas(options);
			if (results.Count == 0) {
				_logger.WriteInfo("No entity schemas found.");
				return 0;
			}
			foreach (EntitySchemaSearchResult result in results) {
				string parent = string.IsNullOrWhiteSpace(result.ParentSchemaName)
					? string.Empty
					: $" | Parent: {result.ParentSchemaName}";
				_logger.WriteInfo(
					$"Schema: {result.SchemaName} | Package: {result.PackageName} | Maintainer: {result.PackageMaintainer}{parent}");
			}
			return 0;
		} catch (Exception exception) {
			_logger.WriteError(exception.Message);
			return 1;
		}
	}

	/// <summary>
	/// Queries <c>SysSchema</c> on the remote environment and returns matching entity schema records.
	/// </summary>
	/// <param name="options">Search criteria and environment settings.</param>
	/// <returns>Read-only list of matching entity schema search results.</returns>
	public virtual IReadOnlyList<EntitySchemaSearchResult> FindSchemas(FindEntitySchemaOptions options) =>
		FindSchemas(options, Timeout.Infinite);

	/// <summary>
	/// Queries <c>SysSchema</c> on the remote environment with an explicit per-request timeout.
	/// </summary>
	/// <remarks>
	/// The bounded overload exists for callers that run this search inside an already-failing operation - the
	/// entity-schema designer builds its error message from it. There the default
	/// <see cref="Timeout.Infinite"/> is the wrong contract: an environment that accepts the connection and
	/// then stops answering would block the caller indefinitely inside a diagnostic, which in a long-lived
	/// MCP server holds the tenant open with no way back. The interactive <c>find-entity-schema</c> command
	/// keeps the unbounded behavior, so its transient re-send budget is unchanged.
	/// </remarks>
	/// <param name="options">Search criteria and environment settings.</param>
	/// <param name="requestTimeoutMs">
	/// Per-request timeout in milliseconds, or <see cref="Timeout.Infinite"/> for no bound.
	/// </param>
	/// <returns>Read-only list of matching entity schema search results.</returns>
	public virtual IReadOnlyList<EntitySchemaSearchResult> FindSchemas(FindEntitySchemaOptions options,
		int requestTimeoutMs) {
		ArgumentNullException.ThrowIfNull(options);
		Validate(options);
		IReadOnlyList<EntitySchemaSearchResult> results = ExecuteFindSchemasQuery(
			BuildFindSchemasQuery(options, includeSearchPattern: true), requestTimeoutMs);
		if (results.Count == 0 && !string.IsNullOrWhiteSpace(options.SearchPattern)) {
			// Issue #1213: a just-created schema was visible by exact identity but not by the
			// server-side contains filter. Cross-check only an empty pattern result and apply the
			// advertised ordinal-ignore-case substring comparison locally.
			string searchPattern = options.SearchPattern.Trim();
			IReadOnlyList<EntitySchemaSearchResult> broaderResults = ExecuteFindSchemasQuery(
				BuildFindSchemasQuery(options, includeSearchPattern: false), requestTimeoutMs);
			results = broaderResults
				.Where(result => result.SchemaName.Contains(searchPattern, StringComparison.OrdinalIgnoreCase))
				.ToList();
			if (broaderResults.Count >= EmptyFilterFallbackRowCount) {
				throw new InvalidOperationException(
					$"Complete results for search pattern '{searchPattern}' could not be confirmed because the broader verification query reached its {EmptyFilterFallbackRowCount}-row safety bound. Use --schema-name or --uid for an exact lookup.");
			}
		}
		return results;
	}

	private IReadOnlyList<EntitySchemaSearchResult> ExecuteFindSchemasQuery(object query, int requestTimeoutMs) {
		FindSchemasResponse response = ExecuteSelectQuery<FindSchemasResponse>(
			_applicationClient,
			_serviceUrlBuilder,
			query,
			requestTimeoutMs);
		return response.Rows
			.Select(row => new EntitySchemaSearchResult(
				row.Name ?? string.Empty,
				row.PackageName ?? string.Empty,
				row.PackageMaintainer ?? string.Empty,
				string.IsNullOrWhiteSpace(row.ParentSchemaName) ? null : row.ParentSchemaName))
			.ToList();
	}

	private static void Validate(FindEntitySchemaOptions options) {
		bool hasSchemaName = !string.IsNullOrWhiteSpace(options.SchemaName);
		bool hasSearchPattern = !string.IsNullOrWhiteSpace(options.SearchPattern);
		bool hasUid = !string.IsNullOrWhiteSpace(options.Uid);
		if (!hasSchemaName && !hasSearchPattern && !hasUid) {
			throw new ArgumentException(
				"At least one of --schema-name, --search-pattern, or --uid is required.");
		}
		if (!string.IsNullOrWhiteSpace(options.Uid)
			&& !Guid.TryParse(options.Uid, out _)) {
			throw new ArgumentException($"'--uid' value '{options.Uid}' is not a valid Guid.");
		}
	}

	private static object BuildFindSchemasQuery(
		FindEntitySchemaOptions options,
		bool includeSearchPattern) {
		List<SelectQueryFilterDefinition> filters =
		[
			new("ManagerName", EntitySchemaManagerName, TextDataValueType)
		];
		if (!string.IsNullOrWhiteSpace(options.SchemaName)) {
			filters.Add(new("Name", options.SchemaName.Trim(), TextDataValueType));
		}
		if (includeSearchPattern && !string.IsNullOrWhiteSpace(options.SearchPattern)) {
			filters.Add(new("Name", options.SearchPattern.Trim(), TextDataValueType,
				ContainsComparisonType));
		}
		if (!string.IsNullOrWhiteSpace(options.Uid)) {
			filters.Add(new("UId", options.Uid.Trim(), GuidDataValueType));
		}
		return BuildSelectQuery("SysSchema", SchemaColumns, filters, EmptyFilterFallbackRowCount);
	}

	private sealed class FindSchemasResponse : SelectQueryResponseBaseDto
	{
		[System.Text.Json.Serialization.JsonPropertyName("rows")]
		public List<FindSchemasRowDto> Rows { get; set; } = [];
	}

	private sealed class FindSchemasRowDto
	{
		[System.Text.Json.Serialization.JsonPropertyName("Name")]
		public string? Name { get; set; }

		[System.Text.Json.Serialization.JsonPropertyName("UId")]
		public string? UId { get; set; }

		[System.Text.Json.Serialization.JsonPropertyName("PackageName")]
		public string? PackageName { get; set; }

		[System.Text.Json.Serialization.JsonPropertyName("PackageMaintainer")]
		public string? PackageMaintainer { get; set; }

		[System.Text.Json.Serialization.JsonPropertyName("ParentSchemaName")]
		public string? ParentSchemaName { get; set; }
	}
}
