Bpmonline Command Line Interface (bpmcli)
=============================

Проект предназначен для интеграции систем разработки и CI/CD
c платформой bpmonline версии 7.13.0 и выше

Возможности bpmcli:
* Перезапуск приложения
* Формирование пакета из файловой системы
* Установка пакета в приложение
* Загрузка/выгрузка контента пакета в приложение при работе в РФС
* Выполнение кода сборки в приложении
* Установка пакета из архива
* Сжатие проекта в пакет

INSTALLATION
---------------------
### Setup


COMMANDS
---------------------

### Перезапуск приложения

Перезапуск приложения с выгрузкой домена

```powershell
bpmcli restart
```
### Настройка окружения

Создание нового окружения: имя , путь к сайту, логин и пароль для подключения
```powershell
bpmcli cfg -e dev -u http://mysite.bpmonline.com -l user -p pa$$word
```
Установка окружения по умолчанию
```powershell
bpmcli cfg -a dev
```
Измененение параметра существущего окружения
```powershell
bpmcli cfg -e dev -p pa$$word
```

Удаление окружения
```powershell
bpmcli remove -e dev
```

Использование неосновного окружения при вызове команд

```powershell
bpmcli restart -e dev
```

### Использование в CI\CD

В системах CI\CD можно не использовать файл конфигурации и передавать параметры
напрямую при каждом вызове команды

```powershell
bpmcli restart -u http://mysite.bpmonline.com -l administrator -p pa$$word
```

### Сжатие проекта в архив пакета
```powershell
bpmcli compress -s C:\bpmonline\src\mypackage -d C:\bpmonline\pkg\mypackage.gz
```

### Установка пакета из архива

```powershell
bpmcli install -f C:\bpmonline\pkg\mypackage.gz
```

### Загрузка сборки

```powershell
bpmcli download -p PackageName
```

### Выгрузка сборки

```powershell
bpmcli upload -p PackageName
```