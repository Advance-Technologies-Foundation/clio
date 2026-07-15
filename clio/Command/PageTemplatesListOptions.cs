namespace Clio.Command {
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Clio.Common;
	using CommandLine;
	using Newtonsoft.Json;

	/// <summary>
	/// Options for the <c>list-page-templates</c> command.
	/// </summary>
	[Verb("list-page-templates", Aliases = ["page-templates", "page-templates-list"], HelpText = "List Freedom UI page templates available for create-page")]
	public class PageTemplatesListOptions : EnvironmentOptions {
		[Option("schema-type", Required = false, HelpText = "Filter by schema type: 'web' (9), 'mobile' (10) or 'desktop' (web templates with group Desktop). Defaults to all.")]
		public string SchemaType { get; set; }
	}

	/// <summary>
	/// Lists Freedom UI page templates advertised by <c>schema.template.api</c>.
	/// </summary>
	public class PageTemplatesListCommand : Command<PageTemplatesListOptions> {
		private readonly ISchemaTemplateCatalog _templateCatalog;
		private readonly ILogger _logger;

		public PageTemplatesListCommand(ISchemaTemplateCatalog templateCatalog, ILogger logger) {
			_templateCatalog = templateCatalog;
			_logger = logger;
		}

		public bool TryListTemplates(PageTemplatesListOptions options, out PageTemplateListResponse response) {
			try {
				PageSchemaType? schemaType = null;
				string groupFilter = null;
				if (!string.IsNullOrWhiteSpace(options.SchemaType)) {
					if (!TryParseTemplateFilter(options.SchemaType, out PageSchemaType parsed, out groupFilter, out string parseError)) {
						response = new PageTemplateListResponse { Success = false, Error = parseError };
						return false;
					}
					schemaType = parsed;
				}
				IReadOnlyList<PageTemplateInfo> templates = _templateCatalog.GetTemplates(schemaType);
				if (groupFilter != null) {
					templates = templates
						.Where(t => string.Equals(t.GroupName, groupFilter, StringComparison.OrdinalIgnoreCase))
						.ToList();
				}
				response = new PageTemplateListResponse {
					Success = true,
					Count = templates.Count,
					Items = templates.ToList()
				};
				return true;
			} catch (Exception ex) {
				response = new PageTemplateListResponse { Success = false, Error = ex.Message };
				return false;
			}
		}

		public override int Execute(PageTemplatesListOptions options) {
			bool success = TryListTemplates(options, out PageTemplateListResponse response);
			if (success && response.Items != null && response.Items.Count > 0) {
				_logger.WriteInfo(FormatTable(response));
				return 0;
			}
			_logger.WriteInfo(JsonConvert.SerializeObject(response, Formatting.Indented));
			return success ? 0 : 1;
		}

		private static string FormatTable(PageTemplateListResponse response) {
			const string nameHeader = "Name";
			const string titleHeader = "Title";
			const string groupHeader = "Group";
			const string typeHeader = "Type";
			const string uidHeader = "UId";
			int nameWidth = Math.Max(nameHeader.Length, response.Items.Max(t => t.Name?.Length ?? 0));
			int titleWidth = Math.Max(titleHeader.Length, response.Items.Max(t => t.Title?.Length ?? 0));
			int groupWidth = Math.Max(groupHeader.Length, response.Items.Max(t => t.GroupName?.Length ?? 0));
			int typeWidth = Math.Max(typeHeader.Length, 6);
			int uidWidth = Math.Max(uidHeader.Length, response.Items.Max(t => t.UId?.Length ?? 0));
			System.Text.StringBuilder sb = new();
			sb.AppendLine();
			sb.Append(' ').Append(Pad(nameHeader, nameWidth)).Append("  ")
				.Append(Pad(titleHeader, titleWidth)).Append("  ")
				.Append(Pad(groupHeader, groupWidth)).Append("  ")
				.Append(Pad(typeHeader, typeWidth)).Append("  ")
				.Append(Pad(uidHeader, uidWidth)).AppendLine();
			sb.Append(' ').Append(new string('-', nameWidth)).Append("  ")
				.Append(new string('-', titleWidth)).Append("  ")
				.Append(new string('-', groupWidth)).Append("  ")
				.Append(new string('-', typeWidth)).Append("  ")
				.Append(new string('-', uidWidth)).AppendLine();
			foreach (PageTemplateInfo item in response.Items) {
				sb.Append(' ').Append(Pad(item.Name, nameWidth)).Append("  ")
					.Append(Pad(item.Title, titleWidth)).Append("  ")
					.Append(Pad(item.GroupName, groupWidth)).Append("  ")
					.Append(Pad(DescribeSchemaType(item.SchemaType), typeWidth)).Append("  ")
					.Append(Pad(item.UId, uidWidth)).AppendLine();
			}
			sb.AppendLine();
			sb.Append($"Total: {response.Count}");
			return sb.ToString();
		}

		private static string Pad(string value, int width) => (value ?? string.Empty).PadRight(width);

		private static string DescribeSchemaType(int schemaType) =>
			PageSchemaTypeExtensions.FromNumericValue(schemaType).ToLabel();

		/// <summary>
		/// Validates and parses a schema-type filter value. This is a pure input validation with no
		/// environment dependency, so callers (e.g. the MCP tool) can reject an invalid schema-type
		/// before resolving the target environment (ENG-91825 env-validation order).
		/// Kept for source compatibility; new call sites should prefer
		/// <see cref="TryParseTemplateFilter"/>, which also recognizes the <c>desktop</c> filter.
		/// </summary>
		/// <param name="value">The schema-type filter supplied by the caller.</param>
		/// <param name="schemaType">The parsed schema type when the value is recognized.</param>
		/// <param name="error">A human-readable error message when the value is not recognized.</param>
		/// <returns><c>true</c> when the value is a recognized schema-type; otherwise <c>false</c>.</returns>
		public static bool TryParseSchemaType(string value, out PageSchemaType schemaType, out string error) =>
			TryParseTemplateFilter(value, out schemaType, out _, out error);

		/// <summary>
		/// Validates and parses a template filter value: a plain schema type (<c>web</c> / <c>mobile</c>)
		/// or the <c>desktop</c> pseudo-filter, which maps to the web catalog narrowed to templates whose
		/// group is <c>Desktop</c> (desktop pages are ordinary web schemas distinguished only by group).
		/// Pure input validation with no environment dependency (ENG-91825 env-validation order).
		/// </summary>
		/// <param name="value">The schema-type filter supplied by the caller.</param>
		/// <param name="schemaType">The parsed schema type when the value is recognized.</param>
		/// <param name="groupFilter">The template group to narrow the catalog to, or <c>null</c> when the
		/// value is a plain schema type.</param>
		/// <param name="error">A human-readable error message when the value is not recognized.</param>
		/// <returns><c>true</c> when the value is a recognized filter; otherwise <c>false</c>.</returns>
		public static bool TryParseTemplateFilter(
			string value, out PageSchemaType schemaType, out string groupFilter, out string error) {
			schemaType = default;
			groupFilter = null;
			error = null;
			if (string.IsNullOrWhiteSpace(value)) {
				// The method is public and may be called without the IsNullOrWhiteSpace pre-guard, so it must
				// be total over its input: a null/blank value is a clean "not recognized", never a NullReferenceException.
				error = $"Unknown schema-type '{value}'. Use 'web', 'mobile' or 'desktop'.";
				return false;
			}
			switch (value.Trim().ToLowerInvariant()) {
				case "web":
				case "freedomuipage":
				case "page":
				case "9":
					schemaType = PageSchemaType.Web;
					return true;
				case "mobile":
				case "mobilepage":
				case "10":
					schemaType = PageSchemaType.Mobile;
					return true;
				case "desktop":
				case "desktoppage":
					schemaType = PageSchemaType.Web;
					groupFilter = SchemaTemplateCatalog.DesktopGroupName;
					return true;
				default:
					error = $"Unknown schema-type '{value}'. Use 'web', 'mobile' or 'desktop'.";
					return false;
			}
		}
	}
}
