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
		IApplicationClient netCoreClient = Substitute.For<IApplicationClient>();
		IApplicationClient netFrameworkClient = Substitute.For<IApplicationClient>();
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
		IApplicationClient netCoreClient = Substitute.For<IApplicationClient>();
		IApplicationClient netFrameworkClient = Substitute.For<IApplicationClient>();
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
		IApplicationClient netCoreClient = Substitute.For<IApplicationClient>();
		IApplicationClient netFrameworkClient = Substitute.For<IApplicationClient>();
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
	[Description("Chooses .NET Core when both service probes succeed but only the .NET Core login marker is reachable.")]
	public void Detect_Should_Return_True_When_Both_Service_Probes_Succeed_But_Only_NetCore_Ui_Marker_Is_Reachable() {
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IHttpClientFactory httpClientFactory = CreateHttpClientFactory(new Dictionary<string, HttpStatusCode> {
			[BuildHealthUrl(true)] = HttpStatusCode.OK,
			[BuildHealthUrl(false)] = HttpStatusCode.OK,
			[BuildUiMarkerUrl(true)] = HttpStatusCode.OK,
			[BuildUiMarkerUrl(false)] = HttpStatusCode.NotFound
		});
		IApplicationClient netCoreClient = Substitute.For<IApplicationClient>();
		IApplicationClient netFrameworkClient = Substitute.For<IApplicationClient>();
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
		IApplicationClient netCoreClient = Substitute.For<IApplicationClient>();
		IApplicationClient netFrameworkClient = Substitute.For<IApplicationClient>();
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
		IApplicationClient netCoreClient = Substitute.For<IApplicationClient>();
		IApplicationClient netFrameworkClient = Substitute.For<IApplicationClient>();
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

	private static void ConfigureFactory(
		IApplicationClientFactory applicationClientFactory,
		IApplicationClient netCoreClient,
		IApplicationClient netFrameworkClient) {
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

	private static IHttpClientFactory CreateHttpClientFactory(IReadOnlyDictionary<string, HttpStatusCode> responsesByUrl) {
		IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
		httpClientFactory.CreateClient(Arg.Any<string>())
			.Returns(_ => new HttpClient(new StubHttpMessageHandler(responsesByUrl), disposeHandler: true));
		return httpClientFactory;
	}

	private static IHttpClientFactory CreateFailingHttpClientFactory(Exception exception) {
		IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
		httpClientFactory.CreateClient(Arg.Any<string>())
			.Returns(_ => new HttpClient(new ThrowingHttpMessageHandler(exception), disposeHandler: true));
		return httpClientFactory;
	}

	private sealed class StubHttpMessageHandler(IReadOnlyDictionary<string, HttpStatusCode> responsesByUrl) : HttpMessageHandler {
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
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
