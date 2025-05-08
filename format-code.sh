#!/bin/bash
# format-code.sh
# Скрипт для автоматического форматирования кода согласно стилям Microsoft

# Цвета для вывода
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GREEN='\033[0;32m'
NC='\033[0m' # No Color

# Проверка и установка dotnet-format
if ! command -v dotnet-format &> /dev/null; then
    echo -e "${YELLOW}Установка инструмента dotnet-format...${NC}"
    dotnet tool install -g dotnet-format
fi

# Полный путь к решению
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
SOLUTION_PATH="$SCRIPT_DIR/clio.sln"

# Форматирование кода с применением стилей из .editorconfig
echo -e "${CYAN}Форматирование кода решения согласно .editorconfig и ruleset...${NC}"
dotnet format "$SOLUTION_PATH" --fix-style info --fix-analyzers info --verbosity diagnostic

echo -e "${GREEN}Форматирование завершено!${NC}"