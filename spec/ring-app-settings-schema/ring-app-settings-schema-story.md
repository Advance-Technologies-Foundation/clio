# ClioRing app-settings schema story

Status: review
Issue: #890

As a ClioRing developer, I want editor guidance for app settings so I can select a development clio build without mistaking the deployment channel label for executable selection.

## Definition of done

- [x] Schema covers all supported settings and nested objects.
- [x] Default settings reference the schema.
- [x] Desktop build/publish includes the schema.
- [x] README explains `Channel` and dev-clio precedence with examples.
- [x] Contract tests pass.
- [x] NativeAOT publish passes.
- [x] `C:\Tools\clio-ring` is refreshed while preserving local settings.
