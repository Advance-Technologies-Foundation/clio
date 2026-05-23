using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for installed-application discovery.
/// </summary>
public sealed class ApplicationGetListTool(IApplicationListService applicationListService) {

	/// <summary>
	/// Legacy MCP tool name retained for the documentation surface served by ToolContractGetTool.
	/// The capability now lives on <see cref="AppsTool"/> with empty identifier arguments.
	/// </summary>
	internal const string ApplicationGetListToolName = "list-apps";

	public ApplicationListResponse ApplicationGetList(
		[Description("Parameters: environment-name (required)")]
		[Required]
		ApplicationGetListArgs args) {
		try {
			return ApplicationToolHelper.CreateListResponse(
				applicationListService.GetApplications(args.EnvironmentName!, null, null)
					.Select(application => new ApplicationListItemResult(
						application.Id.ToString(),
						application.Name,
						application.Code,
						application.Version))
					.ToList());
		} catch (Exception ex) {
			return ApplicationToolHelper.CreateListErrorResponse(ex.Message);
		}
	}
}

/// <summary>
/// MCP tool surface for application context reads.
/// </summary>
public sealed class ApplicationGetInfoTool(IApplicationInfoService applicationInfoService) {
	/// <summary>
	/// Legacy MCP tool name retained for the documentation surface served by ToolContractGetTool.
	/// The capability now lives on <see cref="AppsTool"/> with an id or code argument.
	/// </summary>
	internal const string ApplicationGetInfoToolName = "get-app-info";

	public ApplicationContextResponse ApplicationGetInfo(
		[Description("Parameters: environment-name (required), id or code (exactly one required)")]
		[Required]
		ApplicationGetInfoArgs args) {
		try {
			bool hasId = !string.IsNullOrWhiteSpace(args.Id);
			bool hasCode = !string.IsNullOrWhiteSpace(args.Code);
			if (hasId == hasCode) {
				throw new ArgumentException("Provide exactly one identifier: id or code.");
			}

			ApplicationInfoResult result = applicationInfoService.GetApplicationInfo(
				args.EnvironmentName,
				args.Id,
				args.Code);
			return ApplicationToolHelper.CreateContextResponse(ApplicationToolResultMapper.Map(result));
		} catch (Exception ex) {
			return ApplicationToolHelper.CreateContextErrorResponse(ex.Message);
		}
	}
}

/// <summary>
/// MCP tool surface for application creation.
/// </summary>
[McpServerToolType]
public sealed class ApplicationCreateTool(
	IApplicationCreateService applicationCreateService,
	IApplicationCreateEnrichmentService enrichmentService) {
	/// <summary>
	/// Stable MCP tool name for creating Creatio applications.
	/// </summary>
	internal const string ApplicationCreateToolName = "create-app";

	/// <summary>
	/// Creates a Creatio application and returns the same structured payload as get-app-info.
	/// </summary>
		[Description("Creates a new application in Creatio through backend MCP and returns installed application identity plus the created package and entity context.")]
	public async Task<ApplicationContextResponse> ApplicationCreate(
		[Description("Parameters: environment-name, name, code (required); template-code (optional, defaults to AppFreedomUI — the stable recommended template); description, icon-background, icon-id, client-type-id (optional)")]
		[Required]
		ApplicationCreateRunArgs args) {
		try {
			ValidateCreateArgs(args);
			string effectiveTemplateCode = string.IsNullOrWhiteSpace(args.TemplateCode) ? "AppFreedomUI" : args.TemplateCode.Trim();
			ApplicationOptionalTemplateData? optionalTemplateData = ApplicationToolHelper.ParseOptionalTemplateData(args.OptionalTemplateDataJson);
			ApplicationDataForgeResult dataForge = enrichmentService.Enrich(
				args,
				optionalTemplateData,
				CancellationToken.None);
			ApplicationInfoResult result = applicationCreateService.CreateApplication(
				args.EnvironmentName,
				new ApplicationCreateRequest(
					args.Name,
					args.Code,
					args.Description,
					effectiveTemplateCode,
					args.IconId,
					args.IconBackground,
					args.ClientTypeId,
					optionalTemplateData));
			return ApplicationToolHelper.CreateContextResponse(
				ApplicationToolResultMapper.Map(result),
				dataForge);
		} catch (Exception ex) {
			return ApplicationToolHelper.CreateContextErrorResponse(ex.Message);
		}
	}

	private static readonly string[] KnownTemplates = [
		"AppFreedomUI", "AppFreedomUIv2", "AppWithHomePage", "EmptyApp"
	];

	private static void ValidateCreateArgs(ApplicationCreateRunArgs args) {
		if (string.IsNullOrWhiteSpace(args.Name)) {
			throw new ArgumentException("name is required.");
		}

		if (string.IsNullOrWhiteSpace(args.Code)) {
			throw new ArgumentException("code is required.");
		}

		string effectiveTemplate = string.IsNullOrWhiteSpace(args.TemplateCode) ? "AppFreedomUI" : args.TemplateCode.Trim();
		if (!KnownTemplates.Contains(effectiveTemplate, StringComparer.OrdinalIgnoreCase)) {
			string available = string.Join(", ", KnownTemplates);
			throw new ArgumentException(
				$"Unknown template-code '{args.TemplateCode}'. " +
				$"Use the technical template name, not the display name. " +
				$"Known templates: {available}. Omit template-code to use the default AppFreedomUI.");
		}

		if (!string.IsNullOrWhiteSpace(args.IconBackground)) {
			ApplicationSectionColorPalette.ValidateOrThrow(args.IconBackground.Trim());
		}

		if (args.TitleLocalizations is not null ||
			args.DescriptionLocalizations is not null ||
			args.CaptionLocalizations is not null ||
			args.NameLocalizations is not null) {
			throw new ArgumentException(
				"create-app is scalar-only. Do not send title-localizations, description-localizations, caption-localizations, or name-localizations.");
		}
	}
}

/// <summary>
/// MCP tool surface for creating a section inside an existing application.
/// </summary>
public sealed class ApplicationSectionCreateTool(IApplicationSectionCreateService applicationSectionCreateService) {
	/// <summary>Legacy MCP tool name retained for ToolContractGetTool documentation; entry now lives on <see cref="AppSectionTool"/>.</summary>
	internal const string ApplicationSectionCreateToolName = "create-app-section";

	public async Task<ApplicationSectionContextResponse> ApplicationSectionCreate(
		[Description("Parameters: environment-name, application-code, caption (required); description, entity-schema-name, icon-background, with-mobile-pages (optional)")]
		[Required]
		ApplicationSectionCreateArgs args,
		global::ModelContextProtocol.Server.McpServer server,
		CancellationToken cancellationToken = default) {
		try {
			ValidateSectionCreateArgs(args);
			string resolvedIconBackground = args.IconBackground;
			if (!string.IsNullOrWhiteSpace(resolvedIconBackground) || server?.ClientCapabilities?.Elicitation is not null) {
				resolvedIconBackground = await SectionIconPalette.ResolveAsync(
					server, args.IconBackground, args.Caption, cancellationToken).ConfigureAwait(false);
			}
			ApplicationSectionCreateResult result = applicationSectionCreateService.CreateSection(
				args.EnvironmentName,
				new ApplicationSectionCreateRequest(
					args.ApplicationCode,
					args.Caption,
					args.Description,
					args.EntitySchemaName,
					args.WithMobilePages,
					resolvedIconBackground));
			return ApplicationToolHelper.CreateSectionContextResponse(ApplicationToolResultMapper.Map(result));
		} catch (Exception ex) {
			return ApplicationToolHelper.CreateSectionContextErrorResponse(ex.Message);
		}
	}

	private static void ValidateSectionCreateArgs(ApplicationSectionCreateArgs args) {
		if (string.IsNullOrWhiteSpace(args.ApplicationCode)) {
			throw new ArgumentException("application-code is required.");
		}

		if (string.IsNullOrWhiteSpace(args.Caption)) {
			throw new ArgumentException("caption is required.");
		}

		if (args.TitleLocalizations is not null ||
			args.DescriptionLocalizations is not null ||
			args.CaptionLocalizations is not null ||
			args.NameLocalizations is not null) {
			throw new ArgumentException(
				"create-app-section is scalar-only. Do not send title-localizations, description-localizations, caption-localizations, or name-localizations.");
		}
	}
}

/// <summary>
/// MCP tool surface for updating metadata of a section inside an existing application.
/// </summary>
public sealed class ApplicationSectionUpdateTool(IApplicationSectionUpdateService applicationSectionUpdateService) {
	/// <summary>Legacy MCP tool name retained for ToolContractGetTool documentation; entry now lives on <see cref="AppSectionTool"/>.</summary>
	internal const string ApplicationSectionUpdateToolName = "update-app-section";

	public async Task<ApplicationSectionUpdateContextResponse> ApplicationSectionUpdate(
		[Description("Parameters: environment-name, application-code, section-code (required); caption, description, icon-id, icon-background (optional partial update fields)")]
		[Required]
		ApplicationSectionUpdateArgs args,
		global::ModelContextProtocol.Server.McpServer server,
		CancellationToken cancellationToken = default) {
		try {
			ValidateSectionUpdateArgs(args);
			string resolvedIconBackground = args.IconBackground;
			if (!string.IsNullOrWhiteSpace(resolvedIconBackground)) {
				resolvedIconBackground = await SectionIconPalette.ResolveAsync(
					server, args.IconBackground, args.Caption ?? args.SectionCode, cancellationToken).ConfigureAwait(false);
			}
			ApplicationSectionUpdateResult result = applicationSectionUpdateService.UpdateSection(
				args.EnvironmentName,
				new ApplicationSectionUpdateRequest(
					args.ApplicationCode,
					args.SectionCode,
					args.Caption,
					args.Description,
					args.IconId,
					resolvedIconBackground));
			return ApplicationToolHelper.CreateSectionUpdateContextResponse(ApplicationToolResultMapper.Map(result));
		} catch (Exception ex) {
			return ApplicationToolHelper.CreateSectionUpdateContextErrorResponse(ex.Message);
		}
	}

	private static void ValidateSectionUpdateArgs(ApplicationSectionUpdateArgs args) {
		if (string.IsNullOrWhiteSpace(args.ApplicationCode)) {
			throw new ArgumentException("application-code is required.");
		}

		if (string.IsNullOrWhiteSpace(args.SectionCode)) {
			throw new ArgumentException("section-code is required.");
		}

		if (args.TitleLocalizations is not null ||
			args.DescriptionLocalizations is not null ||
			args.CaptionLocalizations is not null ||
			args.NameLocalizations is not null) {
			throw new ArgumentException(
				"update-app-section is scalar-only. Do not send title-localizations, description-localizations, caption-localizations, or name-localizations.");
		}

		bool hasCaption = args.Caption is not null;
		bool hasDescription = args.Description is not null;
		bool hasIconId = args.IconId is not null;
		bool hasIconBackground = args.IconBackground is not null;
		if (!hasCaption && !hasDescription && !hasIconId && !hasIconBackground) {
			throw new ArgumentException("Provide at least one mutable field: caption, description, icon-id, or icon-background.");
		}
	}
}

/// <summary>
/// MCP tool surface for deleting a section from an existing application.
/// </summary>
public sealed class ApplicationSectionDeleteTool(IApplicationSectionDeleteService applicationSectionDeleteService) {
	/// <summary>Legacy MCP tool name retained for ToolContractGetTool documentation; entry now lives on <see cref="AppSectionTool"/>.</summary>
	internal const string ApplicationSectionDeleteToolName = "delete-app-section";

	public ApplicationSectionDeleteContextResponse ApplicationSectionDelete(
		[Description("Parameters: environment-name, application-code, section-code (all required)")]
		[Required]
		ApplicationSectionDeleteArgs args) {
		try {
			ValidateSectionDeleteArgs(args);
			ApplicationSectionDeleteResult result = applicationSectionDeleteService.DeleteSection(
				args.EnvironmentName,
				new ApplicationSectionDeleteRequest(
					args.ApplicationCode,
					args.SectionCode,
					args.DeleteEntitySchema ?? false));
			return ApplicationToolHelper.CreateSectionDeleteContextResponse(ApplicationToolResultMapper.Map(result));
		} catch (Exception ex) {
			return ApplicationToolHelper.CreateSectionDeleteContextErrorResponse(ex.Message);
		}
	}

	private static void ValidateSectionDeleteArgs(ApplicationSectionDeleteArgs args) {
		if (string.IsNullOrWhiteSpace(args.ApplicationCode)) {
			throw new ArgumentException("application-code is required.");
		}

		if (string.IsNullOrWhiteSpace(args.SectionCode)) {
			throw new ArgumentException("section-code is required.");
		}
	}
}

/// <summary>
/// MCP tool surface for listing sections of an existing Creatio application.
/// </summary>
public sealed class ApplicationSectionGetListTool(IApplicationSectionGetListService applicationSectionGetListService) {
	/// <summary>Legacy MCP tool name retained for ToolContractGetTool documentation; entry now lives on <see cref="AppSectionTool"/>.</summary>
	internal const string ApplicationSectionGetListToolName = "list-app-sections";

	public ApplicationSectionListContextResponse ApplicationSectionGetList(
		[Description("Parameters: environment-name, application-code (both required)")]
		[Required]
		ApplicationSectionGetListArgs args) {
		try {
			if (string.IsNullOrWhiteSpace(args.ApplicationCode)) {
				throw new ArgumentException("application-code is required.");
			}

			ApplicationSectionGetListResult result = applicationSectionGetListService.GetSections(
				args.EnvironmentName,
				new ApplicationSectionGetListRequest(args.ApplicationCode));
			return ApplicationToolHelper.CreateSectionListContextResponse(ApplicationToolResultMapper.Map(result));
		} catch (Exception ex) {
			return ApplicationToolHelper.CreateSectionListContextErrorResponse(ex.Message);
		}
	}
}
