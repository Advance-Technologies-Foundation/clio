using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
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
		[Description("application-get-list parameters")]
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
	[Description("Gets application information from Creatio through backend MCP. Returns application context with packages and entities.")]
	public ApplicationContextResponse ApplicationGetInfo(
		[Description("application-get-info parameters")]
		[Required]
		ApplicationGetInfoArgs args) {
		try {
			bool hasAppId = !string.IsNullOrWhiteSpace(args.AppId);
			bool hasAppCode = !string.IsNullOrWhiteSpace(args.AppCode);
			if (hasAppId == hasAppCode) {
				throw new ArgumentException("Provide exactly one identifier: app-id or app-code.");
			}

			ApplicationInfoResult result = applicationInfoService.GetApplicationInfo(
				args.EnvironmentName,
				args.AppId,
				args.AppCode);
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
public sealed class ApplicationCreateTool(IApplicationCreateService applicationCreateService) {
	/// <summary>
	/// Stable MCP tool name for creating Creatio applications.
	/// </summary>
	internal const string ApplicationCreateToolName = "application-create";

	/// <summary>
	/// Creates a Creatio application and returns the same structured payload as application-get-info.
	/// </summary>
	[McpServerTool(Name = ApplicationCreateToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Creates a new application in Creatio through backend MCP and returns the created application context.")]
	public ApplicationContextResponse ApplicationCreate(
		[Description("application-create parameters")]
		[Required]
		ApplicationCreateArgs args) {
		try {
			ValidateCreateArgs(args);
			ApplicationOptionalTemplateData? optionalTemplateData = ParseOptionalTemplateData(args.OptionalTemplateDataJson);
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
			return ApplicationToolHelper.CreateContextResponse(ApplicationToolResultMapper.Map(result));
		} catch (Exception ex) {
			return ApplicationToolHelper.CreateContextErrorResponse(ex.Message);
		}
	}

	private static void ValidateCreateArgs(ApplicationCreateArgs args) {
		if (string.IsNullOrWhiteSpace(args.Name)) {
			throw new ArgumentException("name is required.");
		}

		if (string.IsNullOrWhiteSpace(args.Code)) {
			throw new ArgumentException("code is required.");
		}

		if (string.IsNullOrWhiteSpace(args.TemplateCode)) {
			throw new ArgumentException("template-code is required.");
		}

		if (string.IsNullOrWhiteSpace(args.IconBackground)) {
			throw new ArgumentException("icon-background is required.");
		}
	}

	private static ApplicationOptionalTemplateData? ParseOptionalTemplateData(string? optionalTemplateDataJson) {
		if (string.IsNullOrWhiteSpace(optionalTemplateDataJson)) {
			return null;
		}

		ApplicationOptionalTemplateDataJsonArgs? optionalTemplateData;
		try {
			optionalTemplateData = JsonSerializer.Deserialize<ApplicationOptionalTemplateDataJsonArgs>(
				optionalTemplateDataJson,
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
		} catch (JsonException ex) {
			throw new ArgumentException(
				$"Invalid optional-template-data-json format: {ex.Message}",
				nameof(optionalTemplateDataJson),
				ex);
		}

		if (optionalTemplateData?.UseAiContentGeneration == true) {
			throw new ArgumentException(
				"useAiContentGeneration=true is not supported in application-create.");
		}

		return optionalTemplateData is null
			? null
			: new ApplicationOptionalTemplateData(
				optionalTemplateData.EntitySchemaName,
				optionalTemplateData.UseExistingEntitySchema,
				optionalTemplateData.UseAiContentGeneration,
				optionalTemplateData.AppSectionDescription);
	}
}
