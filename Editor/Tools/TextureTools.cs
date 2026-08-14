using System;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for configuring texture asset import settings (TextureType, NormalMap, Sprite, max size, sRGB, Read/Write).
    /// </summary>
    public class ConfigureTextureSettingsTool : McpToolBase
    {
        public ConfigureTextureSettingsTool()
        {
            Name = "configure_texture_settings";
            Description = "Configures texture asset import settings (Normal Map, Sprite, Default, Max Size, sRGB, Filter Mode, Wrap Mode, Read/Write).";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            string texturePath = parameters["texturePath"]?.ToObject<string>();
            string textureTypeStr = parameters["textureType"]?.ToObject<string>()?.ToLowerInvariant();
            int? maxSize = parameters["maxTextureSize"]?.ToObject<int?>();
            string wrapModeStr = parameters["wrapMode"]?.ToObject<string>()?.ToLowerInvariant();
            string filterModeStr = parameters["filterMode"]?.ToObject<string>()?.ToLowerInvariant();
            bool? sRGB = parameters["sRGBTexture"]?.ToObject<bool?>();
            bool? isReadable = parameters["isReadable"]?.ToObject<bool?>();
            bool? generateMipMaps = parameters["generateMipMaps"]?.ToObject<bool?>();
            string compressionStr = parameters["compression"]?.ToObject<string>()?.ToLowerInvariant();
            string reason = parameters["reason"]?.ToObject<string>();

            if (string.IsNullOrWhiteSpace(texturePath))
            {
                return McpUnitySocketHandler.CreateErrorResponse("Parameter 'texturePath' is required.", "validation_error");
            }

            // Resolve path
            texturePath = texturePath.Replace('\\', '/').Trim();
            if (!texturePath.StartsWith("Assets/") && !texturePath.StartsWith("Packages/"))
            {
                string[] guids = AssetDatabase.FindAssets($"{texturePath} t:Texture2D");
                if (guids.Length > 0)
                {
                    texturePath = AssetDatabase.GUIDToAssetPath(guids[0]);
                }
                else
                {
                    texturePath = "Assets/" + texturePath.TrimStart('/');
                }
            }

            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse($"Texture asset or TextureImporter not found at path '{texturePath}'.", "asset_not_found");
            }

            // Texture type
            if (!string.IsNullOrEmpty(textureTypeStr))
            {
                switch (textureTypeStr)
                {
                    case "normalmap":
                    case "normal_map":
                    case "normal":
                        importer.textureType = TextureImporterType.NormalMap;
                        importer.sRGBTexture = false;
                        break;
                    case "sprite":
                    case "sprite2d":
                        importer.textureType = TextureImporterType.Sprite;
                        break;
                    case "cookie":
                        importer.textureType = TextureImporterType.Cookie;
                        break;
                    case "gui":
                    case "ui":
                        importer.textureType = TextureImporterType.GUI;
                        break;
                    case "singlechannel":
                    case "mask":
                        importer.textureType = TextureImporterType.SingleChannel;
                        importer.sRGBTexture = false;
                        break;
                    case "default":
                    default:
                        importer.textureType = TextureImporterType.Default;
                        break;
                }
            }

            if (maxSize.HasValue)
            {
                importer.maxTextureSize = Mathf.ClosestPowerOfTwo(maxSize.Value);
            }

            if (sRGB.HasValue)
            {
                importer.sRGBTexture = sRGB.Value;
            }

            if (isReadable.HasValue)
            {
                importer.isReadable = isReadable.Value;
            }

            if (generateMipMaps.HasValue)
            {
                importer.mipmapEnabled = generateMipMaps.Value;
            }

            if (!string.IsNullOrEmpty(wrapModeStr))
            {
                switch (wrapModeStr)
                {
                    case "clamp": importer.wrapMode = TextureWrapMode.Clamp; break;
                    case "mirror": importer.wrapMode = TextureWrapMode.Mirror; break;
                    case "mirroronce": importer.wrapMode = TextureWrapMode.MirrorOnce; break;
                    case "repeat":
                    default: importer.wrapMode = TextureWrapMode.Repeat; break;
                }
            }

            if (!string.IsNullOrEmpty(filterModeStr))
            {
                switch (filterModeStr)
                {
                    case "point": importer.filterMode = FilterMode.Point; break;
                    case "trilinear": importer.filterMode = FilterMode.Trilinear; break;
                    case "bilinear":
                    default: importer.filterMode = FilterMode.Bilinear; break;
                }
            }

            if (!string.IsNullOrEmpty(compressionStr))
            {
                switch (compressionStr)
                {
                    case "uncompressed":
                    case "none":
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        break;
                    case "highquality":
                    case "hq":
                        importer.textureCompression = TextureImporterCompression.CompressedHQ;
                        break;
                    case "lowquality":
                    case "lq":
                        importer.textureCompression = TextureImporterCompression.CompressedLQ;
                        break;
                    case "compressed":
                    default:
                        importer.textureCompression = TextureImporterCompression.Compressed;
                        break;
                }
            }

            importer.SaveAndReimport();

            McpLogger.LogInfo($"[MCP Unity] Configured TextureImporter for '{texturePath}' (type: {importer.textureType}, maxSize: {importer.maxTextureSize})" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully updated texture import settings for '{texturePath}'",
                ["texturePath"] = texturePath,
                ["textureType"] = importer.textureType.ToString(),
                ["maxTextureSize"] = importer.maxTextureSize,
                ["wrapMode"] = importer.wrapMode.ToString(),
                ["filterMode"] = importer.filterMode.ToString(),
                ["sRGBTexture"] = importer.sRGBTexture,
                ["isReadable"] = importer.isReadable,
                ["mipmapEnabled"] = importer.mipmapEnabled
            };
        }
    }
}
