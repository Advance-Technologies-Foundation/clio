using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Autofac;
using Clio.Command;
using Clio.Common;
using Clio.Requests;
using Clio.Tests.Extensions;
using Clio.UserEnvironment;
using FluentAssertions;
using MediatR;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
[Category("UnitTests")]
[TestFixture]
internal class CustomizeDataProtectionCommandTests : BaseCommandTests<CustomizeDataProtectionCommandOptions> {

	#region Fields: Private

	private CustomizeDataProtectionCommand _sut;
	private readonly ILogger _logger = Substitute.For<ILogger>();
	private readonly IMediator _mediator = Substitute.For<IMediator>();
	private readonly ISettingsRepository _settingsRepository = Substitute.For<ISettingsRepository>();

	#endregion

	#region Methods: Private

	private List<IISScannerHandler.RegisteredSite> MockRegisteredSites(Dictionary<string, string> envs, bool mockDir){
		List<IISScannerHandler.RegisteredSite> sites = [];
		foreach (KeyValuePair<string, string> keyValuePair in envs) {
			string sitePath = Path.Join("T:", keyValuePair.Key);

			if (mockDir) {
				FileSystem.Directory.CreateDirectory(sitePath);
			}
			IISScannerHandler.SiteBinding binding = new(keyValuePair.Key, "", "", sitePath);
			List<Uri> uris = [new(keyValuePair.Value)];
			IISScannerHandler.RegisteredSite site = new(binding, uris, IISScannerHandler.SiteType.Core);
			sites.Add(site);
		}
		return sites;
	}

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder){
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.RegisterInstance(_logger);
		containerBuilder.RegisterInstance(_mediator);
		containerBuilder.RegisterInstance(_settingsRepository);
	}

	#endregion

	[Test]
	public void Execute_Returns_EnvNotFound_WhenEnvDoesNotExist(){
		//Arrange
		const string envName = "test";
		_sut = Container.Resolve<CustomizeDataProtectionCommand>();
		CustomizeDataProtectionCommandOptions options = new() {
			EnableDataProtection = true,
			Environment = envName
		};
		_settingsRepository.FindEnvironment(envName)
							.Returns((EnvironmentSettings)null);

		//Act
		int result = _sut.Execute(options);

		//Assert
		result.Should().Be(1);
		_logger.Received(1).WriteError($"Environment: {options.Environment} not found.");
		_logger.ClearReceivedCalls();
	}

	[Test]
	public void Execute_Returns_EnvNotFound_WhenEnvDoesNotHaveUri(){
		//Arrange
		const string envName = "test";
		_sut = Container.Resolve<CustomizeDataProtectionCommand>();
		CustomizeDataProtectionCommandOptions options = new() {
			EnableDataProtection = true,
			Environment = envName
		};

		EnvironmentSettings envSettings = new() {
			Uri = null
		};

		_settingsRepository.FindEnvironment(envName).Returns(envSettings);

		//Act
		int result = _sut.Execute(options);

		//Assert
		result.Should().Be(1);
		_logger.Received(1).WriteError($"Environment: {options.Environment} has an empty Uri.");
		_logger.ClearReceivedCalls();
	}

	[Test]
	public void Execute_Returns_EnvNotFound_WhenEnvUriNotValid(){
		//Arrange
		const string envName = "test";
		_sut = Container.Resolve<CustomizeDataProtectionCommand>();
		CustomizeDataProtectionCommandOptions options = new() {
			EnableDataProtection = true,
			Environment = envName
		};

		EnvironmentSettings envSettings = new() {
			Uri = "some_invalid_uri"
		};

		_settingsRepository.FindEnvironment(envName)
							.Returns(envSettings);

		//Act
		int result = _sut.Execute(options);

		//Assert
		result.Should().Be(1);
		_logger.Received(1).WriteError($"Environment: {options.Environment} has an invalid Uri.");
		_logger.ClearReceivedCalls();
	}

	[Test]
	public void Execute_Returns_Error_DirectoryNotFound(){
		//Arrange
		const string envName = "test";
		_sut = Container.Resolve<CustomizeDataProtectionCommand>();
		CustomizeDataProtectionCommandOptions options = new() {
			EnableDataProtection = true,
			Environment = envName
		};

		const string envUri = "http://localhost:40010";
		EnvironmentSettings envSettings = new() {
			Uri = envUri,
			IsNetCore = true
		};

		_settingsRepository.FindEnvironment(envName)
							.Returns(envSettings);

		Dictionary<string, string> envs = new() {
			{envName, envUri}
		};

		List<IISScannerHandler.RegisteredSite> sites = MockRegisteredSites(envs, false);
		_mediator.Send(Arg.Any<AllRegisteredSitesRequest>())
				.Returns(Task.CompletedTask)
				.AndDoes(callInfo => { (callInfo[0] as AllRegisteredSitesRequest).Callback?.Invoke(sites); });

		//Act
		int result = _sut.Execute(options);

		//Assert
		result.Should().Be(1);
               string envDir = Path.Combine(Path.GetTempPath(), envName);
               _logger.Received(1).WriteError($"Environment: {envUri}/, Directory {envDir} does not exist.");
		_logger.ClearReceivedCalls();
	}

	[Test]
	public void Execute_Returns_Error_RegisteredSiteNotFound(){
		//Arrange
		const string envName = "test";
		_sut = Container.Resolve<CustomizeDataProtectionCommand>();
		CustomizeDataProtectionCommandOptions options = new() {
			EnableDataProtection = true,
			Environment = envName
		};

		const string envUri = "http://localhost:40010";
		EnvironmentSettings envSettings = new() {
			Uri = envUri,
			IsNetCore = true
		};

		_settingsRepository.FindEnvironment(envName)
							.Returns(envSettings);

		Dictionary<string, string> envs = new();

		List<IISScannerHandler.RegisteredSite> sites = MockRegisteredSites(envs, true);
		_mediator.Send(Arg.Any<AllRegisteredSitesRequest>())
				.Returns(Task.CompletedTask)
				.AndDoes(callInfo => { (callInfo[0] as AllRegisteredSitesRequest).Callback?.Invoke(sites); });

		//Act
		int result = _sut.Execute(options);

		//Assert
		result.Should().Be(1);
		_logger.Received(1).WriteError("Did not find any registered sites");
		_logger.ClearReceivedCalls();
	}

	[Test]
	public void Execute_Returns_Error_WhenAppSettingsNotFound(){
		//Arrange
		const string envName = "test";
		_sut = Container.Resolve<CustomizeDataProtectionCommand>();
		CustomizeDataProtectionCommandOptions options = new() {
			EnableDataProtection = true,
			Environment = envName
		};

		const string envUri = "http://localhost:40010";
		EnvironmentSettings envSettings = new() {
			Uri = envUri,
			IsNetCore = true
		};

		_settingsRepository.FindEnvironment(envName)
							.Returns(envSettings);

		Dictionary<string, string> envs = new() {
			{envName, envUri}
		};

		List<IISScannerHandler.RegisteredSite> sites = MockRegisteredSites(envs, true);
		_mediator.Send(Arg.Any<AllRegisteredSitesRequest>())
				.Returns(Task.CompletedTask)
				.AndDoes(callInfo => { (callInfo[0] as AllRegisteredSitesRequest).Callback?.Invoke(sites); });

		//Act
		int result = _sut.Execute(options);

		//Assert
		result.Should().Be(1);
               string envDir = Path.Combine(Path.GetTempPath(), envName);
               _logger.Received(1).WriteError($"Did not find appsettings.json in {envDir}");
		_logger.ClearReceivedCalls();
	}

	[Test]
	public void Execute_Returns_ReadsFile(){
		//Arrange
		const string envName = "test";
		_sut = Container.Resolve<CustomizeDataProtectionCommand>();
		CustomizeDataProtectionCommandOptions options = new() {
			EnableDataProtection = true,
			Environment = envName
		};

		const string envUri = "http://localhost:40010";
		EnvironmentSettings envSettings = new() {
			Uri = envUri,
			IsNetCore = true
		};

		_settingsRepository.FindEnvironment(envName)
							.Returns(envSettings);

		Dictionary<string, string> envs = new() {
			{envName, envUri}
		};

		List<IISScannerHandler.RegisteredSite> sites = MockRegisteredSites(envs, true);
		_mediator.Send(Arg.Any<AllRegisteredSitesRequest>())
				.Returns(Task.CompletedTask)
				.AndDoes(callInfo => { (callInfo[0] as AllRegisteredSitesRequest).Callback?.Invoke(sites); });

               string envDir = Path.Combine(Path.GetTempPath(), envName);
               FileSystem.MockExamplesFolder("Sites/N8_Site", envDir);

		//Act
		int result = _sut.Execute(options);

		//Assert
		result.Should().Be(0);
		_logger.Received(1).WriteInfo("DONE");
		_logger.ClearReceivedCalls();

               string envDir = Path.Combine(Path.GetTempPath(), envName);
               FileSystem.File.Exists(Path.Combine(envDir, "appsettings.json")).Should().BeTrue();
               string appSettingsContent = FileSystem.File.ReadAllText(Path.Combine(envDir, "appsettings.json"));

		JsonDocument doc = JsonDocument.Parse(appSettingsContent);
		JsonElement prop = doc.RootElement.GetProperty("DataProtection").GetProperty("CustomizeDataProtection");
		bool value = prop.GetBoolean();
		value.Should().BeTrue();
	}

}
