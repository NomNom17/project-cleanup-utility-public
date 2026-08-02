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

using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanupUtility.UI
{
    /// <summary>
    /// The kind of toast notification to show, controlling its icon and colour.
    /// </summary>
    public enum ToastType
    {
        Success,
        Warning,
        Info,
        Error
    }

    /// <summary>
    /// Extracted from <c>ProjectCleanupWindow</c> as part of splitting the god-object into cooperating classes. Owns the toast container VisualElement and the toast notification stack/eviction/auto-dismiss logic that was previously implemented inline on the window (<c>BuildToastContainer</c>/<c>ShowToast</c>). <br></br><br></br>
    /// The window creates one instance, adds its container to the root via <see cref="Container"/>, and calls <see cref="Show"/> everywhere it used to call <c>ShowToast(...)</c>.
    /// </summary>
    public class ToastService
    {
        // Max toasts visible at once
        private const int MAX_VISIBLE_TOASTS = 3;

        private readonly VisualElement _toastContainer;

        /// <summary>
        /// The toast container VisualElement. The window is responsible for adding this to the root visual tree (equivalent to the old <c>BuildToastContainer</c> call site order).
        /// </summary>
        public VisualElement Container => _toastContainer;

        /// <summary>
        /// Builds the toast container that sits in the bottom-right, above the status bar.
        /// Toasts stack upward like a tiny carb-loaded skyscraper.
        /// </summary>
        public ToastService()
        {
            _toastContainer = new VisualElement();
            _toastContainer.AddToClassList("toast-container");
            _toastContainer.style.position = Position.Absolute;
            _toastContainer.style.bottom = 28; // above the status bar
            _toastContainer.style.right = 8;
            _toastContainer.style.width = 320;
            _toastContainer.style.flexDirection = FlexDirection.ColumnReverse;
            _toastContainer.pickingMode = PickingMode.Ignore;
        }

        /// <summary>
        /// Shows a toast notification that auto-dismisses after 3 seconds.
        /// Enforces a max of 3 visible toasts - oldest gets evicted like a bad tenant.
        /// </summary>
        public void Show(string message, ToastType type)
        {
            if (_toastContainer == null) return;

            // Evict oldest toast if we're at capacity
            while (_toastContainer.childCount >= MAX_VISIBLE_TOASTS)
            {
                _toastContainer.RemoveAt(_toastContainer.childCount - 1);
            }

            var toast = new VisualElement();
            toast.AddToClassList("toast");
            toast.style.flexDirection = FlexDirection.Row;
            toast.style.paddingLeft = 8;
            toast.style.paddingRight = 8;
            toast.style.paddingTop = 6;
            toast.style.paddingBottom = 6;
            toast.style.marginBottom = 4;
            toast.style.borderBottomLeftRadius = 4;
            toast.style.borderBottomRightRadius = 4;
            toast.style.borderTopLeftRadius = 4;
            toast.style.borderTopRightRadius = 4;

            // Type-specific styling
            string icon;
            Color bgColor;
            string typeClass;
            switch (type)
            {
                case ToastType.Success:
                    icon = "[OK]";
                    bgColor = new Color(0.2f, 0.45f, 0.2f, 0.9f);
                    typeClass = "toast-success";
                    break;
                case ToastType.Warning:
                    icon = "[!]";
                    bgColor = new Color(0.55f, 0.45f, 0.15f, 0.9f);
                    typeClass = "toast-warning";
                    break;
                case ToastType.Error:
                    icon = "[X]";
                    bgColor = new Color(0.55f, 0.2f, 0.2f, 0.9f);
                    typeClass = "toast-error";
                    break;
                default:
                    icon = "[i]";
                    bgColor = new Color(0.2f, 0.35f, 0.55f, 0.9f);
                    typeClass = "toast-info";
                    break;
            }

            toast.AddToClassList(typeClass);
            toast.style.backgroundColor = bgColor;

            var iconLabel = new Label(icon);
            iconLabel.style.marginRight = 6;
            iconLabel.style.fontSize = 14;
            toast.Add(iconLabel);

            var msgLabel = new Label(message);
            msgLabel.AddToClassList("toast-message");
            msgLabel.style.flexGrow = 1;
            msgLabel.style.fontSize = 11;
            msgLabel.style.color = new Color(0.9f, 0.9f, 0.9f);
            toast.Add(msgLabel);

            _toastContainer.Insert(0, toast);

            // Auto-dismiss after 3 seconds - your 15 minutes of fame, compressed
            toast.schedule.Execute(() =>
            {
                if (toast.parent != null)
                {
                    toast.parent.Remove(toast);
                }
            }).ExecuteLater(3000);
        }
    }
}
