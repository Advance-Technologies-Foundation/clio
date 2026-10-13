using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
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
public sealed class TriggerSmokeRunnerTests {

	private const string Me = "410006e1-ca4e-4502-a9ec-e54d922d2c00";
	private const string TriggerId = "5b1c2f8e-0f8e-4d55-9a38-3bb4f6f0e001";
	private const string RunId = "e29511a0-b05e-4b8f-8155-a8a25b76754e";
	private const string ActivityId = "489ae227-5516-487b-aa09-77de84b6feb4";
	private const string CreateUrl = "http://sandbox/0/odata/UsrRequest";
	private const string SelectUrl = "http://sandbox/0/DataService/json/SyncReply/SelectQuery";
	private const string ElementId = "0c1a2b3c-4d5e-4f60-8a9b-0c1d2e3f4a5b";

	private sealed record Harness(
		TriggerSmokeRunner Runner,
		IApplicationClient Client,
		IProcessRunMonitor Monitor,
		ICreatedRecordWatcher Watcher,
		ICreatedRecordCleaner Cleaner);

	private static Harness Create(string createResponse, IReadOnlyList<CreatedRecord> runs,
		IReadOnlyList<CreatedRecord> records, params ProcessRunState[] states) =>
		Create(createResponse, runs, records, TriggerId, states);

	// activityPointsAt: the value of the activity's UsrRequest lookup; the activity is held by RunId's element.
	private static Harness Create(string createResponse, IReadOnlyList<CreatedRecord> runs,
		IReadOnlyList<CreatedRecord> records, string activityPointsAt, params ProcessRunState[] states) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecuteNonReplayablePostRequest(CreateUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(createResponse);
		client.ExecutePostRequest(SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => {
				JObject query = JObject.Parse(call.ArgAt<string>(1));
				JObject columns = (JObject)query["columns"]["items"];
				string rows = query.Value<string>("rootSchemaName") switch {
					"Activity" when columns.ContainsKey("ProcessElementId") =>
						$"[{{\"Id\":\"{ActivityId}\",\"ProcessElementId\":\"{ElementId}\"}}]",
					"Activity" => $"[{{\"Id\":\"{ActivityId}\",\"UsrRequest\":{{\"value\":\"{activityPointsAt}\",\"displayValue\":\"x\"}}}}]",
					"SysProcessElementData" => $"[{{\"Id\":\"{ElementId}\",\"SysProcess\":{{\"value\":\"{RunId}\"}}}}]",
					_ => "[]"
				};
				return "{\"success\":true,\"rows\":" + rows + "}";
			});
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(Arg.Any<string>()).Returns(call => "http://sandbox/0/" + call.Arg<string>());
		urls.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		IRuntimeEntitySchemaReader schemas = Substitute.For<IRuntimeEntitySchemaReader>();
		schemas.GetByName("Activity", Arg.Any<int>()).Returns(new RuntimeEntitySchemaResult(Guid.NewGuid(), "Activity",
			Guid.NewGuid(), "Title", null, [
				new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "UsrRequest", null, null, 10, false, false, "UsrRequest"),
				new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "ProcessElementId", null, null, 0, false, false, null)
			]));
		schemas.GetByName("UsrRequest", Arg.Any<int>()).Returns(new RuntimeEntitySchemaResult(Guid.NewGuid(), "UsrRequest",
			Guid.NewGuid(), "UsrName", null, []));
		WatchBaseline recordBaseline = new() {
			Entities = [new EntityWatch { Entity = "UsrRequest" }, new EntityWatch { Entity = "Activity", References = ["UsrRequest"] }],
			CurrentUserContactId = Me
		};
		WatchBaseline processBaseline = new() { Entities = [new EntityWatch { Entity = "SysProcessLog" }], CurrentUserContactId = Me };
		ICreatedRecordWatcher watcher = Substitute.For<ICreatedRecordWatcher>();
		watcher.Capture(Arg.Is<IReadOnlyList<string>>(list => list.Contains("SysProcessLog")), Arg.Any<int>(),
			Arg.Any<DateTimeOffset?>()).Returns(processBaseline);
		watcher.Capture(Arg.Is<IReadOnlyList<string>>(list => !list.Contains("SysProcessLog")), Arg.Any<int>(),
			Arg.Any<DateTimeOffset?>()).Returns(recordBaseline);
		WatchOutcome runOutcome = new();
		runOutcome.Created.AddRange(runs);
		watcher.Collect(processBaseline, Arg.Any<int>(), Arg.Any<DateTimeOffset?>()).Returns(runOutcome);
		WatchOutcome recordOutcome = new();
		recordOutcome.Created.AddRange(records);
		watcher.Collect(recordBaseline, Arg.Any<int>(), Arg.Any<DateTimeOffset?>()).Returns(recordOutcome);
		IProcessRunMonitor monitor = Substitute.For<IProcessRunMonitor>();
		if (states.Length > 0) {
			monitor.Read(Arg.Any<Guid>(), Arg.Any<int>()).Returns(states[0], states.Skip(1).ToArray());
		}
		monitor.Cancel(Arg.Any<Guid>(), Arg.Any<int>()).Returns((string)null);
		ICreatedRecordCleaner cleaner = Substitute.For<ICreatedRecordCleaner>();
		cleaner.Delete(Arg.Any<IReadOnlyList<CreatedRecord>>(),
			Arg.Any<IReadOnlyDictionary<string, IReadOnlyCollection<string>>>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>())
			.Returns(call => new CleanupReport { Deleted = [.. call.Arg<IReadOnlyList<CreatedRecord>>()] });
		DateTimeOffset now = DateTimeOffset.UtcNow;
		TriggerSmokeRunner runner = new(client, urls, new RunVerificationServices(monitor, watcher, cleaner), schemas) {
			Clock = () => now,
			Sleep = ms => now = now.AddMilliseconds(ms)
		};
		return new Harness(runner, client, monitor, watcher, cleaner);
	}

	private static TriggerSmokeRequest Request(bool cleanup = true) => new("UsrRequest",
		new Dictionary<string, JsonElement> { ["UsrName"] = JsonSerializer.SerializeToElement("Smoke") },
		["Activity"], 5, cleanup);

	private static CreatedRecord Run() => new("SysProcessLog", RunId, "Request handling") { CreatedById = Me };

	private static CreatedRecord Trigger() => new("UsrRequest", TriggerId, "Smoke") { CreatedById = Me };

	private static CreatedRecord Activity() => new("Activity", ActivityId, "Notify") { CreatedById = Me };

	[Test]
	[Description("The trigger is created, the run it started is reported with its state, the activity it created is listed without the trigger, and cleanup cancels the parked run before deleting the activity and the trigger.")]
	public void Run_Should_Report_The_Started_Run_And_Clean_Up() {
		// Arrange
		ProcessRunState parked = new(true, 1, null, [new WaitingElement("Notify", "ProcessSchemaUserTask")]);
		Harness harness = Create($"{{\"Id\":\"{TriggerId}\"}}", [Run()], [Trigger(), Activity()],
			// Seven reads during the 6 s minimum wait, one after the cancel, then the cancelled state.
			parked, parked, parked, parked, parked, parked, parked, parked, new ProcessRunState(true, 4, null, []));

		// Act
		TriggerSmokeResult result = harness.Runner.Run(Request());

		// Assert
		result.Success.Should().BeTrue(because: "a parked run is not a failure");
		result.Processes.Single().Status.Should().Be("cancelled",
			because: "after cleanup cancelled the run, processes must not still say running");
		result.Trigger.Id.Should().Be(TriggerId, because: "the triggering record is reported");
		result.Processes.Should().ContainSingle(because: "one run started");
		result.Created.Select(record => record.Id).Should().Equal([ActivityId],
			because: "created lists what the run made, not the trigger");
		result.Cancelled.Should().Equal([RunId], because: "the caller's parked run is cancelled before deleting");
		Received.InOrder(() => {
			harness.Monitor.Cancel(Guid.Parse(RunId), Arg.Any<int>());
			harness.Cleaner.Delete(Arg.Is<IReadOnlyList<CreatedRecord>>(records =>
					records.Select(record => record.Id).OrderBy(id => id).SequenceEqual(new[] { ActivityId, TriggerId }.OrderBy(id => id))),
				Arg.Any<IReadOnlyDictionary<string, IReadOnlyCollection<string>>>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>());
		});
	}

	[Test]
	[Description("A started run that ended in error fails the check and names the logged error.")]
	public void Run_Should_Fail_When_A_Started_Run_Failed() {
		// Arrange
		Harness harness = Create($"{{\"Id\":\"{TriggerId}\"}}", [Run()], [Trigger()],
			new ProcessRunState(true, 3, "System.Exception: boom", []));

		// Act
		TriggerSmokeResult result = harness.Runner.Run(Request(cleanup: false));

		// Assert
		result.Success.Should().BeFalse(because: "a run the trigger started failed");
		result.Processes.Single().Error.Should().Contain("System.Exception: boom", because: "the logged error is reported");
		harness.Cleaner.DidNotReceiveWithAnyArgs().Delete(default, default, default, default);
	}

	[Test]
	[Description("When no run starts, the check fails and says so with the likely causes: a smoke check whose process never ran has not passed.")]
	public void Run_Should_Warn_When_No_Process_Started() {
		// Arrange
		Harness harness = Create($"{{\"Id\":\"{TriggerId}\"}}", [], [Trigger()]);

		// Act
		TriggerSmokeResult result = harness.Runner.Run(Request());

		// Assert
		result.Processes.Should().BeEmpty(because: "nothing started");
		result.Success.Should().BeFalse(because: "the process under test never ran");
		result.Error.Should().Contain("No process started", because: "an empty result must be explained");
		result.Cleanup.Deleted.Select(record => record.Id).Should().Equal([TriggerId],
			because: "the trigger is cleaned up even when nothing started");
	}

	[Test]
	[Description("A refused create stops the check before any wait, with the server's reasons.")]
	public void Run_Should_Stop_When_The_Trigger_Is_Refused() {
		// Arrange
		Harness harness = Create(
			"{\"error\":{\"code\":\"\",\"message\":\"An error has occurred.\",\"innererror\":{\"message\":\"Column UsrName is required\"}}}",
			[], []);

		// Act
		TriggerSmokeResult result = harness.Runner.Run(Request());

		// Assert
		result.Success.Should().BeFalse(because: "there is no trigger to check");
		result.Error.Should().Contain("Column UsrName is required", because: "the server's own reason is surfaced");
		harness.Monitor.DidNotReceiveWithAnyArgs().Read(default, default);
	}

	[Test]
	[Description("A running run another user started in the same window is never cancelled, and another user's record is never deleted.")]
	public void Run_Should_Not_Touch_Another_Users_Run_Or_Record() {
		// Arrange
		CreatedRecord foreignRun = new("SysProcessLog", RunId, "Scheduled job") { CreatedById = "c4ed336c-3e9b-40fe-8b82-5632650c7b8d" };
		CreatedRecord foreignActivity = new("Activity", ActivityId, "Other") { CreatedById = "c4ed336c-3e9b-40fe-8b82-5632650c7b8d" };
		ProcessRunState parked = new(true, 1, null, [new WaitingElement("Wait", "ProcessSchemaUserTask")]);
		Harness harness = Create($"{{\"Id\":\"{TriggerId}\"}}", [foreignRun], [Trigger(), foreignActivity], parked);

		// Act
		TriggerSmokeResult result = harness.Runner.Run(Request());

		// Assert
		harness.Monitor.DidNotReceiveWithAnyArgs().Cancel(default, default);
		result.Cleanup.Skipped.Select(record => record.Id).Should().Equal([ActivityId],
			because: "another user's record is reported, never deleted");
	}

	[Test]
	[Description("A record the same login created in the window with no lookup to the trigger is not tied to this call: it is skipped, never deleted, and the parked run that holds it is not cancelled.")]
	public void Run_Should_Not_Touch_A_Same_Login_Record_Not_Tied_To_The_Trigger() {
		// Arrange
		ProcessRunState parked = new(true, 1, null, [new WaitingElement("Notify", "ProcessSchemaUserTask")]);
		Harness harness = Create($"{{\"Id\":\"{TriggerId}\"}}", [Run()], [Trigger(), Activity()],
			"9d3c55a1-2b6e-4a43-9f43-6f3f1a2b7c11", parked);

		// Act
		TriggerSmokeResult result = harness.Runner.Run(Request());

		// Assert
		harness.Monitor.DidNotReceiveWithAnyArgs().Cancel(default, default);
		result.Cleanup.Skipped.Select(record => record.Id).Should().Equal([ActivityId],
			because: "nothing ties the activity to the trigger, so it may be another session's record");
		result.Cleanup.Deleted.Select(record => record.Id).Should().Equal([TriggerId],
			because: "only the record this call created is deleted");
	}

	[Test]
	[Description("When the trigger create was not confirmed, cleanup cancels and deletes nothing: no record in the window can be shown to come from this call.")]
	public void Run_Should_Not_Clean_Up_When_The_Trigger_Was_Not_Confirmed() {
		// Arrange
		Harness harness = Create("{}", [Run()], [Trigger(), Activity()]);

		// Act
		TriggerSmokeResult result = harness.Runner.Run(Request());

		// Assert
		harness.Cleaner.DidNotReceiveWithAnyArgs().Delete(default, default, default, default);
		harness.Monitor.DidNotReceiveWithAnyArgs().Cancel(default, default);
		result.Cleanup.Skipped.Should().HaveCount(2, because: "everything that appeared is reported, nothing is deleted");
	}

	[Test]
	[Description("The tool validates its arguments before anything is resolved or created.")]
	[TestCase("UsrRequest", "{}", "Activity", 15, "values is required")]
	[TestCase("UsrRequest", "{\"UsrName\":\"x\"}", null, 15, "watch is required")]
	[TestCase("Usr Request", "{\"UsrName\":\"x\"}", "Activity", 15, "entity must be")]
	[TestCase("UsrRequest", "{\"UsrName\":\"x\"}", "Activity", 31, "timeout must be")]
	public void Tool_Should_Validate_Arguments(string entity, string values, string watch, int timeout, string expected) {
		// Arrange
		TriggerSmokeArgs args = new() {
			EnvironmentName = "dev", Entity = entity,
			Values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(values),
			Watch = watch is null ? null : [watch], Timeout = timeout
		};

		// Act
		string error = TriggerSmokeTool.Validate(args);

		// Assert
		error.Should().Contain(expected, because: "the argument is outside the contract");
	}
}
