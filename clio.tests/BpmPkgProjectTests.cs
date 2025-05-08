using System.Xml.Linq;

using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

public class CreatioPkgProjectTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void CreatioPkgProject_RefToCoreSrc_LoadFromFileRealProjCase()
    {
        XElement expectProj = XElement.Load("ExpectPkgProj.xml", LoadOptions.SetBaseUri);
        CreatioPkgProject proj = CreatioPkgProject.LoadFromFile("OriginPkgProj.xml");
        proj.RefToCoreSrc()
            .Document.Should().BeEquivalentTo(expectProj);
    }

    [Test]
    public void CreatioPkgProject_RefToCoreSrc_LoadFromSDKFileSimpleCase()
    {
        XElement expectElement = XElement.Load("CoreSrcReferenceHint.xml", LoadOptions.SetBaseUri);
        CreatioPkgProject proj = CreatioPkgProject.LoadFromFile("SDKReferenceHint.xml");
        proj.RefToCoreSrc()
            .Document
            .Should().BeEquivalentTo(expectElement);
    }

    [Test]
    public void CreatioPkgProject_RefToBin_LoadFromSDKFileSimpleCase()
    {
        XElement expectElement = XElement.Load("BinReferenceHint.xml", LoadOptions.SetBaseUri);
        CreatioPkgProject proj = CreatioPkgProject.LoadFromFile("SDKReferenceHint.xml");
        proj.RefToBin()
            .Document
            .Should().BeEquivalentTo(expectElement);
    }

    [Test]
    public void CreatioPkgProject_RefToBin_LoadFromCoreSrcFileSimpleCase()
    {
        XElement expectElement = XElement.Load("BinReferenceHint.xml", LoadOptions.SetBaseUri);
        CreatioPkgProject proj = CreatioPkgProject.LoadFromFile("CoreSrcReferenceHint.xml");
        proj.RefToBin()
            .Document
            .Should().BeEquivalentTo(expectElement);
    }

    [Test]
    public void CreatioPkgProject_RefToBinInner_LoadFromCoreSrcInnerFileSimpleCase()
    {
        XElement expectElement = XElement.Load("InnerBinReferenceHint.xml", LoadOptions.SetBaseUri);
        CreatioPkgProject proj = CreatioPkgProject.LoadFromFile("InnerCoreSrcReferenceHint.xml");
        proj.RefToCustomPath(@"..\..\..\..\Bin\")
            .Document
            .Should().BeEquivalentTo(expectElement);
    }

    [Test]
    public void CreatioPkgProject_RefToBin_LoadFromUnitCoreSrcSample()
    {
        XElement expectElement = XElement.Load("UnitBinSample.xml", LoadOptions.SetBaseUri);
        CreatioPkgProject proj = CreatioPkgProject.LoadFromFile("UnitCoreSrcSample.xml");
        proj.RefToCustomPath(@"..\..\..\..\Bin\")
            .RefToUnitBin()
            .Document
            .Should().BeEquivalentTo(expectElement);
    }

    [Test]
    public void CreatioPkgProject_RefToUnitBinDoNothing_LoadFromBinFileSimpleCase()
    {
        XElement expectElement = XElement.Load("BinReferenceHint.xml", LoadOptions.SetBaseUri);
        CreatioPkgProject proj = CreatioPkgProject.LoadFromFile("BinReferenceHint.xml");
        proj.RefToUnitBin()
            .Document
            .Should().BeEquivalentTo(expectElement);
    }

    [Test]
    public void CreatioPkgProject_RefToUnitBin_LoadFromUnitCoreSrcFileSimpleCase()
    {
        XElement expectElement = XElement.Load("UnitBinReferenceHint.xml", LoadOptions.SetBaseUri);
        CreatioPkgProject proj = CreatioPkgProject.LoadFromFile("UnitCoreSrcReferenceHint.xml");
        proj.RefToUnitBin()
            .Document
            .Should().BeEquivalentTo(expectElement);
    }

    [Test]
    public void CreatioPkgProject_RefToUnitCoreSrc_LoadFromUnitBinFileSimpleCase()
    {
        XElement expectElement = XElement.Load("UnitCoreSrcReferenceHint.xml", LoadOptions.SetBaseUri);
        CreatioPkgProject proj = CreatioPkgProject.LoadFromFile("UnitBinReferenceHint.xml");
        proj.RefToUnitCoreSrc()
            .Document
            .Should().BeEquivalentTo(expectElement);
    }

    [Test]
    public void CreatioPkgProject_RefToBin_LoadFromTsCoreBinPathFileSimpleCase()
    {
        XElement expectElement = XElement.Load("BinNet472ReferenceHint.xml", LoadOptions.SetBaseUri);
        CreatioPkgProject proj = CreatioPkgProject.LoadFromFile("TsCoreBinPathNet472ReferenceHint.xml");
        proj.RefToBin()
            .Document
            .Should().BeEquivalentTo(expectElement);
    }

    [Test]
    public void CreatioPkgProject_DetermineCurrentRef_LoadFromSdkFile()
    {
        CreatioPkgProject proj = CreatioPkgProject.LoadFromFile("SDKReferenceHint.xml");

        // proj.CurrentRefType.Should().BeEquivalentTo(RefType.Sdk);
        proj.CurrentRefType.Should().Be(RefType.Sdk);
    }

    [Test]
    public void CreatioPkgProject_DetermineCurrentRef_LoadFromCoreSrcFile()
    {
        CreatioPkgProject proj = CreatioPkgProject.LoadFromFile("CoreSrcReferenceHint.xml");

        // proj.CurrentRefType.Should().BeEquivalentTo(RefType.CoreSrc);
        proj.CurrentRefType.Should().Be(RefType.CoreSrc);
    }

    [Test]
    public void CreatioPkgProject_DetermineCurrentRef_LoadFromUnitCoreSrcFile()
    {
        CreatioPkgProject proj = CreatioPkgProject.LoadFromFile("UnitCoreSrcReferenceHint.xml");

        // proj.CurrentRefType.Should().BeEquivalentTo(RefType.UnitTest);
        proj.CurrentRefType.Should().Be(RefType.UnitTest);
    }

    [Test]
    public void CreatioPkgProject_DetermineCurrentRef_LoadFromUnitBinFile()
    {
        CreatioPkgProject proj = CreatioPkgProject.LoadFromFile("UnitBinReferenceHint.xml");

        // proj.CurrentRefType.Should().BeEquivalentTo(RefType.UnitTest);
        proj.CurrentRefType.Should().Be(RefType.UnitTest);
    }

    [Test]
    public void CreatioPkgProject_DetermineCurrentRef_LoadFromTsCoreBinPath()
    {
        CreatioPkgProject proj = CreatioPkgProject.LoadFromFile("TsCoreBinPathNet472ReferenceHint.xml");

        // proj.CurrentRefType.Should().BeEquivalentTo(RefType.TsCoreBinPath);
        proj.CurrentRefType.Should().Be(RefType.TsCoreBinPath);
    }
}
