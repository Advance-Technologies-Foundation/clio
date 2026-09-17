using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class DataServiceBatchCommandTests : BaseCommandTests<DataServiceBatchOptions> {
	private IApplicationClient _client;
	private IFileSystem _files;
	private DataServiceBatchCommand _command;
	protected override void AdditionalRegistrations(IServiceCollection services) {
		_client = Substitute.For<IApplicationClient>();
		_files = Substitute.For<IFileSystem>();
		services.AddSingleton(_client);
		services.AddSingleton(_files);
	}
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<DataServiceBatchCommand>();
	}
	public override void TearDown() {
		_client.ClearReceivedCalls();
		_files.ClearReceivedCalls();
		base.TearDown();
	}
	private static DataServiceBatchOperation Operation(string action = "update") => new(action, "Contact", Guid.NewGuid(),
		action == "delete" ? [] : new() { ["Name"] = new(1, JsonSerializer.SerializeToElement("Synthetic")) });

	[TestCase(true)]
	[TestCase(false)]
	[Description("CLI rejects oversized files before parsing or submitting, including growth after the size check.")]
	public void Execute_ShouldRejectOversizedInput_WhenFileExceedsBound(bool beforeRead) {
		// Arrange
		_files.GetFileSize("input.json").Returns(beforeRead ? 200001 : 1);
		_files.ReadAllText("input.json").Returns(new string('x', 200001));
		// Act
		int result = _command.Execute(new() { Input = "input.json" });
		// Assert
		result.Should().Be(1, because: "oversized input is rejected");
		_client.ReceivedCalls().Should().BeEmpty(because: "oversized input cannot submit writes");
		if (beforeRead) {
			_files.DidNotReceive().ReadAllText(Arg.Any<string>());
		}
	}

	[TestCase("not-json")]
	[TestCase("null")]
	[Description("CLI rejects malformed operation documents with a failing exit code.")]
	public void Execute_ShouldReturnFailure_WhenInputIsInvalid(string input) {
		// Arrange
		_files.ReadAllText("input.json").Returns(input);
		// Act
		int result = _command.Execute(new() { Input = "input.json" });
		// Assert
		result.Should().Be(1, because: "invalid documents cannot become a successful batch");
		_client.ReceivedCalls().Should().BeEmpty(because: "parsing and validation precede submission");
	}

	[TestCase(0)]
	[TestCase(101)]
	[Description("Reject unbounded or empty batches before any remote request.")]
	public void ExecuteBatch_ShouldRejectInvalidCount_WhenOutsideBounds(int count) {
		// Arrange
		var operations = Enumerable.Range(0, count).Select(_ => Operation()).ToArray();
		// Act
		Action act = () => _command.ExecuteBatch(operations);
		// Assert
		act.Should().Throw<ArgumentException>(because: "only bounded nonempty batches are supported");
		_client.ReceivedCalls().Should().BeEmpty(because: "validation must precede network access");
	}

	[TestCase("select", "Contact", false)]
	[TestCase("update", "Contact.Name", false)]
	[TestCase("insert", "Contact", true)]
	[Description("Reject unsupported queries, broad schema expressions and empty record IDs.")]
	public void ExecuteBatch_ShouldRejectInvalidTarget_WhenContractIsInvalid(string action, string schema, bool emptyId) {
		// Arrange
		var operation = Operation() with { Operation = action, SchemaName = schema, RecordId = emptyId ? Guid.Empty : Guid.NewGuid() };
		// Act
		Action act = () => _command.ExecuteBatch([operation]);
		// Assert
		act.Should().Throw<ArgumentException>(because: "each write must target an explicit record");
		_client.ReceivedCalls().Should().BeEmpty(because: "invalid targets never reach Creatio");
	}

	[TestCase("Id", 0, "\"11111111-1111-1111-1111-111111111111\"")]
	[TestCase("Name", 13, "1")]
	[TestCase("Name", 1, "{}")]
	[TestCase("Name", 4, "1.5")]
	[TestCase("Name", 12, "\"true\"")]
	[Description("Reject identity overrides and incompatible scalar values before submission.")]
	public void ExecuteBatch_ShouldRejectInvalidValue_WhenValueContractIsInvalid(string name, int type, string json) {
		// Arrange
		var operation = Operation() with { Values = new() { [name] = new(type, JsonSerializer.Deserialize<JsonElement>(json)) } };
		// Act
		Action act = () => _command.ExecuteBatch([operation]);
		// Assert
		act.Should().Throw<ArgumentException>(because: "column values cannot change targeting or carry unsupported types");
		_client.ReceivedCalls().Should().BeEmpty(because: "validation must prevent malformed writes");
	}

	[Test]
	[Description("Build native typed operations with exact targeting and correlate reordered mixed outcomes.")]
	public void ExecuteBatch_ShouldPreserveMixedOutcomes_WhenResultsAreReordered() {
		// Arrange
		var operations = new[] { Operation("insert"), Operation(), Operation("delete") };
		string sent = null;
		_client.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30000, 1, 1).Returns(call => {
			sent = call.ArgAt<string>(1);
			using var document = JsonDocument.Parse(sent);
			var queries = document.RootElement.GetProperty("items").EnumerateArray().ToArray();
			return JsonSerializer.Serialize(new { hasErrors = true, queryResults = new[] { 2, 1, 0 }.Select(index => new {
				queryId = queries[index].GetProperty("queryId").GetString(), success = index != 1, rowsAffected = index == 1 ? -1 : 1,
				responseStatus = new { Message = index == 1 ? "Denied password=secret" : null }
			}) });
		});
		// Act
		var result = _command.ExecuteBatch(operations);
		// Assert
		result.Items.Select(item => item.State).Should().Equal(new[] { "completed", "failed", "completed" }, because: "query IDs determine input ordering");
		result.CompletedCount.Should().Be(2, because: "successful neighbors are retained");
		result.FailedCount.Should().Be(1, because: "the middle item failed");
		result.Items[1].Diagnostic.SideEffect.Should().Be("unknown", because: "a native failure can follow applied side effects");
		result.Items[0].Diagnostic.SideEffect.Should().Be("acknowledged", because: "successful neighbors retain their native acknowledgement");
		result.Items[1].Diagnostic.ItemIndex.Should().Be(1, because: "diagnostics must identify the submitted operation");
		result.Items[1].Error.Should().Contain("Denied", because: "native PascalCase validation details must survive sanitization");
		result.UnknownCount.Should().Be(0, because: "every native outcome was correlated");
		JsonSerializer.Serialize(result).Should().NotContain("password=secret", because: "server diagnostics are sanitized");
		using var request = JsonDocument.Parse(sent);
		var items = request.RootElement.GetProperty("items");
		items.EnumerateArray().Select(item => item.GetProperty("__type").GetString()).Should().Equal(new[] {
			"Terrasoft.Nui.ServiceModel.DataContract.InsertQuery", "Terrasoft.Nui.ServiceModel.DataContract.UpdateQuery",
			"Terrasoft.Nui.ServiceModel.DataContract.DeleteQuery"
		}, because: "the platform requires exact native discriminators");
		items.EnumerateArray().Select(item => item.GetProperty("operationType").GetInt32()).Should().Equal(new[] { 1, 2, 3 },
			because: "CRUD operation codes must match the native query types");
		request.RootElement.GetProperty("continueIfError").GetBoolean().Should().BeTrue(because: "native continuation preserves later outcomes");
		items[0].GetProperty("columnValues").GetProperty("items").GetProperty("Id").GetProperty("parameter").GetProperty("value").GetGuid()
			.Should().Be(operations[0].RecordId, because: "insert identity is explicit");
		items[1].GetProperty("filters").ToString().Should().Contain(operations[1].RecordId.ToString(), because: "updates target the requested UUID");
		items[2].GetProperty("filters").ToString().Should().Contain(operations[2].RecordId.ToString(), because: "deletes target the requested UUID");
		_client.Received(1).ExecuteNonReplayablePostRequest(Arg.Any<string>(), sent, 30000, 1, 1);
	}

	[TestCase("<html>login</html>")]
	[TestCase("{\"responseStatus\":{\"message\":\"denied\"}}")]
	[TestCase("{\"queryResults\":[]}")]
	[TestCase("{\"queryResults\":[{\"queryId\":\"00000000-0000-0000-0000-000000000000\",\"success\":true}]}")]
	[Description("A missing or uncorrelated response cannot establish success, failure or nonexecution.")]
	public void ExecuteBatch_ShouldReportUnknown_WhenAcknowledgementIsUnusable(string response) {
		// Arrange
		_client.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30000, 1, 1).Returns(response);
		// Act
		var result = _command.ExecuteBatch([Operation()]);
		// Assert
		result.UnknownCount.Should().Be(1, because: "the write may have committed without a usable acknowledgement");
		result.Success.Should().BeFalse(because: "unknown is never success");
		_client.Received(1).ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30000, 1, 1);
	}

	[Test]
	[Description("Malformed optional row counts do not erase established correlated acknowledgements.")]
	public void ExecuteBatch_ShouldPreserveAcknowledgements_WhenRowCountsAreNullOrStrings() {
		// Arrange
		_client.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30000, 1, 1).Returns(call => {
			using var document = JsonDocument.Parse(call.ArgAt<string>(1));
			return JsonSerializer.Serialize(new { queryResults = document.RootElement.GetProperty("items").EnumerateArray().Select((item, index) => new {
				queryId = item.GetProperty("queryId").GetString(), success = true, rowsAffected = index == 0 ? null : "invalid"
			}) });
		});
		// Act
		var result = _command.ExecuteBatch([Operation(), Operation()]);
		// Assert
		result.CompletedCount.Should().Be(2, because: "both native success acknowledgements are correlated");
		result.Items.Should().OnlyContain(item => item.RowsAffected == null, because: "invalid optional counts are not fabricated");
	}

	[Test]
	[Description("A batch-level validation error retains safe detail without inventing individual outcomes.")]
	public void ExecuteBatch_ShouldPreserveRootError_WhenItemOutcomesAreAbsent() {
		// Arrange
		_client.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30000, 1, 1)
			.Returns("{\"responseStatus\":{\"Message\":\"Invalid date password=secret\"}}");
		// Act
		var result = _command.ExecuteBatch([Operation()]);
		// Assert
		result.UnknownCount.Should().Be(1, because: "a root error cannot establish individual outcomes");
		result.Items[0].Diagnostic.Message.Should().Contain("Invalid date", because: "safe platform validation details remain useful");
		JsonSerializer.Serialize(result).Should().NotContain("password=secret", because: "root errors use the same redaction boundary");
	}

	[Test]
	[Description("A write timeout produces unknown outcomes without replay.")]
	public void ExecuteBatch_ShouldNotRetry_WhenWriteTimesOut() {
		// Arrange
		_client.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30000, 1, 1)
			.Returns(_ => throw new TimeoutException("Timeout password=secret"));
		// Act
		var result = _command.ExecuteBatch([Operation(), Operation()]);
		// Assert
		result.UnknownCount.Should().Be(2, because: "both writes may have committed");
		JsonSerializer.Serialize(result).Should().NotContain("password=secret", because: "exception text is untrusted");
		_client.Received(1).ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30000, 1, 1);
	}
}
