#pragma warning disable CS8618, // Non-nullable field is uninitialized.
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using ATF.Repository;
using ATF.Repository.Attributes;

namespace CreatioModel;

[ExcludeFromCodeCoverage]
[Schema("VwSysSetting")]
public class VwSysSetting : BaseModel
{

    #region Properties: Public

    [SchemaProperty("Code")]
    public string Code { get; set; }

    [SchemaProperty("Description")]
    public string Description { get; set; }

    [SchemaProperty("IsCacheable")]
    public bool IsCacheable { get; set; }

    [SchemaProperty("IsPersonal")]
    public bool IsPersonal { get; set; }

    [SchemaProperty("IsSSPAvailable")]
    public bool IsSSPAvailable { get; set; }

    [SchemaProperty("Name")]
    public string Name { get; set; }

    [LookupProperty("ReferenceSchemaUId")]
    public virtual SysSchema ReferenceSchemaUId { get; set; }

    [SchemaProperty("ReferenceSchemaUId")]
    public Guid ReferenceSchemaUIdId { get; set; }

    [SchemaProperty("ValueTypeName")]
    public string ValueTypeName { get; set; }

    #endregion

}

[ExcludeFromCodeCoverage]
[Schema("SysSettings")]
public class SysSettings : BaseModel
{

    #region Fields: Private

    private readonly Guid AllUsersId = new("a29a3ba5-4b0d-de11-9a51-005056c00008");

    #endregion

    #region Properties: Public

    [SchemaProperty("Code")]
    public string Code { get; set; }

    public string DefValue
    {
        get { return GetDefaultValue(); }
    }

    [SchemaProperty("Description")]
    public string Description { get; set; }

    [SchemaProperty("IsCacheable")]
    public bool IsCacheable { get; set; }

    // [LookupProperty("ReferenceSchemaUId")]
    // public virtual SysSchema ReferenceSchemaUId { get; set; }

    [SchemaProperty("IsPersonal")]
    public bool IsPersonal { get; set; }

    [SchemaProperty("IsSSPAvailable")]
    public bool IsSSPAvailable { get; set; }

    [SchemaProperty("Name")]
    public string Name { get; set; }

    [SchemaProperty("ReferenceSchemaUId")]
    public Guid ReferenceSchemaUIdId { get; set; }

    [DetailProperty("SysSettingsId")]
    public virtual List<SysSettingsValue> SysSettingsValues { get; set; }

    [SchemaProperty("ValueTypeName")]
    public string ValueTypeName { get; set; }

    #endregion

    #region Methods: Private

    private string GetDefaultValue(string adminUnitName = null)
    {
        SysSettingsValue sysSettingsValue
            = SysSettingsValues?.Where(x => x.SysAdminUnitId == AllUsersId)?.FirstOrDefault();
        if (sysSettingsValue != null)
        {
            switch (ValueTypeName)
            {
                case "Boolean":
                    return sysSettingsValue.BooleanValue.ToString().ToLower();
                case "MediumText":
                case "ShortText":
                case "LongText":
                case "Text":
                    return sysSettingsValue.TextValue;
                case "Integer":
                    return sysSettingsValue.IntegerValue.ToString();
                case "Date":
                    return sysSettingsValue.DateTimeValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                case "Time":
                    return sysSettingsValue.DateTimeValue.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                case "DateTime":
                    return sysSettingsValue.DateTimeValue.ToString("o", CultureInfo.InvariantCulture);
                case "Float":
                case "Decimal":
                case "Currency":
                    return sysSettingsValue.FloatValue.ToString(CultureInfo.InvariantCulture);
                case "Lookup":
                    return sysSettingsValue.GuidValue.ToString();
            }
        }
        return "undefined";
    }

    #endregion

}

[ExcludeFromCodeCoverage]
[Schema("SysSettingsValue")]
public class SysSettingsValue : BaseModel
{

    #region Properties: Public

    [SchemaProperty("BooleanValue")]
    public bool BooleanValue { get; set; }

    [SchemaProperty("DateTimeValue")]
    public DateTime DateTimeValue { get; set; }

    [SchemaProperty("FloatValue")]
    public decimal FloatValue { get; set; }

    [SchemaProperty("GuidValue")]
    public Guid GuidValue { get; set; }

    [SchemaProperty("IntegerValue")]
    public int IntegerValue { get; set; }

    [SchemaProperty("IsDef")]
    public bool IsDef { get; set; }

    [LookupProperty("SysAdminUnit")]
    public virtual SysAdminUnit SysAdminUnit { get; set; }

    [SchemaProperty("SysAdminUnit")]
    public Guid SysAdminUnitId { get; set; }

    [LookupProperty("SysSettings")]
    public virtual SysSettings SysSettings { get; set; }

    [SchemaProperty("SysSettings")]
    public Guid SysSettingsId { get; set; }

    [SchemaProperty("TextValue")]
    public string TextValue { get; set; }

    #endregion

}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.
