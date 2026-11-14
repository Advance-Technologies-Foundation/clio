using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.ProcessModel;
using Clio.Common;
using Clio.Package;
using Clio.UserEnvironment;

namespace Clio.Command;

/// <summary>
/// Runs the dynamic-case tools. <c>create-dynamic-case</c>: a validate pass on the server, the record-page plan and
/// its dry run, the missing package dependencies, the build pass and the progress-bar write, in an order that writes
/// nothing until every check of the call has passed. <c>describe-dynamic-case</c> and <c>modify-dynamic-case</c>:
/// one server call each. Every method sends its server calls through one session, so a tool call logs in once.
/// </summary>
public interface IDynamicCaseService {
	/// <summary>
	/// Creates the case (or plans it on a dry run) and returns the result document. Never throws for an expected
	/// refusal or failure: each is an outcome of the document.
	/// </summary>
	/// <param name="options">The call; the tool has already normalized and checked its arguments.</param>
	DynamicCaseResult Create(CreateDynamicCaseOptions options);

	/// <summary>
	/// Reads an existing case, or every case of an object, and returns the server's answer with a designer link per
	/// case. Changes nothing. Never throws for an expected refusal or a transport fault.
	/// </summary>
	/// <param name="options">The call; the tool has already normalized and checked its arguments.</param>
	DescribeDynamicCaseResult Describe(DescribeDynamicCaseOptions options);

	/// <summary>
	/// Changes an existing case in place (or plans the change on a dry run) and returns the server's answer with the
	/// designer link. Never throws for an expected refusal or failure: each is an outcome of the document, and a
	/// write whose reply could not be read is <see cref="DynamicCaseModifyOutcomes.Unknown"/>.
	/// </summary>
	/// <param name="options">The call; the tool has already normalized and checked its arguments.</param>
	ModifyDynamicCaseResult Modify(ModifyDynamicCaseOptions options);
}

/// <inheritdoc />
public sealed class DynamicCaseService(
	IBuildCaseClient buildCaseClient,
	IDynamicCasePageStep pageStep,
	IPackageDependencyManager packageDependencyManager,
	ISettingsRepository settingsRepository,
	IServiceUrlBuilder serviceUrlBuilder)
	: IDynamicCaseService {

	/// <summary>Nothing was missing.</summary>
	public const string DependenciesNoneMissing = "none-missing";

	/// <summary>This call added the missing dependencies: the case was built, or the build's outcome is unknown.</summary>
	public const string DependenciesAdded = "added";

	/// <summary>A dry run: the missing dependencies would be added.</summary>
	public const string DependenciesPlanned = "planned";

	/// <summary>The build did not create the case, and the dependencies this call added were removed again.</summary>
	public const string DependenciesRemovedAfterFailure = "removed-after-failure";

	/// <summary>The build did not create the case, and removing the dependencies this call added failed.</summary>
	public const string DependenciesLeftAfterFailure = "left-after-failure";

	/// <summary>The build failed with leftovers, so the dependencies this call added were kept for them.</summary>
	public const string DependenciesKeptForLeftovers = "kept-for-leftovers";

	/// <summary>Dependencies are missing and the caller asked not to add them.</summary>
	public const string DependenciesMissing = "missing";

	/// <summary>The bar was written.</summary>
	public const string PageInserted = "inserted";

	/// <summary><c>add-progress-bar</c> was false.</summary>
	public const string PageSkipped = "skipped";

	/// <summary>The case is kept but the bar could not be written.</summary>
	public const string PageFailed = "failed";

	/// <summary>The page step was not reached because an earlier step refused or failed.</summary>
	public const string PageNotReached = "not-reached";

	/// <summary>The obligation kind clio adds for the record page.</summary>
	public const string PageArtifact = "page";

	/// <summary>The package dependencies this call added, when the server asked for a compilation after them.</summary>
	public const string DependenciesArtifact = "package-dependencies";

	/// <summary>The tool a failed page write is retried with.</summary>
	public const string RetryTool = "update-page";

	private const string ServerCreated = "created";
	private const string ServerAlreadyPresent = "already-present";
	private const string DesignerPath = "Nui/ViewModule.aspx?vm=DcmDesigner#case/";

	/// <inheritdoc />
	public DynamicCaseResult Create(CreateDynamicCaseOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		BuildCaseRequest request = BuildRequest(options);
		using IDynamicCaseSession session = buildCaseClient.OpenSession(options.Environment);

		// C2: the server's refusals come first, so a caller without rights never reaches the page reads.
		DynamicCaseReply<BuildCaseResponse> validate = session.Build(
			request with { ValidateOnly = true, AllowMissingDependencies = options.AddPackageDependencies });
		if (validate.Kind != DynamicCaseReplyKind.Envelope) {
			return ValidatePassFault(validate);
		}
		BuildCaseResponse plan = validate.Response;
		List<string> missing = plan.MissingDependencies ?? [];
		if (!plan.Success) {
			return FromServer(options, plan,
				Dependencies(plan, missing, [], DependenciesStatusFor(missing), compilationRequired: false), NotReached(), []);
		}

		// C3: every page check, the dry run of the exact page write included, runs before anything is written.
		DynamicCasePagePlan pagePlan = null;
		if (options.AddProgressBar) {
			pagePlan = pageStep.Plan(options.Environment, options.PackageName, options.EntitySchemaName,
				options.PageSchemaName, missing, options.SaveOnChange);
			if (pagePlan.Status == DynamicCasePageStep.StatusRefused) {
				return PageRefusal(options, plan, missing, pagePlan);
			}
		}

		if (options.DryRun) {
			return Planned(options, plan, missing, pagePlan);
		}

		List<string> added = [];
		bool compilationRequired = false;
		if (missing.Count > 0 && options.AddPackageDependencies) {
			DynamicCaseResult refusal = TryAddDependencies(options, plan, missing, pagePlan, out compilationRequired);
			if (refusal is not null) {
				return refusal;
			}
			added = missing;
		}

		// C6: the server re-runs every check, so a change since the validate pass is refused rather than saved; a
		// dependency add that did not take fails closed because missing dependencies are no longer allowed.
		DynamicCaseReply<BuildCaseResponse> build = session.Build(
			request with { ValidateOnly = false, AllowMissingDependencies = false });
		if (build.Kind != DynamicCaseReplyKind.Envelope) {
			return BuildPassUnknown(options, plan, missing, added, compilationRequired, build);
		}
		BuildCaseResponse built = build.Response;
		if (!IsCaseInPlace(built)) {
			if (added.Count > 0 && built.Leftovers is { Count: > 0 }) {
				// What the server could not undo (the case schema, a settings row) still references the packages this
				// call added, so removing them now would leave those artifacts unresolvable.
				return FromServer(options, built,
					Dependencies(built, missing, added, DependenciesKeptForLeftovers, compilationRequired), NotReached(), [
						$"The package dependencies this call added to '{options.PackageName}' ({string.Join(", ", added)}) "
						+ "were kept: the leftovers the server lists still need them. Remove them with "
						+ "remove-package-dependency only after the leftovers are gone."
					]);
			}
			(string status, List<string> warnings) = UndoDependencies(options.PackageName, added);
			// Dependencies that could not be removed still wait for the compilation their add asked for.
			return FromServer(options, built,
				Dependencies(built, missing, added, added.Count > 0 ? status : DependenciesStatusFor(missing),
					compilationRequired && status == DependenciesLeftAfterFailure),
				NotReached(), warnings);
		}

		DynamicCaseDependencies dependencies = Dependencies(built, missing, added,
			added.Count > 0 ? DependenciesAdded : DependenciesNoneMissing, compilationRequired);
		return WithPage(options, built, dependencies, pagePlan);
	}

	/// <inheritdoc />
	public DescribeDynamicCaseResult Describe(DescribeDynamicCaseOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		DescribeCaseRequest request = new() {
			CaseName = NullIfBlank(options.CaseName),
			CaseUId = NullIfBlank(options.CaseUId),
			EntitySchemaName = NullIfBlank(options.EntitySchemaName)
		};
		DynamicCaseReply<DescribeCaseResponse> reply;
		using (IDynamicCaseSession session = buildCaseClient.OpenSession(options.Environment)) {
			reply = session.Describe(request);
		}
		if (reply.Kind != DynamicCaseReplyKind.Envelope) {
			return DescribeFault(reply);
		}
		DescribeCaseResponse response = reply.Response;
		return new DescribeDynamicCaseResult {
			Success = response.Success,
			ErrorCode = response.ErrorCode,
			ErrorMessage = response.ErrorMessage,
			Cases = [.. (response.Cases ?? []).Select(info => ExistingCaseInfo(options.Environment, info))],
			Settings = response.Settings ?? [],
			Notes = response.Notes ?? [],
			Warnings = response.Warnings ?? []
		};
	}

	/// <inheritdoc />
	public ModifyDynamicCaseResult Modify(ModifyDynamicCaseOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		ModifyCaseRequest request = new() {
			CaseName = NullIfBlank(options.CaseName),
			CaseUId = NullIfBlank(options.CaseUId),
			EntitySchemaName = NullIfBlank(options.EntitySchemaName),
			Stages = options.Stages?.Select(StageEdit).ToList(),
			Caption = NullIfBlank(options.Caption),
			Enabled = options.Enabled,
			ConfirmRunningInstances = options.ConfirmRunningInstances,
			ValidateOnly = options.DryRun
		};
		DynamicCaseReply<ModifyCaseResponse> reply;
		using (IDynamicCaseSession session = buildCaseClient.OpenSession(options.Environment)) {
			reply = session.Modify(request);
		}
		if (reply.Kind != DynamicCaseReplyKind.Envelope) {
			return ModifyFault(options.DryRun, reply);
		}
		ModifyCaseResponse response = reply.Response;
		// An answer without an outcome is read as what its success flag says, so a refusal never passes as a write.
		string outcome = response.Outcome ?? (response.Success ? null : DynamicCaseModifyOutcomes.Refused);
		return new ModifyDynamicCaseResult {
			Success = response.Success && DynamicCaseModifyOutcomes.IsSuccess(outcome),
			Outcome = outcome,
			ErrorCode = response.ErrorCode,
			ErrorMessage = response.ErrorMessage,
			Case = response.Case is null ? null : ExistingCaseInfo(options.Environment, response.Case),
			Diff = response.Diff,
			RunningInstances = response.RunningInstances,
			Obligations = response.Obligations,
			Leftovers = response.Leftovers ?? [],
			Notes = response.Notes ?? [],
			Warnings = response.Warnings ?? []
		};
	}

	private static BuildCaseRequest BuildRequest(CreateDynamicCaseOptions options) =>
		new() {
			EntitySchemaName = options.EntitySchemaName,
			StageColumnName = options.StageColumnName,
			PackageName = options.PackageName,
			Name = NullIfBlank(options.Name),
			Caption = NullIfBlank(options.Caption),
			OrderColumn = NullIfBlank(options.OrderColumn),
			Stages = options.Stages is { Count: > 0 }
				? options.Stages.Select(stage => new BuildCaseStageRequest(stage.Value) {
					Group = stage.Group, Successful = stage.Successful
				}).ToList()
				: null
		};

	private static CaseStageEditRequest StageEdit(DynamicCaseStageItem stage) =>
		new() {
			Value = stage.Value,
			Caption = stage.Caption,
			Color = stage.Color,
			GroupSent = stage.GroupSent,
			Group = stage.Group,
			Successful = stage.Successful
		};

	private static string NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

	private static bool IsCaseInPlace(BuildCaseResponse response) =>
		response.Success && response.Outcome is ServerCreated or ServerAlreadyPresent;

	private DynamicCaseResult TryAddDependencies(CreateDynamicCaseOptions options, BuildCaseResponse plan,
		List<string> missing, DynamicCasePagePlan pagePlan, out bool compilationRequired) {
		compilationRequired = false;
		try {
			compilationRequired = packageDependencyManager.AddDependenciesReportingCompilation(options.PackageName,
				missing.Select(name => new PackageDependencySpec(name))).CompilationRequired;
			return null;
		} catch (Exception exception) {
			// The manager answers what it or the storage refused with InvalidOperationException, and
			// SavePackageProperties applies the whole list in one save, so a refusal added nothing; the storage's
			// validation errors are what a dependency cycle comes back as. Anything else is a transport failure that
			// may have come after the save took, so the dependencies are removed again rather than reported as
			// never added.
			bool refused = exception is InvalidOperationException;
			bool validation = refused && exception.Message.Contains(PackageDependencyManager.ValidationErrorsMarker,
				StringComparison.Ordinal);
			(string status, List<string> warnings) = refused
				? (DependenciesMissing, [])
				: RemoveAfterUncertainAdd(options.PackageName, missing, exception);
			return new DynamicCaseResult {
				Success = false,
				Outcome = DynamicCaseOutcomes.Refused,
				ErrorCode = validation ? DynamicCaseErrorCodes.DependencyCycle : DynamicCaseErrorCodes.DependencyAddFailed,
				ErrorMessage = $"Adding the missing package dependencies ({string.Join(", ", missing)}) to "
					+ $"'{options.PackageName}' failed, so the case was not built: {exception.Message}"
					+ (validation
						? " The storage refused the dependency, which usually means one of those packages already "
						+ $"depends on '{options.PackageName}'. Choose a package that sits above them."
						: string.Empty),
				Case = CaseInfo(options, plan.Case, designerUrl: null),
				Settings = plan.Settings,
				Dependencies = Dependencies(plan, missing, status == DependenciesLeftAfterFailure ? missing : [], status,
					compilationRequired: false),
				Page = PageInfo(pagePlan, PageNotReached),
				Notes = [],
				Warnings = [.. plan.Warnings ?? [], .. warnings]
			};
		}
	}

	private (string Status, List<string> Warnings) RemoveAfterUncertainAdd(string packageName, List<string> missing,
		Exception failure) {
		try {
			// A no-op for each one the save did not apply: the removal saves only when something is declared.
			packageDependencyManager.RemoveDependencies(packageName, missing);
			return (DependenciesMissing, []);
		} catch (Exception exception) {
			return (DependenciesLeftAfterFailure, [
				$"Adding the package dependencies to '{packageName}' failed without the storage's answer "
				+ $"({failure.Message}), so the save may have applied them, and removing them again failed too: "
				+ $"{exception.Message} Read the package's dependencies and remove any of {string.Join(", ", missing)} "
				+ "that are declared with remove-package-dependency."
			]);
		}
	}

	private (string Status, List<string> Warnings) UndoDependencies(string packageName, List<string> added) {
		if (added.Count == 0) {
			return (null, []);
		}
		try {
			packageDependencyManager.RemoveDependencies(packageName, added);
			return (DependenciesRemovedAfterFailure, []);
		} catch (Exception exception) {
			return (DependenciesLeftAfterFailure, [
				$"The case was not built, but the package dependencies this call added to '{packageName}' "
				+ $"({string.Join(", ", added)}) could not be removed again: {exception.Message} They are still "
				+ "declared; remove them with remove-package-dependency if they are not wanted."
			]);
		}
	}

	private DynamicCaseResult WithPage(CreateDynamicCaseOptions options, BuildCaseResponse built,
		DynamicCaseDependencies dependencies, DynamicCasePagePlan pagePlan) {
		List<string> notes = [.. built.Notes ?? []];
		List<string> warnings = [.. built.Warnings ?? []];
		AddCompilationNotice(options, dependencies, notes, warnings,
			" The case and the progress bar themselves need no compile.");
		string designerUrl = DesignerUrl(options.Environment, built.Case?.SchemaUId);
		bool alreadyPresent = built.Outcome == ServerAlreadyPresent;
		if (!options.AddProgressBar) {
			AddSkippedBarModeNote(options, notes);
			return Result(alreadyPresent ? DynamicCaseOutcomes.AlreadyPresent : DynamicCaseOutcomes.CreatedPageSkipped,
				options, built, designerUrl, dependencies, new DynamicCasePageInfo { Status = PageSkipped }, notes,
				warnings, pageWritten: false);
		}
		notes.AddRange(pagePlan.Notes);
		warnings.AddRange(pagePlan.Warnings);
		if (pagePlan.Status == DynamicCasePageStep.StatusAlreadyPresent) {
			AddPackageNote(options, pagePlan, notes);
			return Result(alreadyPresent ? DynamicCaseOutcomes.AlreadyPresent : DynamicCaseOutcomes.Created, options,
				built, designerUrl, dependencies, PageInfo(pagePlan, DynamicCasePageStep.StatusAlreadyPresent), notes,
				warnings, pageWritten: false);
		}
		DynamicCasePageWrite write = pageStep.Write(options.Environment, pagePlan);
		warnings.AddRange(write.Warnings ?? []);
		if (write.Inserted) {
			AddPackageNote(options, pagePlan, notes);
			return Result(alreadyPresent ? DynamicCaseOutcomes.AlreadyPresent : DynamicCaseOutcomes.Created, options,
				built, designerUrl, dependencies, PageInfo(pagePlan, PageInserted), notes, warnings, pageWritten: true);
		}
		DynamicCasePageInfo failedPage = new() {
			Status = PageFailed,
			PageSchemaName = pagePlan.PageSchemaName,
			ResolvedBy = pagePlan.ResolvedBy,
			PackageName = pagePlan.DesignPackageName,
			Template = pagePlan.Template,
			Fragment = pagePlan.Fragment,
			SaveOnChange = pagePlan.SaveOnChange,
			Error = write.Error,
			Retry = new DynamicCasePageRetry { Tool = RetryTool, Args = RetryArgs(options, pagePlan) }
		};
		return Result(DynamicCaseOutcomes.CreatedPageFailed, options, built, designerUrl, dependencies, failedPage,
			notes, warnings, pageWritten: false, DynamicCaseErrorCodes.PageAppendRejected,
			$"The progress bar could not be added to '{pagePlan.PageSchemaName}': {write.Error}");
	}

	private static Dictionary<string, string> RetryArgs(CreateDynamicCaseOptions options, DynamicCasePagePlan pagePlan) {
		string mode = pagePlan.WriteMode ?? DynamicCasePageStep.AppendMode;
		Dictionary<string, string> args = new() {
			["environment-name"] = options.Environment,
			["schema-name"] = pagePlan.PageSchemaName,
			["mode"] = mode,
			["body"] = pagePlan.WriteBody ?? pagePlan.Fragment
		};
		if (mode == DynamicCasePageStep.ReplaceMode) {
			// A replace body was built from one version of the page; pinned to it, a retry after a designer save is
			// refused as a conflict instead of overwriting that save.
			args["checksum"] = pagePlan.ExpectedChecksum;
		}
		return args;
	}

	/// <summary>
	/// Tells a caller that the dependencies this call left declared need a configuration compile. Every path that
	/// leaves them declared calls it together with <see cref="WithPageArtifact"/>, so the obligations and the words
	/// agree.
	/// </summary>
	private static void AddCompilationNotice(CreateDynamicCaseOptions options, DynamicCaseDependencies dependencies,
		List<string> notes, List<string> warnings, string tail = "") {
		if (dependencies?.CompilationRequired != true) {
			return;
		}
		// The server's "no compile" note speaks for the case alone; the dependency change it does not know about
		// asked for a compilation, and the wording carries the marker that keeps the tool's own note off.
		notes.RemoveAll(note => note.StartsWith("No compile or restart is needed", StringComparison.Ordinal));
		warnings.Add($"The package dependencies this call added to '{options.PackageName}' cannot take effect "
			+ "until the configuration is compiled: the server asked for a configuration compilation after the "
			+ "change. Run compile-creatio without package-name (a configuration compile)." + tail);
	}

	// The mode lives only on the bar, so with no bar to add a mode the caller named would otherwise be dropped unseen.
	private static void AddSkippedBarModeNote(CreateDynamicCaseOptions options, List<string> notes) {
		if (!options.AddProgressBar && options.SaveOnChange.HasValue) {
			notes.Add("save-on-change was not applied: add-progress-bar is false, so this call adds no progress bar.");
		}
	}

	private static void AddPackageNote(CreateDynamicCaseOptions options, DynamicCasePagePlan pagePlan,
		List<string> notes) {
		if (pagePlan.Status == DynamicCasePageStep.StatusPlanned
			&& !string.IsNullOrWhiteSpace(pagePlan.DesignPackageName)
			&& !string.Equals(pagePlan.DesignPackageName, options.PackageName, StringComparison.OrdinalIgnoreCase)) {
			notes.Add($"The progress bar is written to '{pagePlan.PageSchemaName}' in package "
				+ $"'{pagePlan.DesignPackageName}', not '{options.PackageName}'. Deploy '{pagePlan.DesignPackageName}' "
				+ $"together with '{options.PackageName}'.");
		}
	}

	private static DynamicCaseResult Result(string outcome, CreateDynamicCaseOptions options, BuildCaseResponse built,
		string designerUrl, DynamicCaseDependencies dependencies, DynamicCasePageInfo page, List<string> notes,
		List<string> warnings, bool pageWritten, string errorCode = null, string errorMessage = null) =>
		new() {
			Success = DynamicCaseOutcomes.IsSuccess(outcome),
			Outcome = outcome,
			ErrorCode = errorCode,
			ErrorMessage = errorMessage,
			Case = CaseInfo(options, built.Case, designerUrl),
			Settings = built.Settings,
			Dependencies = dependencies,
			Page = page,
			Obligations = WithPageArtifact(built.Obligations, pageWritten, dependencies?.CompilationRequired == true),
			Leftovers = built.Leftovers ?? [],
			Notes = notes,
			Warnings = warnings
		};

	private static DynamicCaseResult Planned(CreateDynamicCaseOptions options, BuildCaseResponse plan,
		List<string> missing, DynamicCasePagePlan pagePlan) {
		List<string> notes = [.. plan.Notes ?? []];
		List<string> warnings = [.. plan.Warnings ?? []];
		if (pagePlan is not null) {
			notes.AddRange(pagePlan.Notes);
			warnings.AddRange(pagePlan.Warnings);
			AddPackageNote(options, pagePlan, notes);
		}
		AddSkippedBarModeNote(options, notes);
		string dependencyStatus = missing.Count == 0
			? DependenciesNoneMissing
			: options.AddPackageDependencies ? DependenciesPlanned : DependenciesMissing;
		return new DynamicCaseResult {
			Success = true,
			Outcome = DynamicCaseOutcomes.Planned,
			Case = CaseInfo(options, plan.Case, designerUrl: null),
			Settings = plan.Settings,
			Dependencies = Dependencies(plan, missing, [], dependencyStatus, compilationRequired: false),
			Page = pagePlan is null
				? new DynamicCasePageInfo { Status = PageSkipped }
				: PageInfo(pagePlan, pagePlan.Status),
			Obligations = WithPageArtifact(plan.Obligations, pagePlan?.Status == DynamicCasePageStep.StatusPlanned),
			Leftovers = [],
			Notes = notes,
			Warnings = warnings
		};
	}

	private static DynamicCaseResult PageRefusal(CreateDynamicCaseOptions options, BuildCaseResponse plan,
		List<string> missing, DynamicCasePagePlan pagePlan) =>
		new() {
			Success = false,
			Outcome = DynamicCaseOutcomes.Refused,
			ErrorCode = pagePlan.ErrorCode,
			ErrorMessage = pagePlan.ErrorMessage,
			Case = CaseInfo(options, plan.Case, designerUrl: null),
			Settings = plan.Settings,
			Dependencies = Dependencies(plan, missing, [], missing.Count == 0 ? DependenciesNoneMissing : DependenciesMissing,
				compilationRequired: false),
			Page = PageInfo(pagePlan, DynamicCasePageStep.StatusRefused),
			Leftovers = [],
			Notes = [],
			Warnings = plan.Warnings ?? []
		};

	private DynamicCaseResult FromServer(CreateDynamicCaseOptions options, BuildCaseResponse response,
		DynamicCaseDependencies dependencies, DynamicCasePageInfo page, List<string> extraWarnings) {
		string outcome = response.Outcome is DynamicCaseOutcomes.FailedRolledBack or DynamicCaseOutcomes.FailedLeftover
			? response.Outcome
			: DynamicCaseOutcomes.Refused;
		List<string> notes = [.. response.Notes ?? []];
		List<string> warnings = [.. response.Warnings ?? [], .. extraWarnings];
		AddCompilationNotice(options, dependencies, notes, warnings);
		return new DynamicCaseResult {
			Success = false,
			Outcome = outcome,
			ErrorCode = response.ErrorCode,
			ErrorMessage = response.ErrorMessage,
			Case = CaseInfo(options, response.Case, designerUrl: null),
			Settings = response.Settings,
			Dependencies = dependencies,
			Page = page,
			Obligations = WithPageArtifact(response.Obligations, page: false, dependencies?.CompilationRequired == true),
			Leftovers = response.Leftovers ?? [],
			Notes = notes,
			Warnings = warnings
		};
	}

	private static DynamicCaseResult ValidatePassFault(DynamicCaseReply<BuildCaseResponse> reply) {
		// WCF answers a request it cannot read before any operation runs, so the operation exists: a reinstall would
		// restart the site and change nothing.
		bool unreadable = reply.Kind == DynamicCaseReplyKind.NotAnEnvelope
			&& ProcessDesignServerFault.IsUnreadableRequest(reply.Detail);
		// Only a 404 proves the operation is missing. A body that is not the envelope can be a fault from inside
		// BuildCase, an HTML error page or a login page, which a reinstall restarting the site would not fix.
		bool missingOperation = !unreadable && reply.Kind == DynamicCaseReplyKind.RouteMissing;
		string message = unreadable
			? "BuildCase could not read the request, so nothing was written; check the arguments against "
				+ $"get-tool-contract create-dynamic-case. The server said: {reply.Detail}"
			: missingOperation
			? "The environment's CrtProcessBuilder answered BuildCase with a 404, so the installed package does not "
				+ "carry the dynamic-case operation although it passed the "
				+ "version check. Nothing was written. Ask the user, then run install-process-builder to bring the "
				+ $"package up to the version this clio ships. The server said: {reply.Detail}"
			: $"BuildCase could not be called, so nothing was written: {reply.Detail}";
		return new DynamicCaseResult {
			Success = false,
			Outcome = DynamicCaseOutcomes.Refused,
			ErrorCode = missingOperation
				? DynamicCaseErrorCodes.ProcessBuilderWithoutBuildCase
				: DynamicCaseErrorCodes.RequestFailed,
			ErrorMessage = message,
			Page = NotReached(),
			Leftovers = [],
			Notes = [],
			Warnings = []
		};
	}

	private static DynamicCaseResult BuildPassUnknown(CreateDynamicCaseOptions options, BuildCaseResponse plan,
		List<string> missing, List<string> added, bool compilationRequired, DynamicCaseReply<BuildCaseResponse> reply) {
		// Left in place: if the case WAS written it needs them, and a dry-run re-run tells which.
		DynamicCaseDependencies dependencies = Dependencies(plan, missing, added,
			added.Count > 0 ? DependenciesAdded : DependenciesNoneMissing, compilationRequired);
		List<string> notes = [];
		List<string> warnings = added.Count > 0
			? [$"The package dependencies this call added to '{options.PackageName}' ({string.Join(", ", added)}) "
				+ "were left in place because the build outcome is unknown."]
			: [];
		AddCompilationNotice(options, dependencies, notes, warnings);
		return new DynamicCaseResult {
			Success = false,
			Outcome = DynamicCaseOutcomes.Unknown,
			ErrorCode = DynamicCaseErrorCodes.RequestFailed,
			ErrorMessage = "BuildCase returned a response clio could not read, so whether the case was created is "
				+ "UNKNOWN. Re-run this call with dry-run=true before deciding what to do: an equivalent case that "
				+ "exists answers already-present, and nothing else is written by a dry run. "
				+ $"The server said: {reply.Detail}",
			Case = CaseInfo(options, plan.Case, designerUrl: null),
			Settings = plan.Settings,
			Dependencies = dependencies,
			Page = NotReached(),
			Obligations = WithPageArtifact(plan.Obligations, page: false, compilationRequired),
			Leftovers = [],
			Notes = notes,
			Warnings = warnings
		};
	}

	private static DescribeDynamicCaseResult DescribeFault(DynamicCaseReply<DescribeCaseResponse> reply) {
		(string errorCode, string message) = CaseChangeFault("DescribeCase", "describe-dynamic-case", reply.Kind,
			reply.Detail, "Nothing was changed.");
		return new DescribeDynamicCaseResult {
			Success = false,
			ErrorCode = errorCode,
			ErrorMessage = message ?? "DescribeCase could not be called or its answer could not be read; nothing was "
				+ $"changed. The server said: {reply.Detail}",
			Cases = [],
			Settings = [],
			Notes = [],
			Warnings = []
		};
	}

	private static ModifyDynamicCaseResult ModifyFault(bool dryRun, DynamicCaseReply<ModifyCaseResponse> reply) {
		(string errorCode, string message) = CaseChangeFault("ModifyCase", "modify-dynamic-case", reply.Kind,
			reply.Detail, "Nothing was written.");
		if (message is null && !dryRun) {
			// The write was sent and may have been saved before the answer was lost; only a dry run, which writes
			// nothing, can tell what is still to change.
			return new ModifyDynamicCaseResult {
				Success = false,
				Outcome = DynamicCaseModifyOutcomes.Unknown,
				ErrorCode = DynamicCaseErrorCodes.RequestFailed,
				ErrorMessage = "ModifyCase returned a response clio could not read, so whether the case was changed is "
					+ "UNKNOWN. Re-run this call with dry-run=true before deciding what to do: its diff shows what is "
					+ "still to change, and a dry run writes nothing. "
					+ $"The server said: {reply.Detail}",
				Leftovers = [],
				Notes = [],
				Warnings = []
			};
		}
		return new ModifyDynamicCaseResult {
			Success = false,
			Outcome = DynamicCaseModifyOutcomes.Refused,
			ErrorCode = errorCode,
			ErrorMessage = message ?? "ModifyCase could not be called or its answer could not be read; a dry run writes "
				+ $"nothing, so nothing was written. The server said: {reply.Detail}",
			Leftovers = [],
			Notes = [],
			Warnings = []
		};
	}

	/// <summary>
	/// The code and the message of a <c>DescribeCase</c> or <c>ModifyCase</c> call that got no envelope back. The
	/// message is <see langword="null"/> when the fault does not prove that nothing ran: the caller words that case,
	/// because only it knows whether the call could have written.
	/// </summary>
	private static (string ErrorCode, string Message) CaseChangeFault(string operation, string toolName,
		DynamicCaseReplyKind kind, string detail, string nothingDone) {
		// WCF answers a request it cannot read before any operation runs, so the operation exists.
		if (kind == DynamicCaseReplyKind.NotAnEnvelope && ProcessDesignServerFault.IsUnreadableRequest(detail)) {
			return (DynamicCaseErrorCodes.RequestFailed,
				$"{operation} could not read the request. {nothingDone} Check the arguments against get-tool-contract "
				+ $"{toolName}. The server said: {detail}");
		}
		// Only a 404 proves the operation is missing; any other body can be a fault from inside the operation, an
		// HTML error page or a login page, which a reinstall restarting the site would not fix.
		if (kind == DynamicCaseReplyKind.RouteMissing) {
			return (DynamicCaseErrorCodes.ProcessBuilderWithoutCaseChange,
				$"The environment's CrtProcessBuilder answered {operation} with a 404, so the installed package does not "
				+ "carry the operations that read and change an existing dynamic case although it passed the version "
				+ $"check. {nothingDone} Ask the user, then run install-process-builder to bring the package up to the "
				+ $"version this clio ships. The server said: {detail}");
		}
		return (DynamicCaseErrorCodes.RequestFailed, null);
	}

	private static string DependenciesStatusFor(List<string> missing) =>
		missing.Count == 0 ? DependenciesNoneMissing : DependenciesMissing;

	// compilationRequired has no default on purpose: every path that leaves added dependencies declared must say
	// whether they wait for a compile, or the obligations would drop it silently.
	private static DynamicCaseDependencies Dependencies(BuildCaseResponse response, List<string> missing,
		List<string> added, string status, bool compilationRequired) =>
		new() {
			Status = status,
			Required = response.RequiredPackages ?? [],
			Missing = missing,
			Added = added,
			CompilationRequired = compilationRequired
		};

	private static DynamicCaseInfo CaseInfo(CreateDynamicCaseOptions options, BuildCaseCaseInfo info,
		string designerUrl) =>
		info is null
			? null
			: ToCaseInfo(info, designerUrl, info.PackageName ?? options.PackageName,
				info.EntitySchemaName ?? options.EntitySchemaName, info.StageColumnName ?? options.StageColumnName);

	// A described or changed case exists in every outcome that answers one, so it always gets its designer link.
	private DynamicCaseInfo ExistingCaseInfo(string environmentName, BuildCaseCaseInfo info) =>
		ToCaseInfo(info, DesignerUrl(environmentName, info.SchemaUId), info.PackageName, info.EntitySchemaName,
			info.StageColumnName);

	private static DynamicCaseInfo ToCaseInfo(BuildCaseCaseInfo info, string designerUrl, string packageName,
		string entitySchemaName, string stageColumnName) =>
		new() {
			Status = info.Status,
			SchemaName = info.SchemaName,
			SchemaUId = info.SchemaUId,
			Caption = info.Caption,
			PackageName = packageName,
			EntitySchemaName = entitySchemaName,
			StageColumnName = stageColumnName,
			StageLookupSchemaName = info.StageLookupSchemaName,
			Enabled = info.Enabled,
			ActiveVersion = info.ActiveVersion,
			OrderSource = info.OrderSource,
			Stages = info.Stages ?? [],
			Connections = new DynamicCaseConnections { Kind = "all-to-all", Count = info.ConnectionCount },
			FinalStages = info.FinalStages,
			RunningInstances = info.RunningInstances,
			PackageReadOnly = info.PackageReadOnly,
			DesignerUrl = designerUrl
		};

	private static DynamicCasePageInfo PageInfo(DynamicCasePagePlan plan, string status) =>
		plan is null
			? NotReached()
			: new DynamicCasePageInfo {
				Status = status,
				PageSchemaName = plan.PageSchemaName,
				ResolvedBy = plan.ResolvedBy,
				PackageName = plan.DesignPackageName,
				Template = plan.Template,
				Fragment = plan.Fragment,
				// Set by the page step only with a fragment: absent for an already-present bar, which keeps its own mode.
				SaveOnChange = plan.SaveOnChange,
				Error = plan.ErrorMessage,
				Candidates = plan.Candidates is { Count: > 0 } ? [.. plan.Candidates] : null
			};

	private static DynamicCasePageInfo NotReached() => new() { Status = PageNotReached };

	private static DynamicCaseObligations WithPageArtifact(DynamicCaseObligations obligations, bool page,
		bool dependenciesNeedCompile = false) {
		if (!page && !dependenciesNeedCompile) {
			return obligations;
		}
		List<DynamicCaseArtifactObligation> artifacts = [.. obligations?.Artifacts ?? []];
		if (page) {
			// A Freedom UI page is a client schema: it is served as saved, with no compile and no restart.
			artifacts.Add(new DynamicCaseArtifactObligation { Kind = PageArtifact, Compile = false, Restart = false });
		}
		if (dependenciesNeedCompile) {
			artifacts.Add(new DynamicCaseArtifactObligation {
				Kind = DependenciesArtifact, Compile = true, Restart = false
			});
		}
		return new DynamicCaseObligations {
			CompileRequired = (obligations?.CompileRequired ?? false) || dependenciesNeedCompile,
			RestartRequired = obligations?.RestartRequired ?? false,
			ReloadOpenPages = page || (obligations?.ReloadOpenPages ?? false),
			Artifacts = artifacts
		};
	}

	private string DesignerUrl(string environmentName, string schemaUId) {
		if (string.IsNullOrWhiteSpace(schemaUId)) {
			return null;
		}
		EnvironmentSettings environment = settingsRepository.FindEnvironment(environmentName);
		return environment is null ? null : serviceUrlBuilder.Build(DesignerPath + schemaUId, environment);
	}
}
