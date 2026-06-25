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
	[Description("A clean cliogate GetSysInfo response with a 4-part CoreVersion is parsed verbatim into a 4-part System.Version (build/revision are NOT discarded — a version gate must keep them).")]
	public void GetCoreVersion_ShouldReturnFourPartVersion_WhenSysInfoCoreVersionPresent() {
		// Arrange
		IApplicationClient client = SubstituteGetResponse("""{ "SysInfo": { "CoreVersion": "8.1.5.123" } }""");
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		Version result = provider.GetCoreVersion();

		// Assert
		result.Should().Be(new Version(8, 1, 5, 123),
			because: "the raw 4-part CoreVersion from cliogate GetSysInfo must be parsed without discarding build/revision");
	}

	[Test]
	[Description("When the cliogate GetSysInfo probe yields no version the provider falls through to the legacy ApplicationInfoService and returns that version (a legacy fallback that produces a version must NOT return null).")]
	public void GetCoreVersion_ShouldReturnVersion_WhenOnlyLegacyApplicationInfoYieldsVersion() {
		// Arrange — cliogate GetSysInfo (GET) responds with an unusable shape; ApplicationInfo (POST) answers.
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{ "Other": { } }""");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{ "applicationInfo": { "sysValues": { "coreVersion": "8.3.3.3292" } } }""");
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		Version result = provider.GetCoreVersion();

		// Assert
		result.Should().Be(new Version(8, 3, 3, 3292),
			because: "a legacy ApplicationInfoService fallback that yields a version must return that version, never null — null is reserved for no version at all");
	}

	[Test]
	[Description("When the cliogate GetSysInfo probe throws (cliogate not installed) the provider still resolves the version from the legacy ApplicationInfoService rather than reporting it undeterminable.")]
	public void GetCoreVersion_ShouldReturnVersion_WhenSysInfoProbeThrowsButLegacyAnswers() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new HttpRequestException("cliogate not installed"));
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{ "applicationInfo": { "sysValues": { "coreVersion": "8.2.0.100" } } }""");
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		Version result = provider.GetCoreVersion();

		// Assert
		result.Should().Be(new Version(8, 2, 0, 100),
			because: "a thrown cliogate probe must degrade to the legacy source, not surface as an undeterminable version");
	}

	[Test]
	[Description("When neither the cliogate GetSysInfo nor the legacy ApplicationInfoService yields a version the provider returns null (genuinely undeterminable).")]
	public void GetCoreVersion_ShouldReturnNull_WhenNeitherSourceYieldsVersion() {
		// Arrange — both probes respond but carry no usable version.
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{ "SysInfo": { } }""");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{ "applicationInfo": { } }""");
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		Version result = provider.GetCoreVersion();

		// Assert
		result.Should().BeNull(
			because: "null is reserved for the case where NO source produces a parseable version");
	}

	[Test]
	[Description("When both probes throw (environment unreachable) the provider swallows the I/O failure and returns null — it must never let a transport exception escape to the dispatch gate.")]
	public void GetCoreVersion_ShouldReturnNull_WhenBothProbesThrow() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new HttpRequestException("connection refused"));
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new HttpRequestException("connection refused"));
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		Version result = provider.GetCoreVersion();

		// Assert
		result.Should().BeNull(
			because: "an unreachable environment is undeterminable (null) — the provider must be I/O-exception-safe so the gate fails closed rather than crashing");
	}

	[Test]
	[Description("A non-SemVer CoreVersion string (e.g. a custom 'dev' tag) is undeterminable and returns null rather than a silently clamped value.")]
	public void GetCoreVersion_ShouldReturnNull_WhenCoreVersionUnparseable() {
		// Arrange
		IApplicationClient client = SubstituteGetResponse("""{ "SysInfo": { "CoreVersion": "dev" } }""");
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		Version result = provider.GetCoreVersion();

		// Assert
		result.Should().BeNull(
			because: "an unparseable non-empty version is undeterminable; the provider must not guess a clamped value");
	}

	[Test]
	[Description("When the cliogate GetSysInfo probe yields a version the legacy ApplicationInfoService is never probed — GetSysInfo is the primary source.")]
	public void GetCoreVersion_ShouldNotProbeLegacy_WhenSysInfoYieldsVersion() {
		// Arrange
		IApplicationClient client = SubstituteGetResponse("""{ "SysInfo": { "CoreVersion": "8.1.5.123" } }""");
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		provider.GetCoreVersion();

		// Assert
		client.DidNotReceive().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("The version is memoised for the lifetime of the provider: a second GetCoreVersion call does not re-probe the environment.")]
	public void GetCoreVersion_ShouldProbeOnce_WhenCalledTwice() {
		// Arrange
		IApplicationClient client = SubstituteGetResponse("""{ "SysInfo": { "CoreVersion": "8.1.5.123" } }""");
		CreatioVersionProvider provider = CreateProvider(client);

		// Act
		provider.GetCoreVersion();
		provider.GetCoreVersion();

		// Assert
		client.Received(1).ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	#endregion

	#region Methods: Private

	private static IApplicationClient SubstituteGetResponse(string responseBody) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(responseBody);
		return client;
	}

	private static CreatioVersionProvider CreateProvider(IApplicationClient client) {
		EnvironmentSettings env = new() { Uri = EnvironmentUri, IsNetCore = true };
		return new CreatioVersionProvider(client, env, new ServiceUrlBuilderFactory());
	}

	#endregion

}
