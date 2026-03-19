namespace Clio.Command {
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

#region Class: PageListItem

[DataContract]
public class PageListItem {
[DataMember(Name = "name")]
public string Name { get; set; }

[DataMember(Name = "uId")]
public string UId { get; set; }

[DataMember(Name = "packageName")]
public string PackageName { get; set; }
}

#endregion

#region Class: PageListResponse

[DataContract]
public class PageListResponse {
[DataMember(Name = "success")]
public bool Success { get; set; }

[DataMember(Name = "count")]
public int Count { get; set; }

[DataMember(Name = "pages")]
public List<PageListItem> Pages { get; set; }

[DataMember(Name = "error")]
public string Error { get; set; }
}

#endregion

#region Class: PageGetResponse

[DataContract]
public class PageGetResponse {
[DataMember(Name = "success")]
public bool Success { get; set; }

[DataMember(Name = "schemaName")]
public string SchemaName { get; set; }

[DataMember(Name = "schemaUId")]
public string SchemaUId { get; set; }

[DataMember(Name = "packageName")]
public string PackageName { get; set; }

[DataMember(Name = "parentSchemaName")]
public string ParentSchemaName { get; set; }

[DataMember(Name = "body")]
public string Body { get; set; }

[DataMember(Name = "error")]
public string Error { get; set; }
}

#endregion

#region Class: PageUpdateResponse

[DataContract]
public class PageUpdateResponse {
[DataMember(Name = "success")]
public bool Success { get; set; }

[DataMember(Name = "schemaName")]
public string SchemaName { get; set; }

[DataMember(Name = "bodyLength")]
public int BodyLength { get; set; }

[DataMember(Name = "dryRun")]
public bool DryRun { get; set; }

[DataMember(Name = "error")]
public string Error { get; set; }
}

#endregion
}
