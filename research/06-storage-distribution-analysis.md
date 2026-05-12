# Storage / distribution analysis

Документ обґрунтовує архітектурний вибір: чому фінальне зберігання — **NuGet `Creatio.ComponentRegistry`**, а не commit-нутий JSON у clio repo. Базовий контекст і вимоги — у [04](04-multi-version-target-structure.md) і [05](05-source-of-truth-automation.md).

## Три виміри, які треба розрізняти

Питання «де зберігати каталог» змішує три незалежні рішення:

| Вимір | Хто страждає при поганому виборі |
|---|---|
| **Authoring/storage** — як автор редагує і Git зберігає дані | UI / AI команда (developer ergonomics) |
| **Distribution** — як файл потрапляє з extractor-а до consumer-а | Operations (CI complexity, version-pin discipline) |
| **Runtime serving** — як consumer віддає AI | AI Coding Agent (latency, reliability, offline) |

Багато варіантів конкурують у межах одного виміру, а не виключають один одного. Підсумкове рішення — **композиція** з трьох незалежних виборів.

## 10 розглянутих варіантів

1. **Single embedded JSON у clio** (статус-кво). Один `ComponentRegistry.json` ~500KB–1MB у clio repo.
2. **Per-component sharded JSON у clio.** `Data/components/crt.Button.json`, … Loader enumerate-ить директорію.
3. **Per-category JSON у clio.** `containers.json`, `fields.json`, … Compromise sharding.
4. **Sharded authoring + bundled final.** Розробники редагують sharded, build step бандлить у один JSON.
5. **Embedded SQLite database.** Binary `registry.db` shipped разом з clio. Queryable.
6. **NPM package** (`@creatio/component-registry`). Semver-aligned з platform, consumed на CI стороні clio.
7. **NuGet package** (`Creatio.ComponentRegistry`). Native до .NET; `Directory.Packages.props` pin.
8. **GitHub Releases artifacts.** Per-version JSON як attachments у release. Composer fetch через `gh release download`.
9. **Cloud static JSON + local cache.** S3 / Azure Blob (+ опційний CDN). Clio fetch на startup, кеш з TTL.
10. **Cloud REST API service.** Окремий micro-service. Clio MCP робить HTTP-запити, кешує локально.

## Критерії оцінки

Кожен по 0–5; max sum 25.

- **П** — Підтримка (Git review, merge conflicts, edit ergonomics)
- **С** — Стабільність доступу (offline, fault-tolerance, runtime guarantees)
- **А** — Зрозуміла архітектура (cognitive load, debuggability, # moving parts)
- **Т** — Сучасні технології (industry standard, supported tooling)
- **AI** — AI Coding Agent UX (швидкість response, надійність, ефективність)

## Підсумкова таблиця (sorted)

| # | Рішення | Вимір | П | С | А | Т | AI | **Score** | AI-нотатки |
|---|---|---|---|---|---|---|---|---|---|
| 7 | **NuGet package** `Creatio.ComponentRegistry` | Distribution | 4 | 5 | 5 | 5 | 5 | **24** | Runtime in-memory; version-pin → AI бачить стабільний snapshot |
| 2 | **Per-component sharded, embedded** | Storage | 5 | 5 | 4 | 3 | 5 | **22** | Можливий lazy-load per crt.X → ще швидший cold-start |
| 4 | **Sharded authoring + bundled distribution** | Storage | 5 | 5 | 3 | 4 | 5 | **22** | На runtime ідентичний #1; bundle single-load |
| 6 | **NPM package** `@creatio/component-registry` | Distribution | 4 | 4 | 4 | 5 | 5 | **22** | Composer розпаковує в embedded — AI не бачить різниці |
| 3 | **Per-category, embedded** | Storage | 3 | 5 | 4 | 3 | 5 | **20** | Coarser sharding, не впливає на AI |
| 1 | **Single embedded JSON** (статус-кво) | Storage | 2 | 5 | 5 | 2 | 5 | **19** | In-memory після parse; 1MB JSON parse ~50ms — несуттєво |
| 5 | **Embedded SQLite** | Storage | 1 | 5 | 3 | 4 | 5 | **18** | Queryable — потенціал для AI запитів типу «всі компоненти з prop X», але вимагає змін MCP API |
| 8 | **GitHub Releases artifacts** | Distribution | 2 | 4 | 3 | 3 | 5 | **17** | Як distribution — runtime однаковий з #1 |
| 9 | **Cloud static JSON + local cache** | Distribution | 3 | 3 | 3 | 4 | 3 | **16** | First-call 100–500ms; cache staleness; offline dev зламано |
| 10 | **Cloud REST API** | Runtime serving | 1 | 2 | 2 | 4 | 2 | **11** | Кожен MCP tool-call це network round-trip 50–500ms; failures блокують AI workflow |

## Ключові висновки

1. **Distribution choice → admin productivity** (як ми оновлюємо registry). **Storage choice → AI productivity** (як AI читає registry).
2. **Cloud-варіанти (#9, #10)** — drop-in killers для AI runtime UX. Кожен MCP-запит платить network cost; cache не амортизується через короткі AI-сесії та паралельні MCP-clients у різних IDE.
3. **Embedded resource (будь-який варіант #1–#7) однаково гарний для AI** — read-only static data, in-memory після першого parse.
4. **Status quo #1 провалюється на scale.** При 200+ компонентах файл стає 1MB+, merge-conflict-и при паралельних PR-ах гарантовано.

## Композиція-переможець

Жоден з 10-ти **наодинці** не закриває всі три виміри. Архітектурно правильна відповідь — **#6 + #7 + #2** як композитна архітектура:

```
Authoring     → #2 sharded per-component (внутрішньо в NPM pkg)
Distribution  → #6 NPM @creatio/component-registry (UI → composer)
Re-distrib    → #7 NuGet Creatio.ComponentRegistry (composer → clio)
Runtime serv  → embedded resource у NuGet pkg (#7 final)
```

Composite score = **(22 + 22 + 24) / 3 = 22.67/25.**

Деталі топології, ownership і failure-mode — у [05-source-of-truth-automation.md](05-source-of-truth-automation.md#цільова-архітектура).

## Чому не «#6 + #1»

Це був попередній план — NPM як distribution, single embedded JSON commit-нутий у clio repo як final storage.

Composite score = **(22 + 19) / 2 = 20.5/25.** Втрачено 2.17 балів проти оптимальної композиції. Конкретні проблеми, які #1 на final-ланці тягне:

1. **Composer auto-PR-ить single JSON у clio repo щоразу.** При 200+ компонентах × N supported versions кожен релізний пробіг дає 1MB diff у git history. Review безкорисний.
2. **Немає version-pin-у composed registry.** clio v1.10 і v1.11 з різними registry-станами можна ідентифікувати тільки за git-blame.
3. **Dependabot не моніторить.** Composed registry оновлюється «бот-commit»-ом, не bump-ом версії залежності.
4. **Merge-conflicts при паралельних PR-ах** — практично гарантовано.

NuGet final-storage (#7) розв'язує всі чотири пункти за рахунок однієї додаткової ланки у composer pipeline (`dotnet pack` + `dotnet nuget push`).

## Чого свідомо НЕ робимо

- **Cloud API (#10)** — over-engineering для read-only статики; runtime network dependency.
- **Cloud static + cache (#9)** — допустимо тільки як retreat-варіант, якщо internal NuGet feed закритий compliance-вимогами.
- **SQLite (#5)** — overkill для 200 records; не дає переваг queryability при in-memory кеші.
- **GitHub Releases (#8)** — нестандартний для catalog distribution; release-PR ceremony при кожному drop-і.

## Єдиний reasonable trade-off NuGet-варіанту

**Втрата візуальної інспекції JSON у IDE.** Сирий registry лежить у NuGet кеші (`~/.nuget/packages/creatio.componentregistry/x.y.z/`), не безпосередньо в repo.

Mitigation: composer додатково публікує human-readable `registry.<ver>.json` як Jenkins artifact / release notes attachment. 5 хв роботи в pipeline; розв'язує inspection use case.
