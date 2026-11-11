namespace Clio.Tests.Command.PageModalSize;

using System;
using Clio.Command;
using Clio.Command.PageModalSize;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public sealed class SetPageModalSizeCommandTests : BaseCommandTests<SetPageModalSizeOptions> {
	private readonly IPageModalSizeService _service = Substitute.For<IPageModalSizeService>();
	private SetPageModalSizeCommand _command = null!;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(_service);
	}

	[SetUp]
	public void ResolveCommand() => _command = Container.GetRequiredService<SetPageModalSizeCommand>();

	[TearDown]
	public void ClearCalls() => _service.ClearReceivedCalls();

	private static SetPageModalSizeOptions Options(string size = "large") =>
		new() { PageSchemaName = "UsrOrder_MiniPage", PackageName = "Custom", Size = size };

	[Test]
	[Description("Execute returns 0 and maps the old and new size when the service writes the size.")]
	public void Execute_ShouldReturnZero_WhenServiceWrites() {
		// Arrange
		_service.Set(Arg.Any<PageModalSizeWriteRequest>()).Returns(
			new PageModalSizeWriteResult("UsrOrder_MiniPage", "Custom", "medium", 2, "Custom", "large", "rebuild failed"));

		// Act
		int exitCode = _command.Execute(Options());
		bool ok = _command.TrySet(Options(), out SetPageModalSizeResponse response);

		// Assert
		exitCode.Should().Be(0, because: "a written size is a successful command");
		ok.Should().BeTrue(because: "the write succeeded");
		response.PreviousSize.Should().Be("medium", because: "the old size is reported");
		response.PreviousSizeValue.Should().Be(2, because: "the raw old size is reported");
		response.Size.Should().Be("large", because: "the new size is reported");
		response.AvailableSizes.Should().BeNull(because: "the accepted sizes are listed only for a refused size");
		response.Warning.Should().Be("rebuild failed", because: "a non-fatal warning of the write reaches the caller");
	}

	[Test]
	[Description("Execute returns 1 and lists only the error when the page is not a modal page.")]
	public void Execute_ShouldReturnOne_WhenPageIsNotModal() {
		// Arrange
		_service.Set(Arg.Any<PageModalSizeWriteRequest>())
			.Throws(new InvalidOperationException("Page 'UsrOrder_FormPage' is not a modal page."));

		// Act
		int exitCode = _command.Execute(Options());
		_command.TrySet(Options(), out SetPageModalSizeResponse response);

		// Assert
		exitCode.Should().Be(1, because: "a refused write fails the command");
		response.Error.Should().Contain("not a modal page", because: "the refusal reason reaches the caller");
		response.AvailableSizes.Should().BeNull(because: "the size was not the problem");
	}
}

[TestFixture]
[Property("Module", "Command")]
public sealed class GetPageModalSizeCommandTests : BaseCommandTests<GetPageModalSizeOptions> {
	private readonly IPageModalSizeService _service = Substitute.For<IPageModalSizeService>();
	private GetPageModalSizeCommand _command = null!;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(_service);
	}

	[SetUp]
	public void ResolveCommand() => _command = Container.GetRequiredService<GetPageModalSizeCommand>();

	[TearDown]
	public void ClearCalls() => _service.ClearReceivedCalls();

	[Test]
	[Description("TryGet maps the stored size, its layer, the effective size, its package and the override warning.")]
	public void TryGet_ShouldMapReadResult() {
		// Arrange
		_service.Get(Arg.Any<PageModalSizeReadRequest>()).Returns(new PageModalSizeReadResult(
			"EmailMiniPage", "Custom", "large", 3, "CrtUIv2", "extra-large", "UsrVendor", PageModalSizeCatalog.Names,
			"overridden"));

		// Act
		bool ok = _command.TryGet(
			new GetPageModalSizeOptions { PageSchemaName = "EmailMiniPage", PackageName = "Custom" },
			out GetPageModalSizeResponse response);

		// Assert
		ok.Should().BeTrue(because: "the read succeeded");
		response.Size.Should().Be("large", because: "the stored size is mapped");
		response.SizeValue.Should().Be(3, because: "the raw stored value is mapped");
		response.SourcePackageName.Should().Be("CrtUIv2", because: "the layer the size comes from is mapped");
		response.EffectiveSize.Should().Be("extra-large", because: "the size the window opens with is mapped");
		response.EffectiveSourcePackageName.Should().Be("UsrVendor",
			because: "the package that sets the size the window opens with is mapped");
		response.Warning.Should().Be("overridden", because: "an override warning reaches the caller");
	}
}
