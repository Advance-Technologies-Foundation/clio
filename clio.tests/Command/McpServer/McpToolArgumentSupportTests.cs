using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-95885. Pins the shared argument-support helpers, in particular the rejection-only environment
/// alias set: a non-canonical spelling must always produce a rename HINT and never a silent binding, so
/// the accepted field set stays exactly the canonical kebab-case one.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpToolArgumentSupportTests
{
	private static IReadOnlyDictionary<string, JsonElement> Overflow(params string[] keys) {
		Dictionary<string, JsonElement> bag = new(System.StringComparer.Ordinal);
		foreach (string key in keys) {
			bag[key] = JsonSerializer.SerializeToElement("value");
		}
		return bag;
	}

	[Test]
	[Category("Unit")]
	[Description("The bare 'environment' spelling — the one an agent writes when thinking of the CLI -e/--environment flag — maps to a rejection-only rename hint, not a silent binding (ENG-95885).")]
	public void BuildLegacyAliasError_ShouldRenameBareEnvironment_ToEnvironmentName() {
		// Arrange
		IReadOnlyDictionary<string, JsonElement> overflow = Overflow("environment");

		// Act
		string? error = McpToolArgumentSupport.BuildLegacyAliasError(
			overflow, McpToolArgumentSupport.EnvironmentNameAliases, renameSuffix: ".", unknownHint: "Valid: environment-name.");

		// Assert
		error.Should().NotBeNull(
			because: "an overflow key that is a known alias must produce a corrective message, not pass silently");
		error.Should().Contain("'environment' -> 'environment-name'",
			because: "the bare 'environment' spelling is a rejection-only alias that must be reported as a rename");
	}

	[TestCase("environmentName")]
	[TestCase("environment_name")]
	[TestCase("environment")]
	[Category("Unit")]
	[Description("Every known environment-name misspelling maps to the canonical kebab-case name as a rejection-only rename (ENG-95885).")]
	public void EnvironmentNameAliases_ShouldMapEveryKnownMisspelling_ToCanonical(string misspelling) {
		// Act
		bool mapped = McpToolArgumentSupport.EnvironmentNameAliases.TryGetValue(misspelling, out string? canonical);

		// Assert
		mapped.Should().BeTrue(because: $"'{misspelling}' is a spelling an agent emits and must be recognized");
		canonical.Should().Be("environment-name",
			because: "all environment aliases resolve to the single canonical kebab-case field name");
	}

	[Test]
	[Category("Unit")]
	[Description("A genuinely unknown overflow key is reported in the 'Unknown args' branch with the supplied valid-field hint, not silently accepted (ENG-95885).")]
	public void BuildLegacyAliasError_ShouldListGenuinelyUnknownKey_WithValidFieldHint() {
		// Arrange
		IReadOnlyDictionary<string, JsonElement> overflow = Overflow("totally-made-up");

		// Act
		string? error = McpToolArgumentSupport.BuildLegacyAliasError(
			overflow, McpToolArgumentSupport.EnvironmentNameAliases, renameSuffix: ".", unknownHint: "Valid: environment-name.");

		// Assert
		error.Should().NotBeNull(because: "an unknown overflow key must still produce a corrective message");
		error.Should().Contain("Unknown args: 'totally-made-up'",
			because: "a key that matches no alias is reported as unknown rather than renamed");
		error.Should().Contain("Valid: environment-name.",
			because: "the caller-supplied valid-field hint must be appended so the agent can self-correct");
	}

	[Test]
	[Category("Unit")]
	[Description("A clean call with no overflow returns null so it passes straight through (ENG-95885).")]
	public void BuildLegacyAliasError_ShouldReturnNull_WhenNoOverflow() {
		// Act
		string? error = McpToolArgumentSupport.BuildLegacyAliasError(
			Overflow(), McpToolArgumentSupport.EnvironmentNameAliases, renameSuffix: ".", unknownHint: "Valid: environment-name.");

		// Assert
		error.Should().BeNull(because: "no unbound fields means nothing to flag and the call must not be disturbed");
	}
}
