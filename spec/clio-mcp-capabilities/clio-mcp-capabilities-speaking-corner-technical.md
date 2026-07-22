# Speaking Corner: «Clio MCP: capabilities» — технічна версія

Тема й тези для спікінг корнету. Формат — доповідь для **технічної аудиторії** (інженери),
тривалість ~15–20 хв + Q&A. Технічні терміни лишаю англійською, як звично для розробників.

---

## TL;DR (одним реченням)

> Обгорнути CLI в MCP-інструменти — просто. Зробити так, щоб LLM **реально** користувався
> 120+ інструментами — надійно, з мінімальним контекстом, безпечно й у мультитенанті — оце
> інженерна задача. Clio MCP — про це.

---

## Варіанти назви

1. **Clio MCP: як віддати LLM 120+ інструментів і не вбити йому контекст**
2. **Не просто обгортка над CLI: інженерія MCP-сервера для AI-агентів**
3. **Clio MCP: design for LLMs — context economy, guidance, safety, observability**

---

## Абстракт

clio — це CLI **і** MCP-сервер для Creatio. Найпростіший шлях зробити MCP — рефлексією
перетворити кожен `[Verb]` на `[McpServerTool]`. Він і ламається найпершим: 120+ інструментів
у `tools/list` з'їдають контекст агента, він гірше добирає інструмент і частіше помиляється.

У доповіді пройдемося по інженерних рішеннях, якими clio робить велику поверхню **придатною
для LLM**: lazy-schema (resident core + long-tail через `clio-run`), guidance-as-code,
version-accurate component registry, мультитенантний HTTP edge з OAuth і credential passthrough,
типізований стрімінг прогресу та наскрізна safety-модель. Наскрізна теза: це кейс **LLM-facing
API design**, а не «ми виставили CLI».

---

## Тези (план)

### 0. Хук: наївна версія і чому вона ламається (~2 хв)
- clio = CLI + MCP-сервер. Naive path: reflect every verb → tool. Дешево.
- Проблема: **120+ tools у `tools/list`** роздувають системний контекст кожного запиту →
  гірший tool-selection, більше галюцинацій імен, довші відповіді.
- Далі — 6 рішень, кожне закриває конкретну проблему LLM-facing поверхні.

### 1. Lazy-schema: resident core vs long-tail (~4 хв) ⭐
**Проблема:** контекст-бюджет. **Рішення:** показувати мало, решту — на вимогу.
- **~20 resident tools** плоско в `tools/list` — hot paths: discovery/read
  (`list-apps`, `get-page`, `find-entity-schema`, `get-component-info`, `get-guidance`,
  `get-tool-contract`).
- **~100+ long-tail** доступні через один generic executor **`clio-run`** (`command` = ім'я
  інструмента, `args` = JSON), а знаходяться через **`get-tool-contract`** (compact index із
  прапорцем `resident`).
- Виміряний ефект: **≈ −97% контексту** (цитата з коду `McpCoreToolProfile` / ADR).
- Деталі, які оцінить інженер:
  - **Wrapped-call tolerance** — агенти звично double-wrap-ають аргументи (`{"args":{"command":...}}`);
    executor відновлює обидві форми виклику, а не хардфейлить.
  - **"Did you mean"** — на невідоме ім'я віддається Levenshtein-шортліст реальних імен → агент
    самокоригується без зайвого round-trip.
  - **`McpToolCompatibilityCatalog`** — при ренеймі старе ім'я резолвиться в канонічне
    (MCP-аналог hidden-alias політики CLI): guidance, написаний на старе ім'я, не ламається.
  - **Self-dispatch guard** — `clio-run → clio-run` заборонено (інакше рекурсія/DoS).
  - Membership визначено **per-type** (щоб не розходитись зі скануванням `WithTools(types)` SDK).

### 2. Guidance-as-code: вчити, а не лише виставляти кнопки (~3–4 хв) ⭐
**Проблема:** raw tools → plausible-but-wrong вихід; LLM не знає конвенцій Creatio.
- **Server instructions — це тонкий *покажчик*, а не мануал:** «спершу прочитай `core-rules`
  (інваріанти) + `routing` (карта задач)».
- **Понад 50 guidance-статей** у `GuidanceCatalog`, віддаються і як MCP-resources, і через
  `get-guidance name=...`.
- **Two-level routing map**: домен задачі (pages / entities / data / applications) → *конкретна*
  стаття(-і), яку треба прочитати ПЕРЕД дією.
- Guidance **версіонований, тестований (drift-тести), feature-gated** — це продуктова поверхня,
  а не README. Правило: не дублювати контент — деталі живуть один раз у своєму гайді.

### 3. Version-accurate component registry (~3 хв) ⭐
**Проблема:** LLM вигадує властивості компонентів, яких нема на цій версії платформи.
- Каталог Freedom UI-компонентів віддається з **academy CDN, пер-версія**.
- **Stale-while-revalidate** кеш (5-хв TTL): AI **ніколи не блокується** на мережі — свіже
  повертається одразу, стале віддається миттєво + фоновий refresh.
- Версія резолвиться пер-запит через cliogate `GetSysInfo` → SemVer; при невдачі — **soft-degrade
  на `latest`** із маркером `resolvedFrom`, і агента інструктують *перепитати*, а не діяти наосліп.
- **Snapshot guard проти silent data loss**: кожен POCO має `[JsonExtensionData]`, а тест
  порівнює з pinned live-fixture CDN → якщо продюсер додав поле, тест падає (нема тихого дропу).
- **Forward-compatible**: значення зберігаються як `JsonElement`, тож зміна схеми на продюсері
  не потребує узгодженого релізу clio. Плюс окремі flavors: web / mobile / requests.

### 4. Транспорти: stdio vs мультитенантний HTTP edge (~3 хв)
**Проблема:** від «мій локальний Claude» до «спільний сервіс для команди/хмари».
- **stdio** — локальний dev (свій агент/IDE).
- **HTTP (Streamable)** — той самий DI-граф, усі інструменти, але:
  - **OAuth 2.1 bearer JWT** (`auth-authority` / `audience` / `required-scopes` / `issuer`) **або**
    per-request **credential passthrough** (base64-JSON header `X-Integration-Credentials`).
  - **Per-session DI containers** з TTL + LRU-евікшеном (`session-idle-ttl`, `max-sessions`),
    **per-tenant execution locks** (серіалізація по тенанту).
  - **SSRF-захист**: allowlist origin-ів + always-on baseline-блоки (loopback / link-local /
    cloud-metadata).
  - **Refuse-to-start guards**: публічний bind без auth; auth без audience/scope
    (confused-deputy) — стартанути можна лише через явний escape-hatch.

### 5. Long-running ops: типізований стрімінг прогресу (~2 хв)
**Проблема:** деплой на 10 хв = «чорна скринька» для агента й UI.
- Deploy/uninstall емітять **versioned `ClioStageEvent`**: `manifest` → `stage` (running/done/
  failed/skipped) → `run-completed`.
- **`(runId, sequence)`** монотонні: консюмер де-дупить і дропає out-of-order.
- Контракт **дзеркалиться (не спільним бінарником)** у ClioRing UI + on-disk receipt;
  **byte-identical fixture** асертиться з обох боків.
- **Жодного secret-bearing поля by design**; редакція — відповідальність емітера.

### 6. Safety, вплетена наскрізно (~2 хв)
- **Destructive-флаг → host confirmation.** Сам `clio-run` позначений `Destructive`; флаг
  внутрішнього інструмента ехоситься в `_meta` для audit-trail (host може auto-allow, але слід лишається).
- **Credential redaction на кожному failure-path** — і throw-шлях, і structured `success:false`
  content; один-єдиний `SensitiveErrorTextRedactor`. Хости/URI/паролі/шляхи не течуть у транскрипт.
- **Feature toggles** гейтять CLI **і** MCP-поверхню разом (off by default).

### 7. Як тримаємо це чесним: інженерна дисципліна (~2 хв)
- **BaseTool env-aware execution**: `InternalExecute<TCommand>` резолвить *свіжий* command під
  середовище поточного запиту — ніколи не реюзає stale startup-інстанс.
- **MCP maintenance policy**: будь-яка зміна команди зобов'язує ревʼю MCP-поверхні (tools/prompts/
  resources/tests) — так само як докам.
- **Тести**: ~**205 unit** + ~**110 e2e**; e2e ганяє реальний `clio mcp-server`, не лише маппінг.
- **ClioRing як консюмер контракту**: consumer-driven compatibility gate + NativeAOT publish;
  template drift-guards для shipped agent-інструкцій.

---

## Головні висновки (для фіналу)

1. **Велика поверхня ≠ придатна поверхня.** Context economy (lazy-schema) — передумова, а не оптимізація.
2. **Guidance — це API.** LLM, який знає конвенції платформи, будує правильно; сирі інструменти — ні.
3. **Version-accuracy + soft-degrade + snapshot guard** — інакше агент упевнено бреше на неактуальних даних.
4. **Safety й observability вбудовані**, а не прикручені: redaction, destructive-гейти, типізований прогрес.

Наскрізь: Clio MCP — практичний кейс **LLM-facing API design**.

---

## Орієнтовний тайминг (~15–20 хв)

| Блок | Хв |
|---|--:|
| 0. Хук: наївна версія ламається | 2 |
| 1. Lazy-schema ⭐ | 4 |
| 2. Guidance-as-code ⭐ | 3–4 |
| 3. Component registry ⭐ | 3 |
| 4. Транспорти / HTTP edge | 3 |
| 5. Стрімінг прогресу | 2 |
| 6. Safety | 2 |
| 7. Дисципліна | 2 |
| **Разом** | **~18** + Q&A |

⭐ = core. Якщо часу мало — блоки 4–5 у backup-слайди, лишити 0–3 + 6.

---

## Можливі питання від залу (підготуватися)

- **Чому lazy-schema, а не просто менше інструментів?**
  → Long-tail потрібен (повнота), але його не можна тримати в `tools/list`. `clio-run` +
  `get-tool-contract` дають повноту без контекст-податку.
- **Як агент дізнається про long-tail інструмент, якщо його нема в `tools/list`?**
  → `get-tool-contract` (compact index) + guidance-статті посилаються на імена; на промах —
  "did you mean".
- **`clio-run` руйнує per-tool destructive-гейтинг хоста?**
  → Ні: `clio-run` сам `Destructive`, а резолвлена destructiveness внутрішнього інструмента
  ехоситься в `_meta` для audit. Це свідомий ADR-trade-off (описаний у коді).
- **Stale-while-revalidate — чи не отримає агент застарілу схему?**
  → Для payload — так, свідомо (латентність > свіжості); для docs — синхронна ревалідація з
  бюджетом, бо застарілий гайд шкодить більше. Різні політики по тірах.
- **Multi-tenant: де ізоляція?**
  → Per-session DI containers + per-tenant execution locks + credential passthrough/OAuth;
  жодних крос-тенант share-нутих інстансів команд.
- **Секрети в логах/помилках?**
  → Єдиний redactor на всіх failure-шляхах (throw + structured); стрім-контракт не має
  secret-bearing полів by design.

---

## Нотатки для спікера

- Технічна аудиторія любить **проблема → рішення → деталь, яка кусає**. Кожен блок так і побудований.
- Найсильніші три блоки (⭐): lazy-schema, guidance-as-code, component registry — саме вони
  відрізняють «обгортку CLI» від «агент реально будує». Якщо різати — ріж 4–5, не їх.
- Одна жива демка > слайди: `clio-run` невідомого інструмента з "did you mean", або deploy зі
  стрімінгом стадій. Показати `get-tool-contract` / `get-guidance` вживу теж ефектно.
- Числа тримати чесними: «≈ −97% контексту» — цитата з коду; «~20 resident / 100+ long-tail /
  50+ guidance / 205 unit + 110 e2e тестів» — округлені фактичні.
