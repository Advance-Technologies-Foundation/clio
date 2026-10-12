using System.Collections.Generic;
using Clio.Package;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Package;

/// <summary>
/// Issue #1749: Creatio rejects a whole package archive when a folder under <c>Schemas/</c> or <c>Data/</c> has
/// no <c>descriptor.json</c>. These tests pin which folders the pre-flight check reports, and which it leaves alone.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public sealed class PackageItemDescriptorCheckTests {

	[Test]
	[Description("A data binding folder left with only Localization files (the reported case) is returned with the files it holds.")]
	public void FindFoldersWithoutDescriptor_ShouldReturnTheFolder_WhenDataBindingHoldsOnlyLocalization() {
		// Arrange
		string[] files = [
			"descriptor.json",
			"Data/Lookup_Status/Localization/data.en-US.json",
			"Data/Lookup_Status/Localization/data.uk-UA.json"
		];

		// Act
		IReadOnlyList<PackageItemFolderWithoutDescriptor> folders = PackageItemDescriptorCheck.FindFoldersWithoutDescriptor(files);

		// Assert
		folders.Should().ContainSingle("because exactly one element folder has no descriptor.json");
		folders[0].FolderPath.Should().Be("Data/Lookup_Status",
			"because the folder is named relative to the package root so the caller can locate it");
		folders[0].Files.Should().Equal(["Localization/data.en-US.json", "Localization/data.uk-UA.json"],
			"because the files tell the user whether the folder is a leftover or a damaged element");
	}

	[Test]
	[Description("A schema folder that lost its descriptor.json is reported just like a data binding folder.")]
	public void FindFoldersWithoutDescriptor_ShouldReturnTheFolder_WhenSchemaFolderHasNoDescriptor() {
		// Arrange
		string[] files = ["Schemas/UsrOldPage/metadata.json", "Schemas/UsrOldPage/properties.json"];

		// Act
		IReadOnlyList<PackageItemFolderWithoutDescriptor> folders = PackageItemDescriptorCheck.FindFoldersWithoutDescriptor(files);

		// Assert
		folders.Should().ContainSingle("because the platform requires descriptor.json in every Schemas/<Name>/ folder")
			.Which.FolderPath.Should().Be("Schemas/UsrOldPage", "because that is the folder without a descriptor");
	}

	[Test]
	[Description("A package whose every Schemas and Data folder carries descriptor.json is accepted.")]
	public void FindFoldersWithoutDescriptor_ShouldReturnNothing_WhenEveryElementFolderHasADescriptor() {
		// Arrange
		string[] files = [
			"descriptor.json",
			"Schemas/UsrPage/descriptor.json",
			"Schemas/UsrPage/metadata.json",
			"Data/Lookup_Status/descriptor.json",
			"Data/Lookup_Status/data.json",
			"Data/Lookup_Status/Localization/data.en-US.json"
		];

		// Act
		IReadOnlyList<PackageItemFolderWithoutDescriptor> folders = PackageItemDescriptorCheck.FindFoldersWithoutDescriptor(files);

		// Assert
		folders.Should().BeEmpty("because every element folder the platform loads has its descriptor");
	}

	[Test]
	[Description("Files placed directly under Schemas/ or Data/ (the add-package placeholder.txt) are not element folders and are not reported.")]
	public void FindFoldersWithoutDescriptor_ShouldIgnoreFiles_WhenTheyLieDirectlyUnderSchemasOrData() {
		// Arrange
		string[] files = ["Schemas/placeholder.txt", "Data/placeholder.txt"];

		// Act
		IReadOnlyList<PackageItemFolderWithoutDescriptor> folders = PackageItemDescriptorCheck.FindFoldersWithoutDescriptor(files);

		// Assert
		folders.Should().BeEmpty("because the platform enumerates only the directories under Schemas/ and Data/");
	}

	[Test]
	[Description("Assemblies, SqlScripts, Resources and Files keep their own layouts and are never checked, so working packages are not refused.")]
	public void FindFoldersWithoutDescriptor_ShouldIgnoreOtherElementKinds_WhenTheyHaveNoDescriptor() {
		// Arrange
		string[] files = [
			"Assemblies/UsrLib/UsrLib.dll",
			"SqlScripts/UsrScript/script.sql",
			"Resources/UsrPage.ClientUnit/resource.en-US.xml",
			"Files/src/js/module.js",
			"Bin/UsrPkg.dll"
		];

		// Act
		IReadOnlyList<PackageItemFolderWithoutDescriptor> folders = PackageItemDescriptorCheck.FindFoldersWithoutDescriptor(files);

		// Assert
		folders.Should().BeEmpty("because only Schemas/ and Data/ are within the scope of the check");
	}

	[Test]
	[Description("A descriptor.json nested below the element folder does not count as the folder's own descriptor.")]
	public void FindFoldersWithoutDescriptor_ShouldReturnTheFolder_WhenDescriptorIsOnlyNestedDeeper() {
		// Arrange
		string[] files = ["Data/Lookup_Status/Localization/descriptor.json"];

		// Act
		IReadOnlyList<PackageItemFolderWithoutDescriptor> folders = PackageItemDescriptorCheck.FindFoldersWithoutDescriptor(files);

		// Assert
		folders.Should().ContainSingle("because the platform reads descriptor.json directly inside Data/<Name>/")
			.Which.FolderPath.Should().Be("Data/Lookup_Status", "because that folder still has no descriptor of its own");
	}

	[Test]
	[Description("Descriptor name casing is accepted and Windows separators are understood, so the check never refuses an archive the platform can load.")]
	public void FindFoldersWithoutDescriptor_ShouldAcceptTheDescriptor_WhenCasingOrSeparatorsDiffer() {
		// Arrange
		string[] files = [@"Schemas\UsrPage\Descriptor.json", @"Schemas\UsrPage\metadata.json"];

		// Act
		IReadOnlyList<PackageItemFolderWithoutDescriptor> folders = PackageItemDescriptorCheck.FindFoldersWithoutDescriptor(files);

		// Assert
		folders.Should().BeEmpty("because a Windows host reads Descriptor.json and backslash paths come from Windows clients");
	}

	[Test]
	[Description("Every offending folder is reported, ordered by path, so one run lists all of them.")]
	public void FindFoldersWithoutDescriptor_ShouldReturnEveryFolderInOrder_WhenSeveralLackADescriptor() {
		// Arrange
		string[] files = [
			"Schemas/UsrB/metadata.json",
			"Data/Lookup_Z/Localization/data.en-US.json",
			"Data/Lookup_A/Localization/data.en-US.json",
			"Data/Lookup_Ok/descriptor.json"
		];

		// Act
		IReadOnlyList<PackageItemFolderWithoutDescriptor> folders = PackageItemDescriptorCheck.FindFoldersWithoutDescriptor(files);

		// Assert
		folders.Should().HaveCount(3, "because three element folders have no descriptor.json");
		folders.Should().BeInAscendingOrder(folder => folder.FolderPath, System.StringComparer.Ordinal,
			"because a stable order makes the error message reproducible");
	}

	[Test]
	[Description("A file the clioignore filter hands in twice is listed once, so the message does not double the count.")]
	public void FindFoldersWithoutDescriptor_ShouldListEachFileOnce_WhenTheSameFileArrivesTwice() {
		// Arrange
		string[] files = ["Data/Lookup_Status/Localization/data.en-US.json", "Data/Lookup_Status/Localization/data.en-US.json"];

		// Act
		IReadOnlyList<PackageItemFolderWithoutDescriptor> folders = PackageItemDescriptorCheck.FindFoldersWithoutDescriptor(files);

		// Assert
		folders.Should().ContainSingle("because both entries belong to one folder")
			.Which.Files.Should().Equal(["Localization/data.en-US.json"], "because a duplicate entry is still one file");
	}

	[Test]
	[Description("A folder holding only Localization files is classified as a deleted binding's leftover; any other file marks an element that lost its descriptor.")]
	public void HoldsOnlyLocalization_ShouldTellALeftoverFromADamagedElement() {
		// Arrange
		PackageItemFolderWithoutDescriptor leftover = new("Data/Lookup_Status", ["Localization/data.en-US.json"]);
		PackageItemFolderWithoutDescriptor damaged = new("Data/Lookup_Status", ["Localization/data.en-US.json", "data.json"]);

		// Act
		bool leftoverVerdict = leftover.HoldsOnlyLocalization;
		bool damagedVerdict = damaged.HoldsOnlyLocalization;

		// Assert
		leftoverVerdict.Should().BeTrue("because git keeps only the ignored per-culture files of a deleted binding");
		damagedVerdict.Should().BeFalse("because data.json means the binding itself is still there and must not be deleted");
	}

	[Test]
	[Description("Operating system metadata such as .DS_Store next to the Localization files does not turn a deleted binding's leftover into a damaged element.")]
	public void HoldsOnlyLocalization_ShouldIgnoreOperatingSystemFiles_WhenTheyLieNextToLocalization() {
		// Arrange
		PackageItemFolderWithoutDescriptor leftover = new("Data/Lookup_Status",
			[".DS_Store", "Localization/.DS_Store", "Localization/data.en-US.json", "Thumbs.db"]);
		PackageItemFolderWithoutDescriptor onlyMetadata = new("Data/Lookup_Status", [".DS_Store"]);

		// Act
		bool leftoverVerdict = leftover.HoldsOnlyLocalization;
		bool onlyMetadataVerdict = onlyMetadata.HoldsOnlyLocalization;

		// Assert
		leftoverVerdict.Should().BeTrue("because Finder and Explorer files are not part of the element");
		onlyMetadataVerdict.Should().BeFalse("because a folder with no Localization file is not the leftover shape this verdict describes");
	}


	[Test]
	[Description("A folder holding only operating system metadata is a leftover with nothing to restore, so the refusal tells the user to delete it.")]
	public void HoldsOnlyOperatingSystemFiles_ShouldMarkAMetadataOnlyFolderAsALeftover() {
		// Arrange
		PackageItemFolderWithoutDescriptor onlyMetadata = new("Data/Lookup_Status", [".DS_Store", "Localization/Thumbs.db"]);
		PackageItemFolderWithoutDescriptor withLocalization = new("Data/Lookup_Status", [".DS_Store", "Localization/data.en-US.json"]);

		// Act
		PackageItemDescriptorMissingException exception = new([onlyMetadata]);

		// Assert
		onlyMetadata.HoldsOnlyOperatingSystemFiles.Should().BeTrue("because Finder and Explorer files are not part of any element");
		withLocalization.HoldsOnlyOperatingSystemFiles.Should().BeFalse("because a Localization file is part of the binding");
		exception.Message.Should().Contain("only operating system files, delete the folder",
			"because there is no descriptor to restore for a folder that holds no element files");
		exception.Message.Should().NotContain("element files, restore descriptor.json",
			"because telling the user to restore a descriptor for a metadata-only folder is wrong advice");
	}

}
