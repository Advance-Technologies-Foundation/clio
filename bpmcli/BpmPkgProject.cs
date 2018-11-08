using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace bpmcli
{
	public class BpmPkgProject
	{
		private static XNamespace _xNamespace = "http://schemas.microsoft.com/developer/msbuild/2003";
		private const string IncludeAttributeName = "Include";

		private const string PathToBinDebug = @"..\..\..\..\..\..\..\Bin\Debug\";


		private BpmPkgProject(string path) {
			LoadPath = path;
			Document = XElement.Load(path, LoadOptions.PreserveWhitespace);
		}

		public XElement Document { get; }

		public string LoadPath { get; }

		public static BpmPkgProject LoadFromFile(string path) {
			return new BpmPkgProject(path);
		}

		private void ChangeHint(XElement element) {
			var value = element.Attribute(IncludeAttributeName)?.Value;
			if (value != null) {
				int index = value.IndexOf(",", StringComparison.Ordinal);
				if (index > 0) {
					value = value.Substring(0, index);
					element.SetAttributeValue(IncludeAttributeName, value);
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
		}

		public BpmPkgProject RebaseToCoreDebug() {
			List<XElement> rebaseElements = new List<XElement>();
			foreach (var el in Document.Elements(_xNamespace + "ItemGroup")) {
				rebaseElements.AddRange(el.Elements(_xNamespace + "Reference")
					.Where(elem => (
						elem.Element(_xNamespace + "HintPath") != null &&
						((string)elem.Element(_xNamespace + "HintPath")).Contains("BpmonlineSDK"))));
			}
			rebaseElements.ForEach(ChangeHint);
			return this;
		}

		public void SaveChanges() {
			Document.Save(LoadPath);
		}
	}
}
