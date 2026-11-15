using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Inputs the data-binding commands must refuse, shared by the create, add-row and remove-row fixtures.
/// </summary>
internal static class DataBindingRefusedInput {
	/// <summary>Values that are not a plain folder name: each one would move the path off packages/&lt;package&gt;/Data/&lt;binding&gt;.</summary>
	internal static IEnumerable<string> NotPlainNames() => [
		"..",
		".",
		"../Escape",
		"..\\Escape",
		"Pkg/Sub",
		"Pkg\\Sub",
		"Pkg:Stream",
		"Pkg.",
		"Pkg ",
		" Pkg",
		"Pkg\tTab",
		Path.Combine(Path.GetTempPath(), "Escape")
	];

	/// <summary>
	/// The values of <see cref="NotPlainNames"/> that stay refused after trimming: create-data-binding trims
	/// binding-name, so surrounding spaces alone leave a plain name there.
	/// </summary>
	internal static IEnumerable<string> NotPlainTrimmedNames() => NotPlainNames().Where(name => name == name.Trim());

	/// <summary>Localization keys that are not a culture name; each becomes the file name data.&lt;culture&gt;.json.</summary>
	internal static IEnumerable<string> NotCultureNames() => [
		"x/../../../../../../../outside/victim",
		"../victim",
		"en-US/../victim",
		"en-US\\..\\victim",
		"en_US",
		"en US",
		"e",
		"-US",
		"en-",
		string.Empty
	];

	/// <summary>Builds a localizations argument with one culture and one Name value.</summary>
	internal static string LocalizationsFor(string culture) =>
		JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, string>> {
			[culture] = new() { ["Name"] = "Localized" }
		});

	/// <summary>A JSON file beside the test workspaces that a refused command must leave alone.</summary>
	internal static readonly string OutsideVictimPath = Path.Combine(Path.GetTempPath(), "outside", "victim.json");

	/// <summary>
	/// (argument, value) cases for the package and binding names: every value of <see cref="NotPlainNames"/> for
	/// the package, and for the binding either the same set or, when the command trims binding-name, the values
	/// that stay refused after trimming.
	/// </summary>
	internal static IEnumerable<TestCaseData> NameCases(bool bindingNameIsTrimmed) =>
		NotPlainNames().Select(value => new TestCaseData("package", value))
			.Concat((bindingNameIsTrimmed ? NotPlainTrimmedNames() : NotPlainNames())
				.Select(value => new TestCaseData("binding", value)));

	/// <summary>Asserts a failed exit code and exactly one error that contains the expected text.</summary>
	internal static void AssertRefused(Clio.Common.ILogger logger, int result, string expectedMessagePart, string because) {
		result.Should().Be(1, because: because);
		logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains(expectedMessagePart)));
	}

	/// <summary>Captures every path of the mock file system and the content of every file.</summary>
	internal static Dictionary<string, string> Snapshot(MockFileSystem fileSystem) =>
		fileSystem.AllPaths.ToDictionary(path => path,
			path => fileSystem.File.Exists(path) ? fileSystem.File.ReadAllText(path) : "<directory>");
}
