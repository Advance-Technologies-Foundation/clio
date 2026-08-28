using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Behaviour of <see cref="ProcessPageFactsCommand.TryGetFacts"/> — the two guards that decide whether an agent
/// gets page facts or a refusal. Until the <see cref="IProcessPageReader"/> seam existed this flow had zero unit
/// coverage: the command took the concrete <c>PageGetCommand</c>, whose six collaborators made it unstubbable, so
/// the only code deciding facts-or-refusal was also the only code with no tests.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ProcessPageFactsCommandTests {
	private IProcessPageReader _pageReader;
	private ILogger _logger;
	private ProcessPageFactsCommand _command;

	[SetUp]
	public void Setup() {
		_pageReader = Substitute.For<IProcessPageReader>();
		_logger = Substitute.For<ILogger>();
		_command = new ProcessPageFactsCommand(_pageReader, _logger);
	}

	private void StubPage(PageGetResponse page) {
		_pageReader.TryGetPage(Arg.Any<PageGetOptions>(), out Arg.Any<PageGetResponse>())
			.Returns(call => {
				call[1] = page;
				return page.Success;
			});
	}

	private static ProcessPageFactsOptions Options() => new() {
		SchemaName = "UsrRequest_FormPage", Environment = "dev"
	};

	[Test]
	[Description("A get-page failure passes through as data — the error text reaches the caller and nothing throws.")]
	public void TryGetFacts_ShouldPassThroughAPageReadFailure() {
		// Arrange
		StubPage(new PageGetResponse { Success = false, Error = "page not found on this environment" });

		// Act
		bool success = _command.TryGetFacts(Options(), out ProcessPageFactsResponse response);

		// Assert
		success.Should().BeFalse();
		response.Error.Should().Contain("page not found");
		response.SchemaName.Should().Be("UsrRequest_FormPage");
	}

	[Test]
	[Description("A page whose schema type resolves to MOBILE is refused, and the refusal says mobile — the old doc claimed only Classic pages were refused, which misled callers pointing this at a mobile page.")]
	public void TryGetFacts_ShouldRefuseAMobilePage() {
		// Arrange
		StubPage(new PageGetResponse {
			Success = true,
			Page = new PageMetadataInfo { SchemaName = "UsrRequest_FormPage", SchemaType = "mobile" },
			Bundle = new PageBundleInfo()
		});

		// Act
		bool success = _command.TryGetFacts(Options(), out ProcessPageFactsResponse response);

		// Assert
		success.Should().BeFalse();
		response.Error.Should().Contain("MOBILE");
	}

	[Test]
	[Description("An UNKNOWN schema type is not refused outright: the numeric type maps a Classic page AND a platform that omitted the value to the same label, so the guard falls back to body inference. A body that is not mobile JSON reads as web, and the facts are produced — refusing on 'the platform did not tell us' would turn one absent field into a refusal for every page on that environment.")]
	public void TryGetFacts_ShouldFallBackToBodyInferenceWhenTheSchemaTypeIsUnknown() {
		// Arrange — unknown label, but a body that is clearly a web (Angular) schema, not mobile JSON.
		StubPage(new PageGetResponse {
			Success = true,
			Page = new PageMetadataInfo { SchemaName = "UsrRequest_FormPage", SchemaType = "unknown" },
			Raw = new PageRawInfo { Body = "define(\"UsrRequest_FormPage\", [], function() {});" },
			Bundle = new PageBundleInfo()
		});

		// Act
		bool success = _command.TryGetFacts(Options(), out ProcessPageFactsResponse response);

		// Assert
		success.Should().BeTrue(because: "an unresolved type with a web-shaped body is a readable Freedom UI page");
	}

	[Test]
	[Description("An unknown schema type with NO body evidence is refused, and the refusal names the real possibility — a Classic UI page reads back exactly this way — instead of printing the label 'unknown', which the old message did and no doc had promised.")]
	public void TryGetFacts_ShouldRefuseWhenTheTypeCannotBeEstablished() {
		// Arrange
		StubPage(new PageGetResponse {
			Success = true,
			Page = new PageMetadataInfo { SchemaName = "UsrRequest_FormPage", SchemaType = "unknown" },
			Bundle = new PageBundleInfo()
		});

		// Act
		bool success = _command.TryGetFacts(Options(), out ProcessPageFactsResponse response);

		// Assert
		success.Should().BeFalse();
		response.Error.Should().Contain("Classic UI page");
	}

	[Test]
	[Description("A successful read filters to completing candidates and carries no warning when candidates exist.")]
	public void TryGetFacts_ShouldProjectCandidatesFromTheBundle() {
		// Arrange
		StubPage(new PageGetResponse {
			Success = true,
			Page = new PageMetadataInfo { SchemaName = "UsrRequest_FormPage", SchemaType = "web" },
			Bundle = new PageBundleInfo {
				ViewConfig = [new JsonObject {
					["type"] = "crt.Button",
					["name"] = "SaveButton",
					["caption"] = "Save",
					["clicked"] = new JsonObject { ["request"] = "crt.SaveRecordRequest" }
				}]
			}
		});

		// Act
		bool success = _command.TryGetFacts(Options(), out ProcessPageFactsResponse response);

		// Assert
		success.Should().BeTrue();
		response.CompletingButtonCandidates.Should().ContainSingle()
			.Which.Name.Should().Be("SaveButton");
		response.Warnings.Should().BeNull(because: "a page with candidates is the clean, ordinary answer");
	}

	[Test]
	[Description("A page that PASSES the web guard but yields no candidates is flagged, not returned as a clean empty list: that state is ambiguous between 'the page has no buttons' and 'the bundle's shape was not recognised', and an element built without a completing button can never finish at run time.")]
	public void TryGetFacts_ShouldWarnWhenNoCandidateWasFound() {
		// Arrange — a readable web page whose bundle carries no buttons at all.
		StubPage(new PageGetResponse {
			Success = true,
			Page = new PageMetadataInfo { SchemaName = "UsrRequest_FormPage", SchemaType = "web" },
			Bundle = new PageBundleInfo()
		});

		// Act
		bool success = _command.TryGetFacts(Options(), out ProcessPageFactsResponse response);

		// Assert
		success.Should().BeTrue(because: "an empty page is not an error — but it must not read as a clean answer");
		response.CompletingButtonCandidates.Should().BeEmpty();
		response.Warnings.Should().ContainSingle()
			.Which.Should().Contain("can never finish at run time");
	}
}
