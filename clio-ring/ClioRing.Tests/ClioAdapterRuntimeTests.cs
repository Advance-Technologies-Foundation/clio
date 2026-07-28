using System.Diagnostics;
using ClioRing.Ipc;
using ClioRing.Models;
using ClioRing.Services;
using FluentAssertions;
using NUnit.Framework;

namespace ClioRing.Tests;

/// <summary>Verifies ordinary Ring actions use the same resolved clio runtime as IPC workflows.</summary>
[TestFixture]
[Category("Unit")]
public sealed class ClioAdapterRuntimeTests {
	[Test]
	[Description("Release radial actions execute the resolved release shim with the requested command instead of mcp-server.")]
	public void BuildStartInfo_ShouldUseReleaseRuntime_WhenReleaseSelected() {
		// Arrange
		var runtime = new ResolvedClioRuntime(ClioRuntimeMode.Release, ClioIpcSettings.Default);
		var sut = new ClioAdapter(runtime);
		var invocation = new ClioInvocation { Verb = "get-info", EnvName = "ve" };

		// Act
		ProcessStartInfo result = sut.BuildStartInfo(invocation);

		// Assert
		result.FileName.Should().Be(ClioIpcSettings.Default.Command,
			"because ordinary actions and IPC must share the resolved release executable");
		result.ArgumentList.Should().Equal(new[] { "get-info", "-e", "ve" },
			"because mcp-server is replaced by the selected radial action command");
	}

	[Test]
	[Description("Development DLL radial actions execute dotnet with the same DLL before the requested command.")]
	public void BuildStartInfo_ShouldUseDevelopmentDll_WhenDevelopmentSelected() {
		// Arrange
		const string devDll = @"C:\Projects\clio\clio\bin\Debug\net10.0\clio.dll";
		var launch = new ClioIpcSettings { Command = "dotnet", Args = new[] { devDll, "mcp-server" } };
		var runtime = new ResolvedClioRuntime(ClioRuntimeMode.Development, launch);
		var sut = new ClioAdapter(runtime);
		var invocation = new ClioInvocation { Verb = "show-web-app-list" };

		// Act
		ProcessStartInfo result = sut.BuildStartInfo(invocation);

		// Assert
		result.FileName.Should().Be("dotnet", "because a framework-dependent development build uses dotnet");
		result.ArgumentList.Should().Equal(new[] { devDll, "show-web-app-list" },
			"because the development DLL stays before the ordinary clio verb");
	}

	[Test]
	[Description("Ordinary radial actions preserve the explicit development working directory used by IPC workflows.")]
	public void BuildStartInfo_ShouldPreserveWorkingDirectory_WhenDevelopmentTargetRequiresIt() {
		// Arrange
		const string workingDirectory = @"C:\Projects\clio";
		var launch = new ClioIpcSettings {
			Command = "dotnet", Args = new[] { @"clio\bin\Debug\net10.0\clio.dll", "mcp-server" },
			WorkingDirectory = workingDirectory
		};
		var sut = new ClioAdapter(new ResolvedClioRuntime(ClioRuntimeMode.Development, launch));

		// Act
		ProcessStartInfo result = sut.BuildStartInfo(new ClioInvocation { Verb = "get-info" });

		// Assert
		result.WorkingDirectory.Should().Be(workingDirectory,
			"because radial actions must resolve relative development arguments exactly like IPC");
	}
}
