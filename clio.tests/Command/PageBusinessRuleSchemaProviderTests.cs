using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Command.BusinessRules;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class PageBusinessRuleSchemaProviderTests {
	[Test]
	[Category("Unit")]
	[Description("Loads a page schema hierarchy and returns the current schema identifier, parent identifier, and merged bundle.")]
	public void GetSchema_Should_Load_Hierarchy_And_Build_Bundle() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		IPageSchemaBodyParser bodyParser = Substitute.For<IPageSchemaBodyParser>();
		IPageBundleBuilder bundleBuilder = Substitute.For<IPageBundleBuilder>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://dev/DataService/json/SyncReply/SelectQuery");
		applicationClient.ExecutePostRequest(
				"http://dev/DataService/json/SyncReply/SelectQuery",
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("""{"success":true,"rows":[{"UId":"11111111-1111-1111-1111-111111111111"}]}""");
		hierarchyClient.GetParentSchemas(
				"11111111-1111-1111-1111-111111111111",
				"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
			.Returns([
				new PageDesignerHierarchySchema {
					UId = "22222222-2222-2222-2222-222222222222",
					Name = "UsrPage",
					Body = "current"
				},
				new PageDesignerHierarchySchema {
					UId = "33333333-3333-3333-3333-333333333333",
					Name = "BasePage",
					Body = "parent"
				}
			]);
		bodyParser.Parse("current").Returns(new PageParsedSchemaBody());
		bodyParser.Parse("parent").Returns(new PageParsedSchemaBody());
		PageBundleInfo expectedBundle = new() {
			Name = "UsrPage"
		};
		IReadOnlyList<PageSchemaBundlePart>? capturedParts = null;
		bundleBuilder.Build(Arg.Do<IReadOnlyList<PageSchemaBundlePart>>(parts => capturedParts = parts))
			.Returns(expectedBundle);
		PageBusinessRuleSchemaProvider provider = new(
			applicationClient,
			serviceUrlBuilder,
			hierarchyClient,
			bodyParser,
			bundleBuilder);

		// Act
		PageBusinessRuleSchemaContext result = provider.GetSchema(
			" UsrPage ",
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.SchemaUId.Should().Be("22222222-2222-2222-2222-222222222222",
			because: "the page add-on should target the current schema returned at the head of the hierarchy");
		result.ParentSchemaUId.Should().Be(Guid.Parse("33333333-3333-3333-3333-333333333333"),
			because: "the page add-on should target the direct parent schema identifier");
		result.Bundle.Should().BeSameAs(expectedBundle,
			because: "the provider should return the merged bundle built from the hierarchy parts");
		capturedParts.Should().NotBeNull().And.HaveCount(2,
			because: "each hierarchy schema with a body should be parsed and passed to the bundle builder");
		capturedParts![0].Schema.Name.Should().Be("UsrPage",
			because: "the current schema should stay first in the hierarchy parts passed to the bundle builder");
		bodyParser.Received(1).Parse("current");
		bodyParser.Received(1).Parse("parent");
	}

	[Test]
	[Category("Unit")]
	[Description("Uses a same-name page schema from the target package before falling back to root schema discovery.")]
	public void GetSchema_Should_Use_Existing_Schema_In_Target_Package() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		IPageSchemaBodyParser bodyParser = Substitute.For<IPageSchemaBodyParser>();
		IPageBundleBuilder bundleBuilder = Substitute.For<IPageBundleBuilder>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://dev/DataService/json/SyncReply/SelectQuery");
		applicationClient.ExecutePostRequest(
				"http://dev/DataService/json/SyncReply/SelectQuery",
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("""{"success":true,"rows":[{"UId":"44444444-4444-4444-4444-444444444444"}]}""");
		hierarchyClient.GetParentSchemas(
				"44444444-4444-4444-4444-444444444444",
				"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
			.Returns([
				new PageDesignerHierarchySchema {
					UId = "44444444-4444-4444-4444-444444444444",
					Name = "UsrPage",
					Body = "target-package-body"
				},
				new PageDesignerHierarchySchema {
					UId = "33333333-3333-3333-3333-333333333333",
					Name = "BasePage",
					Body = "parent"
				}
			]);
		bodyParser.Parse(Arg.Any<string>()).Returns(new PageParsedSchemaBody());
		bundleBuilder.Build(Arg.Any<IReadOnlyList<PageSchemaBundlePart>>()).Returns(new PageBundleInfo());
		PageBusinessRuleSchemaProvider provider = new(
			applicationClient,
			serviceUrlBuilder,
			hierarchyClient,
			bodyParser,
			bundleBuilder);

		// Act
		PageBusinessRuleSchemaContext result = provider.GetSchema(
			"UsrPage",
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.SchemaUId.Should().Be("44444444-4444-4444-4444-444444444444",
			because: "a replacing schema already present in the target package should be the add-on target");
		result.ParentSchemaUId.Should().Be(Guid.Parse("33333333-3333-3333-3333-333333333333"),
			because: "the add-on parent should come from the resolved target-package hierarchy");
		hierarchyClient.Received(1).GetParentSchemas(
			"44444444-4444-4444-4444-444444444444",
			"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
		hierarchyClient.DidNotReceive().GetDesignPackageUId(Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Fails closed when same-package page schema lookup fails before falling back to root schema discovery.")]
	public void GetSchema_Should_Fail_When_Target_Package_Lookup_Fails() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		IPageSchemaBodyParser bodyParser = Substitute.For<IPageSchemaBodyParser>();
		IPageBundleBuilder bundleBuilder = Substitute.For<IPageBundleBuilder>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://dev/DataService/json/SyncReply/SelectQuery");
		applicationClient.ExecutePostRequest(
				"http://dev/DataService/json/SyncReply/SelectQuery",
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("""{"success":false,"errorInfo":{"message":"access denied"}}""");
		PageBusinessRuleSchemaProvider provider = new(
			applicationClient,
			serviceUrlBuilder,
			hierarchyClient,
			bodyParser,
			bundleBuilder);

		// Act
		Action act = () => provider.GetSchema(
			"UsrPage",
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("Failed to query schema metadata in target package.",
				because: "destructive page rule creation should not choose another schema when target-package lookup fails");
		hierarchyClient.DidNotReceive().GetDesignPackageUId(Arg.Any<string>());
		hierarchyClient.DidNotReceive().GetParentSchemas(
			Arg.Any<string>(),
			Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Falls back through design-package root schema discovery before loading the target-package hierarchy.")]
	public void GetSchema_Should_Resolve_Root_Schema_Before_Target_Package_Hierarchy() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		IPageSchemaBodyParser bodyParser = Substitute.For<IPageSchemaBodyParser>();
		IPageBundleBuilder bundleBuilder = Substitute.For<IPageBundleBuilder>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://dev/DataService/json/SyncReply/SelectQuery");
		applicationClient.ExecutePostRequest(
				"http://dev/DataService/json/SyncReply/SelectQuery",
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(
				"""{"success":true,"rows":[]}""",
				"""{"success":true,"rows":[{"UId":"11111111-1111-1111-1111-111111111111","PackageUId":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"}]}""");
		hierarchyClient.GetDesignPackageUId("11111111-1111-1111-1111-111111111111")
			.Returns("cccccccc-cccc-cccc-cccc-cccccccccccc");
		hierarchyClient.GetParentSchemas(
				"11111111-1111-1111-1111-111111111111",
				"cccccccc-cccc-cccc-cccc-cccccccccccc")
			.Returns([
				new PageDesignerHierarchySchema {
					UId = "11111111-1111-1111-1111-111111111111",
					Name = "UsrPageReplacement",
					Body = "replacement"
				},
				new PageDesignerHierarchySchema {
					UId = "22222222-2222-2222-2222-222222222222",
					Name = "UsrPage",
					Body = "root"
				}
			]);
		hierarchyClient.GetParentSchemas(
				"22222222-2222-2222-2222-222222222222",
				"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
			.Returns([
				new PageDesignerHierarchySchema {
					UId = "55555555-5555-5555-5555-555555555555",
					Name = "UsrPage",
					Body = "target"
				},
				new PageDesignerHierarchySchema {
					UId = "22222222-2222-2222-2222-222222222222",
					Name = "UsrPage",
					Body = "root"
				}
			]);
		bodyParser.Parse(Arg.Any<string>()).Returns(new PageParsedSchemaBody());
		bundleBuilder.Build(Arg.Any<IReadOnlyList<PageSchemaBundlePart>>()).Returns(new PageBundleInfo());
		PageBusinessRuleSchemaProvider provider = new(
			applicationClient,
			serviceUrlBuilder,
			hierarchyClient,
			bodyParser,
			bundleBuilder);

		// Act
		PageBusinessRuleSchemaContext result = provider.GetSchema(
			"UsrPage",
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.SchemaUId.Should().Be("55555555-5555-5555-5555-555555555555",
			because: "the final add-on target should come from the target package hierarchy for the resolved root schema");
		hierarchyClient.Received(1).GetParentSchemas(
			"11111111-1111-1111-1111-111111111111",
			"cccccccc-cccc-cccc-cccc-cccccccccccc");
		hierarchyClient.Received(1).GetParentSchemas(
			"22222222-2222-2222-2222-222222222222",
			"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
	}

	[Test]
	[Category("Unit")]
	[Description("Fails closed when design-package resolution fails before choosing a target-package schema hierarchy.")]
	public void GetSchema_Should_Fail_When_Design_Package_Resolution_Fails() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		IPageSchemaBodyParser bodyParser = Substitute.For<IPageSchemaBodyParser>();
		IPageBundleBuilder bundleBuilder = Substitute.For<IPageBundleBuilder>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://dev/DataService/json/SyncReply/SelectQuery");
		applicationClient.ExecutePostRequest(
				"http://dev/DataService/json/SyncReply/SelectQuery",
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(
				"""{"success":true,"rows":[]}""",
				"""{"success":true,"rows":[{"UId":"11111111-1111-1111-1111-111111111111","PackageUId":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"}]}""");
		hierarchyClient.GetDesignPackageUId("11111111-1111-1111-1111-111111111111")
			.Throws(new InvalidOperationException("Failed to resolve design package: access denied"));
		PageBusinessRuleSchemaProvider provider = new(
			applicationClient,
			serviceUrlBuilder,
			hierarchyClient,
			bodyParser,
			bundleBuilder);

		// Act
		Action act = () => provider.GetSchema(
			"UsrPage",
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("Failed to resolve design package: access denied",
				because: "destructive page rule creation must fail closed instead of falling back to another schema package when design package lookup fails");
		hierarchyClient.DidNotReceive().GetParentSchemas(
			Arg.Any<string>(),
			Arg.Any<string>());
	}
}
