using System;
using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace clio.tests
{
	public class BpmPkgProjectTests
	{
		[SetUp]
		public void Setup() {
		}

		[Test]
		public void BpmPkgProject_RefToCoreSrc_LoadFromFileRealProjCase()
		{
			var expectProj = XElement.Load("ExpectPkgProj.xml", LoadOptions.SetBaseUri);
			var proj = CreatioPkgProject.LoadFromFile("OriginPkgProj.xml");
			proj.RefToCoreSrc()
				.Document.Should().BeEquivalentTo(expectProj);
		}

		[Test]
		public void BpmPkgProject_RefToCoreSrc_LoadFromSDKFileSimpleCase() {
			var expectElement = XElement.Load("CoreSrcReferenceHint.xml", LoadOptions.SetBaseUri);
			var proj = CreatioPkgProject.LoadFromFile("SDKReferenceHint.xml");
			proj.RefToCoreSrc()
				.Document
				.Should().BeEquivalentTo(expectElement);
		}

		[Test]
		public void BpmPkgProject_RefToBin_LoadFromSDKFileSimpleCase() {
			var expectElement = XElement.Load("BinReferenceHint.xml", LoadOptions.SetBaseUri);
			var proj = CreatioPkgProject.LoadFromFile("SDKReferenceHint.xml");
			proj.RefToBin()
				.Document
				.Should().BeEquivalentTo(expectElement);
		}

		[Test]
		public void BpmPkgProject_RefToBin_LoadFromCoreSrcFileSimpleCase() {
			var expectElement = XElement.Load("BinReferenceHint.xml", LoadOptions.SetBaseUri);
			var proj = CreatioPkgProject.LoadFromFile("CoreSrcReferenceHint.xml");
			proj.RefToBin()
				.Document
				.Should().BeEquivalentTo(expectElement);
		}

		[Test]
		public void BpmPkgProject_RefToBinInner_LoadFromCoreSrcInnerFileSimpleCase() {
			var expectElement = XElement.Load("InnerBinReferenceHint.xml", LoadOptions.SetBaseUri);
			var proj = CreatioPkgProject.LoadFromFile("InnerCoreSrcReferenceHint.xml");
			proj.RefToCustomPath(@"..\..\..\..\Bin\")
				.Document
				.Should().BeEquivalentTo(expectElement);
		}

		[Test]
		public void BpmPkgProject_RefToBin_LoadFromUnitCoreSrcSample() {
			var expectElement = XElement.Load("UnitBinSample.xml", LoadOptions.SetBaseUri);
			var proj = CreatioPkgProject.LoadFromFile("UnitCoreSrcSample.xml");
			proj.RefToCustomPath(@"..\..\..\..\Bin\")
				.RefToUnitBin()
				.Document
				.Should().BeEquivalentTo(expectElement);
		}

		[Test]
		public void BpmPkgProject_RefToUnitBinDoNothing_LoadFromBinFileSimpleCase() {
			var expectElement = XElement.Load("BinReferenceHint.xml", LoadOptions.SetBaseUri);
			var proj = CreatioPkgProject.LoadFromFile("BinReferenceHint.xml");
			proj.RefToUnitBin()
				.Document
				.Should().BeEquivalentTo(expectElement);
		}

		[Test]
		public void BpmPkgProject_RefToUnitBin_LoadFromUnitCoreSrcFileSimpleCase() {
			var expectElement = XElement.Load("UnitBinReferenceHint.xml", LoadOptions.SetBaseUri);
			var proj = CreatioPkgProject.LoadFromFile("UnitCoreSrcReferenceHint.xml");
			proj.RefToUnitBin()
				.Document
				.Should().BeEquivalentTo(expectElement);
		}

		[Test]
		public void BpmPkgProject_RefToUnitCoreSrc_LoadFromUnitBinFileSimpleCase() {
			var expectElement = XElement.Load("UnitCoreSrcReferenceHint.xml", LoadOptions.SetBaseUri);
			var proj = CreatioPkgProject.LoadFromFile("UnitBinReferenceHint.xml");
			proj.RefToUnitCoreSrc()
				.Document
				.Should().BeEquivalentTo(expectElement);
		}

		[Test]
		public void BpmPkgProject_RefToBin_LoadFromTsCoreBinPathFileSimpleCase() {
			var expectElement = XElement.Load("BinNet472ReferenceHint.xml", LoadOptions.SetBaseUri);
			var proj = CreatioPkgProject.LoadFromFile("TsCoreBinPathNet472ReferenceHint.xml");
			proj.RefToBin()
				.Document
				.Should().BeEquivalentTo(expectElement);
		}

		[Test]
		public void BpmPkgProject_DetermineCurrentRef_LoadFromSdkFile() {
			var proj = CreatioPkgProject.LoadFromFile("SDKReferenceHint.xml");
			proj.CurrentRefType.Should().BeEquivalentTo(RefType.Sdk);
		}

		[Test]
		public void BpmPkgProject_DetermineCurrentRef_LoadFromCoreSrcFile() {
			var proj = CreatioPkgProject.LoadFromFile("CoreSrcReferenceHint.xml");
			proj.CurrentRefType.Should().BeEquivalentTo(RefType.CoreSrc);
		}

		[Test]
		public void BpmPkgProject_DetermineCurrentRef_LoadFromUnitCoreSrcFile() {
			var proj = CreatioPkgProject.LoadFromFile("UnitCoreSrcReferenceHint.xml");
			proj.CurrentRefType.Should().BeEquivalentTo(RefType.UnitTest);
		}

		[Test]
		public void BpmPkgProject_DetermineCurrentRef_LoadFromUnitBinFile() {
			var proj = CreatioPkgProject.LoadFromFile("UnitBinReferenceHint.xml");
			proj.CurrentRefType.Should().BeEquivalentTo(RefType.UnitTest);
		}

		[Test]
		public void BpmPkgProject_DetermineCurrentRef_LoadFromTsCoreBinPath() {
			var proj = CreatioPkgProject.LoadFromFile("TsCoreBinPathNet472ReferenceHint.xml");
			proj.CurrentRefType.Should().BeEquivalentTo(RefType.TsCoreBinPath);
		}

	}
}