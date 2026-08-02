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
using System.Linq;
using ProjectCleanupUtility.Data;

// --- References ---
// AssetDatabase.GetDependencies (used by AssetScanner to populate DependsOn/ReferencedBy):
//   https://docs.unity3d.com/ScriptReference/AssetDatabase.GetDependencies.html

namespace ProjectCleanupUtility.Core
{
    /// <summary>
    /// Provides query methods over the dependency graph that was built during scanning.
    /// Supports both forward (what does this asset depend on?) and reverse (what depends on this asset?) lookups, as well as chain analysis for the dependency viewer.
    /// </summary>
    /// <remarks>
    ///  This is used to determine whether an asset can be classed as "safe", "caution" or "unsafe" to delete, giving the user a direct indication of the asset's deletion safety.
    /// </remarks>
    public class DependencyGraphBuilder
    {
        private readonly Dictionary<string, AssetInfo> _assetLookup;

        /// <summary>
        /// Constructs a <see cref="DependencyGraphBuilder"/> to enable building and querying dependency relationships between assets.
        /// </summary>
        /// <param name="allAssets">A list of <see cref="AssetInfo"/> representing all assets available for dependency analysis.</param>
        public DependencyGraphBuilder(List<AssetInfo> allAssets)
        {
            _assetLookup = new Dictionary<string, AssetInfo>(StringComparer.OrdinalIgnoreCase);

            if (allAssets != null)
            {
                foreach (AssetInfo asset in allAssets)
                {
                    _assetLookup[asset.Path] = asset;
                }
            }
        }

        /// <summary>
        /// Gets all assets that the given asset directly depends on by using <paramref name="assetPath"/>.
        /// </summary>
        /// <returns>A <see cref="List{T}"/> of <see cref="AssetInfo"/> containing direct dependencies.</returns>
        public List<AssetInfo> GetDirectDependencies(string assetPath)
        {
            if (!_assetLookup.TryGetValue(assetPath, out AssetInfo asset)) return new List<AssetInfo>();

            return asset.DependsOn
                .Where(p => _assetLookup.ContainsKey(p))
                .Select(p => _assetLookup[p])
                .ToList();
        }

        /// <summary>
        /// Gets all assets that directly reference (depend on) the given asset by using <paramref name="assetPath"/>.
        /// </summary>
        /// <returns>A <see cref="List{T}"/> of <see cref="AssetInfo"/> containing direct referencies.</returns>
        public List<AssetInfo> GetDirectReferences(string assetPath)
        {
            if (!_assetLookup.TryGetValue(assetPath, out AssetInfo asset)) return new List<AssetInfo>();

            return asset.ReferencedBy
                .Where(p => _assetLookup.ContainsKey(p))
                .Select(p => _assetLookup[p])
                .ToList();
        }

        /// <summary>
        /// Gets synthetic (non-asset) references for the given asset.
        /// These are entries like <c>"Script/Assets/...", "ProjectSettings/...", or "BuildSettings/..."</c> that were injected by the scanner but don't correspond to real asset paths in the project. Think of them as secret admirers  - they reference you, but they're not in the database.
        /// </summary>
        /// <returns>A list of synthetic reference label strings.</returns>
        public List<string> GetSyntheticReferences(string assetPath)
        {
            if (!_assetLookup.TryGetValue(assetPath, out AssetInfo asset))
                return new List<string>();

            return asset.ReferencedBy
                .Where(p => !_assetLookup.ContainsKey(p))
                .ToList();
        }

        /// <summary>
        /// Recursively finds all assets in the dependency chain (both forward and reverse), up to a specified depth, for visualisation purposes.
        /// </summary>
        /// <param name="assetPath">Starting asset path.</param>
        /// <param name="maxDepth">Maximum recursion depth (default 3).</param>
        /// <returns>A tree of <see cref="DependencyNode"/> objects.</returns>
        public DependencyNode BuildDependencyTree(string assetPath, int maxDepth = 3)
        {
            if (!_assetLookup.TryGetValue(assetPath, out AssetInfo rootAsset)) return null;

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return BuildNodeRecursive(rootAsset, 0, maxDepth, visited);
        }

        private DependencyNode BuildNodeRecursive(AssetInfo asset, int currentDepth, int maxDepth, HashSet<string> visited)
        {
            var node = new DependencyNode
            {
                Asset = asset,
                Depth = currentDepth
            };

            if (currentDepth >= maxDepth || !visited.Add(asset.Path)) return node;

            // Add forward dependencies (what this asset uses)
            foreach (string depPath in asset.DependsOn)
            {
                if (_assetLookup.TryGetValue(depPath, out AssetInfo dep) && !visited.Contains(depPath))
                    node.Dependencies.Add(BuildNodeRecursive(dep, currentDepth + 1, maxDepth, visited));
            }

            // Add reverse references (what uses this asset)
            foreach (string refPath in asset.ReferencedBy)
            {
                if (_assetLookup.TryGetValue(refPath, out AssetInfo refAsset) && !visited.Contains(refPath)) 
                    node.Referrers.Add(BuildNodeRecursive(refAsset, currentDepth + 1, maxDepth, visited));
            }

            return node;
        }

        /// <summary>
        /// Finds all "root" assets (scenes, prefabs, etc.) that transitively depend on the given asset. Useful for understanding why an asset is considered "used".
        /// </summary>
        public List<AssetInfo> FindRootReferences(string assetPath, int maxDepth = 10)
        {
            var roots = new List<AssetInfo>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            FindRootsRecursive(assetPath, maxDepth, 0, visited, roots);
            return roots;
        }

        private void FindRootsRecursive(string path, int maxDepth, int depth, HashSet<string> visited, List<AssetInfo> roots)
        {
            if (depth > maxDepth || !visited.Add(path)) return;

            if (!_assetLookup.TryGetValue(path, out AssetInfo asset)) return;

            // If this asset has no references, it is a root
            if (asset.ReferencedBy.Count == 0)
            {
                roots.Add(asset);
                return;
            }

            foreach (string refPath in asset.ReferencedBy)
            {
                FindRootsRecursive(refPath, maxDepth, depth + 1, visited, roots);
            }
        }
    }

    /// <summary>
    /// A node in the dependency tree, used for the dependency viewer UI.
    /// </summary>
    public class DependencyNode
    {
        public AssetInfo Asset { get; set; }
        public int Depth { get; set; }
        public List<DependencyNode> Dependencies { get; set; } = new List<DependencyNode>();
        public List<DependencyNode> Referrers { get; set; } = new List<DependencyNode>();
    }
}
