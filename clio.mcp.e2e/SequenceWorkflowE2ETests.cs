using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Allure.NUnit;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>Opt-in orchestration proof using isolated records and the real MCP transport.</summary>
[TestFixture, Category("McpE2E.Manual"), AllureNUnit, NonParallelizable]
public sealed class SequenceWorkflowE2ETests : McpContractFixtureBase {
	private string _environment = string.Empty;
	private CancellationToken _token;
	private int _calls;
	private long _responseBytes;

	[Test]
	[Description("Authors isolated steps, enrolls natively, verifies task/call/manual-email progression and compares identical single versus batch updates.")]
	public async Task Workflow_ShouldAdvanceNatively_WithoutDuplicateActivities() {
		// Arrange
		var settings = TestConfiguration.Load();
		if (!Guid.TryParse(Environment.GetEnvironmentVariable("CLIO_SEQUENCE_WORKFLOW_SEED_ID"), out Guid seed)
			|| !Guid.TryParse(Environment.GetEnvironmentVariable("CLIO_SEQUENCE_WORKFLOW_OWNER_ID"), out Guid owner)) {
			Assert.Ignore("Requires an owned disposable lab, seed sequence with all-day schedule, and explicit execution contact. Creates retained synthetic fixtures; never sends email.");
			return;
		}
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		_environment = settings.Sandbox.EnvironmentName!;
		_calls = 0;
		_responseBytes = 0;
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
		_token = timeout.Token;
		var context = await Call<SequenceContextResult>(SequenceContextTool.ToolName, new() { ["sequence-id"] = seed });
		context.Success.Should().BeTrue(because: "the seed must expose a complete supported definition");
		Guid Choice(string schema, string name) => Rows(context, $"choices:{schema}")
			.Single(row => row.GetProperty("Name").GetString() == name).GetProperty("Id").GetGuid();
		Guid sequence = Guid.NewGuid(), rules = Guid.NewGuid(), contact = Guid.NewGuid(), invalidContact = Guid.NewGuid();
		Guid schedule = Rows(context, "selected-schedule").Single().GetProperty("Id").GetGuid();
		Guid[] steps = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
		JsonElement selectedRules = Rows(context, "selected-ruleset").Single();
		foreach (JsonProperty field in selectedRules.EnumerateObject().Where(property => property.Name is not "Id" and not "Name")) {
			bool supported = field.Value.ValueKind == JsonValueKind.Object
				? field.Value.TryGetProperty("value", out JsonElement lookup) && lookup.ValueKind == JsonValueKind.String
					&& Guid.TryParse(lookup.GetString(), out Guid id) && id != Guid.Empty
				: field.Value.ValueKind == JsonValueKind.Number && field.Value.TryGetInt32(out _);
			supported.Should().BeTrue(because: $"seed rules field {field.Name} must contain a populated lookup or integer for this explicitly version-bound fixture");
		}
		var ruleValues = selectedRules.EnumerateObject()
			.Where(property => property.Name is not "Id" and not "Name")
			.ToDictionary(property => property.Name, property => Value(property.Value.ValueKind == JsonValueKind.Object ? 10 : 4,
				property.Value.ValueKind == JsonValueKind.Object ? property.Value.GetProperty("value").GetString()! : (object)property.Value.GetInt32()));
		ruleValues["Name"] = Value(1, "MCP workflow rules");
		ruleValues["MaxActiveParticipantsPerUser"] = Value(4, 100);
		await Batch([
			Operation("insert", "SequenceRuleset", rules, ruleValues),
			Operation("insert", "Contact", contact, new() { ["Name"] = Value(1, "MCP sequence workflow"), ["Email"] = Value(1, "sequence-workflow@example.invalid") }),
			Operation("insert", "Contact", invalidContact, new() { ["Name"] = Value(1, "MCP invalid lifecycle fixture") })
		]);
		await Batch([Operation("insert", "Sequence", sequence, new() {
			["Name"] = Value(1, "MCP workflow " + sequence.ToString("N")[..8]), ["Ruleset"] = Value(10, rules),
			["DeliverySchedule"] = Value(10, schedule), ["Status"] = Value(10, Choice("SequenceStatus", "Draft")), ["Owner"] = Value(10, owner)
		})]);
		string[] kinds = ["Task", "Call", "Email"];
		await Batch(kinds.Select((kind, index) => Operation("insert", "SequenceStep", steps[index], new() {
			["Sequence"] = Value(10, sequence), ["Type"] = Value(10, Choice("SequenceStepType", kind)), ["Index"] = Value(4, 0),
			["Postpone"] = Value(4, 0), ["Measurement"] = Value(10, Choice("SequenceStepPostponeTimeUnits", "Hours")),
			["Priority"] = Value(10, Choice("ActivityPriority", "Medium")), ["Description"] = Value(1, $"<p>{kind} instructions</p>"),
			["Subject"] = Value(1, "Synthetic email subject"), ["Body"] = Value(1, "<p>Synthetic email body</p>"),
			["EmailMode"] = Value(10, Choice("SeqEmailMode", "Manual")), ["EmailThreadingMode"] = Value(10, Choice("SeqEmailThreadingMode", "New thread"))
		})).ToArray());

		// Act
		var authored = await Call<SequenceContextResult>(SequenceContextTool.ToolName, new() { ["sequence-id"] = sequence });
		Rows(authored, "steps").Select(row => row.GetProperty("Index").GetInt32()).Should().Equal(new[] { 1, 2, 3 },
			because: "native listeners must normalize append requests into one-based order");
		await Batch([Operation("update", "Sequence", sequence, new() { ["Status"] = Value(10, Choice("SequenceStatus", "Active")) })]);
		var enrolled = await Call<SequenceEnrollmentResult>(SequenceEnrollmentTool.ToolName, new() {
			["sequence-id"] = sequence, ["contact-ids"] = new[] { contact }
		});
		enrolled.AddedCount.Should().Be(1, because: "native enrollment must accept the newly created synthetic contact");
		enrolled.Readback.Participants.Should().ContainSingle(because: "the new sequence has exactly one enrolled contact");
		Guid participant = enrolled.Readback.Participants[0].GetProperty("id").GetGuid();
		Guid completed = (await Read("ActivityStatus", ["Id", "Name"]))
			.Single(row => row.GetProperty("Name").GetString() == "Completed").GetProperty("Id").GetGuid();
		for (int index = 0; index < 3; index++) {
			JsonElement[] activities = await Activities(sequence);
			activities.Should().HaveCount(index + 1, because: "each completion creates only the next native activity");
			JsonElement current = activities.Single(row => row.GetProperty("IsActiveSequenceStep").GetBoolean());
			Lookup(current, "SequenceStep").Should().Be(steps[index], because: "the current activity follows the authored order");
			Lookup(current, "SequenceParticipant").Should().Be(participant, because: "all generated work belongs to the enrolled participant");
			current.GetProperty("Notes").GetString().Should().Be($"<p>{kinds[index]} instructions</p>", because: "step Description becomes activity Notes");
			if (index < 2) {
				await Batch([Operation("update", "Activity", current.GetProperty("Id").GetGuid(), new() { ["Status"] = Value(10, completed) })]);
			} else {
				current.GetProperty("Title").GetString().Should().Be("Synthetic email subject", because: "email uses Subject rather than its generated step name");
				current.GetProperty("Body").GetString().Should().Be("<p>Synthetic email body</p>", because: "manual email must retain authored HTML without sending it");
			}
		}

		// Assert the negative lifecycle fixture through the same native entity pipeline.
		Guid invalidParticipant = Guid.NewGuid();
		Guid paused = Choice("SeqParticipantStatus", "Paused");
		await Batch([Operation("insert", "SequenceParticipant", invalidParticipant, new() {
			["Sequence"] = Value(10, sequence), ["Participant"] = Value(10, invalidContact),
			["Sequencer"] = Value(10, owner), ["Status"] = Value(10, paused)
		})]);
		var rejected = await Batch([Operation("update", "SequenceParticipant", invalidParticipant, new() {
			["Status"] = Value(10, Choice("SeqParticipantStatus", "Active"))
		})], expectSuccess: false);
		rejected.Items.Single().State.Should().Be("failed", because: "an artificial Paused row without a current activity cannot resume");
		Lookup((await Read("SequenceParticipant", ["Id", "Status"], "Id", invalidParticipant)).Single(), "Status")
			.Should().Be(paused, because: "the native lifecycle guard must preserve the invalid fixture's status");
		(await Activities(sequence)).Should().HaveCount(3, because: "the refused resume must not fabricate another activity");

		await MeasureBatchComparison(contact);
		TestContext.Progress.WriteLine($"Workflow calls={_calls}; responseBytes={_responseBytes}; writeRetries=0; emailsSent=0; sequence={sequence}");
	}

	private async Task MeasureBatchComparison(Guid contact) {
		var operations = Enumerable.Range(0, 3).Select(index => Operation("update", "Contact", contact,
			new() { ["Name"] = Value(1, $"Sequence metric {index}") })).ToArray();
		long bytes = _responseBytes;
		var clock = Stopwatch.StartNew();
		foreach (var operation in operations) {
			await Batch([operation]);
		}
		long baselineMs = clock.ElapsedMilliseconds, baselineBytes = _responseBytes - bytes;
		var baseline = (await Read("Contact", ["Name"], "Id", contact)).Single().GetProperty("Name").GetString();
		await Batch([Operation("update", "Contact", contact, new() { ["Name"] = Value(1, "Sequence metric reset") })]);
		(await Read("Contact", ["Name"], "Id", contact)).Single().GetProperty("Name").GetString()
			.Should().Be("Sequence metric reset", because: "the batch phase must start from a distinct persisted value so a no-op cannot pass");
		bytes = _responseBytes;
		clock.Restart();
		await Batch(operations);
		long batchMs = clock.ElapsedMilliseconds, batchBytes = _responseBytes - bytes;
		var after = (await Read("Contact", ["Name"], "Id", contact)).Single().GetProperty("Name").GetString();
		after.Should().Be(baseline, because: "both measured paths execute the same three updates and must produce the same persisted result");
		TestContext.Progress.WriteLine($"Same workload: 3 Contact updates; single-item calls=3 ms={baselineMs} bytes={baselineBytes}; batch calls=1 ms={batchMs} bytes={batchBytes}. One warm-session sample; no token or throughput claim.");
	}

	private async Task<JsonElement[]> Activities(Guid sequence) => await Read("Activity",
		["Id", "Title", "Notes", "Body", "SequenceStep", "SequenceParticipant", "IsActiveSequenceStep"], "Sequence", sequence);

	private async Task<JsonElement[]> Read(string schema, string[] columns, string? filterColumn = null, Guid? id = null) {
		Dictionary<string, object?> query = new() {
			["rootSchemaName"] = schema, ["operationType"] = 0, ["allColumns"] = false, ["rowCount"] = 100,
			["columns"] = new { items = columns.ToDictionary(name => name, name => new { expression = new { expressionType = 0, columnPath = name } }) }
		};
		if (filterColumn is not null) {
			query["filters"] = new { filterType = 6, isEnabled = true, logicalOperation = 0, items = new {
				target = new { filterType = 1, isEnabled = true, comparisonType = 3,
					leftExpression = new { expressionType = 0, columnPath = filterColumn },
					rightExpression = new { expressionType = 2, parameter = new { dataValueType = 0, value = id } } }
			} };
		}
		var result = await Call<ExecuteEsqResponse>(ExecuteEsqTool.ToolName, new() { ["query"] = query });
		result.Success.Should().BeTrue(because: "every workflow assertion requires successful independent DataService readback");
		return result.Rows!.Value.EnumerateArray().ToArray();
	}

	private async Task<DataServiceBatchResult> Batch(object[] operations, bool expectSuccess = true) {
		var result = await Call<DataServiceBatchResult>(DataServiceBatchTool.ToolName, new() { ["operations"] = operations });
		if (expectSuccess) {
			result.Success.Should().BeTrue(because: "the owned fixture write must be acknowledged before dependent work");
		}
		return result;
	}

	private async Task<T> Call<T>(string tool, Dictionary<string, object?> args) {
		args["environment-name"] = _environment;
		var response = await Session.CallToolAsync(tool, new Dictionary<string, object?> { ["args"] = args }, _token);
		_calls++;
		_responseBytes += response.Content.OfType<TextContentBlock>().Sum(block => Encoding.UTF8.GetByteCount(block.Text));
		response.IsError.Should().NotBeTrue(because: "the scenario must execute the real MCP contract without protocol failures");
		return EntitySchemaStructuredResultParser.Extract<T>(response);
	}

	private static JsonElement[] Rows(SequenceContextResult context, string section) {
		context.Sections[section].State.Should().Be("complete", because: "partial discovery cannot authorize fixture construction");
		return ((JsonElement)context.Sections[section].Data!).EnumerateArray().ToArray();
	}

	private static Guid Lookup(JsonElement row, string column) => row.GetProperty(column).GetProperty("value").GetGuid();
	private static object Value(int type, object value) => new Dictionary<string, object?> { ["data-value-type"] = type, ["value"] = value };
	private static object Operation(string operation, string schema, Guid id, Dictionary<string, object> values) =>
		new Dictionary<string, object?> { ["operation"] = operation, ["schema-name"] = schema, ["record-id"] = id, ["values"] = values };
}
