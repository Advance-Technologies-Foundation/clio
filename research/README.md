# Дослідження: MCP-компоненти і версіонування

Дослідження з еволюції каталогу Freedom UI компонентів, що доступний AI через clio MCP server.

## Контекст

Clio MCP server експонує AI-агентам набір інструментів для роботи з Creatio: створення сторінок, схем, компонентів, керування середовищами тощо. Один із ключових інструментів — `get-component-info` — повертає AI кур-каталог Freedom UI компонентів із описом властивостей.

Поточний каталог:
- 92 компоненти в 5 категоріях
- Зашитий у [clio/Command/McpServer/Data/ComponentRegistry.json](../clio/Command/McpServer/Data/ComponentRegistry.json)
- Жодної version-метадати

Це дослідження аналізує, **як підтримувати каталог при еволюції платформи** (нові версії додають/змінюють/прибирають компоненти і властивості) і **як зробити цю підтримку автоматичною** на основі коду платформи.

## Документи

1. [01-mcp-overview.md](01-mcp-overview.md) — що MCP дозволяє створювати AI (повний перелік `create-*`/`add-*` інструментів і guidance resources).
2. [02-adding-components-to-pages.md](02-adding-components-to-pages.md) — детальний flow, як AI додає компонент на Freedom UI сторінку (`get-page` → `update-page`, marker envelope, append-mode, виявлення parentName).
3. [03-available-components.md](03-available-components.md) — повний список 92 компонентів за категоріями (containers / fields / interactive / display / filtering).
4. [04-multi-version-target-structure.md](04-multi-version-target-structure.md) — цільова структура для підтримки кількох версій платформи: per-entry `availability`, resolver версії, `target-version` resolution stack, відкриті питання.
5. [05-source-of-truth-automation.md](05-source-of-truth-automation.md) — автоматичний SoT із репо `creatio-ui`: AST-екстракція з `@CrtViewElement` декораторів + `*ViewConfig` інтерфейсів, JSDoc-вокабуляр, CI pipeline, npm-публікація, composer-фаза в clio.

## Зведена картина

```
┌──────────────────── creatio-ui ────────────────────┐
│                                                    │
│  @CrtViewElement + *ViewConfig + JSDoc             │
│              │                                     │
│  AST extractor (tools/component-registry-extractor)│
│              │                                     │
│  component-registry.<ver>.json                     │
│              │                                     │
│  Jenkins (.pipeline/Jenkinsfile.Registry)          │
│              │                                     │
│  npm publish @creatio/component-registry@<ver>     │
└──────────────┼─────────────────────────────────────┘
               ▼
┌──────────────────── clio ──────────────────────────┐
│                                                    │
│  composer:                                         │
│  • npm install усіх підтримуваних версій           │
│  • diff snapshot-ів → availability ranges         │
│  • merge overrides.json (AI-специфічні хінти)      │
│              │                                     │
│  ComponentRegistry.json (with availability)        │
│              │                                     │
│  ComponentInfoCatalog → IPlatformVersionResolver  │
│              │                                     │
│  MCP: get-component-info → відфільтровано по версії│
└────────────────────────────────────────────────────┘
```

## Статус

Документи описують цільовий стан і проміжні рішення; реалізація поки не розпочата. Поточний `ComponentInfoCatalog` залишається без змін до моменту pilot-фази.
