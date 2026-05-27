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
		ODataKeyFormatter.FormatEntityKey(Guid).Should().Be(Guid);

	[Test]
	[Category("Unit")]
	[Description("A numeric key is emitted unquoted.")]
	public void FormatEntityKey_Should_Leave_Number_Unquoted() =>
		ODataKeyFormatter.FormatEntityKey("1024").Should().Be("1024");

	[Test]
	[Category("Unit")]
	[Description("A non-GUID, non-numeric key is single-quoted with embedded quotes escaped.")]
	public void FormatEntityKey_Should_Quote_And_Escape_String_Key() =>
		ODataKeyFormatter.FormatEntityKey("O'Brien").Should().Be("'O''Brien'");

	[Test]
	[Category("Unit")]
	[Description("GUID detection matches canonical GUIDs and rejects other strings.")]
	public void IsGuid_Should_Detect_Canonical_Guids() {
		ODataKeyFormatter.IsGuid(Guid).Should().BeTrue();
		ODataKeyFormatter.IsGuid("not-a-guid").Should().BeFalse();
	}

	[Test]
	[Category("Unit")]
	[Description("Id-suffixed fields and navigation paths ending in Id are recognized.")]
	public void IsIdish_Should_Recognize_Id_Fields() {
		ODataKeyFormatter.IsIdish("AccountId").Should().BeTrue();
		ODataKeyFormatter.IsIdish("Account/Id").Should().BeTrue();
		ODataKeyFormatter.IsIdish("Name").Should().BeFalse();
	}

	[Test]
	[Category("Unit")]
	[Description("A GUID literal in an Id-suffixed field is unquoted; a string literal in a plain field is single-quoted.")]
	public void LiteralFor_Should_Honor_Id_Field_Guid_Unquoting() {
		JsonElement guidValue = JsonDocument.Parse($"\"{Guid}\"").RootElement.Clone();
		JsonElement stringValue = JsonDocument.Parse("\"Acme\"").RootElement.Clone();
		ODataKeyFormatter.LiteralFor("AccountId", guidValue).Should().Be(Guid);
		ODataKeyFormatter.LiteralFor("Name", stringValue).Should().Be("'Acme'");
	}
}
