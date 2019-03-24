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
		private const string HintElementName = "HintPath";
		private const string ItemGroupElemetName = "ItemGroup";
		private const string ReferenceElementName = "Reference";

		private const string PathToCoreDebug = @"..\..\..\..\..\..\..\Bin\Debug\";

		private const string PathToBinDebug = @"..\..\..\Bin\";

		private string _activeHint;


		private BpmPkgProject(string path) {
			LoadPath = path;
			Document = XElement.Load(path, LoadOptions.PreserveWhitespace);
			Namespace = Document.Name.Namespace;
		}

		private XName HintPath => Namespace + HintElementName;

		private XName ItemGroup => Namespace + ItemGroupElemetName;

		private XName Reference => Namespace + ReferenceElementName;

		private static XName IncludeAttr => "Include";

		public XElement Document { get; }

		public string LoadPath { get; }

		public XNamespace Namespace { get; }

		public static BpmPkgProject LoadFromFile(string path) {
			return new BpmPkgProject(path);
		}

		private void ChangeHint(XElement element) {
			var value = element.Attribute(IncludeAttr)?.Value;
			if (value != null) {
				int index = value.IndexOf(",", StringComparison.Ordinal);
				if (index > 0) {
					value = value.Substring(0, index);
					element.SetAttributeValue(IncludeAttr, value);
				}
			}
			var hintValue = element.Element(HintPath)?.Value;
			if (hintValue != null) {
				int index = hintValue.LastIndexOf("\\", StringComparison.Ordinal);
				if (index > 0) {
					hintValue = string.Concat(_activeHint, hintValue.Substring(index).TrimStart('\\'));
					element.SetElementValue(HintPath, hintValue);
				}
			}
		}

		public BpmPkgProject RebaseToBinDebug()
		{
			_activeHint = PathToBinDebug;
			Rebase();
			return this;
		}

		private void Rebase() {
			List<XElement> rebaseElements = new List<XElement>();
			foreach (var el in Document.Elements(ItemGroup))
			{
				rebaseElements.AddRange(el.Elements(Reference)
					.Where(elem => (
						elem.Element(HintPath) != null &&
						((string)elem.Element(HintPath)).Contains("BpmonlineSDK"))));
			}
			rebaseElements.ForEach(ChangeHint);
			var package = Document.Elements(ItemGroup).Descendants()
				.Where(x => (string)x.Attribute("Include") == "packages.config").FirstOrDefault();
			package.Remove();
		}

		public BpmPkgProject RebaseToCoreDebug() {
			_activeHint = PathToCoreDebug;
			Rebase();
			return this;
		}

		public void SaveChanges() {
			Document.Save(LoadPath);
		}
	}
}
