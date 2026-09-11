using System;
using System.Collections.Generic;
using Clio.Command.McpServer.Knowledge;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class InstalledKnowledgeVersionsTests {
	[Test]
	[Description("Installed versions come from local markers, include disabled sources, and omit absent markers.")]
	public void Read_ShouldReportRecordedVersions_WhenSourcesHaveMixedInstallationState() {
		// Arrange
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		IKnowledgeSourceInstallationStore store = Substitute.For<IKnowledgeSourceInstallationStore>();
		settings.GetKnowledgeConfiguration().Returns(new KnowledgeConfiguration {
			Sources = new Dictionary<string, KnowledgeSourceConfiguration> {
				["curated"] = new() { Enabled = true, LibraryId = "com.example.curated" },
				["disabled"] = new() { Enabled = false, LibraryId = "com.example.disabled" },
				["absent"] = new() { Enabled = true },
				["git"] = new() { Type = KnowledgeSourceType.Git, LibraryId = "com.example.old" },
				["replaced"] = new() { LibraryId = "com.example.replacement" }
			}
		});
		store.TryReadStartupState("curated").Returns(new KnowledgeSourceStartupState(
			new KnowledgeSourceGenerationPointer("com.example.curated", "1.14.10", 10,
				"generations/10", new string('a', 64), "1.14.10", DateTimeOffset.UnixEpoch), null));
		store.TryReadStartupState("disabled").Returns(new KnowledgeSourceStartupState(
			new KnowledgeSourceGenerationPointer("com.example.disabled", "2.0.0", 20,
				"generations/20", new string('b', 64), "2.0.0", DateTimeOffset.UnixEpoch), null));
		KnowledgeSourceStartupState stale = new(new KnowledgeSourceGenerationPointer(
			"com.example.old", "0.1.0", 1, "generations/1", new string('c', 64), "0.1.0", DateTimeOffset.UnixEpoch), null);
		store.TryReadStartupState("git").Returns(stale);
		store.TryReadStartupState("replaced").Returns(stale);
		ServiceCollection services = new();
		services.AddSingleton(settings);
		services.AddSingleton(store);
		services.AddSingleton<IInstalledKnowledgeVersions, InstalledKnowledgeVersions>();
		using ServiceProvider provider = services.BuildServiceProvider();

		// Act
		IReadOnlyDictionary<string, string> versions = provider.GetRequiredService<IInstalledKnowledgeVersions>().Read();

		// Assert
		versions.Should().BeEquivalentTo(new Dictionary<string, string> {
			["curated"] = "1.14.10", ["disabled"] = "2.0.0"
		}, because: "disabled installs stay visible, but absent records, Git leftovers and another library's marker are not current bundle versions");
	}
}
