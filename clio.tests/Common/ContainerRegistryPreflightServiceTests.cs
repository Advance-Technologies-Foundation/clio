using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
public class ContainerRegistryPreflightServiceTests {
	[Test]
	[Description("ValidatePushTarget should succeed when the registry answers GET /v2/ and accepts upload initiation for the target repository.")]
	public void ValidatePushTarget_ShouldSucceedWhenRegistryAcceptsUploadProbe() {
		// Arrange
		IContainerRegistryCredentialProvider credentialProvider = Substitute.For<IContainerRegistryCredentialProvider>();
		credentialProvider.TryResolveCredentials(Arg.Any<string>(), Arg.Any<Uri>()).Returns((ContainerRegistryCredentials)null);
		using HttpClient httpClient = new(new StubHttpMessageHandler([
			new StubResponse(HttpMethod.Get, "https://registry.krylov.cloud/v2/", new HttpResponseMessage(HttpStatusCode.OK)),
			new StubResponse(HttpMethod.Post, "https://registry.krylov.cloud/v2/acme/creatio-prod/blobs/uploads/",
				new HttpResponseMessage(HttpStatusCode.Accepted) {
					Headers = {
						Location = new Uri("https://registry.krylov.cloud/v2/acme/creatio-prod/blobs/uploads/probe")
					}
				}),
			new StubResponse(HttpMethod.Delete, "https://registry.krylov.cloud/v2/acme/creatio-prod/blobs/uploads/probe",
				new HttpResponseMessage(HttpStatusCode.NoContent))
		]));
		ContainerRegistryPreflightService service = new(httpClient, credentialProvider);

		// Act
		ContainerRegistryPreflightResult result =
			service.ValidatePushTarget("registry.krylov.cloud/acme", "registry.krylov.cloud/acme/creatio-prod:1.0.0");

		// Assert
		result.Success.Should().BeTrue(
			"because a registry that accepts an upload initiation should be considered writable for the requested repository");
		result.Endpoint.Should().Be("https://registry.krylov.cloud/",
			"because the preflight should probe the registry API at the registry host, not at the repository namespace path");
	}

	[Test]
	[Description("ValidatePushTarget should surface authentication requirements when the registry requests auth for GET /v2/.")]
	public void ValidatePushTarget_ShouldReportAuthenticationRequirement() {
		// Arrange
		IContainerRegistryCredentialProvider credentialProvider = Substitute.For<IContainerRegistryCredentialProvider>();
		credentialProvider.TryResolveCredentials(Arg.Any<string>(), Arg.Any<Uri>()).Returns((ContainerRegistryCredentials)null);
		using HttpClient httpClient = new(new StubHttpMessageHandler([
			new StubResponse(HttpMethod.Get, "https://registry.krylov.cloud/v2/", new HttpResponseMessage(HttpStatusCode.Unauthorized))
		]));
		ContainerRegistryPreflightService service = new(httpClient, credentialProvider);

		// Act
		ContainerRegistryPreflightResult result =
			service.ValidatePushTarget("registry.krylov.cloud", "registry.krylov.cloud/creatio-prod:1.0.0");

		// Assert
		result.Success.Should().BeFalse(
			"because a registry that rejects GET /v2/ anonymously cannot be treated as a confirmed anonymous push target");
		result.RequiresAuthentication.Should().BeTrue(
			"because the registry explicitly requested authentication during the initial API probe");
		result.Message.Should().Contain("requires authentication",
			"because the caller needs a clear action when the registry is reachable but protected");
	}

	[Test]
	[Description("ValidatePushTarget should fall back from HTTPS to HTTP when the HTTPS endpoint is unreachable but the HTTP registry endpoint is available.")]
	public void ValidatePushTarget_ShouldFallbackToHttpWhenHttpsEndpointIsUnreachable() {
		// Arrange
		IContainerRegistryCredentialProvider credentialProvider = Substitute.For<IContainerRegistryCredentialProvider>();
		credentialProvider.TryResolveCredentials(Arg.Any<string>(), Arg.Any<Uri>()).Returns((ContainerRegistryCredentials)null);
		using HttpClient httpClient = new(new StubHttpMessageHandler([
			new StubResponse(HttpMethod.Get, "https://registry.krylov.cloud/v2/", new HttpRequestException("Connection refused")),
			new StubResponse(HttpMethod.Get, "http://registry.krylov.cloud/v2/", new HttpResponseMessage(HttpStatusCode.OK)),
			new StubResponse(HttpMethod.Post, "http://registry.krylov.cloud/v2/creatio-prod/blobs/uploads/",
				new HttpResponseMessage(HttpStatusCode.Accepted)),
		]));
		ContainerRegistryPreflightService service = new(httpClient, credentialProvider);

		// Act
		ContainerRegistryPreflightResult result =
			service.ValidatePushTarget("registry.krylov.cloud", "registry.krylov.cloud/creatio-prod:1.0.0");

		// Assert
		result.Success.Should().BeTrue(
			"because the command should still be able to preflight plain HTTP registries on trusted local networks");
		result.Endpoint.Should().Be("http://registry.krylov.cloud/",
			"because the fallback HTTP endpoint is the one that actually accepted the upload probe");
	}

	[Test]
	[Description("ValidatePushTarget should authenticate the probe with locally configured registry credentials when the registry rejects anonymous access.")]
	public void ValidatePushTarget_ShouldUseResolvedCredentialsWhenAnonymousProbeIsUnauthorized() {
		// Arrange
		IContainerRegistryCredentialProvider credentialProvider = Substitute.For<IContainerRegistryCredentialProvider>();
		credentialProvider.TryResolveCredentials(Arg.Any<string>(), Arg.Any<Uri>())
			.Returns(new ContainerRegistryCredentials("docker-publisher", "secret"));
		using HttpClient httpClient = new(new StubHttpMessageHandler([
			new StubResponse(HttpMethod.Get, "https://registry.krylov.cloud/v2/", new HttpResponseMessage(HttpStatusCode.OK)),
			new StubResponse(HttpMethod.Post, "https://registry.krylov.cloud/v2/creatio-dev/blobs/uploads/",
				new HttpResponseMessage(HttpStatusCode.Accepted))
		]));
		ContainerRegistryPreflightService service = new(httpClient, credentialProvider);

		// Act
		ContainerRegistryPreflightResult result =
			service.ValidatePushTarget("registry.krylov.cloud", "registry.krylov.cloud/creatio-dev:1.0.0");

		// Assert
		result.Success.Should().BeTrue(
			"because the preflight should reuse locally configured registry credentials when the CLI already knows how to authenticate");
	}

	private sealed record StubResponse(HttpMethod Method, string Uri, object Result);

	private sealed class StubHttpMessageHandler(IReadOnlyCollection<StubResponse> responses) : HttpMessageHandler {
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
			foreach (StubResponse response in responses) {
				if (request.Method == response.Method
					&& string.Equals(request.RequestUri?.ToString(), response.Uri, StringComparison.Ordinal)) {
					if (response.Result is Exception exception) {
						throw exception;
					}

					return Task.FromResult((HttpResponseMessage)response.Result);
				}
			}

			throw new InvalidOperationException($"Unexpected HTTP request: {request.Method} {request.RequestUri}");
		}
	}
}
