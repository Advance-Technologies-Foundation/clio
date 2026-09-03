using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ATF.Repository.Providers;
using Clio.Command.ProcessModel;
using Clio.Common;
using ErrorOr;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// HTTP-layer tests for <see cref="ServerProcessDescriber"/>: the wrapped <c>{"request":{name|uid}}</c> body, the
/// resolved DescribeProcess route, and each <see cref="ErrorOr{T}"/> branch (success / server-failure / empty /
/// unexpected shape / no identity). These exercise the actual clio→server contract, which the tool tests fake.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "ProcessModel")]
public sealed class ServerProcessDescriberTests {

	private const string DescribeUrl = "http://sandbox/0/rest/ProcessDesignService/DescribeProcess";

	private static ServerProcessDescriber CreateDescriber(IApplicationClient client) {
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.DescribeProcess).Returns(DescribeUrl);
		return new ServerProcessDescriber(client,
			Substitute.For<IDataProvider>(), urlBuilder);
	}

	private static IApplicationClient ClientReturning(string response) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(DescribeUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(response);
		return client;
	}

	[Test]
	[Description("Posts the process code wrapped under 'request.name' to the DescribeProcess route and returns the parsed graph on success.")]
	public void Describe_ShouldPostWrappedNameToDescribeRoute_AndReturnResult_OnSuccess() {
		// Arrange — the element carries its name (= local handle/Name) and uid (= element UId); both must survive.
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\",\"schemaUId\":\"5c58c4c4-134b-4744-9c67-96d9c69c9d55\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\"}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "a successful describe returns the graph, not an error");
		result.Value.Name.Should().Be("UsrProc", because: "the process name is read from the server result");
		result.Value.Elements[0].Name.Should().Be("task1", because: "the element local handle (Name) is read back");
		result.Value.Elements[0].Uid.Should().Be("a1b2c3d4-0000-0000-0000-000000000001",
			because: "the element UId must be surfaced, not dropped (PR #715 review)");
		client.Received(1).ExecutePostRequest(DescribeUrl,
			Arg.Is<string>(body => Wrapped(body)["name"].GetValue<string>() == "UsrProc"),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Deserializes an element parameter's direction and isResult from the server response into the DescribedParameter DTO (so callers can tell an element's outputs, mappable as a source, from its inputs).")]
	public void Describe_ShouldReadParameterDirectionAndIsResult_WhenServerReportsThem() {
		// Arrange — a user task whose parameter is an output (isResult true) while its direction is Variable
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\","
			+ "\"parameters\":[{\"name\":\"PResult\",\"uid\":\"p1\",\"type\":\"Guid\",\"direction\":\"Variable\",\"isResult\":true,\"source\":\"None\"}]}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedParameter parameter = result.Value.Elements[0].Parameters[0];
		parameter.Direction.Should().Be("Variable",
			because: "the parameter's direction must be read from the server, not dropped by the clio DTO");
		parameter.IsResult.Should().BeTrue(
			because: "isResult marks an element output usable as a mapping source and must be deserialized");
	}

	[Test]
	[Description("Deserializes a Lookup ConstValue's valueDisplay - the referenced record's NAME - into the DescribedParameter DTO, beside the unchanged bare-Guid value, so a caller can show a word without a second read.")]
	public void Describe_ShouldReadParameterValueDisplay_WhenServerReportsIt() {
		// Arrange - a Lookup constant the server resolved a name for (ENG-96325); value stays the bare record id
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\","
			+ "\"parameters\":[{\"name\":\"ActivityCategory\",\"uid\":\"p1\",\"type\":\"Lookup\",\"source\":\"ConstValue\","
			+ "\"value\":\"03df85bf-6b19-4dea-8463-d5d49b80bb28\",\"valueDisplay\":\"Call\"}]}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedParameter parameter = result.Value.Elements[0].Parameters[0];
		parameter.Value.Should().Be("03df85bf-6b19-4dea-8463-d5d49b80bb28",
			because: "the runtime encoding is the bare record Guid and the display name must not replace it");
		parameter.ValueDisplay.Should().Be("Call",
			because: "valueDisplay is what the designer renders; dropping it in the clio DTO reinstates the Guid the "
				+ "fix removed, and only the manual e2e suite would notice");
	}

	[Test]
	[Description("Leaves valueDisplay unset (null) when the server omits it - an older package, or a record whose name did not resolve - so the absent field serializes away instead of becoming an empty string.")]
	public void Describe_ShouldLeaveValueDisplayNull_WhenServerOmitsIt() {
		// Arrange - a pre-1.4.0.40 package: the Lookup constant is reported without a display name
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\","
			+ "\"parameters\":[{\"name\":\"ActivityCategory\",\"uid\":\"p1\",\"type\":\"Lookup\",\"source\":\"ConstValue\","
			+ "\"value\":\"03df85bf-6b19-4dea-8463-d5d49b80bb28\"}]}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "an older package omitting the field is not an error");
		result.Value.Elements[0].Parameters[0].ValueDisplay.Should().BeNull(
			because: "an omitted display name must stay null so it serializes away, rather than surfacing as an empty "
				+ "label a caller would render");
	}

	[Test]
	[Description("Leaves direction/isResult unset (null) when an older server omits them, so the absent fields serialize away cleanly.")]
	public void Describe_ShouldLeaveDirectionAndIsResultNull_WhenServerOmitsThem() {
		// Arrange — an older CrtProcessBuilder that does not report direction/isResult on parameters
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\","
			+ "\"parameters\":[{\"name\":\"PResult\",\"uid\":\"p1\",\"type\":\"Guid\",\"source\":\"None\"}]}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedParameter parameter = result.Value.Elements[0].Parameters[0];
		parameter.Direction.Should().BeNull(
			because: "an omitted direction stays null so it serializes away (WhenWritingNull) for older servers");
		parameter.IsResult.Should().BeNull(
			because: "an omitted isResult stays null rather than defaulting to false, avoiding a misleading output");
	}

	[Test]
	[Description("Deserializes an element's data source filter (object + logical operation + conditions + nested groups) from the server response into the DescribedFilter DTO, so describe read-back surfaces the filter instead of dropping it.")]
	public void Describe_ShouldReadElementFilter_WhenServerReportsIt() {
		// Arrange — a signal start whose decoded filter is Age > 30 AND (Address = 'x')
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"SignalStart1\",\"type\":\"ProcessSchemaStartSignalEvent\",\"buildType\":\"signalstart\","
			+ "\"filter\":{\"object\":\"Contact\",\"logicalOperation\":\"and\","
			+ "\"conditions\":[{\"column\":\"Age\",\"comparison\":\"greater\",\"value\":\"30\"}],"
			+ "\"groups\":[{\"logicalOperation\":\"or\",\"conditions\":[{\"column\":\"Address\",\"comparison\":\"equal\",\"value\":\"x\"}]}]}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedFilter filter = result.Value.Elements[0].Filter;
		filter.Should().NotBeNull(because: "the element's data source filter must be surfaced, not dropped by the clio DTO");
		filter.Object.Should().Be("Contact", because: "the filter's root object is read back");
		filter.LogicalOperation.Should().Be("and", because: "the root logical operation is read back");
		filter.Conditions.Should().ContainSingle(because: "the single root condition is deserialized");
		filter.Conditions[0].Column.Should().Be("Age", because: "the condition column round-trips");
		filter.Conditions[0].Comparison.Should().Be("greater", because: "the comparison round-trips");
		filter.Conditions[0].Value.Should().Be("30", because: "the constant value round-trips");
		filter.Groups.Should().ContainSingle(because: "the nested group is deserialized");
		filter.Groups[0].LogicalOperation.Should().Be("or", because: "the nested group operator round-trips");
		filter.Groups[0].Conditions[0].Column.Should().Be("Address", because: "the nested condition round-trips");
	}

	[Test]
	[Description("Deserializes the element-level useBackgroundMode flag reported for every element, and leaves it null when an older server omits it.")]
	public void Describe_ShouldReadElementBackgroundMode_WhenServerReportsIt() {
		// Arrange — one element reporting the flag, one (older-server shape) omitting it
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"SignalStart1\",\"type\":\"ProcessSchemaStartSignalEvent\",\"buildType\":\"signalstart\",\"useBackgroundMode\":true},"
			+ "{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000002\",\"name\":\"EndEvent1\",\"type\":\"ProcessSchemaTerminateEvent\",\"buildType\":\"endevent\"}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		result.Value.Elements[0].UseBackgroundMode.Should().BeTrue(
			because: "the element-level background-mode flag must be deserialized, not dropped by the clio DTO");
		result.Value.Elements[1].UseBackgroundMode.Should().BeNull(
			because: "an omitted flag stays null so it serializes away (WhenWritingNull) for an older server");
	}

	[Test]
	[Description("Deserializes a Send email element's email configuration (mode, sender, subject, hasBody, the decoded body, importance, ignoreErrors, recipients, manual-mode performer) from the server response into the DescribedEmail DTO, so describe read-back surfaces the email block instead of dropping it.")]
	public void Describe_ShouldReadSendEmailConfiguration_WhenServerReportsIt() {
		// Arrange — the shape a runtime-verified CrtProcessBuilder DescribeProcess returns for a configured element
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"SendEmail1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"sendemail\",\"userTaskName\":\"EmailTemplateUserTask\","
			+ "\"email\":{\"mode\":\"manual\",\"sender\":\"[#Lookup.5e487721-02e2-48ee-b755-dfa5160f5315.11111111-2222-3333-4444-555555555555#]\",\"senderDisplay\":\"sales@example.com\","
			+ "\"subject\":\"After modify\",\"hasBody\":true,\"body\":\"<p>Hi [[param:ContactName]]</p>\",\"importance\":\"high\",\"ignoreErrors\":true,"
			+ "\"to\":[{\"name\":\"Recipient1\",\"uid\":\"p1\",\"type\":\"MaxSizeText\",\"source\":\"ConstValue\",\"value\":\"to@example.com\"}],"
			+ "\"performer\":{\"type\":\"role\",\"role\":\"[#Lookup.84f44b9a-4bc3-4cbf-a1a8-cec02c1c029c.a29a3ba5-4b0d-de11-9a51-005056c00008#]\",\"roleDisplay\":\"All employees\",\"showPage\":true}}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedEmail email = result.Value.Elements[0].Email;
		email.Should().NotBeNull(because: "the email block must be deserialized, not dropped by the clio DTO");
		email.Mode.Should().Be("manual", because: "the send-mode token maps to the DTO's mode property");
		email.SenderDisplay.Should().Be("sales@example.com",
			because: "senderDisplay carries the human-readable mailbox identity alongside the sender formula");
		email.Subject.Should().Be("After modify", because: "the subject constant survives the read-back");
		email.HasBody.Should().BeTrue(
			because: "hasBody flags that a custom-message body is present on the element");
		email.Body.Should().Be("<p>Hi [[param:ContactName]]</p>",
			because: "the body field carries the decoded HTML (process-macro tokens back in [[param:…]] author form), "
				+ "so a dropped [JsonPropertyName(\"body\")] or a rename would surface here rather than silently");
		email.Importance.Should().Be("high", because: "the importance token maps to the DTO");
		email.IgnoreErrors.Should().BeTrue(because: "the ignore-sending-errors flag maps to the DTO");
		email.To.Should().ContainSingle(because: "the recipient list must survive read-back")
			.Which.Value.Should().Be("to@example.com",
				because: "the recipient's constant address is carried on the parameter's value");
		email.Performer.Type.Should().Be("role",
			because: "the manual-mode performer kind maps to the nested performer DTO");
		email.Performer.RoleDisplay.Should().Be("All employees",
			because: "roleDisplay carries the resolved role name for a human reader");
		email.Performer.ShowPage.Should().BeTrue(
			because: "the show-execution-page flag is part of the performer block");
	}

	[Test]
	[Description("Deserializes a Perform task's TOP-LEVEL performer block (kind, the stored role formula, the resolved role name, the show-page flag) into DescribedElement.Performer, so the team assignment survives read-back instead of being dropped by the clio DTO — the same failure class that once dropped four email fields.")]
	public void Describe_ShouldReadTopLevelPerformer_WhenServerReportsIt() {
		// Arrange — a Perform task carrying a role performer, as CrtProcessBuilder reports it
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000002\",\"name\":\"Task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\",\"userTaskName\":\"ActivityUserTask\","
			+ "\"performer\":{\"type\":\"role\",\"role\":\"[#Lookup.84f44b9a-4bc3-4cbf-a1a8-cec02c1c029c.a29a3ba5-4b0d-de11-9a51-005056c00008#]\",\"roleDisplay\":\"All employees\",\"showPage\":false}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedPerformer performer = result.Value.Elements[0].Performer;
		performer.Should().NotBeNull(
			because: "the top-level performer must be deserialized, not dropped — it is the only read-back of a "
				+ "team assignment, and a dropped member fails silently rather than loudly");
		performer.Type.Should().Be("role", because: "the performer kind maps to the DTO");
		performer.Role.Should().Contain("a29a3ba5-4b0d-de11-9a51-005056c00008",
			because: "the stored role formula is the re-appliable value create/modify accept back");
		performer.RoleDisplay.Should().Be("All employees",
			because: "roleDisplay carries the resolved role name for a human reader");
		performer.ShowPage.Should().BeFalse(
			because: "the show-execution-page flag is part of the block, and false is its designer-parity value "
				+ "for a role performer — reading it as null would lose a written value");
	}

	[Test]
	[Description("Leaves a Perform task's performer null when the server (an older CrtProcessBuilder, or an element with no assignment) reports no block, so absence stays absent instead of materializing an empty performer a caller could mistake for a configured one.")]
	public void Describe_ShouldLeavePerformerNull_WhenServerOmitsIt() {
		// Arrange
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000003\",\"name\":\"Task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\",\"userTaskName\":\"ActivityUserTask\"}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.Value.Elements[0].Performer.Should().BeNull(
			because: "no reported block means no assignment; inventing an empty one would read as configured");
	}

	[Test]
	[Description("Deserializes ALL TWENTY members of an Approval element's approval block from the server response into the DescribedApproval DTO. This block has no clio-side default and no partial mapping to fall back on: a member the DTO does not declare, or one whose [JsonPropertyName] drifts from the server's [DataMember], is dropped SILENTLY on re-serialize — so every name is asserted individually rather than by spot check.")]
	public void Describe_ShouldReadEveryApprovalMember_WhenServerReportsThem() {
		// Arrange — every member the server's ApprovalDescriptor can report, with distinguishable values
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"Approval1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"approval\",\"userTaskName\":\"ApprovalUserTask\","
			+ "\"approval\":{\"purpose\":\"Approve the order\",\"object\":\"Order\",\"objectUId\":\"1bbf2c48-6bef-4c65-a4f9-e6f27a7dd6cc\","
			+ "\"recordId\":\"[#Lookup.1bbf2c48-6bef-4c65-a4f9-e6f27a7dd6cc.22222222-3333-4444-5555-666666666666#]\",\"recordIdDisplay\":\"Order #42\","
			+ "\"approverType\":\"user\",\"approverEmployee\":\"[#Lookup.30da1e63-2ae1-4b62-9d5b-f9e14a0ec3a1.33333333-4444-5555-6666-777777777777#]\",\"approverEmployeeDisplay\":\"Anna Best\","
			+ "\"approverRole\":\"[#Lookup.1f424900-3d1a-4ffe-badd-a76e62ed952b.44444444-5555-6666-7777-888888888888#]\",\"approverRoleDisplay\":\"All employees\","
			+ "\"allowDelegation\":true,\"notifyApprover\":true,"
			+ "\"approverEmailTemplate\":\"[#Lookup.aaaaaaaa-0000-0000-0000-00000000000a.55555555-6666-7777-8888-999999999999#]\",\"approverEmailTemplateDisplay\":\"Approval requested\","
			+ "\"notifyAuthor\":true,\"authorEmailTemplate\":\"[#Lookup.aaaaaaaa-0000-0000-0000-00000000000a.66666666-7777-8888-9999-aaaaaaaaaaaa#]\",\"authorEmailTemplateDisplay\":\"Approval result\","
			+ "\"recipient\":\"ops@example.com\",\"ignoreEmailErrors\":false,"
			+ "\"approvalSchemaUId\":\"9800f45d-7d2e-44c7-85e5-053c06c8c2d4\"}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedApproval approval = result.Value.Elements[0].Approval;
		approval.Should().NotBeNull(because: "the approval block must be deserialized, not dropped by the clio DTO");
		approval.Purpose.Should().Be("Approve the order", because: "purpose maps to the DTO");
		approval.Object.Should().Be("Order", because: "the resubmittable object NAME maps to the DTO");
		approval.ObjectUId.Should().Be("1bbf2c48-6bef-4c65-a4f9-e6f27a7dd6cc",
			because: "objectUId is the stored identity behind that name");
		approval.RecordId.Should().Contain("22222222-3333-4444-5555-666666666666",
			because: "the record under approval maps to the DTO as its stored macro");
		approval.RecordIdDisplay.Should().Be("Order #42",
			because: "the Display companion is what makes the macro readable, and it drops just as silently");
		approval.ApproverType.Should().Be("user", because: "the approver type token maps to the DTO");
		approval.ApproverEmployee.Should().Contain("33333333-4444-5555-6666-777777777777",
			because: "the employee behind a user/manager approver maps to the DTO");
		approval.ApproverEmployeeDisplay.Should().Be("Anna Best", because: "its Display companion maps too");
		approval.ApproverRole.Should().Contain("44444444-5555-6666-7777-888888888888",
			because: "the role behind a role approver maps to the DTO");
		approval.ApproverRoleDisplay.Should().Be("All employees", because: "its Display companion maps too");
		approval.AllowDelegation.Should().BeTrue(because: "the delegation flag maps to the DTO");
		approval.NotifyApprover.Should().BeTrue(because: "the approver-notification flag maps to the DTO");
		approval.ApproverEmailTemplate.Should().Contain("55555555-6666-7777-8888-999999999999",
			because: "that notification's template maps to the DTO");
		approval.ApproverEmailTemplateDisplay.Should().Be("Approval requested",
			because: "its Display companion maps too");
		approval.NotifyAuthor.Should().BeTrue(because: "the author-notification flag maps to the DTO");
		approval.AuthorEmailTemplate.Should().Contain("66666666-7777-8888-9999-aaaaaaaaaaaa",
			because: "the author notification's template maps to the DTO");
		approval.AuthorEmailTemplateDisplay.Should().Be("Approval result",
			because: "its Display companion maps too");
		approval.Recipient.Should().Be("ops@example.com",
			because: "the author notification's recipient is the field 'Author' cannot work out on its own");
		approval.IgnoreEmailErrors.Should().BeFalse(
			because: "the flag maps as WRITTEN — false has to survive, or it would read as 'not written'");
		approval.ApprovalSchemaUId.Should().Be("9800f45d-7d2e-44c7-85e5-053c06c8c2d4",
			because: "the derived visa schema is reported for traceability and must not be dropped either");
	}

	[Test]
	[Description("Leaves every approval member the server omits at null rather than defaulting it, so a partly reported block cannot read as a verified 'off' — the flags in particular, where false and absent mean different things on this element.")]
	public void Describe_ShouldLeaveOmittedApprovalMembersNull_WhenServerReportsAPartialBlock() {
		// Arrange — a server that reports the block with only the two fields it has written
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"Approval1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"approval\",\"userTaskName\":\"ApprovalUserTask\","
			+ "\"approval\":{\"object\":\"Order\",\"approverType\":\"manager\"}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedApproval approval = result.Value.Elements[0].Approval;
		approval.Should().NotBeNull(because: "a partial block is still a block");
		approval.Object.Should().Be("Order", because: "what the server DID report has to arrive");
		approval.NotifyApprover.Should().BeNull(
			because: "absent must not become false: on this element 'not written' and 'switched off' are "
				+ "different states, and only a nullable flag can tell them apart");
		approval.NotifyAuthor.Should().BeNull(because: "the same holds for the author notification");
		approval.IgnoreEmailErrors.Should().BeNull(
			because: "this one is the sharpest case — the schema default is TRUE, so a false here would assert "
				+ "the opposite of what the element actually does");
		approval.AllowDelegation.Should().BeNull(because: "the same holds for the delegation flag");
		approval.Recipient.Should().BeNull(because: "an unreported recipient is unknown, not empty");
	}

	[Test]
	[Description("Leaves the email block's hasBody unset (null) when an older server omits it, so the absent flag serializes away instead of defaulting to false and reading as a verified 'no body'.")]
	public void Describe_ShouldLeaveEmailHasBodyNull_WhenServerOmitsIt() {
		// Arrange — an older CrtProcessBuilder that reports an email block without the hasBody flag
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"SendEmail1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"sendemail\",\"userTaskName\":\"EmailTemplateUserTask\","
			+ "\"email\":{\"mode\":\"auto\",\"subject\":\"After modify\"}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedEmail email = result.Value.Elements[0].Email;
		email.Should().NotBeNull(because: "the email block itself is still reported by an older server");
		email.HasBody.Should().BeNull(
			because: "an omitted hasBody stays null rather than defaulting to false, which would be indistinguishable "
				+ "from a server that reported 'this element has no custom-message body'. DEFENSIVE only: the server "
				+ "declares hasBody as a non-nullable bool introduced in the same commit as the email block, so no "
				+ "shipped build reports the block without the flag");
		email.IgnoreErrors.Should().BeNull(
			because: "ignoreErrors is genuinely optional on the server (a nullable bool there), so it is the flag that "
				+ "really does arrive absent — hasBody is modelled the same way for safety, not from a known gap");
	}

	[Test]
	[Description("Deserializes a signal start's record trigger — entity, on, and the tracked-change columns array — from the server response into the DescribedSignal DTO, so describe read-back surfaces changedColumns instead of dropping them.")]
	public void Describe_ShouldReadSignalTrackedColumns_WhenServerReportsThem() {
		// Arrange — a signalStart on Order, on:modified, restricted to the Amount + StatusId columns
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"SignalStart1\",\"type\":\"ProcessSchemaStartSignalEvent\",\"buildType\":\"signalstart\","
			+ "\"signal\":{\"entity\":\"Order\",\"entitySchemaUId\":\"5c58c4c4-134b-4744-9c67-96d9c69c9d55\",\"on\":\"modified\",\"changedColumns\":[\"Amount\",\"StatusId\"]}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedSignal signal = result.Value.Elements[0].Signal;
		signal.Should().NotBeNull(because: "a signal start's record trigger must be surfaced, not dropped by the clio DTO");
		signal.Entity.Should().Be("Order", because: "the trigger entity round-trips");
		signal.On.Should().Be("modified", because: "the change type round-trips");
		signal.ChangedColumns.Should().BeEquivalentTo(new[] { "Amount", "StatusId" },
			because: "the tracked-change columns array must be deserialized so describe round-trips them into a build/modify");
	}

	[Test]
	[Description("Leaves the signal's changedColumns null when the server omits them (an any-change signal), so the absent field serializes away cleanly.")]
	public void Describe_ShouldLeaveSignalChangedColumnsNull_WhenServerOmitsThem() {
		// Arrange — an any-change signalStart (no changedColumns)
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"SignalStart1\",\"type\":\"ProcessSchemaStartSignalEvent\",\"buildType\":\"signalstart\","
			+ "\"signal\":{\"entity\":\"Order\",\"on\":\"modified\"}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		result.Value.Elements[0].Signal.ChangedColumns.Should().BeNull(
			because: "an omitted changedColumns stays null so it serializes away (WhenWritingNull) for an any-change signal");
	}

	[Test]
	[Description("Deserializes a lookup condition's displayValue (the resolved caption) alongside its raw id value, so a lookup reads back as a human-readable caption instead of the clio DTO dropping it and leaving only a GUID.")]
	public void Describe_ShouldReadFilterConditionDisplayValue_WhenServerReportsLookupCaption() {
		// Arrange — UsrStage = <guid> with the resolved caption "Approved" carried on displayValue
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"SignalStart1\",\"type\":\"ProcessSchemaStartSignalEvent\",\"buildType\":\"signalstart\","
			+ "\"filter\":{\"object\":\"UsrClioFilterTest\",\"logicalOperation\":\"and\","
			+ "\"conditions\":[{\"column\":\"UsrStage\",\"comparison\":\"equal\",\"value\":\"09cd1bea-6a0e-4972-a6ad-97be3ea83dac\",\"displayValue\":\"Approved\"}]}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedFilterCondition condition = result.Value.Elements[0].Filter.Conditions[0];
		condition.Value.Should().Be("09cd1bea-6a0e-4972-a6ad-97be3ea83dac",
			because: "the raw lookup id round-trips as the value for an unambiguous re-build");
		condition.DisplayValue.Should().Be("Approved",
			because: "the resolved lookup caption is surfaced so the read-back is human-readable, not a bare GUID");
	}

	[Test]
	[Description("Leaves the element filter null when the server reports no filter, so it serializes away for non-filtered elements.")]
	public void Describe_ShouldLeaveFilterNull_WhenServerOmitsIt() {
		// Arrange — an element with no data source filter
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\"}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.Value.Elements[0].Filter.Should().BeNull(
			because: "an element without a filter keeps Filter null so it serializes away (WhenWritingNull)");
	}

	[Test]
	[Description("Deserializes a filter condition's macro (and its integer macroArgument) so a relative-date / system macro survives read-back instead of being dropped by the clio DTO.")]
	public void Describe_ShouldReadFilterConditionMacro_WhenServerReportsIt() {
		// Arrange — CreatedOn = Today (no argument) AND CreatedOn > NextNDays(7)
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"SignalStart1\",\"type\":\"ProcessSchemaStartSignalEvent\",\"buildType\":\"signalstart\","
			+ "\"filter\":{\"object\":\"Contact\",\"logicalOperation\":\"and\",\"conditions\":["
			+ "{\"column\":\"CreatedOn\",\"comparison\":\"equal\",\"macro\":\"Today\"},"
			+ "{\"column\":\"CreatedOn\",\"comparison\":\"greater\",\"macro\":\"NextNDays\",\"macroArgument\":7}]}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedFilter filter = result.Value.Elements[0].Filter;
		filter.Conditions[0].Macro.Should().Be("Today",
			because: "a no-argument macro must surface on read-back, not be dropped by the clio DTO");
		filter.Conditions[0].MacroArgument.Should().BeNull(because: "Today takes no argument");
		filter.Conditions[1].Macro.Should().Be("NextNDays", because: "an argument macro's name surfaces");
		filter.Conditions[1].MacroArgument.Should().Be(7,
			because: "the macro's integer argument must surface, not be dropped by the clio DTO");
	}

	[Test]
	[Description("Deserializes a filter condition's date-part (Year(CreatedOn) = 2026) so the left-hand date-part modifier survives read-back instead of being dropped by the clio DTO.")]
	public void Describe_ShouldReadFilterConditionDatePart_WhenServerReportsIt() {
		// Arrange — Year(CreatedOn) = 2026
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"SignalStart1\",\"type\":\"ProcessSchemaStartSignalEvent\",\"buildType\":\"signalstart\","
			+ "\"filter\":{\"object\":\"Contact\",\"logicalOperation\":\"and\",\"conditions\":["
			+ "{\"column\":\"CreatedOn\",\"comparison\":\"equal\",\"datePart\":\"Year\",\"value\":\"2026\"}]}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedFilter filter = result.Value.Elements[0].Filter;
		filter.Conditions[0].DatePart.Should().Be("Year",
			because: "the left-hand date-part modifier must surface on read-back, not be dropped by the clio DTO");
		filter.Conditions[0].Column.Should().Be("CreatedOn", because: "the date-part column round-trips");
		filter.Conditions[0].Value.Should().Be("2026", because: "the extracted-part integer value round-trips");
	}

	[Test]
	[Description("A parameter reference (process- or element-level) surfaces on read-back as the raw expression meta-path token, matching the server decoder, which never emits a structured processParameter/elementParameter; the reserved structured fields stay null.")]
	public void Describe_ShouldSurfaceParameterReferencesAsExpressionTokens_WhenServerReportsThem() {
		// Arrange — the real server surfaces BOTH reference kinds as an expression token: Address = a raw [#..#]
		// token, Account = an element-parameter meta-path token (never a structured elementParameter object).
		const string elementRefToken =
			"[IsOwnerSchema:false].[IsSchema:false].[Element:{02f3221a-1111-2222-3333-444444444444}].[Parameter:{4d2571e8-5555-6666-7777-888888888888}]";
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"read1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\","
			+ "\"filter\":{\"object\":\"Contact\",\"logicalOperation\":\"and\",\"conditions\":["
			+ "{\"column\":\"Address\",\"comparison\":\"equal\",\"expression\":\"[#Custom.Token#]\"},"
			+ "{\"column\":\"Account\",\"comparison\":\"equal\",\"expression\":\"" + elementRefToken + "\"}]}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedFilter filter = result.Value.Elements[0].Filter;
		filter.Conditions[0].Expression.Should().Be("[#Custom.Token#]",
			because: "a raw expression token must surface on read-back, not be dropped by the clio DTO");
		filter.Conditions[1].Expression.Should().Be(elementRefToken,
			because: "an element-parameter reference is surfaced as the raw meta-path expression token, exactly as the server decoder emits it");
		filter.Conditions[1].ElementParameter.Should().BeNull(
			because: "the current server never emits a structured elementParameter — the reference lives in expression, so the reserved field stays null");
		filter.Conditions[1].ProcessParameter.Should().BeNull(
			because: "the current server never emits a structured processParameter either — references are expression tokens only");
	}

	[Test]
	[Description("Forward-compat only: the reserved elementParameter DTO field still deserializes a structured reference if a future server emits one; documents that the field binds, NOT that the current server produces this shape (it surfaces references as expression tokens).")]
	public void Describe_ShouldBindStructuredElementParameter_AsReservedForwardCompatShape() {
		// Arrange — a synthetic response in a shape the CURRENT server does NOT emit (real references come back as
		// expression tokens); this pins only that the reserved DTO field would bind a future structured ref.
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"read1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\","
			+ "\"filter\":{\"object\":\"Contact\",\"logicalOperation\":\"and\",\"conditions\":["
			+ "{\"column\":\"Account\",\"comparison\":\"equal\",\"elementParameter\":{\"elementName\":\"task1\",\"parameter\":\"Account\"}}]}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedFilterCondition condition = result.Value.Elements[0].Filter.Conditions[0];
		condition.ElementParameter.Should().NotBeNull(
			because: "the reserved elementParameter field must still deserialize a structured ref for forward-compat, even though the current server does not emit this shape");
		condition.ElementParameter.ElementName.Should().Be("task1",
			because: "the structured reference's element name binds when present");
		condition.ElementParameter.Parameter.Should().Be("Account",
			because: "the structured reference's parameter name binds when present");
	}

	[Test]
	[Description("Posts the uid (not the name) when the identity is a uid.")]
	public void Describe_ShouldPostWrappedUid_WhenIdentityIsUid() {
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\"}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		describer.Describe(new ProcessIdentity(null, "5c58c4c4-134b-4744-9c67-96d9c69c9d55", null), null);

		client.Received(1).ExecutePostRequest(DescribeUrl,
			Arg.Is<string>(body => Wrapped(body)["uid"].GetValue<string>() == "5c58c4c4-134b-4744-9c67-96d9c69c9d55"),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Returns an error (not a throw) carrying the server's message when the result reports success=false.")]
	public void Describe_ShouldReturnError_WhenSuccessFalse() {
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":false,\"errorMessage\":\"Process 'UsrProc' was not found.\"}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		result.IsError.Should().BeTrue(because: "a server-reported failure becomes an ErrorOr error");
		result.FirstError.Description.Should().Contain("was not found",
			because: "the server message is surfaced to the caller");
	}

	[Test]
	[Description("Returns an error when the server response body is empty.")]
	public void Describe_ShouldReturnError_WhenResponseEmpty() {
		ServerProcessDescriber describer = CreateDescriber(ClientReturning(""));

		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		result.IsError.Should().BeTrue(because: "an empty server response is a failure, not a graph");
	}

	[Test]
	[Description("Returns an error (without calling the server) when no identity is provided.")]
	public void Describe_ShouldReturnError_WhenNoIdentity() {
		IApplicationClient client = ClientReturning("{}");
		ServerProcessDescriber describer = CreateDescriber(client);

		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity(null, null, null), null);

		result.IsError.Should().BeTrue(because: "a describe needs a code, uid, or caption");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default, default, default, default);
	}

	[Test]
	[Description("Deserializes an element's connections[] entry in full — column, registration state, raw macro and the decoded source — plus the element-level deprecated and writesConnectionsAtRuntime facts, because a member clio's DTO does not declare is dropped SILENTLY and the tool description promises all of them.")]
	public void Describe_ShouldReadConnectionsAndCapabilityFacts_WhenServerReportsThem() {
		// Arrange — one fixed-record connection on a registered column, and both element-level facts
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\","
			+ "\"connections\":[{\"column\":\"Account\",\"registered\":true,\"source\":\"Script\","
			+ "\"value\":\"[#Lookup.c449d832-a4cc-4b01-b9d5-8a12c42a9f89.e308b781-3c5b-4ecb-89ef-5c1ed4da488e#]\","
			+ "\"recordId\":\"e308b781-3c5b-4ecb-89ef-5c1ed4da488e\",\"referenceSchema\":\"Account\"}],"
			+ "\"deprecated\":false,\"writesConnectionsAtRuntime\":true}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedElement element = result.Value.Elements[0];
		element.Connections.Should().HaveCount(1,
			because: "an undeclared collection member deserializes to null, which is how four promised fields were "
				+ "dropped in silence once already");
		DescribedConnection connection = element.Connections[0];
		connection.Column.Should().Be("Account", because: "the column names the connection and is its identity");
		connection.Registered.Should().BeTrue(
			because: "registration is what tells a caller whether the connection is a full citizen or a half one");
		connection.RecordId.Should().Be("e308b781-3c5b-4ecb-89ef-5c1ed4da488e",
			because: "the decoded source is what makes the read-back re-appliable without translating a metapath");
		connection.ReferenceSchema.Should().Be("Account", because: "the entity travels as a NAME the write side takes");
		connection.Value.Should().StartWith("[#Lookup.",
			because: "the raw persisted macro travels alongside the decoded form, which is the forward-compatibility guarantee");
		element.WritesConnectionsAtRuntime.Should().BeTrue(
			because: "this is the verdict a caller is told to read before trusting a binding, so it must reach them");
		element.Deprecated.Should().BeFalse(because: "the retirement fact is reported per element and must not be dropped");
	}

	[Test]
	[Description("Leaves connections/deprecated/writesConnectionsAtRuntime null when an older CrtProcessBuilder omits them, so a stale package degrades to 'not established' rather than to a wrong answer.")]
	public void Describe_ShouldLeaveConnectionsAndCapabilityFactsNull_WhenServerOmitsThem() {
		// Arrange — the pre-connections server shape
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\"}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		DescribedElement element = result.Value.Elements[0];
		element.Connections.Should().BeNull(because: "absent is not empty: nothing was reported, so nothing is claimed");
		element.WritesConnectionsAtRuntime.Should().BeNull(
			because: "null means NOT ESTABLISHED, which is exactly what an older package can say about it");
		element.Deprecated.Should().BeNull(because: "same reason — an omitted fact must not read as false");
	}

	[Test]
	[Description("A field a NEWER server reports that this build does not declare — at the graph root, on an element and inside the email block — survives the clio DTO round trip through [JsonExtensionData] under the command's own serializer options, so clio does not structurally lag the server by a release.")]
	public void Describe_ShouldPreserveUnknownServerFields_WhenReserializingTheGraph() {
		// Arrange — a root fact, an element block and an email-block field the DTOs do not declare. Without an
		// overflow bag each is dropped without a trace, the silent-loss failure the connections DTO calls out.
		// The email case is the one that matters most: that block is where the next email feature lands.
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\",\"futureRootFact\":\"kept\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"sendemail\","
			+ "\"email\":{\"mode\":\"auto\",\"futureEmailFact\":\"kept\"},"
			+ "\"futureBlock\":{\"setting\":42}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act — re-serialize with the SAME options DescribeProcessCommand uses for its output (WriteIndented +
		// WhenWritingNull), so this asserts what a caller actually reads rather than a default-options shape.
		JsonSerializerOptions commandOutputOptions = new() {
			WriteIndented = true,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);
		string reserialized = JsonSerializer.Serialize(result.Value, commandOutputOptions);

		// Assert
		result.IsError.Should().BeFalse(because: "an undeclared member must not fail the read");
		result.Value.AdditionalData.Should().ContainKey("futureRootFact",
			because: "the root overflow bag is what keeps an undeclared server field addressable at all");
		result.Value.Elements[0].AdditionalData.Should().ContainKey("futureBlock",
			because: "an element-level block needs the same protection — that is where a new email/signal-shaped "
				+ "feature lands first");
		result.Value.Elements[0].Email.AdditionalData.Should().ContainKey("futureEmailFact",
			because: "the email block is the feature under active development, so a field a newer server adds there "
				+ "(a template selection, a body format) must not be the one thing that still vanishes");

		// Re-parse rather than string-match: under WriteIndented the exact spacing is a formatting detail, and
		// asserting on it would make this test pass or fail for the wrong reason.
		JsonNode output = JsonNode.Parse(reserialized);
		output["futureRootFact"]!.GetValue<string>().Should().Be("kept",
			because: "capturing it is only half the fix: the describe output is what the caller reads, so the "
				+ "field has to come back out on re-serialization");
		output["elements"]![0]!["futureBlock"]!["setting"]!.GetValue<int>().Should().Be(42,
			because: "the element block must survive verbatim, nesting included, not be flattened or stringified");
		output["elements"]![0]!["email"]!["futureEmailFact"]!.GetValue<string>().Should().Be("kept",
			because: "the email block's unknown field has to reach the output too, not just the in-memory bag");
	}

	// The describer wraps the identity under a "request" property (ProcessDesignService BodyStyle=Wrapped).
	private static JsonNode Wrapped(string body) => JsonNode.Parse(body)["request"];
}
