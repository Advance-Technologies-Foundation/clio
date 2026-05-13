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
	}

	[Test]
	[Description("An empty body is treated as a soft failure.")]
	public async Task ResolveAsync_Falls_To_Latest_When_Response_Empty() {
		// Arrange
		IApplicationClient client = SubstituteClient(string.Empty);
		PlatformVersionResolver resolver = CreateResolver(client);

		// Act
		PlatformVersionResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.Source.Should().Be(VersionResolutionSource.LatestFallback);
		resolution.ResolvedVersion.Should().Be("latest");
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

	private static IApplicationClient SubstituteClient(string responseBody) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(responseBody);
		return client;
	}

	private static PlatformVersionResolver CreateResolver(
		IApplicationClient client,
		string environmentUri = EnvironmentUri,
		TimeProvider? clock = null) {
		EnvironmentSettings env = new() { Uri = environmentUri };
		return new PlatformVersionResolver(
			client,
			env,
			clock ?? new FakeTimeProvider(),
			NullLogger<PlatformVersionResolver>.Instance);
	}

	private sealed class FakeTimeProvider : TimeProvider {
		private DateTimeOffset _now = DateTimeOffset.Parse("2026-05-13T10:00:00Z");

		public override DateTimeOffset GetUtcNow() => _now;
		public void Advance(TimeSpan delta) => _now += delta;
	}
}
