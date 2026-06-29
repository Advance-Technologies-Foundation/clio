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
}

internal sealed class RelatedPageAddonService(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IAddonSchemaDesignerClient addonSchemaDesignerClient,
	IRemoteEntitySchemaDesignerClient entitySchemaDesignerClient)
	: IRelatedPageAddonService {

	private const string RelatedPageAddonName = "RelatedPage";
	private const string EntitySchemaManagerName = "EntitySchemaManager";

	/// <summary>
	/// The standard platform audience roles have fixed, seeded <c>SysAdminUnit</c> Ids that are identical
	/// across Creatio installs. Mapping them by name here means a user (or any other unit) that happens to
	/// share the name can never be picked up by the by-name <see cref="PageSchemaMetadataHelper.QueryRoleUId"/>
	/// lookup, which filters on <c>Name</c> only. Any other (custom) role name still resolves by name.
	/// </summary>
	private static readonly IReadOnlyDictionary<string, string> KnownPlatformRoleIds =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			["All employees"] = "a29a3ba5-4b0d-de11-9a51-005056c00008",
			["All external users"] = "720b771c-e7a7-4f31-9cfb-52cd21c3739f"
		};

	public RelatedPageAddonResult Create(RelatedPageAddonRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		if (string.IsNullOrWhiteSpace(request.EntitySchemaName)) {
			throw new ArgumentException("entity-schema-name is required.");
		}
		if (string.IsNullOrWhiteSpace(request.PackageName)) {
			throw new ArgumentException("package-name is required.");
		}
		if (request.Pages is null || request.Pages.Count == 0) {
			throw new ArgumentException("At least one page is required.");
		}
		// A base default page is mandatory: there must be at least one is-default entry with no
		// type-column-value — the page opened for a record and the fallback when a record's type has no
		// dedicated set. The Interface Designer does not enforce this, so an add-only or typed-only
		// configuration would silently leave records with no page to open. Reject it before any remote call.
		if (!request.Pages.Any(page => page.IsDefault && string.IsNullOrWhiteSpace(page.TypeColumnValue))) {
			throw new ArgumentException(
				"A base default page is required: include one page with is-default=true and no type-column-value "
				+ "(the page opened for a record and the fallback when a record's type has no dedicated set).");
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
		schema.MetaData = metadata.ToJsonString();

		addonSchemaDesignerClient.SaveSchema(schema);
		// Make the saved add-on visible to the current session without a full reload, then rebuild static
		// client content so online and offline users pick up the change.
		addonSchemaDesignerClient.ResetClientScriptCache();
		addonSchemaDesignerClient.BuildConfiguration();

		return new RelatedPageAddonResult(
			entitySchema.UId.ToString("D"), packageUId, pages.Count, RelatedPageAddonName);
	}

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
	/// Chooses the audience role UId stored on a page entry: an explicit role UId (validated as a GUID) wins,
	/// otherwise the role resolved from the spec's role name, otherwise none (the set applies to all users).
	/// An explicit role that is not a GUID is rejected rather than persisted verbatim as a malformed audience.
	/// </summary>
	private static string ResolvePageRole(RelatedPageSpec spec, IReadOnlyDictionary<string, string> roleByName) {
		if (!string.IsNullOrWhiteSpace(spec.Role)) {
			string explicitRole = spec.Role.Trim();
			if (!Guid.TryParse(explicitRole, out _)) {
				throw new ArgumentException(
					$"role '{spec.Role}' is not a valid SysAdminUnit GUID; use role-name to resolve a role by name.");
			}
			return explicitRole;
		}
		return !string.IsNullOrWhiteSpace(spec.RoleName) ? roleByName[spec.RoleName.Trim()] : null;
	}
}
