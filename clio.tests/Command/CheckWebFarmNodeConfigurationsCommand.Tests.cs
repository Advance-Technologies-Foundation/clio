using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Command;
using Clio.Common;
using Clio.Tests.Extensions;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command
{
	[TestFixture]
	public class CheckWebFarmNodeConfigurationsCommandTestCase: BaseClioModuleTests
	{


		#region Methods: Protected

		protected override MockFileSystem CreateFs() {
			MockFileSystem mockFS = base.CreateFs();
			mockFS.MockExamplesFolder("WebFarm/Node1-Main", @"T:\Node1-Main");
			mockFS.MockExamplesFolder("WebFarm/Node3-Correct", @"T:\Node3-Correct");
			mockFS.MockExamplesFolder("WebFarm/Node3-Correct", @"T:\Node4-Correct");
			mockFS.MockExamplesFolder("WebFarm/Node2-Incorrect", @"T:\Node2-Incorrect");
			return mockFS;
		}

		#endregion

		[Test, Category("Unit")]
		public void Execute_CheckTwoNodes_IfNotChanges() {
			ILogger logger = Substitute.For<ILogger>();
			FileSystem clioFileSystem = new FileSystem(FileSystem);
			CheckWebFarmNodeConfigurationsCommand command = 
				new CheckWebFarmNodeConfigurationsCommand(logger, clioFileSystem);
			CheckWebFarmNodeConfigurationsOptions options = 
				new CheckWebFarmNodeConfigurationsOptions 
				{
					Paths = "T:\\Node1-Main,T:\\Node3-Correct,T:\\Node4-Correct" };
			int result = command.Execute(options);
			logger.Received(1).WriteLine("Check started:");
			result.Should().Be(0);
		}

		[Test, Category("Unit")]
		public void Execute_CheckTwoNodes_IfNodesChanged() {
			ILogger logger = Substitute.For<ILogger>();
			FileSystem clioFileSystem = new FileSystem(FileSystem);
			CheckWebFarmNodeConfigurationsCommand command =
				new CheckWebFarmNodeConfigurationsCommand(logger, clioFileSystem);
			CheckWebFarmNodeConfigurationsOptions options =
				new CheckWebFarmNodeConfigurationsOptions {
					Paths = "T:\\Node1-Main,T:\\Node2-Incorrect"
				};
			int result = command.Execute(options);
			logger.Received(1).WriteLine("Check started:");
			result.Should().Be(1);
		}

		[Test, Category("Unit")]
		public void CompareDirectories_CorrectReturn_IfNotChanges() {
			ILogger logger = Substitute.For<ILogger>();
			FileSystem clioFileSystem = new FileSystem(FileSystem);
			var directoryComparer = new DirectoryComparer(clioFileSystem);
			directoryComparer.CompareDirectories("T:\\Node1-Main", "T:\\Node3-Correct").Should().BeEmpty();
		}

		[Test, Category("Unit")]
		public void CompareDirectories_CorrectReturn_IfNodesChanged() {
			ILogger logger = Substitute.For<ILogger>();
			FileSystem clioFileSystem = new FileSystem(FileSystem);
			var directoryComparer = new DirectoryComparer(clioFileSystem);
			directoryComparer.CompareDirectories("T:\\Node1-Main", "T:\\Node2-Incorrect").Should().HaveCount(5);
		}

	}
}
