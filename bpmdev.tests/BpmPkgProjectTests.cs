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
		public void BpmPkgProject_RebaseToCoreDebug_LoadFromFileRealProjCase()
		{
			var expectProj = XElement.Load("ExpectPkgProj.xml", LoadOptions.SetBaseUri);
			var proj = BpmPkgProject.LoadFromFile("OriginPkgProj.xml");
			var document = proj.RebaseToCoreDebug().Document;
			proj.RebaseToCoreDebug()
			  .Document
				.Should().BeEquivalentTo(expectProj);
		}

		[Test]
		public void BpmPkgProject_RebaseToCoreDebug_LoadFromFileSimpleCase() {
			var expectElement = XElement.Load("ExpectReferenceHint.xml", LoadOptions.SetBaseUri);
			var proj = BpmPkgProject.LoadFromFile("OriginReferenceHint.xml");
			var document = proj.RebaseToCoreDebug()
				.Document;
			proj.RebaseToCoreDebug()
				.Document
				.Should().BeEquivalentTo(expectElement);
		}

	}
}