using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using CommandLine;
using static Clio.Package.SelectQueryHelper;

namespace Clio.Command;

/// <summary>
/// Options for listing the printables (reports) registered in a Creatio environment.
/// MCP-only surface: the hidden verb documents the canonical name (and satisfies the
/// command-docs gate) without being registered in the CLI parser — consumed by the
/// <c>list-printables</c> probe tool. OOTB button-action requests initiative (ENG-93187).
/// </summary>
[Verb("list-printables", Hidden = true, HelpText = "List MS Word printables registered in a Creatio environment (MCP probe surface)")]
[FeatureToggle("requests-registry")]
public class ListPrintablesOptions : RemoteCommandOptions {
	/// <summary>
	/// Gets or sets the optional entity schema name filter (e.g. <c>Contact</c>).
	/// When set, only printables attached to that entity — directly via
	/// <c>SysEntitySchema</c> or through their section module — are returned;
	/// when blank, every MS Word printable of the environment is returned.
	/// </summary>
	public string? EntityName { get; set; }
}

/// <summary>
/// One printable available in the target environment. Field names deliberately match
/// the <c>crt.PrintablesRequest</c> parameter names they fill (<c>templateId</c>,
/// <c>printableCaption</c>, <c>convertInPDF</c>) so an agent copies values 1:1.
/// </summary>
public sealed record PrintableSummary(
	[property: JsonPropertyName("templateId")] string TemplateId,
	[property: JsonPropertyName("printableCaption")] string PrintableCaption,
	[property: JsonPropertyName("convertInPDF")] bool ConvertInPdf,
	[property: JsonPropertyName("showInCard")] bool ShowInCard,
	[property: JsonPropertyName("showInSection")] bool ShowInSection,
	[property: JsonPropertyName("entitySchemaName")] string? EntitySchemaName,
	[property: JsonPropertyName("moduleEntitySchemaName")] string? ModuleEntitySchemaName
);

/// <summary>
/// Structured response of the <c>list-printables</c> probe.
/// </summary>
public sealed class ListPrintablesResponse : EnvironmentProbeResponse {
	/// <summary>
	/// Gets the number of returned printables.
	/// </summary>
	[JsonPropertyName("count")]
	public int Count { get; init; }

	/// <summary>
	/// Gets the printables matching the entity filter, ordered by caption. An empty
	/// list on <c>success: true</c> is a definitive answer: the environment has no
	/// matching printables (a menu-mode button would render "no printables"), so a
	/// direct-mode config cannot be authored — fall back to menu mode or ask the user.
	/// </summary>
	[JsonPropertyName("printables")]
	public IReadOnlyList<PrintableSummary> Printables { get; init; } = [];
}

/// <summary>
/// Reads the MS Word printables registered in <c>SysModuleReport</c> of a Creatio
/// environment via a single built-in DataService SelectQuery — no cliogate required,
/// works on any Creatio. The query mirrors the Freedom UI runtime printables service
/// (<c>PrintablesService._createQuery</c> in creatio-ui): same columns, same
/// MS Word type filter, and the same two-path entity linkage (direct
/// <c>SysEntitySchema</c> or the section module's entity).
/// </summary>
public class ListPrintablesCommand : Command<ListPrintablesOptions> {
	/// <summary>
	/// <c>SysModuleReport.Type</c> lookup Id of MS Word printables — the only type the
	/// Freedom UI print button offers (mirrors <c>PrintableType.MSWord</c> in creatio-ui;
	/// DevExpress and FastReport printables use other designers and are excluded there too).
	/// </summary>
	internal const string MsWordPrintableTypeId = "8bc259ef-4276-4906-b7a6-23dc59be7fe2";

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	private static readonly IReadOnlyList<SelectQueryColumnDefinition> PrintableColumns =
	[
		new("Id", "Id"),
		new("Caption", "Caption"),
		new("ConvertInPDF", "ConvertInPDF"),
		new("ShowInCard", "ShowInCard"),
		new("ShowInSection", "ShowInSection"),
		new("SysEntitySchema.Name", "SysEntitySchemaName"),
		new("SysModule.SysModuleEntity.[SysSchema:UId:SysEntitySchemaUId].Name", "SysModuleEntityName")
	];

	public ListPrintablesCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	/// <inheritdoc/>
	public override int Execute(ListPrintablesOptions options) {
		bool ok = TryGetPrintables(options, out ListPrintablesResponse response);
		if (!ok) {
			_logger.WriteError(response.Error ?? "Failed to list printables.");
			return 1;
		}
		if (response.Count == 0) {
			_logger.WriteInfo("No printables found.");
			return 0;
		}
		foreach (PrintableSummary printable in response.Printables) {
			string entity = printable.EntitySchemaName ?? printable.ModuleEntitySchemaName ?? string.Empty;
			_logger.WriteInfo(
				$"Printable: {printable.PrintableCaption} | templateId: {printable.TemplateId} | Entity: {entity}"
				+ $" | PDF: {printable.ConvertInPdf} | Card: {printable.ShowInCard} | Section: {printable.ShowInSection}");
		}
		return 0;
	}

	/// <summary>
	/// Queries <c>SysModuleReport</c> on the remote environment and returns the MS Word
	/// printables matching <see cref="ListPrintablesOptions.EntityName"/> (when set).
	/// Returns <see langword="false"/> with an error-bearing response on transport
	/// failure; an empty match list is a successful, definitive answer.
	/// </summary>
	/// <param name="options">Entity filter and environment settings.</param>
	/// <param name="response">The structured probe response.</param>
	public virtual bool TryGetPrintables(ListPrintablesOptions options, out ListPrintablesResponse response) {
		ArgumentNullException.ThrowIfNull(options);
		try {
			object query = BuildPrintablesQuery();
			ListPrintablesResponseDto dto = ExecuteSelectQuery<ListPrintablesResponseDto>(
				_applicationClient,
				_serviceUrlBuilder,
				query);
			IReadOnlyList<PrintableSummary> printables = dto.Rows
				.Where(row => MatchesEntity(row, options.EntityName))
				.OrderBy(row => row.Caption, StringComparer.OrdinalIgnoreCase)
				.Select(row => new PrintableSummary(
					row.Id ?? string.Empty,
					row.Caption ?? string.Empty,
					row.ConvertInPDF,
					row.ShowInCard,
					row.ShowInSection,
					string.IsNullOrWhiteSpace(row.SysEntitySchemaName) ? null : row.SysEntitySchemaName,
					string.IsNullOrWhiteSpace(row.SysModuleEntityName) ? null : row.SysModuleEntityName))
				.ToList();
			response = new ListPrintablesResponse {
				Success = true,
				Count = printables.Count,
				Printables = printables
			};
			return true;
		} catch (Exception exception) {
			// Transport/deserialisation failure — transient by definition (the probe has no
			// resolution phase, so ResolutionFailed stays omitted and a consumer gate must
			// treat this as a warning, never a write-blocker).
			response = new ListPrintablesResponse {
				Success = false,
				Error = exception.Message
			};
			return false;
		}
	}

	/// <summary>
	/// Mirrors the runtime's two-path entity match (<c>PrintablesService._filterEntityName</c>):
	/// the printable is attached either directly (<c>SysEntitySchema</c>) or through its
	/// section module's entity. Case-insensitive as a deliberate agent-friendliness
	/// deviation from the runtime's strict comparison — entity schema names are unique
	/// case-insensitively, so this cannot produce a wrong match.
	/// </summary>
	private static bool MatchesEntity(ListPrintablesRowDto row, string? entityName) {
		if (string.IsNullOrWhiteSpace(entityName)) {
			return true;
		}
		string wanted = entityName.Trim();
		return string.Equals(row.SysEntitySchemaName, wanted, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(row.SysModuleEntityName, wanted, StringComparison.OrdinalIgnoreCase);
	}

	private static object BuildPrintablesQuery() {
		List<SelectQueryFilterDefinition> filters =
		[
			new("Type.Id", MsWordPrintableTypeId, GuidDataValueType)
		];
		return BuildSelectQuery("SysModuleReport", PrintableColumns, filters);
	}

	private sealed class ListPrintablesResponseDto : SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<ListPrintablesRowDto> Rows { get; set; } = [];
	}

	private sealed class ListPrintablesRowDto {
		[JsonPropertyName("Id")]
		public string? Id { get; set; }

		[JsonPropertyName("Caption")]
		public string? Caption { get; set; }

		[JsonPropertyName("ConvertInPDF")]
		public bool ConvertInPDF { get; set; }

		[JsonPropertyName("ShowInCard")]
		public bool ShowInCard { get; set; }

		[JsonPropertyName("ShowInSection")]
		public bool ShowInSection { get; set; }

		[JsonPropertyName("SysEntitySchemaName")]
		public string? SysEntitySchemaName { get; set; }

		[JsonPropertyName("SysModuleEntityName")]
		public string? SysModuleEntityName { get; set; }
	}
}
