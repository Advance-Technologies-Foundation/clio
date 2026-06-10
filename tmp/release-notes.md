## 🚀 What's New in 8.1.0.55

Hey there, fellow developers! This release packs some sweet improvements to make your Creatio development experience smoother and more productive.

### ✨ Highlights

#### 🖼️ ImageLookup Column Support (ENG-90273)
You can now use **ImageLookup** ("Image link") column type with `crt.ImageInput` in your entity schemas. No more workarounds for image references — it just works!

#### 🔍 Merged All-Packages View for Entity Properties
Calling `get-entity-schema-properties` without specifying a package now returns a **merged view across all packages**. Perfect for understanding the full picture of any entity without hunting through package hierarchies.

#### 📱 Skip Mobile Pages in create-app-section (ENG-91154)
New option to **skip mobile page generation** when creating app sections. If your app is web-only, stop generating artifacts you'll never use.

#### 🛡️ Component Type Validation (ENG-90939)
The agent can no longer invent non-existent `crt.*` component types. We added pre-insert validation to keep your page configs honest and prevent those "why is this not rendering?" head-scratchers.

#### 💡 Better Error Messages for create-app-section
Replaced the opaque "InsertQuery failed." message with **actionable error details**. Now when something goes wrong, you'll actually know *what* went wrong.

### 🧠 MCP Intelligence Upgrades

- **Static filter datePart support** — filter entities by hour/minute with proper ESQ generation
- **Related-list guidance** — new MCP guidance for filtering lists by page data
- **Validation discipline checklist** — expanded guidance resource for entity creation workflows
- **Time-of-day filters** — refined handling of HourMinute date parts with UTC/local consistency

### 📦 Install / Update

```bash
dotnet tool update clio -g
```

Happy building! 🎉

