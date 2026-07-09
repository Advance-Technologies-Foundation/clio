using System;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class ToolCommandResolverNoWriteTests {

	private const string SecretToken = "opaque-passthrough-token-value";

	[Test]
	[Description("Performs no settings-repository write and no persisted-secret write when resolving a passthrough credential context (AC-03 / SM-01).")]
	[Category("Unit")]
	public void Resolve_Should_Not_Persist_Anything_When_Passthrough_Context_Present() {
		// Arrange
		System.IO.Abstractions.IFileSystem originalFileSystem = SettingsRepository.FileSystem;
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		SettingsRepository.FileSystem = fileSystem;
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		ISettingsBootstrapService settingsBootstrapService = Substitute.For<ISettingsBootstrapService>();
		ICredentialContextAccessor accessor = Substitute.For<ICredentialContextAccessor>();
		accessor.Current.Returns(new CredentialContext(
			"https://acme.creatio.com",
			CredentialMaterial.FromAccessToken(SecretToken, "Bearer"),
			McpTransport.Http,
			true));
		ToolCommandResolver resolver = new(
			settingsRepository,
			settingsBootstrapService,
			new NonInteractiveConsole(),
			accessor,
			Substitute.For<ITargetUrlValidator>(),
			new SessionContainerCache(SessionContainerCacheDefaults.IdleTtl, SessionContainerCacheDefaults.MaxSessions));

		try {
			// Act
			Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(new EnvironmentOptions());

			// Assert
			act.Should().NotThrow(
				because: "a bearer passthrough context resolves against an ephemeral environment without dialing or persisting");
			settingsRepository.DidNotReceive().ConfigureEnvironment(Arg.Any<string>(), Arg.Any<EnvironmentSettings>());
			settingsRepository.DidNotReceive().SetActiveEnvironment(Arg.Any<string>());
			settingsRepository.DidNotReceive().RemoveEnvironment(Arg.Any<string>());
			settingsRepository.DidNotReceive().RemoveAllEnvironment();
			settingsRepository.DidNotReceive().SetAutoupdate(Arg.Any<bool>());
			settingsRepository.DidNotReceive().SetFeature(Arg.Any<string>(), Arg.Any<bool>());
			bool secretPersistedToDisk = fileSystem.AllFiles.Any(path =>
				fileSystem.GetFile(path).TextContents?.Contains(SecretToken, StringComparison.Ordinal) == true);
			secretPersistedToDisk.Should().BeFalse(
				because: "the per-request access token must never be written to appsettings.json, a session file, or any disk artifact (AC-03 / SM-01)");
		}
		finally {
			SettingsRepository.FileSystem = originalFileSystem;
		}
	}
}
