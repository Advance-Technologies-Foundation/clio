using Clio;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class JsonFlagNormalizationTests {

	[Test]
	[Description("NormalizeJsonFlagArgs should inject 'true' after a bare --json so it parses as a flag")]
	public void NormalizeJsonFlagArgs_ShouldInjectTrue_WhenBareJson() {
		string[] result = Program.NormalizeJsonFlagArgs(new[] { "list-packages", "-e", "bk", "--json" });
		result.Should().Equal(new[] { "list-packages", "-e", "bk", "--json", "true" },
			because: "a bare --json is normalized to the explicit true form so the parser accepts it");
	}

	[Test]
	[Description("NormalizeJsonFlagArgs should leave an explicit --json true unchanged (strict back-compat)")]
	public void NormalizeJsonFlagArgs_ShouldKeepExplicitTrue_WhenJsonTrue() {
		string[] result = Program.NormalizeJsonFlagArgs(new[] { "list-packages", "-e", "bk", "--json", "true" });
		result.Should().Equal(new[] { "list-packages", "-e", "bk", "--json", "true" },
			because: "the established --json true form must be preserved byte-for-byte");
	}

	[Test]
	[Description("NormalizeJsonFlagArgs should leave an explicit --json false unchanged")]
	public void NormalizeJsonFlagArgs_ShouldKeepExplicitFalse_WhenJsonFalse() {
		string[] result = Program.NormalizeJsonFlagArgs(new[] { "list-packages", "--json", "false" });
		result.Should().Equal(new[] { "list-packages", "--json", "false" },
			because: "--json false must remain the non-json text path");
	}

	[Test]
	[Description("NormalizeJsonFlagArgs should inject 'true' for the -j short alias too")]
	public void NormalizeJsonFlagArgs_ShouldInjectTrue_WhenBareShortAlias() {
		string[] result = Program.NormalizeJsonFlagArgs(new[] { "list-packages", "-j" });
		result.Should().Equal(new[] { "list-packages", "-j", "true" },
			because: "the -j short flag gets the same bare-flag treatment");
	}

	[Test]
	[Description("NormalizeJsonFlagArgs must NOT let a bare --json swallow a following positional argument")]
	public void NormalizeJsonFlagArgs_ShouldNotEatPositional_WhenBareJsonFollowedByValue() {
		string[] result = Program.NormalizeJsonFlagArgs(new[] { "list-packages", "--json", "MyEnv" });
		result.Should().Equal(new[] { "list-packages", "--json", "true", "MyEnv" },
			because: "MyEnv is a positional argument, not the --json value; true is injected between them");
	}

	[Test]
	[Description("IsJsonOutputRequested should be true for a bare --json and for --json true, and false for --json false or absence")]
	public void IsJsonOutputRequested_ShouldReflectTheResolvedValue() {
		Program.IsJsonOutputRequested(new[] { "list-packages", "--json" }).Should().BeTrue(because: "bare --json enables json output");
		Program.IsJsonOutputRequested(new[] { "list-packages", "--json", "true" }).Should().BeTrue(because: "--json true enables json output");
		Program.IsJsonOutputRequested(new[] { "list-packages", "-j" }).Should().BeTrue(because: "bare -j enables json output");
		Program.IsJsonOutputRequested(new[] { "list-packages", "--json", "false" }).Should().BeFalse(because: "--json false stays on the text path");
		Program.IsJsonOutputRequested(new[] { "list-packages", "-e", "bk" }).Should().BeFalse(because: "no --json means no json output");
	}

}
