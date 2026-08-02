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
using ProjectCleanupUtility.Data;

// Unity serialisation reference: https://docs.unity3d.com/ScriptReference/Serializable.html

namespace ProjectCleanupUtility.Utilities
{
    /// <summary>
    /// Resolves an asset's file extension to a broad <see cref="AssetCategory"/>.
    /// This centralises the mapping so it can be reused across the scanner and UI.
    /// </summary>
    public static class AssetCategoryResolver
    {
        private static readonly Dictionary<string, AssetCategory> ExtensionMap = new Dictionary<string, AssetCategory>(StringComparer.OrdinalIgnoreCase)
            {
                // Textures
                { ".png",  AssetCategory.Texture },
                { ".jpg",  AssetCategory.Texture },
                { ".jpeg", AssetCategory.Texture },
                { ".tga",  AssetCategory.Texture },
                { ".bmp",  AssetCategory.Texture },
                { ".psd",  AssetCategory.Texture },
                { ".tif",  AssetCategory.Texture },
                { ".tiff", AssetCategory.Texture },
                { ".gif",  AssetCategory.Texture },
                { ".exr",  AssetCategory.Texture },
                { ".hdr",  AssetCategory.Texture },
                { ".webp", AssetCategory.Texture },

                // Materials
                { ".mat", AssetCategory.Material },

                // Shaders
                { ".shader",       AssetCategory.Shader },
                { ".shadergraph",  AssetCategory.Shader },
                { ".shadersubgraph", AssetCategory.Shader },
                { ".hlsl",         AssetCategory.Shader },
                { ".cginc",        AssetCategory.Shader },
                { ".compute",      AssetCategory.Shader },

                // Models / Meshes
                { ".fbx",   AssetCategory.Model },
                { ".obj",   AssetCategory.Model },
                { ".blend", AssetCategory.Model },
                { ".dae",   AssetCategory.Model },
                { ".3ds",   AssetCategory.Model },
                { ".max",   AssetCategory.Model },
                { ".ma",    AssetCategory.Model },
                { ".mb",    AssetCategory.Model },
                { ".mesh",  AssetCategory.Model },
                { ".ply",   AssetCategory.Model },
                { ".stl",   AssetCategory.Model },
                { ".gltf",  AssetCategory.Model },
                { ".glb",   AssetCategory.Model },

                // Animations
                { ".anim",       AssetCategory.Animation },
                { ".controller", AssetCategory.Animation },
                { ".overrideController", AssetCategory.Animation },
                { ".mask",       AssetCategory.Animation },

                // Audio
                { ".wav",  AssetCategory.Audio },
                { ".mp3",  AssetCategory.Audio },
                { ".ogg",  AssetCategory.Audio },
                { ".aiff", AssetCategory.Audio },
                { ".aif",  AssetCategory.Audio },
                { ".flac", AssetCategory.Audio },
                { ".xm",   AssetCategory.Audio },
                { ".mod",  AssetCategory.Audio },
                { ".it",   AssetCategory.Audio },
                { ".s3m",  AssetCategory.Audio },

                // Prefabs
                { ".prefab", AssetCategory.Prefab },

                // Scenes
                { ".unity", AssetCategory.Scene },

                // Scripts
                { ".cs",      AssetCategory.Script },
                { ".asmdef",  AssetCategory.Script },
                { ".asmref",  AssetCategory.Script },
                { ".dll",     AssetCategory.Script },

                // ScriptableObjects
                { ".asset", AssetCategory.ScriptableObject },

                // Fonts
                { ".ttf",   AssetCategory.Font },
                { ".otf",   AssetCategory.Font },
                { ".fnt",   AssetCategory.Font },
                { ".fontsettings", AssetCategory.Font },

                // Video
                { ".mp4",  AssetCategory.Video },
                { ".mov",  AssetCategory.Video },
                { ".avi",  AssetCategory.Video },
                { ".webm", AssetCategory.Video },

                // Text Assets
                { ".txt",    AssetCategory.TextAsset },
                { ".json",   AssetCategory.TextAsset },
                { ".xml",    AssetCategory.TextAsset },
                { ".csv",    AssetCategory.TextAsset },
                { ".yaml",   AssetCategory.TextAsset },
                { ".yml",    AssetCategory.TextAsset },
                { ".bytes",  AssetCategory.TextAsset },
                { ".html",   AssetCategory.TextAsset },

                // Physics Materials
                { ".physicMaterial",   AssetCategory.PhysicsMaterial },
                { ".physicsMaterial",  AssetCategory.PhysicsMaterial },

                // Lighting
                { ".lighting",     AssetCategory.Lighting },
                { ".giparams",     AssetCategory.Lighting },
                { ".cubemap",      AssetCategory.Lighting },
                { ".flare",        AssetCategory.Lighting },
                { ".renderTexture", AssetCategory.Lighting },

                // UI Toolkit
                { ".uss",  AssetCategory.StyleSheet },
                { ".uxml", AssetCategory.UIDocument },
            };

        /// <summary>
        /// Resolves the given file extension to an <see cref="AssetCategory"/>.
        /// </summary>
        /// <param name="extension">File extension including the dot (e.g. ".png").</param>
        /// <returns>The resolved category, or <see cref="AssetCategory.Other"/> if unknown.</returns>
        public static AssetCategory Resolve(string extension)
        {
            if (string.IsNullOrEmpty(extension))
                return AssetCategory.Unknown;

            return ExtensionMap.TryGetValue(extension, out AssetCategory category)
                ? category
                : AssetCategory.Other;
        }

        /// <summary>
        /// Returns a user-friendly display name (<see langword="string"/>) for the category.
        /// </summary>
        public static string GetDisplayName(AssetCategory category)
        {
            return category switch
            {
                AssetCategory.Texture          => "Textures",
                AssetCategory.Material         => "Materials",
                AssetCategory.Shader           => "Shaders",
                AssetCategory.Model            => "Models / Meshes",
                AssetCategory.Animation        => "Animations",
                AssetCategory.Audio            => "Audio",
                AssetCategory.Prefab           => "Prefabs",
                AssetCategory.Scene            => "Scenes",
                AssetCategory.Script           => "Scripts",
                AssetCategory.ScriptableObject => "Scriptable Objects",
                AssetCategory.Font             => "Fonts",
                AssetCategory.Video            => "Video",
                AssetCategory.TextAsset        => "Text Assets",
                AssetCategory.PhysicsMaterial  => "Physics Materials",
                AssetCategory.Lighting         => "Lighting",
                AssetCategory.StyleSheet       => "Style Sheets",
                AssetCategory.UIDocument       => "UI Documents",
                AssetCategory.Other            => "Other",
                _                              => "Unknown"
            };
        }

        /// <summary>
        /// Returns a detailed description of what a specific file extension represents. Used for rich tooltips in the UI.
        /// </summary>
        /// <param name="extension">File extension including the dot.</param>
        /// <returns>A description <see langword="string"/>, or <c>null</c> if no specific info is available.</returns>
        public static string GetFileFormatDescription(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return null;

            return extension.ToLowerInvariant() switch
            {
                // Textures
                ".png"  => "PNG Image (Portable Network Graphics)  - Lossless compressed raster image",
                ".jpg"  => "JPEG Image  - Lossy compressed raster image",
                ".jpeg" => "JPEG Image  - Lossy compressed raster image",
                ".tga"  => "TGA Image (Targa)  - Uncompressed/RLE raster image",
                ".bmp"  => "BMP Image (Bitmap)  - Uncompressed raster image",
                ".psd"  => "PSD File (Adobe Photoshop Document)  - Layered image source file",
                ".tif"  => "TIFF Image  - High-quality raster image",
                ".tiff" => "TIFF Image  - High-quality raster image",
                ".gif"  => "GIF Image  - Animated/static indexed-colour image",
                ".exr"  => "EXR Image (OpenEXR)  - HDR floating-point image",
                ".hdr"  => "HDR Image  - High Dynamic Range radiance image",
                ".webp" => "WebP Image  - Modern lossy/lossless compressed image",

                // Materials
                ".mat"  => "Unity Material  - Defines surface rendering properties and shader assignment",

                // Shaders
                ".shader"       => "Unity Shader  - ShaderLab source code for GPU rendering",
                ".shadergraph"  => "Shader Graph  - Visual node-based shader",
                ".shadersubgraph" => "Shader Sub Graph  - Reusable shader graph fragment",
                ".hlsl"         => "HLSL Source  - High-Level Shading Language code",
                ".cginc"        => "CG Include  - Shared shader include file",
                ".compute"      => "Compute Shader  - GPU compute program",

                // Models
                ".fbx"   => "FBX Model (Autodesk)  - 3D model with mesh, skeleton, and animations",
                ".obj"   => "OBJ Model (Wavefront)  - Static 3D geometry",
                ".blend" => "Blender File  - Blender native 3D scene/model",
                ".dae"   => "COLLADA Model  - XML-based 3D asset interchange format",
                ".3ds"   => "3DS Model (3D Studio)  - Legacy 3D mesh format",
                ".gltf"  => "glTF Model  - GL Transmission Format 3D scene",
                ".glb"   => "GLB Model  - Binary glTF 3D model",

                // Animations
                ".anim"       => "Unity Animation Clip  - Keyframe animation data",
                ".controller" => "Animator Controller  - State machine for animation blending",
                ".overrideController" => "Animator Override Controller  - Swaps clips on an existing controller",
                ".mask"       => "Avatar Mask  - Defines which bones/body parts are affected by animation",

                // Audio
                ".wav"  => "WAV Audio (Waveform)  - Uncompressed PCM audio",
                ".mp3"  => "MP3 Audio  - Lossy compressed audio",
                ".ogg"  => "OGG Audio (Ogg Vorbis)  - Open-source lossy compressed audio",
                ".aiff" => "AIFF Audio  - Uncompressed audio (Apple)",
                ".aif"  => "AIFF Audio  - Uncompressed audio (Apple)",
                ".flac" => "FLAC Audio  - Lossless compressed audio",

                // Prefabs
                ".prefab" => "Unity Prefab  - Reusable GameObject template with components",

                // Scenes
                ".unity" => "Unity Scene  - Contains GameObjects, lighting, and environment data",

                // ScriptableObjects
                ".asset" => "Unity Asset  - Serialised ScriptableObject or imported asset data",

                // Fonts
                ".ttf" => "TrueType Font  - Scalable vector font",
                ".otf" => "OpenType Font  - Scalable vector font with advanced typographic features",

                // Video
                ".mp4"  => "MP4 Video  - H.264/H.265 compressed video",
                ".mov"  => "MOV Video (QuickTime)  - Apple video container",
                ".webm" => "WebM Video  - VP8/VP9 open video format",

                // Text
                ".txt"   => "Plain Text File",
                ".json"  => "JSON File  - JavaScript Object Notation data",
                ".xml"   => "XML File  - Extensible Markup Language data",
                ".csv"   => "CSV File  - Comma-Separated Values data",
                ".yaml"  => "YAML File  - Human-readable data serialisation",
                ".yml"   => "YAML File  - Human-readable data serialisation",

                // Input
                ".inputactions" => "Input Actions Asset  - Unity Input System action map definition",

                // Lighting
                ".cubemap"      => "Cubemap  - Six-sided environment/reflection map",
                ".flare"        => "Lens Flare  - Camera lens flare effect asset",
                ".renderTexture" => "Render Texture  - GPU-rendered texture target",

                // Wilt / Tutorial
                ".wlt" => "Window Layout  - Unity Editor window layout configuration",

                // USS
                ".uss" => "Unity Style Sheet  - UI Toolkit CSS-like stylesheet",

                // UXML
                ".uxml" => "UXML Document  - UI Toolkit XML layout definition",

                _ => null
            };
        }
    }
}
