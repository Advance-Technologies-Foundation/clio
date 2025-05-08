using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Clio.Project;

namespace Clio;

public class CreatioPkgProject : ICreatioPkgProject
{

    #region Constants: Private

    private const string HintElementName = "HintPath";
    private const string ItemGroupElementName = "ItemGroup";
    private const string PathToBinDebug = @"..\..\..\Bin\";
    private const string PathToCoreDebug = @"..\..\..\..\..\..\..\Bin\Debug\";
    private const string PathToUnitTestBin = @"..\..\UnitTest\bin\Debug\";
    private const string PathToUnitTestCoreSrc = @"..\..\..\UnitTest\bin\Debug\";
    private const string ReferenceElementName = "Reference";
    private const string SdkSearchPattern = "BpmonlineSDK";
    private const string TsCoreBinPathSearchPattern = "$(TsCoreBinPath)";
    private const string UnitTestSearchPattern = "UnitTest";

    #endregion

    #region Fields: Private

    private string _activeHint;

    #endregion

    #region Constructors: Private

    private CreatioPkgProject(string path)
    {
        LoadPath = path;
        Document = XElement.Load(path, LoadOptions.SetBaseUri);
        Namespace = Document.Name.Namespace;
        DetermineCurrentRef();
    }

    #endregion

    #region Constructors: Public

    public CreatioPkgProject()
    { }

    #endregion

    #region Properties: Private

    private static XName IncludeAttr => "Include";

    private XName HintPath => Namespace + HintElementName;

    private XName ItemGroup => Namespace + ItemGroupElementName;

    private XName Reference => Namespace + ReferenceElementName;

    #endregion

    #region Properties: Public

    public RefType CurrentRefType { get; private set; }

    public XElement Document { get; }

    public string LoadPath { get; }

    public XNamespace Namespace { get; }

    #endregion

    #region Methods: Private

    private void ChangeHint(XElement element)
    {
        string value = element.Attribute(IncludeAttr)?.Value;
        if (value != null)
        {
            int index = value.IndexOf(",", StringComparison.Ordinal);
            if (index > 0)
            {
                value = value.Substring(0, index);
                element.SetAttributeValue(IncludeAttr, value);
            }
        }
        string hintValue = element.Element(HintPath)?.Value;
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

    private void ChangeReference()
    {
        List<XElement> refElements = new();
        RecurseSearchItemGroup(Document, ref refElements);
        refElements.ForEach(ChangeHint);
    }

    private void DeletePackagesConfig()
    {
        XElement package = Document.Elements(ItemGroup)
                                   .Descendants().FirstOrDefault(x =>
                                       (string)x.Attribute(IncludeAttr) == "packages.config");
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

    private bool RecurseDetermineCurrentRef(XElement element, string pattern)
    {
        bool result = element.Elements(ItemGroup).Any(el => el.Elements(Reference)
                                                              .Any(elem => elem.Element(HintPath) != null
                                                                  && ((string)elem.Element(HintPath))
                                                                  .Contains(pattern)));
        if (result || !element.HasElements)
        {
            return result;
        }
        foreach (XElement xElement in element.Elements())
        {
            result = RecurseDetermineCurrentRef(xElement, pattern);
            if (result)
            {
                return true;
            }
        }
        return false;
    }

    private void RecurseSearchItemGroup(XElement element, ref List<XElement> refElements)
    {
        if (element.HasElements)
        {
            foreach (XElement xElement in element.Elements())
            {
                RecurseSearchItemGroup(xElement, ref refElements);
            }
        }
        foreach (XElement el in element.Elements(ItemGroup))
        {
            refElements.AddRange(el.Elements(Reference)
                                   .Where(elem => elem.Element(HintPath) != null &&
                                       ((string)elem.Element(HintPath)).Contains(GetSearchPattern(CurrentRefType))));
        }
    }

    #endregion

    #region Methods: Public

    public static CreatioPkgProject LoadFromFile(string path)
    {
        return new CreatioPkgProject(path);
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
        RefType incomeRefType = CurrentRefType;
        CurrentRefType = RefType.UnitTest;
        ChangeReference();
        DeletePackagesConfig();
        CurrentRefType = incomeRefType;
        return this;
    }

    public CreatioPkgProject RefToUnitCoreSrc()
    {
        _activeHint = PathToUnitTestCoreSrc;
        RefType incomeRefType = CurrentRefType;
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

    #endregion

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
