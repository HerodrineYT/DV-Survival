using System;
using System.Collections.Generic;
using System.Linq;
using DVSurvival.Core;
using Newtonsoft.Json;

namespace DVSurvival.Mod
{
    [Serializable]
    internal sealed class SurvivalPlayerRecord
    {
        public string IdentityId = string.Empty;
        public string DisplayName = string.Empty;
        public long LastSeenUtcTicks;
        public SurvivalState State = new SurvivalState();
    }

    [Serializable]
    internal sealed class CabHeaterSaveRecord
    {
        public string CarId = string.Empty;
        public bool IsOn;
        public float? Level;
        [JsonIgnore]
        public float EffectiveLevel => CabHeaterSetting.NormalizeSwitch(Level ?? (IsOn ? 1f : 0f));
        public long LastSeenUtcTicks;
    }

    [Serializable]
    internal sealed class SurvivalSaveDocument
    {
        public int Version = SurvivalConstants.SaveVersion;
        public string SessionId = string.Empty;
        public List<SurvivalPlayerRecord> Players = new List<SurvivalPlayerRecord>();
        public HashSet<string> ConsumedItems = new HashSet<string>(StringComparer.Ordinal);
        public List<CabHeaterSaveRecord> CabHeaters = new List<CabHeaterSaveRecord>();

        public SurvivalPlayerRecord GetOrCreate(string identityId, string displayName, SurvivalTuning tuning)
        {
            var record = Players.FirstOrDefault(p =>
                string.Equals(p.IdentityId, identityId, StringComparison.OrdinalIgnoreCase));
            if (record == null)
            {
                record = new SurvivalPlayerRecord
                {
                    IdentityId = identityId,
                    DisplayName = displayName ?? string.Empty,
                    State = SurvivalState.CreateDefault(tuning)
                };
                Players.Add(record);
            }
            record.DisplayName = string.IsNullOrEmpty(displayName) ? record.DisplayName : displayName;
            record.LastSeenUtcTicks = DateTime.UtcNow.Ticks;
            if (record.State == null || !record.State.IsValid()) record.State = SurvivalState.CreateDefault(tuning);
            Prune();
            return record;
        }

        public float GetCabHeater(string carId)
        {
            if (string.IsNullOrWhiteSpace(carId) || CabHeaters == null) return 0f;
            var record = CabHeaters.FirstOrDefault(value => value != null &&
                string.Equals(value.CarId, carId, StringComparison.OrdinalIgnoreCase));
            return record == null ? 0f : record.EffectiveLevel;
        }

        public void SetCabHeater(string carId, float level)
        {
            if (string.IsNullOrWhiteSpace(carId)) return;
            if (CabHeaters == null) CabHeaters = new List<CabHeaterSaveRecord>();
            var normalizedId = carId.Trim();
            var record = CabHeaters.FirstOrDefault(value => value != null &&
                string.Equals(value.CarId, normalizedId, StringComparison.OrdinalIgnoreCase));
            if (record == null)
            {
                record = new CabHeaterSaveRecord { CarId = normalizedId };
                CabHeaters.Add(record);
            }
            record.Level = CabHeaterSetting.NormalizeSwitch(level);
            record.IsOn = record.Level > 0f;
            record.LastSeenUtcTicks = DateTime.UtcNow.Ticks;
            PruneCabHeaters();
        }

        public void Normalize(SurvivalTuning tuning)
        {
            Version = SurvivalConstants.SaveVersion;
            if (ConsumedItems == null) ConsumedItems = new HashSet<string>(StringComparer.Ordinal);
            if (CabHeaters == null) CabHeaters = new List<CabHeaterSaveRecord>();
            Guid sessionGuid;
            if (!Guid.TryParse(SessionId, out sessionGuid) || sessionGuid == Guid.Empty)
                SessionId = Guid.NewGuid().ToString("D");
            if (Players == null) Players = new List<SurvivalPlayerRecord>();
            Players = Players.Where(p => p != null && !string.IsNullOrWhiteSpace(p.IdentityId))
                .GroupBy(p => p.IdentityId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(p => p.LastSeenUtcTicks).First())
                .ToList();
            foreach (var player in Players)
            {
                if (player.State == null || !player.State.IsValid())
                    player.State = SurvivalState.CreateDefault(tuning);
                else
                    player.State.Clamp();
            }
            CabHeaters = CabHeaters.Where(value => value != null &&
                    !string.IsNullOrWhiteSpace(value.CarId) && value.CarId.Length <= 80)
                .GroupBy(value => value.CarId.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(value => value.LastSeenUtcTicks).First())
                .ToList();
            Prune();
            PruneCabHeaters();
        }

        private void Prune()
        {
            const int maximumRecords = 64;
            if (Players.Count <= maximumRecords) return;
            Players = Players.OrderByDescending(p => p.LastSeenUtcTicks).Take(maximumRecords).ToList();
        }

        private void PruneCabHeaters()
        {
            const int maximumRecords = 128;
            if (CabHeaters.Count <= maximumRecords) return;
            CabHeaters = CabHeaters.OrderByDescending(value => value.LastSeenUtcTicks)
                .Take(maximumRecords).ToList();
        }
    }

    internal static class SurvivalSaveData
    {
        private const string SaveKey = "DVSurvival.State";
        private const int MaximumJsonLength = 1024 * 1024;

        public static SurvivalSaveDocument Read(SaveGameData data, SurvivalTuning tuning)
        {
            SurvivalSaveDocument document = null;
            if (data != null)
            {
                var json = data.GetString(SaveKey);
                if (!string.IsNullOrWhiteSpace(json) && json.Length <= MaximumJsonLength)
                {
                    try { document = JsonConvert.DeserializeObject<SurvivalSaveDocument>(json); }
                    catch (JsonException) { document = null; }
                }
            }
            if (document == null) document = new SurvivalSaveDocument();
            document.Normalize(tuning);
            return document;
        }

        public static void Write(SaveGameData data, SurvivalSaveDocument document, SurvivalTuning tuning)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (document == null) throw new ArgumentNullException(nameof(document));
            document.Normalize(tuning);
            var json = JsonConvert.SerializeObject(document, Formatting.None);
            if (json.Length > MaximumJsonLength)
                throw new InvalidOperationException("DVSurvival save payload exceeded the safety limit.");
            data.SetString(SaveKey, json);
        }
    }
}
