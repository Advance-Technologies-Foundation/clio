using System;
using System.Text.Json;
using Clio.Command.BusinessRules.Filters.Schema;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.BusinessRules;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class StaticFilterProbeTests {

	private const string Envelope =
		"""{"rootSchemaName":"Account","filterType":6,"logicalOperation":0,"isEnabled":true,"className":"Terrasoft.FilterGroup","items":{"Filter_0":{"filterType":1,"comparisonType":9,"className":"Terrasoft.CompareFilter","leftExpression":{"expressionType":0,"columnPath":"Name"},"rightExpression":{"expressionType":2,"parameter":{"dataValueType":1,"value":"A"}}}}}""";

	private IApplicationClient _client = null!;
	private string? _sentBody;

	[SetUp]
	public void SetUp() {
		_client = Substitute.For<IApplicationClient>();
		_sentBody = null;
	}

	[Test]
	[Description("The built envelope runs unchanged as the filter of a one-row COUNT on the lookup's reference schema, and the count comes back.")]
	public void Probe_Should_Run_The_Built_Envelope_And_Return_The_Match_Count() {
		// Arrange
		StaticFilterProbe probe = CreateProbe("""{"success":true,"rows":[{"ProbeCount":4}]}""");

		// Act
		StaticFilterProbeResult result = probe.Probe(Envelope, "Account", "Account");

		// Assert
		result.MatchCount.Should().Be(4, because: "the agent judges the filter's meaning by how many records it keeps");
		result.Warning.Should().BeNull(because: "the filter ran");
		using JsonDocument sent = JsonDocument.Parse(_sentBody!);
		sent.RootElement.GetProperty("rootSchemaName").GetString().Should().Be("Account",
			because: "the static filter narrows the lookup, so it is run on the lookup's reference schema");
		sent.RootElement.GetProperty("filters").GetRawText().Should().Be(JsonDocument.Parse(Envelope).RootElement.GetRawText(),
			because: "the probe must test exactly the envelope that will be saved, not a re-typed copy");
	}

	[Test]
	[Description("A server rejection of the filter becomes an error that names the target and the server message and says nothing was saved.")]
	public void Probe_Should_Throw_When_The_Environment_Rejects_The_Filter() {
		// Arrange
		StaticFilterProbe probe = CreateProbe(
			"""{"success":false,"errorInfo":{"message":"Column by path Nmae not found in schema Account."}}""");

		// Act
		Action act = () => probe.Probe(Envelope, "Account", "Account");

		// Assert
		act.Should().Throw<ArgumentException>(because: "a filter the server cannot run must not be saved into a business rule")
			.Which.Message.Should().Contain("apply-static-filter on 'Account'")
			.And.Contain("Nmae not found", because: "the server says which part of the filter is wrong")
			.And.Contain("Nothing was saved", because: "the agent must know the rule was not written")
			.And.Contain("[untrusted-source-text begin]", because: "server-written text reaches the agent fenced as data");
	}

	[TestCase("Column by path UsrAccessLevel not found in schema Account.")]
	[TestCase("Column by path TimeoutSec not found in schema Account.")]
	[TestCase("Column by path Access not found in schema Account.")]
	[Description("A rejection that names a column containing a marker word is still a rejection, never a saved-unverified rule.")]
	public void Probe_Should_Throw_When_A_Rejected_Column_Contains_A_Marker_Word(string serverMessage) {
		// Arrange
		StaticFilterProbe probe = CreateProbe("{\"success\":false,\"errorInfo\":{\"message\":\"" + serverMessage + "\"}}");

		// Act
		Action act = () => probe.Probe(Envelope, "Account", "Account");

		// Assert
		act.Should().Throw<ArgumentException>(because: "a broken filter must not be saved just because a column name looks like 'access' or 'timeout'")
			.Which.Message.Should().Contain("Nothing was saved");
	}

	[Test]
	[Description("Once a call has spent its time budget for checking filters, the next rule is saved unverified without sending a query.")]
	public void Probe_Should_Warn_Without_Querying_When_The_Call_Budget_Is_Spent() {
		// Arrange
		StaticFilterProbe probe = CreateProbe("""{"success":true,"rows":[{"ProbeCount":1}]}""", () => TimeSpan.FromSeconds(59.5));

		// Act
		StaticFilterProbeResult result = probe.Probe(Envelope, "Account", "Account");

		// Assert
		result.Warning.Should().Contain("saved without running its filter").And.Contain("is spent",
			because: "the agent must know why the filter was not checked");
		_sentBody.Should().BeNull(because: "a spent budget sends no query");
	}

	[Test]
	[Description("A probe never waits longer than what is left of the call's budget.")]
	public void Probe_Should_Cap_The_Request_Timeout_At_The_Remaining_Budget() {
		// Arrange
		int? sentTimeout = null;
		StaticFilterProbe probe = CreateProbe("""{"success":true,"rows":[{"ProbeCount":1}]}""", () => TimeSpan.FromSeconds(45));
		_client.When(client => client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()))
			.Do(callInfo => sentTimeout = callInfo.ArgAt<int>(2));

		// Act
		probe.Probe(Envelope, "Account", "Account");

		// Assert
		sentTimeout.Should().Be(15_000, because: "45 s of the 60 s budget are spent, which is less than the 30 s per-probe cap");
	}

	[TestCase("""{"success":false,"errorInfo":{"message":"User does not have permission to read Account."}}""", "permission")]
	[TestCase("""{"success":false,"errorInfo":{"message":"Transaction was deadlocked on lock resources; deadlock victim."}}""", "deadlock")]
	[TestCase("<html>login</html>", "SelectQuery")]
	[Description("A failure that says nothing about the filter (rights, a transient condition, a non-JSON answer) does not block the rule; it becomes a warning.")]
	public void Probe_Should_Warn_When_The_Filter_Could_Not_Be_Run(string response, string expectedReason) {
		// Arrange
		StaticFilterProbe probe = CreateProbe(response);

		// Act
		StaticFilterProbeResult result = probe.Probe(Envelope, "Account", "Account");

		// Assert
		result.MatchCount.Should().BeNull(because: "nothing was counted");
		result.Warning.Should().Contain("saved without running its filter", because: "the agent must know the filter went unverified")
			.And.Contain(expectedReason, because: "the warning keeps the reason");
	}

	private StaticFilterProbe CreateProbe(string response, Func<TimeSpan>? elapsed = null) {
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("DataService/json/SyncReply/SelectQuery");
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(callInfo => {
				_sentBody = callInfo.ArgAt<string>(1);
				return response;
			});
		return new StaticFilterProbe(_client, urls, elapsed);
	}
}
