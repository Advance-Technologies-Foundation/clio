using System.IO.Abstractions.TestingHelpers;
using Clio.Command;
using Clio.Common;
using Clio.Tests.Extensions;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class CheckWebFarmNodeConfigurationsCommandTestCase : BaseClioModuleTests
{


	#region Methods: Protected

	protected override MockFileSystem CreateFs() {
		MockFileSystem mockFS = base.CreateFs();
		mockFS.MockExamplesFolder("WebFarm/Node1-Main", GetPlatformPath("T", "Node1-Main"));
		mockFS.MockExamplesFolder("WebFarm/Node3-Correct", GetPlatformPath("T", "Node3-Correct"));
		mockFS.MockExamplesFolder("WebFarm/Node3-Correct", GetPlatformPath("T", "Node4-Correct"));
		mockFS.MockExamplesFolder("WebFarm/Node2-Incorrect", GetPlatformPath("T", "Node2-Incorrect"));
		return mockFS;
	}

	#endregion

	[Test, Category("Unit")]
	public void Execute_CheckTwoNodes_IfNotChanges() {
		ILogger logger = Substitute.For<ILogger>();
		FileSystem clioFileSystem = new FileSystem(FileSystem);
		DirectoryComparer directoryComparer = new DirectoryComparer(clioFileSystem, logger);
		CheckWebFarmNodeConfigurationsCommand command =
			new CheckWebFarmNodeConfigurationsCommand(logger, clioFileSystem, directoryComparer);
		CheckWebFarmNodeConfigurationsOptions options =
			new CheckWebFarmNodeConfigurationsOptions {
				Paths = string.Join(",",
					GetPlatformPath("T", "Node1-Main"),
					GetPlatformPath("T", "Node3-Correct"),
					GetPlatformPath("T", "Node4-Correct")
				)
			};
		int result = command.Execute(options);
		result.Should().Be(0);
	}

	[Test, Category("Unit")]
	public void Execute_CheckTwoNodes_IfNodesChanged() {
		ILogger logger = Substitute.For<ILogger>();
		FileSystem clioFileSystem = new FileSystem(FileSystem);
		DirectoryComparer directoryComparer = new DirectoryComparer(clioFileSystem, logger);
		CheckWebFarmNodeConfigurationsCommand command =
			new CheckWebFarmNodeConfigurationsCommand(logger, clioFileSystem, directoryComparer);
		CheckWebFarmNodeConfigurationsOptions options =
			new CheckWebFarmNodeConfigurationsOptions {
				Paths = string.Join(",",
					GetPlatformPath("T", "Node1-Main"),
					GetPlatformPath("T", "Node2-Incorrect")
				)
			};
		int result = command.Execute(options);
		result.Should().Be(1);
	}

	[Test, Category("Unit")]
	public void CompareDirectories_CorrectReturn_IfNotChanges() {
		ILogger logger = Substitute.For<ILogger>();
		FileSystem clioFileSystem = new FileSystem(FileSystem);
		var directoryComparer = new DirectoryComparer(clioFileSystem, logger);
		string node1Main = GetPlatformPath("T", "Node1-Main");
		string node3Correct = GetPlatformPath("T", "Node3-Correct");
		directoryComparer.CompareDirectories(node1Main, node3Correct).Should().BeEmpty();
	}

	[Test, Category("Unit")]
	public void CompareDirectories_CorrectReturn_IfNodesChanged() {
		ILogger logger = Substitute.For<ILogger>();
		FileSystem clioFileSystem = new FileSystem(FileSystem);
		var directoryComparer = new DirectoryComparer(clioFileSystem, logger);
		string node1Main = GetPlatformPath("T", "Node1-Main");
		string node2Incorrect = GetPlatformPath("T", "Node2-Incorrect");
		directoryComparer.CompareDirectories(node1Main, node2Incorrect).Should().HaveCount(5);
	}

	private static string GetPlatformPath(string disk, string folder) {
		if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) {
			return $"{disk}:\\{folder}";
		} else {
			return $"/{disk}/{folder}";
		}
	}

}