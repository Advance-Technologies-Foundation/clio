# Original Requirements (Russian)

## Request
я хочу чтобы АИ агент или пользователь для задач когда нужно поработать с env мог получить данные вызвав команду clio которая покажет все свойства которые сохранены в конфиге для этого env а также мог получить доступ

## Desired Command Variants

1. `clio env {EnvName}` - показывает настройки по конкретному env
2. `clio env -e {EnvName}` - показывает настройки по конкретному env (альтернативный синтаксис)
3. `clio env` - показывает все environments (как сейчас)

## Current State
- Есть команда `clio envs` (aliases: `show-web-app-list`, `show-web-app`)
- Хотим дополнить функционал новой командой `clio env`

---

## Translation & Analysis

### What is Needed
An AI agent or user working with environments needs to retrieve data by calling a clio command that will show all properties saved in the config for a specific environment.

### Current Command
- `clio envs` - existing command with aliases

### Desired Enhancement
Add new command variants:
- `clio env {EnvName}` - show settings for specific env
- `clio env -e {EnvName}` - show settings for specific env (alternative syntax)
- `clio env` - show all environments (like current behavior)