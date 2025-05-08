#pragma warning disable CS8618, // Non-nullable field is uninitialized.
using System;
using System.Diagnostics.CodeAnalysis;
using ATF.Repository;
using ATF.Repository.Attributes;

namespace CreatioModel;

[ExcludeFromCodeCoverage]
[Schema("VwWebServiceV2")]
public class VwWebServiceV2 : BaseModel
{

    #region Properties: Public

    [SchemaProperty("Caption")]
    public string Caption { get; set; }

    [SchemaProperty("Description")]
    public string Description { get; set; }

    [SchemaProperty("ExtendParent")]
    public bool ExtendParent { get; set; }

    [SchemaProperty("IsChanged")]
    public bool IsChanged { get; set; }

    [SchemaProperty("IsLocked")]
    public bool IsLocked { get; set; }

    [SchemaProperty("ManagerName")]
    public string ManagerName { get; set; }

    [SchemaProperty("MetaData")]
    public object MetaData { get; set; }

    [SchemaProperty("MetaDataModifiedOn")]
    public DateTime MetaDataModifiedOn { get; set; }

    [SchemaProperty("Name")]
    public string Name { get; set; }

    [SchemaProperty("NeedInstall")]
    public bool NeedInstall { get; set; }

    [SchemaProperty("NeedUpdateSourceCode")]
    public bool NeedUpdateSourceCode { get; set; }

    [SchemaProperty("NeedUpdateStructure")]
    public bool NeedUpdateStructure { get; set; }

    [SchemaProperty("PackageUId")]
    public Guid PackageUId { get; set; }

    [LookupProperty("Parent")]
    public virtual SysSchema Parent { get; set; }

    [SchemaProperty("Parent")]
    public Guid ParentId { get; set; }

    [SchemaProperty("ProcessListeners")]
    public int ProcessListeners { get; set; }

    [LookupProperty("SysPackage")]
    public virtual SysPackage SysPackage { get; set; }

    [SchemaProperty("SysPackage")]
    public Guid SysPackageId { get; set; }

    [LookupProperty("SysWorkspace")]
    public virtual SysWorkspace SysWorkspace { get; set; }

    [SchemaProperty("SysWorkspace")]
    public Guid SysWorkspaceId { get; set; }

    [SchemaProperty("TypeName")]
    public string TypeName { get; set; }

    [SchemaProperty("UId")]
    public Guid UId { get; set; }

    #endregion

}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.
