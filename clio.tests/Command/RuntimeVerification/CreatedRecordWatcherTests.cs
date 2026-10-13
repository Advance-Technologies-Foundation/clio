using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.RuntimeVerification;
using Clio.Common;
using Clio.Common.EntitySchema;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.RuntimeVerification;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class CreatedRecordWatcherTests {

	private const string SelectUrl = "http://sandbox/0/DataService/json/SyncReply/SelectQuery";
	private const string Baseline = "2026-10-12T23:24:50.907";
	private const string KnownId = "489ae227-5516-487b-aa09-77de84b6feb4";
	private const string NewId = "8b2a5bd0-138b-4d52-90f6-a50d5433b9b4";
	private const string MeId = "410006e1-ca4e-4502-a9ec-e54d922d2c00";

	private static RuntimeEntitySchemaResult Schema(string name, string titleColumn, params (string Column, string Reference)[] lookups) =>
		new(Guid.NewGuid(), name, Guid.NewGuid(), titleColumn, null,
			[.. lookups.Select(lookup => new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), lookup.Column, null, null, 10,
				false, false, lookup.Reference))]);

	private sealed record Harness(CreatedRecordWatcher Watcher, List<JObject> Queries, IRuntimeEntitySchemaReader SchemaReader);

	// Answers the "newest" query (rowCount 1, no filter), the "at baseline" query (no title column) and the
	// "since baseline" query (title column requested) from the given row sets.
	private static Harness Create(string newestRows, string atBaselineRows, string sinceBaselineRows,
		RuntimeEntitySchemaResult schema = null) {
		List<JObject> queries = [];
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => {
				JObject query = JObject.Parse(call.ArgAt<string>(1));
				queries.Add(query);
				JObject columns = (JObject)query["columns"]["items"];
				if (query.Value<string>("rootSchemaName") == "Contact") {
					return "{\"success\":true,\"rows\":[{\"Id\":\"" + MeId + "\"}]}";
				}
				string rows = query.Value<int>("rowCount") == 1 && columns.Count == 1 ? newestRows
					: columns.ContainsKey("Title") ? sinceBaselineRows
					: atBaselineRows;
				return "{\"success\":true,\"rows\":" + rows + "}";
			});
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		IRuntimeEntitySchemaReader schemaReader = Substitute.For<IRuntimeEntitySchemaReader>();
		schemaReader.GetByName(Arg.Any<string>(), Arg.Any<int>())
			.Returns(schema ?? Schema("Activity", "Title", ("Owner", "Contact")));
		return new Harness(new CreatedRecordWatcher(client, urlBuilder, schemaReader), queries, schemaReader);
	}

	[Test]
	[Description("A watch name typed in another case is stored in the platform's own spelling, so the cleanup can match it against the platform's reference names.")]
	public void Capture_Should_Use_The_Platforms_Spelling_Of_The_Entity_Name() {
		// Arrange
		Harness harness = Create("[]", "[]", "[]");

		// Act
		WatchBaseline baseline = harness.Watcher.Capture(["activity"], 10_000);

		// Assert
		baseline.Entities.Single().Entity.Should().Be("Activity",
			because: "the schema reader returns the canonical name, and References hold canonical names too");
	}

	[Test]
	[Description("The baseline is the newest CreatedOn exactly as DataService wrote it - milliseconds kept - and is sent back as a quoted JSON date, so the after-run read compares like with like; a record already at the baseline is not reported, a new one is, with its primary display value as title.")]
	public void Collect_Should_Report_Only_Records_Created_After_The_Baseline() {
		// Arrange
		Harness harness = Create(
			$"[{{\"CreatedOn\":\"{Baseline}\"}}]",
			$"[{{\"Id\":\"{KnownId}\",\"CreatedOn\":\"{Baseline}\"}}]",
			$"[{{\"Id\":\"{KnownId}\",\"CreatedOn\":\"{Baseline}\",\"Title\":\"Old\"}},"
			+ $"{{\"Id\":\"{NewId}\",\"CreatedOn\":\"2026-10-12T23:25:16.345\",\"Title\":\"Task\","
			+ $"\"ClioCreatedBy\":{{\"value\":\"{MeId}\",\"displayValue\":\"Supervisor\"}}}}]");

		// Act
		WatchBaseline baseline = harness.Watcher.Capture(["Activity"], 10_000);
		WatchOutcome outcome = harness.Watcher.Collect(baseline, 10_000);

		// Assert
		CreatedRecord created = outcome.Created.Should()
			.ContainSingle(because: "only the row that was not there before the action is new").Which;
		(created.Entity, created.Id, created.Title).Should().Be(("Activity", NewId, "Task"),
			because: "the record is named by entity, id and its primary display value");
		created.CreatedById.Should().Be(MeId, because: "the creator decides whether a cleanup may delete the record");
		baseline.CurrentUserContactId.Should().Be(MeId,
			because: "the calling user's contact is read once, through the CurrentUserContact macro");
		JObject since = harness.Queries.Last();
		since["filters"]["items"]["sinceBaseline"]["rightExpression"]["parameter"]["value"].ToString()
			.Should().Be($"\"{Baseline}\"",
				because: "DataService refuses an unquoted date, and a culture-formatted one without milliseconds "
					+ "selects the wrong rows - the defect a JObject.Parse round trip caused live");
		since["filters"]["items"]["sinceBaseline"]["comparisonType"].Value<int>().Should().Be(8,
			because: "greater-or-equal keeps a record created in the same millisecond as the baseline");
		baseline.Entities.Single().References.Should().Contain("Contact",
			because: "the lookup targets order the cleanup");
		outcome.Warnings.Should().BeEmpty(because: "every read succeeded");
	}

	[Test]
	[Description("An entity that had no rows before the action has no baseline, so every row read afterwards is new.")]
	public void Collect_Should_Report_Every_Row_Of_A_Table_That_Was_Empty() {
		// Arrange
		Harness harness = Create("[]", "[]", $"[{{\"Id\":\"{NewId}\",\"CreatedOn\":\"2026-10-12T23:25:16.345\",\"Title\":\"Task\"}}]");

		// Act
		WatchBaseline baseline = harness.Watcher.Capture(["Activity"], 10_000);
		WatchOutcome outcome = harness.Watcher.Collect(baseline, 10_000);

		// Assert
		baseline.Entities.Single().Baseline.Should().BeNull(because: "an empty table has no newest row");
		outcome.Created.Select(record => record.Id).Should().Equal([NewId], because: "the table was empty before");
		((JObject)harness.Queries.Last()["filters"]["items"]).ContainsKey("sinceBaseline").Should().BeFalse(
			because: "with no baseline there is no lower bound to filter on");
	}

	[Test]
	[Description("A row with no CreatedOn never becomes the baseline: the newest query filters it out (PostgreSQL sorts NULL first in a descending order), and an empty value is treated as no baseline instead of a lower bound that could report every existing row.")]
	public void Capture_Should_Ignore_A_Row_Without_CreatedOn() {
		// Arrange
		Harness harness = Create("[{\"CreatedOn\":null}]", "[]", "[]");

		// Act
		WatchBaseline baseline = harness.Watcher.Capture(["Activity"], 10_000);
		harness.Watcher.Collect(baseline, 10_000);

		// Assert
		baseline.Entities.Single().Baseline.Should().BeNull(because: "an empty CreatedOn is no lower bound");
		JObject newest = CreatedRecordWatcher.BuildNewestQuery("Activity");
		newest["filters"]["items"]["hasCreatedOn"]["comparisonType"].Value<int>().Should().Be(2,
			because: "only rows that have a CreatedOn can be the newest one");
		harness.Queries.Last()["filters"]["items"]["hasCreatedOn"].Should().NotBeNull(
			because: "a row without CreatedOn predates the run and is never reported as created");
	}

	[Test]
	[Description("When more existing rows share the newest CreatedOn than can be remembered, the entity is not watched: a pre-existing row past the cap would otherwise be reported as created and deleted by a cleanup.")]
	public void Capture_Should_Not_Watch_An_Entity_Whose_Newest_Rows_Exceed_The_Known_Id_Cap() {
		// Arrange
		string atBaseline = "[" + string.Join(",", Enumerable.Range(0, 501)
			.Select(_ => $"{{\"Id\":\"{Guid.NewGuid()}\",\"CreatedOn\":\"{Baseline}\"}}")) + "]";
		Harness harness = Create($"[{{\"CreatedOn\":\"{Baseline}\"}}]", atBaseline, atBaseline);

		// Act
		WatchBaseline baseline = harness.Watcher.Capture(["Activity"], 10_000);
		WatchOutcome outcome = harness.Watcher.Collect(baseline, 10_000);

		// Assert
		baseline.Entities.Single().Error.Should().Contain("cannot be told apart",
			because: "the known ids would be truncated");
		outcome.Created.Should().BeEmpty(because: "an unwatched entity reports nothing, so nothing pre-existing is deleted");
	}

	[Test]
	[Description("An entity not yet read when stopAt passes is reported unread instead of being read after the tool answered.")]
	public void Collect_Should_Not_Read_After_StopAt() {
		// Arrange
		Harness harness = Create("[]", "[]", "[]");
		WatchBaseline baseline = harness.Watcher.Capture(["Activity"], 10_000);
		int before = harness.Queries.Count;

		// Act
		WatchOutcome outcome = harness.Watcher.Collect(baseline, 10_000, DateTimeOffset.UtcNow.AddSeconds(-1));

		// Assert
		harness.Queries.Should().HaveCount(before, because: "no request is sent past stopAt");
		outcome.Warnings.Should().ContainSingle(because: "the unread entity must be named")
			.Which.Should().Contain("response deadline", because: "the warning says why it was not read");
	}

	[Test]
	[Description("A name that is not a plain schema name is never sent to the server; it comes back as a warning, and the other entities are still watched.")]
	public void Capture_Should_Refuse_A_Name_That_Is_Not_A_Schema_Name() {
		// Arrange
		Harness harness = Create("[]", "[]", "[]");

		// Act
		WatchBaseline baseline = harness.Watcher.Capture(["Activity;drop", "Activity"], 10_000);
		WatchOutcome outcome = harness.Watcher.Collect(baseline, 10_000);

		// Assert
		harness.SchemaReader.DidNotReceive().GetByName("Activity;drop", Arg.Any<int>());
		outcome.Warnings.Should().ContainSingle(because: "the bad name must be reported, not silently dropped")
			.Which.Should().Contain("not a plain entity schema name", because: "the warning says why");
		baseline.Entities.Should().HaveCount(2, because: "the valid entity is still watched");
	}

	[Test]
	[Description("An entity whose schema cannot be read is reported as not watched instead of failing the whole verification.")]
	public void Capture_Should_Report_An_Entity_It_Cannot_Read() {
		// Arrange
		Harness harness = Create("[]", "[]", "[]");
		harness.SchemaReader.GetByName("Missing", Arg.Any<int>())
			.Returns(_ => throw new InvalidOperationException("Schema not found"));

		// Act
		WatchOutcome outcome = harness.Watcher.Collect(harness.Watcher.Capture(["Missing"], 10_000), 10_000);

		// Assert
		outcome.Created.Should().BeEmpty(because: "nothing could be read");
		outcome.Warnings.Should().ContainSingle(because: "the unreadable entity must be named")
			.Which.Should().Contain("'Missing' was not watched", because: "the caller learns which entity is blind");
	}
}

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ProcessRunMonitorTests {

	private const string SelectUrl = "http://sandbox/0/DataService/json/SyncReply/SelectQuery";
	private const string CancelUrl = "http://sandbox/0/ServiceModel/ProcessEngineService.svc/CancelExecution";
	private static readonly Guid ProcessId = Guid.Parse("e29511a0-b05e-4b8f-8155-a8a25b76754e");

	private static (ProcessRunMonitor Monitor, IApplicationClient Client) Create(Func<JObject, string> rows) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => "{\"success\":true,\"rows\":" + rows(JObject.Parse(call.ArgAt<string>(1))) + "}");
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.CancelProcessExecution).Returns(CancelUrl);
		return (new ProcessRunMonitor(client, urlBuilder), client);
	}

	[Test]
	[Description("A parked run reads as running, with the elements it waits on named by caption and short type.")]
	public void Read_Should_Report_The_Elements_A_Running_Run_Waits_On() {
		// Arrange
		(ProcessRunMonitor monitor, _) = Create(query => query.Value<string>("rootSchemaName") == "SysProcessLog"
			? "[{\"StatusValue\":1,\"ErrorDescription\":\"\"}]"
			: "[{\"Caption\":\"Task 1\",\"Type\":\"Terrasoft.Core.Process.ProcessSchemaUserTask\"}]");

		// Act
		ProcessRunState state = monitor.Read(ProcessId, 10_000);

		// Assert
		state.StatusValue.Should().Be(1, because: "SysProcessStatus.Value 1 is Running");
		state.IsTerminal.Should().BeFalse(because: "a running run has not ended");
		state.WaitingOn.Should().Equal([new WaitingElement("Task 1", "ProcessSchemaUserTask")],
			because: "the caller is told what the run waits for, which is what makes waiting longer unnecessary");
	}

	[Test]
	[Description("A failed run reads as terminal with the first line of the logged error; the stack trace is dropped.")]
	public void Read_Should_Report_The_First_Line_Of_The_Error() {
		// Arrange
		(ProcessRunMonitor monitor, _) = Create(_ =>
			"[{\"StatusValue\":3,\"ErrorDescription\":\"System.Exception: boom\\r\\n   at X()\"}]");

		// Act
		ProcessRunState state = monitor.Read(ProcessId, 10_000);

		// Assert
		state.IsTerminal.Should().BeTrue(because: "Error is a final status");
		state.ErrorSummary.Should().Be("System.Exception: boom", because: "the first line is the actionable part");
	}

	[Test]
	[Description("A failed read of the elements a running run waits on keeps the root status that was read, with an empty waitingOn and a note, instead of failing the whole read.")]
	public void Read_Should_Keep_The_Status_When_The_Waiting_Elements_Cannot_Be_Read() {
		// Arrange
		(ProcessRunMonitor monitor, _) = Create(query => query.Value<string>("rootSchemaName") == "SysProcessLog"
			? "[{\"StatusValue\":1,\"ErrorDescription\":\"\"}]"
			: throw new InvalidOperationException("The operation has timed out."));

		// Act
		ProcessRunState state = monitor.Read(ProcessId, 10_000);

		// Assert
		state.StatusValue.Should().Be(1, because: "the root status was read and stands on its own");
		state.WaitingOn.Should().BeEmpty(because: "the elements could not be read");
		state.Note.Should().Contain("could not be read", because: "the caller is told why waitingOn is empty");
	}

	[Test]
	[Description("A run whose log row does not exist reads as not found rather than as some status.")]
	public void Read_Should_Report_A_Missing_Row_As_Not_Found() {
		// Arrange
		(ProcessRunMonitor monitor, _) = Create(_ => "[]");

		// Act
		ProcessRunState state = monitor.Read(ProcessId, 10_000);

		// Assert
		state.Found.Should().BeFalse(because: "there is no row to read a status from");
	}

	[Test]
	[Description("Cancel posts the id as ONE string, the shape CancelExecution splits on ';' - a JSON array is accepted with 200 and cancels nothing - and reads an empty answer as accepted.")]
	public void Cancel_Should_Post_The_Id_As_A_Single_String() {
		// Arrange
		(ProcessRunMonitor monitor, IApplicationClient client) = Create(_ => "[]");
		client.ExecuteNonReplayablePostRequest(CancelUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);

		// Act
		string error = monitor.Cancel(ProcessId, 10_000);

		// Assert
		error.Should().BeNull(because: "the service returns nothing when it accepts the request");
		client.Received(1).ExecuteNonReplayablePostRequest(CancelUrl,
			$"{{\"processDataIds\":\"{ProcessId:D}\"}}", 10_000, 1, Arg.Any<int>());
	}

	[Test]
	[Description("Any body from CancelExecution, which returns void, is reported as an error.")]
	public void Cancel_Should_Report_A_Body_As_An_Error() {
		// Arrange
		(ProcessRunMonitor monitor, IApplicationClient client) = Create(_ => "[]");
		client.ExecuteNonReplayablePostRequest(CancelUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<html>Request Error</html>");

		// Act
		string error = monitor.Cancel(ProcessId, 10_000);

		// Assert
		error.Should().Contain("Request Error", because: "the caller must learn the cancel did not go through");
	}
}
