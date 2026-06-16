using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using Clio.Utilities;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
internal class ExternalLinkCommandDispatchTests : BaseCommandTests<ExternalLinkOptions> {
	private readonly ILogger _logger = Substitute.For<ILogger>();
	private readonly SpyRegAppCommand _regAppCommand = new();

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(_logger);
		containerBuilder.AddSingleton<RegAppCommand>(_regAppCommand);
	}

	[Test]
	[Description("Routes RegisterEnvironment deep links through ExternalLinkCommand, the type-keyed dispatcher, and the discovered handler.")]
	public void Execute_Should_Dispatch_RegisterEnvironment_Request_ToHandler() {
		// Arrange
		ExternalLinkCommand sut = Container.GetRequiredService<ExternalLinkCommand>();
		ExternalLinkOptions options = new() {
			Content = "clio://RegisterEnvironment?uri=http://localhost:5000&login=Supervisor&password=Supervisor&isnetcore=true&safe=false&environmentpath=/app&environmentname=studio-dev"
		};

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "the command should successfully dispatch supported deep links through the handler dispatcher");
		_regAppCommand.CapturedOptions.Should().NotBeNull(because: "the dispatched handler should forward parsed arguments to RegAppCommand");
		_regAppCommand.CapturedOptions!.EnvironmentName.Should().Be("studio-dev", because: "the explicit environment name should be preserved");
		_regAppCommand.CapturedOptions.Uri.Should().Be("http://localhost:5000", because: "the URI should be normalized before forwarding");
		_regAppCommand.CapturedOptions.Login.Should().Be("Supervisor", because: "the login should flow through the dispatched request");
		_regAppCommand.CapturedOptions.Password.Should().Be("Supervisor", because: "the password should flow through the dispatched request");
		_regAppCommand.CapturedOptions.IsNetCore.Should().BeTrue(because: "boolean query parameters should be parsed by the request handler");
		_regAppCommand.CapturedOptions.Safe.Should().Be("False", because: "the safe flag should preserve the command option representation");
		_regAppCommand.CapturedOptions.EnvironmentPath.Should().Be("/app", because: "additional request arguments should survive the dispatch");
	}

	[Test]
	[Description("Routes RegisterOAuthCredentials deep links through ExternalLinkCommand and resolves the OAuth registration handler from the dispatcher.")]
	public void Execute_Should_Dispatch_RegisterOAuthCredentials_Request_ToHandler() {
		// Arrange
		ExternalLinkCommand sut = Container.GetRequiredService<ExternalLinkCommand>();
		ExternalLinkOptions options = new() {
			Content = "clio://RegisterOAuthCredentials?protocol=https:&host=129117-crm-bundle.creatio.com&name=vscode&clientId=client-id&clientSecret=client-secret"
		};

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "the command should dispatch supported OAuth deep links through the handler dispatcher");
		_regAppCommand.CapturedOptions.Should().NotBeNull(because: "the OAuth handler should be resolved from the registered handler set");
		_regAppCommand.CapturedOptions!.EnvironmentName.Should().Be("vscode", because: "the OAuth handler should map the provided name into the target environment name");
		_regAppCommand.CapturedOptions.Uri.Should().Be("https://129117-crm-bundle.creatio.com", because: "the OAuth handler should reconstruct the base URL from protocol and host");
		_regAppCommand.CapturedOptions.AuthAppUri.Should().Be("https://129117-crm-bundle-is.creatio.com/connect/token", because: "the OAuth handler should derive the identity server endpoint");
		_regAppCommand.CapturedOptions.ClientId.Should().Be("client-id", because: "client credentials should survive the dispatch");
		_regAppCommand.CapturedOptions.ClientSecret.Should().Be("client-secret", because: "client credentials should survive the dispatch");
	}

	private sealed class SpyRegAppCommand : RegAppCommand {
		public SpyRegAppCommand()
			: base(
				Substitute.For<ISettingsRepository>(),
				Substitute.For<IApplicationClientFactory>(),
				Substitute.For<IPowerShellFactory>(),
				Substitute.For<ILogger>()) {
		}

		public RegAppOptions? CapturedOptions { get; private set; }

		public override int Execute(RegAppOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
