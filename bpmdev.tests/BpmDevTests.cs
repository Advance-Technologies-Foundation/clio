using System.Xml;
using System.Xml.Linq;
using bpmdcli;
using FluentAssertions;
using NUnit.Framework;

namespace bpmdev.tests
{
	public class BpmDevTests
	{
		[SetUp]
		public void Setup() {
		}

		[Test]
		public void BpmPkgProject_RebaseToCoreDebug_RealProjCase() {
			var expectProj = XElement.Load("ExpectPkgProj.xml");
			var proj = BpmPkgProject.LoadFromFile("OriginPkgProj.xml");
			proj.RebaseToCoreDebug()
				.Document
				.Should().BeEquivalentTo(expectProj);
		}

		[Test]
		public void BpmPkgProject_RebaseToCoreDebug_SimpleCase() {
			var expectElement = XElement.Load("ExpectReferenceHint.xml");
			var proj = BpmPkgProject.LoadFromFile("OriginReferenceHint.xml");
			proj.RebaseToCoreDebug()
				.Document
				.Should().BeEquivalentTo(expectElement);
		}
	}
}