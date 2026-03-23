using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Requests;
using Clio.UserEnvironment;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Requests;

[TestFixture]
public class UnregisterEnvironmentHandlerTests {
	[Test]
	[Description("Maps the UnregisterEnvironment deep-link query parameter into UnregAppOptions and forwards it to UnregAppCommand.")]
	public async Task Handle_ShouldForwardEnvironmentNameToUnregAppCommand() {
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		UnregAppCommand unregCommand = new(settingsRepository);
		UnregisterEnvironmentHandler sut = new(unregCommand);
		UnregisterEnvironment request = new() {
			Content = "clio://UnregisterEnvironment?name=studio-dev"
		};

		// Act
		await sut.Handle(request, CancellationToken.None);

		// Assert
		settingsRepository.Received(1).RemoveEnvironment("studio-dev");
		settingsRepository.DidNotReceive().RemoveAllEnvironment();
	}
}
