# MCP overview: що AI може створювати через clio MCP

Clio MCP server — це CLI та MCP-сервер для Creatio. Через MCP AI отримує набір `tools`, `prompts` та `resources` для управління Creatio-середовищами, пакетами, схемами і застосунками.

Джерело правди про загальний контракт: [clio/Command/McpServer/McpServerInstructions.cs](../clio/Command/McpServer/McpServerInstructions.cs) — текст інструкцій, що віддається MCP-клієнтам під час ініціалізації.

## Створювані сутності (інструменти `create-*` / `add-*`)

| Інструмент | Сутність | Файл |
|---|---|---|
| `reg-web-app` | Реєстрація нового Creatio-середовища | [RegWebAppTool.cs](../clio/Command/McpServer/Tools/RegWebAppTool.cs) |
| `create-workspace` | Локальний робочий простір Clio | [CreateWorkspaceTool.cs](../clio/Command/McpServer/Tools/CreateWorkspaceTool.cs) |
| `create-app` | Новий застосунок (Composable App) | [ApplicationTool.cs](../clio/Command/McpServer/Tools/ApplicationTool.cs) |
| `create-app-section` | Секція застосунку (з прив'язкою до entity schema) | ApplicationTool.cs |
| `create-page` | Freedom UI сторінка (AMD-модуль, без компіляції) | [PageCreateTool.cs](../clio/Command/McpServer/Tools/PageCreateTool.cs) |
| `create-client-unit-schema` | Низькорівневий client-unit JS schema | [ClientUnitSchemaCreateTool.cs](../clio/Command/McpServer/Tools/ClientUnitSchemaCreateTool.cs) |
| `create-entity-schema` | Таблиця/entity у БД | [EntitySchemaTool.cs](../clio/Command/McpServer/Tools/EntitySchemaTool.cs) |
| `create-lookup` | Довідник | EntitySchemaTool.cs |
| `add-item-model` | Елемент моделі (колонки, реляції тощо) | [AddItemModelTool.cs](../clio/Command/McpServer/Tools/AddItemModelTool.cs) |
| `create-entity-business-rule` | Бізнес-правило на рівні entity | [BusinessRuleTool.cs](../clio/Command/McpServer/Tools/BusinessRuleTool.cs) |
| `create-data-binding` | JSON-біндинги через пакет | [DataBindingTool.cs](../clio/Command/McpServer/Tools/DataBindingTool.cs) |
| `add-data-binding-row` | Рядок у JSON-біндинг | DataBindingTool.cs |
| `create-data-binding-db` | Біндинг прямо у БД | [DataBindingDbTool.cs](../clio/Command/McpServer/Tools/DataBindingDbTool.cs) |
| `upsert-data-binding-row-db` | Upsert рядка у БД | DataBindingDbTool.cs |
| `create-schema` | Generic-схема (включно з C#/Source code) | [SchemaCreateTool.cs](../clio/Command/McpServer/Tools/SchemaCreateTool.cs) |
| `create-sql-schema` | SQL-скриптова схема | [SqlSchemaCreateTool.cs](../clio/Command/McpServer/Tools/SqlSchemaCreateTool.cs) |
| `generate-process-model` | Модель бізнес-процесу | [GenerateProcessModelTool.cs](../clio/Command/McpServer/Tools/GenerateProcessModelTool.cs) |

## Категорії інструментів за безпекою

Кожен інструмент маркований у `[McpServerTool]` атрибуті:

- **ReadOnly** — не змінює стан (`list-pages`, `get-schema`, `list-environments`, `get-component-info`).
- **Destructive** — змінює/видаляє дані (всі `create-*`, `update-*`, `delete-*`).
- **Idempotent** — безпечно повторювати (read-only зазвичай так, mutate — ні).
- **OpenWorld** — взаємодіє з зовнішніми системами поза локальним станом.

## Правила компіляції

Із [McpServerInstructions.cs](../clio/Command/McpServer/McpServerInstructions.cs):

**`compile-creatio` ПОТРІБЕН**:
- Додавання/зміна C# схем (Source code, SqlScript, бізнес-процеси з виконуваним кодом).
- Після `push-workspace` якщо пакети містять перелічене вище.
- Відновлення з помилки «schema is missing in runtime».

**`compile-creatio` НЕ потрібен**:
- Після `create-app`, `create-app-section`, `create-page`, `update-page` — Freedom UI bodies це AMD-модулі у runtime.
- Після `create-entity-schema` / `update-entity-schema` / `modify-entity-schema-column` — інструменти самі застосовують DDL і refresh runtime-схему.
- Після `create-data-binding` / `add-data-binding-row` / `upsert-data-binding-row-db` — data seeding не змінює скомпільованих артефактів.

## Структура папок MCP server

```
clio/Command/McpServer/
├── AGENTS.md                    — конвенції для розробки MCP-tools
├── McpServerCommand.cs          — entry point, hosting
├── McpServerInstructions.cs     — інструкції для AI-клієнтів
├── Data/
│   └── ComponentRegistry.json   — кур-каталог Freedom UI компонентів
├── Tools/                       — всі MCP-tools (~70+ файлів)
├── Prompts/                     — готові prompt-и для типових сценаріїв
└── Resources/                   — guidance-резурси для AI
    ├── PageCreationGuidanceResource.cs
    ├── PageModificationGuidanceResource.cs
    ├── PageSchemaConvertersGuidanceResource.cs
    ├── PageSchemaHandlersGuidanceResource.cs
    ├── PageSchemaValidatorsGuidanceResource.cs
    ├── DataBindingsGuidanceResource.cs
    ├── DataForgeOrchestrationGuidanceResource.cs
    └── ...
```

## Правила для розробки нових MCP-tools

Із [clio/Command/McpServer/AGENTS.md](../clio/Command/McpServer/AGENTS.md):

- Похідні від `BaseTool<TOptions>`.
- Два шляхи виконання:
  - `InternalExecute(options)` — для команд без per-call environment.
  - `InternalExecute<TCommand>(options)` — для environment-чутливих команд (наявність `environment-name`/URI/login/password у options).
- Environment-чутливі команди завжди резолвити свіжою інстанцією через `InternalExecute<TCommand>`, не використовувати закешований startup-time command.
- Маркувати destructive флаги коректно.
- E2E coverage у [clio.mcp.e2e](../clio.mcp.e2e) обов'язковий для будь-яких змін.
