using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ATF.Repository;
using ATF.Repository.Mock;
using ATF.Repository.Providers;
using Clio.Command.ProcessModel;
using Clio.CreatioModel;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Unit coverage for <see cref="ProcessVersionLibReader"/>: what it reports for an unversioned process, for a
/// version family, and — the load-bearing half — what it reports when it could not establish anything at all.
/// </summary>
/// <remarks>
/// The distinction under test is that absence and zero are different answers. A process with no versions is
/// version 0 and carries no warning; a read that failed carries a warning and no values. Collapsing the two is
/// the defect this reader exists to prevent, so every failure case asserts BOTH halves.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "ProcessModel")]
public sealed class ProcessVersionLibReaderTests {

	private const string SchemaName = "VwProcessLib";
	private const string PackageSchemaName = "SysPackage";
	private const string PackageName = "UsrInvoicing";

	private static readonly Guid RootUId = Guid.Parse("332eac25-1443-4e4e-a972-6c0e66cb9243");
	private static readonly Guid ChildUId = Guid.Parse("b5e5162a-254a-430f-8978-4738c6ebf76b");
	private static readonly Guid PackageUId = Guid.Parse("864d1545-a641-46c3-b866-e57bd6d39579");

	/// <summary>The root of a SECOND, unrelated process, used to prove the family filter actually filters.</summary>
	private static readonly Guid ForeignRootUId = Guid.Parse("1a9d5c30-6f47-4a1b-9d52-0c8e3b7d4f21");

	/// <summary>
	/// The keys are the view's column names, because that is what ATF puts in the select and what the mock
	/// replays — not the model's property names, which differ for <c>Parent</c>.
	/// </summary>
	private static Dictionary<string, object> Row(Guid uid, string name, int? version, bool? isActive,
		Guid rootUId, string caption = "Invoice approval") =>
		new() {
			["Id"] = uid,
			["UId"] = uid,
			["Name"] = name,
			["Caption"] = caption,
			["Version"] = version,
			["IsActiveVersion"] = isActive,
			["VersionParentUId"] = rootUId,
			["PackageUId"] = PackageUId,
			["Enabled"] = true
		};

	/// <summary>
	/// The mock replays one canned row set for every query on the schema, so the row under test is placed
	/// first: the by-UId read then resolves to it whether or not the filter reaches the provider.
	/// </summary>
	/// <remarks>
	/// Measured on ATF.Repository.Mock 2.0.3.1: <c>DataProviderMock</c> honours NEITHER the <c>Where</c>
	/// predicate, NOR <c>Take</c>, NOR the <c>FirstOrDefault</c> predicate — 62 canned rows come back as 62
	/// for every one of those. That is why the family selection is asserted through
	/// <see cref="ProcessVersionLibReader.SelectFamily"/>, which runs in memory over whatever the provider
	/// returned: a filter that lives only in the query is invisible to every test in this file.
	/// </remarks>
	private static ProcessVersionLibReader ReaderOver(params Dictionary<string, object>[] rows) =>
		ReaderOver(rows, PackageRow(PackageUId, PackageName));

	/// <summary>
	/// A reader over the view rows plus an explicit <c>SysPackage</c> row set, for the cases that are ABOUT
	/// the package names rather than merely carrying them.
	/// </summary>
	private static ProcessVersionLibReader ReaderOver(Dictionary<string, object>[] rows,
		params Dictionary<string, object>[] packageRows) {
		DataProviderMock provider = new();
		provider.MockItems(SchemaName).Returns(rows.ToList());
		provider.MockItems(PackageSchemaName).Returns(packageRows.ToList());
		return new ProcessVersionLibReader(provider);
	}

	/// <summary>A <c>SysPackage</c> row, keyed by the view's own column names for the same reason as <see cref="Row"/>.</summary>
	private static Dictionary<string, object> PackageRow(Guid uid, string name) =>
		new() {
			["Id"] = uid,
			["UId"] = uid,
			["Name"] = name
		};

	/// <summary>A row object of the kind the describe caption arm already holds when it calls the reader.</summary>
	private static VwProcessLib RowObject(Guid uid, string name, int? version, bool? isActive, Guid rootUId) =>
		new() {
			UId = uid,
			Name = name,
			Caption = "Invoice approval",
			Version = version,
			IsActiveVersion = isActive,
			VersionParentUId = rootUId,
			PackageUId = PackageUId,
			Enabled = true
		};

	[Test]
	[Description("An unversioned process reports version 0, active, a one-member root family and NO warning.")]
	public void Read_Should_ReportRootOnlyFamily_When_SchemaHasNoVersions() {
		// Arrange
		ProcessVersionLibReader sut = ReaderOver(
			Row(RootUId, "UsrAccount_CreateFollowUpTask", version: 0, isActive: true, rootUId: RootUId));

		// Act
		ProcessVersionFacts facts = sut.Read(RootUId.ToString());

		// Assert
		facts.Version.Should().Be(0,
			because: "a process with no versions is version 0 in the process library — a fact, not an absence");
		facts.IsActiveVersion.Should().BeTrue(
			because: "the only member of a family is the version that runs");
		facts.Versions.Should().ContainSingle(v => v.IsRoot,
			because: "a row whose VersionParentUId equals its own UId is the family root");
		facts.VersionRootSchemaUId.Should().Be(RootUId.ToString(),
			because: "the family is keyed on the root, and this row is the root");
		facts.FamilyTruncated.Should().BeFalse(because: "one member is far below the cap");
		facts.Warning.Should().BeNull(
			because: "nothing failed, and the absence of a warning is how a caller tells this from an unestablished read");
	}

	[Test]
	[Description("Reading the root of a two-member family reports it inactive and names the version that runs.")]
	public void Read_Should_NameTheActiveVersion_When_ReadAgainstTheRoot() {
		// Arrange
		ProcessVersionLibReader sut = ReaderOver(
			Row(RootUId, "InvoiceVisaProcess", version: 0, isActive: false, rootUId: RootUId),
			Row(ChildUId, "InvoiceVisaProcessInvoice1", version: 1, isActive: true, rootUId: RootUId));

		// Act
		ProcessVersionFacts facts = sut.Read(RootUId.ToString());

		// Assert
		facts.IsActiveVersion.Should().BeFalse(
			because: "the root of this family is not the version the runtime executes");
		facts.ActiveVersionName.Should().Be("InvoiceVisaProcessInvoice1",
			because: "the caller needs the name of the running version to re-describe it without a second lookup");
		facts.ActiveVersionSchemaUId.Should().Be(ChildUId.ToString(),
			because: "the active member's UId identifies it unambiguously, unlike the shared caption");
		facts.Versions.Select(v => v.Version).Should().ContainInOrder(new int?[] { 0, 1 },
			"the family is reported ascending by version");
		facts.Versions.Should().ContainSingle(v => v.IsActiveVersion == true,
			because: "the process library reports exactly one active member for this family");
	}

	[Test]
	[Description("Every established read states which authority answered, because the runtime consults another.")]
	public void Read_Should_StateTheProcessLibraryAsItsSource_When_FactsWereEstablished() {
		// Arrange
		ProcessVersionLibReader sut = ReaderOver(
			Row(RootUId, "InvoiceVisaProcess", version: 0, isActive: true, rootUId: RootUId));

		// Act
		ProcessVersionFacts facts = sut.Read(RootUId.ToString());

		// Assert
		facts.ActiveVersionSource.Should().Be("process-library-view",
			because: "the view and the schema manager can rank a family differently, so the answer says which one it is");
	}

	[Test]
	[Description("A view with no row for the schema yields a warning and no version values, not version 0.")]
	public void Read_Should_ReportNotEstablished_When_TheViewHasNoRow() {
		// Arrange
		ProcessVersionLibReader sut = ReaderOver();

		// Act
		ProcessVersionFacts facts = sut.Read(RootUId.ToString());

		// Assert
		facts.Warning.Should().Contain(RootUId.ToString(),
			because: "the warning has to name the schema whose facts could not be established");
		facts.Version.Should().BeNull(because: "an absent row establishes nothing — it does not establish zero");
		facts.IsActiveVersion.Should().BeNull(because: "the same absence cannot be reported as 'this one runs'");
		facts.Versions.Should().BeNull(
			because: "an empty list would read as 'checked, no versions', which is a different claim");
		facts.ActiveVersionSource.Should().BeNull(because: "no authority answered, so none is credited");
	}

	[Test]
	[Description("Every family member names the package it lives in, not just its UId. A builder asking which package a version lives in reads the answer, and manual testing on ENG-94374 saw it rendered as the raw GUID 'lives in package a00051f4-...' because packageUId was all the read carried.")]
	public void Read_Should_NameEachMembersPackage_When_ThePackageRowsAreReadable() {
		// Arrange
		ProcessVersionLibReader sut = ReaderOver(
			Row(RootUId, "InvoiceVisaProcess", version: 0, isActive: false, rootUId: RootUId),
			Row(ChildUId, "InvoiceVisaProcessInvoice1", version: 1, isActive: true, rootUId: RootUId));

		// Act
		ProcessVersionFacts facts = sut.Read(RootUId.ToString());

		// Assert
		facts.Versions.Should().OnlyContain(v => v.PackageName == PackageName,
			because: "the package a version lives in is reported by NAME, which is what a person is asking for");
		facts.Versions.Should().OnlyContain(v => v.PackageUId == PackageUId.ToString(),
			because: "the UId stays on the response as the unambiguous identity - the name is added, not swapped in");
		facts.Warning.Should().BeNull(
			because: "naming the packages succeeded, and a warning here would read as a standing that was not established");
	}

	[Test]
	[Description("A cross-package family names each member's OWN package. Version numbering is counted within a package, so a family spread over two packages is normal rather than an error - and reporting one name for all of them would state the opposite.")]
	public void Read_Should_NameEachPackageSeparately_When_TheFamilySpansPackages() {
		// Arrange
		Guid secondPackageUId = Guid.Parse("6f2d9c11-45ab-42a7-9c1e-2d4b8f0a7e33");
		Dictionary<string, object> child = Row(ChildUId, "InvoiceVisaProcessUsrOther1", version: 1,
			isActive: true, rootUId: RootUId);
		child["PackageUId"] = secondPackageUId;
		ProcessVersionLibReader sut = ReaderOver(
			[Row(RootUId, "InvoiceVisaProcess", version: 0, isActive: false, rootUId: RootUId), child],
			PackageRow(PackageUId, PackageName),
			PackageRow(secondPackageUId, "UsrOther"));

		// Act
		ProcessVersionFacts facts = sut.Read(RootUId.ToString());

		// Assert
		facts.Versions.Single(v => v.IsRoot).PackageName.Should().Be(PackageName,
			because: "the root keeps its own package");
		facts.Versions.Single(v => !v.IsRoot).PackageName.Should().Be("UsrOther",
			because: "a version need not land in the root's package, and the answer has to say where it did land");
	}

	[Test]
	[Description("A package UId with no SysPackage row leaves that member unnamed and raises NO warning. An individual name that does not resolve is a gap in one field, not a standing that could not be established - and the warning channel is what a caller reads as 'the version facts are unknown'.")]
	public void Read_Should_LeaveThePackageUnnamedWithoutWarning_When_ItsRowIsMissing() {
		// Arrange
		ProcessVersionLibReader sut = ReaderOver(
			[Row(RootUId, "InvoiceVisaProcess", version: 0, isActive: true, rootUId: RootUId)],
			PackageRow(Guid.Parse("0c1e5f74-9a3d-4f5e-8b21-73d0c6a9e415"), "UsrSomethingElse"));

		// Act
		ProcessVersionFacts facts = sut.Read(RootUId.ToString());

		// Assert
		facts.Versions.Should().OnlyContain(v => v.PackageName == null,
			because: "nothing named this package, and an invented name is worse than none");
		facts.Versions.Should().OnlyContain(v => v.PackageUId == PackageUId.ToString(),
			because: "the identity the view did report survives the name that could not be resolved");
		facts.Version.Should().Be(0,
			because: "the version facts are established whether or not the packages could be named");
		facts.Warning.Should().BeNull(
			because: "one unresolved name is not an unestablished standing, and warning here would train a caller to report UNKNOWN for a read that answered");
	}

	[Test]
	[Description("A SysPackage read that comes back with NO ROWS is treated as not read, not as read-and-empty. A Creatio environment always carries packages, so an empty set is a refusal - and measured on ATF.Repository 2.0.3.1 a null or unsuccessful IItemsResponse does not throw, it yields no rows, which is the shape a restricted SysPackage comes back as. Classified as success it would publish every member with no package name and NO warning, the one combination the contract says cannot occur.")]
	public void Read_Should_WarnAboutTheUnreadablePackages_When_ThePackageQueryReturnsNoRows() {
		// Arrange
		ProcessVersionLibReader sut = ReaderOver(
			[Row(RootUId, "InvoiceVisaProcess", version: 0, isActive: true, rootUId: RootUId)],
			[]);

		// Act
		ProcessVersionFacts facts = sut.Read(RootUId.ToString());

		// Assert
		facts.Version.Should().Be(0,
			because: "the view answered, so the version facts are established whatever SysPackage did");
		facts.Versions.Should().OnlyContain(v => v.PackageName == null,
			because: "no row named this package, and an invented name is worse than none");
		facts.Warning.Should().Contain("package names could not be read",
			because: "every member lost its name at once, which is the case a caller has to be told about - "
				+ "silently answering in UIds is the defect the name was added to remove");
	}

	[Test]
	[Description("When the package table itself cannot be read, every member loses its name at once - so THAT is reported, unlike a single UId with no row. Without it the answer degrades silently to raw GUIDs in front of a builder who asked which package a version lives in.")]
	public void Read_Should_WarnAboutTheUnreadablePackages_When_ThePackageQueryFails() {
		// Arrange
		DataProviderMock inner = new();
		inner.MockItems(SchemaName).Returns(new List<Dictionary<string, object>> {
			Row(RootUId, "InvoiceVisaProcess", version: 0, isActive: true, rootUId: RootUId)
		});
		ProcessVersionLibReader sut = new(new FailingSchemaDataProvider(inner, PackageSchemaName));

		// Act
		ProcessVersionFacts facts = sut.Read(RootUId.ToString());

		// Assert
		facts.Version.Should().Be(0,
			because: "the version facts came from the view, which answered - the packages are a separate read");
		facts.Versions.Should().OnlyContain(v => v.PackageName == null,
			because: "no name was established for any member");
		facts.Warning.Should().Be(
			"the package names could not be read, so those facts were not established",
			because: "the gap was deliberately rephrased to compose with the shared tail without a doubled "
				+ "'so' and without claiming the version facts were lost - a Contain cannot see either");
	}

	[Test]
	[Description("A transport failure degrades to a warning and never propagates, so describe still answers.")]
	public void Read_Should_ReportNotEstablished_When_TheDataServiceReadFails() {
		// Arrange
		IDataProvider provider = Substitute.For<IDataProvider>();
		provider.GetItems(null).ThrowsForAnyArgs(new WebException("simulated transport failure"));
		ProcessVersionLibReader sut = new(provider);

		// Act
		Func<ProcessVersionFacts> act = () => sut.Read(RootUId.ToString());

		// Assert
		ProcessVersionFacts facts = act.Should().NotThrow(
				because: "a failed version read must never turn a successful describe into an error")
			.Which;
		facts.Warning.Should().Contain("simulated transport failure",
			because: "the caller is told why the facts are absent, not merely that they are");
		facts.Version.Should().BeNull(because: "a failed read establishes nothing");
	}

	[Test]
	[Description("A family longer than the cap is reported capped, and says so rather than truncating silently.")]
	public void Read_Should_CapTheFamilyAndSaySo_When_ItExceedsTheCap() {
		// Arrange
		Dictionary<string, object>[] rows = Enumerable.Range(0, 80)
			.Select(i => Row(i == 0 ? RootUId : Guid.NewGuid(), $"UsrProcess_Custom{i}", version: i,
				isActive: i == 0, rootUId: RootUId))
			.ToArray();
		ProcessVersionLibReader sut = ReaderOver(rows);

		// Act
		ProcessVersionFacts facts = sut.Read(RootUId.ToString());

		// Assert
		facts.Versions.Should().HaveCount(ProcessVersionLibReader.FamilyCap,
			because: "the reported family is bounded, because this read sits behind a response deadline");
		facts.FamilyTruncated.Should().BeTrue(
			because: "a silently cut list reads as a complete one, which is the failure mode the flag exists for");
		facts.ActiveVersionName.Should().Be("UsrProcess_Custom0",
			because: "the active member is named even when the family had to be capped");
	}

	[Test]
	[Description("NULL from the view stays NOT ESTABLISHED instead of collapsing into version 0 / not active.")]
	public void Read_Should_LeaveFactsUnestablished_When_TheViewReturnsNulls() {
		// Arrange
		ProcessVersionLibReader sut = ReaderOver(
			Row(RootUId, "InvoiceVisaProcess", version: null, isActive: null, rootUId: RootUId));

		// Act
		ProcessVersionFacts facts = sut.Read(RootUId.ToString());

		// Assert
		facts.Version.Should().BeNull(
			because: "the view returns NULL when the schema's package does not resolve, and 0 is a different claim");
		facts.IsActiveVersion.Should().BeNull(
			because: "reporting an unknown flag as false would name the wrong version as the running one");
		facts.Versions.Should().ContainSingle()
			.Which.Version.Should().BeNull(because: "the family member carries the same unknown, not a zero");
		facts.ActiveVersionName.Should().BeNull(
			because: "no member is flagged active, so none may be presented as the one that runs");
		facts.Warning.Should().NotBeNull(
			because: "absent values without a warning is the one combination the contract forbids - it is exactly what a caller reads as 'unversioned'");
		facts.Warning.Should().Contain("version number",
			because: "the warning names WHICH fact could not be established, not merely that something could not");
		facts.Warning.Should().Contain("active-version flag",
			because: "both NULL columns are unestablished here, and reporting one hides the other");
	}

	[Test]
	[Description("An identity that is not a schema UId is refused with a warning instead of throwing.")]
	public void Read_Should_ReportNotEstablished_When_TheIdentityIsNotAGuid() {
		// Arrange
		ProcessVersionLibReader sut = ReaderOver(
			Row(RootUId, "InvoiceVisaProcess", version: 0, isActive: true, rootUId: RootUId));

		// Act
		Func<ProcessVersionFacts> act = () => sut.Read("not-a-guid");

		// Assert
		ProcessVersionFacts facts = act.Should().NotThrow(
				because: "the describe path passes through whatever the server reported, so a malformed identity is data, not a bug")
			.Which;
		facts.Warning.Should().Contain("not-a-guid",
			because: "the warning names the value that could not be parsed");
		facts.Version.Should().BeNull(because: "nothing was read, so nothing was established");
	}
	[Test]
	[Description("A family with no member flagged active reports the values it did establish AND a warning naming the gap, instead of a silent absence.")]
	public void Read_Should_WarnAboutTheMissingActiveMember_When_NoMemberIsFlagged() {
		// Arrange - reachable whenever the view resolves the rows but flags none of them, which is not the
		// same failure as a read that did not happen.
		ProcessVersionLibReader sut = ReaderOver(
			Row(RootUId, "InvoiceVisaProcess", version: 0, isActive: false, rootUId: RootUId),
			Row(ChildUId, "InvoiceVisaProcessInvoice1", version: 1, isActive: false, rootUId: RootUId));

		// Act
		ProcessVersionFacts facts = sut.Read(RootUId.ToString());

		// Assert
		facts.Version.Should().Be(0,
			because: "this schema's own version WAS established, and a partial answer keeps what it knows");
		facts.ActiveVersionName.Should().BeNull(because: "no member was flagged, so none can be named");
		facts.Warning.Should().Contain("flagged no active version",
			because: "the prompt tells an agent to read isActiveVersion before narrating, so an unanswerable check has to say so");
		facts.Versions.Should().HaveCount(2,
			because: "the family was read successfully; only the active-version fact was missing from it");
	}

	[Test]
	[Description("A family with TWO members flagged active names neither, and says how many are flagged, because which one runs is decided by a key this view does not expose.")]
	public void Read_Should_NameNoActiveVersion_When_TwoMembersAreFlagged() {
		// Arrange - reachable per ADR choice 4: the platform logs and swallows sibling deactivation failures,
		// so a partial activation leaves two members flagged.
		ProcessVersionLibReader sut = ReaderOver(
			Row(RootUId, "InvoiceVisaProcess", version: 0, isActive: true, rootUId: RootUId),
			Row(ChildUId, "InvoiceVisaProcessInvoice1", version: 1, isActive: true, rootUId: RootUId));

		// Act
		ProcessVersionFacts facts = sut.Read(RootUId.ToString());

		// Assert
		facts.ActiveVersionName.Should().BeNull(
			because: "an unordered ATF result would name whichever row came first, differently between calls, and the prompt steers the agent onto exactly that graph");
		facts.ActiveVersionSchemaUId.Should().BeNull(
			because: "publishing a pointer here would hand the caller a specific wrong version to launch");
		facts.Warning.Should().Contain("flags 2 active versions",
			because: "the caller has to learn the library is inconsistent, not that no version is active");
		facts.Versions.Should().HaveCount(2,
			because: "the family itself was read fine; only the which-one-runs fact is unanswerable");
	}

	[Test]
	[Description("A cancelled HTTP read degrades to a warning: TaskCanceledException is what the transport actually raises on timeout, and it is the reason the shared failure surface exists.")]
	public void Read_Should_ReportNotEstablished_When_TheTransportRaisesTaskCanceled() {
		// Arrange - Creatio.Client posts through HttpClient, whose timeout raises TaskCanceledException and
		// NEVER TimeoutException. Before ProcessLibRead.Guarded listed OperationCanceledException, this
		// escaped the reader and surfaced as an unhandled exception inside the MCP server.
		IDataProvider provider = Substitute.For<IDataProvider>();
		provider.GetItems(null).ThrowsForAnyArgs(new TaskCanceledException("simulated request timeout"));
		ProcessVersionLibReader sut = new(provider);

		// Act
		Func<ProcessVersionFacts> act = () => sut.Read(RootUId.ToString());

		// Assert
		ProcessVersionFacts facts = act.Should().NotThrow(
				because: "a version read that times out must never turn a successful describe into an error")
			.Which;
		facts.Warning.Should().Contain("simulated request timeout",
			because: "the caller is told why the facts are absent, and a timeout is the most likely why");
		facts.Version.Should().BeNull(because: "a read that did not complete establishes nothing");
	}

	[Test]
	[Description("Rows of a SECOND process reach the reader and are excluded: the family filter is the single guard against publishing an unrelated schema as the version to launch, and describe hands that pointer to an agent which is told to re-describe by it and run it.")]
	public void Read_Should_ExcludeForeignFamilies_When_TheViewReturnsMoreThanOne() {
		// Arrange - the foreign row is the one flagged active, so losing the filter does not merely add a
		// member: it makes the answer name a schema belonging to a different process, or refuse to name one at
		// all because two are flagged.
		ProcessVersionLibReader sut = ReaderOver(
			Row(RootUId, "InvoiceVisaProcess", version: 0, isActive: false, rootUId: RootUId),
			Row(ChildUId, "InvoiceVisaProcessInvoice1", version: 1, isActive: true, rootUId: RootUId),
			Row(Guid.NewGuid(), "UsrOrder_ApproveCustom1", version: 1, isActive: true, rootUId: ForeignRootUId,
				caption: "Order approval"));

		// Act
		ProcessVersionFacts facts = sut.Read(RootUId.ToString());

		// Assert
		facts.Versions.Select(v => v.Name).Should()
			.BeEquivalentTo(["InvoiceVisaProcess", "InvoiceVisaProcessInvoice1"],
				because: "the reported family is the read schema's own, and a member of another process in it "
					+ "would be published to an agent as a version of this one");
		facts.ActiveVersionName.Should().Be("InvoiceVisaProcessInvoice1",
			because: "the active member is chosen within the family; a foreign flagged row must not be able to "
				+ "become the pointer describe tells the agent to launch");
		facts.ActiveVersionSchemaUId.Should().Be(ChildUId.ToString(),
			because: "the pointer is what an agent re-describes and runs, so it must identify this family");
		facts.Warning.Should().BeNull(
			because: "everything was established; a second family leaking in would show up here as the "
				+ "two-members-flagged gap instead");
	}

	[Test]
	[Description("A schema whose family key the view did not establish publishes no values at all. VersionParentUId is the one column left non-nullable, so a view NULL arrives as Guid.Empty — and using it as an identity would select every other row that also defaulted.")]
	public void Read_Should_ReportNotEstablished_When_TheFamilyKeyIsDefaulted() {
		// Arrange
		ProcessVersionLibReader sut = ReaderOver(
			Row(RootUId, "InvoiceVisaProcess", version: 3, isActive: true, rootUId: Guid.Empty));

		// Act
		ProcessVersionFacts facts = sut.Read(RootUId.ToString());

		// Assert
		facts.Warning.Should().Contain("no version family key",
			because: "the warning names WHICH fact was missing, and this one is the identity everything else "
				+ "is keyed on");
		facts.Version.Should().BeNull(
			because: "the row's own version was readable, but publishing it beside a family nobody could "
				+ "select would present a partial answer as a complete one");
		facts.Versions.Should().BeNull(because: "no family could be selected, so none may be reported");
		facts.ActiveVersionSource.Should().BeNull(because: "no authority established anything here");
	}

	[Test]
	[Description("A family read that comes back with no member of the schema's own family reports Versions as absent rather than empty: an empty list reads as 'checked, and there are no versions', which is the one claim this reader must never make without having checked.")]
	public void Read_Should_ReportNotEstablished_When_TheFamilyReadReturnsNothing() {
		// Arrange - reachable through the row entry point, where the row the caller holds and the family the
		// view answers with are two separate reads that can disagree.
		DataProviderMock provider = new();
		provider.MockItems(SchemaName).Returns([
			Row(Guid.NewGuid(), "UsrOrder_Approve", version: 0, isActive: true, rootUId: ForeignRootUId)
		]);
		ProcessVersionLibReader sut = new(provider);

		// Act
		ProcessVersionFacts facts = sut.Read(RowObject(RootUId, "InvoiceVisaProcess", 0, true, RootUId));

		// Assert
		facts.Warning.Should().Contain("no version family",
			because: "the caller is told the family could not be established, not handed an empty one");
		facts.Versions.Should().BeNull(
			because: "an empty list would read as a checked answer with no versions in it");
		facts.Version.Should().BeNull(because: "a partial family answer publishes no version facts");
	}

	[Test]
	[Description("The row entry point skips the identity lookup the caller already performed. The describe caption arm holds this exact row, and re-fetching it by UId is a DataService round-trip that establishes nothing new.")]
	public void Read_Should_SkipTheIdentityQuery_When_TheCallerSuppliesTheRow() {
		// Arrange
		DataProviderMock provider = new();
		IItemsMock items = provider.MockItems(SchemaName).Returns([
			Row(RootUId, "InvoiceVisaProcess", version: 0, isActive: false, rootUId: RootUId),
			Row(ChildUId, "InvoiceVisaProcessInvoice1", version: 1, isActive: true, rootUId: RootUId)
		]);
		// Mocked even though this test is about the query COUNT on the view: an unmocked SysPackage answers
		// with no rows, which the reader reads as a refused package table and reports - correctly, and
		// unrelated to what is under test here.
		provider.MockItems(PackageSchemaName).Returns([PackageRow(PackageUId, PackageName)]);
		ProcessVersionLibReader sut = new(provider);

		// Act
		ProcessVersionFacts facts = sut.Read(RowObject(RootUId, "InvoiceVisaProcess", 0, false, RootUId));

		// Assert
		items.ReceivedCount.Should().Be(1,
			because: "only the FAMILY still has to be read; the schema's own row was handed in, and the UId "
				+ "entry point pays two queries for the same answer");
		facts.ActiveVersionName.Should().Be("InvoiceVisaProcessInvoice1",
			because: "skipping the identity query must not change the facts the reader establishes");
		facts.Warning.Should().BeNull(because: "the family was read and its active member established");
	}

	[Test]
	[Description("A SysPackage read that is merely SLOW loses the NAMES and nothing else. Guarded converts a package read that FAILS into an absent name but cannot convert one that is still running, so without a separate budget a stalled package table spends the version read's own time and the OUTER expiry discards facts that were already computed. Every other test here answers instantly, so the slice is unreachable from them: deleting the WithinBudget wrapper leaves them all green.")]
	public void Read_Should_KeepTheVersionFacts_When_ThePackageReadStalls() {
		// Arrange
		using BlockingSchemaDataProvider provider = new(ViewRows(), PackageSchemaName);
		ProcessVersionLibReader sut = new(provider, TimeSpan.FromSeconds(1));

		// Act
		ProcessVersionFacts facts = sut.Read(RootUId.ToString());

		// Assert
		facts.Version.Should().Be(0,
			because: "the view answered; only the package table is stuck, and the two are separate reads");
		facts.ActiveVersionSource.Should().Be("process-library-view",
			because: "an authority answered, which is what separates this from a read that established nothing");
		facts.Versions.Should().NotBeNull().And.OnlyContain(v => v.PackageName == null,
			because: "the names are what the stall costs");
		facts.Warning.Should().Contain("package names could not be read",
			because: "the caller has to know WHY it is answering in UIds");
		facts.Warning.Should().NotContain("did not complete within",
			because: "that is the OUTER expiry - reporting it here would mean the version facts were lost too, "
				+ "which is the failure the slice exists to prevent");
	}

	[Test]
	[Description("The by-UId entry point measures the identity query too. That overload pays a lookup BEFORE the family read, and a clock started after it leaves that time unmeasured - the package read then computes its share against a budget already partly spent and can outlast the outer wait, discarding version facts that were in hand. This is the path describe takes for process-name and process-uid, so the row-overload test alone does not cover it.")]
	public void Read_Should_KeepTheVersionFacts_When_TheIdentityQueryAlreadySpentPartOfTheBudget() {
		// Arrange
		using BlockingSchemaDataProvider provider = new(ViewRows(), PackageSchemaName) {
			DelayBeforeViewAnswer = TimeSpan.FromMilliseconds(1600)
		};
		ProcessVersionLibReader sut = new(provider, TimeSpan.FromSeconds(4));

		// Act
		// The by-UId overload, so the delay is paid TWICE - once for the identity query and once for the
		// family - which is exactly the term a stopwatch started inside FactsForRow would miss.
		ProcessVersionFacts facts = sut.Read(RootUId.ToString());

		// Assert
		facts.Version.Should().Be(0,
			because: "both view reads finished inside the budget, so the standing is established");
		facts.Warning.Should().Contain("package names could not be read",
			because: "only the names may be lost to a stalled package table");
		facts.Warning.Should().NotContain("did not complete within",
			because: "that is the OUTER expiry, and reaching it means the identity query's time went "
				+ "unmeasured - the version facts are then discarded after being computed");
	}

	[Test]
	[Description("The slice is bounded by what is LEFT, not by a fraction of the total. The package read starts only after the family read, so a third of the TOTAL on top of a family read that already spent most of it pushes past the outer bound - the outer wait expires and discards version facts that were computed. Reverting PackageReadBudget to the slice alone reddens this and nothing else.")]
	public void Read_Should_KeepTheVersionFacts_When_TheFamilyReadAlreadySpentMostOfTheBudget() {
		// Arrange
		using BlockingSchemaDataProvider provider = new(ViewRows(), PackageSchemaName) {
			DelayBeforeViewAnswer = TimeSpan.FromMilliseconds(3200)
		};
		// 4 s against a 3.2 s delay, not 2 s against 1.6 s: the fixture is Parallelizable and already holds
		// two pool threads, and ~200 ms of slack for two Task.Run scheduling hops flakes as `Version` null
		// plus "did not complete within" - the test reporting the very defect it exists to disprove.
		ProcessVersionLibReader sut = new(provider, TimeSpan.FromSeconds(4));

		// Act
		// The ROW entry point, so the view is read once: the by-UId overload also pays the identity query,
		// and two delays would expire the outer budget before the package read is ever reached - which is a
		// different failure from the one under test.
		ProcessVersionFacts facts = sut.Read(RowObject(RootUId, "InvoiceVisaProcess", 0, true, RootUId));

		// Assert
		facts.Version.Should().Be(0,
			because: "the family read finished inside the budget, so its facts are established whatever the "
				+ "package read then did with the little that was left");
		facts.Warning.Should().Contain("package names could not be read",
			because: "the names are the only thing a stalled package table may cost");
		facts.Warning.Should().NotContain("did not complete within",
			because: "a slice taken from the TOTAL rather than the REMAINING time is what pushes the whole read "
				+ "past its bound, and that loss is the one under test");
	}

	[Test]
	[Description("A read that is merely SLOW degrades exactly like one that failed. RemoteDataProvider is constructed with no timeout, so without a clio-side budget the only bound was the 120 s MCP read deadline — and by the time that fires the whole describe is lost along with the graph it had already built.")]
	public void Read_Should_ReportNotEstablished_When_TheReadOutrunsItsBudget() {
		// Arrange
		IDataProvider provider = Substitute.For<IDataProvider>();
		provider.GetItems(null).ReturnsForAnyArgs(_ => {
			Thread.Sleep(TimeSpan.FromSeconds(2));
			return null;
		});
		ProcessVersionLibReader sut = new(provider, TimeSpan.FromMilliseconds(100));

		// Act
		Func<ProcessVersionFacts> act = () => sut.Read(RootUId.ToString());

		// Assert
		ProcessVersionFacts facts = act.Should().NotThrow(
				because: "a version read that outran its budget must degrade, not turn a working describe into "
					+ "an error - the same contract a FAILED read already had")
			.Which;
		facts.Warning.Should().Contain("did not complete within",
			because: "the caller is told the read was abandoned rather than that the process has no versions");
		facts.Version.Should().BeNull(because: "an abandoned read establishes nothing");
		facts.ActiveVersionSource.Should().BeNull(because: "no authority answered inside the budget");
	}

	[Test]
	[Description("A truncated family whose flagged member fell outside the FETCH blames the reader's own cap, not the platform. The library did flag one; this reader never read the row, and 'flagged no active version' beside FamilyTruncated is a pair of contradictory claims an agent then reports as fact.")]
	public void Read_Should_BlameItsOwnCap_When_TheFlaggedMemberFellOutsideTheFetch() {
		// Arrange - the flagged member sits past FamilyCap + 1, which is all the query takes and in no defined
		// order, so it is absent from the fetched set entirely.
		Dictionary<string, object>[] rows = Enumerable.Range(0, 60)
			.Select(i => Row(i == 0 ? RootUId : Guid.NewGuid(), $"UsrProcess_Custom{i}", version: i,
				isActive: i == 55, rootUId: RootUId))
			.ToArray();
		ProcessVersionLibReader sut = ReaderOver(rows);

		// Act
		ProcessVersionFacts facts = sut.Read(RootUId.ToString());

		// Assert
		facts.FamilyTruncated.Should().BeTrue(because: "60 members is over the reader's cap of 50");
		facts.Warning.Should().Contain($"exceeded the {ProcessVersionLibReader.FamilyCap}-member read cap",
			because: "the honest statement is about this reader's bound, which is what actually stopped the "
				+ "active member being seen");
		facts.Warning.Should().NotContain("flagged no active version",
			because: "the library DID flag one - claiming otherwise is a statement about the platform that "
				+ "this read never checked, and it contradicts FamilyTruncated in the same answer");
		facts.ActiveVersionName.Should().BeNull(
			because: "no member of the fetched set was flagged, so none may be named");
	}

	[Test]
	[Description("A truncated family whose flagged member WAS fetched still names it, and says it fell outside the members reported. The cap is applied after the active member is resolved precisely so a long family can still be told which version runs.")]
	public void Read_Should_StillNameTheActiveVersion_When_ItFellOutsideTheReportedMembers() {
		// Arrange - the flagged member is the 51st fetched row and carries the HIGHEST version, so it survives
		// the fetch and is then ordered out of the 50 published.
		Dictionary<string, object>[] rows = Enumerable.Range(0, 60)
			.Select(i => Row(i == 0 ? RootUId : Guid.NewGuid(), $"UsrProcess_Custom{i}",
				version: i == 50 ? 59 : i, isActive: i == 50, rootUId: RootUId))
			.ToArray();
		ProcessVersionLibReader sut = ReaderOver(rows);

		// Act
		ProcessVersionFacts facts = sut.Read(RootUId.ToString());

		// Assert
		facts.ActiveVersionName.Should().Be("UsrProcess_Custom50",
			because: "the active member is looked up in the fetched set rather than the capped one, so the "
				+ "caller can still reach the version that runs");
		facts.Versions.Should().NotContain(v => v.Name == "UsrProcess_Custom50",
			because: "it sorts last by version and the published list is capped at 50");
		facts.Warning.Should().Contain($"fell outside the {ProcessVersionLibReader.FamilyCap} members reported",
			because: "naming a version that is absent from the list beside it would read as a contradiction "
				+ "unless the gap says why");
	}

	/// <summary>
	/// Answers every schema through <paramref name="inner"/> except one, which fails the way a DataService
	/// read fails.
	/// </summary>
	/// <remarks>
	/// <see cref="DataProviderMock"/> can be told what a schema RETURNS and not that it throws, and a
	/// substitute that throws for any argument cannot separate the family read from the package read - the
	/// two go through the same provider. The distinction under test is exactly that separation: the version
	/// facts have to survive a package read that did not.
	/// </remarks>
	private sealed class FailingSchemaDataProvider(DataProviderMock inner, string failingSchemaName)
		: IDataProvider {

		public IDefaultValuesResponse GetDefaultValues(string schemaName) => inner.GetDefaultValues(schemaName);

		public IItemsResponse GetItems(ISelectQuery selectQuery) =>
			string.Equals(selectQuery?.RootSchemaName, failingSchemaName, StringComparison.OrdinalIgnoreCase)
				? throw new WebException($"simulated failure reading '{failingSchemaName}'")
				: inner.GetItems(selectQuery);

		public IExecuteResponse BatchExecute(List<IBaseQuery> queries) => inner.BatchExecute(queries);

		public T GetSysSettingValue<T>(string sysSettingCode) => inner.GetSysSettingValue<T>(sysSettingCode);

		public bool GetFeatureEnabled(string featureCode) => inner.GetFeatureEnabled(featureCode);

		public IExecuteProcessResponse ExecuteProcess(IExecuteProcessRequest request) =>
			inner.ExecuteProcess(request);
	}

	/// <summary>The canned view rows the budget tests read a family out of.</summary>
	private static List<Dictionary<string, object>> ViewRows() => [
		Row(RootUId, "InvoiceVisaProcess", version: 0, isActive: true, rootUId: RootUId)
	];

	/// <summary>
	/// Answers the view from a canned set and BLOCKS one schema until disposal.
	/// </summary>
	/// <remarks>
	/// The blocked read is what no other fixture here can produce: <see cref="DataProviderMock"/> answers
	/// instantly, so a budget that is never reached is a budget no assertion can see - both the slice and its
	/// remaining-time bound delete cleanly with the suite green.
	/// <para>
	/// A gate released on Dispose rather than a sleep-to-completion: the test asserts on the reader's own
	/// expiry, so it must not also wait for the blocked call, and a fixed sleep would trade one timing
	/// assumption for a slower one. <see cref="DelayBeforeViewAnswer"/> exists for the second case, where the
	/// point is that the FAMILY read has already spent most of the budget before the package read starts.
	/// </para>
	/// </remarks>
	private sealed class BlockingSchemaDataProvider(
		List<Dictionary<string, object>> viewRows, string blockedSchemaName) : IDataProvider, IDisposable {
		private readonly ManualResetEventSlim _release = new(false);

		public TimeSpan DelayBeforeViewAnswer { get; init; } = TimeSpan.Zero;

		public IItemsResponse GetItems(ISelectQuery selectQuery) {
			if (string.Equals(selectQuery?.RootSchemaName, blockedSchemaName, StringComparison.OrdinalIgnoreCase)) {
				_release.Wait();
				return new CannedItemsResponse([]);
			}
			if (DelayBeforeViewAnswer > TimeSpan.Zero) {
				_release.Wait(DelayBeforeViewAnswer);
			}
			return new CannedItemsResponse(viewRows);
		}

		public IDefaultValuesResponse GetDefaultValues(string schemaName) => null;

		public IExecuteResponse BatchExecute(List<IBaseQuery> queries) => null;

		public T GetSysSettingValue<T>(string sysSettingCode) => default;

		public bool GetFeatureEnabled(string featureCode) => false;

		public IExecuteProcessResponse ExecuteProcess(IExecuteProcessRequest request) => null;

		public void Dispose() {
			// Set only. Disposing while the abandoned reader thread is still inside Wait() is a race of its
			// own, and the gate is a per-test object the GC can take.
			_release.Set();
		}
	}

	private sealed class CannedItemsResponse(List<Dictionary<string, object>> items) : IItemsResponse {
		public bool Success => true;

		public string ErrorMessage => null;

		public List<Dictionary<string, object>> Items => items;
	}
}
