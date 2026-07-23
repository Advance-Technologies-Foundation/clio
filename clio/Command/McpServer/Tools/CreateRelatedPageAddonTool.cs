using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Command.RelatedPages;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class CreateRelatedPageAddonTool(
	CreateRelatedPageAddonCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<CreateRelatedPageAddonOptions>(command, logger, commandResolver) {

	internal const string ToolName = "create-related-page-addon";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Configure the RelatedPage add-on for an object (entity schema): which Freedom UI pages open by default and for adding records, optionally per audience and per type. " +
		"Per-page role-name 'All external users' binds the PORTAL (self-service) audience and 'All employees' the internal one. " +
		"Writes the RelatedPage add-on via AddonSchemaDesignerService and rebuilds static content. " +
		"The pages list fully REPLACES the object's current related-page configuration; an EMPTY pages list clears all bindings (reset to inline — the effective delete). " +
		"A GENERAL base default page is MANDATORY: always include an is-default entry with no type-column-value scoped to the general audience — the 'All employees' role (or no role) — as the page opened for a record and the fallback for any record TYPE with no dedicated set (the general base is the INTERNAL audience; portal users still need their own 'All external users' default). The default page also serves record creation, so add a separate is-add page only when you want a DIFFERENT add page. Portal ('All external users') and type-specific pages layer on top; never bind only portal, only typed, or only add pages without the general base. " +
		"Set schema-type=mobile to write the MobileRelatedPage add-on instead (the object's default mobile edit page in the Creatio Mobile app) — pass a single is-default page with no role and no type-column-value; roles, record types and portal audiences are web-only. Used at the end of a web→mobile conversion to register the converted mobile form page. " +
			"Resolve page-schema-name values with list-pages and the object/package with get-app-info. " +
		"Call get-guidance with name related-page-binding to learn the full flow. Prefer environment-name; keep direct connection args for emergency fallback only.")]
	public CreateRelatedPageAddonResponse CreateRelatedPageAddon(
		[Description("Parameters: entity-schema-name, package-name, pages (required); type-column-uid optional; environment-name preferred; uri/login/password emergency fallback only.")]
		[Required] CreateRelatedPageAddonArgs args) {
		if (string.IsNullOrWhiteSpace(args?.EntitySchemaName)) {
			return new CreateRelatedPageAddonResponse { Success = false, Error = RelatedPageAddonMessages.EntitySchemaNameRequired };
		}
		if (string.IsNullOrWhiteSpace(args.PackageName)) {
			return new CreateRelatedPageAddonResponse { Success = false, Error = RelatedPageAddonMessages.PackageNameRequired };
		}
		if (args.Pages is null) {
			return new CreateRelatedPageAddonResponse { Success = false, Error = RelatedPageAddonMessages.PagesRequired };
		}
		if (args.Pages.Any(page => page is null)) {
			return new CreateRelatedPageAddonResponse { Success = false, Error = RelatedPageAddonMessages.PagesEntryRequired };
		}

		CreateRelatedPageAddonOptions options = new() {
			EntitySchemaName = args.EntitySchemaName,
			PackageName = args.PackageName,
			TypeColumnUId = args.TypeColumnUId,
			SchemaType = args.SchemaType,
			Pages = args.Pages
				.Select(p => new RelatedPageSpec(
					p.PageSchemaName,
					p.IsDefault ?? false,
					p.IsAdd ?? false,
					p.IsSspDefault ?? false,
					p.Role,
					p.TypeColumnValue,
					p.RoleName,
					p.PageSchemaUId))
				.ToList(),
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};

		return ExecuteWithCleanLog(() => {
			CreateRelatedPageAddonCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<CreateRelatedPageAddonCommand>(options);
			} catch (Exception ex) {
				return new CreateRelatedPageAddonResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryCreate(options, out CreateRelatedPageAddonResponse response);
			return response;
		});
	}
}

public sealed record CreateRelatedPageAddonArgs(
	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("Object (entity schema) name the related pages belong to, e.g. 'UsrDeliveryItem'.")]
	[property: Required]
	string EntitySchemaName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Package that owns the add-on configuration.")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("pages")]
	[property: Description("Related-page entries. Fully replaces the object's current configuration. Mark exactly one entry per role/type bucket with is-default=true (the page opened on a record) and one with is-add=true (the page used to add a record); the same page may serve both. A general base default entry is MANDATORY — always include one with is-default=true, no type-column-value, and the 'All employees' role (or no role): the main record page and the fallback for any record TYPE. Portal ('All external users') users need their own default entry — the internal base is not a substitute for the portal audience. The default also serves adding, so add a separate is-add entry only for a DIFFERENT add page. Portal ('All external users') and type-specific pages layer on top; never bind only portal, only typed, or only add pages without the general base. Send an EMPTY list to clear ALL bindings (reset to inline) — that is the effective delete; the base-default rule does not apply to an intentional clear.")]
	[property: Required]
	IReadOnlyList<RelatedPageArg> Pages,

	[property: JsonPropertyName("type-column-uid")]
	[property: Description("Optional UId of the type column that drives type-specific page sets. Omit for a single page set.")]
	string? TypeColumnUId,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name, e.g. 'local'. Preferred for normal MCP work.")]
	string? EnvironmentName,

	[property: JsonPropertyName("uri")]
	[property: Description("Direct Creatio URL. Use only when bootstrap is broken or before the environment can be registered through reg-web-app.")]
	string? Uri,

	[property: JsonPropertyName("login")]
	[property: Description("Direct Creatio login paired with `uri`. Emergency fallback only.")]
	string? Login,

	[property: JsonPropertyName("password")]
	[property: Description("Direct Creatio password paired with `uri`. Emergency fallback only.")]
	string? Password,

	[property: JsonPropertyName("schema-type")]
	[property: Description("Target UI: 'web' (RelatedPage add-on, default) or 'mobile' (MobileRelatedPage add-on — the page opened for a record in the Creatio Mobile app). For 'mobile' pass a single is-default page with no role and no type-column-value (the object's default mobile edit page); roles, record types and portal audiences are web-only concepts.")]
	string? SchemaType = null
);

public sealed record RelatedPageArg(
	[property: JsonPropertyName("page-schema-name")]
	[property: Description("Freedom UI page schema name, e.g. 'UsrDeliveryItemFormPage'. Required UNLESS page-schema-uid is supplied. When both are present, page-schema-uid wins and the name is ignored.")]
	string PageSchemaName,

	[property: JsonPropertyName("is-default")]
	[property: Description("When true, this page opens by default when a record is opened. Default false.")]
	bool? IsDefault,

	[property: JsonPropertyName("is-add")]
	[property: Description("When true, this page is used when adding a new record. Default false.")]
	bool? IsAdd,

	[property: JsonPropertyName("is-ssp-default")]
	[property: Description("Low-level RelatedPagesMetadata IsSspDefault flag; leave false. This is NOT how the portal audience is set — to target portal (self-service) users, add a page entry with role-name 'All external users'. Default false.")]
	bool? IsSspDefault,

	[property: JsonPropertyName("role")]
	[property: Description("Optional audience role UId. Only two audiences are supported: 'All employees' (a29a3ba5-4b0d-de11-9a51-005056c00008) and the portal 'All external users' (720b771c-e7a7-4f31-9cfb-52cd21c3739f); any other role UId is rejected. Omit for all users (the general audience). Prefer role-name.")]
	string? Role,

	[property: JsonPropertyName("type-column-value")]
	[property: Description("Optional type-column value (used with type-column-uid) for a type-specific page set.")]
	string? TypeColumnValue,

	[property: JsonPropertyName("role-name")]
	[property: Description("Optional audience role NAME (alternative to role). Only 'All external users' (portal/self-service) and 'All employees' (internal) are supported — any other role name is rejected, because the Interface Designer offers no other audience. Omit for all users (the general audience).")]
	string? RoleName = null,

	[property: JsonPropertyName("page-schema-uid")]
	[property: Description("Optional explicit page schema UId. Prefer this when replaying a get-related-page-addon result: it is used verbatim (page-schema-name is not resolved), so a page whose name no longer reverse-resolves still round-trips instead of being silently dropped. Omit to resolve by page-schema-name.")]
	string? PageSchemaUId = null
);
