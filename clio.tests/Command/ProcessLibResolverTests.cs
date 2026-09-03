using System;
using System.Collections.Generic;
using Clio.Command.ProcessModel;
using Clio.CreatioModel;
using ErrorOr;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit tests for the pure process-resolution selection logic: resolve by system Name
/// (code) with a fallback to display Caption, and ambiguity handling.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "ProcessModel")]
public sealed class ProcessLibResolverTests {

	private static readonly Guid FamilyA = Guid.Parse("332eac25-1443-4e4e-a972-6c0e66cb9243");
	private static readonly Guid FamilyB = Guid.Parse("7c1d4f6e-9b02-4a55-8d31-1f0a5e2c7b48");

	/// <summary>
	/// A candidate that is its own family, which is what an unversioned process is: VersionParentUId is
	/// COALESCE(parent.UId, own.UId), so it can never be left unset without making distinct processes look
	/// like one family to the resolver.
	/// </summary>
	private static VwProcessLib Row(string name, string caption) =>
		new() { Name = name, Caption = caption, VersionParentUId = Guid.NewGuid() };

	/// <summary>
	/// A caption candidate carrying the active-version flag and an EXPLICIT family key. The flag is left
	/// NULL by <see cref="Row"/> on purpose, so the pre-existing ambiguity tests keep exercising the
	/// unestablished-flag path.
	/// </summary>
	private static VwProcessLib VersionRow(string name, string caption, int version, bool? isActiveVersion,
		Guid? family = null) =>
		new() {
			Name = name,
			Caption = caption,
			Version = version,
			IsActiveVersion = isActiveVersion,
			VersionParentUId = family ?? FamilyA
		};

	[Test]
	[Description("Resolves the process by exact system Name (code) when a Name match is present.")]
	public void Resolve_Should_Return_NameMatch_When_Present() {
		// Arrange
		VwProcessLib byName = Row("UsrProcess_e629820", "Business process 1");

		// Act
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("UsrProcess_e629820", byName, []);

		// Assert
		result.IsError.Should().BeFalse(because: "an exact Name match is a valid resolution");
		result.Value.Name.Should().Be("UsrProcess_e629820", because: "the matched row should be returned");
	}

	[Test]
	[Description("Prefers the exact Name match over any Caption matches.")]
	public void Resolve_Should_Prefer_Name_Over_Caption_Matches() {
		// Arrange
		VwProcessLib byName = Row("UsrProcess_e629820", "Business process 1");
		var byCaption = new List<VwProcessLib> { Row("UsrProcess_other", "ignored") };

		// Act
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("UsrProcess_e629820", byName, byCaption);

		// Assert
		result.IsError.Should().BeFalse(because: "a Name match resolves regardless of caption matches");
		result.Value.Name.Should().Be("UsrProcess_e629820",
			because: "the Name match takes precedence over caption candidates");
	}

	[Test]
	[Description("Falls back to a single Caption match and returns the system Name as the code.")]
	public void Resolve_Should_Fall_Back_To_Single_Caption_Match() {
		// Arrange
		var byCaption = new List<VwProcessLib> { Row("UsrProcess_e629820", "Business process 1") };

		// Act
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("Business process 1", byName: null, byCaption);

		// Assert
		result.IsError.Should().BeFalse(because: "a single caption match is an unambiguous resolution");
		result.Value.Name.Should().Be("UsrProcess_e629820",
			because: "the resolved code is the system Name even when a caption was passed");
	}

	[Test]
	[Description("Returns NotFound when neither Name nor Caption matches anything.")]
	public void Resolve_Should_Return_NotFound_When_Nothing_Matches() {
		// Arrange
		// (no rows match the requested value)

		// Act
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("Does not exist", byName: null, []);

		// Assert
		result.IsError.Should().BeTrue(because: "a value matching no process cannot be resolved");
		result.FirstError.Type.Should().Be(ErrorType.NotFound, because: "nothing matched the value");
		result.FirstError.Description.Should().Contain("name or caption",
			because: "the message should explain both lookup paths were tried");
	}

	[Test]
	[Description("Returns Conflict listing candidate codes when a Caption matches more than one process.")]
	public void Resolve_Should_Return_Conflict_When_Caption_Is_Ambiguous() {
		// Arrange
		var byCaption = new List<VwProcessLib> {
			Row("UsrProcess_aaa", "Business process 1"),
			Row("UsrProcess_bbb", "Business process 1")
		};

		// Act
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("Business process 1", byName: null, byCaption);

		// Assert
		result.IsError.Should().BeTrue(because: "an ambiguous caption cannot be resolved to one process");
		result.FirstError.Type.Should().Be(ErrorType.Conflict, because: "the caption matched multiple processes");
		result.FirstError.Description.Should().Contain("Multiple processes match",
			because: "the message should state the ambiguity");
		result.FirstError.Description.Should().Contain("UsrProcess_aaa").And.Contain("UsrProcess_bbb",
			because: "the candidate codes should be listed so the caller can pick one");
	}

	[Test]
	[Description("Tolerates a null caption list and returns NotFound without throwing.")]
	public void Resolve_Should_Tolerate_Null_Caption_List() {
		// Arrange
		// (byCaption is null)

		// Act
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("x", byName: null, byCaption: null);

		// Assert
		result.IsError.Should().BeTrue(because: "no match is available when both inputs are empty/null");
		result.FirstError.Type.Should().Be(ErrorType.NotFound, because: "a null caption list means nothing matched");
	}
	[Test]
	[Description("Resolves a caption shared by a whole version family to the version the runtime executes.")]
	public void Resolve_Should_Return_TheActiveVersion_When_TheCaptionMatchesAVersionFamily() {
		// Arrange — every version of a process carries the same caption, so this is ONE process, not three.
		List<VwProcessLib> byCaption = [
			VersionRow("InvoiceVisaProcess", "Invoice approval", 0, isActiveVersion: false),
			VersionRow("InvoiceVisaProcessInvoice1", "Invoice approval", 1, isActiveVersion: true),
			VersionRow("InvoiceVisaProcessInvoice2", "Invoice approval", 2, isActiveVersion: false)
		];

		// Act
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("Invoice approval", null, byCaption);

		// Assert
		result.IsError.Should().BeFalse(
			because: "a caption matching one family is not ambiguous — exactly one of its members runs");
		result.Value.Name.Should().Be("InvoiceVisaProcessInvoice1",
			because: "the active version is the schema the runtime executes, and answering for another member answers for a graph nobody runs");
	}

	[Test]
	[Description("Keeps the ambiguity error when several DIFFERENT processes share a caption, each active in its own family.")]
	public void Resolve_Should_Return_Conflict_When_SeveralActiveProcessesShareTheCaption() {
		// Arrange
		List<VwProcessLib> byCaption = [
			VersionRow("UsrProcess_first", "Business process 1", 0, isActiveVersion: true, family: FamilyA),
			VersionRow("UsrProcess_second", "Business process 1", 0, isActiveVersion: true,
				family: FamilyB)
		];

		// Act
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("Business process 1", null, byCaption);

		// Assert
		result.IsError.Should().BeTrue(
			because: "two processes that each run are genuinely ambiguous, and the active-version filter must not disguise that");
		result.FirstError.Type.Should().Be(ErrorType.Conflict,
			because: "the pre-existing ambiguity classification is unchanged for real ambiguity");
		result.FirstError.Description.Should().Contain("UsrProcess_second",
			because: "the caller picks a code out of the candidate list, so every match is named");
	}

	[Test]
	[Description("Falls back to the ambiguity error rather than an arbitrary pick when no candidate's active-version flag is established.")]
	public void Resolve_Should_Return_Conflict_When_NoCandidateIsFlaggedActive() {
		// Arrange — IsActiveVersion is nullable because the view returns NULL when the package does not resolve.
		List<VwProcessLib> byCaption = [
			VersionRow("UsrProcess_first", "Business process 1", 0, isActiveVersion: null, family: FamilyA),
			VersionRow("UsrProcess_second", "Business process 1", 1, isActiveVersion: null,
				family: FamilyB)
		];

		// Act
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("Business process 1", null, byCaption);

		// Assert
		result.IsError.Should().BeTrue(
			because: "an unestablished flag is not permission to choose — the caller is asked for a code instead");
		result.FirstError.Type.Should().Be(ErrorType.Conflict,
			because: "the fallback is the error that already existed, not a new failure mode");
	}

	[Test]
	[Description("Resolves a single caption match even when its active-version flag was never established.")]
	public void Resolve_Should_Return_TheSingleMatch_When_ItsActiveFlagIsUnestablished() {
		// Arrange
		List<VwProcessLib> byCaption = [VersionRow("UsrProcess_only", "Business process 1", 0, isActiveVersion: null)];

		// Act
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("Business process 1", null, byCaption);

		// Assert
		result.IsError.Should().BeFalse(
			because: "one candidate needs no narrowing, so an unestablished flag must not turn a working resolution into an error");
		result.Value.Name.Should().Be("UsrProcess_only", because: "the only match is the answer");
	}

	[Test]
	[Description("Refuses a caption shared by two DIFFERENT processes even when only one of them is flagged active, because a single flagged row does not make the candidates one family.")]
	public void Resolve_Should_Return_Conflict_When_OnlyOneOfTwoFamiliesIsFlaggedActive() {
		// Arrange — family B's row is unflagged, which the view really does return: its own active member
		// was renamed away from this caption, or its package does not resolve and the flag comes back NULL.
		List<VwProcessLib> byCaption = [
			VersionRow("UsrOrder_Approve", "Approval", 0, isActiveVersion: false, family: FamilyA),
			VersionRow("UsrOrder_ApproveCustom1", "Approval", 1, isActiveVersion: true, family: FamilyA),
			VersionRow("UsrInvoice_Approve", "Approval", 0, isActiveVersion: null, family: FamilyB)
		];

		// Act
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("Approval", null, byCaption);

		// Assert
		result.IsError.Should().BeTrue(
			because: "the candidates span two families, so exactly one active row proves nothing about which process was meant");
		result.FirstError.Type.Should().Be(ErrorType.Conflict,
			because: "answering for family A here would silently drop a distinct process carrying the same caption");
		result.FirstError.Description.Should().Contain("UsrInvoice_Approve",
			because: "the dropped candidate is exactly the one the caller has to see to pick a code");
	}

	[Test]
	[Description("Refuses one family that flags MORE than one version active, naming how many, because the runtime picks between them by a key this resolver does not read.")]
	public void Resolve_Should_Return_Conflict_When_OneFamilyFlagsTwoActiveVersions() {
		// Arrange - reachable per ADR choice 4: sibling deactivation failures are logged and swallowed, so a
		// partial activation leaves two members flagged.
		List<VwProcessLib> byCaption = [
			VersionRow("UsrOrder_Approve", "Approval", 0, isActiveVersion: true, family: FamilyA),
			VersionRow("UsrOrder_ApproveCustom1", "Approval", 1, isActiveVersion: true, family: FamilyA)
		];

		// Act
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("Approval", null, byCaption);

		// Assert
		result.IsError.Should().BeTrue(
			because: "two flagged members mean the library itself is inconsistent, and picking one would be a guess dressed as an answer");
		result.FirstError.Description.Should().Contain("flags 2 of its versions as active",
			because: "'Multiple processes match' would send the reader hunting for a second process that does not exist");
	}

	[Test]
	[Description("Refuses one family where NO version is flagged active, and says that rather than reporting several processes.")]
	public void Resolve_Should_Return_Conflict_When_OneFamilyHasNoActiveVersion() {
		// Arrange
		List<VwProcessLib> byCaption = [
			VersionRow("UsrOrder_Approve", "Approval", 0, isActiveVersion: false, family: FamilyA),
			VersionRow("UsrOrder_ApproveCustom1", "Approval", 1, isActiveVersion: null, family: FamilyA)
		];

		// Act
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("Approval", null, byCaption);

		// Assert
		result.IsError.Should().BeTrue(because: "there is no active version to narrow to, so the caller must pick a code");
		result.FirstError.Description.Should().Contain("established no active version",
			because: "the refusal names the shape that blocked it, which is what tells the caller what to do next");
	}

	[Test]
	[Description("Refuses a caption whose ONLY match the library reports is not the active version, because the surfaces that resolve captions carry no version fields to reveal it.")]
	public void Resolve_Should_Return_Conflict_When_TheOnlyMatchIsExplicitlyNotActive() {
		// Arrange - the family's active member was renamed away from this caption, so only the inactive root
		// still carries it.
		List<VwProcessLib> byCaption = [
			VersionRow("UsrOrder_Approve", "Approval", 0, isActiveVersion: false, family: FamilyA)
		];

		// Act
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("Approval", null, byCaption);

		// Assert
		result.IsError.Should().BeTrue(
			because: "get-process-signature, generate-process-model and run-process would otherwise answer for a graph nobody runs, with nothing in their output to say so");
		result.FirstError.Description.Should().Contain("is NOT the active version",
			because: "the shipped help promises a caption resolves to the active version, so the refusal has to say why it could not");
	}

}
