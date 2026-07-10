using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for installed-application discovery. Resolves the target environment per request
/// through <see cref="IToolCommandResolver"/> so the tool honors the mcp-http credential-passthrough
/// header (<c>X-Integration-Credentials</c>) as well as registered-environment / stdio targets, and
/// executes under the per-tenant lock and in-flight guard (FR-05, ENG-93347).
/// </summary>
[McpServerToolType]
public sealed class ApplicationGetListTool(
	ILogger logger,
	IToolCommandResolver commandResolver,
	IApplicationListService applicationListService)
	: BaseTool<EnvironmentOptions>(null, logger, commandResolver) {

	private readonly IToolCommandResolver _commandResolver = commandResolver;

	/// <summary>
	/// Stable MCP tool name for listing installed applications.
	/// </summary>
	internal const string ApplicationGetListToolName = "list-apps";

	/// <summary>
	/// Returns installed applications from the requested Creatio environment as structured JSON.
	/// </summary>
	[McpServerTool(Name = ApplicationGetListToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Gets list of all applications from Creatio through backend MCP.")]
	public ApplicationListResponse ApplicationGetList(
		[Description("Parameters: environment-name (required unless credential passthrough supplies the tenant)")]
		[Required]
		ApplicationGetListArgs args) {
		EnvironmentOptions options = new() { Environment = args.EnvironmentName };
		// ExecuteWithCleanLog(options, ...) — the OPTIONS-AWARE overload — keys the execution lock and
		// the session-container in-flight guard off THIS call's tenant (FR-05), not the shared fallback.
		return ExecuteWithCleanLog(options, () => {
			try {
				EnvironmentSettings settings = _commandResolver.Resolve<EnvironmentSettings>(options);
				return ApplicationToolHelper.CreateListResponse(
					applicationListService.GetApplications(settings, null, null)
						.Select(application => new ApplicationListItemResult(
							application.Id.ToString(),
							application.Name,
							application.Code,
							application.Version))
						.ToList());
			} catch (Exception ex) {
				return ApplicationToolHelper.CreateListErrorResponse(SensitiveErrorTextRedactor.Redact(ex.Message));
			}
		});
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
	internal const string ApplicationGetInfoToolName = "get-app-info";

	/// <summary>
	/// Returns primary package and runtime entity metadata for an installed application.
	/// </summary>
	[McpServerTool(Name = ApplicationGetInfoToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Gets application information from Creatio through backend MCP. Returns installed application identity plus package and entity context. "
		+ "Each entity column round-trips into sync-schemas update-entity as-is — send the same column object back and add an 'action' verb (modify/remove); "
		+ "no field renaming needed, and no separate get-tool-contract call is required to learn the write shape. "
		+ "Long-running: streams notifications/progress while working — await completion and do not retry on a perceived timeout.")]
	public async Task<ApplicationContextResponse> ApplicationGetInfo(
		[Description("Parameters: environment-name (required), id or code (exactly one required)")]
		[Required]
		ApplicationGetInfoArgs args,
		global::ModelContextProtocol.Server.McpServer server,
		RequestContext<CallToolRequestParams> requestContext,
		CancellationToken cancellationToken = default) {
		try {
			bool hasId = !string.IsNullOrWhiteSpace(args.Id);
			bool hasCode = !string.IsNullOrWhiteSpace(args.Code);
			if (hasId == hasCode) {
				throw new ArgumentException("Provide exactly one identifier: id or code.");
			}

			ApplicationInfoResult result = await McpProgressHeartbeat.RunWithProgressAsync(
				server,
				requestContext?.Params?.ProgressToken,
				ApplicationGetInfoToolName,
				() => applicationInfoService.GetApplicationInfo(
					args.EnvironmentName,
					args.Id,
					args.Code),
				cancellationToken).ConfigureAwait(false);
			return ApplicationToolHelper.CreateContextResponse(ApplicationToolResultMapper.Map(result));
		} catch (Exception ex) {
			return ApplicationToolHelper.CreateContextErrorResponse(SensitiveErrorTextRedactor.Redact(ex.Message));
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
	[McpServerTool(Name = ApplicationCreateToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Creates a new application in Creatio through backend MCP and returns installed application identity plus the created package and entity context. Long-running: streams notifications/progress while working — await completion and do not retry on a perceived timeout.")]
	public async Task<ApplicationContextResponse> ApplicationCreate(
		[Description("Parameters: environment-name, name, code (required); template-code (optional, defaults to AppFreedomUI — the stable recommended template); description, icon-background, icon-id, client-type-id, with-mobile-pages (optional, defaults to true; set false for a web-only app to skip mobile pages)")]
		[Required]
		ApplicationCreateArgs args,
		global::ModelContextProtocol.Server.McpServer server,
		RequestContext<CallToolRequestParams> requestContext,
		CancellationToken cancellationToken = default) {
		try {
			ValidateCreateArgs(args);
			string effectiveTemplateCode = string.IsNullOrWhiteSpace(args.TemplateCode) ? "AppFreedomUI" : args.TemplateCode.Trim();
			ApplicationOptionalTemplateData? optionalTemplateData = ApplicationToolHelper.ParseOptionalTemplateData(args.OptionalTemplateDataJson);
			(ApplicationDataForgeResult dataForge, ApplicationInfoResult result) = await McpProgressHeartbeat.RunWithProgressAsync(
				server,
				requestContext?.Params?.ProgressToken,
				ApplicationCreateToolName,
				() => {
					ApplicationDataForgeResult forge = enrichmentService.Enrich(
						args,
						optionalTemplateData,
						cancellationToken);
					ApplicationInfoResult created = applicationCreateService.CreateApplication(
						args.EnvironmentName,
						new ApplicationCreateRequest(
							args.Name,
							args.Code,
							args.Description,
							effectiveTemplateCode,
							args.IconId,
							args.IconBackground,
							args.ClientTypeId,
							optionalTemplateData,
							args.WithMobilePages));
					return (forge, created);
				},
				cancellationToken).ConfigureAwait(false);
			return ApplicationToolHelper.CreateContextResponse(
				ApplicationToolResultMapper.Map(result),
				dataForge);
		} catch (Exception ex) {
			return ApplicationToolHelper.CreateContextErrorResponse(SensitiveErrorTextRedactor.Redact(ex.Message));
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
[McpServerToolType]
public sealed class ApplicationSectionCreateTool(IApplicationSectionCreateService applicationSectionCreateService) {
	/// <summary>
	/// Stable MCP tool name for creating sections in existing Creatio applications.
	/// </summary>
	internal const string ApplicationSectionCreateToolName = "create-app-section";

	/// <summary>
	/// Section-insert budget (ms) used when the call runs behind the MCP response deadline. Far
	/// larger than the synchronous-path 90 s default so the backend section generation — which can
	/// exceed the client's hard request ceiling on a cold/large stand (ENG-91316) — completes in the
	/// background after the tool has already returned an "in-progress, poll" envelope.
	/// </summary>
	internal const int BackgroundInsertTimeoutMs = 600_000;

	/// <summary>
	/// Per-request readback budget (ms) for the background/MCP path. Because the continuation runs
	/// detached on <c>Task.Run(work, CancellationToken.None)</c> — designed to outlive both the response
	/// deadline and a client disconnect — a success-path readback that Creatio accepts but never answers
	/// would otherwise hold a thread-pool worker and HTTP connection for the life of the long-lived server
	/// process. Bounding each readback HTTP call (mirrors the 30 s recovery-readback budget) caps that:
	/// a wedged readback is abandoned and the agent verifies via <c>list-app-sections</c> regardless (ENG-91316).
	/// </summary>
	internal const int BackgroundReadbackTimeoutMs = 30_000;

	/// <summary>
	/// Creates a section in an existing Creatio application and returns structured readback data.
	/// </summary>
	[McpServerTool(Name = ApplicationSectionCreateToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Creates a section inside an existing application in Creatio through backend MCP and returns structured section, entity, and page readback data. Long-running: streams notifications/progress while working — await completion and do not retry on a perceived timeout. If the response is bounded by a deadline before the work finishes it returns error-class=creatio-timeout with section-created=in-progress: the section is still being created server-side — do NOT retry create-app-section (that would duplicate it) and do NOT fall back to create-page; instead poll list-app-sections / get-app-info until the section and its <Code>_ListPage / <Code>_FormPage appear. On failure the response carries error-class (transport | creatio-timeout | server-error), section-created (true | false | unknown | in-progress), and retry-guidance — follow that guidance instead of blind retries.")]
	public async Task<ApplicationSectionContextResponse> ApplicationSectionCreate(
		[Description("Parameters: environment-name, application-code, caption (required); description, entity-schema-name, code, icon-background, with-mobile-pages (optional). entity-schema-name must reference an existing object (validated before creation); several sections may target the same object, so reuse is allowed. The section code is generated from the caption; a non-Latin caption (for example 'Контакти') cannot produce a valid Latin code, so pass an explicit code such as code='Contacts'. If the object does not exist, creation fails with a 'does not exist' error; on a detail-less rejection a section with that code may already exist — inspect existing sections with list-app-sections.")]
		[Required]
		ApplicationSectionCreateArgs args,
		global::ModelContextProtocol.Server.McpServer server,
		RequestContext<CallToolRequestParams> requestContext,
		CancellationToken cancellationToken = default) {
		try {
			ValidateSectionCreateArgs(args);
			string resolvedIconBackground = args.IconBackground;
			// Resolve a supplied color with elicitation disabled (server: null): an unknown value fails
			// fast instead of prompting a client that may advertise elicitation but never answer. A
			// missing color is left for the service to assign a default.
			if (!string.IsNullOrWhiteSpace(resolvedIconBackground)) {
				resolvedIconBackground = await SectionIconPalette.ResolveAsync(
					server: null, args.IconBackground, args.Caption, cancellationToken).ConfigureAwait(false);
			}
			ApplicationSectionCreateResult result = await McpProgressHeartbeat.RunWithProgressAndDeadlineAsync(
				server,
				requestContext?.Params?.ProgressToken,
				ApplicationSectionCreateToolName,
				() => applicationSectionCreateService.CreateSection(
					args.EnvironmentName,
					new ApplicationSectionCreateRequest(
						args.ApplicationCode,
						args.Caption,
						args.Description,
						args.EntitySchemaName,
						args.WithMobilePages,
						resolvedIconBackground,
						args.CaptionCulture,
						args.Code),
					BackgroundInsertTimeoutMs,
					BackgroundReadbackTimeoutMs),
				deadline: null,
				cancellationToken: cancellationToken).ConfigureAwait(false);
			return ApplicationToolHelper.CreateSectionContextResponse(ApplicationToolResultMapper.Map(result));
		} catch (McpResponseDeadlineExceededException) {
			return ApplicationToolHelper.CreateSectionInProgressResponse(args.Caption, args.Code);
		} catch (ApplicationSectionCreateException ex) {
			return ApplicationToolHelper.CreateSectionContextErrorResponse(ex);
		} catch (Exception ex) {
			return ApplicationToolHelper.CreateSectionContextErrorResponse(SensitiveErrorTextRedactor.Redact(ex.Message));
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
[McpServerToolType]
public sealed class ApplicationSectionUpdateTool(IApplicationSectionUpdateService applicationSectionUpdateService) {
	/// <summary>
	/// Stable MCP tool name for updating sections in existing Creatio applications.
	/// </summary>
	internal const string ApplicationSectionUpdateToolName = "update-app-section";

	/// <summary>
	/// Updates metadata of a section in an existing Creatio application and returns structured before and after readback data.
	/// </summary>
	[McpServerTool(Name = ApplicationSectionUpdateToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Updates metadata of a section inside an existing application in Creatio through backend MCP and returns structured section readback data before and after the update. Long-running: streams notifications/progress while working — await completion and do not retry on a perceived timeout.")]
	public async Task<ApplicationSectionUpdateContextResponse> ApplicationSectionUpdate(
		[Description("Parameters: environment-name, application-code, section-code (required); caption, description, icon-id, icon-background (optional partial update fields)")]
		[Required]
		ApplicationSectionUpdateArgs args,
		global::ModelContextProtocol.Server.McpServer server,
		RequestContext<CallToolRequestParams> requestContext,
		CancellationToken cancellationToken = default) {
		try {
			ValidateSectionUpdateArgs(args);
			string resolvedIconBackground = args.IconBackground;
			// Resolve a supplied color with elicitation disabled (server: null) so an unknown value
			// fails fast instead of prompting a client that may never answer.
			if (!string.IsNullOrWhiteSpace(resolvedIconBackground)) {
				resolvedIconBackground = await SectionIconPalette.ResolveAsync(
					server: null, args.IconBackground, args.Caption ?? args.SectionCode, cancellationToken).ConfigureAwait(false);
			}
			ApplicationSectionUpdateResult result = await McpProgressHeartbeat.RunWithProgressAsync(
				server,
				requestContext?.Params?.ProgressToken,
				ApplicationSectionUpdateToolName,
				() => applicationSectionUpdateService.UpdateSection(
					args.EnvironmentName,
					new ApplicationSectionUpdateRequest(
						args.ApplicationCode,
						args.SectionCode,
						args.Caption,
						args.Description,
						args.IconId,
						resolvedIconBackground)),
				cancellationToken).ConfigureAwait(false);
			return ApplicationToolHelper.CreateSectionUpdateContextResponse(ApplicationToolResultMapper.Map(result));
		} catch (Exception ex) {
			return ApplicationToolHelper.CreateSectionUpdateContextErrorResponse(SensitiveErrorTextRedactor.Redact(ex.Message));
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
[McpServerToolType]
public sealed class ApplicationSectionDeleteTool(IApplicationSectionDeleteService applicationSectionDeleteService) {
	/// <summary>
	/// Stable MCP tool name for deleting sections from existing Creatio applications.
	/// </summary>
	internal const string ApplicationSectionDeleteToolName = "delete-app-section";

	/// <summary>
	/// Deletes a section from an existing Creatio application and returns structured readback of the deleted section.
	/// </summary>
	[McpServerTool(Name = ApplicationSectionDeleteToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Deletes a section from an existing application in Creatio through backend MCP and returns structured deleted-section readback data. Long-running: streams notifications/progress while working — await completion and do not retry on a perceived timeout.")]
	public async Task<ApplicationSectionDeleteContextResponse> ApplicationSectionDelete(
		[Description("Parameters: environment-name, application-code, section-code (all required)")]
		[Required]
		ApplicationSectionDeleteArgs args,
		global::ModelContextProtocol.Server.McpServer server,
		RequestContext<CallToolRequestParams> requestContext,
		CancellationToken cancellationToken = default) {
		try {
			ValidateSectionDeleteArgs(args);
			ApplicationSectionDeleteResult result = await McpProgressHeartbeat.RunWithProgressAsync(
				server,
				requestContext?.Params?.ProgressToken,
				ApplicationSectionDeleteToolName,
				() => applicationSectionDeleteService.DeleteSection(
					args.EnvironmentName,
					new ApplicationSectionDeleteRequest(
						args.ApplicationCode,
						args.SectionCode,
						args.DeleteEntitySchema ?? false)),
				cancellationToken).ConfigureAwait(false);
			return ApplicationToolHelper.CreateSectionDeleteContextResponse(ApplicationToolResultMapper.Map(result));
		} catch (Exception ex) {
			return ApplicationToolHelper.CreateSectionDeleteContextErrorResponse(SensitiveErrorTextRedactor.Redact(ex.Message));
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
	internal const string ApplicationSectionGetListToolName = "list-app-sections";

	/// <summary>
	/// Returns all sections of an existing Creatio application.
	/// </summary>
	[McpServerTool(Name = ApplicationSectionGetListToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Gets the list of sections inside an existing application in Creatio through backend MCP and returns structured section list data. Long-running: streams notifications/progress while working — await completion and do not retry on a perceived timeout.")]
	public async Task<ApplicationSectionListContextResponse> ApplicationSectionGetList(
		[Description("Parameters: environment-name, application-code (both required)")]
		[Required]
		ApplicationSectionGetListArgs args,
		global::ModelContextProtocol.Server.McpServer server,
		RequestContext<CallToolRequestParams> requestContext,
		CancellationToken cancellationToken = default) {
		try {
			if (string.IsNullOrWhiteSpace(args.ApplicationCode)) {
				throw new ArgumentException("application-code is required.");
			}

			ApplicationSectionGetListResult result = await McpProgressHeartbeat.RunWithProgressAsync(
				server,
				requestContext?.Params?.ProgressToken,
				ApplicationSectionGetListToolName,
				() => applicationSectionGetListService.GetSections(
					args.EnvironmentName,
					new ApplicationSectionGetListRequest(args.ApplicationCode)),
				cancellationToken).ConfigureAwait(false);
			return ApplicationToolHelper.CreateSectionListContextResponse(ApplicationToolResultMapper.Map(result));
		} catch (Exception ex) {
			return ApplicationToolHelper.CreateSectionListContextErrorResponse(SensitiveErrorTextRedactor.Redact(ex.Message));
		}
	}
}
