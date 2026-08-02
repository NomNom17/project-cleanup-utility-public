// -----------------------------------------------------------------------
// Project Cleanup Utility
// Copyright (C) 2026 NomNom
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Source: https://github.com/NomNom17/Project-Cleanup-Utility
// -----------------------------------------------------------------------

using ProjectCleanupUtility.Data;
using ProjectCleanupUtility.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEditor;
using UnityEditor.VersionControl;
using UnityEngine;
using Debug = UnityEngine.Debug;

// --- References ---
// AssetDatabase.GetAllAssetPaths:  https://docs.unity3d.com/6000.1/Documentation/ScriptReference/AssetDatabase.html
// AssetDatabase.GetDependencies:   https://docs.unity3d.com/ScriptReference/AssetDatabase.GetDependencies.html
// AssetDatabase.FindAssets:        https://docs.unity3d.com/ScriptReference/AssetDatabase.FindAssets.html
// Resources.Load path conventions: https://docs.unity3d.com/ScriptReference/Resources.Load.html
// EditorBuildSettings.scenes:      https://docs.unity3d.com/ScriptReference/EditorBuildSettings-scenes.html
// Addressables.LoadAssetAsync:     https://docs.unity3d.com/Packages/com.unity.addressables@1.13/manual/LoadingAddressableAssets.html

namespace ProjectCleanupUtility.Core
{
    /// <summary>
    /// Scans the Unity project to find all assets and determine which are unused.
    /// Uses <see cref="AssetDatabase.GetDependencies"/> to build a reverse-dependency map, then identifies assets with zero incoming references.
    /// </summary>
    public class AssetScanner
    {
        // Events for progress reporting
        public event Action<string, float> OnProgressUpdated;
        public event Action<ScanResult> OnScanComplete;
        public event Action<string> OnScanError;

        private WhitelistConfig _whitelist;
        private volatile bool _cancelRequested;
        private CancellationTokenSource _cts;


        // Pre-compiled patterns for detecting string-based asset references in C# source code.
        // These catch the common ways developers load assets via code rather than serialised Inspector fields, which AssetDatabase.GetDependencies misses entirely.
        private static readonly Regex ResourcesLoadRegex = new Regex(@"Resources\.Load[^(]*\(\s*(?:[$@])?""([^""]+)""", RegexOptions.Compiled);

        private static readonly Regex AssetPathLiteralRegex = new Regex(@"""(Assets/[^""]+\.[a-zA-Z0-9]+)""", RegexOptions.Compiled);

        private static readonly Regex AddressablesRegex = new Regex(@"Addressables\.(?:LoadAssetAsync|InstantiateAsync|LoadAssetsAsync)[^(]*\(\s*(?:[$@])?""([^""]+)""", RegexOptions.Compiled);

        private static readonly Regex GuidStringRegex = new Regex(@"""([0-9a-fA-F]{32})""", RegexOptions.Compiled);

        /// <summary>
        /// Request cancellation of the current scan by setting <see cref="_cancelRequested"/> to <see langword="true"/>.
        /// </summary>
        public void RequestCancel()
        {
            _cancelRequested = true;
            _cts?.Cancel();
        }

        /// <summary>
        /// Performs a full project scan to identify unused assets.
        /// </summary>
        /// <param name="whitelist">Optional whitelist config to exclude certain assets,
        /// <see langword="null"/> by default.</param>
        /// <returns>The <see cref="ScanResult"/> containing all used and unused assets.</returns>
        public async System.Threading.Tasks.Task<ScanResult> Scan(WhitelistConfig whitelist = null)
        {
            _cancelRequested = false;
            _cts = new CancellationTokenSource();
            _whitelist = whitelist;
            var stopwatch = Stopwatch.StartNew();

            try {
                ReportProgress("Gathering all asset paths...", 0f);

                // Get all asset paths in the project
                string[] allPaths = AssetDatabase.GetAllAssetPaths()
                    .Where(IsValidAssetPath)
                    .ToArray();

                if (_cancelRequested) return null;

                ReportProgress($"Found {allPaths.Length} assets. Building asset map...", 0.1f);

                // Build AssetInfo objects for each valid asset
                Dictionary<string, AssetInfo> assetMap = BuildAssetMap(allPaths);

                if (_cancelRequested) return null;

                // Build dependency graph (reverse references)
                BuildDependencyGraph(assetMap, allPaths);

                if (_cancelRequested) return null;

                // Mark assets referenced by ProjectSettings or Build Settings
                ReportProgress("Checking Project Settings references...", 0.85f);
                MarkProjectSettingsReferences(assetMap);
                MarkBuildSceneReferences(assetMap);

                if (_cancelRequested) return null;

                // Scan C# scripts for string-based asset references
                ReportProgress("Scanning scripts for string-based references...", 0.87f);
                MarkScriptStringReferences(assetMap);

                if (_cancelRequested) return null;

                // Compute deletion safety for every asset
                ReportProgress("Evaluating deletion safety...", 0.89f);
                ComputeDeletionSafety(assetMap);

                if (Provider.isActive)
                {
                    ReportProgress("Querying Perforce status...", 0.93f);
                    await QueryVcsStatus(assetMap);
                }

                // Apply whitelist and identify unused assets
                ReportProgress("Identifying unused assets...", 0.9f);
                ApplyWhitelist(assetMap);
                ExcludeToolOwnAssets(assetMap);

                var unusedAssets = assetMap.Values
                    .Where(a => a.IsUnused)
                    .OrderByDescending(a => a.SizeBytes)
                    .ToList();

                stopwatch.Stop();

                // Build result
                var result = new ScanResult
                {
                    AllAssets = assetMap.Values.ToList(),
                    UnusedAssets = unusedAssets,
                    ScanTimestamp = DateTime.Now,
                    ScanDurationSeconds = stopwatch.Elapsed.TotalSeconds
                };

                ReportProgress("Scan complete.", 1f);
                OnScanComplete?.Invoke(result);

                Debug.Log($"[Project Cleanup Utility] Scan complete in " + $"{result.ScanDurationSeconds:F2}s. " + $"Found {result.UnusedAssetCount} unused assets " + $"({FormatBytes(result.UnusedSizeBytes)}) out of " + $"{result.TotalAssetCount} total.");

                return result;
            }

            catch (Exception ex)
            {
                stopwatch.Stop();
                var error = $"Scan failed: {ex.Message}";
                Debug.LogError($"[Project Cleanup Utility] {error}\n{ex.StackTrace}");
                OnScanError?.Invoke(error);
                return null;
            }

            finally
            {
                EditorUtility.ClearProgressBar();
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// Filters out non-asset paths (folders, packages, meta files, etc.), to reduce redundant files from appearing in the unused asset list.
        /// </summary>
        /// <param name="path">The path to check.</param>
        /// <returns><see langword="true"/> if the path is valid, <see langword="false"/> otherwise.</returns>
        private static bool IsValidAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (!path.StartsWith("Assets/")) return false;
            if (AssetDatabase.IsValidFolder(path)) return false;
            return !path.EndsWith(".meta");
        }

        /// <summary>
        /// Creates AssetInfo objects for all valid paths and populates metadata.
        /// </summary>
        /// <param name="paths">The paths to scan.</param>
        /// <returns>A <see cref="Dictionary{TKey, TValue}"/> mapping asset paths to their corresponding
        /// <see cref="AssetInfo"/> objects.</returns>
        private Dictionary<string, AssetInfo> BuildAssetMap(string[] paths)
        {
            var map = new Dictionary<string, AssetInfo>(paths.Length, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < paths.Length; i++)
            {
                if (_cancelRequested) return map;

                // Explicit types are used here (instead of var) to keep the metadata
                // being extracted for each asset (path, extension, GUID) self-documenting at a glance.
                string path = paths[i];
                string extension = Path.GetExtension(path);
                string guid = AssetDatabase.AssetPathToGUID(path);
                long fileSize = 0;

                // Try to get the file size, if possible
                bool isReadOnly = false;

                try
                {
                    string fullPath = Path.GetFullPath(path);

                    if (File.Exists(fullPath))
                    {
                        var fi = new FileInfo(fullPath);
                        fileSize = fi.Length;
                        isReadOnly = fi.IsReadOnly;
                    }
                }

                catch (Exception ex)
                {
                    Debug.LogWarning($"[Project Cleanup Utility] Could not read file info for {path}: {ex.Message}");
                }

                var info = new AssetInfo
                {
                    Path = path,
                    Name = Path.GetFileName(path),
                    GUID = guid,
                    Extension = extension,
                    Category = AssetCategoryResolver.Resolve(extension),
                    SizeBytes = fileSize,
                    IsReadOnly = isReadOnly
                };

                map[path] = info;

                // Update the progress bar
                if (i % 500 == 0)
                {
                    float progress = 0.1f + (float)i / paths.Length * 0.3f;
                    ReportProgress($"Building asset map... ({i}/{paths.Length})", progress);
                }
            }

            return map; // reminder, this isn't an in-game minimap
        }

        /// <summary>
        /// Uses <see cref="AssetDatabase.GetDependencies"/> to build the reverse reference map.
        /// For each asset, we find what it depends on, and mark those targets as "referenced by" the source asset.
        /// </summary>
        private void BuildDependencyGraph(Dictionary<string, AssetInfo> assetMap, string[] paths)
        {
            ReportProgress("Building dependency graph...", 0.4f);

            for (int i = 0; i < paths.Length; i++)
            {
                if (_cancelRequested) return;

                string sourcePath = paths[i];

                // Get direct dependencies of this asset
                string[] dependencies = AssetDatabase.GetDependencies(sourcePath, recursive: false);

                if (!assetMap.TryGetValue(sourcePath, out AssetInfo sourceInfo)) continue;

                foreach (string depPath in dependencies)
                {
                    // Skip self-references (stops being narcissistic)
                    if (string.Equals(depPath, sourcePath, StringComparison.OrdinalIgnoreCase)) continue;

                    // Only track dependencies that are within our scanned asset map (skip built-in / package assets so that DependsOn.Count matches what the side panel displays).
                    if (assetMap.TryGetValue(depPath, out AssetInfo depInfo))
                    {
                        sourceInfo.DependsOn.Add(depPath);
                        depInfo.ReferencedBy.Add(sourcePath);
                    }
                }

                if (i % 200 == 0)
                {
                    float progress = 0.4f + (float)i / paths.Length * 0.5f;

                    ReportProgress($"Analysing dependencies... ({i}/{paths.Length})", progress);
                }
            }
        }
        
        /// <summary>
        /// Scans all files under <c>ProjectSettings/</c> for GUID references to assets.
        /// This catches render-pipeline assets, input settings, physics settings, etc. that are referenced by the engine but not by any asset file.
        /// </summary>
        /// <returns>A <see cref="Dictionary{TKey,TValue}"/> mapping asset paths to their corresponding
        /// <see cref="AssetInfo"/> objects.</returns>
        private void MarkProjectSettingsReferences(Dictionary<string, AssetInfo> assetMap)
        {
            // Build a GUID -> path lookup for fast resolution
            var guidToPath = new Dictionary<string, string>(assetMap.Count, StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in assetMap)
            {
                if (!string.IsNullOrEmpty(kvp.Value.GUID))
                    guidToPath[kvp.Value.GUID] = kvp.Key;
            }

            string projectSettingsDir = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "ProjectSettings");

            if (!Directory.Exists(projectSettingsDir)) return;

            // GUID pattern 32 hex characters
            var guidRegex = new System.Text.RegularExpressions.Regex(@"[{,]?\s*guid:\s*([0-9a-fA-F]{32})", System.Text.RegularExpressions.RegexOptions.Compiled);

            string[] settingsFiles = Directory.GetFiles(projectSettingsDir, "*", SearchOption.TopDirectoryOnly);

            foreach (string settingsFile in settingsFiles)
            {
                if (_cancelRequested) return;

                try
                {
                    string content = File.ReadAllText(settingsFile);
                    var matches = guidRegex.Matches(content);

                    string settingsName = Path.GetFileName(settingsFile);

                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        string guid = match.Groups[1].Value;

                        if (guidToPath.TryGetValue(guid, out string assetPath) && assetMap.TryGetValue(assetPath, out AssetInfo info))
                        {
                            // Add a synthetic "referenced by" entry so the asset is no longer considered unused.
                            string syntheticRef = $"ProjectSettings/{settingsName}";

                            if (!info.ReferencedBy.Contains(syntheticRef)) info.ReferencedBy.Add(syntheticRef);
                        }
                    }
                }

                catch (Exception ex)
                {
                    Debug.LogWarning($"[Project Cleanup Utility] Could not read settings file {settingsFile}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Marks scenes that are included in Build Settings as referenced, since they will be included in the build regardless of asset dependencies.
        /// </summary>
        /// <param name="assetMap">The asset map to update.</param>
        private void MarkBuildSceneReferences(Dictionary<string, AssetInfo> assetMap)
        {
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (_cancelRequested) return;
                if (!scene.enabled) continue;

                if (assetMap.TryGetValue(scene.path, out AssetInfo info))
                {
                    string syntheticRef = "BuildSettings/ScenesInBuild";

                    if (!info.ReferencedBy.Contains(syntheticRef)) info.ReferencedBy.Add(syntheticRef);
                }
            }
        }

        /// <summary>
        /// Reads all C# scripts in the project and searches for string literals that reference asset paths or GUIDs. This catches assets loaded via <c>Resources.Load("path")</c>, <c>AssetDatabase.LoadAssetAtPath("Assets/...")</c>, <c>Addressables.LoadAssetAsync("key")</c>, or raw path strings in code.
        /// <br/><br/>
        /// Without this step, any asset referenced exclusively through code (and never dragged into an Inspector field) would be incorrectly flagged as unused.
        /// </summary>
        /// <param name="assetMap">The asset map to update.</param>
        private void MarkScriptStringReferences(Dictionary<string, AssetInfo> assetMap)
        {
            // Build lookup structures for fast matching
            var guidToPath = new Dictionary<string, string>(assetMap.Count, StringComparer.OrdinalIgnoreCase);

            // Map of filename-without-extension to asset paths (for Resources.Load matches)
            var resourceNameToAssets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in assetMap)
            {
                AssetInfo asset = kvp.Value;
                if (!string.IsNullOrEmpty(asset.GUID)) guidToPath[asset.GUID] = kvp.Key;

                // Build a lookup for Resources.Load style paths.
                // Resources.Load("Sprites/Hero") could match "Assets/Resources/Sprites/Hero.png" or any nested Resources folder like "Assets/MyStuff/Resources/Sprites/Hero.png".
                string path = kvp.Key;
                int resourcesIdx = path.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);

                if (resourcesIdx >= 0)
                {
                    // Extract the part after "/Resources/", without extension
                    string afterResources = path.Substring(resourcesIdx + "/Resources/".Length);
                    string withoutExt = Path.ChangeExtension(afterResources, null);
                    
                    if (!string.IsNullOrEmpty(withoutExt))
                    {
                        // Remove trailing dot left by ChangeExtension on some runtimes
                        withoutExt = withoutExt.TrimEnd('.');
                        
                        if (!resourceNameToAssets.TryGetValue(withoutExt, out var list))
                        {
                            list = new List<string>(1);
                            resourceNameToAssets[withoutExt] = list;
                        }

                        list.Add(path);
                    }
                }
            }

            // Find all .cs files in Assets/
            string assetsDir = Application.dataPath; // returns <ProjectRoot>/Assets
            if (!Directory.Exists(assetsDir)) return;

            string[] csharpFiles;

            try
            {
                csharpFiles = Directory.GetFiles(assetsDir, "*.cs", SearchOption.AllDirectories);
            }

            catch (Exception ex)
            {
                Debug.LogWarning($"[Project Cleanup Utility] Could not enumerate scripts: {ex.Message}");
                return;
            }

            int processed = 0;

            foreach (string csFile in csharpFiles)
            {
                if (_cancelRequested) return;

                processed++;

                if (processed % 100 == 0)
                {
                    float progress = 0.87f + (float)processed / csharpFiles.Length * 0.01f;
                    ReportProgress($"Scanning scripts... ({processed}/{csharpFiles.Length})", progress);
                }

                string content;

                try
                {
                    content = File.ReadAllText(csFile);
                }

                catch (Exception ex)
                {
                    Debug.LogWarning($"[Project Cleanup Utility] Could not read script {csFile}: {ex.Message}");
                    continue;
                }

                // Convert the absolute script path to a project-relative path for the ref label
                string scriptRelative = "Assets" + csFile.Substring(assetsDir.Length).Replace('\\', '/');

                // Scan Pattern 1: Resources.Load("path")
                foreach (Match match in ResourcesLoadRegex.Matches(content))
                {
                    string resourcePath = match.Groups[1].Value;

                    if (resourceNameToAssets.TryGetValue(resourcePath, out var matches))
                    {
                        foreach (string assetPath in matches)
                        {
                            AddScriptReference(assetMap, assetPath, scriptRelative);
                        }
                    }
                }

                // Scan Pattern 2: "Assets/Some/Path.ext" literal strings ----
                foreach (Match match in AssetPathLiteralRegex.Matches(content))
                {
                    string literalPath = match.Groups[1].Value;

                    if (assetMap.ContainsKey(literalPath))
                    {
                        AddScriptReference(assetMap, literalPath, scriptRelative);
                    }
                }

                // Scan Pattern 3: Addressables.LoadAssetAsync("key")
                // Addressable keys can be arbitrary strings; we try to match them against known asset paths or filenames. This is best-effort because addressable keys are user-defined and may not match any file directly.
                foreach (Match match in AddressablesRegex.Matches(content))
                {
                    string key = match.Groups[1].Value;

                    // Try as a direct asset path first
                    if (assetMap.ContainsKey(key))
                    {
                        AddScriptReference(assetMap, key, scriptRelative);
                    }
                    // Try as a Resources-style relative path
                    else if (resourceNameToAssets.TryGetValue(key, out var addrMatches))
                    {
                        foreach (string assetPath in addrMatches)
                            AddScriptReference(assetMap, assetPath, scriptRelative);
                    }
                }

                // Scan Pattern 4: GUID strings (32 hex chars in quotes)
                foreach (Match match in GuidStringRegex.Matches(content))
                {
                    string guid = match.Groups[1].Value;

                    if (guidToPath.TryGetValue(guid, out string guidAssetPath))
                    {
                        AddScriptReference(assetMap, guidAssetPath, scriptRelative);
                    }
                }
            }
        }

        /// <summary>
        /// Adds a synthetic "referenced by" entry to an asset, marking it as referenced by a C# script. Prevents the same script from being added multiple times because nobody likes a clingy referrer.
        /// </summary>
        private static void AddScriptReference(Dictionary<string, AssetInfo> assetMap, string assetPath, string scriptPath)
        {
            if (assetMap.TryGetValue(assetPath, out AssetInfo info))
            {
                string syntheticRef = $"Script/{scriptPath}";

                if (!info.ReferencedBy.Contains(syntheticRef))
                {
                    info.ReferencedBy.Add(syntheticRef);
                }
            }
        }

        /// <summary>
        /// Classifies each asset's deletion safety based on who references it.
        /// <list type="bullet">
        /// Safe - 0 incoming references (nothing will break).
        /// </list>
        /// <list type="bullet">
        /// Caution - referenced only by ProjectSettings / BuildSettings.
        /// </list>
        /// <list type="bullet">
        /// Unsafe - referenced by at least one other project asset.
        /// </list>
        /// </summary>
        private static void ComputeDeletionSafety(Dictionary<string, AssetInfo> assetMap)
        {
            foreach (var kvp in assetMap)
            {
                AssetInfo asset = kvp.Value;

                if (asset.ReferenceCount == 0)
                {
                    asset.Safety = DeletionSafety.Safe;
                    continue;
                }

                // Check whether ALL references are synthetic (ProjectSettings / BuildSettings)
                bool hasProjectAssetRef = false;

                foreach (string refPath in asset.ReferencedBy)
                {
                    // Synthetic refs injected by MarkProjectSettingsReferences /
                    // MarkBuildSceneReferences always start with "ProjectSettings/" or "BuildSettings/"
                    if (!refPath.StartsWith("ProjectSettings/") && !refPath.StartsWith("BuildSettings/"))
                    {
                        hasProjectAssetRef = true;
                        break;
                    }
                }

                asset.Safety = hasProjectAssetRef ? DeletionSafety.Unsafe : DeletionSafety.Caution;
            }
        }

        /// <summary>
        /// Applies the whitelist configuration to mark excluded assets.
        /// </summary>
        /// <param name="assetMap">The asset map to update.</param>
        private void ApplyWhitelist(Dictionary<string, AssetInfo> assetMap)
        {
            if (_whitelist == null) return;

            foreach (var kvp in assetMap)
            {
                if (_whitelist.IsWhitelisted(kvp.Key))
                {
                    kvp.Value.IsWhitelisted = true;
                }
            }
        }

        /// <summary>
        /// Locates the tool's own assembly definition and marks every asset under that folder as whitelisted. Prevents the tool from flagging its own USS stylesheets, icons, or config files as unused - because a tool recommending its own deletion is either deeply philosophical or a bug.
        /// </summary>
        private static void ExcludeToolOwnAssets(Dictionary<string, AssetInfo> assetMap)
        {
            string[] asmdefGuids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset ProjectCleanupUtility");

            foreach (string guid in asmdefGuids)
            {
                string asmdefPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(asmdefPath)) continue;

                string toolFolder = Path.GetDirectoryName(asmdefPath)?.Replace('\\', '/');
                if (string.IsNullOrEmpty(toolFolder)) continue;

                if (!toolFolder.EndsWith("/"))
                    toolFolder += "/";

                foreach (var kvp in assetMap)
                {
                    if (kvp.Key.StartsWith(toolFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        kvp.Value.IsWhitelisted = true;
                    }
                }

                Debug.Log($"[Project Cleanup Utility] Auto-excluded tool folder: {toolFolder}");
            }
        }

        /// <summary>
        /// Reports scan progress both via event and Unity's progress bar.
        /// </summary>
        private void ReportProgress(string message, float progress)
        {
            OnProgressUpdated?.Invoke(message, progress);

            EditorUtility.DisplayProgressBar("Project Cleanup Utility", message, progress);
        }

        /// <summary>
        /// Formats a byte count into a human-readable string using B, KB, MB, or GB,
        /// choosing the largest unit that keeps the value readable.
        /// </summary>
        /// <param name="bytes">The size in bytes to format.</param>
        /// <returns>A formatted <see langword="string"/>.</returns>
        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return order == 0
                ? $"{size:0} {suffixes[order]}"
                : $"{size:0.##} {suffixes[order]}";
        }

        private static System.Threading.Tasks.Task QueryVcsStatus(Dictionary<string, AssetInfo> assetMap)
        {
            // NOTE: not "async" internally. UnityEditor.VersionControl.Task.Wait() is a blocking call that must run on the main thread — there is no supported non-blocking or background-thread way to await it (as far as I know). This still returns Task so the caller's existing "await QueryVcsStatus(...)" doesn't need to change.

            // Provider.Status takes an Asset array — batch all assets in one call rather than calling it per-file which would be extremely slow on Perforce.
            var vcAssets = assetMap.Values
                .Select(a => new UnityEditor.VersionControl.Asset(a.Path))
                .ToArray();

            var paths = assetMap.Values.Select(a => a.Path).ToArray();
            var task = Provider.Status(paths, false);
            task.Wait();

            if (!task.success) return System.Threading.Tasks.Task.CompletedTask;

            foreach (var vcAsset in task.assetList)
            {
                string path = vcAsset.path.Replace("\\", "/");
                if (!assetMap.TryGetValue(path, out var info)) continue;

                var s = vcAsset.state;

                if ((s & Asset.States.AddedLocal) != 0)
                    info.PerforceStatus = VcsStatus.Added;
                else if ((s & Asset.States.DeletedLocal) != 0)
                    info.PerforceStatus = VcsStatus.Deleted;
                else if ((s & Asset.States.LockedRemote) != 0)
                {
                    info.PerforceStatus = VcsStatus.LockedByOther;
                    info.VcsOtherUser = "another user";
                }
                else if ((s & Asset.States.CheckedOutRemote) != 0)
                {
                    info.PerforceStatus = VcsStatus.CheckedOutOther;
                    info.VcsOtherUser = "another user";
                }
                else if ((s & Asset.States.CheckedOutLocal) != 0)
                    info.PerforceStatus = VcsStatus.CheckedOutLocal;
                else if ((s & Asset.States.OutOfSync) != 0)
                    info.PerforceStatus = VcsStatus.OutOfDate;
                else if ((s & Asset.States.Unversioned) != 0)
                    info.PerforceStatus = VcsStatus.Unversioned;
                else if ((s & Asset.States.Synced) != 0)
                    info.PerforceStatus = VcsStatus.UpToDate;
            }

            return System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
