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

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanupUtility.UI
{
    /// <summary>
    /// Extracted from <c>ProjectCleanupWindow</c> as part of splitting the god-object into cooperating classes. Owns the accessibility *settings* - their EditorPrefs persistence and their application to the root VisualElement via USS class toggles / inline font-size - independent of the EditorWindow. This makes the settings logic reviewable/reasoned-about without needing an EditorWindow instance. <br><br></br></br>
    ///
    /// The accessibility *panel* intentionally stays on <c>ProjectCleanupWindow</c>, since building UI Toolkit VisualElements is exactly the "UI construction" concern the window is meant to own as the composition root - it simply calls into this controller's properties and Apply*/Save methods from its value-changed callbacks instead of manipulating EditorPrefs/USS classes directly.
    /// </summary>
    public class AccessibilityController
    {
        private const string PREF_COLOUR_BLIND_MODE = "ProjectCleanup_ColourBlindMode";
        private const string PREF_HIGH_CONTRAST = "ProjectCleanup_HighContrast";
        private const string PREF_UI_SCALE = "ProjectCleanup_UIScale";
        private const string PREF_FONT_SIZE_OFFSET = "ProjectCleanup_FontSizeOffset";

        private readonly VisualElement _root;

        /// <summary>0=Off, 1=Deuteranopia, 2=Protanopia, 3=Tritanopia</summary>
        public int ColourBlindMode { get; set; } = 0;

        public bool HighContrast { get; set; } = false;

        public float UIScale { get; set; } = 1.0f;

        /// <summary>-2 to +4</summary>
        public int FontSizeOffset { get; set; } = 0;

        /// <summary>
        /// </summary>
        /// <param name="root">The window's root VisualElement, which colour-blind/high-contrast USS classes and the font-size inline style are applied to.</param>
        public AccessibilityController(VisualElement root)
        {
            _root = root;
        }

        /// <summary>
        /// Loads accessibility preferences from EditorPrefs. Called as part of the window's overall <c>LoadPreferences</c>.
        /// </summary>
        public void LoadPreferences()
        {
            ColourBlindMode = EditorPrefs.GetInt(PREF_COLOUR_BLIND_MODE, 0);
            HighContrast = EditorPrefs.GetBool(PREF_HIGH_CONTRAST, false);
            UIScale = EditorPrefs.GetFloat(PREF_UI_SCALE, 1.0f);
            FontSizeOffset = EditorPrefs.GetInt(PREF_FONT_SIZE_OFFSET, 0);
        }

        /// <summary>
        /// Saves accessibility preferences to EditorPrefs. Called as part of the window's overall <c>SavePreferences</c>.
        /// </summary>
        public void SavePreferences()
        {
            EditorPrefs.SetInt(PREF_COLOUR_BLIND_MODE, ColourBlindMode);
            EditorPrefs.SetBool(PREF_HIGH_CONTRAST, HighContrast);
            EditorPrefs.SetFloat(PREF_UI_SCALE, UIScale);
            EditorPrefs.SetInt(PREF_FONT_SIZE_OFFSET, FontSizeOffset);
        }

        /// <summary>
        /// Applies all persisted accessibility settings at once. Equivalent to the three calls the window used to make individually right after <c>CreateGUI</c> finished building the tree (colour-blind mode, high contrast, then font size/UI scale).
        /// </summary>
        public void ApplyAll()
        {
            ApplyColourBlindMode();
            ApplyHighContrast();
            ApplyFontSizeOffset(); // also applies UI scale since both share font-size
        }

        /// <summary>
        /// Applies the selected colour-blind mode by swapping USS classes on the root element. Uses the Okabe-Ito palette - designed specifically for colour vision deficiency.
        /// </summary>
        public void ApplyColourBlindMode()
        {
            if (_root == null) return;

            // Remove all colour-blind classes first
            _root.RemoveFromClassList("cb-deuteranopia");
            _root.RemoveFromClassList("cb-protanopia");
            _root.RemoveFromClassList("cb-tritanopia");

            switch (ColourBlindMode)
            {
                case 1: _root.AddToClassList("cb-deuteranopia"); break;
                case 2: _root.AddToClassList("cb-protanopia"); break;
                case 3: _root.AddToClassList("cb-tritanopia"); break;
            }
        }

        /// <summary>
        /// Toggles high-contrast mode which increases border widths and contrast ratios. For when the default theme feels like reading grey text on a slightly different shade of grey background.
        /// </summary>
        public void ApplyHighContrast()
        {
            if (_root == null) return;

            if (HighContrast) _root.AddToClassList("high-contrast");
            else _root.RemoveFromClassList("high-contrast");
        }

        /// <summary>
        /// Applies a global font size offset to the root element via inline style, combined with the UI scale factor. USS inherits font-size down the tree, so setting it on root effectively scales all text. The scale factor (0.8-1.5) is multiplied into the base size so both controls work together without needing transform.scale.
        /// </summary>
        public void ApplyFontSizeOffset()
        {
            if (_root == null) return;

            // Default base font size in Unity editor is ~12px
            float baseFontSize = (12 + FontSizeOffset) * UIScale;
            baseFontSize = Mathf.Clamp(baseFontSize, 8, 24);
            _root.style.fontSize = Mathf.RoundToInt(baseFontSize);
        }

        /// <summary>
        /// Scales the entire UI by adjusting the root font size multiplier.
        /// UI Toolkit's <c>transform.scale</c> breaks layout bounds, so instead we use a combined font-size approach - the scale factor is folded into the font size calculation alongside the offset. This way users get a uniform size increase without the layout imploding.
        /// </summary>
        public void ApplyUIScale()
        {
            // Scale is applied via ApplyFontSizeOffset() which combines both the offset and the scale factor into a single font-size value.
            ApplyFontSizeOffset();
        }

        /// <summary>
        /// Formats a font size offset as a short human-readable label (e.g. "+2px", "Default").
        /// </summary>
        public static string FontSizeOffsetLabel(int offset)
        {
            if (offset == 0) return "Default";
            return offset > 0 ? $"+{offset}px" : $"{offset}px";
        }
    }
}
