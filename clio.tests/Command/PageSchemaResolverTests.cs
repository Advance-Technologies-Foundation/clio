namespace Clio.Tests.Command;

using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageSchemaResolverTests {
	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private static readonly Guid PackageId = Guid.Parse("aa000000-0000-0000-0000-000000000001");

	private IApplicationClient _applicationClient = null!;
	private IServiceUrlBuilder _serviceUrlBuilder = null!;
	private IPageDesignerHierarchyClient _hierarchyClient = null!;
	private PageSchemaResolver _resolver = null!;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_resolver = new PageSchemaResolver(_applicationClient, _serviceUrlBuilder, _hierarchyClient);
	}

	private void StubSelectQuery(string response) =>
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(response);

	private void StubSelectQueue(params string[] responses) {
		Queue<string> queue = new(responses);
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(_ => queue.Dequeue());
	}

	[Test]
	[Description("Prefers the page variant DEFINED IN the target package (package-scoped lookup) and returns its effective designer hierarchy — so a page replaced across packages resolves deterministically for the addon's package rather than an arbitrary flat-query row.")]
	public void ResolveHierarchy_ShouldPreferPackageLocalVariant() {
		// Arrange — QueryExistingSchemaInPackage finds the page in the target package; its hierarchy is returned.
		const string packageLocalUId = "dd000000-0000-0000-0000-0000000000a1";
		const string effectiveUId = "dd000000-0000-0000-0000-0000000000ef";
		StubSelectQuery($$"""{"success": true, "rows": [{"UId": "{{packageLocalUId}}"}]}""");
		_hierarchyClient.GetParentSchemas(packageLocalUId, PackageId.ToString())
			.Returns(new[] { new PageDesignerHierarchySchema { UId = effectiveUId, Name = "UsrPage" } });

		// Act
		IReadOnlyList<PageDesignerHierarchySchema> hierarchy = _resolver.ResolveHierarchy("UsrPage", PackageId);

		// Assert
		hierarchy[0].UId.Should().Be(effectiveUId,
			because: "the package-local variant's effective hierarchy top is returned, not an arbitrary flat-query row");
		_hierarchyClient.Received(1).GetParentSchemas(packageLocalUId, PackageId.ToString());
	}

	[Test]
	[Description("Surfaces a clean lookup failure (not a raw JSON parse exception) when the page lookup returns a non-JSON body — e.g. an expired-session HTML redirect — because the resolver routes through the guarded ExecuteSelectQuery.")]
	public void ResolveHierarchy_ShouldFailCleanly_WhenLookupReturnsNonJson() {
		// Arrange — the SysSchema SelectQuery returns an HTML login page.
		StubSelectQuery("<!DOCTYPE html><html><body>login</body></html>");

		// Act
		Action act = () => _resolver.ResolveHierarchy("UsrPage", PackageId);

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*Failed to query schema metadata*",
			because: "a non-JSON response must surface as a clean lookup error, not a raw JObject.Parse exception");
	}

	[Test]
	[Description("When the page is NOT defined in the target package, resolves the ROOT schema (SysSchema by name → designer hierarchy → name-matching root) and returns its effective hierarchy for the package — the replacement-aware fallback that is the core of the #6 fix.")]
	public void ResolveHierarchy_ShouldResolveRootViaHierarchy_WhenNotInTargetPackage() {
		// Arrange
		const string rootUId = "dd000000-0000-0000-0000-0000000000b0";
		const string effectiveUId = "dd000000-0000-0000-0000-0000000000ef";
		const string designPackage = "dd000000-0000-0000-0000-0000000000d5";
		// 1) QueryExistingSchemaInPackage finds NO row in the target package; 2) QuerySysSchemaRow returns the
		// schema's UId + owning package.
		StubSelectQueue(
			"""{"success": true, "rows": []}""",
			$$"""{"success": true, "rows": [{"UId": "{{rootUId}}", "PackageUId": "{{designPackage}}"}]}""");
		_hierarchyClient.GetDesignPackageUId(rootUId).Returns(designPackage);
		// Root-finding hierarchy (in the design package): the name-matching entry is the root.
		_hierarchyClient.GetParentSchemas(rootUId, designPackage)
			.Returns(new[] { new PageDesignerHierarchySchema { UId = rootUId, Name = "UsrPage" } });
		// Effective hierarchy of that root as seen from the target package: element [0] is the effective schema.
		_hierarchyClient.GetParentSchemas(rootUId, PackageId.ToString())
			.Returns(new[] { new PageDesignerHierarchySchema { UId = effectiveUId, Name = "UsrPage" } });

		// Act
		IReadOnlyList<PageDesignerHierarchySchema> hierarchy = _resolver.ResolveHierarchy("UsrPage", PackageId);

		// Assert
		hierarchy[0].UId.Should().Be(effectiveUId,
			because: "the root variant resolved via SysSchema + the designer hierarchy yields the effective page for the package");
	}

	[Test]
	[Description("Throws a clear 'hierarchy is empty' error when the designer returns no hierarchy for the resolved schema.")]
	public void ResolveHierarchy_ShouldThrow_WhenHierarchyEmpty() {
		// Arrange — the page is found in the target package, but its designer hierarchy comes back empty.
		const string packageLocalUId = "dd000000-0000-0000-0000-0000000000a1";
		StubSelectQuery($$"""{"success": true, "rows": [{"UId": "{{packageLocalUId}}"}]}""");
		_hierarchyClient.GetParentSchemas(packageLocalUId, PackageId.ToString())
			.Returns(Array.Empty<PageDesignerHierarchySchema>());

		// Act
		Action act = () => _resolver.ResolveHierarchy("UsrPage", PackageId);

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*hierarchy is empty*",
			because: "an empty designer hierarchy is a resolution failure and must be surfaced clearly");
	}

	[Test]
	[Description("Surfaces a clear not-found error when the page is neither in the target package nor resolvable as a root SysSchema row.")]
	public void ResolveHierarchy_ShouldThrow_WhenRootSchemaNotFound() {
		// Arrange — QueryExistingSchemaInPackage finds nothing, and the root SysSchema lookup also returns no row.
		StubSelectQueue(
			"""{"success": true, "rows": []}""",
			"""{"success": true, "rows": []}""");

		// Act
		Action act = () => _resolver.ResolveHierarchy("UsrPage", PackageId);

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*not found*",
			because: "when neither the package-local nor the root lookup finds the schema, resolution must fail clearly");
	}
}
