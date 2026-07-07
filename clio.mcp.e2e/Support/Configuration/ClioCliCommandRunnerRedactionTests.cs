using Clio.Mcp.E2E.Support.Configuration;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>
/// Env-free unit coverage for <see cref="ClioCliCommandRunner.RedactSecrets"/>: the diagnostics blocks
/// echo the full clio command line on failure, and reg-web-app passes the sandbox login/password
/// verbatim, so the value following a secret flag must never reach the (TeamCity-retained) CI log.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
public sealed class ClioCliCommandRunnerRedactionTests {

	[Test]
	[Description("The value following -p and -l must be replaced with *** in the space-separated form.")]
	public void RedactSecrets_ShouldMaskValues_WhenSpaceSeparatedSecretFlagsPresent() {
		// Arrange
		string arguments = "reg-web-app dev -u https://stand -l admin -p s3cr3t";

		// Act
		string result = ClioCliCommandRunner.RedactSecrets(arguments);

		// Assert
		result.Should().Be("reg-web-app dev -u https://stand -l *** -p ***",
			because: "the login and password values must be redacted while the rest of the command is preserved");
		result.Should().NotContain("s3cr3t",
			because: "the plaintext password must never reach the CI log");
		result.Should().NotContain("admin",
			because: "the login value must also be redacted as a sensitive credential");
	}

	[Test]
	[Description("The inline --password=secret / --login=value form must also be redacted.")]
	public void RedactSecrets_ShouldMaskValues_WhenInlineSecretFlagsPresent() {
		// Arrange
		string arguments = "reg-web-app dev --login=admin --password=s3cr3t";

		// Act
		string result = ClioCliCommandRunner.RedactSecrets(arguments);

		// Assert
		result.Should().Be("reg-web-app dev --login=*** --password=***",
			because: "the inline secret form must be redacted just like the space-separated form");
		result.Should().NotContain("s3cr3t",
			because: "the plaintext password must never reach the CI log even in the inline form");
	}

	[Test]
	[Description("A command line without secret flags must be returned unchanged.")]
	public void RedactSecrets_ShouldReturnUnchanged_WhenNoSecretFlagsPresent() {
		// Arrange
		string arguments = "ping-app -e dev";

		// Act
		string result = ClioCliCommandRunner.RedactSecrets(arguments);

		// Assert
		result.Should().Be("ping-app -e dev",
			because: "a command line that carries no secret must pass through untouched");
	}
}
