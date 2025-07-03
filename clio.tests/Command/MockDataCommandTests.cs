using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Clio.Command;
using Clio.Common;
using Clio.Tests.Extensions;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
internal class MockDataCommandTests : BaseCommandTests<MockDataCommandOptions>
{

	#region Methods: Protected

	protected override MockFileSystem CreateFs(){
               MockFileSystem mockFS = base.CreateFs();
               string root = Path.Combine(Path.GetTempPath(), "MockDataProjects");
               mockFS.MockExamplesFolder("MockDataProjects", root);
               return mockFS;
	}

	#endregion

	[Test]
	public void CreateDataFiles(){
		FileSystem clioFileSystem = new FileSystem(FileSystem);
		// Arrange
		MockDataCommand command = new MockDataCommand(null, null, clioFileSystem);
               string root = Path.Combine(Path.GetTempPath(), "MockDataProjects");
               MockDataCommandOptions options = new MockDataCommandOptions {
                       Models = root,
                       Data = Path.Combine(root, "Tests", "MockData")
               };
		command.Execute(options);
		FileSystem.Directory.Exists(options.Models).Should().BeTrue();
		FileSystem.Directory.Exists(options.Data).Should().BeTrue();
	}

	[Test]
	public void FindModels(){
		// Arrange
		FileSystem clioFileSystem = new FileSystem(FileSystem);
		MockDataCommand command = new MockDataCommand(null, null, clioFileSystem);
               string root = Path.Combine(Path.GetTempPath(), "MockDataProjects");
               MockDataCommandOptions options = new MockDataCommandOptions {
                       Models = root.Replace(Path.DirectorySeparatorChar, '/'),
                       Data = Path.Combine(root, "Tests", "MockData").Replace(Path.DirectorySeparatorChar, '/')
               };
		List<string> models = command.FindModels(options.Models);
		models.Count.Should().Be(3);
		models.Should().Contain("Contact");
		models.Should().Contain("Account");
	}

	[Test]
	public void GetODataData(){
		// Arrange
		FileSystem clioFileSystem = new FileSystem(FileSystem);
		IApplicationClient mockCreatioClient = Substitute.For<IApplicationClient>();
               string root = Path.Combine(Path.GetTempPath(), "MockDataProjects");
               string contactExpectedContent
                       = clioFileSystem.ReadAllText(Path.Combine(root, "Expected", "Contact.json"));
               string orderExpectedContent
                       = clioFileSystem.ReadAllText(Path.Combine(root, "Expected", "Order.json"));
               string accountExpectedContent
                       = clioFileSystem.ReadAllText(Path.Combine(root, "Expected", "Account.json"));
		mockCreatioClient
			.ExecuteGetRequest(Arg.Is<string>(s => s.EndsWith("Contact")), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>()).Returns(contactExpectedContent);
		mockCreatioClient
			.ExecuteGetRequest(Arg.Is<string>(s => s.EndsWith("Order")), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(orderExpectedContent);
		mockCreatioClient
			.ExecuteGetRequest(Arg.Is<string>(s => s.EndsWith("Account")), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>()).Returns(accountExpectedContent);
		MockDataCommand command = new MockDataCommand(mockCreatioClient, new EnvironmentSettings(), clioFileSystem);
               MockDataCommandOptions options = new MockDataCommandOptions {
                       Models = root,
                       Data = Path.Combine(root, "Tests", "MockData")
               };
		//Act
		command.Execute(options);

		//Assert
		List<string> models = command.FindModels(options.Models);
		string[] dataFiles = FileSystem.Directory.GetFiles(options.Data, "*.json", SearchOption.AllDirectories);
		dataFiles.Count().Should().Be(models.Count);
		foreach (string dataFile in dataFiles) {
			string model = Path.GetFileNameWithoutExtension(dataFile);
                       string expectedContent
                               = clioFileSystem.ReadAllText(Path.Combine(root, "Expected", $"{model}.json"));
			string actualContent = clioFileSystem.ReadAllText(dataFile);
			actualContent.Should().Be(expectedContent);
		}
	}

}