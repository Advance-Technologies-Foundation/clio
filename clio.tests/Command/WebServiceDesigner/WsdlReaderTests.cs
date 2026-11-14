using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.WebServiceDesigner;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command.WebServiceDesigner;

/// <summary>read-wsdl end to end inside clio: the two sources, the checks before anything is read, and the result.</summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class WsdlReaderTests {

	private static readonly Guid PackageUId = Guid.Parse("6d7929ef-24da-1079-a4e8-dfb3c988735c");

	private IWebServiceDesignerClient _client;
	private ICreatioRightsClient _rights;
	private IWebServicePrefixReader _prefix;
	private IWebServiceDesigner _designer;
	private MockFileSystem _fileSystem;
	private WsdlReader _sut;

	[SetUp]
	public void SetUp() {
		_client = Substitute.For<IWebServiceDesignerClient>();
		_rights = Substitute.For<ICreatioRightsClient>();
		_prefix = Substitute.For<IWebServicePrefixReader>();
		_designer = Substitute.For<IWebServiceDesigner>();
		_rights.GetCanExecuteOperation("CanManageSolution", Arg.Any<CreatioRequestOptions>()).Returns(true);
		_prefix.Read().Returns("Usr");
		_client.IsSchemaNameUnique(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		_fileSystem = new MockFileSystem();
		_sut = new WsdlReader(_client, _rights, _prefix, _designer, new WsdlParser(), _fileSystem);
	}

	private static byte[] FixtureBytes(params string[] parts) =>
		File.ReadAllBytes(Path.Combine([TestContext.CurrentContext.TestDirectory, "Examples", "Wsdl", .. parts]));

	private string GivenFile(string name, byte[] content) {
		string path = _fileSystem.Path.Combine(_fileSystem.Path.GetTempPath(), "wsdl", name);
		_fileSystem.AddFile(path, new MockFileData(content));
		return path;
	}

	private string GivenFixture(params string[] parts) => GivenFile(parts[^1], FixtureBytes(parts));

	private static WsdlReadRequest Files(params string[] paths) => new("dev", null, paths);

	private static WsdlRegistrationArgs Arguments(WsdlServiceView service) =>
		service.Arguments!.Value.Deserialize<WsdlRegistrationArgs>();

	[Test]
	[Description("wsdl-files: one argument object per SOAP port, the SOAP 1.2 port marked as Studio's pick, nothing null written, no download and no save.")]
	public void Read_Should_Return_One_Argument_Object_Per_Soap_Port() {
		// Arrange
		string path = GivenFixture("calculator.wsdl");

		// Act
		WsdlReadResult result = _sut.Read(Files(path));

		// Assert
		result.Success.Should().BeTrue(because: $"a readable WSDL gives a result; error: {result.Error}");
		result.Services.Select(s => (s.Port, s.StudioPick)).Should().Equal([("CalculatorSoap", false), ("CalculatorSoap12", true)],
			because: "every SOAP port is returned and Studio's pick is marked (D12)");
		Arguments(result.Services[1]).Type.Should().Be("soap12", because: "the port's SOAP version is the service type");
		result.Services[0].Arguments!.Value.GetRawText().Should().NotContain(":null",
			because: "members that do not apply are left out, not written as null");
		JsonNode.Parse(result.Services[0].Arguments!.Value.GetRawText())!["methods"]!.AsArray()
			.Should().OnlyContain(m => m!["uri"] == null && m["http-method"] == null, because: "a SOAP method has no uri or verb");
		_client.DidNotReceiveWithAnyArgs().DownloadWsdl(default);
		_client.DidNotReceiveWithAnyArgs().SaveSchema(default);
	}

	[TestCase("calculator.wsdl")]
	[TestCase("wizard-features.wsdl")]
	[Description("Every port's arguments, with package-name added, pass register-web-service's own validation and save, as an agent would send them.")]
	public void Read_Output_Should_Be_Valid_Register_Web_Service_Input(string fixture) {
		// Arrange
		string path = GivenFixture(fixture);
		RegisterWebServiceTool register = RegisterTool(out Func<ServiceSchemaDto> saved);

		// Act
		WsdlReadResult result = _sut.Read(Files(path));

		// Assert
		foreach (WsdlServiceView service in result.Services) {
			JsonObject arguments = JsonNode.Parse(service.Arguments!.Value.GetRawText())!.AsObject();
			arguments["package-name"] = "Custom";
			RegisterWebServiceArgs args = arguments.Deserialize<RegisterWebServiceArgs>()!;
			WebServiceToolResult registered = register.RegisterWebService(args);
			registered.Success.Should().BeTrue(
				because: $"read-wsdl output for port {service.Port} must be valid register input; error: {registered.Error}");
			saved().Methods.Items.Values.Should().OnlyContain(m => m.Request.HttpMethodType == ServiceSchemaCodes.HttpMethods["POST"],
				because: "the register path builds SOAP methods");
		}
	}

	[Test]
	[Description("A SOAP-header array and an object array from the WSDL are saved with the members the platform renders them by: the header flag, prefixed item elements and items.")]
	public void Read_Output_Should_Save_Headers_And_Arrays_As_The_Platform_Renders_Them() {
		// Arrange
		string path = GivenFixture("wizard-features.wsdl");
		RegisterWebServiceTool register = RegisterTool(out Func<ServiceSchemaDto> saved);
		WsdlReadResult result = _sut.Read(Files(path) with { Port = "ClioWizardFeaturesSoap", Operations = ["TaggedCall", "GetByRoles"] });
		JsonObject arguments = JsonNode.Parse(result.Services.Single().Arguments!.Value.GetRawText())!.AsObject();
		arguments["package-name"] = "Custom";

		// Act
		WebServiceToolResult registered = register.RegisterWebService(arguments.Deserialize<RegisterWebServiceArgs>()!);

		// Assert
		registered.Success.Should().BeTrue(because: $"the trimmed arguments are valid; error: {registered.Error}");
		ServiceMethodDto tagged = saved().Methods.Items.Values.Single(m => m.Name == "UsrTaggedCall");
		ServiceParameterDto tags = tagged.Request.Parameters.Items.Values.Single();
		(tags.IsSoapHeader, tags.IsArray, tags.SequenceElementName, tags.Path).Should().Be((true, true, "tem:Tag", "tem:TagsHeader"),
			because: "the platform renders the header array by these members");
		tagged.Request.Uri.Should().Be("tem:TaggedCall", because: "the message element is stored where the platform reads it");
		ServiceParameterDto roles = saved().Methods.Items.Values.Single(m => m.Name == "UsrGetByRoles").Request.Parameters.Items.Values
			.Single(p => p.IsArray);
		(roles.DataValueTypeName, roles.SequenceElementName, roles.ItemProperties.Items.Count).Should().Be(("Object", "ns0:RoleObject", 3),
			because: "an object array keeps its item element and fields");
	}

	private RegisterWebServiceTool RegisterTool(out Func<ServiceSchemaDto> saved) {
		IWebServiceDesignerClient client = Substitute.For<IWebServiceDesignerClient>();
		client.IsSchemaNameUnique(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		client.FindExistingSysSettingCodes(Arg.Any<IReadOnlyCollection<string>>())
			.Returns(call => call.Arg<IReadOnlyCollection<string>>());
		client.Select("SysPackage", Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>()).Returns([new JsonObject { ["UId"] = PackageUId.ToString() }]);
		ServiceSchemaDto last = null;
		client.When(c => c.SaveSchema(Arg.Any<ServiceSchemaDto>())).Do(call => last = call.Arg<ServiceSchemaDto>());
		client.ReadSchema(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(_ => last);
		var designer = new Clio.Command.WebServiceDesigner.WebServiceDesigner(client, _rights, _prefix);
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IWebServiceDesigner>(Arg.Any<EnvironmentOptions>()).Returns(designer);
		saved = () => last;
		return new RegisterWebServiceTool(resolver);
	}

	[Test]
	[Description("wsdl-url: the platform downloads the WSDL (and its schemas) through its own endpoint; clio reads no file and sends the URL as given.")]
	public void Read_Should_Download_A_Url_Through_The_Platform() {
		// Arrange
		const string url = "http://www.dneonline.com/calculator.asmx?WSDL";
		_client.DownloadWsdl(url).Returns([new WsdlSourceFile(url, FixtureBytes("calculator.wsdl"), AsciiText: true)]);

		// Act
		WsdlReadResult result = _sut.Read(new WsdlReadRequest("dev", url, null));

		// Assert
		result.Services.Should().HaveCount(2, because: "the downloaded calculator WSDL has two SOAP ports");
		_client.Received(1).DownloadWsdl(url);
	}

	[TestCase("ftp://example.org/x.wsdl", "absolute http or https")]
	[TestCase("x.wsdl", "absolute http or https")]
	[TestCase("http://user:secret@example.org/x.wsdl", "credentials")]
	[Description("A wsdl-url that is not an absolute http(s) URL, or that carries credentials, is refused before anything is sent.")]
	public void Read_Should_Refuse_A_Bad_Url(string url, string reason) {
		// Arrange / Act
		Action act = () => _sut.Read(new WsdlReadRequest("dev", url, null));

		// Assert
		act.Should().Throw<ArgumentException>(because: "the URL is checked first").WithMessage($"*{reason}*");
		_client.DidNotReceiveWithAnyArgs().DownloadWsdl(default);
	}

	[Test]
	[Description("wsdl-url and wsdl-files together, or neither, are refused.")]
	public void Read_Should_Need_Exactly_One_Source() {
		// Arrange / Act
		Action both = () => _sut.Read(new WsdlReadRequest("dev", "http://example.org/x?wsdl", ["/x.wsdl"]));
		Action neither = () => _sut.Read(new WsdlReadRequest("dev", null, null));

		// Assert
		both.Should().Throw<ArgumentException>(because: "one source at a time").WithMessage("*not both*");
		neither.Should().Throw<ArgumentException>(because: "a source is needed").WithMessage("*not both and not neither*");
	}

	[Test]
	[Description("Without CanManageSolution, or when the schema-name prefix cannot be read, nothing is downloaded.")]
	public void Read_Should_Check_Rights_And_Prefix_First() {
		// Arrange
		_rights.GetCanExecuteOperation("CanManageSolution", Arg.Any<CreatioRequestOptions>()).Returns(false);
		var request = new WsdlReadRequest("dev", "http://example.org/x?wsdl", null);

		// Act
		Action noRights = () => _sut.Read(request);

		// Assert
		noRights.Should().Throw<UnauthorizedAccessException>(because: "Studio's WSDL download needs CanManageSolution");
		_rights.GetCanExecuteOperation("CanManageSolution", Arg.Any<CreatioRequestOptions>()).Returns(true);
		_prefix.Read().Throws(new InvalidOperationException("The SchemaNamePrefix system setting could not be read"));
		((Action)(() => _sut.Read(request))).Should().Throw<InvalidOperationException>(
			because: "codes cannot be built without the prefix (D11)");
		_client.DidNotReceiveWithAnyArgs().DownloadWsdl(default);
	}

	[Test]
	[Description("wsdl-files must be existing absolute paths, at most 100 of at most 10 MB each.")]
	public void Read_Should_Validate_Local_Files() {
		// Arrange
		string big = GivenFile("big.wsdl", new byte[WsdlReader.MaxFileBytes + 1]);
		string missing = _fileSystem.Path.Combine(_fileSystem.Path.GetTempPath(), "wsdl", "missing.wsdl");
		string[] many = Enumerable.Range(0, WsdlReader.MaxFiles + 1).Select(i => missing).ToArray();

		// Act / Assert
		((Action)(() => _sut.Read(Files("relative.wsdl")))).Should().Throw<ArgumentException>(because: "the server's folder is not the user's")
			.WithMessage("*absolute local path*");
		((Action)(() => _sut.Read(Files(missing)))).Should().Throw<ArgumentException>(because: "the file must exist")
			.WithMessage("*does not exist*");
		((Action)(() => _sut.Read(Files(big)))).Should().Throw<ArgumentException>(because: "the platform's own size cap applies")
			.WithMessage("*larger than 10 MB*");
		((Action)(() => _sut.Read(Files(many)))).Should().Throw<ArgumentException>(because: "the platform's own count cap applies")
			.WithMessage("*at most 100 files*");
	}

	[Test]
	[Description("The WSDL and its imported schemas given as local files are merged; an import by URL finds the file of that name.")]
	public void Read_Should_Merge_Local_Schema_Files() {
		// Arrange
		string[] paths = [GivenFixture("split", "orders.wsdl"), GivenFixture("split", "orders-operations.xsd"),
			GivenFixture("split", "orders-common.xsd"), GivenFixture("split", "orders-money.xsd")];

		// Act
		WsdlReadResult result = _sut.Read(Files(paths));

		// Assert
		WebServiceMethodArg method = Arguments(result.Services.Single()).Methods.Single();
		method.ResponseParameters.Select(p => p.Path).Should().Contain("tem:total/ns0:amount",
			because: "a type from a schema imported by an imported schema is read");
	}

	[Test]
	[Description("port picks one port, operations keeps the named ones; an unknown port or operation is refused with the names there are.")]
	public void Read_Should_Select_Port_And_Operations() {
		// Arrange
		string path = GivenFixture("calculator.wsdl");

		// Act
		WsdlReadResult result = _sut.Read(Files(path) with { Port = "CalculatorSoap", Operations = ["add"] });
		Action unknownPort = () => _sut.Read(Files(path) with { Port = "CalculatorHttp" });
		Action unknownOperation = () => _sut.Read(Files(path) with { Operations = ["Power"] });

		// Assert
		result.Services.Should().ContainSingle(because: "one port was asked for")
			.Which.Port.Should().Be("CalculatorSoap", because: "the port named is returned");
		Arguments(result.Services[0]).Methods.Select(m => m.Caption).Should().Equal(["Add"],
			because: "operation names match case-insensitively");
		unknownPort.Should().Throw<ArgumentException>(because: "the port does not exist")
			.WithMessage("*CalculatorSoap, CalculatorSoap12*");
		unknownOperation.Should().Throw<ArgumentException>(because: "the operation does not exist")
			.WithMessage("*Power*Add, Subtract, Multiply, Divide*");
	}

	[Test]
	[Description("A WSDL too large to return whole comes back as operation names per port, with a notice to read it again with operations.")]
	public void Read_Should_List_Operation_Names_When_The_Result_Is_Too_Large() {
		// Arrange
		var operations = new StringBuilder();
		var messages = new StringBuilder();
		var types = new StringBuilder();
		var binding = new StringBuilder();
		for (int i = 0; i < 300; i++) {
			types.Append($"<xs:element name=\"Op{i}\"><xs:complexType><xs:sequence><xs:element name=\"firstValue\" type=\"xs:string\" /><xs:element name=\"secondValue\" type=\"xs:string\" /><xs:element name=\"thirdValue\" type=\"xs:int\" /></xs:sequence></xs:complexType></xs:element>");
			types.Append($"<xs:element name=\"Op{i}Response\"><xs:complexType><xs:sequence><xs:element name=\"result\" type=\"xs:string\" /></xs:sequence></xs:complexType></xs:element>");
			messages.Append($"<wsdl:message name=\"Op{i}In\"><wsdl:part name=\"p\" element=\"tns:Op{i}\" /></wsdl:message><wsdl:message name=\"Op{i}Out\"><wsdl:part name=\"p\" element=\"tns:Op{i}Response\" /></wsdl:message>");
			operations.Append($"<wsdl:operation name=\"Op{i}\"><wsdl:input message=\"tns:Op{i}In\" /><wsdl:output message=\"tns:Op{i}Out\" /></wsdl:operation>");
			binding.Append($"<wsdl:operation name=\"Op{i}\"><soap:operation soapAction=\"urn:op{i}\" /></wsdl:operation>");
		}
		string xml = $"""
			<wsdl:definitions xmlns:wsdl="http://schemas.xmlsoap.org/wsdl/" xmlns:soap="http://schemas.xmlsoap.org/wsdl/soap/"
				xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:tns="urn:big" targetNamespace="urn:big">
				<wsdl:types><xs:schema targetNamespace="urn:big" elementFormDefault="qualified">{types}</xs:schema></wsdl:types>
				{messages}<wsdl:portType name="P">{operations}</wsdl:portType>
				<wsdl:binding name="B" type="tns:P"><soap:binding transport="http://schemas.xmlsoap.org/soap/http" />{binding}</wsdl:binding>
				<wsdl:service name="Big"><wsdl:port name="Soap" binding="tns:B"><soap:address location="http://localhost/big" /></wsdl:port></wsdl:service>
			</wsdl:definitions>
			""";
		string path = GivenFile("big.wsdl", Encoding.UTF8.GetBytes(xml));

		// Act
		WsdlReadResult summary = _sut.Read(Files(path));
		WsdlReadResult filtered = _sut.Read(Files(path) with { Operations = ["Op7"] });

		// Assert
		WsdlServiceView port = summary.Services.Single();
		port.Arguments.Should().BeNull(because: "the whole result would be too large for the agent");
		port.Operations.Should().HaveCount(300, because: "every operation name is listed");
		summary.Notices.Should().Contain(n => n.Contains("operations"), because: "the agent is told how to read the rest");
		filtered.Services.Single().Arguments.Should().NotBeNull(because: "a filtered read returns arguments");
	}

	[TestCase(System.IO.FileAttributes.Device)]
	[TestCase(System.IO.FileAttributes.ReparsePoint)]
	[Description("A device or a reparse point (a link) given as a WSDL file is refused before it is opened.")]
	public void Read_Should_Refuse_A_Special_File(System.IO.FileAttributes attributes) {
		// Arrange
		string path = _fileSystem.Path.Combine(_fileSystem.Path.GetTempPath(), "wsdl", "special.wsdl");
		_fileSystem.AddFile(path, new MockFileData(FixtureBytes("calculator.wsdl")) { Attributes = attributes });

		// Act
		Action act = () => _sut.Read(Files(path));

		// Assert
		act.Should().Throw<ArgumentException>(because: "only a regular file is read").WithMessage("*not a regular file*");
	}

	[Test]
	[Description("An empty file is refused before it is opened: a FIFO or a character device looks like one, and opening a FIFO blocks.")]
	public void Read_Should_Refuse_An_Empty_File() {
		// Arrange
		string path = GivenFile("fifo.wsdl", []);

		// Act
		Action act = () => _sut.Read(Files(path));

		// Assert
		act.Should().Throw<ArgumentException>(because: "nothing to read, and the open could block").WithMessage("*empty or not a regular file*");
	}

	private static string InlineWsdl(string fields) => $"""
		<wsdl:definitions xmlns:wsdl="http://schemas.xmlsoap.org/wsdl/" xmlns:soap="http://schemas.xmlsoap.org/wsdl/soap/"
			xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:tns="http://example.org/t/" targetNamespace="http://example.org/t/">
			<wsdl:types><xs:schema elementFormDefault="qualified" targetNamespace="http://example.org/t/">
				<xs:element name="Op"><xs:complexType><xs:sequence>{fields}</xs:sequence></xs:complexType></xs:element>
				<xs:element name="OpResponse"><xs:complexType><xs:sequence /></xs:complexType></xs:element>
			</xs:schema></wsdl:types>
			<wsdl:message name="OpIn"><wsdl:part name="p" element="tns:Op" /></wsdl:message>
			<wsdl:message name="OpOut"><wsdl:part name="p" element="tns:OpResponse" /></wsdl:message>
			<wsdl:portType name="P"><wsdl:operation name="Op"><wsdl:input message="tns:OpIn" /><wsdl:output message="tns:OpOut" /></wsdl:operation></wsdl:portType>
			<wsdl:binding name="B" type="tns:P"><soap:binding transport="http://schemas.xmlsoap.org/soap/http" />
				<wsdl:operation name="Op"><soap:operation soapAction="urn:Op" /><wsdl:input /></wsdl:operation></wsdl:binding>
			<wsdl:service name="Svc"><wsdl:port name="Soap" binding="tns:B"><soap:address location="http://localhost/svc" /></wsdl:port></wsdl:service>
		</wsdl:definitions>
		""";

	[Test]
	[Description("A result whose paths and captions alone pass the result limit is listed by operation name before any port is mapped: twenty ports on one binding would otherwise copy the same long paths once per port.")]
	public void Read_Should_List_Names_Only_When_The_Paths_Alone_Are_Too_Long() {
		// Arrange
		var types = new StringBuilder("<xs:complexType name=\"Leaves\"><xs:sequence>");
		types.Append(string.Concat(Enumerable.Range(0, 200).Select(i => $"<xs:element name=\"f{i}\" type=\"xs:string\" />")));
		types.Append("</xs:sequence></xs:complexType>");
		for (int level = 1; level < 5; level++) {
			string next = level + 1 < 5 ? $"tns:C{level + 1}" : "tns:Leaves";
			types.Append($"<xs:complexType name=\"C{level}\"><xs:sequence><xs:element name=\"w{level}{new string('a', 995)}\" "
				+ $"type=\"{next}\" /></xs:sequence></xs:complexType>");
		}
		string fields = $"<xs:element name=\"w0{new string('a', 995)}\" type=\"tns:C1\" />";
		string ports = string.Concat(Enumerable.Range(0, 20).Select(i =>
			$"<wsdl:port name=\"Soap{i}\" binding=\"tns:B\"><soap:address location=\"http://localhost/svc\" /></wsdl:port>"));
		string wsdl = InlineWsdl(fields)
			.Replace("<xs:element name=\"Op\">", types + "<xs:element name=\"Op\">")
			.Replace("<wsdl:port name=\"Soap\" binding=\"tns:B\"><soap:address location=\"http://localhost/svc\" /></wsdl:port>", ports);
		string path = GivenFile("ports.wsdl", Encoding.UTF8.GetBytes(wsdl));
		long before = GC.GetAllocatedBytesForCurrentThread();

		// Act
		WsdlReadResult result = _sut.Read(Files(path) with { Port = null });
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		// Assert
		result.Services.Should().OnlyContain(s => s.Arguments == null, because: "nothing is mapped when the paths alone are too long");
		result.Notices.Should().Contain(n => n.Contains("too large"), because: "the agent is told why");
		allocated.Should().BeLessThan(150_000_000,
			because: "mapping and serializing each of the twenty ports copied the same 2 million characters of paths twenty times");
	}

	[TestCase(@"\??\UNC\host\share\a.wsdl")]
	[TestCase(@"\??\GLOBALROOT\Device\Mup\host\share\a.wsdl")]
	[Description("An NT path that reaches a network share is refused like a UNC path, before anything is opened.")]
	public void Read_Should_Refuse_An_Nt_Network_Path(string path) {
		// Arrange
		WsdlReadRequest request = Files(path);

		// Act
		Action act = () => _sut.Read(request);

		// Assert
		act.Should().Throw<ArgumentException>(because: "opening it would connect to the host with the user's credentials")
			.WithMessage("*must be an absolute local path*");
		_rights.DidNotReceiveWithAnyArgs().GetCanExecuteOperation(default, default);
	}

	[Test]
	[Description("Past the parameter limit the result lists operation names before anything is mapped or serialized.")]
	public void Read_Should_List_Names_Only_Past_The_Parameter_Limit() {
		// Arrange
		string fields = string.Concat(Enumerable.Range(0, WsdlReader.MaxMappedParameters + 1)
			.Select(i => $"<xs:element name=\"f{i}\" type=\"xs:string\" />"));
		string path = GivenFile("wide.wsdl", Encoding.UTF8.GetBytes(InlineWsdl(fields)));

		// Act
		WsdlReadResult result = _sut.Read(Files(path));

		// Assert
		result.Services.Single().Arguments.Should().BeNull(because: "nothing is mapped past the limit");
		result.Services.Single().Operations.Should().Equal(["Op"], because: "the operation names are listed");
		result.Notices.Should().Contain(n => n.Contains("too large"), because: "the agent is told why");
	}

	[Test]
	[Description("A platform download error is flattened and cut before it reaches the agent.")]
	public void Read_Should_Flatten_And_Cut_A_Download_Error() {
		// Arrange
		const string url = "http://example.org/calc?wsdl";
		_client.DownloadWsdl(url).Throws(new InvalidOperationException("first line\nsecond line " + new string('x', 2_000)));

		// Act
		Action act = () => _sut.Read(new WsdlReadRequest("dev", url, null));

		// Assert
		string message = act.Should().Throw<InvalidOperationException>(because: "the download failed").Which.Message;
		message.Should().NotContain("\n", because: "the platform's text is flattened");
		message.Length.Should().BeLessThan(1_000, because: "the platform's text is cut");
		message.Should().Contain("wsdl-files", because: "the way on is still given");
	}

	[TestCase("http://localhost/svc.asmx?wsdl")]
	[TestCase("http://LocalHost./svc.asmx?wsdl")]
	[TestCase("http://127.0.0.1:8080/svc.asmx?wsdl")]
	[TestCase("http://[::1]/svc.asmx?wsdl")]
	[TestCase("http://169.254.169.254/latest/meta-data")]
	[TestCase("http://[fe80::1]/svc.asmx?wsdl")]
	[TestCase("http://api.localhost/svc.asmx?wsdl")]
	[TestCase("http://0.0.0.0/svc.asmx?wsdl")]
	[TestCase("http://0.1.2.3/svc.asmx?wsdl")]
	[TestCase("http://[::127.0.0.1]/svc.asmx?wsdl")]
	[TestCase("http://[64:ff9b::a9fe:a9fe]/latest/meta-data")]
	[TestCase("http://[::ffff:169.254.169.254]/latest/meta-data")]
	[TestCase("http://metadata.google.internal/computeMetadata/v1/")]
	[TestCase("http://[fd00:ec2::254]/latest/meta-data")]
	[TestCase("http://100.100.100.200/latest/meta-data")]
	[Description("A loopback or link-local wsdl-url is refused before the stand is asked to download it, since the stand would read it on itself.")]
	public void Read_Should_Refuse_A_Stand_Local_Url(string url) {
		// Act
		Action act = () => _sut.Read(new WsdlReadRequest("dev", url, null));

		// Assert
		act.Should().Throw<ArgumentException>(because: "the stand must not be pointed at itself").WithMessage("*loopback or link-local*");
		_client.DidNotReceiveWithAnyArgs().DownloadWsdl(default);
	}

	[Test]
	[Description("A private network address stays allowed: an internal API is the common case.")]
	public void Read_Should_Allow_A_Private_Network_Url() {
		// Arrange
		const string url = "http://10.0.0.5/calc.asmx?wsdl";
		_client.DownloadWsdl(url).Returns([new WsdlSourceFile(url, FixtureBytes("calculator.wsdl"), AsciiText: true)]);

		// Act
		WsdlReadResult result = _sut.Read(new WsdlReadRequest("dev", url, null));

		// Assert
		result.Success.Should().BeTrue(because: $"a private address is read; error: {result.Error}");
		_client.Received(1).DownloadWsdl(url);
	}

	[TestCase("https://f.example.org/api/svc?code=SECRET&wsdl")]
	[TestCase("https://s.example.org/svc.asmx?wsdl&sig=SECRET")]
	[Description("A key in a query parameter common hosts use (an Azure Functions code, a SAS signature) is refused like any key.")]
	public void Read_Should_Refuse_A_Hosting_Key_In_The_Query(string url) {
		// Act
		Action act = () => _sut.Read(new WsdlReadRequest("dev", url, null));

		// Assert
		act.Should().Throw<ArgumentException>(because: "the URL travels to the stand and back").WithMessage("*key or token*")
			.Which.Message.Should().NotContain("SECRET", because: "the refusal does not repeat the key");
		_client.DidNotReceiveWithAnyArgs().DownloadWsdl(default);
	}

	[Test]
	[Description("A name the download turned into '?' and the parser left out still gives the warning to use wsdl-files.")]
	public void Read_Should_Warn_About_Replaced_Characters_The_Parser_Dropped() {
		// Arrange
		const string url = "http://example.org/calc?wsdl";
		string xml = Encoding.UTF8.GetString(FixtureBytes("calculator.wsdl"))
			.Replace("operation name=\"Add\"", "operation name=\"A?d\"");
		_client.DownloadWsdl(url).Returns([new WsdlSourceFile(url, Encoding.ASCII.GetBytes(xml), AsciiText: true)]);

		// Act
		WsdlReadResult result = _sut.Read(new WsdlReadRequest("dev", url, null));

		// Assert
		result.Warnings.Should().Contain(w => w.Contains("wsdl-files") && w.Contains("non-ASCII"),
			because: "the operation was left out for a '?' the download put there, not for a fault of the WSDL");
	}

	[Test]
	[Description("Warnings the parser left out are counted in the one count line of the result.")]
	public void Read_Should_Count_The_Warnings_The_Parser_Left_Out() {
		// Arrange
		string fields = string.Concat(Enumerable.Range(0, WsdlParser.MaxWarnings + 20)
			.Select(i => $"<xs:element name=\"f{i}\" type=\"tns:Missing{i}\" />"));
		string path = GivenFile("many.wsdl", Encoding.UTF8.GetBytes(InlineWsdl(fields)));

		// Act
		WsdlReadResult result = _sut.Read(Files(path));

		// Assert
		result.Warnings.Should().HaveCount(WarningLog.MaxKept + 1, because: "the kept warnings and one count line");
		result.Warnings[^1].Should().Be("...and 20 more warnings.", because: "the parser's dropped warnings are counted");
	}

	[Test]
	[Description("A result too large even for one operation of one port says so instead of asking to narrow it again.")]
	public void Read_Should_Say_When_One_Operation_Is_Too_Large() {
		// Arrange
		string fields = string.Concat(Enumerable.Range(0, 3_000)
			.Select(i => $"<xs:element name=\"aVeryLongFieldNameNumber{i}\" type=\"xs:string\" />"));
		string path = GivenFile("big.wsdl", Encoding.UTF8.GetBytes(InlineWsdl(fields)));

		// Act
		WsdlReadResult result = _sut.Read(new WsdlReadRequest("dev", null, [path], Port: "Soap", Operations: ["Op"]));
		int longest = result.Services.SelectMany(s => s.Operations).Max(n => n.Length);

		// Assert
		result.Services.Single().Operations.Should().Equal(["Op"], because: "the result lists the operation name only");
		result.Notices.Should().Contain(n => n.Contains("even for one operation"),
			because: "reading again with operations cannot make it smaller");
		longest.Should().BeLessThanOrEqualTo(101, because: "listed names are cut like any WSDL text");
	}

	[TestCase(@"\\host\share\x.wsdl")]
	[TestCase("//host/share/x.wsdl")]
	[Description("A network path is refused before any request: reading it would make the machine running clio connect to that host with the user's credentials.")]
	public void Read_Should_Refuse_A_Network_Path(string path) {
		// Arrange
		WsdlReadRequest request = Files(path);

		// Act
		Action act = () => _sut.Read(request);

		// Assert
		act.Should().Throw<ArgumentException>(because: "only local files are read").WithMessage("*network paths are not read*");
		_rights.DidNotReceiveWithAnyArgs().GetCanExecuteOperation(default, default);
	}

	[Test]
	[Description("Files that are each under the size cap but together over the total cap are refused.")]
	public void Read_Should_Cap_The_Total_Size_Of_Local_Files() {
		// Arrange
		long each = WsdlReader.MaxFileBytes - 1;
		int count = (int)(WsdlReader.MaxTotalBytes / each) + 1;
		string[] paths = Enumerable.Range(0, count).Select(i => GivenFile($"part{i}.xsd", new byte[each])).ToArray();

		// Act
		Action act = () => _sut.Read(Files(paths));

		// Assert
		act.Should().Throw<ArgumentException>(because: "the files hold more than a download may").WithMessage("*together*");
	}

	[Test]
	[Description("A wsdl-url with a key or token in its query is refused before anything is sent, because the URL travels to the stand and back in its messages.")]
	public void Read_Should_Refuse_A_Url_Carrying_A_Key() {
		// Arrange
		var request = new WsdlReadRequest("dev", "http://example.org/svc?wsdl&apikey=s3cr3t", null);

		// Act
		Action act = () => _sut.Read(request);

		// Assert
		act.Should().Throw<ArgumentException>(because: "the key would be echoed").WithMessage("*key or token*");
		_rights.DidNotReceiveWithAnyArgs().GetCanExecuteOperation(default, default);
	}

	[Test]
	[Description("A failed download passes on the platform's reason and points at wsdl-files.")]
	public void Read_Should_Point_At_Local_Files_When_The_Download_Fails() {
		// Arrange
		const string url = "http://example.org/svc?wsdl";
		_client.DownloadWsdl(url).Throws(
			new InvalidOperationException("The platform refused to download the WSDL: Error downloading schema"));

		// Act
		Action act = () => _sut.Read(new WsdlReadRequest("dev", url, null));

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "the stand could not download it")
			.WithMessage("*Error downloading schema*wsdl-files*");
	}

	[Test]
	[Description("A WSDL whose endpoint is on another host than the URL it was downloaded from gives a notice to show the base URI.")]
	public void Read_Should_Notice_An_Endpoint_On_Another_Host() {
		// Arrange
		const string url = "http://mirror.example.org/calc?wsdl";
		_client.DownloadWsdl(url).Returns([new WsdlSourceFile(url, FixtureBytes("calculator.wsdl"), AsciiText: true)]);

		// Act
		WsdlReadResult result = _sut.Read(new WsdlReadRequest("dev", url, null));

		// Assert
		result.Notices.Should().ContainSingle(n => n.Contains("www.dneonline.com") && n.Contains("mirror.example.org"),
			because: "the calls would go to the host the WSDL names");
	}

	[Test]
	[Description("A filtered read that is still too large lists operation names too, up to the listing limit.")]
	public void Read_Should_Apply_The_Size_Limit_To_A_Filtered_Read() {
		// Arrange
		string path = GivenFile("big.wsdl", Encoding.UTF8.GetBytes(BigWsdl(WsdlReader.MaxListedOperations + 10)));
		string[] all = Enumerable.Range(0, WsdlReader.MaxListedOperations + 10).Select(i => $"Op{i}").ToArray();

		// Act
		WsdlReadResult result = _sut.Read(Files(path) with { Operations = all });

		// Assert
		result.Services.Single().Arguments.Should().BeNull(because: "naming every operation does not lift the limit");
		result.Services.Single().Operations.Should().HaveCount(WsdlReader.MaxListedOperations + 1,
			because: "the names are listed up to the limit, plus a count line");
	}

	[Test]
	[Description("operations without port keeps, in each port, the operations it has; a name no port has is refused.")]
	public void Read_Should_Filter_Operations_Across_Ports() {
		// Arrange
		string path = GivenFixture("wizard-features.wsdl");

		// Act
		WsdlReadResult result = _sut.Read(Files(path) with { Operations = ["GetByRef"] });

		// Assert
		result.Services.Should().HaveCount(2, because: "both SOAP ports have the operation");
		result.Services.Should().OnlyContain(s => Arguments(s).Methods.Single().Caption == "GetByRef",
			because: "each port keeps the named operation only");
	}

	private static string BigWsdl(int operationCount, int ports = 1) {
		var operations = new StringBuilder();
		var messages = new StringBuilder();
		var types = new StringBuilder();
		var binding = new StringBuilder();
		for (int i = 0; i < operationCount; i++) {
			types.Append($"<xs:element name=\"Op{i}\"><xs:complexType><xs:sequence><xs:element name=\"firstValue\" type=\"xs:string\" /><xs:element name=\"secondValue\" type=\"xs:string\" /><xs:element name=\"thirdValue\" type=\"xs:int\" /></xs:sequence></xs:complexType></xs:element>");
			types.Append($"<xs:element name=\"Op{i}Response\"><xs:complexType><xs:sequence><xs:element name=\"result\" type=\"xs:string\" /></xs:sequence></xs:complexType></xs:element>");
			messages.Append($"<wsdl:message name=\"Op{i}In\"><wsdl:part name=\"p\" element=\"tns:Op{i}\" /></wsdl:message><wsdl:message name=\"Op{i}Out\"><wsdl:part name=\"p\" element=\"tns:Op{i}Response\" /></wsdl:message>");
			operations.Append($"<wsdl:operation name=\"Op{i}\"><wsdl:input message=\"tns:Op{i}In\" /><wsdl:output message=\"tns:Op{i}Out\" /></wsdl:operation>");
			binding.Append($"<wsdl:operation name=\"Op{i}\"><soap:operation soapAction=\"urn:op{i}\" /></wsdl:operation>");
		}
		return $"""
			<wsdl:definitions xmlns:wsdl="http://schemas.xmlsoap.org/wsdl/" xmlns:soap="http://schemas.xmlsoap.org/wsdl/soap/"
				xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:tns="urn:big" targetNamespace="urn:big">
				<wsdl:types><xs:schema targetNamespace="urn:big" elementFormDefault="qualified">{types}</xs:schema></wsdl:types>
				{messages}<wsdl:portType name="P">{operations}</wsdl:portType>
				<wsdl:binding name="B" type="tns:P"><soap:binding transport="http://schemas.xmlsoap.org/soap/http" />{binding}</wsdl:binding>
				<wsdl:service name="Big">{string.Concat(Enumerable.Range(0, ports).Select(i =>
					$"<wsdl:port name=\"Soap{(i == 0 ? string.Empty : i)}\" binding=\"tns:B\"><soap:address location=\"http://localhost/big\" /></wsdl:port>"))}</wsdl:service>
			</wsdl:definitions>
			""";
	}

	[Test]
	[Description("A too-large result lists at most the listing limit of operation names over every port together, so the fallback stays small.")]
	public void Read_Should_Cap_The_Listed_Names_Over_Every_Port() {
		// Arrange
		string path = GivenFile("big2.wsdl", Encoding.UTF8.GetBytes(BigWsdl(400, ports: 2)));

		// Act
		WsdlReadResult result = _sut.Read(Files(path));

		// Assert
		result.Services.Should().HaveCount(2, because: "both ports are listed");
		result.Services.SelectMany(s => s.Operations).Count(n => !n.StartsWith("...and ")).Should()
			.Be(WsdlReader.MaxListedOperations, because: "the limit holds over the ports together");
		result.Services[1].Operations[^1].Should().Be("...and 300 more", because: "the rest of the second port is counted");
	}

	[Test]
	[Description("A wrong service-code is refused also when the result falls back to operation names past the parameter limit.")]
	public void Read_Should_Check_The_Service_Code_Before_The_Parameter_Limit() {
		// Arrange
		string fields = string.Concat(Enumerable.Range(0, WsdlReader.MaxMappedParameters + 1)
			.Select(i => $"<xs:element name=\"f{i}\" type=\"xs:string\" />"));
		string path = GivenFile("wide2.wsdl", Encoding.UTF8.GetBytes(InlineWsdl(fields)));
		_designer.Describe("UsrRest", false).Returns(new WebServiceToolResult(true,
			Service: new WebServiceView(Guid.NewGuid(), "UsrRest", "Rest", "Custom", "REST", "http://localhost", 0, null, [], null)));

		// Act
		Action act = () => _sut.Read(Files(path) with { ServiceCode = "UsrRest" });

		// Assert
		act.Should().Throw<ArgumentException>(because: "a WSDL's operations go into a SOAP service").WithMessage("*REST service*");
	}

	[Test]
	[Description("A service code already used on the environment gets a notice that leaves the choice to the user: another code the user names, or service-code to change that service.")]
	public void Read_Should_Notice_A_Service_Code_In_Use() {
		// Arrange
		string path = GivenFixture("calculator.wsdl");
		_client.IsSchemaNameUnique(ServiceSchemaCodes.ManagerName, "UsrCalculator").Returns(false);

		// Act
		WsdlReadResult result = _sut.Read(Files(path));

		// Assert
		string notice = result.Notices.Should().ContainSingle(because: "both ports share the code, so it is checked once").Subject;
		notice.Should().Contain("service-code 'UsrCalculator'", because: "the notice names the way to change that service");
		notice.Should().Contain("Ask the user", because: "an agent given only the options picked a code by itself on a stand");
	}

	[Test]
	[Description("A name downloaded with '?' (the platform's ASCII download of a non-ASCII character) gives a warning to use wsdl-files.")]
	public void Read_Should_Warn_About_Replaced_Characters_In_A_Download() {
		// Arrange
		const string url = "http://example.org/calc?wsdl";
		string xml = Encoding.UTF8.GetString(FixtureBytes("calculator.wsdl")).Replace("name=\"Calculator\"", "name=\"Calc?lator\"");
		_client.DownloadWsdl(url).Returns([new WsdlSourceFile(url, Encoding.ASCII.GetBytes(xml), AsciiText: true)]);

		// Act
		WsdlReadResult result = _sut.Read(new WsdlReadRequest("dev", url, null));

		// Assert
		result.Warnings.Should().Contain(w => w.Contains("wsdl-files") && w.Contains("non-ASCII"),
			because: "the agent can recover the real names from a local copy");
	}

	[Test]
	[Description("service-code reads that SOAP service, so matching operations keep its codes; a REST service is refused.")]
	public void Read_Should_Read_A_Target_Service() {
		// Arrange
		string path = GivenFixture("calculator.wsdl");
		WebServiceView soap = new(Guid.NewGuid(), "UsrCalc", "Calc", "Custom", "SOAP 1.1", "http://localhost", 0, null,
			[new WebServiceMethodView("UsrAddOld", "Add", "POST", null, 5000, "xml", false, [], [], "tem:Add", "http://tempuri.org/Add")],
			[new WebServiceNamespaceView("tem", "http://tempuri.org/", true)]);
		_designer.Describe("UsrCalc", false).Returns(new WebServiceToolResult(true, Service: soap));
		_client.IsSchemaNameUnique(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
		_designer.Describe("UsrRest", false).Returns(new WebServiceToolResult(true,
			Service: soap with { Code = "UsrRest", Type = "REST" }));

		// Act
		WsdlReadResult result = _sut.Read(Files(path) with { ServiceCode = "UsrCalc", Port = "CalculatorSoap" });
		Action rest = () => _sut.Read(Files(path) with { ServiceCode = "UsrRest" });

		// Assert
		result.Services.Single().ExistingMethods.Should().Equal(["UsrAddOld"], because: "Add is already a method of the service");
		result.Notices.Should().BeNull(because: "no new service code is proposed, so none is checked");
		_client.DidNotReceiveWithAnyArgs().IsSchemaNameUnique(default, default);
		rest.Should().Throw<ArgumentException>(because: "WSDL operations go into a SOAP service").WithMessage("*REST*");
	}

	[Test]
	[Description("A service-code that cannot be read is refused with the reason describe gave, so a typo and missing rights are told apart.")]
	public void Read_Should_Give_The_Reason_A_Target_Service_Cannot_Be_Read() {
		// Arrange
		string path = GivenFixture("calculator.wsdl");
		_designer.Describe("UsrNope", false).Returns(new WebServiceToolResult(false, Error: "Web service 'UsrNope' was not found."));

		// Act
		Action act = () => _sut.Read(Files(path) with { ServiceCode = "UsrNope" });

		// Assert
		act.Should().Throw<ArgumentException>(because: "the service cannot be read").WithMessage("*was not found*");
	}

	[Test]
	[Description("A service-code that names no registered service says the argument is only for an existing service, so the agent drops it for a new one instead of guessing.")]
	public void Read_Should_Say_ServiceCode_Is_For_An_Existing_Service() {
		// Arrange
		string path = GivenFixture("calculator.wsdl");
		_designer.Describe("UsrTcNew", false).Returns(_ =>
			throw new ArgumentException("web service 'UsrTcNew' is not registered; list-web-services shows the registered codes."));

		// Act
		Action act = () => _sut.Read(Files(path) with { ServiceCode = "UsrTcNew" });

		// Assert
		act.Should().Throw<ArgumentException>(because: "no such service exists")
			.WithMessage("*is not registered*only for an existing SOAP service*without it*",
				because: "agents passed the code of the service they were about to register (seen in isolated runs)");
	}
}
