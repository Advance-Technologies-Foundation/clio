using ClioLauncher.Ipc;
using ClioLauncher.Services;
using FluentAssertions;
using NUnit.Framework;

namespace ClioLauncher.Tests;

/// <summary>
/// Unit tests for <see cref="DevClioLaunch.Build"/> (story 11): the pure resolution of a validated dev-clio
/// build path into the <see cref="ClioIpcSettings"/> that launches the clio MCP child process. A <c>.dll</c>
/// is driven by <c>dotnet</c> (dll then the <c>mcp-server</c> verb); a <c>.exe</c> is launched directly with
/// only the <c>mcp-server</c> verb. These assert the exact resolved Command/Args at the source (the settings
/// VM tests cover the same shape via the composition root; this pins the pure builder directly).
/// </summary>
[TestFixture]
[Category("Unit")]
public sealed class DevClioLaunchTests {
	[Test]
	[Description("A .dll dev-clio path resolves to 'dotnet <dll> mcp-server' with the exact args in order.")]
	public void Build_ShouldLaunchViaDotnetWithDllThenMcpServer_WhenPathIsDll() {
		// Arrange — a dev clio.dll build path.
		const string dll = @"C:\dev\clio\clio.dll";

		// Act — build the launch settings.
		ClioIpcSettings resolved = DevClioLaunch.Build(dll);

		// Assert — a .dll is driven by dotnet, passing the dll then the mcp-server verb, in that exact order.
		resolved.Command.Should().Be("dotnet", because: "a .dll dev build is launched via the dotnet host");
		resolved.Args.Should().Equal(new[] { dll, "mcp-server" },
			because: "dotnet receives the dev dll first then the mcp-server verb, in order");
	}

	[Test]
	[Description("A .exe dev-clio path resolves to '<exe> mcp-server' launched directly with only the mcp-server verb.")]
	public void Build_ShouldLaunchExeDirectlyWithMcpServer_WhenPathIsExe() {
		// Arrange — a dev clio.exe build path.
		const string exe = @"C:\dev\clio\clio.exe";

		// Act — build the launch settings.
		ClioIpcSettings resolved = DevClioLaunch.Build(exe);

		// Assert — a .exe is launched directly as the command with only the mcp-server verb.
		resolved.Command.Should().Be(exe, because: "a .exe dev build is launched directly as the process command");
		resolved.Args.Should().Equal(new[] { "mcp-server" },
			because: "the exe is invoked with only the mcp-server verb (no dll argument)");
	}

	[Test]
	[Description("The .dll detection is case-insensitive so a .DLL extension still resolves to the dotnet launch path.")]
	public void Build_ShouldTreatUppercaseDllAsDll_WhenExtensionCaseDiffers() {
		// Arrange — a dev build path with an upper-case .DLL extension.
		const string dll = @"C:\dev\clio\CLIO.DLL";

		// Act — build the launch settings.
		ClioIpcSettings resolved = DevClioLaunch.Build(dll);

		// Assert — case-insensitive extension matching still routes a .DLL through the dotnet host.
		resolved.Command.Should().Be("dotnet", because: "the .dll extension check is case-insensitive");
		resolved.Args.Should().Equal(new[] { dll, "mcp-server" },
			because: "an upper-case .DLL is still driven by dotnet with the dll then the mcp-server verb");
	}
}
