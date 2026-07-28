using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Story 3 (ENG-93347) coverage of <see cref="ApplicationListService"/>, focused on the new
/// settings-based <c>GetApplications</c> overload: it must never consult
/// <see cref="ISettingsRepository"/>, must reject a null settings argument before any factory
/// invocation, and must produce the same payload as the name-based path.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ApplicationListServiceTests {

	private static readonly Guid AlphaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
	private static readonly Guid BetaId = Guid.Parse("22222222-2222-2222-2222-222222222222");

	private ISettingsRepository _settingsRepository = null!;
	private IApplicationClientFactory _applicationClientFactory = null!;
	private IApplicationClient _applicationClient = null!;
	private ApplicationListService _sut = null!;
	private EnvironmentSettings _environment = null!;

	[SetUp]
	public void SetUp() {
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		_applicationClient = Substitute.For<IApplicationClient>();
		_environment = new EnvironmentSettings {
			Uri = "https://example.invalid",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true
		};
		_settingsRepository.FindEnvironment("sandbox").Returns(_environment);
		_applicationClientFactory.CreateEnvironmentClient(_environment).Returns(_applicationClient);
		_sut = new ApplicationListService(
			_settingsRepository,
			_applicationClientFactory,
			new ServiceUrlBuilderFactory());
	}

	private void ConfigureHappyPathResponse() {
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns($$"""
				{
					"success": true,
					"rows": [
						{ "Id": "{{BetaId}}", "Code": "BETA", "Name": "Beta", "Version": "2.0.0", "Description": "Beta description" },
						{ "Id": "{{AlphaId}}", "Code": "ALPHA", "Name": "Alpha", "Version": "1.0.0", "Description": "Alpha description" }
					]
				}
				""");
	}

	[Test]
	[Description("The settings-based GetApplications overload loads the same ordered application list as the name-based one without any ISettingsRepository lookup.")]
	public void GetApplications_ShouldLoadApplicationsWithoutRepositoryLookup_WhenSettingsProvided() {
		// Arrange
		ConfigureHappyPathResponse();
		_settingsRepository.ClearReceivedCalls();

		// Act
		IReadOnlyList<InstalledApplicationListItem> applications = _sut.GetApplications(_environment, null, null);

		// Assert
		applications.Select(application => application.Name).Should().Equal(new[] { "Alpha", "Beta" },
			because: "the settings-based overload must keep the deterministic name/code ordering of the name-based path");
		applications[0].Id.Should().Be(AlphaId,
			because: "the settings-based overload must preserve installed-application identifiers");
		_settingsRepository.ReceivedCalls().Should().BeEmpty(
			because: "the settings-based overload must never consult ISettingsRepository — the caller already supplied settings");
	}

	[Test]
	[Description("The settings-based GetApplications overload rejects a null EnvironmentSettings argument before the client factory is invoked.")]
	public void GetApplications_ShouldThrowArgumentNullException_WhenSettingsIsNull() {
		// Arrange

		// Act
		Action action = () => _sut.GetApplications((EnvironmentSettings)null!, null, null);

		// Assert
		action.Should().Throw<ArgumentNullException>(
			because: "a null settings argument is a programming error that must fail fast in the guard clause");
		_applicationClientFactory.DidNotReceiveWithAnyArgs().CreateEnvironmentClient(default);
	}

	[Test]
	[Description("The name-based GetApplications overload keeps resolving the environment through ISettingsRepository and returns the same ordered payload — the pre-change baseline is unchanged.")]
	public void GetApplications_ShouldResolveEnvironmentByName_WhenEnvironmentNameProvided() {
		// Arrange
		ConfigureHappyPathResponse();

		// Act
		IReadOnlyList<InstalledApplicationListItem> applications = _sut.GetApplications("sandbox", null, null);

		// Assert
		applications.Select(application => application.Code).Should().Equal(new[] { "ALPHA", "BETA" },
			because: "the name-based path must keep producing the same deterministic ordering after the core extraction");
		_settingsRepository.Received(1).FindEnvironment("sandbox");
	}

	[Test]
	[Description("The name-based GetApplications overload keeps rejecting a blank environment name with the pre-change ArgumentException.")]
	public void GetApplications_ShouldThrowArgumentException_WhenEnvironmentNameIsBlank() {
		// Arrange

		// Act
		Action action = () => _sut.GetApplications(" ", null, null);

		// Assert
		action.Should().Throw<ArgumentException>()
			.WithMessage("Environment name is required.*",
				because: "the CLI-facing name-based contract must be byte-for-byte unchanged by the settings-based addition");
	}
}
