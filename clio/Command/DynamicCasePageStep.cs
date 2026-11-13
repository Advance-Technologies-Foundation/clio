using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.McpServer.Tools;
using Clio.Common;

namespace Clio.Command;

/// <summary>The planned progress-bar step, or why the page cannot take the bar.</summary>
public sealed class DynamicCasePagePlan {
	/// <summary><c>planned</c>, <c>already-present</c> or <c>refused</c>.</summary>
	public string Status { get; init; }

	/// <summary>A page refusal code; only when <see cref="Status"/> is <c>refused</c>.</summary>
	public string ErrorCode { get; init; }

	/// <summary>The refusal text.</summary>
	public string ErrorMessage { get; init; }

	/// <summary>The record page.</summary>
	public string PageSchemaName { get; init; }

	/// <summary>How the record page was found.</summary>
	public string ResolvedBy { get; init; }

	/// <summary>The design package the bar is written into.</summary>
	public string DesignPackageName { get; init; }

	/// <summary>The nearest template in the page's schema chain.</summary>
	public string Template { get; init; }

	/// <summary>
	/// The marker-wrapped append body with the bar's two items: what the dry run and the write send on append, and
	/// on replace the record of the names the write added, which undo removes.
	/// </summary>
	public string Fragment { get; init; }

	/// <summary>The bar's <c>saveOnChange</c> in <see cref="Fragment"/>.</summary>
	public bool SaveOnChange { get; init; }

	/// <summary>
	/// <c>append</c>, or <c>replace</c> for a page stored in the full form (every record page create-app makes),
	/// which append refuses to merge into.
	/// </summary>
	public string WriteMode { get; init; } = DynamicCasePageStep.AppendMode;

	/// <summary>
	/// The body the dry run checked and the write sends: <see cref="Fragment"/> on append; on replace the page's own
	/// stored body with the two items added to its viewConfigDiff. A plan without it sends <see cref="Fragment"/>.
	/// </summary>
	public string WriteBody { get; init; }

	/// <summary>
	/// The editable schema's checksum the plan was made against; the write pins it, so a change made in the designer
	/// between the plan and the write is refused instead of being merged over. Null when no replacing schema exists
	/// at plan time: then nothing is pinned, and a designer save that creates one in between is not caught.
	/// </summary>
	public string ExpectedChecksum { get; init; }

	/// <summary>The typed pages to choose from, on <c>record-page-ambiguous</c>.</summary>
	public IReadOnlyList<DynamicCasePageCandidate> Candidates { get; init; } = [];

	/// <summary>Notes about the page the caller relays.</summary>
	public IReadOnlyList<string> Notes { get; init; } = [];

	/// <summary>Caveats of the page step.</summary>
	public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>What writing the planned fragment did.</summary>
/// <param name="Inserted">Whether the page now carries the bar.</param>
/// <param name="Error">Why it does not.</param>
/// <param name="Warnings">Caveats of the save.</param>
public sealed record DynamicCasePageWrite(bool Inserted, string Error, IReadOnlyList<string> Warnings);

/// <summary>The record-page half of <c>create-dynamic-case</c>.</summary>
public interface IDynamicCasePageStep {
	/// <summary>
	/// Resolves the record page, runs the read-only checks on its merged bundle and a DRY RUN of the exact write (an
	/// append of the fragment, or for a page stored in the full form a replace of its body with the fragment's two
	/// items added), so every gate the save applies has run before the case is written. Writes nothing.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="packageName">The case's package; the add-on is read in its hierarchy.</param>
	/// <param name="entitySchemaName">The case object.</param>
	/// <param name="explicitPageSchemaName">A page the caller named, or <see langword="null"/>.</param>
	/// <param name="saveOnChange">Whether a stage click on the bar saves the record at once.</param>
	DynamicCasePagePlan Plan(string environmentName, string packageName, string entitySchemaName,
		string explicitPageSchemaName, IReadOnlyList<string> fallbackPackageNames = null, bool saveOnChange = false);

	/// <summary>Writes the planned body to the page's design package, in the planned mode.</summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="plan">A plan whose status is <c>planned</c>.</param>
	DynamicCasePageWrite Write(string environmentName, DynamicCasePagePlan plan);
}

/// <inheritdoc />
public sealed class DynamicCasePageStep(
	IDynamicCaseRecordPageResolver recordPageResolver,
	IProcessPageReader pageReader,
	IPageBodyWriter pageBodyWriter,
	IPageDataSourceReferenceValidator dataSourceValidator)
	: IDynamicCasePageStep {

	/// <summary>The page is ready to take the bar.</summary>
	public const string StatusPlanned = "planned";

	/// <summary>The page already carries a stage progress bar.</summary>
	public const string StatusAlreadyPresent = "already-present";

	/// <summary>The page cannot take the bar.</summary>
	public const string StatusRefused = "refused";

	/// <summary>The write mode of a page stored in the diff form.</summary>
	public const string AppendMode = "append";

	/// <summary>The write mode of a page stored in the full form.</summary>
	public const string ReplaceMode = "replace";

	/// <inheritdoc />
	public DynamicCasePagePlan Plan(string environmentName, string packageName, string entitySchemaName,
		string explicitPageSchemaName, IReadOnlyList<string> fallbackPackageNames = null, bool saveOnChange = false) {
		RecordPageResolution resolution = recordPageResolver.Resolve(packageName, entitySchemaName, explicitPageSchemaName,
			fallbackPackageNames);
		if (resolution.ErrorCode is not null) {
			return new DynamicCasePagePlan {
				Status = StatusRefused,
				ErrorCode = resolution.ErrorCode,
				ErrorMessage = resolution.ErrorMessage,
				ResolvedBy = resolution.ResolvedBy,
				Candidates = resolution.Candidates
			};
		}
		string pageSchemaName = resolution.PageSchemaNames[0];
		if (!pageReader.TryGetPage(new PageGetOptions { SchemaName = pageSchemaName, Environment = environmentName },
				out PageGetResponse page) || page?.Bundle is null) {
			return Refused(DynamicCaseErrorCodes.RecordPageNotFound, pageSchemaName, resolution.ResolvedBy,
				$"Record page '{pageSchemaName}' could not be read: {page?.Error ?? "no answer"}.");
		}
		if (!string.Equals(page.Page?.SchemaType, PageSchemaType.Web.ToLabel(), StringComparison.OrdinalIgnoreCase)) {
			return Refused(DynamicCaseErrorCodes.RecordPageNotFreedom, pageSchemaName, resolution.ResolvedBy,
				$"'{pageSchemaName}' is not a Freedom UI web page (schema type '{page.Page?.SchemaType}'); the stage bar "
				+ "exists on Freedom UI web record pages only.");
		}
		ProgressBarPlacementCheck check = ProgressBarPlacement.Check(page.Bundle, entitySchemaName);
		if (check.ErrorCode is not null) {
			return Refused(check.ErrorCode, pageSchemaName, resolution.ResolvedBy, check.ErrorMessage);
		}
		List<string> notes = [];
		if (!ProgressBarPlacement.IsTabbedTemplate(check.Template)) {
			notes.Add($"Record page '{pageSchemaName}' is built on '{check.Template ?? "an unknown template"}', not a "
				+ "tabbed template; the product adds the stage bar only to tabbed pages, so check the layout after the "
				+ "page is reloaded.");
		}
		string designPackageName = page.Page?.DesignPackageName;
		string expectedChecksum = page.Editable is { EditableSchemaExists: true } ? page.Editable.Checksum : null;
		if (check.BarAlreadyPresent) {
			return new DynamicCasePagePlan {
				Status = StatusAlreadyPresent,
				PageSchemaName = pageSchemaName,
				ResolvedBy = resolution.ResolvedBy,
				DesignPackageName = designPackageName,
				Template = check.Template,
				Notes = notes
			};
		}
		// Append REPLACES an entry with the same operation and name, so reusing a name the page already has would
		// overwrite that element instead of adding the bar.
		string suffix = check.NamesTaken ? "_" + Guid.NewGuid().ToString("N")[..8] : string.Empty;
		string fragment = ProgressBarFragment.BuildBody(pageSchemaName, entitySchemaName, suffix, saveOnChange);
		SchemaValidationResult references = dataSourceValidator.Validate(fragment, () => page.Bundle.ModelConfig?.ToJsonString());
		if (references is { IsValid: false }) {
			return Refused(DynamicCaseErrorCodes.PageAppendRejected, pageSchemaName, resolution.ResolvedBy,
				$"The progress bar on '{pageSchemaName}' references data the page does not have: "
				+ string.Join("; ", references.Errors));
		}
		string writeMode = AppendMode;
		string writeBody = fragment;
		// Raw is the body append would merge into: the design package's own schema, or an empty diff-form scaffold
		// when that package has none yet, so only an existing full-form schema takes the replace path.
		if (PageBodyMerger.UsesUnsupportedFullConfigForm(page.Raw?.Body, PageBodyMerger.PageBodyRole.Current, out _)) {
			if (expectedChecksum is null) {
				// A replace sends the body read here; unpinned, a designer save made before the write is lost.
				return Refused(DynamicCaseErrorCodes.PageAppendRejected, pageSchemaName, resolution.ResolvedBy,
					$"'{pageSchemaName}' is stored in the full form, so the bar is written by a replace of its body, "
					+ "and the page's checksum could not be read to pin that replace against a concurrent change. "
					+ "Retry the call.");
			}
			if (!ProgressBarFragment.TryInsertIntoFullBody(page.Raw.Body, entitySchemaName, suffix, out writeBody,
					out string insertError, saveOnChange)) {
				return Refused(DynamicCaseErrorCodes.PageAppendRejected, pageSchemaName, resolution.ResolvedBy,
					$"The progress bar cannot be added to '{pageSchemaName}', stored in the full form: {insertError}.");
			}
			writeMode = ReplaceMode;
		}
		if (!pageBodyWriter.TryUpdatePage(BuildOptions(environmentName, pageSchemaName, writeBody, writeMode,
				dryRun: true, expectedChecksum), out PageUpdateResponse dryRun) || dryRun?.Success != true) {
			return Refused(DynamicCaseErrorCodes.PageAppendRejected, pageSchemaName, resolution.ResolvedBy,
				$"The {writeMode} dry run of the progress bar on '{pageSchemaName}' was rejected: "
				+ $"{dryRun?.Error ?? "no answer"}");
		}
		return new DynamicCasePagePlan {
			Status = StatusPlanned,
			PageSchemaName = pageSchemaName,
			ResolvedBy = resolution.ResolvedBy,
			DesignPackageName = designPackageName,
			Template = check.Template,
			Fragment = fragment,
			SaveOnChange = saveOnChange,
			WriteMode = writeMode,
			WriteBody = writeBody,
			ExpectedChecksum = expectedChecksum,
			Notes = notes,
			Warnings = dryRun.Warnings?.ToList() ?? []
		};
	}

	/// <inheritdoc />
	public DynamicCasePageWrite Write(string environmentName, DynamicCasePagePlan plan) {
		ArgumentNullException.ThrowIfNull(plan);
		try {
			if (!pageBodyWriter.TryUpdatePage(BuildOptions(environmentName, plan.PageSchemaName,
					plan.WriteBody ?? plan.Fragment, plan.WriteMode ?? AppendMode, dryRun: false, plan.ExpectedChecksum),
					out PageUpdateResponse saved) || saved?.Success != true) {
				return new DynamicCasePageWrite(false, saved?.Error ?? "The page save gave no answer.", []);
			}
			return new DynamicCasePageWrite(true, null, saved.Warnings?.ToList() ?? []);
		} catch (Exception exception) {
			// The case is already written at this point; a page failure is reported with the retry call rather than
			// thrown, so it cannot turn a kept case into a call that reads as having changed nothing.
			return new DynamicCasePageWrite(false, exception.Message, []);
		}
	}

	private static DynamicCasePagePlan Refused(string code, string pageSchemaName, string resolvedBy, string message) =>
		new() {
			Status = StatusRefused,
			ErrorCode = code,
			ErrorMessage = message,
			PageSchemaName = pageSchemaName,
			ResolvedBy = resolvedBy
		};

	private static PageUpdateOptions BuildOptions(string environmentName, string pageSchemaName, string body,
		string mode, bool dryRun, string expectedChecksum) =>
		new() {
			Environment = environmentName,
			SchemaName = pageSchemaName,
			Body = body,
			Mode = mode,
			DryRun = dryRun,
			Validate = true,
			ExpectedChecksum = expectedChecksum
		};
}
