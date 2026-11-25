using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ODataWriteVerifierTests {

	private const string Id = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";
	private const string Product = "4c7a6ead-4eb8-4fc0-863e-98a664569fed";
	private const string Contact = "16be3651-8fe2-4159-8dd0-a803d4683dd3";
	private const string KeyUrl = "http://creatio/odata/ForecastSheet(" + Id + ")";

	private IApplicationClient _client = null!;
	private IServiceUrlBuilder _urlBuilder = null!;
	private ODataWriteVerifier _verifier = null!;

	[SetUp]
	public void SetUp() {
		_client = Substitute.For<IApplicationClient>();
		_urlBuilder = Substitute.For<IServiceUrlBuilder>();
		_urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		_verifier = new ODataWriteVerifier();
	}

	private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

	private static string NewGuid() => Guid.NewGuid().ToString();

	[Test]
	[Description("A field the entity rule replaced is reported with the sent and the stored value (the ForecastSheet case of clio#1995).")]
	public void VerifyRecord_Should_Report_A_Field_Stored_With_Another_Value() {
		// Arrange
		_client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($"{{\"Id\":\"{Id}\",\"ForecastEntityId\":\"{Contact}\",\"Name\":\"Q1\"}}");

		// Act
		ODataWriteVerification result = _verifier.VerifyRecord(_client, _urlBuilder, "ForecastSheet", Id,
			Json($"{{\"ForecastEntityId\":\"{Product}\",\"Name\":\"Q1\"}}"), null);

		// Assert
		result.Verified.Should().BeTrue(because: "the read-back returned the record");
		result.Diverged.Should().ContainSingle(because: "only ForecastEntityId was stored differently");
		result.Diverged[0].Field.Should().Be("ForecastEntityId", because: "the divergence names the field as sent");
		result.Diverged[0].Sent.GetString().Should().Be(Product, because: "the divergence carries the sent value");
		result.Diverged[0].Stored.GetString().Should().Be(Contact, because: "the divergence carries the stored value");
		result.DivergenceWarning.Should().Contain("ForecastEntityId",
			because: "the warning names the field the caller must look at");
		_client.Received(1).ExecuteGetRequest(KeyUrl + "?$select=ForecastEntityId%2CName",
			ODataWriteVerifier.ReadBackTimeoutMs, 1);
	}

	[Test]
	[Description("A record stored exactly as sent, up to GUID case and a time-zone form, is verified with no divergence.")]
	public void VerifyRecord_Should_Report_No_Divergence_When_The_Values_Match() {
		// Arrange
		_client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($"{{\"Id\":\"{Id}\",\"OwnerId\":\"{Product.ToUpperInvariant()}\",\"DueDate\":\"2024-01-01T02:00:00Z\"}}");

		// Act
		ODataWriteVerification result = _verifier.VerifyRecord(_client, _urlBuilder, "ForecastSheet", Id,
			Json($"{{\"OwnerId\":\"{Product}\",\"DueDate\":\"2024-01-01T04:00:00+02:00\"}}"),
			new Dictionary<string, string> { ["OwnerId"] = "Edm.Guid", ["DueDate"] = "Edm.DateTimeOffset" });

		// Assert
		result.Verified.Should().BeTrue(because: "the read-back returned the record");
		result.Diverged.Should().BeEmpty(because: "both values are the sent values in another form");
		result.DivergenceWarning.Should().BeNull(because: "there is nothing to warn about");
	}

	[TestCase("{\"error\":{\"code\":\"\",\"message\":\"No access\"}}")]
	[TestCase("")]
	[TestCase("<html><head><title>500</title></head></html>")]
	[Description("A read-back that answers with an error, nothing or a non-JSON page leaves the write unverified with a reason, and echoes no server text.")]
	public void VerifyRecord_Should_Be_Unverified_When_The_Read_Back_Fails(string body) {
		// Arrange
		_client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns(body);

		// Act
		ODataWriteVerification result = _verifier.VerifyRecord(_client, _urlBuilder, "ForecastSheet", Id,
			Json("{\"Name\":\"Q1\"}"), null);

		// Assert
		result.Verified.Should().BeFalse(because: "no stored value was read");
		result.Note.Should().Contain("not compared", because: "the caller is told why the write was not verified");
		result.Note.Should().NotContain("No access", because: "server-authored prose is not echoed into the transcript");
	}

	[Test]
	[Description("A transport exception from the read-back is reported as unverified, never thrown into the write's response.")]
	public void VerifyRecord_Should_Be_Unverified_When_The_Read_Back_Throws() {
		// Arrange
		_client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new InvalidOperationException("connection reset"));

		// Act
		ODataWriteVerification result = _verifier.VerifyRecord(_client, _urlBuilder, "ForecastSheet", Id,
			Json("{\"Name\":\"Q1\"}"), null);

		// Assert
		result.Verified.Should().BeFalse(because: "the read-back did not complete");
		result.Note.Should().Contain("connection reset", because: "the transport reason is kept, redacted");
	}

	[Test]
	[Description("When every sent field is one a read-back cannot compare (a binary column, an @odata.bind lookup), no GET is made and the write is NOT reported as verified: the note names the fields.")]
	public void VerifyRecord_Should_Not_Claim_Verified_When_No_Sent_Field_Can_Be_Compared() {
		// Act
		ODataWriteVerification result = _verifier.VerifyRecord(_client, _urlBuilder, "SysImage", Id,
			Json("{\"Data\":\"iVBORw0KGgo=\",\"Owner@odata.bind\":\"Contact(" + Contact + ")\"}"),
			new Dictionary<string, string> { ["Data"] = "Edm.Stream" });

		// Assert
		result.Verified.Should().BeFalse(because: "a lookup set through @odata.bind can be replaced by a rule, and nothing was checked");
		result.Note.Should().Contain("'Data'", because: "the caller is told which fields were not compared")
			.And.Contain("'Owner@odata.bind'", because: "a skipped lookup binding is named too");
		_client.DidNotReceiveWithAnyArgs().ExecuteGetRequest(null!, 0, 0, 0);
	}

	[Test]
	[Description("A sent field that cannot be compared is named in the note even when the other fields were read back and compared.")]
	public void VerifyRecord_Should_Name_Skipped_Fields_Next_To_Compared_Ones() {
		// Arrange
		_client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($"{{\"Id\":\"{Id}\",\"Name\":\"Q1\"}}");

		// Act
		ODataWriteVerification result = _verifier.VerifyRecord(_client, _urlBuilder, "ForecastSheet", Id,
			Json("{\"Name\":\"Q1\",\"Owner@odata.bind\":\"Contact(" + Contact + ")\"}"), null);

		// Assert
		result.Verified.Should().BeTrue(because: "Name was read back and compared");
		result.Note.Should().Contain("'Owner@odata.bind' was not compared",
			because: "verified=true must not hide a sent lookup that was never checked");
		_client.Received(1).ExecuteGetRequest(KeyUrl + "?$select=Name", ODataWriteVerifier.ReadBackTimeoutMs, 1);
	}

	[Test]
	[Description("A sent field the read-back does not return is named in the note and not reported as a divergence.")]
	public void VerifyRecord_Should_Name_A_Field_The_Read_Back_Did_Not_Return() {
		// Arrange
		_client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($"{{\"Id\":\"{Id}\",\"Name\":\"Q1\"}}");

		// Act
		ODataWriteVerification result = _verifier.VerifyRecord(_client, _urlBuilder, "ForecastSheet", Id,
			Json("{\"Name\":\"Q1\",\"Notes\":\"x\"}"), null);

		// Assert
		result.Verified.Should().BeTrue(because: "the record was read");
		result.Diverged.Should().BeEmpty(because: "a missing field is not evidence of another stored value");
		result.Note.Should().Contain("'Notes'", because: "the caller is told which field was not compared");
	}

	[Test]
	[Description("Created rows are read back by Id with one batched GET, and each row gets its own outcome.")]
	public void VerifyRecords_Should_Read_A_Batch_With_One_Get_And_Compare_Each_Row() {
		// Arrange
		string first = NewGuid();
		string second = NewGuid();
		_client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($"{{\"value\":[{{\"Id\":\"{first}\",\"Path\":\"Contact\"}},{{\"Id\":\"{second.ToUpperInvariant()}\",\"Path\":\"Product\"}}]}}");
		List<ODataSentRecord> records = [
			new(first, Json("{\"Path\":\"Product\"}")),
			new(second, Json("{\"Path\":\"Product\"}"))
		];

		// Act
		IReadOnlyDictionary<string, ODataWriteVerification> outcomes = _verifier.VerifyRecords(_client, _urlBuilder,
			"ForecastColumn", records, null, () => 60_000);

		// Assert
		outcomes[first].Diverged.Should().ContainSingle(because: "the first row was stored as Contact");
		outcomes[second].Verified.Should().BeTrue(because: "the second row was returned, in another Id letter case");
		outcomes[second].Diverged.Should().BeEmpty(because: "the second row was stored as sent");
		_client.Received(1).ExecuteGetRequest(
			Arg.Is<string>(url => url.StartsWith("http://creatio/odata/ForecastColumn?$filter=", StringComparison.Ordinal)
				&& url.Contains(first, StringComparison.Ordinal) && url.Contains(second, StringComparison.Ordinal)
				&& url.EndsWith("&$select=Id%2CPath", StringComparison.Ordinal)),
			Arg.Any<int>(), 1);
	}

	[Test]
	[Description("A large batch is split so that no read-back query passes the IIS query-string limit.")]
	public void VerifyRecords_Should_Split_Ids_Into_Queries_Under_The_Length_Limit() {
		// Arrange
		List<ODataSentRecord> records = Enumerable.Range(0, 100)
			.Select(_ => new ODataSentRecord(NewGuid(), Json("{\"Name\":\"x\"}")))
			.ToList();
		List<string> urls = [];
		_client.ExecuteGetRequest(Arg.Do<string>(urls.Add), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"value\":[]}");

		// Act
		_verifier.VerifyRecords(_client, _urlBuilder, "Account", records, null, () => 60_000);

		// Assert
		urls.Count.Should().BeGreaterThan(1, because: "100 ids do not fit one query");
		urls.Select(url => url[(url.IndexOf('?') + 1)..].Length).Should()
			.OnlyContain(length => length <= ODataWriteVerifier.MaxQueryLength,
				because: "IIS refuses a query string over its limit");
		records.Should().OnlyContain(record => urls.Any(url => url.Contains(record.Id, StringComparison.Ordinal)),
			because: "every created row is read back");
	}

	[Test]
	[Description("When the batch budget is spent, the rows are left unverified and no read-back is sent.")]
	public void VerifyRecords_Should_Not_Read_When_The_Budget_Is_Spent() {
		// Arrange
		string id = NewGuid();

		// Act
		IReadOnlyDictionary<string, ODataWriteVerification> outcomes = _verifier.VerifyRecords(_client, _urlBuilder,
			"Account", [new ODataSentRecord(id, Json("{\"Name\":\"x\"}"))], null,
			() => ODataWriteVerifier.MinReadBudgetMs - 1);

		// Assert
		outcomes[id].Verified.Should().BeFalse(because: "there was no time left to read the row");
		outcomes[id].Note.Should().Contain("budget", because: "the caller is told why");
		_client.DidNotReceiveWithAnyArgs().ExecuteGetRequest(null!, 0, 0, 0);
	}

	[Test]
	[Description("A created row the read-back does not return (for example hidden by access rights) is unverified, not diverged.")]
	public void VerifyRecords_Should_Leave_A_Row_The_Read_Back_Did_Not_Return_Unverified() {
		// Arrange
		string id = NewGuid();
		_client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"value\":[]}");

		// Act
		IReadOnlyDictionary<string, ODataWriteVerification> outcomes = _verifier.VerifyRecords(_client, _urlBuilder,
			"Account", [new ODataSentRecord(id, Json("{\"Name\":\"x\"}"))], null, () => 60_000);

		// Assert
		outcomes[id].Verified.Should().BeFalse(because: "the stored row was not seen");
		outcomes[id].Diverged.Should().BeEmpty(because: "an unseen row is not evidence of another stored value");
	}
	[Test]
	[Description("A created record whose Id is not a GUID is unverified with that reason, and no read-back is sent for it.")]
	public void VerifyRecords_Should_Name_A_Missing_Record_Id_As_The_Reason() {
		// Act
		IReadOnlyDictionary<string, ODataWriteVerification> outcomes = _verifier.VerifyRecords(_client, _urlBuilder,
			"Account", [new ODataSentRecord("not-a-guid", Json("{\"Name\":\"x\"}"))], null, () => 60_000);

		// Assert
		outcomes["not-a-guid"].Verified.Should().BeFalse(because: "a record without a GUID Id cannot be addressed");
		outcomes["not-a-guid"].Note.Should().Contain("Id", because: "the reason is the missing Id, not a failed read")
			.And.NotContain("usable answer", because: "no read-back was sent, so none failed");
		_client.DidNotReceiveWithAnyArgs().ExecuteGetRequest(null!, 0, 0, 0);
	}

	[Test]
	[Description("A cancelled call reads no further chunk; its rows are unverified with the cancellation as the reason.")]
	public void VerifyRecords_Should_Not_Read_After_Cancellation() {
		// Arrange
		string id = NewGuid();
		using System.Threading.CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		// Act
		IReadOnlyDictionary<string, ODataWriteVerification> outcomes = _verifier.VerifyRecords(_client, _urlBuilder,
			"Account", [new ODataSentRecord(id, Json("{\"Name\":\"x\"}"))], null, () => 60_000, cancellation.Token);

		// Assert
		outcomes[id].Verified.Should().BeFalse(because: "the caller is gone, so nothing is read");
		outcomes[id].Note.Should().Contain("cancelled", because: "the reason is the cancellation");
		_client.DidNotReceiveWithAnyArgs().ExecuteGetRequest(null!, 0, 0, 0);
	}

	[Test]
	[Description("When the budget runs out between chunks, the chunks already read keep their outcome and only the rest are unverified.")]
	public void VerifyRecords_Should_Read_Chunks_Until_The_Budget_Runs_Out() {
		// Arrange
		List<ODataSentRecord> records = Enumerable.Range(0, 100)
			.Select(_ => new ODataSentRecord(NewGuid(), Json("{\"Name\":\"x\"}")))
			.ToList();
		_client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => "{\"value\":[" + string.Join(",", records
				.Where(record => call.ArgAt<string>(0).Contains(record.Id, StringComparison.Ordinal))
				.Select(record => $"{{\"Id\":\"{record.Id}\",\"Name\":\"x\"}}")) + "]}");
		int budgetCalls = 0;

		// Act - the first chunk sees a full budget, every later one sees it spent
		IReadOnlyDictionary<string, ODataWriteVerification> outcomes = _verifier.VerifyRecords(_client, _urlBuilder,
			"Account", records, null, () => budgetCalls++ == 0 ? 60_000 : ODataWriteVerifier.MinReadBudgetMs - 1);

		// Assert
		_client.ReceivedWithAnyArgs(1).ExecuteGetRequest(null!, 0, 0, 0);
		outcomes.Values.Should().Contain(outcome => outcome.Verified,
			because: "the rows of the first chunk were read before the budget ran out");
		outcomes.Values.Should().Contain(outcome => !outcome.Verified && outcome.Note!.Contains("budget"),
			because: "the rows after it were not read and say why");
		outcomes.Should().HaveCount(records.Count, because: "every row gets an outcome");
	}

	[Test]
	[Description("When the union of sent field names is too long for $select, the chunk reads every column instead, and still compares the sent fields.")]
	public void VerifyRecords_Should_Drop_Select_When_The_Field_List_Is_Too_Long() {
		// Arrange
		string id = NewGuid();
		string fields = string.Join(",", Enumerable.Range(0, 60).Select(index => $"\"LongColumnName{index:D3}\":\"v\""));
		string url = null!;
		_client.ExecuteGetRequest(Arg.Do<string>(value => url = value), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($"{{\"value\":[{{\"Id\":\"{id}\",\"LongColumnName000\":\"other\"}}]}}");

		// Act
		IReadOnlyDictionary<string, ODataWriteVerification> outcomes = _verifier.VerifyRecords(_client, _urlBuilder,
			"Account", [new ODataSentRecord(id, Json("{" + fields + "}"))], null, () => 60_000);

		// Assert
		url.Should().NotContain("$select=", because: "a $select this long would crowd the Ids out of the query");
		url[(url.IndexOf('?') + 1)..].Length.Should().BeLessThanOrEqualTo(ODataWriteVerifier.MaxQueryLength,
			because: "the query still fits the IIS limit");
		outcomes[id].Diverged.Should().ContainSingle(divergence => divergence.Field == "LongColumnName000",
			because: "the sent fields are still compared against the full record");
	}

}
