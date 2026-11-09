using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AnimeStudio.Automation;

public sealed class HeadlessExportOptions
{
    public Game Game { get; set; } = new Game(GameType.Normal, "Unity", GameCategory.Unity);
    public Func<MonoBehaviour, TypeTree> ResolveTypeTree { get; set; }
    public bool convertTexture { get; set; } = true;
    public bool enableHDR { get; set; }
    public ImageFormat convertType { get; set; } = ImageFormat.Png;
    public bool convertAudio { get; set; } = true;
    public bool restoreExtensionName { get; set; } = true;
    public bool allowDuplicates { get; set; } = false;
    public bool collectAnimations { get; set; } = false;
    public bool exportMaterials { get; set; } = true;
    public bool eulerFilter { get; set; } = false;
    public float filterPrecision { get; set; } = 0.25f;
    public bool exportAllNodes { get; set; } = true;
    public bool exportSkins { get; set; } = true;
    public bool exportAnimations { get; set; } = true;
    public bool exportBlendShape { get; set; } = true;
    public bool castToBone { get; set; }
    public int boneSize { get; set; } = 1;
    public float scaleFactor { get; set; } = 1;
    public int fbxVersion { get; set; } = 3;
    public int fbxFormat { get; set; } = 0;
    public Dictionary<string, (bool, int)> Uvs { get; set; } = new()
    {
        ["UV0"] = (true, 0), ["UV1"] = (true, 1), ["UV2"] = (true, 2), ["UV3"] = (true, 3),
        ["UV4"] = (true, 4), ["UV5"] = (true, 5), ["UV6"] = (true, 6), ["UV7"] = (true, 7),
    };
    public Dictionary<string, int> Textures { get; set; } = new();
}

/// <summary>A data-only counterpart of the GUI AssetItem. It has no image hashing or UI side effects.</summary>
public sealed class AssetItem
{
    public Object Asset;
    public string Text;
    public string Container = "";
    public ClassIDType Type;
    public long m_PathID;
    public string ExternalPath;
    public string VirtualContent;
    public string InfoText;
    public AssetItem(Object asset)
    {
        Asset = asset; Text = asset.Name; Type = asset.type; m_PathID = asset.m_PathID;
    }
    public AssetItem(string name, ClassIDType type, string externalPath)
    {
        Text = name; Type = type; ExternalPath = externalPath;
    }
}

public static class ExactJson
{
    public static string Serialize(object value) => JsonConvert.SerializeObject(value, Formatting.Indented,
        new JsonSerializerSettings { Converters = { new StringEnumConverter(), new DecimalInt64Converter() }, ReferenceLoopHandling = ReferenceLoopHandling.Error });

    private sealed class DecimalInt64Converter : JsonConverter
    {
        public override bool CanConvert(Type type) => type == typeof(long) || type == typeof(ulong);
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => writer.WriteValue(Convert.ToString(value, CultureInfo.InvariantCulture));
        public override object ReadJson(JsonReader reader, Type type, object value, JsonSerializer serializer) =>
            type == typeof(long) ? (object)long.Parse((string)reader.Value, CultureInfo.InvariantCulture) : ulong.Parse((string)reader.Value, CultureInfo.InvariantCulture);
    }
}
