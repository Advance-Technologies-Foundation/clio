using Clio.Command;
using Clio.Common;
using Clio.Tests.Extensions;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
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
		public void FindModelsp() {
			// Arrange
			var clioFileSystem = new FileSystem(_fileSystem);
			var command = new MockDataCommand(clioFileSystem);
			var options = new MockDataCommandOptions() {
				Models = @"T:\MockDataProjects",
				Data = @"T:\MockDataProjects\Tests\MockData"
			};
			var models = command.FindModels(options.Models);
			models.Count.Should().Be(3);
			models.Should().Contain("Contact");
			models.Should().Contain("Account");
		}
	}
}
