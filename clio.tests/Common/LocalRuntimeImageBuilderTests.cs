using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using Clio.Common.RuntimeAttachment;
using Clio.Common.OperatorBootstrap;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture, Category("Integration"), Property("Module", "Common")]
public class LocalRuntimeImageBuilderTests {
	private string _archive;

	[SetUp]
	public void SetUp() => _archive = Path.Combine(Path.GetTempPath(), "clio-runtime-" + Guid.NewGuid().ToString("N") + ".zip");

	[TearDown]
	public void TearDown() { if (File.Exists(_archive)) { File.Delete(_archive); } }

	private void WriteArchive(string framework, bool duplicate = false) {
		using ZipArchive zip = ZipFile.Open(_archive, ZipArchiveMode.Create);
		using (StreamWriter writer = new(zip.CreateEntry("app/Terrasoft.WebHost.runtimeconfig.json").Open())) {
			writer.Write("{\"runtimeOptions\":{\"tfm\":\"" + framework + "\"}}");
		}
		if (duplicate) { zip.CreateEntry("other/Terrasoft.WebHost.runtimeconfig.json"); }
	}

	[TestCase("net8.0", "8.0"), TestCase("net10.0", "10.0")]
	[Description("The runtime label comes from the actual ZIP configuration rather than its filename.")]
	public void DetectRuntime_ShouldReadTargetFramework_WhenArchiveIsSupported(string framework, string expected) {
		// Arrange
		WriteArchive(framework);
		// Act
		string runtime = LocalRuntimeImageBuilder.DetectRuntime(_archive);
		// Assert
		runtime.Should().Be(expected, "Creatio filenames do not reliably encode the .NET target");
	}

	[Test, Description("Ambiguous archives cannot silently select the wrong .NET runtime.")]
	public void DetectRuntime_ShouldRejectAmbiguity_WhenTwoRuntimeConfigurationsExist() {
		// Arrange
		WriteArchive("net10.0", true);
		// Act
		Action act = () => LocalRuntimeImageBuilder.DetectRuntime(_archive);
		// Assert
		act.Should().Throw<InvalidOperationException>("the build must have one authoritative application runtime");
	}

	[Test, Description("Unsupported framework archives fail before invoking a build or mutating Rancher.")]
	public void DetectRuntime_ShouldRejectUnsupportedFramework_WhenArchiveIsNetFramework() {
		// Arrange
		WriteArchive("net472");
		// Act
		Action act = () => LocalRuntimeImageBuilder.DetectRuntime(_archive);
		// Assert
		act.Should().Throw<InvalidOperationException>("Windows/IIS images are outside this Linux runtime flow");
	}

	[TestCase(false, false), TestCase(true, false), TestCase(false, true), Platform("Win")]
	[Description("Windows orchestration translates staging paths without a shell and cleans only its own volume after success or failure.")]
	public void Build_ShouldCleanOwnedStaging_WhenPlanOrBuildCompletes(bool malformedPlan, bool buildFailure) {
		// Arrange
		WriteArchive("net10.0");
		IAttachmentProcess process = Substitute.For<IAttachmentProcess>();
		IProcessExecutor executor = Substitute.For<IProcessExecutor>();
		List<string[]> shortCalls = [];
		List<ProcessExecutionOptions> longCalls = [];
		process.Run("rdctl", Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>()).Returns(call => {
			string[] args = call.ArgAt<IReadOnlyList<string>>(1).ToArray();
			shortCalls.Add(args);
			if (args.Contains("/work/context/plan.tsv")) {
				return malformedPlan ? "bad-plan" : "/work/context/03-dev/context\tcreatio-dev:test\tbuild-arg:BASE_IMAGE=base:1\tlabel:org.creatio.database-source=test";
			}
			return args.Contains("inspect") ? "/test-volume/_data" : "";
		});
		executor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>()).Returns(call => {
			ProcessExecutionOptions request = call.Arg<ProcessExecutionOptions>();
			longCalls.Add(request);
			return Task.FromResult(new ProcessExecutionResult { Started = true,
				ExitCode = buildFailure && request.ArgumentList.Contains("build") ? 1 : 0 });
		});
		using ServiceProvider container = new ServiceCollection().AddSingleton(process).AddSingleton(executor)
			.AddSingleton(Substitute.For<IRancherDesktopTarget>()).AddSingleton(Substitute.For<ILogger>())
			.AddTransient<ILocalRuntimeImageBuilder, LocalRuntimeImageBuilder>().BuildServiceProvider();
		// Act
		Action act = () => container.GetRequiredService<ILocalRuntimeImageBuilder>().Build(new RuntimeOptions { Context = "rancher-desktop", Source = _archive });
		// Assert
		if (malformedPlan || buildFailure) { act.Should().Throw<InvalidOperationException>("bad preparation or failed Docker builds cannot report success"); }
		else { act.Should().NotThrow("a valid build plan should finish"); }
		string volume = shortCalls.Single(a => a.Contains("create")).Last();
		shortCalls.Should().Contain(a => a.Contains("volume") && a.Contains("rm") && a.Last() == volume, "only this invocation's staging volume must be cleaned");
		if (!malformedPlan) {
			longCalls.Last().ArgumentList.Should().Contain("/test-volume/_data/context/03-dev/context", "the remote context must map to the volume mountpoint");
			longCalls.Last().ArgumentList.Should().Contain("DOTNET_VERSION=10.0", "the build must use the framework read from the ZIP");
		}
	}
}
