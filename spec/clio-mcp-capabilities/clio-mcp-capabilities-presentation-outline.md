# Презентація: «Clio MCP: capabilities»

**Майстер-аутлайн доповіді (скрипт спікера).**
Консолідує оглядову та технічну версії тез під конкретний формат.

| Параметр | Значення |
|---|---|
| Аудиторія | технічна (інженери) |
| Тривалість | ~60 хв контенту + Q&A |
| Структура | 2 блоки, 50/50 |
| Блок 1 | Основні функціональні можливості (широта) + **жива демка** |
| Блок 2 | Основні технологічні ньюанси (глибина) |
| Наскрізна метафора | MCP = «розетка»; clio = «руки і знання» для AI |

> Супутні файли (референс): `clio-mcp-capabilities-speaking-corner.md` (оглядова версія),
> `clio-mcp-capabilities-speaking-corner-technical.md` (технічна версія). Цей файл — робочий скрипт.

---

## Варіанти назви
1. **Clio MCP: як віддати LLM ключі від Creatio — і не пошкодувати**
2. Від CLI до AI-агента: можливості та інженерія Clio MCP
3. Clio MCP: capabilities — широта можливостей і design for LLMs

## Абстракт
clio — це CLI **і** MCP-сервер для Creatio. У першій половині подивимось, **що** AI-агент реально
вміє робити на Creatio через clio — від підняття середовища до готової Freedom UI сторінки (з живою
демкою). У другій — **як** це зроблено інженерно, щоб велика поверхня (120+ інструментів) була
придатною для LLM: lazy-schema, guidance-as-code, version-accurate component registry, мультитенантний
HTTP edge, типізований стрімінг прогресу та safety-модель. Наскрізна теза: це кейс **LLM-facing API
design**, а не «ми виставили CLI».

---

## Основні тези (кістяк аргументу)

Самі твердження, які доповідь стверджує (структуру/таймінг див. нижче). Блок 1 — 8 тез
можливостей; Блок 2 — 7 тез-рішень. Кожна теза Блоку 2 захищає широту Блоку 1.

### 🔵 Блок 1 — Функціональні можливості
> **Головна теза:** clio перетворює AI-агента на full-cycle розробника Creatio — він не виконує
> окремі команди, а **компонує** їх: від пустої машини до готового застосунку.

1. **Один clio — два обличчя.** Той самий інструмент — і для людини в терміналі, і для AI через MCP.
2. **Агент сам готує майданчик.** Provision & deploy автоматизовані (інфраструктура → Creatio + Identity → інспекція).
3. **Агент задає структуру, а не файли.** Workspace, ui-project, пакети та залежності — одиниці рішення.
4. **Дані — програмно й повторювано.** Сутності + читання/запис через ESQ та OData.
5. **Freedom UI — серце можливостей.** Сторінки, компоненти, бізнес-правила, requests, дашборди. Найбільше «знань» clio.
6. **Логіка і бренд — теж на агенті.** Бізнес-процеси (BPMN) і кастомні теми.
7. **До продакшена, не до демо.** DataForge, права доступу, OAuth/Identity, інтеграційні тести.
8. **Сила — у композиції.** Один запит прошиває кілька фаз; 120+ інструментів працюють як один конвеєр.

### 🟣 Блок 2 — Технологічні ньюанси
> **Головна теза:** зарефлексити CLI у MCP — тривіально. Зробити 120+ інструментів придатними для
> LLM (контекст, надійність, безпека, мультитенант) — інженерія. Кожна проблема має виміряне рішення.

1. **Велика поверхня ≠ придатна поверхня.** lazy-schema: ~20 resident, решта за `clio-run`; ≈ −97% контексту.
2. **Guidance — це API.** Тонкий покажчик + 50+ версіонованих статей + routing ведуть агента ПЕРЕД дією.
3. **Version-accuracy рятує від упевненої брехні.** Компоненти під версію середовища + soft-degrade + snapshot guard.
4. **Мультитенант — це безпекова поверхня, і вона gated.** OAuth/passthrough, per-session/tenant ізоляція, refuse-to-start.
5. **Прогрес — це теж типізований контракт.** `ClioStageEvent` з `(runId, sequence)`, дзеркало в ClioRing, no secrets.
6. **Safety вбудована, не прикручена.** Destructive-гейти + `_meta` audit, єдиний redactor, feature toggles (CLI+MCP).
7. **Контракт для LLM тестується як публічний API.** Env-aware, ~205 unit + ~110 e2e, consumer-driven ClioRing gate.

---

## Загальний таймінг (~60 хв)

| Блок | Хв |
|---|--:|
| Вступ / хук | 3 |
| **Блок 1 — Функціональні можливості** (вкл. демо ~10) | ~27 |
| Місток між блоками | 2 |
| **Блок 2 — Технологічні ньюанси** | ~26 |
| Фінал / висновки | 2 |
| **Разом** | **~60** + Q&A |

---

## Вступ / хук (~3 хв)
- clio починався як CLI, яким користується **людина** в терміналі (deploy, пакети, схеми).
- Через MCP той самий clio стає інструментом, яким керує **AI**.
- MCP у двох словах: відкритий стандарт-«розетка», у яку AI-агент вмикається до зовнішніх інструментів.
- Обіцянка доповіді: спершу **що** воно вміє (з живою демкою), потім **як** зроблено, щоб це працювало.
- 🔑 «Дати LLM 120+ інструментів — легко. Зробити, щоб він ними реально користувався — оце робота.»

---

# 🔵 Блок 1 — Основні функціональні можливості (~27 хв)

**Рамка:** «Один агент — весь цикл розробки на Creatio.» Головна ідея — агент не робить один трюк,
він **компонує** інструменти вздовж усього lifecycle. Проходимо 6 фаз, далі — жива демка, що прошиває їх.

### Фаза 1 — Provision & Deploy (~2 хв)
- **Меседж:** від пустої машини до живого Creatio — без ручного клацання.
- Інфраструктурні перевірки → підняття: `assert-infrastructure` → `find-empty-iis-port` →
  `deploy-creatio` / `deploy-identity`.
- Інспекція середовища одним звітом: `describe-environment` (coreVersion, db engine, framework,
  product, license, locale).
- Discovery білдів, реєстрація середовища, встановлення cliogate.

### Фаза 2 — Workspace & Packages (~2 хв)
- **Меседж:** агент задає структуру рішення, а не купу окремих файлів.
- `create-workspace`, `new-ui-project` (Angular remote-module).
- Пакети: `push-pkg`, add/remove package dependency, lock/unlock.
- 🔑 типова гоча (місток у Блок 2): «GetSchemaDesignItem returned an HTML error page» = брак
  залежності пакета → guidance веде до `add-package-dependency`.

### Фаза 3 — Data model (~2 хв)
- **Меседж:** сутності й дані — програмно й повторювано.
- `create-entity-schema`, `find-entity-schema`, entity/column properties.
- Запити: **ESQ** (EntitySchemaQuery) і **OData CRUD** — читання/запис даних середовища.

### Фаза 4 — Freedom UI (~4 хв, найбагатший домен)
- **Меседж:** тут агент будує **власне застосунок**, і саме тут найбільше «знань» вкладено в clio.
- Сторінки: `create-page` (з шаблонів) → page modification — вставка полів, контейнерів, компонентів,
  кнопок із хендлерами (`viewConfigDiff`).
- **Business rules**: декларативні visibility / required / editable / value.
- **Requests**: прив'язка дій до платформних `crt.*Request` (print, close, run-process, ...).
- **Related pages / related lists**: майстер-деталь, фільтрація за даними сторінки.
- **Dashboards & widgets**: chart / indicator, розкладка на 12-колонковій сітці, dashboard rights.

### Фаза 5 — Automate & Brand (~2 хв)
- **Меседж:** логіка процесів і фірмовий вигляд.
- **Business processes** (BPMN) — проєктування процесу (feature-gated).
- **Themes** — create / restyle / delete / list / set default; доставка теми в середовище.

### Фаза 6 — Seed, Secure & Test (~2 хв)
- **Меседж:** доводимо застосунок до продакшн-готовності.
- **DataForge** — генерація тестових даних; data bindings / lookup seeding.
- **Security**: identity assertion (Identity Service V3), server-to-server OAuth, record rights.
- **Testing**: integration test projects (ATF.Repository, Allure), process-сценарії.
- (+ Operate: `compile-creatio`, restart, `clear-redis`, support-mode діагностика.)

### 🎬 ЖИВА ДЕМКА (~10 хв) — «Agent builds a page»
**Мета:** показати композицію фаз 1→4 живцем і **непомітно посіяти** ньюанси Блоку 2 (агент читає
guidance, тягне component-info під версію, диспатчить через clio-run).

**Передумови (чек-лист перед виступом):**
- [ ] MCP-клієнт (Claude Desktop / Claude Code / IDE) підключений до `clio mcp-server` (stdio).
- [ ] Готове dev-середовище зареєстроване (env-name); `get-info`/`describe-environment` проходить.
- [ ] Потрібні інструменти в клієнті **allow-listed**, щоб не ловити confirmation-промпти в ефірі.
- [ ] Термінал/шрифт великі; лог викликів інструментів видно на екрані.
- [ ] **Fallback:** записаний скрінкаст + скріншоти кожного кроку (демо в ефірі падають — це нормально).

**Сценарій (говорити, що відбувається, поки агент працює):**
1. «Покажи мої застосунки Creatio» → `list-apps` (resident tool працює одразу).
2. «Створи сутність `UsrInvoice` з полями Number, Amount, DueDate» → `create-entity-schema`.
3. «Зроби Freedom UI сторінку-список для неї» → `create-page`.
   → *звернути увагу залу:* агент СПЕРШУ прочитав guidance (`page-creation`) і `get-component-info`.
4. «Додай графік суми за місяцями» → chart-widget guidance + `get-component-info` під версію платформи.
5. Відкрити результат у Creatio UI.

**Плант для Блоку 2 (проговорити вголос):** «Помітили: перед тим як щось будувати, агент читав
*guidance* і тягнув *актуальні* компоненти. Чому це критично — у другій половині.»

**Якщо часу обмаль:** урізати до кроків 1–3 (сутність + сторінка). Крок 4 — опційний «вау».

### Місток-числа (кінець Блоку 1)
- **Понад 120 інструментів, 50+ guidance-статей**, повний lifecycle.
- Питання-місток: *«Широта — це добре. Але як воно не розвалюється, коли за кермом LLM?»*

---

## 🔗 Місток між блоками (~2 хв)
- Блок 1 = **широта**. Широта — легка частина: зарефлексити CLI у tools нескладно.
- Проблема починається, коли за кермом **LLM** і користувачів багато.
- Блок 2 — 7 неочевидних ньюансів, кожен формату **гоча → рішення → деталь, що кусає**.

---

# 🟣 Блок 2 — Основні технологічні ньюанси (~26 хв)

### N1 — «Не можна просто виставити LLM 120 tools» → lazy-schema ⭐ (~5 хв)
- **Гоча:** 120+ tools у `tools/list` роздувають контекст кожного запиту → гірший tool-selection,
  галюцинації імен, довші відповіді.
- **Рішення:** ~20 **resident** tools плоско (hot paths: discovery/read); ~100+ **long-tail** через
  один generic executor **`clio-run`** (`command`+`args`); discovery через **`get-tool-contract`**
  (compact index із прапорцем `resident`).
- **Деталь, що кусає:** ≈ **−97% контексту** (з коду/ADR); wrapped-call tolerance (агенти
  double-wrap-ають args); «did you mean» (Levenshtein на промах імені); `McpToolCompatibilityCatalog`
  (реймапінг старих імен = MCP-аналог hidden CLI-aliases); self-dispatch guard (нема `clio-run→clio-run`).
- 🔑 «Велика поверхня ≠ придатна поверхня. Context economy — передумова, не оптимізація.»

### N2 — «Інструменти без знань → впевнено неправильно» → guidance-as-code ⭐ (~5 хв)
- **Гоча:** сирі tools + LLM, який не знає конвенцій Creatio, = plausible-but-wrong вихід.
- **Рішення:** server instructions — **тонкий покажчик**, не мануал («спершу `core-rules` +
  `routing`»); **50+ guidance-статей** у `GuidanceCatalog`, віддаються як MCP-resources і через
  `get-guidance`; **two-level routing map**: домен задачі → *конкретна* стаття ПЕРЕД дією.
- **Деталь:** guidance версіонований, тестований (drift-тести), feature-gated; контент живе один раз
  (без дублювання між instructions / routing / гайдом).
- 🔑 «Guidance — це API. Ми віддаємо агенту не лише кнопки, а й *як ними користуватись*.»

### N3 — «Схеми компонентів дрейфують по версіях» → version-accurate registry ⭐ (~5 хв)
- **Гоча:** LLM вигадує властивості компонентів, яких нема на цій версії платформи.
- **Рішення:** каталог Freedom UI-компонентів з **academy CDN, пер-версія**; **stale-while-revalidate**
  кеш (AI не блокується на мережі); версія — пер-запит через cliogate `GetSysInfo`→SemVer; при
  невдачі **soft-degrade на `latest`** + маркер `resolvedFrom` → агента інструктують *перепитати*.
- **Деталь:** **snapshot guard** проти silent data loss (`[JsonExtensionData]` + pinned live-fixture:
  продюсер додав поле = тест падає); значення як `JsonElement` → forward-compatible без релізу clio;
  flavors web/mobile/requests.
- 🔑 «Без version-accuracy + soft-degrade агент упевнено бреше на неактуальних даних.»

### N4 — «Кілька користувачів = мультитенант» → HTTP edge (~4 хв)
- **Гоча:** від «мій локальний Claude» до «спільний сервіс команди/хмари» — ізоляція й авторизація.
- **Рішення:** **stdio** (локально) vs **HTTP (Streamable)** — той самий DI-граф, усі tools, але:
  **OAuth 2.1 bearer JWT** (authority/audience/scopes/issuer) *або* per-request **credential
  passthrough** (header `X-Integration-Credentials`); **per-session DI containers** (TTL+LRU);
  **per-tenant execution locks**.
- **Деталь:** **SSRF-захист** (allowlist + baseline-блоки: loopback/link-local/cloud-metadata);
  **refuse-to-start guards** (публічний bind без auth; auth без audience/scope = confused-deputy).
- 🔑 «Мультитенант — це не фіча, а безпекова поверхня; вона gated на старті.»

### N5 — «Довга операція = чорна скринька» → typed streaming (~3 хв)
- **Гоча:** deploy на 10 хв — жодного зворотного зв'язку для агента й UI.
- **Рішення:** deploy/uninstall емітять **versioned `ClioStageEvent`**: `manifest` → `stage`
  (running/done/failed/skipped) → `run-completed`; **`(runId, sequence)`** монотонні (де-дуп +
  drop out-of-order).
- **Деталь:** контракт **дзеркалиться** (не спільним бінарником) у ClioRing UI + on-disk receipt;
  **byte-identical fixture** асертиться з обох боків; **жодного secret-поля by design**.
- 🔑 «Прогрес — це теж контракт: типізований, впорядкований, без секретів.»

### N6 — «Один рядок логу зливає пароль» → safety наскрізь (~3 хв)
- **Гоча:** інструменти оперують хостами, токенами, паролями — вони не мають текти в транскрипт.
- **Рішення:** **destructive-флаг → host confirmation** (сам `clio-run` destructive; флаг внутрішнього
  інструмента ехоситься в `_meta` для audit); **credential redaction** на throw- і structured-failure
  шляхах (єдиний `SensitiveErrorTextRedactor`); **feature toggles** гейтять CLI+MCP разом.
- 🔑 «Safety вбудована, а не прикручена: за замовчуванням — безпечно.»

### N7 — «Як тримаємо це чесним» → інженерна дисципліна (~2 хв)
- **BaseTool env-aware execution:** `InternalExecute<TCommand>` резолвить *свіжий* command під env
  поточного запиту — ніколи не реюзає stale startup-інстанс.
- **MCP maintenance policy:** будь-яка зміна команди зобов'язує ревʼю MCP-поверхні (tools/prompts/
  resources/tests) — нарівні з доками.
- **Тести:** ~**205 unit** + ~**110 e2e** (e2e ганяє реальний `clio mcp-server`, не лише маппінг).
- **ClioRing** як консюмер контракту: consumer-driven compatibility gate + NativeAOT publish.
- 🔑 «Контракт для LLM тестується як публічний API — бо він ним і є.»

---

## Фінал / висновки (~2 хв)
1. **Один агент — весь lifecycle Creatio** (Блок 1): provision → model → build → automate → secure/test.
2. **Велика поверхня ≠ придатна поверхня** — context economy (lazy-schema) робить її вживаною.
3. **Guidance — це API**, а version-accuracy рятує від упевненої брехні.
4. **Safety й observability вбудовані**, не прикручені.
- Заключна фраза: «MCP — розетка. clio — руки і знання. Разом — агент, що реально будує на Creatio.»

---

## Q&A: підготуватися
- Чим відрізняється від виклику CLI командами? → структурований опис + guidance + safety-позначки;
  агент сам добирає й **компонує** кроки.
- Безпечно давати AI доступ до прод? → destructive-гейти, redaction, мультитенант-ізоляція, OAuth.
- Прив'язано лише до Claude? → ні, MCP відкритий: будь-який MCP-клієнт (IDE, Copilot, ClioRing).
- Як агент знаходить long-tail інструмент? → `get-tool-contract` + guidance-посилання + «did you mean».
- `clio-run` не ламає destructive-гейтинг? → сам destructive + `_meta` audit echo (свідомий ADR-trade-off).
- Stale-while-revalidate не віддасть застарілу схему? → для payload свідомо (латентність > свіжості),
  для docs — синхронна ревалідація з бюджетом. Різні політики по тірах.

## Нотатки для спікера
- Наскрізна метафора «розетка + руки і знання» — тримати від хука до фіналу.
- Демо в Блоці 1 навмисно сіє ньюанси Блоку 2 (guidance, component-info, clio-run) — у Блоці 2
  посилатися назад: «пам'ятаєте, у демці агент читав guidance? ось чому».
- Числа тримати чесними: «≈ −97% контексту» — цитата з коду; «~20 resident / 100+ long-tail /
  50+ guidance / ~205 unit + ~110 e2e» — округлені фактичні.
- Технічні деталі транспортів/контракту прогресу — мати запасні слайди на випадок глибоких питань.

## Орієнтовна розкладка слайдів (~30–36)
- Титул (1), хук+MCP у двох словах (2)
- Блок 1: рамка (1), 6 фаз (6), демо-титул+сценарій (2), числа-місток (1) — ~13
- Місток (1)
- Блок 2: рамка (1), N1–N7 (по 1–2 = ~11), висновки-блоку (1) — ~13
- Фінал (1), Q&A/дякую (1)
