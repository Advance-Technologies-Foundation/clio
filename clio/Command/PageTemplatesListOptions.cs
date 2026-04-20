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
		[Option("schema-type", Required = false, HelpText = "Filter by schema type: 'web' (FreedomUIPage=9) or 'mobile' (MobilePage=10). Defaults to all.")]
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
			_logger.WriteInfo(JsonConvert.SerializeObject(response));
			return success ? 0 : 1;
		}

		private static bool TryParseSchemaType(string value, out PageSchemaType schemaType, out string error) {
			schemaType = default;
			error = null;
			switch (value.Trim().ToLowerInvariant()) {
				case "web":
				case "freedomuipage":
				case "page":
				case "9":
					schemaType = PageSchemaType.FreedomUIPage;
					return true;
				case "mobile":
				case "mobilepage":
				case "10":
					schemaType = PageSchemaType.MobilePage;
					return true;
				default:
					error = $"Unknown schema-type '{value}'. Use 'web' or 'mobile'.";
					return false;
			}
		}
	}
}
