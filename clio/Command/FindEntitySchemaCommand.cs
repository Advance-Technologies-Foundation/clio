using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
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

	[Option("search-pattern", Required = false, HelpText = "Case-insensitive substring to search in entity schema names")]
	public string? SearchPattern { get; set; }

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
/// Finds entity schemas in a Creatio environment using a single DataService query on SysSchema.
/// Accepts exact name, case-insensitive substring pattern, or UId as search criteria.
/// </summary>
public sealed class FindEntitySchemaCommand : Command<FindEntitySchemaOptions>
{
	private const string EntitySchemaManagerName = "EntitySchemaManager";
	private const int ContainsComparisonType = 10;

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
					$"{result.SchemaName} | {result.PackageName} ({result.PackageMaintainer}){parent}");
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
	public IReadOnlyList<EntitySchemaSearchResult> FindSchemas(FindEntitySchemaOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		Validate(options);
		object query = BuildFindSchemasQuery(options);
		FindSchemasResponse response = ExecuteSelectQuery<FindSchemasResponse>(
			_applicationClient,
			_serviceUrlBuilder,
			query);
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

	private static object BuildFindSchemasQuery(FindEntitySchemaOptions options) {
		List<SelectQueryFilterDefinition> filters =
		[
			new("ManagerName", EntitySchemaManagerName, TextDataValueType)
		];
		if (!string.IsNullOrWhiteSpace(options.SchemaName)) {
			filters.Add(new("Name", options.SchemaName.Trim(), TextDataValueType));
		}
		if (!string.IsNullOrWhiteSpace(options.SearchPattern)) {
			filters.Add(new("Name", options.SearchPattern.Trim(), TextDataValueType,
				ContainsComparisonType));
		}
		if (!string.IsNullOrWhiteSpace(options.Uid)) {
			filters.Add(new("UId", options.Uid.Trim(), GuidDataValueType));
		}
		return BuildSelectQuery("SysSchema", SchemaColumns, filters);
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
