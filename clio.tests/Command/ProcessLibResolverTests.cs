using System.Collections.Generic;
using Clio.Command.ProcessModel;
using Clio.CreatioModel;
using ErrorOr;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit tests for the pure process-resolution selection logic: resolve by system Name
/// (code) with a fallback to display Caption, and ambiguity handling.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "ProcessModel")]
public sealed class ProcessLibResolverTests {

	private static VwProcessLib Row(string name, string caption) =>
		new() { Name = name, Caption = caption };

	[Test]
	[Description("Resolves the process by exact system Name (code) when a Name match is present.")]
	public void Resolve_Should_Return_NameMatch_When_Present() {
		// Arrange
		VwProcessLib byName = Row("UsrProcess_e629820", "Business process 1");

		// Act
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("UsrProcess_e629820", byName, []);

		// Assert
		result.IsError.Should().BeFalse(because: "an exact Name match is a valid resolution");
		result.Value.Name.Should().Be("UsrProcess_e629820", because: "the matched row should be returned");
	}

	[Test]
	[Description("Prefers the exact Name match over any Caption matches.")]
	public void Resolve_Should_Prefer_Name_Over_Caption_Matches() {
		// Arrange
		VwProcessLib byName = Row("UsrProcess_e629820", "Business process 1");
		var byCaption = new List<VwProcessLib> { Row("UsrProcess_other", "ignored") };

		// Act
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("UsrProcess_e629820", byName, byCaption);

		// Assert
		result.IsError.Should().BeFalse(because: "a Name match resolves regardless of caption matches");
		result.Value.Name.Should().Be("UsrProcess_e629820",
			because: "the Name match takes precedence over caption candidates");
	}

	[Test]
	[Description("Falls back to a single Caption match and returns the system Name as the code.")]
	public void Resolve_Should_Fall_Back_To_Single_Caption_Match() {
		// Arrange
		var byCaption = new List<VwProcessLib> { Row("UsrProcess_e629820", "Business process 1") };

		// Act
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("Business process 1", byName: null, byCaption);

		// Assert
		result.IsError.Should().BeFalse(because: "a single caption match is an unambiguous resolution");
		result.Value.Name.Should().Be("UsrProcess_e629820",
			because: "the resolved code is the system Name even when a caption was passed");
	}

	[Test]
	[Description("Returns NotFound when neither Name nor Caption matches anything.")]
	public void Resolve_Should_Return_NotFound_When_Nothing_Matches() {
		// Arrange
		// (no rows match the requested value)

		// Act
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("Does not exist", byName: null, []);

		// Assert
		result.IsError.Should().BeTrue(because: "a value matching no process cannot be resolved");
		result.FirstError.Type.Should().Be(ErrorType.NotFound, because: "nothing matched the value");
		result.FirstError.Description.Should().Contain("name or caption",
			because: "the message should explain both lookup paths were tried");
	}

	[Test]
	[Description("Returns Conflict listing candidate codes when a Caption matches more than one process.")]
	public void Resolve_Should_Return_Conflict_When_Caption_Is_Ambiguous() {
		// Arrange
		var byCaption = new List<VwProcessLib> {
			Row("UsrProcess_aaa", "Business process 1"),
			Row("UsrProcess_bbb", "Business process 1")
		};

		// Act
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("Business process 1", byName: null, byCaption);

		// Assert
		result.IsError.Should().BeTrue(because: "an ambiguous caption cannot be resolved to one process");
		result.FirstError.Type.Should().Be(ErrorType.Conflict, because: "the caption matched multiple processes");
		result.FirstError.Description.Should().Contain("Multiple processes match",
			because: "the message should state the ambiguity");
		result.FirstError.Description.Should().Contain("UsrProcess_aaa").And.Contain("UsrProcess_bbb",
			because: "the candidate codes should be listed so the caller can pick one");
	}

	[Test]
	[Description("Tolerates a null caption list and returns NotFound without throwing.")]
	public void Resolve_Should_Tolerate_Null_Caption_List() {
		// Arrange
		// (byCaption is null)

		// Act
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("x", byName: null, byCaption: null);

		// Assert
		result.IsError.Should().BeTrue(because: "no match is available when both inputs are empty/null");
		result.FirstError.Type.Should().Be(ErrorType.NotFound, because: "a null caption list means nothing matched");
	}
}
