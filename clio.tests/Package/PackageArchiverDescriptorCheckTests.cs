using System;
using System.Linq;
using System.IO.Abstractions.TestingHelpers;
using Clio.Package;
using Clio.Tests.Command;
using Clio.Tests.Infrastructure;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Package;

/// <summary>
/// Issue #1749: <see cref="PackageArchiver.Pack(string, string, bool, bool)"/> refuses to build an archive Creatio
/// would reject with "Invalid descriptor", and names the local folder to fix. The check runs on the files that
/// actually go into the archive, so folders that never reach it are not reported.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public sealed class PackageArchiverDescriptorCheckTests : BaseClioModuleTests {

	private static readonly string WorkspacePath = TestFileSystem.GetRootedPath("ws-1749");
	private static readonly string PackagePath = TestFileSystem.GetRootedPath("ws-1749", "packages", "UsrI1749Pkg");
	private static readonly string ArchivePath = TestFileSystem.GetRootedPath("out", "UsrI1749Pkg.gz");

	private IPackageArchiver _sut;

	public override void Setup() {
		base.Setup();
		_sut = Container.GetRequiredService<IPackageArchiver>();
		AddPackageFile("descriptor.json", "{}");
		AddPackageFile(FileSystem.Path.Combine("Schemas", "UsrI1749Page", "descriptor.json"), "{}");
		AddPackageFile(FileSystem.Path.Combine("Schemas", "UsrI1749Page", "metadata.json"), "{}");
		AddPackageFile(FileSystem.Path.Combine("Data", "placeholder.txt"), string.Empty);
		FileSystem.AddDirectory(TestFileSystem.GetRootedPath("out"));
	}

	private void AddPackageFile(string relativePath, string content) =>
		FileSystem.AddFile(FileSystem.Path.Combine(PackagePath, relativePath), new MockFileData(content));

	[Test]
	[Description("A data binding folder holding only Localization files stops the pack with the full local path of the folder, and no archive is written.")]
	public void Pack_ShouldThrowNamingTheLocalFolder_WhenDataBindingFolderHasNoDescriptor() {
		// Arrange
		AddPackageFile(FileSystem.Path.Combine("Data", "UsrI1749Binding", "Localization", "data.en-US.json"), "{}");
		string expectedFolder = FileSystem.Path.Combine(PackagePath, "Data", "UsrI1749Binding");

		// Act
		Action act = () => _sut.Pack(PackagePath, ArchivePath, true, true);

		// Assert
		PackageItemDescriptorMissingException exception = act.Should().Throw<PackageItemDescriptorMissingException>(
			"because Creatio rejects the whole archive when a Data/<Name>/ folder has no descriptor.json").Which;
		exception.Folders.Should().ContainSingle("because one folder lacks its descriptor")
			.Which.FolderPath.Should().Be(expectedFolder, "because the user must be able to find the folder on disk");
		exception.Message.Should().StartWith(
			$"This package folder has no descriptor.json: {expectedFolder} (1 file: Localization/data.en-US.json; only Localization files, delete the folder)",
			"because the folder path and the verdict must sit on the first and only line of the error");
		exception.Message.Should().NotContain("\n", "because a reader that keeps only the [ERR] line must still see the path");
		FileSystem.File.Exists(ArchivePath).Should().BeFalse("because the archive is not built for a package Creatio would reject");
	}

	[Test]
	[Description("A package whose Schemas and Data folders all carry descriptor.json is packed as before.")]
	public void Pack_ShouldWriteTheArchive_WhenEveryElementFolderHasADescriptor() {
		// Arrange
		AddPackageFile(FileSystem.Path.Combine("Data", "UsrI1749Binding", "descriptor.json"), "{}");
		AddPackageFile(FileSystem.Path.Combine("Data", "UsrI1749Binding", "Localization", "data.en-US.json"), "{}");

		// Act
		Action act = () => _sut.Pack(PackagePath, ArchivePath, true, true);

		// Assert
		act.Should().NotThrow("because the package is loadable by Creatio");
		FileSystem.File.Exists(ArchivePath).Should().BeTrue("because packing a valid package must still produce the archive");
	}

	[Test]
	[Description("An empty folder under Schemas/ never reaches the archive, so it is not reported and the archive is written.")]
	public void Pack_ShouldWriteTheArchive_WhenTheFolderWithoutDescriptorIsEmpty() {
		// Arrange
		FileSystem.AddDirectory(FileSystem.Path.Combine(PackagePath, "Schemas", "UsrI1749Deleted"));

		// Act
		Action act = () => _sut.Pack(PackagePath, ArchivePath, true, true);

		// Assert
		act.Should().NotThrow("because an archive holds files only, so the platform never sees an empty folder");
		FileSystem.File.Exists(ArchivePath).Should().BeTrue("because nothing the platform would reject is packed");
	}

	[Test]
	[Description("A folder whose files the workspace clioignore excludes never reaches the archive, so it is not reported.")]
	public void Pack_ShouldWriteTheArchive_WhenClioIgnoreExcludesEveryFileOfTheFolder() {
		// Arrange
		AddPackageFile(FileSystem.Path.Combine("Data", "UsrI1749Binding", "Localization", "data.en-US.json"), "{}");
		FileSystem.AddFile(FileSystem.Path.Combine(WorkspacePath, ".clio", CreatioPackage.IgnoreFileName),
			new MockFileData("**/Localization/" + Environment.NewLine));

		// Act
		Action act = () => _sut.Pack(PackagePath, ArchivePath, true, true);

		// Assert
		act.Should().NotThrow("because the check reads the files that go into the archive, after clioignore filtering");
		FileSystem.File.Exists(ArchivePath).Should().BeTrue("because the archive Creatio receives holds no folder without a descriptor");
	}

	[Test]
	[Description("Folders of the other element kinds without descriptor.json are packed as before, so legitimate layouts are not refused.")]
	public void Pack_ShouldWriteTheArchive_WhenOnlyOtherElementKindsLackADescriptor() {
		// Arrange
		AddPackageFile(FileSystem.Path.Combine("Assemblies", "UsrLib", "UsrLib.dll"), "dll");
		AddPackageFile(FileSystem.Path.Combine("SqlScripts", "UsrScript", "script.sql"), "select 1");
		AddPackageFile(FileSystem.Path.Combine("Resources", "UsrI1749Page.ClientUnit", "resource.en-US.xml"), "<x/>");
		AddPackageFile(FileSystem.Path.Combine("Files", "src", "js", "module.js"), "//");

		// Act
		Action act = () => _sut.Pack(PackagePath, ArchivePath, true, true);

		// Assert
		act.Should().NotThrow("because only Schemas/ and Data/ are checked");
		FileSystem.File.Exists(ArchivePath).Should().BeTrue("because packing must keep working for these layouts");
	}

	[Test]
	[Description("A schema folder that still holds element files is reported with the advice to restore its descriptor, not to delete it.")]
	public void Pack_ShouldAdviseRestoringTheDescriptor_WhenTheFolderStillHoldsElementFiles() {
		// Arrange
		AddPackageFile(FileSystem.Path.Combine("Schemas", "UsrI1749Lost", "metadata.json"), "{}");

		// Act
		Action act = () => _sut.Pack(PackagePath, ArchivePath, true, true);

		// Assert
		act.Should().Throw<PackageItemDescriptorMissingException>("because the schema folder has no descriptor.json")
			.Which.Message.Should().Contain("(1 file: metadata.json; element files, restore descriptor.json)",
				"because deleting a folder that still holds the element can remove the element from the environment");
	}

	[Test]
	[Description("A refused package leaves a previously built archive at the destination untouched.")]
	public void Pack_ShouldKeepTheExistingArchive_WhenThePackageIsRefused() {
		// Arrange
		AddPackageFile(FileSystem.Path.Combine("Data", "UsrI1749Binding", "Localization", "data.en-US.json"), "{}");
		FileSystem.AddFile(ArchivePath, new MockFileData("previous archive"));

		// Act
		Action act = () => _sut.Pack(PackagePath, ArchivePath, true, true);

		// Assert
		act.Should().Throw<PackageItemDescriptorMissingException>("because the package holds a folder without a descriptor");
		FileSystem.File.ReadAllText(ArchivePath).Should().Be("previous archive",
			"because a refusal must change nothing, including an archive built by an earlier run");
	}

	[Test]
	[Description("The multi-package overload behind generate-pkg-zip with several names checks every package and names the folders of all of them.")]
	public void Pack_ShouldNameTheFoldersOfEveryPackage_WhenSeveralPackagesAreRefused() {
		// Arrange
		string packagesPath = TestFileSystem.GetRootedPath("ws-1749", "packages");
		AddPackageFile(FileSystem.Path.Combine("Data", "UsrI1749Binding", "Localization", "data.en-US.json"), "{}");
		string secondPackage = FileSystem.Path.Combine(packagesPath, "UsrI1749Second");
		FileSystem.AddFile(FileSystem.Path.Combine(secondPackage, "descriptor.json"), new MockFileData("{}"));
		FileSystem.AddFile(FileSystem.Path.Combine(secondPackage, "Data", "UsrI1749Other", "Localization", "data.en-US.json"),
			new MockFileData("{}"));
		string zipPath = TestFileSystem.GetRootedPath("out", "packages.zip");

		// Act
		Action act = () => _sut.Pack(packagesPath, zipPath, ["UsrI1749Pkg", "UsrI1749Second"], true, true);

		// Assert
		act.Should().Throw<PackageItemDescriptorMissingException>("because both packages hold a folder without a descriptor")
			.Which.Folders.Should().HaveCount(2, "because the second package is still checked after the first one is refused");
		FileSystem.File.Exists(zipPath).Should().BeFalse("because no zip is written for refused packages");
	}

	[Test]
	[Description("More than twenty refused folders are summarised so one log entry stays readable, while Folders keeps every one.")]
	public void Message_ShouldListTwentyFoldersAndCountTheRest_WhenMoreFoldersAreRefused() {
		// Arrange
		PackageItemFolderWithoutDescriptor[] folders = Enumerable.Range(1, 23)
			.Select(index => new PackageItemFolderWithoutDescriptor($"/ws/packages/UsrPkg/Data/Lookup_{index:00}",
				["Localization/data.en-US.json"]))
			.ToArray();

		// Act
		PackageItemDescriptorMissingException exception = new(folders);

		// Assert
		exception.Folders.Should().HaveCount(23, "because callers that aggregate refusals need every folder");
		exception.Message.Should().Contain("Lookup_20", "because the first twenty folders are listed")
			.And.NotContain("Lookup_21", "because the list is capped at twenty folders")
			.And.Contain("; and 3 more.", "because the reader must know how many folders were not listed");
	}

}
