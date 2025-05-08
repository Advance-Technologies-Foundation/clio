using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Clio.Dto;

internal class EntitySchemaResponse
{

    #region Properties: Public

    [JsonProperty("collection")]
    public List<EntitySchema> Collection { get; set; }

    #endregion

}

internal class EntitySchema
{

    #region Properties: Public

    [JsonProperty("caption")]
    public string Caption { get; set; }

    [JsonProperty("extendParent")]
    public bool ExtendsParent { get; set; }

    [JsonProperty("id")]
    public Guid Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("packageUId")]
    public Guid PackageUId { get; set; }

    [JsonProperty("parentUId")]
    public Guid ParentUId { get; set; }

    [JsonProperty("uId")]
    public Guid Uid { get; set; }

    [JsonProperty("isVirtual")]
    public bool Virtual { get; set; }

    #endregion

}
