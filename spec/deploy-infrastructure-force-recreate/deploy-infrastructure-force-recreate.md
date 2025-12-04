# Deploy Infrastructure Force Recreate

## 📌 Problem Description

The `clio deploy-infrastructure` command works incorrectly if a namespace for clio services with pods, services, and PVCs from a previous deployment already exists on the machine. This leads to conflicts and errors when attempting to redeploy infrastructure.

## 🎯 Required Features

### 1. Enhancement of `clio deploy-infrastructure` Command

#### Functionality:
- **Namespace existence check**: Before deployment, check if a namespace for clio exists
- **Interactive prompt**: When an existing namespace is detected, ask the user if they want to recreate it
- **Deletion upon user consent**:
  - Delete the entire namespace
  - Delete all contents (pods, services, deployments, etc.)
  - Delete PVCs (Persistent Volume Claims)
  - Delete the namespace itself
  - Then create a new namespace and deploy infrastructure

#### New parameter: `--force`
- **Purpose**: Automatically recreate the namespace without interactive prompt
- **Behavior**: If namespace exists, delete it and its contents, then redeploy
- **Syntax**: `clio deploy-infrastructure --force`

### 2. New Command: `clio delete-infrastructure`

#### Functionality:
- **Namespace existence check**: Check if a namespace for clio exists
- **Deletion**:
  - Delete the namespace and all its contents (pods, services, deployments, PVCs, etc.)
  - Output confirmation of successful deletion
- **Syntax**: `clio delete-infrastructure`

## 🔧 Technical Requirements

1. **Namespace name source**: Use the namespace name from configuration files that are already used in the `deploy-infrastructure` command
2. **K8S services**: Use existing Kubernetes services already implemented in the clio project
3. **Architecture**: Follow the existing command pattern (Command pattern) in the project
4. **Error handling**: Add proper error handling when working with K8S API
5. **Logging**: Use the project's existing logging system

## 📊 Execution Algorithm

### For `deploy-infrastructure`:
```
1. Load configuration and get namespace name
2. Check if namespace exists
3. If it exists:
   a. If --force flag is set → proceed to step 4
   b. Otherwise → ask user (interactively)
   c. If user declines → exit
4. Delete namespace and contents (if necessary)
5. Create new namespace
6. Deploy infrastructure
```

### For `delete-infrastructure`:
```
1. Load configuration and get namespace name
2. Check if namespace exists
3. If it exists:
   a. Ask user for confirmation (unless --force flag is set)
   b. Delete namespace and all its contents
   c. Output confirmation
4. If not found → output message that namespace was not found
```

## 📚 Reference Information

- **Configuration files**: Located in K8S templates (review existing `deploy-infrastructure` implementation)
- **K8S services**: Located in `clio/Common/` or `clio/Command/` folders
- **Existing commands**: Study implementation of other commands to maintain style and patterns
