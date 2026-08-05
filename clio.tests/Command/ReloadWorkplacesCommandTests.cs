using System;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public class ReloadWorkplacesCommandTests : BaseCommandTests<ReloadWorkplacesOptions> {

	#region Fields: Private

	private readonly ILogger _loggerMock = Substitute.For<ILogger>();
	private readonly IWorkplaceCacheReloader _reloaderMock = Substitute.For<IWorkplaceCacheReloader>();

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder){
		containerBuilder.AddSingleton<IWorkplaceCacheReloader>(_reloaderMock);
		containerBuilder.AddSingleton<ILogger>(_loggerMock);
		base.AdditionalRegistrations(containerBuilder);
	}

	#endregion

	#region Methods: Public

	[TearDown]
	public void ClearReceivedCalls(){
		_reloaderMock.ClearReceivedCalls();
		_loggerMock.ClearReceivedCalls();
	}

	[Test]
	[Description("Reloads the navigation caches and tells the user to refresh, so the agent does not fall back to prescribing a re-login after a successful publish.")]
	public void Execute_ShouldReloadAndReportNoReloginNeeded_WhenTheGateSucceeds(){
		// Arrange
		ReloadWorkplacesCommand sut = Container.GetRequiredService<ReloadWorkplacesCommand>();
		ReloadWorkplacesOptions options = new() {Environment = "dev"};

		// Act
		int actual = sut.Execute(options);

		// Assert
		actual.Should().Be(0,
			because: "a successful reload is the whole point of the command and must report success");
		_reloaderMock.Received(1).Reload();
		_loggerMock.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("refresh the page", StringComparison.OrdinalIgnoreCase)));
	}

	[Test]
	[Description("Surfaces the reload failure reason verbatim and returns a non-zero exit code, so a stale navigation cache is never reported as published.")]
	public void Execute_ShouldReportTheFailureReason_WhenTheGateCannotReload(){
		// Arrange
		const string reason = "IWorkplaceManager was not found on this environment.";
		ReloadWorkplacesCommand sut = Container.GetRequiredService<ReloadWorkplacesCommand>();
		ReloadWorkplacesOptions options = new() {Environment = "dev"};
		_reloaderMock.When(reloader => reloader.Reload())
			.Throw(new InvalidOperationException(reason));

		// Act
		int actual = sut.Execute(options);

		// Assert
		actual.Should().Be(1,
			because: "reporting success on a failed reload would let the agent promise a refresh that shows nothing");
		_loggerMock.Received(1).WriteError(reason);
		_loggerMock.DidNotReceive().WriteInfo(Arg.Any<string>());
	}

	#endregion

}
