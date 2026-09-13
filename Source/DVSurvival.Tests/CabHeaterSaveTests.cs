using System.Collections.Generic;
using DVSurvival.Core;
using DVSurvival.Mod;
using Xunit;

// Only the game's string storage API is substituted; production JSON serialization,
// migration, bounds and record normalization are exercised unchanged.
internal sealed class SaveGameData
{
    private readonly Dictionary<string, string> strings = new Dictionary<string, string>();
    public string GetString(string key)
    {
        string value;
        return strings.TryGetValue(key, out value) ? value : null;
    }
    public void SetString(string key, string value) { strings[key] = value; }
}

namespace DVSurvival.Tests
{
    public sealed class CabHeaterSaveTests
    {
        [Fact]
        public void OldBinarySavesAndIntermediateDm3LevelsMigrateToOffOn()
        {
            var save = new SaveGameData();
            save.SetString("DVSurvival.State",
                "{\"CabHeaters\":[{\"CarId\":\"legacy-on\",\"IsOn\":true},{\"CarId\":\"legacy-off\",\"IsOn\":false}]}");
            var tuning = new SurvivalTuning();
            var document = SurvivalSaveData.Read(save, tuning);
            Assert.Equal(1f, document.GetCabHeater("legacy-on"));
            Assert.Equal(0f, document.GetCabHeater("legacy-off"));
            document.CabHeaters.Add(new CabHeaterSaveRecord { CarId = "dm3-low", Level = 1f / 3f });
            document.CabHeaters.Add(new CabHeaterSaveRecord { CarId = "dm3-medium", Level = 2f / 3f });
            SurvivalSaveData.Write(save, document, tuning);
            var restored = SurvivalSaveData.Read(save, tuning);
            Assert.Equal(1f, restored.GetCabHeater("legacy-on"));
            Assert.Equal(0f, restored.GetCabHeater("legacy-off"));
            Assert.Equal(1f, restored.GetCabHeater("dm3-low"));
            Assert.Equal(1f, restored.GetCabHeater("dm3-medium"));
        }

        [Fact]
        public void ExplicitOffOverridesLegacyOnAndSeparateLocosStayIndependent()
        {
            var save = new SaveGameData();
            save.SetString("DVSurvival.State",
                "{\"CabHeaters\":[{\"CarId\":\"dm3\",\"IsOn\":true,\"Level\":0}]}");
            var tuning = new SurvivalTuning();
            var document = SurvivalSaveData.Read(save, tuning);
            Assert.Equal(0f, document.GetCabHeater("dm3"));
            document.SetCabHeater("de2", 1f);
            Assert.Equal(0f, document.GetCabHeater("dm3"));
            Assert.Equal(1f, document.GetCabHeater("DE2"));
            Assert.Equal(0f, document.GetCabHeater("unseen"));
        }
    }
}
