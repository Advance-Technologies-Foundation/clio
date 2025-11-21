# Анализ и улучшение команды deploy-creatio

## Текущее состояние

### Основной файл: `CreatioInstallerService.cs`

Команда `deploy-creatio` (aliases: `dc`, `ic`, `install-creation`) в настоящее время:

**Обязательно использует IIS для развертывания:**
```csharp
int createSiteResult = dbRestoreResult switch {
    0 => CreateIISSite(unzippedDirectory, options).GetAwaiter().GetResult(),
    _ => ExitWithErrorMessage("Database restore failed")
};
```

**Проблемы текущей реализации:**
1. ❌ Жестко привязано к IIS (только для Windows)
2. ❌ На macOS и Linux команда не работает (не может создать IIS сайт)
3. ❌ Нет способа явно запретить использование IIS на Windows
4. ❌ Нет автоопределения ОС
5. ❌ Нет прямого запуска через `dotnet Terrasoft.WebHost.dll` на других платформах

### Архитектура развертывания:

```
Execute()
  ├─ Распаковка ZIP файла
  ├─ Проверка и создание БД (MSSQL или PostgreSQL)
  ├─ Инициализация инфраструктуры (Redis, pgAdmin, PostgreSQL в K8s)
  ├─ Запуск приложения (IIS на Windows или dotnet на macOS/Linux)
  ├─ Обновление ConnectionString
  └─ Регистрация приложения
```

## Требуемые улучшения

### 1. Автоопределение платформы

Система должна автоматически определить ОС и выбрать соответствующий метод развертывания:

```csharp
public enum DeploymentPlatform
{
    Windows,      // IIS
    macOS,        // dotnet run / Terrasoft.WebHost.dll
    Linux         // dotnet run / Terrasoft.WebHost.dll
}

private DeploymentPlatform DetectPlatform()
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return DeploymentPlatform.Windows;
    
    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        return DeploymentPlatform.macOS;
    
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        return DeploymentPlatform.Linux;
    
    throw new PlatformNotSupportedException("Unknown platform");
}
```

### 2. Новые параметры команды

```csharp
[Verb("deploy-creatio", ...)]
public class PfInstallerOptions : EnvironmentNameOptions
{
    // Существующие параметры...
    
    /// <summary>
    /// Способ развертывания (auto, iis, dotnet)
    /// </summary>
    [Option("deployment", Required = false, Default = "auto", 
        HelpText = "Deployment method: auto|iis|dotnet")]
    public string DeploymentMethod { get; set; }
    
    /// <summary>
    /// Явный запрет на использование IIS даже на Windows
    /// </summary>
    [Option("no-iis", Required = false, Default = false,
        HelpText = "Don't use IIS on Windows (use dotnet run instead)")]
    public bool NoIIS { get; set; }
    
    /// <summary>
    /// Путь для развертывания приложения
    /// </summary>
    [Option("app-path", Required = false,
        HelpText = "Application installation path")]
    public string AppPath { get; set; }
    
    /// <summary>
    /// Использовать SSL/HTTPS для приложения
    /// </summary>
    [Option("use-https", Required = false, Default = false,
        HelpText = "Use HTTPS (requires certificate for dotnet)")]
    public bool UseHttps { get; set; }
    
    /// <summary>
    /// Путь к SSL сертификату (.pem или .pfx)
    /// </summary>
    [Option("cert-path", Required = false,
        HelpText = "Path to SSL certificate file (.pem or .pfx)")]
    public string CertificatePath { get; set; }
    
    /// <summary>
    /// Пароль для SSL сертификата (если требуется)
    /// </summary>
    [Option("cert-password", Required = false,
        HelpText = "Password for SSL certificate")]
    public string CertificatePassword { get; set; }
    
    /// <summary>
    /// Автоматически запустить приложение после развертывания
    /// </summary>
    [Option("auto-run", Required = false, Default = true,
        HelpText = "Automatically run application after deployment")]
    public bool AutoRun { get; set; }
}
```

### 3. Интерфейс и реализация стратегий развертывания

```csharp
/// <summary>
/// Интерфейс для стратегии развертывания приложения
/// </summary>
public interface IDeploymentStrategy
{
    /// <summary>
    /// Проверить, применима ли эта стратегия на текущей платформе
    /// </summary>
    bool CanDeploy();
    
    /// <summary>
    /// Развернуть приложение
    /// </summary>
    Task<int> Deploy(DirectoryInfo appDirectory, PfInstallerOptions options);
    
    /// <summary>
    /// Получить URL приложения
    /// </summary>
    string GetApplicationUrl(PfInstallerOptions options);
    
    /// <summary>
    /// Получить описание стратегии
    /// </summary>
    string GetDescription();
}

// Конкретные реализации:
public class IISDeploymentStrategy : IDeploymentStrategy { }
public class DotNetDeploymentStrategy : IDeploymentStrategy { }
```

### 4. Новая архитектура Execute()

```csharp
public override int Execute(PfInstallerOptions options)
{
    // Инициализация
    ValidateOptions(options);
    DirectoryInfo unzippedDirectory = PrepareApplication(options);
    
    // Выбор стратегии развертывания
    IDeploymentStrategy deploymentStrategy = SelectDeploymentStrategy(options);
    
    _logger.WriteInfo($"Selected deployment strategy: {deploymentStrategy.GetDescription()}");
    _logger.WriteInfo($"Platform: {RuntimeInformation.OSDescription}");
    
    // Подготовка БД (одинакова для всех платформ)
    int dbRestoreResult = PrepareDatabse(unzippedDirectory, options);
    if (dbRestoreResult != 0)
        return ExitWithErrorMessage("Database preparation failed");
    
    // Развертывание приложения (зависит от стратегии)
    int deployResult = deploymentStrategy
        .Deploy(unzippedDirectory, options)
        .GetAwaiter()
        .GetResult();
    
    if (deployResult != 0)
        return ExitWithErrorMessage("Application deployment failed");
    
    // Постразвертывающие операции
    string appUrl = deploymentStrategy.GetApplicationUrl(options);
    
    int updateConnectionStringResult = UpdateConnectionString(unzippedDirectory, options)
        .GetAwaiter()
        .GetResult();
    
    if (updateConnectionStringResult != 0)
        return ExitWithErrorMessage("Failed to update ConnectionString");
    
    // Регистрация в clio
    RegisterApplication(options, appUrl);
    
    return 0;
}

private IDeploymentStrategy SelectDeploymentStrategy(PfInstallerOptions options)
{
    // Если явно указано --no-iis, не использовать IIS
    if (options.NoIIS && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        _logger.WriteInfo("IIS explicitly disabled, using dotnet run");
        return _serviceProvider.GetRequiredService<DotNetDeploymentStrategy>();
    }
    
    // Если явно указан метод
    if (!string.IsNullOrEmpty(options.DeploymentMethod) && options.DeploymentMethod != "auto")
    {
        return SelectStrategyByName(options.DeploymentMethod);
    }
    
    // Автоопределение по ОС
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return _serviceProvider.GetRequiredService<IISDeploymentStrategy>();
    
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        return _serviceProvider.GetRequiredService<DotNetDeploymentStrategy>();
    
    throw new PlatformNotSupportedException("Current platform is not supported");
}

private IDeploymentStrategy SelectStrategyByName(string methodName)
{
    return methodName.ToLower() switch
    {
        "iis" => _serviceProvider.GetRequiredService<IISDeploymentStrategy>(),
        "dotnet" => _serviceProvider.GetRequiredService<DotNetDeploymentStrategy>(),
        _ => throw new ArgumentException($"Unknown deployment method: {methodName}")
    };
}
```

## Примеры использования

### Пример 1: Автоматический выбор (рекомендуется)
```bash
# На Windows → использует IIS
clio deploy-creatio --ZipFile creatio.zip

# На macOS → использует dotnet run
clio deploy-creatio --ZipFile creatio.zip

# На Linux → использует dotnet run
clio deploy-creatio --ZipFile creatio.zip
```

### Пример 2: Явно указать Windows без IIS (dotnet)
```bash
clio deploy-creatio --ZipFile creatio.zip --no-iis
# или
clio deploy-creatio --ZipFile creatio.zip --deployment dotnet
```

### Пример 3: Полная спецификация параметров
```bash
clio deploy-creatio \
  --ZipFile creatio.zip \
  --SiteName my-app \
  --SitePort 8080 \
  --deployment dotnet \
  --app-path /opt/creatio \
  --db pg \
  --platform net6
```

### Пример 4: С HTTPS/SSL сертификатом
```bash
clio deploy-creatio \
  --ZipFile creatio.zip \
  --SiteName my-app \
  --SitePort 443 \
  --deployment dotnet \
  --use-https \
  --cert-path /etc/ssl/certs/my-app.pfx \
  --cert-password "certificate-password"
```

### Пример 5: Без автоматического запуска (для отладки)
```bash
clio deploy-creatio \
  --ZipFile creatio.zip \
  --SiteName dev-app \
  --no-iis \
  --auto-run false
```

### Пример 6: IIS развертывание на Windows (явно)
```bash
clio deploy-creatio \
  --ZipFile creatio.zip \
  --SiteName prod-app \
  --SitePort 80 \
  --deployment iis \
  --db mssql
```

## Реализация стратегий развертывания

### 1. IIS Strategy (Windows only)
**Условие**: Windows OS, `--deployment iis` или автоматически на Windows (если не `--no-iis`)

**Действия**:
- Создать IIS AppPool
- Скопировать файлы в `%iis-clio-root-path%\{siteName}`
- Создать IIS веб-сайт
- Настроить привязки HTTP/HTTPS
- Запустить приложение через IIS
- Регистрация в clio

### 2. DotNet Strategy (macOS, Linux, Windows)
**Условие**: `--deployment dotnet` или автоматически на macOS/Linux

**Действия**:
- Скопировать файлы приложения в `{AppPath}` (default: `~/creatio/{SiteName}`)
- Создать конфигурацию `appsettings.json` с параметрами:
  - Port: `{SitePort}`
  - ConnectionString: из конфига БД
  - HTTPS (опционально): сертификат и ключ
- Создать systemd service (Linux) или launchd (macOS) для автозапуска:
  ```
  [Unit]
  Description=Creatio Application {SiteName}
  After=network.target
  
  [Service]
  Type=simple
  User=creatio
  WorkingDirectory=/opt/creatio/{SiteName}
  ExecStart=/usr/bin/dotnet Terrasoft.WebHost.dll
  Restart=on-failure
  RestartSec=10
  Environment="ASPNETCORE_URLS=http://0.0.0.0:{SitePort}"
  Environment="ASPNETCORE_ENVIRONMENT=Production"
  
  [Install]
  WantedBy=multi-user.target
  ```
- Запустить сервис: `systemctl start creatio-{SiteName}` (Linux)
- Убедиться что приложение доступно на `http://localhost:{SitePort}`
- Регистрация в clio

**Особенности**:
- На macOS: использует `launchctl` для управления сервисом
- На Linux: использует `systemd` для управления сервисом
- На Windows (с --no-iis): запускает процесс через консоль или background task
- Автоматический перезапуск при падении

## Инфраструктура (одинакова для всех платформ)

Используется Kubernetes в локальном кластере (via Docker Desktop на macOS/Windows или minikube на Linux):

```yaml
Namespace: creatio
Services:
  ├─ PostgreSQL (StatefulSet + Service)
  ├─ Redis (Deployment + Service)
  └─ pgAdmin (Deployment + Service)
```

**Команды инфраструктуры** (выполняются перед развертыванием приложения):
```bash
kubectl apply -f clio-namespace.yaml
kubectl apply -f postgres/
kubectl apply -f redis/
kubectl apply -f pgadmin/
```

**Переменные окружения** для подключения:
```
DATABASE_HOST=postgres.creatio.svc.cluster.local
DATABASE_PORT=5432
REDIS_HOST=redis.creatio.svc.cluster.local
REDIS_PORT=6379
```

## Таблица совместимости платформ и методов развертывания

| Стратегия | Windows | macOS | Linux | Требования |
|-----------|---------|-------|-------|-----------|
| IIS       | ✅      | ❌    | ❌    | Windows + IIS installed |
| DotNet    | ✅*     | ✅    | ✅    | .NET 6+ SDK |

*На Windows с флагом `--no-iis`

## Поток выполнения команды deploy-creatio

```
1. Валидация параметров
   ├─ Проверка ZIP файла
   ├─ Проверка портов (1-65535)
   └─ Проверка путей
   
2. Определение платформы и стратегии
   ├─ Определить OS (Windows/macOS/Linux)
   ├─ Проверить флаги (--no-iis, --deployment)
   └─ Выбрать стратегию развертывания
   
3. Подготовка приложения
   ├─ Распаковка ZIP
   ├─ Копирование файлов в целевую директорию
   └─ Инициализация структуры каталогов
   
4. Подготовка инфраструктуры (K8s)
   ├─ Создать namespace
   ├─ Развернуть PostgreSQL
   ├─ Развернуть Redis
   └─ Развернуть pgAdmin
   
5. Подготовка БД
   ├─ Выполнить миграции
   ├─ Инициализировать схемы
   └─ Создать системные данные
   
6. Развертывание приложения (зависит от стратегии)
   ├─ [IIS] Создать AppPool, сайт, привязки
   └─ [DotNet] Создать сервис, скопировать конфиги
   
7. Конфигурация приложения
   ├─ Обновить ConnectionString
   ├─ Установить HTTPS (если нужно)
   └─ Обновить параметры приложения
   
8. Запуск приложения
   ├─ Запустить сервис
   ├─ Проверить доступность (health check)
   └─ Дождаться готовности
   
9. Регистрация в clio
   ├─ Добавить окружение
   ├─ Настроить параметры подключения
   └─ Проверить связь
   
10. Завершение
    ├─ Вывести URL для доступа
    ├─ Открыть браузер (если --auto-run)
    └─ Вывести статус
```

## Файлы для создания/модификации

### Новые файлы:
1. `Common/DeploymentStrategies/IDeploymentStrategy.cs` - интерфейс стратегии
2. `Common/DeploymentStrategies/IISDeploymentStrategy.cs` - развертывание через IIS (Windows)
3. `Common/DeploymentStrategies/DotNetDeploymentStrategy.cs` - развертывание через dotnet (macOS/Linux/Windows)
4. `Common/DeploymentStrategies/DeploymentStrategyFactory.cs` - фабрика для выбора стратегии
5. `Common/SystemServices/ISystemServiceManager.cs` - интерфейс для управления сервисами
6. `Common/SystemServices/LinuxSystemServiceManager.cs` - управление systemd сервисами
7. `Common/SystemServices/MacOSSystemServiceManager.cs` - управление launchd сервисами
8. `Common/SystemServices/WindowsSystemServiceManager.cs` - управление Windows сервисами

### Модификация:
1. `Command/CreatioInstallCommand/InstallerCommand.cs` - добавить новые параметры
2. `Command/CreatioInstallCommand/CreatioInstallerService.cs` - переписать Execute() с использованием стратегий
3. `BindingsModule.cs` - регистрация стратегий и менеджеров сервисов в DI контейнере
4. `Commands.md` - обновить документацию команды deploy-creatio

## Преимущества новой архитектуры

✅ **Кроссплатформенность** - работает на Windows (IIS/dotnet), macOS (dotnet), Linux (dotnet)  
✅ **Гибкость** - можно выбрать способ развертывания явно или использовать автоматический выбор  
✅ **Расширяемость** - паттерн Strategy позволяет легко добавить новые способы развертывания  
✅ **Простота** - единая команда для всех платформ  
✅ **Безопасность** - явное управление SSL/HTTPS сертификатами  
✅ **Автоматизация** - systemd (Linux) и launchd (macOS) интеграция для автозапуска  
✅ **Видимость** - детальное логирование всех операций  
✅ **Надежность** - автоматический перезапуск при падении приложения  

## Параметры командной строки (справочник)

| Параметр | Флаг | Тип | Обязательный | По умолчанию | Описание |
|----------|------|-----|-------------|------------|----------|
| ZIP файл | `--ZipFile` | string | Да | - | Путь к ZIP архиву с приложением |
| Имя сайта | `--SiteName` | string | Нет | - | Имя приложения (спросит, если не указано) |
| Порт | `--SitePort` | int | Нет | 40000-40100 | Порт приложения (спросит, если не указано) |
| Метод развертывания | `--deployment` | string | Нет | auto | auto\|iis\|dotnet |
| Без IIS | `--no-iis` | bool | Нет | false | Не использовать IIS на Windows |
| Путь приложения | `--app-path` | string | Нет | ~/creatio/{SiteName} | Директория установки приложения |
| Использовать HTTPS | `--use-https` | bool | Нет | false | Использовать HTTPS вместо HTTP |
| Путь сертификата | `--cert-path` | string | Нет | - | Путь к SSL сертификату (.pem или .pfx) |
| Пароль сертификата | `--cert-password` | string | Нет | - | Пароль для SSL сертификата |
| Автоматический запуск | `--auto-run` | bool | Нет | true | Запустить приложение после развертывания |
| Тип БД | `--db` | string | Нет | pg | pg\|mssql |
| Платформа | `--platform` | string | Нет | - | net6\|netframework |
| Тихий режим | `--silent` | bool | Нет | false | Не показывать интерактивные подсказки |
| Продукт | `--product` | string | Нет | - | Краткое имя продукта (s\|semse\|bcj) |

## Фазы реализации

### Фаза 1: Базовая поддержка (MVP)
- [ ] Интерфейс `IDeploymentStrategy`
- [ ] `IISDeploymentStrategy` (рефакторинг текущего кода)
- [ ] `DotNetDeploymentStrategy` (базовая версия)
- [ ] Новые параметры в `PfInstallerOptions`
- [ ] Логика выбора стратегии в `Execute()`

### Фаза 2: Улучшение и документация
- [ ] Обновление `Commands.md`
- [ ] Unit тесты для стратегий
- [ ] Integration тесты для кроссплатформенности
- [ ] Примеры использования для разных сценариев
