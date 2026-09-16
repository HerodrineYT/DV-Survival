using System;
using System.IO;
using System.Reflection;
using UnityEngine;

// Run in Unity against the built Survival assembly, not a duplicated model.
public static class VerifyStationOffice
{
    public static FakeRuntime Runtime = new FakeRuntime();
    public sealed class FakeRuntime
    {
        public bool HasWeatherTemperature { get { return true; } }
        public FakeState CurrentState { get; set; } = new FakeState();
    }
    public sealed class FakeState
    {
        public float TemperatureCelsius { get; set; }
        public string Current { get; set; } = "Winter";
    }

    public static void Run(string modDirectory)
    {
        var assembly = Assembly.LoadFrom(Path.Combine(modDirectory, "DVSurvival.dll"));
        var game = Assembly.Load("Assembly-CSharp");
        var detectorType = game.GetType("TutorialPlayerDetector", true);
        var root = new GameObject("Room without office name");
        var unrelated = new GameObject("stationoffice decorative wall");
        var regularOffice = new GameObject("Office_7_interior(Clone)");
        var house = new GameObject("House_interior");
        try
        {
            root.transform.position = new Vector3(10000, 0, 10000);
            root.transform.rotation = Quaternion.Euler(0, 38, 0);
            root.transform.localScale = new Vector3(1.4f, 1, .8f);
            var volume = root.AddComponent<BoxCollider>();
            volume.center = new Vector3(0, 1.5f, 0);
            volume.size = new Vector3(8, 3, 6);
            volume.isTrigger = true;
            volume.enabled = false;
            var detector = root.AddComponent(detectorType);
            var kind = detectorType.GetField("detectorType");
            kind.SetValue(detector, Enum.Parse(kind.FieldType, "StationOffice"));
            ((Behaviour)detector).enabled = false;

            unrelated.transform.position = root.transform.position + Vector3.right * 30;
            var wall = unrelated.AddComponent<BoxCollider>();
            wall.center = new Vector3(0, 1.5f, 0);
            wall.size = new Vector3(8, 3, 6);
            wall.isTrigger = true;
            regularOffice.transform.position = root.transform.position + Vector3.right * 60;
            regularOffice.transform.rotation = Quaternion.Euler(0, -29, 0);
            house.transform.position = root.transform.position + Vector3.right * 100;
            AddOfficeVolumes(game, regularOffice);
            AddOfficeVolumes(game, house);
            Physics.SyncTransforms();

            var settings = Activator.CreateInstance(assembly.GetType("DVSurvival.Mod.SurvivalModSettings", true));
            var heaterType = assembly.GetType("DVSurvival.Mod.CabHeaterSwitchSystem", true);
            var heaters = Activator.CreateInstance(heaterType, new object[] { new Action<string, float>((id, value) => { }) });
            var samplerType = assembly.GetType("DVSurvival.Mod.EnvironmentSampler", true);
            var sampler = Activator.CreateInstance(samplerType, new[] { settings, heaters });
            var provider = Get(sampler, "seasons");
            Set(provider, "probeAttempted", true);
            Set(provider, "runtimeField", typeof(VerifyStationOffice).GetField("Runtime"));
            Set(provider, "currentStateProperty", typeof(FakeRuntime).GetProperty("CurrentState"));
            Set(provider, "temperatureProperty", typeof(FakeState).GetProperty("TemperatureCelsius"));
            Set(provider, "seasonProperty", typeof(FakeState).GetProperty("Current"));
            Set(provider, "weatherTemperatureProperty", typeof(FakeRuntime).GetProperty("HasWeatherTemperature"));
            var sample = samplerType.GetMethod("Sample", BindingFlags.Instance | BindingFlags.NonPublic);
            Func<Vector3, float, object> read = (position, outside) => {
                Runtime.CurrentState.TemperatureCelsius = outside;
                return sample.Invoke(sampler, new object[] { position, 0f, false, false, 0f, false, false });
            };

            var inside = root.transform.position;
            var outdoors = root.transform.TransformPoint(new Vector3(5, 0, 0));
            Equal(22, Number(read(inside, -25), "AmbientTemperatureCelsius"), "disabled office volume");
            Equal(22, Number(read(inside, 11), "AmbientTemperatureCelsius"), "cool spring office");
            Equal(30, Number(read(inside, 30), "AmbientTemperatureCelsius"), "office does not cool summer air");
            Equal(-25, Number(read(outdoors, -25), "AmbientTemperatureCelsius"), "outside rotated room");
            Equal(-25, Number(read(unrelated.transform.position, -25), "AmbientTemperatureCelsius"), "named scenery is not a room");
            var officeLeft = regularOffice.transform.TransformPoint(new Vector3(-3, 0, 0));
            var officeRight = regularOffice.transform.TransformPoint(new Vector3(3, 0, 0));
            Equal(22, Number(read(officeLeft, -25), "AmbientTemperatureCelsius"), "native office left volume");
            Equal(22, Number(read(officeRight, -25), "AmbientTemperatureCelsius"), "native office right volume");
            Equal(-25, Number(read(regularOffice.transform.position, -25), "AmbientTemperatureCelsius"), "gap between office volumes is outdoors");
            Equal(-25, Number(read(house.transform.TransformPoint(new Vector3(-3, 0, 0)), -25), "AmbientTemperatureCelsius"), "same AO volume in house is not a station office");
            var roomEnvironment = read(inside, -25);
            Equal(0, Number(roomEnvironment, "RainIntensity"), "sheltered rain");
            Equal(0, Number(roomEnvironment, "WindSpeedMetersPerSecond"), "sheltered wind");
            Equal(.03f, Number(roomEnvironment, "Exposure"), "sheltered exposure");

            root.transform.position += new Vector3(-500, 0, 650);
            Equal(22, Number(read(root.transform.position, -25), "AmbientTemperatureCelsius"), "origin-shifted room");
            Equal(-25, Number(read(inside, -25), "AmbientTemperatureCelsius"), "old origin is outdoors");
            root.SetActive(false);
            Equal(-25, Number(read(root.transform.position, -25), "AmbientTemperatureCelsius"), "unloaded room ignored");
            ((IDisposable)heaters).Dispose();
            Debug.Log("STATION_OFFICE_OK: authored AO office boxes, two-room gap, house exclusion, disabled native room, rotated/scaled containment, cold22, warm30, origin shift, unload");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(unrelated);
            UnityEngine.Object.DestroyImmediate(regularOffice); UnityEngine.Object.DestroyImmediate(house);
        }
    }

    private static void AddOfficeVolumes(Assembly game, GameObject parent)
    {
        var volumeObject = new GameObject("PostProcOfficeVolumeAO");
        volumeObject.SetActive(false);
        volumeObject.transform.SetParent(parent.transform, false);
        for (int side = -1; side <= 1; side += 2)
        {
            var box = volumeObject.AddComponent<BoxCollider>();
            box.center = new Vector3(side * 3, 1.5f, 0);
            box.size = new Vector3(4, 3, 4);
            box.enabled = false;
        }
        var controller = (Behaviour)volumeObject.AddComponent(game.GetType("DV.PostProcessingVolumeAOController", true));
        controller.enabled = false; // The fixture has no game's graphics preferences singleton.
        volumeObject.SetActive(true);
    }

    private static object Get(object target, string field)
    { return target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
    private static void Set(object target, string field, object value)
    { target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value); }
    private static float Number(object target, string field)
    { return (float)target.GetType().GetField(field).GetValue(target); }
    private static void Equal(float expected, float actual, string label)
    { if (Math.Abs(expected - actual) > .001f) throw new Exception(label + ": expected " + expected + ", got " + actual); }
}
