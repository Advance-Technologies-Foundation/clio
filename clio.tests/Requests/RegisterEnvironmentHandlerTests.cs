using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using Clio.Requests;
using Clio.UserEnvironment;
using Clio.Utilities;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Requests;

[TestFixture]
[Category("Unit")]
[Property("Module", "Requests")]
public class RegisterEnvironmentHandlerTests {
	[Test]
	[Description("Maps the RegisterEnvironment deep-link query parameters into RegAppOptions and forwards them to RegAppCommand.")]
	public async Task Handle_ShouldForwardSupportedArgumentsToRegAppCommand() {
		// Arrange
		SpyRegAppCommand regCommand = new();
		RegisterEnvironmentHandler sut = new(regCommand);
		RegisterEnvironment request = new() {
			Content =
				"clio://RegisterEnvironment?uri=http://localhost:5000&login=Supervisor&password=Supervisor&isnetcore=true&safe=false&environmentpath=/app&environmentname=studio-dev"
		};

		// Act
		await sut.Handle(request, CancellationToken.None);

		// Assert
		regCommand.CapturedOptions.Should().NotBeNull(because: "the handler should invoke RegAppCommand with parsed options");
		regCommand.CapturedOptions!.EnvironmentName.Should().Be("studio-dev",
			because: "the explicit environment name should be forwarded");
		regCommand.CapturedOptions.Uri.Should().Be("http://localhost:5000",
			because: "the URI should be preserved without a trailing slash");
		regCommand.CapturedOptions.Login.Should().Be("Supervisor",
			because: "the login parameter should be forwarded");
		regCommand.CapturedOptions.Password.Should().Be("Supervisor",
			because: "the password parameter should be forwarded");
		regCommand.CapturedOptions.IsNetCore.Should().BeTrue(
			because: "the isnetcore parameter should be parsed as a boolean");
		regCommand.CapturedOptions.Safe.Should().Be("False",
			because: "the safe parameter should be forwarded using RegAppOptions string representation");
		regCommand.CapturedOptions.EnvironmentPath.Should().Be("/app",
			because: "the environment path should be forwarded");
	}

	[Test]
	[Description("Derives an environment name from the URI when the deep link does not provide one explicitly.")]
	public async Task Handle_ShouldDeriveEnvironmentNameFromUri_WhenNameIsMissing() {
		// Arrange
		SpyRegAppCommand regCommand = new();
		RegisterEnvironmentHandler sut = new(regCommand);
		RegisterEnvironment request = new() {
			Content =
				"clio://RegisterEnvironment?uri=http://studio-dev.example.local:5000&login=Supervisor&password=Supervisor&isnetcore=true"
		};

		// Act
		await sut.Handle(request, CancellationToken.None);

		// Assert
		regCommand.CapturedOptions.Should().NotBeNull(because: "the handler should invoke RegAppCommand with parsed options");
		regCommand.CapturedOptions!.EnvironmentName.Should().Be("studio-dev-example-local-5000",
			because: "the handler should derive a stable default name from the URI host and non-default port");
	}

	private sealed class SpyRegAppCommand : RegAppCommand {
		public SpyRegAppCommand()
			: base(
				Substitute.For<ISettingsRepository>(),
				Substitute.For<IApplicationClientFactory>(),
				Substitute.For<IPowerShellFactory>(),
				Substitute.For<ILogger>()) {
		}

		public RegAppOptions CapturedOptions { get; private set; }

		public override int Execute(RegAppOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
