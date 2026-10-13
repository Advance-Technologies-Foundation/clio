using System.Collections.Generic;
using System.Threading;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-101924 on the update-page save path: the binding check narrows to the inputs the mobile registry declares,
/// through the <c>ValidateBody</c> seam that gates the write.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageUpdateToolMobileBindingTests {

	private static PageUpdateTool BuildTool(IMobileComponentInfoCatalog mobileCatalog) =>
		new(
			command: null,
			logger: ConsoleLogger.Instance,
			commandResolver: Substitute.For<IToolCommandResolver>(),
			mobileComponentCatalog: mobileCatalog,
			webComponentCatalog: Substitute.For<IComponentInfoCatalog>(),
			pageBaselineGuard: new PageBaselineGuard(Substitute.For<System.IO.Abstractions.IFileSystem>()),
			new PersistedResourceKeyReader(),
			new PageDataSourceReferenceValidator(new PageSchemaBodyParser()));

	private static IMobileComponentInfoCatalog LiveMobileCatalog() {
		IMobileComponentInfoCatalog catalog = Substitute.For<IMobileComponentInfoCatalog>();
		catalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(MobileFieldBindingDeclaredInputsTests.LiveCatalog());
		return catalog;
	}

	[Test]
	[Description("update-page saves a body whose only unprovable binding sits in a property the registry does not declare.")]
	public void ValidateBody_WhenBindingIsInUndeclaredProperty_DoesNotAbortTheSave() {
		// Arrange
		PageUpdateTool tool = BuildTool(LiveMobileCatalog());
		PageUpdateOptions options = new() {
			SchemaName = "UsrTest_MobileFormPage",
			Body = MobileFieldBindingDeclaredInputsTests.Body(
				"""{"operation":"insert","name":"LeadList","values":{"type":"crt.List","selectionState":"$LeadList_SelectionState"}}""")
		};

		// Act
		(PageUpdateResponse failure, IReadOnlyList<string> _) = tool.ValidateBody(options, requestedVersion: null);

		// Assert
		failure.Should().BeNull(
			because: "selectionState is not a crt.List input, so the save path must not block on its binding");
	}

	[Test]
	[Description("update-page still aborts the save when a declared input binds an undeclared attribute.")]
	public void ValidateBody_WhenDeclaredInputBindsUndeclaredAttribute_AbortsTheSave() {
		// Arrange
		PageUpdateTool tool = BuildTool(LiveMobileCatalog());
		PageUpdateOptions options = new() {
			SchemaName = "UsrTest_MobileFormPage",
			Body = MobileFieldBindingDeclaredInputsTests.Body(
				"""{"operation":"insert","name":"LeadList","values":{"type":"crt.List","visible":"$UsrMissing"}}""")
		};

		// Act
		(PageUpdateResponse failure, IReadOnlyList<string> _) = tool.ValidateBody(options, requestedVersion: null);

		// Assert
		failure.Should().NotBeNull(because: "visible is an inherited input the runtime reads, so its binding is checked");
		failure.Error.Should().Contain("UsrMissing", because: "the failure must name the undeclared attribute");
	}
}
