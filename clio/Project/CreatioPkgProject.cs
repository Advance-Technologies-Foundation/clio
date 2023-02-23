using Clio.Project;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Clio
{

	public class CreatioPkgProject : ICreatioPkgProject
	{
		private const string HintElementName = "HintPath";
		private const string ItemGroupElementName = "ItemGroup";
		private const string ReferenceElementName = "Reference";

		private const string PathToCoreDebug = @"..\..\..\..\..\..\..\Bin\Debug\";
		private const string PathToBinDebug = @"..\..\..\Bin\";
		private const string PathToUnitTestBin = @"..\..\UnitTest\bin\Debug\";
		private const string PathToUnitTestCoreSrc = @"..\..\..\UnitTest\bin\Debug\";

		private const string SdkSearchPattern = "BpmonlineSDK";
		private const string UnitTestSearchPattern = "UnitTest";
		private const string TsCoreBinPathSearchPattern = "$(TsCoreBinPath)";


		private string _activeHint;

		private CreatioPkgProject(string path)
		{
			LoadPath = path;
			Document = XElement.Load(path, LoadOptions.SetBaseUri);
			Namespace = Document.Name.Namespace;
			DetermineCurrentRef();
		}

		private XName HintPath => Namespace + HintElementName;

		private XName ItemGroup => Namespace + ItemGroupElementName;

		private XName Reference => Namespace + ReferenceElementName;

		private static XName IncludeAttr => "Include";

		public RefType CurrentRefType
		{
			get; private set;
		}

		public XElement Document
		{
			get;
		}

		public string LoadPath
		{
			get;
		}

		public XNamespace Namespace
		{
			get;
		}

		public static CreatioPkgProject LoadFromFile(string path)
		{
			return new CreatioPkgProject(path);
		}

		public CreatioPkgProject()
		{

		}

		private void ChangeHint(XElement element)
		{
			var value = element.Attribute(IncludeAttr)?.Value;
			if (value != null)
			{
				int index = value.IndexOf(",", StringComparison.Ordinal);
				if (index > 0)
				{
					value = value.Substring(0, index);
					element.SetAttributeValue(IncludeAttr, value);
				}
			}
			var hintValue = element.Element(HintPath)?.Value;
			if (hintValue != null)
			{
				int index = hintValue.LastIndexOf("\\", StringComparison.Ordinal);
				if (index > 0)
				{
					hintValue = string.Concat(_activeHint, hintValue.Substring(index).TrimStart('\\'));
					element.SetElementValue(HintPath, hintValue);
				}
			}
		}

		private void DeletePackagesConfig()
		{
			var package = Document.Elements(ItemGroup)
				.Descendants().FirstOrDefault(x => (string)x.Attribute(IncludeAttr) == "packages.config");
			package?.Remove();
		}

		private void DetermineCurrentRef()
		{
			if (RecurseDetermineCurrentRef(Document, SdkSearchPattern))
			{
				CurrentRefType = RefType.Sdk;
			}
			else if (RecurseDetermineCurrentRef(Document, PathToCoreDebug))
			{
				CurrentRefType = RefType.CoreSrc;
			}
			else if (RecurseDetermineCurrentRef(Document, UnitTestSearchPattern))
			{
				CurrentRefType = RefType.UnitTest;
			}
			else if (RecurseDetermineCurrentRef(Document, TsCoreBinPathSearchPattern))
			{
				CurrentRefType = RefType.TsCoreBinPath;
			}
		}

		private bool RecurseDetermineCurrentRef(XElement element, string pattern)
		{
			var result = element.Elements(ItemGroup).Any(el => el.Elements(Reference)
				.Any(elem => elem.Element(HintPath) != null
				 && ((string)elem.Element(HintPath)).Contains(pattern)));
			if (result || !element.HasElements)
			{
				return result;
			}
			foreach (var xElement in element.Elements())
			{
				result = RecurseDetermineCurrentRef(xElement, pattern);
				if (result)
				{
					return true;
				}
			}
			return false;
		}

		private string GetSearchPattern(RefType type)
		{
			switch (type)
			{
				case RefType.Sdk:
				return SdkSearchPattern;
				case RefType.CoreSrc:
				return PathToCoreDebug;
				case RefType.UnitTest:
				return UnitTestSearchPattern;
				case RefType.TsCoreBinPath:
				return TsCoreBinPathSearchPattern;
				default:
				return "undefined";
			}
		}

		private void ChangeReference()
		{
			List<XElement> refElements = new List<XElement>();
			RecurseSearchItemGroup(Document, ref refElements);
			refElements.ForEach(ChangeHint);
		}

		private void RecurseSearchItemGroup(XElement element, ref List<XElement> refElements)
		{
			if (element.HasElements)
			{
				foreach (var xElement in element.Elements())
				{
					RecurseSearchItemGroup(xElement, ref refElements);
				}
			}
			foreach (var el in element.Elements(ItemGroup))
			{
				refElements.AddRange(el.Elements(Reference)
					.Where(elem => elem.Element(HintPath) != null &&
						((string)elem.Element(HintPath)).Contains(GetSearchPattern(CurrentRefType))));
			}
		}

		public CreatioPkgProject RefToBin()
		{
			_activeHint = PathToBinDebug;
			ChangeReference();
			DeletePackagesConfig();
			CurrentRefType = RefType.Bin;
			return this;
		}

		public CreatioPkgProject RefToCoreSrc()
		{
			_activeHint = PathToCoreDebug;
			ChangeReference();
			DeletePackagesConfig();
			CurrentRefType = RefType.CoreSrc;
			return this;
		}

		public CreatioPkgProject RefToCustomPath(string path)
		{
			_activeHint = path;
			ChangeReference();
			CurrentRefType = RefType.Custom;
			return this;
		}

		public CreatioPkgProject RefToUnitBin()
		{
			_activeHint = PathToUnitTestBin;
			var incomeRefType = CurrentRefType;
			CurrentRefType = RefType.UnitTest;
			ChangeReference();
			DeletePackagesConfig();
			CurrentRefType = incomeRefType;
			return this;
		}

		public CreatioPkgProject RefToUnitCoreSrc()
		{
			_activeHint = PathToUnitTestCoreSrc;
			var incomeRefType = CurrentRefType;
			CurrentRefType = RefType.UnitTest;
			ChangeReference();
			DeletePackagesConfig();
			CurrentRefType = incomeRefType;
			return this;
		}

		public void SaveChanges()
		{
			Document.Save(LoadPath);
		}

	}

	public enum RefType
	{
		Undef = 0,
		Sdk = 1,
		Bin = 2,
		CoreSrc = 3,
		Custom = 4,
		UnitTest = 5,
		TsCoreBinPath = 6
	}
}
