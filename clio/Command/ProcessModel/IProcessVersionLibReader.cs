using System;
using System.Collections.Generic;
using System.Linq;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.CreatioModel;
using CreatioModel;

namespace Clio.Command.ProcessModel;

/// <summary>
/// One member of a process's version family, exactly as the process-library view reports it.
/// </summary>
/// <remarks>
/// This is a read model, not a wire contract: it carries no serialization attributes, so the describe
/// surface is free to project it into whatever shape it publishes.
/// </remarks>
public sealed record ProcessVersionFamilyMember {

	/// <summary>Schema UId of this family member.</summary>
	public string SchemaUId { get; init; }

	/// <summary>Schema name (code). Every version of a process has a different one.</summary>
	public string Name { get; init; }

	/// <summary>Display caption. Every version of a process usually has the SAME one.</summary>
	public string Caption { get; init; }

	/// <summary>Version number, or <c>null</c> when the view could not establish it.</summary>
	public int? Version { get; init; }

	/// <summary>Whether the process library reports this member as the active version.</summary>
	public bool? IsActiveVersion { get; init; }

	/// <summary>
	/// Whether this member is the family root. The only field that answers it: a root's version number is
	/// stamped rather than derived — commonly 0, but stock content carries roots numbered 1 and 2.
	/// </summary>
	public bool IsRoot { get; init; }

	/// <summary>UId of the package this member lives in.</summary>
	public string PackageUId { get; init; }

	/// <summary>
	/// Name of that package, or <c>null</c> when it could not be resolved.
	/// </summary>
	/// <remarks>
	/// A convenience over <see cref="PackageUId"/>, which stays the authority: the process library reports
	/// the UId and nothing else, so this is read from <c>SysPackage</c> in a separate query and a family
	/// whose package rows cannot be read still reports every other fact. Absent here therefore means "not
	/// named", never "no package" — and unlike the value members on
	/// <see cref="ProcessVersionFacts"/>, an individual absence carries no warning of its own. Only a
	/// package read that FAILED does, because that is the case where every member loses its name at once
	/// and the answer silently degrades to GUIDs.
	/// </remarks>
	public string PackageName { get; init; }

	/// <summary>
	/// Whether the process is enabled. This is FAMILY state, not per-version state: the platform keys
	/// enable/disable on the root schema, so every member of a family reports the same value.
	/// </summary>
	public bool Enabled { get; init; }
}

/// <summary>
/// A process's version facts as read from the platform's own process-library view.
/// </summary>
/// <remarks>
/// Every value member is nullable and means NOT ESTABLISHED when absent — never "version 0" and never
/// "unversioned". <see cref="Warning"/> is what separates the two, and the invariant is one-directional:
/// an absent value ALWAYS comes with a warning naming the fact that could not be established, while a
/// process that genuinely has no versions reports <see cref="Version"/> 0 with no warning. The warning is
/// not limited to a read that failed outright — a read can succeed and still establish less than
/// everything (the view returns NULL for a schema whose package does not resolve; a family can come back
/// with no member flagged active), and those answers carry the values they did establish alongside it.
/// </remarks>
public sealed record ProcessVersionFacts {

	/// <summary>The read schema's own version number.</summary>
	public int? Version { get; init; }

	/// <summary>Whether the read schema is the version the process library reports as active.</summary>
	public bool? IsActiveVersion { get; init; }

	/// <summary>Schema UId of the version the process library reports as active.</summary>
	public string ActiveVersionSchemaUId { get; init; }

	/// <summary>Schema name of the version the process library reports as active.</summary>
	public string ActiveVersionName { get; init; }

	/// <summary>Schema UId of the version-family root — the identity the family is keyed on.</summary>
	public string VersionRootSchemaUId { get; init; }

	/// <summary>The family, ascending by version. <c>null</c> when it could not be established.</summary>
	/// <remarks>
	/// A LOWER BOUND on the family, not a census of it, and this is the one place in this reader where an
	/// incomplete answer can arrive without a warning.
	/// <para>
	/// Every other gap is detectable from the rows themselves and is reported through
	/// <see cref="Warning"/> - a null version number, a family with no member flagged active, the reader's own
	/// cap via <see cref="FamilyTruncated"/>. This one is not: the set comes from <c>VwProcessLib</c>, a
	/// platform view whose SQL is neither in this repository nor in the core sources, so a family member the
	/// view does not return is indistinguishable from a member that does not exist. Nothing here can
	/// cross-check it, and the substitute provider replays a canned set whatever the filter says, so no test
	/// can observe it either.
	/// </para>
	/// <para>
	/// Consequence for a caller: treat a SHORT list as "these members are certainly in the family", never as
	/// "the family has only these". A decision that depends on the family being complete - is this the last
	/// version, is there anything left to activate - is not supported by this field, and
	/// <see cref="ActiveVersionSchemaUId"/> plus <see cref="Warning"/> are what to read instead. Making it a
	/// census would need a second, independent source for the family; there is none in this surface today.
	/// </para>
	/// </remarks>
	public IReadOnlyList<ProcessVersionFamilyMember> Versions { get; init; }

	/// <summary>Whether the family was longer than the cap and <see cref="Versions"/> is partial.</summary>
	/// <remarks>
	/// Covers THIS reader's cap only. It is not a completeness signal for the underlying view - see the
	/// remarks on <see cref="Versions"/> - so <c>false</c> means "not cut by us", never "all of them".
	/// </remarks>
	public bool FamilyTruncated { get; init; }

	/// <summary>
	/// Which authority answered — <c>process-library-view</c> whenever the facts were established, and absent
	/// alongside <see cref="Warning"/> on every answer that established none, so its presence is itself the
	/// signal that an authority answered. Stated rather than implied because the runtime consults the schema
	/// manager instead, and the two rank candidates by different tail keys: a family that ties on the user
	/// property and the schema property can diverge.
	/// </summary>
	public string ActiveVersionSource { get; init; }

	/// <summary>Why the facts could not be established, or <c>null</c> when they were.</summary>
	public string Warning { get; init; }
}

/// <summary>
/// Reads a process's version facts and its version family from the platform's own process-library view
/// (<see cref="VwProcessLib"/>), which already carries the version number, the active-version flag and
/// the family key. Read-only DataService; requires no <c>CrtProcessBuilder</c> operation.
/// </summary>
public interface IProcessVersionLibReader {

	/// <summary>
	/// Reads the version facts for a schema.
	/// </summary>
	/// <param name="schemaUId">The schema UId, as a GUID string.</param>
	/// <returns>
	/// The facts. A transport or payload failure, an unparsable identity, an absent row and a read that
	/// outran its wall-clock budget all yield facts whose values are absent and whose
	/// <see cref="ProcessVersionFacts.Warning"/> says why — this method does not throw for them, because a
	/// version read must never turn a successful describe into an error. It does not swallow everything: an
	/// ATF expression failure is a defect in this code and propagates.
	/// </returns>
	ProcessVersionFacts Read(string schemaUId);

	/// <summary>
	/// Reads the version facts for a schema whose process-library row the caller already holds.
	/// </summary>
	/// <param name="row">The row, as the caller's own query returned it.</param>
	/// <returns>The facts, on the same terms as <see cref="Read(string)"/>.</returns>
	/// <remarks>
	/// Exists because the describe caption path has already fetched this exact row — it carries every field
	/// the facts need about the schema itself — and re-fetching it by UId is a DataService round-trip that
	/// establishes nothing new. The trade-off is that the row is read BEFORE the describe POST rather than
	/// after it, which is already true of the caption resolution that produced it.
	/// </remarks>
	ProcessVersionFacts Read(VwProcessLib row);
}

/// <inheritdoc cref="IProcessVersionLibReader" />
public sealed class ProcessVersionLibReader : IProcessVersionLibReader {

	/// <summary>The only authority this reader can speak for.</summary>
	internal const string ProcessLibraryViewSource = "process-library-view";

	/// <summary>
	/// Upper bound on the reported family. A process with more versions than this is pathological, and an
	/// unbounded family read is a response-size and latency risk on a surface bounded by a read deadline.
	/// </summary>
	internal const int FamilyCap = 50;

	/// <summary>
	/// Wall-clock budget for one whole version read, family included.
	/// </summary>
	/// <remarks>
	/// The same order as the describe POST's own 10 s, because the two sit inside one deadline-bounded MCP
	/// read and this one had no clio-side bound at all: <c>RemoteDataProvider</c> is constructed with no
	/// timeout, so its ceiling was whatever the ATF library defaults to. Degrading on failure was never
	/// enough — <see cref="ProcessLibRead.Guarded"/> converts a read that FAILS into a warning but cannot
	/// convert one that is merely SLOW, and by the time the MCP read deadline fires the describe is lost
	/// along with the graph it had already built.
	/// </remarks>
	internal static readonly TimeSpan DefaultReadBudget = TimeSpan.FromSeconds(10);

	private readonly IDataProvider _dataProvider;
	private readonly TimeSpan _readBudget;

	/// <summary>
	/// Creates a reader bounded by <see cref="DefaultReadBudget"/>.
	/// </summary>
	/// <param name="dataProvider">The DataService provider the read goes through.</param>
	public ProcessVersionLibReader(IDataProvider dataProvider)
		: this(dataProvider, DefaultReadBudget) { }

	// Separate from the public constructor because Microsoft DI selects a constructor whose every parameter
	// it can resolve, and a registered TimeSpan is not something this container has: a single constructor
	// carrying an optional budget would make the registration unresolvable.
	internal ProcessVersionLibReader(IDataProvider dataProvider, TimeSpan readBudget) {
		_dataProvider = dataProvider;
		_readBudget = readBudget;
	}

	/// <inheritdoc />
	public ProcessVersionFacts Read(string schemaUId) {
		if (!Guid.TryParse(schemaUId, out Guid uid)) {
			return NotEstablished($"'{schemaUId}' is not a schema UId");
		}
		return WithinBudget(() => ProcessLibRead.Guarded(() => {
			IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_dataProvider);
			VwProcessLib row = ctx.Models<VwProcessLib>().FirstOrDefault(p => p.UId == uid);
			return row is null
				? NotEstablished($"the process library has no row for schema '{uid}'")
				: FactsForRow(ctx, row);
		}, ReadFailed));
	}

	/// <inheritdoc />
	public ProcessVersionFacts Read(VwProcessLib row) {
		if (row is null) {
			return NotEstablished("no process-library row was supplied");
		}
		return WithinBudget(() => ProcessLibRead.Guarded(
			() => FactsForRow(AppDataContextFactory.GetAppDataContext(_dataProvider), row), ReadFailed));
	}

	/// <summary>
	/// The rows of one family out of a fetched set that may carry rows of others.
	/// </summary>
	/// <remarks>
	/// The identical predicate is also pushed into the query, and the duplication is deliberate. The query's
	/// copy is a performance narrowing that nothing in clio can observe — the view SQL is not in this
	/// repository and the test provider replays its canned set whatever the filter says — while this copy is
	/// the guard that DECIDES, on rows a test can hand it. Without it, the single defence against publishing
	/// an unrelated process as <c>activeVersionName</c> lived only in a LINQ clause whose deletion left every
	/// test green.
	/// </remarks>
	internal static List<VwProcessLib> SelectFamily(IEnumerable<VwProcessLib> fetched, Guid rootUId) =>
		// One row over the cap, so a family longer than the cap can be reported as truncated rather than
		// silently cut.
		fetched
			.Where(p => p.VersionParentUId == rootUId)
			.Take(FamilyCap + 1)
			.ToList();

	private ProcessVersionFacts FactsForRow(IAppDataContext ctx, VwProcessLib row) {
		// The family key is the one column this feature left non-nullable, and it is used as an IDENTITY.
		// A defaulted value would not select a family: it would select every row that also defaulted, and
		// publish an unrelated process as the version to launch.
		if (row.VersionParentUId == Guid.Empty) {
			return NotEstablished($"the process library reports no version family key for schema '{row.UId}'");
		}
		List<VwProcessLib> family = ReadFamily(ctx, row.VersionParentUId);
		return BuildFacts(row, family, () => ReadPackageNames(ctx));
	}

	/// <summary>
	/// Wall-clock slice of <see cref="_readBudget"/> the package-name read may spend.
	/// </summary>
	/// <remarks>
	/// A third, so a stalled <c>SysPackage</c> table cannot spend what the family read still needs. Letting
	/// it run on the shared budget trades the whole answer - version, active-version flag and family - for a
	/// field whose absence this reader already knows how to report.
	/// </remarks>
	private TimeSpan PackageReadBudget => TimeSpan.FromTicks(_readBudget.Ticks / 3);

	/// <summary>
	/// The package names this environment has, keyed by package UId, or <c>null</c> when they could not be read.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Separated from the family read because the two answer different questions and fail independently: a
	/// family that reads fine must not lose its version facts because <c>SysPackage</c> did not answer. The
	/// failure is absorbed here rather than by the caller's guard, which would degrade the WHOLE read. A
	/// read that is merely SLOW is absorbed the same way and for the same reason: the budget it would
	/// otherwise spend is the one the version facts are answered out of, and losing them to a convenience
	/// field is the worse trade.
	/// </para>
	/// <para>
	/// No rows is NOT an answer. A Creatio environment always carries packages, so an empty set means the
	/// read was refused rather than that there are none to name: measured on ATF.Repository 2.0.3.1, a null
	/// or unsuccessful <c>IItemsResponse</c> does not throw, it yields no rows, which is the shape a
	/// restricted <c>SysPackage</c> comes back as. Reporting that as read would publish every member with
	/// no package name and no warning, the one combination the contract on
	/// <see cref="ProcessVersionFamilyMember.PackageName"/> says cannot occur.
	/// </para>
	/// <para>
	/// Unfiltered on purpose. The filter that belongs here is "the UIds this family uses", and expressing it
	/// needs a collection <c>Contains</c> in the ATF expression tree — an unproven shape on this surface,
	/// where a wrong expression raises <c>ExpressionConvertException</c>, which
	/// <see cref="ProcessLibRead.Guarded"/> deliberately does not catch (it is a call-site defect, not a fact
	/// that could not be established). Asking per UId instead trades one round-trip for up to
	/// <see cref="FamilyCap"/> of them on a cross-package family. So one query returns the table: a few
	/// hundred rows of scalars, inside the slice of the budget this read is given.
	/// </para>
	/// </remarks>
	private Dictionary<Guid, string> ReadPackageNames(IAppDataContext ctx) =>
		ProcessLibRead.WithinBudget(PackageReadBudget,
			() => ProcessLibRead.Guarded<Dictionary<Guid, string>>(
				() => {
					List<SysPackage> rows = ctx.Models<SysPackage>().ToList();
					// Last write wins rather than throwing: SysPackage.UId is unique in practice, and a
					// duplicate is not a reason to lose the version facts this read exists to deliver.
					return rows.Count == 0
						? null
						: rows.GroupBy(package => package.UId)
							.ToDictionary(group => group.Key, group => group.Last().Name);
				},
				_ => null),
			() => null);

	private static List<VwProcessLib> ReadFamily(IAppDataContext ctx, Guid rootUId) =>
		// The root is included without a second query: the view computes VersionParentUId as
		// COALESCE(parent.UId, own.UId), so a root's value is its own UId.
		SelectFamily(
			ctx.Models<VwProcessLib>()
				.Where(p => p.VersionParentUId == rootUId)
				.Take(FamilyCap + 1)
				.ToList(),
			rootUId);

	private ProcessVersionFacts WithinBudget(Func<ProcessVersionFacts> read) =>
		ProcessLibRead.WithinBudget(_readBudget, read,
			() => NotEstablished(
				$"the process library read did not complete within {_readBudget.TotalSeconds:0.##}s"));

	private static ProcessVersionFacts BuildFacts(VwProcessLib row, List<VwProcessLib> fetched,
		Func<Dictionary<Guid, string>> readPackageNames) {
		if (fetched.Count == 0) {
			// The schema's own row came back but its family did not, so nothing about the family is
			// established. Publishing an empty list here would read as "checked, and there are no versions",
			// which is the one thing this reader must never say without having checked.
			return NotEstablished(
				$"the process library returned no version family for root '{row.VersionParentUId}'");
		}
		// Ordered here rather than in the query: Version is nullable, and ordering a nullable column
		// through ATF is unproven, while the set is bounded at FamilyCap + 1 rows.
		List<VwProcessLib> family = fetched
			.OrderBy(p => p.Version ?? int.MaxValue)
			.ThenBy(p => p.Name)
			.Take(FamilyCap)
			.ToList();
		// Looked up in the fetched set rather than the capped one, so the active member survives the cap
		// applied below. That is a bound, not a guarantee: the query takes FamilyCap + 1 rows in no defined
		// order, so on a family LARGER than that the active member can be missing from the set entirely —
		// which is why its absence is reported as an unestablished fact instead of passing silently.
		List<VwProcessLib> flagged = fetched.Where(p => p.IsActiveVersion == true).ToList();
		// Exactly one, or none named. Sibling deactivation failures are logged and swallowed by the platform
		// (ADR choice 4), so a partial activation really does leave two members flagged, and the runtime then
		// picks between them by a key this view does not expose. FirstOrDefault over an unordered ATF result
		// would name the loser, differently between calls, while publishing two isActiveVersion: true members.
		VwProcessLib active = flagged.Count == 1 ? flagged[0] : null;
		// Deferred to here because the early return above would pay for the names and then discard them, and
		// the read spends a slice of the same budget the version facts are answered out of.
		Dictionary<Guid, string> packageNames = readPackageNames();
		return new ProcessVersionFacts {
			Version = row.Version,
			IsActiveVersion = row.IsActiveVersion,
			ActiveVersionSchemaUId = active?.UId.ToString(),
			ActiveVersionName = active?.Name,
			VersionRootSchemaUId = row.VersionParentUId.ToString(),
			Versions = family.Select(member => ToMember(member, packageNames)).ToList(),
			FamilyTruncated = fetched.Count > FamilyCap,
			ActiveVersionSource = ProcessLibraryViewSource,
			Warning = Unestablished(row, flagged, family, truncated: fetched.Count > FamilyCap,
				packagesRead: packageNames is not null)
		};
	}

	/// <summary>
	/// Why a fact is missing from an otherwise successful read, or <c>null</c> when none is.
	/// </summary>
	/// <remarks>
	/// A read can succeed and still establish less than everything: the view returns NULL for a schema
	/// whose package does not resolve, and a family can come back with no member flagged active. Without
	/// this, those answers carried absent values and NO warning — the one combination the contract on
	/// <see cref="ProcessVersionFacts"/> says cannot occur, and the one a caller reads as "unversioned".
	/// </remarks>
	private static string Unestablished(VwProcessLib row, List<VwProcessLib> flagged,
		List<VwProcessLib> published, bool truncated, bool packagesRead) {
		List<string> gaps = [];
		// Reported once for the whole read rather than per member: when the package table did not answer,
		// EVERY member loses its name at once, and a caller that says nothing renders raw GUIDs at a builder
		// who asked which package a version lives in.
		if (!packagesRead) {
			gaps.Add("the package names could not be read, so every version reports its package as a UId only");
		}
		if (row.Version is null) {
			gaps.Add("the view established no version number for this schema");
		}
		if (row.IsActiveVersion is null) {
			gaps.Add("the view established no active-version flag for this schema");
		}
		if (flagged.Count == 0) {
			// On a truncated family this reader's OWN cap is a sufficient explanation, and blaming the platform
			// for flagging nothing would be a claim about the library it never checked — the flagged member can
			// be absent from the fetch entirely, since the query takes FamilyCap + 1 rows in no defined order.
			// Saying both at once ("no active version" beside FamilyTruncated) is worse than saying neither.
			gaps.Add(truncated
				? $"the family exceeded the {FamilyCap}-member read cap, so the active version may not have "
					+ "been read"
				: $"the process library flagged no active version in family '{row.VersionParentUId}'");
		} else if (flagged.Count > 1) {
			gaps.Add($"the process library flags {flagged.Count} active versions in family "
				+ $"'{row.VersionParentUId}' ({string.Join(", ", flagged.Select(p => p.Name))}), and which one "
				+ "the runtime executes is decided by a key this view does not expose");
		} else if (published.TrueForAll(p => p.UId != flagged[0].UId)) {
			// The cap is applied after the active member is resolved, so a long family can name an active
			// version that is not among the members published beside it.
			gaps.Add($"the active version '{flagged[0].Name}' fell outside the {FamilyCap} members reported");
		}
		return gaps.Count == 0 ? null : $"{string.Join("; ", gaps)}, so those facts were not established";
	}

	private static ProcessVersionFamilyMember ToMember(VwProcessLib p, Dictionary<Guid, string> packageNames) =>
		new() {
			SchemaUId = p.UId.ToString(),
			Name = p.Name,
			Caption = p.Caption,
			Version = p.Version,
			IsActiveVersion = p.IsActiveVersion,
			IsRoot = p.UId == p.VersionParentUId,
			PackageUId = p.PackageUId.ToString(),
			PackageName = packageNames is not null && packageNames.TryGetValue(p.PackageUId, out string name)
				? name
				: null,
			Enabled = p.Enabled
		};

	private static ProcessVersionFacts ReadFailed(Exception e) =>
		NotEstablished($"reading the process library failed: {e.Message}");

	private static ProcessVersionFacts NotEstablished(string reason) =>
		new() { Warning = $"{reason}, so the version facts were not established" };
}
