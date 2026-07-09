using Clio;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.UserEnvironment;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit coverage for the cache-key builders in <see cref="ToolCommandResolver"/> (Story 8, FR-07/FR-11):
/// two requests to the same URL with distinct tokens must produce distinct keys (no cross-tenant
/// collision), and the key must be SHA-256 hashed so no raw secret appears in it.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ToolCommandResolverCacheKeyTests {

	private const string Url = "https://tenant.creatio.com";
	private const string TokenOne = "tenant-one-secret-bearer-token";
	private const string TokenTwo = "tenant-two-secret-bearer-token";

	[Test]
	[Description("The legacy cache key differs for the same URL with distinct access tokens (FR-07).")]
	public void BuildCacheKey_ShouldDifferForSameUrlDistinctTokens_WhenTokensDiffer() {
		// Arrange
		EnvironmentOptions options = new();
		EnvironmentSettings first = new() { Uri = Url, AccessToken = TokenOne };
		EnvironmentSettings second = new() { Uri = Url, AccessToken = TokenTwo };

		// Act
		string keyOne = ToolCommandResolver.BuildCacheKey(options, first);
		string keyTwo = ToolCommandResolver.BuildCacheKey(options, second);

		// Assert
		keyOne.Should().NotBe(keyTwo,
			because: "two tokens on the same URL must resolve to distinct containers, not collide");
	}

	[Test]
	[Description("The legacy cache key never contains the raw token, only its SHA-256 hash (FR-11).")]
	public void BuildCacheKey_ShouldBeSecretFree_WhenTokenIsPresent() {
		// Arrange
		EnvironmentOptions options = new();
		EnvironmentSettings settings = new() { Uri = Url, AccessToken = TokenOne };

		// Act
		string key = ToolCommandResolver.BuildCacheKey(options, settings);

		// Assert
		key.Should().NotContain(TokenOne,
			because: "the credential material is hashed before it is placed in the key");
	}

	[Test]
	[Description("The passthrough cache key differs for the same URL with distinct access tokens (FR-07).")]
	public void BuildPassthroughCacheKey_ShouldDifferForSameUrlDistinctTokens_WhenTokensDiffer() {
		// Arrange
		CredentialContext first = new(Url,
			CredentialMaterial.FromAccessToken(TokenOne, "Bearer"), McpTransport.Http, true);
		CredentialContext second = new(Url,
			CredentialMaterial.FromAccessToken(TokenTwo, "Bearer"), McpTransport.Http, true);

		// Act
		string keyOne = ToolCommandResolver.BuildPassthroughCacheKey(first);
		string keyTwo = ToolCommandResolver.BuildPassthroughCacheKey(second);

		// Assert
		keyOne.Should().NotBe(keyTwo,
			because: "two passthrough tokens on the same URL must resolve to distinct containers");
	}

	[Test]
	[Description("The passthrough cache key never contains the raw token, only its SHA-256 hash (FR-11).")]
	public void BuildPassthroughCacheKey_ShouldBeSecretFree_WhenTokenIsPresent() {
		// Arrange
		CredentialContext context = new(Url,
			CredentialMaterial.FromAccessToken(TokenOne, "Bearer"), McpTransport.Http, true);

		// Act
		string key = ToolCommandResolver.BuildPassthroughCacheKey(context);

		// Assert
		key.Should().NotContain(TokenOne,
			because: "the credential material is hashed before it is placed in the key");
	}
}
