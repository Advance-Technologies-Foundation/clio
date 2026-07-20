using System;
using System.IO;
using System.IO.Abstractions;
using Clio.Command.McpServer.Knowledge;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeRootPathProviderTests {
	[Test]
	[Platform("Win")]
	[Description("Rejects a Windows network path so cached Git guidance cannot be replaced by a remote filesystem peer.")]
	public void GetOrCreateRoot_ShouldRejectNetworkPath_OnWindows() {
		// Arrange
		const string configuredRoot = @"\\knowledge-server\clio-cache\knowledge";
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetKnowledgeRootPath().Returns(configuredRoot);
		IFileSystem fileSystem = new FileSystem();
		KnowledgeRootPathProvider provider = new(settingsRepository, fileSystem);

		// Act
		Action act = () => provider.GetOrCreateRoot();

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*local non-device filesystem*",
				because: "the offline Git trust cache must remain on a filesystem controlled by this machine");
	}

	[Test]
	[Description("Rejects an existing symbolic-link or junction ancestor before creating the configured knowledge root.")]
	public void GetOrCreateRoot_ShouldNotCreateDirectory_WhenExistingAncestorIsReparsePoint() {
		// Arrange
		string ancestor = Path.Combine(Path.GetTempPath(), $"knowledge-link-{Guid.NewGuid():N}");
		string configuredRoot = Path.Combine(ancestor, "knowledge");
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetKnowledgeRootPath().Returns(configuredRoot);
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.Path.Returns(new FileSystem().Path);
		fileSystem.Directory.Exists(ancestor).Returns(true);
		fileSystem.File.GetAttributes(ancestor).Returns(FileAttributes.Directory | FileAttributes.ReparsePoint);
		KnowledgeRootPathProvider provider = new(settingsRepository, fileSystem);

		// Act
		Action act = () => provider.GetOrCreateRoot();

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "knowledge storage must reject a redirected ancestor before it can create anything in the link target");
		fileSystem.Directory.DidNotReceive().CreateDirectory(configuredRoot);
	}
}
