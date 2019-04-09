using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Xml.Linq;

namespace bpmcli
{

	public class BpmPkgProject
	{
		private const string HintElementName = "HintPath";
		private const string ItemGroupElementName = "ItemGroup";
		private const string ReferenceElementName = "Reference";

		private const string PathToCoreDebug = @"..\..\..\..\..\..\..\Bin\Debug\";
		private const string PathToBinDebug = @"..\..\..\Bin\";

		private const string SdkSearchPattern = "BpmonlineSDK";


		private string _activeHint;

		private BpmPkgProject(string path) {
			LoadPath = path;
			Document = XElement.Load(path, LoadOptions.SetBaseUri);
			Namespace = Document.Name.Namespace;
			DetermineCurrentRef();
		}

		private XName HintPath => Namespace + HintElementName;

		private XName ItemGroup => Namespace + ItemGroupElementName;

		private XName Reference => Namespace + ReferenceElementName;

		private static XName IncludeAttr => "Include";

		public RefType CurrentRefType { get; private set; }

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

		private void DeletePackagesConfig() {
			var package = Document.Elements(ItemGroup)
				.Descendants().FirstOrDefault(x => (string)x.Attribute("Include") == "packages.config");
			package?.Remove();
		}

		private void DetermineCurrentRef() {
			if (Document.Elements(ItemGroup).Any(el => el.Elements(Reference)
					.Any(elem => elem.Element(HintPath) != null 
						&& ((string)elem.Element(HintPath)).Contains(SdkSearchPattern)))) {
				CurrentRefType = RefType.Sdk;
			}
			else if (Document.Elements(ItemGroup).Any(el => el.Elements(Reference)
				.Any(elem => elem.Element(HintPath) != null
					&& ((string)elem.Element(HintPath)).Contains(PathToCoreDebug)))) {
				CurrentRefType = RefType.CoreSrc;
			}
		}

		private string GetSearchPattern(RefType type) {
			switch (type) {
				case RefType.Sdk:
					return SdkSearchPattern;
				case RefType.CoreSrc:
					return PathToCoreDebug;
				default:
					return "undefined";
			}
		}

		private void ChangeReference() {
			List<XElement> rebaseElements = new List<XElement>();
			foreach (var el in Document.Elements(ItemGroup)) {
				rebaseElements.AddRange(el.Elements(Reference)
					.Where(elem => (
						elem.Element(HintPath) != null &&
						((string)elem.Element(HintPath)).Contains(GetSearchPattern(CurrentRefType)))));
			}
			rebaseElements.ForEach(ChangeHint);
		}

		public BpmPkgProject RefToBin() {
			_activeHint = PathToBinDebug;
			ChangeReference();
			DeletePackagesConfig();
			CurrentRefType = RefType.Bin;
			return this;
		}

		public BpmPkgProject RefToCoreSrc() {
			_activeHint = PathToCoreDebug;
			ChangeReference();
			DeletePackagesConfig();
			CurrentRefType = RefType.CoreSrc;
			return this;
		}

		public BpmPkgProject RefToCustomPath(string path) {
			_activeHint = path;
			ChangeReference();
			CurrentRefType = RefType.Custom;
			return this;
		}

		public void SaveChanges() {
			Document.Save(LoadPath);
		}
	}

	public enum RefType
	{
		Undef = 0,
		Sdk = 1,
		Bin = 2,
		CoreSrc = 3,
		Custom = 4
	}
}
