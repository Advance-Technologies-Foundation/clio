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
               string root = Path.Combine(Path.GetTempPath(), "webfarm");
               mockFS.MockExamplesFolder("WebFarm/Node1-Main", Path.Combine(root, "Node1-Main"));
               mockFS.MockExamplesFolder("WebFarm/Node3-Correct", Path.Combine(root, "Node3-Correct"));
               mockFS.MockExamplesFolder("WebFarm/Node3-Correct", Path.Combine(root, "Node4-Correct"));
               mockFS.MockExamplesFolder("WebFarm/Node2-Incorrect", Path.Combine(root, "Node2-Incorrect"));
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
               string root = Path.Combine(Path.GetTempPath(), "webfarm");
               CheckWebFarmNodeConfigurationsOptions options =
                       new CheckWebFarmNodeConfigurationsOptions {
                               Paths = string.Join(',',
                                       Path.Combine(root, "Node1-Main"),
                                       Path.Combine(root, "Node3-Correct"),
                                       Path.Combine(root, "Node4-Correct"))
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
               string root = Path.Combine(Path.GetTempPath(), "webfarm");
               CheckWebFarmNodeConfigurationsOptions options =
                       new CheckWebFarmNodeConfigurationsOptions {
                               Paths = string.Join(',',
                                       Path.Combine(root, "Node1-Main"),
                                       Path.Combine(root, "Node2-Incorrect"))
                       };
		int result = command.Execute(options);
		result.Should().Be(1);
	}

	[Test, Category("Unit")]
	public void CompareDirectories_CorrectReturn_IfNotChanges() {
		ILogger logger = Substitute.For<ILogger>();
		FileSystem clioFileSystem = new FileSystem(FileSystem);
		var directoryComparer = new DirectoryComparer(clioFileSystem, logger);
               string root = Path.Combine(Path.GetTempPath(), "webfarm");
               directoryComparer.CompareDirectories(Path.Combine(root, "Node1-Main"), Path.Combine(root, "Node3-Correct")).Should().BeEmpty();
	}

	[Test, Category("Unit")]
	public void CompareDirectories_CorrectReturn_IfNodesChanged() {
		ILogger logger = Substitute.For<ILogger>();
		FileSystem clioFileSystem = new FileSystem(FileSystem);
		var directoryComparer = new DirectoryComparer(clioFileSystem, logger);
               string root = Path.Combine(Path.GetTempPath(), "webfarm");
               directoryComparer.CompareDirectories(Path.Combine(root, "Node1-Main"), Path.Combine(root, "Node2-Incorrect")).Should().HaveCount(5);
	}

}