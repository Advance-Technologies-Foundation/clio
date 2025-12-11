# show-local-envs

Displays local Creatio environments that have `environmentPath` configured and reports their state using a colored table. Columns: Name, Status, Url, Path, Reason.

## Usage

```bash
clio show-local-envs
```

## Status rules
- **OK**: ping succeeded and login succeeded.
- **Error Auth data**: ping succeeded but login failed.
- **Deleted**: environment directory is missing, contains only the `Logs` directory, or access is denied.
- **Not runned**: ping failed, but the environment directory exists and contains items beyond `Logs`.

## Data sources and checks
- Environments are retrieved via the settings abstraction (`ISettingsRepository`); the config file is not read directly.
- Ping/login checks reuse the existing application client logic.
- Filesystem checks rely on the filesystem abstraction to validate `environmentPath`.

## Output example

```text
+------+--------------------+----------------------+---------------------+---------------------------+
| Name | Status             | Url                  | Path                | Reason                    |
+------+--------------------+----------------------+---------------------+---------------------------+
| dev  | [OK]               | http://localhost     | /env/app            | healthy                   |
| qa   | [Error Auth data]  | http://qa.local      | /qa/app             | login failed              |
| old  | [Deleted]          | http://old.local     | /deleted/app        | directory not found       |
| svc  | [Not runned]       | http://svc.local     | /svc/app            | ping failed (timeout)     |
+------+--------------------+----------------------+---------------------+---------------------------+
```

## Notes
- If no environments have `environmentPath` set, the command prints an informative message and exits with `0`.
- Reasons are single-line summaries (newline characters are stripped).
