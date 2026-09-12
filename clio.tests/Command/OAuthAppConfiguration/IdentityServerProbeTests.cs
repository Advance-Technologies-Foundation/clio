using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.OAuthAppConfiguration;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.OAuthAppConfiguration;

[TestFixture]
[Property("Module", "Command")]
internal sealed class IdentityServerProbeTests : BaseClioModuleTests {
	private IApplicationClientFactory _applicationClientFactory;
	private IOwnedApplicationClient _applicationClient;
	private IIdentityServerProbe _sut;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		_applicationClient = Substitute.For<IOwnedApplicationClient>();
		IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
		containerBuilder.AddSingleton(_applicationClientFactory);
		containerBuilder.AddSingleton(httpClientFactory);
	}

	public override void Setup() {
		base.Setup();
		_sut = Container.GetRequiredService<IIdentityServerProbe>();
		_applicationClientFactory.CreateBearerEnvironmentClient(Arg.Any<EnvironmentSettings>(),
			Arg.Any<string>())
			.Returns(_applicationClient);
	}

	public override void TearDown() {
		_applicationClient?.Dispose();
		base.TearDown();
	}

	[Test]
	[Description("The bearer DataService smoke test uses an ephemeral CreatioClient instead of raw HttpClient.")]
	public void RunBearerDataServiceSmokeTest_ShouldUseApplicationClientFactory_WhenTokenIsPresent() {
		// Arrange
		EnvironmentSettings environment = new() { Uri = "https://dev.creatio.com", IsNetCore = true };
		_applicationClient.ExecutePostRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));

		// Act
		int status = _sut.RunBearerDataServiceSmokeTest(environment,
			"https://dev.creatio.com/DataService/json/SyncReply/SelectQuery", "opaque-token");

		// Assert
		status.Should().Be(204, because: "the probe reports the exact Creatio response status");
		_applicationClientFactory.Received(1).CreateBearerEnvironmentClient(environment, "opaque-token");
		_ = _applicationClient.Received(1).ExecutePostRequestAsync(
			"https://dev.creatio.com/DataService/json/SyncReply/SelectQuery",
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"Contact\"")),
			100_000, 1, 1, Arg.Any<CancellationToken>());
		((IDisposable)_applicationClient).Received(1).Dispose();
	}

	[Test]
	[Description("The bearer DataService smoke test skips client creation when the token is empty.")]
	public void RunBearerDataServiceSmokeTest_ShouldReturnZero_WhenTokenIsMissing() {
		// Act
		int status = _sut.RunBearerDataServiceSmokeTest(new EnvironmentSettings(), "https://dev", string.Empty);

		// Assert
		status.Should().Be(0, because: "no authenticated request can be issued without a token");
		_applicationClientFactory.DidNotReceive().CreateBearerEnvironmentClient(
			Arg.Any<EnvironmentSettings>(), Arg.Any<string>());
	}
}
