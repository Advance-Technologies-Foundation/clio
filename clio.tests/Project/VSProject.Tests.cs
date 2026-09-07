using System;
using System.IO;
using Clio.Project;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Project;

/// <summary>
/// Integration, not Unit: every test here writes and reads real files. The Unit tier is defined as no
/// I/O and no external dependencies, so a filesystem fixture in it makes the fast gate host-dependent.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("Integration")]
[Property("Module", "Core")]
public class VSProjectTests
{

	#region Setup/Teardown

	[SetUp]
	public void SetUp(){
		//A per-test root under the test directory: the fixture must never reuse, and therefore never
		//delete, a directory it did not create.
		_scratchRoot = Path.Combine(TestContext.CurrentContext.TestDirectory,
			$"vsproject-{TestContext.CurrentContext.Test.ID}");
		Directory.CreateDirectory(_scratchRoot);
		_destinationPath = Path.Combine(_scratchRoot, "dest");
		Directory.CreateDirectory(_destinationPath);
		_originalCurrentDirectory = Directory.GetCurrentDirectory();
	}

	[TearDown]
	public void TearDown(){
		//Restored first: a test that moved the process working directory into the scratch root would
		//otherwise keep a handle on the tree being deleted.
		Directory.SetCurrentDirectory(_originalCurrentDirectory);
		if (Directory.Exists(_scratchRoot)) {
			Directory.Delete(_scratchRoot, true);
		}
	}

	#endregion

	#region Fields: Private

	private string _destinationPath;
	private string _originalCurrentDirectory;
	private string _scratchRoot;

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

	[Test]
	[Description("Defaults a missing destination to <cwd>/Files/cs, the branch the explicit-destination tests bypass entirely")]
	public void AddFile_WritesUnderFilesCs_WhenDestinationIsNotSupplied(){
		// Arrange
		string packageRoot = Path.Combine(_scratchRoot, "package");
		Directory.CreateDirectory(Path.Combine(packageRoot, "Files", "cs"));
		Directory.SetCurrentDirectory(packageRoot);
		VSProject sut = new(destPath: null, @namespace: "Clio.Tests.Generated");

		// Act
		sut.AddFile("DefaultDestination", "namespace <Namespace> { }");

		// Assert
		File.Exists(Path.Combine(packageRoot, "Files", "cs", "DefaultDestination.cs")).Should().BeTrue(
			because: "an omitted destination must resolve to <cwd>/Files/cs, which is the only path "
				+ "add-item relies on when it is run from inside a package");
	}

	[TestCase("../Outside", TestName = "AddFile_Rejects_ParentTraversal")]
	[TestCase("sub/Outside", TestName = "AddFile_Rejects_ForwardSlashPath")]
	[TestCase("sub\\Outside", TestName = "AddFile_Rejects_BackslashPath")]
	[TestCase("..", TestName = "AddFile_Rejects_BareParentDirectory")]
	[Description("Rejects an item name that is not one plain file name, because Path.Combine discards the destination for a rooted name and add-item passes server-returned keys here (issue 1279)")]
	public void AddFile_RejectsNameThatIsNotASingleFileName(string name){
		// Arrange
		VSProject sut = new(_destinationPath, "Clio.Tests.Generated");

		// Act
		Action act = () => sut.AddFile(name, "namespace <Namespace> { }");

		// Assert
		act.Should().Throw<ArgumentException>(
			because: "a name carrying a directory, a drive or '..' would write outside the destination");
		Directory.GetFiles(_scratchRoot, "*Outside*", SearchOption.AllDirectories).Should().BeEmpty(
			because: "a rejected name must leave no file behind anywhere under the scratch root");
	}

	[Test]
	[Description("Rejects a rooted item name, the exact input that made add-item exit 0 while writing to an absolute path outside the destination (issue 1279)")]
	public void AddFile_RejectsRootedName(){
		// Arrange
		VSProject sut = new(_destinationPath, "Clio.Tests.Generated");
		string rootedName = Path.Combine(_scratchRoot, "Outside");

		// Act
		Action act = () => sut.AddFile(rootedName, "namespace <Namespace> { }");

		// Assert
		act.Should().Throw<ArgumentException>(
			because: "Path.Combine silently discards the destination when the second argument is rooted");
		File.Exists(rootedName + ".cs").Should().BeFalse(
			because: "the rooted path must never be written, inside or outside the destination");
	}

}
