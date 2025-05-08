#pragma warning disable CS8618, // Non-nullable field is uninitialized.
using System.Diagnostics.CodeAnalysis;
using ATF.Repository;
using ATF.Repository.Attributes;
using YamlDotNet.Serialization;

namespace CreatioModel;

[ExcludeFromCodeCoverage]
[Schema("SysInstalledApp")]
public class SysInstalledApp : BaseModel
{

    #region Fields: Private

    private string _version;

    #endregion

    #region Properties: Public

    [YamlMember(Alias = "aliases")]
    [SchemaProperty("Aliases")]
    public string[] Aliases { get; set; }

    [YamlMember(Alias = "apphub")]
    public string AppHubName { get; set; }

    [YamlMember(Alias = "branch")]
    public string Branch { get; set; }

    [YamlMember(Alias = "code")]
    [SchemaProperty("Code")]
    public string Code { get; set; }

    [SchemaProperty("Description")]
    public string Description { get; set; }

    [YamlMember(Alias = "name")]
    [SchemaProperty("Name")]
    public string Name { get; set; }

    [YamlMember(Alias = "version")]
    [SchemaProperty("Version")]
    public string Version
    {
        get { return string.IsNullOrEmpty(_version) ? "none" : _version; }
        set { _version = value; }
    }

    public string ZipFileName { get; internal set; }

    #endregion

    #region Methods: Public

    public override string ToString()
    {
        return $"\"Id: {Id}, Name: {Name}, Code: {Code}\"";
    }

    #endregion

}

[ExcludeFromCodeCoverage]
[Schema("Contact")]
public class Contact : BaseModel
{

    #region Properties: Public

    [SchemaProperty("Name")]
    public string Name { get; set; }

    #endregion

}

#pragma warning restore CS8618 // Non-nullable field is uninitialized.
