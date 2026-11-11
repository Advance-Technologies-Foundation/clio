using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command;

/// <summary>
/// Options for creating a minimal dynamic case (DCM) through the ProcessDesignService package. Consumed by the MCP
/// <c>create-dynamic-case</c> tool, which sets these properties directly; there is no CLI verb.
/// </summary>
// A version literal rather than presence: BuildCase is an operation an older package does not carry AT ALL, so
// presence alone would let the call reach a package that answers its route with a 404.
[RequiresPackage(BundledPackages.ProcessBuilderPackageName, MinimumProcessBuilderVersion,
	Hint = BundledPackages.ProcessBuilderInstallHint)]
public sealed class CreateDynamicCaseOptions : EnvironmentOptions {

	/// <summary>The first CrtProcessBuilder archive that carries <c>BuildCase</c>.</summary>
	/// <remarks>
	/// The one place the floor is written. It must sit above every archive without the operation: the archive this
	/// clio ships is 1.6.6.152 and has no <c>BuildCase</c>, so the first cut that can carry it is above 1.6.6.152, and
	/// this number is provisional until that cut is claimed. Until the rebundle lands,
	/// <c>BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement</c> reports that this clio demands a version it
	/// does not ship, which is true; once it lands, <c>BundledArchive_ShouldCarryBuildCase_WhenItMeetsTheCaseFloor</c>
	/// checks the archive's content, not only its number. Move this constant to the claimed cut in the rebundle commit.
	/// </remarks>
	public const string MinimumProcessBuilderVersion = "1.6.6.153";

	/// <summary>The editable package that receives the case and both settings bindings.</summary>
	public string PackageName { get; set; } = string.Empty;

	/// <summary>The object the case belongs to.</summary>
	public string EntitySchemaName { get; set; } = string.Empty;

	/// <summary>The lookup column whose values become the stages.</summary>
	public string StageColumnName { get; set; } = string.Empty;

	/// <summary>The explicit stage order (lookup value Ids or exact display values); empty for the default order.</summary>
	public IReadOnlyList<string> Stages { get; set; } = [];

	/// <summary>An integer column of the lookup that orders the stages; ignored when <see cref="Stages"/> is given.</summary>
	public string OrderColumn { get; set; }

	/// <summary>The case's schema name; the server derives one when empty.</summary>
	public string Name { get; set; }

	/// <summary>The case's caption; the server derives one when empty.</summary>
	public string Caption { get; set; }

	/// <summary>Whether to add the stage progress bar to the object's record page.</summary>
	public bool AddProgressBar { get; set; } = true;

	/// <summary>The Freedom UI record page to decorate; resolved from the object when empty.</summary>
	public string PageSchemaName { get; set; }

	/// <summary>Whether to add the package dependencies the case needs and the package does not reach yet.</summary>
	public bool AddPackageDependencies { get; set; } = true;

	/// <summary>Run every check and return the plan without writing anything.</summary>
	public bool DryRun { get; set; }
}

/// <summary>
/// Creates a minimal dynamic case and reports the outcome: the result document, a one-line verdict, the notes to
/// relay and the warnings. The exit code is 0 for <c>planned</c>, <c>created</c>, <c>created-page-skipped</c> and
/// <c>already-present</c>, and 1 for everything else.
/// </summary>
public class CreateDynamicCaseCommand(IDynamicCaseService dynamicCaseService, ILogger logger)
	: Command<CreateDynamicCaseOptions> {

	private const string NothingWritten = " Nothing was written.";

	private const string NothingWrittenPhrase = "nothing was written";

	private static readonly JsonSerializerOptions ResultJsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	/// <inheritdoc />
	public override int Execute(CreateDynamicCaseOptions options) {
		try {
			ArgumentNullException.ThrowIfNull(options);
			EnsureRequired(options.Environment, "environment-name");
			EnsureRequired(options.PackageName, "package-name");
			EnsureRequired(options.EntitySchemaName, "entity-schema-name");
			EnsureRequired(options.StageColumnName, "stage-column-name");
			DynamicCaseResult result = dynamicCaseService.Create(options);
			Report(result);
			return result.Success ? 0 : 1;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}

	/// <summary>The one-line verdict of an outcome, the sentence an agent repeats to the user.</summary>
	/// <param name="result">The result document.</param>
	internal static string Verdict(DynamicCaseResult result) {
		string name = result.Case?.SchemaName ?? "the case";
		string page = result.Page?.PageSchemaName ?? "the record page";
		bool dependenciesLeft = result.Dependencies?.Status == DynamicCaseService.DependenciesLeftAfterFailure;
		string nothingWritten = dependenciesLeft
			? " The case was not written, but the package dependencies this call added are still declared."
			: NothingWritten;
		bool caseExisted = result.Case?.Status == DynamicCaseOutcomes.AlreadyPresent;
		return result.Outcome switch {
			DynamicCaseOutcomes.Planned when caseExisted => $"Dry run: the equivalent case '{name}' already exists, so a "
				+ $"real run writes nothing to it.{NothingWritten}",
			DynamicCaseOutcomes.Planned => $"Dry run: '{name}' can be created as planned.{NothingWritten}",
			DynamicCaseOutcomes.CreatedPageFailed when caseExisted => $"The equivalent case '{name}' already exists and "
				+ $"was kept, but the progress bar was NOT added to '{page}': {result.Page?.Error} Add it with the "
				+ "update-page call in page.retry.",
			DynamicCaseOutcomes.Created => $"Dynamic case '{name}' created (UId: {result.Case?.SchemaUId})."
				+ PageClause(result),
			DynamicCaseOutcomes.CreatedPageSkipped => $"Dynamic case '{name}' created (UId: {result.Case?.SchemaUId}). "
				+ "add-progress-bar=false: the page was not changed.",
			DynamicCaseOutcomes.AlreadyPresent => $"The equivalent case '{name}' already exists; nothing was written "
				+ "to it." + PageClause(result),
			DynamicCaseOutcomes.CreatedPageFailed => $"Dynamic case '{name}' was CREATED and kept, but the progress bar "
				+ $"was NOT added to '{page}': {result.Page?.Error} Add it with the update-page call in page.retry.",
			DynamicCaseOutcomes.FailedRolledBack => $"The build failed and was rolled back [{result.ErrorCode}]: "
				+ $"{result.ErrorMessage}{nothingWritten}",
			DynamicCaseOutcomes.FailedLeftover => $"The build failed [{result.ErrorCode}] and could not undo everything "
				+ $"it wrote: {result.ErrorMessage} Remove, in this order: {string.Join("; ", result.Leftovers ?? [])}.",
			DynamicCaseOutcomes.Unknown => result.ErrorMessage,
			_ => $"Refused [{result.ErrorCode}]: {result.ErrorMessage}"
				+ (SaysNothingWasWritten(result) ? string.Empty : nothingWritten)
		};
	}

	// Some refusals already say it in their own words; repeating it would read as two separate claims.
	private static bool SaysNothingWasWritten(DynamicCaseResult result) =>
		result.ErrorMessage?.Contains(NothingWrittenPhrase, StringComparison.OrdinalIgnoreCase) == true;

	private static string PageClause(DynamicCaseResult result) => result.Page?.Status switch {
		DynamicCaseService.PageInserted => $" The progress bar was added to '{result.Page.PageSchemaName}'.",
		DynamicCasePageStep.StatusAlreadyPresent =>
			$" '{result.Page.PageSchemaName}' already carries a stage progress bar; the page was not changed.",
		_ => string.Empty
	};

	private void Report(DynamicCaseResult result) {
		string verdict = Verdict(result);
		if (result.Success) {
			logger.WriteInfo(verdict);
		} else {
			logger.WriteError(verdict);
		}
		logger.WriteInfo(JsonSerializer.Serialize(result, ResultJsonOptions));
		foreach (string note in (result.Notes ?? []).Where(note => !string.IsNullOrWhiteSpace(note))) {
			logger.WriteInfo(note);
		}
		foreach (string warning in (result.Warnings ?? []).Where(warning => !string.IsNullOrWhiteSpace(warning))) {
			logger.WriteWarning(warning);
		}
	}

	private static void EnsureRequired(string value, string argumentName) {
		if (string.IsNullOrWhiteSpace(value)) {
			throw new InvalidOperationException($"{argumentName} is required and cannot be empty.");
		}
	}
}
