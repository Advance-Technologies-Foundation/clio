﻿using CommandLine;

namespace Clio.Command;

[Verb("upload-licenses", Aliases = new[] { "lic" }, HelpText = "Upload licenses")]
public class UploadLicensesOptions : RemoteCommandOptions
{
    [Value(0, MetaName = "FilePath", Required = true, HelpText = "License file path")]
    public string FilePath { get; set; }
}
