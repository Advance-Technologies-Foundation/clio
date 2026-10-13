using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.McpServer.Tools;
using Clio.Command.RuntimeVerification;
using Clio.Common;
using Clio.Common.EntitySchema;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>ENG-103024: odata-delete explains every refusal, deletes batches, and cascades owned children.</summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ODataDeleteExplainCascadeTests {

	private const string ActivityId = "72f65c41-3de6-4fdc-a185-1a99d347aa79";
	private const string ParticipantId = "a1798c77-a0e1-4918-923f-fb1c0c0d022a";
	private const string AccountId = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";
	private const string ContactId = "16474501-adcc-45cb-8dc4-484c1dbf779b";

	private const string ProcessRefusal =
		"{\"error\":{\"code\":\"\",\"message\":\"An error has occurred.\",\"innererror\":{\"message\":\"Item cannot be deleted because it is being used in a process.\",\"type\":\"\",\"stacktrace\":\"\"}}}";

	private static (ODataDeleteTool Tool, IRecordDeletionService Service) CreateTool() {
		IRecordDeletionService service = Substitute.For<IRecordDeletionService>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IRecordDeletionService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		return (new ODataDeleteTool(resolver, new OperationCorrelationIdProvider()), service);
	}

	private static RecordDeleteDiagnosis Diagnosis(bool? visible = true, string processElementId = null,
		params ReferencingRows[] referencing) {
		RecordDeleteDiagnosis diagnosis = new() { Visible = visible, ProcessElementId = processElementId };
		diagnosis.Referencing.AddRange(referencing);
		return diagnosis;
	}

	private static RecordDeleteOutcome Refused(string detail = null) =>
		new(false, "An error has occurred.", detail, true, true);

	private static RecordDeleteOutcome Deleted() => new(true, null, null, true, true);

	// A confirmed cascade the way the contract asks for it: a dry run first, then the call with its plan hash.
	private static ODataWriteResponse CascadeConfirmed(ODataDeleteTool tool, ODataDeleteArgs args) {
		ODataWriteResponse dryRun = tool.Delete(args with { DryRun = true, Confirm = false });
		return tool.Delete(args with { CascadePlanHash = dryRun.CascadePlanHash });
	}

	[Test]
	[Description("The opaque \"An error has occurred.\" refusal of a process-bound activity is explained with the server's inner reason and the ProcessElementId binding, and how to free it.")]
	public void Delete_Should_Explain_A_Record_Held_By_A_Running_Process() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		service.Delete("Activity", ActivityId, Arg.Any<int>())
			.Returns(Refused("[untrusted-source-text begin] Item cannot be deleted because it is being used in a process. [untrusted-source-text end]"));
		service.Diagnose("Activity", ActivityId, Arg.Any<int>(), Arg.Any<DateTimeOffset>())
			.Returns(Diagnosis(processElementId: "b623d2cd-629e-4d63-9d27-ca4c6b8ffe37",
				referencing: new ReferencingRows("ActivityParticipant", "Activity", [ParticipantId], false, ReferencingRows.CascadeKind)));

		// Act
		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Activity", Id = ActivityId, Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse(because: "the server refused the delete");
		response.Explanation.Should().Contain("being used in a process",
			because: "the server's inner message is the real reason and must not be hidden behind the headline");
		response.Explanation.Should().Contain("b623d2cd-629e-4d63-9d27-ca4c6b8ffe37",
			because: "the process element holding the record is named");
		response.Explanation.Should().Contain("cancel that run",
			because: "the explanation says how to free the record");
		response.Explanation.Should().NotContain("ActivityParticipant.Activity",
			because: "a cascade reference does not block the delete and must not be blamed");
	}

	[Test]
	[Description("A refusal with no server reason and no referencing rows is still explained: the remaining causes are rights or an event handler.")]
	public void Delete_Should_Explain_A_Refusal_With_No_Detail() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		service.Delete("Activity", ActivityId, Arg.Any<int>()).Returns(Refused());
		service.Diagnose("Activity", ActivityId, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis());

		// Act
		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Activity", Id = ActivityId, Confirm = true
		});

		// Assert
		response.Explanation.Should().Contain("delete rights",
			because: "with nothing else found, the explanation names the causes that remain");
	}

	[Test]
	[Description("A refusal caused by rows that still reference the record names each (entity, column, count, kind) and points at dry-run and cascade.")]
	public void Delete_Should_List_The_Rows_That_Still_Reference_The_Record() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		service.Delete("Account", AccountId, Arg.Any<int>()).Returns(Refused());
		ReferencingRows contacts = new("Contact", "Account", [ContactId], false, ReferencingRows.LinkedKind);
		service.Diagnose("Account", AccountId, Arg.Any<int>(), Arg.Any<DateTimeOffset>())
			.Returns(Diagnosis(referencing: contacts));

		// Act
		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Account", Id = AccountId, Confirm = true
		});

		// Assert
		response.Referencing.Should().Equal([contacts], because: "the blocking rows are returned as data");
		response.Explanation.Should().Contain("Contact.Account x1 (linked)",
			because: "the explanation names where the blocking rows are");
		response.Explanation.Should().Contain("dry-run: true", because: "the next safe step is a dry run");
	}

	[Test]
	[Description("dry-run lists what cascade would delete - owned children, deepest first - and what blocks the delete, needs no confirm, and deletes nothing.")]
	public void DryRun_Should_Plan_Without_Deleting() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		ReferencingRows participant = new("ActivityParticipant", "Activity", [ParticipantId], false, ReferencingRows.OwnedKind);
		service.Diagnose("Activity", ActivityId, Arg.Any<int>(), Arg.Any<DateTimeOffset>())
			.Returns(Diagnosis(referencing: participant));
		service.Diagnose("ActivityParticipant", ParticipantId, Arg.Any<int>(), Arg.Any<DateTimeOffset>())
			.Returns(Diagnosis());

		// Act
		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Activity", Id = ActivityId, Cascade = true, DryRun = true
		});

		// Assert
		response.Success.Should().BeTrue(because: "a plan was built");
		response.Id.Should().BeNull(because: "a dry run deleted nothing, so no id may read like a deletion");
		response.Plan.Should().ContainSingle(because: "one id was asked about")
			.Which.CascadeWouldDelete.Should().Equal([new RecordKey("ActivityParticipant", ParticipantId)],
				because: "the owned participant is what cascade would delete");
		service.DidNotReceiveWithAnyArgs().Delete(default, default, default);
	}

	[Test]
	[Description("cascade deletes the owned children before the record.")]
	public void Cascade_Should_Delete_Owned_Children_First() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		ReferencingRows participant = new("ActivityParticipant", "Activity", [ParticipantId], false, ReferencingRows.OwnedKind);
		service.Diagnose("Activity", ActivityId, Arg.Any<int>(), Arg.Any<DateTimeOffset>())
			.Returns(Diagnosis(referencing: participant));
		service.Diagnose("ActivityParticipant", ParticipantId, Arg.Any<int>(), Arg.Any<DateTimeOffset>())
			.Returns(Diagnosis());
		service.Delete(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>()).Returns(Deleted());

		// Act
		ODataWriteResponse response = CascadeConfirmed(tool, new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Activity", Id = ActivityId, Cascade = true, Confirm = true
		});

		// Assert
		response.Success.Should().BeTrue(because: "the child and the record were deleted");
		Received.InOrder(() => {
			service.Delete("ActivityParticipant", ParticipantId, Arg.Any<int>());
			service.Delete("Activity", ActivityId, Arg.Any<int>());
		});
		response.CascadeDeleted.Should().Equal([new RecordKey("ActivityParticipant", ParticipantId)],
			because: "the answer lists what cascade removed");
	}

	[Test]
	[Description("cascade checks the whole tree before deleting anything: a linked row that would still block the record refuses the cascade, and no owned child is deleted.")]
	public void Cascade_Should_Delete_Nothing_When_A_Linked_Row_Would_Still_Block() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		ReferencingRows participant = new("ActivityParticipant", "Activity", [ParticipantId], false, ReferencingRows.OwnedKind);
		ReferencingRows linked = new("Case", "ParentActivity", [ContactId], false, ReferencingRows.LinkedKind);
		service.Diagnose("Activity", ActivityId, Arg.Any<int>(), Arg.Any<DateTimeOffset>())
			.Returns(Diagnosis(referencing: [participant, linked]));

		// Act
		ODataWriteResponse response = CascadeConfirmed(tool, new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Activity", Id = ActivityId, Cascade = true, Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse(because: "the linked Case would still block the activity");
		response.Error.Should().Contain("Case.ParentActivity", because: "the refusal names the blocking link");
		service.DidNotReceiveWithAnyArgs().Delete(default, default, default);
	}

	[Test]
	[Description("cascade is refused outright for shared master data such as Contact, before any call.")]
	public void Cascade_Should_Be_Refused_For_Contact() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();

		// Act
		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = ContactId, Cascade = true, Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse(because: "rows requiring a contact are not owned by it");
		service.DidNotReceiveWithAnyArgs().Diagnose(default, default, default, default);
	}

	[Test]
	[Description("cascade refuses, deleting nothing, when the referencing tables could not all be checked: the owned rows are not known for certain.")]
	public void Cascade_Should_Refuse_When_The_Scan_Is_Incomplete() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		RecordDeleteDiagnosis partial = Diagnosis();
		partial.Complete = false;
		service.Diagnose("Activity", ActivityId, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(partial);

		// Act
		ODataWriteResponse response = CascadeConfirmed(tool, new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Activity", Id = ActivityId, Cascade = true, Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse(because: "an unknown set of children is not deleted blind");
		response.Error.Should().Contain("cascade refused", because: "the refusal says why");
		service.DidNotReceiveWithAnyArgs().Delete(default, default, default);
	}

	[Test]
	[Description("An owned row at the deepest cascade level is checked like every other: one held by a running process refuses the cascade before anything is deleted.")]
	public void Cascade_Should_Check_A_Leaf_At_The_Depth_Limit() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		string level2 = Guid.NewGuid().ToString();
		string level3 = Guid.NewGuid().ToString();
		service.Diagnose("UsrRoot", AccountId, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis(
			referencing: new ReferencingRows("UsrLevel1", "Root", [ParticipantId], false, ReferencingRows.OwnedKind)));
		service.Diagnose("UsrLevel1", ParticipantId, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis(
			referencing: new ReferencingRows("UsrLevel2", "Parent", [level2], false, ReferencingRows.OwnedKind)));
		service.Diagnose("UsrLevel2", level2, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis(
			referencing: new ReferencingRows("Activity", "UsrLevel2", [level3], false, ReferencingRows.OwnedKind)));
		service.Diagnose("Activity", level3, Arg.Any<int>(), Arg.Any<DateTimeOffset>())
			.Returns(Diagnosis(processElementId: "b623d2cd-629e-4d63-9d27-ca4c6b8ffe37"));

		// Act
		ODataWriteResponse response = CascadeConfirmed(tool, new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "UsrRoot", Id = AccountId, Cascade = true, Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse(because: "the deepest owned activity is held by a running process");
		response.Error.Should().Contain("live process element", because: "the refusal names the blocker");
		service.DidNotReceiveWithAnyArgs().Delete(default, default, default);
	}

	[Test]
	[Description("Shared master data is never deleted as an owned row: a Contact whose required lookup points at the record refuses the cascade.")]
	public void Cascade_Should_Refuse_To_Delete_A_Contact_As_An_Owned_Row() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		service.Diagnose("UsrProject", AccountId, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis(
			referencing: new ReferencingRows("Contact", "UsrProject", [ContactId], false, ReferencingRows.OwnedKind)));

		// Act
		ODataWriteResponse response = CascadeConfirmed(tool, new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "UsrProject", Id = AccountId, Cascade = true, Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse(because: "cascade never deletes a Contact");
		response.Error.Should().Contain("never deletes Contact", because: "the refusal says why");
		service.DidNotReceiveWithAnyArgs().Delete(default, default, default);
	}

	[Test]
	[Description("A row the platform deletes with the record is checked too: an owned row still referencing it would make the record's DELETE fail after the owned children were gone, so the cascade is refused first.")]
	public void Cascade_Should_Refuse_When_A_Platform_Cascade_Row_Is_Still_Referenced() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		string owned = Guid.NewGuid().ToString();
		string blocker = Guid.NewGuid().ToString();
		service.Diagnose("Activity", ActivityId, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis(
			referencing: [
				new ReferencingRows("ActivityParticipant", "Activity", [ParticipantId], false, ReferencingRows.CascadeKind),
				new ReferencingRows("UsrNote", "Activity", [owned], false, ReferencingRows.OwnedKind)
			]));
		service.Diagnose("ActivityParticipant", ParticipantId, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis(
			referencing: new ReferencingRows("UsrLink", "Participant", [blocker], false, ReferencingRows.OwnedKind)));
		service.Diagnose("UsrNote", owned, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis());

		// Act
		ODataWriteResponse response = CascadeConfirmed(tool, new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Activity", Id = ActivityId, Cascade = true, Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse(because: "the platform's own cascade would fail on UsrLink");
		response.Error.Should().Contain("ActivityParticipant", because: "the refusal names the platform-cascade row");
		service.DidNotReceiveWithAnyArgs().Delete(default, default, default);
	}

	[Test]
	[Description("id with an empty ids array still answers in the single-record shape, so explanation is at the top level.")]
	public void Delete_Should_Answer_A_Single_Id_With_Empty_Ids_As_A_Single() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		service.Delete("Activity", ActivityId, Arg.Any<int>()).Returns(Refused());
		service.Diagnose("Activity", ActivityId, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis());

		// Act
		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Activity", Id = ActivityId, Ids = [], Confirm = true
		});

		// Assert
		response.Explanation.Should().NotBeNullOrWhiteSpace(because: "a single id is answered at the top level");
		response.Results.Should().BeNull(because: "the batch shape is only for ids");
	}

	[Test]
	[Description("ids deletes each record and reports each outcome; one refusal does not stop the others.")]
	public void Ids_Should_Report_Each_Outcome() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		service.Delete("Contact", ContactId, Arg.Any<int>()).Returns(Deleted());
		service.Delete("Contact", AccountId, Arg.Any<int>()).Returns(Refused());
		service.Diagnose("Contact", AccountId, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis(visible: false));

		// Act
		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Contact", Ids = [ContactId, AccountId], Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse(because: "one of the two was refused");
		response.Results.Should().HaveCount(2, because: "every id gets an outcome");
		response.Results!.Single(result => result.Id == ContactId).Success.Should().BeTrue(because: "it was deleted");
		response.Results!.Single(result => result.Id == AccountId).Explanation.Should().Contain("not visible",
			because: "a record the user cannot see is explained as such");
	}

	[Test]
	[Description("id and ids together, more than 50 ids, or a non-GUID in ids are refused before any remote call.")]
	[TestCase(true, 1)]
	[TestCase(false, 51)]
	public void Ids_Should_Be_Validated_Before_Any_Call(bool alsoId, int count) {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		string[] ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid().ToString()).ToArray();

		// Act
		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Contact", Id = alsoId ? ContactId : null, Ids = ids, Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse(because: "the targets are outside the contract");
		service.DidNotReceiveWithAnyArgs().Delete(default, default, default);
	}

	[Test]
	[Description("cascade is refused for a lookup (dictionary) record before anything is read: rows that require a lookup value only refer to it (review: ActivityStatus would have taken its activities with it).")]
	public void Cascade_Should_Be_Refused_For_A_Lookup_Target() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		service.IsLookup("ActivityStatus", Arg.Any<int>()).Returns(true);

		// Act
		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "ActivityStatus", Id = AccountId, Cascade = true, DryRun = true
		});

		// Assert
		response.Success.Should().BeFalse(because: "a dictionary value owns nothing");
		response.Error.Should().Contain("lookup (dictionary)", because: "the refusal says why");
		service.DidNotReceiveWithAnyArgs().Diagnose(default, default, default, default);
		service.DidNotReceiveWithAnyArgs().Delete(default, default, default);
	}

	[Test]
	[Description("A lookup row reached below the record refuses the cascade when rows require it: they are not owned by it.")]
	public void Cascade_Should_Refuse_A_Lookup_Node_With_Owned_Rows() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		service.IsLookup("UsrKind", Arg.Any<int>()).Returns(true);
		service.Diagnose("UsrRoot", AccountId, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis(
			referencing: new ReferencingRows("UsrKind", "Root", [ParticipantId], false, ReferencingRows.OwnedKind)));
		service.Diagnose("UsrKind", ParticipantId, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis(
			referencing: new ReferencingRows("Activity", "UsrKind", [ActivityId], false, ReferencingRows.OwnedKind)));

		// Act
		ODataWriteResponse response = CascadeConfirmed(tool, new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "UsrRoot", Id = AccountId, Cascade = true, Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse(because: "the activity requiring the lookup value is not owned by it");
		response.Error.Should().Contain("UsrKind").And.Contain("lookup (dictionary)", because: "the refusal names the node");
		service.DidNotReceiveWithAnyArgs().Delete(default, default, default);
	}

	[Test]
	[Description("A confirmed cascade without the dry run's cascade-plan-hash is refused before anything is deleted.")]
	public void Cascade_Should_Require_The_Plan_Hash() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		service.Diagnose(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis());

		// Act
		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Activity", Id = ActivityId, Cascade = true, Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse(because: "the approval is not bound to a plan");
		response.Error.Should().Contain("cascade-plan-hash", because: "the refusal says what is missing");
		service.DidNotReceiveWithAnyArgs().Delete(default, default, default);
	}

	[Test]
	[Description("An owned row that appeared after the dry run changes the plan, so the confirmed cascade is refused and nothing is deleted.")]
	public void Cascade_Should_Refuse_When_The_Plan_Changed_Since_The_Dry_Run() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		string newcomer = Guid.NewGuid().ToString();
		service.Diagnose("Activity", ActivityId, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(
			Diagnosis(referencing: new ReferencingRows("UsrNote", "Activity", [ParticipantId], false, ReferencingRows.OwnedKind)),
			Diagnosis(referencing: new ReferencingRows("UsrNote", "Activity", [ParticipantId, newcomer], false,
				ReferencingRows.OwnedKind)));
		service.Diagnose("UsrNote", Arg.Any<string>(), Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis());

		// Act
		ODataWriteResponse response = CascadeConfirmed(tool, new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Activity", Id = ActivityId, Cascade = true, Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse(because: "the user never saw the second note");
		response.Error.Should().Contain("2 owned row(s)", because: "the refusal says what the plan is now");
		service.DidNotReceiveWithAnyArgs().Delete(default, default, default);
	}

	[Test]
	[Description("A row at the depth limit is checked for rows the platform deletes with it, and the dry run lists such rows under platform-cascade.")]
	public void Cascade_Should_Check_And_List_Platform_Cascade_Rows() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		string level2 = Guid.NewGuid().ToString();
		string level3 = Guid.NewGuid().ToString();
		string dependent = Guid.NewGuid().ToString();
		service.Diagnose("UsrRoot", AccountId, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis(
			referencing: new ReferencingRows("UsrLevel1", "Root", [ParticipantId], false, ReferencingRows.OwnedKind)));
		service.Diagnose("UsrLevel1", ParticipantId, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis(
			referencing: new ReferencingRows("UsrLevel2", "Parent", [level2], false, ReferencingRows.OwnedKind)));
		service.Diagnose("UsrLevel2", level2, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis(
			referencing: new ReferencingRows("UsrLevel3", "Parent", [level3], false, ReferencingRows.OwnedKind)));
		service.Diagnose("UsrLevel3", level3, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis(
			referencing: new ReferencingRows("UsrPart", "Parent", [dependent], false, ReferencingRows.CascadeKind)));
		service.Diagnose("UsrPart", dependent, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis());

		// Act
		ODataWriteResponse plan = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "UsrRoot", Id = AccountId, Cascade = true, DryRun = true
		});

		// Assert
		service.Received().Diagnose("UsrPart", dependent, Arg.Any<int>(), Arg.Any<DateTimeOffset>());
		plan.Plan!.Single().PlatformCascade.Should().Equal([new RecordKey("UsrPart", dependent)],
			because: "a dry run shows what disappears with the leaf, not only what clio deletes");
		plan.CascadePlanHash.Should().NotBeNullOrWhiteSpace(because: "the confirm is bound to this plan");
		plan.Note.Should().Contain("record-level rights", because: "the dry run says what the check cannot see");
	}

	[Test]
	[Description("An owned row whose DELETE got no answer is reported as unconfirmed - it may be gone - and not as not deleted.")]
	public void Cascade_Should_Report_An_Unanswered_Child_Delete_As_Unconfirmed() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		service.Diagnose("Activity", ActivityId, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(
			Diagnosis(referencing: new ReferencingRows("UsrNote", "Activity", [ParticipantId], false, ReferencingRows.OwnedKind)));
		service.Diagnose("UsrNote", ParticipantId, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis());
		service.Delete("UsrNote", ParticipantId, Arg.Any<int>()).Returns(new RecordDeleteOutcome(false, "timed out", null, true, false));

		// Act
		ODataWriteResponse response = CascadeConfirmed(tool, new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Activity", Id = ActivityId, Cascade = true, Confirm = true
		});

		// Assert
		response.Error.Should().Contain("may have been deleted", because: "an unanswered DELETE may have landed");
		response.CascadeUnconfirmed.Should().Equal([new RecordKey("UsrNote", ParticipantId)],
			because: "the row is listed apart from the confirmed deletions");
		service.DidNotReceive().Delete("Activity", ActivityId, Arg.Any<int>());
	}

	[Test]
	[Description("ids listing a parent before its child: the parent refused while the child exists is tried again after the child went, so one call deletes both.")]
	public void Ids_Should_Retry_A_Record_Refused_Because_Of_Another_Of_The_Batch() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		service.Delete("Account", AccountId, Arg.Any<int>()).Returns(Refused(), Deleted());
		service.Delete("Account", ContactId, Arg.Any<int>()).Returns(Deleted());

		// Act
		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Account", Ids = [AccountId, ContactId], Confirm = true
		});

		// Assert
		response.Success.Should().BeTrue(because: "the second pass deleted the parent");
		service.DidNotReceiveWithAnyArgs().Diagnose(default, default, default, default);
	}

	[Test]
	[Description("cascade over ids: an id its predecessor's cascade already deleted is reported deleted, not failed.")]
	public void Cascade_Should_Report_An_Id_Removed_By_An_Earlier_Cascade_As_Deleted() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		service.Diagnose("UsrTask", AccountId, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(
			Diagnosis(referencing: new ReferencingRows("UsrTask", "Parent", [ContactId], false, ReferencingRows.OwnedKind)));
		service.Diagnose("UsrTask", ContactId, Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(Diagnosis());
		service.Delete(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>()).Returns(Deleted());

		// Act
		ODataWriteResponse response = CascadeConfirmed(tool, new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "UsrTask", Ids = [AccountId, ContactId], Cascade = true, Confirm = true
		});

		// Assert
		response.Success.Should().BeTrue(because: "both records are gone");
		response.Results!.Single(result => result.Id == ContactId).Explanation.Should().Contain("earlier id's cascade",
			because: "the caller is told why the second id needed no DELETE of its own");
		service.Received(1).Delete("UsrTask", ContactId, Arg.Any<int>());
	}

	[Test]
	[Description("QA D2: an id that does not exist is reported not found - single and batch alike - and no DELETE is sent for it.")]
	public void Delete_Should_Report_A_Missing_Record_As_Not_Found() {
		// Arrange
		(ODataDeleteTool tool, IRecordDeletionService service) = CreateTool();
		service.Exists("Activity", ActivityId, Arg.Any<int>()).Returns(false);
		service.Exists("Activity", ParticipantId, Arg.Any<int>()).Returns(true);
		service.Delete("Activity", ParticipantId, Arg.Any<int>()).Returns(Deleted());

		// Act
		ODataWriteResponse single = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Activity", Id = ActivityId, Confirm = true
		});
		ODataWriteResponse batch = tool.Delete(new ODataDeleteArgs {
			EnvironmentName = "dev", Entity = "Activity", Ids = [ActivityId, ParticipantId], Confirm = true
		});

		// Assert
		single.Success.Should().BeFalse(because: "nothing was deleted");
		single.Error.Should().Contain("was not found", because: "the answer matches what a dry run says");
		batch.Results!.Single(result => result.Id == ActivityId).Error.Should().Contain("was not found",
			because: "the batch answers the same way as the single");
		batch.Results!.Single(result => result.Id == ParticipantId).Success.Should().BeTrue(because: "it existed");
		service.DidNotReceive().Delete("Activity", ActivityId, Arg.Any<int>());
	}
}

/// <summary>ENG-103024: the diagnosis reads referencing rows through the metadata and classifies them.</summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class RecordDeletionServiceTests {

	private const string SelectUrl = "http://sandbox/0/DataService/json/SyncReply/SelectQuery";
	private const string ActivityId = "72f65c41-3de6-4fdc-a185-1a99d347aa79";

	private static RuntimeEntitySchemaResult Schema(string name, bool view = false,
		params RuntimeEntitySchemaColumnResult[] columns) =>
		new(Guid.NewGuid(), name, Guid.NewGuid(), null, null, columns, IsDBView: view);

	private static RuntimeEntitySchemaColumnResult Lookup(string name, string reference, bool required, bool cascade) =>
		new(Guid.NewGuid(), name, null, null, 10, required, false, reference, IsCascade: cascade);

	[Test]
	[Description("Referencing rows are found through SysEntitySchemaReference, audit columns and views are skipped, and each hit is classified cascade, owned or linked from the referencing column.")]
	public void Diagnose_Should_Classify_Referencing_Rows() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => {
				JObject query = JObject.Parse(call.ArgAt<string>(1));
				string root = query.Value<string>("rootSchemaName");
				string rows = root switch {
					"Activity" => $"[{{\"Id\":\"{ActivityId}\",\"ProcessElementId\":\"\"}}]",
					"SysEntitySchemaReference" => "[{\"SchemaName\":\"ActivityParticipant\",\"ColumnName\":\"Activity\"},"
						+ "{\"SchemaName\":\"ActivityParticipant\",\"ColumnName\":\"Activity\"},"
						+ "{\"SchemaName\":\"Case\",\"ColumnName\":\"ParentActivity\"},"
						+ "{\"SchemaName\":\"Call\",\"ColumnName\":\"Activity\"},"
						+ "{\"SchemaName\":\"VwLastActivity\",\"ColumnName\":\"Activity\"},"
						+ "{\"SchemaName\":\"Contact\",\"ColumnName\":\"CreatedBy\"}]",
					_ => "[{\"Id\":\"a1798c77-a0e1-4918-923f-fb1c0c0d022a\"}]"
				};
				return "{\"success\":true,\"rows\":" + rows + "}";
			});
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		IRuntimeEntitySchemaReader schemas = Substitute.For<IRuntimeEntitySchemaReader>();
		schemas.GetByName("Activity", Arg.Any<int>()).Returns(Schema("Activity", false,
			new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "ProcessElementId", null, null, 0, false, false, null)));
		schemas.GetByName("ActivityParticipant", Arg.Any<int>())
			.Returns(Schema("ActivityParticipant", false, Lookup("Activity", "Activity", true, true)));
		schemas.GetByName("Case", Arg.Any<int>()).Returns(Schema("Case", false, Lookup("ParentActivity", "Activity", false, false)));
		schemas.GetByName("Call", Arg.Any<int>()).Returns(Schema("Call", false, Lookup("Activity", "Activity", true, false)));
		schemas.GetByName("VwLastActivity", Arg.Any<int>()).Returns(Schema("VwLastActivity", true));
		RecordDeletionService service = new(client, urls, schemas);

		// Act
		RecordDeleteDiagnosis diagnosis = service.Diagnose("Activity", ActivityId, 10_000, DateTimeOffset.UtcNow.AddMinutes(1));

		// Assert
		diagnosis.Visible.Should().BeTrue(because: "the record's own row came back");
		diagnosis.Referencing.Select(rows => (rows.Entity, rows.Column, rows.Kind)).Should().Equal(
			[("ActivityParticipant", "Activity", "cascade"), ("Call", "Activity", "owned"), ("Case", "ParentActivity", "linked")],
			because: "each referencing column is classified once, views and audit columns excluded");
		diagnosis.Complete.Should().BeTrue(because: "every referencing column was checked");
		client.DidNotReceive().ExecutePostRequest(SelectUrl,
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"Contact\"")), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Two referencing columns of one entity are read in one select with an OR filter, and each hit is attributed to its column.")]
	public void Diagnose_Should_Read_One_Entity_In_One_Select() {
		// Arrange
		List<JObject> queries = [];
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => {
				JObject query = JObject.Parse(call.ArgAt<string>(1));
				queries.Add(query);
				string rows = query.Value<string>("rootSchemaName") switch {
					"Activity" => $"[{{\"Id\":\"{ActivityId}\"}}]",
					"SysEntitySchemaReference" => "[{\"SchemaName\":\"Case\",\"ColumnName\":\"ParentActivity\"},"
						+ "{\"SchemaName\":\"Case\",\"ColumnName\":\"OriginActivity\"}]",
					_ => $"[{{\"Id\":\"a1798c77-a0e1-4918-923f-fb1c0c0d022a\",\"ParentActivity\":{{\"value\":\"{ActivityId}\"}},"
						+ "\"OriginActivity\":null}]"
				};
				return "{\"success\":true,\"rows\":" + rows + "}";
			});
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		IRuntimeEntitySchemaReader schemas = Substitute.For<IRuntimeEntitySchemaReader>();
		schemas.GetByName("Case", Arg.Any<int>()).Returns(Schema("Case", false,
			Lookup("ParentActivity", "Activity", false, false), Lookup("OriginActivity", "Activity", false, false)));
		RecordDeletionService service = new(client, urls, schemas);

		// Act
		RecordDeleteDiagnosis diagnosis = service.Diagnose("Activity", ActivityId, 10_000, DateTimeOffset.UtcNow.AddMinutes(1));

		// Assert
		queries.Count(query => query.Value<string>("rootSchemaName") == "Case").Should().Be(1,
			because: "the columns of one referencing entity share one select");
		diagnosis.Referencing.Select(rows => rows.Column).Should().Equal(["ParentActivity"],
			because: "only the column that points at the record is reported");
	}

	[Test]
	[Description("A diagnosis whose stop instant has passed sends no request and is incomplete.")]
	public void Diagnose_Should_Send_Nothing_Past_Its_Stop() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		RecordDeletionService service = new(client, Substitute.For<IServiceUrlBuilder>(),
			Substitute.For<IRuntimeEntitySchemaReader>());

		// Act
		RecordDeleteDiagnosis diagnosis = service.Diagnose("Activity", ActivityId, 10_000, DateTimeOffset.UtcNow.AddSeconds(-1));

		// Assert
		diagnosis.Complete.Should().BeFalse(because: "nothing was checked");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default, default, default, default);
	}

	[Test]
	[Description("An entity registered in SysLookup is a lookup; one that is neither registered nor derived from a base lookup is not; one whose schema cannot be read counts as a lookup.")]
	public void IsLookup_Should_Read_SysLookup_And_The_Parent_Schema() {
		// Arrange
		Guid statusUId = Guid.NewGuid();
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => call.ArgAt<string>(1).Contains(statusUId.ToString("D"))
				? "{\"success\":true,\"rows\":[{\"Id\":\"x\"}]}"
				: "{\"success\":true,\"rows\":[]}");
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		IRuntimeEntitySchemaReader schemas = Substitute.For<IRuntimeEntitySchemaReader>();
		schemas.GetByName("ActivityStatus", Arg.Any<int>())
			.Returns(new RuntimeEntitySchemaResult(statusUId, "ActivityStatus", Guid.NewGuid(), null, null, []));
		schemas.GetByName("Activity", Arg.Any<int>()).Returns(Schema("Activity"));
		schemas.GetByName("Missing", Arg.Any<int>()).Returns(_ => throw new InvalidOperationException("no schema"));
		RecordDeletionService service = new(client, urls, schemas);

		// Act / Assert
		service.IsLookup("ActivityStatus", 10_000).Should().BeTrue(because: "it is registered in SysLookup");
		service.IsLookup("Activity", 10_000).Should().BeFalse(because: "it is a business entity");
		service.IsLookup("Missing", 10_000).Should().BeTrue(because: "not knowing must not allow a cascade");
	}

	[Test]
	[Description("A lookup derived through an intermediate schema (UsrBaseStatus -> BaseLookup) is a lookup; a chain that reaches BaseEntity without a base lookup is not; a chain that cannot be read counts as a lookup.")]
	public void IsLookup_Should_Walk_The_Parent_Chain_To_A_Base_Lookup() {
		// Arrange
		Guid statusUId = Guid.NewGuid();
		Guid midUId = Guid.NewGuid();
		Guid baseLookupUId = Guid.NewGuid();
		Guid plainUId = Guid.NewGuid();
		Guid plainMidUId = Guid.NewGuid();
		Guid baseEntityUId = Guid.NewGuid();
		Guid unreadableUId = Guid.NewGuid();
		Dictionary<Guid, string> names = new() {
			[midUId] = "UsrBaseStatus", [baseLookupUId] = "BaseLookup", [plainMidUId] = "UsrBasePlain",
			[baseEntityUId] = "BaseEntity", [unreadableUId] = "UsrUnreadable"
		};
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => {
				string body = call.ArgAt<string>(1);
				string hit = names.Where(pair => body.Contains(pair.Key.ToString("D"))).Select(pair => pair.Value).FirstOrDefault();
				return hit is null
					? "{\"success\":true,\"rows\":[]}"
					: "{\"success\":true,\"rows\":[{\"Name\":\"" + hit + "\"}]}";
			});
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		IRuntimeEntitySchemaReader schemas = Substitute.For<IRuntimeEntitySchemaReader>();
		schemas.GetByName("UsrStatus", Arg.Any<int>()).Returns(
			new RuntimeEntitySchemaResult(statusUId, "UsrStatus", Guid.NewGuid(), null, null, [], ParentUId: midUId));
		schemas.GetByName("UsrBaseStatus", Arg.Any<int>()).Returns(
			new RuntimeEntitySchemaResult(midUId, "UsrBaseStatus", Guid.NewGuid(), null, null, [], ParentUId: baseLookupUId));
		schemas.GetByName("UsrPlain", Arg.Any<int>()).Returns(
			new RuntimeEntitySchemaResult(Guid.NewGuid(), "UsrPlain", Guid.NewGuid(), null, null, [], ParentUId: plainMidUId));
		schemas.GetByName("UsrBasePlain", Arg.Any<int>()).Returns(
			new RuntimeEntitySchemaResult(plainMidUId, "UsrBasePlain", Guid.NewGuid(), null, null, [], ParentUId: baseEntityUId));
		schemas.GetByName("UsrBroken", Arg.Any<int>()).Returns(
			new RuntimeEntitySchemaResult(Guid.NewGuid(), "UsrBroken", Guid.NewGuid(), null, null, [], ParentUId: unreadableUId));
		schemas.GetByName("UsrUnreadable", Arg.Any<int>()).Returns(_ => throw new InvalidOperationException("no schema"));
		RecordDeletionService service = new(client, urls, schemas);

		// Act / Assert
		service.IsLookup("UsrStatus", 10_000).Should().BeTrue(because: "UsrStatus -> UsrBaseStatus -> BaseLookup");
		service.IsLookup("UsrPlain", 10_000).Should().BeFalse(because: "its chain ends at BaseEntity with no base lookup");
		service.IsLookup("UsrBroken", 10_000).Should().BeTrue(because: "a chain that cannot be read must not allow a cascade");
	}

	[Test]
	[Description("The cascade plan hash does not depend on the order the walk listed rows in, so an unchanged tree confirms with the hash its dry run gave.")]
	public void HashPlans_Should_Ignore_The_Order_Of_The_Walk() {
		// Arrange
		string id = Guid.NewGuid().ToString("D");
		RecordKey a = new("UsrChild", Guid.NewGuid().ToString("D"));
		RecordKey b = new("UsrChild", Guid.NewGuid().ToString("D"));
		RecordKey c = new("UsrGrand", Guid.NewGuid().ToString("D"));
		ODataDeleteTool.CascadePlan First(params RecordKey[] order) => new(order.ToList(), [], null, null) {
			PlatformCascade = order.Reverse().ToList()
		};

		// Act
		string one = ODataDeleteTool.HashPlans([(id, First(c, a, b))]);
		string other = ODataDeleteTool.HashPlans([(id, First(b, c, a))]);
		string changed = ODataDeleteTool.HashPlans([(id, First(c, a))]);

		// Assert
		other.Should().Be(one, because: "the same rows in another walk order are the same plan");
		changed.Should().NotBe(one, because: "a different set of rows is a different plan");
	}

	[Test]
	[Description("A truncated reference metadata list keeps every later diagnosis of the same entity incomplete, not only the first one that read it.")]
	public void Diagnose_Should_Stay_Incomplete_On_A_Cached_Truncated_Reference_List() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		string metadata = "[" + string.Join(",", Enumerable.Range(0, 5_000)
			.Select(i => $"{{\"SchemaName\":\"Usr{i}\",\"ColumnName\":\"Activity\"}}")) + "]";
		client.ExecutePostRequest(SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => {
				string root = JObject.Parse(call.ArgAt<string>(1)).Value<string>("rootSchemaName");
				return "{\"success\":true,\"rows\":" + (root switch {
					"Activity" => $"[{{\"Id\":\"{ActivityId}\"}}]",
					"SysEntitySchemaReference" => metadata,
					_ => "[]"
				}) + "}";
			});
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		IRuntimeEntitySchemaReader schemas = Substitute.For<IRuntimeEntitySchemaReader>();
		RecordDeletionService service = new(client, urls, schemas);

		// Act
		RecordDeleteDiagnosis first = service.Diagnose("Activity", ActivityId, 10_000, DateTimeOffset.UtcNow.AddMinutes(1));
		RecordDeleteDiagnosis second = service.Diagnose("Activity", ActivityId, 10_000, DateTimeOffset.UtcNow.AddMinutes(1));

		// Assert
		first.Complete.Should().BeFalse(because: "the metadata read hit its row cap");
		second.Complete.Should().BeFalse(because: "the cached list is just as truncated as the first read");
	}

	[Test]
	[Description("The inner message of an OData error body is returned fenced, first line only; a body without one gives null.")]
	public void ReadInnerMessage_Should_Return_The_Inner_Reason() {
		// Arrange
		const string body = "{\"error\":{\"code\":\"\",\"message\":\"An error has occurred.\",\"innererror\":{\"message\":\"Item cannot be deleted because it is being used in a process.\\r\\n at X\"}}}";

		// Act
		string inner = RecordDeletionService.ReadInnerMessage(body);

		// Assert
		inner.Should().Contain("Item cannot be deleted because it is being used in a process.",
			because: "that is the reason the headline hides");
		inner.Should().Contain("untrusted-source-text", because: "server text reaches an agent fenced");
		inner.Should().NotContain(" at X", because: "only the first line is kept");
		RecordDeletionService.ReadInnerMessage("{\"error\":{\"message\":\"x\"}}").Should().BeNull(
			because: "there is no inner message to report");
	}
}
