using System;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// FR-05 correctness invariant (FIX 4 / L3, ENG-93208): the key <see cref="IToolCommandResolver.GetTenantKey"/>
/// returns MUST equal the key <see cref="ISessionContainerCache.Acquire"/> caches the container under —
/// for BOTH the passthrough path (<c>BuildPassthroughCacheKey</c>) and the legacy/registry path
/// (<c>BuildCacheKey</c>). If the two ever drift, the per-tenant execution lock keys off a different
/// identity than the shared session it guards: the same tenant would get two locks (unsafe) or two
/// tenants would collapse onto one (needless serialization). This is the direct analog of B1's
/// fail-first identity test — it fails loudly the moment the two key derivations diverge.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class TenantKeyEquivalenceTests {

	private const string EnvironmentName = "equivalence-env";
	private const string Url = "https://equivalence.creatio.com";
	private const string Token = "equivalence-secret-bearer-token";

	// Captures the exact key Acquire is called with while delegating to a real cache so the resolved
	// command still comes from a real BindingsModule container (the resolution must actually succeed —
	// the key is what we assert on).
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

	private static ToolCommandResolver CreateResolver(
		KeyCapturingSessionCache cache, ICredentialContextAccessor credentialContextAccessor) {
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.IsEnvironmentExists(EnvironmentName).Returns(true);
		settingsRepository.FindEnvironment(EnvironmentName).Returns(new EnvironmentSettings {
			Uri = Url,
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true
		});
		ISettingsBootstrapService settingsBootstrapService = Substitute.For<ISettingsBootstrapService>();
		settingsBootstrapService.GetReport().Returns(new SettingsBootstrapReport(
			"healthy", SettingsRepository.AppSettingsFile, EnvironmentName, EnvironmentName, 1, [], [], true, true));
		return new ToolCommandResolver(
			settingsRepository,
			settingsBootstrapService,
			new NonInteractiveConsole(),
			credentialContextAccessor,
			Substitute.For<ITargetUrlValidator>(),
			cache);
	}

	[Test]
	[Description("On the legacy/registry path, GetTenantKey returns the SAME key the container is cached under.")]
	public void GetTenantKey_ShouldEqualAcquireKey_WhenLegacyRegistryPath() {
		// Arrange — null Current keeps the resolver on the non-passthrough (registry) branch.
		ICredentialContextAccessor accessor = Substitute.For<ICredentialContextAccessor>();
		KeyCapturingSessionCache cache = new();
		ToolCommandResolver resolver = CreateResolver(cache, accessor);
		EnvironmentOptions options = new() { Environment = EnvironmentName };

		// Act — resolve (captures the Acquire key) then compute the tenant key independently.
		resolver.Resolve<CreateEntitySchemaCommand>(options);
		string acquireKey = cache.LastAcquireKey;
		string tenantKey = resolver.GetTenantKey(options);

		// Assert
		tenantKey.Should().Be(acquireKey,
			because: "the per-tenant lock must key off the exact identity the registry container is cached under");
	}

	[TestCase(false, TestName = "GetTenantKey_ShouldEqualAcquireKey_WhenPassthroughFrameworkPath")]
	[TestCase(true, TestName = "GetTenantKey_ShouldEqualAcquireKey_WhenPassthroughCorePath")]
	[Description("On the passthrough path, GetTenantKey returns the SAME passthrough key the container is cached under for each runtime.")]
	public void GetTenantKey_ShouldEqualAcquireKey_WhenPassthroughPath(bool isNetCore) {
		// Arrange — a present credential context selects the passthrough branch in BOTH Resolve and GetTenantKey.
		ICredentialContextAccessor accessor = Substitute.For<ICredentialContextAccessor>();
		accessor.Current.Returns(new CredentialContext(
			Url, CredentialMaterial.FromAccessToken(Token, "Bearer"), isNetCore, McpTransport.Http, true));
		KeyCapturingSessionCache cache = new();
		ToolCommandResolver resolver = CreateResolver(cache, accessor);
		EnvironmentOptions options = new();

		// Act
		resolver.Resolve<CreateEntitySchemaCommand>(options);
		string acquireKey = cache.LastAcquireKey;
		string tenantKey = resolver.GetTenantKey(options);

		// Assert
		tenantKey.Should().Be(acquireKey,
			because: "the passthrough lock must key off the exact credential-discriminating identity the container is cached under");
		tenantKey.Should().StartWith("passthrough:",
			because: "the passthrough branch keys the container (and lock) under the passthrough discriminator");
	}
}
