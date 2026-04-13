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
[McpServerToolType]
public sealed class ApplicationGetListTool(IApplicationListService applicationListService) {

	/// <summary>
	/// Stable MCP tool name for listing installed applications.
	/// </summary>
	internal const string ApplicationGetListToolName = "application-get-list";

	/// <summary>
	/// Returns installed applications from the requested Creatio environment as structured JSON.
	/// </summary>
	[McpServerTool(Name = ApplicationGetListToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Gets list of all applications from Creatio through backend MCP.")]
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
[McpServerToolType]
public sealed class ApplicationGetInfoTool(IApplicationInfoService applicationInfoService) {
	/// <summary>
	/// Stable MCP tool name for reading structured application info.
	/// </summary>
	internal const string ApplicationGetInfoToolName = "application-get-info";

	/// <summary>
	/// Returns primary package and runtime entity metadata for an installed application.
	/// </summary>
	[McpServerTool(Name = ApplicationGetInfoToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Gets application information from Creatio through backend MCP. Returns installed application identity plus package and entity context.")]
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
	internal const string ApplicationCreateToolName = "application-create";

	/// <summary>
	/// Creates a Creatio application and returns the same structured payload as application-get-info.
	/// </summary>
	[McpServerTool(Name = ApplicationCreateToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Creates a new application in Creatio through backend MCP and returns installed application identity plus the created package and entity context.")]
	public async Task<ApplicationContextResponse> ApplicationCreate(
		[Description("Parameters: environment-name, name, code, template-code, icon-background (all required); description, icon-id, client-type-id (optional)")]
		[Required]
		ApplicationCreateArgs args) {
		try {
			ValidateCreateArgs(args);
			ApplicationOptionalTemplateData? optionalTemplateData = ApplicationToolHelper.ParseOptionalTemplateData(args.OptionalTemplateDataJson);
			ApplicationDataForgeResult dataForge = await enrichmentService.EnrichAsync(
				args,
				optionalTemplateData,
				CancellationToken.None);
			ApplicationInfoResult result = applicationCreateService.CreateApplication(
				args.EnvironmentName,
				new ApplicationCreateRequest(
					args.Name,
					args.Code,
					args.Description,
					args.TemplateCode,
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

	private static void ValidateCreateArgs(ApplicationCreateArgs args) {
		if (string.IsNullOrWhiteSpace(args.Name)) {
			throw new ArgumentException("name is required.");
		}

		if (string.IsNullOrWhiteSpace(args.Code)) {
			throw new ArgumentException("code is required.");
		}

		if (string.IsNullOrWhiteSpace(args.TemplateCode)) {
			throw new ArgumentException(
				"template-code is required. " +
				"Provide the technical template name as a top-level field, for example AppFreedomUI.");
		}

		if (!KnownTemplates.Contains(args.TemplateCode.Trim(), StringComparer.OrdinalIgnoreCase)) {
			string available = string.Join(", ", KnownTemplates);
			throw new ArgumentException(
				$"Unknown template-code '{args.TemplateCode}'. " +
				$"Use the technical template name, not the display name. " +
				$"Known templates: {available}");
		}

		if (string.IsNullOrWhiteSpace(args.IconBackground)) {
			throw new ArgumentException(
				"icon-background is required. " +
				"Provide a top-level #RRGGBB value such as #1F5F8B.");
		}
	}
}

/// <summary>
/// MCP tool surface for creating a section inside an existing application.
/// </summary>
[McpServerToolType]
public sealed class ApplicationSectionCreateTool(IApplicationSectionCreateService applicationSectionCreateService) {
	/// <summary>
	/// Stable MCP tool name for creating sections in existing Creatio applications.
	/// </summary>
	internal const string ApplicationSectionCreateToolName = "application-section-create";

	/// <summary>
	/// Creates a section in an existing Creatio application and returns structured readback data.
	/// </summary>
	[McpServerTool(Name = ApplicationSectionCreateToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Creates a section inside an existing application in Creatio through backend MCP and returns structured section, entity, and page readback data.")]
	public ApplicationSectionContextResponse ApplicationSectionCreate(
		[Description("Parameters: environment-name, application-code, caption (required); description, entity-schema-name, with-mobile-pages (optional)")]
		[Required]
		ApplicationSectionCreateArgs args) {
		try {
			ValidateSectionCreateArgs(args);
			ApplicationSectionCreateResult result = applicationSectionCreateService.CreateSection(
				args.EnvironmentName,
				new ApplicationSectionCreateRequest(
					args.ApplicationCode,
					args.Caption,
					args.Description,
					args.EntitySchemaName,
					args.WithMobilePages));
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
				"application-section-create is scalar-only. Do not send title-localizations, description-localizations, caption-localizations, or name-localizations.");
		}
	}
}

/// <summary>
/// MCP tool surface for updating metadata of a section inside an existing application.
/// </summary>
[McpServerToolType]
public sealed class ApplicationSectionUpdateTool(IApplicationSectionUpdateService applicationSectionUpdateService) {
	/// <summary>
	/// Stable MCP tool name for updating sections in existing Creatio applications.
	/// </summary>
	internal const string ApplicationSectionUpdateToolName = "application-section-update";

	/// <summary>
	/// Updates metadata of a section in an existing Creatio application and returns structured before and after readback data.
	/// </summary>
	[McpServerTool(Name = ApplicationSectionUpdateToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Updates metadata of a section inside an existing application in Creatio through backend MCP and returns structured section readback data before and after the update.")]
	public ApplicationSectionUpdateContextResponse ApplicationSectionUpdate(
		[Description("Parameters: environment-name, application-code, section-code (required); caption, description, icon-id, icon-background (optional partial update fields)")]
		[Required]
		ApplicationSectionUpdateArgs args) {
		try {
			ValidateSectionUpdateArgs(args);
			ApplicationSectionUpdateResult result = applicationSectionUpdateService.UpdateSection(
				args.EnvironmentName,
				new ApplicationSectionUpdateRequest(
					args.ApplicationCode,
					args.SectionCode,
					args.Caption,
					args.Description,
					args.IconId,
					args.IconBackground));
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
				"application-section-update is scalar-only. Do not send title-localizations, description-localizations, caption-localizations, or name-localizations.");
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
[McpServerToolType]
public sealed class ApplicationSectionDeleteTool(IApplicationSectionDeleteService applicationSectionDeleteService) {
	/// <summary>
	/// Stable MCP tool name for deleting sections from existing Creatio applications.
	/// </summary>
	internal const string ApplicationSectionDeleteToolName = "application-section-delete";

	/// <summary>
	/// Deletes a section from an existing Creatio application and returns structured readback of the deleted section.
	/// </summary>
	[McpServerTool(Name = ApplicationSectionDeleteToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Deletes a section from an existing application in Creatio through backend MCP and returns structured deleted-section readback data.")]
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
[McpServerToolType]
public sealed class ApplicationSectionGetListTool(IApplicationSectionGetListService applicationSectionGetListService) {
	/// <summary>
	/// Stable MCP tool name for listing sections of existing Creatio applications.
	/// </summary>
	internal const string ApplicationSectionGetListToolName = "application-section-get-list";

	/// <summary>
	/// Returns all sections of an existing Creatio application.
	/// </summary>
	[McpServerTool(Name = ApplicationSectionGetListToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Gets the list of sections inside an existing application in Creatio through backend MCP and returns structured section list data.")]
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
