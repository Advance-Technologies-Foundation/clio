using Clio.Common.ObjectRights;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.ObjectRights;

/// <summary>
/// The rules the object-rights commands share: the security/system name list the connected listing leaves out, and the
/// one-line rendering of names and service messages.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class ObjectRightsSupportTests {

	[TestCase("SysAdminUnit", true)]
	[TestCase("sysuserinrole", true)]
	[TestCase("UsrOrderRights", true)]
	[TestCase("UsrOrder", false)]
	[TestCase("Contact", false)]
	[Description("The guard matches a family prefix or suffix in any case, and nothing else.")]
	public void IsSecurityOrSystemObject_ShouldMatchTheFamilies_WhenGivenAName(string schemaName, bool expected) {
		// Act
		bool actual = ObjectRightsSupport.IsSecurityOrSystemObject(schemaName);

		// Assert
		actual.Should().Be(expected, because: $"'{schemaName}' {(expected ? "is" : "is not")} a security or system object");
	}

	[Test]
	[Description("A service message with line breaks is rendered on one line, so it cannot start a line of its own inside a result.")]
	public void DisplayError_ShouldKeepTheMessageOnOneLine_WhenItHasLineBreaks() {
		// Act
		string rendered = ObjectRightsSupport.DisplayError("Violation of PRIMARY KEY\r\nThe statement has been terminated.");

		// Assert
		rendered.Should().NotContain("\n", because: "a line break would start a new output line");
		rendered.Should().Contain("The statement has been terminated.", because: "the message itself is kept");
	}

	[Test]
	[Description("A service message is redacted before it is printed: the CLI and the log have no redaction pass of their own.")]
	public void DisplayError_ShouldRedactTheHost_WhenTheMessageCarriesARequestUri() {
		// Act
		string rendered = ObjectRightsSupport.DisplayError(
			"Unexpected response from https://tenant.example/0/ServiceModel/RightManagementService.svc/Save");

		// Assert
		rendered.Should().NotContain("tenant.example", because: "a request URI names the customer's host");
		rendered.Should().StartWith("Unexpected response from", because: "the readable part of the message is kept");
	}
}
