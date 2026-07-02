using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;

namespace Clio.Command.RelatedPages;

/// <summary>
/// Input for configuring the <c>RelatedPage</c> add-on of an object. <see cref="Pages"/> fully
/// replaces the object's current related-page configuration.
/// </summary>
public sealed record RelatedPageAddonRequest(
	string PackageName,
	string EntitySchemaName,
	IReadOnlyList<RelatedPageSpec> Pages,
	string TypeColumnUId);

/// <summary>
/// Outcome of a <see cref="IRelatedPageAddonService.Create"/> call.
/// </summary>
public sealed record RelatedPageAddonResult(
	string EntitySchemaUId,
	string PackageUId,
	int PageCount,
	string AddonName);

/// <summary>
/// Identifies the object whose current <c>RelatedPage</c> configuration should be read.
/// </summary>
public sealed record RelatedPageAddonReadRequest(
	string PackageName,
	string EntitySchemaName);

/// <summary>
/// The object's current <c>RelatedPage</c> configuration, decoded from the add-on metadata. Lets a caller
/// read the existing page set before a (replace-not-merge) <see cref="IRelatedPageAddonService.Create"/>,
/// so a single page can be added or removed without losing the rest.
/// </summary>
public sealed record RelatedPageAddonReadResult(
	string EntitySchemaName,
	string EntitySchemaUId,
	string PackageName,
	string PackageUId,
	string AddonName,
	string TypeColumnUId,
	int PageCount,
	IReadOnlyList<RelatedPageEntry> Pages);

/// <summary>
/// One decoded entry of the <c>RelatedPage</c> add-on's page set. Raw UIds are always returned (for a safe
/// read-modify-write round-trip); names are the best-effort reverse resolution (page schema name; the
/// standard platform audience role names) and may be <c>null</c> when not resolvable.
/// </summary>
public sealed record RelatedPageEntry(
	string PageSchemaUId,
	string PageSchemaName,
	bool IsDefault,
	bool IsAdd,
	bool IsSspDefault,
	string Role,
	string RoleName,
	string TypeColumnValue);

/// <summary>
/// Configures the <c>RelatedPage</c> add-on attached to an object (entity schema). Public seam over the
/// internal <see cref="IAddonSchemaDesignerClient"/> so the public command can depend on it.
/// </summary>
public interface IRelatedPageAddonService {
	/// <summary>
	/// Resolves the object/package/pages, then performs the add-on round-trip:
	/// <c>GetSchema</c> (the server auto-provisions the descriptor), replace the <c>Pages</c> metadata,
	/// <c>SaveSchema</c>, reset the client script cache, and rebuild static configuration.
	/// </summary>
	RelatedPageAddonResult Create(RelatedPageAddonRequest request);

	/// <summary>
	/// Reads the object's current <c>RelatedPage</c> configuration: resolves the object/package, fetches the
	/// add-on via <c>GetSchema</c>, and decodes the page set (with best-effort reverse resolution of page and
	/// role names). Read-only — performs no save/rebuild. Pairs with <see cref="Create"/> so a caller can do a
	/// safe read-modify-write instead of blindly replacing the configuration.
	/// </summary>
	RelatedPageAddonReadResult Get(RelatedPageAddonReadRequest request);
}

internal sealed class RelatedPageAddonService(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IAddonSchemaDesignerClient addonSchemaDesignerClient,
	IRemoteEntitySchemaDesignerClient entitySchemaDesignerClient)
	: IRelatedPageAddonService {

	private const string RelatedPageAddonName = "RelatedPage";
	private const string EntitySchemaManagerName = "EntitySchemaManager";
	private const string EmployeesRoleName = "All employees";

	/// <summary>
	/// The standard platform audience roles are seeded <c>SysAdminUnit</c> records with fixed Ids that are
	/// identical across Creatio installs — "All employees" (<c>a29a3ba5-…</c>) and the portal "All external
	/// users" role, which Creatio core references by the seeded constant <c>SysAdminUnitAllPortalUsersId</c>.
	/// Mapping them by their fixed Ids avoids a lookup for the common audiences and is safe because the Ids are
	/// invariant. Any other (custom) role name still resolves through
	/// <see cref="PageSchemaMetadataHelper.QueryRoleUId"/>, which excludes user rows and rejects an ambiguous
	/// (multi-match) name.
	/// </summary>
	private static readonly IReadOnlyDictionary<string, string> KnownPlatformRoleIds =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			["All employees"] = "a29a3ba5-4b0d-de11-9a51-005056c00008",
			["All external users"] = "720b771c-e7a7-4f31-9cfb-52cd21c3739f"
		};

	// Reverse of KnownPlatformRoleIds (UId -> name), used by the read path to surface friendly audience names
	// for the standard platform roles without a SysAdminUnit lookup.
	private static readonly IReadOnlyDictionary<string, string> KnownPlatformRoleNamesById =
		KnownPlatformRoleIds.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Validates the whole request before any remote call: required fields, a mandatory base default page,
	/// type-column consistency, and audience-role well-formedness (role/role-name are mutually exclusive and an
	/// explicit role must be a GUID). Centralizing the guard clauses keeps <see cref="Create"/> as orchestration.
	/// </summary>
	private static void ValidateRequest(RelatedPageAddonRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		if (string.IsNullOrWhiteSpace(request.EntitySchemaName)) {
			throw new ArgumentException(RelatedPageAddonMessages.EntitySchemaNameRequired);
		}
		if (string.IsNullOrWhiteSpace(request.PackageName)) {
			throw new ArgumentException(RelatedPageAddonMessages.PackageNameRequired);
		}
		if (request.Pages is null || request.Pages.Count == 0) {
			throw new ArgumentException("At least one page is required.");
		}
		// A per-page type-column-value is meaningless without the type column it keys on: the platform matches
		// typed page sets by (TypeColumnUId, TypeColumnValue), so a typed page with no TypeColumnUId can never be
		// selected for a record type. Reject the inconsistent combination instead of silently saving dead pages.
		if (string.IsNullOrWhiteSpace(request.TypeColumnUId)
			&& request.Pages.Any(page => !string.IsNullOrWhiteSpace(page.TypeColumnValue))) {
			throw new ArgumentException(
				"type-column-uid is required when any page sets a type-column-value: a typed page set needs the "
				+ "type column it is keyed by, otherwise the platform can never match those pages to a record type.");
		}
		// type-column-uid is the entity column's u-id; reject a non-GUID up front (symmetric with the role and
		// package UId guards) instead of persisting a malformed value the platform can never resolve.
		if (!string.IsNullOrWhiteSpace(request.TypeColumnUId) && !Guid.TryParse(request.TypeColumnUId.Trim(), out _)) {
			throw new ArgumentException(
				$"type-column-uid '{request.TypeColumnUId}' is not a valid GUID; it must be the entity column's u-id.");
		}
		// A page entry that sets BOTH an explicit role UId and a role name is ambiguous: role wins and role-name
		// would be silently ignored. Reject it so the caller's intended audience is never quietly dropped.
		if (request.Pages.Any(page =>
				!string.IsNullOrWhiteSpace(page.Role) && !string.IsNullOrWhiteSpace(page.RoleName))) {
			throw new ArgumentException(
				"A page entry sets both role and role-name; provide only one — role-name to resolve a role by name, "
				+ "or role for an explicit SysAdminUnit UId.");
		}
		// An explicit role must be a SysAdminUnit GUID. Validated here — before any remote call — symmetric with
		// the guards above (it previously fired inside BuildPages, after several round-trips).
		foreach (RelatedPageSpec page in request.Pages) {
			if (!string.IsNullOrWhiteSpace(page.Role) && !Guid.TryParse(page.Role.Trim(), out _)) {
				throw new ArgumentException(
					$"role '{page.Role}' is not a valid SysAdminUnit GUID; use role-name to resolve a role by name.");
			}
		}
		// A base default page for the GENERAL audience is mandatory: at least one is-default entry with no
		// type-column-value whose audience is general — role-less OR the "All employees" role (the shape the
		// Interface Designer writes for the base set). It is the page opened for a record and the fallback for any
		// type; portal ("All external users") and other role- or type-specific sets are layered on top. So an
		// add-only, typed-only, or audience-scoped-only configuration (e.g. a lone portal default) is rejected: the
		// designer does not guard against it, but it leaves the general audience with no page, so the tool must.
		// Checked after the role guards so an ambiguous/malformed role reports its own error first.
		if (!request.Pages.Any(page => page.IsDefault
				&& string.IsNullOrWhiteSpace(page.TypeColumnValue)
				&& IsGeneralAudience(page))) {
			throw new ArgumentException(
				"A base default page for the general audience is required: include one page with is-default=true, no "
				+ "type-column-value, and either no role or the 'All employees' role (the page opened for a record and "
				+ "the fallback for any type or audience without a dedicated set). Portal ('All external users') and "
				+ "type-specific pages are layered on top; a portal-only or other audience-scoped-only binding is rejected.");
		}
	}

	// A page targets the GENERAL audience when it is role-less (applies to everyone) or scoped to the "All
	// employees" role — the base set the designer writes. Portal ("All external users") and custom roles are not
	// general, so a configuration whose only untyped default is one of those has no base page for the general audience.
	private static bool IsGeneralAudience(RelatedPageSpec page) =>
		(string.IsNullOrWhiteSpace(page.Role) && string.IsNullOrWhiteSpace(page.RoleName))
		|| string.Equals(page.RoleName?.Trim(), EmployeesRoleName, StringComparison.OrdinalIgnoreCase)
		|| string.Equals(page.Role?.Trim(), KnownPlatformRoleIds[EmployeesRoleName], StringComparison.OrdinalIgnoreCase);

	public RelatedPageAddonResult Create(RelatedPageAddonRequest request) {
		ValidateRequest(request);

		(string packageUId, string packageError) = PageSchemaMetadataHelper.QueryPackageUId(
			applicationClient, serviceUrlBuilder, request.PackageName);
		if (packageError != null) {
			throw new InvalidOperationException(packageError);
		}
		if (!Guid.TryParse(packageUId, out Guid packageId)) {
			throw new InvalidOperationException(
				$"Resolved package '{request.PackageName}' UId '{packageUId}' is not a valid GUID.");
		}

		EntityDesignSchemaDto entitySchema = ResolveEntitySchema(request.EntitySchemaName, packageId, request.PackageName);

		IReadOnlyDictionary<string, string> roleByName = ResolveRoleNames(request.Pages);
		JsonArray pages = BuildPages(request.Pages, roleByName);
		var metadata = new JsonObject {
			["Pages"] = pages,
			["TypeColumnUId"] = string.IsNullOrWhiteSpace(request.TypeColumnUId) ? null : request.TypeColumnUId.Trim()
		};

		var addonRequest = new AddonGetRequestDto {
			AddonName = RelatedPageAddonName,
			TargetSchemaUId = entitySchema.UId,
			// Mirror the Interface Designer / Business Rule add-on path: a replacing or derived object
			// reports the parent it extends; a plain object reports none (Guid.Empty).
			TargetParentSchemaUId = entitySchema.ParentSchema?.UId ?? Guid.Empty,
			TargetPackageUId = packageId,
			TargetSchemaManagerName = EntitySchemaManagerName,
			UseFullHierarchy = true
		};

		AddonSchemaDto schema = addonSchemaDesignerClient.GetSchema(addonRequest);
		// Replace the add-on metadata wholesale. The RelatedPage add-on's MetaData is exactly
		// {"Pages":[...],"TypeColumnUId":...} (verified backend contract) — it carries no sibling fields that a
		// full replace could silently drop — and the Interface Designer likewise rewrites the whole document on
		// save. RelatedPageAddonServiceTests pins this so a future contract change fails a test instead of silently
		// dropping data.
		schema.MetaData = metadata.ToJsonString();

		addonSchemaDesignerClient.SaveSchema(schema);
		// Make the saved add-on visible to the current session without a full reload, then rebuild static
		// client content so online and offline users pick up the change.
		addonSchemaDesignerClient.ResetClientScriptCache();
		addonSchemaDesignerClient.BuildConfiguration();

		return new RelatedPageAddonResult(
			entitySchema.UId.ToString("D"), packageUId, pages.Count, RelatedPageAddonName);
	}

	public RelatedPageAddonReadResult Get(RelatedPageAddonReadRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		if (string.IsNullOrWhiteSpace(request.EntitySchemaName)) {
			throw new ArgumentException(RelatedPageAddonMessages.EntitySchemaNameRequired);
		}
		if (string.IsNullOrWhiteSpace(request.PackageName)) {
			throw new ArgumentException(RelatedPageAddonMessages.PackageNameRequired);
		}

		(string packageUId, string packageError) = PageSchemaMetadataHelper.QueryPackageUId(
			applicationClient, serviceUrlBuilder, request.PackageName);
		if (packageError != null) {
			throw new InvalidOperationException(packageError);
		}
		if (!Guid.TryParse(packageUId, out Guid packageId)) {
			throw new InvalidOperationException(
				$"Resolved package '{request.PackageName}' UId '{packageUId}' is not a valid GUID.");
		}

		EntityDesignSchemaDto entitySchema = ResolveEntitySchema(request.EntitySchemaName, packageId, request.PackageName);

		var addonRequest = new AddonGetRequestDto {
			AddonName = RelatedPageAddonName,
			TargetSchemaUId = entitySchema.UId,
			TargetParentSchemaUId = entitySchema.ParentSchema?.UId ?? Guid.Empty,
			TargetPackageUId = packageId,
			TargetSchemaManagerName = EntitySchemaManagerName,
			UseFullHierarchy = true
		};

		// Read-only: GetSchema returns the (server auto-provisioned) add-on with its current metadata; no save.
		AddonSchemaDto schema = addonSchemaDesignerClient.GetSchema(addonRequest);
		IReadOnlyList<RelatedPageEntry> pages = DecodePages(schema.MetaData, out string typeColumnUId);

		return new RelatedPageAddonReadResult(
			request.EntitySchemaName, entitySchema.UId.ToString("D"), request.PackageName, packageUId,
			RelatedPageAddonName, typeColumnUId, pages.Count, pages);
	}

	/// <summary>
	/// Decodes the add-on's <c>metaData</c> JSON string into the page set, mirroring the shape
	/// <see cref="BuildPages"/> writes. Raw UIds are preserved; page and role names are best-effort reverse
	/// resolutions (null when not resolvable). Returns an empty list for a never-configured object.
	/// </summary>
	private IReadOnlyList<RelatedPageEntry> DecodePages(string metaData, out string typeColumnUId) {
		typeColumnUId = null;
		var entries = new List<RelatedPageEntry>();
		if (string.IsNullOrWhiteSpace(metaData)) {
			return entries;
		}
		JsonNode root = JsonNode.Parse(metaData);
		typeColumnUId = root?["TypeColumnUId"]?.GetValue<string>();
		if (root?["Pages"] is not JsonArray pages) {
			return entries;
		}
		foreach (JsonNode page in pages) {
			if (page is null) {
				continue;
			}
			string pageSchemaUId = page["PageSchemaUId"]?.GetValue<string>();
			string role = page["Role"]?.GetValue<string>();
			entries.Add(new RelatedPageEntry(
				pageSchemaUId,
				ResolvePageName(pageSchemaUId),
				page["IsDefault"]?.GetValue<bool>() ?? false,
				page["Actions"]?["Add"]?.GetValue<bool>() ?? false,
				page["IsSspDefault"]?.GetValue<bool>() ?? false,
				role,
				ResolveRoleName(role),
				page["TypeColumnValue"]?.GetValue<string>()));
		}
		return entries;
	}

	/// <summary>Best-effort reverse resolution of a page <c>PageSchemaUId</c> to its schema name; null if absent/unresolvable.</summary>
	private string ResolvePageName(string pageSchemaUId) =>
		PageSchemaMetadataHelper.QueryPageSchemaNameByUId(applicationClient, serviceUrlBuilder, pageSchemaUId);

	/// <summary>Reverse resolution of a role UId to the standard platform audience name; null for custom/unknown roles.</summary>
	private static string ResolveRoleName(string roleUId) =>
		!string.IsNullOrWhiteSpace(roleUId) && KnownPlatformRoleNamesById.TryGetValue(roleUId, out string name)
			? name
			: null;

	/// <summary>
	/// Resolves the target object (entity schema) through the entity schema designer — the same source the
	/// Business Rule add-on path uses — so the add-on request carries the object's real <c>UId</c> and,
	/// for a replacing/derived schema, its parent schema <c>UId</c> (none for a plain object). Returns a
	/// clean "not found" failure when the object does not exist in the package.
	/// </summary>
	private EntityDesignSchemaDto ResolveEntitySchema(string entitySchemaName, Guid packageId, string packageName) {
		// Use the throwing GetSchemaDesignItem (like the business-rule path), NOT TryGetSchemaDesignItem: the
		// Try variant returns null on an HTML / auth-redirect / server-error page, which would be misreported as
		// "object not found" below. GetSchemaDesignItem surfaces those transport/server faults with a clear error.
		// Note: it returns Clio.Command.EntitySchemaDesigner.DesignerResponse, which collides by simple name with
		// Clio.Command.DesignerResponse — let the compiler infer the type.
		var response = entitySchemaDesignerClient.GetSchemaDesignItem(
			new GetSchemaDesignItemRequestDto {
				Name = entitySchemaName.Trim(),
				PackageUId = packageId,
				UseFullHierarchy = true,
				Cultures = [EntitySchemaDesignerSupport.DefaultCultureName]
			},
			new RemoteCommandOptions());
		return response?.Schema
			?? throw new InvalidOperationException(
				$"Object (entity schema) '{entitySchemaName}' not found in package '{packageName}'. The object must "
				+ "be visible from that package — if it lives elsewhere, add a package dependency.");
	}

	/// <summary>
	/// Resolves every distinct role NAME referenced by the page specs to its <c>SysAdminUnit</c> Id once,
	/// so a role is queried a single time even when several pages target the same audience. An explicit role
	/// UId on a spec wins and is not resolved here. The standard platform audiences resolve to their fixed
	/// seeded Ids; any other role name that cannot be found fails the whole call.
	/// </summary>
	private IReadOnlyDictionary<string, string> ResolveRoleNames(IReadOnlyList<RelatedPageSpec> specs) {
		var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (RelatedPageSpec spec in specs) {
			if (!string.IsNullOrWhiteSpace(spec.Role)
				|| string.IsNullOrWhiteSpace(spec.RoleName)) {
				continue;
			}
			string roleName = spec.RoleName.Trim();
			if (resolved.ContainsKey(roleName)) {
				continue;
			}
			if (KnownPlatformRoleIds.TryGetValue(roleName, out string knownRoleId)) {
				resolved[roleName] = knownRoleId;
				continue;
			}
			(string roleUId, string roleError) = PageSchemaMetadataHelper.QueryRoleUId(
				applicationClient, serviceUrlBuilder, roleName);
			if (roleError != null) {
				throw new InvalidOperationException(roleError);
			}
			resolved[roleName] = roleUId;
		}
		return resolved;
	}

	/// <summary>
	/// Builds the <c>Pages</c> array stored in the add-on's <c>metaData</c> string, mirroring the shape the
	/// Interface Designer sends: each entry carries a fresh <c>UId</c>, the resolved <c>PageSchemaUId</c>,
	/// <c>IsDefault</c>/<c>IsSspDefault</c> flags, an <c>Actions.Add</c> flag, and an optional audience
	/// <c>Role</c> (from an explicit UId or a resolved role name — e.g. the portal "All external users"
	/// role) plus <c>TypeColumnValue</c>.
	/// </summary>
	private JsonArray BuildPages(
		IReadOnlyList<RelatedPageSpec> specs,
		IReadOnlyDictionary<string, string> roleByName) {
		IReadOnlyDictionary<string, string> pageUIdByName = ResolvePageSchemaUIds(specs);
		var pages = new JsonArray();
		foreach (RelatedPageSpec spec in specs) {
			string role = ResolvePageRole(spec, roleByName);
			pages.Add(new JsonObject {
				["UId"] = Guid.NewGuid().ToString("D"),
				["PageSchemaUId"] = pageUIdByName[spec.PageSchemaName.Trim()],
				["IsDefault"] = spec.IsDefault,
				["IsSspDefault"] = spec.IsSspDefault,
				["Actions"] = new JsonObject { ["Add"] = spec.IsAdd },
				["Role"] = string.IsNullOrWhiteSpace(role) ? null : role,
				["TypeColumnValue"] = string.IsNullOrWhiteSpace(spec.TypeColumnValue) ? null : spec.TypeColumnValue.Trim()
			});
		}
		return pages;
	}

	/// <summary>
	/// Resolves every DISTINCT page-schema name to its <c>PageSchemaUId</c> once (case-insensitive), so a page
	/// named by several specs — e.g. the same page used as both the default and the add page — is queried a
	/// single time rather than per entry. Also enforces the non-empty page name and validates each resolved
	/// UId as a GUID (symmetric with the role/package/type-column UId guards).
	/// </summary>
	private IReadOnlyDictionary<string, string> ResolvePageSchemaUIds(IReadOnlyList<RelatedPageSpec> specs) {
		var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (RelatedPageSpec spec in specs) {
			if (string.IsNullOrWhiteSpace(spec.PageSchemaName)) {
				throw new ArgumentException("Each page entry requires a page-schema-name.");
			}
			string pageName = spec.PageSchemaName.Trim();
			if (resolved.ContainsKey(pageName)) {
				continue;
			}
			(string pageUId, string pageError) = PageSchemaMetadataHelper.QueryPageSchemaUId(
				applicationClient, serviceUrlBuilder, pageName);
			if (pageError != null) {
				throw new InvalidOperationException(pageError);
			}
			if (!Guid.TryParse(pageUId, out _)) {
				throw new InvalidOperationException(
					$"Resolved page '{pageName}' UId '{pageUId}' is not a valid GUID.");
			}
			resolved[pageName] = pageUId;
		}
		return resolved;
	}

	/// <summary>
	/// Chooses the audience role UId stored on a page entry: an explicit role UId wins (already validated as a
	/// GUID upfront in <see cref="ValidateRequest"/>), otherwise the role resolved from the spec's role name,
	/// otherwise none (the set applies to all users).
	/// </summary>
	private static string ResolvePageRole(RelatedPageSpec spec, IReadOnlyDictionary<string, string> roleByName) {
		if (!string.IsNullOrWhiteSpace(spec.Role)) {
			return spec.Role.Trim();
		}
		return !string.IsNullOrWhiteSpace(spec.RoleName) ? roleByName[spec.RoleName.Trim()] : null;
	}
}
