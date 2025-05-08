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
    private string _version;

    [YamlMember(Alias = "name")]
    [SchemaProperty("Name")]
    public string Name { get; set; }

    [YamlMember(Alias = "code")]
    [SchemaProperty("Code")]
    public string Code { get; set; }

    [YamlMember(Alias = "aliases")]
    [SchemaProperty("Aliases")]
    public string[] Aliases { get; set; }

    [SchemaProperty("Description")] public string Description { get; set; }

    [YamlMember(Alias = "version")]
    [SchemaProperty("Version")]
    public string Version
    {
        get => string.IsNullOrEmpty(_version) ? "none" : _version;
        set => _version = value;
    }

    [YamlMember(Alias = "apphub")] public string AppHubName { get; set; }

    public string ZipFileName { get; internal set; }

    [YamlMember(Alias = "branch")] public string Branch { get; set; }

    public override string ToString() => $"\"Id: {Id}, Name: {Name}, Code: {Code}\"";
}

[ExcludeFromCodeCoverage]
[Schema("Contact")]
public class Contact : BaseModel
{
    [SchemaProperty("Name")] public string Name { get; set; }
}

#pragma warning restore CS8618 // Non-nullable field is uninitialized.
