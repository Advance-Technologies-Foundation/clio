using System;
using System.Collections.Generic;
using Clio.Command.McpServer;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PlatformApiKeyGateTests
{
	private const string PrimaryKey = "primary-platform-key";
	private const string RotationKey = "rotation-platform-key";

	private static IPlatformApiKeyGate CreateGate(params string[] keys) =>
		new PlatformApiKeyGate(keys);

	[Test]
	[Description("PassthroughEnabled is false when the gate is built with no configured keys.")]
	public void PassthroughEnabled_ShouldBeFalse_WhenNoKeysConfigured() {
		// Arrange
		IPlatformApiKeyGate sut = CreateGate();

		// Act
		bool enabled = sut.PassthroughEnabled;

		// Assert
		enabled.Should().BeFalse(
			because: "with no configured key the gate is fail-closed and passthrough is disabled");
	}

	[Test]
	[Description("PassthroughEnabled is true when at least one key is configured.")]
	public void PassthroughEnabled_ShouldBeTrue_WhenAtLeastOneKeyConfigured() {
		// Arrange
		IPlatformApiKeyGate sut = CreateGate(PrimaryKey);

		// Act
		bool enabled = sut.PassthroughEnabled;

		// Assert
		enabled.Should().BeTrue(
			because: "a single configured key is enough to enable passthrough mode (AC-05)");
	}

	[Test]
	[Description("IsAuthorized is true when the Bearer token exactly matches the single configured key.")]
	public void IsAuthorized_ShouldReturnTrue_WhenBearerTokenMatchesConfiguredKey() {
		// Arrange
		IPlatformApiKeyGate sut = CreateGate(PrimaryKey);

		// Act
		bool authorized = sut.IsAuthorized($"Bearer {PrimaryKey}");

		// Assert
		authorized.Should().BeTrue(
			because: "a request presenting the exact configured key must be authorized (AC-01)");
	}

	[Test]
	[Description("IsAuthorized matches the scheme case-insensitively.")]
	public void IsAuthorized_ShouldReturnTrue_WhenSchemeCasingDiffers() {
		// Arrange
		IPlatformApiKeyGate sut = CreateGate(PrimaryKey);

		// Act
		bool authorized = sut.IsAuthorized($"bEaReR {PrimaryKey}");

		// Assert
		authorized.Should().BeTrue(
			because: "the Bearer scheme comparison is case-insensitive per the HTTP auth spec");
	}

	[Test]
	[Description("IsAuthorized is true when the presented key matches any member of a comma-configured rotation set.")]
	public void IsAuthorized_ShouldReturnTrue_WhenTokenMatchesAnyRotationKey() {
		// Arrange
		IPlatformApiKeyGate sut = CreateGate(PrimaryKey, RotationKey);

		// Act
		bool authorizedPrimary = sut.IsAuthorized($"Bearer {PrimaryKey}");
		bool authorizedRotation = sut.IsAuthorized($"Bearer {RotationKey}");

		// Assert
		authorizedPrimary.Should().BeTrue(
			because: "the first member of the rotation set must authorize (AC-04)");
		authorizedRotation.Should().BeTrue(
			because: "any member of the rotation set must authorize to support key rotation (AC-04)");
	}

	[Test]
	[Description("IsAuthorized is false when the Bearer token does not match any configured key.")]
	public void IsAuthorized_ShouldReturnFalse_WhenTokenMismatches() {
		// Arrange
		IPlatformApiKeyGate sut = CreateGate(PrimaryKey, RotationKey);

		// Act
		bool authorized = sut.IsAuthorized("Bearer some-other-key");

		// Assert
		authorized.Should().BeFalse(
			because: "a token matching no configured key must be rejected (AC-03)");
	}

	[Test]
	[Description("IsAuthorized is false when the Authorization value uses a non-Bearer scheme.")]
	public void IsAuthorized_ShouldReturnFalse_WhenSchemeIsNotBearer() {
		// Arrange
		IPlatformApiKeyGate sut = CreateGate(PrimaryKey);

		// Act
		bool authorized = sut.IsAuthorized($"Basic {PrimaryKey}");

		// Assert
		authorized.Should().BeFalse(
			because: "only the Bearer scheme carries the platform API key (AC-03)");
	}

	[Test]
	[Description("IsAuthorized is false when the token equals a key but omits the Bearer scheme.")]
	public void IsAuthorized_ShouldReturnFalse_WhenSchemeIsMissing() {
		// Arrange
		IPlatformApiKeyGate sut = CreateGate(PrimaryKey);

		// Act
		bool authorized = sut.IsAuthorized(PrimaryKey);

		// Assert
		authorized.Should().BeFalse(
			because: "a bare key without the Bearer scheme is not a valid Authorization value (AC-03)");
	}

	[Test]
	[Description("IsAuthorized is false when the Authorization value is blank.")]
	public void IsAuthorized_ShouldReturnFalse_WhenValueIsBlank() {
		// Arrange
		IPlatformApiKeyGate sut = CreateGate(PrimaryKey);

		// Act
		bool authorized = sut.IsAuthorized("   ");

		// Assert
		authorized.Should().BeFalse(
			because: "a blank Authorization value carries no key and must be rejected (AC-03)");
	}

	[Test]
	[Description("IsAuthorized is false when the Authorization value is null.")]
	public void IsAuthorized_ShouldReturnFalse_WhenValueIsNull() {
		// Arrange
		IPlatformApiKeyGate sut = CreateGate(PrimaryKey);

		// Act
		bool authorized = sut.IsAuthorized(null);

		// Assert
		authorized.Should().BeFalse(
			because: "a missing Authorization header carries no key and must be rejected (AC-03)");
	}

	[Test]
	[Description("IsAuthorized is false when the Bearer scheme is present but the token is empty.")]
	public void IsAuthorized_ShouldReturnFalse_WhenTokenIsEmptyAfterScheme() {
		// Arrange
		IPlatformApiKeyGate sut = CreateGate(PrimaryKey);

		// Act
		bool authorized = sut.IsAuthorized("Bearer    ");

		// Assert
		authorized.Should().BeFalse(
			because: "an empty token after the scheme cannot match a configured key (AC-03)");
	}

	[Test]
	[Description("IsAuthorized never surfaces the configured key material in its behavior for an invalid attempt.")]
	public void IsAuthorized_ShouldNotEchoKeyMaterial_WhenRejecting() {
		// Arrange
		IPlatformApiKeyGate sut = CreateGate(PrimaryKey);

		// Act
		Action act = () => sut.IsAuthorized("Bearer wrong");

		// Assert
		act.Should().NotThrow(
			because: "rejection is a plain boolean result and must not throw or leak the key (FR-11)");
	}
}

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PlatformApiKeyConfigurationTests
{
	[Test]
	[Description("Resolve returns an empty set when neither the flag nor the env var is set.")]
	public void Resolve_ShouldReturnEmpty_WhenNoSourceProvided() {
		// Arrange & Act
		IReadOnlyList<string> keys = PlatformApiKeyConfiguration.Resolve(null, null);

		// Assert
		keys.Should().BeEmpty(
			because: "with no configured source there is no key and passthrough stays disabled (AC-05)");
	}

	[Test]
	[Description("Resolve returns the flag key when only the CLI flag is provided.")]
	public void Resolve_ShouldReturnFlagKey_WhenOnlyFlagProvided() {
		// Arrange & Act
		IReadOnlyList<string> keys = PlatformApiKeyConfiguration.Resolve("flag-key", null);

		// Assert
		keys.Should().ContainSingle().Which.Should().Be("flag-key",
			because: "the --platform-api-key flag enables passthrough on its own (AC-05)");
	}

	[Test]
	[Description("Resolve returns the env-var key when only the environment variable is provided.")]
	public void Resolve_ShouldReturnEnvKey_WhenOnlyEnvProvided() {
		// Arrange & Act
		IReadOnlyList<string> keys = PlatformApiKeyConfiguration.Resolve(null, "env-key");

		// Assert
		keys.Should().ContainSingle().Which.Should().Be("env-key",
			because: "the CLIO_MCP_HTTP_PLATFORM_API_KEY env var enables passthrough on its own (AC-05)");
	}

	[Test]
	[Description("Resolve unions both sources, splitting comma sets and trimming each entry.")]
	public void Resolve_ShouldUnionTrimAndSplit_WhenBothSourcesProvided() {
		// Arrange & Act
		IReadOnlyList<string> keys = PlatformApiKeyConfiguration.Resolve(" a , b ", "c, d");

		// Assert
		keys.Should().Equal(["a", "b", "c", "d"],
			because: "both comma-separated sources are unioned and each entry is trimmed");
	}

	[Test]
	[Description("Resolve drops empty entries produced by stray commas and whitespace.")]
	public void Resolve_ShouldDropEmptyEntries_WhenSourceHasStrayCommas() {
		// Arrange & Act
		IReadOnlyList<string> keys = PlatformApiKeyConfiguration.Resolve("a,, ,b", "  ");

		// Assert
		keys.Should().Equal(["a", "b"],
			because: "empty and whitespace-only entries carry no key and must be dropped");
	}
}
