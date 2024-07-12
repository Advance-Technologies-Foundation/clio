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
		mockFS.MockExamplesFolder("MockDataProjects", @"T:\MockDataProjects");
		return mockFS;
	}

	#endregion

	[Test]
	public void CreateDataFiles(){
		FileSystem clioFileSystem = new FileSystem(_fileSystem);
		// Arrange
		MockDataCommand command = new MockDataCommand(null, null, clioFileSystem);
		MockDataCommandOptions options = new MockDataCommandOptions {
			Models = @"T:\MockDataProjects",
			Data = @"T:\MockDataProjects\Tests\MockData"
		};
		command.Execute(options);
		_fileSystem.Directory.Exists(options.Models).Should().BeTrue();
		_fileSystem.Directory.Exists(options.Data).Should().BeTrue();
	}

	[Test]
	public void FindModels(){
		// Arrange
		FileSystem clioFileSystem = new FileSystem(_fileSystem);
		MockDataCommand command = new MockDataCommand(null, null, clioFileSystem);
		MockDataCommandOptions options = new MockDataCommandOptions {
			Models = @"T:/MockDataProjects",
			Data = @"T:/MockDataProjects/Tests/MockData"
		};
		List<string> models = command.FindModels(options.Models);
		models.Count.Should().Be(3);
		models.Should().Contain("Contact");
		models.Should().Contain("Account");
	}

	[Test]
	public void GetODataData(){
		// Arrange
		FileSystem clioFileSystem = new FileSystem(_fileSystem);
		IApplicationClient mockCreatioClient = Substitute.For<IApplicationClient>();
		string contactExpectedContent
			= clioFileSystem.ReadAllText(Path.Combine("T:/MockDataProjects", "Expected", "Contact.json"));
		string orderExpectedContent
			= clioFileSystem.ReadAllText(Path.Combine("T:/MockDataProjects", "Expected", "Order.json"));
		string accountExpectedContent
			= clioFileSystem.ReadAllText(Path.Combine("T:/MockDataProjects", "Expected", "Account.json"));
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
			Models = @"T:\MockDataProjects",
			Data = @"T:\MockDataProjects\Tests\MockData"
		};
		//Act
		command.Execute(options);

		//Assert
		List<string> models = command.FindModels(options.Models);
		string[] dataFiles = _fileSystem.Directory.GetFiles(options.Data, "*.json", SearchOption.AllDirectories);
		dataFiles.Count().Should().Be(models.Count);
		foreach (string dataFile in dataFiles) {
			string model = Path.GetFileNameWithoutExtension(dataFile);
			string expectedContent
				= clioFileSystem.ReadAllText(Path.Combine("T:/MockDataProjects", "Expected", $"{model}.json"));
			string actualContent = clioFileSystem.ReadAllText(dataFile);
			actualContent.Should().Be(expectedContent);
		}
	}

}