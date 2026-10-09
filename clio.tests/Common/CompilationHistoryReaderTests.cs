using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class CompilationHistoryReaderTests {

	private const string ApplicationInfoUrl = "http://test/0/ServiceModel/ApplicationInfoService.svc/GetApplicationInfo";
	private const string SelectUrl = "http://test/0/DataService/json/SyncReply/SelectQuery";
	private const string UtcPlusThree = """{"applicationInfo":{"sysValues":{"userTimezoneOffset":180}}}""";

	private static readonly DateTimeOffset Now = new(2026, 10, 8, 10, 30, 0, TimeSpan.Zero);

	private static (CompilationHistoryReader Reader, IApplicationClient Client) CreateReader(string applicationInfo,
		string rows) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(ApplicationInfoUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(applicationInfo);
		client.ExecutePostRequest(SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(rows);
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetApplicationInfo).Returns(ApplicationInfoUrl);
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		TimeProvider clock = Substitute.For<TimeProvider>();
		clock.GetUtcNow().Returns(Now);
		return (new CompilationHistoryReader(client, urlBuilder, clock), client);
	}

	[Test]
	[Description("Reads the session's offset and the newest rows through the same client, each request bounded by the timeout, and converts the session-zone times to UTC with that offset.")]
	public void ReadLatest_Should_ConvertTheSessionZoneTimesToUtc_WithTheSessionsOwnOffset() {
		// Arrange
		(CompilationHistoryReader reader, IApplicationClient client) = CreateReader(UtcPlusThree,
			"""{"success":true,"rows":[{"CreatedOn":"2026-10-08T09:54:41.340","ProjectName":"A.csproj","Result":true,"DurationInSeconds":2,"ErrorsWarnings":"[]"}]}""");

		// Act
		CompilationHistoryReading reading = reader.ReadLatest(10, 10_000);

		// Assert
		client.Received(1).ExecutePostRequest(ApplicationInfoUrl, "{}", 10_000, Arg.Any<int>(), Arg.Any<int>());
		client.Received(1).ExecutePostRequest(SelectUrl, CompilationHistoryReader.BuildSelectQuery(10), 10_000,
			Arg.Any<int>(), Arg.Any<int>());
		reading.Rows.Should().ContainSingle(because: "the answer holds one row");
		reading.Rows[0].FinishedUtc.Should().Be(new DateTime(2026, 10, 8, 6, 54, 41, 340, DateTimeKind.Utc),
			because: "DataService wrote the row in the session's UTC+3 zone (measured: 09:54:41 for a row stored as 06:54:41 UTC)");
		reading.Rows[0].FinishedUtc.Kind.Should().Be(DateTimeKind.Utc,
			because: "the time is compared with a UTC clock and serialized for an agent, which must see a Z");
		reading.CheckedUtc.Should().Be(Now.UtcDateTime,
			because: "a row's age is counted back from this clock, so it has to be the same one every time");
	}

	[Test]
	[Description("The query asks DataService for the newest rows first, with exactly the columns the rows are built from.")]
	public void BuildSelectQuery_Should_AskForTheNewestRowsWithTheMappedColumns() {
		// Arrange
		const int count = 7;

		// Act
		string body = CompilationHistoryReader.BuildSelectQuery(count);

		// Assert
		using JsonDocument query = JsonDocument.Parse(body);
		JsonElement root = query.RootElement;
		JsonElement columns = root.GetProperty("columns").GetProperty("items");
		root.GetProperty("rootSchemaName").GetString().Should().Be("CompilationHistory", because: "the rows live there");
		root.GetProperty("rowCount").GetInt32().Should().Be(count, because: "the caller decides how many rows it reads");
		columns.GetProperty("CreatedOn").GetProperty("orderDirection").GetInt32().Should().Be(2,
			because: "descending by time puts the newest rows - the ones a caller matches to its compile - first");
		foreach (string column in new[] { "ProjectName", "Result", "DurationInSeconds", "ErrorsWarnings" }) {
			columns.TryGetProperty(column, out _).Should().BeTrue(because: "the row maps the {0} column", column);
		}
	}

	[Test]
	[Description("Maps a DataService row: the project, duration, result and diagnostics (warnings included).")]
	public void ParseRows_Should_MapEveryColumn_WhenTheRowCarriesThem() {
		// Arrange
		const string body = """
			{"success":true,"rows":[{
			  "CreatedOn":"2026-10-08T09:54:41.340",
			  "ProjectName":"Terrasoft.Configuration.Dev.csproj",
			  "Result":false,
			  "DurationInSeconds":141,
			  "ErrorsWarnings":"[{\"errorNumber\":\"CS0103\",\"errorText\":\"The name 'x' does not exist\",\"fileName\":\"C:\\\\a\\\\UsrProc.cs\",\"line\":3,\"column\":5,\"isWarning\":false},{\"errorNumber\":\"CS0114\",\"errorText\":\"hides\",\"isWarning\":true}]"
			}]}
			""";

		// Act
		IReadOnlyList<CompilationHistoryRow> rows = CompilationHistoryReader.ParseRows(body, TimeSpan.Zero);

		// Assert
		CompilationHistoryRow row = rows[0];
		row.FinishedUtc.Should().Be(new DateTime(2026, 10, 8, 9, 54, 41, 340, DateTimeKind.Utc),
			because: "with a zero session offset the session time is already UTC");
		row.ProjectName.Should().Be("Terrasoft.Configuration.Dev.csproj", because: "the project is mapped as returned");
		row.DurationSeconds.Should().Be(141, because: "DurationInSeconds is mapped as returned");
		row.Succeeded.Should().BeFalse(because: "Result=false is a failed build");
		row.Diagnostics.Should().HaveCount(2, because: "ErrorsWarnings holds one error and one warning");
		row.Diagnostics[0].ErrorNumber.Should().Be("CS0103", because: "the error is parsed from ErrorsWarnings");
		row.Diagnostics[1].IsWarning.Should().BeTrue(because: "a warning stays marked as one, so a caller can drop it");
	}

	[TestCase("", TestName = "an empty body")]
	[TestCase("<html><body>Login</body></html>", TestName = "an HTML login page")]
	[TestCase("""{"success":false,"errorInfo":{"message":"denied"}}""", TestName = "a failed query")]
	[TestCase("""{"value":[]}""", TestName = "an answer without rows")]
	[TestCase("""{"success":true,"rows":[{"ProjectName":"A.csproj"}]}""", TestName = "a row without a time")]
	[TestCase("""{"success":true,"rows":[{"CreatedOn":"not a date"}]}""", TestName = "a row with an unreadable time")]
	[TestCase("""{"success":true,"rows":[{"CreatedOn":"2026-10-08T06:54:41Z"}]}""", TestName = "a time that carries its own zone")]
	[TestCase("""{"success":true,"rows":[{"CreatedOn":"0001-01-01T01:00:00"}]}""", TestName = "a time that cannot take the offset")]
	[Description("Anything that is not a successful answer of session-zone rows is unreadable, and the message is clio's own: it quotes nothing the server sent. A time with its own zone is refused rather than shifted by the session offset a second time.")]
	public void ParseRows_Should_Throw_WhenTheAnswerIsNotSessionZoneRows(string body) {
		// Arrange
		TimeSpan sessionOffset = TimeSpan.FromHours(3);

		// Act
		Action act = () => CompilationHistoryReader.ParseRows(body, sessionOffset);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a caller must be able to tell 'no history to show' from an empty history")
			.Which.Message.Should().Be("The environment did not answer with compilation-history rows.",
				because: "server-authored text never reaches a diagnostic, so the message is a fixed local sentence");
	}

	[TestCase("""{"applicationInfo":{"sysValues":{"userTimezoneOffset":-300}}}""", -300)]
	[TestCase("""{"applicationInfo":{"sysValues":{"userTimezoneOffset":0}}}""", 0)]
	[Description("The session offset is read from applicationInfo.sysValues.userTimezoneOffset, in minutes and with its sign.")]
	public void ParseSessionOffset_Should_ReadTheOffsetInMinutes(string body, int expectedMinutes) {
		// Arrange
		TimeSpan expected = TimeSpan.FromMinutes(expectedMinutes);

		// Act
		TimeSpan offset = CompilationHistoryReader.ParseSessionOffset(body);

		// Assert
		offset.Should().Be(expected, because: "a zone west of UTC has a negative offset, and UTC has none");
	}

	[TestCase("", TestName = "no body")]
	[TestCase("<html>login</html>", TestName = "a login page")]
	[TestCase("""{"applicationInfo":{"sysValues":{}}}""", TestName = "no offset")]
	[TestCase("""{"applicationInfo":{"sysValues":{"userTimezoneOffset":"180"}}}""", TestName = "an offset that is not a number")]
	[Description("Without the session's offset the rows' times cannot be converted, so the read stops with clio's own message.")]
	public void ParseSessionOffset_Should_Throw_WhenTheOffsetIsMissing(string body) {
		// Arrange
		string answer = body;

		// Act
		Action act = () => CompilationHistoryReader.ParseSessionOffset(answer);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a time that cannot be converted to UTC must not be shown as if it were UTC")
			.Which.Message.Should().Be("The environment did not report the session's time-zone offset.",
				because: "the message is clio's own sentence");
	}

	[Test]
	[Description("A failed offset read stops the history read before it asks for rows.")]
	public void ReadLatest_Should_NotQueryTheRows_WhenTheOffsetCannotBeRead() {
		// Arrange
		(CompilationHistoryReader reader, IApplicationClient client) = CreateReader("<html>login</html>",
			"""{"success":true,"rows":[]}""");

		// Act
		Action act = () => reader.ReadLatest(10, 10_000);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "rows without a known zone cannot be shown as UTC");
		client.DidNotReceive().ExecutePostRequest(SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<int>());
	}
}
