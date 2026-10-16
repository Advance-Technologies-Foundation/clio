namespace Clio.Tests.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.EntityConnections;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class EntityConnectionsServiceTests {
	private const string PackageUId = "aa000000-0000-0000-0000-000000000001";
	private static readonly Guid EntityUId = Guid.Parse("bb000000-0000-0000-0000-000000000002");
	private static readonly Guid DesignerUId = Guid.Parse("bb000000-0000-0000-0000-0000000000ff");
	private static readonly Guid Owner = Guid.Parse("cc000000-0000-0000-0000-000000000001");
	private static readonly Guid Account = Guid.Parse("cc000000-0000-0000-0000-000000000002");
	private static readonly Guid Contact = Guid.Parse("cc000000-0000-0000-0000-000000000003");
	private static readonly Guid Name = Guid.Parse("cc000000-0000-0000-0000-000000000004");
	private static readonly Guid Deleted = Guid.Parse("cc000000-0000-0000-0000-0000000000dd");

	private IEntityAddonTargetResolver _targetResolver = null!;
	private IAddonSchemaDesignerClient _addonClient = null!;
	private IEntityConnectionsReader _reader = null!;
	private EntityConnectionsService _service = null!;
	private AddonGetRequestDto _addonRequest;
	private AddonSchemaDto _savedSchema;
	private Queue<EntityConnectionsSnapshot> _snapshots = null!;
	private string _metaData = "";

	[SetUp]
	public void SetUp() {
		_targetResolver = Substitute.For<IEntityAddonTargetResolver>();
		_addonClient = Substitute.For<IAddonSchemaDesignerClient>();
		_reader = Substitute.For<IEntityConnectionsReader>();
		_savedSchema = null;
		_addonRequest = null;
		_metaData = "";
		_snapshots = new Queue<EntityConnectionsSnapshot>();

		_targetResolver.Resolve(Arg.Any<string>(), Arg.Any<string>())
			.Returns(new EntityAddonTarget(PackageUId, Guid.Parse(PackageUId), new EntityDesignSchemaDto {
				UId = DesignerUId,
				Name = "UsrOrder",
				Columns = [
					Lookup(Owner, "Owner", "Contact", "Owner caption"),
					Lookup(Account, "Account", "Account", "Account caption"),
					new EntitySchemaColumnDto { UId = Name, Name = "Name", DataValueType = 28 }
				],
				InheritedColumns = [Lookup(Contact, "Contact", "Contact", "Contact caption")]
			}));
		_addonClient.GetSchema(Arg.Any<AddonGetRequestDto>()).Returns(call => {
			_addonRequest = call.Arg<AddonGetRequestDto>();
			return new AddonSchemaDto {
				MetaData = _metaData,
				AdditionalData = new Dictionary<string, JsonElement> {
					["targetSchemaUId"] = JsonSerializer.SerializeToElement(EntityUId.ToString())
				}
			};
		});
		_addonClient.When(client => client.SaveSchema(Arg.Any<AddonSchemaDto>()))
			.Do(call => {
				_savedSchema = call.Arg<AddonSchemaDto>();
				_metaData = _savedSchema.MetaData;
			});
		_reader.Read(Arg.Any<Guid>()).Returns(_ => _snapshots.Count > 1 ? _snapshots.Dequeue() : _snapshots.Peek());

		_service = new EntityConnectionsService(
			_targetResolver, _addonClient, _reader, new EntityConnectionsAddonMetadata());
	}

	private static EntitySchemaColumnDto Lookup(Guid uId, string name, string reference, string caption) => new() {
		UId = uId,
		Name = name,
		DataValueType = 10,
		Caption = [new Clio.Command.EntitySchemaDesigner.LocalizableStringDto { CultureName = "en-US", Value = caption }],
		ReferenceSchema = new EntityDesignSchemaDto { Name = reference }
	};

	private void Effective(params Guid[] columnUIds) =>
		_snapshots.Enqueue(new EntityConnectionsSnapshot(columnUIds, false, null));

	private static EntityConnectionsUpdateRequest Update(params string[] connections) =>
		new("Custom", "UsrOrder", connections);

	private JsonObject Saved() => JsonNode.Parse(_savedSchema.MetaData)!.AsObject();

	private static List<Guid> ColumnsOf(JsonObject metadata, string key) =>
		metadata[key]!.AsArray().Select(entry => Guid.Parse(entry!["ColumnUId"]!.GetValue<string>())).ToList();

	[Test]
	[Description("Get resolves names, captions and referenced objects of the effective list, names the package layer, and reads the add-on and connections of the persisted target identity.")]
	public void Get_ShouldMapEffectiveListAndPackageLayer_WhenServiceAnswers() {
		// Arrange
		_metaData = $$"""{"items":[{"ColumnUId":"{{Account}}","Position":0}],"removed":[{"ColumnUId":"{{Deleted}}"}]}""";
		Effective(Account, Owner, Contact);

		// Act
		EntityConnectionsReadResult result = _service.Get(new EntityConnectionsReadRequest("Custom", "UsrOrder"));

		// Assert
		result.EntitySchemaUId.Should().Be(EntityUId.ToString("D"),
			because: "the add-on target identity is reported, not the designer copy UId");
		_reader.Received().Read(EntityUId);
		_targetResolver.Received().Resolve("Custom", "UsrOrder");
		_addonRequest.TargetPackageUId.Should().Be(Guid.Parse(PackageUId),
			because: "the add-on is read in the context of the resolved package");
		_addonRequest.AddonName.Should().Be("EntityConnections", because: "the EntityConnections add-on is read");
		_addonRequest.TargetSchemaManagerName.Should().Be("EntitySchemaManager",
			because: "the add-on attaches to the object through the entity schema manager");
		result.Connections.Select(connection => connection.ColumnName).Should().Equal(["Account", "Owner", "Contact"],
			because: "the effective order from the server is kept and inherited columns are resolved too");
		result.Connections[1].Caption.Should().Be("Owner caption", because: "the column caption is resolved");
		result.Connections[1].ReferenceSchemaName.Should().Be("Contact", because: "the referenced object is resolved");
		result.Connections[2].Position.Should().Be(2, because: "positions follow the effective order");
		result.PackageLayer.Items.Should().Equal(["Account"], because: "add-on items are named by column code");
		result.PackageLayer.Removed.Should().Equal([Deleted.ToString("D")],
			because: "a removed column the object no longer has is reported by its UId");
		result.Warnings.Should().BeEmpty(because: "the service answered and its list reflects the add-on");
	}

	[Test]
	[Description("Get reports the table fallback warning so a caller knows the add-on is not reflected.")]
	public void Get_ShouldReportWarning_WhenListCameFromTableFallback() {
		// Arrange
		_snapshots.Enqueue(new EntityConnectionsSnapshot([Owner], true, "table fallback"));

		// Act
		EntityConnectionsReadResult result = _service.Get(new EntityConnectionsReadRequest("Custom", "UsrOrder"));

		// Assert
		result.Warnings.Should().Equal(["table fallback"], because: "the reader's warning reaches the caller");
	}

	[Test]
	[Description("Get warns when the effective list does not reflect the add-on content, the sign of a server that does not apply it.")]
	public void Get_ShouldWarn_WhenEffectiveListDoesNotReflectAddon() {
		// Arrange
		_metaData = $$"""{"items":[{"ColumnUId":"{{Account}}","Position":0}]}""";
		Effective(Owner, Account);

		// Act
		EntityConnectionsReadResult result = _service.Get(new EntityConnectionsReadRequest("Custom", "UsrOrder"));

		// Assert
		result.Warnings.Should().ContainSingle(warning => warning.Contains("UseConnectionsComponent"),
			because: "an add-on item that is not first means the server merge ignored the add-on");
	}

	[Test]
	[Description("Update reorders the list by writing the minimal add-on layer, resets the script cache, does not rebuild static content, and reports the state read back.")]
	public void Update_ShouldWriteAddonAndReadBack_WhenOrderChanges() {
		// Arrange
		Effective(Owner, Account, Contact);
		Effective(Contact, Owner, Account);

		// Act
		EntityConnectionsUpdateResult result = _service.Update(Update("contact", "Owner", "Account"));

		// Assert
		result.Changed.Should().BeTrue(because: "a different order is written");
		ColumnsOf(Saved(), "items").Should().Equal([Contact],
			because: "only the moved column becomes an item, as the Object designer writes it");
		ColumnsOf(Saved(), "removed").Should().BeEmpty(because: "nothing was removed");
		_addonClient.Received(1).ResetClientScriptCache();
		_addonClient.DidNotReceive().BuildConfiguration();
		result.State.Connections.Select(connection => connection.ColumnName).Should().Equal(["Contact", "Owner", "Account"],
			because: "the result is the state read back after the save");
		result.State.Warnings.Should().BeEmpty(because: "the state read back equals the requested list");
	}

	[Test]
	[Description("Update writes nothing and reports changed=false when the list already matches.")]
	public void Update_ShouldNotWrite_WhenListAlreadyMatches() {
		// Arrange
		Effective(Owner, Account);

		// Act
		EntityConnectionsUpdateResult result = _service.Update(Update("Owner", "Account"));

		// Assert
		result.Changed.Should().BeFalse(because: "the desired list equals the effective one");
		_addonClient.DidNotReceive().SaveSchema(Arg.Any<AddonSchemaDto>());
		_addonClient.DidNotReceive().ResetClientScriptCache();
	}

	[Test]
	[Description("Update removes every connection when the desired list is empty.")]
	public void Update_ShouldRemoveEveryConnection_WhenListIsEmpty() {
		// Arrange
		Effective(Owner, Account);
		Effective();

		// Act
		EntityConnectionsUpdateResult result = _service.Update(Update());

		// Assert
		result.Changed.Should().BeTrue(because: "removing every connection is a change");
		ColumnsOf(Saved(), "removed").Should().Equal([Owner, Account], because: "each effective column is removed");
	}

	[Test]
	[Description("Update rejects a column the object does not have, with no write.")]
	public void Update_ShouldFailWithoutWrite_WhenColumnIsUnknown() {
		// Arrange
		Effective(Owner);

		// Act
		Action act = () => _service.Update(Update("Owner", "Missing"));

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage("*'Missing'*",
			because: "the error names the column the object does not have");
		_addonClient.DidNotReceive().SaveSchema(Arg.Any<AddonSchemaDto>());
	}

	[Test]
	[Description("Update rejects a non-lookup column with its data type, with no write — only lookups can be connections.")]
	public void Update_ShouldFailWithoutWrite_WhenColumnIsNotLookup() {
		// Arrange
		Effective(Owner);

		// Act
		Action act = () => _service.Update(Update("Owner", "Name"));

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage("*'Name' (text250)*",
			because: "the error names the column and its data type");
		_addonClient.DidNotReceive().SaveSchema(Arg.Any<AddonSchemaDto>());
	}

	[Test]
	[Description("Update rejects a column listed twice (case-insensitive), with no remote call.")]
	public void Update_ShouldFailWithoutWrite_WhenColumnIsRepeated() {
		// Act
		Action act = () => _service.Update(Update("Owner", "owner"));

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage("*more than once*",
			because: "a column can be one connection only");
		_addonClient.DidNotReceive().GetSchema(Arg.Any<AddonGetRequestDto>());
		_addonClient.DidNotReceive().SaveSchema(Arg.Any<AddonSchemaDto>());
	}

	[Test]
	[Description("Update refuses to write when the list came from the table fallback, as the Object designer makes the node read-only then.")]
	public void Update_ShouldRefuseWrite_WhenListCameFromTableFallback() {
		// Arrange
		_snapshots.Enqueue(new EntityConnectionsSnapshot([Owner], true, "table fallback"));

		// Act
		Action act = () => _service.Update(Update("Account", "Owner"));

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*table fallback*",
			because: "a diff against a list without the add-on would rewrite it from incomplete data");
		_addonClient.DidNotReceive().SaveSchema(Arg.Any<AddonSchemaDto>());
	}

	[Test]
	[Description("Update refuses to write when the effective list does not reflect the add-on content.")]
	public void Update_ShouldRefuseWrite_WhenEffectiveListDoesNotReflectAddon() {
		// Arrange
		_metaData = $$"""{"removed":[{"ColumnUId":"{{Owner}}"}]}""";
		Effective(Owner, Account);

		// Act
		Action act = () => _service.Update(Update("Account"));

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*UseConnectionsComponent*",
			because: "a removed column still listed means the server does not apply the add-on");
		_addonClient.DidNotReceive().SaveSchema(Arg.Any<AddonSchemaDto>());
	}

	[Test]
	[Description("Update drops a column the object no longer has from the stored content and writes it even when the list is otherwise unchanged.")]
	public void Update_ShouldCleanUpDeletedColumn_WhenListIsOtherwiseUnchanged() {
		// Arrange
		_metaData = $$"""{"items":[{"ColumnUId":"{{Owner}}","Position":0}],"removed":[{"ColumnUId":"{{Deleted}}"}],"futureField":true}""";
		Effective(Owner, Account);

		// Act
		EntityConnectionsUpdateResult result = _service.Update(Update("Owner", "Account"));

		// Assert
		result.Changed.Should().BeTrue(because: "the server rejects an add-on that references a missing column, so it is rewritten");
		ColumnsOf(Saved(), "removed").Should().BeEmpty(because: "the deleted column leaves the removals");
		ColumnsOf(Saved(), "items").Should().Equal([Owner], because: "the other stored items are kept");
		Saved()["futureField"]!.GetValue<bool>().Should().BeTrue(because: "unknown top-level fields survive the write");
	}

	[Test]
	[Description("Update re-adds a column removed by the current layer and warns when the state read back still differs (a removal from another layer wins).")]
	public void Update_ShouldWarn_WhenStateReadBackDiffersFromRequest() {
		// Arrange
		_metaData = $$"""{"removed":[{"ColumnUId":"{{Account}}"}]}""";
		Effective(Owner);
		Effective(Owner);

		// Act
		EntityConnectionsUpdateResult result = _service.Update(Update("Owner", "Account"));

		// Assert
		ColumnsOf(Saved(), "removed").Should().BeEmpty(because: "the re-added column leaves this layer's removals");
		ColumnsOf(Saved(), "items").Should().Equal([Owner, Account], because: "the re-added column has no table row");
		result.State.Warnings.Should().ContainSingle(warning => warning.Contains("differs from the requested"),
			because: "the caller must learn that the server still does not list the column");
	}

	[Test]
	[Description("Update surfaces a failed script-cache reset as a warning instead of failing the committed save.")]
	public void Update_ShouldSurfaceResetWarning_WhenCacheResetFails() {
		// Arrange
		Effective(Owner, Account);
		Effective(Account, Owner);
		_addonClient.ResetClientScriptCache().Returns("reset failed");

		// Act
		EntityConnectionsUpdateResult result = _service.Update(Update("Account", "Owner"));

		// Assert
		result.Changed.Should().BeTrue(because: "the save is committed before the reset");
		result.State.Warnings.Should().Contain("reset failed", because: "an MCP caller cannot read server logs");
	}

	[Test]
	[Description("Get fails when the add-on response carries no persisted target identity.")]
	public void Get_ShouldFail_WhenAddonResponseHasNoTargetIdentity() {
		// Arrange
		_addonClient.GetSchema(Arg.Any<AddonGetRequestDto>()).Returns(new AddonSchemaDto { MetaData = "" });

		// Act
		Action act = () => _service.Get(new EntityConnectionsReadRequest("Custom", "UsrOrder"));

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*targetSchemaUId*",
			because: "the designer copy UId is never used as the identity the connections are stored under");
		_reader.DidNotReceive().Read(Arg.Any<Guid>());
	}

	[Test]
	[Description("When another package outside this package's hierarchy has its own EntityConnections layer, the effective list does not start with this package's items: Get warns that another package's layer may be the cause, and Update still writes.")]
	public void Update_ShouldWrite_WhenAnotherPackageLayerShiftsThisPackageItems() {
		// Arrange
		// This package places Account first; another package's layer (not a parent) places Contact before it.
		_metaData = $$"""{"items":[{"ColumnUId":"{{Account}}","Position":0}]}""";
		Effective(Contact, Account, Owner);
		Effective(Contact, Account, Owner);
		Effective(Owner, Contact, Account);

		// Act
		EntityConnectionsReadResult read = _service.Get(new EntityConnectionsReadRequest("Custom", "UsrOrder"));
		EntityConnectionsUpdateResult result = _service.Update(Update("Owner", "Contact", "Account"));

		// Assert
		read.Warnings.Should().ContainSingle(warning => warning.Contains("another package"),
			because: "items out of place can come from another package's layer, and the warning names that cause");
		read.Warnings.Should().NotContain(warning => warning.StartsWith("The effective list does not match"),
			because: "no removed column is listed, so nothing proves the add-on is ignored");
		result.Changed.Should().BeTrue(
			because: "items shifted by another package's layer do not prove the add-on is ignored, so the write goes ahead");
		_addonClient.Received(1).SaveSchema(Arg.Any<AddonSchemaDto>());
	}

	[Test]
	[Description("A column this package removes that is still in the effective list proves the add-on is not applied, even when another package also has a layer, so Update refuses to write.")]
	public void Update_ShouldRefuseWrite_WhenColumnThisPackageRemovedIsStillListed() {
		// Arrange
		// Items are out of place as with another package's layer, but Owner, removed here, is still listed.
		_metaData = $$"""{"items":[{"ColumnUId":"{{Account}}","Position":0}],"removed":[{"ColumnUId":"{{Owner}}"}]}""";
		Effective(Contact, Account, Owner);

		// Act
		Action act = () => _service.Update(Update("Contact", "Account"));

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*a column the add-on removes is still listed*",
			because: "removals of all layers are united, so no other package can bring a removed column back");
		_addonClient.DidNotReceive().SaveSchema(Arg.Any<AddonSchemaDto>());
	}

	[TestCase(null, "Custom", "entity-schema-name")]
	[TestCase("UsrOrder", " ", "package-name")]
	[Description("Get and Update reject a missing target before any remote call.")]
	public void GetAndUpdate_ShouldRejectMissingTarget_BeforeRemoteCall(string entity, string package, string field) {
		// Act
		Action get = () => _service.Get(new EntityConnectionsReadRequest(package, entity));
		Action update = () => _service.Update(new EntityConnectionsUpdateRequest(package, entity, ["Owner"]));

		// Assert
		get.Should().Throw<ArgumentException>().WithMessage($"*{field}*", because: "the missing field is named");
		update.Should().Throw<ArgumentException>().WithMessage($"*{field}*", because: "the missing field is named");
		_targetResolver.DidNotReceive().Resolve(Arg.Any<string>(), Arg.Any<string>());
	}
}
