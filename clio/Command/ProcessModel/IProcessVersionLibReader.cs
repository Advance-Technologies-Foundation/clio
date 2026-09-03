using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
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
/// "unversioned". <see cref="Warning"/> is what separates the two: a process that genuinely has no
/// versions reports <see cref="Version"/> 0 with no warning, while a read that could not be performed
/// reports no values and a warning saying why.
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
		try {
			IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(dataProvider);
			VwProcessLib row = ctx.Models<VwProcessLib>().FirstOrDefault(p => p.UId == uid);
			if (row is null) {
				return NotEstablished($"the process library has no row for schema '{uid}'");
			}
			return BuildFacts(row, ReadFamily(ctx, row.VersionParentUId));
		} catch (WebException e) {
			return ReadFailed(e);
		} catch (HttpRequestException e) {
			return ReadFailed(e);
		} catch (JsonException e) {
			return ReadFailed(e);
		} catch (TimeoutException e) {
			return ReadFailed(e);
		} catch (InvalidOperationException e) {
			return ReadFailed(e);
		}
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
		// Ordered here rather than in the query: Version is nullable, and ordering a nullable column
		// through ATF is unproven, while the set is bounded at FamilyCap + 1 rows.
		List<VwProcessLib> family = fetched
			.OrderBy(p => p.Version ?? int.MaxValue)
			.ThenBy(p => p.Name)
			.Take(FamilyCap)
			.ToList();
		// Looked up in the fetched set rather than the capped one, so the active member is still named
		// when it sorts past the cap.
		VwProcessLib active = fetched.FirstOrDefault(p => p.IsActiveVersion == true);
		return new ProcessVersionFacts {
			Version = row.Version,
			IsActiveVersion = row.IsActiveVersion,
			ActiveVersionSchemaUId = active?.UId.ToString(),
			ActiveVersionName = active?.Name,
			VersionRootSchemaUId = row.VersionParentUId.ToString(),
			Versions = family.Select(ToMember).ToList(),
			FamilyTruncated = fetched.Count > FamilyCap,
			ActiveVersionSource = ProcessLibraryViewSource
		};
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
