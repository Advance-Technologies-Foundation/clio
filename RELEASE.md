# Release Process

## Быстрый способ: GitHub Copilot команда

### `/release` 🚀

Используйте команду `/release` в GitHub Copilot для автоматического создания нового релиза:

1. Откройте GitHub Copilot Chat
2. Введите `/release`
3. Copilot автоматически:
   - Найдет последний тег релиза
   - Инкрементирует минорную версию на 1
   - Создаст и запушит новый тег
   - Создаст GitHub release (если доступен GitHub CLI)
   - Подтвердит создание тега и релиза

**Пример:**
- Текущий тег: `8.0.1.42`
- Новый тег: `8.0.1.43`
- Автоматически создается GitHub release

## Альтернативные способы

### Автоматические скрипты

Скрипты автоматически создают тег и GitHub release:

#### PowerShell (Windows/macOS/Linux):
```powershell
# Интерактивный режим (создает тег + GitHub release)
.\create-release.ps1

# Автоматический режим (без подтверждения)
.\create-release.ps1 -Force
```

#### Bash (macOS/Linux):
```bash
# Интерактивный режим (создает тег + GitHub release)
./create-release.sh

# Автоматический режим (без подтверждения)
./create-release.sh --force
```

**Что делают скрипты:**
- Находят последний тег
- Инкрементируют версию (X.Y.Z.W → X.Y.Z.W+1)
- Создают и пушат новый тег
- Создают GitHub release (если установлен GitHub CLI)
- Автоматически запускают NuGet публикацию

## Ручной способ: создание релиза

### 1. Pre-release checklist

Before creating a release tag, verify the following:

**BMAD artifacts**
- [ ] All features in this release have an approved test plan in `spec/test-plans/`
- [ ] All stories for this release are `done` in `spec/sprint-status.yaml`
- [ ] Run `/bmad-status` to confirm no feature is stuck in `in-progress` or `review`

**Test gates**
- [ ] `make test-unit` passes locally
- [ ] CI is green on master (`make check-pr`)
- [ ] MCP E2E tests run manually if any MCP tool was changed (not in CI yet)
- [ ] SonarCloud has no new unresolved issues

**Documentation**
- [ ] Command docs updated (`clio/docs/commands/`, `clio/help/en/`, `clio/Commands.md`)
- [ ] AGENTS.md updated if any new policies were introduced
- [ ] What's new draft ready (see [Release Notes](#6-обязательные-release-notes-whats-new) below)

### 1. Подготовка

Убедитесь, что все изменения готовы и протестированы в master ветке.

### 2. Создание релиза

1. **Создайте тег версии** в формате `X.Y.Z.W`:
   ```bash
   git tag 8.0.1.43
   git push origin 8.0.1.43
   ```

2. **Создайте GitHub Release**:
   
   **Вариант A: GitHub CLI (рекомендуется)**
   ```bash
   gh release create 8.0.1.43 --title "Release 8.0.1.43" --notes "Automated release 8.0.1.43"
   ```
   
   **Вариант B: Веб-интерфейс**
   - Перейдите на страницу [Releases](https://github.com/Advance-Technologies-Foundation/clio/releases)
   - Нажмите "Create a new release"
   - Выберите созданный тег
   - Заполните описание релиза
   - Нажмите "Publish release"

### 3. Автоматическая публикация

После создания релиза автоматически запустится workflow `release-to-nuget`, который:

1. ✅ **Извлечет версию** из тега (поддерживает форматы `8.0.1.43` и `v8.0.1.43`)
2. ✅ **Проверит формат** версии (должен быть `X.Y.Z.W`)
3. ✅ **Checks that the `Build` workflow is green** for the tagged commit (it does not re-run the tests itself — see [Release test gate](#release-test-gate-and-integration-coverage))
4. ✅ **Соберет пакет** clio с версией из тега
5. ✅ **Опубликует в NuGet** автоматически

#### Release test gate and integration coverage

The release workflow (`.github/workflows/reliase-to-nuget.yml`) does not run `dotnet test`. It waits for the
`Build` run (`build.yml`) of the tagged commit and publishes only when that run is green. Only a run started by a
`push` to `master` counts: a `pull_request` run of the same SHA skips every build and test job, so it would look
green with zero executed tests. The selection lives in `.github/scripts/Select-ReleaseBuildRun.ps1` and is covered
by `clio.tests/ReleaseWorkflow/ReleaseBuildRunSelectorTests.cs`, which fails if the selector accepts a non-master
or non-push run.

**Decision (issue #1573): integration coverage relies on pre-merge evidence (option A).** `build.yml` runs the
`Integration` category only on `pull_request` (its unit shards exclude it), so a green master `Build` does not
re-prove integration tests on the tagged commit. The evidence is the pull-request run before merge: the
`Integration Tests` check is required by the master ruleset, and that run tests the PR merged into master, which
is normally the tree that lands. Re-running it at release time would test an identical tree again and add
minutes to every release (each of the three hosted integration shards takes about 3.5 minutes including its
build; one self-hosted runner would run them in sequence).

**Option B (not taken):** add a step before "Pack clio with release version" that runs

```powershell
dotnet test .\clio.tests\clio.tests.csproj -c Release --filter "TestCategory=Integration"
```

and fails on a non-zero exit code. Spell the filter `TestCategory`, never the `Category` alias
([why](docs/knowledge/Tests/nunit-adapter-drops-large-non-category-shard-filters.md)). If option B is adopted,
update this section and the comment above the gate step in the workflow.

### 4. Требования к версии

- **Правильные форматы**: `8.0.1.43`, `v8.0.1.43`
- **Неправильные форматы**: `8.0.1`, `v8.0`, `release-8.0.1.43`

### 5. Локальная сборка с версией

Для локальной сборки с определенной версией:

```bash
dotnet pack .\clio\clio.csproj -c Release --output ./output /p:AssemblyVersion=8.0.1.43 /p:FileVersion=8.0.1.43 /p:Version=8.0.1.43
```

### 6. Обязательные Release Notes (What's new)

> **ВАЖНО:** Каждый релиз **обязан** содержать описание изменений (What's new) в GitHub Release.
> Эти заметки отображаются пользователям при выполнении команды `clio update`.

При разработке через кодинг-агенты (GitHub Copilot, Claude Code и др.) агент **обязан**:

1. **Вести список изменений** в процессе разработки — каждый значимый коммит должен содержать понятное описание
2. **Формировать What's new** при создании релиза — краткий, понятный список изменений на английском языке
3. **Включать What's new в GitHub Release** — через параметр `--notes` в `gh release create`

**Формат What's new:**
```markdown
- Add feature X for better Y
- Fix issue with Z when doing W
- Improve performance of operation Q
```

**Пример создания релиза с What's new:**
```bash
gh release create 8.0.2.65 --title "Release 8.0.2.65" --notes "$(cat <<'EOF'
- Fix player name display in environment list
- Add interactive quiz easter egg
- Improve update command UX with spinner and release notes
EOF
)"
```

**Без What's new** пользователи команды `clio update` не увидят описание изменений, что снижает доверие к обновлениям.

### 7. Примечания

- **cliogate проект** остается без изменений версионирования
- **Только clio пакет** получает версию из тега релиза
- При локальной разработке используется версия по умолчанию `8.0.1.42`
