using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

/// <summary>
/// Guards what lands beside <c>clio.tests.dll</c>. Code under test launches tools such as <c>git</c> by bare
/// name, and a bare-name launch looks in the launching process's own directory before <c>PATH</c> (Windows
/// <c>CreateProcess</c> searches the application directory first; on Unix the .NET launcher checks the
/// executable's directory and then the current directory), so a tool-named file here is what those tests start.
/// </summary>
/// <remarks>
/// See <c>docs/knowledge/Tests/the-git-process-fixture-must-not-reach-the-clio-tests-output.md</c>.
/// </remarks>
[TestFixture]
[Category("Unit")]
// Without a Module trait no targeted TestCategory=Unit&Module=X run executes this guard.
[Property("Module", "Core")]
public sealed class TestOutputDirectoryTests {

	private const string StaleOutputHint =
		" If clio.mcp.e2e.csproj already has it, the files predate the fix: rebuild, or delete them.";

	private static readonly string OutputDirectory = AppContext.BaseDirectory;

	[Test]
	[Description("No file named git (the Unix apphost) or git.* (git.exe, git.dll, git.deps.json, git.runtimeconfig.json) sits beside clio.tests.dll, so a bare-name git launch from a unit test reaches the real Git rather than clio.process.fixture.")]
	public void OutputDirectory_ShouldContainNoGitNamedFile_WhenTheTestProjectsAreBuilt() {
		// Arrange
		string[] fileNames = Directory.GetFiles(OutputDirectory).Select(Path.GetFileName).ToArray();

		// Act
		string[] gitNamed = fileNames
			.Where(name => name.Equals("git", StringComparison.OrdinalIgnoreCase)
				|| name.StartsWith("git.", StringComparison.OrdinalIgnoreCase))
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		// Assert
		gitNamed.Should().BeEmpty(
			because: "the clio.process.fixture ProjectReference in clio.mcp.e2e.csproj must carry Private=\"false\" "
				+ "so the fake git is not copied here." + StaleOutputHint + " Found in " + OutputDirectory + ": "
				+ string.Join(", ", gitNamed));
	}

	[Test]
	[Description("Every <name>.runtimeconfig.json beside clio.tests.dll has its <name>.dll, so no application in the test output is one that cannot start.")]
	public void OutputDirectory_ShouldContainNoRuntimeConfigWithoutItsAssembly_WhenTheTestProjectsAreBuilt() {
		// Arrange
		const string runtimeConfigSuffix = ".runtimeconfig.json";
		string[] runtimeConfigs = Directory.GetFiles(OutputDirectory, "*" + runtimeConfigSuffix);

		// Act
		string[] withoutAssembly = runtimeConfigs
			.Select(Path.GetFileName)
			.Select(name => name[..^runtimeConfigSuffix.Length])
			.Where(application => !File.Exists(Path.Combine(OutputDirectory, application + ".dll")))
			.OrderBy(application => application, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		// Assert
		withoutAssembly.Should().BeEmpty(
			because: "a runtimeconfig.json without its assembly is typically an Exe ProjectReference with "
				+ "ReferenceOutputAssembly=\"false\" but without Private=\"false\"." + StaleOutputHint
				+ " Missing assemblies in " + OutputDirectory + ": " + string.Join(", ", withoutAssembly));
	}

}
