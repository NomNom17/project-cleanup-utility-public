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

// Unity serialisation reference: https://docs.unity3d.com/ScriptReference/Serializable.html

namespace ProjectCleanupUtility.Data
{
    /// <summary>
    /// Holds the complete results of an asset scan, including statistics and categorised breakdowns for the UI to display.
    /// </summary>
    [Serializable]
    public class ScanResult
    {
        /// <summary>
        /// A <see cref="List{T}"/> of all assets found, stored as <see cref="AssetInfo"/>.
        /// </summary>
        public List<AssetInfo> AllAssets { get; set; } = new List<AssetInfo>();
        /// <summary>
        /// A <see cref="List{T}"/> of all unused/discarded assets found, stored as <see cref="AssetInfo"/>.
        /// </summary>
        public List<AssetInfo> UnusedAssets { get; set; } = new List<AssetInfo>();
        public DateTime ScanTimestamp { get; set; }
        public double ScanDurationSeconds { get; set; }


        public int TotalAssetCount => AllAssets?.Count ?? 0;
        public int UnusedAssetCount => UnusedAssets?.Count ?? 0;
        public long TotalSizeBytes => AllAssets?.Sum(a => a.SizeBytes) ?? 0;
        public long UnusedSizeBytes => UnusedAssets?.Sum(a => a.SizeBytes) ?? 0;

        /// <summary>
        /// Percentage of total project size that is unused, and how unlucky you are in life.
        /// </summary>
        public float UnusedPercentage =>
            TotalSizeBytes > 0 ? (float)UnusedSizeBytes / TotalSizeBytes * 100f : 0f;

        /// <summary>
        /// Returns unused assets grouped by their category, sorted by total size descending.
        /// </summary>
        public Dictionary<AssetCategory, List<AssetInfo>> UnusedByCategory()
        {
            if (UnusedAssets == null) return new Dictionary<AssetCategory, List<AssetInfo>>();

            return UnusedAssets
                .GroupBy(a => a.Category)
                .OrderByDescending(g => g.Sum(a => a.SizeBytes))
                .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.SizeBytes).ToList());
        }

        /// <summary>
        /// Returns a size breakdown by category for the statistics panel.
        /// </summary>
        public List<CategorySizeInfo> GetCategorySizeBreakdown()
        {
            if (UnusedAssets == null) return new List<CategorySizeInfo>();

            return UnusedAssets
                .GroupBy(a => a.Category)
                .Select(g => new CategorySizeInfo
                {
                    Category = g.Key,
                    Count = g.Count(),
                    TotalSizeBytes = g.Sum(a => a.SizeBytes)
                })
                .OrderByDescending(c => c.TotalSizeBytes)
                .ToList();
        }
    }

    /// <summary>
    /// Size information for a single asset category.
    /// </summary>
    [Serializable]
    public class CategorySizeInfo
    {
        public AssetCategory Category { get; set; }
        public int Count { get; set; }
        public long TotalSizeBytes { get; set; }

        public string SizeFormatted => FormatBytes(TotalSizeBytes);

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
    }
}
