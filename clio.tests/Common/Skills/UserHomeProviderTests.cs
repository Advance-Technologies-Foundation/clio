using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Common.Skills;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.Skills;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
[NonParallelizable]
public sealed class UserHomeProviderTests {
	[Test]
	[Description("Resolves the user home from the USERPROFILE/HOME environment override so agent homes can be isolated (parity with the toolkit's Path.home()).")]
	public void GetAgentHome_ShouldResolveUnderEnvironmentHomeOverride() {
		// Arrange
		string variableName = OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME";
		string isolatedHome = OperatingSystem.IsWindows() ? @"C:\isolated-home" : "/isolated-home";
		string original = Environment.GetEnvironmentVariable(variableName);
		MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>(),
			OperatingSystem.IsWindows() ? @"C:\" : "/");
		UserHomeProvider sut = new(new Clio.Common.FileSystem(mockFileSystem));

		try {
			Environment.SetEnvironmentVariable(variableName, isolatedHome);

			// Act
			string codexHome = sut.GetAgentHome("codex");

			// Assert
			codexHome.Should().Be(Path.Combine(isolatedHome, ".codex"),
				because: "the agent home must resolve under the environment home override, not the OS profile folder");
		}
		finally {
			Environment.SetEnvironmentVariable(variableName, original);
		}
	}
}
