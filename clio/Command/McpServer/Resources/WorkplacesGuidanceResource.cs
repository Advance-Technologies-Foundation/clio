using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// The workplaces guide: how to create, update, and delete a Creatio navigation workplace
/// (<c>SysWorkplace</c>), grant or remove role access, and add, remove, or move its sections with the
/// generic odata tools, shipping every change as a package data binding.
/// </summary>
[McpServerResourceType]
public sealed class WorkplacesGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/workplaces";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP workplaces guide

		       A workplace (`SysWorkplace`) is a named container in the Creatio left navigation: it groups
		       sections and is shown only to the roles granted access to it. Managing one spans three tables —
		       this guide owns that model. Read `core-rules` first, and `data-bindings` for the binding-tool
		       contract this guide relies on.

		       ## The three tables
		       - `SysWorkplace` — the workplace itself. Key columns: `Name`; `Position` (order in the switcher);
		         `SysApplicationClientTypeId` (which client the workplace belongs to — web or mobile, see Client
		         type below); `HomePageUId` (the workplace's home page — owned by `home-page`, not by this guide).
		         Also `Type` (a `SysWorkplaceType` lookup: `General`, `Portal`, ...) and `LoaderId` (the workplace
		         loader schema) — you rarely set these on a live write because the platform defaults them, but a
		         data binding MUST carry them; see Client type.
		       - `SysModuleInWorkplace` — one row per section in the workplace. Required `SysModuleId` (the
		         section, a `SysModule` row) and `SysWorkplaceId`; `Position` orders the sections.
		       - `SysAdminUnitInWorkplace` — one row per role that sees the workplace. Required `SysAdminUnitId`
		         (a role or user, a `SysAdminUnit` row) and `SysWorkplaceId`.
		       All three key on `Id`. Lookups take a GUID, but the column form differs by the call you are making:
		       - `odata-create` / `odata-update` payloads and `odata-delete` keys: the `<Field>Id` scalar —
		         `SysWorkplaceId`, `SysModuleId`, `SysAdminUnitId`.
		       - `odata-read` FILTERS over a junction table: the navigation path `SysWorkplace/Id`, NOT the
		         scalar `SysWorkplaceId`.
		       - data-binding (`*-db`) calls: the logical column name — `SysWorkplace`, `SysModule`,
		         `SysAdminUnit`.

		       ## Confirmation gates
		       The gate is per tool, not per table, and one destructive tool has NO gate at all:
		       - `odata-create` is NOT destructive and accepts no `confirm` argument — do not send one.
		       - `odata-update` and `odata-delete` ARE destructive and require `confirm=true`.
		       - `remove-data-binding-row-db` is flagged destructive but accepts NO `confirm` argument either, and
		         it DELETES THE LIVE RECORD as well as unbinding it (see Ship every change as a data binding).
		         Nothing holds the call for you, so get the user's agreement yourself before every invocation.
		       Separately from any tool gate, granting or removing a role's visibility changes who can see data:
		       confirm that intent with the USER first, even though the create itself is ungated.

		       ## Not to be confused with
		       Navigation `SysWorkplace` is NOT `SysWorkspace` (the dev configuration workspace) and NOT clio
		       `create-workspace` (a local project folder). This guide covers navigation `SysWorkplace` only.

		       ## Ask where things belong before you write
		       Placement and audience are the user's decisions, not yours. When the request names no target
		       workplace, STOP and ask before any NAVIGATION write — `SysWorkplace`, `SysModuleInWorkplace`,
		       `SysAdminUnitInWorkplace`, or their bindings. `create-app` performs such a write itself (it creates
		       `My applications` and the section placement), so for a NEW app the decision is due BEFORE
		       `create-app` — ask it in the same turn you confirm the environment, and at the latest immediately
		       after `create-app` and before anything further. Asking once the pages are built is a defect, not a
		       late-but-equivalent order: it makes the user re-decide work you already finished. Offer these
		       options:
		       - a NEW workplace named for the app (recommend this when scaffolding a new app — it keeps the
		         app's navigation self-contained);
		       - the `My applications` workplace — note it may not exist yet, and leaving the app there restricts
		         it to administrators (see New apps start in a default workplace);
		       - an existing workplace the user names (list the available `Name` values so the choice is real).
		       When the app is being SCAFFOLDED, its SECTION and its HOME PAGE go in the SAME workplace and that is
		       ONE question — ask it once for both, then apply both. Reproduced failure mode: asking only about the
		       home page bound it to one workplace while the section stayed in `My applications`, so no single
		       workplace showed a working app. `home-page` owns the home-page half of the write; this guide owns
		       the section half; neither is finished alone.
		       Then ask WHO SHOULD SEE IT, because placement alone does not answer that. A new workplace has no
		       `SysAdminUnitInWorkplace` row and is invisible to everyone, so choosing one obliges you to grant an
		       audience. Do NOT offer this as free text and do NOT reduce it to the two extremes
		       (`System administrators` vs everyone) — that framing makes a blanket grant look like the only way to
		       let a normal user in. Build the option set from the environment instead:
		       1. `odata-read` `SysAdminUnit` filtered to roles, not users — `$filter=SysAdminUnitTypeId ne
		          <user type>` is fragile, so read `Name` plus `SysAdminUnitTypeId` and keep the organisational and
		          functional roles. Sort by `Name` and cap the list at a handful.
		       2. Offer, in this order: the role(s) the request itself implies (if it says "for sales managers",
		          the matching role comes first and is the RECOMMENDED option); then the other concrete roles you
		          just read; then `All employees` — labelled as granting every user in the system, and never
		          pre-marked as the recommendation.
		       3. Offer `System administrators` only as the deliberate admin-tool answer, and say what it costs:
		          ordinary users will not see the app at all. It is the `create-app` default, so picking it changes
		          nothing and is the one option that needs no write.
		       Pick the narrowest role that satisfies the request. If the request scopes the app or page
		       to a role, an existing target workplace's `SysAdminUnitInWorkplace` rows should match that role; if
		       they do not, surface the mismatch and confirm rather than silently granting access.

		       ## Client type
		       A workplace belongs to one client type via `SysApplicationClientTypeId`, so a web workplace never
		       appears in the Creatio Mobile app and vice versa. Resolve the intended client type explicitly and
		       pass it in BOTH writes — the `odata-create` payload AND the binding row. For the mobile flow read
		       `mobile-page-modification`. The two writes fail differently, so do not generalise from one to the
		       other:
		       - LIVE row: omitting `SysApplicationClientTypeId` is survivable. The platform's live write path fills
		         in the WEB client, identical to passing it explicitly (observed on the stand tested: the omitted
		         row read back with the same client type, loader, and type as a product workplace — treat that as
		         an observation, not a contract). Pass it anyway; it is the wrong value for mobile.
		       - BINDING row: omitting it is NOT survivable, and it fails silently. A binding ships only the columns
		         you passed and install supplies no defaults, so the workplace arrives on the target environment
		         with EMPTY `SysApplicationClientType`, `Type`, and `LoaderId`. That much is reproduced end to end
		         on a real cross-environment transfer. An empty client type matches no client, so do not expect the
		         workplace to render — but the verified fact is the empty columns, not the rendering failure.
		       `SysApplicationClientType` is not alone in this trap: `Type` and `LoaderId` are also platform-set
		       on a live insert and silently absent from a hand-built binding. Ship all three — see Ship every
		       change as a data binding for the full per-table column set.

		       ## Ship every change as a data binding
		       Each operation below is TWO steps: write the live row (`odata-create` / `odata-update`), then mirror
		       it into the target package so it transfers — `create-data-binding-db` for the FIRST bind of a row
		       (it adopts an existing row by `Id`), `upsert-data-binding-row-db` for every later change to a
		       binding that already exists. Read `data-bindings` for those tool contracts and for how to inspect a
		       package's existing bindings first.

		       `remove-data-binding-row-db` is the exception and it is dangerous: it DELETES THE LIVE RECORD and
		       then unbinds it, and it has no `confirm` gate. `data-bindings` owns that contract — read it. What
		       matters here: there is no way to un-ship a workplace row without deleting the workplace, which is
		       why New apps start in a default workplace tells you to leave the `My applications` bindings alone.

		       A binding ships ONLY the columns you passed (`data-bindings` owns the projection rule and the
		       `IsForceUpdate: false` first-install-only consequence). For the three workplace tables that means:
		       - `SysWorkplace` — `Id`, `Name`, `Position`, `SysApplicationClientType`, `Type`, `LoaderId`, plus
		         `HomePageUId` when the workplace has a home page.
		       - `SysModuleInWorkplace` — `Id`, `SysWorkplace`, `SysModule`. Deliberately no `Position`: the server
		         assigns it on insert and a shipped value is discarded (see Rules that bite). This applies to a
		         binding you CREATE. When you MOVE a section, you are upserting over the binding `create-app`
		         already shipped, and an upsert rewrites the columns you pass without dropping the ones already
		         there — so `Position` stays in that binding and you cannot remove it with these tools. Verified
		         and harmless: the target gets a possibly-meaningless order value, not an empty column. Do not
		         chase it, and do not delete the binding to "clean" the column set — `remove-data-binding-row-db`
		         would take the live placement row with it.
		       - `SysAdminUnitInWorkplace` — `Id`, `SysWorkplace`, `SysAdminUnit`.
		       `Type`, `LoaderId`, and the client-type GUID are set for you on the LIVE write, so you will not have
		       them to hand — get them by READ-BACK: right after `odata-create`, `odata-read` `SysWorkplace` for the
		       new `Id` selecting `SysApplicationClientTypeId`, `TypeId`, `LoaderId`, then pass those values into
		       the binding row under their binding-form names `SysApplicationClientType`, `Type`, `LoaderId`.
		       (Only FK columns drop the `Id` suffix in binding form; `LoaderId` is the column's real name and
		       keeps it.) To choose the client type up front instead, `odata-read` `SysApplicationClientType` and
		       match `Name` — `Web` or `Mobile`.
		       Get this right on the FIRST install: per `data-bindings`, a corrected package cannot overwrite a
		       workplace row that already exists on the target, so a wrong first install is repaired by hand there.

		       Binding NAMES are suffixed and the suffix differs per table. Two cases, and never leave
		       `binding-name` unset in either — the default is the bare schema name, which creates a PARALLEL
		       binding instead of touching the app's:
		       - the package already ships the binding: do NOT derive the name, read it with `execute-esq` over
		         `SysPackageSchemaData` (filter `SysPackage.Name`; columns `Name`, `SysSchema.Name`). For an app
		         whose section code is `UsrTodo`, `create-app` ships `SysWorkplace_MyApps` and
		         `SysAdminUnitInWorkplace_MyApps` (suffix = the WORKPLACE) alongside `SysModuleInWorkplace_UsrTodo`
		         (suffix = the SECTION code).
		       - you are creating the binding: you choose the name and pass it explicitly — `<Schema>_<Workplace>`
		         for `SysWorkplace` / `SysAdminUnitInWorkplace` (e.g. `SysWorkplace_Todo`) and
		         `<Schema>_<SectionCode>` for `SysModuleInWorkplace`.
		       Each binding appears as a `Data/<Schema>_<Suffix>/data.json` folder in the package.

		       ## Operations
		       - Create a workplace: `odata-create` `SysWorkplace` (`Name`, `Position`, and the client type per
		         Client type above), read the row back for `TypeId` / `LoaderId` / `SysApplicationClientTypeId`,
		         then `create-data-binding-db` with the FULL column set from Ship every change as a data binding.
		         Binding only `Name`/`Position` yields a package that installs a workplace with no client type.
		         A brand-new workplace has NO `SysAdminUnitInWorkplace` rows, so nobody can see it yet — agree the
		         audience with the user and grant it (see Grant / remove a role's access) or the workplace is dead.
		       - Update a workplace: `odata-update` by `Id` with `confirm=true`, then `upsert-data-binding-row-db`
		         on its binding with the full column set.
		       - Delete a workplace: children FIRST — see Deleting a workplace below.
		       - Grant / remove a role's access: `odata-create` / `odata-delete` a `SysAdminUnitInWorkplace` row,
		         then bind it. Resolve the role `Id` from `SysAdminUnit` by name first — names are not guaranteed
		         unique, so if more than one row matches, disambiguate before writing. Changing a role is a remove
		         of the old row plus a grant of the new one. This decides who can reach the data behind every
		         section in the workplace: confirm the role list with the USER, start from the NARROWEST role that
		         satisfies the request, and never default to a blanket role such as `All employees` because it is
		         convenient — see Confirmation gates.
		       - Add / remove a section: `odata-create` / `odata-delete` a `SysModuleInWorkplace` row. Resolve the
		         section `Id` from `SysModule` by code first — codes are NOT unique (`Code = 'Contact'` returns two
		         rows on a stock environment) and the zero-GUID rule below does not catch the wrong one, because a
		         wrong-but-real `SysModuleId` is non-zero. Disambiguate by reading which `SysModuleId` the existing
		         `SysModuleInWorkplace` rows already use. To remove ONE row you still need its `binding-name` (read
		         `SysPackageSchemaData` as in Deleting a workplace step 1); only the ORDER is free, because
		         `remove-data-binding-row-db` deletes the live row itself — so it alone is enough, and a preceding
		         `odata-delete` is redundant. The children-first rule below applies only to the parent cascade.
		       - Move a section between workplaces: the TARGET `SysWorkplace` row must already have its OWN
		         `SysWorkplace` binding in the same package — bind it FIRST. Then `odata-update` the row's
		         `SysWorkplaceId` to the target with `confirm=true` and `upsert-data-binding-row-db` the junction
		         binding row so the new placement ships. Without the target's binding the junction binding fails
		         with `SaveSchema failed: Data is not bound for connected object "SysWorkplace" by column
		         "SysWorkplace"`, a message that names neither the cause nor the fix. Verified for a workplace you
		         created in your own package. When the target is a pre-existing product workplace (`Sales`,
		         `Service`, ...) it is bound in a PRODUCT package, so adopting it into yours makes your package ship
		         a product row with whatever column set you pass — that path is NOT verified, so surface it to the
		         user and prefer a workplace your own package owns. A move is ONE row changing parents — do not
		         create a row in the target and leave the source row behind.
		       - Set or unset the workplace's home page: `home-page` owns `HomePageUId`, including how to unset it
		         safely. Go there; do not improvise it from this guide.

		       ## Deleting a workplace
		       This destroys the workplace, every section placement in it, and every role grant on it, and no
		       re-install brings them back. Step 0 is NOT a tool call: confirm with the USER that this workplace on
		       THIS environment is the intended target, and say what will be lost. Note also that step 4 deletes the
		       live child rows, not just their bindings (see Ship every change as a data binding).
		       Deleting the parent CASCADES its child rows in the database but NOT in the package, and once the
		       children are gone from the database you can no longer read their `Id` values with `odata-read` —
		       recovering them then means digging them out of an exported package. Orphaned child rows otherwise
		       stay in the package and get reinstalled later. So read first and delete children first:
		       1. `execute-esq` over `SysPackageSchemaData` (filter `SysPackage.Name`; columns `Name`,
		          `SysSchema.Name`) — cache the `binding-name` of every workplace-related binding in the package.
		          `remove-data-binding-row-db` REQUIRES `binding-name` and the binding tools have no list mode, so
		          steps 4-6 cannot run without this read. Do not skip it and do not guess the names.
		       2. `odata-read` `SysModuleInWorkplace` filtered by `SysWorkplace/Id` — cache the `Id` values.
		       3. `odata-read` `SysAdminUnitInWorkplace` filtered by `SysWorkplace/Id` — cache the `Id` values.
		       4. `remove-data-binding-row-db` for each cached child row.
		       5. `odata-delete` the `SysWorkplace` row with `confirm=true`.
		       6. `remove-data-binding-row-db` for the `SysWorkplace` binding row.
		       Recovery if the parent was already deleted: get the binding NAMES from `SysPackageSchemaData` as in
		       step 1, then get the orphaned row `Id` values with `read-data-binding-db`, which lists every bound row
		       of that binding. `execute-esq` cannot do this (`SysPackageSchemaData` holds one record per BINDING,
		       not per bound row), and neither can the write-side binding tools.

		       ## New apps start in a default workplace
		       `create-app` registers its section in the `My applications` workplace and ships that placement as a
		       `SysModuleInWorkplace` binding in the app's package — the app does NOT appear in a business
		       workplace on its own. Two verified facts about `My applications` that change the decision:
		       - it does not necessarily pre-exist. On an environment with no custom app there is no
		         `My applications` row at all — `create-app` CREATES it, along with a `SysWorkplace_MyApps` binding.
		         So it can be missing from the `Name` list you show the user.
		       - it is NOT a neutral place to leave the app. The single `SysAdminUnitInWorkplace` row `create-app`
		         ships grants `System administrators` only, so leaving the section there means only administrators
		         ever see the app.
		       Once the target workplace is agreed (see Ask where things belong), MOVE the app out of
		       `My applications` rather than adding a second placement:
		       - move the section row's `SysWorkplaceId` to the target workplace, and update the binding so the
		         new placement transfers — bind the target workplace FIRST (see Operations);
		       - if you created a home page, point the TARGET workplace at it per `home-page` and update that
		         workplace's binding;
		       - CHECK `My applications`.`HomePageUId` and clear it when it points at THIS app's home page. The
		         `AppFreedomUI` template creates no home page and leaves it empty, but `AppWithHomePage` creates one
		         AND points `My applications` at it — verified on a live stand, where the shared `My applications`
		         was still opening a custom app's home page long after that app was built, and its
		         `SysWorkplace_MyApps` binding shipped 7 columns carrying that `HomePageUId`. So the package
		         EXPORTS a mutation of a workplace it does not own: installing it repoints `My applications` on
		         every target environment. To clear it, `odata-update` `HomePageUId` to
		         `00000000-0000-0000-0000-000000000000` and then `upsert-data-binding-row-db` the
		         `SysWorkplace_MyApps` row with that same zero GUID. Do NOT remove the binding to "clean" the
		         column — `remove-data-binding-row-db` deletes the live shared workplace, and an upsert cannot drop
		         a column that is already in a binding. Say plainly what remains: the package still ships a
		         `My applications` row, so on install it clears whatever home page that environment had there.
		         Leaving the app's own UId in place is strictly worse — that hijacks the shared workplace instead of
		         emptying one field. If `HomePageUId` points at something you did not create, leave it alone and
		         surface the conflict;
		       - grant the agreed audience on the target workplace if it has none yet (see Grant / remove a role's
		         access) — a workplace with no `SysAdminUnitInWorkplace` row is invisible to everyone;
		       - verify `My applications` no longer lists the section.
		       Your package still ships `SysWorkplace_MyApps` and `SysAdminUnitInWorkplace_MyApps`, so it will
		       recreate an empty `My applications` on every target environment. Do NOT reflexively "clean that up":
		       there is no tool that unbinds a row without deleting it, `remove-data-binding-row-db` DELETES the
		       live record, and `My applications` is SHARED — deleting it cascades the section placements and role
		       grants of every OTHER custom app on the environment, unrecoverably. Default to leaving both bindings
		       and telling the user the package creates an empty `My applications`. Remove them only when ALL of
		       these hold: the user agreed after being told the row is deleted live; `odata-read`
		       `SysModuleInWorkplace` filtered by `SysWorkplace/Id` shows no other app's sections; and you remove
		       the CHILD binding (`SysAdminUnitInWorkplace_MyApps`) BEFORE the parent (`SysWorkplace_MyApps`), per
		       Deleting a workplace.
		       Adding the section to the target while leaving the `My applications` row in place leaves the app in
		       two places at once and is not what the user asked for.

		       ## Rules that bite
		       - There is NO unique constraint on (`SysModule`, `SysWorkplace`): adding or moving a section
		         already present in the target creates a duplicate. Read the target's rows first and skip if the
		         section is already there.
		       - `Position` is unstable on BOTH tables, for different reasons. `SysModuleInWorkplace.Position`
		         ignores the value you send outright (verified: sent 99, stored 123) — which is why the binding
		         column set omits it. `SysWorkplace.Position` DOES keep the value you insert (verified: sent 23,
		         read back 23, shipped 23, still 23 on the target), but the platform renumbers every workplace's
		         `Position` whenever workplaces are added or removed — one install that created two workplaces
		         renumbered all 22 pre-existing rows. Read back for the actual order; never treat `Position` as an
		         identifier.
		       - A junction row whose `SysModuleId` or `SysWorkplaceId` is the zero GUID
		         (`00000000-0000-0000-0000-000000000000`) is dead weight: it inserts and reads back but binds
		         nothing. Assert both links are non-zero after every write; repair a zero-GUID row by creating a
		         replacement row with a new `Id` rather than patching the broken one.

		       ## When changes appear
		       Workplace, section, and edit-page lists are cached PER SESSION, so a signed-in user keeps seeing the old
		       navigation and a browser refresh alone shows nothing. Do not claim a restart is required; it is not.
		       Finish every navigation change by publishing it:
		       - `reload-workplaces` (requires cliogate) reloads the platform navigation caches, after which a plain
		         page refresh is enough and NO re-login is needed. Call it as the LAST step, after the final write —
		         run it earlier and the writes that follow are stale again. Then tell the user to refresh.
		       - If it fails or cliogate is not installed, say the change is applied but that F5 is not enough and
		         users must log out and back in. Never promise a refresh you did not publish.
		       Why this is needed even though the platform self-heals sometimes: Creatio invalidates those caches from
		       an entity event listener on `SysUserInRole` / `SysAdminUnitInWorkplace` INSERT and DELETE only. So a
		       role grant made through `odata-create` may publish itself, while creating a workplace, moving a section,
		       or pointing `HomePageUId` at a home page invalidates nothing — and a row written by the binding tools
		       goes straight through the database engine, which raises no entity events at all. Do not rely on the
		       listener: ordering it correctly is fragile and it does not cover the section or home-page cases.

		       ## Verify
		       Read back after every mutation with `odata-read` (filter junctions by `SysWorkplace/Id`); do not
		       treat an install log as proof. After a MOVE, read BOTH the source and the target workplace — a
		       move is only correct when the row is present in one and absent from the other.
		       Bindings need their OWN check: a live row that reads back perfectly says nothing about what the
		       package will install, because the two carry different column sets. Check the binding BEFORE it
		       reaches any environment: `read-data-binding-db` with `package-name` and `binding-name` reports the
		       shipped column set directly, and it must carry every column listed in Ship every change as a data
		       binding. It lists a localizable column such as `Name` inline; the package export splits those into a
		       `Localization` folder, so `read-data-binding-db` shows one more column than `data.json` for the same
		       binding — that is the same binding, not a discrepancy. Fall back to `download-application` plus
		       extracting `Data/<Schema>_<Suffix>/data.json` only when you need the raw archive; it is several steps
		       for the same answer. That is how the missing columns were found. Reading the workplace back after
		       install (assert `SysApplicationClientTypeId`, `TypeId`, `LoaderId` are non-empty) CONFIRMS the
		       result but does not protect you, because a wrong first install cannot be repaired by re-installing.
		       """
	};

	/// <summary>
	/// Returns the workplaces guide covering workplace CRUD, role access, and section membership.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "workplaces-guidance")]
	[Description("Returns the clio MCP workplaces guide: create/update/delete a navigation workplace, grant/remove role access, and add/remove/move sections via the odata tools, shipping each change as a package data binding.")]
	public ResourceContents GetGuide() => Guide;
}
