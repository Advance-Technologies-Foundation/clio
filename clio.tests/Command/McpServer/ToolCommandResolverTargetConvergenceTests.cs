using System;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// AC-00 / TC-U-706 (ENG-95262, story 7) — the DEFECT test. One resolved target must produce ONE session
/// key whether it was reached by REGISTERED ENVIRONMENT NAME or by EXPLICIT URI.
/// <para>
/// Before the fix, <c>ToolCommandResolver.BuildCacheKey</c> built its identity as
/// <c>options.Environment ?? "default"</c> + <c>"|"</c> + <c>settings.Uri</c>, so a single target yielded
/// <c>myenv|http://x</c> through the name branch and <c>default|http://x</c> through the URI branch.
/// ENG-94529 put the uri INTO the identity (fixing a re-pointed environment handing back a stale client)
/// but did not make the two branches converge.
/// </para>
/// <para>
/// Why this is load-bearing for stage 7: every registry the stage moves to the parent is keyed by tenant.
/// With a split key, <c>compile-creatio</c> invoked by environment name and <c>compile-status</c> polled by
/// explicit URI land in different buckets, and the symptom is not an error — it is <c>compile-status</c>
/// answering "no such operation" for a compile that is running.
/// </para>
/// Both key derivations are asserted: <see cref="IToolCommandResolver.GetTenantKey"/> (what the per-tenant
/// execution lock and the operation registries key off) and the key <see cref="ISessionContainerCache.Acquire"/>
/// caches the container under (what the authenticated session is shared by).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ToolCommandResolverTargetConvergenceTests {

	private const string EnvironmentName = "convergence-env";
	private const string EnvironmentUri = "https://convergence.creatio.com";
	private const string Login = "Supervisor";
	private const string Password = "Supervisor";

	// Captures the exact key Acquire is called with while still delegating to a real cache, so the
	// resolution genuinely succeeds and the key we assert on is the one production would cache under.
	private sealed class KeyCapturingSessionCache : ISessionContainerCache {
		private readonly ISessionContainerCache _inner =
			new SessionContainerCache(SessionContainerCacheDefaults.IdleTtl, SessionContainerCacheDefaults.MaxSessions);

		public string LastAcquireKey { get; private set; }

		public IServiceProvider Acquire(string cacheKey, Func<IServiceProvider> factory) {
			LastAcquireKey = cacheKey;
			return _inner.Acquire(cacheKey, factory);
		}

		public void MarkInUse(string cacheKey) => _inner.MarkInUse(cacheKey);

		public void MarkAvailable(string cacheKey) => _inner.MarkAvailable(cacheKey);
	}

	private static ToolCommandResolver CreateResolver(ISessionContainerCache cache, string registeredUri) {
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.IsEnvironmentExists(EnvironmentName).Returns(true);
		settingsRepository.FindEnvironment(EnvironmentName).Returns(new EnvironmentSettings {
			Uri = registeredUri,
			Login = Login,
			Password = Password,
			IsNetCore = true
		});
		ISettingsBootstrapService settingsBootstrapService = Substitute.For<ISettingsBootstrapService>();
		settingsBootstrapService.GetReport().Returns(new SettingsBootstrapReport(
			"healthy", SettingsRepository.AppSettingsFile, EnvironmentName, EnvironmentName, 1, [], [], true, true));
		return new ToolCommandResolver(
			settingsRepository,
			settingsBootstrapService,
			Substitute.For<ICredentialContextAccessor>(),
			Substitute.For<ITargetUrlValidator>(),
			cache,
			new SessionTargetNormalizer());
	}

	// The two ways an agent reaches ONE target. The credential half of the key
	// (Login|Password|ClientId|AccessToken|AccessTokenType|Cookie|IsNetCore) is deliberately identical on
	// both sides, so the only thing that can differ is the TARGET half this AC is about.
	private static EnvironmentOptions ByRegisteredName() => new() { Environment = EnvironmentName };

	private static EnvironmentOptions ByExplicitUri(string uri) => new() {
		Uri = uri,
		Login = Login,
		Password = Password,
		IsNetCore = true
	};

	[Test]
	[Description("AC-00: a registered environment NAME and an explicit URI for the same target produce the same GetTenantKey, so the tenant-keyed registries stage 7 moves to the parent cannot be split at birth.")]
	public void GetTenantKey_ShouldBeIdentical_WhenSameTargetReachedByNameAndByExplicitUri() {
		// Arrange
		ToolCommandResolver resolver = CreateResolver(new KeyCapturingSessionCache(), EnvironmentUri);

		// Act
		string keyByName = resolver.GetTenantKey(ByRegisteredName());
		string keyByUri = resolver.GetTenantKey(ByExplicitUri(EnvironmentUri));

		// Assert
		keyByUri.Should().Be(keyByName,
			because: $"one resolved target must produce one tenant key; by-name was '{keyByName}' and "
				+ $"by-uri was '{keyByUri}', and a split here makes compile-status answer 'no such "
				+ "operation' for a compile that is running");
	}

	[Test]
	[Description("AC-00: the container cache key — the key the authenticated session itself is shared by — also converges for a target reached by name and by explicit URI.")]
	public void AcquireKey_ShouldBeIdentical_WhenSameTargetReachedByNameAndByExplicitUri() {
		// Arrange
		KeyCapturingSessionCache byNameCache = new();
		KeyCapturingSessionCache byUriCache = new();
		ToolCommandResolver byNameResolver = CreateResolver(byNameCache, EnvironmentUri);
		ToolCommandResolver byUriResolver = CreateResolver(byUriCache, EnvironmentUri);

		// Act
		byNameResolver.Resolve<CreateEntitySchemaCommand>(ByRegisteredName());
		byUriResolver.Resolve<CreateEntitySchemaCommand>(ByExplicitUri(EnvironmentUri));

		// Assert
		byUriCache.LastAcquireKey.Should().Be(byNameCache.LastAcquireKey,
			because: $"both branches resolve the same target, so they must share one cached container; "
				+ $"by-name cached under '{byNameCache.LastAcquireKey}' and by-uri under "
				+ $"'{byUriCache.LastAcquireKey}'");
	}

	[Test]
	[Description("AC-00: convergence survives the T-5 folds — an explicit URI that differs from the registered one only by scheme case, host case, an elided default port and a trailing slash still resolves to the same tenant key.")]
	public void GetTenantKey_ShouldBeIdentical_WhenExplicitUriDiffersOnlyByFoldedComponents() {
		// Arrange
		ToolCommandResolver resolver = CreateResolver(new KeyCapturingSessionCache(), EnvironmentUri);

		// Act
		string keyByName = resolver.GetTenantKey(ByRegisteredName());
		string keyByFoldedUri = resolver.GetTenantKey(ByExplicitUri("HTTPS://Convergence.Creatio.COM:443/"));

		// Assert
		keyByFoldedUri.Should().Be(keyByName,
			because: $"scheme case, host case, the elided https default port and one trailing slash are all "
				+ $"T-5 folds; by-name was '{keyByName}' and by-folded-uri was '{keyByFoldedUri}'");
	}

	[Test]
	[Description("AC-00 near-miss on the real path: an explicit URI whose host differs from the registered environment's host must NOT converge, because over-normalisation on a sticky worker is a credential crossover rather than a cache miss.")]
	public void GetTenantKey_ShouldDiffer_WhenExplicitUriPointsAtAnotherHost() {
		// Arrange
		ToolCommandResolver resolver = CreateResolver(new KeyCapturingSessionCache(), EnvironmentUri);

		// Act
		string keyByName = resolver.GetTenantKey(ByRegisteredName());
		string keyByOtherHost = resolver.GetTenantKey(ByExplicitUri("https://other.creatio.com"));

		// Assert
		keyByOtherHost.Should().NotBe(keyByName,
			because: "two different hosts are two different targets and must never share a worker or a session");
	}
}
