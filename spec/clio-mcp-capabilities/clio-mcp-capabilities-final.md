# Тема та тези для SC: Clio MCP: capabilities

> Фінальна версія для подачі на Speaking Corner. Повний скрипт спікера, сценарій демо й
> розкладку слайдів див. у `clio-mcp-capabilities-presentation-outline.md`.

---

## Тема

**Clio MCP: capabilities**
*Що вміє AI‑агент на Creatio — і як це зроблено, щоб працювало з LLM.*

**Формат:** ~1 год, технічна аудиторія, 2 блоки (50/50), жива демка в Блоці 1.

---

## Абстракт

clio — це CLI **і** MCP‑сервер для платформи Creatio. Через MCP будь‑який AI‑агент отримує доступ
до **120+ інструментів** clio і може вести повний цикл розробки: від підняття середовища до готової
Freedom UI сторінки.

Доповідь із двох частин. Перша — **як агент будує застосунок**: створення додатку, об'єктів і
сторінок, хендлери та валідатори, добір компонентів, бізнес‑правила (з живою демкою: сутність →
сторінка → компонент → правило). Друга — **як** це зроблено інженерно, щоб велика поверхня була придатною для LLM:
lazy‑schema (≈ −97% контексту), guidance‑as‑code, version‑accurate каталог компонентів,
мультитенантний HTTP edge, типізований стрімінг прогресу та вбудована safety‑модель.

Наскрізна теза: **Clio MCP — це кейс LLM‑facing API design, а не «обгортка над CLI».**
Метафора доповіді: MCP — «розетка»; clio — «руки і знання» для AI.

---

## Тези

### 🔵 Блок 1 — Основні функціональні можливості

> **Головна теза:** AI‑агент проходить повний шлях створення додатку Creatio — від структури
> застосунку до сторінок із логікою — **компонуючи** кроки, а не виконуючи окремі команди.
> Наскрізь Freedom UI: додаток → об'єкти → сторінки → хендлери/валідатори → компоненти → бізнес‑правила.

1. **Створення додатку.** Агент створює застосунок як **цілісну одиницю** й керує його секціями.
   `create-app`, `find-app`, `list-apps`, `get-app-info`, `*-app-section`; guidance: `app-modeling`.
2. **Об'єкти: створення та редагування.** Сутності, колонки, властивості — програмно й повторювано.
   `create-entity-schema`, `update-entity-schema`, `find-entity-schema`, `get-`/`set-entity-schema-properties`, `get-entity-schema-column-properties`, `modify-entity-schema-column`.
3. **Сторінки: створення та редагування.** Freedom UI з шаблонів + **безпечне** редагування тіла (replace/append/diff).
   `list-page-templates` → `create-page` → `get-page` → `update-page` → `sync-pages`, `validate-page`.
4. **Хендлери та валідатори.** Клієнтська логіка прямо в тілі схеми сторінки — за канонічними патернами.
   через `update-page`; guidance: `page-schema-handlers`, `page-schema-validators`, `page-schema-creatio-devkit-common`, `page-schema-resources`.
5. **Створення відповідних компонентів.** Правильний Freedom UI компонент з каталогу **під версію** платформи, вставлений через `viewConfigDiff`.
   `get-component-info` (resident), `get-request-info`; guidance: `page-modification-components`, `related-list`, `chart-widget`, `indicator-widget`.
6. **Робота з бізнес‑правилами.** Повний CRUD над декларативними правилами — окремо для **об'єкта** і для **сторінки** (visibility / required / editable / value, фільтрація лукапів).
   `*-entity-business-rules`, `*-page-business-rules`, `apply-static-filter`; guidance: `business-rules`, `business-rule-filters`.

### 🟣 Блок 2 — Основні технологічні ньюанси

> **Головна теза:** зарефлексити CLI у MCP — тривіально. Зробити 120+ інструментів придатними для
> LLM (контекст, надійність, безпека, мультитенант) — інженерія. Кожна проблема має виміряне рішення.

1. **Велика поверхня ≠ придатна поверхня.** lazy‑schema: ~20 resident tools, решта за `clio-run` + `get-tool-contract`; ефект **≈ −97% контексту**.
2. **Guidance — це API.** Server instructions = тонкий покажчик; 50+ версіонованих статей + routing map ведуть агента до правильного шаблону **ПЕРЕД** дією.
3. **Version‑accuracy рятує від упевненої брехні.** Каталог компонентів під версію середовища + soft‑degrade при невизначеності + snapshot guard проти silent data loss.
4. **Мультитенант — це безпекова поверхня, і вона gated.** OAuth 2.1 / credential passthrough, per‑session та per‑tenant ізоляція, SSRF‑захист, refuse‑to‑start запобіжники.
5. **Прогрес — це теж типізований контракт.** `ClioStageEvent` (manifest→stage→run‑completed) з `(runId, sequence)` впорядкуванням, дзеркало в ClioRing + on‑disk receipt, без secret‑полів by design.
6. **Safety вбудована, не прикручена.** Destructive‑гейти + `_meta` audit, єдиний redactor секретів, feature toggles гейтять CLI і MCP разом.
7. **Контракт для LLM тестується як публічний API.** Env‑aware виконання, ~205 unit + ~110 e2e на реальному сервері, consumer‑driven gate з боку ClioRing.

---

## Наскрізні висновки (для фіналу)
1. **Один агент будує застосунок Creatio** (Блок 1): додаток → об'єкти → сторінки → хендлери/валідатори → компоненти → бізнес‑правила.
2. **Велика поверхня ≠ придатна поверхня** — context economy (lazy‑schema) робить її вживаною.
3. **Guidance — це API**, а version‑accuracy рятує від упевненої брехні.
4. **Safety й observability вбудовані**, не прикручені.

*«MCP — розетка. clio — руки і знання. Разом — агент, що реально будує на Creatio.»*
