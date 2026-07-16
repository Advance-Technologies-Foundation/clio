using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Common.IIS;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class NetFrameworkHttpsConfiguratorTests {
	private const string RootPath = @"C:\site\Web.config";
	private const string InnerPath = @"C:\site\Terrasoft.WebApp\Web.config";

	[Test]
	[Description("Switches both ServiceModel config sources and Microsoft WebSocket encryption to HTTPS idempotently.")]
	public void Configure_ShouldApplyRequiredNetFrameworkHttpsSettings_Idempotently() {
		// Arrange
		MockFileSystem fileSystem = CreateFileSystem();
		NetFrameworkHttpsConfigurator sut = new(fileSystem);

		// Act
		sut.Configure(@"C:\site");
		string firstRoot = fileSystem.File.ReadAllText(RootPath);
		string firstInner = fileSystem.File.ReadAllText(InnerPath);
		sut.Configure(@"C:\site");

		// Assert
		firstRoot.Should().Contain(@"Terrasoft.WebApp\ServiceModel\https\behaviors.config",
			because: "the root WCF behavior configuration must use its HTTPS variant");
		firstRoot.Should().Contain(@"Terrasoft.WebApp\ServiceModel\https\bindings.config",
			because: "the root WCF binding configuration must use its HTTPS variant");
		firstInner.Should().Contain("encrypted=\"true\"",
			because: "the Microsoft WebSocket service must advertise encrypted client connections");
		fileSystem.File.ReadAllText(RootPath).Should().Be(firstRoot,
			because: "reapplying the HTTPS configuration must not produce additional XML changes");
		fileSystem.File.ReadAllText(InnerPath).Should().Be(firstInner,
			because: "reapplying the HTTPS configuration must be idempotent");
	}

	[Test]
	[Description("Fails before writing either file when the required Microsoft WebSocket service is missing.")]
	public void Configure_ShouldNotPartiallyWrite_WhenRequiredInnerElementIsMissing() {
		// Arrange
		MockFileSystem fileSystem = CreateFileSystem(inner: "<configuration />");
		string originalRoot = fileSystem.File.ReadAllText(RootPath);
		NetFrameworkHttpsConfigurator sut = new(fileSystem);

		// Act
		Action act = () => sut.Configure(@"C:\site");

		// Assert
		act.Should().Throw<InvalidDataException>(
			because: "an unexpected Creatio configuration shape must fail clearly instead of silently producing a broken site");
		fileSystem.File.ReadAllText(RootPath).Should().Be(originalRoot,
			because: "both documents are validated before either is committed");
	}

	private static MockFileSystem CreateFileSystem(string inner = null) => new(new Dictionary<string, MockFileData> {
		[RootPath] = new("""
			<configuration><system.serviceModel>
			<behaviors configSource="Terrasoft.WebApp\ServiceModel\http\behaviors.config" />
			<bindings configSource="Terrasoft.WebApp\ServiceModel\http\bindings.config" />
			</system.serviceModel></configuration>
			"""),
		[InnerPath] = new(inner ?? """
			<configuration><wsService type="Terrasoft.Messaging.MicrosoftWSService.MicrosoftWSService, Terrasoft.Messaging.MicrosoftWSService" encrypted="false" portForClientConnection="0" /></configuration>
			""")
	});
}
