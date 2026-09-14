using System;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using Clio.Utilities;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class RegAppCommandAutoDetectionTests {
	private readonly ILogger _logger = Substitute.For<ILogger>();

	[Test]
	[Description("Uses the runtime detector and persists its result when a URI is provided without an explicit IsNetCore override.")]
	public void Execute_Should_AutoDetect_Runtime_When_Uri_Is_Provided_And_IsNetCore_Is_Omitted() {
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IEnvironmentRuntimeDetectionService runtimeDetectionService = Substitute.For<IEnvironmentRuntimeDetectionService>();
		runtimeDetectionService.Detect(Arg.Any<EnvironmentSettings>()).Returns(true);
		RegAppCommand sut = new(
			settingsRepository,
			applicationClientFactory,
			Substitute.For<IPowerShellFactory>(),
			_logger,
			runtimeDetectionService);
		RegAppOptions options = new() {
			EnvironmentName = "sandbox",
			Uri = "http://example.invalid/",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		int result = sut.Execute(options);

		result.Should().Be(0,
			because: "auto-detection should allow normal registration flow to continue");
		runtimeDetectionService.Received(1).Detect(Arg.Is<EnvironmentSettings>(settings =>
			settings.Uri == "http://example.invalid"
			&& settings.Login == "Supervisor"
			&& settings.Password == "Supervisor"));
		settingsRepository.Received().ConfigureEnvironment("sandbox", Arg.Is<EnvironmentSettings>(settings =>
			settings.Uri == "http://example.invalid"
			&& settings.IsNetCore));
	}

	[Test]
	[Description("Persists nothing and reports a failure when runtime auto-detection refuses to name a runtime.")]
	public void Execute_Should_Not_Register_The_Environment_When_Runtime_Detection_Refuses() {
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		IEnvironmentRuntimeDetectionService runtimeDetectionService = Substitute.For<IEnvironmentRuntimeDetectionService>();
		runtimeDetectionService.Detect(Arg.Any<EnvironmentSettings>())
			.Throws(new InvalidOperationException("Unable to auto-detect the Creatio runtime."));
		RegAppCommand sut = new(
			settingsRepository,
			Substitute.For<IApplicationClientFactory>(),
			Substitute.For<IPowerShellFactory>(),
			_logger,
			runtimeDetectionService);
		RegAppOptions options = new() {
			EnvironmentName = "sandbox",
			Uri = "http://example.invalid/",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "a runtime that could not be determined is a registration failure, not a warning");
		settingsRepository.DidNotReceiveWithAnyArgs().ConfigureEnvironment(default!, default!);
		_logger.Received().WriteError(Arg.Is<string>(text => text.Contains("Unable to auto-detect the Creatio runtime")));
	}

	[Test]
	[Description("Writes the environment exactly once, with the detected runtime, instead of persisting a guess first.")]
	public void Execute_Should_Persist_The_Environment_Once_With_The_Detected_Runtime() {
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		IEnvironmentRuntimeDetectionService runtimeDetectionService = Substitute.For<IEnvironmentRuntimeDetectionService>();
		runtimeDetectionService.Detect(Arg.Any<EnvironmentSettings>()).Returns(true);
		RegAppCommand sut = new(
			settingsRepository,
			Substitute.For<IApplicationClientFactory>(),
			Substitute.For<IPowerShellFactory>(),
			_logger,
			runtimeDetectionService);
		RegAppOptions options = new() {
			EnvironmentName = "sandbox",
			Uri = "http://example.invalid/",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "detection succeeded, so registration should complete normally");
		settingsRepository.Received(1).ConfigureEnvironment("sandbox", Arg.Any<EnvironmentSettings>());
		settingsRepository.DidNotReceive().ConfigureEnvironment("sandbox", Arg.Is<EnvironmentSettings>(settings => !settings.IsNetCore));
	}

	[Test]
	[Description("Keeps the runtime already recorded for an existing environment, and completes the rest of the update, when detection refuses at the same URI.")]
	public void Execute_Should_Keep_The_Recorded_Runtime_When_Detection_Refuses_At_The_Same_Uri() {
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.FindEnvironment("sandbox").Returns(new EnvironmentSettings {
			Uri = "http://example.invalid",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true
		});
		IEnvironmentRuntimeDetectionService runtimeDetectionService = Substitute.For<IEnvironmentRuntimeDetectionService>();
		runtimeDetectionService.Detect(Arg.Any<EnvironmentSettings>())
			.Throws(new InvalidOperationException("Unable to auto-detect the Creatio runtime."));
		RegAppCommand sut = new(
			settingsRepository,
			Substitute.For<IApplicationClientFactory>(),
			Substitute.For<IPowerShellFactory>(),
			_logger,
			runtimeDetectionService);
		RegAppOptions options = new() {
			EnvironmentName = "sandbox",
			Uri = "http://example.invalid/",
			Login = "Supervisor",
			Password = "RotatedPassword"
		};

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "a site whose runtime is already recorded, re-registered at the same URI, must not lose the rest of the update over a runtime nobody asked to change");
		settingsRepository.Received(1).ConfigureEnvironment("sandbox", Arg.Is<EnvironmentSettings>(settings =>
			settings.IsNetCore
			&& settings.Password == "RotatedPassword"));
		_logger.Received().WriteWarning(Arg.Is<string>(text =>
			text.Contains("Unable to auto-detect the Creatio runtime")
			&& text.Contains("--IsNetCore")));
	}

	[Test]
	[Description("Still refuses to register when detection fails and the supplied URI differs from the one the environment was registered with.")]
	public void Execute_Should_Not_Register_When_Detection_Refuses_At_A_Different_Uri() {
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.FindEnvironment("sandbox").Returns(new EnvironmentSettings {
			Uri = "http://example.invalid",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true
		});
		IEnvironmentRuntimeDetectionService runtimeDetectionService = Substitute.For<IEnvironmentRuntimeDetectionService>();
		runtimeDetectionService.Detect(Arg.Any<EnvironmentSettings>())
			.Throws(new InvalidOperationException("Unable to auto-detect the Creatio runtime."));
		RegAppCommand sut = new(
			settingsRepository,
			Substitute.For<IApplicationClientFactory>(),
			Substitute.For<IPowerShellFactory>(),
			_logger,
			runtimeDetectionService);
		RegAppOptions options = new() {
			EnvironmentName = "sandbox",
			Uri = "http://another.invalid/",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		// Act
		int result = sut.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "a different URI is a different site, and the recorded runtime says nothing about it");
		settingsRepository.DidNotReceiveWithAnyArgs().ConfigureEnvironment(default!, default!);
	}

	[Test]
	[Description("Preserves the stored runtime flag when an existing environment is updated without a new URI or explicit IsNetCore override.")]
	public void Execute_Should_Preserve_Existing_Runtime_When_Uri_Is_Not_Provided() {
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.FindEnvironment("sandbox").Returns(new EnvironmentSettings {
			Uri = "http://example.invalid",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true
		});
		IEnvironmentRuntimeDetectionService runtimeDetectionService = Substitute.For<IEnvironmentRuntimeDetectionService>();
		RegAppCommand sut = new(
			settingsRepository,
			Substitute.For<IApplicationClientFactory>(),
			Substitute.For<IPowerShellFactory>(),
			_logger,
			runtimeDetectionService);
		RegAppOptions options = new() {
			EnvironmentName = "sandbox",
			Login = "AnotherSupervisor"
		};

		int result = sut.Execute(options);

		result.Should().Be(0,
			because: "partial updates should keep the previously detected runtime when the site URI is unchanged");
		runtimeDetectionService.DidNotReceive().Detect(Arg.Any<EnvironmentSettings>());
		settingsRepository.Received(1).ConfigureEnvironment("sandbox", Arg.Is<EnvironmentSettings>(settings =>
			settings.IsNetCore
			&& settings.Login == "AnotherSupervisor"));
	}

	[Test]
	[Description("Uses the IIS scanner site type instead of forcing IsNetCore to false when add-from-iis imports environments.")]
	public void Execute_Should_Use_Discovered_Iis_Runtime_When_AddFromIis_Is_Enabled() {
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		IIisEnvironmentDiscoveryService iisEnvironmentDiscoveryService = Substitute.For<IIisEnvironmentDiscoveryService>();
		iisEnvironmentDiscoveryService.Discover("remote-user", "remote-pass", "ts1-infr-web01")
			.Returns([
				new IisEnvironmentDescriptor("framework-site", @"C:\Creatio\Framework", "http://framework.local", false),
				new IisEnvironmentDescriptor("core-site", @"C:\Creatio\Core", "http://core.local", true)
			]);
		RegAppCommand sut = new(
			settingsRepository,
			Substitute.For<IApplicationClientFactory>(),
			Substitute.For<IPowerShellFactory>(),
			_logger,
			null,
			iisEnvironmentDiscoveryService);
		RegAppOptions options = new() {
			FromIis = true,
			Host = "ts1-infr-web01",
			Login = "remote-user",
			Password = "remote-pass"
		};

		int result = sut.Execute(options);

		result.Should().Be(0,
			because: "IIS import should register every discovered Creatio site");
		settingsRepository.Received(1).ConfigureEnvironment("framework-site", Arg.Is<EnvironmentSettings>(settings =>
			!settings.IsNetCore
			&& settings.Uri == "http://framework.local"));
		settingsRepository.Received(1).ConfigureEnvironment("core-site", Arg.Is<EnvironmentSettings>(settings =>
			settings.IsNetCore
			&& settings.Uri == "http://core.local"));
	}
}
