using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace bpmdcli
{
	public class BpmDevTool
	{
		private static XNamespace _xNamespace = "http://schemas.microsoft.com/developer/msbuild/2003";

		private const string PathToBinDebug = @"..\..\..\..\..\..\..\Bin\Debug\";

		public XElement ChangeHint(XElement element) {
			var value = element.Attribute("Include")?.Value;
			if (value != null) {
				int index = value.IndexOf(",", StringComparison.Ordinal);
				if (index > 0) {
					value = value.Substring(0, index);
					element.SetAttributeValue("Include", value);
				}
			}
			var hintValue = element.Element(_xNamespace + "HintPath")?.Value;
			if (hintValue != null) {
				int index = hintValue.LastIndexOf("\\", StringComparison.Ordinal);
				if (index > 0) {
					hintValue = string.Concat(PathToBinDebug, hintValue.Substring(index).TrimStart('\\'));
					element.SetElementValue(_xNamespace + "HintPath", hintValue);
				}
			}
			return element;
		}

		public XElement Rebase(XElement testProj) {
			List<XElement> rebaseElements = new List<XElement>();
			foreach (var el in testProj.Elements(_xNamespace + "ItemGroup")) {
				rebaseElements.AddRange(el.Elements(_xNamespace + "Reference")
					.Where(elem => (
						elem.Element(_xNamespace + "HintPath") != null &&
						((string)elem.Element(_xNamespace + "HintPath")).Contains("BpmonlineSDK"))));
			}

			//var rebaseElements =
			//	from el in testProj.Elements(ad + "ItemGroup")
			//	where ((string)el.Element(ad + "Reference").Attribute("Include")).StartsWith("Terrasoft.")
			//	select el;

			return rebaseElements.FirstOrDefault();

		}
	}
}
