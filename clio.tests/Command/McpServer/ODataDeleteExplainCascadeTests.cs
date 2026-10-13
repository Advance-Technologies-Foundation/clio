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
		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
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
		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
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
		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
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
		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
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
		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
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
		ODataWriteResponse response = tool.Delete(new ODataDeleteArgs {
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
