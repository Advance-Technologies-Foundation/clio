using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Net.Http;
using System.Net.Security;
using Clio;
using Clio.Common;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Locks the transport contract of the client <see cref="EnvironmentAvailabilityProbe"/> resolves.
/// </summary>
/// <remarks>
/// The probe is the sole producer of <c>EnvironmentReachable</c>, which gates BOTH completion rules in
/// <see cref="CompilationCompletionDecider"/>. On the default registration it would be the one component of
/// the feature that validates server certificates — the compile POST and the verdict read both go through
/// creatio.client, which accepts any certificate — so on a self-signed stand the probe would fail forever, no
/// completion rule could fire, and an ordinary build would wait out the full timeout and exit 1. The client is
/// private to the probe, so the container is the only place this can be observed: a registration change that
/// drops the handler leaves every other test green.
/// </remarks>
[TestFixture]
[Category("Unit")]
[NonParallelizable]
[Property("Module", "Common")]
public sealed class EnvironmentAvailabilityProbeDiRegistrationTests {

	[Test]
	[Description("The probe's named client is built on a handler that accepts any server certificate and does not follow redirects, so a self-signed stand reads as reachable and a login redirect is not chased.")]
	public void ProbeClient_Should_AcceptAnyCertificate_AndRefuseRedirects() {
		// Arrange
		System.IO.Abstractions.IFileSystem originalFileSystem = SettingsRepository.FileSystem;

		try {
			MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
			SettingsRepository.FileSystem = fileSystem;
			IServiceCollection services = new ServiceCollection();
			new BindingsModule(fileSystem).RegisterInto(services);

			// Act
			using ServiceProvider provider = services.BuildServiceProvider();
			HttpMessageHandler primaryHandler = PrimaryHandlerOf(provider
				.GetRequiredService<IHttpMessageHandlerFactory>()
				.CreateHandler(EnvironmentAvailabilityProbe.HttpClientName));

			// Assert
			HttpClientHandler typedHandler = primaryHandler.Should().BeOfType<HttpClientHandler>(
					"because the certificate and redirect guards live on the primary handler the factory builds for this client")
				.Subject;
			typedHandler.ServerCertificateCustomValidationCallback.Should().NotBeNull(
				"because clio is routinely pointed at self-signed dev stands, and a probe that rejects them turns off every completion rule while the compile POST and the verdict read still succeed");
			typedHandler.ServerCertificateCustomValidationCallback
				.Invoke(new HttpRequestMessage(), null, null, SslPolicyErrors.RemoteCertificateChainErrors)
				.Should().BeTrue(
					"because an untrusted chain is the normal case on the stands this feature exists to help, not a reason to call the environment unreachable");
			typedHandler.AllowAutoRedirect.Should().BeFalse(
				"because a 302 to the login page already proves the application is answering, and following it only spends the probe's budget");
		} finally {
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
