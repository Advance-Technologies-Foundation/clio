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
		public void RebasePkgProj_TestMethod() {
			var originProj = XElement.Load("OriginPkgProj.xml");
			var expectProj = XElement.Load("ExpectPkgProj.xml");
			var devTool = new BpmDevTool();
			var changedProj = devTool.ChangeCorePathToDebug(originProj);
			changedProj.Should().BeEquivalentTo(expectProj);
		}

		[Test]
		public void ChangeHint_TestMethod() {
			var originElement = XElement.Load("OriginReferenceHint.xml");
			var expectElement = XElement.Load("ExpectReferenceHint.xml");
			var devTool = new BpmDevTool();
			var changedElement = devTool.ChangeHint(originElement);
			changedElement.Should().BeEquivalentTo(expectElement);
		}
	}
}