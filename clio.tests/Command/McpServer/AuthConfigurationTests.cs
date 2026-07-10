using Clio.Command.McpServer;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class AuthConfigurationTests
{
	private static readonly AuthEnvironment NoEnvironment = new(null, null, null, null, null);

	private static McpHttpServerCommandOptions Options(
		string authority = null,
		string audience = null,
		string requiredScopes = null,
		string issuer = null,
		bool allowInsecureMetadata = false) =>
		new() {
			AuthAuthority = authority,
			AuthAudience = audience,
			AuthRequiredScopes = requiredScopes,
			AuthIssuer = issuer,
			AuthAllowInsecureMetadata = allowInsecureMetadata
		};

	[Test]
	[Description("Enabled is false when neither the flag nor the env var configures an authority.")]
	public void Enabled_ShouldBeFalse_WhenNoAuthorityConfigured() {
		// Arrange
		AuthConfiguration sut = AuthConfiguration.Resolve(Options(), NoEnvironment);

		// Act
		bool enabled = sut.Enabled;

		// Assert
		enabled.Should().BeFalse(
			because: "authorization is fail-safe off when no discovery authority is configured");
	}

	[Test]
	[Description("Enabled is true when the authority is supplied via the CLI flag.")]
	public void Enabled_ShouldBeTrue_WhenAuthorityFromFlag() {
		// Arrange
		AuthConfiguration sut = AuthConfiguration.Resolve(
			Options(authority: "https://id.example"), NoEnvironment);

		// Act & Assert
		sut.Enabled.Should().BeTrue(because: "a configured authority enables the resource-server auth");
		sut.Authority.Should().Be("https://id.example",
			because: "the flag authority is carried through verbatim (trimmed)");
	}

	[Test]
	[Description("The authority falls back to the environment variable when the flag is blank.")]
	public void Resolve_ShouldUseEnvAuthority_WhenFlagBlank() {
		// Arrange
		AuthEnvironment env = new("https://env.example", null, null, null, null);

		// Act
		AuthConfiguration sut = AuthConfiguration.Resolve(Options(), env);

		// Assert
		sut.Authority.Should().Be("https://env.example",
			because: "the env var is the fallback source for the single-valued authority");
	}

	[Test]
	[Description("The CLI flag authority wins over the environment variable when both are set.")]
	public void Resolve_ShouldPreferFlagAuthority_OverEnv() {
		// Arrange
		AuthEnvironment env = new("https://env.example", null, null, null, null);

		// Act
		AuthConfiguration sut = AuthConfiguration.Resolve(
			Options(authority: "https://flag.example"), env);

		// Assert
		sut.Authority.Should().Be("https://flag.example",
			because: "an explicit flag takes precedence over the environment variable");
	}

	[Test]
	[Description("Audiences union the flag and env comma-sets, trimmed and non-empty.")]
	public void Resolve_ShouldUnionAudiences_FromFlagAndEnv() {
		// Arrange
		AuthEnvironment env = new(null, "creatio_ai_api , ", null, null, null);

		// Act
		AuthConfiguration sut = AuthConfiguration.Resolve(
			Options(authority: "https://id.example", audience: "clio_mcp_api"), env);

		// Assert
		sut.Audiences.Should().BeEquivalentTo(["clio_mcp_api", "creatio_ai_api"],
			because: "audiences union both sources via CommaSet, dropping blanks");
	}

	[Test]
	[Description("Required scopes union the flag and env comma-sets.")]
	public void Resolve_ShouldUnionRequiredScopes_FromFlagAndEnv() {
		// Arrange
		AuthEnvironment env = new(null, null, "mcp:admin", null, null);

		// Act
		AuthConfiguration sut = AuthConfiguration.Resolve(
			Options(authority: "https://id.example", requiredScopes: "mcp:tools"), env);

		// Assert
		sut.RequiredScopes.Should().BeEquivalentTo(["mcp:tools", "mcp:admin"],
			because: "all required scopes from both sources are collected");
	}

	[Test]
	[Description("Issuers union the flag and env comma-sets.")]
	public void Resolve_ShouldUnionIssuers_FromFlagAndEnv() {
		// Arrange
		AuthEnvironment env = new(null, null, null, "https://internal.svc:8080", null);

		// Act
		AuthConfiguration sut = AuthConfiguration.Resolve(
			Options(authority: "https://id.example", issuer: "https://id.example"), env);

		// Assert
		sut.Issuers.Should().BeEquivalentTo(["https://id.example", "https://internal.svc:8080"],
			because: "the public and internal issuers are both accepted");
	}

	[Test]
	[Description("RequireHttpsMetadata defaults to true when insecure metadata is not allowed.")]
	public void Resolve_ShouldRequireHttpsMetadata_ByDefault() {
		// Arrange
		AuthConfiguration sut = AuthConfiguration.Resolve(
			Options(authority: "https://id.example"), NoEnvironment);

		// Act & Assert
		sut.RequireHttpsMetadata.Should().BeTrue(
			because: "metadata is HTTPS-only unless the operator explicitly opts out");
	}

	[Test]
	[Description("The allow-insecure-metadata flag turns RequireHttpsMetadata off.")]
	public void Resolve_ShouldAllowHttpMetadata_WhenFlagSet() {
		// Arrange
		AuthConfiguration sut = AuthConfiguration.Resolve(
			Options(authority: "http://internal.svc:8080", allowInsecureMetadata: true), NoEnvironment);

		// Act & Assert
		sut.RequireHttpsMetadata.Should().BeFalse(
			because: "the flag opts into plain-HTTP metadata for an internal-DNS authority");
	}

	[Test]
	[Description("A truthy allow-insecure-metadata env var turns RequireHttpsMetadata off.")]
	public void Resolve_ShouldAllowHttpMetadata_WhenEnvTruthy() {
		// Arrange
		AuthEnvironment env = new(null, null, null, null, "1");

		// Act
		AuthConfiguration sut = AuthConfiguration.Resolve(
			Options(authority: "http://internal.svc:8080"), env);

		// Assert
		sut.RequireHttpsMetadata.Should().BeFalse(
			because: "a truthy env value ('1') opts into plain-HTTP metadata");
	}

	[Test]
	[Description("Blank whitespace inputs resolve to a disabled, empty configuration.")]
	public void Resolve_ShouldBeDisabledAndEmpty_WhenInputsBlank() {
		// Arrange
		AuthConfiguration sut = AuthConfiguration.Resolve(
			Options(authority: "   ", audience: " , ", requiredScopes: "", issuer: null), NoEnvironment);

		// Assert
		sut.Enabled.Should().BeFalse(because: "a whitespace-only authority is treated as unset");
		sut.Audiences.Should().BeEmpty(because: "blank comma-set entries are dropped");
		sut.RequiredScopes.Should().BeEmpty(because: "an empty scopes value yields no scopes");
	}
}
