using System;
using Clio.Command.McpServer;
using CommandLine;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Story 12 (FR-14) CLI-flag acceptance gate for the mcp-http credential-passthrough flags:
/// confirms the five new options parse under their kebab-case names, apply the ADR safe defaults
/// when unset, split their comma-set values through the shared resolvers, and fail the parse on a
/// non-parseable integer value (AC-01/AC-04/AC-ERR). The always-on <c>--port</c>/<c>--host</c>/
/// <c>--path</c> flags are covered by <see cref="McpHttpServerCommandTests"/>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class McpHttpServerCommandOptionsTests {

	// A parser that never writes help/errors to the console, so a deliberately-invalid parse in a
	// test does not spam stdout while still surfacing the NotParsed result.
	private static ParserResult<McpHttpServerCommandOptions> Parse(params string[] args) {
		using Parser parser = new(with => with.HelpWriter = null);
		return parser.ParseArguments<McpHttpServerCommandOptions>(args);
	}

	[Test]
	[Description("The five passthrough flags apply the ADR safe defaults (X-Integration-Credentials header, 5m TTL, 50 max-sessions, unset key/allowlist) when no arguments are provided (AC-04).")]
	public void Parse_ShouldApplyPassthroughDefaults_WhenNoArgumentsProvided() {
		// Arrange & Act
		ParserResult<McpHttpServerCommandOptions> result = Parse(Array.Empty<string>());

		// Assert
		McpHttpServerCommandOptions opts = null;
		result.WithParsed(o => opts = o);
		result.Tag.Should().Be(ParserResultType.Parsed,
			because: "mcp-http with no arguments must parse successfully using defaults");
		opts.Should().NotBeNull(because: "a successful parse yields the options instance");
		opts!.CredentialsHeaderName.Should().Be("X-Integration-Credentials",
			because: "the default credential header name is the ADR-specified X-Integration-Credentials");
		opts.SessionIdleTtl.Should().Be("5m",
			because: "the default idle-TTL flag value is 5 minutes per the ADR safe default");
		opts.MaxSessions.Should().Be(50,
			because: "the default maximum session-container count is the ADR safe default of 50");
		opts.PlatformApiKey.Should().BeNull(
			because: "the platform API key is unset by default (fail-closed: passthrough disabled)");
		opts.AllowedBaseUrls.Should().BeNull(
			because: "the allowed-base-urls allowlist is unset by default (baseline-only egress protection)");
	}

	[Test]
	[Description("The default --session-idle-ttl value resolves to the ADR 5-minute TimeSpan through SessionContainerCacheDefaults (AC-04).")]
	public void Parse_ShouldResolveDefaultIdleTtlToFiveMinutes_WhenSessionIdleTtlUnset() {
		// Arrange
		ParserResult<McpHttpServerCommandOptions> result = Parse(Array.Empty<string>());
		McpHttpServerCommandOptions opts = null;
		result.WithParsed(o => opts = o);

		// Act
		TimeSpan resolved = SessionContainerCacheDefaults.ResolveIdleTtl(opts!.SessionIdleTtl);

		// Assert
		resolved.Should().Be(TimeSpan.FromMinutes(5),
			because: "the default '5m' flag value must resolve to the ADR safe default of 5 minutes");
	}

	[Test]
	[Description("All five passthrough flags are accepted under their kebab-case long names and override the defaults (AC-01).")]
	public void Parse_ShouldUseProvidedKebabCaseValues_WhenAllPassthroughFlagsSpecified() {
		// Arrange
		string[] args = [
			"--credentials-header-name", "X-My-Credentials",
			"--platform-api-key", "key-one,key-two",
			"--allowed-base-urls", "https://acme.creatio.com,https://beta.creatio.com",
			"--session-idle-ttl", "10m",
			"--max-sessions", "100"
		];

		// Act
		ParserResult<McpHttpServerCommandOptions> result = Parse(args);

		// Assert
		McpHttpServerCommandOptions opts = null;
		result.WithParsed(o => opts = o);
		result.Tag.Should().Be(ParserResultType.Parsed,
			because: "every passthrough flag uses a valid kebab-case long name and value (CLIO001)");
		opts.Should().NotBeNull(because: "a successful parse yields the options instance");
		opts!.CredentialsHeaderName.Should().Be("X-My-Credentials",
			because: "the --credentials-header-name value must override the default");
		opts.PlatformApiKey.Should().Be("key-one,key-two",
			because: "the --platform-api-key value is captured verbatim for later comma-set resolution");
		opts.AllowedBaseUrls.Should().Be("https://acme.creatio.com,https://beta.creatio.com",
			because: "the --allowed-base-urls value is captured verbatim for later comma-set resolution");
		opts.SessionIdleTtl.Should().Be("10m",
			because: "the --session-idle-ttl value must override the default");
		opts.MaxSessions.Should().Be(100,
			because: "the --max-sessions integer value must override the default");
	}

	[Test]
	[Description("The parsed --platform-api-key comma-set resolves into trimmed, non-empty key entries (AC-01).")]
	public void Parse_ShouldResolvePlatformApiKeyCommaSet_WhenFlagHasMultipleEntries() {
		// Arrange
		ParserResult<McpHttpServerCommandOptions> result = Parse("--platform-api-key", " key-one , key-two ,");
		McpHttpServerCommandOptions opts = null;
		result.WithParsed(o => opts = o);

		// Act
		System.Collections.Generic.IReadOnlyList<string> keys =
			PlatformApiKeyConfiguration.Resolve(opts!.PlatformApiKey, environmentValue: null);

		// Assert
		keys.Should().Equal(["key-one", "key-two"],
			because: "the comma-set resolver splits on commas, trims each entry, and drops empty entries");
	}

	[Test]
	[Description("The parsed --allowed-base-urls comma-set resolves into trimmed, non-empty origin entries (AC-01).")]
	public void Parse_ShouldResolveAllowedBaseUrlsCommaSet_WhenFlagHasMultipleEntries() {
		// Arrange
		ParserResult<McpHttpServerCommandOptions> result =
			Parse("--allowed-base-urls", " https://acme.creatio.com , https://beta.creatio.com ");
		McpHttpServerCommandOptions opts = null;
		result.WithParsed(o => opts = o);

		// Act
		System.Collections.Generic.IReadOnlyList<string> origins =
			AllowedBaseUrlsConfiguration.Resolve(opts!.AllowedBaseUrls);

		// Assert
		origins.Should().Equal(["https://acme.creatio.com", "https://beta.creatio.com"],
			because: "the comma-set resolver splits on commas, trims each entry, and drops empty entries");
	}

	[Test]
	[Description("A suffixed --session-idle-ttl duration resolves to the matching TimeSpan (AC-01).")]
	public void Parse_ShouldResolveSuffixedDuration_WhenSessionIdleTtlIsProvided() {
		// Arrange
		ParserResult<McpHttpServerCommandOptions> result = Parse("--session-idle-ttl", "90s");
		McpHttpServerCommandOptions opts = null;
		result.WithParsed(o => opts = o);

		// Act
		TimeSpan resolved = SessionContainerCacheDefaults.ResolveIdleTtl(opts!.SessionIdleTtl);

		// Assert
		resolved.Should().Be(TimeSpan.FromSeconds(90),
			because: "the '90s' suffixed duration must resolve to 90 seconds");
	}

	[Test]
	[Description("A non-parseable --max-sessions integer value fails the parse (NotParsed), which the top-level handler surfaces as an Error and a non-zero exit (AC-ERR).")]
	public void Parse_ShouldFailParse_WhenMaxSessionsIsNotAnInteger() {
		// Arrange
		string[] args = ["--max-sessions", "not-a-number"];

		// Act
		ParserResult<McpHttpServerCommandOptions> result = Parse(args);

		// Assert
		result.Tag.Should().Be(ParserResultType.NotParsed,
			because: "an integer option given a non-numeric value must fail parsing and yield a non-zero exit (AC-ERR)");
	}
}
