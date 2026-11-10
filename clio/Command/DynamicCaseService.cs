using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Package;
using Clio.UserEnvironment;

namespace Clio.Command;

/// <summary>
/// Runs <c>create-dynamic-case</c>: a validate pass on the server, the record-page plan and its append dry run, the
/// missing package dependencies, the build pass and the progress-bar append, in an order that writes nothing until
/// every check of the call has passed.
/// </summary>
public interface IDynamicCaseService {
	/// <summary>
	/// Creates the case (or plans it on a dry run) and returns the result document. Never throws for an expected
	/// refusal or failure: each is an outcome of the document.
	/// </summary>
	/// <param name="options">The call; the tool has already normalized and checked its arguments.</param>
	DynamicCaseResult Create(CreateDynamicCaseOptions options);
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

	/// <summary>This call added the missing dependencies and the case was built.</summary>
	public const string DependenciesAdded = "added";

	/// <summary>A dry run: the missing dependencies would be added.</summary>
	public const string DependenciesPlanned = "planned";

	/// <summary>The build did not create the case, and the dependencies this call added were removed again.</summary>
	public const string DependenciesRemovedAfterFailure = "removed-after-failure";

	/// <summary>The build did not create the case, and removing the dependencies this call added failed.</summary>
	public const string DependenciesLeftAfterFailure = "left-after-failure";

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

	/// <summary>The tool a failed page write is retried with.</summary>
	public const string RetryTool = "update-page";

	private const string ServerCreated = "created";
	private const string ServerAlreadyPresent = "already-present";
	private const string DesignerPath = "Nui/ViewModule.aspx?vm=DcmDesigner#case/";

	/// <inheritdoc />
	public DynamicCaseResult Create(CreateDynamicCaseOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		BuildCaseRequest request = BuildRequest(options);

		// C2: the server's refusals come first, so a caller without rights never reaches the page reads.
		BuildCaseReply validate = buildCaseClient.Send(options.Environment,
			request with { ValidateOnly = true, AllowMissingDependencies = options.AddPackageDependencies });
		if (validate.Kind != BuildCaseReplyKind.Envelope) {
			return ValidatePassFault(validate);
		}
		BuildCaseResponse plan = validate.Response;
		List<string> missing = plan.MissingDependencies ?? [];
		if (!plan.Success) {
			return FromServer(options, plan, Dependencies(plan, missing, [], DependenciesStatusFor(missing)),
				NotReached(), []);
		}

		// C3: every page check, the append dry run of the exact fragment included, runs before anything is written.
		DynamicCasePagePlan pagePlan = null;
		if (options.AddProgressBar) {
			pagePlan = pageStep.Plan(options.Environment, options.PackageName, options.EntitySchemaName,
				options.PageSchemaName);
			if (pagePlan.Status == DynamicCasePageStep.StatusRefused) {
				return PageRefusal(options, plan, missing, pagePlan);
			}
		}

		if (options.DryRun) {
			return Planned(options, plan, missing, pagePlan);
		}

		List<string> added = [];
		if (missing.Count > 0 && options.AddPackageDependencies) {
			DynamicCaseResult refusal = TryAddDependencies(options, plan, missing, pagePlan);
			if (refusal is not null) {
				return refusal;
			}
			added = missing;
		}

		// C6: the server re-runs every check, so a change since the validate pass is refused rather than saved; a
		// dependency add that did not take fails closed because missing dependencies are no longer allowed.
		BuildCaseReply build = buildCaseClient.Send(options.Environment,
			request with { ValidateOnly = false, AllowMissingDependencies = false });
		if (build.Kind != BuildCaseReplyKind.Envelope) {
			return BuildPassUnknown(options, plan, missing, added, build);
		}
		BuildCaseResponse built = build.Response;
		if (!IsCaseInPlace(built)) {
			(string status, List<string> warnings) = UndoDependencies(options.PackageName, added);
			return FromServer(options, built,
				Dependencies(built, missing, added, added.Count > 0 ? status : DependenciesStatusFor(missing)),
				NotReached(), warnings);
		}

		DynamicCaseDependencies dependencies = Dependencies(built, missing, added,
			added.Count > 0 ? DependenciesAdded : DependenciesNoneMissing);
		return WithPage(options, built, dependencies, pagePlan);
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
				? options.Stages.Select(value => new BuildCaseStageRequest(value)).ToList()
				: null
		};

	private static string NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

	private static bool IsCaseInPlace(BuildCaseResponse response) =>
		response.Success && response.Outcome is ServerCreated or ServerAlreadyPresent;

	private DynamicCaseResult TryAddDependencies(CreateDynamicCaseOptions options, BuildCaseResponse plan,
		List<string> missing, DynamicCasePagePlan pagePlan) {
		try {
			packageDependencyManager.AddDependencies(options.PackageName,
				missing.Select(name => new PackageDependencySpec(name)));
			return null;
		} catch (Exception exception) {
			// SavePackageProperties applies the whole list in one save, so a refused save added nothing; the
			// storage's validation errors are what a dependency cycle comes back as.
			bool validation = exception.Message.Contains("Validation errors:", StringComparison.Ordinal);
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
				Dependencies = Dependencies(plan, missing, [], DependenciesMissing),
				Page = PageInfo(pagePlan, PageNotReached),
				Notes = [],
				Warnings = plan.Warnings ?? []
			};
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
		string designerUrl = DesignerUrl(options.Environment, built.Case?.SchemaUId);
		bool alreadyPresent = built.Outcome == ServerAlreadyPresent;
		if (!options.AddProgressBar) {
			return Result(alreadyPresent ? DynamicCaseOutcomes.AlreadyPresent : DynamicCaseOutcomes.CreatedPageSkipped,
				options, built, designerUrl, dependencies, new DynamicCasePageInfo { Status = PageSkipped }, notes,
				warnings, pageWritten: false);
		}
		notes.AddRange(pagePlan.Notes);
		warnings.AddRange(pagePlan.Warnings);
		AddPackageNote(options, pagePlan, notes);
		if (pagePlan.Status == DynamicCasePageStep.StatusAlreadyPresent) {
			return Result(alreadyPresent ? DynamicCaseOutcomes.AlreadyPresent : DynamicCaseOutcomes.Created, options,
				built, designerUrl, dependencies, PageInfo(pagePlan, DynamicCasePageStep.StatusAlreadyPresent), notes,
				warnings, pageWritten: false);
		}
		DynamicCasePageWrite write = pageStep.Write(options.Environment, pagePlan);
		warnings.AddRange(write.Warnings ?? []);
		if (write.Inserted) {
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
			Error = write.Error,
			Retry = new DynamicCasePageRetry {
				Tool = RetryTool,
				Args = new Dictionary<string, string> {
					["environment-name"] = options.Environment,
					["schema-name"] = pagePlan.PageSchemaName,
					["mode"] = "append",
					["body"] = pagePlan.Fragment
				}
			}
		};
		return Result(DynamicCaseOutcomes.CreatedPageFailed, options, built, designerUrl, dependencies, failedPage,
			notes, warnings, pageWritten: false, DynamicCaseErrorCodes.PageAppendRejected,
			$"The progress bar could not be added to '{pagePlan.PageSchemaName}': {write.Error}");
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
			Obligations = WithPageArtifact(built.Obligations, pageWritten),
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
		string dependencyStatus = missing.Count == 0
			? DependenciesNoneMissing
			: options.AddPackageDependencies ? DependenciesPlanned : DependenciesMissing;
		return new DynamicCaseResult {
			Success = true,
			Outcome = DynamicCaseOutcomes.Planned,
			Case = CaseInfo(options, plan.Case, designerUrl: null),
			Settings = plan.Settings,
			Dependencies = Dependencies(plan, missing, [], dependencyStatus),
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
			Dependencies = Dependencies(plan, missing, [], missing.Count == 0 ? DependenciesNoneMissing : DependenciesMissing),
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
		return new DynamicCaseResult {
			Success = false,
			Outcome = outcome,
			ErrorCode = response.ErrorCode,
			ErrorMessage = response.ErrorMessage,
			Case = CaseInfo(options, response.Case, designerUrl: null),
			Settings = response.Settings,
			Dependencies = dependencies,
			Page = page,
			Obligations = response.Obligations,
			Leftovers = response.Leftovers ?? [],
			Notes = response.Notes ?? [],
			Warnings = [.. response.Warnings ?? [], .. extraWarnings]
		};
	}

	private static DynamicCaseResult ValidatePassFault(BuildCaseReply reply) {
		bool missingOperation = reply.Kind is BuildCaseReplyKind.RouteMissing or BuildCaseReplyKind.NotAnEnvelope;
		string message = missingOperation
			? "The environment's CrtProcessBuilder answered BuildCase with "
				+ (reply.Kind == BuildCaseReplyKind.RouteMissing ? "a 404" : "a body that is not its result")
				+ ", so the installed package does not carry the dynamic-case operation although it passed the "
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
		List<string> missing, List<string> added, BuildCaseReply reply) =>
		new() {
			Success = false,
			Outcome = DynamicCaseOutcomes.Unknown,
			ErrorCode = DynamicCaseErrorCodes.RequestFailed,
			ErrorMessage = "BuildCase returned a response clio could not read, so whether the case was created is "
				+ "UNKNOWN. Re-run this call with dry-run=true before deciding what to do: an equivalent case that "
				+ "exists answers already-present, and nothing else is written by a dry run. "
				+ $"The server said: {reply.Detail}",
			Case = CaseInfo(options, plan.Case, designerUrl: null),
			Settings = plan.Settings,
			// Left in place: if the case WAS written it needs them, and a dry-run re-run tells which.
			Dependencies = Dependencies(plan, missing, added, added.Count > 0 ? DependenciesAdded : DependenciesNoneMissing),
			Page = NotReached(),
			Leftovers = [],
			Notes = [],
			Warnings = added.Count > 0
				? [$"The package dependencies this call added to '{options.PackageName}' ({string.Join(", ", added)}) "
					+ "were left in place because the build outcome is unknown."]
				: []
		};

	private static string DependenciesStatusFor(List<string> missing) =>
		missing.Count == 0 ? DependenciesNoneMissing : DependenciesMissing;

	private static DynamicCaseDependencies Dependencies(BuildCaseResponse response, List<string> missing,
		List<string> added, string status) =>
		new() {
			Status = status,
			Required = response.RequiredPackages ?? [],
			Missing = missing,
			Added = added
		};

	private static DynamicCaseInfo CaseInfo(CreateDynamicCaseOptions options, BuildCaseCaseInfo info,
		string designerUrl) {
		if (info is null) {
			return null;
		}
		return new DynamicCaseInfo {
			Status = info.Status,
			SchemaName = info.SchemaName,
			SchemaUId = info.SchemaUId,
			Caption = info.Caption,
			PackageName = info.PackageName ?? options.PackageName,
			EntitySchemaName = info.EntitySchemaName ?? options.EntitySchemaName,
			StageColumnName = info.StageColumnName ?? options.StageColumnName,
			StageLookupSchemaName = info.StageLookupSchemaName,
			Enabled = info.Enabled,
			ActiveVersion = info.ActiveVersion,
			OrderSource = info.OrderSource,
			Stages = info.Stages ?? [],
			Connections = new DynamicCaseConnections { Kind = "all-to-all", Count = info.ConnectionCount },
			DesignerUrl = designerUrl
		};
	}

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
				Error = plan.ErrorMessage,
				Candidates = plan.Candidates is { Count: > 0 } ? [.. plan.Candidates] : null
			};

	private static DynamicCasePageInfo NotReached() => new() { Status = PageNotReached };

	private static DynamicCaseObligations WithPageArtifact(DynamicCaseObligations obligations, bool page) {
		if (!page) {
			return obligations;
		}
		List<DynamicCaseArtifactObligation> artifacts = [.. obligations?.Artifacts ?? []];
		// A Freedom UI page is a client schema: it is served as saved, with no compile and no restart.
		artifacts.Add(new DynamicCaseArtifactObligation { Kind = PageArtifact, Compile = false, Restart = false });
		return new DynamicCaseObligations {
			CompileRequired = obligations?.CompileRequired ?? false,
			RestartRequired = obligations?.RestartRequired ?? false,
			ReloadOpenPages = true,
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
