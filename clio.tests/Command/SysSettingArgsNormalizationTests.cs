using Clio;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class SysSettingArgsNormalizationTests {

	[Test]
	[Description("NormalizeGetSysSettingArgs should inject --get so the get-syssetting alias reads instead of writing an empty value")]
	public void NormalizeGetSysSettingArgs_ShouldInjectGet_WhenInvokedViaGetSyssettingAlias() {
		// Arrange
		string[] args = { "get-syssetting", "Maintainer", "-e", "bk" };

		// Act
		string[] result = Program.NormalizeGetSysSettingArgs(args);

		// Assert
		result.Should().Equal(new[] { "get-syssetting", "Maintainer", "-e", "bk", "--get" },
			because: "the get-syssetting alias must resolve to the read path so it never overwrites the setting");
	}

	[Test]
	[Description("NormalizeGetSysSettingArgs should treat the alias case-insensitively")]
	public void NormalizeGetSysSettingArgs_ShouldInjectGet_WhenAliasCasingDiffers() {
		// Arrange
		string[] args = { "GET-SYSSETTING", "Maintainer" };

		// Act
		string[] result = Program.NormalizeGetSysSettingArgs(args);

		// Assert
		result.Should().Equal(new[] { "GET-SYSSETTING", "Maintainer", "--get" },
			because: "verb matching is case-insensitive, so the get-syssetting alias is normalized regardless of casing");
	}

	[Test]
	[Description("NormalizeGetSysSettingArgs should not add a second --get when the flag is already present")]
	public void NormalizeGetSysSettingArgs_ShouldNotDuplicateGet_WhenGetFlagAlreadyPresent() {
		// Arrange
		string[] args = { "get-syssetting", "Maintainer", "--get" };

		// Act
		string[] result = Program.NormalizeGetSysSettingArgs(args);

		// Assert
		result.Should().Equal(new[] { "get-syssetting", "Maintainer", "--get" },
			because: "a duplicate --get would break parsing; the flag is injected only when it is missing");
	}

	[Test]
	[Description("NormalizeGetSysSettingArgs should leave set-syssetting invocations untouched")]
	public void NormalizeGetSysSettingArgs_ShouldNotInjectGet_WhenInvokedViaSetSyssettingVerb() {
		// Arrange
		string[] args = { "set-syssetting", "Maintainer", "ATF" };

		// Act
		string[] result = Program.NormalizeGetSysSettingArgs(args);

		// Assert
		result.Should().Equal(new[] { "set-syssetting", "Maintainer", "ATF" },
			because: "only the get-syssetting alias implies read mode; set-syssetting must still write");
	}

	[Test]
	[Description("NormalizeGetSysSettingArgs should return empty args unchanged")]
	public void NormalizeGetSysSettingArgs_ShouldReturnUnchanged_WhenArgsAreEmpty() {
		// Arrange
		string[] args = System.Array.Empty<string>();

		// Act
		string[] result = Program.NormalizeGetSysSettingArgs(args);

		// Assert
		result.Should().BeSameAs(args,
			because: "with no verb there is nothing to normalize and the original array is returned");
	}

	[Test]
	[Description("NormalizeGetSysSettingArgs should tolerate a null args array")]
	public void NormalizeGetSysSettingArgs_ShouldReturnNull_WhenArgsAreNull() {
		// Arrange
		string[] args = null;

		// Act
		string[] result = Program.NormalizeGetSysSettingArgs(args);

		// Assert
		result.Should().BeNull(because: "a null args array has no verb to inspect and is passed through untouched");
	}

}
