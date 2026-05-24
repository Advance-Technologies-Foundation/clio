using FluentAssertions;
using NUnit.Framework;
using System.IO;
using System.Xml.Linq;

namespace Clio.Tests;

[Category("Unit")]
[Property("Module", "Core")]
public class CreatioPkgProjectTests
{
	private string _previousDirectory = null!;

	[SetUp]
	public void Setup()
	{
		// On CI (Windows runner) tests run with the working directory set to a `TestResult\`
		// subfolder of bin/Debug, while the XML fixtures used by the tests below are copied
		// next to the test assembly via <CopyToOutputDirectory>Always</CopyToOutputDirectory>.
		// Point Environment.CurrentDirectory at the test binary directory so the relative
		// XElement.Load / CreatioPkgProject.LoadFromFile calls resolve consistently across
		// developer machines and CI.
		_previousDirectory = Directory.GetCurrentDirectory();
		Directory.SetCurrentDirectory(TestContext.CurrentContext.TestDirectory);
	}

	[TearDown]
	public void TearDown()
	{
		if (!string.IsNullOrEmpty(_previousDirectory))
		{
			Directory.SetCurrentDirectory(_previousDirectory);
		}
	}

	[Test]
	public void CreatioPkgProject_RefToCoreSrc_LoadFromFileRealProjCase()
	{
		var expectProj = XElement.Load("ExpectPkgProj.xml", LoadOptions.SetBaseUri);
		var proj = CreatioPkgProject.LoadFromFile("OriginPkgProj.xml");
		proj.RefToCoreSrc()
			.Document.Should().BeEquivalentTo(expectProj);
	}

	[Test]
	public void CreatioPkgProject_RefToCoreSrc_LoadFromSDKFileSimpleCase()
	{
		var expectElement = XElement.Load("CoreSrcReferenceHint.xml", LoadOptions.SetBaseUri);
		var proj = CreatioPkgProject.LoadFromFile("SDKReferenceHint.xml");
		proj.RefToCoreSrc()
			.Document
			.Should().BeEquivalentTo(expectElement);
	}

	[Test]
	public void CreatioPkgProject_RefToBin_LoadFromSDKFileSimpleCase()
	{
		var expectElement = XElement.Load("BinReferenceHint.xml", LoadOptions.SetBaseUri);
		var proj = CreatioPkgProject.LoadFromFile("SDKReferenceHint.xml");
		proj.RefToBin()
			.Document
			.Should().BeEquivalentTo(expectElement);
	}

	[Test]
	public void CreatioPkgProject_RefToBin_LoadFromCoreSrcFileSimpleCase()
	{
		var expectElement = XElement.Load("BinReferenceHint.xml", LoadOptions.SetBaseUri);
		var proj = CreatioPkgProject.LoadFromFile("CoreSrcReferenceHint.xml");
		proj.RefToBin()
			.Document
			.Should().BeEquivalentTo(expectElement);
	}

	[Test]
	public void CreatioPkgProject_RefToBinInner_LoadFromCoreSrcInnerFileSimpleCase()
	{
		var expectElement = XElement.Load("InnerBinReferenceHint.xml", LoadOptions.SetBaseUri);
		var proj = CreatioPkgProject.LoadFromFile("InnerCoreSrcReferenceHint.xml");
		proj.RefToCustomPath(@"..\..\..\..\Bin\")
			.Document
			.Should().BeEquivalentTo(expectElement);
	}

	[Test]
	public void CreatioPkgProject_RefToBin_LoadFromUnitCoreSrcSample()
	{
		var expectElement = XElement.Load("UnitBinSample.xml", LoadOptions.SetBaseUri);
		var proj = CreatioPkgProject.LoadFromFile("UnitCoreSrcSample.xml");
		proj.RefToCustomPath(@"..\..\..\..\Bin\")
			.RefToUnitBin()
			.Document
			.Should().BeEquivalentTo(expectElement);
	}

	[Test]
	public void CreatioPkgProject_RefToUnitBinDoNothing_LoadFromBinFileSimpleCase()
	{
		var expectElement = XElement.Load("BinReferenceHint.xml", LoadOptions.SetBaseUri);
		var proj = CreatioPkgProject.LoadFromFile("BinReferenceHint.xml");
		proj.RefToUnitBin()
			.Document
			.Should().BeEquivalentTo(expectElement);
	}

	[Test]
	public void CreatioPkgProject_RefToUnitBin_LoadFromUnitCoreSrcFileSimpleCase()
	{
		var expectElement = XElement.Load("UnitBinReferenceHint.xml", LoadOptions.SetBaseUri);
		var proj = CreatioPkgProject.LoadFromFile("UnitCoreSrcReferenceHint.xml");
		proj.RefToUnitBin()
			.Document
			.Should().BeEquivalentTo(expectElement);
	}

	[Test]
	public void CreatioPkgProject_RefToUnitCoreSrc_LoadFromUnitBinFileSimpleCase()
	{
		var expectElement = XElement.Load("UnitCoreSrcReferenceHint.xml", LoadOptions.SetBaseUri);
		var proj = CreatioPkgProject.LoadFromFile("UnitBinReferenceHint.xml");
		proj.RefToUnitCoreSrc()
			.Document
			.Should().BeEquivalentTo(expectElement);
	}

	[Test]
	public void CreatioPkgProject_RefToBin_LoadFromTsCoreBinPathFileSimpleCase()
	{
		var expectElement = XElement.Load("BinNet472ReferenceHint.xml", LoadOptions.SetBaseUri);
		var proj = CreatioPkgProject.LoadFromFile("TsCoreBinPathNet472ReferenceHint.xml");
		proj.RefToBin()
			.Document
			.Should().BeEquivalentTo(expectElement);
	}

	[Test]
	public void CreatioPkgProject_DetermineCurrentRef_LoadFromSdkFile()
	{
		var proj = CreatioPkgProject.LoadFromFile("SDKReferenceHint.xml");
		//proj.CurrentRefType.Should().BeEquivalentTo(RefType.Sdk);
		proj.CurrentRefType.Should().Be(RefType.Sdk);
	}

	[Test]
	public void CreatioPkgProject_DetermineCurrentRef_LoadFromCoreSrcFile()
	{
		var proj = CreatioPkgProject.LoadFromFile("CoreSrcReferenceHint.xml");
		//proj.CurrentRefType.Should().BeEquivalentTo(RefType.CoreSrc);
		proj.CurrentRefType.Should().Be(RefType.CoreSrc);
	}

	[Test]
	public void CreatioPkgProject_DetermineCurrentRef_LoadFromUnitCoreSrcFile()
	{
		var proj = CreatioPkgProject.LoadFromFile("UnitCoreSrcReferenceHint.xml");
		//proj.CurrentRefType.Should().BeEquivalentTo(RefType.UnitTest);
		proj.CurrentRefType.Should().Be(RefType.UnitTest);
	}

	[Test]
	public void CreatioPkgProject_DetermineCurrentRef_LoadFromUnitBinFile()
	{
		var proj = CreatioPkgProject.LoadFromFile("UnitBinReferenceHint.xml");
		//proj.CurrentRefType.Should().BeEquivalentTo(RefType.UnitTest);
		proj.CurrentRefType.Should().Be(RefType.UnitTest);
	}

	[Test]
	public void CreatioPkgProject_DetermineCurrentRef_LoadFromTsCoreBinPath()
	{
		var proj = CreatioPkgProject.LoadFromFile("TsCoreBinPathNet472ReferenceHint.xml");
		//proj.CurrentRefType.Should().BeEquivalentTo(RefType.TsCoreBinPath);
		proj.CurrentRefType.Should().Be(RefType.TsCoreBinPath);
	}

}