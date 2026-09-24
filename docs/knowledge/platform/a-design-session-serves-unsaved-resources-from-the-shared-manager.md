---
description: While a process is open in a design session, its app-pool-wide resource manager answers the session snapshot - values not yet saved, including the captions the design-time load just synchronized from called processes - until something reloads it from SysLocalizableValue; loading the process's runtime instance (SchemaManagerItem.Instance) is one such reload, so "what is stored" must be read from the database, never from that manager
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-100077
date: 2026-09-24
---

**What is true** — measured on a .NET Framework stand inside ONE CrtProcessBuilder modify request on a sub-process
caller (diagnostic build, 2026-09-24): the element caption's resource manager (`Workspace.ResourceStorage`, named
after the process UId) answered the caption the design-time load had just copied from the called process ("A9"),
which was not saved anywhere. Right after `ProcessSchemaManager.FindItemByUId(<caller>).Instance`, the SAME manager
object answered the value in `SysLocalizableValue` ("A8"). Source: the session snapshot is serialized from the
in-memory values after the design-load sync (`ResourcePackage`), `FindDesignItem` → `UpdateResourceManager` loads
it into the shared manager (`SchemaManager.cs:4621-4661, 2712-2720`), and building the runtime instance ends in
`InitializeSchemaResourceManager` → `ReleaseAllResources()` (`SchemaManager.cs:1057`), after which the next read
reloads from the database.

**Why it is this way** — the platform keeps one resource manager per schema for the whole app pool and lets a
design session overwrite it with the session's view; nothing distinguishes saved from unsaved values in it.

**What breaks if you ignore it** — a server-side "compare with what was stored" that reads the shared manager
compares the new value with itself and reports nothing, or works only while some unrelated call happens to load
the runtime instance first. An unreleased CrtProcessBuilder 1.6.6.19 build carried such a caption report: it
answered correctly only because the self-reference check loaded the caller's instance, and a later change to that
check silenced it. The report was removed (captions are display text; binding goes by name and UId). If one is
ever needed again, read the stored side from the database - a `Select` on `SysLocalizableValue` with no-lock
hints, as CrtCampaignUtils' `CampaignEventHandler.SerializeLocalizableValues` does - never through the shared
manager.
