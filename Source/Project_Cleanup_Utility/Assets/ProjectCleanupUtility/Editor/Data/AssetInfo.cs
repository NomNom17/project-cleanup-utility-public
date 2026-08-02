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

// Unity serialisation reference: https://docs.unity3d.com/ScriptReference/Serializable.html

namespace ProjectCleanupUtility.Data
{
    /// <summary>
    /// Represents detailed information about a single asset in the project.
    /// Used as the data model for the asset list view and dependency graph.
    /// </summary>
    [Serializable]
    public class AssetInfo
    {
        /// <summary>
        /// <see langword="string"/> File Path of the asset
        /// </summary>
        public string Path { get; set; }
        /// <summary>
        /// <see langword="string"/> File name of the asset
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// <see langword="string"/> Unity generated GUID of the asset
        /// </summary>
        public string GUID { get; set; }

        /// <summary>
        /// <see langword="string"/> File extension of the asset
        /// </summary>
        public string Extension { get; set; }
        /// <summary>
        /// <see cref="AssetCategory"/> of the asset
        /// </summary>
        public AssetCategory Category { get; set; }
        /// <summary>
        /// The size of the asset in bytes (<see langword="long"/>)
        /// </summary>
        public long SizeBytes { get; set; }

        /// <summary>
        /// <see cref="List{T}"/> of dependencies in <see langword="string"/>.
        /// </summary>
        public List<string> DependsOn { get; set; } = new List<string>();
        /// <summary>
        /// <see cref="List{T}"/> of assets that are referencing this asset in <see langword="string"/>
        /// </summary>
        public List<string> ReferencedBy { get; set; } = new List<string>();

        /// <summary>
        /// Has the asset been whitelisted from being scanned and deleted?
        /// </summary>
        public bool IsWhitelisted { get; set; }
        /// <summary>
        /// Has the asset been quarantined?
        /// </summary>
        public bool IsQuarantined { get; set; }
        /// <summary>
        /// Original file path of the quarantined file
        /// </summary>
        public string QuarantineOriginalPath { get; set; }

        /// <summary>
        /// Computed <see cref="DeletionSafety"/> rating. Set by the scanner after analysis. Set as <see cref="DeletionSafety.Unknown"/> by default.
        /// </summary>
        public DeletionSafety Safety { get; set; } = DeletionSafety.Unknown;

        /// <summary>
        /// Human-readable file size string (e.g. "1.5 MB") instead of printing the size in bytes (overstimulation-inducing).
        /// </summary>
        public string SizeFormatted => FormatBytes(SizeBytes);

        /// <summary>
        /// Number of assets (<see langword="int"/>) that reference this one.
        /// </summary>
        public int ReferenceCount => ReferencedBy?.Count ?? 0;

        /// <summary>
        /// Number of assets (<see langword="int"/>) this one depends on.
        /// </summary>
        public int DependencyCount => DependsOn?.Count ?? 0;

        /// <summary>
        /// Whether the file on disk has the read-only OS attribute set.
        /// If true, the quarantine operation will need to strip this flag before moving the file - which it does automatically, but surfacing it here means you can see at a glance which assets are going to need that extra step without having to stare at the console output afterwards.
        /// </summary>
        public bool IsReadOnly { get; set; }

        /// <summary>
        /// Whether this asset is considered unused (no references and not whitelisted).
        /// </summary>
        public bool IsUnused => ReferenceCount == 0 && !IsWhitelisted;

        /// <summary>
        /// Perforce status for this asset. Null if VCS is not active or status has not been queried.
        /// </summary>
        public VcsStatus PerforceStatus { get; set; } = VcsStatus.Unknown;

        /// <summary>
        /// If the asset is checked out or locked by another user, their username is stored here.
        /// </summary>
        public string VcsOtherUser { get; set; }

        /// <summary>
        /// Whether this asset cannot be safely moved/deleted due to a VCS lock held by another user.
        /// </summary>
        public bool IsVcsBlocked => PerforceStatus == VcsStatus.LockedByOther;


        /// <summary>
        /// Formats bytes into KB, MB, GB, or TB, choosing the largest unit that keeps the value readable.
        /// </summary>
        /// <param name="bytes">The raw size in bytes to format.</param>
        /// <returns>A <see langword="string"/> containing formatted bytes into correct and simplified unit.</returns>
        private static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "Unknown";
            if (bytes == 0) return "0 B";

            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
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

        public override string ToString()
        {
            return $"[{Category}] {Name} ({SizeFormatted}) - Refs: {ReferenceCount}";
        }
    }

    /// <summary>
    /// Indicates how safe it is to delete an asset based on its references and the criticality of those references.
    /// </summary>
    public enum DeletionSafety
    {
        /// <summary>Not yet analysed.</summary>
        Unknown,

        /// <summary>
        /// No incoming references at all - nothing will break.
        /// </summary>
        Safe,

        /// <summary>
        /// Only referenced by ProjectSettings / BuildSettings (engine-level), not by other project assets. Deletion may affect project config.
        /// </summary>
        Caution,

        /// <summary>
        /// Referenced by other project assets. Deleting this will cause missing-reference errors in those assets.
        /// </summary>
        Unsafe
    }

    /// <summary>
    /// Broad categories for asset classification, used for filtering.
    /// </summary>
    public enum AssetCategory
    {
        Unknown,
        Texture,
        Material,
        Shader,
        Model,
        Animation,
        Audio,
        Prefab,
        Scene,
        Script,
        ScriptableObject,
        Font,
        Video,
        TextAsset,
        PhysicsMaterial,
        Lighting,
        /// <summary>USS files  - UI Toolkit style sheets.</summary>
        StyleSheet,
        /// <summary>UXML files  - UI Toolkit layout documents.</summary>
        UIDocument,
        Other
    }

    public enum VcsStatus
    {
        Unknown, // VCS not active or not yet queried
        Unversioned, // Not under version control
        UpToDate, // Synced, not checked out
        CheckedOutLocal, // Checked out by the current user
        CheckedOutOther, // Checked out by someone else (but not exclusively locked)
        LockedByOther, // Exclusively locked by another user — cannot move/delete
        OutOfDate, // Local version is behind depot head
        Added, // Scheduled for add (not yet submitted)
        Deleted, // Scheduled for delete
    }
}
