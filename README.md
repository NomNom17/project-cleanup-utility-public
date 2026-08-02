# Project Cleanup Utility

## NOTE
**Please note; the detection algorithm is not 100% perfect. Please use this tool as a guide to removing unused assets in your project and to pursue further checks of your own to ensure the file you may be permanently deleting, is not used anywhere! Please see Known Limitations for more information.**

## Overview
A Unity Editor tool for finding, quarantining, and safely removing unused assets from a Unity project, to help speed up Editor loading time and performance, as well as reducing the overall project's file size.

Instead of deleting assets outright, the tool follows a **Detect → Quarantine → Review → Restore → Permanently Delete** workflow: unused assets are first moved into a quarantine folder inside your project, where you can review them and restore anything that turns out to still be needed before committing to a permanent delete.

## Installation (.unitypackage)

1. Click on **Releases** on the left hand side, and download `ProjectCleanupUtility.unitypackage`.
2. Click and drag the Unity Package into your project, by having your project opened in Unity Editor, and drop it into the **Project Browser**.
3. Import all files, which should look like
   ```
   Assets/Editor/ProjectCleanupUtility/
   ├── ProjectCleanupUtility.asmdef
   ├── LICENSE
   ├── NOTICE
   ├── README.md
   ├── Core/
   │   ├── AssetScanner.cs
   │   ├── DependencyGraphBuilder.cs
   │   ├── ExportService.cs
   │   ├── QuarantineManager.cs
   │   ├── ScanOrchestrator.cs
   │   └── UndoController.cs
   ├── Data/
   │   ├── AssetInfo.cs
   │   ├── ScanResult.cs
   │   └── WhitelistConfig.cs
   ├── UI/
   │   ├── AccessibilityController.cs
   │   ├── ToastService.cs
   │   └── Views/
   │       └── ProjectCleanupWindow.cs
   ├── Utilities/
   │   └── AssetCategoryResolver.cs
   └── Styles/
       └── ProjectCleanupUtility.uss
   ```
_Please be aware the file structure may change during any future development of this tool._

4. Switch back to Unity after importing the Unity Package, and you should see the tool registered under **Tools ▸ Project Cleanup Utility**.

## Key Features
- **Full project scan** — builds a reverse-dependency map using `AssetDatabase.GetDependencies`, and identifies assets with zero incoming references.
- **Code-reference detection** — scans `.cs` files for string-literal asset references (`Resources.Load`, `AssetDatabase.LoadAssetAtPath`, `Addressables.LoadAssetAsync`, raw asset paths, and GUIDs) so assets loaded purely from code aren't misflagged as unused. This is regex/heuristic-based — see [Known Limitations](#known-limitations).
- **Quarantine / restore workflow** — unused assets are moved (not deleted) into `Assets/_Quarantine/`, preserving their original relative folder structure, with a manifest tracking how to restore each one.
- **Dependency graph view** — see what an asset depends on and what depends on it before deciding whether to remove it.
- **Whitelisting** — permanently exclude specific assets or paths from being flagged as unused.
- **Duplicate detection** — finds byte-identical duplicate assets (via MD5 hashing) so you can reclaim wasted disk space.
- **CSV / XLSX export** — export scan results, including a dependency breakdown, for sharing or offline review.
- **Accessibility support** — colour-blind-safe palettes (deuteranopia, protanopia, tritanopia), a high-contrast mode, and adjustable font/UI scale.
- **Version control awareness** — integrates with Perforce/Plastic SCM through Unity's `VersionControl.Provider` API when active (see [Version Control Notes](#version-control-notes)).

## Quick Start
1. Open the tool from the Unity Editor menu: **Tools ▸ Project Cleanup Utility**.
2. Click **Scan** to run a full project scan. This builds the dependency graph and identifies unused assets.
3. Review the **Assets** tab — sort/filter by category, size, or deletion safety, and inspect the dependency panel for any asset before acting on it.
4. Select assets you're confident about and click **Quarantine Selected**. This moves them into `Assets/_Quarantine/` — nothing is deleted yet.
5. Switch to the **Quarantine** tab to review quarantined assets. You can **Restore** anything you change your mind about, or **Delete Permanently** once you're confident.
6. Use **Whitelist Selected** to permanently exclude assets you never want flagged again (e.g. assets only referenced by a scene not currently open, or resources loaded via a code path the string-reference scanner can't see).

## Known Limitations

- **Regex-based code-reference detection can produce false negatives.** The scanner looks for string literals in your C# source that match common asset-loading patterns (`Resources.Load("...")`, `AssetDatabase.LoadAssetAtPath("...")`, `Addressables.LoadAssetAsync("...")`, raw `Assets/...` paths, and GUID strings). It cannot detect references built from concatenated strings, variables, or `string.Format`/string interpolation with non-literal segments. Since this detection directly feeds the "Safe to delete" classification, an asset that *is* referenced by code through one of these dynamic patterns can still be marked "Safe" and deleted. **Always review the dependency panel and use Quarantine (not permanent delete) for anything you're not 100% sure about.**
- **Single-slot undo.** The tool's Undo button only remembers the *most recent* action. Performing a second quarantine/restore/whitelist action overwrites the ability to undo the first one. This is a tool-level undo, not Unity's native Ctrl+Z — the native Undo menu will not reverse these operations.
- **Permanent delete is genuinely permanent.** Deleting from quarantine (or from the main Assets list) removes the asset with no recovery path inside the tool itself. Your only fallback is your version control history, if you have one active.

## Performance

- **Quarantine, Restore, Delete Permanently, and Undo update the visible asset list incrementally.** Only the very first scan (and any manual rescan you trigger yourself) does the expensive work: rebuilding the full asset map, the dependency graph, the .cs text scan for string references, and the VCS status query. After that, quarantining/restoring/deleting/undoing assets just adds or removes those specific entries from the in-memory list and re-renders the list/stats panels - it does not re-scan the project. On large projects this is dramatically faster than the previous behaviour of re-running a full scan after every action.
- **Trigger a manual full rescan** with the **Scan Project** button (or the `Ctrl+R`/`F5` shortcuts) whenever you need up-to-date results from outside the tool's own actions - for example after pulling changes from version control, after another teammate's commit, after editing assets directly in the Project window, or simply if the References/Dependencies/Safety columns look stale (see the note below).
- **Known limitation - dependency-graph staleness between rescans.** Incremental updates only add/remove the directly affected assets from the list; they deliberately do **not** recompute `ReferenceCount`/`ReferencedBy`/`DependencyCount`/`DependsOn`/`Safety` for *other* assets that referenced or were referenced by the changed ones. For example, quarantining asset X does not update whether some asset Y - previously only referenced by X - should now show as unreferenced. Those fields reflect the last full scan until you run another one. This is a deliberate tradeoff: recomputing dependency data correctly on every action would mean re-resolving every direct referrer/dependency of the changed assets, which for widely-shared assets approaches the cost of the full scan this feature exists to avoid. Run a manual rescan (Scan Project / `Ctrl+R` / `F5`) periodically, and especially before relying on reference counts for a batch of permanent deletes.

## Version Control Notes

The tool integrates with Perforce and Plastic SCM through Unity's [`UnityEditor.VersionControl.Provider`](https://docs.unity3d.com/6000.1/Documentation/ScriptReference/VersionControl.Provider.html) API. When a VCS provider is active (`Provider.isActive`), the tool:

- Checks for exclusive locks held by other users before quarantining or permanently deleting an asset, and refuses the operation if the asset is locked.
- Checks assets out (`AssetDatabase.MakeEditable`) before moving or deleting them.
- Uses `Provider.Delete` for permanent deletes so the change is reflected in version control, not just on disk.

**If you don't use Perforce or Plastic SCM** — including if you use **Git**, **SVN**, or no version control at all — `Provider.isActive` is simply `false`, and the tool falls straight through to plain `AssetDatabase.MoveAsset`/`AssetDatabase.DeleteAsset` calls. You are not blocked in any way; you just don't get the exclusive-lock safety checks that Perforce/Plastic users get. In practice this means Git users should rely on their own workflow discipline (branches, review, `git status` before committing) in place of the VCS-lock checks this tool provides for Perforce/Plastic users.

### Working with Git

Because quarantine is implemented as a physical file move within your project (`Assets/_Quarantine/...`), it shows up in `git status` like any other file move. You have two reasonable options:

1. **`.gitignore` the quarantine folder and manifest.** Quarantined-but-not-yet-deleted assets won't be tracked by Git, so quarantining/restoring assets produces no diff noise. The tradeoff: if something goes wrong and you need to recover a quarantined file's history, Git won't have it — you're relying entirely on the tool's own manifest and the quarantine folder's on-disk contents.
2. **Commit the quarantine folder and manifest deliberately**, treating it as a recoverable backup. This means every quarantine/restore action produces a commit-worthy diff (a large one, if you quarantine many assets at once, since Git sees a delete-and-add rather than a move in many cases), but you get full Git history and can recover a quarantined file the same way you'd recover any other historical file.

Neither option is a universal default — pick whichever fits your team's workflow, and see `.gitignore.sample` in this repository for a starting point with both options laid out.

## License

Apache License, Version 2.0 — see [`LICENSE`](./LICENSE) and [`NOTICE`](./NOTICE).
