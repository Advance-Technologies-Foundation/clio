using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Package;
using CommandLine;
using static Clio.Package.SelectQueryHelper;

namespace Clio.Command;

/// <summary>
/// CLI options for finding installed applications (with their sections) by name, code, or pattern.
/// </summary>
[Verb("find-app", HelpText = "Find installed applications and their sections in a Creatio environment by name, code, or pattern")]
public class FindAppOptions : RemoteCommandOptions {
	[Option("search-pattern", Required = false,
		HelpText = "Case-insensitive substring matched against application name, code, description, and section captions/codes")]
	public string? SearchPattern { get; set; }

	[Option("pattern", Required = false, Hidden = true, HelpText = "Alias for --search-pattern")]
	public string? SearchPatternAlias {
		get => SearchPattern;
		set { if (!string.IsNullOrEmpty(value)) SearchPattern = value; }
	}

	[Option("code", Required = false, HelpText = "Exact installed application code to match")]
	public string? Code { get; set; }

	[Option("json", Required = false, Default = false, HelpText = "Output as indented JSON instead of a table")]
	public bool JsonFormat { get; set; }
}

/// <summary>
/// Structured application search result returned by <see cref="FindAppCommand"/>, carrying the
/// installed application identity together with all of its sections in a single payload.
/// </summary>
/// <param name="Id">Installed application identifier.</param>
/// <param name="Code">Installed application code (use directly for follow-up MCP calls).</param>
/// <param name="Name">Installed application display name.</param>
/// <param name="Version">Installed application version, or null when not set.</param>
/// <param name="Description">Installed application description, or null when not set.</param>
/// <param name="Sections">Sections that belong to the application.</param>
public sealed record AppSearchResult(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("version")] string? Version,
	[property: JsonPropertyName("description")] string? Description,
	[property: JsonPropertyName("sections")] IReadOnlyList<AppSectionSearchResult> Sections);

/// <summary>
/// Structured section item returned as part of an <see cref="AppSearchResult"/>.
/// </summary>
/// <param name="Code">Section code.</param>
/// <param name="Caption">Section caption.</param>
/// <param name="EntitySchemaName">Bound entity schema name, or null when not set.</param>
/// <param name="Description">Section description, or null when not set.</param>
public sealed record AppSectionSearchResult(
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("caption")] string Caption,
	[property: JsonPropertyName("entity-schema-name")] string? EntitySchemaName,
	[property: JsonPropertyName("description")] string? Description);

/// <summary>
/// Finds installed applications and their sections in a single environment round-trip.
/// Issues exactly two DataService SelectQuery calls — all <c>SysInstalledApp</c> rows and all
/// <c>ApplicationSection</c> rows — joins them in memory by application id, and filters by an
/// optional case-insensitive pattern (matched across application name/code/description and section
/// captions/codes) and/or an exact application code. An empty pattern returns every application
/// (with its sections), removing the N+1 list-apps + per-app list-app-sections round-trips an agent
/// would otherwise make to map an imprecise application name to its code.
/// </summary>
public class FindAppCommand : Command<FindAppOptions> {
	private const string DescriptionColumn = "Description";

	private static readonly IReadOnlyList<SelectQueryColumnDefinition> AppColumns =
	[
		new("Id", "Id"),
		new("Code", "Code"),
		new("Name", "Name"),
		new(DescriptionColumn, DescriptionColumn),
		new("Version", "Version")
	];

	private static readonly IReadOnlyList<SelectQueryColumnDefinition> SectionColumns =
	[
		new("Id", "Id"),
		new("ApplicationId", "ApplicationId"),
		new("Caption", "Caption"),
		new("Code", "Code"),
		new(DescriptionColumn, DescriptionColumn),
		new("EntitySchemaName", "EntitySchemaName")
	];

	private static readonly IReadOnlyList<SelectQueryFilterDefinition> NoFilters = [];

	private static readonly JsonSerializerOptions WriteIndentedOptions = new() {
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	public FindAppCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	/// <inheritdoc/>
	public override int Execute(FindAppOptions options) {
		try {
			IReadOnlyList<AppSearchResult> results = FindApplications(options);
			if (options.JsonFormat) {
				_logger.WriteInfo(JsonSerializer.Serialize(results, WriteIndentedOptions));
				return 0;
			}

			if (results.Count == 0) {
				_logger.WriteInfo("No applications found.");
				return 0;
			}

			foreach (AppSearchResult app in results) {
				string version = string.IsNullOrWhiteSpace(app.Version) ? string.Empty : $" v{app.Version}";
				_logger.WriteInfo($"App: {app.Name} ({app.Code}){version} | Sections: {app.Sections.Count}");
				foreach (AppSectionSearchResult section in app.Sections) {
					string entity = string.IsNullOrWhiteSpace(section.EntitySchemaName)
						? string.Empty
						: $" -> {section.EntitySchemaName}";
					_logger.WriteInfo($"  - {section.Caption} ({section.Code}){entity}");
				}
			}
			return 0;
		} catch (Exception exception) {
			_logger.WriteError(exception.Message);
			return 1;
		}
	}

	/// <summary>
	/// Queries the remote environment for installed applications and their sections, then returns the
	/// applications that match the supplied search criteria together with their sections.
	/// </summary>
	/// <param name="options">Search criteria and environment settings.</param>
	/// <returns>Read-only list of matching applications, each carrying its sections.</returns>
	public virtual IReadOnlyList<AppSearchResult> FindApplications(FindAppOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		string? pattern = string.IsNullOrWhiteSpace(options.SearchPattern) ? null : options.SearchPattern.Trim();
		string? code = string.IsNullOrWhiteSpace(options.Code) ? null : options.Code.Trim();

		InstalledAppsResponse appsResponse = ExecuteSelectQuery<InstalledAppsResponse>(
			_applicationClient,
			_serviceUrlBuilder,
			BuildSelectQuery("SysInstalledApp", AppColumns, NoFilters));
		SectionsResponse sectionsResponse = ExecuteSelectQuery<SectionsResponse>(
			_applicationClient,
			_serviceUrlBuilder,
			BuildSelectQuery("ApplicationSection", SectionColumns, NoFilters));

		ILookup<string, AppSectionSearchResult> sectionsByApp = sectionsResponse.Rows
			.Where(section => !string.IsNullOrWhiteSpace(section.ApplicationId))
			.ToLookup(
				section => section.ApplicationId,
				section => new AppSectionSearchResult(
					section.Code ?? string.Empty,
					section.Caption ?? string.Empty,
					string.IsNullOrWhiteSpace(section.EntitySchemaName) ? null : section.EntitySchemaName,
					string.IsNullOrWhiteSpace(section.Description) ? null : section.Description),
				StringComparer.OrdinalIgnoreCase);

		return appsResponse.Rows
			.Select(app => new AppSearchResult(
				app.Id ?? string.Empty,
				app.Code ?? string.Empty,
				app.Name ?? string.Empty,
				string.IsNullOrWhiteSpace(app.Version) ? null : app.Version,
				string.IsNullOrWhiteSpace(app.Description) ? null : app.Description,
				sectionsByApp[app.Id ?? string.Empty]
					.OrderBy(section => section.Caption, StringComparer.OrdinalIgnoreCase)
					.ThenBy(section => section.Code, StringComparer.OrdinalIgnoreCase)
					.ToList()))
			.Where(app => MatchesCode(app, code) && MatchesPattern(app, pattern))
			.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
			.ThenBy(app => app.Code, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static bool MatchesCode(AppSearchResult app, string? code) =>
		code is null || string.Equals(app.Code, code, StringComparison.OrdinalIgnoreCase);

	private static bool MatchesPattern(AppSearchResult app, string? pattern) {
		if (pattern is null) {
			return true;
		}

		return Contains(app.Name, pattern)
			|| Contains(app.Code, pattern)
			|| Contains(app.Description, pattern)
			|| app.Sections.Any(section => Contains(section.Caption, pattern) || Contains(section.Code, pattern));
	}

	private static bool Contains(string? value, string pattern) =>
		!string.IsNullOrEmpty(value) && value.Contains(pattern, StringComparison.OrdinalIgnoreCase);

	private sealed class InstalledAppsResponse : SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")] public List<InstalledAppRowDto> Rows { get; set; } = [];
	}

	private sealed class InstalledAppRowDto {
		[JsonPropertyName("Id")] public string? Id { get; set; }
		[JsonPropertyName("Code")] public string? Code { get; set; }
		[JsonPropertyName("Name")] public string? Name { get; set; }
		[JsonPropertyName("Description")] public string? Description { get; set; }
		[JsonPropertyName("Version")] public string? Version { get; set; }
	}

	private sealed class SectionsResponse : SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")] public List<ApplicationSectionRecord> Rows { get; set; } = [];
	}
}
