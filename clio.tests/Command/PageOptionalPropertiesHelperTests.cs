namespace Clio.Tests.Command;

using Clio.Command;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageOptionalPropertiesHelperTests
{
	[Test]
	[Description("Parses a well-formed JSON array of key/value objects.")]
	public void TryParse_Returns_Array_When_Payload_Is_Valid() {
		// Arrange
		const string json = """[{"key":"DashboardsEntitySchemaName","value":"Contact"}]""";

		// Act
		bool result = PageOptionalPropertiesHelper.TryParse(json, out JArray parsed, out string error);

		// Assert
		result.Should().BeTrue("because a valid JSON array must be accepted");
		error.Should().BeNull("because there is no error on a valid payload");
		parsed.Should().HaveCount(1, "because the single supplied object must be parsed");
		parsed[0]["key"]!.ToString().Should().Be("DashboardsEntitySchemaName",
			because: "the parsed entry must preserve the supplied key");
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	[Description("Treats an absent/blank payload as not supplied.")]
	public void TryParse_Returns_Null_Array_When_Payload_Is_Absent(string json) {
		// Act
		bool result = PageOptionalPropertiesHelper.TryParse(json, out JArray parsed, out string error);

		// Assert
		result.Should().BeTrue("because an absent payload is valid (nothing to seed)");
		parsed.Should().BeNull("because there is nothing to parse when the payload is absent");
		error.Should().BeNull("because an absent payload is not an error");
	}

	[TestCase("{ not-an-array }")]
	[TestCase("not json at all")]
	[TestCase("""{"key":"x"}""")]
	[TestCase("[1,2,3]")]
	[TestCase("""["DashboardsEntitySchemaName"]""")]
	[TestCase("""[{"foo":1}]""")]
	[TestCase("""[{"kye":"DashboardsEntitySchemaName","value":"Contact"}]""")]
	[TestCase("""[{"key":"","value":"Contact"}]""")]
	[TestCase("""[{"key":"   ","value":"Contact"}]""")]
	[TestCase("""[{"key":"DashboardsEntitySchemaName","value":"Contact"}, 42]""")]
	[Description("Rejects a payload that is not a valid JSON array of objects with a non-blank key, using the canonical error.")]
	public void TryParse_Returns_Canonical_Error_When_Payload_Is_Malformed(string json) {
		// Act
		bool result = PageOptionalPropertiesHelper.TryParse(json, out JArray parsed, out string error);

		// Assert
		result.Should().BeFalse("because a malformed payload must be rejected");
		parsed.Should().BeNull("because nothing is parsed when the payload is malformed");
		error.Should().Be(PageOptionalPropertiesHelper.InvalidOptionalPropertiesError,
			because: "both create-page and update-page must report the same canonical error wording");
	}
}
