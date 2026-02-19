using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
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
[TestFixture]
internal class CustomizeDataProtectionCommandTests : BaseCommandTests<CustomizeDataProtectionCommandOptions> {

	#region Fields: Private

	private CustomizeDataProtectionCommand _sut;
	private readonly ILogger _logger = Substitute.For<ILogger>();
	private readonly IMediator _mediator = Substitute.For<IMediator>();
	private readonly ISettingsRepository _settingsRepository = Substitute.For<ISettingsRepository>();

	#endregion

	#region Methods: Private

	private static string GetPlatformPath(string disk, string folder) {
		if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) {
			return $"{disk}:\\{folder}";
		} else {
			return $"/{disk}/{folder}";
		}
	}

	private List<IISScannerHandler.RegisteredSite> MockRegisteredSites(Dictionary<string, string> envs, bool mockDir){
		List<IISScannerHandler.RegisteredSite> sites = [];
		foreach (KeyValuePair<string, string> keyValuePair in envs) {
			string sitePath = GetPlatformPath("T", keyValuePair.Key);

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

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder){
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(_logger);
		containerBuilder.AddSingleton(_mediator);
		containerBuilder.AddSingleton(_settingsRepository);
	}

	#endregion

	[Test]
	[Description("Ensures that the command returns EnvNotFound when the specified environment does not exist.")]
	public void Execute_Returns_EnvNotFound_WhenEnvDoesNotExist(){
		//Arrange
		const string envName = "test";
		_sut = Container.GetRequiredService<CustomizeDataProtectionCommand>();
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
	[Description("Ensures that the command returns EnvNotFound when the specified environment does not have a URI.")]
	public void Execute_Returns_EnvNotFound_WhenEnvDoesNotHaveUri(){
		//Arrange
		const string envName = "test";
		_sut = Container.GetRequiredService<CustomizeDataProtectionCommand>();
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
		result.Should().Be(1, "error code when the environment does not have a URI");
		_logger.Received(1).WriteError($"Environment: {options.Environment} has an empty Uri.");
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("Ensures that the command returns EnvNotFound when the specified environment has an invalid URI.")]
	public void Execute_Returns_EnvNotFound_WhenEnvUriNotValid(){
		//Arrange
		const string envName = "test";
		_sut = Container.GetRequiredService<CustomizeDataProtectionCommand>();
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
		result.Should().Be(1, "error code when the environment has an invalid URI");
		_logger.Received(1).WriteError($"Environment: {options.Environment} has an invalid Uri.");
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("Ensures that the command returns an error when the directory for the specified environment is not found.")]
	public void Execute_Returns_Error_DirectoryNotFound(){
		//Arrange
		const string envName = "test";
		_sut = Container.GetRequiredService<CustomizeDataProtectionCommand>();
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
		result.Should().Be(1, "the directory for the specified environment does not exist");
		_logger.Received(1).WriteError($"Environment: {envUri}/, Directory {GetPlatformPath("T", envName)} does not exist.");
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("Ensures that the command returns an error when no registered site is found for the specified environment.")]
	public void Execute_Returns_Error_RegisteredSiteNotFound(){
		//Arrange
		const string envName = "test";
		_sut = Container.GetRequiredService<CustomizeDataProtectionCommand>();
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
		result.Should().Be(1, "the command should return an error when no registered sites are found for the specified environment");
		_logger.Received(1).WriteError("Did not find any registered sites");
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("Ensures that the command returns an error when the appsettings.json file is not found.")]
	public void Execute_Returns_Error_WhenAppSettingsNotFound(){
		//Arrange
		const string envName = "test";
		_sut = Container.GetRequiredService<CustomizeDataProtectionCommand>();
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
		result.Should().Be(1, "the command should return an error when the appsettings.json file is not found");
		_logger.Received(1).WriteError($"Did not find appsettings.json in {GetPlatformPath("T", envName)}");
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("Ensures that the command reads the appsettings.json file and applies the data protection settings.")]
	public void Execute_Returns_ReadsFile(){
		//Arrange
		const string envName = "test";
		_sut = Container.GetRequiredService<CustomizeDataProtectionCommand>();
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

		FileSystem.MockExamplesFolder("Sites/N8_Site", GetPlatformPath("T", envName));

		//Act
		int result = _sut.Execute(options);

		//Assert
		result.Should().Be(0, "the command should successfully execute when the appsettings.json file is found and modified");
		_logger.Received(1).WriteInfo("DONE");
		_logger.ClearReceivedCalls();

		FileSystem.File.Exists(Path.Combine(GetPlatformPath("T", envName), "appsettings.json")).Should().BeTrue();
		string appSettingsContent = FileSystem.File.ReadAllText(Path.Combine(GetPlatformPath("T", envName), "appsettings.json"));

		JsonDocument doc = JsonDocument.Parse(appSettingsContent);
		JsonElement prop = doc.RootElement.GetProperty("DataProtection").GetProperty("CustomizeDataProtection");
		bool value = prop.GetBoolean();
		value.Should().BeTrue();
	}

}
