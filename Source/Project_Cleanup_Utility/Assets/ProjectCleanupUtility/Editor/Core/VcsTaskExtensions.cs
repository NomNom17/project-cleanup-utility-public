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
