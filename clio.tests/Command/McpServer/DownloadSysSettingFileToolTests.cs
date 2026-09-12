using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[Property("Module", "McpServer")]
public sealed class DownloadSysSettingFileToolTests : BaseClioModuleTests {
	private IDownloadSysSettingFileService _service;
	private IToolCommandResolver _resolver;
	private DownloadSysSettingFileTool _tool;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_service = Substitute.For<IDownloadSysSettingFileService>();
		_resolver = Substitute.For<IToolCommandResolver>();
		services.AddSingleton(_service);
		services.AddSingleton(_resolver);
		services.AddTransient<DownloadSysSettingFileTool>();
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_resolver.Resolve<DownloadSysSettingFileCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(Container.GetRequiredService<DownloadSysSettingFileCommand>());
		_resolver.Resolve<IRequiredPackageChecker>(Arg.Any<EnvironmentOptions>()).Returns(Substitute.For<IRequiredPackageChecker>());
		_tool = Container.GetRequiredService<DownloadSysSettingFileTool>();
	}

	[TearDown]
	public override void TearDown() {
		_service.ClearReceivedCalls();
		_resolver.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("The download is a non-overwriting local write in the long-tail catalog, not a resident read tool.")]
	public void Download_ShouldAdvertiseLocalWrite_WhenContractIsInspected() {
		// Arrange
		MethodInfo method = typeof(DownloadSysSettingFileTool).GetMethod(nameof(DownloadSysSettingFileTool.Download))!;
		// Act
		McpServerToolAttribute attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;
		// Assert
		attribute.Name.Should().Be(DownloadSysSettingFileTool.ToolName, because: "the shared constant names the tool");
		attribute.ReadOnly.Should().BeFalse(because: "the tool writes a local file");
		attribute.Destructive.Should().BeFalse(because: "existing files are preserved");
		attribute.Idempotent.Should().BeFalse(because: "a repeated call refuses the existing filename");
		McpCoreToolProfile.CoreToolTypes.Should().NotContain(typeof(DownloadSysSettingFileTool), because: "clio-run exposes this long-tail operation");
	}

	[Test]
	[Description("The MCP adapter resolves the selected environment and forwards the exact code and filename.")]
	public void Download_ShouldResolveSelectedEnvironment_WhenArgumentsAreValid() {
		// Arrange
		string path = Path.Combine(Path.GetTempPath(), "chosen.json");
		_service.Download("LogoImage", path).Returns(new DownloadSysSettingFileReceipt(path, 23));
		// Act
		DownloadSysSettingFileResult result = _tool.Download(new("requested-env", "LogoImage", path));
		// Assert
		result.ExitCode.Should().Be(0, because: "the resolved service completed the file copy");
		result.FileName.Should().Be(path, because: "the adapter must preserve the caller's path");
		result.ByteCount.Should().Be(23, because: "the service owns the byte count");
		result.Output.Should().Contain(m => m.LogDecoratorType == LogDecoratorType.Info, because: "successful calls include Info");
		_resolver.ReceivedCalls().Where(c => c.GetMethodInfo().Name == "Resolve").SelectMany(c => c.GetArguments())
			.OfType<EnvironmentOptions>().Should().OnlyContain(o => o.Environment == "requested-env", because: "both the package gate and command must use this call's environment");
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("relative.bin")]
	[Description("The MCP adapter refuses missing or relative filenames without reaching a service.")]
	public void Download_ShouldRefuseFilename_WhenNotAbsolute(string path) {
		// Arrange
		DownloadSysSettingFileArgs args = new("env", "Blob", path);
		// Act
		DownloadSysSettingFileResult result = _tool.Download(args);
		// Assert
		result.ExitCode.Should().NotBe(0, because: "MCP requires an explicit absolute destination");
		result.Output.Should().Contain(m => m.LogDecoratorType == LogDecoratorType.Error, because: "the caller needs a refusal reason");
		_service.ReceivedCalls().Should().BeEmpty(because: "invalid paths must not initiate a download");
	}
}
