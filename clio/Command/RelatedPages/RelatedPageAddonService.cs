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
	IRemoteEntitySchemaDesignerClient entitySchemaDesignerClient,
	ILogger logger)
	: IRelatedPageAddonService {

	private const string RelatedPageAddonName = "RelatedPage";
	private const string EntitySchemaManagerName = "EntitySchemaManager";
	private const string EmployeesRoleName = "All employees";
	private const string PortalRoleName = "All external users";

	/// <summary>
	/// The two audiences a related-page binding supports, mapped to their fixed seeded <c>SysAdminUnit</c> Ids
	/// (identical across Creatio installs): the general audience "All employees" (<c>a29a3ba5-…</c>) and the
	/// portal "All external users" role, which Creatio core references by the seeded constant
	/// <c>SysAdminUnitAllPortalUsersId</c>. Mapping them by their fixed Ids avoids a lookup and is safe because
	/// the Ids are invariant. These are the ONLY audiences the Interface Designer can produce: a custom role
	/// (by name or UId) is rejected up front by <see cref="ValidateRequest"/>, because the platform's runtime
	/// resolution of an arbitrary role in a related-page set has never been verified — writing one would risk a
	/// silently non-working configuration.
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
		if (request.Pages is null) {
			throw new ArgumentException(
				"pages is required (send an empty list to clear all bindings / reset to inline).");
		}
		if (request.Pages.Count == 0) {
			// An explicitly empty page set is a deliberate reset-to-inline: it clears every related-page binding.
			// This is the effective delete — the platform has no add-on delete, and an unconfigured object also
			// reports an empty Pages set — so there is nothing further to validate. The per-page, base-default,
			// and uniqueness guards below apply only to a non-empty configuration.
			return;
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
		// Each page needs a way to identify its page schema: a page-schema-name (resolved to a UId) OR an explicit
		// page-schema-uid (used verbatim — the round-trip vehicle from get-related-page-addon, robust to a page whose
		// name no longer reverse-resolves). Require at least one, and validate an explicit UId as a GUID up front
		// (symmetric with the role / package / type-column UId guards).
		foreach (RelatedPageSpec page in request.Pages) {
			bool hasName = !string.IsNullOrWhiteSpace(page.PageSchemaName);
			bool hasUId = !string.IsNullOrWhiteSpace(page.PageSchemaUId);
			if (!hasName && !hasUId) {
				throw new ArgumentException("Each page entry requires a page-schema-name or a page-schema-uid.");
			}
			if (hasUId && !Guid.TryParse(page.PageSchemaUId.Trim(), out _)) {
				throw new ArgumentException(
					$"page-schema-uid '{page.PageSchemaUId}' is not a valid GUID; it must be the page schema's u-id.");
			}
		}
		// A page may describe its audience by an explicit role UId OR by role-name. get-related-page-addon returns
		// BOTH (the raw UId for a safe round-trip plus the friendly name), so a verbatim read-modify-write replay
		// legitimately carries both. Reject ONLY a genuine conflict — both set but pointing at DIFFERENT audiences
		// (role wins downstream via ResolvePageRole, so a disagreeing role-name would be silently dropped). When
		// they agree (the round-trip case) accept it and let role win. A malformed role is deferred to the GUID
		// guard below; a role-name outside the supported audiences surfaces here as a conflict.
		foreach (RelatedPageSpec page in request.Pages) {
			if (string.IsNullOrWhiteSpace(page.Role) || string.IsNullOrWhiteSpace(page.RoleName)
				|| !Guid.TryParse(page.Role.Trim(), out Guid roleId)) {
				continue;
			}
			bool roleNameAgrees =
				KnownPlatformRoleIds.TryGetValue(page.RoleName.Trim(), out string roleNameUId)
				&& Guid.TryParse(roleNameUId, out Guid roleNameId)
				&& roleNameId == roleId;
			if (!roleNameAgrees) {
				throw new ArgumentException(
					$"A page entry sets both role ('{page.Role.Trim()}') and role-name ('{page.RoleName.Trim()}') and "
					+ "they point at different audiences; provide only one, or make role-name resolve to the same "
					+ "audience as role.");
			}
		}
		// An explicit role must be a SysAdminUnit GUID. Validated here — before any remote call — symmetric with
		// the guards above (it previously fired inside BuildPages, after several round-trips).
		foreach (RelatedPageSpec page in request.Pages) {
			if (!string.IsNullOrWhiteSpace(page.Role) && !Guid.TryParse(page.Role.Trim(), out _)) {
				throw new ArgumentException(
					$"role '{page.Role}' is not a valid SysAdminUnit GUID; use role-name to resolve a role by name.");
			}
		}
		// Strict parity with the Interface Designer: a binding supports ONLY the two audiences the designer can
		// produce — the general audience (no role, or the "All employees" role) and the portal audience (the
		// "All external users" role). A custom role (by name or by a valid-but-other UId) is rejected: the
		// designer offers no such column, and the platform's runtime resolution of an arbitrary role in a
		// related-page set has never been verified, so writing one would risk a config that silently never
		// resolves. Checked after the GUID guard so a malformed role reports its own error first.
		foreach (RelatedPageSpec page in request.Pages) {
			if (!IsRecognizedAudience(page)) {
				throw new ArgumentException(
					"A page entry targets an unsupported audience. Related-page bindings support only the general "
					+ "audience (no role, or the 'All employees' role) and the portal audience (the 'All external "
					+ "users' role); a custom role is not supported.");
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
		// At most one default page and one add page per (audience x type) cell — the shape the designer enforces
		// structurally. Two defaults for the same audience+type is ambiguous (which page opens?), as is two add
		// pages. The SAME page may still be both the default and the add for a cell (one is-default entry + one
		// is-add entry) — the designer's own two-entry pattern — so this guards duplicate FLAGS, not duplicate pages.
		foreach (IGrouping<(string, string), RelatedPageSpec> cell in request.Pages.GroupBy(AudienceTypeCell)) {
			if (cell.Count(page => page.IsDefault) > 1) {
				throw new ArgumentException(
					"More than one default page targets the same audience and type. Each (audience, type) may have "
					+ "only one is-default page; remove the duplicate default.");
			}
			if (cell.Count(page => page.IsAdd) > 1) {
				throw new ArgumentException(
					"More than one add page targets the same audience and type. Each (audience, type) may have only "
					+ "one is-add page; remove the duplicate add page.");
			}
		}
	}

	// A page targets the GENERAL audience when it is role-less (applies to everyone) or scoped to the "All
	// employees" role — the base set the designer writes. Portal ("All external users") and custom roles are not
	// general, so a configuration whose only untyped default is one of those has no base page for the general audience.
	private static bool IsGeneralAudience(RelatedPageSpec page) =>
		(string.IsNullOrWhiteSpace(page.Role) && string.IsNullOrWhiteSpace(page.RoleName))
		|| string.Equals(page.RoleName?.Trim(), EmployeesRoleName, StringComparison.OrdinalIgnoreCase)
		|| IsRoleUId(page.Role, KnownPlatformRoleIds[EmployeesRoleName]);

	// A page targets the PORTAL audience when it is scoped to the "All external users" role — by name or by its
	// seeded UId.
	private static bool IsPortalAudience(RelatedPageSpec page) =>
		string.Equals(page.RoleName?.Trim(), PortalRoleName, StringComparison.OrdinalIgnoreCase)
		|| IsRoleUId(page.Role, KnownPlatformRoleIds[PortalRoleName]);

	// True when a role reference equals a known platform role Id AS A GUID — so a valid UId in ANY format (canonical
	// "D", brace "{…}", or dash-less "N") matches the seeded Id. A plain string compare would wrongly reject e.g.
	// "{720b771c-…}" (accepted by Guid.TryParse) as an unsupported audience.
	private static bool IsRoleUId(string role, string knownRoleId) =>
		Guid.TryParse(role?.Trim(), out Guid roleId)
		&& Guid.TryParse(knownRoleId, out Guid knownId)
		&& roleId == knownId;

	// Normalizes a UId/GUID string to canonical "D" format (accepting e.g. brace "{…}" or dash-less "N" input), so
	// values written into the add-on metadata match what the Interface Designer stores and the platform matches at
	// runtime. A non-GUID value is returned trimmed (callers validate GUID-ness where it is required).
	private static string NormalizeGuid(string value) =>
		Guid.TryParse(value?.Trim(), out Guid parsed) ? parsed.ToString("D") : value?.Trim();

	// A page's audience is recognized when it is the general audience (role-less / "All employees") or the portal
	// audience ("All external users") — the only two the designer produces. Anything else is a custom role, which
	// is not supported (see ValidateRequest).
	private static bool IsRecognizedAudience(RelatedPageSpec page) =>
		IsGeneralAudience(page) || IsPortalAudience(page);

	// Normalizes a page to its (audience, type) cell for the uniqueness guard. The general audience (role-less or
	// "All employees") collapses to one bucket — two untyped general defaults would both match an employee — and
	// the portal audience to another. The type key is the trimmed TypeColumnValue LOWER-CASED, so the cell is
	// case-insensitive (a lookup GUID differing only in letter case is the same type) — consistent with the
	// case-insensitive audience matching, and it never lets a case-only variant slip past as a distinct cell. Only
	// recognized audiences reach here (ValidateRequest rejects the rest first).
	private static (string audience, string type) AudienceTypeCell(RelatedPageSpec page) =>
		(IsPortalAudience(page) ? PortalRoleName : EmployeesRoleName,
			string.IsNullOrWhiteSpace(page.TypeColumnValue) ? null : page.TypeColumnValue.Trim().ToLowerInvariant());

	// Parses the add-on's existing MetaData into a mutable object so the write can PRESERVE any top-level field
	// this tool does not model (see Create — keeps us safe against a future platform field added without a tool
	// update). A null / blank / non-object / unparseable body yields an empty object, so a fresh or malformed
	// baseline just starts clean instead of failing the write.
	private static JsonObject ParseMetadataObject(string metaData) {
		if (string.IsNullOrWhiteSpace(metaData)) {
			return new JsonObject();
		}
		try {
			return JsonNode.Parse(metaData) as JsonObject ?? new JsonObject();
		} catch (System.Text.Json.JsonException) {
			return new JsonObject();
		}
	}

	public RelatedPageAddonResult Create(RelatedPageAddonRequest request) {
		ValidateRequest(request);

		(string packageUId, Guid packageId, EntityDesignSchemaDto entitySchema) =
			ResolveTarget(request.PackageName, request.EntitySchemaName);
		ValidateTypeColumn(request.TypeColumnUId, entitySchema);

		IReadOnlyDictionary<string, string> roleByName = ResolveRoleNames(request.Pages);
		JsonArray pages = BuildPages(request.Pages, roleByName);

		AddonGetRequestDto addonRequest = BuildAddonGetRequest(entitySchema, packageId);
		AddonSchemaDto schema = addonSchemaDesignerClient.GetSchema(addonRequest);
		// Start from the FETCHED metadata and replace only the two keys this tool owns (Pages, TypeColumnUId), so
		// any OTHER top-level field survives the write. The RelatedPage MetaData is exactly
		// {"Pages":[...],"TypeColumnUId":...} today (verified backend contract), but preserving unknown top-level
		// keys keeps the tool safe if the platform later adds a field and this tool is not updated in lockstep — a
		// wholesale rebuild would silently drop it. Pages and TypeColumnUId are still FULLY replaced (replace-not-
		// merge for the configuration this tool manages).
		JsonObject metadata = ParseMetadataObject(schema.MetaData);
		metadata["Pages"] = pages;
		// A reset-to-inline (empty page set) carries no typed page sets, and the empty-clear path early-returns from
		// ValidateRequest before the TypeColumnUId GUID guard — so drop any TypeColumnUId here instead of persisting
		// an unvalidated value that could never apply to an empty configuration.
		metadata["TypeColumnUId"] = request.Pages.Count == 0 || string.IsNullOrWhiteSpace(request.TypeColumnUId)
			? null
			: NormalizeGuid(request.TypeColumnUId);
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

		(string packageUId, Guid packageId, EntityDesignSchemaDto entitySchema) =
			ResolveTarget(request.PackageName, request.EntitySchemaName);

		AddonGetRequestDto addonRequest = BuildAddonGetRequest(entitySchema, packageId);
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
		// Parse defensively (mirrors the write path's ParseMetadataObject): a malformed body or a non-object root
		// yields an empty object, and every field below is read tolerantly, so a wrong-typed payload decodes to what
		// it can rather than throwing a raw framework exception out of a read.
		JsonObject root = ParseMetadataObject(metaData);
		typeColumnUId = ReadString(root, "TypeColumnUId");
		if (root["Pages"] is not JsonArray pages) {
			return entries;
		}
		// Reverse-resolve every DISTINCT PageSchemaUId to its name ONCE, then look up locally — mirroring the write
		// path's dedup (ResolvePageSchemaUIds). A Role x Type matrix reuses the same page across many entries (e.g.
		// one page as both the default and the add for several types), so a per-entry lookup would issue O(entries)
		// round-trips instead of O(distinct pages).
		IReadOnlyDictionary<string, string> pageNameByUId = ResolvePageNames(pages);
		foreach (JsonNode page in pages) {
			if (page is not JsonObject pageObject) {
				continue; // skip a null or non-object entry rather than dereferencing it
			}
			string pageSchemaUId = ReadString(pageObject, "PageSchemaUId");
			string role = ReadString(pageObject, "Role");
			bool isAdd = pageObject["Actions"] is JsonObject actions && ReadBool(actions, "Add");
			entries.Add(new RelatedPageEntry(
				pageSchemaUId,
				pageSchemaUId is not null && pageNameByUId.TryGetValue(pageSchemaUId, out string pageName) ? pageName : null,
				ReadBool(pageObject, "IsDefault"),
				isAdd,
				ReadBool(pageObject, "IsSspDefault"),
				role,
				ResolveRoleName(role),
				ReadString(pageObject, "TypeColumnValue")));
		}
		return entries;
	}

	/// <summary>
	/// Reverse-resolves every DISTINCT non-empty <c>PageSchemaUId</c> in the page set to its schema name with a
	/// single lookup per distinct UId (case-insensitive), mirroring the write path's
	/// <see cref="ResolvePageSchemaUIds"/> dedup. A UId that does not resolve maps to <c>null</c> (best-effort) and
	/// is not re-queried.
	/// </summary>
	private IReadOnlyDictionary<string, string> ResolvePageNames(JsonArray pages) {
		var pageNameByUId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (JsonNode page in pages) {
			string pageSchemaUId = page is JsonObject pageObject ? ReadString(pageObject, "PageSchemaUId") : null;
			if (string.IsNullOrWhiteSpace(pageSchemaUId) || pageNameByUId.ContainsKey(pageSchemaUId)) {
				continue;
			}
			pageNameByUId[pageSchemaUId] = ResolvePageName(pageSchemaUId);
		}
		return pageNameByUId;
	}

	// Reads a string field WITHOUT throwing on a wrong-typed payload: null when the key is absent or not a scalar;
	// the raw scalar text when it is present but not a JSON string (e.g. stored as a number). The platform is not
	// known to emit such shapes, but a read must degrade gracefully rather than surface a framework exception.
	private static string ReadString(JsonObject parent, string key) =>
		parent[key] is JsonValue value
			? (value.TryGetValue(out string text) ? text : value.ToString())
			: null;

	// Reads a boolean flag WITHOUT throwing: a real JSON bool passes through; a bool stored as the string
	// "true"/"false" is parsed; anything else (absent, number, object) is false.
	private static bool ReadBool(JsonObject parent, string key) {
		if (parent[key] is not JsonValue value) {
			return false;
		}
		if (value.TryGetValue(out bool flag)) {
			return flag;
		}
		return value.TryGetValue(out string text) && bool.TryParse(text, out bool parsed) && parsed;
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
	/// Shared prologue for <see cref="Create"/> and <see cref="Get"/>: resolves the package to its UId (rejecting a
	/// missing package or a non-GUID UId), then resolves the target object (entity schema) from that package. Both
	/// entry points target the same object in the same package, so the resolution lives here rather than being
	/// duplicated (and drifting) in each.
	/// </summary>
	private (string packageUId, Guid packageId, EntityDesignSchemaDto entitySchema) ResolveTarget(
		string packageName, string entitySchemaName) {
		(string packageUId, string packageError) = PageSchemaMetadataHelper.QueryPackageUId(
			applicationClient, serviceUrlBuilder, packageName);
		if (packageError != null) {
			throw new InvalidOperationException(packageError);
		}
		if (!Guid.TryParse(packageUId, out Guid packageId)) {
			throw new InvalidOperationException(
				$"Resolved package '{packageName}' UId '{packageUId}' is not a valid GUID.");
		}
		EntityDesignSchemaDto entitySchema = ResolveEntitySchema(entitySchemaName, packageId, packageName);
		return (packageUId, packageId, entitySchema);
	}

	/// <summary>
	/// Builds the <c>RelatedPage</c> add-on <c>GetSchema</c> request for the resolved object — the single shape both
	/// <see cref="Create"/> and <see cref="Get"/> send. Mirrors the Interface Designer / Business Rule add-on path:
	/// a replacing or derived object reports the parent it extends; a plain object reports none (<c>Guid.Empty</c>).
	/// </summary>
	private static AddonGetRequestDto BuildAddonGetRequest(EntityDesignSchemaDto entitySchema, Guid packageId) =>
		new() {
			AddonName = RelatedPageAddonName,
			TargetSchemaUId = entitySchema.UId,
			TargetParentSchemaUId = entitySchema.ParentSchema?.UId ?? Guid.Empty,
			TargetPackageUId = packageId,
			TargetSchemaManagerName = EntitySchemaManagerName,
			UseFullHierarchy = true
		};

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
	/// Verifies a supplied <c>TypeColumnUId</c> is a real column of the resolved object (its own or an inherited
	/// column). Typed page sets are matched by (TypeColumnUId, TypeColumnValue), so a well-formed-but-wrong column
	/// UId would write typed pages the platform can never select — a silent <c>Success=true</c> with dead pages.
	/// The object's schema was already fetched to resolve it, so this adds no round-trip. Fail-soft: if the fetched
	/// schema exposes no columns (an unverifiable response), the check is skipped rather than false-rejecting a
	/// valid set — mirroring the codebase's other best-effort existence checks. The GUID shape is enforced upstream
	/// by <see cref="ValidateRequest"/>; a non-empty non-GUID value never reaches here with a column to match.
	/// </summary>
	private void ValidateTypeColumn(string typeColumnUId, EntityDesignSchemaDto entitySchema) {
		if (string.IsNullOrWhiteSpace(typeColumnUId) || !Guid.TryParse(typeColumnUId.Trim(), out Guid typeColumnId)) {
			return;
		}
		var columns = SchemaColumns(entitySchema).ToList();
		if (columns.Count == 0) {
			// Never false-reject: an unverifiable schema must not block a valid write. But an object that supports
			// related pages exposing ZERO columns (own + inherited) is unlikely — a partial/incomplete GetSchema (a
			// transient server condition) is the more probable cause — so leave a warning trail, because an unverified
			// TypeColumnUId is about to be written straight into the add-on (replace-not-merge).
			logger.WriteWarning(
				$"type-column-uid '{typeColumnUId.Trim()}' could not be verified: object '{entitySchema.Name}' returned "
				+ "no columns (own or inherited), so the type-column existence check was skipped and the value is written "
				+ "unverified. If the binding does not resolve at runtime, re-check the type column — the schema fetch may "
				+ "have been incomplete.");
			return;
		}
		if (columns.All(column => column.UId != typeColumnId)) {
			throw new InvalidOperationException(
				$"type-column-uid '{typeColumnUId}' is not a column of object '{entitySchema.Name}'. It must be the "
				+ "u-id of a column on the object — the record-type lookup (e.g. Type or Category) the typed page sets "
				+ "are keyed by.");
		}
	}

	// The object's own plus inherited columns (a type column is commonly inherited — e.g. Case.Category), matching
	// how the column-properties read path spans both sets.
	private static IEnumerable<EntitySchemaColumnDto> SchemaColumns(EntityDesignSchemaDto entitySchema) =>
		(entitySchema.Columns ?? []).Concat(entitySchema.InheritedColumns ?? []);

	/// <summary>
	/// Resolves every distinct role NAME referenced by the page specs to its <c>SysAdminUnit</c> Id. An explicit
	/// role UId on a spec wins and is not resolved here. Only the two supported audiences ("All employees" and
	/// "All external users") map to their fixed seeded Ids — no remote lookup is issued; any other name is
	/// rejected upstream by <see cref="ValidateRequest"/>, so it never reaches here.
	/// </summary>
	private static IReadOnlyDictionary<string, string> ResolveRoleNames(IReadOnlyList<RelatedPageSpec> specs) {
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
			// Unreachable in the normal flow: ValidateRequest already rejects any audience outside the two known
			// roles. Kept as a defensive guard so a caller that bypasses validation fails loudly rather than
			// silently dropping the audience.
			throw new InvalidOperationException(
				$"Role name '{roleName}' is not a supported audience; only 'All employees' and 'All external users' "
				+ "are supported.");
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
				["PageSchemaUId"] = ResolvePageUId(spec, pageUIdByName),
				["IsDefault"] = spec.IsDefault,
				["IsSspDefault"] = spec.IsSspDefault,
				["Actions"] = new JsonObject { ["Add"] = spec.IsAdd },
				["Role"] = string.IsNullOrWhiteSpace(role) ? null : role,
				["TypeColumnValue"] = string.IsNullOrWhiteSpace(spec.TypeColumnValue) ? null : NormalizeGuid(spec.TypeColumnValue)
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
			// A spec with an explicit page-schema-uid is used verbatim (validated as a GUID in ValidateRequest), so
			// only a spec WITHOUT one needs its name resolved. ValidateRequest already guaranteed name-or-uid, so a
			// blank name here means the uid path is taken.
			if (!string.IsNullOrWhiteSpace(spec.PageSchemaUId) || string.IsNullOrWhiteSpace(spec.PageSchemaName)) {
				continue;
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
	/// The <c>PageSchemaUId</c> stored for a spec: an explicit <c>page-schema-uid</c> wins (the round-trip vehicle
	/// from <c>get-related-page-addon</c>, which also survives a page whose name no longer resolves); otherwise the
	/// UId resolved from <c>page-schema-name</c> via <see cref="ResolvePageSchemaUIds"/>.
	/// </summary>
	private static string ResolvePageUId(RelatedPageSpec spec, IReadOnlyDictionary<string, string> pageUIdByName) =>
		!string.IsNullOrWhiteSpace(spec.PageSchemaUId)
			? spec.PageSchemaUId.Trim()
			: pageUIdByName[spec.PageSchemaName.Trim()];

	/// <summary>
	/// Chooses the audience role UId stored on a page entry: an explicit role UId wins (already validated as a
	/// GUID upfront in <see cref="ValidateRequest"/>), otherwise the role resolved from the spec's role name,
	/// otherwise none (the set applies to all users).
	/// </summary>
	private static string ResolvePageRole(RelatedPageSpec spec, IReadOnlyDictionary<string, string> roleByName) {
		if (!string.IsNullOrWhiteSpace(spec.Role)) {
			// Normalize to canonical "D" format so a valid UId given in brace/N format is stored the way the
			// Interface Designer writes it (and is matched by the platform at runtime), not verbatim.
			return NormalizeGuid(spec.Role);
		}
		if (string.IsNullOrWhiteSpace(spec.RoleName)) {
			return null;
		}
		string roleName = spec.RoleName.Trim();
		if (!roleByName.TryGetValue(roleName, out string roleUId)) {
			// ValidateRequest already rejects any audience outside the two known roles, so this is unreachable in the
			// normal flow. Look up via TryGetValue rather than the indexer so that, if that upstream invariant ever
			// diverges, this fails with a clear, traceable message instead of a bare KeyNotFoundException.
			throw new InvalidOperationException(
				$"Unexpected unresolved audience '{roleName}' — ValidateRequest should have rejected it before this point.");
		}
		return roleUId;
	}
}
