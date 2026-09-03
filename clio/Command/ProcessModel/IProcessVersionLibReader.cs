using System;
using System.Collections.Generic;
using System.Linq;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.CreatioModel;

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

	/// <summary>Whether this member is the family root (version 0).</summary>
	public bool IsRoot { get; init; }

	/// <summary>UId of the package this member lives in.</summary>
	public string PackageUId { get; init; }

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
	public IReadOnlyList<ProcessVersionFamilyMember> Versions { get; init; }

	/// <summary>Whether the family was longer than the cap and <see cref="Versions"/> is partial.</summary>
	public bool FamilyTruncated { get; init; }

	/// <summary>
	/// Which authority answered. Always <c>process-library-view</c> here, and stated rather than implied
	/// because the runtime consults the schema manager instead, and the two rank candidates by different
	/// tail keys: a family that ties on the user property and the schema property can diverge.
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
	/// The facts. A transport or payload failure, an unparsable identity and an absent row all yield
	/// facts whose values are absent and whose <see cref="ProcessVersionFacts.Warning"/> says why — this
	/// method does not throw for them, because a version read must never turn a successful describe into
	/// an error. It does not swallow everything: an ATF expression failure is a defect in this code and
	/// propagates.
	/// </returns>
	ProcessVersionFacts Read(string schemaUId);
}

/// <inheritdoc cref="IProcessVersionLibReader" />
public sealed class ProcessVersionLibReader(IDataProvider dataProvider) : IProcessVersionLibReader {

	/// <summary>The only authority this reader can speak for.</summary>
	internal const string ProcessLibraryViewSource = "process-library-view";

	/// <summary>
	/// Upper bound on the reported family. A process with more versions than this is pathological, and an
	/// unbounded family read is a response-size and latency risk on a surface bounded by a read deadline.
	/// </summary>
	internal const int FamilyCap = 50;

	/// <inheritdoc />
	public ProcessVersionFacts Read(string schemaUId) {
		if (!Guid.TryParse(schemaUId, out Guid uid)) {
			return NotEstablished($"'{schemaUId}' is not a schema UId");
		}
		return ProcessLibRead.Guarded(() => {
			IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(dataProvider);
			VwProcessLib row = ctx.Models<VwProcessLib>().FirstOrDefault(p => p.UId == uid);
			if (row is null) {
				return NotEstablished($"the process library has no row for schema '{uid}'");
			}
			// The family key is the one column this feature left non-nullable, and it is used as an IDENTITY.
			// A defaulted value would not select a family: it would select every row that also defaulted, and
			// publish an unrelated process as the version to launch.
			if (row.VersionParentUId == Guid.Empty) {
				return NotEstablished($"the process library reports no version family key for schema '{uid}'");
			}
			return BuildFacts(row, ReadFamily(ctx, row.VersionParentUId));
		}, ReadFailed);
	}

	private List<VwProcessLib> ReadFamily(IAppDataContext ctx, Guid rootUId) =>
		// One row over the cap, so a family longer than the cap can be reported as truncated rather than
		// silently cut. The root is included without a second query: the view computes VersionParentUId as
		// COALESCE(parent.UId, own.UId), so a root's value is its own UId.
		ctx.Models<VwProcessLib>()
			.Where(p => p.VersionParentUId == rootUId)
			.Take(FamilyCap + 1)
			.ToList();

	private static ProcessVersionFacts BuildFacts(VwProcessLib row, List<VwProcessLib> fetched) {
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
		return new ProcessVersionFacts {
			Version = row.Version,
			IsActiveVersion = row.IsActiveVersion,
			ActiveVersionSchemaUId = active?.UId.ToString(),
			ActiveVersionName = active?.Name,
			VersionRootSchemaUId = row.VersionParentUId.ToString(),
			Versions = family.Select(ToMember).ToList(),
			FamilyTruncated = fetched.Count > FamilyCap,
			ActiveVersionSource = ProcessLibraryViewSource,
			Warning = Unestablished(row, flagged, family)
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
		List<VwProcessLib> published) {
		List<string> gaps = [];
		if (row.Version is null) {
			gaps.Add("the view established no version number for this schema");
		}
		if (row.IsActiveVersion is null) {
			gaps.Add("the view established no active-version flag for this schema");
		}
		if (flagged.Count == 0) {
			gaps.Add($"the process library flagged no active version in family '{row.VersionParentUId}'");
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

	private static ProcessVersionFamilyMember ToMember(VwProcessLib p) =>
		new() {
			SchemaUId = p.UId.ToString(),
			Name = p.Name,
			Caption = p.Caption,
			Version = p.Version,
			IsActiveVersion = p.IsActiveVersion,
			IsRoot = p.UId == p.VersionParentUId,
			PackageUId = p.PackageUId.ToString(),
			Enabled = p.Enabled
		};

	private static ProcessVersionFacts ReadFailed(Exception e) =>
		NotEstablished($"reading the process library failed: {e.Message}");

	private static ProcessVersionFacts NotEstablished(string reason) =>
		new() { Warning = $"{reason}, so the version facts were not established" };
}
