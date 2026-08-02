// -----------------------------------------------------------------------
// Project Cleanup Utility
// Copyright 2026 NomNom. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// See the LICENSE and NOTICE files in the root of this repository for
// full license text and attribution requirements.
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.VersionControl;
using UnityEngine;
using ProjectCleanupUtility.Data;

// --- References ---
// AssetDatabase.MoveAsset:         https://docs.unity3d.com/6000.1/Documentation/ScriptReference/AssetDatabase.MoveAsset.html
// AssetDatabase.MakeEditable:      https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetDatabase.MakeEditable.html
// AssetDatabase.StartAssetEditing: https://docs.unity3d.com/6000.1/Documentation/ScriptReference/AssetDatabase.StartAssetEditing.html
// VersionControl.Provider.Delete:  https://docs.unity3d.com/6000.1/Documentation/ScriptReference/VersionControl.Provider.Delete.html

namespace ProjectCleanupUtility.Core
{
    /// <summary>
    /// Manages the quarantine workflow for unused assets. Instead of deleting assets immediately, they are moved to a quarantine folder. This allows users to review and restore assets before permanently deleting them.
    /// Workflow: Detect -> Quarantine -> Review -> Restore -> Permanently Delete
    /// </summary>
    public class QuarantineManager
    {
        // Default quarantine folder inside the project
        private const string DEFAULT_QUARANTINE_FOLDER = "Assets/_Quarantine";

        // Manifest file that tracks quarantined asset original paths
        private const string MANIFEST_FILENAME = "_quarantine_manifest.json";

        public event Action<string> OnStatusMessage;
        public event Action<string> OnError;

        /// <summary>
        /// Whether an active version control provider (Perforce, Plastic SCM, etc.) is currently running. Cached once per manager lifetime because checking every single file op would be dramatic.
        /// </summary>
        private bool IsVersionControlActive => Provider.isActive;

        private string _quarantineFolder;

        public string QuarantineFolder
        {
            get => _quarantineFolder;
            set => _quarantineFolder = value;
        }

        public QuarantineManager(string quarantineFolder = null)
        {
            _quarantineFolder = quarantineFolder ?? DEFAULT_QUARANTINE_FOLDER;
        }

        /// <summary>
        /// Moves a list of assets to the quarantine folder, preserving their relative directory structure so they can be restored later.
        /// </summary>
        /// <param name="assets">Assets to quarantine.</param>
        /// <returns>Number of assets successfully quarantined.</returns>
        public int QuarantineAssets(List<AssetInfo> assets)
        {
            if (assets == null || assets.Count == 0)
            {
                OnError?.Invoke("No assets provided for quarantine.");
                return 0;
            }

            EnsureQuarantineFolderExists();
            var manifest = LoadManifest();
            int successCount = 0;
            int unsavedSinceLastManifestWrite = 0;
            const int MANIFEST_SAVE_INTERVAL = 10;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (AssetInfo asset in assets)
                {
                    if (asset.IsQuarantined)
                    {
                        OnStatusMessage?.Invoke($"Skipped (already quarantined): {asset.Path}");
                        continue;
                    }

                    if (IsVersionControlActive && asset.IsVcsBlocked)
                    {
                        OnError?.Invoke($"Cannot quarantine '{asset.Name}' — exclusively locked by {asset.VcsOtherUser} in Perforce.");
                        continue;
                    }

                    try
                    {
                        // Build the quarantine destination path preserving structure
                        string relativePath = asset.Path;
                        string destPath = Path.Combine(_quarantineFolder, relativePath).Replace("\\", "/");

                        // Ensure destination directory exists
                        string destDir = Path.GetDirectoryName(destPath);

                        if (!string.IsNullOrEmpty(destDir))
                        {
                            EnsureFolderExists(destDir);
                        }

                        // Handle filename conflicts
                        destPath = GetUniqueAssetPath(destPath);

                        // VCS (Version Control System): checkout the asset before moving it
                        if (!EnsureEditable(asset.Path))
                        {
                            OnError?.Invoke($"Version control refused to check out: {asset.Name}");
                            continue;
                        }

                        // Move the asset
                        string moveError = AssetDatabase.MoveAsset(asset.Path, destPath);

                        if (string.IsNullOrEmpty(moveError))
                        {

                            // Record in manifest
                            manifest.Entries.Add(new QuarantineEntry
                            {
                                OriginalPath = asset.Path,
                                QuarantinePath = destPath,
                                QuarantineDate = DateTime.Now.ToString("o"),
                                AssetGUID = asset.GUID,
                                SizeBytes = asset.SizeBytes
                            });

                            asset.IsQuarantined = true;
                            asset.QuarantineOriginalPath = asset.Path;
                            successCount++;
                            unsavedSinceLastManifestWrite++;

                            OnStatusMessage?.Invoke($"Quarantined: {asset.Name}");

                            // Save the manifest incrementally so a mid-batch crash only orphans the assets moved since the last incremental save, rather than the entire batch.
                            if (unsavedSinceLastManifestWrite >= MANIFEST_SAVE_INTERVAL)
                            {
                                SaveManifest(manifest);
                                unsavedSinceLastManifestWrite = 0;
                            }
                        }

                        else
                        {
                            OnError?.Invoke($"Failed to quarantine {asset.Name}: {moveError}");
                        }
                    }

                    catch (Exception ex)
                    {
                        OnError?.Invoke($"Error quarantining {asset.Name}: {ex.Message}");
                    }
                }
            }

            finally
            {
                AssetDatabase.StopAssetEditing();
                SaveManifest(manifest);
                AssetDatabase.Refresh();
            }

            OnStatusMessage?.Invoke($"Quarantined {successCount}/{assets.Count} assets to " + $"{_quarantineFolder}");

            Debug.Log($"[Project Cleanup Utility] Quarantined {successCount} assets.");
            return successCount;
        }

        /// <summary>
        /// Restores quarantined assets back to their original locations.
        /// </summary>
        /// <param name="assets">Assets to restore.</param>
        /// <returns>Number (<see langword="int"/>) of assets successfully restored.</returns>
        public int RestoreAssets(List<AssetInfo> assets)
        {
            if (assets == null || assets.Count == 0)
            {
                OnError?.Invoke("No assets provided for restore.");
                return 0;
            }

            var manifest = LoadManifest();
            int successCount = 0;

            AssetDatabase.StartAssetEditing();

            try
            {
                foreach (AssetInfo asset in assets)
                {
                    try
                    {
                        // Find the manifest entry for this asset
                        QuarantineEntry entry = manifest.Entries.FirstOrDefault(e =>
                            string.Equals(e.AssetGUID, asset.GUID, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(e.QuarantinePath, asset.Path, StringComparison.OrdinalIgnoreCase));

                        if (entry == null)
                        {
                            OnError?.Invoke($"No quarantine record found for: {asset.Name}");
                            continue;
                        }

                        // Ensure the original directory exists
                        string origDir = Path.GetDirectoryName(entry.OriginalPath);
                        if (!string.IsNullOrEmpty(origDir))
                        {
                            EnsureFolderExists(origDir);
                        }

                        // Move the asset back
                        string currentPath = asset.Path.StartsWith(_quarantineFolder)
                            ? asset.Path
                            : entry.QuarantinePath;

                        // VCS: checkout the quarantined asset before restoring it
                        if (!EnsureEditable(currentPath))
                        {
                            OnError?.Invoke($"Version control refused to check out: {asset.Name}");
                            continue;
                        }

                        string moveError = AssetDatabase.MoveAsset(currentPath, entry.OriginalPath);

                        if (string.IsNullOrEmpty(moveError))
                        {
                            manifest.Entries.Remove(entry);
                            asset.IsQuarantined = false;
                            asset.Path = entry.OriginalPath;
                            successCount++;

                            OnStatusMessage?.Invoke($"Restored: {asset.Name} → {entry.OriginalPath}");
                        }

                        else
                        {
                            OnError?.Invoke($"Failed to restore {asset.Name}: {moveError}");
                        }
                    }

                    catch (Exception ex)
                    {
                        OnError?.Invoke($"Error restoring {asset.Name}: {ex.Message}");
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                SaveManifest(manifest);
                AssetDatabase.Refresh();
            }

            CleanEmptyQuarantineFolders();

            OnStatusMessage?.Invoke($"Restored {successCount}/{assets.Count} assets.");
            Debug.Log($"[Project Cleanup Utility] Restored {successCount} assets.");

            return successCount;
        }

        /// <summary>
        /// Permanently deletes quarantined assets. This cannot be undone.
        /// </summary>
        /// <param name="assets">Assets to permanently delete.</param>
        /// <returns>Number of assets successfully deleted.</returns>
        public System.Threading.Tasks.Task<int> PermanentlyDelete(List<AssetInfo> assets)
        {
            if (assets == null || assets.Count == 0)
            {
                OnError?.Invoke("No assets provided for deletion.");
                return System.Threading.Tasks.Task.FromResult(0);
            }

            var manifest = LoadManifest();
            int successCount = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (AssetInfo asset in assets)
                {
                    if (IsVersionControlActive && asset.IsVcsBlocked)
                    {
                        OnError?.Invoke($"Cannot delete '{asset.Name}' — exclusively locked by {asset.VcsOtherUser} in Perforce.");
                        continue;
                    }

                    try
                    {
                        string pathToDelete = asset.Path;
                        bool deleted = false;

                        if (IsVersionControlActive)
                        {
                            // VCS: use Provider.Delete to remove from both disk and version control in one shot.
                            // NOTE: UnityEditor.VersionControl.Task.Wait() is a blocking call that Unity's own documentation and reference usage require to run on the main thread — there is no supported non-blocking or background-thread-safe way to await it (an earlier attempt to offload this to a background thread via Task.Run threw "Wait can only be called from the main thread." at runtime). This call is intentionally synchronous.
                            var vcsTask = Provider.Delete(pathToDelete);
                            vcsTask.Wait();
                            deleted = vcsTask.success;
                        }
                        else
                        {
                            deleted = AssetDatabase.DeleteAsset(pathToDelete);
                        }

                        if (deleted)
                        {
                            // Remove from manifest if present
                            manifest.Entries.RemoveAll(e =>
                                string.Equals(e.AssetGUID, asset.GUID, StringComparison.OrdinalIgnoreCase));

                            successCount++;
                            OnStatusMessage?.Invoke($"Deleted: {asset.Name}");
                        }
                        else
                        {
                            OnError?.Invoke($"Failed to delete {asset.Name}");
                        }
                    }

                    catch (Exception ex)
                    {
                        OnError?.Invoke($"Error deleting {asset.Name}: {ex.Message}");
                    }
                }
            }

            finally
            {
                AssetDatabase.StopAssetEditing();
                SaveManifest(manifest);
                AssetDatabase.Refresh();
            }

            CleanEmptyQuarantineFolders();

            OnStatusMessage?.Invoke($"Permanently deleted {successCount}/{assets.Count} assets.");
            Debug.Log($"[Project Cleanup Utility] Permanently deleted {successCount} assets.");

            return System.Threading.Tasks.Task.FromResult(successCount);
        }

        /// <summary>
        /// Returns all currently quarantined assets from the manifest.
        /// </summary>
        public List<QuarantineEntry> GetQuarantinedAssets()
        {
            return LoadManifest().Entries;
        }

        /// <summary>
        /// Clears the entire quarantine (permanently deletes all quarantined assets).
        /// </summary>
        public System.Threading.Tasks.Task ClearQuarantine()
        {
            // NOTE: no longer "async" internally — see PermanentlyDelete for why. Still returns Task so existing awaiting call sites don't need to change.
            if (AssetDatabase.IsValidFolder(_quarantineFolder))
            {
                if (IsVersionControlActive)
                {
                    // VCS: delete the entire quarantine folder through version control so it doesn't leave orphaned entries.
                    // NOTE: UnityEditor.VersionControl.Task.Wait() must run synchronously on the main thread — there is no supported non-blocking alternative (see PermanentlyDelete for details).
                    var vcsTask = Provider.Delete(_quarantineFolder);
                    vcsTask.Wait();
                }

                else
                {
                    AssetDatabase.DeleteAsset(_quarantineFolder);
                }

                AssetDatabase.Refresh();
                OnStatusMessage?.Invoke("Quarantine cleared.");
                Debug.Log("[Project Cleanup Utility] Quarantine cleared.");
            }

            return System.Threading.Tasks.Task.CompletedTask;
        }


        private string ManifestPath => Path.Combine(_quarantineFolder, MANIFEST_FILENAME).Replace("\\", "/");

        private QuarantineManifest LoadManifest()
        {
            string path = ManifestPath;

            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    return JsonUtility.FromJson<QuarantineManifest>(json) ?? new QuarantineManifest();
                }

                catch (Exception ex)
                {
                    Debug.LogWarning($"[Project Cleanup Utility] Failed to load manifest: {ex.Message}");
                }
            }

            return new QuarantineManifest();
        }

        private void SaveManifest(QuarantineManifest manifest)
        {
            try
            {
                EnsureQuarantineFolderExists();

                string manifestPath = ManifestPath;
                string tempPath = manifestPath + ".tmp";
                string backupPath = manifestPath + ".bak";
                bool manifestExists = File.Exists(manifestPath);

                // VCS: make the manifest (and its backup, if present) writable before we touch them
                if (manifestExists)
                {
                    AssetDatabase.MakeEditable(manifestPath);
                }

                if (File.Exists(backupPath))
                {
                    AssetDatabase.MakeEditable(backupPath);
                }

                string json = JsonUtility.ToJson(manifest, prettyPrint: true);

                // Write to a temp file first so a crash mid-write never leaves a half-written manifest behind - only the temp file would be corrupt.
                File.WriteAllText(tempPath, json);

                // Keep a rolling backup of the previous manifest before we replace it.
                // Skipped on the very first save, since there is no existing manifest yet.
                if (manifestExists)
                {
                    try
                    {
                        File.Copy(manifestPath, backupPath, overwrite: true);
                    }
                    catch (Exception backupEx)
                    {
                        Debug.LogWarning($"[Project Cleanup Utility] Failed to back up manifest: {backupEx.Message}");
                    }
                }

                // Atomically replace the real manifest with the freshly written temp file.
                // File.Replace is atomic on the file systems Unity runs on; fall back to a delete-then-move for older .NET/Mono runtimes where File.Replace may be unavailable or unsupported (e.g. across volumes).
                try
                {
                    if (manifestExists)
                    {
                        File.Replace(tempPath, manifestPath, null);
                    }
                    else
                    {
                        File.Move(tempPath, manifestPath);
                    }
                }
                catch (Exception replaceEx)
                {
                    Debug.LogWarning($"[Project Cleanup Utility] File.Replace failed, falling back to delete-then-move: {replaceEx.Message}");

                    if (File.Exists(manifestPath))
                    {
                        File.Delete(manifestPath);
                    }

                    File.Move(tempPath, manifestPath);
                }

                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Project Cleanup Utility] Failed to save manifest: {ex.Message}");
            }
        }

        /// <summary>
        /// Attempts to make a file editable before a move or delete operation.
        /// Two separate problems can cause a file to be read-only:
        /// <br></br>
        ///   1. A VCS lock (Perforce, Plastic SCM) - AssetDatabase.MakeEditable handles this. <br></br>
        ///   2. The read-only file attribute on disk - Unity sets this on imported assets
        ///      that belong to a package or in certain project configurations. This has
        ///      nothing to do with VCS and MakeEditable does not clear it reliably. <br></br><br></br>
        /// Both are handled here so neither silently blocks quarantine.
        /// </summary>
        /// <param name="assetPath">Asset path relative to project root (e.g. Assets/...).</param>
        /// <returns>True if the file is now editable, false if it still cannot be written.</returns>
        private bool EnsureEditable(string assetPath)
        {
            // try the Unity/VCS route first
            AssetDatabase.MakeEditable(assetPath);

            // also clear the read-only attribute directly on disk.
            // MakeEditable does not always clear this - it is a VCS concept, not a filesystem concept. Unity can mark imported asset files as read-only as an internal safeguard. File.SetAttributes removes that flag so the subsequent MoveAsset call is not refused by the OS.
            try
            {
                string fullPath = Path.GetFullPath(assetPath);

                if (File.Exists(fullPath))
                {
                    FileAttributes attrs = File.GetAttributes(fullPath);

                    if ((attrs & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(fullPath, attrs & ~FileAttributes.ReadOnly);
                        Debug.Log($"[Project Cleanup Utility] Cleared read-only flag: {assetPath}");
                    }
                }

                // Also clear the .meta file - Unity moves it alongside the asset
                string metaPath = fullPath + ".meta";

                if (File.Exists(metaPath))
                {
                    FileAttributes metaAttrs = File.GetAttributes(metaPath);

                    if ((metaAttrs & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(metaPath, metaAttrs & ~FileAttributes.ReadOnly);
                    }
                }
            }

            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[Project Cleanup Utility] Could not clear read-only flag on {assetPath}: {ex.Message}");
                return false;
            }

            return true;
        }

        private void EnsureQuarantineFolderExists()
        {
            EnsureFolderExists(_quarantineFolder);
        }

        private void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            string[] parts = folderPath.Replace("\\", "/").Split('/');
            string currentPath = parts[0]; // "Assets"

            for (int i = 1; i < parts.Length; i++)
            {
                string nextPath = currentPath + "/" + parts[i];

                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, parts[i]);
                }

                currentPath = nextPath;
            }
        }

        private string GetUniqueAssetPath(string desiredPath)
        {
            if (!File.Exists(desiredPath)) return desiredPath;

            string dir = Path.GetDirectoryName(desiredPath)?.Replace("\\", "/");
            string nameWithoutExt = Path.GetFileNameWithoutExtension(desiredPath);
            string ext = Path.GetExtension(desiredPath);
            int counter = 1;

            string newPath;
            do
            {
                newPath = $"{dir}/{nameWithoutExt}_{counter}{ext}";
                counter++;
            } 
            
            while (File.Exists(newPath));

            return newPath;
        }

        private void CleanEmptyQuarantineFolders()
        {
            // Clean up empty subdirectories in the quarantine folder
            if (!AssetDatabase.IsValidFolder(_quarantineFolder)) return;

            string fullPath = Path.GetFullPath(_quarantineFolder);

            if (!Directory.Exists(fullPath)) return;

            try
            {
                CleanEmptyDirectories(fullPath);
            }

            catch (Exception ex)
            {
                Debug.LogWarning($"[Project Cleanup Utility] Error cleaning quarantine: {ex.Message}");
            }
        }

        private void CleanEmptyDirectories(string directory)
        {
            foreach (string subDir in Directory.GetDirectories(directory))
            {
                CleanEmptyDirectories(subDir);
            }

            // Check if the directory is now empty (no files, no subdirectories)
            if (Directory.GetFiles(directory).Length == 0 && Directory.GetDirectories(directory).Length == 0 && !directory.Replace("\\", "/").EndsWith(_quarantineFolder.Replace("\\", "/")))
            {
                try
                {
                    // VCS: make the meta file editable before nuking it
                    string metaFile = directory + ".meta";

                    if (File.Exists(metaFile))
                    {
                        AssetDatabase.MakeEditable(metaFile);
                        File.Delete(metaFile);
                    }

                    Directory.Delete(directory);
                }

                catch (Exception ex)
                {
                    Debug.LogWarning($"[Project Cleanup Utility] Could not clean empty directory {directory}: {ex.Message}");
                }
            }
        }
    }

    [Serializable]
    public class QuarantineManifest
    {
        public List<QuarantineEntry> Entries = new List<QuarantineEntry>();
    }

    [Serializable]
    public class QuarantineEntry
    {
        public string OriginalPath;
        public string QuarantinePath;
        public string QuarantineDate;
        public string AssetGUID;
        public long SizeBytes;
    }
}
