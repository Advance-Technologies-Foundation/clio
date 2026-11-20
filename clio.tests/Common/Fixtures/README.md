# Redaction fixtures

- `site-text-redaction-cases.json`: the Accelerator host lane's 350 invented site texts, each with what must go and what
  must stay (`SiteTextRedactionFixtureTests`); `overMatch` lists the reason phrases master's frozen chain removes.
- `redaction-master-baseline.txt`: the tokens master `1915d7543`'s redactor removed from each string of
  `RedactionDifferentialCorpus` (`RedactionMasterDifferentialTests`). Regenerate it from a master build with
  `CLIO_MASTER_DLL=<master>/clio/bin/Debug/net10.0/clio.dll CLIO_MASTER_COMMIT=<full id> dotnet test --filter
  "FullyQualifiedName~RedactionMasterDifferentialTests.RegenerateMasterBaseline"` after changing the corpus.
