# download-sys-setting-file

Save the exact bytes of a **Binary** system setting into a caller-named file.

```sh
clio download-sys-setting-file -e dev --code LogoImage --file-name ./logo.bin
```

| Argument | Required | Meaning |
|---|---|---|
| `--code` | Yes | Existing Binary system setting code. |
| `--file-name` | Yes | Destination path including filename, in an existing directory. Relative CLI paths resolve from the current directory. |
| `-e`, `--environment` | Standard environment selection | Registered environment; standard URI/login/OAuth options also apply. |

Requires ClioGate (`clio install-gate -e dev`) and permission to read/manage system settings.
Reads the authenticated user's effective value, including the platform's default fallback;
it does not promise the All-Users default when a personal override exists.

The command decodes Base64 and saves bytes without guessing MIME type, changing the extension,
parsing JSON/XML, or converting text encoding or line endings. JSON, XML, text and images
uploaded **as Binary** all work. Ordinary Text settings are rejected even if their strings look
like Base64. A `.png` destination does not convert or validate the data as PNG.

The destination must not exist. A failed download does not publish a partial destination file.
Missing/unreadable settings, non-Binary settings, empty values, invalid Base64 and values exceeding
the existing 10 MB Binary limit fail explicitly. Upload security policy does not restrict downloads.

Success prints metadata, never the payload:

```json
{"file-name":"/absolute/path/logo.bin","byte-count":1234}
```

## MCP through clio-run

The tool is discoverable through `get-tool-contract`; it is not a resident tool.
The required MCP filename must be absolute and names a file on the MCP server host.

```json
{
  "command": "download-sys-setting-file",
  "args": {
    "environment-name": "dev",
    "code": "LogoImage",
    "file-name": "/absolute/path/logo.bin"
  }
}
```

The result contains `exit-code`, `execution-log-messages`, `file-name` and `byte-count`.
On failure, the file metadata is null and diagnostics explain the refusal. Local file creation
makes the tool `ReadOnly=false`; it is `Destructive=false` because it never overwrites.

Use `update-sys-setting` with `value-file-path` to upload; `get-sys-setting` continues to return
an empty scalar value for Binary settings and `list-sys-settings` shows `<binary>`.
