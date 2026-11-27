using System.Text.Json;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ODataStoredValueComparerTests {

	private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

	[TestCase("\"4C7A6EAD-4EB8-4FC0-863E-98A664569FED\"", "\"4c7a6ead-4eb8-4fc0-863e-98a664569fed\"", "Edm.Guid")]
	[TestCase("\"4c7a6ead-4eb8-4fc0-863e-98a664569fed\"", "\"4c7a6ead-4eb8-4fc0-863e-98a664569fed\"", null)]
	[TestCase("\"2024-01-01T04:00:00+02:00\"", "\"2024-01-01T02:00:00Z\"", "Edm.DateTimeOffset")]
	[TestCase("1", "1.0", "Edm.Decimal")]
	[TestCase("1", "1.0", null)]
	[TestCase("\"12.50\"", "12.5", "Edm.Decimal")]
	[TestCase("0.5", "0.5", "Edm.Double")]
	[TestCase("null", "null", "Edm.Guid")]
	[TestCase("true", "true", "Edm.Boolean")]
	[TestCase("\"Acme\"", "\"Acme\"", "Edm.String")]
	[TestCase("\"2024-03-05\"", "\"2024-03-05\"", "Edm.Date")]
	[TestCase("\"2024-03-05\"", "\"2024-03-05T00:00:00Z\"", "Edm.Date")]
	[TestCase("null", "\"\"", "Edm.String")]
	[TestCase("null", "0", "Edm.Int32")]
	[TestCase("null", "0.0", null)]
	[TestCase("null", "false", "Edm.Boolean")]
	[TestCase("null", "\"00000000-0000-0000-0000-000000000000\"", "Edm.Guid")]
	[Description("Values that are the same value in another form its type allows are the same, so a read-back reports no false divergence; a null clear reads back as the column's empty default.")]
	public void Match_Should_Accept_The_Same_Value_In_Another_Form(string sent, string stored, string edmType) {
		// Arrange
		JsonElement sentValue = Json(sent);
		JsonElement storedValue = Json(stored);

		// Act
		ODataValueMatch match = ODataStoredValueComparer.Match(sentValue, storedValue, edmType);

		// Assert
		match.Should().Be(ODataValueMatch.Same, because: $"{sent} and {stored} are the same {edmType ?? "untyped"} value");
	}

	[TestCase("\"4C7A6EAD-4EB8-4FC0-863E-98A664569FED\"", "\"4c7a6ead-4eb8-4fc0-863e-98a664569fed\"", null)]
	[TestCase("\"2024-01-01T04:00:00+02:00\"", "\"2024-01-01T02:00:00Z\"", null)]
	[TestCase("\"2024-01-01\"", "\"2024-01-01T05:00:00Z\"", null)]
	[TestCase("\"2024-01-01\"", "\"2023-12-31T22:00:00Z\"", "Edm.DateTimeOffset")]
	[TestCase("\"2024-01-01\"", "\"2024-01-01T12:00:00Z\"", "Edm.DateTimeOffset")]
	[TestCase("null", "\"00000000-0000-0000-0000-000000000000\"", null)]
	[Description("A match that holds only under an assumption - the column type is unknown, or the server's time zone decides a date-only value - is unconfirmed, never the same: the same text in a string column, or a rule's noon, would be another value.")]
	public void Match_Should_Leave_A_Match_That_Needs_The_Type_Or_The_Server_Zone_Unconfirmed(string sent, string stored,
		string edmType) {
		// Arrange
		JsonElement sentValue = Json(sent);
		JsonElement storedValue = Json(stored);

		// Act
		ODataValueMatch match = ODataStoredValueComparer.Match(sentValue, storedValue, edmType);

		// Assert
		match.Should().Be(ODataValueMatch.Unconfirmed,
			because: $"{stored} is the {edmType ?? "untyped"} value {sent} only under an assumption the read-back cannot check");
	}

	[TestCase("\"4c7a6ead-4eb8-4fc0-863e-98a664569fed\"", "\"16be3651-8fe2-4159-8dd0-a803d4683dd3\"", "Edm.Guid")]
	[TestCase("\"4c7a6ead-4eb8-4fc0-863e-98a664569fed\"", "\"16be3651-8fe2-4159-8dd0-a803d4683dd3\"", null)]
	[TestCase("\"4C7A6EAD-4EB8-4FC0-863E-98A664569FED\"", "\"4c7a6ead-4eb8-4fc0-863e-98a664569fed\"", "Edm.String")]
	[TestCase("\"Product\"", "\"Contact\"", "Edm.String")]
	[TestCase("\"Acme\"", "\"acme\"", "Edm.String")]
	[TestCase("\"Acme\"", "\"Acme \"", null)]
	[TestCase("\"2024-01-01T04:00:00+02:00\"", "\"2024-01-01T04:00:00Z\"", "Edm.DateTimeOffset")]
	[TestCase("\"2024-01-01\"", "\"2024-01-01T05:07:00Z\"", "Edm.DateTimeOffset")]
	[TestCase("1", "2", "Edm.Int32")]
	[TestCase("1e-30", "0", "Edm.Double")]
	[TestCase("1e-30", "0", null)]
	[TestCase("\"00000000-0000-0000-0000-000000000000\"", "null", "Edm.Guid")]
	[TestCase("true", "false", "Edm.Boolean")]
	[TestCase("\"1\"", "\"01\"", "Edm.String")]
	[TestCase("\"2024-01-01\"", "\"2024-01-03T00:00:00Z\"", "Edm.DateTimeOffset")]
	[TestCase("null", "\"kept\"", "Edm.String")]
	[TestCase("null", "\"00000000-0000-0000-0000-000000000000\"", "Edm.String")]
	[TestCase("null", "5", "Edm.Int32")]
	[TestCase("\"\"", "null", "Edm.String")]
	[Description("A different stored value is reported as a divergence: a case or whitespace change of a string, a number a decimal would round to zero, and the empty GUID written into a text column where null was sent.")]
	public void Match_Should_Reject_A_Different_Value(string sent, string stored, string edmType) {
		// Arrange
		JsonElement sentValue = Json(sent);
		JsonElement storedValue = Json(stored);

		// Act
		ODataValueMatch match = ODataStoredValueComparer.Match(sentValue, storedValue, edmType);

		// Assert
		match.Should().Be(ODataValueMatch.Different,
			because: $"{stored} is not the {edmType ?? "untyped"} value {sent} that was sent");
	}

	[TestCase("Edm.Stream", false)]
	[TestCase("Edm.Binary", false)]
	[TestCase("Edm.String", true)]
	[TestCase(null, true)]
	[Description("Binary columns are not read back: OData does not return their content for a comparison.")]
	public void IsComparable_Should_Exclude_Binary_Types(string edmType, bool expected) {
		// Act
		bool comparable = ODataStoredValueComparer.IsComparable(edmType);

		// Assert
		comparable.Should().Be(expected, because: $"{edmType ?? "an unknown type"} is {(expected ? "" : "not ")}comparable");
	}
}
