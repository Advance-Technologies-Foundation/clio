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
	public void Resolve_Should_Return_NameMatch_When_Present() {
		VwProcessLib byName = Row("UsrProcess_e629820", "Business process 1");

		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("UsrProcess_e629820", byName, []);

		result.IsError.Should().BeFalse();
		result.Value.Name.Should().Be("UsrProcess_e629820");
	}

	[Test]
	public void Resolve_Should_Prefer_Name_Over_Caption_Matches() {
		VwProcessLib byName = Row("UsrProcess_e629820", "Business process 1");
		var byCaption = new List<VwProcessLib> { Row("UsrProcess_other", "ignored") };

		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("UsrProcess_e629820", byName, byCaption);

		result.IsError.Should().BeFalse();
		result.Value.Name.Should().Be("UsrProcess_e629820");
	}

	[Test]
	public void Resolve_Should_Fall_Back_To_Single_Caption_Match() {
		var byCaption = new List<VwProcessLib> { Row("UsrProcess_e629820", "Business process 1") };

		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("Business process 1", byName: null, byCaption);

		result.IsError.Should().BeFalse();
		// Code is the system Name even though a caption was passed.
		result.Value.Name.Should().Be("UsrProcess_e629820");
	}

	[Test]
	public void Resolve_Should_Return_NotFound_When_Nothing_Matches() {
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("Does not exist", byName: null, []);

		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.NotFound);
		result.FirstError.Description.Should().Contain("name or caption");
	}

	[Test]
	public void Resolve_Should_Return_Conflict_When_Caption_Is_Ambiguous() {
		var byCaption = new List<VwProcessLib> {
			Row("UsrProcess_aaa", "Business process 1"),
			Row("UsrProcess_bbb", "Business process 1")
		};

		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("Business process 1", byName: null, byCaption);

		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.Conflict);
		result.FirstError.Description.Should().Contain("Multiple processes match");
		result.FirstError.Description.Should().Contain("UsrProcess_aaa").And.Contain("UsrProcess_bbb");
	}

	[Test]
	public void Resolve_Should_Tolerate_Null_Caption_List() {
		ErrorOr<VwProcessLib> result = ProcessLibResolver.Resolve("x", byName: null, byCaption: null);

		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.NotFound);
	}
}
