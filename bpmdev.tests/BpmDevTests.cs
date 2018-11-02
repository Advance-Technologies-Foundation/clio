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
        public void Setup()
        {
        }

        [Test]
        [Ignore("not implemented")]
        public void RebasePkgProj_TestMethod()
        {
	        var testProj = XElement.Load("PkgProjStruct.xml");
	        var devTool = new BpmDevTool();
	        var rebasedDoc = devTool.Rebase(testProj);
			Assert.Pass();
        }

		[Test]
		public void ChangeHint_TestMethod() {
			var sorceElement = XElement.Load("SourceReferenceHint.xml");
			var targetElement = XElement.Load("TargetReferenceHint.xml");
			var devTool = new BpmDevTool();
			var changedElement = devTool.ChangeHint(sorceElement);
			changedElement.Should().BeEquivalentTo(targetElement);
			Assert.Pass();
		}
	}
}