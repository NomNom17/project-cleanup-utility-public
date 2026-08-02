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
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace ProjectCleanupUtility.Core
{
    /// <summary>
    /// The types of actions you can regret. Missing from this list: career choices.
    /// </summary>
    public enum UndoActionType
    {
        Quarantine,
        Restore,
        Whitelist
    }

    /// <summary>
    /// Lightweight record of an asset involved in an undoable action.
    /// </summary>
    public class UndoAssetRecord
    {
        public string Path;
        public string GUID;
        public long SizeBytes;
    }

    /// <summary>
    /// Represents a single undoable action - think of it as a save point in a video game, except you only get one, and it expires when you do something else.
    /// </summary>
    public class UndoableAction
    {
        public UndoActionType Type;
        public List<UndoAssetRecord> Assets = new List<UndoAssetRecord>();
        public DateTime Timestamp;

        public string Description
        {
            get
            {
                string verb = Type switch
                {
                    UndoActionType.Quarantine => "Quarantined",
                    UndoActionType.Restore    => "Restored",
                    UndoActionType.Whitelist  => "Whitelisted",
                    _                         => "Modified"
                };

                return $"{verb} {Assets.Count} asset(s)";
            }
        }
    }

    /// <summary>
    /// Extracted from <c>ProjectCleanupWindow</c> as part of splitting the god-object into cooperating classes.
    /// Owns the single-slot undo bookkeeping (<see cref="UndoableAction"/>) and reversing a quarantine/restore/whitelist action via <see cref="QuarantineManager"/>/<see cref="WhitelistConfig"/>.
    /// </summary>
    public class UndoController
    {
        private readonly QuarantineManager _quarantineManager;

        private UndoableAction _lastUndoableAction;

        /// <summary>
        /// The currently recorded undoable action, or null if there is nothing to undo.
        /// </summary>
        public UndoableAction LastUndoableAction => _lastUndoableAction;

        /// <summary>
        /// Raised immediately after an action is recorded via <see cref="RecordUndoableAction"/>.
        /// </summary>
        public event Action<UndoableAction> OnActionRecorded;

        /// <summary>
        /// Raised after an undo has been applied. Carries a human-readable result message (for a success toast), the action type that was undone, and - when the undone action changed live-set membership (Quarantine/Restore, not Whitelist) - the affected AssetInfo list plus the incremental-update kind the window should apply via its existing ApplyIncrementalUpdate/IncrementalUpdateKind plumbing. For Whitelist undos, <c>affectedAssets</c> is still populated but the window does not need to run an incremental update for them (whitelist undo never touched _scanResult/_displayedAssets membership in the first place).
        /// </summary>
        public event Action<string, UndoActionType, List<AssetInfo>> OnUndoApplied;

        public UndoController(QuarantineManager quarantineManager)
        {
            _quarantineManager = quarantineManager;
        }

        /// <summary>
        /// Records an action so the user can undo it later.
        /// </summary>
        public void RecordUndoableAction(UndoActionType type, List<AssetInfo> assets)
        {
            _lastUndoableAction = new UndoableAction
            {
                Type = type,
                Timestamp = DateTime.Now,
                Assets = assets.Select(a => new UndoAssetRecord
                {
                    Path = a.Path,
                    GUID = a.GUID,
                    SizeBytes = a.SizeBytes
                }).ToList()
            };

            OnActionRecorded?.Invoke(_lastUndoableAction);
        }

        /// <summary>
        /// Reverses the last recorded action. Permanent deletes can't be undone - we're not wizards - and are therefore never recorded as undoable actions in the first place.
        /// </summary>
        public void OnUndoLastAction()
        {
            if (_lastUndoableAction == null) return;

            var action = _lastUndoableAction;
            var assetInfos = action.Assets.Select(r => new AssetInfo
            {
                Path = r.Path,
                GUID = r.GUID,
                SizeBytes = r.SizeBytes
            }).ToList();

            int count = 0;
            string message;
            switch (action.Type)
            {
                case UndoActionType.Quarantine:
                    count = _quarantineManager.RestoreAssets(assetInfos);
                    message = $"Undo: Restored {count} asset(s) from quarantine.";
                    break;

                case UndoActionType.Restore:
                    count = _quarantineManager.QuarantineAssets(assetInfos);
                    message = $"Undo: Re-quarantined {count} asset(s).";
                    break;

                case UndoActionType.Whitelist:
                    var config = WhitelistConfig.GetOrCreateConfig();
                    foreach (var asset in assetInfos)
                    {
                        config.RemovePath(asset.Path);
                        count++;
                    }
                    AssetDatabase.SaveAssets();
                    message = $"Undo: Removed {count} asset(s) from whitelist.";
                    break;

                default:
                    message = "Undo: Nothing to do.";
                    break;
            }

            // Clear the undo action
            _lastUndoableAction = null;

            OnUndoApplied?.Invoke(message, action.Type, assetInfos);
        }
    }
}
