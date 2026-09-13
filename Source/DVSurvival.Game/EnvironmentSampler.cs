using System;
using System.Collections.Generic;
using DV;
using DV.CabControls;
using DV.InventorySystem;
using DV.Openables;
using DV.Simulation.Cars;
using DV.Simulation.Controllers;
using DV.ThingTypes;
using DV.Utils;
using DV.WeatherSystem;
using DVSurvival.Core;
using UnityEngine;

namespace DVSurvival.Mod
{
    internal sealed class EnvironmentSampler
    {
        private const float WorldObjectScanIntervalSeconds = 30f;
        private const float FireboxScanIntervalSeconds = 5f;
        private const float HeatedBuildingTemperature = 22f;
        private const float FireboxHeatRadiusMeters = 7f;
        private const string CabFanOutputPortId = "cabFan.OUTPUT";
        private readonly SurvivalModSettings settings;
        private readonly CabHeaterSwitchSystem cabHeaters;
        private readonly DvSeasonsTemperatureProvider seasons = new DvSeasonsTemperatureProvider();
        private readonly Dictionary<byte, Vector3> previousRemotePositions = new Dictionary<byte, Vector3>();
        private Shop[] shops = new Shop[0];
        private BedSleeping[] beds = new BedSleeping[0];
        private FireboxSimController[] fireboxes = new FireboxSimController[0];
        private readonly Collider[] buildingProbe = new Collider[64];
        private readonly Collider[] trainProbe = new Collider[48];
        private WeatherDriver weather;
        private float nextShopScan;
        private float nextBedScan;
        private float nextWeatherProbe;
        private float nextFireboxScan;
        private Transform cachedPlayerTransform;
        private CustomFirstPersonController cachedPlayerController;
        private TrainCar cachedCar;
        private GameObject cachedInterior;
        private GameObject cachedExternal;
        private float nextCabControlScan;
        private DoorsAndWindowsController[] cachedDoorsAndWindows = new DoorsAndWindowsController[0];
        private FireboxSimController cachedFirebox;
        private EngineOnReader cachedEngineOn;
        private ControlImplBase cachedCabFanControl;
        private ControlImplBase cachedDm1uHeaterControl;
        private LocoSim.Implementations.SimulationFlow cachedClimateFlow;
        private LocoSim.Implementations.Port cachedRpmNormalized, cachedIdleRpmNormalized, cachedEngineOnPort;
        private LocoSim.Implementations.Port cachedCabFanOutputPort;
        private bool cachedCabScanComplete;
        private OpenableControl[] cachedOpenables = new OpenableControl[0];
        private ControlImplBase[] cachedOpeningControls = new ControlImplBase[0];
        private readonly Dictionary<TrainCar, CabHistory> cabHistory = new Dictionary<TrainCar, CabHistory>();
        public string CabinStatus { get; private set; } = string.Empty;

        private sealed class CabHistory
        {
            public readonly CabinClimate Climate = new CabinClimate();
            public float LastSampleTime;
        }

        public EnvironmentSampler(SurvivalModSettings settings, CabHeaterSwitchSystem cabHeaters)
        {
            this.settings = settings;
            this.cabHeaters = cabHeaters ?? throw new ArgumentNullException(nameof(cabHeaters));
        }

        public SurvivalEnvironment SampleLocal(float gameHours)
        {
            CabinStatus = string.Empty;
            var transform = PlayerManager.PlayerTransform;
            if (transform == null) return Default(gameHours);
            var inCar = PlayerManager.Car != null;
            var inLocomotive = inCar && PlayerManager.Car.IsLoco;
            if (transform != cachedPlayerTransform)
            {
                cachedPlayerTransform = transform;
                cachedPlayerController = transform.GetComponent<CustomFirstPersonController>();
                if (cachedPlayerController == null)
                    cachedPlayerController = transform.GetComponentInChildren<CustomFirstPersonController>(true);
                if (cachedPlayerController == null)
                    cachedPlayerController = transform.GetComponentInParent<CustomFirstPersonController>();
            }
            var controller = cachedPlayerController;
            var position = controller == null ? transform.position : controller.transform.position;
            var activity = 0f;
            if (controller != null && controller.m_Input.sqrMagnitude > 0.01f)
                activity = controller.m_IsWalking ? 0.45f : 1f;
            var underwater = controller != null && controller.underwater;
            return Sample(position, gameHours, inCar, inLocomotive, activity, underwater, true);
        }

        public SurvivalEnvironment SampleRemote(SurvivalPlayerInfo player, float gameHours, float realSeconds)
        {
            if (player == null) return Default(gameHours);
            var position = new Vector3(player.PositionX, player.PositionY, player.PositionZ);
            var activity = 0f;
            Vector3 previous;
            if (previousRemotePositions.TryGetValue(player.PlayerId, out previous) && realSeconds > 0.01f && !player.IsOnCar)
            {
                var speed = Vector3.Distance(position, previous) / realSeconds;
                if (speed < 30f) activity = Mathf.Clamp01(speed / 7f);
            }
            previousRemotePositions[player.PlayerId] = position;
            return Sample(position, gameHours, player.IsOnCar, player.OccupiedCarIsLocomotive,
                activity, false, false);
        }

        public bool IsLocalNearShop()
        {
            return PlayerManager.PlayerTransform != null &&
                IsNear(PlayerManager.PlayerTransform.position, settings.ShopUseDistance, true);
        }

        public bool IsPlayerNearShop(SurvivalPlayerInfo player)
        {
            if (player == null) return false;
            return IsNear(new Vector3(player.PositionX, player.PositionY, player.PositionZ),
                settings.ShopUseDistance, true);
        }

        public bool IsPlayerNearBed(SurvivalPlayerInfo player)
        {
            if (player == null) return false;
            return IsNear(new Vector3(player.PositionX, player.PositionY, player.PositionZ),
                settings.BedValidationDistance, false);
        }

        public bool IsLocalNearBed()
        {
            return PlayerManager.PlayerTransform != null &&
                IsNear(PlayerManager.PlayerTransform.position, settings.BedValidationDistance, false);
        }

        public bool IsPlayerNearNativeBed(SurvivalPlayerInfo player, SurvivalActionRequest request)
        {
            if (player == null || !player.IsLoaded) return false;
            if (IsPlayerNearBed(player)) return true;
            // Buildings/cab interiors near a remote client need not be streamed on the host.
            // Check the native bed anchor against MP's authenticated player position instead
            // of accepting all requests when the host's local bed cache is empty.
            var absolute = new Vector3(player.PositionX, player.PositionY, player.PositionZ) - WorldMover.currentMove;
            return NativeBedValidation.IsNearReportedBed(request, absolute.x, absolute.y,
                absolute.z, settings.BedValidationDistance);
        }

        public bool IsPlayerNearMovingTrain(SurvivalPlayerInfo player, float minimumSpeedKmh)
        {
            if (player == null) return false;
            var position = new Vector3(player.PositionX, player.PositionY, player.PositionZ);
            var count = Physics.OverlapSphereNonAlloc(position, 3.25f, trainProbe,
                TrainColliderLayerMask(), QueryTriggerInteraction.Collide);
            for (var index = 0; index < count; index++)
            {
                var collider = trainProbe[index];
                trainProbe[index] = null;
                if (collider == null) continue;
                var car = collider.GetComponentInParent<TrainCar>();
                if (car != null && car.GetAbsSpeed() * 3.6f > minimumSpeedKmh) return true;
            }
            return false;
        }

        public double PlayerMoney
        {
            get
            {
                var inventory = SingletonBehaviour<Inventory>.Instance;
                return inventory == null ? 0d : inventory.PlayerMoney;
            }
        }

        public bool TrySpendMoney(int amount)
        {
            if (amount < 0) return false;
            var inventory = SingletonBehaviour<Inventory>.Instance;
            return inventory != null && inventory.RemoveMoney(amount);
        }

        public double DeductMoneyUpTo(int amount)
        {
            if (amount <= 0) return 0d;
            var inventory = SingletonBehaviour<Inventory>.Instance;
            if (inventory == null) return 0d;
            var deducted = Math.Min(amount, Math.Max(0d, inventory.PlayerMoney));
            if (deducted <= 0d) return 0d;
            if (deducted >= amount && inventory.RemoveMoney(amount)) return amount;
            return inventory.SetMoney(Math.Max(0d, inventory.PlayerMoney - deducted)) ? deducted : 0d;
        }

        public void RefundMoney(int amount)
        {
            if (amount <= 0) return;
            var inventory = SingletonBehaviour<Inventory>.Instance;
            if (inventory != null) inventory.AddMoney(amount);
        }

        public void ForgetPlayer(byte playerId)
        {
            previousRemotePositions.Remove(playerId);
        }

        public void Reset()
        {
            shops = new Shop[0];
            beds = new BedSleeping[0];
            fireboxes = new FireboxSimController[0];
            weather = null;
            nextShopScan = 0f;
            nextBedScan = 0f;
            nextWeatherProbe = 0f;
            nextFireboxScan = 0f;
            cachedPlayerTransform = null;
            cachedPlayerController = null;
            InvalidateCabControls();
            cabHistory.Clear();
            previousRemotePositions.Clear();
            seasons.Reset();
        }

        /// <summary>
        /// Invalidates only locomotive hierarchy/control references. Cabin thermal history is
        /// deliberately retained so reconnecting does not make the air temperature jump.
        /// </summary>
        public void InvalidateCabControls()
        {
            cachedCar = null;
            cachedInterior = null;
            cachedExternal = null;
            nextCabControlScan = 0f;
            cachedDoorsAndWindows = new DoorsAndWindowsController[0];
            cachedFirebox = null;
            cachedEngineOn = null;
            cachedCabFanControl = null;
            cachedDm1uHeaterControl = null;
            cachedClimateFlow = null;
            cachedRpmNormalized = cachedIdleRpmNormalized = cachedEngineOnPort = null;
            cachedCabFanOutputPort = null;
            cachedCabScanComplete = false;
            cachedOpenables = new OpenableControl[0];
            cachedOpeningControls = new ControlImplBase[0];
        }

        public float GetDayLengthMinutes()
        {
            ProbeWeather();
            try
            {
                if (weather != null) return Mathf.Clamp((float)weather.DayLengthInMinutes, 1f, 1440f);
            }
            catch
            {
                // Fall through to the game parameters.
            }
            try { return Mathf.Clamp(Globals.G.GameParams.DayLengthInMinutes, 1f, 1440f); }
            catch { return 144f; }
        }

        public bool IsGameTimeOverridden
        {
            get
            {
                ProbeWeather();
                try { return weather != null && weather.TimeOfDayHours.IsOverridden; }
                catch { return false; }
            }
        }

        public bool TryGetGameDateTime(out DateTime value)
        {
            value = default(DateTime);
            ProbeWeather();
            try
            {
                if (weather == null || weather.manager == null || weather.manager.todSky == null ||
                    weather.manager.todSky.Cycle == null)
                    return false;
                value = weather.manager.RealDateTime;
                return value != DateTime.MinValue && value != DateTime.MaxValue;
            }
            catch { return false; }
        }

        private SurvivalEnvironment Sample(Vector3 position, float gameHours, bool inCar,
            bool inLocomotive, float activity, bool underwater, bool isLocalPlayer)
        {
            ProbeWeather();
            var rain = weather == null ? 0f : Mathf.Clamp01(weather.RainValue.CurrentValue);
            var wind = weather == null ? 0f : Mathf.Clamp((float)weather.WindSpeed, 0f, 80f);
            var roof = inCar || HasRoof(position);
            var exposure = inCar ? 0.10f : (roof ? 0.28f : 1f);
            var shelterWarmth = inCar ? 2.5f : 0f;
            bool isWinter;
            var ambient = GetAmbientTemperature(position, rain, out isWinter);
            var thermalRecovery = 1f;
            // Exact cab controls exist only in the local player's loaded interior. Remote clients
            // report their measured environment to the authoritative host separately.
            if (isLocalPlayer && inLocomotive && PlayerManager.Car != null)
            {
                ApplyLocomotiveClimate(PlayerManager.Car, position, ambient, ref ambient, ref rain,
                    ref wind, ref exposure, ref shelterWarmth, ref thermalRecovery);
            }
            else if (!inCar && IsInsideHeatedBuilding(BuildingProbePosition(position, isLocalPlayer), roof))
            {
                ambient = HeatedBuildingTemperature;
                rain = 0f;
                wind = 0f;
                exposure = 0.03f;
                shelterWarmth = 0f;
                thermalRecovery = 2.5f;
            }
            else if (!inCar)
            {
                ApplyNearbyFireboxHeat(position, ref ambient);
            }
            if (underwater)
            {
                exposure = 1f;
                ambient = Math.Min(ambient, 8f);
                rain = 1f;
                thermalRecovery = 1f;
            }
            return new SurvivalEnvironment
            {
                GameHours = gameHours,
                AmbientTemperatureCelsius = ambient,
                RainIntensity = rain,
                WindSpeedMetersPerSecond = wind,
                Exposure = exposure,
                Activity = Mathf.Clamp01(activity),
                ShelterWarmthCelsius = shelterWarmth,
                ThermalRecoveryMultiplier = thermalRecovery,
                IsWinter = isWinter
            };
        }

        private void ApplyLocomotiveClimate(TrainCar car, Vector3 position, float outside,
            ref float ambient, ref float rain, ref float wind, ref float exposure,
            ref float shelterWarmth, ref float thermalRecovery)
        {
            RefreshCabComponents(car);
            var open = false;
            for (int i = 0; !open && i < cachedDoorsAndWindows.Length; i++)
                try { open = cachedDoorsAndWindows[i] != null && cachedDoorsAndWindows[i].AnythingOpen(); }
                catch { /* Other initialized controllers/direct controls can still report openings. */ }
            // Direct cached controls also cover interiors whose controller has not initialized its entries.
            for (int i = 0; !open && i < cachedOpenables.Length; i++)
            {
                var control = cachedOpeningControls[i];
                var opening = cachedOpenables[i];
                if (control == null || opening == null) continue;
                open = opening.closedAtZero ? control.Value >= 0.1f : control.Value <= 0.9f;
            }
            var isDm1u = car != null && car.carType == TrainCarType.LocoDM1U;
            var isDm3 = car != null && car.carType == TrainCarType.LocoDM3;
            var isVanillaDieselHeater = IsVanillaDieselHeater(car);
            var fanValue = cachedCabFanControl == null ? 0f : cachedCabFanControl.Value;
            var fanOutputValue = cachedCabFanOutputPort == null ? 0f : cachedCabFanOutputPort.Value;
            var fanOn = !float.IsNaN(fanValue) && !float.IsInfinity(fanValue) &&
                (isDm1u ? fanValue >= 0.5f : fanValue > 0.1f);
            if (isVanillaDieselHeater && !isDm3)
                fanOn = !float.IsNaN(fanOutputValue) && !float.IsInfinity(fanOutputValue) &&
                    fanOutputValue > 0.1f;
            var heaterValue = cachedDm1uHeaterControl == null ? 0f : cachedDm1uHeaterControl.Value;
            var dm1uHeaterOn = isDm1u && !float.IsNaN(heaterValue) &&
                !float.IsInfinity(heaterValue) && heaterValue >= 0.5f;
            var heaterPower = isVanillaDieselHeater ? cabHeaters.GetLevel(car) : 1f;
            var vanillaDieselHeaterOn = isVanillaDieselHeater && heaterPower > 0f;
            float? steamTarget = null;
            // DM1U is diesel-only. Refuse any stray/custom FireboxSimController so it cannot
            // bypass the stock Heating Rotary fail-closed gate.
            if (!isDm1u && cachedFirebox != null)
            {
                var temperature = 0f;
                var hasTemperature = TryGetFireboxTemperature(car, out temperature);
                var heat = hasTemperature
                    ? Mathf.InverseLerp(80f, 1050f, temperature)
                    : Mathf.Max(cachedFirebox.CombustionRateNormalized,
                        cachedFirebox.NormalizedFireboxContents * 0.45f);
                if (!cachedFirebox.IsFireOn) heat *= 0.15f;
                var distanceFactor = Mathf.Clamp01(1f -
                    Vector3.Distance(position, cachedFirebox.transform.position) / FireboxHeatRadiusMeters);
                distanceFactor = Mathf.Max(0.38f, distanceFactor);
                var doorFactor = Mathf.Lerp(0.62f, 1.35f,
                    Mathf.Clamp01(cachedFirebox.FireboxDoorOpening));
                steamTarget = outside + heat * 38f * distanceFactor * doorFactor;
            }
            var running = false;
            var load = 0f;
            try
            {
                running = cachedEngineOnPort != null ? cachedEngineOnPort.Value > 0.5f :
                    cachedEngineOn != null && cachedEngineOn.IsOn;
                if (cachedRpmNormalized != null)
                {
                    var idle = cachedIdleRpmNormalized == null ? 0.25f : cachedIdleRpmNormalized.Value;
                    load = Mathf.InverseLerp(Mathf.Clamp(idle, 0f, 0.95f), 1f, cachedRpmNormalized.Value);
                    if (float.IsNaN(load) || float.IsInfinity(load)) load = 0f;
                }
            }
            catch { running = false; }
            CabHistory history;
            if (!cabHistory.TryGetValue(car, out history))
            {
                // Bounded cache, no scene searches and no per-frame dictionary allocations.
                if (cabHistory.Count >= 32) cabHistory.Clear();
                history = new CabHistory { LastSampleTime = Time.time };
                cabHistory.Add(car, history);
            }
            var elapsed = Mathf.Clamp(Time.time - history.LastSampleTime, 0f, 5f);
            history.LastSampleTime = Time.time;
            var heatingRateMultiplier = isDm1u && !fanOn ? 0.25f : isDm3 ? heaterPower : 1f;
            var heaterAllowsEngineHeat = isDm1u ? dm1uHeaterOn :
                !isVanillaDieselHeater || vanillaDieselHeaterOn;
            // A second adapter-level gate ensures DM1U engine heat begins only after the
            // physical heater is enabled; CabinClimate independently enforces the same rule.
            ambient = history.Climate.Advance(elapsed, outside, running && heaterAllowsEngineHeat,
                load, open, fanOn, steamTarget, heatingRateMultiplier, heaterAllowsEngineHeat, heaterPower);
            float sharedCabAir = settings.UseDvSeasonsTemperature && SeasonsCabHeating.Active
                ? SeasonsCabHeating.GetCabinTemperature(car) : float.NaN;
            if (!float.IsNaN(sharedCabAir) && !float.IsInfinity(sharedCabAir)) ambient = sharedCabAir;
            CabinStatus = ModLocalization.Text("Кабина: проёмы ", "Cabin: openings ") +
                (open ? ModLocalization.Text("открыты", "open") : ModLocalization.Text("закрыты", "closed")) +
                " [" + cachedOpenables.Length + "]; " +
                (isDm1u
                    ? ModLocalization.Text("отопитель ", "heater ") +
                        (dm1uHeaterOn ? ModLocalization.Text("включён", "on") : ModLocalization.Text("выключен", "off")) +
                        ModLocalization.Text("; вентилятор ", "; fan ") +
                        (fanOn ? ModLocalization.Text("включён", "on") : ModLocalization.Text("выключен", "off"))
                    : isVanillaDieselHeater
                        ? ModLocalization.Text("отопитель ", "heater ") +
                            (vanillaDieselHeaterOn ? ModLocalization.Text("включён", "on") : ModLocalization.Text("выключен", "off")) +
                            ModLocalization.Text("; вентилятор ", "; fan ") +
                            (fanOn ? ModLocalization.Text("включён", "on") : ModLocalization.Text("выключен", "off")) +
                            ModLocalization.Text("; двигатель ", "; engine ") +
                            (running ? ModLocalization.Text("включён", "on") : ModLocalization.Text("выключен", "off"))
                    : ModLocalization.Text("двигатель ", "engine ") +
                        (running ? ModLocalization.Text("включён", "on") : ModLocalization.Text("выключен", "off"))) +
                ModLocalization.Text("; улица ", "; outdoors ") + ModLocalization.Number(outside, "F1") + " °C";
            ambient = Mathf.Clamp(ambient, SurvivalEnvironment.MinimumAirTemperature, SurvivalEnvironment.MaximumAirTemperature);
            rain *= open ? 0.22f : 0f;
            wind *= open ? 0.65f : 0f;
            exposure = open ? 0.68f : 0.05f;
            shelterWarmth = 0f;
            thermalRecovery = open ? 1.15f : 2.15f;
        }

        private void RefreshCabComponents(TrainCar car)
        {
            var interior = car == null ? null : car.loadedInterior;
            var external = car == null ? null : car.loadedExternalInteractables;
            var flow = car == null || car.SimController == null ? null : car.SimController.SimulationFlow;
            var isVanillaDieselHeater = IsVanillaDieselHeater(car);
            var heaterControlReady = !isVanillaDieselHeater || interior == null;
            var sameContext = car == cachedCar && interior == cachedInterior && external == cachedExternal &&
                flow == cachedClimateFlow;
            if (sameContext && (cachedCabScanComplete || Time.realtimeSinceStartup < nextCabControlScan)) return;
            nextCabControlScan = Time.realtimeSinceStartup + 3f;
            cachedCar = car;
            cachedInterior = interior;
            cachedExternal = external;
            if (isVanillaDieselHeater && interior != null)
            {
                heaterControlReady = cabHeaters.Ensure(car);
                if (!heaterControlReady) heaterControlReady = cabHeaters.IsSetupSettled(car);
            }
            // Persistent cabin and external door objects are not necessarily children of loadedInterior.
            var controllers = new HashSet<DoorsAndWindowsController>();
            var openings = new HashSet<OpenableControl>();
            var roots = new[] { car == null ? null : car.gameObject, interior, external,
                car == null || car.interior == null ? null : car.interior.gameObject };
            foreach (var root in roots)
            {
                if (root == null) continue;
                foreach (var value in root.GetComponentsInChildren<DoorsAndWindowsController>(true))
                {
                    controllers.Add(value);
                    if (value.entries != null)
                        foreach (var opening in value.entries) if (opening != null) openings.Add(opening);
                }
                foreach (var opening in root.GetComponentsInChildren<OpenableControl>(true)) openings.Add(opening);
            }
            cachedDoorsAndWindows = new DoorsAndWindowsController[controllers.Count];
            controllers.CopyTo(cachedDoorsAndWindows);
            cachedFirebox = car == null ? null : car.GetComponentInChildren<FireboxSimController>(true);
            cachedEngineOn = car == null ? null : car.GetComponentInChildren<EngineOnReader>(true);
            cachedClimateFlow = flow;
            cachedRpmNormalized = cachedIdleRpmNormalized = cachedEngineOnPort = null;
            cachedCabFanOutputPort = null;
            cachedCabScanComplete = false;
            if (flow != null)
            {
                // Build 99 exposes these ports on DE2, DH4 and DE6. TryGetPort's final
                // argument only suppresses an empty-id diagnostic; it does not make a
                // missing non-empty port silent, so never probe unrelated locomotives.
                if (isVanillaDieselHeater && car.carType != TrainCarType.LocoDM3)
                {
                    flow.TryGetPort(CabFanOutputPortId, out cachedCabFanOutputPort, false);
                }
                foreach (var component in flow.OrderedSimComps)
                {
                    var direct = component as LocoSim.Implementations.DieselEngineDirect;
                    var power = component as LocoSim.Implementations.DieselEnginePowerSource;
                    var drive = component as LocoSim.Implementations.DieselEngineDirectDrive;
                    if (direct != null)
                    {
                        cachedRpmNormalized = direct.engineRpmNormalizedReadOut;
                        cachedIdleRpmNormalized = direct.engineIdleRpmNormalizedReadOut;
                        cachedEngineOnPort = direct.engineOnReadOut;
                    }
                    else if (power != null)
                    {
                        cachedRpmNormalized = power.engineRpmNormalizedReadOut;
                        cachedIdleRpmNormalized = power.engineIdleRpmNormalizedReadOut;
                        cachedEngineOnPort = power.engineOnReadOut;
                    }
                    else if (drive != null)
                    {
                        cachedRpmNormalized = drive.engineRpmNormalizedReadOut;
                        cachedIdleRpmNormalized = drive.engineIdleRpmNormalizedReadOut;
                        cachedEngineOnPort = drive.engineOnReadOut;
                    }
                    if (cachedEngineOnPort != null) break;
                }
            }
            cachedOpenables = new OpenableControl[openings.Count];
            openings.CopyTo(cachedOpenables);
            cachedOpeningControls = new ControlImplBase[cachedOpenables.Length];
            for (int i = 0; i < cachedOpenables.Length; i++)
                cachedOpeningControls[i] = cachedOpenables[i].GetComponent<ControlImplBase>();
            cachedCabFanControl = null;
            cachedDm1uHeaterControl = null;
            if (interior == null) return;
            foreach (var control in interior.GetComponentsInChildren<ControlImplBase>(true))
            {
                if (control == null) continue;
                if (cachedCabFanControl == null && IsCabFanName(control.transform))
                    cachedCabFanControl = control;
                if (car != null && car.carType == TrainCarType.LocoDM1U &&
                    cachedDm1uHeaterControl == null && IsDm1uHeaterName(control.transform))
                    cachedDm1uHeaterControl = control;
                if (cachedCabFanControl != null && cachedDm1uHeaterControl != null) break;
            }
            var isDm1u = car != null && car.carType == TrainCarType.LocoDM1U;
            cachedCabScanComplete = isDm1u
                ? cachedCabFanControl != null && cachedDm1uHeaterControl != null
                // Bootstrap failures retry at the existing three-second cadence. Successful
                // setup and a bounded final failure both stop further hierarchy rescans.
                : heaterControlReady;
        }

        private static bool TryGetFireboxTemperature(TrainCar car, out float temperature)
        {
            temperature = 0f;
            try
            {
                if (car == null || car.SimController == null || car.SimController.SimulationFlow == null)
                    return false;
                LocoSim.Implementations.Port port;
                if (!car.SimController.SimulationFlow.TryGetPort("firebox.TEMPERATURE", out port, false) ||
                    port == null)
                    return false;
                temperature = port.Value;
                return !float.IsNaN(temperature) && !float.IsInfinity(temperature) && temperature >= 0f;
            }
            catch { return false; }
        }

        private void ApplyNearbyFireboxHeat(Vector3 position, ref float ambient)
        {
            RefreshFireboxes();
            var best = ambient;
            foreach (var firebox in fireboxes)
            {
                if (firebox == null || !firebox.IsFireOn) continue;
                var distanceFactor = Mathf.Clamp01(1f -
                    Vector3.Distance(position, firebox.transform.position) / FireboxHeatRadiusMeters);
                if (distanceFactor <= 0f) continue;
                var heat = Mathf.Max(firebox.CombustionRateNormalized,
                    firebox.NormalizedFireboxContents * 0.45f);
                var doorFactor = Mathf.Lerp(0.35f, 1.25f,
                    Mathf.Clamp01(firebox.FireboxDoorOpening));
                best = Mathf.Max(best, ambient + heat * 24f * distanceFactor * doorFactor);
            }
            ambient = Mathf.Clamp(best, SurvivalEnvironment.MinimumAirTemperature, SurvivalEnvironment.MaximumAirTemperature);
        }

        private bool IsInsideHeatedBuilding(Vector3 position, bool hasRoof)
        {
            // Vanilla's TutorialPlayerDetector checks the camera against the office volume with
            // Collider.ClosestPoint. Use the same point and containment rule. Runtime clones do
            // not reliably preserve the prefab's Ignore Raycast layer, so query all layers in a
            // tiny, non-allocating sphere instead of scanning the world or relying on a layer.
            var count = Physics.OverlapSphereNonAlloc(position, 0.2f, buildingProbe,
                ~0, QueryTriggerInteraction.Collide);
            for (var index = 0; index < count; index++)
            {
                var collider = buildingProbe[index];
                buildingProbe[index] = null;
                if (collider != null && IsHeatedBuildingName(collider.transform) &&
                    (collider.ClosestPoint(position) - position).sqrMagnitude < 0.0001f)
                    return true;
            }
            // Shops do not all have a dedicated interior trigger. Keep the proximity fallback,
            // but require a roof so the heated zone cannot leak into the street.
            return hasRoof && IsNear(position, 22f, true);
        }

        private static Vector3 BuildingProbePosition(Vector3 fallback, bool isLocalPlayer)
        {
            if (isLocalPlayer && PlayerManager.PlayerCamera != null)
                return PlayerManager.PlayerCamera.transform.position;
            // Remote player reports normally supersede host-side sampling. This offset keeps the
            // fallback at head/chest height, matching the local camera containment check.
            return fallback + Vector3.up * 1.55f;
        }

        private static int TrainColliderLayerMask()
        {
            var layer = LayerMask.NameToLayer("Train_Big_Collider");
            return layer >= 0 ? 1 << layer : 1 << 10;
        }

        private static bool IsHeatedBuildingName(Transform transform)
        {
            for (var depth = 0; transform != null && depth < 7; depth++, transform = transform.parent)
            {
                var normalized = NormalizeName(transform.name);
                if (normalized.Contains("stationoffice") || normalized.Contains("itemshopinterior") ||
                    normalized.Contains("shopinterior") || normalized.Contains("stationofficeplayerdetector"))
                    return true;
            }
            return false;
        }

        private static bool IsCabFanName(Transform transform)
        {
            for (var depth = 0; transform != null && depth < 4; depth++, transform = transform.parent)
            {
                var normalized = NormalizeName(transform.name);
                if (normalized.Contains("cabfan") || normalized.Contains("fanswitch")) return true;
            }
            return false;
        }

        private static bool IsDm1uHeaterName(Transform transform)
        {
            // The stock DM1U control hierarchy is RightCluster/Heating Rotary/C_Heating Rotary.
            // Match that unique control rather than other locomotives' decorative CabHeater labels.
            for (var depth = 0; transform != null && depth < 4; depth++, transform = transform.parent)
            {
                var normalized = NormalizeName(transform.name);
                if (normalized == "cheatingrotary" || normalized == "heatingrotary") return true;
            }
            return false;
        }

        private static bool IsVanillaDieselHeater(TrainCar car)
        {
            if (car == null) return false;
            return car.carType == TrainCarType.LocoShunter ||
                car.carType == TrainCarType.LocoDH4 ||
                car.carType == TrainCarType.LocoDM3 ||
                car.carType == TrainCarType.LocoDiesel;
        }

        private static string NormalizeName(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty :
                value.Replace("_", string.Empty).Replace("-", string.Empty)
                    .Replace(" ", string.Empty).ToLowerInvariant();
        }

        private float GetAmbientTemperature(Vector3 position, float rain, out bool isWinter)
        {
            var seasonal = 18f;
            var fromSeasons = settings.UseDvSeasonsTemperature && seasons.TryGetTemperature(out seasonal);
            var date = DateTime.Now;
            try
            {
                if (weather != null && weather.manager != null) date = weather.manager.DateTime;
            }
            catch
            {
                // Use the system clock only as a last-resort fallback.
            }
            if (!fromSeasons || !seasons.TryGetWinter(out isWinter))
                isWinter = date.Month == 12 || date.Month <= 2;
            // Seasons 0.3.13+ already includes live weather and the daily cycle.
            if (fromSeasons && seasons.IncludesWeather) return seasonal;
            if (!fromSeasons)
            {
                var yearPhase = 2d * Math.PI * (date.DayOfYear - 172d) / 365.25d;
                seasonal = 11f + 10f * (float)Math.Cos(yearPhase);
            }
            var hour = date.TimeOfDay.TotalHours;
            var diurnal = 3.8f * (float)Math.Sin(2d * Math.PI * (hour - 9d) / 24d);
            var altitudeCooling = Mathf.Clamp(-(position.y - 200f) * 0.0035f, -5f, 2f);
            return Mathf.Clamp(seasonal + diurnal + altitudeCooling - rain * 2.5f,
                SurvivalEnvironment.MinimumAirTemperature, SurvivalEnvironment.MaximumAirTemperature);
        }

        private bool IsNear(Vector3 position, float distance, bool shop)
        {
            var maxSqr = distance * distance;
            if (shop)
            {
                RefreshShops();
                foreach (var value in shops)
                    if (value != null && (value.transform.position - position).sqrMagnitude <= maxSqr) return true;
            }
            else
            {
                RefreshBeds();
                foreach (var value in beds)
                    if (value != null && ((value.transform.position - position).sqrMagnitude <= maxSqr ||
                        (value.pillowTarget != null && (value.pillowTarget.position - position).sqrMagnitude <= maxSqr))) return true;
            }
            return false;
        }

        private void RefreshShops()
        {
            if (Time.realtimeSinceStartup < nextShopScan) return;
            nextShopScan = Time.realtimeSinceStartup + WorldObjectScanIntervalSeconds;
            shops = UnityEngine.Object.FindObjectsOfType<Shop>() ?? new Shop[0];
        }

        private void RefreshBeds()
        {
            if (Time.realtimeSinceStartup < nextBedScan) return;
            nextBedScan = Time.realtimeSinceStartup + WorldObjectScanIntervalSeconds;
            beds = UnityEngine.Object.FindObjectsOfType<BedSleeping>() ?? new BedSleeping[0];
        }

        private void RefreshFireboxes()
        {
            if (Time.realtimeSinceStartup < nextFireboxScan) return;
            nextFireboxScan = Time.realtimeSinceStartup + FireboxScanIntervalSeconds;
            fireboxes = UnityEngine.Object.FindObjectsOfType<FireboxSimController>() ??
                new FireboxSimController[0];
        }

        private void ProbeWeather()
        {
            if (weather != null || Time.realtimeSinceStartup < nextWeatherProbe) return;
            nextWeatherProbe = Time.realtimeSinceStartup + 5f;
            weather = UnityEngine.Object.FindObjectOfType<WeatherDriver>();
        }

        private static bool HasRoof(Vector3 position)
        {
            RaycastHit hit;
            return Physics.Raycast(position + Vector3.up * 0.25f, Vector3.up, out hit, 15f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private static SurvivalEnvironment Default(float gameHours)
        {
            return new SurvivalEnvironment
            {
                GameHours = gameHours,
                AmbientTemperatureCelsius = 18f,
                Exposure = 0.5f
            };
        }
    }
}
