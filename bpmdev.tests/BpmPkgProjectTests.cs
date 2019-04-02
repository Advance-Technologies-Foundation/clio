using System;
using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace bpmcli.tests
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
			var proj = BpmPkgProject.LoadFromFile("OriginPkgProj.xml");
			proj.RefToCoreSrc()
				.Document.Should().BeEquivalentTo(expectProj);
		}

		[Test]
		public void BpmPkgProject_RefToCoreSrc_LoadFromSDKFileSimpleCase() {
			var expectElement = XElement.Load("CoreSrcReferenceHint.xml", LoadOptions.SetBaseUri);
			var proj = BpmPkgProject.LoadFromFile("SDKReferenceHint.xml");
			proj.RefToCoreSrc()
				.Document
				.Should().BeEquivalentTo(expectElement);
		}

		[Test]
		public void BpmPkgProject_RefToBin_LoadFromSDKFileSimpleCase() {
			var expectElement = XElement.Load("BinReferenceHint.xml", LoadOptions.SetBaseUri);
			var proj = BpmPkgProject.LoadFromFile("SDKReferenceHint.xml");
			proj.RefToBin()
				.Document
				.Should().BeEquivalentTo(expectElement);
		}

		[Test]
		public void BpmPkgProject_RefToBin_LoadFromCoreSrcFileSimpleCase() {
			var expectElement = XElement.Load("BinReferenceHint.xml", LoadOptions.SetBaseUri);
			var proj = BpmPkgProject.LoadFromFile("CoreSrcReferenceHint.xml");
			proj.RefToBin()
				.Document
				.Should().BeEquivalentTo(expectElement);
		}

		[Test]
		public void BpmPkgProject_DetermineCurrentRef_LoadFromSdkFileSimpleCase() {
			var proj = BpmPkgProject.LoadFromFile("SDKReferenceHint.xml");
			proj.CurrentRefType.Should().BeEquivalentTo(RefType.Sdk);
		}

		[Test]
		public void BpmPkgProject_DetermineCurrentRef_LoadFromCoreSrcFileSimpleCase() {
			var proj = BpmPkgProject.LoadFromFile("CoreSrcReferenceHint.xml");
			proj.CurrentRefType.Should().BeEquivalentTo(RefType.CoreSrc);
		}

	}
}