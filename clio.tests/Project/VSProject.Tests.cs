using System.IO;
using Clio.Project;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Project;

[TestFixture]
[Category("Unit")]
[Property("Module", "Core")]
public class VSProjectTests
{

	#region Setup/Teardown

	[SetUp]
	public void SetUp(){
		_destinationPath = Path.Combine(TestContext.CurrentContext.TestDirectory,
			$"vsproject-{TestContext.CurrentContext.Test.ID}");
		Directory.CreateDirectory(_destinationPath);
	}

	[TearDown]
	public void TearDown(){
		if (Directory.Exists(_destinationPath)) {
			Directory.Delete(_destinationPath, true);
		}
	}

	#endregion

	#region Fields: Private

	private string _destinationPath;

	#endregion

	[Test]
	[Description("Writes the generated class into the destination directory, not into its parent under a mangled name (issue 1279)")]
	public void AddFile_WritesFileInsideDestinationDirectory(){
		// Arrange
		VSProject sut = new(_destinationPath, "Clio.Tests.Generated");

		// Act
		sut.AddFile("MyService", "namespace <Namespace> { public class MyService {} }");

		// Assert
		File.Exists(Path.Combine(_destinationPath, "MyService.cs")).Should().BeTrue(
			because: "a hard-coded backslash separator made the file land in the parent "
				+ "directory as 'cs\\MyService.cs' on macOS and Linux");
		Directory.GetFiles(Directory.GetParent(_destinationPath)!.FullName, "*MyService*")
			.Should().BeEmpty(because: "nothing may be written outside the destination directory");
	}

	[Test]
	[Description("Replaces the namespace placeholder in the generated file (issue 1279)")]
	public void AddFile_ReplacesNamespacePlaceholder(){
		// Arrange
		VSProject sut = new(_destinationPath, "Clio.Tests.Generated");

		// Act
		sut.AddFile("MyService", "namespace <Namespace> { }");

		// Assert
		File.ReadAllText(Path.Combine(_destinationPath, "MyService.cs")).Should()
			.Be("namespace Clio.Tests.Generated { }",
				because: "the namespace placeholder must be replaced with the project namespace");
	}

}
