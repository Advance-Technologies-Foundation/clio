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
/// that does not already hold the section exists), <c>none</c> (write nothing, only report where the section is),
/// or blank to reuse the workplace when it exists and create it otherwise.
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
	string Mode = null);

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
/// <param name="Position">Position read back after the write.</param>
/// <param name="Created">Whether this call created the workplace (false: an existing one was reused).</param>
/// <param name="BindingName">Data binding that ships the workplace row.</param>
public sealed record WorkplacePlacementWorkplace(
	[property: JsonPropertyName("id")] Guid Id,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("client-type")] string ClientType,
	[property: JsonPropertyName("position")] int Position,
	[property: JsonPropertyName("created")] bool Created,
	[property: JsonPropertyName("binding-name")] string BindingName);

/// <summary>The section placement (<c>SysModuleInWorkplace</c> row).</summary>
/// <param name="SectionId">The <c>SysModule</c> id.</param>
/// <param name="Code">The section code.</param>
/// <param name="PlacementId">The <c>SysModuleInWorkplace</c> row id.</param>
/// <param name="Action"><c>moved</c>, <c>created</c> or <c>already-present</c>.</param>
/// <param name="BindingName">Data binding that ships the placement row.</param>
public sealed record WorkplacePlacementSection(
	[property: JsonPropertyName("section-id")] Guid SectionId,
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("placement-id")] Guid PlacementId,
	[property: JsonPropertyName("action")] string Action,
	[property: JsonPropertyName("binding-name")] string BindingName);

/// <summary>One role grant (<c>SysAdminUnitInWorkplace</c> row).</summary>
/// <param name="Name">Role name.</param>
/// <param name="RoleId">The <c>SysAdminUnit</c> id.</param>
/// <param name="GrantId">The <c>SysAdminUnitInWorkplace</c> row id.</param>
/// <param name="Action"><c>granted</c> or <c>already-granted</c>.</param>
/// <param name="BindingName">Data binding that ships the grant row.</param>
public sealed record WorkplacePlacementRole(
	[property: JsonPropertyName("role-id")] Guid RoleId,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("grant-id")] Guid GrantId,
	[property: JsonPropertyName("action")] string Action,
	[property: JsonPropertyName("binding-name")] string BindingName);

/// <summary>The outcome of <see cref="IWorkplacePlacementService.Place"/>.</summary>
/// <param name="PackageName">Package that ships every row.</param>
/// <param name="Workplace">The target workplace.</param>
/// <param name="Section">The section placement.</param>
/// <param name="Roles">The role grants this call asked for.</param>
/// <param name="Warnings">Non-fatal findings the caller should relay.</param>
/// <param name="SectionWorkplaces">Every workplace that holds the section after the call, read back.</param>
/// <param name="NextStep">After a write: how the user gets to see the change; null when nothing was written.</param>
/// <remarks>With mode <c>none</c>, <paramref name="PackageName"/>, <paramref name="Workplace"/> and
/// <paramref name="Section"/> are null and nothing was written.</remarks>
public sealed record WorkplacePlacementResult(
	string PackageName,
	WorkplacePlacementWorkplace Workplace,
	WorkplacePlacementSection Section,
	IReadOnlyList<WorkplacePlacementRole> Roles,
	IReadOnlyList<string> Warnings,
	IReadOnlyList<WorkplaceLocation> SectionWorkplaces,
	string NextStep = null);

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
	/// as a warning.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// A precondition failed. Every input (package, client type, section, roles) is resolved before the first
	/// write, so such a failure changes nothing; a failure during the writes leaves what was already written, and
	/// a re-run converges.
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

	/// <inheritdoc />
	public WorkplacePlacementResult Place(WorkplacePlacementRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		Validate(request);
		_boundIds.Clear();
		List<string> warnings = [];
		string mode = NormalizeMode(request.Mode);
		if (mode == ModeNone) {
			// Leave the section where it is: nothing is resolved for writing and nothing is written.
			SectionModuleRecord unchanged = ResolveSection(request.SectionCode.Trim());
			return new WorkplacePlacementResult(null, null, null, [], warnings, ReadSectionWorkplaces(unchanged.Id));
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
		string workplaceName = request.WorkplaceName.Trim();
		string bindingSuffix = BuildBindingSuffix(workplaceName, section.Code);
		HashSet<Guid> myApplicationsIds = FindMyApplicationsIds(request, clientTypeId);
		HashSet<Guid> holdingSection = repository.FindSectionPlacements(section.Id)
			.Select(row => row.WorkplaceId).ToHashSet();
		CheckModeAgainstExistingWorkplaces(request, package, mode, workplaceName, clientTypeId, holdingSection);

		WorkplaceState workplace = EnsureWorkplace(request, package, workplaceName, clientTypeId, bindingSuffix,
			holdingSection, warnings);
		WorkplacePlacementSection placement = EnsureSectionPlacement(request, package, section, workplace.Record,
			myApplicationsIds, warnings);
		List<WorkplacePlacementRole> grants = roles
			.Select(role => EnsureRoleGrant(request, package, workplace.Record.Id, role, bindingSuffix))
			.ToList();

		VerifyReadBack(section, workplace.Record, placement, grants);
		if (repository.FindRoleGrants(workplace.Record.Id).Count == 0) {
			warnings.Add("The workplace has no role grant, so nobody sees it. Pass roles to grant access.");
		}
		return new WorkplacePlacementResult(
			package.PackageName,
			new WorkplacePlacementWorkplace(workplace.Record.Id, workplace.Record.Name, clientTypeName,
				workplace.Record.Position, workplace.Created, workplace.BindingName),
			placement,
			grants,
			warnings,
			ReadSectionWorkplaces(section.Id),
			ResetNavigationCache(warnings));
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

	private static string NormalizeMode(string mode) =>
		string.IsNullOrWhiteSpace(mode) ? null : mode.Trim().ToLowerInvariant();

	private void CheckModeAgainstExistingWorkplaces(WorkplacePlacementRequest request, PackageTargetResolution package,
		string mode, string workplaceName, Guid clientTypeId, HashSet<Guid> holdingSection) {
		if (mode is null) {
			return;
		}
		IReadOnlyList<WorkplaceRecord> existing = repository.FindWorkplaces(workplaceName, clientTypeId);
		if (mode == ModeExisting && existing.Count == 0) {
			throw new InvalidOperationException(
				$"No workplace named '{workplaceName}' exists for this client type; pass mode=new to create it.");
		}
		if (mode == ModeNew && existing.Count > 0) {
			// A re-run of an earlier mode=new call finds its own workplace: holding the section and already shipped
			// by this package. A same-named workplace made any other way is refused, not adopted.
			bool ownRerun = existing.Any(workplace => holdingSection.Contains(workplace.Id)
				&& FindBindingContaining(request, package, WorkplaceSchema, workplace.Id) is not null);
			if (!ownRerun) {
				throw new InvalidOperationException(
					$"A workplace named '{workplaceName}' already exists ({string.Join(", ", existing.Select(w => w.Id))}); " +
					"a second one with the same name would compete for every binding. Pass mode=existing to use it, " +
					"or choose another name.");
			}
		}
	}

	private IReadOnlyList<WorkplaceLocation> ReadSectionWorkplaces(Guid sectionId) =>
		repository.FindSectionPlacements(sectionId)
			.Select(row => row.WorkplaceId)
			.Distinct()
			.Select(id => new WorkplaceLocation(id, repository.GetWorkplace(id)?.Name))
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
		string workplaceName, Guid clientTypeId, string bindingSuffix, HashSet<Guid> holdingSection,
		List<string> warnings) {
		IReadOnlyList<WorkplaceRecord> existing = repository.FindWorkplaces(workplaceName, clientTypeId);
		bool created = existing.Count == 0;
		Guid workplaceId;
		string bindingName;
		if (created) {
			workplaceId = Guid.NewGuid();
			bindingName = $"{WorkplaceSchema}_{bindingSuffix}";
			EnsureBinding(request, package, WorkplaceSchema, bindingName);
			// The live insert lets the platform assign Position, Type and LoaderId; they are read back below.
			Upsert(request, package, bindingName, new Dictionary<string, object> {
				["Id"] = workplaceId,
				["Name"] = workplaceName,
				["SysApplicationClientType"] = clientTypeId
			});
		}
		else {
			// Prefer the one that already holds the section, so a re-run never spreads it over two of them.
			workplaceId = (existing.FirstOrDefault(record => holdingSection.Contains(record.Id)) ?? existing[0]).Id;
			if (existing.Count > 1) {
				warnings.Add($"{existing.Count} workplaces are named '{workplaceName}' for this client type " +
					$"({string.Join(", ", existing.Select(record => record.Id))}); {workplaceId} was used.");
			}
			bindingName = FindBindingContaining(request, package, WorkplaceSchema, workplaceId);
			if (bindingName is null) {
				bindingName = $"{WorkplaceSchema}_{bindingSuffix}";
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
		Upsert(request, package, bindingName, values);
		WorkplaceRecord readBack = repository.GetWorkplace(workplaceId) ?? record;
		return new WorkplaceState(readBack, created, bindingName);
	}

	private WorkplacePlacementSection EnsureSectionPlacement(WorkplacePlacementRequest request,
		PackageTargetResolution package, SectionModuleRecord section, WorkplaceRecord target,
		HashSet<Guid> myApplicationsIds, List<string> warnings) {
		IReadOnlyList<WorkplaceJunctionRecord> placements = repository.FindSectionPlacements(section.Id);
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
		}

		// A new row cannot be in any binding yet, so only an existing one is looked up.
		string bindingName = action == ActionCreated
			? null
			: FindBindingContaining(request, package, SectionPlacementSchema, placementId);
		if (bindingName is null) {
			bindingName = $"{SectionPlacementSchema}_{BuildBindingSuffix(section.Code, section.Code)}";
			EnsureBinding(request, package, SectionPlacementSchema, bindingName);
			if (action == ActionMoved) {
				warnings.Add($"Placement {placementId} was not shipped by package '{package.PackageName}'. If another " +
					"package (usually the app's own) binds it, that package still ships the old workplace; place the " +
					"section from that package instead.");
			}
		}
		Upsert(request, package, bindingName, new Dictionary<string, object> {
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
		if (bindingName is null) {
			bindingName = $"{RoleGrantSchema}_{bindingSuffix}";
			EnsureBinding(request, package, RoleGrantSchema, bindingName);
		}
		Upsert(request, package, bindingName, new Dictionary<string, object> {
			["Id"] = grantId,
			["SysWorkplace"] = workplaceId,
			["SysAdminUnit"] = role.Id
		});
		return new WorkplacePlacementRole(role.Id, role.Name, grantId,
			existing is null ? ActionGranted : ActionAlreadyGranted, bindingName);
	}

	private void VerifyReadBack(SectionModuleRecord section, WorkplaceRecord target,
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
		dataBindingDbService.CreateBinding(new CreateDataBindingDbOptions {
			Environment = request.EnvironmentName,
			PackageName = package.PackageName,
			SchemaName = schemaName,
			BindingName = bindingName
		});
		bindings[bindingName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	}

	private void Upsert(WorkplacePlacementRequest request, PackageTargetResolution package, string bindingName,
		Dictionary<string, object> values) {
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
	/// Builds the per-table binding-name suffix from <paramref name="name"/>: its ASCII letters and digits, or
	/// <paramref name="fallback"/>'s when it has none.
	/// </summary>
	internal static string BuildBindingSuffix(string name, string fallback) {
		static string Clean(string value) {
			StringBuilder builder = new();
			foreach (char character in value ?? string.Empty) {
				if (char.IsAsciiLetterOrDigit(character)) {
					builder.Append(character);
				}
			}
			return builder.ToString();
		}
		string suffix = Clean(name);
		return suffix.Length > 0 ? suffix : Clean(fallback);
	}

	private sealed record WorkplaceState(WorkplaceRecord Record, bool Created, string BindingName);
}
