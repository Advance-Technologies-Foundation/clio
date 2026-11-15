using System;
using System.IO;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
internal sealed class DataBindingPathGuardTests {
	[TestCaseSource(typeof(DataBindingRefusedInput), nameof(DataBindingRefusedInput.NotPlainNames))]
	[Description("Refuses every value that is not a plain folder name and names the argument in the message.")]
	public void RequirePlainName_ShouldRefuse_WhenValueIsNotAPlainFolderName(string value) {
		// Arrange
		Action act = () => DataBindingPathGuard.RequirePlainName(value, "Package name");

		// Act & Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a separator, a dot segment, a rooted path or a character invalid in a file name moves the path off its folder")
			.WithMessage("Package name*is not a plain folder name*", because: "the message names the argument that was refused");
	}

	[TestCase("TestPkg")]
	[TestCase("Usr_Package2")]
	[TestCase("SysSettings")]
	[TestCase("Custom.Binding")]
	[TestCase("Binding With Spaces")]
	[Description("Accepts ordinary package and binding folder names, including inner dots and spaces.")]
	public void RequirePlainName_ShouldAccept_WhenValueIsAPlainFolderName(string value) {
		// Arrange
		Action act = () => DataBindingPathGuard.RequirePlainName(value, "Binding name");

		// Act & Assert
		act.Should().NotThrow(because: "an ordinary folder name stays inside its parent folder");
	}

	[TestCaseSource(typeof(DataBindingRefusedInput), nameof(DataBindingRefusedInput.NotCultureNames))]
	[Description("Refuses a localization key that is not a culture name, because the key becomes part of a file name.")]
	public void RequireCultureName_ShouldRefuse_WhenKeyIsNotACultureName(string culture) {
		// Arrange
		Action act = () => DataBindingPathGuard.RequireCultureName(culture);

		// Act & Assert
		act.Should().Throw<InvalidOperationException>(because: "only letters, digits and hyphens may reach data.<culture>.json")
			.WithMessage("*is not a culture name*", because: "the message says what a valid key looks like");
	}

	[TestCase("en-US")]
	[TestCase("de-DE")]
	[TestCase("uk-UA")]
	[TestCase("sr-Latn-RS")]
	[TestCase("zh-Hans")]
	[Description("Accepts culture names in the language-region form Creatio uses, including a script subtag.")]
	public void RequireCultureName_ShouldAccept_WhenKeyIsACultureName(string culture) {
		// Arrange
		Action act = () => DataBindingPathGuard.RequireCultureName(culture);

		// Act & Assert
		act.Should().NotThrow(because: "a culture name is a safe file-name part");
	}

	[TestCase("packages", true)]
	[TestCase(".", false)]
	[TestCase("..", false)]
	[Description("Treats only descendants as inside: the root itself and a parent segment are outside.")]
	public void IsStrictlyInside_ShouldAnswerByPosition_WhenCandidateIsRelativeToTheRoot(string relativeCandidate, bool expected) {
		// Arrange
		string root = Path.Combine(Path.GetTempPath(), "clio-guard-root");
		string candidate = Path.GetFullPath(Path.Combine(root, relativeCandidate));

		// Act
		bool inside = DataBindingPathGuard.IsStrictlyInside(root, candidate);

		// Assert
		inside.Should().Be(expected, because: "only a path below the root is inside it");
	}

	[Test]
	[Description("Treats a folder whose name only starts with the root's name as outside.")]
	public void IsStrictlyInside_ShouldRefuse_WhenCandidateIsAPrefixSibling() {
		// Arrange
		string root = Path.Combine(Path.GetTempPath(), "clio-guard-root");

		// Act
		bool inside = DataBindingPathGuard.IsStrictlyInside(root, root + "-evil" + Path.DirectorySeparatorChar + "file.png");

		// Assert
		inside.Should().BeFalse(because: "a shared name prefix does not make a folder a descendant");
	}

	[Test]
	[Description("Compares letter case exactly on every platform: a path that differs from the root only by case may be a different folder, or a link, on a case-sensitive volume.")]
	public void IsStrictlyInside_ShouldRefuse_WhenOnlyLetterCaseDiffers() {
		// Arrange
		string root = Path.Combine(Path.GetTempPath(), "Clio-Case-Root");
		string candidate = Path.Combine(Path.GetTempPath(), "clio-case-root", "icon.png");

		// Act
		bool inside = DataBindingPathGuard.IsStrictlyInside(root, candidate);

		// Assert
		inside.Should().BeFalse(because: "only a path spelled with the root's exact prefix is inside it");
	}
}
