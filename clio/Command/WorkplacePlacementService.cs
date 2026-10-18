using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command;

/// <summary>
/// What to place where: one section, one navigation workplace, the roles that see it, and the package that ships
/// the result.
/// </summary>
/// <param name="EnvironmentName">Registered clio environment.</param>
/// <param name="PackageName">Package that ships every row; blank resolves the environment's current package.</param>
/// <param name="SectionCode">The section's <c>SysModule.Code</c>.</param>
/// <param name="WorkplaceName">Workplace to create or reuse, matched by name and client type.</param>
/// <param name="ClientType"><c>web</c> or <c>mobile</c>.</param>
/// <param name="Roles"><c>SysAdminUnit</c> names or ids that see the workplace; may be empty.</param>
/// <param name="RemoveFromMyApplications">Move the section's <c>My applications</c> placement instead of adding one.</param>
/// <param name="Position">Optional workplace position in the switcher.</param>
/// <param name="Mode">
/// <c>existing</c> (the workplace must already exist), <c>new</c> (create it; refused when a same-named workplace
/// exists that the target package does not already ship), <c>none</c> (write nothing, only report where the section is),
/// or blank to reuse the workplace when it exists and create it otherwise.
/// </param>
/// <param name="WorkplaceType">
/// <c>general</c>, <c>portal</c> or <c>self-service</c>: the type a created workplace gets, and the type a reused one
/// must already have (its type is never changed). Blank keeps the platform default on create and checks nothing.
/// </param>
public sealed record WorkplacePlacementRequest(
	string EnvironmentName,
	string PackageName,
	string SectionCode,
	string WorkplaceName,
	string ClientType,
	IReadOnlyList<string> Roles,
	bool RemoveFromMyApplications,
	int? Position,
	string Mode = null,
	string WorkplaceType = null);

/// <summary>A workplace that holds the section.</summary>
/// <param name="Id">Workplace id.</param>
/// <param name="Name">Workplace name.</param>
public sealed record WorkplaceLocation(
	[property: JsonPropertyName("id")] Guid Id,
	[property: JsonPropertyName("name")] string Name);

/// <summary>The workplace the section was placed in.</summary>
/// <param name="Id">Workplace id.</param>
/// <param name="Name">Workplace name.</param>
/// <param name="ClientType">Client type name (Web or Mobile).</param>
/// <param name="Type">
/// The workplace type read back: <c>general</c>, <c>portal</c> or <c>self-service</c>, another type's name, or null
/// when the type is empty.
/// </param>
/// <param name="Position">Position read back after the write.</param>
/// <param name="Created">Whether this call created the workplace (false: an existing one was reused).</param>
/// <param name="BindingName">
/// Data binding that ships the workplace row; null when another package ships it, so it was used but not bound.
/// </param>
public sealed record WorkplacePlacementWorkplace(
	[property: JsonPropertyName("id")] Guid Id,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("client-type")] string ClientType,
	[property: JsonPropertyName("type")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string Type,
	[property: JsonPropertyName("position")] int Position,
	[property: JsonPropertyName("created")] bool Created,
	[property: JsonPropertyName("binding-name")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string BindingName);

/// <summary>The section placement (<c>SysModuleInWorkplace</c> row).</summary>
/// <param name="SectionId">The <c>SysModule</c> id.</param>
/// <param name="Code">The section code.</param>
/// <param name="PlacementId">The <c>SysModuleInWorkplace</c> row id.</param>
/// <param name="Action"><c>moved</c>, <c>created</c> or <c>already-present</c>.</param>
/// <param name="BindingName">Data binding that ships the placement row; null when another package ships it.</param>
public sealed record WorkplacePlacementSection(
	[property: JsonPropertyName("section-id")] Guid SectionId,
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("placement-id")] Guid PlacementId,
	[property: JsonPropertyName("action")] string Action,
	[property: JsonPropertyName("binding-name")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string BindingName);

/// <summary>One role grant (<c>SysAdminUnitInWorkplace</c> row).</summary>
/// <param name="Name">Role name.</param>
/// <param name="RoleId">The <c>SysAdminUnit</c> id.</param>
/// <param name="GrantId">The <c>SysAdminUnitInWorkplace</c> row id.</param>
/// <param name="Action"><c>granted</c> or <c>already-granted</c>.</param>
/// <param name="BindingName">Data binding that ships the grant row; null when another package ships it.</param>
public sealed record WorkplacePlacementRole(
	[property: JsonPropertyName("role-id")] Guid RoleId,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("grant-id")] Guid GrantId,
	[property: JsonPropertyName("action")] string Action,
	[property: JsonPropertyName("binding-name")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string BindingName);

/// <summary>A write the call started: a row upsert, or (with no <paramref name="Id"/>) the creation of an empty binding.</summary>
/// <param name="Schema">Entity schema of the row or binding.</param>
/// <param name="Id">The written row id; null for a created binding.</param>
/// <param name="BindingName">The binding the row was upserted into, or that was created.</param>
public sealed record WorkplacePlacementWrite(
	[property: JsonPropertyName("schema")] string Schema,
	[property: JsonPropertyName("id")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	Guid? Id,
	[property: JsonPropertyName("binding-name")] string BindingName);

/// <summary>The outcome of <see cref="IWorkplacePlacementService.Place"/>.</summary>
/// <param name="PackageName">Package that ships every row.</param>
/// <param name="Workplace">The target workplace.</param>
/// <param name="Section">The section placement.</param>
/// <param name="Roles">The role grants this call asked for.</param>
/// <param name="Warnings">Non-fatal findings the caller should relay.</param>
/// <param name="SectionWorkplaces">Every workplace that holds the section after the call, read back.</param>
/// <param name="NextStep">After a write: how the user gets to see the change; null when nothing was written.</param>
/// <param name="Error">
/// Set when the call failed after its first write: what failed. <paramref name="Written"/> then lists what is already
/// on the environment, and <paramref name="Workplace"/>, <paramref name="Section"/> and
/// <paramref name="SectionWorkplaces"/> are null.
/// </param>
/// <param name="Written">Every write started before the failure, the failing one included; null on success.</param>
/// <remarks>With mode <c>none</c>, <paramref name="PackageName"/>, <paramref name="Workplace"/> and
/// <paramref name="Section"/> are null and nothing was written.</remarks>
public sealed record WorkplacePlacementResult(
	string PackageName,
	WorkplacePlacementWorkplace Workplace,
	WorkplacePlacementSection Section,
	IReadOnlyList<WorkplacePlacementRole> Roles,
	IReadOnlyList<string> Warnings,
	IReadOnlyList<WorkplaceLocation> SectionWorkplaces,
	string NextStep = null,
	string Error = null,
	IReadOnlyList<WorkplacePlacementWrite> Written = null);

/// <summary>
/// Places a section in a navigation workplace, grants roles access to it, and ships every row it touches as a
/// package data binding.
/// </summary>
public interface IWorkplacePlacementService {

	/// <summary>
	/// Creates or reuses the workplace (matched by name and client type), places the section in it — moving the
	/// <c>My applications</c> placement when <see cref="WorkplacePlacementRequest.RemoveFromMyApplications"/> is
	/// set — grants the roles, binds every row into the package, and reads the result back. Re-running with the
	/// same request changes nothing. Nothing is deleted: a row the call does not own is left in place and reported
	/// as a warning. A move out of <c>My applications</c> is refused before any write when no role would see the
	/// target workplace, or when another package ships the moved row (installing it would undo the move). An
	/// existing workplace another package ships is used but never bound into the target package, so installing
	/// either package cannot overwrite or remove the other's workplace; a position change for it is refused. A
	/// requested <see cref="WorkplacePlacementRequest.WorkplaceType"/> is set on a created workplace; a reused one of
	/// another type is refused before any write, because the type is never changed.
	/// </summary>
	/// <returns>
	/// The placement; or, when a step failed after the first write, a result whose
	/// <see cref="WorkplacePlacementResult.Error"/> says what failed and whose
	/// <see cref="WorkplacePlacementResult.Written"/> lists what is already on the environment. The navigation
	/// cache is reset in both cases, and a re-run converges.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// A precondition failed. Every input (package, client type, section, roles) is resolved before the first
	/// write, so such a failure changes nothing.
	/// </exception>
	WorkplacePlacementResult Place(WorkplacePlacementRequest request);
}

/// <inheritdoc />
internal sealed class WorkplacePlacementService(
	IWorkplaceRepository repository,
	IDataBindingDbService dataBindingDbService,
	IPackageTargetResolver packageTargetResolver,
	INavigationCacheResetter navigationCacheResetter,
	IApplicationClient applicationClient,
	EnvironmentSettings environmentSettings) : IWorkplacePlacementService {

	/// <summary>The binding <c>create-app</c> ships for <c>My applications</c>; culture-independent, unlike its name.</summary>
	internal const string MyApplicationsBindingName = "SysWorkplace_MyApps";

	/// <summary>The re-login instruction every navigation change needs.</summary>
	internal const string ReloginNote =
		"Workplace and section lists are cached per session: tell the user to log out and back in to see the change " +
		"(a browser refresh is not enough; no restart is needed).";

	// Per call: schema -> binding name -> bound row ids in the target package, so each binding is read once.
	private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _boundIds =
		new(StringComparer.OrdinalIgnoreCase);

	// Per call: every write made so far, reported when a later step fails.
	private readonly List<WorkplacePlacementWrite> _written = [];

	/// <summary>Name of the default workplace <c>create-app</c> places new sections in.</summary>
	internal const string MyApplicationsWorkplaceName = "My applications";

	internal const string WorkplaceSchema = "SysWorkplace";
	internal const string SectionPlacementSchema = "SysModuleInWorkplace";
	internal const string RoleGrantSchema = "SysAdminUnitInWorkplace";

	internal const string ActionMoved = "moved";
	internal const string ActionCreated = "created";
	internal const string ActionAlreadyPresent = "already-present";
	internal const string ActionGranted = "granted";
	internal const string ActionAlreadyGranted = "already-granted";

	internal const string ModeExisting = "existing";
	internal const string ModeNew = "new";
	internal const string ModeNone = "none";

	private static readonly Dictionary<string, string> ClientTypeNames = new(StringComparer.OrdinalIgnoreCase) {
		["web"] = "Web",
		["mobile"] = "Mobile"
	};

	/// <summary>
	/// The accepted workplace-type values: matched by the platform-seeded id, and by the culture-independent
	/// <c>SysWorkplaceType.Code</c> only when no seeded row exists and exactly one row carries the code.
	/// </summary>
	private static readonly IReadOnlyList<(string Value, int Code, Guid SeededId)> WorkplaceTypes = [
		("general", 0, new Guid("000a9225-728b-4778-a951-c42439ffe024")),
		("portal", 1, new Guid("111c7ff1-8224-45b0-a24e-fcc983fc0c70")),
		("self-service", 3, new Guid("a77fefc0-227d-4e21-9992-a9a7ee889e21"))
	];

	/// <inheritdoc />
	public WorkplacePlacementResult Place(WorkplacePlacementRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		Validate(request);
		_boundIds.Clear();
		_written.Clear();
		List<string> warnings = [];
		string mode = NormalizeMode(request.Mode);
		if (mode == ModeNone) {
			// Leave the section where it is: nothing is resolved for writing and nothing is written.
			SectionModuleRecord unchanged = ResolveSection(request.SectionCode.Trim());
			return new WorkplacePlacementResult(null, null, null, [], warnings,
				ToLocations(repository.FindSectionPlacements(unchanged.Id)));
		}

		// Resolve every input before the first write, so a wrong argument changes nothing.
		PackageTargetResolution package = packageTargetResolver.Resolve(request.PackageName, requireEditable: true);
		if (!package.Success) {
			throw new InvalidOperationException(package.Error ?? "The target package could not be resolved.");
		}
		string clientTypeName = ClientTypeNames[request.ClientType.Trim()];
		Guid clientTypeId = repository.FindClientTypeId(clientTypeName)
			?? throw new InvalidOperationException(
				$"SysApplicationClientType '{clientTypeName}' was not found in the environment.");
		SectionModuleRecord section = ResolveSection(request.SectionCode.Trim());
		IReadOnlyList<AdminUnitRecord> roles = ResolveRoles(request.Roles ?? []);
		IReadOnlyList<WorkplaceTypeRecord> workplaceTypes = repository.FindWorkplaceTypes();
		Guid? requestedTypeId = ResolveWorkplaceType(request.WorkplaceType, workplaceTypes);
		string workplaceName = request.WorkplaceName.Trim();
		string bindingSuffix = BuildBindingSuffix(workplaceName, section.Code);
		HashSet<Guid> myApplicationsIds = FindMyApplicationsIds(request, clientTypeId);
		IReadOnlyList<WorkplaceJunctionRecord> sectionPlacements = repository.FindSectionPlacements(section.Id);
		HashSet<Guid> holdingSection = sectionPlacements.Select(row => row.WorkplaceId).ToHashSet();
		IReadOnlyList<WorkplaceRecord> existing = repository.FindWorkplaces(workplaceName, clientTypeId);
		// Prefer one of the requested type, then the one that already holds the section, so a re-run never spreads
		// the section over two of them.
		WorkplaceRecord reused = existing
			.OrderByDescending(record => requestedTypeId is null || record.TypeId == requestedTypeId)
			.ThenByDescending(record => holdingSection.Contains(record.Id))
			.FirstOrDefault();
		CheckModeAgainstExistingWorkplaces(request, package, mode, workplaceName, existing);
		CheckWorkplaceType(reused, requestedTypeId, workplaceTypes);
		PackageBindingRecord workplaceOwner = FindForeignWorkplaceOwner(request, package, reused);
		CheckMoveBeforeWriting(request, package, roles, sectionPlacements, myApplicationsIds, existing, reused);

		WorkplacePlacementResult placed;
		if (existing.Count > 1 && reused is not null) {
			warnings.Add($"{existing.Count} workplaces are named '{workplaceName}' for this client type " +
				$"({string.Join(", ", existing.Select(record => record.Id))}); {reused.Id} was used.");
		}
		try {
			WorkplaceState workplace = EnsureWorkplace(request, package, clientTypeId, requestedTypeId, bindingSuffix,
				reused, workplaceOwner, warnings);
			// Grant before placing: a failed grant then leaves the section where it was, not in a workplace nobody sees.
			List<WorkplacePlacementRole> grants = roles
				.Select(role => EnsureRoleGrant(request, package, workplace.Record.Id, role, bindingSuffix))
				.ToList();
			WorkplacePlacementSection placement = EnsureSectionPlacement(request, package, section, workplace.Record,
				sectionPlacements, myApplicationsIds, warnings);

			ReadBack readBack = VerifyReadBack(section, workplace.Record, placement, grants);
			if (readBack.GrantCount == 0) {
				warnings.Add("The workplace has no role grant, so nobody sees it. Pass roles to grant access.");
			}
			placed = new WorkplacePlacementResult(
				package.PackageName,
				new WorkplacePlacementWorkplace(workplace.Record.Id, workplace.Record.Name, clientTypeName,
					DescribeWorkplaceType(workplace.Record.TypeId, workplaceTypes), workplace.Record.Position,
					workplace.Created, workplace.BindingName),
				placement,
				grants,
				warnings,
				ToLocations(readBack.Placements));
		}
		catch (Exception ex) when (_written.Count > 0 && ex is not OperationCanceledException) {
			// Writes had started: report what is already on the environment instead of an error that reads as
			// "nothing changed", and still reset the navigation cache the written rows invalidate.
			string nextStep = ResetNavigationCache(warnings);
			return new WorkplacePlacementResult(package.PackageName, null, null, [], warnings, null, nextStep,
				$"{ex.Message} The call failed after its first write: the rows in 'written' are already on the " +
				"environment. Re-run the same call to converge.",
				_written.ToList());
		}
		return placed with { NextStep = ResetNavigationCache(warnings) };
	}

	private string ResetNavigationCache(List<string> warnings) {
		string warning = navigationCacheResetter.TryReset(applicationClient, environmentSettings);
		if (!string.IsNullOrWhiteSpace(warning)) {
			warnings.Add(warning);
		}
		return $"{ReloginNote} {navigationCacheResetter.BuildBrowserSessionNote(environmentSettings)}";
	}

	/// <summary>
	/// The <c>My applications</c> workplaces of the client type: matched by the binding <c>create-app</c> ships for
	/// them (culture-independent) and by the en-US name.
	/// </summary>
	private HashSet<Guid> FindMyApplicationsIds(WorkplacePlacementRequest request, Guid clientTypeId) {
		HashSet<Guid> ids = repository.FindWorkplaces(MyApplicationsWorkplaceName, clientTypeId)
			.Select(workplace => workplace.Id).ToHashSet();
		if (ids.Count > 0) {
			return ids;
		}
		// The name is localizable: on a non-English profile, find the workplace through create-app's binding.
		foreach (string packageName in repository.FindPackagesShippingBinding(MyApplicationsBindingName)) {
			BoundBindingProjection binding = dataBindingDbService.ReadBinding(new ReadDataBindingDbOptions {
				Environment = request.EnvironmentName,
				PackageName = packageName,
				BindingName = MyApplicationsBindingName
			});
			foreach (IReadOnlyDictionary<string, string> row in binding.Rows) {
				if (row.TryGetValue("Id", out string value) && Guid.TryParse(value, out Guid id)
					&& repository.GetWorkplace(id)?.ClientTypeId == clientTypeId) {
					ids.Add(id);
				}
			}
			if (ids.Count > 0) {
				// Every app's binding names the same shared workplace; one hit is enough.
				break;
			}
		}
		return ids;
	}

	private static Guid? ResolveWorkplaceType(string value, IReadOnlyList<WorkplaceTypeRecord> types) {
		if (string.IsNullOrWhiteSpace(value)) {
			return null;
		}
		(string name, int code, Guid seededId) = FindKnownWorkplaceType(value);
		WorkplaceTypeRecord match = FindWorkplaceTypeRow(code, seededId, types);
		return match?.Id ?? throw new InvalidOperationException(
			$"Workplace type '{name}' (SysWorkplaceType code {code}) was not found in the environment.");
	}

	private static (string Value, int Code, Guid SeededId) FindKnownWorkplaceType(string value) =>
		WorkplaceTypes.FirstOrDefault(type =>
			string.Equals(type.Value, value.Trim(), StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// The row of a known workplace type: the platform-seeded id first; otherwise the only row that carries the code.
	/// Null when neither exists or the code is ambiguous.
	/// </summary>
	private static WorkplaceTypeRecord FindWorkplaceTypeRow(int code, Guid seededId,
		IReadOnlyList<WorkplaceTypeRecord> types) {
		WorkplaceTypeRecord seeded = types.FirstOrDefault(type => type.Id == seededId);
		if (seeded is not null) {
			return seeded;
		}
		List<WorkplaceTypeRecord> coded = types.Where(type => type.Code == code).ToList();
		return coded.Count == 1 ? coded[0] : null;
	}

	/// <summary>The workplace type as the argument spells it; another type by its name; null when empty.</summary>
	private static string DescribeWorkplaceType(Guid typeId, IReadOnlyList<WorkplaceTypeRecord> types) {
		if (typeId == Guid.Empty) {
			return null;
		}
		WorkplaceTypeRecord record = types.FirstOrDefault(type => type.Id == typeId);
		(string Value, int Code, Guid SeededId) known = WorkplaceTypes.FirstOrDefault(type =>
			type.SeededId == typeId || FindWorkplaceTypeRow(type.Code, type.SeededId, types)?.Id == typeId);
		return known.Value ?? record?.Name ?? typeId.ToString();
	}

	/// <summary>
	/// Refuses, before any write, to use an existing workplace whose type differs from the requested one: the type is
	/// never changed, and placing the section there would ship it to the wrong audience.
	/// </summary>
	private static void CheckWorkplaceType(WorkplaceRecord reused, Guid? requestedTypeId,
		IReadOnlyList<WorkplaceTypeRecord> types) {
		if (reused is null || requestedTypeId is null || reused.TypeId == requestedTypeId) {
			return;
		}
		string current = DescribeWorkplaceType(reused.TypeId, types) ?? "empty";
		string requested = DescribeWorkplaceType(requestedTypeId.Value, types);
		throw new InvalidOperationException(
			$"Workplace '{reused.Name}' ({reused.Id}) is of type {current}, not {requested}; this tool never changes " +
			"the type of an existing workplace, so nothing was written. Choose another workplace-name to create a " +
			$"{requested} workplace, or omit workplace-type to use this one as it is.");
	}

	private static string NormalizeMode(string mode) =>
		string.IsNullOrWhiteSpace(mode) ? null : mode.Trim().ToLowerInvariant();

	private void CheckModeAgainstExistingWorkplaces(WorkplacePlacementRequest request, PackageTargetResolution package,
		string mode, string workplaceName, IReadOnlyList<WorkplaceRecord> existing) {
		if (mode is null) {
			return;
		}
		if (mode == ModeExisting && existing.Count == 0) {
			throw new InvalidOperationException(
				$"No workplace named '{workplaceName}' exists for this client type; pass mode=new to create it.");
		}
		if (mode == ModeNew && existing.Count > 0) {
			// A re-run of an earlier mode=new call finds its own workplace already shipped by this package, even when
			// that call failed before placing the section. A same-named workplace made any other way is refused.
			bool ownRerun = existing.Any(workplace =>
				FindBindingContaining(request, package, WorkplaceSchema, workplace.Id) is not null);
			if (!ownRerun) {
				throw new InvalidOperationException(
					$"A workplace named '{workplaceName}' already exists ({string.Join(", ", existing.Select(w => w.Id))}); " +
					"a second one with the same name would compete for every binding. Pass mode=existing to use it, " +
					"or choose another name.");
			}
		}
	}

	/// <summary>
	/// Refuses, before any write, a move out of <c>My applications</c> that would hide the section (no role sees the
	/// target) or that another package would undo on install (it ships the moved row).
	/// </summary>
	private void CheckMoveBeforeWriting(WorkplacePlacementRequest request, PackageTargetResolution package,
		IReadOnlyList<AdminUnitRecord> roles,
		IReadOnlyList<WorkplaceJunctionRecord> sectionPlacements, HashSet<Guid> myApplicationsIds,
		IReadOnlyList<WorkplaceRecord> existing, WorkplaceRecord reused) {
		if (!request.RemoveFromMyApplications) {
			return;
		}
		HashSet<Guid> holdingSection = sectionPlacements.Select(row => row.WorkplaceId).ToHashSet();
		if (reused is not null && (holdingSection.Contains(reused.Id) || myApplicationsIds.Contains(reused.Id))) {
			// Already placed in the target, or the target is My applications itself: nothing is moved.
			return;
		}
		WorkplaceJunctionRecord toMove = sectionPlacements
			.FirstOrDefault(row => myApplicationsIds.Contains(row.WorkplaceId));
		if (toMove is null) {
			return;
		}
		bool targetHasGrant = reused is not null && repository.FindRoleGrants(reused.Id).Count > 0;
		if (roles.Count == 0 && !targetHasGrant) {
			throw new InvalidOperationException(
				$"Moving the section out of '{MyApplicationsWorkplaceName}' into '{request.WorkplaceName.Trim()}', which no role sees, " +
				"would hide the app from everyone. Pass roles, or remove-from-my-applications=false to add a placement.");
		}
		// Checked even when the target package ships the row too: two packages shipping it is decided by install order.
		PackageBindingRecord owner = FindBindingInOtherPackage(request, package, SectionPlacementSchema, toMove.Id);
		if (owner is not null) {
			throw new InvalidOperationException(
				$"The section's '{MyApplicationsWorkplaceName}' placement {toMove.Id} is shipped by package " +
				$"'{owner.PackageName}' (binding {owner.BindingName}), so installing that package would undo the " +
				$"move. Pass package-name={owner.PackageName}, or remove-from-my-applications=false.");
		}
	}

	/// <summary>
	/// The package that ships the reused workplace when the target package does not: that workplace is used but
	/// not bound, and a position change for it is refused before any write.
	/// </summary>
	private PackageBindingRecord FindForeignWorkplaceOwner(WorkplacePlacementRequest request,
		PackageTargetResolution package, WorkplaceRecord reused) {
		if (reused is null || FindBindingContaining(request, package, WorkplaceSchema, reused.Id) is not null) {
			return null;
		}
		PackageBindingRecord owner = FindBindingInOtherPackage(request, package, WorkplaceSchema, reused.Id);
		if (owner is not null && request.Position is not null && request.Position != reused.Position) {
			throw new InvalidOperationException(
				$"Workplace '{reused.Name}' ({reused.Id}) is shipped by package '{owner.PackageName}', so its " +
				"position is not this package's to change. Omit position, or use a workplace of your own.");
		}
		return owner;
	}

	private PackageBindingRecord FindBindingInOtherPackage(WorkplacePlacementRequest request,
		PackageTargetResolution package, string schemaName, Guid rowId) {
		string id = rowId.ToString();
		foreach (PackageBindingRecord binding in repository.FindBindingsShippingRow(schemaName, rowId)
			.Where(binding => !string.Equals(binding.PackageName, package.PackageName, StringComparison.OrdinalIgnoreCase))) {
			if (!binding.Unverified) {
				return binding;
			}
			// Bound data the one-query read could not parse: read that binding alone instead of failing open.
			BoundBindingProjection rows = dataBindingDbService.ReadBinding(new ReadDataBindingDbOptions {
				Environment = request.EnvironmentName,
				PackageName = binding.PackageName,
				BindingName = binding.BindingName
			});
			if (rows.Rows.Any(row => row.TryGetValue("Id", out string boundId)
				&& string.Equals(boundId, id, StringComparison.OrdinalIgnoreCase))) {
				return binding;
			}
		}
		return null;
	}

	private static IReadOnlyList<WorkplaceLocation> ToLocations(IReadOnlyList<WorkplaceJunctionRecord> placements) =>
		placements
			.GroupBy(row => row.WorkplaceId)
			.Select(group => new WorkplaceLocation(group.Key, group.First().WorkplaceName))
			.ToList();

	private static void Validate(WorkplacePlacementRequest request) {
		if (string.IsNullOrWhiteSpace(request.SectionCode)) {
			throw new InvalidOperationException("section-code is required.");
		}
		string mode = NormalizeMode(request.Mode);
		if (mode is not null and not (ModeExisting or ModeNew or ModeNone)) {
			throw new InvalidOperationException("mode must be 'existing', 'new' or 'none'.");
		}
		if (mode == ModeNone) {
			if (request.Roles is { Count: > 0 }) {
				throw new InvalidOperationException("mode=none writes nothing, so roles cannot be granted; omit roles.");
			}
			return;
		}
		if (string.IsNullOrWhiteSpace(request.WorkplaceName)) {
			throw new InvalidOperationException("workplace-name is required unless mode is 'none'.");
		}
		if (string.IsNullOrWhiteSpace(request.ClientType) || !ClientTypeNames.ContainsKey(request.ClientType.Trim())) {
			throw new InvalidOperationException("client-type must be 'web' or 'mobile'.");
		}
		if (!string.IsNullOrWhiteSpace(request.WorkplaceType) && FindKnownWorkplaceType(request.WorkplaceType).Value is null) {
			throw new InvalidOperationException(
				$"workplace-type must be one of: {string.Join(", ", WorkplaceTypes.Select(type => type.Value))}.");
		}
		if (request.Roles?.Any(string.IsNullOrWhiteSpace) == true) {
			throw new InvalidOperationException("roles must not contain blank entries.");
		}
		if (string.Equals(request.WorkplaceName.Trim(), MyApplicationsWorkplaceName, StringComparison.OrdinalIgnoreCase)
			&& request.RemoveFromMyApplications) {
			throw new InvalidOperationException(
				$"The target is '{MyApplicationsWorkplaceName}' itself; pass remove-from-my-applications=false.");
		}
	}

	private SectionModuleRecord ResolveSection(string code) {
		IReadOnlyList<SectionModuleRecord> candidates = repository.FindSectionsByCode(code);
		if (candidates.Count == 0) {
			throw new InvalidOperationException(
				$"No section with SysModule.Code '{code}' was found. Pass the section code, not its caption.");
		}
		if (candidates.Count == 1) {
			return candidates[0];
		}
		// Codes are not unique; the registration a workplace already uses is the real one.
		List<SectionModuleRecord> placed = candidates
			.Where(candidate => repository.FindSectionPlacements(candidate.Id).Count > 0)
			.ToList();
		if (placed.Count == 1) {
			return placed[0];
		}
		throw new InvalidOperationException(
			$"SysModule.Code '{code}' matches {candidates.Count} sections " +
			$"({string.Join(", ", candidates.Select(candidate => candidate.Id))}) and they cannot be told apart.");
	}

	private IReadOnlyList<AdminUnitRecord> ResolveRoles(IReadOnlyList<string> roles) {
		List<AdminUnitRecord> resolved = [];
		foreach (string role in roles.Select(value => value.Trim())) {
			AdminUnitRecord record;
			if (Guid.TryParse(role, out Guid roleId)) {
				record = repository.GetAdminUnit(roleId)
					?? throw new InvalidOperationException($"No role with id '{roleId}' was found in SysAdminUnit.");
			}
			else {
				IReadOnlyList<AdminUnitRecord> matches = repository.FindAdminUnitsByName(role);
				record = matches.Count switch {
					0 => throw new InvalidOperationException($"No role named '{role}' was found in SysAdminUnit."),
					1 => matches[0],
					_ => throw new InvalidOperationException(
						$"Role name '{role}' matches {matches.Count} SysAdminUnit rows " +
						$"({string.Join(", ", matches.Select(match => match.Id))}); pass the id instead.")
				};
			}
			if (resolved.All(existing => existing.Id != record.Id)) {
				resolved.Add(record);
			}
		}
		return resolved;
	}

	private WorkplaceState EnsureWorkplace(WorkplacePlacementRequest request, PackageTargetResolution package,
		Guid clientTypeId, Guid? typeId, string bindingSuffix, WorkplaceRecord reused, PackageBindingRecord foreignOwner,
		List<string> warnings) {
		string workplaceName = request.WorkplaceName.Trim();
		if (foreignOwner is not null) {
			// Another package owns the row: binding it here too would let either install overwrite or remove it.
			warnings.Add($"Workplace '{reused.Name}' is shipped by package '{foreignOwner.PackageName}', so it was " +
				$"used but not bound into '{package.PackageName}'; an environment that installs '{package.PackageName}' " +
				$"needs '{foreignOwner.PackageName}' for the workplace.");
			return new WorkplaceState(reused, false, null);
		}
		bool created = reused is null;
		Guid workplaceId;
		string bindingName;
		if (created) {
			workplaceId = Guid.NewGuid();
			bindingName = WorkplaceBindingName(bindingSuffix);
			EnsureBinding(request, package, WorkplaceSchema, bindingName);
			// The live insert lets the platform assign Position, LoaderId and (unless requested) Type; they are read
			// back below.
			Dictionary<string, object> inserted = new() {
				["Id"] = workplaceId,
				["Name"] = workplaceName,
				["SysApplicationClientType"] = clientTypeId
			};
			if (typeId is not null) {
				inserted["Type"] = typeId.Value;
			}
			Upsert(request, package, WorkplaceSchema, bindingName, inserted);
		}
		else {
			workplaceId = reused.Id;
			bindingName = FindBindingContaining(request, package, WorkplaceSchema, workplaceId);
			if (bindingName is null) {
				bindingName = WorkplaceBindingName(bindingSuffix);
				EnsureBinding(request, package, WorkplaceSchema, bindingName);
				warnings.Add($"Workplace '{workplaceName}' already existed and was not shipped by package " +
					$"'{package.PackageName}'; it is now bound there, so installing the package ships it.");
			}
		}

		WorkplaceRecord record = repository.GetWorkplace(workplaceId)
			?? throw new InvalidOperationException($"Workplace {workplaceId} could not be read back.");
		int position = request.Position ?? record.Position;
		Dictionary<string, object> values = new() {
			["Id"] = record.Id,
			["Name"] = record.Name ?? workplaceName,
			["Position"] = position,
			["SysApplicationClientType"] = clientTypeId
		};
		// An install supplies no defaults: a binding without Type and LoaderId installs a broken workplace.
		if (record.TypeId != Guid.Empty) {
			values["Type"] = record.TypeId;
		}
		if (record.LoaderId != Guid.Empty) {
			values["LoaderId"] = record.LoaderId;
		}
		if (record.TypeId == Guid.Empty || record.LoaderId == Guid.Empty) {
			warnings.Add($"Workplace {record.Id} has an empty Type or LoaderId, so its binding cannot carry them " +
				"and installing the package elsewhere creates an incomplete workplace.");
		}
		Upsert(request, package, WorkplaceSchema, bindingName, values);
		WorkplaceRecord readBack = repository.GetWorkplace(workplaceId) ?? record;
		return new WorkplaceState(readBack, created, bindingName);
	}

	private WorkplacePlacementSection EnsureSectionPlacement(WorkplacePlacementRequest request,
		PackageTargetResolution package, SectionModuleRecord section, WorkplaceRecord target,
		IReadOnlyList<WorkplaceJunctionRecord> placements, HashSet<Guid> myApplicationsIds, List<string> warnings) {
		// The placements read before the first write: the workplace and grant writes do not change them.
		bool myApplicationsUnknown = myApplicationsIds.Count == 0;
		if (myApplicationsIds.Contains(target.Id)) {
			// The target is My applications itself: nothing to move out of.
			myApplicationsIds = [];
		}
		List<WorkplaceJunctionRecord> inMyApplications = placements
			.Where(placement => myApplicationsIds.Contains(placement.WorkplaceId))
			.ToList();
		WorkplaceJunctionRecord inTarget = placements.FirstOrDefault(placement => placement.WorkplaceId == target.Id);

		Guid placementId;
		string action;
		if (inTarget is not null) {
			placementId = inTarget.Id;
			action = ActionAlreadyPresent;
			if (request.RemoveFromMyApplications && inMyApplications.Count > 0) {
				warnings.Add($"The section is also in '{MyApplicationsWorkplaceName}' " +
					$"(row {string.Join(", ", inMyApplications.Select(row => row.Id))}); it was left there because " +
					"this tool never deletes a placement.");
			}
		}
		else if (request.RemoveFromMyApplications && inMyApplications.Count > 0) {
			placementId = inMyApplications[0].Id;
			action = ActionMoved;
			if (inMyApplications.Count > 1) {
				warnings.Add($"The section has {inMyApplications.Count} placements in '{MyApplicationsWorkplaceName}'; " +
					$"only {placementId} was moved.");
			}
		}
		else {
			placementId = Guid.NewGuid();
			action = ActionCreated;
			if (request.RemoveFromMyApplications && myApplicationsUnknown
				&& placements.Any(placement => placement.WorkplaceId != target.Id)) {
				warnings.Add($"'{MyApplicationsWorkplaceName}' could not be identified (no en-US name and no " +
					$"{MyApplicationsBindingName} binding), so the section was added to '{target.Name}', not moved; " +
					"it also stays in its other workplaces (see section-workplaces).");
			}
		}

		return WritePlacement(request, package, section, target, placementId, action);
	}

	/// <summary>Binds and upserts the placement row, unless another package ships it already in the target.</summary>
	private WorkplacePlacementSection WritePlacement(WorkplacePlacementRequest request,
		PackageTargetResolution package, SectionModuleRecord section, WorkplaceRecord target, Guid placementId,
		string action) {
		// A new row cannot be in any binding yet, so only an existing one is looked up.
		string bindingName = action == ActionCreated
			? null
			: FindBindingContaining(request, package, SectionPlacementSchema, placementId);
		if (action == ActionAlreadyPresent && bindingName is null
			&& FindBindingInOtherPackage(request, package, SectionPlacementSchema, placementId) is not null) {
			// Another package ships the placement already in the target: leave it to that package.
			return new WorkplacePlacementSection(section.Id, section.Code, placementId, action, null);
		}
		if (bindingName is null) {
			bindingName = $"{SectionPlacementSchema}_{BuildBindingSuffix(section.Code, section.Code)}";
			// A moved row no other package ships (checked before the first write) is now shipped from this one.
			EnsureBinding(request, package, SectionPlacementSchema, bindingName);
		}
		Upsert(request, package, SectionPlacementSchema, bindingName, new Dictionary<string, object> {
			["Id"] = placementId,
			["SysWorkplace"] = target.Id,
			["SysModule"] = section.Id
		});
		return new WorkplacePlacementSection(section.Id, section.Code, placementId, action, bindingName);
	}

	private WorkplacePlacementRole EnsureRoleGrant(WorkplacePlacementRequest request, PackageTargetResolution package,
		Guid workplaceId, AdminUnitRecord role, string bindingSuffix) {
		WorkplaceJunctionRecord existing = repository.FindRoleGrants(workplaceId)
			.FirstOrDefault(grant => grant.TargetId == role.Id);
		Guid grantId = existing?.Id ?? Guid.NewGuid();
		string bindingName = existing is null ? null : FindBindingContaining(request, package, RoleGrantSchema, grantId);
		if (existing is not null && bindingName is null
			&& FindBindingInOtherPackage(request, package, RoleGrantSchema, grantId) is not null) {
			// Another package ships this grant: it already exists, and binding it here too would make two owners.
			return new WorkplacePlacementRole(role.Id, role.Name, grantId, ActionAlreadyGranted, null);
		}
		if (bindingName is null) {
			bindingName = $"{RoleGrantSchema}_{bindingSuffix}";
			EnsureBinding(request, package, RoleGrantSchema, bindingName);
		}
		Upsert(request, package, RoleGrantSchema, bindingName, new Dictionary<string, object> {
			["Id"] = grantId,
			["SysWorkplace"] = workplaceId,
			["SysAdminUnit"] = role.Id
		});
		return new WorkplacePlacementRole(role.Id, role.Name, grantId,
			existing is null ? ActionGranted : ActionAlreadyGranted, bindingName);
	}

	private ReadBack VerifyReadBack(SectionModuleRecord section, WorkplaceRecord target,
		WorkplacePlacementSection placement, IReadOnlyList<WorkplacePlacementRole> grants) {
		IReadOnlyList<WorkplaceJunctionRecord> placements = repository.FindSectionPlacements(section.Id);
		if (!placements.Any(row => row.Id == placement.PlacementId && row.WorkplaceId == target.Id)) {
			throw new InvalidOperationException(
				$"Read-back failed: placement {placement.PlacementId} is not in workplace {target.Id}.");
		}
		HashSet<Guid> grantedRoles = repository.FindRoleGrants(target.Id).Select(grant => grant.TargetId).ToHashSet();
		WorkplacePlacementRole missing = grants.FirstOrDefault(grant => !grantedRoles.Contains(grant.RoleId));
		if (missing is not null) {
			throw new InvalidOperationException(
				$"Read-back failed: role '{missing.Name}' has no grant on workplace {target.Id}.");
		}
		return new ReadBack(placements, grantedRoles.Count);
	}

	private string FindBindingContaining(WorkplacePlacementRequest request, PackageTargetResolution package,
		string schemaName, Guid rowId) {
		string id = rowId.ToString();
		return LoadBindings(request, package, schemaName)
			.Where(binding => binding.Value.Contains(id))
			.Select(binding => binding.Key)
			.FirstOrDefault();
	}

	private Dictionary<string, HashSet<string>> LoadBindings(WorkplacePlacementRequest request,
		PackageTargetResolution package, string schemaName) {
		if (_boundIds.TryGetValue(schemaName, out Dictionary<string, HashSet<string>> cached)) {
			return cached;
		}
		Dictionary<string, HashSet<string>> bindings = new(StringComparer.OrdinalIgnoreCase);
		foreach (string bindingName in repository.FindBindingNames(package.PackageUId, schemaName)) {
			BoundBindingProjection binding = dataBindingDbService.ReadBinding(new ReadDataBindingDbOptions {
				Environment = request.EnvironmentName,
				PackageName = package.PackageName,
				BindingName = bindingName
			});
			bindings[bindingName] = binding.Rows
				.Select(row => row.TryGetValue("Id", out string boundId) ? boundId : null)
				.Where(boundId => !string.IsNullOrWhiteSpace(boundId))
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
		}
		_boundIds[schemaName] = bindings;
		return bindings;
	}

	private void EnsureBinding(WorkplacePlacementRequest request, PackageTargetResolution package, string schemaName,
		string bindingName) {
		Dictionary<string, HashSet<string>> bindings = LoadBindings(request, package, schemaName);
		if (bindings.ContainsKey(bindingName)) {
			return;
		}
		// Created empty on purpose: create-data-binding-db matches a row by Name before Id, which would bind a
		// different same-named workplace. Rows go in through the upsert, which matches by Id only.
		// Recorded before the call: a call that fails half-way may already have changed the environment.
		_written.Add(new WorkplacePlacementWrite(schemaName, null, bindingName));
		dataBindingDbService.CreateBinding(new CreateDataBindingDbOptions {
			Environment = request.EnvironmentName,
			PackageName = package.PackageName,
			SchemaName = schemaName,
			BindingName = bindingName
		});
		bindings[bindingName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	}

	private void Upsert(WorkplacePlacementRequest request, PackageTargetResolution package, string schemaName,
		string bindingName, Dictionary<string, object> values) {
		// Recorded before the call: the upsert writes the live row before it saves the binding.
		_written.Add(new WorkplacePlacementWrite(schemaName, (Guid)values["Id"], bindingName));
		dataBindingDbService.UpsertRow(new UpsertDataBindingRowDbOptions {
			Environment = request.EnvironmentName,
			PackageName = package.PackageName,
			BindingName = bindingName,
			ValuesJson = JsonSerializer.Serialize(values)
		});
		foreach (Dictionary<string, HashSet<string>> bindings in _boundIds.Values) {
			if (bindings.TryGetValue(bindingName, out HashSet<string> ids)) {
				ids.Add(values["Id"].ToString());
			}
		}
	}

	/// <summary>
	/// The workplace binding name; never <see cref="MyApplicationsBindingName"/>, which identifies <c>My applications</c>.
	/// </summary>
	internal static string WorkplaceBindingName(string suffix) {
		string name = $"{WorkplaceSchema}_{suffix}";
		return string.Equals(name, MyApplicationsBindingName, StringComparison.OrdinalIgnoreCase)
			? $"{name}Workplace"
			: name;
	}

	/// <summary>
	/// Builds the per-table binding-name suffix from <paramref name="name"/>: its ASCII letters and digits, or
	/// <paramref name="fallback"/>'s when it has none.
	/// </summary>
	internal static string BuildBindingSuffix(string name, string fallback) {
		static string Clean(string value) {
			StringBuilder builder = new();
			foreach (char character in (value ?? string.Empty).Where(char.IsAsciiLetterOrDigit)) {
				builder.Append(character);
			}
			return builder.ToString();
		}
		string suffix = Clean(name);
		return suffix.Length > 0 ? suffix : Clean(fallback);
	}

	private sealed record WorkplaceState(WorkplaceRecord Record, bool Created, string BindingName);

	private sealed record ReadBack(IReadOnlyList<WorkplaceJunctionRecord> Placements, int GrantCount);
}
