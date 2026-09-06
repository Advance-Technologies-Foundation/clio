using System;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// DISPATCH-level counterpart to <see cref="EmittedSchemaRequiredContractTests"/>: the schema guards
/// assert what the contract SAYS, these assert what the resolver actually ACCEPTS, so the two are held
/// against each other instead of only against themselves (issue #965, AC-4).
/// </summary>
/// <remarks>
/// The connection alternative every environment-sensitive contract advertises is
/// <c>[["environment-name"], ["uri","login","password"]]</c>. Only a real resolve settles whether that
/// second branch describes the runtime: <see cref="ToolCommandResolver"/>'s environment-less path fills an
/// <see cref="Clio.EnvironmentSettings"/> from the options and gates solely on a non-empty
/// <c>Uri</c>. It therefore accepts a uri WITHOUT credentials too — the advertised triple is stricter than
/// the runtime, deliberately, for consistency with the 75 curated contracts that already spell it that
/// way. That asymmetry is recorded here so it stays a decision instead of drifting into a surprise.
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
[NonParallelizable]
public sealed class ConnectionAlternativeDispatchTests {

	private const string EnvironmentNameValue = "dev";
	private const string UriValue = "http://localhost";
	private const string CredentialValue = "Supervisor";

	private IFileSystem _originalFileSystem;

	[SetUp]
	public void SetUp() {
		// SettingsRepository.FileSystem is a process-wide static; swapping it is why this fixture is
		// [NonParallelizable].
		_originalFileSystem = SettingsRepository.FileSystem;
		SettingsRepository.FileSystem = TestFileSystem.MockFileSystem();
	}

	[TearDown]
	public void TearDown() {
		SettingsRepository.FileSystem = _originalFileSystem;
	}

	[Test]
	[Category("Unit")]
	[Description("A payload carrying ONLY environment-name resolves a command, because the first branch of the advertised connection any-of must be dispatchable on its own (issue #965).")]
	public void Resolve_Should_Accept_EnvironmentNameOnlyPayload() {
		// Arrange
		ToolCommandResolver resolver = CreateResolverWithRegisteredEnvironment();
		EnvironmentOptions options = new() { Environment = EnvironmentNameValue };

		// Act
		Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(options);

		// Assert
		act.Should().NotThrow(
			because: "the contract tells a caller that a registered environment name alone is a complete " +
				"connection payload, so a call shaped that way must not be rejected");
	}

	[Test]
	[Category("Unit")]
	[Description("A payload carrying ONLY the uri/login/password triple resolves a command, because the second branch of the advertised connection any-of must be dispatchable on its own (issue #965).")]
	public void Resolve_Should_Accept_DirectCredentialPayload() {
		// Arrange
		ToolCommandResolver resolver = CreateResolverWithoutRegisteredEnvironments();
		EnvironmentOptions options = new() {
			Uri = UriValue,
			Login = CredentialValue,
			Password = CredentialValue
		};

		// Act
		Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(options);

		// Assert
		act.Should().NotThrow(
			because: "the explicit uri/login/password triple is the documented emergency fallback, so the " +
				"branch the contract advertises must actually dispatch");
	}

	[Test]
	[Category("Unit")]
	[Description("A payload carrying a uri and NO credentials also resolves, recording that the advertised uri/login/password triple is deliberately stricter than the runtime rather than a description of it (issue #965).")]
	public void Resolve_Should_Accept_UriWithoutCredentials_ShowingTheAdvertisedTripleIsStricterThanRuntime() {
		// Arrange
		ToolCommandResolver resolver = CreateResolverWithoutRegisteredEnvironments();
		EnvironmentOptions options = new() { Uri = UriValue };

		// Act
		Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(options);

		// Assert
		act.Should().NotThrow(
			because: "the environment-less resolution path gates only on a non-empty Uri, so login and " +
				"password are not what makes the second branch dispatchable; the contract keeps asking for " +
				"the full triple for consistency with the curated contracts, and this test is what stops " +
				"that consistency choice from being mistaken for a runtime requirement");
	}

	private static ToolCommandResolver CreateResolverWithRegisteredEnvironment() {
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.IsEnvironmentExists(EnvironmentNameValue).Returns(true);
		settingsRepository.FindEnvironment(EnvironmentNameValue).Returns(new Clio.EnvironmentSettings {
			Uri = UriValue,
			Login = CredentialValue,
			Password = CredentialValue
		});
		return CreateResolver(settingsRepository, HealthyBootstrap());
	}

	private static ToolCommandResolver CreateResolverWithoutRegisteredEnvironments() {
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.IsEnvironmentExists(Arg.Any<string>()).Returns(false);
		return CreateResolver(settingsRepository, HealthyBootstrap());
	}

	private static ISettingsBootstrapService HealthyBootstrap() {
		ISettingsBootstrapService settingsBootstrapService = Substitute.For<ISettingsBootstrapService>();
		settingsBootstrapService.GetReport().Returns(new SettingsBootstrapReport(
			"healthy", SettingsRepository.AppSettingsFile, EnvironmentNameValue, EnvironmentNameValue,
			1, [], [], true, true));
		return settingsBootstrapService;
	}

	private static ToolCommandResolver CreateResolver(
		ISettingsRepository settingsRepository,
		ISettingsBootstrapService settingsBootstrapService) =>
		new(
			settingsRepository,
			settingsBootstrapService,
			// Null Current keeps the resolver on the ordinary stdio (non-passthrough) path.
			Substitute.For<ICredentialContextAccessor>(),
			Substitute.For<ITargetUrlValidator>(),
			new SessionContainerCache(SessionContainerCacheDefaults.IdleTtl, SessionContainerCacheDefaults.MaxSessions),
			new SessionTargetNormalizer());
}
