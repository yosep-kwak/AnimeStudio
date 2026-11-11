using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;

namespace AnimeStudio
{
    public static class UnityCNManager
    {
        private static List<List<string>> baseData = new()
        {
            new() { "PGR_GLB_KR", "Punishing Gray Raven - Global/Korea", "6B75726F6B75726F6B75726F6B75726F" },
            new() { "PGR_CN_JP_TW", "Punishing Gray Raven - China/Japan/Taiwan", "587865636F6472506547616B61326536" },
            new() { "PGR_CN_JP_TW_OLD", "Punishing Gray Raven - China/Japan/Taiwan Old Key", "7935585076714C4F72436F6B57524961" },
            new() { "Archeland_KalpaOfUniverse", "Archeland/Kalpa of Universe", "426C61636B4A61636B50726F6A656374" },
            new() { "Archeland_1114", "Archeland 1.1.14", "50726F6A65637441726368654C616E64" },
            new() { "NeuralCloud", "Neural Cloud", "31636162383436663532393031633965" },
            new() { "NeuralCloudCN", "Neural Cloud CN", "62363238363766353164326561376266" },
            new() { "HiganEruthyll", "Higan: Eruthyll", "45317832633361346C35693662377572" },
            new() { "WhiteCord", "White Chord", "79756C6F6E6731383638676E6F6C7579" },
            new() { "Mecharashi", "Mecharashi", "33384338334631333245374637413041" },
            new() { "CastlevaniaMoonNightFantasy", "Castlevania: Moon Night Fantasy", "31323334353637383132333435363738" },
            new() { "HYSXZY", "Huā Yì Shān Xīn Zhī Yuè", "494E484A6E68647970716B3534377864" },
            new() { "DoulaContinent", "Doula Continent", "52346366773339474644326661785756" },
            new() { "BlessGlobal", "Bless Global", "6C6F6E67747567616D652E796A66623F" },
            new() { "Starside", "Starside", "41394A3542384D4A50554D3539464B57" },
            new() { "ResonanceSoltice", "Resonance Soltice", "5265736F6E616E63655265626F726E52" },
            new() { "OblivionOverride", "Oblivion Override", "7179666D6F6F6E323331323433343532" },
            new() { "Dawnlands", "Dawnland", "636F6465737339353237636F64657373" },
            new() { "BB", "BB", "5F6C4E3F3A3F233F3F3F3F663F1A3F3F" },
            new() { "DynastyLegends2", "Dynasty Legends 2", "746169686567616D6573323032323032" },
            new() { "EvernightCN", "Evernight CN", "68687878747478736868787874747873" },
            new() { "XintianlongBabu", "Xintianlong Babu", "61323562623133346363326464333265" },
            new() { "FrostpunkBeyondTheIce", "Frostpunk: Beyond the Ice", "7368756978696E673838383838383838" },
            new() { "CatFantasy", "Cat Fantasy", "43614461566637323538576877363433" }
        };
        private static List<List<string>> storedData;
        private static readonly string UnityCNKeysPath = "CNKeys.json";
        private static bool readOnly;

        static UnityCNManager() {}

        public static void InitializeReadOnly()
        {
            if (readOnly) return;
            readOnly = true;
            storedData = null;
        }

        private static List<List<string>> CopyKeys(List<List<string>> keys) =>
            keys.Select(entry => new List<string>(entry)).ToList();

        private static void RequireWritableStore()
        {
            if (readOnly) throw new InvalidOperationException("The headless UnityCN key store is read-only.");
        }

        public static List<List<string>> GetKeys()
        {
            if (readOnly) return ReadJson();
            if (storedData == null)
            {
                storedData = ReadJson();
            }
            return storedData;
        }

        public static List<List<string>> GetDefaultKeys() => readOnly ? CopyKeys(baseData) : baseData;

        public static void SetKeys(List<List<string>> keys)
        {
            RequireWritableStore();
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UnityCNKeysPath), JsonSerializer.Serialize(keys, options));
            storedData = keys;
        }

        public static List<List<string>> ReadJson()
        {
            if (readOnly) return CopyKeys(storedData ??= ReadKeyStoreSnapshot());
            try
            {
                var json = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UnityCNKeysPath));
                var list = JsonSerializer.Deserialize<List<List<string>>>(json);

                if (list.Count == 0)
                {
                    return ResetJson();
                }

                storedData = list;
                return list;
            } catch
            {
                Logger.Error($"[UnityCN] Failed to read {UnityCNKeysPath} file, creating a new one.");
                return ResetJson();
            }
        }

        private static List<List<string>> ReadKeyStoreSnapshot()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UnityCNKeysPath);
                var keys = JsonSerializer.Deserialize<List<List<string>>>(File.ReadAllText(path));
                if (keys is { Count: > 0 } && keys.All(entry => entry is { Count: >= 3 } &&
                    entry.Take(3).All(field => field != null))) return keys;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
            return CopyKeys(baseData);
        }

        public static List<List<string>> ResetJson()
        {
            RequireWritableStore();
            var options = new JsonSerializerOptions {
                WriteIndented = true
            };
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UnityCNKeysPath), JsonSerializer.Serialize(baseData, options));
            return baseData;
        }

        public static void SetKey(UnityCN.Entry entry)
        {
            RequireWritableStore();
            if (UnityCN.SetKey(entry))
            {
                Logger.Info($"[UnityCN] Selected Key is {entry.Name}");
            }
            else
            {
                Logger.Info($"[UnityCN] No Key is selected !!");
            }
        }
    }
}
