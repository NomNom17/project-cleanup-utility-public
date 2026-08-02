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

using System.Threading.Tasks;
using UnityEditor.VersionControl;

namespace ProjectCleanupUtility.Core
{
    /// <summary>
    /// Bridges <see cref="UnityEditor.VersionControl.Task"/> - Unity's own version-control task type, which predates .NET's async/await and is not directly awaitable - into a real <see cref="System.Threading.Tasks.Task"/> so version-control calls can be awaited like any other asynchronous operation.
    /// </summary>
    /// <remarks>
    /// <see cref="UnityEditor.VersionControl.Task"/> only exposes a blocking <c>Wait()</c> method and a <c>SetCompletionAction</c> hook that accepts a fixed <see cref="CompletionAction"/> enum value, not an arbitrary completion callback. To avoid freezing the Editor UI thread, the blocking <c>Wait()</c> call is pushed onto a background thread pool thread via <see cref="System.Threading.Tasks.Task.Run(System.Action)"/>, and the resulting <see cref="System.Threading.Tasks.Task"/> - which genuinely supports <c>await</c> - is returned.
    /// </remarks>
    internal static class VcsTaskExtensions
    {
        /// <summary>
        /// Waits for a <see cref="UnityEditor.VersionControl.Task"/> to complete without blocking the calling (Editor UI) thread, by running the blocking <see cref="UnityEditor.VersionControl.Task.Wait"/> call on a background thread pool thread.
        /// </summary>
        /// <param name="vcsTask">The version-control task returned by a <c>Provider</c> method (e.g. <c>Provider.Delete</c>, <c>Provider.Checkout</c>, <c>Provider.Status</c>).</param>
        /// <returns>A <see cref="System.Threading.Tasks.Task"/> that completes once <paramref name="vcsTask"/> finishes, and can be awaited directly.</returns>
        public static System.Threading.Tasks.Task WaitAsync(this UnityEditor.VersionControl.Task vcsTask)
        {
            return System.Threading.Tasks.Task.Run(() => vcsTask.Wait());
        }
    }
}
