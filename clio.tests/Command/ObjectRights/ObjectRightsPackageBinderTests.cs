using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.ObjectRights;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ObjectRights;

/// <summary>
/// <see cref="ObjectRightsPackageBinder"/>: which package the object's operation permission rows are bound to, the
/// refusals before any write, the binding found elsewhere, and what the binding write sends. DataService is mocked
/// per queried schema; the binding writer and the package resolver are substitutes, so these tests pin the decisions,
/// not the SaveSchema payload (PackageDataBindingWriter's own tests do that).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class ObjectRightsPackageBinderTests {

	private const string SelectUrl = "http://host/0/DataService/json/SyncReply/SelectQuery";
	private static readonly Guid SchemaUId = Guid.Parse("35a9057f-800d-414b-acfb-c919c215857a");
	private static readonly Guid AppPackageUId = Guid.Parse("5a1c4f3e-0000-4000-8000-000000000001");
	private static readonly Guid RowA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
	private static readonly Guid RowB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
	private static readonly Guid OtherBindingUId = Guid.Parse("cccccccc-0000-4000-8000-000000000003");

	private IApplicationClient _client;
	private IPackageDataBindingWriter _writer;
	private IPackageTargetResolver _resolver;
	private ObjectRightsPackageBinder _binder;

	[SetUp]
	public void SetUp() {
		_client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		_writer = Substitute.For<IPackageDataBindingWriter>();
		_writer.FetchBoundRows(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>()).Returns(new List<Dictionary<string, JsonNode>>());
		_resolver = Substitute.For<IPackageTargetResolver>();
		_resolver.Resolve(Arg.Any<string>(), Arg.Any<bool>())
			.Returns(call => PackageTargetResolution.Resolved((string)call[0] ?? "Custom", AppPackageUId));
		SchemaIn(("UsrApp", false));
		StoredRows(RowA, RowB);
		Bindings();
		EntitySchemas();
		_binder = new ObjectRightsPackageBinder(_client, urlBuilder, _writer, _resolver);
	}

	private void Select(string schema, string json, bool inFilter = false) =>
		_client.ExecutePostRequest(SelectUrl, Arg.Is<string>(body => body.Contains($"\"rootSchemaName\":\"{schema}\"")
				&& body.Contains("rightExpressions") == inFilter),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns(json);

	private void SchemaIn(params (string Package, bool ExtendParent)[] layers) =>
		Select("SysSchema", "{\"success\":true,\"rows\":[" + string.Join(",", layers.Select(layer =>
			$"{{\"ExtendParent\":{layer.ExtendParent.ToString().ToLowerInvariant()},\"PackageName\":\"{layer.Package}\"}}"))
			+ "]}");

	// The schemas the binding-name suffixes are looked up as: entity schemas exist under these names.
	private void EntitySchemas(params string[] names) =>
		Select("SysSchema", "{\"success\":true,\"rows\":[" + string.Join(",", names.Select(name =>
			$"{{\"Name\":\"{name}\",\"ManagerName\":\"EntitySchemaManager\"}}")) + "]}", inFilter: true);

	private void StoredRows(params Guid[] ids) =>
		Select(ObjectRightsPackageBinder.RightsSchema, "{\"success\":true,\"rows\":["
			+ string.Join(",", ids.Select((id, i) => $"{{\"Position\":{i},\"Id\":\"{id}\"}}")) + "]}");

	private void Bindings(params (Guid UId, string Name, string Package)[] bindings) =>
		Select("SysPackageSchemaData", "{\"success\":true,\"rows\":[" + string.Join(",", bindings.Select(b =>
			$"{{\"UId\":\"{b.UId}\",\"Name\":\"{b.Name}\",\"PackageName\":\"{b.Package}\"}}")) + "]}");

	private static ObjectRightsBindingTarget Target(ExistingObjectRightsBinding existing = null) =>
		new("UsrApp", AppPackageUId, "SysEntitySchemaOperationRight_UsrFoo", ["UsrApp"], existing);

	[Test]
	[Description("Without a package argument the rows are bound to the package of the object's base schema, under SysEntitySchemaOperationRight_<code>, and the switch is in the same package.")]
	public void ResolveTarget_ShouldUseBaseSchemaPackage_WhenNoPackageNamed() {
		// Arrange
		SchemaIn(("UsrApp", false), ("Custom", true));

		// Act
		ObjectRightsBindingTarget target = _binder.ResolveTarget("UsrFoo", SchemaUId, null, null);

		// Assert
		target.Refused.Should().BeFalse(because: "the base schema's package is editable");
		target.PackageName.Should().Be("UsrApp", because: "the default is the package of the object's base schema");
		target.BindingName.Should().Be("SysEntitySchemaOperationRight_UsrFoo",
			because: "one binding per object, named after the rights table and the object's code");
		target.SchemaPackages.Should().Equal(new[] { "UsrApp", "Custom" },
			because: "every package with a layer of the object's schema is reported, the base one first");
		target.SwitchInSamePackage.Should().BeTrue(because: "the binding package holds the object's schema");
		_resolver.Received(1).Resolve("UsrApp", true);
	}

	[Test]
	[Description("A named package wins over the schema's package; the switch is then reported as being elsewhere.")]
	public void ResolveTarget_ShouldUseNamedPackage_WhenPackageNamed() {
		// Act
		ObjectRightsBindingTarget target = _binder.ResolveTarget("UsrFoo", SchemaUId, "UsrAppRights", null);

		// Assert
		target.PackageName.Should().Be("UsrAppRights", because: "the package the caller named receives the binding");
		target.SwitchInSamePackage.Should().BeFalse(because: "the object's schema is in UsrApp, not in UsrAppRights");
		_resolver.Received(1).Resolve("UsrAppRights", true);
	}

	[Test]
	[Description("A locked package is refused before any write, and the refusal names the CurrentPackageId package as one to use.")]
	public void ResolveTarget_ShouldRefuseAndNameUsablePackage_WhenPackageLocked() {
		// Arrange
		_resolver.Resolve("CrtBase", true).Returns(PackageTargetResolution.Unresolvable("Package 'CrtBase' is locked."));
		_resolver.Resolve(null, true).Returns(PackageTargetResolution.Resolved("Custom", AppPackageUId));

		// Act
		ObjectRightsBindingTarget target = _binder.ResolveTarget("UsrFoo", SchemaUId, "CrtBase", null);

		// Assert
		target.Refused.Should().BeTrue(because: "a locked package cannot receive the binding");
		target.Refusal.Should().Contain("locked", because: "the reason comes from the package resolver")
			.And.Contain("package='Custom'", because: "the refusal names a package the caller can use")
			.And.Contain("Nothing was changed", because: "the refusal comes before any write");
	}

	[Test]
	[Description("An object whose own package is locked (a product object) is refused, naming that package.")]
	public void ResolveTarget_ShouldRefuse_WhenSchemaPackageLocked() {
		// Arrange
		SchemaIn(("CrtBase", false));
		_resolver.Resolve("CrtBase", true).Returns(PackageTargetResolution.Unresolvable("Package 'CrtBase' is locked."));

		// Act
		ObjectRightsBindingTarget target = _binder.ResolveTarget("Contact", SchemaUId, null, null);

		// Assert
		target.Refused.Should().BeTrue(because: "the default package is locked");
		target.Refusal.Should().Contain("'Contact' is in package 'CrtBase'",
			because: "the caller learns why the default package does not work");
	}

	[Test]
	[Description("Rows of this object bound in another editable package are reported as the existing binding.")]
	public void ResolveTarget_ShouldReportExistingBinding_WhenRowsBoundElsewhere() {
		// Arrange
		Bindings((OtherBindingUId, "ManualRights", "Custom"));
		_writer.FetchBoundRows(OtherBindingUId, Arg.Any<CreatioRequestOptions>()).Returns(new List<Dictionary<string, JsonNode>> {
			new() { ["Id"] = JsonValue.Create(RowB.ToString()) }
		});

		// Act
		ObjectRightsBindingTarget target = _binder.ResolveTarget("UsrFoo", SchemaUId, null, null);

		// Assert
		target.Existing.Should().Be(new ExistingObjectRightsBinding("ManualRights", "Custom"),
			because: "a binding that holds one of the object's rows already ships them");
	}

	[Test]
	[Description("A binding of another object's rows is not reported, and the call's own binding is not read.")]
	public void ResolveTarget_ShouldIgnoreOwnAndUnrelatedBindings() {
		// Arrange
		Guid own = Guid.NewGuid();
		Bindings((own, "SysEntitySchemaOperationRight_UsrFoo", "UsrApp"), (OtherBindingUId, "Other", "Custom"));
		_writer.FetchBoundRows(OtherBindingUId, Arg.Any<CreatioRequestOptions>()).Returns(new List<Dictionary<string, JsonNode>> {
			new() {
				["Id"] = JsonValue.Create(Guid.NewGuid().ToString()),
				["SubjectSchemaUId"] = JsonValue.Create(Guid.NewGuid().ToString())
			}
		});

		// Act
		ObjectRightsBindingTarget target = _binder.ResolveTarget("UsrFoo", SchemaUId, null, null);

		// Assert
		target.Existing.Should().BeNull(because: "neither binding holds a row of this object elsewhere");
		_writer.DidNotReceive().FetchBoundRows(own, Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("A failed read refuses the target instead of throwing, so nothing is written.")]
	public void ResolveTarget_ShouldRefuse_WhenReadFails() {
		// Arrange
		Select("SysSchema", "{\"success\":false,\"errorInfo\":{\"message\":\"boom\"}}");

		// Act
		ObjectRightsBindingTarget target = _binder.ResolveTarget("UsrFoo", SchemaUId, null, null);

		// Assert
		target.Refused.Should().BeTrue(because: "without the package nothing may be written");
		target.Refusal.Should().Contain("boom", because: "the refusal carries the server's reason");
	}

	[Test]
	[Description("Bind saves the object's stored rows in priority order, keyed on Id and force-updating the operations and the position.")]
	public void Bind_ShouldSaveStoredRowsInOrder_WithForceUpdatedOperations() {
		// Arrange
		Guid existing = Guid.NewGuid();
		_writer.FindBinding(AppPackageUId, "SysEntitySchemaOperationRight_UsrFoo")
			.Returns(new PackageDataBindingRef(existing, "SysEntitySchemaOperationRight"));

		// Act
		ObjectRightsBindingResult result = _binder.Bind(Target(), "UsrFoo", SchemaUId);

		// Assert
		result.Should().Be(new ObjectRightsBindingResult(ObjectRightsBindingStatus.Written, 2),
			because: "both stored rows are bound");
		_writer.Received(1).ProjectSchema("SysEntitySchemaOperationRight", ObjectRightsPackageBinder.BoundColumns);
		_writer.Received(1).SaveBinding(Arg.Is<PackageRef>(p => p.Name == "UsrApp"),
			"SysEntitySchemaOperationRight_UsrFoo", "SysEntitySchemaOperationRight", Arg.Any<DataBindingDbSchema>(),
			Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { RowA.ToString(), RowB.ToString() })),
			existing,
			Arg.Is<DataBindingColumnPolicy>(policy => policy.KeyColumns.SequenceEqual(new[] { "Id" })
				&& policy.ForceUpdateColumns.Contains("CanRead") && policy.ForceUpdateColumns.Contains("Position")));
	}

	[Test]
	[Description("An object with no stored rows removes its existing binding instead of saving an empty one.")]
	public void Bind_ShouldRemoveBinding_WhenNoStoredRows() {
		// Arrange
		StoredRows();
		_writer.FindBinding(AppPackageUId, "SysEntitySchemaOperationRight_UsrFoo")
			.Returns(new PackageDataBindingRef(Guid.NewGuid(), "SysEntitySchemaOperationRight"));

		// Act
		ObjectRightsBindingResult result = _binder.Bind(Target(), "UsrFoo", SchemaUId);

		// Assert
		result.Status.Should().Be(ObjectRightsBindingStatus.Removed, because: "the binding no longer has rows to ship");
		_writer.Received(1).DeleteBinding(Arg.Is<PackageRef>(p => p.Name == "UsrApp"),
			"SysEntitySchemaOperationRight_UsrFoo");
		_writer.DidNotReceiveWithAnyArgs().SaveBinding(default, default, default, default, default, default, default);
	}

	[Test]
	[Description("An object with no stored rows and no binding writes nothing.")]
	public void Bind_ShouldWriteNothing_WhenNoRowsAndNoBinding() {
		// Arrange
		StoredRows();

		// Act
		ObjectRightsBindingResult result = _binder.Bind(Target(), "UsrFoo", SchemaUId);

		// Assert
		result.Status.Should().Be(ObjectRightsBindingStatus.NothingToBind, because: "there is nothing to bind or remove");
		_writer.DidNotReceiveWithAnyArgs().DeleteBinding(default, default);
	}

	[Test]
	[Description("When the rows are bound elsewhere, Bind writes nothing.")]
	public void Bind_ShouldWriteNothing_WhenBoundElsewhere() {
		// Act
		ObjectRightsBindingResult result =
			_binder.Bind(Target(new ExistingObjectRightsBinding("Manual", "Custom")), "UsrFoo", SchemaUId);

		// Assert
		result.Status.Should().Be(ObjectRightsBindingStatus.LeftToExisting, because: "no second binding is written");
		_writer.DidNotReceiveWithAnyArgs().SaveBinding(default, default, default, default, default, default, default);
	}

	[Test]
	[Description("A same-name binding that delivers another entity is left alone and reported as a failure.")]
	public void Bind_ShouldFail_WhenNameTakenByAnotherEntity() {
		// Arrange
		_writer.FindBinding(AppPackageUId, "SysEntitySchemaOperationRight_UsrFoo")
			.Returns(new PackageDataBindingRef(Guid.NewGuid(), "Contact"));

		// Act
		ObjectRightsBindingResult result = _binder.Bind(Target(), "UsrFoo", SchemaUId);

		// Assert
		result.Status.Should().Be(ObjectRightsBindingStatus.Failed, because: "the name belongs to another binding");
		result.Error.Should().Contain("'Contact'", because: "the error names what the binding delivers");
		_writer.DidNotReceiveWithAnyArgs().SaveBinding(default, default, default, default, default, default, default);
	}

	[Test]
	[Description("A rejected save is returned as a failure, not thrown.")]
	public void Bind_ShouldReturnFailure_WhenSaveRejected() {
		// Arrange
		_writer.When(w => w.SaveBinding(Arg.Any<PackageRef>(), Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<DataBindingDbSchema>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<Guid?>(),
				Arg.Any<DataBindingColumnPolicy>()))
			.Do(_ => throw new InvalidOperationException("SaveSchema failed"));

		// Act
		ObjectRightsBindingResult result = _binder.Bind(Target(), "UsrFoo", SchemaUId);

		// Assert
		result.Status.Should().Be(ObjectRightsBindingStatus.Failed, because: "the save was rejected");
		result.Error.Should().Contain("SaveSchema failed", because: "the reason reaches the caller");
	}

	[Test]
	[Description("Without a package argument, the object's own binding written earlier into another package is updated there instead of a second copy being written.")]
	public void ResolveTarget_ShouldKeepOwnBindingPackage_WhenOwnBindingIsElsewhere() {
		// Arrange
		Bindings((OtherBindingUId, "SysEntitySchemaOperationRight_UsrFoo", "UsrAppRights"));

		// Act
		ObjectRightsBindingTarget target = _binder.ResolveTarget("UsrFoo", SchemaUId, null, null);

		// Assert
		target.PackageName.Should().Be("UsrAppRights", because: "the object's binding already lives in UsrAppRights");
		target.Existing.Should().BeNull(because: "the binding is the target, not a second binding elsewhere");
		_resolver.Received(1).Resolve("UsrAppRights", true);
	}

	[Test]
	[Description("With a package argument other than the one holding the object's own binding, the call is refused before any write, naming that package, so the binding never keeps the previous rows.")]
	public void ResolveTarget_ShouldRefuse_WhenOwnBindingIsInAnotherPackageThanTheNamedOne() {
		// Arrange
		Bindings((OtherBindingUId, "SysEntitySchemaOperationRight_UsrFoo", "UsrAppRights"));

		// Act
		ObjectRightsBindingTarget target = _binder.ResolveTarget("UsrFoo", SchemaUId, "UsrOther", null);

		// Assert
		target.Refused.Should().BeTrue(because: "saving the rows would leave the binding in UsrAppRights with the previous rows");
		target.Refusal.Should().Contain("package='UsrAppRights'", because: "the caller learns which package to name instead");
		target.Refusal.Should().Contain("Nothing was changed", because: "the refusal comes before any write");
	}

	[Test]
	[Description("Naming the package that already holds the object's own binding is not a refusal: the binding is updated there.")]
	public void ResolveTarget_ShouldTargetOwnBinding_WhenItsPackageIsNamed() {
		// Arrange
		Bindings((OtherBindingUId, "SysEntitySchemaOperationRight_UsrFoo", "UsrAppRights"));

		// Act
		ObjectRightsBindingTarget target = _binder.ResolveTarget("UsrFoo", SchemaUId, "UsrAppRights", null);

		// Assert
		target.Refused.Should().BeFalse(because: $"the named package holds the binding. Refusal: {target.Refusal}");
		target.PackageName.Should().Be("UsrAppRights", because: "the named package receives the binding");
		target.Existing.Should().BeNull(because: "the binding is the target, not a second binding elsewhere");
	}

	[Test]
	[Description("Once the target package holds the object's own binding, the bindings made by hand are not read again.")]
	public void ResolveTarget_ShouldSkipHandMadeScan_WhenTargetHoldsOwnBinding() {
		// Arrange
		Bindings((Guid.NewGuid(), "SysEntitySchemaOperationRight_UsrFoo", "UsrApp"), (OtherBindingUId, "ManualRights", "Custom"));

		// Act
		ObjectRightsBindingTarget target = _binder.ResolveTarget("UsrFoo", SchemaUId, null, null);

		// Assert
		target.Existing.Should().BeNull(because: "the first bind into UsrApp already checked the bindings made by hand");
		_writer.DidNotReceiveWithAnyArgs().FetchBoundRows(default, default);
	}

	[Test]
	[Description("The hand-made binding reads share the call's request options, so they are bounded by its budget.")]
	public void ResolveTarget_ShouldPassRequestOptionsToBoundRowReads() {
		// Arrange
		CreatioRequestOptions options = new() { TimeOut = 5_000 };
		Bindings((OtherBindingUId, "ManualRights", "Custom"));

		// Act
		_binder.ResolveTarget("UsrFoo", SchemaUId, null, options);

		// Assert
		_writer.Received(1).FetchBoundRows(OtherBindingUId, options);
	}

	[Test]
	[Description("Bindings named by the convention for other objects that exist are not read row by row.")]
	public void ResolveTarget_ShouldNotReadConventionalBindingsOfOtherObjects() {
		// Arrange
		Bindings((OtherBindingUId, "SysEntitySchemaOperationRight_UsrBar", "UsrApp"));
		EntitySchemas("UsrBar");

		// Act
		ObjectRightsBindingTarget target = _binder.ResolveTarget("UsrFoo", SchemaUId, null, null);

		// Assert
		target.Existing.Should().BeNull(because: "the binding belongs to UsrBar by its name");
		_writer.DidNotReceiveWithAnyArgs().FetchBoundRows(default, default);
	}

	[Test]
	[Description("A binding named with the convention prefix whose suffix is no object (made by hand) is read row by row, and reported when it holds one of the object's rows.")]
	public void ResolveTarget_ShouldReportPrefixedHandMadeBinding_WhenItsSuffixIsNoObject() {
		// Arrange
		Bindings((OtherBindingUId, "SysEntitySchemaOperationRight_App", "UsrApp"));
		_writer.FetchBoundRows(OtherBindingUId, Arg.Any<CreatioRequestOptions>()).Returns(
			new List<Dictionary<string, JsonNode>> { new() { ["Id"] = JsonValue.Create(RowA.ToString()) } });

		// Act
		ObjectRightsBindingTarget target = _binder.ResolveTarget("UsrFoo", SchemaUId, null, null);

		// Assert
		target.Existing.Should().NotBeNull(because: "SysEntitySchemaOperationRight_App names no object, so it is a hand-made binding");
		target.Existing.BindingName.Should().Be("SysEntitySchemaOperationRight_App",
			because: "the binding that already ships the row is reported instead of writing a second one");
	}

	[Test]
	[Description("Once the target package holds the object's own binding, a hand-made binding in the same package that holds one of its rows does not freeze it: the own binding is updated.")]
	public void ResolveTarget_ShouldLetOwnBindingWin_WhenHandMadeBindingInTargetHoldsARow() {
		// Arrange
		Bindings((Guid.NewGuid(), "SysEntitySchemaOperationRight_UsrFoo", "UsrApp"), (OtherBindingUId, "MyRights", "UsrApp"));
		_writer.FetchBoundRows(OtherBindingUId, Arg.Any<CreatioRequestOptions>()).Returns(
			new List<Dictionary<string, JsonNode>> { new() { ["Id"] = JsonValue.Create(RowA.ToString()) } });

		// Act
		ObjectRightsBindingTarget target = _binder.ResolveTarget("UsrFoo", SchemaUId, null, null);

		// Assert
		target.Existing.Should().BeNull(because: "the object's own binding in UsrApp is the target and is kept in step");
		target.PackageName.Should().Be("UsrApp", because: "the own binding's package receives the rows");
	}
}
