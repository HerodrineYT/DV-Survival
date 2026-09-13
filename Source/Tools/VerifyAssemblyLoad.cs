using System;
using System.IO;
using System.Reflection;

internal static class VerifyAssemblyLoad
{
    private static string[] searchDirectories;

    private static int Main(string[] args)
    {
        if (args.Length != 6 && args.Length != 7)
        {
            Console.Error.WriteLine("Usage: VerifyAssemblyLoad <mod-dir> <managed-dir> <umm-dir> <multiplayer-dir> <custom-items-dir> <language-helper-dir> [--offline]");
            return 64;
        }
        bool offline = args.Length == 7 && args[6] == "--offline";
        searchDirectories = offline ? new[] { args[0], args[1], args[2], args[4], args[5] } : args;
        AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
        try
        {
            Verify(Path.Combine(args[0], "DVSurvival.Core.dll"), null, null);
            if (!offline) Verify(Path.Combine(args[0], "DVSurvival.Multiplayer.dll"), null, null);
            Verify(Path.Combine(args[0], "DVSurvival.dll"), "DVSurvival.Mod.Main", "Load");
            VerifyGameApis(args[1], offline ? null : args[3]);
            VerifyDigitalSpeedometerPatch(args[0], args[1], args[2]);
            VerifyNativeSleepPatch(args[0], args[1], args[2]);
            RequireMethod(Path.Combine(args[1], "Assembly-CSharp.dll"), "StorageSerializer",
                "LoadStorageData", BindingFlags.Public | BindingFlags.Static);
            var migration = Assembly.LoadFrom(Path.Combine(args[0], "DVSurvival.dll"))
                .GetType("DVSurvival.Mod.RemovedProvisionMigration", true)
                .GetMethod("IsRemoved", BindingFlags.NonPublic | BindingFlags.Static);
            foreach (string prefab in new[] { "custom_item_mod/DVSurvival_Moonshine/prefab",
                "custom_item_mod/DVSurvival_Water/prefab", "custom_item_mod/DVSurvival_Moonshine2/prefab", null })
            {
                bool expected = prefab == "custom_item_mod/DVSurvival_Moonshine/prefab";
                if (!Equals(migration.Invoke(null, new object[] { prefab }), expected))
                    throw new InvalidOperationException("Retired item migration must match only the exact old prefab.");
            }
            Console.WriteLine("OK: native storage migration API and exact retired-prefab matching");
            RequireMethod(Path.Combine(args[5], "DVLangHelper.Runtime.dll"), "DVLangHelper.Runtime.TranslationInjector",
                "AddTranslationsFromCsv", BindingFlags.Public | BindingFlags.Instance);
            RequireMethod(Path.Combine(args[1], "Assembly-CSharp.dll"), "IndicatorGauge",
                "SetNeedleRotation", BindingFlags.NonPublic | BindingFlags.Instance);
            RequireMethod(Path.Combine(args[1], "Assembly-CSharp.dll"), "IndicatorGaugeLagging",
                "Update", BindingFlags.NonPublic | BindingFlags.Instance);
            RequireMethod(Path.Combine(args[1], "Assembly-CSharp.dll"), "DV.Simulation.Ports.IndicatorPortReader",
                "Init", BindingFlags.Public | BindingFlags.Instance);
            RequireMethod(Path.Combine(args[1], "Assembly-CSharp.dll"), "DV.HUD.LocoHUDProvider",
                "SpeedometerUpdated", BindingFlags.NonPublic | BindingFlags.Instance);
            RequireMethod(Path.Combine(args[1], "Assembly-CSharp.dll"), "DV.HUD.LocoHUDProvider",
                "SpeedometerVisualUpdated", BindingFlags.NonPublic | BindingFlags.Instance);
            RequireMethod(Path.Combine(args[1], "Assembly-CSharp.dll"), "DV.Customization.Gadgets.Implementations.GadgetDigitalSpeedometerLOD",
                "Update", BindingFlags.NonPublic | BindingFlags.Instance);
            if (offline)
            {
                var assembly = Assembly.LoadFrom(Path.Combine(args[0], "DVSurvival.dll"));
                foreach (var reference in assembly.GetReferencedAssemblies())
                    if (reference.Name == "MultiplayerAPI" || reference.Name == "DVSurvival.Multiplayer")
                        throw new InvalidOperationException("Game assembly has a hard multiplayer reference.");
                var runtime = assembly.GetType("DVSurvival.Mod.SurvivalRuntime", true);
                var instance = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(runtime);
                var factory = runtime.GetMethod("CreateNetworkBridge", BindingFlags.NonPublic | BindingFlags.Instance);
                var bridge = factory.Invoke(instance, new object[] { args[0] });
                if (bridge.GetType().Name != "OfflineSurvivalBridge") throw new InvalidOperationException("Offline fallback failed.");
                Console.WriteLine("OK: loaded without MultiplayerAPI; selected OfflineSurvivalBridge");
            }
            RequireMethod(Path.Combine(args[4], "custom_item_mod.dll"), "custom_item_mod.ItemModsFinder",
                "InitializeItems", BindingFlags.Public | BindingFlags.Static);
            RequireMethod(Path.Combine(args[4], "custom_item_mod.dll"), "custom_item_mod.CustomItem",
                "AddToShop", BindingFlags.Public | BindingFlags.Instance);
            RequireMethod(Path.Combine(args[1], "Assembly-CSharp.dll"), "DV.Shops.ScanItemCashRegisterModule",
                "AddItemsToBuy", BindingFlags.Public | BindingFlags.Instance);
            RequireMethod(Path.Combine(args[1], "Assembly-CSharp.dll"), "DV.CabControls.ControlImplBase",
                "add_Used", BindingFlags.Public | BindingFlags.Instance);
            RequireMethod(Path.Combine(args[1], "DV.Inventory.dll"), "DV.InventorySystem.Inventory",
                "AddItemToInventory", BindingFlags.Public | BindingFlags.Instance);
            return 0;
        }
        catch (ReflectionTypeLoadException exception)
        {
            Console.Error.WriteLine("TYPE LOAD FAILED");
            foreach (var loaderException in exception.LoaderExceptions)
                Console.Error.WriteLine("{0}: {1}", loaderException.GetType().FullName, loaderException.Message);
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= ResolveAssembly;
        }
    }

    private static void VerifyDigitalSpeedometerPatch(string modDirectory, string managedDirectory, string ummDirectory)
    {
        var game = Assembly.LoadFrom(Path.Combine(managedDirectory, "Assembly-CSharp.dll"));
        var mod = Assembly.LoadFrom(Path.Combine(modDirectory, "DVSurvival.dll"));
        var harmony = Assembly.LoadFrom(Path.Combine(ummDirectory, "0Harmony.dll"));
        var original = game.GetType("DV.Customization.Gadgets.Implementations.GadgetDigitalSpeedometerLOD", true)
            .GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
        var getInstructions = harmony.GetType("HarmonyLib.PatchProcessor", true).GetMethod("GetOriginalInstructions",
            new[] { typeof(MethodBase), typeof(System.Reflection.Emit.ILGenerator) });
        var instructions = getInstructions.Invoke(null, new object[] { original, null });
        var transpiler = mod.GetType("DVSurvival.Mod.PersonalDigitalSpeedometerPatch", true)
            .GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static);
        var rewritten = (System.Collections.IEnumerable)transpiler.Invoke(null, new[] { instructions });
        int count = 0;
        foreach (var instruction in rewritten) count++;
        if (count < 2) throw new InvalidOperationException("Digital speedometer transpiler returned no instructions.");
        var displayType = mod.GetType("DVSurvival.Mod.FatigueSpeedometer", true);
        foreach (var methodName in new[] { "ApplyNeedle", "LateTick" })
            if (displayType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static) == null)
                throw new MissingMethodException(displayType.FullName, methodName);
        if (mod.GetType("DVSurvival.Mod.PersonalLaggingSpeedGaugePatch", true)
                .GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static) == null ||
            mod.GetType("DVSurvival.Mod.RegisterSpeedPortReaderPatch", true)
                .GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static) == null)
            throw new MissingMethodException("Final analog speedometer patches are missing.");
        var factor = displayType.GetProperty("Factor", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
        if (!Equals(factor, 1f)) throw new InvalidOperationException("Speedometer must stay truthful outside an active personal session.");
        Console.WriteLine("OK: production digital speedometer IL rewritten; visual delegates bind; inactive display factor is 1");
    }

    private static void VerifyNativeSleepPatch(string modDirectory, string managedDirectory, string ummDirectory)
    {
        var mod = Assembly.LoadFrom(Path.Combine(modDirectory, "DVSurvival.dll"));
        var harmony = Assembly.LoadFrom(Path.Combine(ummDirectory, "0Harmony.dll"));
        var patch = mod.GetType("DVSurvival.Mod.NativeSleepOriginPatch", true);
        var target = (MethodBase)patch.GetMethod("TargetMethod", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, null);
        if (target == null || target.Name != "MoveNext" ||
            target.DeclaringType.DeclaringType.FullName != "DV.BedSleepingController")
            throw new InvalidOperationException("Sleep patch must target native coroutine MoveNext.");
        var reader = harmony.GetType("HarmonyLib.PatchProcessor", true).GetMethod("GetOriginalInstructions",
            new[] { typeof(MethodBase), typeof(System.Reflection.Emit.ILGenerator) });
        var instructions = reader.Invoke(null, new object[] { target, null });
        var rewritten = (System.Collections.IEnumerable)patch.GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new[] { instructions });
        int replacements = 0, bedLoads = 0;
        foreach (var instruction in rewritten)
        {
            var field = instruction.GetType().GetField("operand").GetValue(instruction) as FieldInfo;
            if (field != null && field.Name == "bed" && field.FieldType.FullName == "DV.BedSleeping") bedLoads++;
            var method = instruction.GetType().GetField("operand").GetValue(instruction) as MethodInfo;
            if (method == null) continue;
            if (method.DeclaringType.FullName == "DV.TimeAdvance" && method.Name == "AdvanceTime")
                throw new InvalidOperationException("Unmarked native sleep call remains.");
            if (method.DeclaringType == patch && method.Name == "AdvanceSleepTime") replacements++;
        }
        if (replacements != 1 || bedLoads < 1) throw new InvalidOperationException("Native sleep must pass its actual bed to the wrapper.");
        var wrapper = patch.GetMethod("AdvanceSleepTime", BindingFlags.NonPublic | BindingFlags.Static);
        if (wrapper.GetParameters().Length != 3 || wrapper.GetParameters()[2].ParameterType.FullName != "DV.BedSleeping")
            throw new InvalidOperationException("Native bed wrapper signature changed.");
        if (!Equals(false, patch.GetProperty("IsAdvancingSleep", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null, null)))
            throw new InvalidOperationException("Sleep origin must be inactive outside the native call.");
        Console.WriteLine("OK: actual game SleepCoro IL rewritten at exactly one time-skip call; no active sleep marker outside it");
    }

    private static void Verify(string path, string entryTypeName, string entryMethodName)
    {
        var assembly = Assembly.LoadFrom(path);
        var types = assembly.GetTypes();
        if (entryTypeName != null)
        {
            var type = assembly.GetType(entryTypeName, true);
            var method = type.GetMethod(entryMethodName, BindingFlags.Public | BindingFlags.Static);
            if (method == null || method.ReturnType != typeof(bool))
                throw new MissingMethodException(entryTypeName, entryMethodName);
            RequirePng(assembly, "panel", 128, 128);
            RequirePng(assembly, "dial", 96, 96);
            RequirePng(assembly, "icons", 512, 64);
            RequirePng(assembly, "rings", 1056, 960);
            RequirePng(assembly, "thermal", 256, 8);
            RequirePng(assembly, "provisions", 2048, 512, "DVSurvival.Items.");
            RequirePng(assembly, "provisions-en", 2048, 512, "DVSurvival.Items.");
            RequirePng(assembly, "inventory-ru", 1280, 512, "DVSurvival.Items.");
            RequirePng(assembly, "inventory-en", 1280, 512, "DVSurvival.Items.");
        }
        Console.WriteLine("OK: loaded {0} types from {1}", types.Length, assembly.GetName().Name);
    }

    private static void RequirePng(Assembly assembly, string name, int width, int height, string prefix = "DVSurvival.Hud.")
    {
        using (var stream = assembly.GetManifestResourceStream(prefix + name + ".png"))
        {
            if (stream == null) throw new InvalidDataException("Missing HUD asset " + name);
            var header = new byte[24];
            if (stream.Read(header, 0, header.Length) != header.Length ||
                header[0] != 137 || header[1] != 80 || header[2] != 78 || header[3] != 71)
                throw new InvalidDataException("Invalid HUD PNG " + name);
            var actualWidth = header[16] << 24 | header[17] << 16 | header[18] << 8 | header[19];
            var actualHeight = header[20] << 24 | header[21] << 16 | header[22] << 8 | header[23];
            if (actualWidth != width || actualHeight != height)
                throw new InvalidDataException("HUD atlas layout mismatch " + name);
        }
    }

    private static void VerifyGameApis(string managedDirectory, string multiplayerDirectory)
    {
        RequireMethod(Path.Combine(managedDirectory, "Assembly-CSharp.dll"), "LoadingScreenManager",
            "get_IsLoading", BindingFlags.Public | BindingFlags.Static);
        RequireMethod(Path.Combine(managedDirectory, "Assembly-CSharp.dll"), "FastTravelController",
            "get_IsFastTravelling", BindingFlags.Public | BindingFlags.Static);
        foreach (var getter in new[] { "get_ItemIconSprite", "get_ItemIconSpriteDropped", "get_ItemIconSpriteSimple", "set_PreviewPrefab" })
            RequireMethod(Path.Combine(managedDirectory, "Assembly-CSharp.dll"), "InventoryItemSpec", getter,
                BindingFlags.Public | BindingFlags.Instance);
        RequireMethod(Path.Combine(managedDirectory, "DV.UserManagement.dll"), "DV.UserManagement.UserManager",
            "get_CurrentUser", BindingFlags.Public | BindingFlags.Instance);
        RequireMethod(Path.Combine(managedDirectory, "Assembly-CSharp.dll"),
            "DV.BedSleepingController", "get_IsSleeping", BindingFlags.Public | BindingFlags.Instance);
        RequireMethod(Path.Combine(managedDirectory, "Assembly-CSharp.dll"),
            "DV.TimeAdvance", "AdvanceTime", BindingFlags.Public | BindingFlags.Static);
        RequireMethod(Path.Combine(managedDirectory, "DV.CharacterController.dll"),
            "CustomFirstPersonController", "ProcessInputAndGetSpeed", BindingFlags.NonPublic | BindingFlags.Instance);
        RequireMethod(Path.Combine(managedDirectory, "DV.CharacterController.dll"),
            "CustomFirstPersonController", "get_CapsuleHeightNoVRCrouch", BindingFlags.Public | BindingFlags.Instance);
        RequireMethod(Path.Combine(managedDirectory, "DV.CharacterController.dll"),
            "CustomFirstPersonController", "GetTraversableLayers", BindingFlags.Public | BindingFlags.Instance);
        RequireMethod(Path.Combine(managedDirectory, "DV.CharacterController.dll"),
            "CustomFirstPersonController", "Update", BindingFlags.NonPublic | BindingFlags.Instance);
        var controllerType = Assembly.LoadFrom(Path.Combine(managedDirectory, "DV.CharacterController.dll"))
            .GetType("CustomFirstPersonController", true);
        foreach (var fieldName in new[] { "m_MoveDir", "underwaterVelocity" })
        {
            var field = controllerType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null || field.FieldType.FullName != "UnityEngine.Vector3")
                throw new MissingFieldException(controllerType.FullName, fieldName);
        }
        RequireMethod(Path.Combine(managedDirectory, "Assembly-CSharp.dll"),
            "TrainCar", "GetAbsSpeed", BindingFlags.Public | BindingFlags.Instance);
        RequireMethod(Path.Combine(managedDirectory, "Assembly-CSharp.dll"),
            "PlayerManager", "TeleportPlayer", BindingFlags.Public | BindingFlags.Static);
        RequireMethod(Path.Combine(managedDirectory, "Assembly-CSharp.dll"),
            "DV.Teleporters.FastTravelDestination", "get_ActiveDestinations", BindingFlags.Public | BindingFlags.Static);
        RequireMethod(Path.Combine(managedDirectory, "DV.TerrainSystem.dll"),
            "DV.TerrainSystem.TerrainGrid", "IsInLoadedRegion", BindingFlags.Public | BindingFlags.Instance);
        var travelType = Assembly.LoadFrom(Path.Combine(managedDirectory, "Assembly-CSharp.dll"))
            .GetType("FastTravelController", true);
        var nativeTravel = travelType.GetMethod("FastTravel", BindingFlags.NonPublic | BindingFlags.Instance);
        var withoutLoco = travelType.GetMethod("FastTravelWithoutLocomotive", BindingFlags.NonPublic | BindingFlags.Instance);
        if (nativeTravel == null || withoutLoco == null ||
            nativeTravel.ReturnType != typeof(System.Collections.IEnumerator) ||
            withoutLoco.ReturnType != typeof(System.Collections.IEnumerator))
            throw new MissingMethodException("Native fast travel coroutines changed.");
        var travelParameters = nativeTravel.GetParameters();
        if (travelParameters.Length != 4 ||
            travelParameters[0].ParameterType.FullName != "DV.Teleporters.FastTravelDestination" ||
            !travelParameters[1].ParameterType.IsGenericType ||
            travelParameters[1].ParameterType.GetGenericTypeDefinition() != typeof(Func<,>) ||
            travelParameters[1].ParameterType.GetGenericArguments()[0].FullName != "UnityEngine.Transform" ||
            travelParameters[1].ParameterType.GetGenericArguments()[1] != typeof(System.Collections.IEnumerator) ||
            travelParameters[2].ParameterType != typeof(bool) || travelParameters[3].ParameterType != typeof(int) ||
            withoutLoco.GetParameters().Length != 1 ||
            withoutLoco.GetParameters()[0].ParameterType.FullName != "UnityEngine.Transform")
            throw new MissingMethodException("Native fast travel argument signature changed.");
        RequireMethod(Path.Combine(managedDirectory, "Assembly-CSharp.dll"),
            "DV.Openables.DoorsAndWindowsController", "AnythingOpen", BindingFlags.Public | BindingFlags.Instance);
        RequireMethod(Path.Combine(managedDirectory, "Assembly-CSharp.dll"),
            "DV.Simulation.Cars.FireboxSimController", "get_FireboxDoorOpening", BindingFlags.Public | BindingFlags.Instance);
        RequireMethod(Path.Combine(managedDirectory, "DV.Simulation.dll"),
            "LocoSim.Implementations.SimulationFlow", "TryGetPort", BindingFlags.Public | BindingFlags.Instance);
        RequireMethod(Path.Combine(managedDirectory, "DV.Inventory.dll"),
            "DV.InventorySystem.Inventory", "RemoveMoney", BindingFlags.Public | BindingFlags.Instance);
        RequireMethod(Path.Combine(managedDirectory, "DV.Inventory.dll"),
            "DV.InventorySystem.Inventory", "SetMoney", BindingFlags.Public | BindingFlags.Instance);
        if (multiplayerDirectory != null) RequireMethod(Path.Combine(multiplayerDirectory, "MultiplayerAPI.dll"),
            "MPAPI.Interfaces.IServer", "RegisterSerializablePacket", BindingFlags.Public | BindingFlags.Instance);
        if (multiplayerDirectory != null) RequireMethod(Path.Combine(multiplayerDirectory, "MultiplayerAPI.dll"),
            "MPAPI.Interfaces.IClient", "SendSerializablePacketToServer", BindingFlags.Public | BindingFlags.Instance);
        Console.WriteLine("OK: required Derail Valley and Multiplayer API hooks are present");
    }

    private static void RequireMethod(string assemblyPath, string typeName, string methodName, BindingFlags flags)
    {
        var assembly = Assembly.LoadFrom(assemblyPath);
        var type = assembly.GetType(typeName, true);
        if (type.GetMethods(flags).Length == 0)
            throw new MissingMethodException(typeName, methodName);
        foreach (var method in type.GetMethods(flags))
            if (method.Name == methodName) return;
        throw new MissingMethodException(typeName, methodName);
    }

    private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
    {
        var requested = new AssemblyName(args.Name);
        if (requested.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var directory in searchDirectories)
        {
            var candidate = Path.Combine(directory, requested.Name + ".dll");
            if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
        }
        return null;
    }
}
