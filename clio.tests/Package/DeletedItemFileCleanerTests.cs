using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Package;

[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public sealed class DeletedItemFileCleanerTests {

	private const int SqlScriptType = 0;
	private const int SchemaDataType = 1;
	private const int AssemblyType = 2;
	private const int EntitySchemaType = 3;
	private const int ClientUnitSchemaType = 4;
	private const int LocalizationSchemaType = 13;

	private static readonly string PackageFolder =
		Path.Combine(Path.GetTempPath(), "clio-1746-site", "Terrasoft.Configuration", "Pkg", "UsrPkg");

	private MockFileSystem _mockFileSystem;
	private IEnvironmentPackageFolderResolver _packageFolderResolver;
	private DeletedItemFileCleaner _cleaner;

	[SetUp]
	public void SetUp() {
		_mockFileSystem = new MockFileSystem();
		_packageFolderResolver = Substitute.For<IEnvironmentPackageFolderResolver>();
		_cleaner = new DeletedItemFileCleaner(_packageFolderResolver, new Clio.Common.FileSystem(_mockFileSystem));
	}

	[Test]
	[Description("An entity's schema folder and every resource folder, including culture files a repository may ignore, are removed; folders of other items stay.")]
	public void Clean_ShouldRemoveSchemaAndResourceFolders_WhenPackageFolderIsReachable() {
		// Arrange
		ArrangePackageFolderFound();
		AddFile("Schemas", "UsrFoo", "metadata.json");
		AddFile("Resources", "UsrFoo.Entity", "resource.en-US.xml");
		AddFile("Resources", "UsrFoo.Entity", "resource.uk-UA.xml");
		AddFile("Schemas", "UsrFooBar", "metadata.json");
		AddFile("Resources", "UsrFooBar.Entity", "resource.en-US.xml");
		AddFile("Data", "UsrFoo", "descriptor.json");

		// Act
		DeletedItemFileCleanupResult result = _cleaner.Clean(Request(EntitySchemaType));

		// Assert
		result.Status.Should().Be(DeletedItemFileCleanupStatus.Cleaned, because: "the package folder is reachable");
		result.RemovedFolders.Should().BeEquivalentTo(["Schemas/UsrFoo/", "Resources/UsrFoo.Entity/"],
			because: "the schema folder and its resource folder hold the deleted entity");
		result.RemainingFolders.Should().BeEmpty(because: "every matching folder was removable");
		FolderExists("Schemas", "UsrFoo").Should().BeFalse(because: "the next pkg-to-db must not find the schema again");
		FolderExists("Resources", "UsrFoo.Entity").Should().BeFalse(
			because: "a resource folder left with only ignored culture files breaks the next install");
		FolderExists("Schemas", "UsrFooBar").Should().BeTrue(because: "a schema whose name only starts with the deleted name is another item");
		FolderExists("Resources", "UsrFooBar.Entity").Should().BeTrue(because: "resources of another schema are not touched");
		FolderExists("Data", "UsrFoo").Should().BeTrue(because: "a data binding with the same name is a separate item");
	}

	[Test]
	[Description("A deleted data binding takes its whole Data folder, including Localization files, and nothing else.")]
	public void Clean_ShouldRemoveDataFolder_WhenDeletedItemIsADataBinding() {
		// Arrange
		ArrangePackageFolderFound();
		AddFile("Data", "Lookup_UsrFoo", "descriptor.json");
		AddFile(Path.Combine("Data", "Lookup_UsrFoo", "Localization"), "data.uk-UA.json");
		AddFile("Schemas", "Lookup_UsrFoo", "metadata.json");

		// Act
		DeletedItemFileCleanupResult result = _cleaner.Clean(Request(SchemaDataType, "Lookup_UsrFoo"));

		// Assert
		result.RemovedFolders.Should().Equal(["Data/Lookup_UsrFoo/"], because: "a binding lives only in Data");
		FolderExists("Data", "Lookup_UsrFoo").Should().BeFalse(because: "the binding folder is removed with its localization files");
		FolderExists("Schemas", "Lookup_UsrFoo").Should().BeTrue(because: "deleting a binding must not touch a schema");
	}

	[TestCase(SqlScriptType, "SqlScripts", "UsrScript", "UsrScript")]
	[TestCase(SqlScriptType, "SqlScripts", "UsrScript.sql", "UsrScript")]
	[TestCase(AssemblyType, "Assemblies", "UsrLib.dll", "UsrLib")]
	[TestCase(AssemblyType, "Assemblies", "Usr.Lib", "Usr.Lib")]
	[Description("SQL scripts and assemblies are removed from their own areas, with a file extension in the item name tolerated.")]
	public void Clean_ShouldRemoveItemFolder_WhenDeletedItemIsAScriptOrAssembly(int itemType, string area,
		string itemName, string folderName) {
		// Arrange
		ArrangePackageFolderFound();
		AddFile(area, folderName, "descriptor.json");

		// Act
		DeletedItemFileCleanupResult result = _cleaner.Clean(Request(itemType, itemName));

		// Assert
		result.RemovedFolders.Should().Equal([$"{area}/{folderName}/"], because: "the platform stores the item in that folder");
		FolderExists(area, folderName).Should().BeFalse(because: "the item folder is removed");
	}

	[Test]
	[Description("Folder names are matched case-insensitively, like Creatio item names.")]
	public void Clean_ShouldMatchFolderNamesCaseInsensitively_WhenFolderCasingDiffers() {
		// Arrange
		ArrangePackageFolderFound();
		AddFile("Schemas", "usrfoo", "metadata.json");
		AddFile("Resources", "USRFOO.ClientUnit", "resource.en-US.xml");

		// Act
		DeletedItemFileCleanupResult result = _cleaner.Clean(Request(ClientUnitSchemaType, "UsrFoo"));

		// Assert
		result.RemovedFolders.Should().BeEquivalentTo(["Schemas/usrfoo/", "Resources/USRFOO.ClientUnit/"],
			because: "the platform does not care about the folder name casing");
		FolderExists("Schemas", "usrfoo").Should().BeFalse(because: "the differently cased schema folder is the item");
	}

	[Test]
	[Description("A bare Resources/<name> folder written by older platform versions is removed with the schema.")]
	public void Clean_ShouldRemoveBareResourceFolder_WhenOlderLayoutIsPresent() {
		// Arrange
		ArrangePackageFolderFound();
		AddFile("Schemas", "UsrFoo", "metadata.json");
		AddFile("Resources", "UsrFoo", "resource.en-US.xml");

		// Act
		DeletedItemFileCleanupResult result = _cleaner.Clean(Request(EntitySchemaType));

		// Assert
		result.RemovedFolders.Should().BeEquivalentTo(["Schemas/UsrFoo/", "Resources/UsrFoo/"],
			because: "the platform's own delete removes both resource folder forms");
	}

	[Test]
	[Description("A localization item only owns resource folders; a schema folder with the same name belongs to a schema.")]
	public void Clean_ShouldRemoveOnlyResources_WhenDeletedItemIsALocalizationSchema() {
		// Arrange
		ArrangePackageFolderFound();
		AddFile("Resources", "Contact.Entity", "resource.uk-UA.xml");
		AddFile("Schemas", "UsrOther", "metadata.json");

		// Act
		DeletedItemFileCleanupResult result = _cleaner.Clean(Request(LocalizationSchemaType, "Contact"));

		// Assert
		result.RemovedFolders.Should().Equal(["Resources/Contact.Entity/"], because: "localization items are resources only");
		FolderExists("Schemas", "UsrOther").Should().BeTrue(because: "schema folders are never part of a localization item");
	}

	[Test]
	[Description("A localization item next to a schema of the same name leaves the shared resource folder and reports it.")]
	public void Clean_ShouldKeepSharedResources_WhenLocalizationItemHasASchemaOfTheSameName() {
		// Arrange
		ArrangePackageFolderFound();
		AddFile("Resources", "Contact.Entity", "resource.uk-UA.xml");
		AddFile("Schemas", "Contact", "metadata.json");

		// Act
		DeletedItemFileCleanupResult result = _cleaner.Clean(Request(LocalizationSchemaType, "Contact"));

		// Assert
		result.RemovedFolders.Should().BeEmpty(because: "removing the folder would strip the remaining schema's captions");
		result.RemainingFolders.Should().BeEmpty(because: "the folder is not left behind by mistake and must not be removed by hand");
		result.KeptFolders.Should().ContainSingle(because: "the deliberately kept folder is reported")
			.Which.Should().Contain("Resources/Contact.*/").And.Contain("schema 'Contact'");
		FolderExists("Resources", "Contact.Entity").Should().BeTrue(because: "the schema still uses these resources");
	}

	[Test]
	[Description("With an unreachable package folder the resolver's reason and the expected folders are returned.")]
	public void Clean_ShouldReportNotCleaned_WhenPackageFolderIsUnreachable() {
		// Arrange
		_packageFolderResolver.Resolve("dev", "UsrPkg", null)
			.Returns(EnvironmentPackageFolderResolution.NotFound("no site folder (EnvironmentPath) is registered"));

		// Act
		DeletedItemFileCleanupResult result = _cleaner.Clean(Request(EntitySchemaType));

		// Assert
		result.Status.Should().Be(DeletedItemFileCleanupStatus.NotCleaned, because: "the folder could not be reached");
		result.Problem.Should().Contain("EnvironmentPath", because: "the resolver's reason is relayed");
		result.ExpectedFolders.Should().Contain("Schemas/UsrFoo/", because: "the caller must remove the folders by hand");
	}

	[Test]
	[Description("An item type clio has no layout for is not guessed at; nothing is removed and the reason is returned.")]
	public void Clean_ShouldReportNotCleaned_WhenItemTypeIsUnknown() {
		// Arrange
		ArrangePackageFolderFound();
		AddFile("Schemas", "UsrFoo", "metadata.json");

		// Act
		DeletedItemFileCleanupResult result = _cleaner.Clean(Request(99));

		// Assert
		result.Status.Should().Be(DeletedItemFileCleanupStatus.NotCleaned, because: "the layout of type 99 is unknown");
		result.Problem.Should().Contain("99", because: "the problem names the unknown type");
		FolderExists("Schemas", "UsrFoo").Should().BeTrue(because: "a guess could remove another item's folder");
		_packageFolderResolver.DidNotReceiveWithAnyArgs().Resolve(default, default, default);
	}

	[Test]
	[Description("An item folder that is itself a symbolic link is not followed and is reported as left behind.")]
	public void Clean_ShouldKeepSymbolicLinkedItemFolder_WhenItemFolderIsALink() {
		// Arrange
		ArrangePackageFolderFound();
		string target = Path.Combine(Path.GetTempPath(), "clio-1746-elsewhere");
		_mockFileSystem.AddFile(Path.Combine(target, "metadata.json"), new MockFileData("{}"));
		_mockFileSystem.AddDirectory(Path.Combine(PackageFolder, "Schemas"));
		_mockFileSystem.Directory.CreateSymbolicLink(Path.Combine(PackageFolder, "Schemas", "UsrFoo"), target);

		// Act
		DeletedItemFileCleanupResult result = _cleaner.Clean(Request(EntitySchemaType));

		// Assert
		result.RemainingFolders.Should().ContainSingle(because: "the link is reported instead of being removed")
			.Which.Should().Contain("Schemas/UsrFoo/").And.Contain("symbolic link");
		_mockFileSystem.File.Exists(Path.Combine(target, "metadata.json")).Should().BeTrue(
			because: "clio never deletes outside the package folder through a link");
	}

	[Test]
	[Description("An area folder that is a symbolic link is not followed, so nothing outside the package folder is removed.")]
	public void Clean_ShouldNotFollowLinkedAreaFolder_WhenAreaFolderIsALink() {
		// Arrange
		ArrangePackageFolderFound();
		string target = Path.Combine(Path.GetTempPath(), "clio-1746-outside-schemas");
		_mockFileSystem.AddFile(Path.Combine(target, "UsrFoo", "metadata.json"), new MockFileData("{}"));
		_mockFileSystem.Directory.CreateSymbolicLink(Path.Combine(PackageFolder, "Schemas"), target);

		// Act
		DeletedItemFileCleanupResult result = _cleaner.Clean(Request(EntitySchemaType));

		// Assert
		result.RemainingFolders.Should().ContainSingle(because: "the linked area is reported instead of being listed")
			.Which.Should().Contain("Schemas/UsrFoo/").And.Contain("symbolic link");
		_mockFileSystem.Directory.Exists(Path.Combine(target, "UsrFoo")).Should().BeTrue(
			because: "a folder reached through a link below the package folder is outside the package");
	}

	[Test]
	[Description("An area folder that cannot be listed is reported with the error instead of failing the cleanup.")]
	public void Clean_ShouldReportArea_WhenAreaFolderCannotBeListed() {
		// Arrange
		ArrangePackageFolderFound();
		Clio.Common.IFileSystem fileSystem = Substitute.For<Clio.Common.IFileSystem>();
		string schemas = Path.Combine(PackageFolder, "Schemas");
		fileSystem.Combine(PackageFolder, "Schemas").Returns(schemas);
		fileSystem.Combine(PackageFolder, "Resources").Returns(Path.Combine(PackageFolder, "Resources"));
		fileSystem.ExistsDirectory(schemas).Returns(true);
		fileSystem.GetDirectoryInfo(Arg.Any<string>()).Returns(callInfo =>
			_mockFileSystem.DirectoryInfo.New(callInfo.Arg<string>()));
		fileSystem.GetDirectories(schemas).Throws(new UnauthorizedAccessException("listing denied"));
		DeletedItemFileCleaner cleaner = new(_packageFolderResolver, fileSystem);

		// Act
		DeletedItemFileCleanupResult result = cleaner.Clean(Request(EntitySchemaType));

		// Assert
		result.Status.Should().Be(DeletedItemFileCleanupStatus.Cleaned, because: "the cleanup ran and reports what it could not do");
		result.RemainingFolders.Should().ContainSingle(because: "the unlisted area may still hold the schema")
			.Which.Should().Contain("Schemas/UsrFoo/").And.Contain("listing denied");
	}

	[TestCase("../UsrFoo")]
	[TestCase("Usr/Foo")]
	[Description("An item name that is not a single folder name is refused with its own reason, before the package folder is resolved.")]
	public void Clean_ShouldRefuseUnsafeItemName_WhenNameIsNotASingleFolderName(string itemName) {
		// Arrange

		// Act
		DeletedItemFileCleanupResult result = _cleaner.Clean(Request(EntitySchemaType, itemName));

		// Assert
		result.Status.Should().Be(DeletedItemFileCleanupStatus.NotCleaned, because: "such a name cannot be matched safely");
		result.Problem.Should().Contain("not a valid folder name", because: "the reason names the actual cause");
		_packageFolderResolver.DidNotReceiveWithAnyArgs().Resolve(default, default, default);
	}

	[Test]
	[Description("A folder that cannot be removed is reported with the error, while the other folders are still removed.")]
	public void Clean_ShouldReportRemainingFolder_WhenRemovalFails() {
		// Arrange
		ArrangePackageFolderFound();
		Clio.Common.IFileSystem fileSystem = Substitute.For<Clio.Common.IFileSystem>();
		string schemas = Path.Combine(PackageFolder, "Schemas");
		string resources = Path.Combine(PackageFolder, "Resources");
		fileSystem.Combine(PackageFolder, "Schemas").Returns(schemas);
		fileSystem.Combine(PackageFolder, "Resources").Returns(resources);
		fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(true);
		fileSystem.GetDirectories(schemas).Returns([Path.Combine(schemas, "UsrFoo")]);
		fileSystem.GetDirectories(resources).Returns([Path.Combine(resources, "UsrFoo.Entity")]);
		fileSystem.GetDirectoryInfo(Arg.Any<string>()).Returns(callInfo =>
			_mockFileSystem.DirectoryInfo.New(callInfo.Arg<string>()));
		fileSystem.When(fs => fs.DeleteDirectory(Path.Combine(resources, "UsrFoo.Entity"), true))
			.Do(_ => throw new UnauthorizedAccessException("access denied"));
		DeletedItemFileCleaner cleaner = new(_packageFolderResolver, fileSystem);

		// Act
		DeletedItemFileCleanupResult result = cleaner.Clean(Request(EntitySchemaType));

		// Assert
		result.RemovedFolders.Should().Equal(["Schemas/UsrFoo/"], because: "the removable folder is still removed");
		result.RemainingFolders.Should().ContainSingle(because: "one folder could not be removed")
			.Which.Should().Contain("Resources/UsrFoo.Entity/").And.Contain("access denied");
	}

	private static DeletedItemFileCleanupRequest Request(int itemType, string itemName = "UsrFoo") =>
		new("dev", null, "UsrPkg", itemName, itemType);

	private void ArrangePackageFolderFound() {
		_mockFileSystem.AddDirectory(PackageFolder);
		_packageFolderResolver.Resolve("dev", "UsrPkg", null)
			.Returns(EnvironmentPackageFolderResolution.Found(PackageFolder));
	}

	private void AddFile(string area, string folder, string fileName) {
		_mockFileSystem.AddFile(Path.Combine(PackageFolder, area, folder, fileName), new MockFileData("{}"));
	}

	private void AddFile(string relativeFolder, string fileName) {
		_mockFileSystem.AddFile(Path.Combine(PackageFolder, relativeFolder, fileName), new MockFileData("{}"));
	}

	private bool FolderExists(string area, string folder) =>
		_mockFileSystem.Directory.Exists(Path.Combine(PackageFolder, area, folder));
}
