// Headless adaptation of AnimeStudio.GUI/Exporter.cs; same format conversion algorithms.
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AnimeStudio.Automation
{
    public sealed class HeadlessExporter
    {
        public HeadlessExportOptions Options { get; }
        public List<string> Diagnostics { get; } = new List<string>();
        public HeadlessExporter(HeadlessExportOptions options) => Options = options;
        public bool ExportVirtualAsset(AssetItem item, string exportPath, ExportType exportType)
        {
            return exportType switch
            {
                ExportType.Convert => ExportVirtualConvertFile(item, exportPath),
                ExportType.Raw => ExportVirtualRawFile(item, exportPath),
                ExportType.Dump => ExportVirtualDumpFile(item, exportPath),
                ExportType.JSON => ExportVirtualJsonFile(item, exportPath),
                _ => false,
            };
        }

        private bool ExportVirtualConvertFile(AssetItem item, string exportPath)
        {
            switch (item.Type)
            {
                case ClassIDType.Texture2D:
                    return AFKJourneyUtils.DecodeDxtFile(item.ExternalPath, Path.Combine(exportPath, item.Text)) != null;
                case ClassIDType.TextAsset:
                    if (!TryExportFile(exportPath, item, ".json", out var textPath))
                        return false;
                    var text = item.VirtualContent ?? AFKJourneyUtils.DecryptJsoneToText(File.ReadAllBytes(item.ExternalPath));
                    File.WriteAllText(textPath, text);
                    return true;
                default:
                    return false;
            }
        }

        private bool ExportVirtualRawFile(AssetItem item, string exportPath)
        {
            var extension = Path.GetExtension(item.ExternalPath);
            if (!TryExportFile(exportPath, item, string.IsNullOrEmpty(extension) ? ".dat" : extension, out var rawPath))
                return false;
            File.Copy(item.ExternalPath, rawPath, overwrite: false);
            return true;
        }

        private bool ExportVirtualDumpFile(AssetItem item, string exportPath)
        {
            if (!TryExportFile(exportPath, item, ".txt", out var dumpPath))
                return false;
            var text = item.VirtualContent ?? item.InfoText ?? item.ExternalPath;
            File.WriteAllText(dumpPath, text);
            return true;
        }

        private bool ExportVirtualJsonFile(AssetItem item, string exportPath)
        {
            if (item.Type != ClassIDType.TextAsset)
            {
                return ExportVirtualDumpFile(item, exportPath);
            }

            if (!TryExportFile(exportPath, item, ".json", out var jsonPath))
                return false;
            var text = item.VirtualContent ?? AFKJourneyUtils.DecryptJsoneToText(File.ReadAllBytes(item.ExternalPath));
            File.WriteAllText(jsonPath, text);
            return true;
        }

        public bool ExportTexture2D(AssetItem item, string exportPath)
        {
            var m_Texture2D = (Texture2D)item.Asset;
            if (Options.convertTexture)
            {
                bool isHDR = m_Texture2D.m_TextureFormat == TextureFormat.RGBAHalf && Options.enableHDR;

                var type = Options.convertType;
                if (isHDR)
                {
                    type = ImageFormat.Hdr;
                }
                if (!TryExportFile(exportPath, item, "." + type.ToString().ToLower(), out var exportFullPath))
                    return false;

                if (isHDR)
                {
                    var buff = ArrayPool<Byte>.Shared.Rent((int)m_Texture2D.image_data.Size);
                    m_Texture2D.image_data.GetData(buff);

                    int outPutSize = m_Texture2D.m_Width * m_Texture2D.m_Height * 4;
                    var stream = ArrayPool<Half>.Shared.Rent(outPutSize);

                    for (var i = 0; i < outPutSize; i += 4)
                    {
                        stream[i] = Half.ToHalf(buff, i * 2);
                        stream[i + 1] = Half.ToHalf(buff, i * 2 + 2);
                        stream[i + 2] = Half.ToHalf(buff, i * 2 + 4);
                        stream[i + 3] = Half.ToHalf(buff, i * 2 + 6);
                    }

                    var flippedStream = ArrayPool<Half>.Shared.Rent(outPutSize);

                    for (int y = 0; y < m_Texture2D.m_Height; y++)
                    {
                        int rowSize = m_Texture2D.m_Width * 4;
                        int srcRowStart = y * rowSize;
                        int destRowStart = (m_Texture2D.m_Height - y - 1) * rowSize;

                        Array.Copy(stream, srcRowStart, flippedStream, destRowStart, rowSize);
                    }

                    String header =
                        "#?RADIANCE\n" +
                        "PRIMARIES=0 0 0 0 0 0 0 0\nFORMAT=32-bit_rle_rgbe\n\n" +
                        "-Y " + m_Texture2D.m_Height.ToString() + " +X " +
                        m_Texture2D.m_Width.ToString() + "\n";

                    using (var file = new BinaryWriter(File.OpenWrite(exportFullPath)))
                    {
                        file.Write(System.Text.Encoding.ASCII.GetBytes(header));

                        // from https://github.com/Opioid/rgbe/blob/master/encode.go
                        for (int i = 0; i != outPutSize; i += 4)
                        {
                            float maxComponent = Math.Max(flippedStream[i], System.Math.Max(flippedStream[i + 1], flippedStream[i + 2]));
                            byte[] rgbe = new byte[4];

                            if (maxComponent < 1e-32f)
                            {
                                rgbe[0] = rgbe[1] = rgbe[2] = rgbe[3] = 0;
                            } else
                            {
                                int exponent;
                                float mantissa = (float)Double.Frexp((double)maxComponent, out exponent);

                                mantissa *= 256.0f / maxComponent;

                                rgbe[0] = (byte)(flippedStream[i] * mantissa);
                                rgbe[1] = (byte)(flippedStream[i + 1] * mantissa);
                                rgbe[2] = (byte)(flippedStream[i + 2] * mantissa);

                                rgbe[3] = (byte)(exponent + 128);
                            }

                            foreach (byte channel in rgbe)
                            {
                                file.Write(channel);
                            }
                        }
                    }

                    ArrayPool<Byte>.Shared.Return(buff);
                    ArrayPool<Half>.Shared.Return(stream);
                    ArrayPool<Half>.Shared.Return(flippedStream);
                    return true;
                } else
                {
                    var image = m_Texture2D.ConvertToImage(true);

                    if (image == null)
                    {
                        return false;
                    }

                    using (image)
                    {
                        using (var file = File.OpenWrite(exportFullPath))
                        {
                            image.WriteToStream(file, type);
                        }

                        return true;
                    }
                }
            }
            else
            {
                if (!TryExportFile(exportPath, item, ".tex", out var exportFullPath))
                    return false;
                File.WriteAllBytes(exportFullPath, m_Texture2D.image_data.GetData());
                return true;
            }
        }

        public bool ExportAudioClip(AssetItem item, string exportPath)
        {
            var m_AudioClip = (AudioClip)item.Asset;
            var m_AudioData = m_AudioClip.m_AudioData.GetData();
            if (m_AudioData == null || m_AudioData.Length == 0)
                return false;
            var converter = new AudioClipConverter(m_AudioClip);
            if (Options.convertAudio && converter.IsSupport)
            {
                if (!TryExportFile(exportPath, item, ".wav", out var exportFullPath))
                    return false;
                var buffer = converter.ConvertToWav();
                if (buffer == null)
                    return false;
                File.WriteAllBytes(exportFullPath, buffer);
            }
            else
            {
                if (!TryExportFile(exportPath, item, converter.GetExtensionName(), out var exportFullPath))
                    return false;
                File.WriteAllBytes(exportFullPath, m_AudioData);
            }
            return true;
        }

        public bool ExportShader(AssetItem item, string exportPath)
        {
            if (!TryExportFile(exportPath, item, ".shader", out var exportFullPath))
                return false;
            var m_Shader = (Shader)item.Asset;
            if (m_Shader.m_SubProgramBlob == null && m_Shader.compressedBlob == null && m_Shader.m_Script == null) return false;
            var str = m_Shader.Convert();
            if (str != null)
                Diagnostics.AddRange(str.Split('\n').Where(line => line.Contains("disassembly error") || line.Contains("disassembly not supported") || line.Contains("decompile/disassembly error")).Select(line => line.Trim()).Distinct());
            if (string.IsNullOrEmpty(str)) return false;
            File.WriteAllText(exportFullPath, str);
            return true;
        }

        public bool ExportTextAsset(AssetItem item, string exportPath)
        {
            var m_TextAsset = (TextAsset)(item.Asset);
            var extension = ".txt";
            if (Options.restoreExtensionName)
            {
                if (!string.IsNullOrEmpty(item.Container))
                {
                    extension = Path.GetExtension(item.Container);
                }
            }
            if (!TryExportFile(exportPath, item, extension, out var exportFullPath))
                return false;
            File.WriteAllBytes(exportFullPath, m_TextAsset.m_Script);
            return true;
        }

        public bool ExportMonoBehaviour(AssetItem item, string exportPath)
        {
            if (!TryExportFile(exportPath, item, ".json", out var exportFullPath))
                return false;
            var m_MonoBehaviour = (MonoBehaviour)item.Asset;
            var type = m_MonoBehaviour.ToType();
            if (type == null)
            {
                var m_Type = Options.ResolveTypeTree?.Invoke(m_MonoBehaviour);
                type = m_MonoBehaviour.ToType(m_Type);
            }
            if (type == null) return false;
            var str = ExactJson.Serialize(type);
            File.WriteAllText(exportFullPath, str);
            return true;
        }

        public bool ExportMiHoYoBinData(AssetItem item, string exportPath)
        {
            string exportFullPath;
            if (item.Asset is MiHoYoBinData m_MiHoYoBinData)
            {
                switch (m_MiHoYoBinData.Type)
                {
                    case MiHoYoBinDataType.JSON:

                        if (!TryExportFile(exportPath, item, ".json", out exportFullPath))
                            return false;
                        var json = m_MiHoYoBinData.Dump() as string;
                        if (json.Length != 0)
                        {
                            File.WriteAllText(exportFullPath, json);
                            return true;
                        }
                        break;
                    case MiHoYoBinDataType.Bytes:
                        var extension = ".bin";
                        if (Options.restoreExtensionName)
                        {
                            if (!string.IsNullOrEmpty(item.Container))
                            {
                                extension = Path.GetExtension(item.Container);
                            }
                        }
                        if (!TryExportFile(exportPath, item, extension, out exportFullPath))
                            return false;
                        var bytes = m_MiHoYoBinData.Dump() as byte[];
                        if (!bytes.IsNullOrEmpty())
                        {
                            File.WriteAllBytes(exportFullPath, bytes);
                            return true;
                        }
                        break;
                }
            }
            return false;
        }

        public bool ExportFont(AssetItem item, string exportPath)
        {
            var m_Font = (Font)item.Asset;
            if (m_Font.m_FontData != null)
            {
                var extension = ".ttf";
                if (m_Font.m_FontData[0] == 79 && m_Font.m_FontData[1] == 84 && m_Font.m_FontData[2] == 84 && m_Font.m_FontData[3] == 79)
                {
                    extension = ".otf";
                }
                if (!TryExportFile(exportPath, item, extension, out var exportFullPath))
                    return false;
                File.WriteAllBytes(exportFullPath, m_Font.m_FontData);
                return true;
            }
            return false;
        }

        public bool ExportMesh(AssetItem item, string exportPath)
        {
            var m_Mesh = (Mesh)item.Asset;
            if (m_Mesh.m_VertexCount <= 0)
                return false;
            if (!TryExportFile(exportPath, item, ".obj", out var exportFullPath))
                return false;
            var sb = new StringBuilder();
            sb.AppendLine("g " + m_Mesh.m_Name);
            #region Vertices
            if (m_Mesh.m_Vertices == null || m_Mesh.m_Vertices.Length == 0)
            {
                return false;
            }
            int c = 3;
            if (m_Mesh.m_Vertices.Length == m_Mesh.m_VertexCount * 4)
            {
                c = 4;
            }
            for (int v = 0; v < m_Mesh.m_VertexCount; v++)
            {
                sb.AppendFormat("v {0} {1} {2}\r\n", -m_Mesh.m_Vertices[v * c], m_Mesh.m_Vertices[v * c + 1], m_Mesh.m_Vertices[v * c + 2]);
            }
            #endregion

            #region UV
            if (m_Mesh.m_UV0?.Length > 0)
            {
                c = 4;
                if (m_Mesh.m_UV0.Length == m_Mesh.m_VertexCount * 2)
                {
                    c = 2;
                }
                else if (m_Mesh.m_UV0.Length == m_Mesh.m_VertexCount * 3)
                {
                    c = 3;
                }
                for (int v = 0; v < m_Mesh.m_VertexCount; v++)
                {
                    sb.AppendFormat("vt {0} {1}\r\n", m_Mesh.m_UV0[v * c], m_Mesh.m_UV0[v * c + 1]);
                }
            }
            #endregion

            #region Normals
            if (m_Mesh.m_Normals?.Length > 0)
            {
                if (m_Mesh.m_Normals.Length == m_Mesh.m_VertexCount * 3)
                {
                    c = 3;
                }
                else if (m_Mesh.m_Normals.Length == m_Mesh.m_VertexCount * 4)
                {
                    c = 4;
                }
                for (int v = 0; v < m_Mesh.m_VertexCount; v++)
                {
                    sb.AppendFormat("vn {0} {1} {2}\r\n", -m_Mesh.m_Normals[v * c], m_Mesh.m_Normals[v * c + 1], m_Mesh.m_Normals[v * c + 2]);
                }
            }
            #endregion

            #region Face
            int sum = 0;
            for (var i = 0; i < m_Mesh.m_SubMeshes.Count; i++)
            {
                sb.AppendLine($"g {m_Mesh.m_Name}_{i}");
                int indexCount = (int)m_Mesh.m_SubMeshes[i].indexCount;
                var end = sum + indexCount / 3;
                for (int f = sum; f < end; f++)
                {
                    sb.AppendFormat("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}\r\n", m_Mesh.m_Indices[f * 3 + 2] + 1, m_Mesh.m_Indices[f * 3 + 1] + 1, m_Mesh.m_Indices[f * 3] + 1);
                }
                sum = end;
            }
            #endregion

            sb.Replace("NaN", "0");
            File.WriteAllText(exportFullPath, sb.ToString());
            return true;
        }

        public bool ExportVideoClip(AssetItem item, string exportPath)
        {
            var m_VideoClip = (VideoClip)item.Asset;
            if (m_VideoClip.m_ExternalResources.m_Size > 0)
            {
                if (!TryExportFile(exportPath, item, Path.GetExtension(m_VideoClip.m_OriginalPath), out var exportFullPath))
                    return false;
                m_VideoClip.m_VideoData.WriteData(exportFullPath);
                return true;
            }
            return false;
        }

        public bool ExportMovieTexture(AssetItem item, string exportPath)
        {
            var m_MovieTexture = (MovieTexture)item.Asset;
            if (!TryExportFile(exportPath, item, ".ogv", out var exportFullPath))
                return false;
            File.WriteAllBytes(exportFullPath, m_MovieTexture.m_MovieData);
            return true;
        }

        public bool ExportSprite(AssetItem item, string exportPath)
        {
            var type = Options.convertType;
            if (!TryExportFile(exportPath, item, "." + type.ToString().ToLower(), out var exportFullPath))
                return false;
            var image = ((Sprite)item.Asset).GetImage();
            if (image != null)
            {
                using (image)
                {
                    using (var file = File.OpenWrite(exportFullPath))
                    {
                        image.WriteToStream(file, type);
                    }
                    return true;
                }
            }
            return false;
        }

        public bool ExportRawFile(AssetItem item, string exportPath)
        {
            if (!TryExportFile(exportPath, item, ".dat", out var exportFullPath))
                return false;
            File.WriteAllBytes(exportFullPath, item.Asset.GetRawData());
            return true;
        }

        private bool TryExportFile(string dir, AssetItem item, string extension, out string fullPath)
        {
            var fileName = FixFileName(item.Text);
            fullPath = Path.Combine(dir, $"{fileName}{extension}");
            if (!File.Exists(fullPath))
            {
                Directory.CreateDirectory(dir);
                return true;
            }
            if (Options.allowDuplicates)
            {
                for (int i = 1; i < int.MaxValue; i++)
                {
                    fullPath = Path.Combine(dir, $"{fileName} ({i}){extension}");
                    if (!File.Exists(fullPath))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        private bool TryExportFolder(string dir, AssetItem item, out string fullPath)
        {
            var fileName = FixFileName(item.Text);
            fullPath = Path.Combine(dir, fileName);
            if (!Directory.Exists(fullPath))
            {
                return true;
            }
            if (Options.allowDuplicates)
            {
                for (int i = 1; i < int.MaxValue; i++)
                {
                    fullPath = Path.Combine(dir, $"{fileName} ({i})");
                    if (!Directory.Exists(fullPath))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        public bool ExportAnimationClip(AssetItem item, string exportPath)
        {
            if (!TryExportFile(exportPath, item, ".anim", out var exportFullPath))
                return false;
            var m_AnimationClip = (AnimationClip)item.Asset;
            var str = m_AnimationClip.Convert();
            if (string.IsNullOrEmpty(str))
                return false;
            File.WriteAllText(exportFullPath, str);
            return true;
        }

        public bool ExportAnimator(AssetItem item, string exportPath, List<AssetItem> animationList = null)
        {
            if (!TryExportFolder(exportPath, item, out var exportFullPath))
                return false;

            exportFullPath = Path.Combine(exportFullPath, FixFileName(item.Text) + ".fbx");
            var m_Animator = (Animator)item.Asset;
            var options = new ModelConverter.Options()
            {
                imageFormat = Options.convertType,
                game = Options.Game,
                collectAnimations = Options.collectAnimations,
                exportMaterials = Options.exportMaterials,
                materials = new HashSet<Material>(),
                uvs = Options.Uvs,
                texs = Options.Textures,
            };
            var convert = animationList != null
                ? new ModelConverter(m_Animator, options, animationList.Select(x => (AnimationClip)x.Asset).ToArray())
                : new ModelConverter(m_Animator, options);
            if (options.exportMaterials)
            {
                var materialExportPath = Path.Combine(Path.GetDirectoryName(exportFullPath), "Materials");
                Directory.CreateDirectory(materialExportPath);
                foreach (var material in options.materials)
                {
                    var matItem = new AssetItem(material);
                    ExportJSONFile(matItem, materialExportPath);
                }
            }
            ExportFbx(convert, exportFullPath);
            return true;
        }

        public bool ExportGameObject(AssetItem item, string exportPath, List <AssetItem> animationList = null)
        {
            if (!TryExportFolder(exportPath, item, out var exportFullPath))
                return false;

            var m_GameObject = (GameObject)item.Asset;
            return ExportGameObject(m_GameObject, exportFullPath + Path.DirectorySeparatorChar, animationList);
        }

        public bool ExportGameObject(GameObject gameObject, string exportPath, List<AssetItem> animationList = null)
        {
            var options = new ModelConverter.Options()
            {
                imageFormat = Options.convertType,
                game = Options.Game,
                collectAnimations = Options.collectAnimations,
                exportMaterials = Options.exportMaterials,
                materials = new HashSet<Material>(),
                uvs = Options.Uvs,
                texs = Options.Textures,
            };
            var convert = animationList != null
                ? new ModelConverter(gameObject, options, animationList.Select(x => (AnimationClip)x.Asset).ToArray())
                : new ModelConverter(gameObject, options);

            if (convert.MeshList.Count == 0)
            {
                Logger.Info($"GameObject {gameObject.m_Name} has no mesh, skipping...");
                return false;
            }
            if (options.exportMaterials)
            {
                var materialExportPath = Path.Combine(exportPath, "Materials");
                Directory.CreateDirectory(materialExportPath);
                foreach (var material in options.materials)
                {
                    var matItem = new AssetItem(material);
                    ExportJSONFile(matItem, materialExportPath);
                }
            }
            exportPath = exportPath + FixFileName(gameObject.m_Name) + ".fbx";
            ExportFbx(convert, exportPath);
            return true;
        }

        public void ExportGameObjectMerge(List<GameObject> gameObject, string exportPath, List<AssetItem> animationList = null)
        {
            var rootName = Path.GetFileNameWithoutExtension(exportPath);
            var options = new ModelConverter.Options()
            {
                imageFormat = Options.convertType,
                game = Options.Game,
                collectAnimations = Options.collectAnimations,
                exportMaterials = Options.exportMaterials,
                materials = new HashSet<Material>(),
                uvs = Options.Uvs,
                texs = Options.Textures,
            };
            var convert = animationList != null
                ? new ModelConverter(rootName, gameObject, options, animationList.Select(x => (AnimationClip)x.Asset).ToArray())
                : new ModelConverter(rootName, gameObject, options);
            if (options.exportMaterials)
            {
                var materialExportPath = Path.Combine(Path.GetDirectoryName(exportPath), "Materials");
                Directory.CreateDirectory(materialExportPath);
                foreach (var material in options.materials)
                {
                    var matItem = new AssetItem(material);
                    ExportJSONFile(matItem, materialExportPath);
                }
            }
            ExportFbx(convert, exportPath);
        }

        private void ExportFbx(IImported convert, string exportPath)
        {
            var exportOptions = new Fbx.ExportOptions()
            {
                eulerFilter = Options.eulerFilter,
                filterPrecision = (float)Options.filterPrecision,
                exportAllNodes = Options.exportAllNodes,
                exportSkins = Options.exportSkins,
                exportAnimations = Options.exportAnimations,
                exportBlendShape = Options.exportBlendShape,
                castToBone = Options.castToBone,
                boneSize = (int)Options.boneSize,
                scaleFactor = (float)Options.scaleFactor,
                fbxVersion = Options.fbxVersion,
                fbxFormat = Options.fbxFormat
            };
            var previous = Directory.GetCurrentDirectory();
            var directory = Path.GetDirectoryName(Path.GetFullPath(exportPath));
            Directory.CreateDirectory(directory);
            try
            {
                Directory.SetCurrentDirectory(directory);
                ModelExporter.ExportFbx(exportPath, convert, exportOptions);
            }
            finally { Directory.SetCurrentDirectory(previous); }
        }

        public bool ExportDumpFile(AssetItem item, string exportPath)
        {
            if (!TryExportFile(exportPath, item, ".txt", out var exportFullPath))
                return false;
            var str = item.Asset.Dump();
            if (str == null && item.Asset is MonoBehaviour m_MonoBehaviour)
            {
                var m_Type = Options.ResolveTypeTree?.Invoke(m_MonoBehaviour);
                str = m_MonoBehaviour.Dump(m_Type);
            }
            if (str != null)
            {
                File.WriteAllText(exportFullPath, str);
                return true;
            }
            return false;
        }

        public bool ExportConvertFile(AssetItem item, string exportPath)
        {
            switch (item.Type)
            {
                case ClassIDType.GameObject:
                    return ExportGameObject(item, exportPath);
                case ClassIDType.Texture2D:
                    return ExportTexture2D(item, exportPath);
                case ClassIDType.AudioClip:
                    return ExportAudioClip(item, exportPath);
                case ClassIDType.Shader:
                    return ExportShader(item, exportPath);
                case ClassIDType.TextAsset:
                    return ExportTextAsset(item, exportPath);
                case ClassIDType.MonoBehaviour:
                    return ExportMonoBehaviour(item, exportPath);
                case ClassIDType.Font:
                    return ExportFont(item, exportPath);
                case ClassIDType.Mesh:
                    return ExportMesh(item, exportPath);
                case ClassIDType.VideoClip:
                    return ExportVideoClip(item, exportPath);
                case ClassIDType.MovieTexture:
                    return ExportMovieTexture(item, exportPath);
                case ClassIDType.Sprite:
                    return ExportSprite(item, exportPath);
                case ClassIDType.Animator:
                    return ExportAnimator(item, exportPath);
                case ClassIDType.AnimationClip:
                    return ExportAnimationClip(item, exportPath);
                case ClassIDType.MiHoYoBinData:
                    return ExportMiHoYoBinData(item, exportPath);
                case ClassIDType.Material:
                    return ExportJSONFile(item, exportPath);
                case ClassIDType.NapAssetBundleIndexAsset:
                    return ExportJSONFile(item, exportPath);
                default:
                    return ExportRawFile(item, exportPath);
            }
        }

        public bool ExportJSONFile(AssetItem item, string exportPath)
        {
            if (!TryExportFile(exportPath, item, ".json", out var exportFullPath))
                return false;

            var token = Newtonsoft.Json.Linq.JObject.Parse(ExactJson.Serialize(item.Asset));
            token["m_PathID"] = item.m_PathID.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var str = token.ToString(Formatting.Indented);
            File.WriteAllText(exportFullPath, str);
            return true;
        }

        public string FixFileName(string str)
        {
            str = string.IsNullOrWhiteSpace(str) ? "asset" : str;
            str = Path.GetInvalidFileNameChars().Aggregate(str, (current, c) => current.Replace(c, '_')).TrimEnd('.', ' ');
            if (str.Length == 0 || str == "." || str == "..") str = "asset";
            if (str.Length > 120) str = str.Substring(0, 120);
            return "asset_" + str;
        }
    }
}
