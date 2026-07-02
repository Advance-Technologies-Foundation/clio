namespace Clio.Tests.Command;

using System;
using Clio.Command;
using Clio.Command.RelatedPages;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class GetRelatedPageAddonCommandTests {
	private IRelatedPageAddonService _service = null!;
	private ILogger _logger = null!;
	private GetRelatedPageAddonCommand _command = null!;

	[SetUp]
	public void SetUp() {
		_service = Substitute.For<IRelatedPageAddonService>();
		_logger = Substitute.For<ILogger>();
		_command = new GetRelatedPageAddonCommand(_service, _logger);
	}

	private static GetRelatedPageAddonOptions Options() =>
		new() { EntitySchemaName = "UsrDeliveryItem", PackageName = "Custom" };

	private static RelatedPageAddonReadResult SampleResult() =>
		new("UsrDeliveryItem", "bb000000-0000-0000-0000-000000000002", "Custom",
			"aa000000-0000-0000-0000-000000000001", "RelatedPage", null, 1,
			new[] {
				new RelatedPageEntry("cc000000-0000-0000-0000-00000000000a", "UsrDeliveryItemFormPage",
					true, false, false, null, null, null)
			});

	[Test]
	[Description("TryGet maps the service read result into the response when the service succeeds.")]
	public void TryGet_ShouldMapResultIntoResponse_WhenServiceSucceeds() {
		// Arrange
		_service.Get(Arg.Any<RelatedPageAddonReadRequest>()).Returns(SampleResult());

		// Act
		bool ok = _command.TryGet(Options(), out GetRelatedPageAddonResponse response);

		// Assert
		ok.Should().BeTrue(because: "a successful read returns true");
		response.Success.Should().BeTrue(because: "the response reflects the successful read");
		response.EntitySchemaName.Should().Be("UsrDeliveryItem",
			because: "the object name is carried into the response");
		response.PageCount.Should().Be(1,
			because: "the decoded page count is carried into the response");
		response.Pages.Should().ContainSingle(page => page.PageSchemaName == "UsrDeliveryItemFormPage",
			because: "the decoded entry is mapped into the response DTO");
	}

	[Test]
	[Description("Execute returns exit code 0 and logs the serialized response when the service succeeds.")]
	public void Execute_ShouldReturnZero_WhenServiceSucceeds() {
		// Arrange
		_service.Get(Arg.Any<RelatedPageAddonReadRequest>()).Returns(SampleResult());

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(0, because: "a successful read maps to exit code 0 for the shell/CI");
		_logger.Received().WriteInfo(Arg.Is<string>(message => message.Contains("UsrDeliveryItemFormPage")));
	}

	[Test]
	[Description("Execute returns exit code 1 and reports the error when the service throws.")]
	public void Execute_ShouldReturnOne_WhenServiceThrows() {
		// Arrange
		_service.Get(Arg.Any<RelatedPageAddonReadRequest>())
			.Returns(_ => throw new InvalidOperationException("boom"));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "a failed read maps to exit code 1 for the shell/CI");
		_logger.Received().WriteInfo(Arg.Is<string>(message => message.Contains("boom")));
	}
}
