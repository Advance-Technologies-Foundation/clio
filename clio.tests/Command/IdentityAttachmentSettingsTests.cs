using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Tests.Infrastructure;
using Clio.Common;
using Clio.Common.IIS;
using Clio.Command.IdentityServiceDeployment;
using Clio.Requests;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NSubstitute;

namespace Clio.Tests.Command;

[TestFixture, Category("Unit"), Property("Module", "Command"), NonParallelizable]
public sealed class IdentityAttachmentSettingsTests {
	private System.IO.Abstractions.IFileSystem _original;
	private MockFileSystem _files;
	private ServiceProvider _container;
	private ISettingsRepository _settings;
	private string _crmPath;
	private IdentityServiceAttachment _attachment;

	[SetUp]
	public void SetUp() {
		_original = SettingsRepository.FileSystem;
		_files = TestFileSystem.MockFileSystem();
		_crmPath = Path.Combine(Path.GetTempPath(), "identity-settings", "crm");
		_attachment = new IdentityServiceAttachment {
			EnvironmentPath = Path.Combine(Path.GetTempPath(), "identity-settings", "identity"),
			IisTarget = "custom-name", ApplicationPool = "custom-pool", Uri = "http://localhost:40201"
		};
		_files.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(JsonConvert.SerializeObject(new Settings {
			ActiveEnvironmentKey = "crm", Environments = new Dictionary<string, EnvironmentSettings> {
				["crm"] = new() { EnvironmentPath = _crmPath, IdentityService = _attachment,
					AuthAppUri = _attachment.Uri + "/connect/token", ClientId = "id", ClientSecret = "secret", Login = "retained-login" }
			}
		})));
		ServiceCollection services = new();
		services.AddSingleton<System.IO.Abstractions.IFileSystem>(_files);
		services.AddSingleton<ISettingsRepository, SettingsRepository>();
		_container = services.BuildServiceProvider();
		_settings = _container.GetRequiredService<ISettingsRepository>();
	}

	[TearDown]
	public void TearDown() { _container.Dispose(); SettingsRepository.FileSystem = _original; }

	[TestCase("{}"), TestCase("{\"IdentityService\":null}"), TestCase("{\"IdentityService\":{}}")]
	[Description("Old and explicitly empty environment records expose visible empty identity fields.")]
	public void Serialize_ShouldExposeEmptyFields_WhenNoIdentityExists(string json) {
		// Arrange
		EnvironmentSettings environment = JsonConvert.DeserializeObject<EnvironmentSettings>(json);
		// Act
		JObject result = JObject.Parse(JsonConvert.SerializeObject(environment,
			new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
		// Assert
		result["IdentityService"]!["EnvironmentPath"]!.Value<string>().Should().BeEmpty(because: "no attachment is represented by an empty path");
		result["IdentityService"]!["IisTarget"]!.Value<string>().Should().BeEmpty(because: "the optional component remains visible without inventing an IIS target");
	}

	[TestCase(null), TestCase(""), TestCase(" ")]
	[Description("Ordinary registration updates preserve attachment; clone-and-rename retains it too.")]
	public void ConfigureEnvironment_ShouldPreserveIdentity_WhenUpdatingOrRenaming(string emptyPath) {
		// Arrange
		EnvironmentSettings update = new() { Uri = "http://localhost:40200",
			IdentityService = new IdentityServiceAttachment { EnvironmentPath = emptyPath } };
		// Act
		_settings.ConfigureEnvironment("crm", update);
		EnvironmentSettings clone = new();
		clone.Merge(_settings.FindCurrentEnvironment("crm"));
		_settings.ConfigureEnvironment("renamed", clone);
		// Assert
		_settings.FindCurrentEnvironment("crm").IdentityService.Should().Be(_attachment, because: "an empty update must not erase deletion authority");
		_settings.FindCurrentEnvironment("renamed").IdentityService.Should().Be(_attachment, because: "environment rename clones through Merge");
	}

	[TestCase(false), TestCase(true)]
	[Description("Compare-and-clear removes only matching authentication references and preserves unrelated credentials.")]
	public void UpdateIdentityAttachment_ShouldClearOnlyMatchingCredentials_WhenRemovalCompletes(bool unrelated) {
		// Arrange
		if (unrelated) {
			_settings.ConfigureEnvironment("crm", new EnvironmentSettings { AuthAppUri = "https://another.example/connect/token", ClientId = "other", ClientSecret = "other-secret" });
		}
		// Act
		bool result = _settings.UpdateIdentityAttachment("crm", _crmPath, _attachment, new IdentityServiceAttachment(), true);
		EnvironmentSettings current = _settings.FindCurrentEnvironment("crm");
		// Assert
		result.Should().BeTrue(because: "the exact attachment still authorizes clearing");
		current.IdentityService.Should().Be(new IdentityServiceAttachment(), because: "successful removal leaves visible empty identity fields");
		current.ClientId.Should().Be(unrelated ? "other" : "", because: "only credentials using the removed identity may be cleared");
		current.Login.Should().Be("retained-login", because: "identity removal preserves unrelated environment properties");
	}

	[Test]
	[Description("A stale cleanup request cannot remove a replaced attachment.")]
	public void UpdateIdentityAttachment_ShouldRefuse_WhenAttachmentChanged() {
		// Arrange
		IdentityServiceAttachment stale = _attachment with { IisTarget = "old" };
		// Act
		bool result = _settings.UpdateIdentityAttachment("crm", _crmPath, stale, new IdentityServiceAttachment(), true);
		// Assert
		result.Should().BeFalse(because: "the cleanup authority no longer matches");
		_settings.FindCurrentEnvironment("crm").IdentityService.Should().Be(_attachment, because: "a replacement belongs to a different operation");
	}

	[Test]
	[Description("A persisted cleanup checkpoint lets a partial uninstall retry without authenticating through removed identity.")]
	public void Uninstall_ShouldResumeFromPersistedCheckpoint_WhenPoolRemovalFails() {
		// Arrange
		IIisScanner iis = Substitute.For<IIisScanner>();
		iis.TryFindAllIisTargets(out Arg.Any<IReadOnlyList<UnregisteredSite>>()).Returns(call => {
			call[0] = Array.Empty<UnregisteredSite>(); return true;
		});
		iis.TryFindAllVirtualDirectories(out Arg.Any<IReadOnlyList<IisVirtualDirectory>>()).Returns(call => {
			call[0] = Array.Empty<IisVirtualDirectory>(); return true;
		});
		iis.DeleteAppPoolIfUnused(_attachment.ApplicationPool).Returns(IisAppPoolMutationResult.Failed);
		_files.AddDirectory(_attachment.EnvironmentPath);
		IIdentityReferenceCleanup references = Substitute.For<IIdentityReferenceCleanup>();
		ServiceCollection services = new();
		services.AddSingleton(_settings).AddSingleton(iis).AddSingleton(references)
			.AddSingleton<System.IO.Abstractions.IFileSystem>(_files).AddSingleton(Substitute.For<ILogger>())
			.AddSingleton(Substitute.For<IDeploymentTargetReservation>())
			.AddTransient<IIdentityServiceLifecycle, IdentityServiceLifecycle>().AddTransient<UninstallIdentityCommand>();
		using ServiceProvider container = services.BuildServiceProvider();
		UninstallIdentityCommand command = container.GetRequiredService<UninstallIdentityCommand>();
		UninstallIdentityOptions options = new() { Environment = "crm" };
		// Act
		int first = command.Execute(options);
		EnvironmentSettings interrupted = _settings.FindCurrentEnvironment("crm");
		iis.DeleteAppPoolIfUnused(_attachment.ApplicationPool).Returns(IisAppPoolMutationResult.Completed);
		int retry = command.Execute(options);
		EnvironmentSettings final = _settings.FindCurrentEnvironment("crm");
		// Assert
		first.Should().Be(1, because: "a failed pool removal must not report completion");
		interrupted.IdentityService.CrmReferencesCleared.Should().BeTrue(because: "CRM cleanup completed before IIS removal failed");
		retry.Should().Be(0, because: "the saved checkpoint allows cleanup of the remaining artifacts");
		references.ReceivedCalls().Should().ContainSingle(because: "retry must not authenticate through an identity that may already be gone");
		final.IdentityService.IsEmpty.Should().BeTrue(because: "all recorded artifacts are now removed");
		final.ClientSecret.Should().BeEmpty(because: "only matching credentials clear after successful artifact cleanup");
	}
}
