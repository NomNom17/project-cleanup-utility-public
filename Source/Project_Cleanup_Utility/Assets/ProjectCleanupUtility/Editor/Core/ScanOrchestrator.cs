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

using ProjectCleanupUtility.Data;
using System;
using System.Threading.Tasks;

namespace ProjectCleanupUtility.Core
{
    /// <summary>
    /// Extracted from <c>ProjectCleanupWindow</c> as part of splitting the god-object into cooperating classes.
    /// Owns the <see cref="AssetScanner"/> and <see cref="QuarantineManager"/> instances and their event subscription lifecycle - just moved out of the EditorWindow so the window no longer has to know about wiring these two services together. <br></br>
    ///
    /// The window calls <see cref="Enable"/> from its own <c>OnEnable</c> and <see cref="Disable"/> from its own <c>OnDisable</c>. It subscribes to this orchestrator's re-raised events (<see cref="OnProgressUpdated"/>, <see cref="OnScanComplete"/>, <see cref="OnScanError"/>, <see cref="OnQuarantineStatusMessage"/>, <see cref="OnQuarantineError"/>) instead of the scanner/quarantine manager's events directly, but the underlying event flow, ordering, and payloads are unchanged. <br></br>
    ///
    /// Scan-triggering (<see cref="Scan"/>) is a thin async wrapper around <see cref="AssetScanner.Scan"/> - the caller (window) still owns deciding when to call it, showing/hiding the progress overlay, and everything it does with the resulting <see cref="ScanResult"/>.
    /// </summary>
    public class ScanOrchestrator
    {
        private AssetScanner _scanner;
        private QuarantineManager _quarantineManager;

        /// <summary>The owned <see cref="AssetScanner"/> instance, created in <see cref="Enable"/>.</summary>
        public AssetScanner Scanner => _scanner;

        /// <summary>The owned <see cref="QuarantineManager"/> instance, created in <see cref="Enable"/>.</summary>
        public QuarantineManager QuarantineManager => _quarantineManager;

        /// <summary>Re-raised from <see cref="AssetScanner.OnProgressUpdated"/>.</summary>
        public event Action<string, float> OnProgressUpdated;

        /// <summary>Re-raised from <see cref="AssetScanner.OnScanComplete"/>.</summary>
        public event Action<ScanResult> OnScanComplete;

        /// <summary>Re-raised from <see cref="AssetScanner.OnScanError"/>.</summary>
        public event Action<string> OnScanError;

        /// <summary>Re-raised from <see cref="QuarantineManager.OnStatusMessage"/>.</summary>
        public event Action<string> OnQuarantineStatusMessage;

        /// <summary>Re-raised from <see cref="QuarantineManager.OnError"/>.</summary>
        public event Action<string> OnQuarantineError;

        /// <summary>
        /// Creates the <see cref="AssetScanner"/>/<see cref="QuarantineManager"/> instances and subscribes to their events exactly once for the lifetime of the window, rather than re-subscribing on every scan/quarantine click. Mirrors the previous <c>ProjectCleanupWindow.OnEnable</c> body.
        /// </summary>
        public void Enable()
        {
            _scanner = new AssetScanner();
            _quarantineManager = new QuarantineManager();

            _scanner.OnProgressUpdated += HandleScanProgress;
            _scanner.OnScanComplete += HandleScanComplete;
            _scanner.OnScanError += HandleScanError;
            _quarantineManager.OnStatusMessage += HandleQuarantineStatusMessage;
            _quarantineManager.OnError += HandleQuarantineError;
        }

        /// <summary>
        /// Unsubscribes from scanner/quarantine manager events to avoid leaking handlers (and stale references to the owning window) across domain reloads. Mirrors the previous <c>ProjectCleanupWindow.OnDisable</c> body.
        /// </summary>
        public void Disable()
        {
            if (_scanner != null)
            {
                _scanner.OnProgressUpdated -= HandleScanProgress;
                _scanner.OnScanComplete -= HandleScanComplete;
                _scanner.OnScanError -= HandleScanError;
            }

            if (_quarantineManager != null)
            {
                _quarantineManager.OnStatusMessage -= HandleQuarantineStatusMessage;
                _quarantineManager.OnError -= HandleQuarantineError;
            }
        }

        /// <summary>
        /// Runs a full project scan. Thin wrapper around <see cref="AssetScanner.Scan"/> so the window's call site reads identically to before (<c>await Scan(whitelist)</c>) while the scanner instance itself lives here rather than on the window.
        /// </summary>
        public Task<ScanResult> Scan(WhitelistConfig whitelist = null)
        {
            return _scanner.Scan(whitelist);
        }

        private void HandleScanProgress(string msg, float progress) => OnProgressUpdated?.Invoke(msg, progress);

        private void HandleScanComplete(ScanResult result) => OnScanComplete?.Invoke(result);

        private void HandleScanError(string message) => OnScanError?.Invoke(message);

        private void HandleQuarantineStatusMessage(string message) => OnQuarantineStatusMessage?.Invoke(message);

        private void HandleQuarantineError(string message) => OnQuarantineError?.Invoke(message);
    }
}
