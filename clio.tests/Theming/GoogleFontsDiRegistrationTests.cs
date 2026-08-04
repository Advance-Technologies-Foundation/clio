using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Net.Http;
using Clio;
using Clio.Tests.Infrastructure;
using Clio.Theming;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Theming;

/// <summary>
/// Locks the DI lifetime contract of the Google Fonts availability seam (see
/// <c>CredentialPassthroughDiRegistrationTests</c> for the pattern): the verdict memo must be a
/// singleton so it survives across CLI/MCP calls, while the probing catalog stays a transient typed
/// HTTP client. A registration change that silently demotes the cache to transient would re-probe
/// every family on every call with no other test or runtime signal — it must fail here.
/// The same applies to the four probe guards configured on that typed client (budget, user agent,
/// redirect refusal, cookie refusal): the client is private to <c>GoogleFontsCatalog</c>, so the container is the only
/// place they can be observed, and dropping them leaves every other test green.
/// </summary>
[TestFixture]
[Category("Unit")]
[NonParallelizable]
[Property("Module", "Theming")]
public sealed class GoogleFontsDiRegistrationTests {

	[Test]
	[Description("Resolves ONE availability cache for the whole container (across root and scopes) while each catalog resolution is a fresh transient typed client sharing it.")]
	public void Container_Should_ShareSingletonAvailabilityCache_AcrossTransientCatalogs() {
		// Arrange
		IFileSystem originalFileSystem = SettingsRepository.FileSystem;

		try {
			MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
			SettingsRepository.FileSystem = fileSystem;
			IServiceCollection services = new ServiceCollection();
			new BindingsModule(fileSystem).RegisterInto(services);

			// Act
			using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions {
				ValidateOnBuild = true,
				ValidateScopes = true
			});
			IGoogleFontsAvailabilityCache rootCache = provider.GetRequiredService<IGoogleFontsAvailabilityCache>();
			using IServiceScope firstScope = provider.CreateScope();
			using IServiceScope secondScope = provider.CreateScope();

			// Assert
			firstScope.ServiceProvider.GetRequiredService<IGoogleFontsAvailabilityCache>().Should().BeSameAs(rootCache,
				"because the verdict memo only saves round trips if every resolution shares the one process-wide instance");
			secondScope.ServiceProvider.GetRequiredService<IGoogleFontsAvailabilityCache>().Should().BeSameAs(rootCache,
				"because a scoped or transient cache would silently re-probe every family on every CLI/MCP call");
			provider.GetRequiredService<IGoogleFontsCatalog>().Should().NotBeSameAs(
				provider.GetRequiredService<IGoogleFontsCatalog>(),
				"because the catalog is a typed HTTP client and must stay transient so its handler keeps rotating");
		}
		finally {
			SettingsRepository.FileSystem = originalFileSystem;
		}
	}

	[Test]
	[Description("Two independently built containers share ONE availability memo. The MCP resolver builds a container per tenant and the session cache evicts them, so a per-container singleton would re-probe for every tenant and forget everything on eviction.")]
	public void AvailabilityCache_ShouldBeShared_AcrossIndependentlyBuiltContainers() {
		// Arrange
		IFileSystem originalFileSystem = SettingsRepository.FileSystem;

		try {
			MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
			SettingsRepository.FileSystem = fileSystem;
			IServiceCollection first = new ServiceCollection();
			IServiceCollection second = new ServiceCollection();
			new BindingsModule(fileSystem).RegisterInto(first);
			new BindingsModule(fileSystem).RegisterInto(second);

			// Act
			using ServiceProvider firstProvider = first.BuildServiceProvider();
			using ServiceProvider secondProvider = second.BuildServiceProvider();

			// Assert
			secondProvider.GetRequiredService<IGoogleFontsAvailabilityCache>().Should()
				.BeSameAs(firstProvider.GetRequiredService<IGoogleFontsAvailabilityCache>(),
					because: "the memo is only worth having if a verdict probed for one tenant is still there for the next container built");
		}
		finally {
			SettingsRepository.FileSystem = originalFileSystem;
		}
	}

	[Test]
	[Description("Resolves the typed probe client and its primary handler from the container and locks the four guards BindingsModule configures on them: the short probe budget, the clio user agent, the refusal to follow redirects, and the refusal to keep cookies.")]
	public void ProbeClient_Should_CarryProbeBudgetAndUserAgent_AndRefuseRedirectsAndCookies() {
		// Arrange
		IFileSystem originalFileSystem = SettingsRepository.FileSystem;

		try {
			MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
			SettingsRepository.FileSystem = fileSystem;
			IServiceCollection services = new ServiceCollection();
			new BindingsModule(fileSystem).RegisterInto(services);

			// Act
			using ServiceProvider provider = services.BuildServiceProvider();
			using HttpClient client = provider.GetRequiredService<IHttpClientFactory>()
				.CreateClient(nameof(IGoogleFontsCatalog));
			HttpMessageHandler primaryHandler = PrimaryHandlerOf(provider
				.GetRequiredService<IHttpMessageHandlerFactory>()
				.CreateHandler(nameof(IGoogleFontsCatalog)));

			// Assert
			client.Timeout.Should().Be(GoogleFontsCatalog.ProbeTimeout,
				"because the probe budget is the only bound on the synchronous WhenAll the theme build performs — losing it would stall a build on HttpClient's 100 s default");
			client.DefaultRequestHeaders.UserAgent.ToString().Should().Contain("clio",
				"because the undocumented metadata endpoint must see an identifiable caller instead of an anonymous agent-less request");
			HttpClientHandler typedHandler = primaryHandler.Should().BeOfType<HttpClientHandler>(
					"because the redirect and cookie guards live on the primary handler the factory builds for this client")
				.Subject;
			typedHandler.AllowAutoRedirect.Should().BeFalse(
				"because a followed captive-portal or proxy redirect would answer 200 and read as InCatalog instead of Unverified");
			typedHandler.UseCookies.Should().BeFalse(
				"because the factory pools this handler, so a consent or tracking cookie set by fonts.google.com would be replayed on every later probe by every caller");
		}
		finally {
			SettingsRepository.FileSystem = originalFileSystem;
		}
	}

	private static HttpMessageHandler PrimaryHandlerOf(HttpMessageHandler handler) {
		while (handler is DelegatingHandler delegatingHandler) {
			handler = delegatingHandler.InnerHandler;
		}
		return handler;
	}
}
