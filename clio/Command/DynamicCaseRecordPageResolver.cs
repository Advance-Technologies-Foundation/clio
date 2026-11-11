using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.RelatedPages;
using Clio.Common;

namespace Clio.Command;

/// <summary>How the record page of a case object was found, or why it could not be.</summary>
/// <param name="PageSchemaNames">The target pages; one today, a list so a later decoration can reuse the resolver.</param>
/// <param name="ResolvedBy"><c>explicit</c>, <c>related-page-addon</c> or <c>sys-module-edit</c>.</param>
/// <param name="ErrorCode">A page refusal code, or <see langword="null"/> when a page was found.</param>
/// <param name="ErrorMessage">The refusal text.</param>
/// <param name="Candidates">The typed pages to choose from, on <c>record-page-ambiguous</c>.</param>
public sealed record RecordPageResolution(
	IReadOnlyList<string> PageSchemaNames,
	string ResolvedBy,
	string ErrorCode,
	string ErrorMessage,
	IReadOnlyList<DynamicCasePageCandidate> Candidates);

/// <summary>
/// Finds the Freedom UI web record page an object opens, the way the Freedom UI navigation does: the object's
/// <c>RelatedPage</c> add-on first, the <c>SysModuleEdit</c> card registration when the add-on lists nothing.
/// </summary>
public interface IDynamicCaseRecordPageResolver {
	/// <summary>Resolves the record page of <paramref name="entitySchemaName"/>; reads only.</summary>
	/// <param name="packageName">The package whose hierarchy the add-on is read in.</param>
	/// <param name="entitySchemaName">The case object.</param>
	/// <param name="explicitPageSchemaName">A page the caller named; it wins over any registration.</param>
	RecordPageResolution Resolve(string packageName, string entitySchemaName, string explicitPageSchemaName);
}

/// <inheritdoc />
public sealed class DynamicCaseRecordPageResolver(
	IRelatedPageAddonService relatedPageAddonService,
	IEntityClientSchemaReader entityClientSchemaReader)
	: IDynamicCaseRecordPageResolver {

	/// <summary>Resolved from the caller's <c>page-schema-name</c>.</summary>
	public const string ResolvedByExplicit = "explicit";

	/// <summary>Resolved from the object's <c>RelatedPage</c> add-on.</summary>
	public const string ResolvedByRelatedPageAddon = "related-page-addon";

	/// <summary>Resolved from the object's <c>SysModuleEdit</c> card registration.</summary>
	public const string ResolvedBySysModuleEdit = "sys-module-edit";

	private const string RemedyNamePage = " Pass page-schema-name with the Freedom UI record page to decorate.";

	/// <inheritdoc />
	public RecordPageResolution Resolve(string packageName, string entitySchemaName, string explicitPageSchemaName) {
		if (!string.IsNullOrWhiteSpace(explicitPageSchemaName)) {
			return Found(explicitPageSchemaName.Trim(), ResolvedByExplicit);
		}
		string addonFailure = null;
		IReadOnlyList<RelatedPageEntry> pages = [];
		try {
			pages = relatedPageAddonService
				.Get(new RelatedPageAddonReadRequest(packageName, entitySchemaName, RelatedPageSchemaType.Web))
				?.Pages ?? [];
		} catch (Exception exception) {
			addonFailure = exception.Message;
		}
		// An add-on that could not be read is not an empty one: the card registrations would then decorate a page
		// the object does not open, or call a Freedom object Classic. The read fails, for example, when the case
		// package does not reach the object yet (its dependencies are added only after these checks).
		if (addonFailure is not null) {
			return NotFound($"The RelatedPage add-on of '{entitySchemaName}' could not be read in '{packageName}', so "
				+ $"the record page the object opens is unknown: {addonFailure}" + RemedyNamePage);
		}
		List<RelatedPageEntry> defaults = pages.Where(IsEmployeeDefault).ToList();
		if (defaults.Count > 0) {
			return FromAddon(entitySchemaName, defaults);
		}
		return FromSysModuleEdit(entitySchemaName);
	}

	// The Freedom UI picks the web default for an employee: the portal default and a page restricted to a custom role
	// are other audiences' pages, and a caller decorating them would change what someone else sees.
	private static bool IsEmployeeDefault(RelatedPageEntry page) =>
		page is { IsDefault: true, IsSspDefault: false }
		&& !string.IsNullOrWhiteSpace(page.PageSchemaUId)
		&& (string.IsNullOrWhiteSpace(page.Role)
			|| string.Equals(page.RoleName, RelatedPageSpecBuilder.EmployeesRoleName, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(page.Role, SysAdminUnitIds.AllEmployees.ToString(), StringComparison.OrdinalIgnoreCase));

	private static RecordPageResolution FromAddon(string entitySchemaName, List<RelatedPageEntry> defaults) {
		List<RelatedPageEntry> untyped = defaults.Where(page => string.IsNullOrWhiteSpace(page.TypeColumnValue)).ToList();
		List<string> untypedNames = untyped
			.Select(page => page.PageSchemaName)
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (untypedNames.Count == 1) {
			return Found(untypedNames[0], ResolvedByRelatedPageAddon);
		}
		if (untyped.Count > 0 && untypedNames.Count == 0) {
			return NotFound($"The RelatedPage add-on of '{entitySchemaName}' names a default page whose schema could "
				+ "not be read." + RemedyNamePage);
		}
		List<DynamicCasePageCandidate> candidates = defaults
			.Where(page => !string.IsNullOrWhiteSpace(page.PageSchemaName))
			.Select(page => new DynamicCasePageCandidate {
				PageSchemaName = page.PageSchemaName,
				TypeColumnValue = page.TypeColumnValue
			})
			.ToList();
		string reason = untypedNames.Count > 1
			? $"The RelatedPage add-on of '{entitySchemaName}' lists {untypedNames.Count} default pages"
			: $"'{entitySchemaName}' opens a different record page per record type";
		return new RecordPageResolution([], ResolvedByRelatedPageAddon, DynamicCaseErrorCodes.RecordPageAmbiguous,
			reason + ", so the page to decorate has to be named." + RemedyNamePage, candidates);
	}

	private RecordPageResolution FromSysModuleEdit(string entitySchemaName) {
		bool read = entityClientSchemaReader.TryResolve(
			new ListEntityClientSchemasOptions { EntityName = entitySchemaName },
			out ListEntityClientSchemasResponse response);
		if (!read || response?.Success != true) {
			return NotFound($"'{entitySchemaName}' has no RelatedPage add-on default page, and its card registrations "
				+ $"could not be read: {response?.Error ?? "no answer"}." + RemedyNamePage);
		}
		List<MigrationEditPageInfo> cards = (response.EditPages ?? [])
			.Where(page => !string.IsNullOrWhiteSpace(page.CardSchema))
			.ToList();
		List<MigrationEditPageInfo> untyped = cards.Where(page => string.IsNullOrWhiteSpace(page.TypeColumnValue)).ToList();
		if (untyped.Count == 0 && cards.Count > 0) {
			return new RecordPageResolution([], ResolvedBySysModuleEdit, DynamicCaseErrorCodes.RecordPageAmbiguous,
				$"'{entitySchemaName}' opens a different record page per record type, so the page to decorate has "
				+ "to be named." + RemedyNamePage,
				cards.Select(page => new DynamicCasePageCandidate {
					PageSchemaName = page.CardSchema, TypeColumnValue = page.TypeColumnValue
				}).ToList());
		}
		List<MigrationEditPageInfo> distinctUntyped = untyped
			.GroupBy(page => page.CardSchema, StringComparer.OrdinalIgnoreCase)
			.Select(group => group.First())
			.ToList();
		if (distinctUntyped.Count > 1) {
			// The registrations come back unordered, and an object may register a portal card next to the employee
			// one: picking the first would decorate whichever page the database returns first.
			return new RecordPageResolution([], ResolvedBySysModuleEdit, DynamicCaseErrorCodes.RecordPageAmbiguous,
				$"'{entitySchemaName}' registers {distinctUntyped.Count} record pages, so the page to decorate has to be "
				+ "named." + RemedyNamePage,
				distinctUntyped.Select(page => new DynamicCasePageCandidate {
					PageSchemaName = page.CardSchema, TypeColumnValue = page.TypeColumnValue
				}).ToList());
		}
		MigrationEditPageInfo card = distinctUntyped.FirstOrDefault();
		if (card is null) {
			return NotFound($"'{entitySchemaName}' has neither a RelatedPage add-on default page nor a card "
				+ "registration." + RemedyNamePage);
		}
		if (string.Equals(card.Kind, ListEntityClientSchemasCommand.KindClassic, StringComparison.OrdinalIgnoreCase)) {
			return new RecordPageResolution([], ResolvedBySysModuleEdit, DynamicCaseErrorCodes.RecordPageNotFreedom,
				$"'{entitySchemaName}' opens the Classic page '{card.CardSchema}', which cannot carry the Freedom UI "
				+ "stage bar. Create a Freedom UI record page (create-page) and register it as the object's default "
				+ "(create-related-page-addon), or pass page-schema-name with an existing Freedom UI record page.", []);
		}
		return Found(card.CardSchema, ResolvedBySysModuleEdit);
	}

	private static RecordPageResolution Found(string pageSchemaName, string resolvedBy) =>
		new([pageSchemaName], resolvedBy, null, null, []);

	private static RecordPageResolution NotFound(string message) =>
		new([], null, DynamicCaseErrorCodes.RecordPageNotFound, message, []);
}
