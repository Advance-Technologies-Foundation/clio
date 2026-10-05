using System;
using System.Linq;
using System.Text.Json;
using Clio.Common;
using Clio.Common.ObjectRights;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.ObjectRights;

/// <summary>
/// The record layer of <see cref="RightManagementServiceClient"/>: reading the "Use record permissions" switch and the
/// default record rules, and the exact <c>SaveAdministratedObject</c> payload of a record save — the FULL rule list when
/// a rule changes (the platform replaces the list), untouched (<c>null</c>) when only the switch changes.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class RightManagementServiceClientRecordTests {

	private const string SchemaUId = "35a9057f-800d-414b-acfb-c919c215857a";
	private static readonly Guid AllEmployees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid External = Guid.Parse("720b771c-e7a7-4f31-9cfb-52cd21c3739f");
	private static readonly Guid Admins = Guid.Parse("83a43ebc-f36b-1410-298d-001e8c82bcad");

	private const string SelectUrl = "http://host/0/DataService/json/SyncReply/SelectQuery";
	private const string GetUrl = "http://host/0/ServiceModel/RightManagementService.svc/GetAdministratedObject";
	private const string SaveUrl = "http://host/0/ServiceModel/RightManagementService.svc/SaveAdministratedObject";

	private IApplicationClient _applicationClient;
	private RightManagementServiceClient _client;
	private string _savedPayload;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetAdministratedObject).Returns(GetUrl);
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveAdministratedObject).Returns(SaveUrl);
		Post(SelectUrl).Returns($"{{\"success\":true,\"rows\":[{{\"UId\":\"{SchemaUId}\"}}]}}");
		_savedPayload = null;
		Post(SaveUrl).Returns(callInfo => { _savedPayload = (string)callInfo[1]; return "{\"success\":true}"; });
		_client = new RightManagementServiceClient(_applicationClient, urlBuilder);
	}

	private string Post(string url) =>
		_applicationClient.ExecutePostRequest(url, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());

	private static string RuleJson(Guid author, Guid grantee, int read, int edit, int delete, bool flag = false,
		string extra = "") =>
		"{\"authorSysAdminUnit\":{\"id\":\"" + author + "\",\"name\":\"A\"},\"granteeSysAdminUnit\":{\"id\":\"" + grantee
		+ "\",\"name\":\"G\"},\"readRightLevel\":" + read + ",\"editRightLevel\":" + edit + ",\"deleteRightLevel\":"
		+ delete + ",\"doNotApplyForManager\":" + (flag ? "true" : "false") + extra + "}";

	private void ObjectIs(bool recordsOn, params string[] rules) => ObjectIsWith(recordsOn, false, rules);

	private void ObjectIsWith(bool recordsOn, bool recordRightsDenied, params string[] rules) =>
		Post(GetUrl).Returns("{\"success\":true,\"administratedObject\":{\"name\":\"UsrFoo\",\"uId\":\"" + SchemaUId
			+ "\",\"administratedByOperations\":true,\"administratedByColumns\":false,\"administratedByRecords\":"
			+ (recordsOn ? "true" : "false") + ",\"recordRightsDenied\":" + (recordRightsDenied ? "true" : "false") + ","
			+ "\"entitySchemaOperationsRights\":[{\"id\":\"o\",\"position\":0,\"canRead\":true,\"sysAdminUnit\":{\"id\":\""
			+ AllEmployees + "\"}}],\"entitySchemaColumnsRights\":[],\"entityOperationGrantees\":[],"
			+ "\"entitySchemaRecordDefRights\":[" + string.Join(",", rules) + "]}}");

	private ObjectRightsInfo Read() => _client.GetObjectRights("UsrFoo", new CreatioRequestOptions());

	private JsonElement Saved() => JsonDocument.Parse(_savedPayload).RootElement.GetProperty("administratedObject");

	[Test]
	[Description("The read projects the switch, the schema UId and every rule — both ids and names, the three levels and the manager flag; a stored level outside 0..2 is kept as is so the planner can refuse it.")]
	public void GetObjectRights_ShouldProjectRecordLayer() {
		// Arrange
		ObjectIs(true, RuleJson(AllEmployees, Admins, 2, 1, 0, flag: true), RuleJson(External, AllEmployees, 3, 0, 0));

		// Act
		ObjectRightsInfo info = Read();

		// Assert
		info.AdministratedByRecords.Should().BeTrue(because: "the switch is read from administratedByRecords");
		info.SchemaUId.Should().Be(Guid.Parse(SchemaUId), because: "the apply step starts the process with it");
		info.RecordRules.Should().HaveCount(2, because: "every rule is listed");
		info.RecordRules[0].Should().Be(new DefaultRecordRule(AllEmployees, "A", Admins, "G", RecordRightLevel.Delegated,
			RecordRightLevel.Granted, RecordRightLevel.NotSet, true), because: "every wire field is projected");
		((int)info.RecordRules[1].Read).Should().Be(3, because: "an invalid stored level is not normalised away");
	}

	[Test]
	[Description("An object whose read has no record collection (or an 8.3 read without recordRightsDenied) reads as no rules, not as a failure.")]
	public void GetObjectRights_ShouldReadNoRules_WhenCollectionIsMissing() {
		// Arrange
		Post(GetUrl).Returns("{\"success\":true,\"administratedObject\":{\"name\":\"UsrFoo\",\"administratedByOperations\":false}}");

		// Act
		ObjectRightsInfo info = Read();

		// Assert
		info.IsRead.Should().BeTrue(because: "the object was read");
		info.RecordState.Rules.Should().BeEmpty(because: "a missing collection is no rules");
		info.AdministratedByRecords.Should().BeFalse(because: "a missing switch is off");
	}

	[Test]
	[Description("A record save that changes a rule sends the FULL planned list: an untouched rule exactly as read (a field the projection does not know included), the changed rule's levels replaced, a new rule built from the plan; a dropped rule is left out. The operation, column and grantee collections are null; recordRightsDenied round-trips.")]
	public void SaveRecords_ShouldSendFullList_WhenRulesChange() {
		// Arrange
		ObjectIs(true, RuleJson(External, AllEmployees, 1, 1, 1, extra: ",\"futureField\":7"),
			RuleJson(AllEmployees, AllEmployees, 1, 1, 0), RuleJson(Admins, Admins, 1, 0, 0));
		ObjectRightsInfo info = Read();
		DefaultRecordRule kept = info.RecordRules[0];
		DefaultRecordRule changed = info.RecordRules[1] with { Edit = RecordRightLevel.NotSet };
		DefaultRecordRule added = new(AllEmployees, "All employees", Admins, "Admins", RecordRightLevel.Delegated,
			RecordRightLevel.NotSet, RecordRightLevel.NotSet, true);

		// Act
		ObjectRightsSaveResult result = _client.Save(info.Snapshot,
			new DefaultRecordRightsState(true, new[] { kept, changed, added }), new CreatioRequestOptions());

		// Assert
		result.Succeeded.Should().BeTrue(because: "the service acknowledged the save");
		JsonElement saved = Saved();
		JsonElement[] rules = saved.GetProperty("entitySchemaRecordDefRights").EnumerateArray().ToArray();
		rules.Should().HaveCount(3, because: "the dropped Admins rule is left out and the new one added");
		rules[0].GetProperty("futureField").GetInt32().Should().Be(7, because: "an untouched rule is sent exactly as read");
		rules[1].GetProperty("editRightLevel").GetInt32().Should().Be(0, because: "the changed rule carries its new level");
		rules[2].GetProperty("granteeSysAdminUnit").GetProperty("id").GetString().Should().Be(Admins.ToString(),
			because: "the new rule is built from the plan");
		rules[2].GetProperty("readRightLevel").GetInt32().Should().Be(2, because: "a delegated level is 2 on the wire");
		rules[2].GetProperty("doNotApplyForManager").GetBoolean().Should().BeTrue(because: "the flag is written");
		saved.GetProperty("entitySchemaOperationsRights").ValueKind.Should().Be(JsonValueKind.Null,
			because: "a record save leaves the operation rows untouched");
		saved.GetProperty("entitySchemaColumnsRights").ValueKind.Should().Be(JsonValueKind.Null, because: "untouched");
		saved.GetProperty("entityOperationGrantees").ValueKind.Should().Be(JsonValueKind.Null, because: "untouched");
		saved.GetProperty("recordRightsDenied").GetBoolean().Should().BeFalse(because: "the legacy field round-trips as read");
		saved.GetProperty("administratedByOperations").GetBoolean().Should().BeTrue(because: "the other switch is sent as read");
	}

	[Test]
	[Description("A record save that changes only the switch sends the rule list as null (leave untouched), so no stored rule is re-sent.")]
	public void SaveRecords_ShouldSendNullRules_WhenOnlySwitchChanges() {
		// Arrange
		ObjectIs(false, RuleJson(External, AllEmployees, 1, 1, 1));
		ObjectRightsInfo info = Read();

		// Act
		_client.Save(info.Snapshot, info.RecordState with { AdministratedByRecords = true }, new CreatioRequestOptions());

		// Assert
		JsonElement saved = Saved();
		saved.GetProperty("administratedByRecords").GetBoolean().Should().BeTrue(because: "the switch is planned on");
		saved.GetProperty("entitySchemaRecordDefRights").ValueKind.Should().Be(JsonValueKind.Null,
			because: "the rules did not change, so the replace-whole-list save must not touch them");
	}

	[Test]
	[Description("A record save is sent once: a transport retry of a committed save is never made.")]
	public void SaveRecords_ShouldSendOnce() {
		// Arrange
		ObjectIs(false);
		ObjectRightsInfo info = Read();

		// Act
		_client.Save(info.Snapshot, info.RecordState with { AdministratedByRecords = true },
			new CreatioRequestOptions { MaxAttempts = 3 });

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(SaveUrl, Arg.Any<string>(), Arg.Any<int>(), 1, Arg.Any<int>());
	}

	[Test]
	[Description("recordRightsDenied is sent back exactly as read — true stays true — and is not used otherwise.")]
	public void SaveRecords_ShouldRoundTripRecordRightsDenied() {
		// Arrange
		ObjectIsWith(true, true);
		ObjectRightsInfo info = Read();

		// Act
		_client.Save(info.Snapshot, info.RecordState with { AdministratedByRecords = false }, new CreatioRequestOptions());

		// Assert
		Saved().GetProperty("recordRightsDenied").GetBoolean().Should().BeTrue(because: "the legacy field is sent back as read");
	}

	[Test]
	[Description("A changed rule gets only the fields the plan changes: a level field the change does not touch — here a missing one — stays exactly as read, and a new rule node carries the two units, the three levels and the flag.")]
	public void SaveRecords_ShouldWriteOnlyChangedFields() {
		// Arrange
		string withoutEdit = RuleJson(AllEmployees, Admins, 1, 0, 0).Replace(",\"editRightLevel\":0", "");
		ObjectIs(true, withoutEdit);
		ObjectRightsInfo info = Read();
		DefaultRecordRule changed = info.RecordRules[0] with { Read = RecordRightLevel.Delegated };
		DefaultRecordRule added = new(External, "External", AllEmployees, "Employees", RecordRightLevel.Granted,
			RecordRightLevel.NotSet, RecordRightLevel.NotSet, false);

		// Act
		_client.Save(info.Snapshot, new DefaultRecordRightsState(true, new[] { changed, added }), new CreatioRequestOptions());

		// Assert
		JsonElement[] rules = Saved().GetProperty("entitySchemaRecordDefRights").EnumerateArray().ToArray();
		rules[0].GetProperty("readRightLevel").GetInt32().Should().Be(2, because: "the named level is written");
		rules[0].TryGetProperty("editRightLevel", out _).Should().BeFalse(because: "a field the change does not touch stays as read");
		rules[1].EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(new[] {
			"authorSysAdminUnit", "granteeSysAdminUnit", "readRightLevel", "editRightLevel", "deleteRightLevel",
			"doNotApplyForManager" }, because: "a new rule carries exactly the fields the page writes");
		rules[1].GetProperty("authorSysAdminUnit").EnumerateObject().Select(property => property.Name)
			.Should().Equal(new[] { "id" }, because: "a unit is named by its id");
	}
}
