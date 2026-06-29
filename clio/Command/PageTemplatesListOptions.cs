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
		[Option("schema-type", Required = false, HelpText = "Filter by schema type: 'web' (9) or 'mobile' (10). Defaults to all.")]
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
				if (!string.IsNullOrWhiteSpace(options.SchemaType)) {
					if (!TryParseSchemaType(options.SchemaType, out PageSchemaType parsed, out string parseError)) {
						response = new PageTemplateListResponse { Success = false, Error = parseError };
						return false;
					}
					schemaType = parsed;
				}
				IReadOnlyList<PageTemplateInfo> templates = _templateCatalog.GetTemplates(schemaType);
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
		/// </summary>
		/// <param name="value">The schema-type filter supplied by the caller.</param>
		/// <param name="schemaType">The parsed schema type when the value is recognized.</param>
		/// <param name="error">A human-readable error message when the value is not recognized.</param>
		/// <returns><c>true</c> when the value is a recognized schema-type; otherwise <c>false</c>.</returns>
		public static bool TryParseSchemaType(string value, out PageSchemaType schemaType, out string error) {
			schemaType = default;
			error = null;
			if (string.IsNullOrWhiteSpace(value)) {
				// The method is public and may be called without the IsNullOrWhiteSpace pre-guard, so it must
				// be total over its input: a null/blank value is a clean "not recognized", never a NullReferenceException.
				error = $"Unknown schema-type '{value}'. Use 'web' or 'mobile'.";
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
				default:
					error = $"Unknown schema-type '{value}'. Use 'web' or 'mobile'.";
					return false;
			}
		}
	}
}
