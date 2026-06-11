using System;
using System.Net.Http;
using System.Threading.Tasks;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class CurrentUserCultureResolverTests
{
	private const string EnvironmentUri = "https://creatio.test";

	[Test]
	[Description("A valid userCulture.displayValue resolves to the environment tier as a validated culture name.")]
	public async Task ResolveAsync_ShouldReturnResolvedCulture_WhenUserCultureDisplayValueIsValid()
	{
		// Arrange
		IApplicationClient client = SubstituteClient(
			"""{ "applicationInfo": { "sysValues": { "userCulture": { "displayValue": "uk-UA", "value": "g" } } } }""");
		CurrentUserCultureResolver resolver = CreateResolver(client);

		// Act
		CultureResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.Success.Should().BeTrue(
			because: "a present, parseable userCulture.displayValue is a successful resolution");
		resolution.Culture.Should().Be("uk-UA",
			because: "the resolver must return the logged-in user's profile culture from sysValues.userCulture");
		resolution.FailureReason.Should().BeNull(
			because: "a successful resolution carries no failure reason");
	}

	[Test]
	[Description("A differently-cased displayValue is normalized to the canonical CultureInfo.Name.")]
	public async Task ResolveAsync_ShouldNormalizeToCultureInfoName_WhenDisplayValueDiffersInCase()
	{
		// Arrange
		IApplicationClient client = SubstituteClient(
			"""{ "applicationInfo": { "sysValues": { "userCulture": { "displayValue": "EN-us" } } } }""");
		CurrentUserCultureResolver resolver = CreateResolver(client);

		// Act
		CultureResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.Culture.Should().Be("en-US",
			because: "the resolved value must be the canonical CultureInfo.Name, not the raw payload casing");
	}

	[Test]
	[Description("A malformed displayValue that CultureInfo cannot parse fails with userCulture-invalid and never throws.")]
	public async Task ResolveAsync_ShouldReturnFailedUserCultureInvalid_WhenDisplayValueIsMalformed()
	{
		// Arrange
		IApplicationClient client = SubstituteClient(
			"""{ "applicationInfo": { "sysValues": { "userCulture": { "displayValue": "not_a_culture!!" } } } }""");
		CurrentUserCultureResolver resolver = CreateResolver(client);

		// Act
		CultureResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.Success.Should().BeFalse(
			because: "a string CultureInfo cannot parse must never become a caption culture key");
		resolution.FailureReason.Should().Be(CurrentUserCultureResolver.ReasonUserCultureInvalid,
			because: "an unparseable culture is reported as userCulture-invalid");
		resolution.Culture.Should().Be(EntitySchemaDesignerSupport.DefaultCultureName,
			because: "a failed resolution exposes the en-US fallback for the non-fatal creation path");
	}

	[Test]
	[Description("An empty displayValue fails with userCulture-missing.")]
	public async Task ResolveAsync_ShouldReturnFailedUserCultureMissing_WhenDisplayValueIsEmpty()
	{
		// Arrange
		IApplicationClient client = SubstituteClient(
			"""{ "applicationInfo": { "sysValues": { "userCulture": { "displayValue": "" } } } }""");
		CurrentUserCultureResolver resolver = CreateResolver(client);

		// Act
		CultureResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.Success.Should().BeFalse(
			because: "an empty culture value cannot be used");
		resolution.FailureReason.Should().Be(CurrentUserCultureResolver.ReasonUserCultureMissing,
			because: "an empty/whitespace displayValue is reported as userCulture-missing");
	}

	[Test]
	[Description("When userCulture is absent the resolver fails with userCulture-missing and must NOT substitute primaryCulture (Mi-1).")]
	public async Task ResolveAsync_ShouldReturnFailedAndNotSubstitutePrimaryCulture_WhenUserCultureAbsent()
	{
		// Arrange — only the system primaryCulture is present; the user's profile culture is not.
		IApplicationClient client = SubstituteClient(
			"""{ "applicationInfo": { "sysValues": { "primaryCulture": { "displayValue": "uk-UA" } } } }""");
		CurrentUserCultureResolver resolver = CreateResolver(client);

		// Act
		CultureResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.Success.Should().BeFalse(
			because: "the profile culture is specifically the logged-in user's, so a missing userCulture is a failure");
		resolution.FailureReason.Should().Be(CurrentUserCultureResolver.ReasonUserCultureMissing,
			because: "userCulture absence is reported as userCulture-missing");
		resolution.Culture.Should().NotBe("uk-UA",
			because: "the system primaryCulture must never be silently substituted for the user's profile culture");
	}

	[Test]
	[Description("A transport error surfaces as an unreachable failure rather than throwing into the creation path.")]
	public async Task ResolveAsync_ShouldReturnFailedUnreachable_WhenEndpointThrows()
	{
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new HttpRequestException("connection refused"));
		CurrentUserCultureResolver resolver = CreateResolver(client);

		// Act
		CultureResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.Success.Should().BeFalse(
			because: "an unreachable endpoint must degrade gracefully, never throw into the creation path");
		resolution.FailureReason.Should().Be(CurrentUserCultureResolver.ReasonUnreachable,
			because: "a generic transport failure is classified as unreachable");
	}

	[Test]
	[Description("A 401/Unauthorized error is classified as an unauthorized failure.")]
	public async Task ResolveAsync_ShouldReturnFailedUnauthorized_WhenEndpointReturnsUnauthorized()
	{
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new HttpRequestException("The remote server returned 401 Unauthorized"));
		CurrentUserCultureResolver resolver = CreateResolver(client);

		// Act
		CultureResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.FailureReason.Should().Be(CurrentUserCultureResolver.ReasonUnauthorized,
			because: "an authentication failure must be distinguishable from a generic unreachable error");
	}

	[Test]
	[Description("Without an active environment URI the resolver short-circuits and never calls IApplicationClient.")]
	public async Task ResolveAsync_ShouldReturnFailedNoActiveEnvironment_WhenUriIsEmpty()
	{
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		CurrentUserCultureResolver resolver = CreateResolver(client, environmentUri: string.Empty);

		// Act
		CultureResolution resolution = await resolver.ResolveAsync();

		// Assert
		resolution.FailureReason.Should().Be(CurrentUserCultureResolver.ReasonNoActiveEnvironment,
			because: "no environment means there is nothing to probe");
		client.DidNotReceive().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("On .NET Framework the probe targets the /0/ServiceModel ApplicationInfoService alias.")]
	public async Task ResolveAsync_ShouldBuildNetFrameworkUrl_WhenEnvironmentIsNotNetCore()
	{
		// Arrange
		IApplicationClient client = SubstituteClient(
			"""{ "applicationInfo": { "sysValues": { "userCulture": { "displayValue": "en-US" } } } }""");
		CurrentUserCultureResolver resolver = CreateResolver(client, isNetCore: false);

		// Act
		await resolver.ResolveAsync();

		// Assert
		client.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("/0/ServiceModel/ApplicationInfoService.svc/GetApplicationInfo")),
			Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Two factory.Create() calls sharing the singleton cache produce a single GetApplicationInfo probe within the TTL (AC-05/M-5).")]
	public async Task ResolveAsync_ShouldProbeOnce_WhenCalledTwiceAcrossFactoryCreatesWithinTtl()
	{
		// Arrange
		IApplicationClient client = SubstituteClient(
			"""{ "applicationInfo": { "sysValues": { "userCulture": { "displayValue": "uk-UA" } } } }""");
		IApplicationClientFactory clientFactory = Substitute.For<IApplicationClientFactory>();
		clientFactory.CreateEnvironmentClient(Arg.Any<EnvironmentSettings>()).Returns(client);
		ICurrentUserCultureCache sharedCache = new CurrentUserCultureCache(new FakeTimeProvider());
		CurrentUserCultureResolverFactory factory = new(
			clientFactory, new ServiceUrlBuilderFactory(), sharedCache, NullLoggerFactory.Instance);

		// Act — two distinct resolver instances built from the same factory share the singleton cache.
		CultureResolution first = await factory.Create(Env()).ResolveAsync();
		CultureResolution second = await factory.Create(Env()).ResolveAsync();

		// Assert
		first.Culture.Should().Be("uk-UA", because: "the first call resolves from the environment");
		second.Culture.Should().Be("uk-UA", because: "the second call is served from the shared cache");
		client.Received(1).ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("After the cache TTL elapses the next resolution probes the endpoint again (Mi-6).")]
	public async Task ResolveAsync_ShouldReprobe_WhenCacheTtlHasExpired()
	{
		// Arrange
		IApplicationClient client = SubstituteClient(
			"""{ "applicationInfo": { "sysValues": { "userCulture": { "displayValue": "uk-UA" } } } }""");
		FakeTimeProvider clock = new();
		ICurrentUserCultureCache cache = new CurrentUserCultureCache(clock);
		CurrentUserCultureResolver resolver = CreateResolver(client, cache: cache);

		// Act
		await resolver.ResolveAsync();
		clock.Advance(TimeSpan.FromMinutes(6));
		await resolver.ResolveAsync();

		// Assert — a resolution older than the 5-minute TTL must be refreshed (NSubstitute
		// verification carries no `because`; the intent is documented here).
		client.Received(2).ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("A failed resolution is not cached, so the next call re-probes and can recover.")]
	public async Task ResolveAsync_ShouldReprobe_WhenPreviousResolutionFailed()
	{
		// Arrange — first probe throws (transient), second returns a valid culture.
		IApplicationClient client = Substitute.For<IApplicationClient>();
		int call = 0;
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ =>
			{
				call++;
				if (call == 1)
				{
					throw new HttpRequestException("connection refused");
				}

				return """{ "applicationInfo": { "sysValues": { "userCulture": { "displayValue": "uk-UA" } } } }""";
			});
		CurrentUserCultureResolver resolver = CreateResolver(client);

		// Act
		CultureResolution firstResolution = await resolver.ResolveAsync();
		CultureResolution secondResolution = await resolver.ResolveAsync();

		// Assert
		firstResolution.Success.Should().BeFalse(because: "the first probe failed transiently");
		secondResolution.Success.Should().BeTrue(
			because: "a failure must not be cached, so the retry resolves the culture");
		secondResolution.Culture.Should().Be("uk-UA");
	}

	private static IApplicationClient SubstituteClient(string responseBody)
	{
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(responseBody);
		return client;
	}

	private static EnvironmentSettings Env(string uri = EnvironmentUri, bool isNetCore = true) =>
		new() { Uri = uri, IsNetCore = isNetCore };

	private static CurrentUserCultureResolver CreateResolver(
		IApplicationClient client,
		string environmentUri = EnvironmentUri,
		ICurrentUserCultureCache? cache = null,
		bool isNetCore = true) =>
		new(
			client,
			Env(environmentUri, isNetCore),
			new ServiceUrlBuilderFactory(),
			cache ?? new CurrentUserCultureCache(new FakeTimeProvider()),
			NullLogger<CurrentUserCultureResolver>.Instance);

	private sealed class FakeTimeProvider : TimeProvider
	{
		private DateTimeOffset _now = DateTimeOffset.Parse("2026-06-09T10:00:00Z");

		public override DateTimeOffset GetUtcNow() => _now;

		public void Advance(TimeSpan delta) => _now += delta;
	}
}
