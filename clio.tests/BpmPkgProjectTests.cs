using System.IO;
using FluentAssertions;
using NUnit.Framework;
using System.Xml.Linq;

namespace Clio.Tests;

[Category("Unit")]
[Property("Module", "Core")]
public class CreatioPkgProjectTests
{
	[SetUp]
	public void Setup()
	{
	}

	// Resolves a fixture file relative to the test assembly directory so the
	// tests pass regardless of the process working directory (which may be set
	// to a TestResults sub-folder by the XPlat Code Coverage collector on
	// Windows CI runners).
	private static string F(string name) =>
		Path.Combine(TestContext.CurrentContext.TestDirectory, name);

	[Test]
	public void CreatioPkgProject_RefToCoreSrc_LoadFromFileRealProjCase()
	{
		var expectProj = XElement.Load(F("ExpectPkgProj.xml"), LoadOptions.SetBaseUri);
		var proj = CreatioPkgProject.LoadFromFile(F("OriginPkgProj.xml"));
		proj.RefToCoreSrc()
			.Document.Should().BeEquivalentTo(expectProj);
	}

	[Test]
	public void CreatioPkgProject_RefToCoreSrc_LoadFromSDKFileSimpleCase()
	{
		var expectElement = XElement.Load(F("CoreSrcReferenceHint.xml"), LoadOptions.SetBaseUri);
		var proj = CreatioPkgProject.LoadFromFile(F("SDKReferenceHint.xml"));
		proj.RefToCoreSrc()
			.Document
			.Should().BeEquivalentTo(expectElement);
	}

	[Test]
	public void CreatioPkgProject_RefToBin_LoadFromSDKFileSimpleCase()
	{
		var expectElement = XElement.Load(F("BinReferenceHint.xml"), LoadOptions.SetBaseUri);
		var proj = CreatioPkgProject.LoadFromFile(F("SDKReferenceHint.xml"));
		proj.RefToBin()
			.Document
			.Should().BeEquivalentTo(expectElement);
	}

	[Test]
	public void CreatioPkgProject_RefToBin_LoadFromCoreSrcFileSimpleCase()
	{
		var expectElement = XElement.Load(F("BinReferenceHint.xml"), LoadOptions.SetBaseUri);
		var proj = CreatioPkgProject.LoadFromFile(F("CoreSrcReferenceHint.xml"));
		proj.RefToBin()
			.Document
			.Should().BeEquivalentTo(expectElement);
	}

	[Test]
	public void CreatioPkgProject_RefToBinInner_LoadFromCoreSrcInnerFileSimpleCase()
	{
		var expectElement = XElement.Load(F("InnerBinReferenceHint.xml"), LoadOptions.SetBaseUri);
		var proj = CreatioPkgProject.LoadFromFile(F("InnerCoreSrcReferenceHint.xml"));
		proj.RefToCustomPath(@"..\..\..\..\Bin\")
			.Document
			.Should().BeEquivalentTo(expectElement);
	}

	[Test]
	public void CreatioPkgProject_RefToBin_LoadFromUnitCoreSrcSample()
	{
		var expectElement = XElement.Load(F("UnitBinSample.xml"), LoadOptions.SetBaseUri);
		var proj = CreatioPkgProject.LoadFromFile(F("UnitCoreSrcSample.xml"));
		proj.RefToCustomPath(@"..\..\..\..\Bin\")
			.RefToUnitBin()
			.Document
			.Should().BeEquivalentTo(expectElement);
	}

	[Test]
	public void CreatioPkgProject_RefToUnitBinDoNothing_LoadFromBinFileSimpleCase()
	{
		var expectElement = XElement.Load(F("BinReferenceHint.xml"), LoadOptions.SetBaseUri);
		var proj = CreatioPkgProject.LoadFromFile(F("BinReferenceHint.xml"));
		proj.RefToUnitBin()
			.Document
			.Should().BeEquivalentTo(expectElement);
	}

	[Test]
	public void CreatioPkgProject_RefToUnitBin_LoadFromUnitCoreSrcFileSimpleCase()
	{
		var expectElement = XElement.Load(F("UnitBinReferenceHint.xml"), LoadOptions.SetBaseUri);
		var proj = CreatioPkgProject.LoadFromFile(F("UnitCoreSrcReferenceHint.xml"));
		proj.RefToUnitBin()
			.Document
			.Should().BeEquivalentTo(expectElement);
	}

	[Test]
	public void CreatioPkgProject_RefToUnitCoreSrc_LoadFromUnitBinFileSimpleCase()
	{
		var expectElement = XElement.Load(F("UnitCoreSrcReferenceHint.xml"), LoadOptions.SetBaseUri);
		var proj = CreatioPkgProject.LoadFromFile(F("UnitBinReferenceHint.xml"));
		proj.RefToUnitCoreSrc()
			.Document
			.Should().BeEquivalentTo(expectElement);
	}

	[Test]
	public void CreatioPkgProject_RefToBin_LoadFromTsCoreBinPathFileSimpleCase()
	{
		var expectElement = XElement.Load(F("BinNet472ReferenceHint.xml"), LoadOptions.SetBaseUri);
		var proj = CreatioPkgProject.LoadFromFile(F("TsCoreBinPathNet472ReferenceHint.xml"));
		proj.RefToBin()
			.Document
			.Should().BeEquivalentTo(expectElement);
	}

	[Test]
	public void CreatioPkgProject_DetermineCurrentRef_LoadFromSdkFile()
	{
		var proj = CreatioPkgProject.LoadFromFile(F("SDKReferenceHint.xml"));
		proj.CurrentRefType.Should().Be(RefType.Sdk);
	}

	[Test]
	public void CreatioPkgProject_DetermineCurrentRef_LoadFromCoreSrcFile()
	{
		var proj = CreatioPkgProject.LoadFromFile(F("CoreSrcReferenceHint.xml"));
		proj.CurrentRefType.Should().Be(RefType.CoreSrc);
	}

	[Test]
	public void CreatioPkgProject_DetermineCurrentRef_LoadFromUnitCoreSrcFile()
	{
		var proj = CreatioPkgProject.LoadFromFile(F("UnitCoreSrcReferenceHint.xml"));
		proj.CurrentRefType.Should().Be(RefType.UnitTest);
	}

	[Test]
	public void CreatioPkgProject_DetermineCurrentRef_LoadFromUnitBinFile()
	{
		var proj = CreatioPkgProject.LoadFromFile(F("UnitBinReferenceHint.xml"));
		proj.CurrentRefType.Should().Be(RefType.UnitTest);
	}

	[Test]
	public void CreatioPkgProject_DetermineCurrentRef_LoadFromTsCoreBinPath()
	{
		var proj = CreatioPkgProject.LoadFromFile(F("TsCoreBinPathNet472ReferenceHint.xml"));
		proj.CurrentRefType.Should().Be(RefType.TsCoreBinPath);
	}

}
