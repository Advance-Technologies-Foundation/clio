# Task: Implementation of link-core-src command for Creatio core development

## Context
**Role:** Creatio platform developer  
**Goal:** Develop new features in the core system  
**Prerequisites:**
- Creatio core repository has been cloned
- Application has been deployed locally using `deploy-creatio` command

---

## Required Command

```bash
clio link-core-src -e {EnvName} -c {CoreDirPath}
# or
clio link-core-src -e {EnvName} --core-path {CoreDirPath}
```

**Purpose:** Link core source code to deployed application for local development

---

## Functional Requirements

### 1. Connection String Synchronization (connectionstring.config)
**What to do:**
- Find `connectionstring.config` file from deployed application (case-insensitive search)
- Copy the content of the found file
- Transfer the content to `connectionstring.config` file in core source directory

**Note:** This is needed for database connection from core code

---

### 2. Port Configuration (appsettings.config)
**What to do:**
- Read application port from clio environment config (`EnvName`)
- Set this port in `appsettings.config` file in core directory

**Note:** Ensures port compatibility between deployed application and core code

---

### 3. Enable LAX Mode (app.config)
**What to do:**
- Find `app.config` file in core source code
- Enable LAX mode in this file (setup procedure already exists in `deploy` command, need to reuse)

**Note:** LAX mode is required for core development

---

### 4. Create Symbolic Link (Symlink)
**Structure:**
- Net8 application: `Terrasoft.WebHost` folder in core repository (several levels deep)
- Target path: path to application folder from clio config

**What to do:**
- Create symlink to `Terrasoft.WebHost` folder from core
- Symlink should be located in target application folder (specified in clio config)

**Behavior:**
- When running `creatio start -e {EnvName}` application loads code through symlink → from core repository
- Any changes in core code are immediately visible in running application

---

## Expected Result

After executing the command, developer can:

1. **Preparation:**
   ```bash
   clio link-core-src -e {EnvName} --core-path {CoreDirPath}
   ```

2. **Start Application:**
   ```bash
   creatio start -e {EnvName}
   ```

3. **Development:**
   - Edit code in core repository (`{CoreDirPath}`)
   - Running application uses these changes through symlink
   - No rebuild or application restart needed after each change

---

## Important Implementation Details

- **File Search:** Case-insensitive (Windows compatibility)
- **Code Reuse:** Use existing LAX setup procedure from `deploy` command
- **Platforms:** Support cross-platform compatibility for symlink operations
- **Validation:** Check for existence of all required files before executing operations
