# Анализ команды create-infrastructure (create-k8-files)

**Дата анализа:** 24 ноября 2025  
**Статус:** Проведено детальное исследование возможностей и документации

---

## 📋 Обзор команды

### Общая информация

| Параметр | Значение |
|----------|----------|
| **Название команды** | `create-k8-files` |
| **Алиас** | `ck8f` |
| **Класс** | `CreateInfrastructureCommand` |
| **Описание** | "Prepare K8 files for deployment" |
| **Платформы** | Windows, macOS, Linux |
| **Зависимости** | Kubernetes (kubectl), Rancher Desktop или аналог |

### Сопутствующая команда

| Параметр | Значение |
|----------|----------|
| **Название команды** | `open-k8-files` |
| **Алиасы** | `cfg-k8f`, `cfg-k8s` |
| **Класс** | `OpenInfrastructureCommand` |
| **Описание** | "Open folder K8 files for deployment" |
| **Поддержка** | **⚠️ Только Windows** |

---

## 🎯 Функциональность команды create-k8-files

### Что делает команда

```csharp
public override int Execute(CreateInfrastructureOptions options) {
    // 1. Определяет путь для сохранения файлов
    string to = Path.Join(SettingsRepository.AppSettingsFolderPath, "infrastructure");
    
    // 2. Определяет источник файлов из шаблонов
    string location = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    string from = Path.Join(location, "tpl","k8", "infrastructure");
    
    // 3. Копирует все файлы инфраструктуры
    _fileSystem.CopyDirectory(from, to, true);
    
    // 4. Выводит информацию пользователю
    Console.WriteLine("All files have been copied to: {to}");
}
```

### Шаг за шагом

1. **📂 Копирование файлов**
   - Копирует из: `{ApplicationExecutableDirectory}/tpl/k8/infrastructure`
   - Копирует в: `{AppDataPath}/clio/infrastructure`
   - **Windows:** `C:\Users\YOUR_USER\AppData\Local\creatio\clio\infrastructure`
   - **macOS:** `~/.creatio/clio/infrastructure` (или аналогично)
   - **Linux:** `~/.creatio/clio/infrastructure` (или аналогично)

2. **ℹ️ Вывод информации о доступных сервисах**
   - Postgres SQL Server (latest, port 5432)
   - Microsoft SQL Server 2022 (latest developer edition, port 1434)
   - Redis Server (latest, port 6379)
   - Email Listener (1.0.10, port 1090)

3. **⚠️ Важные замечания**
   - ❌ Команда **НЕ развертывает** инфраструктуру автоматически
   - 📌 Требует ручного запуска `kubectl apply -f infrastructure`
   - 🔍 Требует ручной проверки и редактирования параметров

---

## 📦 Копируемые файлы инфраструктуры

### Структура директории

```
infrastructure/
├─ clio-namespace.yaml              # Создание namespace
├─ clio-storage-class.yaml          # Настройка storage class
├─ redis/
│  ├─ redis-services.yaml           # LoadBalancer + ClusterIP сервисы
│  ├─ redis-workload.yaml           # Deployment конфигурация
│  └─ redis-volumes.yaml            # PVC для хранилища
├─ postgres/
│  ├─ postgres-secrets.yaml         # Учетные данные (root/root)
│  ├─ postgres-services.yaml        # LoadBalancer + ClusterIP сервисы
│  ├─ postgres-stateful-set.yaml    # StatefulSet конфигурация
│  └─ postgres-volumes.yaml         # PVC для данных и backup
├─ pgadmin/
│  ├─ pgadmin-secrets.yaml          # Учетные данные
│  ├─ pgadmin-services.yaml         # LoadBalancer сервис
│  ├─ pgadmin-workload.yaml         # Deployment конфигурация
│  └─ pgadmin-volumes.yaml          # PVC для конфигурации
├─ mssql/
│  ├─ mssql-secrets.yaml            # Учетные данные (sa/пароль)
│  ├─ mssql-services.yaml           # LoadBalancer + ClusterIP сервисы
│  ├─ mssql-stateful-set.yaml       # StatefulSet конфигурация
│  ├─ mssql-volumes.yaml            # PVC для данных (20GB)
│  └─ mssq-secrets.yaml             # (дублирует mssql-secrets.yaml?)
├─ email-listener/
│  ├─ email-listener-services.yaml  # Services конфигурация
│  └─ email-listener-workload.yaml  # Deployment конфигурация
└─ sonarqube/                       # Опционально для анализа кода
   ├─ sonarqube.yaml
   └─ sonarqube copy.yaml
```

### Типы Kubernetes ресурсов

| Ресурс | Использование | Назначение |
|--------|---------------|-----------|
| **Namespace** | `clio-namespace.yaml` | Изоляция ресурсов инфраструктуры |
| **StorageClass** | `clio-storage-class.yaml` | Определение класса хранилища |
| **PersistentVolumeClaim** | `*-volumes.yaml` | Запрос хранилища для БД и конфигов |
| **Secret** | `*-secrets.yaml` | Хранение паролей и учетных данных |
| **Service** | `*-services.yaml` | Expose сервисов наружу и внутри кластера |
| **StatefulSet** | `postgres`, `mssql` | Stateful приложения (БД) |
| **Deployment** | `redis`, `pgadmin`, email-listener | Stateless приложения |

---

## 🔧 Инструкции по развертыванию

### Требуемые компоненты

1. **Kubernetes кластер**
   - Rancher Desktop (рекомендуется для Windows)
   - Docker Desktop with Kubernetes
   - minikube (для Linux)
   - любой другой локальный K8s кластер

2. **kubectl**
   - Должен быть установлен и настроен
   - Должен иметь доступ к кластеру

3. **Достаточно ресурсов**
   - RAM: минимум 8 GB (рекомендуется 16 GB)
   - Disk space: ~80 GB (20 GB для MSSQL + 40 GB для PostgreSQL)
   - CPU: минимум 4 cores (рекомендуется 8)

### Шаги развертывания (из документации)

**Шаг 1: Запустить команду создания файлов**
```bash
clio create-k8-files
```

**Шаг 2: Проверить и отредактировать файлы**
```bash
# Открыть папку с файлами
clio open-k8-files
```

**Вещи для проверки:**
- ✅ `mssql-stateful-set.yaml` - секция `resources` (зависит от железа)
- ✅ `mssql-stateful-set.yaml` - принимаете ли вы Terms & Conditions Microsoft SQL Server Developer Edition
- ✅ `mssql-stateful-set.yaml` - достаточно ли 20 GB disk space
- ✅ `postgres-stateful-set.yaml` - секция `resources`
- ✅ `postgres-stateful-set.yaml` - достаточно ли 40 GB для данных + 5 GB для backup

**Шаг 3: Развернуть Kubernetes ресурсы**

```bash
# Перейти в директорию с файлами
cd C:\Users\YOUR_USER\AppData\Local\creatio\clio\infrastructure  # Windows
# или
cd ~/.creatio/clio/infrastructure  # macOS/Linux

# Применить все ресурсы по очередности
kubectl apply -f clio-namespace.yaml
kubectl apply -f clio-storage-class.yaml

# Redis
kubectl apply -f redis

# MSSQL
kubectl apply -f mssql/mssql-volumes.yaml
kubectl apply -f mssql

# PostgreSQL
kubectl apply -f postgres/postgres-volumes.yaml
kubectl apply -f postgres
kubectl apply -f pgadmin
```

Или развернуть все сразу (не рекомендуется):
```bash
kubectl apply -f infrastructure -R
```

**Шаг 4: Проверить развертывание**

```bash
# Посмотреть все pods в namespace
kubectl get pods -n clio-infrastructure

# Посмотреть services
kubectl get svc -n clio-infrastructure

# Посмотреть volumes
kubectl get pv
kubectl get pvc -n clio-infrastructure

# Проверить статус pod
kubectl describe pod {pod-name} -n clio-infrastructure

# Логи
kubectl logs {pod-name} -n clio-infrastructure
```

**Шаг 5: Запустить Creatio**

После развертывания инфраструктуры:
```bash
clio deploy-creatio --ZipFile <path-to-creatio.zip>
```

---

## 🔐 Учетные данные по умолчанию

### PostgreSQL
- **Хост:** localhost:5432 (LoadBalancer) или postgres-service-internal:5432 (ClusterIP)
- **Имя пользователя:** root
- **Пароль:** root
- **Namespace:** clio-infrastructure

### pgAdmin
- **Хост:** localhost:1080
- **Имя пользователя:** root@creatio.com
- **Пароль:** root

### MSSQL Server
- **Хост:** localhost:1433 (LoadBalancer) или mssql-service-internal:1433 (ClusterIP)
- **Имя пользователя:** sa
- **Пароль:** $Zarelon01$Zarelon01
- **Версия:** 2022 Developer Edition
- **Namespace:** clio-infrastructure

### Redis
- **Хост:** localhost:6379 (LoadBalancer) или redis-service-internal:6379 (ClusterIP)
- **Порт:** 6379
- **Namespace:** clio-infrastructure

---

## ⚙️ Конфигурация подключения в Creatio

### Файл appsettings.json

После развертывания инфраструктуры в Creatio нужно настроить подключение:

```json
{
  "dbConnectionStringKeys": {
    "k8-postgres": {
      "uri": "postgres://root:root@127.0.0.1:5432",
      "workingFolder": "\\\\wsl.localhost\\rancher-desktop\\mnt\\clio-infrastructure\\postgres\\data"
    },
    "k8-mssql": {
      "uri": "mssql://sa:$Zarelon01$Zarelon01@127.0.0.1:1433",
      "workingFolder": "\\\\wsl.localhost\\rancher-desktop\\mnt\\clio-infrastructure\\mssql\\data"
    }
  }
}
```

### Регистрация окружения в clio

```json
{
  "Environments": {
    "my-creatio-dev": {
      "DbServerKey": "k8-postgres",
      "DbName": "creatio_db",
      "BackupFilePath": "/path/to/backup.bak"
    }
  }
}
```

---

## 🔴 Критические пробелы и проблемы

### 1. Команда НЕ развертывает инфраструктуру автоматически

**Проблема:**
```
Clio will not deploy infrastructure automatically
```

**Требуется:**
- Ручной запуск `kubectl apply -f ...`
- Пользователь должен знать Kubernetes команды
- Ошибка при запуске kubectl остается незамеченной

**Решение для create-dev-env:**
```csharp
private int DeployInfrastructure(string infrastructureFolder)
{
    var deploymentOrder = new[] {
        "clio-namespace.yaml",
        "clio-storage-class.yaml",
        "redis",
        "postgres/postgres-volumes.yaml",
        "postgres",
        "pgadmin"
    };
    
    foreach (var resource in deploymentOrder) {
        var resourcePath = Path.Combine(infrastructureFolder, resource);
        var process = Process.Start("kubectl", $"apply -f {resourcePath}");
        if (process.ExitCode != 0)
            throw new Exception($"Failed to deploy {resource}");
    }
    
    // Подождать пока все pods готовы
    WaitForPodsReady("clio-infrastructure", new[] { "postgres", "redis", "pgadmin" });
}
```

---

### 2. Команда open-k8-files работает ТОЛЬКО на Windows

**Проблема:**
```csharp
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
    Process.Start("explorer.exe", infrsatructureCfgFilesFolder);
    return 0;
} else {
    Console.WriteLine("Clio open-k8-files command is only supported on: 'windows'.");
    return 1;  // ❌ ОШИБКА
}
```

**Решение:**
Поддержать macOS и Linux:
```csharp
public override int Execute(OpenInfrastructureOptions options) {
    string infraFolder = Path.Join(
        SettingsRepository.AppSettingsFolderPath, 
        "infrastructure"
    );
    
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
        Process.Start("explorer.exe", infraFolder);
    } 
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
        Process.Start("open", new string[] { infraFolder });
    } 
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
        Process.Start("xdg-open", new string[] { infraFolder });
    }
    
    return 0;
}
```

---

### 3. Нет параметров конфигурации для команды

**Проблема:**
```csharp
public class CreateInfrastructureOptions
{
    // ❌ ПУСТО - нет параметров!
}
```

**Требуется для create-dev-env:**

```csharp
public class CreateInfrastructureOptions
{
    [Option("namespace", Default = "clio-infrastructure",
        HelpText = "Kubernetes namespace for infrastructure")]
    public string Namespace { get; set; }
    
    [Option("storage-class", Default = "default",
        HelpText = "Storage class for persistent volumes")]
    public string StorageClass { get; set; }
    
    [Option("auto-deploy", Default = false,
        HelpText = "Automatically deploy infrastructure (requires kubectl)")]
    public bool AutoDeploy { get; set; }
    
    [Option("skip-redis", Default = false,
        HelpText = "Skip Redis deployment")]
    public bool SkipRedis { get; set; }
    
    [Option("skip-postgres", Default = false,
        HelpText = "Skip PostgreSQL deployment")]
    public bool SkipPostgres { get; set; }
    
    [Option("skip-pgadmin", Default = false,
        HelpText = "Skip pgAdmin deployment")]
    public bool SkipPgAdmin { get; set; }
    
    [Option("postgres-storage", Default = "40Gi",
        HelpText = "Storage size for PostgreSQL")]
    public string PostgresStorageSize { get; set; }
    
    [Option("mssql-storage", Default = "20Gi",
        HelpText = "Storage size for MSSQL")]
    public string MssqlStorageSize { get; set; }
    
    [Option("wait-ready", Default = true,
        HelpText = "Wait for all pods to be ready")]
    public bool WaitForReady { get; set; }
}
```

---

### 4. Нет проверки зависимостей

**Проблема:**
- Не проверяет, установлен ли kubectl
- Не проверяет, доступен ли Kubernetes кластер
- Не проверяет, достаточно ли ресурсов

**Решение:**
```csharp
private void ValidatePrerequisites()
{
    // Проверить kubectl
    if (!IsKubectlInstalled())
        throw new Exception("kubectl not found. Install kubectl and add to PATH");
    
    // Проверить кластер
    if (!IsKubernetesAccessible())
        throw new Exception("Cannot access Kubernetes cluster");
    
    // Проверить ресурсы
    var nodeResources = GetNodeResources();
    if (nodeResources.MemoryGB < 8)
        throw new Exception("Insufficient memory. Minimum 8GB required");
    
    if (nodeResources.DiskGB < 80)
        throw new Exception("Insufficient disk space. Minimum 80GB required");
}
```

---

### 5. Нет валидации YAML файлов перед развертыванием

**Проблема:**
- Нет проверки синтаксиса YAML перед применением
- Нет валидации Kubernetes manifests
- Ошибки Kubernetes остаются незамеченными

**Решение:**
```bash
# Перед apply-ом
kubectl apply -f {file} --dry-run=client --validate=true
```

---

### 6. Жесткие значения по умолчанию

**Проблема:**
- Пароли захардкодены (sa/$Zarelon01$Zarelon01)
- Порты захардкодены (5432, 1433, 6379, 1090)
- Размеры storage захардкодены (20GB MSSQL, 40GB PostgreSQL)

**Решение:**
- Сделать параметризованными (как я показал выше)
- Использовать переменные окружения
- Спрашивать интерактивно при развертывании

---

### 7. Нет инструкций по откату

**Проблема:**
- Нет команды для удаления инфраструктуры
- Нет документации по cleanup

**Решение:**
```bash
# Новая команда
clio delete-infrastructure [--namespace clio-infrastructure]

# Или
kubectl delete namespace clio-infrastructure
```

---

## 📊 Сравнение требований create-dev-env с возможностями create-k8-files

| Требование | Статус | Пробелы |
|-----------|--------|---------|
| Создание YAML файлов инфраструктуры | ✅ Есть | - |
| Автоматическое развертывание инфраструктуры | ❌ Нет | Требуется `kubectl apply` вручную |
| Параметризация инфраструктуры | ❌ Нет | Нет параметров команды |
| Проверка зависимостей | ❌ Нет | Нет валидации kubectl/K8s |
| Проверка ресурсов | ❌ Нет | Нет проверки RAM/Disk |
| Интерактивное взаимодействие | ❌ Нет | Просто копирует файлы |
| Поддержка macOS/Linux | ⚠️ Частично | open-k8-files только на Windows |
| Логирование и отладка | ❌ Нет | Только сообщение в консоль |
| Здоровье check | ❌ Нет | Нет проверки готовности pods |

---

## 🎯 Рекомендации для create-dev-env

### Архитектура

```
create-dev-env
├─ 1. Создать YAML файлы
│  └─ Вызвать create-k8-files
│
├─ 2. Параметризовать файлы
│  ├─ Заменить namespace (если указано)
│  ├─ Заменить storage sizes
│  └─ Заменить пароли (если нужно)
│
├─ 3. Развернуть инфраструктуру
│  ├─ Валидировать kubectl
│  ├─ Валидировать K8s cluster
│  ├─ Запустить kubectl apply с проверкой ошибок
│  └─ Дождаться готовности всех pods
│
├─ 4. Конфигурировать подключение
│  └─ Обновить appsettings.json с координатами сервисов
│
└─ 5. Проверить доступность сервисов
   ├─ PostgreSQL health check
   ├─ Redis health check
   └─ MSSQL health check (если используется)
```

### Требуемые изменения в CreateInfrastructureCommand

1. ✅ Добавить параметры в `CreateInfrastructureOptions`
2. ✅ Реализовать параметризацию YAML файлов (шаблоны)
3. ✅ Реализовать метод `DeployInfrastructure()` с kubectl
4. ✅ Реализовать валидацию зависимостей
5. ✅ Реализовать health checks для сервисов
6. ✅ Реализовать правильную обработку ошибок
7. ✅ Поддержать macOS/Linux в open-k8-files
8. ✅ Добавить более подробное логирование

### Приоритет

| Изменение | Приоритет | Трудоемкость |
|-----------|-----------|-------------|
| Параметризация YAML | 🔴 Высокий | 🟡 Средняя (4-6ч) |
| Автоматическое развертывание | 🔴 Высокий | 🟡 Средняя (6-8ч) |
| Валидация зависимостей | 🔴 Высокий | 🟢 Малая (2-3ч) |
| Health checks | 🟡 Средний | 🟡 Средняя (4-6ч) |
| Поддержка macOS/Linux в open-k8-files | 🟡 Средний | 🟢 Малая (1-2ч) |
| YAML templates вместо копирования | 🟡 Средний | 🟡 Средняя (6-8ч) |

---

## 📝 Следующие шаги

1. **Для create-dev-env:** Встроить create-k8-files с автоматическим развертыванием
2. **Улучшить create-k8-files:** Добавить параметры и автоматизацию
3. **Документировать:** Обновить Commands.md с примерами для macOS
4. **Тестировать:** Протестировать на macOS с Rancher Desktop
5. **Откат:** Создать команду для удаления инфраструктуры

---

## 📚 Связанные документы

- [`create-dev-env-4-mac.md`](./create-dev-env-4-mac.md) - Требования
- [`analysis-dev-env-vs-deploy-creatio.md`](./analysis-dev-env-vs-deploy-creatio.md) - Анализ deploy-creatio
- [`clio/Commands.md`](../clio/Commands.md) - Документация команд
- [`clio/Command/CreateInfrastructure.cs`](../clio/Command/CreateInfrastructure.cs) - Исходный код
