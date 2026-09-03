using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using ATF.Repository.Mock;
using ATF.Repository.Providers;
using Clio.Command.ProcessModel;
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

	private static readonly Guid RootUId = Guid.Parse("332eac25-1443-4e4e-a972-6c0e66cb9243");
	private static readonly Guid ChildUId = Guid.Parse("b5e5162a-254a-430f-8978-4738c6ebf76b");
	private static readonly Guid PackageUId = Guid.Parse("864d1545-a641-46c3-b866-e57bd6d39579");

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
	private static ProcessVersionLibReader ReaderOver(params Dictionary<string, object>[] rows) {
		DataProviderMock provider = new();
		provider.MockItems(SchemaName).Returns(rows.ToList());
		return new ProcessVersionLibReader(provider);
	}

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

}
