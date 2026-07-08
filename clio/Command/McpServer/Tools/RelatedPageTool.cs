using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command.RelatedPages;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Registers an existing Freedom UI page as a RELATED page of an entity — for the web designer
/// (<c>RelatedPage</c> add-on) or the mobile app (<c>MobileRelatedPage</c> add-on), optionally as the
/// entity's default page. Used at the end of a web→mobile conversion to make the converted mobile form
/// page the entity's default mobile edit page, and reusable for registering web/extra related pages.
/// </summary>
[McpServerToolType]
public sealed class RelatedPageTool(
	RegisterRelatedPageCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<RegisterRelatedPageOptions>(command, logger, commandResolver) {

	internal const string ToolName = "register-related-page";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Register an existing Freedom UI page as a RELATED page of an entity. " +
		"schema-type=mobile writes the MobileRelatedPage add-on (the page opened for a record in the Creatio Mobile app); " +
		"schema-type=web writes the RelatedPage add-on (the page opened for a record in the web designer). " +
		"is-default=true makes it the entity's single default page (clears the previous default); is-default=false adds it as an additional related page. " +
		"Typical use: after converting a web form page to mobile, register the mobile page as the entity's default mobile edit page (schema-type=mobile, is-default=true), after the user approves (Gate S). " +
		"Idempotent. The add-on is written into package-name, which must be unlocked/editable.")]
	public CommandExecutionResult RegisterRelatedPage(
		[Description("Parameters: environment-name, package-name, entity-schema-name, page-schema-name (all required); schema-type (web|mobile, default mobile); is-default (default true).")]
		[Required]
		RegisterRelatedPageArgs args) {
		RegisterRelatedPageOptions options = new() {
			EnvironmentName = args.EnvironmentName,
			PackageName = args.PackageName,
			EntitySchemaName = args.EntitySchemaName,
			PageSchemaName = args.PageSchemaName,
			SchemaType = ParseSchemaType(args.SchemaType),
			IsDefault = args.IsDefault ?? true
		};
		return InternalExecute<RegisterRelatedPageCommand>(options);
	}

	private static RelatedPageSchemaType ParseSchemaType(string schemaType) =>
		string.Equals(schemaType, "web", StringComparison.OrdinalIgnoreCase)
			? RelatedPageSchemaType.Web
			: RelatedPageSchemaType.Mobile;
}

/// <summary>
/// MCP argument wrapper for <c>register-related-page</c>.
/// </summary>
public sealed record RegisterRelatedPageArgs {
	[JsonPropertyName("environment-name")]
	[Description("Registered Creatio environment name.")]
	[Required]
	public string EnvironmentName { get; init; } = null!;

	[JsonPropertyName("package-name")]
	[Description("Target package name (must be editable/unlocked) where the related-page add-on is written.")]
	[Required]
	public string PackageName { get; init; } = null!;

	[JsonPropertyName("entity-schema-name")]
	[Description("Entity schema name the page is related to (e.g. 'Lead').")]
	[Required]
	public string EntitySchemaName { get; init; } = null!;

	[JsonPropertyName("page-schema-name")]
	[Description("Existing page schema name to register (e.g. 'UsrLeads_MobileFormPage').")]
	[Required]
	public string PageSchemaName { get; init; } = null!;

	[JsonPropertyName("schema-type")]
	[Description("Target UI: 'mobile' (MobileRelatedPage add-on, default) or 'web' (RelatedPage add-on).")]
	public string SchemaType { get; init; } = "mobile";

	[JsonPropertyName("is-default")]
	[Description("When true (default) the page becomes the entity's single default page; when false it is added as an additional related page.")]
	public bool? IsDefault { get; init; }
}
