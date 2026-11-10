namespace Clio.Tests.Command.PageModalSize;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.PageModalSize;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class AppearanceSettingsLayerLocatorTests {
	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private static readonly Guid PageUId = Guid.Parse("cc000000-0000-0000-0000-00000000000a");

	private IApplicationClient _applicationClient;
	private IAddonSchemaDesignerClient _addonClient;
	private AppearanceSettingsLayerLocator _locator;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_addonClient = Substitute.For<IAddonSchemaDesignerClient>();
		_locator = new AppearanceSettingsLayerLocator(_applicationClient, serviceUrlBuilder, _addonClient);
	}

	private static string PackageUId(string packageName) =>
		new Guid(packageName.PadRight(16, '_').Take(16).Select(character => (byte)character).ToArray()).ToString();

	// The layers of the page add-on, and the package dependencies as "package>dependency" pairs.
	private void StubEnvironment(string[] layerPackages, params string[] dependencies) {
		string layers = string.Join(",", layerPackages.Select(name =>
			$$"""{"PackageName": "{{name}}", "PackageUId": "{{PackageUId(name)}}"}"""));
		string edges = string.Join(",", dependencies.Select(edge => edge.Split('>')).Select(edge =>
			$$"""{"PackageName": "{{edge[0]}}", "DependsOnPackageName": "{{edge[1]}}"}"""));
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Is<string>(body => body.Contains("SysSchema")))
			.Returns($$"""{"success": true, "rows": [{{layers}}]}""");
		_applicationClient.ExecutePostRequest(SelectQueryUrl,
				Arg.Is<string>(body => body.Contains("SysPackageDependency")))
			.Returns($$"""{"success": true, "rows": [{{edges}}]}""");
	}

	// The size each package sees on its own (useFullHierarchy=false).
	private void StubOwnSize(string packageName, int? size) =>
		_addonClient.GetSchema(Arg.Is<AddonGetRequestDto>(request =>
				request.TargetPackageUId == Guid.Parse(PackageUId(packageName)) && !request.UseFullHierarchy))
			.Returns(new AddonSchemaDto {
				MetaData = size is null ? "{}" : "{\"modalSettings\": {\"size\": " + size + "}}"
			});

	[Test]
	[Description("Names the layer no other layer's package depends on, without reading any layer's size.")]
	public void FindSource_ShouldNameTopLayer_WhenOneLayerIsOnTop() {
		// Arrange
		StubEnvironment(["CrtUIv2", "UsrA", "UsrB"], "UsrA>CrtUIv2", "UsrB>UsrA");

		// Act
		AppearanceSettingsLayerSource source = _locator.FindSource("EmailMiniPage", PageUId, null, 4);

		// Assert
		source.PackageName.Should().Be("UsrB", because: "UsrB depends on every other layer's package");
		source.Warning.Should().BeNull(because: "the package was found");
		_addonClient.DidNotReceive().GetSchema(Arg.Any<AddonGetRequestDto>());
	}

	[Test]
	[Description("Picks among parallel top layers the one whose own view holds the size (measured on 10.2.760).")]
	public void FindSource_ShouldPickParallelLayerHoldingSize_WhenSeveralLayersAreOnTop() {
		// Arrange
		StubEnvironment(["CrtUIv2", "UsrA", "UsrB", "UsrC"], "UsrA>CrtUIv2", "UsrB>UsrA", "UsrC>CrtUIv2");
		StubOwnSize("UsrB", 4);
		StubOwnSize("UsrC", 3);

		// Act
		AppearanceSettingsLayerSource source = _locator.FindSource("EmailMiniPage", PageUId, null, 4);

		// Assert
		source.PackageName.Should().Be("UsrB", because: "only UsrB's own view holds the size the window opens with");
		source.CandidatePackageNames.Should().BeEmpty(because: "the package was told apart");
	}

	[Test]
	[Description("Lists the parallel candidates and names none when their own views do not tell them apart.")]
	public void FindSource_ShouldListCandidates_WhenParallelLayersCannotBeToldApart() {
		// Arrange
		StubEnvironment(["CrtUIv2", "UsrZ", "UsrB"], "UsrZ>CrtUIv2", "UsrB>CrtUIv2");
		StubOwnSize("UsrZ", 4);
		StubOwnSize("UsrB", 4);

		// Act
		AppearanceSettingsLayerSource source = _locator.FindSource("EmailMiniPage", PageUId, null, 4);

		// Assert
		source.PackageName.Should().BeNull(because: "two parallel layers hold the same size and none may be guessed");
		source.CandidatePackageNames.Should().Equal(["UsrB", "UsrZ"], because: "both candidates are named, sorted");
		source.Warning.Should().Contain("parallel packages 'UsrB', 'UsrZ'", because: "the caller learns why");
	}

	[Test]
	[Description("Only considers the layers the given package sees: itself and its dependencies, directly or not.")]
	public void FindSource_ShouldStayInsideVisiblePackages_WhenVisibleFromPackageIsGiven() {
		// Arrange
		StubEnvironment(["CrtUIv2", "UsrA", "UsrB"], "UsrA>CrtUIv2", "UsrB>UsrA", "UsrD>UsrA");

		// Act
		AppearanceSettingsLayerSource source = _locator.FindSource("EmailMiniPage", PageUId, "UsrD", 3);

		// Assert
		source.PackageName.Should().Be("UsrA",
			because: "UsrD sees UsrA and CrtUIv2 only; UsrB's layer is above it and not part of its view");
	}

	[Test]
	[Description("Treats None (0) and a missing size as the same 'no size' when matching parallel layers.")]
	public void FindSource_ShouldMatchNoneAsNoSize_WhenSizeIsMissing() {
		// Arrange
		StubEnvironment(["CrtUIv2", "UsrA", "UsrC"], "UsrA>CrtUIv2", "UsrC>CrtUIv2");
		StubOwnSize("UsrA", 0);
		StubOwnSize("UsrC", 2);

		// Act
		AppearanceSettingsLayerSource source = _locator.FindSource("EmailMiniPage", PageUId, null, null);

		// Assert
		source.PackageName.Should().Be("UsrA", because: "a layer that stores None sets 'no size'");
	}

	[Test]
	[Description("Reports a failed layer query as a warning, never as an exception or a guessed package.")]
	public void FindSource_ShouldWarn_WhenLayerQueryFails() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns("""{"success": false}""");

		// Act
		AppearanceSettingsLayerSource source = _locator.FindSource("EmailMiniPage", PageUId, null, 4);

		// Assert
		source.PackageName.Should().BeNull(because: "nothing could be read");
		source.Warning.Should().Contain("Failed to query add-on schema layers",
			because: "the failed layer query is reported, not mistaken for 'no layer'");
	}

	[Test]
	[Description("Reports a failed package dependency query as a warning, never as a guessed package.")]
	public void FindSource_ShouldWarn_WhenDependencyQueryFails() {
		// Arrange
		StubEnvironment(["CrtUIv2", "UsrA"], "UsrA>CrtUIv2");
		_applicationClient.ExecutePostRequest(SelectQueryUrl,
				Arg.Is<string>(body => body.Contains("SysPackageDependency")))
			.Returns("""{"success": false}""");

		// Act
		AppearanceSettingsLayerSource source = _locator.FindSource("EmailMiniPage", PageUId, null, 4);

		// Assert
		source.PackageName.Should().BeNull(because: "without dependencies no layer can be put on top");
		source.Warning.Should().Contain("Failed to query package dependencies",
			because: "the failed dependency query is reported");
	}

	[Test]
	[Description("Turns a failed add-on read of a parallel layer into a warning instead of an exception.")]
	public void FindSource_ShouldWarn_WhenOwnViewReadThrows() {
		// Arrange
		StubEnvironment(["CrtUIv2", "UsrB", "UsrC"], "UsrB>CrtUIv2", "UsrC>CrtUIv2");
		_addonClient.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(_ => throw new System.Net.Http.HttpRequestException("timeout"));

		// Act
		AppearanceSettingsLayerSource source = _locator.FindSource("EmailMiniPage", PageUId, null, 4);

		// Assert
		source.PackageName.Should().BeNull(because: "the parallel layers could not be told apart");
		source.Warning.Should().Contain("timeout",
			because: "the lookup only decorates a read or a refusal and must never fail the caller");
	}

	[Test]
	[Description("Lists every parallel top layer when no own view holds the size.")]
	public void FindSource_ShouldListAllTops_WhenNoParallelLayerHoldsSize() {
		// Arrange
		StubEnvironment(["CrtUIv2", "UsrB", "UsrC"], "UsrB>CrtUIv2", "UsrC>CrtUIv2");
		StubOwnSize("UsrB", 2);
		StubOwnSize("UsrC", 3);

		// Act
		AppearanceSettingsLayerSource source = _locator.FindSource("EmailMiniPage", PageUId, null, 4);

		// Assert
		source.PackageName.Should().BeNull(because: "no top layer holds the size the window opens with");
		source.CandidatePackageNames.Should().Equal(["UsrB", "UsrC"], because: "every top layer is a candidate");
	}

	[Test]
	[Description("Reads the layers and the dependencies once per instance, however many lookups follow.")]
	public void FindSource_ShouldQueryEnvironmentOnce_WhenCalledTwice() {
		// Arrange
		StubEnvironment(["CrtUIv2", "UsrA"], "UsrA>CrtUIv2");

		// Act
		_locator.FindSource("EmailMiniPage", PageUId, null, 4);
		_locator.FindSource("EmailMiniPage", PageUId, "UsrA", 3);

		// Assert
		_applicationClient.Received(2).ExecutePostRequest(SelectQueryUrl, Arg.Any<string>());
	}

	[Test]
	[Description("Sees a layer in the package itself or in a package it depends on, directly or not.")]
	public void Sees_ShouldFollowDependencies() {
		// Arrange
		StubEnvironment(["CrtUIv2", "UsrA", "UsrB"], "UsrA>CrtUIv2", "UsrB>UsrA");

		// Act
		bool seesIndirect = _locator.Sees("EmailMiniPage", "UsrB", "CrtUIv2");
		bool seesItself = _locator.Sees("EmailMiniPage", "UsrB", "UsrB");
		bool seesAbove = _locator.Sees("EmailMiniPage", "UsrA", "UsrB");

		// Assert
		seesIndirect.Should().BeTrue(because: "UsrB depends on CrtUIv2 through UsrA");
		seesItself.Should().BeTrue(because: "a package sees its own layer");
		seesAbove.Should().BeFalse(because: "UsrA does not depend on UsrB");
	}

	[Test]
	[Description("Asks for the add-on's own layers by name and manager.")]
	public void FindSource_ShouldQueryAddonLayersByName() {
		// Arrange
		StubEnvironment(["CrtUIv2"]);

		// Act
		_locator.FindSource("EmailMiniPage", PageUId, null, 3);

		// Assert
		_applicationClient.Received().ExecutePostRequest(SelectQueryUrl, Arg.Is<string>(body =>
			body.Contains("EmailMiniPageAppearanceSettings") && body.Contains("AddonSchemaManager")));
	}
}
