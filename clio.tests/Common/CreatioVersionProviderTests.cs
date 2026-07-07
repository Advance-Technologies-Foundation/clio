using System;
using System.Net.Http;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class CreatioVersionProviderTests
{

	#region Constants: Private

	private const string EnvironmentUri = "https://creatio.test";

	#endregion

	#region Methods: Tests

	[Test]
	[Description("A clean ungated ApplicationInfoService response with a 4-part coreVersion resolves to Resolved with a 4-part System.Version (build/revision are NOT discarded — a version gate must keep them). ApplicationInfo is the PRIMARY source.")]
	public void Resolve_ShouldReturnResolvedFourPartVersion_WhenApplicationInfoCoreVersionPresent() {
		// Arrange
		IApplicationClient client = SubstitutePostResponse("""{ "applicationInfo": { "sysValues": { "coreVersion": "8.1.5.123" } } }""");
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		CreatioVersionResolution result = provider.Resolve();

		// Assert
		result.Status.Should().Be(CreatioVersionResolutionStatus.Resolved,
			because: "a parseable primary coreVersion is a fully resolved version");
		result.Version.Should().Be(new Version(8, 1, 5, 123),
			because: "the raw 4-part coreVersion from the ungated ApplicationInfoService must be parsed without discarding build/revision");
	}

	[Test]
	[Description("When the primary ungated ApplicationInfoService probe yields no version the provider falls through to the secondary cliogate GetSysInfo and resolves that version (only-legacy CoreVersion path).")]
	public void Resolve_ShouldReturnResolved_WhenOnlySecondarySysInfoYieldsVersion() {
		// Arrange — ApplicationInfo (POST) responds with an unusable shape; cliogate GetSysInfo (GET) answers.
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{ "applicationInfo": { } }""");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{ "SysInfo": { "CoreVersion": "8.3.3.3292" } }""");
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		CreatioVersionResolution result = provider.Resolve();

		// Assert
		result.Status.Should().Be(CreatioVersionResolutionStatus.Resolved,
			because: "a secondary cliogate GetSysInfo probe that yields a version must resolve, never degrade to undeterminable");
		result.Version.Should().Be(new Version(8, 3, 3, 3292),
			because: "the only-legacy CoreVersion path must surface the secondary source's version");
	}

	[Test]
	[Description("When the primary ApplicationInfoService probe throws the provider still resolves the version from the secondary cliogate GetSysInfo rather than reporting it undeterminable or probe-failed.")]
	public void Resolve_ShouldReturnResolved_WhenApplicationInfoProbeThrowsButSysInfoAnswers() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new HttpRequestException("application info unavailable"));
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{ "SysInfo": { "CoreVersion": "8.2.0.100" } }""");
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		CreatioVersionResolution result = provider.Resolve();

		// Assert
		result.Status.Should().Be(CreatioVersionResolutionStatus.Resolved,
			because: "a thrown primary probe must degrade to the secondary source — a secondary version is the normal Resolved path, not check-failed");
		result.Version.Should().Be(new Version(8, 2, 0, 100),
			because: "the resolved version must come from the responding secondary source");
	}

	[Test]
	[Description("When both sources RESPOND but neither carries a usable version the provider returns ReachableWithoutVersion — the environment is reachable yet its version is undeterminable.")]
	public void Resolve_ShouldReturnReachableWithoutVersion_WhenNeitherSourceYieldsVersion() {
		// Arrange — both probes respond but carry no usable version.
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{ "applicationInfo": { } }""");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{ "SysInfo": { } }""");
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		CreatioVersionResolution result = provider.Resolve();

		// Assert
		result.Status.Should().Be(CreatioVersionResolutionStatus.ReachableWithoutVersion,
			because: "a source that responded but produced no parseable version means reachable-but-undeterminable, not probe-failed");
		result.Version.Should().BeNull(
			because: "only a Resolved status carries a non-null version");
	}

	[Test]
	[Description("When BOTH probes throw a soft-degradable exception (environment unreachable / access denied) the provider returns ProbeFailed and never lets the transport exception escape to the dispatch gate.")]
	public void Resolve_ShouldReturnProbeFailed_WhenBothProbesThrow() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new HttpRequestException("connection refused"));
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new HttpRequestException("connection refused"));
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		CreatioVersionResolution result = provider.Resolve();

		// Assert
		result.Status.Should().Be(CreatioVersionResolutionStatus.ProbeFailed,
			because: "when every attempted source fails to respond the version check could not be performed — ProbeFailed, and the provider must be I/O-exception-safe so the gate fails closed rather than crashing");
		result.Version.Should().BeNull(
			because: "only a Resolved status carries a non-null version");
	}

	[Test]
	[Description("When the primary probe throws (no response) but the secondary RESPONDS without a usable version the provider returns ReachableWithoutVersion — a single responding source is enough to rule out ProbeFailed.")]
	public void Resolve_ShouldReturnReachableWithoutVersion_WhenPrimaryThrowsAndSecondaryRespondsNoVersion() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new HttpRequestException("application info unavailable"));
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{ "SysInfo": { } }""");
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		CreatioVersionResolution result = provider.Resolve();

		// Assert
		result.Status.Should().Be(CreatioVersionResolutionStatus.ReachableWithoutVersion,
			because: "a single source that responded (even with no usable version) proves the environment was reachable, so the outcome is ReachableWithoutVersion, not ProbeFailed");
	}

	[Test]
	[Description("A non-SemVer coreVersion string (e.g. a custom 'dev' tag) from a responding primary source is undeterminable: the environment responded, so the outcome is ReachableWithoutVersion rather than a silently clamped version.")]
	public void Resolve_ShouldReturnReachableWithoutVersion_WhenCoreVersionUnparseable() {
		// Arrange
		IApplicationClient client = SubstitutePostResponse("""{ "applicationInfo": { "sysValues": { "coreVersion": "dev" } } }""");
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		CreatioVersionResolution result = provider.Resolve();

		// Assert
		result.Status.Should().Be(CreatioVersionResolutionStatus.ReachableWithoutVersion,
			because: "an unparseable non-empty version from a responding source is reachable-but-undeterminable; the provider must not guess a clamped value");
		result.Version.Should().BeNull(
			because: "only a Resolved status carries a non-null version");
	}

	[Test]
	[Description("A dev-build coreVersion '0.0.0.0' resolves verbatim into a 4-part new Version(0,0,0,0), so the checker's dev-build bypass engages and a dev stand is never gated.")]
	public void Resolve_ShouldReturnResolvedZeroVersion_WhenCoreVersionIsDevBuildSentinel() {
		// Arrange
		IApplicationClient client = SubstitutePostResponse("""{ "applicationInfo": { "sysValues": { "coreVersion": "0.0.0.0" } } }""");
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		CreatioVersionResolution result = provider.Resolve();

		// Assert
		result.Status.Should().Be(CreatioVersionResolutionStatus.Resolved,
			because: "the dev-build sentinel '0.0.0.0' is a parseable version, so it resolves rather than degrading");
		result.Version.Should().Be(new Version(0, 0, 0, 0),
			because: "the provider must parse the dev-build sentinel verbatim so the checker's dev-build bypass recognises it");
		result.Version.Revision.Should().Be(0,
			because: "a 4-part 0.0.0.0 parses with an explicit Revision component of 0 (the checker also accepts a 3-part 0.0.0, but the provider returns whatever the environment reports)");
	}

	[Test]
	[Description("A 3-part dev-build coreVersion '0.0.0' resolves verbatim into a 3-part new Version(0,0,0) (Revision -1) — still a parseable version, so it resolves and the checker's dev-build bypass recognises it.")]
	public void Resolve_ShouldReturnResolvedThreePartZeroVersion_WhenCoreVersionIsThreePartDevBuild() {
		// Arrange
		IApplicationClient client = SubstitutePostResponse("""{ "applicationInfo": { "sysValues": { "coreVersion": "0.0.0" } } }""");
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		CreatioVersionResolution result = provider.Resolve();

		// Assert
		result.Status.Should().Be(CreatioVersionResolutionStatus.Resolved,
			because: "a 3-part 0.0.0 is a parseable version, so it resolves rather than degrading to undeterminable");
		result.Version.Should().Be(new Version(0, 0, 0),
			because: "the provider must parse the 3-part dev-build sentinel verbatim");
	}

	[Test]
	[Description("A non-degrading exception (InvalidOperationException) thrown by the client PROPAGATES out of Resolve — the narrowed catch must not swallow unexpected exceptions into a degraded resolution.")]
	public void Resolve_ShouldPropagate_WhenClientThrowsNonTransportException() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new InvalidOperationException("unexpected programming error"));
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		Action act = () => provider.Resolve();

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "an unexpected exception family is not a transport/parse failure and must propagate, never be soft-degraded to a resolution");
	}

	[Test]
	[Description("A transport exception (HttpRequestException) from the primary probe still soft-degrades: the provider falls through to the secondary cliogate GetSysInfo and resolves its version rather than throwing.")]
	public void Resolve_ShouldDegradeToFallthrough_WhenPrimaryProbeThrowsTransportException() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new HttpRequestException("application info unavailable"));
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{ "SysInfo": { "CoreVersion": "8.2.0.100" } }""");
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		CreatioVersionResolution result = provider.Resolve();

		// Assert
		result.Status.Should().Be(CreatioVersionResolutionStatus.Resolved,
			because: "a transport exception is a soft-degradable family, so the primary probe degrades to the secondary source");
		result.Version.Should().Be(new Version(8, 2, 0, 100),
			because: "the resolved version must come from the responding secondary source");
	}

	[Test]
	[Description("When the primary ungated ApplicationInfoService yields a version the secondary cliogate GetSysInfo is never probed — ApplicationInfo is the primary source, so a non-admin / cliogate-less environment incurs no failing gated call.")]
	public void Resolve_ShouldNotProbeSysInfo_WhenApplicationInfoYieldsVersion() {
		// Arrange
		IApplicationClient client = SubstitutePostResponse("""{ "applicationInfo": { "sysValues": { "coreVersion": "8.1.5.123" } } }""");
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		provider.Resolve();

		// Assert
		client.DidNotReceive().ExecuteGetRequest(
			Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("The resolution is memoised for the lifetime of the provider: a second Resolve call does not re-probe the environment.")]
	public void Resolve_ShouldProbeOnce_WhenCalledTwice() {
		// Arrange
		IApplicationClient client = SubstitutePostResponse("""{ "applicationInfo": { "sysValues": { "coreVersion": "8.1.5.123" } } }""");
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		provider.Resolve();
		provider.Resolve();

		// Assert
		client.Received(1).ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	#endregion

	#region Methods: Private

	private static IApplicationClient SubstitutePostResponse(string responseBody) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(responseBody);
		return client;
	}

	private static CreatioVersionProvider CreateProvider(IApplicationClient client) {
		EnvironmentSettings env = new() { Uri = EnvironmentUri, IsNetCore = true };
		return new CreatioVersionProvider(client, env, new ServiceUrlBuilderFactory());
	}

	#endregion

}
