#pragma warning disable CS8618, // Non-nullable field is uninitialized.
using System.Diagnostics.CodeAnalysis;
using ATF.Repository;
using ATF.Repository.Attributes;

namespace CreatioModel;

[ExcludeFromCodeCoverage]
[Schema("AppFeature")]
public class AppFeature : BaseModel
{

    #region Properties: Public

    [SchemaProperty("Code")]
    public string Code { get; set; }

    [SchemaProperty("Description")]
    public string Description { get; set; }

    [SchemaProperty("Name")]
    public string Name { get; set; }

    [SchemaProperty("Source")]
    public string Source { get; set; }

    [SchemaProperty("State")]
    public bool State { get; set; }

    [SchemaProperty("StateForCurrentUser")]
    public bool StateForCurrentUser { get; set; }

    #endregion

}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.
