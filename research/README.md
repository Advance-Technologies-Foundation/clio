# Дослідження: MCP-компоненти і версіонування

Дослідження з еволюції каталогу Freedom UI компонентів, що доступний AI через clio MCP server.

## Контекст

Clio MCP server експонує AI-агентам набір інструментів для роботи з Creatio: створення сторінок, схем, компонентів, керування середовищами. Один із ключових інструментів — `get-component-info` — повертає AI кур-каталог Freedom UI компонентів із описом властивостей.

Поточний каталог:
- 92 компоненти в 5 категоріях
- Зашитий у [clio/Command/McpServer/Data/ComponentRegistry.json](../clio/Command/McpServer/Data/ComponentRegistry.json)
- Жодної version-метадати

Це дослідження визначає **цільову архітектуру**: як підтримувати каталог при еволюції платформи (нові версії додають/змінюють/прибирають компоненти і властивості), як зробити цю підтримку автоматичною на основі коду платформи, і як вибрати оптимальне сховище та distribution-канали.

## Документи

1. [03-extraction-analysis.md](03-extraction-analysis.md) — extractor-фільтри, інваріант `**/designtime/**`, цифри 192/92/100, повний список 100 нових кандидатів для seeding `overrides.json`.
2. [04-multi-version-target-structure.md](04-multi-version-target-structure.md) — цільова структура реєстру в clio: per-entry `availability`, resolver версії, `target-version` resolution stack, fallback policy, NuGet-base loader.
3. [05-source-of-truth-automation.md](05-source-of-truth-automation.md) — цільова архітектура SoT із трьома доменами: creatio-ui (source) → composer-repo (integration) → clio (consumer). NuGet-distribution, ownership matrix, failure-mode design, версіонування `Creatio.ComponentRegistry`.

## Цільова архітектура

```
                  ┌──────────── creatio-ui ────────────┐
                  │   @CrtViewElement + *ViewConfig    │
                  │   + JSDoc (@since, @aiCategory)    │
                  │             │                      │
                  │   AST extractor (Jenkins on        │
                  │   branch-cut / push / GA-tag)      │
                  └─────────────┼──────────────────────┘
                                │ npm publish (GA only)
                                ▼
              ┌─────── @creatio/component-registry ─────┐
              │   per-version sharded JSON              │
              └─────────────┼───────────────────────────┘
                            │ npm install (composer CI)
                            ▼
   ┌────── creatio-component-registry-composer ────────┐
   │   supported-versions.json (manual PR)             │
   │   overrides.json (AI team owned)                  │
   │   ──────────────────────                          │
   │   diff snapshots → availability ranges            │
   │   merge overrides → unified bundle                │
   │   stamp metadata.json (provenance)                │
   │   dotnet pack + nuget push                        │
   └─────────────┼─────────────────────────────────────┘
                 │ NuGet publish (independent semver)
                 ▼
       ┌─── Creatio.ComponentRegistry NuGet pkg ───┐
       │   ComponentRegistry.json (unified)        │
       │   metadata.json (provenance)              │
       └─────────────┼─────────────────────────────┘
                     │ <PackageReference> у Directory.Packages.props
                     ▼
           ┌─────── clio.csproj ────────┐
           │   ComponentInfoCatalog     │
           │     (Assembly.GetManifest  │
           │      ResourceStream)       │
           │   IPlatformVersionResolver │
           │   MCP get-component-info   │
           └────────────────────────────┘
```

## Ключові архітектурні рішення

- **Три ізольовані домени** з ownership matrix: creatio-ui (Platform-UI team), composer-repo (AI / clio team), clio (clio team). Жоден не має write-access до іншого окрім стандартних PR-flow.
- **Distribution**: NPM (UI → composer) + NuGet (composer → clio). Незалежні semver на обох ланцюгах.
- **Storage у clio = NuGet embedded resource.** Файл `clio/Command/McpServer/Data/ComponentRegistry.json` видаляється з clio repo; loader читає через `Assembly.GetManifestResourceStream`.
- **Runtime serving — 100% offline.** Жодного network call під час AI MCP сесії.
- **Version resolution**: explicit > environment-name + GetSysInfo probe > latest-fallback. Завжди повертається `resolvedTargetVersion` + `resolvedFrom`.
- **Fallback policy**: `latest known` (максимальна `since`-версія, обчислена composer-фазою). Не `unrestricted`, не `error`, не `oldest`.
- **Composer живе у власному repo** `creatio-component-registry-composer` — single responsibility, ізольована CI, відокремлена від UI source і consumer.

## Статус

Документи описують **цільовий стан**. Реалізація поки не розпочата; поточний `ComponentInfoCatalog` залишається без змін.

Етапи доставки — у [04-multi-version-target-structure.md](04-multi-version-target-structure.md#етапи-доставки-target-architecture). Без проміжного pilot-етапу; кожен етап завершується release-ом NuGet/NPM-пакета у production-feed.
