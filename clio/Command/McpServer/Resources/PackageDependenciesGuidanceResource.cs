using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Canonical guidance for managing Creatio package dependencies through clio MCP: the schema-designer
/// HTML-error recovery path (add-package-dependency), the symmetric remove-package-dependency cleanup,
/// and the anti-patterns that previously cost agents multiple workaround detours.
/// </summary>
[McpServerResourceType]
public sealed class PackageDependenciesGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/package-dependencies";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP package-dependencies guide

		       PURPOSE
		       - Manage the dependency list of a workspace/custom package via PackageService.svc:
		         add-package-dependency (extend) and remove-package-dependency (trim). Both round-trip
		         the package's full properties and are idempotent (re-adding / removing an absent
		         dependency is a no-op).

		       WHEN YOU NEED THIS (the canonical symptom)
		       - A schema mutation (modify-entity-schema-column / update-entity-schema / create-entity /
		         sync-schemas) fails with:
		           "GetSchemaDesignItem returned an HTML error page instead of JSON".
		       - The MOST COMMON cause is NOT a server bug: the package you are writing into does not
		         depend on the package/app that OWNS the upper layer of the object you are extending.
		         A replacing schema can only be created when the target package depends on the owner of
		         the object's top layer.
		         Classic example: extending the Opportunity layer from a custom package (for example
		         `Custom`) that depends only on `CrtCore` → the designer fails until the package also
		         depends on `CrtLeadOppMgmtApp` (the app that owns the Opportunity layer).

		       AUTOMATIC RESOLUTION
		       clio automatically detects when GetSchemaDesignItem fails because of a missing
		       dependency: it finds the package containing the target schema, adds it as a
		       dependency, and retries the operation — one transparent recovery cycle. This
		       applies ONLY to WRITE operations (modify-entity-schema-column, update-entity-schema,
		       create-entity) and ONLY when exactly one candidate package is found.
		       When multiple candidates exist, clio refuses to auto-resolve (ambiguous) and
		       instructs the user to add the correct dependency manually.
		       Read-only operations (get-entity-schema-properties, get-entity-schema-column-properties)
		       never trigger auto-resolution.

		       NOT THIS CASE: a transient network flap (DNS resolution failure, connection reset,
		       timeout, gateway 502/503/504) is a DIFFERENT failure class from a missing dependency.
		       `sync-schemas` retries transient network faults per operation on its own and, on a
		       mid-batch abort, returns a `resume-plan` — resubmit only `resume-plan.operations`. Do
		       NOT add a package dependency in response to a DNS/timeout error; add one only for the
		       "GetSchemaDesignItem returned an HTML error page" missing-dependency symptom above.

		       MANUAL RECOVERY (when auto-resolution is not available)
		       1) Identify the owning app/package of the object's upper layer (for Opportunity it is
		          `CrtLeadOppMgmtApp`; use get-app-info / find-entity-schema to confirm the owner of
		          other objects).
		       2) add-package-dependency
		            --package-name <your package>  --dependencies <OwningPackage>
		          (MCP: dependencies = [{ name: "<OwningPackage>" }]; version defaults to installed).
		       3) Retry the original schema mutation. It now succeeds.

		       DO NOT (anti-patterns that waste time and are unsafe)
		       - Do NOT write the column/schema directly into the owning (managed) package
		         (for example into `CrtLeadOppMgmtApp`). Your changes belong in your own package.
		       - Do NOT fall back to raw SQL, direct OData writes to SysPackageDependency, or DataService
		         to patch dependencies — those are blocked, permission-gated, or unsafe. Use
		         add-package-dependency.
		       - Do NOT hand-edit a package descriptor and push-pkg to add a dependency — it conflicts.

		       REMOVING A DEPENDENCY (cleanup / rollback)
		       - remove-package-dependency
		           --package-name <your package>  --dependencies <PackageToRemove>
		         Removes matching entries by name (idempotent) and returns the resulting dependency list.
		       - Use it when fully rolling back an experiment that added a dependency only to unblock the
		         designer. Note: if the extension you created still exists, the dependency is CORRECT and
		         should stay — only remove it when nothing in your package relies on the owner anymore.

		       NOTES
		       - A dependency change may report compilation-required; run compile-configuration to apply.
		       - Both operations need the same elevated package-management access as other package tools.
		       """
	};

	/// <summary>
	/// Returns the canonical package-dependencies guidance article.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "package-dependencies-guidance")]
	[Description("Returns the canonical clio MCP guidance for managing package dependencies: the schema-designer HTML-error recovery via add-package-dependency, the symmetric remove-package-dependency cleanup, and the anti-patterns (no writes into the owning managed package, no raw SQL/OData/DataService).")]
	public ResourceContents GetGuide() => Guide;
}
