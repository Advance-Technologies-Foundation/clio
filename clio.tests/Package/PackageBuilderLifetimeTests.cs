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

	[Test]
	[Description("A poll give-up is reported as a composed InvalidOperationException carrying the poll fault, and the response-less HTTP request is still cancelled, observed and disposed (review finding on PackageBuilder.CompileWithPolling: the memory barrier was fixed but nothing pinned the behaviour).")]
	public void Build_ShouldThrowComposedFaultAndDisposeClient_WhenPollGivesUp() {
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
		InvalidOperationException pollFault = new("Compilation history is unreachable after 10 rounds.");
		poller.When(value => value.Poll(Arg.Any<DateTime>(), Arg.Any<CancellationToken>(),
			Arg.Any<Action<CompilationHistory>>())).Do(_ => throw pollFault);
		PackageBuilder sut = new(settings, factory, urlBuilder, Substitute.For<ILogger>(), poller);

		// Act
		Action act = () => sut.Build(["UsrPackage"]);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a give-up on the poll thread must surface on the main thread instead of leaving the loop running to its full timeout with nothing watching the compile")
			.WithMessage("*could not be monitored*")
			.WithInnerException<InvalidOperationException>()
			.WithMessage(pollFault.Message);
		requestCancellation.IsCancellationRequested.Should().BeTrue(
			because: "the pending response-less request must be cancelled and observed before the fault is thrown");
		client.Received(1).Dispose();
	}

	[Test]
	[Description("A failed baseline read is reported as a warning and the compilation still goes ahead from DateTime.MinValue: after ClassifyingDataProvider a failed OData round throws instead of returning empty, so an unguarded read would abort the build before the compilation request was ever sent (review finding on the GetBaseline call sites).")]
	public void Build_ShouldWarnAndCompile_WhenBaselineReadThrows() {
		// Arrange
		EnvironmentSettings settings = new() { Uri = "https://dev.creatio.com" };
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Timeout.Infinite, 1, 1,
				Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult(new HttpResponseMessage()));
		IApplicationClientFactory factory = Substitute.For<IApplicationClientFactory>();
		factory.CreateClient(settings).Returns(client);
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("https://dev.creatio.com/build");
		ICompilationHistoryPoller poller = Substitute.For<ICompilationHistoryPoller>();
		poller.GetBaseline()
			.Returns<CompilationHistory>(_ => throw new InvalidOperationException("Failed reading compilation history."));
		poller.When(value => value.Poll(Arg.Any<DateTime>(), Arg.Any<CancellationToken>(),
			Arg.Any<Action<CompilationHistory>>())).Do(call => call.ArgAt<CancellationToken>(1).WaitHandle.WaitOne());
		ILogger logger = Substitute.For<ILogger>();
		PackageBuilder sut = new(settings, factory, urlBuilder, logger, poller);

		// Act
		Action act = () => sut.Build(["UsrPackage"]);

		// Assert
		act.Should().NotThrow(
			because: "a transient compilation-history failure must not abort a compile that the server would have run");
		logger.Received().WriteWarning(Arg.Is<string>(message =>
			message.Contains("compilation history baseline", StringComparison.Ordinal)));
		poller.Received(1).Poll(DateTime.MinValue, Arg.Any<CancellationToken>(), Arg.Any<Action<CompilationHistory>>());
	}

	private static async Task<HttpResponseMessage> WaitForCancellationAsync(CancellationToken cancellationToken) {
		try {
			await Task.Delay(Timeout.Infinite, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			throw new HttpRequestException("The server closed the settled compilation connection.");
		}
		return new HttpResponseMessage();
	}
}
