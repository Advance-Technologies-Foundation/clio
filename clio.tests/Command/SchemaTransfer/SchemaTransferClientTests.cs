using System;
using System.Collections.Generic;
using Clio.Command.SchemaTransfer;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.SchemaTransfer;

/// <summary>
/// Covers the gate JSON/HTTP boundary of <see cref="SchemaTransferClient"/>.
/// </summary>
/// <remarks>
/// <c>Post&lt;T&gt;</c> is the only place the gate envelope becomes behavior, and the binding is load-bearing:
/// <c>Clio.Common.Responses.BaseResponse</c> annotates <c>success</c>/<c>errorInfo</c> with
/// <c>[DataMember(Name = ...)]</c>, which <c>System.Text.Json</c> ignores entirely — the envelope binds ONLY
/// because the client sets <c>PropertyNameCaseInsensitive = true</c>. If that coupling were ever broken,
/// <c>Success</c> would silently deserialize to <c>false</c> AFTER the environment already did the work, and the
/// operator would be told the call failed. These tests pin the coupling and the ambiguity message the
/// requirement (R7/AC3) rests on, which is produced by the SERVER and only surfaced here.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class SchemaTransferClientTests {

	private const string FindLayersUrl = "http://local/rest/CreatioApiGateway/FindSchemaLayers";
	private const string ExportUrl = "http://local/rest/CreatioApiGateway/ExportSchema";
	private const string ImportUrl = "http://local/rest/CreatioApiGateway/ImportSchema";
	private const string SchemaName = "UsrProbeSchema";
	private const string PackageName = "UsrProbePackage";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private SchemaTransferClient _sut;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.FindSchemaLayers).Returns(FindLayersUrl);
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ExportSchema).Returns(ExportUrl);
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ImportSchema).Returns(ImportUrl);
		_sut = new SchemaTransferClient(_applicationClient, _serviceUrlBuilder);
	}

	[Test]
	[Description("Binds success and the layer list of a FindSchemaLayers envelope through the case-insensitive options")]
	public void FindLayers_Should_Bind_Envelope_And_Layers() {
		// Arrange
		StubResponse(FindLayersUrl, """
			{"success":true,"layers":[
			{"schemaId":"9f5e8dd4-9d9c-4a4f-9a7b-1b6a5f0f1f11","schemaUId":"8375dacb-4ea5-4103-b07a-d365f8d276f3",
			"schemaName":"UsrProbeSchema","caption":"Probe","managerName":"SourceCodeSchemaManager",
			"packageName":"UsrProbePackage","packageUId":"0d2e1c4c-2b7c-4a58-8a1a-2c0d3d3f6a12"}]}
			""");

		// Act
		IReadOnlyList<SchemaLayerDto> layers = _sut.FindLayers(SchemaName, null);

		// Assert
		layers.Should().HaveCount(1,
			because: "the envelope carries exactly one layer and `success` must bind to true, or the call would throw");
		layers[0].PackageName.Should().Be(PackageName,
			because: "the create-versus-replace decision is made from the package the layer belongs to");
		layers[0].ManagerName.Should().Be("SourceCodeSchemaManager",
			because: "a name is unique only per (manager, package), so the manager is part of the layer identity");
		_applicationClient.Received(1).ExecutePostRequest(FindLayersUrl, Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Surfaces the platform message verbatim when the gate refuses an ambiguous export")]
	public void Export_Should_Surface_Platform_Ambiguity_Message_Verbatim() {
		// Arrange — the body the gate's `layers.Count > 1` branch actually produces.
		const string platformMessage =
			"Schema 'UsrProbeSchema' matches 2 layers: 'UsrProbePackage' (SourceCodeSchemaManager), "
			+ "'UsrProbePackage' (AddonSchemaManager). They all live in the same package, so specify the "
			+ "manager (--manager-name) to disambiguate.";
		StubResponse(ExportUrl, $$"""
			{"success":false,"errorInfo":{"message":"{{platformMessage}}"},"candidates":[
			{"schemaName":"UsrProbeSchema","managerName":"SourceCodeSchemaManager","packageName":"UsrProbePackage"},
			{"schemaName":"UsrProbeSchema","managerName":"AddonSchemaManager","packageName":"UsrProbePackage"}]}
			""");

		// Act
		Action act = () => _sut.Export(SchemaName, null, null);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage(platformMessage,
				because: "AC3 requires the ambiguity to reach the operator with the candidate layers named; "
					+ "replacing it with the generic fallback would hide the only information that resolves the retry");
	}

	[Test]
	[Description("Falls back to the client's own message when the gate reports failure with no platform message")]
	public void Export_Should_Fall_Back_When_Platform_Reports_No_Message() {
		// Arrange
		StubResponse(ExportUrl, """{"success":false,"errorInfo":{"message":"   "}}""");

		// Act
		Action act = () => _sut.Export(SchemaName, null, null);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage($"Could not export schema '{SchemaName}'.",
				because: "a failure with no platform text must still name the operation that failed");
	}

	[Test]
	[Description("Returns the verbatim payload and the identity of the exported layer on success")]
	public void Export_Should_Return_Payload_And_Layer_Identity() {
		// Arrange
		StubResponse(ExportUrl, """
			{"success":true,
			"schema":{"schemaUId":"8375dacb-4ea5-4103-b07a-d365f8d276f3","schemaName":"UsrProbeSchema",
			"managerName":"SourceCodeSchemaManager","packageName":"UsrProbePackage"},
			"schemaData":"{\"Name\":\"UsrProbeSchema\",\"UId\":\"8375dacb-4ea5-4103-b07a-d365f8d276f3\"}"}
			""");

		// Act
		(SchemaLayerDto schema, string schemaData) = _sut.Export(SchemaName, PackageName, null);

		// Assert
		schema.SchemaUId.Should().Be("8375dacb-4ea5-4103-b07a-d365f8d276f3",
			because: "the UId is the identity the bundle descriptor records and R6/AC4 preserves across environments");
		schemaData.Should().Be("""{"Name":"UsrProbeSchema","UId":"8375dacb-4ea5-4103-b07a-d365f8d276f3"}""",
			because: "the payload is the only thing import consumes and must survive the round trip byte-for-byte");
	}

	[Test]
	[Description("Refuses a success envelope that carries no payload rather than writing an empty bundle")]
	public void Export_Should_Throw_When_Success_Carries_No_Payload() {
		// Arrange
		StubResponse(ExportUrl, """{"success":true,"schemaData":""}""");

		// Act
		Action act = () => _sut.Export(SchemaName, PackageName, null);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage($"*returned no payload for schema '{SchemaName}'*",
				because: "an empty payload would be written to disk as a bundle that import silently cannot apply");
	}

	[Test]
	[Description("Names the route and the cliogate requirement when the environment returns a non-JSON body")]
	public void Post_Should_Explain_A_Non_Json_Body() {
		// Arrange — what an auth redirect or a WCF error page actually returns.
		StubResponse(FindLayersUrl, "<html><body>Object moved to here.</body></html>");

		// Act
		Action act = () => _sut.FindLayers(SchemaName, null);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage($"*{FindLayersUrl}*did not return a JSON response*cliogate 2.0.0.46*",
				because: "raw HTML is useless in a CLI or MCP transcript; the route and the gate version are the "
					+ "two things that let the operator fix it");
	}

	[Test]
	[Description("Reports an empty response body as such instead of dereferencing null")]
	public void Post_Should_Report_An_Empty_Response_Body() {
		// Arrange
		StubResponse(FindLayersUrl, "null");

		// Act
		Action act = () => _sut.FindLayers(SchemaName, null);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage($"{FindLayersUrl} returned an empty response.",
				because: "a null envelope must not surface as a NullReferenceException from the caller's stack");
	}

	[Test]
	[Description("Posts the payload and the target package to the import route unchanged")]
	public void Import_Should_Post_Payload_And_Package_To_The_Import_Route() {
		// Arrange
		const string payload = """{"Name":"UsrProbeSchema","UId":"8375dacb-4ea5-4103-b07a-d365f8d276f3"}""";
		StubResponse(ImportUrl, """{"success":true,"importResult":"Schema imported."}""");

		// Act
		string importResult = _sut.Import(payload, PackageName);

		// Assert
		importResult.Should().Be("Schema imported.",
			because: "the platform importer's own diagnostic is what the command reports back to the operator");
		_applicationClient.Received(1).ExecutePostRequest(
			ImportUrl,
			Arg.Is<string>(body => body.Contains("\"schemaData\"") && body.Contains("\"packageName\"")
				&& body.Contains(PackageName)),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	private void StubResponse(string url, string body) =>
		_applicationClient
			.ExecutePostRequest(url, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(body);
}
