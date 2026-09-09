using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.Http;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class EnvironmentRuntimeDetectionServiceTests {
	private const string BaseUri = "http://localhost:5007";

	[Test]
	[Description("Chooses the .NET Core route when both health endpoints respond but only the .NET Core SelectQuery succeeds.")]
	public void Detect_Should_Return_True_When_Both_Health_Endpoints_Respond_But_Only_NetCore_Service_Succeeds() {
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IHttpClientFactory httpClientFactory = CreateHttpClientFactory(new Dictionary<string, HttpStatusCode> {
			[BuildHealthUrl(true)] = HttpStatusCode.OK,
			[BuildHealthUrl(false)] = HttpStatusCode.OK,
			[BuildUiMarkerUrl(true)] = HttpStatusCode.OK,
			[BuildUiMarkerUrl(false)] = HttpStatusCode.NotFound
		});
		IOwnedApplicationClient netCoreClient = Substitute.For<IOwnedApplicationClient>();
		IOwnedApplicationClient netFrameworkClient = Substitute.For<IOwnedApplicationClient>();
		ConfigureFactory(applicationClientFactory, netCoreClient, netFrameworkClient);
		ConfigureClientWarmup(netCoreClient, true);
		ConfigureClientWarmup(netFrameworkClient, false);
		ConfigureServiceSuccess(netCoreClient, true);
		ConfigureServiceFailure(netFrameworkClient, false, "Framework SelectQuery failed.");
		EnvironmentRuntimeDetectionService sut = new(applicationClientFactory, httpClientFactory, new ServiceUrlBuilderFactory());

		bool result = sut.Detect(CreateEnvironment());

		result.Should().BeTrue(
			because: "the detector should prefer the runtime whose authenticated SelectQuery probe succeeds");
	}

	[Test]
	[Description("Chooses the .NET Framework route when both health endpoints respond but only the .NET Framework SelectQuery succeeds.")]
	public void Detect_Should_Return_False_When_Both_Health_Endpoints_Respond_But_Only_NetFramework_Service_Succeeds() {
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IHttpClientFactory httpClientFactory = CreateHttpClientFactory(new Dictionary<string, HttpStatusCode> {
			[BuildHealthUrl(true)] = HttpStatusCode.OK,
			[BuildHealthUrl(false)] = HttpStatusCode.OK,
			[BuildUiMarkerUrl(true)] = HttpStatusCode.NotFound,
			[BuildUiMarkerUrl(false)] = HttpStatusCode.OK
		});
		IOwnedApplicationClient netCoreClient = Substitute.For<IOwnedApplicationClient>();
		IOwnedApplicationClient netFrameworkClient = Substitute.For<IOwnedApplicationClient>();
		ConfigureFactory(applicationClientFactory, netCoreClient, netFrameworkClient);
		ConfigureClientWarmup(netCoreClient, true);
		ConfigureClientWarmup(netFrameworkClient, false);
		ConfigureServiceFailure(netCoreClient, true, "NetCore SelectQuery failed.");
		ConfigureServiceSuccess(netFrameworkClient, false);
		EnvironmentRuntimeDetectionService sut = new(applicationClientFactory, httpClientFactory, new ServiceUrlBuilderFactory());

		bool result = sut.Detect(CreateEnvironment());

		result.Should().BeFalse(
			because: "the detector should return the framework route when that is the only authenticated probe that works");
	}

	[Test]
	[Description("Fails with detailed probe diagnostics when both authenticated SelectQuery probes fail even if both health checks are reachable.")]
	public void Detect_Should_Throw_When_Both_Service_Probes_Fail() {
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IHttpClientFactory httpClientFactory = CreateHttpClientFactory(new Dictionary<string, HttpStatusCode> {
			[BuildHealthUrl(true)] = HttpStatusCode.OK,
			[BuildHealthUrl(false)] = HttpStatusCode.OK,
			[BuildUiMarkerUrl(true)] = HttpStatusCode.OK,
			[BuildUiMarkerUrl(false)] = HttpStatusCode.OK
		});
		IOwnedApplicationClient netCoreClient = Substitute.For<IOwnedApplicationClient>();
		IOwnedApplicationClient netFrameworkClient = Substitute.For<IOwnedApplicationClient>();
		ConfigureFactory(applicationClientFactory, netCoreClient, netFrameworkClient);
		ConfigureClientWarmup(netCoreClient, true);
		ConfigureClientWarmup(netFrameworkClient, false);
		ConfigureServiceFailure(netCoreClient, true, "NetCore SelectQuery failed.");
		ConfigureServiceFailure(netFrameworkClient, false, "Framework SelectQuery failed.");
		EnvironmentRuntimeDetectionService sut = new(applicationClientFactory, httpClientFactory, new ServiceUrlBuilderFactory());

		Action act = () => sut.Detect(CreateEnvironment());

		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>()
			.Which;
		exception.Message.Should().Contain(BuildSelectUrl(true),
			because: "the failure should name the .NET Core service probe URL for troubleshooting");
		exception.Message.Should().Contain(BuildSelectUrl(false),
			because: "the failure should name the .NET Framework service probe URL for troubleshooting");
		exception.Message.Should().Contain("--IsNetCore true",
			because: "the failure should tell the caller how to override detection explicitly");
	}

	[Test]
	[Description("Falls back to UI markers and returns false (Framework) when both service probes fail but only the .NET Framework UI marker succeeds — the scenario seen on 120458-studio.creatio.com.")]
	public void Detect_Should_Return_False_When_Both_Service_Probes_Fail_But_Only_NetFramework_Ui_Marker_Succeeds() {
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IHttpClientFactory httpClientFactory = CreateHttpClientFactory(new Dictionary<string, HttpStatusCode> {
			[BuildHealthUrl(true)] = HttpStatusCode.OK,
			[BuildHealthUrl(false)] = HttpStatusCode.OK,
			[BuildUiMarkerUrl(true)] = HttpStatusCode.NotFound,
			[BuildUiMarkerUrl(false)] = HttpStatusCode.OK
		});
		IOwnedApplicationClient netCoreClient = Substitute.For<IOwnedApplicationClient>();
		IOwnedApplicationClient netFrameworkClient = Substitute.For<IOwnedApplicationClient>();
		ConfigureFactory(applicationClientFactory, netCoreClient, netFrameworkClient);
		ConfigureClientWarmup(netCoreClient, true);
		ConfigureClientWarmup(netFrameworkClient, false);
		ConfigureServiceFailure(netCoreClient, true, "NetCore SelectQuery failed.");
		ConfigureServiceFailure(netFrameworkClient, false, "Framework returned HTML.");
		EnvironmentRuntimeDetectionService sut = new(applicationClientFactory, httpClientFactory, new ServiceUrlBuilderFactory());

		bool result = sut.Detect(CreateEnvironment());

		result.Should().BeFalse(because: "NuiLogin.aspx present and Login.html absent is the .NET Framework fingerprint");
	}

	[Test]
	[Description("Falls back to UI markers and returns true (NetCore) when both service probes fail but only the .NET Core UI marker succeeds.")]
	public void Detect_Should_Return_True_When_Both_Service_Probes_Fail_But_Only_NetCore_Ui_Marker_Succeeds() {
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IHttpClientFactory httpClientFactory = CreateHttpClientFactory(new Dictionary<string, HttpStatusCode> {
			[BuildHealthUrl(true)] = HttpStatusCode.OK,
			[BuildHealthUrl(false)] = HttpStatusCode.OK,
			[BuildUiMarkerUrl(true)] = HttpStatusCode.OK,
			[BuildUiMarkerUrl(false)] = HttpStatusCode.NotFound
		});
		IOwnedApplicationClient netCoreClient = Substitute.For<IOwnedApplicationClient>();
		IOwnedApplicationClient netFrameworkClient = Substitute.For<IOwnedApplicationClient>();
		ConfigureFactory(applicationClientFactory, netCoreClient, netFrameworkClient);
		ConfigureClientWarmup(netCoreClient, true);
		ConfigureClientWarmup(netFrameworkClient, false);
		ConfigureServiceFailure(netCoreClient, true, "NetCore SelectQuery failed.");
		ConfigureServiceFailure(netFrameworkClient, false, "Framework SelectQuery failed.");
		EnvironmentRuntimeDetectionService sut = new(applicationClientFactory, httpClientFactory, new ServiceUrlBuilderFactory());

		bool result = sut.Detect(CreateEnvironment());

		result.Should().BeTrue(because: "Login.html present and NuiLogin.aspx absent is the .NET Core fingerprint");
	}

	[Test]
	[Description("Chooses .NET Core when both service probes succeed but only the .NET Core login marker is reachable.")]
	public void Detect_Should_Return_True_When_Both_Service_Probes_Succeed_But_Only_NetCore_Ui_Marker_Is_Reachable() {
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IHttpClientFactory httpClientFactory = CreateHttpClientFactory(new Dictionary<string, HttpStatusCode> {
			[BuildHealthUrl(true)] = HttpStatusCode.OK,
			[BuildHealthUrl(false)] = HttpStatusCode.OK,
			[BuildUiMarkerUrl(true)] = HttpStatusCode.OK,
			[BuildUiMarkerUrl(false)] = HttpStatusCode.NotFound
		});
		IOwnedApplicationClient netCoreClient = Substitute.For<IOwnedApplicationClient>();
		IOwnedApplicationClient netFrameworkClient = Substitute.For<IOwnedApplicationClient>();
		ConfigureFactory(applicationClientFactory, netCoreClient, netFrameworkClient);
		ConfigureClientWarmup(netCoreClient, true);
		ConfigureClientWarmup(netFrameworkClient, false);
		ConfigureServiceSuccess(netCoreClient, true);
		ConfigureServiceSuccess(netFrameworkClient, false);
		EnvironmentRuntimeDetectionService sut = new(applicationClientFactory, httpClientFactory, new ServiceUrlBuilderFactory());

		bool result = sut.Detect(CreateEnvironment());

		result.Should().BeTrue(
			because: "the NET8 login page marker should break the tie when both service routes are available");
	}

	[Test]
	[Description("Chooses .NET Framework when both service probes succeed but only the framework login marker is reachable.")]
	public void Detect_Should_Return_False_When_Both_Service_Probes_Succeed_But_Only_NetFramework_Ui_Marker_Is_Reachable() {
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IHttpClientFactory httpClientFactory = CreateHttpClientFactory(new Dictionary<string, HttpStatusCode> {
			[BuildHealthUrl(true)] = HttpStatusCode.OK,
			[BuildHealthUrl(false)] = HttpStatusCode.OK,
			[BuildUiMarkerUrl(true)] = HttpStatusCode.NotFound,
			[BuildUiMarkerUrl(false)] = HttpStatusCode.OK
		});
		IOwnedApplicationClient netCoreClient = Substitute.For<IOwnedApplicationClient>();
		IOwnedApplicationClient netFrameworkClient = Substitute.For<IOwnedApplicationClient>();
		ConfigureFactory(applicationClientFactory, netCoreClient, netFrameworkClient);
		ConfigureClientWarmup(netCoreClient, true);
		ConfigureClientWarmup(netFrameworkClient, false);
		ConfigureServiceSuccess(netCoreClient, true);
		ConfigureServiceSuccess(netFrameworkClient, false);
		EnvironmentRuntimeDetectionService sut = new(applicationClientFactory, httpClientFactory, new ServiceUrlBuilderFactory());

		bool result = sut.Detect(CreateEnvironment());

		result.Should().BeFalse(
			because: "the framework login page marker should break the tie when both service routes are available");
	}

	[Test]
	[Description("Fails with an ambiguity diagnostic when both authenticated SelectQuery probes and both UI markers succeed.")]
	public void Detect_Should_Throw_When_Both_Service_Probes_And_Both_Ui_Markers_Succeed() {
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IHttpClientFactory httpClientFactory = CreateHttpClientFactory(new Dictionary<string, HttpStatusCode> {
			[BuildHealthUrl(true)] = HttpStatusCode.OK,
			[BuildHealthUrl(false)] = HttpStatusCode.OK,
			[BuildUiMarkerUrl(true)] = HttpStatusCode.OK,
			[BuildUiMarkerUrl(false)] = HttpStatusCode.OK
		});
		IOwnedApplicationClient netCoreClient = Substitute.For<IOwnedApplicationClient>();
		IOwnedApplicationClient netFrameworkClient = Substitute.For<IOwnedApplicationClient>();
		ConfigureFactory(applicationClientFactory, netCoreClient, netFrameworkClient);
		ConfigureClientWarmup(netCoreClient, true);
		ConfigureClientWarmup(netFrameworkClient, false);
		ConfigureServiceSuccess(netCoreClient, true);
		ConfigureServiceSuccess(netFrameworkClient, false);
		EnvironmentRuntimeDetectionService sut = new(applicationClientFactory, httpClientFactory, new ServiceUrlBuilderFactory());

		Action act = () => sut.Detect(CreateEnvironment());

		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>()
			.Which;
		exception.Message.Should().Contain("both .NET Core / NET8 and .NET Framework service probes succeeded",
			because: "the detector should stop instead of silently guessing when both route families look valid");
	}

	[Test]
	[Description("Chooses .NET Framework without credentials when the unauthenticated UI marker only resolves for the /0 route.")]
	public void Detect_Should_Return_False_When_Credentials_Are_Missing_And_Only_NetFramework_Ui_Marker_Is_Reachable() {
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IHttpClientFactory httpClientFactory = CreateHttpClientFactory(new Dictionary<string, HttpStatusCode> {
			[BuildHealthUrl(true)] = HttpStatusCode.OK,
			[BuildHealthUrl(false)] = HttpStatusCode.OK,
			[BuildUiMarkerUrl(true)] = HttpStatusCode.NotFound,
			[BuildUiMarkerUrl(false)] = HttpStatusCode.OK
		});
		EnvironmentRuntimeDetectionService sut = new(applicationClientFactory, httpClientFactory, new ServiceUrlBuilderFactory());

		bool result = sut.Detect(new EnvironmentSettings {
			Uri = BaseUri
		});

		result.Should().BeFalse(
			because: "URL-only registration should still resolve the framework route when the login marker is conclusive");
		applicationClientFactory.DidNotReceiveWithAnyArgs().CreateEnvironmentClient(default!);
	}

	[Test]
	[Description("Surfaces a reachability diagnostic when the target host cannot be contacted during unauthenticated auto-detection.")]
	public void Detect_Should_Throw_With_Reachability_Diagnostic_When_Host_Cannot_Be_Reached() {
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IHttpClientFactory httpClientFactory = CreateFailingHttpClientFactory(
			new HttpRequestException("nodename nor servname provided, or not known"));
		EnvironmentRuntimeDetectionService sut = new(applicationClientFactory, httpClientFactory, new ServiceUrlBuilderFactory());

		Action act = () => sut.Detect(new EnvironmentSettings {
			Uri = "http://ts1-infr-web01:88/studioenu_14771250_0401"
		});

		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>()
			.Which;
		exception.Message.Should().Contain("could not be reached from this machine",
			because: "the detector should explain that the host is unreachable instead of implying a runtime mismatch");
		exception.Message.Should().Contain("ts1-infr-web01:88",
			because: "the diagnostic should identify which host could not be reached");
	}

	[Test]
	[Description("Chooses .NET Framework when the .NET Core login marker answers 404 and the framework marker fails at transport level on a cold site.")]
	public void Detect_Should_Return_False_When_NetCore_Ui_Marker_Is_NotFound_And_NetFramework_Ui_Marker_Fails_To_Respond() {
		// Arrange
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IHttpClientFactory httpClientFactory = CreateHttpClientFactory(
			new Dictionary<string, HttpStatusCode> {
				[BuildHealthUrl(true)] = HttpStatusCode.OK,
				[BuildHealthUrl(false)] = HttpStatusCode.OK,
				[BuildUiMarkerUrl(true)] = HttpStatusCode.NotFound
			},
			new Dictionary<string, Exception> {
				[BuildUiMarkerUrl(false)] =
					new HttpRequestException("An existing connection was forcibly closed by the remote host.")
			});
		IOwnedApplicationClient netCoreClient = Substitute.For<IOwnedApplicationClient>();
		IOwnedApplicationClient netFrameworkClient = Substitute.For<IOwnedApplicationClient>();
		ConfigureFactory(applicationClientFactory, netCoreClient, netFrameworkClient);
		ConfigureClientWarmup(netCoreClient, true);
		ConfigureClientWarmup(netFrameworkClient, false);
		ConfigureServiceFailure(netCoreClient, true, "SelectQuery failed.");
		ConfigureServiceThrows(netFrameworkClient, false, new TaskCanceledException("A task was canceled."));
		EnvironmentRuntimeDetectionService sut = new(applicationClientFactory, httpClientFactory, new ServiceUrlBuilderFactory());

		// Act
		bool result = sut.Detect(CreateEnvironment());

		// Assert
		result.Should().BeFalse(
			because: "a 404 proves Login.html is absent, while a reset connection on NuiLogin.aspx proves nothing, so the framework route is the only runtime not ruled out");
	}

	[Test]
	[Description("Chooses .NET Core when the framework login marker answers 404 and the .NET Core marker fails at transport level on a cold site.")]
	public void Detect_Should_Return_True_When_NetFramework_Ui_Marker_Is_NotFound_And_NetCore_Ui_Marker_Fails_To_Respond() {
		// Arrange
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IHttpClientFactory httpClientFactory = CreateHttpClientFactory(
			new Dictionary<string, HttpStatusCode> {
				[BuildHealthUrl(true)] = HttpStatusCode.OK,
				[BuildHealthUrl(false)] = HttpStatusCode.OK,
				[BuildUiMarkerUrl(false)] = HttpStatusCode.NotFound
			},
			new Dictionary<string, Exception> {
				[BuildUiMarkerUrl(true)] = new TaskCanceledException("A task was canceled.")
			});
		IOwnedApplicationClient netCoreClient = Substitute.For<IOwnedApplicationClient>();
		IOwnedApplicationClient netFrameworkClient = Substitute.For<IOwnedApplicationClient>();
		ConfigureFactory(applicationClientFactory, netCoreClient, netFrameworkClient);
		ConfigureClientWarmup(netCoreClient, true);
		ConfigureClientWarmup(netFrameworkClient, false);
		ConfigureServiceFailure(netCoreClient, true, "NetCore SelectQuery failed.");
		ConfigureServiceFailure(netFrameworkClient, false, "Framework SelectQuery failed.");
		EnvironmentRuntimeDetectionService sut = new(applicationClientFactory, httpClientFactory, new ServiceUrlBuilderFactory());

		// Act
		bool result = sut.Detect(CreateEnvironment());

		// Assert
		result.Should().BeTrue(
			because: "an explicit 404 on NuiLogin.aspx rules out the framework route even when the NET8 marker never answered");
	}

	[Test]
	[Description("Prefers the conclusive login markers over the health endpoints when no credentials are supplied.")]
	public void Detect_Should_Prefer_Ui_Markers_Over_Health_Probes_When_Credentials_Are_Missing() {
		// Arrange
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IHttpClientFactory httpClientFactory = CreateHttpClientFactory(
			new Dictionary<string, HttpStatusCode> {
				[BuildHealthUrl(true)] = HttpStatusCode.OK,
				[BuildUiMarkerUrl(true)] = HttpStatusCode.NotFound
			},
			new Dictionary<string, Exception> {
				[BuildHealthUrl(false)] = new TaskCanceledException("A task was canceled."),
				[BuildUiMarkerUrl(false)] =
					new HttpRequestException("An existing connection was forcibly closed by the remote host.")
			});
		EnvironmentRuntimeDetectionService sut = new(applicationClientFactory, httpClientFactory, new ServiceUrlBuilderFactory());

		// Act
		bool result = sut.Detect(new EnvironmentSettings {
			Uri = BaseUri
		});

		// Assert
		result.Should().BeFalse(
			because: "/api/HealthCheck/Ping also answers on a .NET Framework site, so a single successful health probe must not outweigh a 404 on Login.html");
	}

	[Test]
	[Description("Still fails with diagnostics when both login markers answer 404 and neither runtime can be ruled in.")]
	public void Detect_Should_Throw_When_Both_Ui_Markers_Are_NotFound() {
		// Arrange
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IHttpClientFactory httpClientFactory = CreateHttpClientFactory(new Dictionary<string, HttpStatusCode> {
			[BuildHealthUrl(true)] = HttpStatusCode.OK,
			[BuildHealthUrl(false)] = HttpStatusCode.OK,
			[BuildUiMarkerUrl(true)] = HttpStatusCode.NotFound,
			[BuildUiMarkerUrl(false)] = HttpStatusCode.NotFound
		});
		IOwnedApplicationClient netCoreClient = Substitute.For<IOwnedApplicationClient>();
		IOwnedApplicationClient netFrameworkClient = Substitute.For<IOwnedApplicationClient>();
		ConfigureFactory(applicationClientFactory, netCoreClient, netFrameworkClient);
		ConfigureClientWarmup(netCoreClient, true);
		ConfigureClientWarmup(netFrameworkClient, false);
		ConfigureServiceFailure(netCoreClient, true, "NetCore SelectQuery failed.");
		ConfigureServiceFailure(netFrameworkClient, false, "Framework SelectQuery failed.");
		EnvironmentRuntimeDetectionService sut = new(applicationClientFactory, httpClientFactory, new ServiceUrlBuilderFactory());

		// Act
		Action act = () => sut.Detect(CreateEnvironment());

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "two absent markers rule out both runtimes, so guessing one of them would be worse than asking for --IsNetCore");
	}

	private static void ConfigureFactory(
		IApplicationClientFactory applicationClientFactory,
		IOwnedApplicationClient netCoreClient,
		IOwnedApplicationClient netFrameworkClient) {
		applicationClientFactory.CreateEnvironmentClient(Arg.Is<EnvironmentSettings>(settings => settings.IsNetCore))
			.Returns(netCoreClient);
		applicationClientFactory.CreateEnvironmentClient(Arg.Is<EnvironmentSettings>(settings => !settings.IsNetCore))
			.Returns(netFrameworkClient);
	}

	private static EnvironmentSettings CreateEnvironment() =>
		new() {
			Uri = BaseUri,
			Login = "Supervisor",
			Password = "Supervisor"
		};

	private static void ConfigureServiceSuccess(IApplicationClient client, bool isNetCore) {
		client.ExecutePostRequest(
				BuildSelectUrl(isNetCore),
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("""{"success":true,"rows":[{"Id":"1"}]}""");
	}

	private static void ConfigureServiceFailure(IApplicationClient client, bool isNetCore, string errorMessage) {
		client.ExecutePostRequest(
					BuildSelectUrl(isNetCore),
					Arg.Any<string>(),
					Arg.Any<int>(),
					Arg.Any<int>(),
					Arg.Any<int>())
				.Returns($"{{\"success\":false,\"errorInfo\":{{\"message\":\"{errorMessage}\"}}}}");
	}

	private static void ConfigureServiceThrows(IApplicationClient client, bool isNetCore, Exception exception) {
		client.ExecutePostRequest(
				BuildSelectUrl(isNetCore),
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Throws(exception);
	}

	private static void ConfigureClientWarmup(IApplicationClient client, bool isNetCore) {
		client.ExecuteGetRequest(
				BuildHealthUrl(isNetCore),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("OK");
	}

	private static string BuildHealthUrl(bool isNetCore) =>
		$"{BaseUri}{(isNetCore ? "/api/HealthCheck/Ping" : "/0/api/HealthCheck/Ping")}";

	private static string BuildSelectUrl(bool isNetCore) =>
		new ServiceUrlBuilder(new EnvironmentSettings {
			Uri = BaseUri,
			IsNetCore = isNetCore
		}).Build(ServiceUrlBuilder.KnownRoute.Select);

	private static string BuildUiMarkerUrl(bool isNetCore) =>
		$"{BaseUri}{(isNetCore ? "/Login/Login.html" : "/0/Login/NuiLogin.aspx")}";

	private static IHttpClientFactory CreateHttpClientFactory(IReadOnlyDictionary<string, HttpStatusCode> responsesByUrl) =>
		CreateHttpClientFactory(responsesByUrl, new Dictionary<string, Exception>());

	private static IHttpClientFactory CreateHttpClientFactory(
		IReadOnlyDictionary<string, HttpStatusCode> responsesByUrl,
		IReadOnlyDictionary<string, Exception> exceptionsByUrl) {
		IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
		httpClientFactory.CreateClient(Arg.Any<string>())
			.Returns(_ => new HttpClient(new StubHttpMessageHandler(responsesByUrl, exceptionsByUrl), disposeHandler: true));
		return httpClientFactory;
	}

	private static IHttpClientFactory CreateFailingHttpClientFactory(Exception exception) {
		IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
		httpClientFactory.CreateClient(Arg.Any<string>())
			.Returns(_ => new HttpClient(new ThrowingHttpMessageHandler(exception), disposeHandler: true));
		return httpClientFactory;
	}

	private sealed class StubHttpMessageHandler(
		IReadOnlyDictionary<string, HttpStatusCode> responsesByUrl,
		IReadOnlyDictionary<string, Exception> exceptionsByUrl) : HttpMessageHandler {
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
			if (exceptionsByUrl.TryGetValue(request.RequestUri!.ToString(), out Exception? mappedException)) {
				return Task.FromException<HttpResponseMessage>(mappedException);
			}
			HttpStatusCode statusCode = responsesByUrl.TryGetValue(request.RequestUri!.ToString(), out HttpStatusCode mappedStatusCode)
				? mappedStatusCode
				: HttpStatusCode.NotFound;
			return Task.FromResult(new HttpResponseMessage(statusCode) {
				RequestMessage = request,
				Content = new StringContent(string.Empty)
			});
		}
	}

	private sealed class ThrowingHttpMessageHandler(Exception exception) : HttpMessageHandler {
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromException<HttpResponseMessage>(exception);
	}
}
