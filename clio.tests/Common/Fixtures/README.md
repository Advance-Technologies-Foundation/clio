# Redaction fixtures

- `site-text-redaction-cases.json`: the Accelerator host lane's 350 invented site texts, each with what must go and what
  must stay (`SiteTextRedactionFixtureTests`); `overMatch` lists the reason phrases master's frozen chain removes.
- `redaction-master-baseline.txt.gz` (gzip, about 0.7 MB; 3.4 MB of text): for each of the 13,597 strings of
  `RedactionDifferentialCorpus`, the tokens master `1915d7543`'s REAL sinks removed - every entry point and every sink of
  `RedactionSinks.All`, driven through master's own seams - and the output pins (16-hex SHA-256 prefixes) of master's
  `Redact` and `RedactCredentials` (`RedactionMasterDifferentialTests`). Read it with `gzip -dc`. Regenerate it from a
  master build with `CLIO_MASTER_DLL=<master>/clio/bin/Debug/net10.0/clio.dll CLIO_MASTER_COMMIT=<full id> dotnet test
  --filter "FullyQualifiedName~RedactionMasterDifferentialTests.RegenerateMasterBaseline"` after changing the corpus or
  the sinks (about 10 s).
