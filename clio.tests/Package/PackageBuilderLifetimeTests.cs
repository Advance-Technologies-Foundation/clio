using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.CreatioModel;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public sealed class PackageBuilderLifetimeTests {
	[Test]
	[Description("Cancels and observes the pending 8.3.3 HTTP request before a settled package build returns.")]
	public void Build_ShouldCancelAndDisposePendingClient_WhenCompilationHistorySettles() {
		// Arrange
		EnvironmentSettings settings = new() { Uri = "https://dev.creatio.com" };
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		CancellationToken requestCancellation = default;
		client.ExecutePostRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Timeout.Infinite, 1, 1,
			Arg.Any<CancellationToken>()).Returns(call => {
			requestCancellation = call.ArgAt<CancellationToken>(5);
			return WaitForCancellationAsync(requestCancellation);
		});
		IApplicationClientFactory factory = Substitute.For<IApplicationClientFactory>();
		factory.CreateClient(settings).Returns(client);
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("https://dev.creatio.com/build");
		ICompilationHistoryPoller poller = Substitute.For<ICompilationHistoryPoller>();
		poller.GetBaseline().Returns(new CompilationHistory { CreatedOn = DateTime.UtcNow.AddMinutes(-1) });
		poller.When(value => value.Poll(Arg.Any<DateTime>(), Arg.Any<CancellationToken>(),
			Arg.Any<Action<CompilationHistory>>())).Do(call => {
			CancellationToken pollCancellation = call.ArgAt<CancellationToken>(1);
			call.ArgAt<Action<CompilationHistory>>(2)(new CompilationHistory {
				CreatedOn = DateTime.UtcNow,
				Result = true
			});
			pollCancellation.WaitHandle.WaitOne();
		});
		PackageBuilder sut = new(settings, factory, urlBuilder, Substitute.For<ILogger>(), poller);

		// Act
		sut.Build(["UsrPackage"]);

		// Assert
		requestCancellation.IsCancellationRequested.Should().BeTrue(
			because: "a settled compilation must stop the response-less HTTP request");
		client.Received(1).Dispose();
	}

	private static async Task<HttpResponseMessage> WaitForCancellationAsync(CancellationToken cancellationToken) {
		await Task.Delay(Timeout.Infinite, cancellationToken);
		return new HttpResponseMessage();
	}
}
