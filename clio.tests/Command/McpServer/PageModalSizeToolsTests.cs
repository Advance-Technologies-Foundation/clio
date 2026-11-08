using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Command.PageModalSize;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

// NonParallelizable: TearDown restores McpToolExecutionLock's process-global facade, like the sibling
// get-related-page-addon tool tests.
[TestFixture]
[NonParallelizable]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageModalSizeToolsTests {
	private const string TenantKey = "https://tenant-under-test.creatio.com|modal-size";

	private IPageModalSizeService _service = null!;
	private IToolCommandResolver _commandResolver = null!;
	private GetPageModalSizeTool _getTool = null!;
	private SetPageModalSizeTool _setTool = null!;
	private PageModalSizeWriteRequest _capturedWrite;
	private SetPageModalSizeOptions _resolverOptions;

	[SetUp]
	public void SetUp() {
		ConsoleLogger.Instance.ClearMessages();
		_capturedWrite = null;
		_resolverOptions = null;
		_service = Substitute.For<IPageModalSizeService>();
		_service.Get(Arg.Any<PageModalSizeReadRequest>()).Returns(new PageModalSizeReadResult(
			"UsrOrder_MiniPage", "Custom", "large", 3, "Custom", "large", PageModalSizeCatalog.Names));
		_service.Set(Arg.Any<PageModalSizeWriteRequest>()).Returns(call => {
			_capturedWrite = call.Arg<PageModalSizeWriteRequest>();
			PageModalSizeCatalog.Parse(_capturedWrite.Size);
			return new PageModalSizeWriteResult("UsrOrder_MiniPage", "Custom", "small", 1, null, "large", null);
		});
		_commandResolver = Substitute.For<IToolCommandResolver>();
		_commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns(TenantKey);
		_commandResolver.Resolve<GetPageModalSizeCommand>(Arg.Any<GetPageModalSizeOptions>())
			.Returns(new GetPageModalSizeCommand(_service, ConsoleLogger.Instance));
		_commandResolver.Resolve<SetPageModalSizeCommand>(Arg.Any<SetPageModalSizeOptions>())
			.Returns(call => {
				_resolverOptions = call.Arg<SetPageModalSizeOptions>();
				return new SetPageModalSizeCommand(_service, ConsoleLogger.Instance);
			});
		IPageModalSizeService startupService = Substitute.For<IPageModalSizeService>();
		_getTool = new GetPageModalSizeTool(
			new GetPageModalSizeCommand(startupService, ConsoleLogger.Instance), ConsoleLogger.Instance, _commandResolver);
		_setTool = new SetPageModalSizeTool(
			new SetPageModalSizeCommand(startupService, ConsoleLogger.Instance), ConsoleLogger.Instance, _commandResolver);
	}

	[TearDown]
	public void TearDown() {
		ConsoleLogger.Instance.ClearMessages();
		McpToolExecutionLock.Configure(
			TenantExecutionLockProvider.Shared,
			new SessionContainerCache(SessionContainerCacheDefaults.IdleTtl, SessionContainerCacheDefaults.MaxSessions));
		_commandResolver.ClearReceivedCalls();
	}

	[Test]
	[Description("get-page-modal-size resolves the command for the environment and returns the stored and effective size.")]
	public void GetPageModalSize_ShouldReturnSize_WhenEnvironmentProvided() {
		// Act
		GetPageModalSizeResponse response = _getTool.GetPageModalSize(
			new GetPageModalSizeArgs("UsrOrder_MiniPage", "Custom") { EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeTrue(because: "the read succeeded");
		response.Size.Should().Be("large", because: "the stored size is mapped into the response");
		response.SourcePackageName.Should().Be("Custom", because: "the layer the size comes from is reported");
		response.AvailableSizes.Should().Contain("extra-large", because: "the read names the accepted sizes");
		_commandResolver.Received(1).Resolve<GetPageModalSizeCommand>(
			Arg.Is<GetPageModalSizeOptions>(options => options.Environment == "dev"));
	}

	[Test]
	[Description("set-page-modal-size maps page, package, size and environment onto the command and returns the old and new size.")]
	public void SetPageModalSize_ShouldMapArgsAndReturnSizes() {
		// Act
		SetPageModalSizeResponse response = _setTool.SetPageModalSize(
			new SetPageModalSizeArgs("UsrOrder_MiniPage", "Custom", "L") { EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeTrue(because: "the write succeeded");
		response.PreviousSize.Should().Be("small", because: "the old size is reported");
		response.Size.Should().Be("large", because: "the new size is reported");
		_capturedWrite.Should().Be(new PageModalSizeWriteRequest("Custom", "UsrOrder_MiniPage", "L"),
			because: "the args reach the service unchanged");
		_resolverOptions.Environment.Should().Be("dev", because: "the command is resolved for the requested environment");
	}

	[Test]
	[Description("set-page-modal-size refuses an unsupported size and lists the accepted sizes.")]
	public void SetPageModalSize_ShouldListAvailableSizes_WhenSizeIsUnsupported() {
		// Act
		SetPageModalSizeResponse response = _setTool.SetPageModalSize(
			new SetPageModalSizeArgs("UsrOrder_MiniPage", "Custom", "huge") { EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(because: "the platform does not support that size");
		response.AvailableSizes.Should().Equal(["small", "medium", "large", "extra-large"],
			because: "the agent must be able to name the accepted sizes to the user");
		response.Error.Should().NotContain("(Parameter", because: "the error is a clean message for the user");
	}

	[Test]
	[Description("set-page-modal-size refuses a misspelled argument instead of dropping it, and writes nothing.")]
	public void SetPageModalSize_ShouldRefuseUnknownArgument() {
		// Arrange
		SetPageModalSizeArgs args = new("UsrOrder_MiniPage", "Custom", "large") {
			EnvironmentName = "dev",
			ExtensionData = new Dictionary<string, JsonElement> {
				["window-size"] = JsonSerializer.SerializeToElement("large")
			}
		};

		// Act
		SetPageModalSizeResponse response = _setTool.SetPageModalSize(args);

		// Assert
		response.Success.Should().BeFalse(because: "an unknown argument may be the size the caller meant");
		response.Error.Should().Contain("window-size", because: "the error names the unknown argument");
		_service.DidNotReceive().Set(Arg.Any<PageModalSizeWriteRequest>());
	}

	[Test]
	[Description("get-page-modal-size refuses a misspelled argument instead of reading another page.")]
	public void GetPageModalSize_ShouldRefuseUnknownArgument() {
		// Arrange
		GetPageModalSizeArgs args = new("UsrOrder_MiniPage", "Custom") {
			EnvironmentName = "dev",
			ExtensionData = new Dictionary<string, JsonElement> {
				["page-name"] = JsonSerializer.SerializeToElement("Other")
			}
		};

		// Act
		GetPageModalSizeResponse response = _getTool.GetPageModalSize(args);

		// Assert
		response.Success.Should().BeFalse(because: "an unknown argument is refused");
		_service.DidNotReceive().Get(Arg.Any<PageModalSizeReadRequest>());
	}

	[Test]
	[Description("The read tool is advertised read-only and the write tool as a non-destructive, idempotent write.")]
	public void Tools_ShouldCarryExpectedSafetyAnnotations() {
		// Arrange
		McpServerToolAttribute getAttribute = typeof(GetPageModalSizeTool)
			.GetMethod(nameof(GetPageModalSizeTool.GetPageModalSize))!.GetCustomAttribute<McpServerToolAttribute>()!;
		McpServerToolAttribute setAttribute = typeof(SetPageModalSizeTool)
			.GetMethod(nameof(SetPageModalSizeTool.SetPageModalSize))!.GetCustomAttribute<McpServerToolAttribute>()!;

		// Assert
		getAttribute.Name.Should().Be("get-page-modal-size", because: "the tool name is the agent contract");
		getAttribute.ReadOnly.Should().BeTrue(because: "the read changes nothing");
		setAttribute.Name.Should().Be("set-page-modal-size", because: "the tool name is the agent contract");
		setAttribute.ReadOnly.Should().BeFalse(because: "the tool writes an add-on");
		setAttribute.Destructive.Should().BeFalse(
			because: "the write only replaces the size; every other add-on field is kept");
		setAttribute.Idempotent.Should().BeTrue(because: "writing the same size twice leaves the same state");
	}
}
