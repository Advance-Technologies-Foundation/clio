using System.Collections.Generic;
using Clio.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageHierarchyChainTests {
	private const string PageName = "UsrOrders_FormPage";
	private const string DesignPackage = "design-pkg";
	private const string LeafUId = "leaf-uid";
	private const string BaseUId = "base-uid";

	private static readonly List<PageDesignerHierarchySchema> AnchorChain = [
		new() { UId = LeafUId, Name = PageName, PackageUId = DesignPackage },
		new() { UId = BaseUId, Name = PageName, PackageUId = "app-pkg" },
		new() { UId = "template-uid", Name = "BaseTemplate", PackageUId = "vendor-pkg" }
	];

	[Test]
	[Description("FindRootSchemaUId returns the LOWEST chain entry carrying the page name, not the template at the chain root.")]
	public void FindRootSchemaUId_Should_Return_The_Lowest_Entry_With_The_Page_Name() {
		// Act
		string rootUId = PageHierarchyChain.FindRootSchemaUId(AnchorChain, PageName.ToUpperInvariant());

		// Assert
		rootUId.Should().Be(BaseUId, because: "the base page sits above the template and the name match ignores case");
	}

	[Test]
	[Description("A chain already read from the root variant is reused without a second designer read.")]
	public void ReadFromRootVariant_Should_Reuse_The_Anchor_Chain_When_It_Was_Read_From_The_Root() {
		// Arrange
		IPageDesignerHierarchyClient client = Substitute.For<IPageDesignerHierarchyClient>();

		// Act
		IReadOnlyList<PageDesignerHierarchySchema> chain =
			PageHierarchyChain.ReadFromRootVariant(client, PageName, BaseUId, DesignPackage, AnchorChain);

		// Assert
		chain.Should().BeSameAs(AnchorChain, because: "the anchor is the root variant already");
		client.DidNotReceiveWithAnyArgs().GetParentSchemas(default, default);
	}

	[Test]
	[Description("A chain that does not carry the page name is returned as is: there is no root variant to re-read from.")]
	public void ReadFromRootVariant_Should_Reuse_The_Anchor_Chain_When_The_Page_Name_Is_Missing() {
		// Arrange
		IPageDesignerHierarchyClient client = Substitute.For<IPageDesignerHierarchyClient>();

		// Act
		IReadOnlyList<PageDesignerHierarchySchema> chain =
			PageHierarchyChain.ReadFromRootVariant(client, "Other_FormPage", LeafUId, DesignPackage, AnchorChain);

		// Assert
		chain.Should().BeSameAs(AnchorChain, because: "nothing names the page, so nothing can be re-read");
		client.DidNotReceiveWithAnyArgs().GetParentSchemas(default, default);
	}

	[Test]
	[Description("A chain read from a replacing variant is re-read from the root variant for the same design package.")]
	public void ReadFromRootVariant_Should_Reread_From_The_Root_Variant_When_Anchored_Elsewhere() {
		// Arrange
		List<PageDesignerHierarchySchema> fullChain = [.. AnchorChain];
		IPageDesignerHierarchyClient client = Substitute.For<IPageDesignerHierarchyClient>();
		client.GetParentSchemas(BaseUId, DesignPackage).Returns(fullChain);

		// Act
		IReadOnlyList<PageDesignerHierarchySchema> chain =
			PageHierarchyChain.ReadFromRootVariant(client, PageName, LeafUId, DesignPackage, AnchorChain);

		// Assert
		chain.Should().BeSameAs(fullChain, because: "a chain read from a replacing variant can miss layers");
	}

	[Test]
	[Description("An empty or null re-read falls back to the anchor chain instead of an empty chain.")]
	[TestCase(true)]
	[TestCase(false)]
	public void ReadFromRootVariant_Should_Fall_Back_To_The_Anchor_Chain_When_The_Reread_Is_Empty(bool returnNull) {
		// Arrange
		IPageDesignerHierarchyClient client = Substitute.For<IPageDesignerHierarchyClient>();
		client.GetParentSchemas(BaseUId, DesignPackage)
			.Returns(returnNull ? null : new List<PageDesignerHierarchySchema>());

		// Act
		IReadOnlyList<PageDesignerHierarchySchema> chain =
			PageHierarchyChain.ReadFromRootVariant(client, PageName, LeafUId, DesignPackage, AnchorChain);

		// Assert
		chain.Should().BeSameAs(AnchorChain, because: "an empty re-read carries no chain to use");
	}

	[Test]
	[Description("A failed re-read propagates, so each caller reports it (update-page fails closed).")]
	public void ReadFromRootVariant_Should_Propagate_A_Failed_Reread() {
		// Arrange
		IPageDesignerHierarchyClient client = Substitute.For<IPageDesignerHierarchyClient>();
		client.GetParentSchemas(BaseUId, DesignPackage)
			.Returns(_ => throw new System.InvalidOperationException("designer service unavailable"));

		// Act
		System.Action act = () =>
			PageHierarchyChain.ReadFromRootVariant(client, PageName, LeafUId, DesignPackage, AnchorChain);

		// Assert
		act.Should().Throw<System.InvalidOperationException>(because: "the caller decides how a failed read is reported");
	}
}
