using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using Clio.Common.IIS;
using Clio.Common.ScenarioHandlers;
using Clio.Tests.Command;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
internal class CreateIISSiteHandlerTests : BaseClioModuleTests {
	private IProcessExecutor _processExecutor;
	private INetFrameworkHttpsConfigurator _httpsConfigurator;
	private IIisCertificateBindingService _certificateBindingService;
	private ICreateIISSiteHandler _handler;
	private string _deploymentRoot;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_processExecutor = Substitute.For<IProcessExecutor>();
		_httpsConfigurator = Substitute.For<INetFrameworkHttpsConfigurator>();
		_certificateBindingService = Substitute.For<IIisCertificateBindingService>();
		_processExecutor.Execute(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()).Returns("ok");
		containerBuilder.AddSingleton(_processExecutor);
		containerBuilder.AddSingleton(_httpsConfigurator);
		containerBuilder.AddSingleton(_certificateBindingService);
	}

	public override void Setup() {
		base.Setup();
		_handler = Container.GetRequiredService<ICreateIISSiteHandler>();
		_deploymentRoot = Path.Combine(Path.GetTempPath(), "clio-create-iis-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_deploymentRoot);
	}

	public override void TearDown() {
		if (Directory.Exists(_deploymentRoot)) {
			Directory.Delete(_deploymentRoot, recursive: true);
		}
		base.TearDown();
	}

	[Test]
	[Description("Validates the create IIS site request explicitly and rejects invalid scenario handler input before site creation runs.")]
	public async Task Handle_ShouldThrowValidationException_WhenCreateIISSiteRequestIsInvalid() {
		// Arrange
		CreateIISSiteRequest request = new() {
			Arguments = []
		};

		// Act
		Func<Task> act = async () => await _handler.Handle(request);

		// Assert
		await act.Should().ThrowAsync<ValidationException>(
			because: "the handler should run the registered FluentValidation validator before creating the IIS site");
	}

	[Test]
	[Description("Creates one HTTP binding without HTTPS configuration or certificate attachment.")]
	public async Task Handle_ShouldCreateOnlyHttpBinding_WhenProtocolIsHttp() {
		// Arrange
		CreateIISSiteRequest request = CreateRequest("http", isNetFramework: false, certificateThumbprint: string.Empty);

		// Act
		await _handler.Handle(request);

		// Assert
		_processExecutor.Received(1).Execute(
			Arg.Any<string>(),
			Arg.Is<string>(command => command.Contains("/bindings:\"http/*:40187:k-host.example.com\"", StringComparison.Ordinal)),
			true);
		_httpsConfigurator.DidNotReceive().Configure(Arg.Any<string>());
		_certificateBindingService.DidNotReceive().Attach(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Creates one .NET 8 HTTPS binding and attaches the selected certificate without changing application XML.")]
	public async Task Handle_ShouldAttachCertificateWithoutConfigTransform_WhenNet8UsesHttps() {
		// Arrange
		const string thumbprint = "AABBCCDDEEFF00112233445566778899AABBCCDD";
		CreateIISSiteRequest request = CreateRequest("https", isNetFramework: false, certificateThumbprint: thumbprint);

		// Act
		await _handler.Handle(request);

		// Assert
		_processExecutor.Received(1).Execute(
			Arg.Any<string>(),
			Arg.Is<string>(command => command.Contains("/bindings:\"https/*:40187:k-host.example.com\"", StringComparison.Ordinal)),
			true);
		_httpsConfigurator.DidNotReceive().Configure(Arg.Any<string>());
		_certificateBindingService.Received(1).Attach("handler-site", thumbprint);
	}

	[Test]
	[Description("Transforms .NET Framework configuration before creating its one HTTPS binding and attaching the selected certificate.")]
	public async Task Handle_ShouldConfigureNetFrameworkAndAttachCertificate_WhenNetFrameworkUsesHttps() {
		// Arrange
		const string thumbprint = "AABBCCDDEEFF00112233445566778899AABBCCDD";
		CreateIISSiteRequest request = CreateRequest("https", isNetFramework: true, certificateThumbprint: thumbprint);

		// Act
		await _handler.Handle(request);

		// Assert
		Received.InOrder(() => {
			_httpsConfigurator.Configure(Path.Combine(_deploymentRoot, "handler-site"));
			_processExecutor.Execute(
				Arg.Any<string>(),
				Arg.Is<string>(command => command.Contains("/bindings:\"https/*:40187:k-host.example.com\"", StringComparison.Ordinal)),
				true);
			_certificateBindingService.Attach("handler-site", thumbprint);
		});
	}

	private CreateIISSiteRequest CreateRequest(string protocol, bool isNetFramework, string certificateThumbprint) {
		string applicationPath = Path.Combine(_deploymentRoot, "handler-site");
		Directory.CreateDirectory(applicationPath);
		return new CreateIISSiteRequest {
			Arguments = new Dictionary<string, string> {
				["siteName"] = "handler-site",
				["port"] = "40187",
				["sourceDirectory"] = applicationPath,
				["destinationDirectory"] = _deploymentRoot,
				["isNetFramework"] = isNetFramework.ToString(),
				["protocol"] = protocol,
				["hostName"] = "k-host.example.com",
				["certificateThumbprint"] = certificateThumbprint
			}
		};
	}
}
