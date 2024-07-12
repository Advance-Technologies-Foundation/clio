using Clio.Command;
using Clio.Common;
using Clio.Tests.Extensions;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Clio.Tests.Command
{
	[TestFixture]
	internal class MockDataCommandTests : BaseCommandTests<MockDataCommandOptions>
	{
		protected override MockFileSystem CreateFs() {
			var mockFS = base.CreateFs();
			mockFS.MockExamplesFolder("MockDataProjects", @"T:\MockDataProjects");
			return mockFS;
		}

		[Test]
		public void CreateDataFiles() {
			var clioFileSystem = new FileSystem(_fileSystem);
			// Arrange
			var command = new MockDataCommand(clioFileSystem);
			var options = new MockDataCommandOptions() {
				Models = @"T:\MockDataProjects",
				Data = @"T:\MockDataProjects\Tests\MockData"
			};
			command.Execute(options);
			_fileSystem.Directory.Exists(options.Models).Should().BeTrue();
			_fileSystem.Directory.Exists(options.Data).Should().BeTrue();
		}

		[Test]
		public void FindModels() {
			// Arrange
			var clioFileSystem = new FileSystem(_fileSystem);
			var command = new MockDataCommand(clioFileSystem);
			var options = new MockDataCommandOptions() {
				Models = @"T:/MockDataProjects",
				Data = @"T:/MockDataProjects/Tests/MockData"
			};
			var models = command.FindModels(options.Models);
			models.Count.Should().Be(3);
			models.Should().Contain("Contact");
			models.Should().Contain("Account");
		}

		[Test]
		public void GetODataData() {
			// Arrange
			var clioFileSystem = new FileSystem(_fileSystem);
			var mockCreatioClient = Substitute.For<IApplicationClient>();
			string contactExpectedContent = clioFileSystem.ReadAllText(Path.Combine("T:/MockDataProjects", "Expected", "Contact.json"));
			string orderExpectedContent = clioFileSystem.ReadAllText(Path.Combine("T:/MockDataProjects", "Expected", "Order.json"));
			string accountExpectedContent = clioFileSystem.ReadAllText(Path.Combine("T:/MockDataProjects", "Expected", "Account.json"));
			mockCreatioClient.ExecuteGetRequest(Arg.Is<string>(s => s.EndsWith("Contact")), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns(contactExpectedContent);
			mockCreatioClient.ExecuteGetRequest(Arg.Is<string>(s => s.EndsWith("Order")), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns(orderExpectedContent);
			mockCreatioClient.ExecuteGetRequest(Arg.Is<string>(s => s.EndsWith("Account")), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns(accountExpectedContent);
			var command = new MockDataCommand(mockCreatioClient, new EnvironmentSettings(), clioFileSystem);
			var options = new MockDataCommandOptions() {
				Models = @"T:\MockDataProjects",
				Data = @"T:\MockDataProjects\Tests\MockData"
			};
			command.Execute(options);
			var models = command.FindModels(options.Models);
			var dataFiles = _fileSystem.Directory.GetFiles(options.Data, "*.json", System.IO.SearchOption.AllDirectories);
			dataFiles.Count().Should().Be(models.Count);
			foreach (var dataFile in dataFiles) {
				var model = Path.GetFileNameWithoutExtension(dataFile);
				var expectedContent = clioFileSystem.ReadAllText(Path.Combine("T:/MockDataProjects", "Expected", $"{model}.json"));
				var actualContent = clioFileSystem.ReadAllText(dataFile);
				actualContent.Should().Be(expectedContent);
			}
		}

		
	}
}
