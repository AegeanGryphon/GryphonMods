# GryphonMods Workflows

---

## 1. Reconcile Local Environment Against GitHub

Use this to verify that the local repo, built DLLs, manifest, and GitHub releases are all in sync before starting new work.

### Steps

**Check git status**
```powershell
cd C:\Users\corym\source\repos\GryphonMods
git fetch origin
git status
git log --oneline -10
```
- No uncommitted changes should be present before starting a new mod or release.
- If there are modified files, determine whether they represent an in-progress fix that needs a proper versioned release before proceeding.

**Cross-check manifest against GitHub releases**
- Open `manifest.json` and note each mod's `version` and `downloadUrl`.
- For each mod, confirm a GitHub release exists at the tag `<ModName>-v<version>` with the DLL asset attached.
- If a mod's manifest version has no matching GitHub release, that's an incomplete release — do not assume it was never released. Check GitHub first.

**Verify built DLLs match manifest versions**
- Each mod's built DLL is at `<ModName>\bin\Release\net6.0\<ModName>.dll`.
- The DLL's product version (from `FileVersionInfo.ProductVersion`) must match the `<Version>` in its `.csproj`.
- If they differ, the last build was done on a different version than what's in source — rebuild before releasing.

**Check for orphaned commits**
- Any commit message referencing a mod version (e.g. "Add BinderSpawn v1.0.0 to manifest") is a signal that a release was done. Verify the GitHub release actually exists before assuming "not yet released."

---

## 2. New Mod Idea

### Research Phase
1. Identify the game behavior to modify and the classes involved.
2. Search `F:\Il2CppDumper\Il2CppDumper-net6-win-v6.7.46\dump.cs` for relevant class names, method signatures, and field offsets.
3. Check `DummyDll\Assembly-CSharp.dll` if you need a decompiler view.
4. Confirm the patch target: prefer synchronous entry points over coroutines; prefer Prefix+Postfix swap pattern for collection fields (never modify in-place).

### Project Setup
1. Create `GryphonMods\<ModName>\` folder.
2. Copy `.csproj` from the mod template in `lumentale_modding.md`. Set `<AssemblyName>`, `<RootNamespace>`, and `<Version>1.0.0</Version>`.
3. Create `MyPluginInfo.cs`:
   - `PLUGIN_GUID = "com.corym.lumentale.<modname>"`
   - `PLUGIN_NAME = "<ModName>"`
   - `PLUGIN_VERSION = "1.0.0"`
4. Create `Plugin.cs` from the skeleton in `lumentale_modding.md`. Add Harmony patch classes.
5. Build to verify it compiles:
   ```powershell
   & "C:\Program Files\dotnet\dotnet.exe" build "GryphonMods\<ModName>\<ModName>.csproj" -c Release
   ```
6. Copy built DLL to `C:\Program Files (x86)\Steam\steamapps\common\LumenTale Memories of Trey\BepInEx\plugins\` for in-game testing.

### Add to Manifest (pre-release placeholder)
Add a new entry to `manifest.json` with the correct metadata. Set `downloadUrl` to the eventual release URL pattern even before the release exists — it will be live once the release is created.

### Commit Source
```powershell
git add <ModName>/
git commit -m "<ModName>: initial implementation"
```

---

## 3. Update Mod and Commit to GitHub

Every fix or feature addition is a full new release. Never update an existing release's DLL in place.

### Steps

**1. Bump the version — in both files**

`<ModName>/<ModName>.csproj`:
```xml
<Version>1.0.1</Version>
```

`<ModName>/MyPluginInfo.cs`:
```csharp
public const string PLUGIN_VERSION = "1.0.1";
```

**2. Build**
```powershell
& "C:\Program Files\dotnet\dotnet.exe" build "GryphonMods\<ModName>\<ModName>.csproj" -c Release
```
Confirm `Build succeeded. 0 Warning(s). 0 Error(s).`

**3. Commit**
```powershell
git add <ModName>/<ModName>.csproj <ModName>/MyPluginInfo.cs <ModName>/Plugin.cs
git commit -m "<ModName> v1.0.1 — <short description of fix>"
```

**4. Create GitHub release and upload DLL**
```powershell
git push
gh release create <ModName>-v1.0.1 `
  "<ModName>/bin/Release/net6.0/<ModName>.dll#<ModName>.dll" `
  --title "<ModName> v1.0.1" `
  --notes "<Release notes describing the fix>"
```

**5. Update manifest**

In `manifest.json`, update the mod's entry:
```json
"version": "1.0.1",
"downloadUrl": "https://github.com/AegeanGryphon/GryphonMods/releases/download/<ModName>-v1.0.1/<ModName>.dll",
"changelog": "**v1.0.1** — <same description as release notes>"
```

**6. Commit and push manifest**
```powershell
git add manifest.json
git commit -m "Bump <ModName> to v1.0.1 in manifest"
git push
```

### Release tag pattern
- Mods: `<ModName>-v<version>` (e.g. `BinderSpawn-v1.0.1`)
- Launcher: `Launcher-v<version>` (e.g. `Launcher-v1.0.3`)
