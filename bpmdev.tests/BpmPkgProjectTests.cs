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
		public void BpmPkgProject_RebaseToCoreDebug_RealProjCase() {
			var expectProj = XElement.Load("ExpectPkgProj.xml", LoadOptions.PreserveWhitespace);
			var proj = BpmPkgProject.LoadFromFile("OriginPkgProj.xml");
			proj.RebaseToCoreDebug()
				.Document
				.Should().BeEquivalentTo(expectProj);
		}

		[Test]
		public void BpmPkgProject_RebaseToCoreDebug_SimpleCase() {
			var expectElement = XElement.Load("ExpectReferenceHint.xml", LoadOptions.PreserveWhitespace);
			var proj = BpmPkgProject.LoadFromFile("OriginReferenceHint.xml");
			proj.RebaseToCoreDebug()
				.Document
				.Should().BeEquivalentTo(expectElement);
		}

	}
}