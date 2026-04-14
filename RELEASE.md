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
3. ✅ **Запустит тесты** clio
4. ✅ **Выполнит анализ кода** через SonarQube
5. ✅ **Соберет пакет** clio с версией из тега
6. ✅ **Опубликует в NuGet** автоматически

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
