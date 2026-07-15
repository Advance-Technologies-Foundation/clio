using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using Clio.Common.IIS;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class PinCertificateCommandTests : BaseCommandTests<PinCertificateOptions> {
	private const string Thumbprint = "AABBCCDDEEFF00112233445566778899AABBCCDD";
	private ISettingsRepository _settingsRepository;
	private IIisCertificateResolver _resolver;
	private ICertificateSelectionPrompt _prompt;
	private PinCertificateCommand _sut;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_resolver = Substitute.For<IIisCertificateResolver>();
		_prompt = Substitute.For<ICertificateSelectionPrompt>();
		containerBuilder.AddSingleton(_settingsRepository);
		containerBuilder.AddSingleton(_resolver);
		containerBuilder.AddSingleton(_prompt);
	}

	public override void Setup() {
		base.Setup();
		_sut = Container.GetRequiredService<PinCertificateCommand>();
	}

	public override void TearDown() {
		_settingsRepository.ClearReceivedCalls();
		_resolver.ClearReceivedCalls();
		_prompt.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Normalizes and persists an explicitly supplied usable certificate thumbprint without prompting.")]
	public void Execute_ShouldPersistExplicitUsableThumbprint_WithoutPrompting() {
		// Arrange
		IisCertificateInfo certificate = Certificate();
		_resolver.GetUsableCertificates(Arg.Any<string>(), Arg.Any<DateTimeOffset>()).Returns([certificate]);
		PinCertificateOptions options = new() { Thumbprint = "aa bb cc dd ee ff 00 11 22 33 44 55 66 77 88 99 aa bb cc dd" };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "a supplied thumbprint matching an eligible installed certificate is valid");
		_settingsRepository.Received(1).SetPinnedIisCertificateThumbprint(Thumbprint);
		_prompt.DidNotReceive().Select(Arg.Any<IReadOnlyList<IisCertificateInfo>>());
	}

	[Test]
	[Description("Shows eligible certificates through the prompt and persists the interactive selection when no thumbprint is supplied.")]
	public void Execute_ShouldPersistInteractiveSelection_WhenThumbprintIsOmitted() {
		// Arrange
		IisCertificateInfo certificate = Certificate();
		_resolver.GetUsableCertificates(Arg.Any<string>(), Arg.Any<DateTimeOffset>()).Returns([certificate]);
		_prompt.Select(Arg.Any<IReadOnlyList<IisCertificateInfo>>()).Returns(Thumbprint);

		// Act
		int result = _sut.Execute(new PinCertificateOptions());

		// Assert
		result.Should().Be(0, because: "choosing an eligible displayed certificate should succeed");
		_settingsRepository.Received(1).SetPinnedIisCertificateThumbprint(Thumbprint);
	}

	[Test]
	[Description("Returns an error and leaves settings unchanged when an explicit thumbprint is not usable for the host.")]
	public void Execute_ShouldFail_WhenExplicitThumbprintIsUnavailable() {
		// Arrange
		_resolver.GetUsableCertificates(Arg.Any<string>(), Arg.Any<DateTimeOffset>()).Returns([]);

		// Act
		int result = _sut.Execute(new PinCertificateOptions { Thumbprint = Thumbprint });

		// Assert
		result.Should().Be(1, because: "an explicit invalid pin should be reported instead of silently persisting unusable state");
		_settingsRepository.DidNotReceive().SetPinnedIisCertificateThumbprint(Arg.Any<string>());
	}

	[Test]
	[Description("Clears the persisted IIS certificate preference without inspecting the certificate store.")]
	public void Execute_ShouldClearPin_WhenClearIsRequested() {
		// Arrange

		// Act
		int result = _sut.Execute(new PinCertificateOptions { Clear = true });

		// Assert
		result.Should().Be(0, because: "clearing a preference is valid even if no certificates are installed");
		_settingsRepository.Received(1).SetPinnedIisCertificateThumbprint(null);
		_resolver.DidNotReceive().GetUsableCertificates(Arg.Any<string>(), Arg.Any<DateTimeOffset>());
	}

	private static IisCertificateInfo Certificate() => new(
		Thumbprint,
		"CN=k-host.example.com",
		["k-host.example.com"],
		DateTimeOffset.Now.AddDays(-1),
		DateTimeOffset.Now.AddYears(1),
		true,
		true);
}
