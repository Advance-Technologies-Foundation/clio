using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PlatformVersionResolverTests {
	private const string EnvironmentUri = "https://creatio.test";

	[Test]
	[Description("A clean cliogate response that exposes a 4-part CoreVersion is mapped to a 3-part SemVer.")]
	public async Task ResolveAsync_Returns_Environment_For_Valid_CoreVersion() {
		// Arrange
		IApplicationClient client = SubstituteClient("""{ "SysInfo": { "CoreVersion": "8.1.5.123" } }""");
		PlatformVersionResolver resolver = CreateResolver(client);

		// Act
		PlatformVersionResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.Source.Should().Be(VersionResolutionSource.Environment,
			because: "a successful probe must surface as the 'environment' tier");
		resolution.ResolvedVersion.Should().Be("8.1.5",
			because: "the CDN filenames are 3-part GA tags; the build/revision component is dropped");
	}

	[Test]
	[Description("Probe failures (network down, cliogate missing) fall back to latest without throwing.")]
	public async Task ResolveAsync_Falls_To_Latest_When_Probe_Throws() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new HttpRequestException("connection refused"));
		PlatformVersionResolver resolver = CreateResolver(client);

		// Act
		PlatformVersionResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.Source.Should().Be(VersionResolutionSource.LatestFallback,
			because: "any probe failure must degrade gracefully; AI must never fail because cliogate is unreachable");
		resolution.ResolvedVersion.Should().Be("latest");
		resolution.Reason.Should().Be(VersionFallbackReason.ProbeError,
			because: "a thrown probe is a transient class — the caller must be able to tell it apart from a genuinely undeterminable version and consider a retry (ENG-91583 AC#3)");
	}

	[Test]
	[Description("An empty body is treated as a soft failure with the stable core-version-missing reason — retrying the same empty shape will not help.")]
	public async Task ResolveAsync_Falls_To_Latest_When_Response_Empty() {
		// Arrange
		IApplicationClient client = SubstituteClient(string.Empty);
		PlatformVersionResolver resolver = CreateResolver(client);

		// Act
		PlatformVersionResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.Source.Should().Be(VersionResolutionSource.LatestFallback);
		resolution.ResolvedVersion.Should().Be("latest");
		resolution.Reason.Should().Be(VersionFallbackReason.CoreVersionMissing,
			because: "a probe that responded but carried no usable CoreVersion is stable, not transient (ENG-91583 AC#3)");
	}

	[Test]
	[Description("A response that lacks the SysInfo node is treated as a soft failure.")]
	public async Task ResolveAsync_Falls_To_Latest_When_SysInfo_Missing() {
		// Arrange
		IApplicationClient client = SubstituteClient("""{ "Other": { "CoreVersion": "8.1.5" } }""");
		PlatformVersionResolver resolver = CreateResolver(client);

		// Act
		PlatformVersionResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.Source.Should().Be(VersionResolutionSource.LatestFallback,
			because: "the resolver must not guess where the CoreVersion lives — the shape is part of the contract");
	}

	[Test]
	[Description("A response where CoreVersion is missing is treated as a soft failure.")]
	public async Task ResolveAsync_Falls_To_Latest_When_CoreVersion_Missing() {
		// Arrange
		IApplicationClient client = SubstituteClient("""{ "SysInfo": { "ProductBuild": "abc" } }""");
		PlatformVersionResolver resolver = CreateResolver(client);

		// Act
		PlatformVersionResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.Source.Should().Be(VersionResolutionSource.LatestFallback);
	}

	[Test]
	[Description("A non-numeric CoreVersion is treated as a soft failure.")]
	public async Task ResolveAsync_Falls_To_Latest_When_CoreVersion_Unparseable() {
		// Arrange
		IApplicationClient client = SubstituteClient("""{ "SysInfo": { "CoreVersion": "dev" } }""");
		PlatformVersionResolver resolver = CreateResolver(client);

		// Act
		PlatformVersionResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.Source.Should().Be(VersionResolutionSource.LatestFallback,
			because: "custom dev builds with non-SemVer CoreVersion strings must not crash the AI catalog flow");
		resolution.Reason.Should().Be(VersionFallbackReason.CoreVersionUnparseable,
			because: "the value itself is the blocker, so the reason must be the stable core-version-unparseable, not a transient probe error (ENG-91583 AC#3)");
	}

	[Test]
	[Description("Without an active environment URI the resolver short-circuits to latest without probing.")]
	public async Task ResolveAsync_Falls_To_Latest_When_Environment_Uri_Empty() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		PlatformVersionResolver resolver = CreateResolver(client, environmentUri: string.Empty);

		// Act
		PlatformVersionResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.Source.Should().Be(VersionResolutionSource.LatestFallback,
			because: "no environment means there is nothing to probe; the resolver must not call IApplicationClient at all");
		resolution.Reason.Should().Be(VersionFallbackReason.NoActiveEnvironment,
			because: "an absent environment URI is a clear input gap, not a probe error — the reason must say so (ENG-91583 AC#3)");
		client.DidNotReceive().ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Successive calls within the cache TTL produce a single probe of cliogate.")]
	public async Task ResolveAsync_Caches_Result_Within_Ttl() {
		// Arrange
		IApplicationClient client = SubstituteClient("""{ "SysInfo": { "CoreVersion": "8.2.0.1" } }""");
		FakeTimeProvider clock = new();
		PlatformVersionResolver resolver = CreateResolver(client, clock: clock);

		// Act
		PlatformVersionResolution first = await resolver.ResolveAsync();
		clock.Advance(TimeSpan.FromMinutes(3));
		PlatformVersionResolution second = await resolver.ResolveAsync();

		// Assert
		first.ResolvedVersion.Should().Be("8.2.0");
		second.ResolvedVersion.Should().Be("8.2.0");
		client.Received(1).ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("On a .NET Framework (IsNetCore=false) environment the resolver hits /0/rest/CreatioApiGateway/GetSysInfo so cliogate actually responds.")]
	public async Task ResolveAsync_Builds_NetFramework_Url_With_WebAppAlias_Prefix() {
		// Arrange
		IApplicationClient client = SubstituteClient("""{ "SysInfo": { "CoreVersion": "8.1.5.123" } }""");
		PlatformVersionResolver resolver = CreateResolver(client, isNetCore: false);

		// Act
		PlatformVersionResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.Source.Should().Be(VersionResolutionSource.Environment,
			because: "the probe must succeed via the WebAppAlias-prefixed cliogate path");
		client.Received().ExecuteGetRequest(
			Arg.Is<string>(url => url.Contains("/0/rest/CreatioApiGateway/GetSysInfo")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("On a .NET Core / Freedom UI environment (IsNetCore=true) the resolver hits /rest/CreatioApiGateway/GetSysInfo directly.")]
	public async Task ResolveAsync_Builds_NetCore_Url_Without_WebAppAlias() {
		// Arrange
		IApplicationClient client = SubstituteClient("""{ "SysInfo": { "CoreVersion": "8.1.5.123" } }""");
		PlatformVersionResolver resolver = CreateResolver(client, isNetCore: true);

		// Act
		PlatformVersionResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.Source.Should().Be(VersionResolutionSource.Environment);
		client.Received().ExecuteGetRequest(
			Arg.Is<string>(url => url.Contains("/rest/CreatioApiGateway/GetSysInfo")
				&& !url.Contains("/0/rest/CreatioApiGateway/GetSysInfo")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("After 5 minutes the cache expires and the next call probes again.")]
	public async Task ResolveAsync_Refreshes_After_Cache_Expiry() {
		// Arrange
		IApplicationClient client = SubstituteClient("""{ "SysInfo": { "CoreVersion": "8.2.0.1" } }""");
		FakeTimeProvider clock = new();
		PlatformVersionResolver resolver = CreateResolver(client, clock: clock);

		// Act
		await resolver.ResolveAsync();
		clock.Advance(TimeSpan.FromMinutes(6));
		await resolver.ResolveAsync();

		// Assert
		client.Received(2).ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("ApplicationInfoService (no cliogate) is the primary version source: its coreVersion resolves to the environment tier even when the cliogate GetSysInfo probe fails — this is the works-without-cliogate guarantee.")]
	public async Task ResolveAsync_Resolves_From_ApplicationInfo_When_Cliogate_Absent() {
		// Arrange — ApplicationInfo returns a version; cliogate GetSysInfo throws (not installed).
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{ "applicationInfo": { "sysValues": { "coreVersion": "8.3.3.3292" } } }""");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new HttpRequestException("cliogate not installed"));
		PlatformVersionResolver resolver = CreateResolver(client);

		// Act
		PlatformVersionResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.Source.Should().Be(VersionResolutionSource.Environment,
			because: "ApplicationInfoService needs only auth, so version resolution must succeed without cliogate");
		resolution.ResolvedVersion.Should().Be("8.3.3",
			because: "the 4-part coreVersion from ApplicationInfo is normalised to the 3-part CDN tag");
		resolution.Reason.Should().Be(VersionFallbackReason.None,
			because: "a clean environment resolution never ran the fallback, so it must carry no fallback reason (ENG-91583 AC#3)");
	}

	[Test]
	[Description("When BOTH the ApplicationInfo and cliogate probes throw, the fallback is classified as the transient probe-error rather than a stable undeterminable reason (ENG-91583 AC#3).")]
	public async Task ResolveAsync_Reports_ProbeError_When_Both_Probes_Throw() {
		// Arrange — ApplicationInfo (POST) and cliogate GetSysInfo (GET) both fail with a network error.
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new HttpRequestException("application-info unreachable"));
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new HttpRequestException("cliogate unreachable"));
		PlatformVersionResolver resolver = CreateResolver(client);

		// Act
		PlatformVersionResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.Source.Should().Be(VersionResolutionSource.LatestFallback,
			because: "two failed probes still degrade to the latest superset");
		resolution.Reason.Should().Be(VersionFallbackReason.ProbeError,
			because: "both failures were thrown requests — the transient class must win so a retry is signalled as worthwhile");
	}

	[Test]
	[Description("When ApplicationInfo yields a version the cliogate GetSysInfo probe is never attempted — the non-cliogate path is primary, not a fallback.")]
	public async Task ResolveAsync_Does_Not_Probe_Cliogate_When_ApplicationInfo_Succeeds() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{ "applicationInfo": { "sysValues": { "coreVersion": "8.2.1.100" } } }""");
		PlatformVersionResolver resolver = CreateResolver(client);

		// Act
		PlatformVersionResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.ResolvedVersion.Should().Be("8.2.1");
		client.DidNotReceive().ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("When ApplicationInfo returns an unexpected shape the resolver falls back to the cliogate GetSysInfo probe — older Creatio versions stay covered.")]
	public async Task ResolveAsync_Falls_Back_To_Cliogate_When_ApplicationInfo_Unusable() {
		// Arrange — ApplicationInfo lacks the sysValues node; cliogate answers.
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{ "applicationInfo": { } }""");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{ "SysInfo": { "CoreVersion": "8.1.5.7" } }""");
		PlatformVersionResolver resolver = CreateResolver(client);

		// Act
		PlatformVersionResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.Source.Should().Be(VersionResolutionSource.Environment,
			because: "the cliogate fallback must still resolve when ApplicationInfo is unusable");
		resolution.ResolvedVersion.Should().Be("8.1.5");
	}

	[Test]
	[Description("On a .NET Framework environment the ApplicationInfo probe hits /0/ServiceModel/ApplicationInfoService.svc/GetApplicationInfo (WebAppAlias prefix).")]
	public async Task ResolveAsync_Builds_NetFramework_ApplicationInfo_Url() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{ "applicationInfo": { "sysValues": { "coreVersion": "8.3.3.1" } } }""");
		PlatformVersionResolver resolver = CreateResolver(client, isNetCore: false);

		// Act
		await resolver.ResolveAsync();

		// Assert
		client.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("/0/ServiceModel/ApplicationInfoService.svc/GetApplicationInfo")),
			Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	private static IApplicationClient SubstituteClient(string responseBody) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(responseBody);
		return client;
	}

	private static PlatformVersionResolver CreateResolver(
		IApplicationClient client,
		string environmentUri = EnvironmentUri,
		TimeProvider? clock = null,
		bool isNetCore = true) {
		EnvironmentSettings env = new() { Uri = environmentUri, IsNetCore = isNetCore };
		return new PlatformVersionResolver(
			client,
			env,
			new ServiceUrlBuilderFactory(),
			clock ?? new FakeTimeProvider(),
			NullLogger<PlatformVersionResolver>.Instance);
	}

	private sealed class FakeTimeProvider : TimeProvider {
		private DateTimeOffset _now = DateTimeOffset.Parse("2026-05-13T10:00:00Z");

		public override DateTimeOffset GetUtcNow() => _now;
		public void Advance(TimeSpan delta) => _now += delta;
	}
}
