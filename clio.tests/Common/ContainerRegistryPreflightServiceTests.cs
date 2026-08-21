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
[Category("Unit")]
[Property("Module", "Common")]
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
			new StubResponse(HttpMethod.Get, "https://registry.krylov.cloud/v2/",
				new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused")),
			new StubResponse(HttpMethod.Get, "http://registry.krylov.cloud/v2/", new HttpResponseMessage(HttpStatusCode.OK)),
			new StubResponse(HttpMethod.Post, "http://registry.krylov.cloud/v2/creatio-prod/blobs/uploads/",
				new HttpResponseMessage(HttpStatusCode.Accepted)),
		]));
		ContainerRegistryPreflightService service = new(httpClient, credentialProvider);

		// Act
		ContainerRegistryPreflightResult result =
			service.ValidatePushTarget(
				"registry.krylov.cloud", "registry.krylov.cloud/creatio-prod:1.0.0", allowInsecureRegistry: true);

		// Assert
		result.Success.Should().BeTrue(
			"because the command should still be able to preflight plain HTTP registries on trusted local networks when the caller opted in with --allow-insecure-registry");
		result.Endpoint.Should().Be("http://registry.krylov.cloud/",
			"because the fallback HTTP endpoint is the one that actually accepted the upload probe");
	}

	[Test]
	[Description("ValidatePushTarget should not fall back to HTTP for a bare-authority registry prefix when allowInsecureRegistry is left at its default of false, and should mention the opt-in flag in the failure message.")]
	public void ValidatePushTarget_ShouldNotFallbackToHttpAndShouldMentionFlag_WhenAllowInsecureRegistryIsDefault() {
		// Arrange
		IContainerRegistryCredentialProvider credentialProvider = Substitute.For<IContainerRegistryCredentialProvider>();
		credentialProvider.TryResolveCredentials(Arg.Any<string>(), Arg.Any<Uri>()).Returns((ContainerRegistryCredentials)null);
		using HttpClient httpClient = new(new StubHttpMessageHandler([
			new StubResponse(HttpMethod.Get, "https://registry.krylov.cloud/v2/",
				new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused"))
		]));
		ContainerRegistryPreflightService service = new(httpClient, credentialProvider);

		// Act
		ContainerRegistryPreflightResult result =
			service.ValidatePushTarget("registry.krylov.cloud", "registry.krylov.cloud/creatio-prod:1.0.0");

		// Assert
		result.Success.Should().BeFalse(
			"because HTTPS was unreachable and the default (secure) mode must not silently fall back to plaintext HTTP");
		result.Message.Should().NotContain("http://registry.krylov.cloud",
			"because no plaintext HTTP endpoint should have been probed without an explicit opt-in");
		result.Message.Should().Contain("--allow-insecure-registry",
			"because the failure message should tell the operator how to opt in if the registry is intentionally HTTP-only");
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

	[Test]
	[Description("ValidatePushTarget should reject an explicit http:// registry prefix when allowInsecureRegistry is left at its default of false, with a message mentioning the opt-in flag.")]
	public void ValidatePushTarget_ShouldRejectExplicitHttpScheme_WhenAllowInsecureRegistryIsDefault() {
		// Arrange
		IContainerRegistryCredentialProvider credentialProvider = Substitute.For<IContainerRegistryCredentialProvider>();
		credentialProvider.TryResolveCredentials(Arg.Any<string>(), Arg.Any<Uri>()).Returns((ContainerRegistryCredentials)null);
		using HttpClient httpClient = new(new StubHttpMessageHandler([]));
		ContainerRegistryPreflightService service = new(httpClient, credentialProvider);

		// Act
		ContainerRegistryPreflightResult result =
			service.ValidatePushTarget("http://myregistry:5000", "myregistry:5000/repo:1.0.0");

		// Assert
		result.Success.Should().BeFalse(
			"because an explicit http:// scheme must not be probed without the --allow-insecure-registry opt-in, for consistency with the bare-authority fallback case");
		result.Message.Should().Contain("--allow-insecure-registry",
			"because the failure message should tell the operator how to opt in if the registry is intentionally HTTP-only");
	}

	[Test]
	[Description("ValidatePushTarget should probe an explicit http:// registry prefix when allowInsecureRegistry is set to true.")]
	public void ValidatePushTarget_ShouldProbeExplicitHttpScheme_WhenAllowInsecureRegistryIsTrue() {
		// Arrange
		IContainerRegistryCredentialProvider credentialProvider = Substitute.For<IContainerRegistryCredentialProvider>();
		credentialProvider.TryResolveCredentials(Arg.Any<string>(), Arg.Any<Uri>()).Returns((ContainerRegistryCredentials)null);
		using HttpClient httpClient = new(new StubHttpMessageHandler([
			new StubResponse(HttpMethod.Get, "http://myregistry:5000/v2/", new HttpResponseMessage(HttpStatusCode.OK)),
			new StubResponse(HttpMethod.Post, "http://myregistry:5000/v2/repo/blobs/uploads/",
				new HttpResponseMessage(HttpStatusCode.Accepted))
		]));
		ContainerRegistryPreflightService service = new(httpClient, credentialProvider);

		// Act
		ContainerRegistryPreflightResult result =
			service.ValidatePushTarget("http://myregistry:5000", "myregistry:5000/repo:1.0.0", allowInsecureRegistry: true);

		// Assert
		result.Success.Should().BeTrue(
			"because an explicit http scheme is probed once the caller opts in with --allow-insecure-registry");
		result.Endpoint.Should().Be("http://myregistry:5000/",
			"because the early-return absolute-prefix branch probes exactly the scheme and authority the caller specified, with no https-first attempt and no additional candidate");
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("ValidatePushTarget should always honor an explicit https:// registry prefix, independent of the allowInsecureRegistry flag, because that scheme is already secure.")]
	public void ValidatePushTarget_ShouldProbeExplicitHttpsScheme_RegardlessOfAllowInsecureRegistry(
		bool allowInsecureRegistry) {
		// Arrange
		IContainerRegistryCredentialProvider credentialProvider = Substitute.For<IContainerRegistryCredentialProvider>();
		credentialProvider.TryResolveCredentials(Arg.Any<string>(), Arg.Any<Uri>()).Returns((ContainerRegistryCredentials)null);
		using HttpClient httpClient = new(new StubHttpMessageHandler([
			new StubResponse(HttpMethod.Get, "https://myregistry:5000/v2/", new HttpResponseMessage(HttpStatusCode.OK)),
			new StubResponse(HttpMethod.Post, "https://myregistry:5000/v2/repo/blobs/uploads/",
				new HttpResponseMessage(HttpStatusCode.Accepted))
		]));
		ContainerRegistryPreflightService service = new(httpClient, credentialProvider);

		// Act
		ContainerRegistryPreflightResult result =
			service.ValidatePushTarget("https://myregistry:5000", "myregistry:5000/repo:1.0.0", allowInsecureRegistry);

		// Assert
		result.Success.Should().BeTrue(
			"because an explicit https scheme in the registry prefix is already secure and is honored as-is, independent of the allowInsecureRegistry opt-in");
		result.Endpoint.Should().Be("https://myregistry:5000/",
			"because the early-return absolute-prefix branch probes exactly the scheme and authority the caller specified, with no additional candidate");
	}

	[Test]
	[Description("ValidatePushTarget should not mention --allow-insecure-registry for a bare-authority registry prefix when the probe failure is an unrelated HTTP error response rather than evidence of a missing HTTPS listener.")]
	public void ValidatePushTarget_ShouldNotMentionFlag_WhenBareAuthorityProbeFailsWithUnrelatedHttpError() {
		// Arrange
		IContainerRegistryCredentialProvider credentialProvider = Substitute.For<IContainerRegistryCredentialProvider>();
		credentialProvider.TryResolveCredentials(Arg.Any<string>(), Arg.Any<Uri>()).Returns((ContainerRegistryCredentials)null);
		using HttpClient httpClient = new(new StubHttpMessageHandler([
			new StubResponse(HttpMethod.Get, "https://registry.krylov.cloud/v2/", new HttpResponseMessage(HttpStatusCode.NotFound))
		]));
		ContainerRegistryPreflightService service = new(httpClient, credentialProvider);

		// Act
		ContainerRegistryPreflightResult result =
			service.ValidatePushTarget("registry.krylov.cloud", "registry.krylov.cloud/creatio-prod:1.0.0");

		// Assert
		result.Success.Should().BeFalse(
			"because a 404 response from GET /v2/ does not confirm registry API availability");
		result.Message.Should().NotContain("--allow-insecure-registry",
			"because a generic HTTP error response is not evidence of a missing HTTPS listener, so suggesting the insecure opt-in would be misleading");
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
