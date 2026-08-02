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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// --- References ---
// ScriptableObject / CreateAssetMenu: https://docs.unity3d.com/ScriptReference/ScriptableObject.html
// AssetDatabase.FindAssets:           https://docs.unity3d.com/ScriptReference/AssetDatabase.FindAssets.html
// Regex (System.Text.RegularExpressions): https://learn.microsoft.com/en-us/dotnet/api/system.text.regularexpressions.regex

namespace ProjectCleanupUtility.Data
{
    /// <summary>
    /// ScriptableObject (everyone's favourite) that persists whitelist rules across sessions, so you don't run the risk of losing the whitelist rules.
    /// Users can whitelist specific paths, folders, extensions, and regex patterns.
    /// Stored in the project so settings are shared across the team via version control. It's a nice to have, and really show how inclusive I am.
    /// </summary>
    [CreateAssetMenu(fileName = "ProjectCleanupWhitelist", menuName = "Tools/Project Cleanup Utility/Whitelist Config")]
    public class WhitelistConfig : ScriptableObject
    {
        [Header("Whitelisted Paths")]
        [Tooltip("Exact asset paths to exclude from unused detection.")]
        [SerializeField] private List<string> whitelistedPaths = new List<string>();

        [Header("Whitelisted Folders")]
        [Tooltip("All assets under these folder paths will be excluded.")]
        [SerializeField] private List<string> whitelistedFolders = new List<string>();

        [Header("Whitelisted Extensions")]
        [Tooltip("File extensions to exclude (e.g. '.cs', '.shader').")]
        [SerializeField] private List<string> whitelistedExtensions = new List<string>();

        [Header("Regex Patterns")]
        [Tooltip("Regex patterns matched against asset paths. Matching assets are excluded.")]
        [SerializeField] private List<string> regexPatterns = new List<string>();

        // Default folders to always exclude (Unity internals, packages, etc.)
        private static readonly string[] DefaultExcludedFolders = new[]
        {
            "Packages/",
            "Assets/Plugins/",
            "Assets/Editor Default Resources/",
            "Assets/StreamingAssets/",
            "Assets/Resources/"
        };

        // Default extensions to always exclude
        private static readonly string[] DefaultExcludedExtensions = new[]
        {
            ".cs",
            ".asmdef",
            ".asmref",
            ".dll"
        };

        // ---- Public API ----
        public IReadOnlyList<string> WhitelistedPaths => whitelistedPaths;
        public IReadOnlyList<string> WhitelistedFolders => whitelistedFolders;
        public IReadOnlyList<string> WhitelistedExtensions => whitelistedExtensions;
        public IReadOnlyList<string> RegexPatterns => regexPatterns;

        /// <summary>
        /// Checks if an asset path should be excluded from unused detection.
        /// </summary>
        public bool IsWhitelisted(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return true;

            // Check default exclusions first
            foreach (string folder in DefaultExcludedFolders)
            {
                if (assetPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            string extension = System.IO.Path.GetExtension(assetPath)?.ToLowerInvariant();

            foreach (string ext in DefaultExcludedExtensions)
            {
                if (string.Equals(extension, ext, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Check user-defined exact paths
            if (whitelistedPaths.Any(p =>
                string.Equals(p, assetPath, StringComparison.OrdinalIgnoreCase)))
                return true;

            // Check user-defined folders
            foreach (string folder in whitelistedFolders)
            {
                if (!string.IsNullOrEmpty(folder) &&
                    assetPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Check user-defined extensions
            if (!string.IsNullOrEmpty(extension) &&
                whitelistedExtensions.Any(e =>
                    string.Equals(e, extension, StringComparison.OrdinalIgnoreCase)))
                return true;

            // Check regex patterns
            foreach (string pattern in regexPatterns)
            {
                if (string.IsNullOrEmpty(pattern)) continue;
                try
                {
                    if (Regex.IsMatch(assetPath, pattern, RegexOptions.IgnoreCase))
                        return true;
                }
                catch (ArgumentException)
                {
                    // Invalid regex, skip it
                    Debug.LogWarning(
                        $"[Project Cleanup Utility] Invalid regex pattern: {pattern}");
                }
            }

            return false;
        }


        public void AddPath(string path)
        {
            if (!whitelistedPaths.Contains(path))
            {
                whitelistedPaths.Add(path);
                MarkDirty();
            }
        }

        public void RemovePath(string path)
        {
            if (whitelistedPaths.Remove(path))
                MarkDirty();
        }

        public void AddFolder(string folder)
        {
            if (!whitelistedFolders.Contains(folder))
            {
                whitelistedFolders.Add(folder);
                MarkDirty();
            }
        }

        public void RemoveFolder(string folder)
        {
            if (whitelistedFolders.Remove(folder))
                MarkDirty();
        }

        public void AddExtension(string extension)
        {
            if (!extension.StartsWith("."))
                extension = "." + extension;

            if (!whitelistedExtensions.Contains(extension))
            {
                whitelistedExtensions.Add(extension);
                MarkDirty();
            }
        }

        public void RemoveExtension(string extension)
        {
            if (whitelistedExtensions.Remove(extension))
                MarkDirty();
        }

        public void AddRegexPattern(string pattern)
        {
            if (!regexPatterns.Contains(pattern))
            {
                regexPatterns.Add(pattern);
                MarkDirty();
            }
        }

        public void RemoveRegexPattern(string pattern)
        {
            if (regexPatterns.Remove(pattern))
                MarkDirty();
        }

        private void MarkDirty()
        {
            EditorUtility.SetDirty(this); // extremely dirty indeed
        }

        private static WhitelistConfig _cachedInstance;

        /// <summary>
        /// Finds the first WhitelistConfig in the project, or creates one if none exists.
        /// </summary>
        public static WhitelistConfig GetOrCreateConfig()
        {
            if (_cachedInstance != null) return _cachedInstance;

            // Search for existing config
            string[] guids = AssetDatabase.FindAssets("t:WhitelistConfig");

            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _cachedInstance = AssetDatabase.LoadAssetAtPath<WhitelistConfig>(path);
                if (_cachedInstance != null) return _cachedInstance;
            }

            // Create new config
            _cachedInstance = CreateInstance<WhitelistConfig>();
            const string configPath = "Assets/Editor/ProjectCleanupWhitelist.asset";

            // Ensure directory exists
            string dir = System.IO.Path.GetDirectoryName(configPath);

            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            AssetDatabase.CreateAsset(_cachedInstance, configPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Project Cleanup Utility] Created whitelist config at: {configPath}");

            return _cachedInstance;
        }
    }
}
