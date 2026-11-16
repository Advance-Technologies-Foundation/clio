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

	private JsonElement SubmitAndReadParameter(DataServiceBatchOperation operation, string column) {
		string sent = null;
		_client.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30000, 1, 1).Returns(call => {
			sent = call.ArgAt<string>(1);
			return "{}";
		});
		_command.ExecuteBatch([operation]);
		using JsonDocument request = JsonDocument.Parse(sent);
		return request.RootElement.GetProperty("items")[0].GetProperty("columnValues").GetProperty("items")
			.GetProperty(column).GetProperty("parameter").Clone();
	}

	[TestCase("insert", 7, "2026-01-31T09:00:00.000")]
	[TestCase("update", 7, "2026-01-31T09:00:00")]
	[TestCase("insert", 8, "2026-01-01")]
	[TestCase("update", 8, "2026-01-01")]
	[TestCase("insert", 9, "09:30:00")]
	[TestCase("update", 9, "09:30:00")]
	[TestCase("insert", 7, "2026-01-31T09:00:00+02:00")]
	[Description("A plain DateTime, Date or Time string is sent as a quoted JSON string, the form DataService reads.")]
	public void ExecuteBatch_ShouldSendDateOrTimeValueAsQuotedJsonString_WhenValueIsPlainText(string action, int type, string text) {
		// Arrange
		var operation = Operation(action) with { Values = new() { ["StartDate"] = new(type, JsonSerializer.SerializeToElement(text)) } };
		// Act
		JsonElement parameter = SubmitAndReadParameter(operation, "StartDate");
		// Assert
		parameter.GetProperty("dataValueType").GetInt32().Should().Be(type, because: "the caller's data value type is kept");
		parameter.GetProperty("value").ValueKind.Should().Be(JsonValueKind.String, because: "the parameter value travels as text");
		string wireValue = parameter.GetProperty("value").GetString();
		using JsonDocument inner = JsonDocument.Parse(wireValue);
		inner.RootElement.ValueKind.Should().Be(JsonValueKind.String, because: "DataService reads a date or time parameter as JSON and refuses bare text");
		inner.RootElement.GetString().Should().Be(text, because: "quoting must not change the date or time text");
	}

	[TestCase(7, "2026-01-31T09:00:00.000")]
	[TestCase(8, "2026-01-01")]
	[TestCase(9, "09:30:00")]
	[Description("A DateTime, Date or Time value the caller already passed as a JSON string literal is not quoted a second time.")]
	public void ExecuteBatch_ShouldNotQuoteDateOrTimeValueTwice_WhenValueIsAlreadyAJsonStringLiteral(int type, string text) {
		// Arrange
		string alreadyQuoted = JsonSerializer.Serialize(text);
		var plain = Operation() with { Values = new() { ["StartDate"] = new(type, JsonSerializer.SerializeToElement(text)) } };
		var quoted = Operation() with { Values = new() { ["StartDate"] = new(type, JsonSerializer.SerializeToElement(alreadyQuoted)) } };
		// Act
		string plainWireValue = SubmitAndReadParameter(plain, "StartDate").GetProperty("value").GetString();
		string quotedWireValue = SubmitAndReadParameter(quoted, "StartDate").GetProperty("value").GetString();
		// Assert
		quotedWireValue.Should().Be(alreadyQuoted, because: "callers that quote the value themselves must keep working unchanged");
		plainWireValue.Should().Be(quotedWireValue, because: "a plain and an already quoted value must reach DataService in the same form");
	}

	[TestCase(7)]
	[TestCase(8)]
	[TestCase(9)]
	[Description("A null under a DateTime, Date or Time type stays a JSON null, so the column is cleared rather than set to text.")]
	public void ExecuteBatch_ShouldKeepNull_WhenDateOrTimeValueIsNull(int type) {
		// Arrange
		var operation = Operation() with { Values = new() { ["StartDate"] = new(type, JsonSerializer.Deserialize<JsonElement>("null")) } };
		// Act
		JsonElement parameter = SubmitAndReadParameter(operation, "StartDate");
		// Assert
		parameter.GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null, because: "null clears the value and is never turned into text");
		parameter.GetProperty("dataValueType").GetInt32().Should().Be(type, because: "the caller's data value type is kept");
	}

	[TestCase(7, "1767225600", "DateTime")]
	[TestCase(8, "20260101", "Date")]
	[TestCase(9, "{}", "Time")]
	[TestCase(8, "true", "Date")]
	[TestCase(7, "\"\"", "DateTime")]
	[TestCase(8, "\"\\\"\\\"\"", "Date")]
	[Description("A number, object, boolean or empty text under a DateTime, Date or Time type is refused before any request, naming the column and the type.")]
	public void ExecuteBatch_ShouldRejectBeforeAnyRequest_WhenDateOrTimeValueIsNotText(int type, string json, string typeName) {
		// Arrange
		var operation = Operation() with { Values = new() {
			["Name"] = new(1, JsonSerializer.SerializeToElement("Synthetic")),
			["StartDate"] = new(type, JsonSerializer.Deserialize<JsonElement>(json))
		} };
		// Act
		Action act = () => _command.ExecuteBatch([Operation(), operation]);
		// Assert
		act.Should().Throw<ArgumentException>(because: "DataService cannot read such a value as a date or time")
			.Which.Message.Should().Contain("'StartDate'", because: "the caller needs to know which column to fix")
			.And.Contain($"'{typeName}'", because: "the caller needs to know which type the value has to match")
			.And.Contain($"data-value-type {type}", because: "the message uses the argument name the caller passed");
		_client.ReceivedCalls().Should().BeEmpty(because: "the whole batch is validated before the first write");
	}

	[TestCase(" \"2026-01-01T00:00:00\" ")]
	[TestCase("\"2026-01-01T00:00:00\"\n")]
	[TestCase("\r\n\t\"2026-01-01T00:00:00\"")]
	[Description("A JSON string literal with whitespace around it, such as one read from a file with a trailing newline, reaches DataService as the same single literal and is not quoted a second time.")]
	public void ExecuteBatch_ShouldSendTheTrimmedLiteral_WhenAJsonStringLiteralHasWhitespaceAroundIt(string value) {
		// Arrange
		var operation = Operation() with { Values = new() { ["StartDate"] = new(7, JsonSerializer.SerializeToElement(value)) } };
		// Act
		JsonElement parameter = SubmitAndReadParameter(operation, "StartDate");
		// Assert
		string wireValue = parameter.GetProperty("value").GetString();
		wireValue.Should().Be("\"2026-01-01T00:00:00\"", because: "the literal is forwarded without the whitespace around it, as one JSON string literal");
		using JsonDocument inner = JsonDocument.Parse(wireValue);
		inner.RootElement.ValueKind.Should().Be(JsonValueKind.String, because: "DataService reads the parameter as JSON and must find a string");
		inner.RootElement.GetString().Should().Be("2026-01-01T00:00:00", because: "a second quoting would leave the caller's quotes inside the date text");
	}

	[TestCase(7, true)]
	[TestCase(8, true)]
	[TestCase(9, true)]
	[TestCase(7, false)]
	[Description("A date or time text with an escaped lone surrogate, quoted by the caller or plain, cannot be decoded and is refused by validation with the ordinary message instead of failing with an unrelated exception.")]
	public void ExecuteBatch_ShouldRejectBeforeAnyRequest_WhenDateOrTimeTextHasALoneSurrogateEscape(int type, bool quotedByCaller) {
		// Arrange
		// The escape is assembled from parts so that no tool or editor can decode it into a raw lone surrogate.
		string escape = "\\" + "uD800";
		string json = quotedByCaller ? "\"\\\"\\" + escape + "\\\"\"" : "\"" + escape + "\"";
		var operation = Operation() with { Values = new() { ["StartDate"] = new(type, JsonSerializer.Deserialize<JsonElement>(json)) } };
		// Act
		Action act = () => _command.ExecuteBatch([operation]);
		// Assert
		act.Should().Throw<ArgumentException>(because: "an undecodable text is an invalid value, not an internal error")
			.Which.Message.Should().Contain("'StartDate'", because: "the caller needs to know which column to fix")
			.And.Contain($"data-value-type {type}", because: "the refusal is the ordinary date and time validation message");
		_client.ReceivedCalls().Should().BeEmpty(because: "the whole batch is validated before the first write");
	}

	[TestCase(7, "DateTime")]
	[TestCase(8, "Date")]
	[TestCase(9, "Time")]
	[Description("A date or time value object with no value property is refused before any request, naming the column and the type.")]
	public void ExecuteBatch_ShouldRejectBeforeAnyRequest_WhenDateOrTimeValueObjectHasNoValue(int type, string typeName) {
		// Arrange
		DataServiceBatchOperation[] operations = JsonSerializer.Deserialize<DataServiceBatchOperation[]>(
			"[{\"operation\":\"update\",\"schema-name\":\"Contact\",\"record-id\":\"11111111-1111-1111-1111-111111111111\","
			+ "\"values\":{\"StartDate\":{\"data-value-type\":" + type + "}}}]");
		// Act
		Action act = () => _command.ExecuteBatch(operations);
		// Assert
		operations[0].Values["StartDate"].Value.ValueKind.Should().Be(JsonValueKind.Undefined, because: "the test is about a value object that has no value property");
		act.Should().Throw<ArgumentException>(because: "a missing value is neither a date text nor an explicit null")
			.Which.Message.Should().Contain("'StartDate'", because: "the caller needs to know which column to fix")
			.And.Contain($"'{typeName}'", because: "the caller needs to know which type the value has to match")
			.And.Contain("JSON kind Undefined", because: "the message says what was received");
		_client.ReceivedCalls().Should().BeEmpty(because: "the whole batch is validated before the first write");
	}

	[TestCase(0, "\"11111111-1111-1111-1111-111111111111\"")]
	[TestCase(1, "\"2026-01-01\"")]
	[TestCase(1, "\"\\\"already quoted text\\\"\"")]
	[TestCase(4, "42")]
	[TestCase(5, "1.5")]
	[TestCase(10, "\"22222222-2222-2222-2222-222222222222\"")]
	[TestCase(12, "true")]
	[Description("A value under a type other than DateTime, Date or Time reaches DataService exactly as the caller passed it.")]
	public void ExecuteBatch_ShouldSendValueUnchanged_WhenTypeIsNotDateOrTime(int type, string json) {
		// Arrange
		JsonElement value = JsonSerializer.Deserialize<JsonElement>(json);
		var operation = Operation() with { Values = new() { ["Field"] = new(type, value) } };
		// Act
		JsonElement parameter = SubmitAndReadParameter(operation, "Field");
		// Assert
		parameter.GetProperty("value").ValueKind.Should().Be(value.ValueKind, because: "only date and time values are reshaped");
		JsonElement.DeepEquals(parameter.GetProperty("value"), value).Should().BeTrue(because: "other types are forwarded as the caller wrote them");
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
