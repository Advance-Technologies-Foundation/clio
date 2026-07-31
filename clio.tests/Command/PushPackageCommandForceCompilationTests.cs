using Clio;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// ENG-93157 RC-10: `push-package --force-compilation` must run its internal compile without the new
/// interactive heavy-operation prompt, so a declined prompt cannot postpone the compile while
/// push-package still reports success. This is the only coverage of that in-process call site.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PushPackageCommandForceCompilationTests {

	[Test]
	[Description("push-package --force-compilation invokes the internal compile with IsSilent=true so it proceeds without prompting and its exit code reliably reflects the real compilation outcome (RC-10).")]
	public void Execute_ShouldRunForcedCompileSilently_WhenForceCompilation() {
		// Arrange
		EnvironmentSettings environmentSettings = new() { Uri = "http://localhost", Login = "s", Password = "p" };
		IPackageInstaller packageInstaller = Substitute.For<IPackageInstaller>();
		packageInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(),
			Arg.Any<PackageInstallOptions>(), Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
		IMarketplace marketplace = Substitute.For<IMarketplace>();
		ICompileConfigurationCommand compileConfigurationCommand = Substitute.For<ICompileConfigurationCommand>();
		CompileConfigurationOptions capturedCompileOptions = null;
		compileConfigurationCommand.Execute(Arg.Do<CompileConfigurationOptions>(o => capturedCompileOptions = o))
			.Returns(0);
		ILogger logger = Substitute.For<ILogger>();
		PushPackageCommand command = new(environmentSettings, packageInstaller, marketplace,
			compileConfigurationCommand, logger);
		PushPkgOptions options = new() { Name = "UsrPackage.gz", ForceCompilation = true };

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "a successful install followed by a successful forced compile returns success");
		compileConfigurationCommand.Received(1).Execute(Arg.Any<CompileConfigurationOptions>());
		capturedCompileOptions.Should().NotBeNull(
			because: "ForceCompilation must invoke the compile command");
		capturedCompileOptions!.IsSilent.Should().BeTrue(
			because: "the forced compile must run silently so a heavy-operation prompt can never postpone it and leave push-package falsely reporting success");
	}

	[Test]
	[Description("RC-16 drift guard: every in-process CompileConfigurationOptions construction helper sets IsSilent=true, so a future in-process caller that forgets to compile silently fails CI instead of reopening the prompt-hang / false-success class (RC-10/RC-12/RC-15). Add new helpers here.")]
	public void InProcessCompileOptionBuilders_ShouldAllBeSilent() {
		// Act — the known in-process helpers that build compile options for a programmatic compile.
		CompileConfigurationOptions pushForceOptions =
			PushPackageCommand.CreateFromPushPkgOptions(new PushPkgOptions { Environment = "dev" });
		CompileConfigurationOptions envUiOptions = EnvManageUiCommand.BuildEnvUiCompileOptions("dev");

		// Assert
		pushForceOptions.IsSilent.Should().BeTrue(
			because: "push-package --force-compilation compiles in-process and must not prompt (RC-10)");
		envUiOptions.IsSilent.Should().BeTrue(
			because: "the env-ui compile menu action compiles in-process and must not prompt (RC-12)");
	}
}
