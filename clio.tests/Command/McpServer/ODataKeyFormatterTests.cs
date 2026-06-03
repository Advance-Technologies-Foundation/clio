using System.Text.Json;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class ODataKeyFormatterTests {
	private const string Guid = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";

	[Test]
	[Category("Unit")]
	[Description("A GUID key is emitted unquoted for an addressed OData segment.")]
	public void FormatEntityKey_Should_Leave_Guid_Unquoted() =>
		ODataKeyFormatter.FormatEntityKey(Guid).Should().Be(Guid,
			because: "OData v4 keys for GUID-typed primary columns must be unquoted");

	[Test]
	[Category("Unit")]
	[Description("A numeric key is emitted unquoted.")]
	public void FormatEntityKey_Should_Leave_Number_Unquoted() =>
		ODataKeyFormatter.FormatEntityKey("1024").Should().Be("1024",
			because: "numeric keys are emitted as raw literals, not quoted strings");

	[Test]
	[Category("Unit")]
	[Description("A non-GUID, non-numeric key is single-quoted with embedded quotes escaped.")]
	public void FormatEntityKey_Should_Quote_And_Escape_String_Key() =>
		ODataKeyFormatter.FormatEntityKey("O'Brien").Should().Be("'O''Brien'",
			because: "string keys must be single-quoted with embedded apostrophes doubled per OData syntax");

	[Test]
	[Category("Unit")]
	[Description("GUID detection matches canonical GUIDs and rejects other strings.")]
	public void IsGuid_Should_Detect_Canonical_Guids() {
		ODataKeyFormatter.IsGuid(Guid).Should().BeTrue(because: "a canonical GUID must be recognized");
		ODataKeyFormatter.IsGuid("not-a-guid").Should().BeFalse(because: "non-GUID strings must be rejected");
	}

	[Test]
	[Category("Unit")]
	[Description("Id-suffixed fields and navigation paths ending in Id are recognized.")]
	public void IsIdish_Should_Recognize_Id_Fields() {
		ODataKeyFormatter.IsIdish("AccountId").Should().BeTrue(because: "an Id-suffixed lookup column is a foreign key");
		ODataKeyFormatter.IsIdish("Account/Id").Should().BeTrue(because: "a navigation path ending in Id is a foreign key");
		ODataKeyFormatter.IsIdish("Name").Should().BeFalse(because: "a plain column is not a foreign key");
	}

	[Test]
	[Category("Unit")]
	[Description("A GUID literal in an Id-suffixed field is unquoted; a string literal in a plain field is single-quoted.")]
	public void LiteralFor_Should_Honor_Id_Field_Guid_Unquoting() {
		JsonElement guidValue = JsonDocument.Parse($"\"{Guid}\"").RootElement.Clone();
		JsonElement stringValue = JsonDocument.Parse("\"Acme\"").RootElement.Clone();
		ODataKeyFormatter.LiteralFor("AccountId", guidValue).Should().Be(Guid,
			because: "a GUID in an Id-suffixed field must be emitted unquoted");
		ODataKeyFormatter.LiteralFor("Name", stringValue).Should().Be("'Acme'",
			because: "a string in a plain field must be single-quoted");
	}

	[Test]
	[Category("Unit")]
	[Description("Valid OData entity-set identifiers pass the allowlist.")]
	public void IsValidEntityName_Should_Accept_Valid_Names() {
		ODataKeyFormatter.IsValidEntityName("Contact").Should().BeTrue(because: "simple identifiers are valid entity sets");
		ODataKeyFormatter.IsValidEntityName("_SysAdminUnit").Should().BeTrue(because: "an underscore prefix is allowed");
		ODataKeyFormatter.IsValidEntityName("UsrCustom123").Should().BeTrue(because: "digits after the first character are allowed");
	}

	[Test]
	[Category("Unit")]
	[Description("Names with path separators, query syntax or a leading digit are rejected to block URL injection.")]
	public void IsValidEntityName_Should_Reject_Invalid_Names() {
		ODataKeyFormatter.IsValidEntityName("../Service").Should().BeFalse(because: "path traversal must be blocked");
		ODataKeyFormatter.IsValidEntityName("Contact?$filter=1").Should().BeFalse(because: "query-option injection must be blocked");
		ODataKeyFormatter.IsValidEntityName("Contact(1)").Should().BeFalse(because: "key segments must not be smuggled via the entity name");
		ODataKeyFormatter.IsValidEntityName("1Contact").Should().BeFalse(because: "a leading digit is not a valid identifier");
		ODataKeyFormatter.IsValidEntityName(" ").Should().BeFalse(because: "blank names are invalid");
	}
}
