using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DV.Teleporters;
using DV.Utils;
using DVSurvival.Core;
using UnityEngine;
using UnityModManagerNet;

namespace DVSurvival.Mod
{
    internal sealed class SurvivalRuntime : IDisposable
    {
        private const float SimulationIntervalSeconds = 1f;
        private const float StateBroadcastIntervalSeconds = 2f;
        private const float HelloRetrySeconds = 3f;
        private const float SaveCheckpointSeconds = 30f;
        private const float LocalEnvironmentIntervalSeconds = 0.5f;
        private const float AppUtilProbeIntervalSeconds = 5f;
        private const float SessionPrepareRetrySeconds = 1f;
        private const float EnvironmentReportIntervalSeconds = 1f;
        private const float EnvironmentReportTimeoutSeconds = 3.5f;
        private const float TraumaCooldownSeconds = 1.25f;
        private const float HomeRespawnRetrySeconds = 0.5f;
        private const int DeathPenalty = 5000;
        private readonly UnityModManager.ModEntry entry;
        private readonly SurvivalModSettings settings;
        private readonly CabHeaterSwitchSystem cabHeaters;
        private readonly EnvironmentSampler environment;
        private readonly ISurvivalNetworkBridge network;
        private readonly Dictionary<byte, SurvivalPlayerRecord> activeRecords =
            new Dictionary<byte, SurvivalPlayerRecord>();
        private readonly Dictionary<byte, PendingHello> pendingHellos =
            new Dictionary<byte, PendingHello>();
        private readonly Dictionary<byte, float> nextActionTimes = new Dictionary<byte, float>();
        private readonly Dictionary<byte, float> nextTraumaTimes = new Dictionary<byte, float>();
        private readonly Dictionary<byte, RemoteEnvironmentRecord> remoteEnvironments =
            new Dictionary<byte, RemoteEnvironmentRecord>();
        private readonly Dictionary<string, float> confirmedCabHeaterStates =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly GameCalendarClock gameCalendarClock = new GameCalendarClock();
        private readonly SleepCalendarReconciler sleepCalendar = new SleepCalendarReconciler();
        private readonly Dictionary<uint, float> calendarJumpRealSeconds =
            new Dictionary<uint, float>();
        private readonly Dictionary<ulong, PendingNativeSleepAction> pendingNativeSleeps =
            new Dictionary<ulong, PendingNativeSleepAction>();
        private readonly HashSet<string> retiredHostSessions =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> retiredHostSessionOrder = new Queue<string>();
        private SaveGameManager saveManager;
        private SaveGameData sessionSaveData;
        private SurvivalSaveDocument document;
        private SurvivalState currentState;
        private SurvivalEnvironment lastLocalEnvironment = new SurvivalEnvironment();
        private SurvivalHud hud;
        private PersonalProvisionItems personalItems;
        private readonly ActionRequestGate actionRequests = new ActionRequestGate();
        private readonly Dictionary<uint, NativeProvisionToken> pendingItemUses = new Dictionary<uint, NativeProvisionToken>();
        private PlayerImpactMonitor impactMonitor;
        private bool started;
        private bool disposed;
        private bool sessionReady;
        private bool receivedNetworkState;
        private bool lastAuthority;
        private byte lastLocalAuthorityId = byte.MaxValue;
        private float simulationAccumulator;
        private float nextStateBroadcast;
        private float nextHello;
        private float nextSaveCheckpoint;
        private float nextLocalEnvironmentUpdate;
        private float nextAppUtilProbe;
        private float nextSessionPrepareAttempt;
        private float nextEnvironmentReport;
        private float lastCompletedSleepRequestTime = -100f;
        private uint requestSequence;
        private uint stateSequence;
        private uint lastReceivedSequence;
        private uint lastCollapseCount;
        private uint environmentReportSequence;
        private uint calendarJumpSequence;
        private uint lastConnectionGeneration;
        private SurvivalStateMessage lastHostMessage;
        private DV.AppUtil appUtil;
        private bool stateDirty;
        private bool hasLocalStateSnapshot;
        private readonly HomeRespawnFlow homeRespawn = new HomeRespawnFlow();
        private float respawnTraumaGraceUntil;
        private float nextHouseFallbackScan;
        private float nextRespawnWarning;
        private FastTravelDestination cachedHouseDestination;
        private float nextHomeRespawnAttempt;
        private float movementMultiplier = 1f;
        private uint pendingCabHeaterRequestId;
        private string pendingCabHeaterCarId = string.Empty;
        private float pendingCabHeaterPreviousState;
        private float pendingCabHeaterExpiresAt;

        public SurvivalRuntime(UnityModManager.ModEntry entry, SurvivalModSettings settings,
            ISurvivalNetworkBridge bridge = null)
        {
            this.entry = entry ?? throw new ArgumentNullException(nameof(entry));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            settings.Clamp();
            cabHeaters = new CabHeaterSwitchSystem(OnCabHeaterSwitchChanged);
            environment = new EnvironmentSampler(settings, cabHeaters);
            network = bridge ?? CreateNetworkBridge(entry.Path);
            network.HelloReceived += OnHelloReceived;
            network.ActionReceived += OnActionReceived;
            network.EnvironmentReceived += OnEnvironmentReceived;
            network.PlayerDisconnected += OnPlayerDisconnected;
            network.StateReceived += OnStateReceived;
            network.Initialize(entry.Info.Id);
            lastConnectionGeneration = network.ConnectionGeneration;
            currentState = SurvivalState.CreateDefault(settings.ToTuning());
        }

        public SurvivalState CurrentState { get { return currentState; } }
        public SurvivalEnvironment CurrentEnvironment { get { return lastLocalEnvironment; } }
        public string CabinStatus { get { return environment.CabinStatus; } }
        public bool IsSessionReady { get { return sessionReady; } }
        public bool IsRespawning { get { return homeRespawn.Pending || Time.realtimeSinceStartup < respawnTraumaGraceUntil; } }
        internal bool IsHomeTravelPending { get { return homeRespawn.Pending; } }
        public bool HasConfirmedLocalState
        {
            get { return started && sessionReady && (!network.IsSessionActive || network.IsSupported) &&
                (network.IsAuthority || receivedNetworkState); }
        }

        public string NetworkStatus
        {
            get
            {
                if (network.IsSessionActive && !network.IsSupported)
                    return string.Format(ModLocalization.Text(
                        "Multiplayer {0} устарел; требуется {1}",
                        "Multiplayer {0} is outdated; {1} is required"),
                        network.MultiplayerVersion, SurvivalConstants.MinimumMultiplayerVersion);
                if (!network.IsAvailable) return ModLocalization.Text("Локальный режим", "Local mode");
                if (!network.IsSessionActive)
                    return ModLocalization.Text("Multiplayer доступен — одиночная сессия",
                        "Multiplayer available — single-player session");
                return network.IsAuthority
                    ? ModLocalization.Text("Хост рассчитывает личные потребности всех игроков",
                        "Host simulates each player's personal needs")
                    : ModLocalization.Text("Личные потребности синхронизированы с хостом",
                        "Personal needs synchronized with the host");
            }
        }

        public float MovementMultiplier
        {
            get { return sessionReady ? movementMultiplier : 1f; }
        }

        public void Start()
        {
            if (started || disposed) return;
            started = true;
            WorldStreamingInit.LoadingFinished += OnWorldLoaded;
            UnloadWatcher.UnloadRequested += OnWorldUnloading;
            network.SetEnabled(true);
            CreateHud();
            PlayerManager.PlayerTeleportStarted += OnPlayerTeleportStarted;
            var manager = UnityEngine.Object.FindObjectOfType<SaveGameManager>();
            if (!UnloadWatcher.isUnloading && WorldStreamingInit.IsLoaded && manager != null &&
                manager.data != null)
                OnWorldLoaded();
            entry.Logger.Log("Survival runtime started. " + NetworkStatus);
        }

        public void Stop()
        {
            if (!started) return;
            EndSession();
            started = false;
            WorldStreamingInit.LoadingFinished -= OnWorldLoaded;
            UnloadWatcher.UnloadRequested -= OnWorldUnloading;
            network.SetEnabled(false);
            PlayerManager.PlayerTeleportStarted -= OnPlayerTeleportStarted;
            DestroyImpactMonitor();
            DestroyHud();
            environment.Reset();
            ResetCabHeaterControls();
            confirmedCabHeaterStates.Clear();
        }

        public void OnSessionStart()
        {
            if (!started || disposed) return;
            TryPrepareSession();
        }

        public void Tick(float deltaTime)
        {
            if (!started || disposed) return;
            HandleNetworkConnectionGeneration();
            ResolveExpiredCabHeaterRequest();
            if (!sessionReady)
            {
                if (WorldStreamingInit.IsLoaded &&
                    Time.realtimeSinceStartup >= nextSessionPrepareAttempt)
                {
                    nextSessionPrepareAttempt = Time.realtimeSinceStartup + SessionPrepareRetrySeconds;
                    TryPrepareSession();
                }
                return;
            }
            if (UnloadWatcher.isUnloading || saveManager == null)
            {
                OnWorldUnloading();
                return;
            }

            HandleAuthorityTransition();
            UpdateLocalEnvironment();
            ProcessPendingHomeRespawn();
            if (network.IsSessionActive && !network.IsSupported) return;

            if (network.IsSessionActive && !network.IsAuthority &&
                Time.realtimeSinceStartup >= nextEnvironmentReport)
            {
                nextEnvironmentReport = Time.realtimeSinceStartup + EnvironmentReportIntervalSeconds;
                network.SendEnvironment(new SurvivalEnvironmentReport
                {
                    Sequence = ++environmentReportSequence,
                    Environment = lastLocalEnvironment == null
                        ? new SurvivalEnvironment()
                        : lastLocalEnvironment.Clone()
                });
            }

            if (network.IsSessionActive && !network.IsAuthority)
            {
                if (!receivedNetworkState && Time.realtimeSinceStartup >= nextHello)
                {
                    nextHello = Time.realtimeSinceStartup + HelloRetrySeconds;
                    SendHello();
                }
                return;
            }

            EnsureLocalAuthorityRecord();
            ProcessPendingHellos();
            if (deltaTime <= 0f || IsGamePaused()) return;
            simulationAccumulator += Math.Min(deltaTime, 5f);
            if (simulationAccumulator >= SimulationIntervalSeconds)
            {
                var elapsed = simulationAccumulator;
                simulationAccumulator = 0f;
                var fallbackHours = ObserveAndQueueCalendar(elapsed);
                if (fallbackHours > 0f) AdvanceAuthority(elapsed, fallbackHours);
            }
            DrainCalendarOperations(Time.realtimeSinceStartup);
            if (network.IsSessionActive && Time.realtimeSinceStartup >= nextStateBroadcast)
            {
                nextStateBroadcast = Time.realtimeSinceStartup + StateBroadcastIntervalSeconds;
                SendAllStates();
            }
            if (Time.realtimeSinceStartup >= nextSaveCheckpoint)
            {
                nextSaveCheckpoint = Time.realtimeSinceStartup + SaveCheckpointSeconds;
                Checkpoint();
            }
        }

        public void RequestConsume(ProvisionKind kind)
        {
            RequestAction(new SurvivalActionRequest
            {
                RequestId = ++requestSequence,
                Action = SurvivalActionKind.Consume,
                Provision = kind
            });
        }

        private void OnCabHeaterSwitchChanged(string carId, float level)
        {
            if (string.IsNullOrWhiteSpace(carId)) return;
            if (!HasConfirmedLocalState)
            {
                // A newly connected client has no authoritative heater snapshot yet.
                cabHeaters.ApplyConfirmed(carId, 0f);
                return;
            }
            var previous = GetConfirmedCabHeaterState(carId);
            if (pendingCabHeaterRequestId != 0u)
            {
                cabHeaters.ApplyConfirmed(carId, previous);
                return;
            }
            var car = PlayerManager.Car;
            if (!SupportsCabHeater(car) || !string.Equals(car.CarGUID, carId,
                    StringComparison.OrdinalIgnoreCase))
            {
                cabHeaters.ApplyConfirmed(carId, previous);
                return;
            }
            var id = ++requestSequence;
            if (id == 0u) id = ++requestSequence;
            pendingCabHeaterRequestId = id;
            pendingCabHeaterCarId = carId;
            pendingCabHeaterPreviousState = previous;
            pendingCabHeaterExpiresAt = Time.realtimeSinceStartup + 5f;
            if (!RequestAction(new SurvivalActionRequest
            {
                RequestId = id,
                Action = SurvivalActionKind.SetCabHeater,
                Amount = level,
                ItemIdentity = carId
            }))
                RejectPendingCabHeater();
        }

        public void RequestPhysicalConsume(NativeProvisionToken token)
        {
            if (token == null || !HasConfirmedLocalState) return;
            foreach (var pending in pendingItemUses.Where(p => p.Value == null || p.Value == token).Select(p => p.Key).ToArray())
                pendingItemUses.Remove(pending);
            if (pendingItemUses.Count >= 16) { token.Confirm(SurvivalResultCode.RateLimited); return; }
            var id = ++requestSequence;
            pendingItemUses.Add(id, token);
            RequestAction(new SurvivalActionRequest {
                RequestId = id, Action = SurvivalActionKind.ConsumePhysical, Provision = token.Kind,
                ItemIdentity = token.Identity
            });
        }

        internal bool TryGetGameDateTime(out DateTime value)
        {
            return environment.TryGetGameDateTime(out value);
        }

        public void OnWorldTimeAdvanceCompleted(long calendarBeforeTicks,
            long calendarAfterTicks, bool isNativeSleepOrigin = false)
        {
            if (!sessionReady || !network.IsAuthority || calendarBeforeTicks <= 0L ||
                calendarAfterTicks <= calendarBeforeTicks)
                return;
            try
            {
                var before = new DateTime(calendarBeforeTicks);
                var after = new DateTime(calendarAfterTicks);
                // First account for normal calendar progress since the last sample, then queue
                // the exact host-side TimeAdvance interval. Capturing it in the Harmony postfix
                // avoids client/host clock drift and prevents the next periodic sample from
                // counting the same skip a second time.
                var realSeconds = simulationAccumulator;
                simulationAccumulator = 0f;
                var beforeHours = ObserveCalendar(before, realSeconds, false);
                if (beforeHours > 0f) AdvanceAuthority(realSeconds, beforeHours);
                // A generic relayed TimeAdvance may be fast travel. It can authorize duration-only
                // clock alignment only when the host already has a native sleep intent; the host's
                // own call is independently identified at the native SleepCoro call site.
                var allowsDurationAlignment = isNativeSleepOrigin ||
                    sleepCalendar.PendingSleepCount > 0;
                var immediateHours = ObserveCalendar(after, 0f, true,
                    allowsDurationAlignment);
                if (immediateHours > 0f) AdvanceAuthority(0f, immediateHours);
            }
            catch (ArgumentOutOfRangeException)
            {
                RebaseGameCalendar();
            }
        }

        private bool IsPersonalSleepMode => PersonalSleepValidator.IsNoAdvanceMode(
            network.IsSessionActive, network.IsNativeSleepTimeSuppressed, environment.IsGameTimeOverridden);

        public bool OnLocalSleepStarting(float amountOfSecondsToSleep,
            long calendarBeforeTicks)
        {
            // In MP the intent is sent before Multiplayer's own TimeAdvance request. Both use the
            // client's reliable ordered channel, so the authority can mark the subsequent relayed
            // interval as eligible for cross-clock duration alignment. Host/offline sleep keeps the
            // completion path below, where the exact local calendar result is already available.
            if (!sessionReady || !network.IsSessionActive || network.IsAuthority ||
                IsPersonalSleepMode ||
                !network.IsSupported || !HasConfirmedLocalState || lastHostMessage == null ||
                string.IsNullOrEmpty(lastHostMessage.SessionId) ||
                amountOfSecondsToSleep < 360f || amountOfSecondsToSleep > 86400f ||
                float.IsNaN(amountOfSecondsToSleep) || float.IsInfinity(amountOfSecondsToSleep) ||
                calendarBeforeTicks <= 0L ||
                Time.realtimeSinceStartup - lastCompletedSleepRequestTime < 1f)
                return false;
            try
            {
                var calendarAfterTicks = checked(calendarBeforeTicks +
                    TimeSpan.FromSeconds(amountOfSecondsToSleep).Ticks);
                if (calendarAfterTicks > DateTime.MaxValue.Ticks) return false;
                var sent = RequestAction(new SurvivalActionRequest
                {
                    RequestId = ++requestSequence,
                    Action = SurvivalActionKind.Sleep,
                    Amount = amountOfSecondsToSleep / 3600f,
                    CalendarBeforeTicks = calendarBeforeTicks,
                    CalendarAfterTicks = calendarAfterTicks
                });
                if (!sent) return false;
                lastCompletedSleepRequestTime = Time.realtimeSinceStartup;
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (OverflowException) { return false; }
        }

        public bool OnLocalSleepWithoutTimeAdvanceCompleted(float amountOfSecondsToSleep)
        {
            entry.Logger.Log("Native sleep without calendar jump: hours=" + amountOfSecondsToSleep / 3600f +
                ", ready=" + sessionReady + ", native=" + NativeSleepOriginPatch.IsAdvancingSleep +
                ", mp=" + network.IsSessionActive + ", authority=" + network.IsAuthority +
                ", mpTimeDisabled=" + network.IsNativeSleepTimeSuppressed +
                ", clockOverridden=" + environment.IsGameTimeOverridden);
            if (!sessionReady || !NativeSleepOriginPatch.IsAdvancingSleep ||
                !IsPersonalSleepMode ||
                Time.realtimeSinceStartup - lastCompletedSleepRequestTime < 1f)
                return false;
            var request = new SurvivalActionRequest
            {
                RequestId = ++requestSequence,
                Action = SurvivalActionKind.SleepWithoutTimeAdvance,
                Amount = amountOfSecondsToSleep / 3600f
            };
            if (!PersonalSleepValidator.IsValid(true, true, true, request) || !RequestAction(request))
                return false;
            lastCompletedSleepRequestTime = Time.realtimeSinceStartup;
            return true;
        }

        public void OnLocalSleepCompleted(float amountOfSecondsToSleep,
            long calendarBeforeTicks = 0L, long calendarAfterTicks = 0L)
        {
            if (!sessionReady || amountOfSecondsToSleep <= 0f ||
                Time.realtimeSinceStartup - lastCompletedSleepRequestTime < 1f)
                return;
            lastCompletedSleepRequestTime = Time.realtimeSinceStartup;
            var sleepHours = amountOfSecondsToSleep / 3600f;
            if (calendarBeforeTicks > 0L && calendarAfterTicks > calendarBeforeTicks)
            {
                var measuredHours = TimeSpan.FromTicks(calendarAfterTicks - calendarBeforeTicks).TotalHours;
                if (measuredHours >= 0.1d && measuredHours <= 24d)
                    sleepHours = (float)measuredHours;
            }
            RequestAction(new SurvivalActionRequest
            {
                RequestId = ++requestSequence,
                Action = SurvivalActionKind.Sleep,
                Amount = sleepHours,
                CalendarBeforeTicks = calendarBeforeTicks,
                CalendarAfterTicks = calendarAfterTicks
            });
            // There are no other players whose independently delivered action can claim this
            // jump in local play, so finish the transaction without an artificial ten-second wait.
            if (!network.IsSessionActive && network.IsAuthority)
                DrainCalendarOperations(double.MaxValue);
        }

        public void OnLocalTrauma(TraumaKind kind, float magnitude, float playerHeightMeters)
        {
            if (!sessionReady || IsRespawning || kind == TraumaKind.None) return;
            RequestAction(new SurvivalActionRequest
            {
                RequestId = ++requestSequence,
                Action = SurvivalActionKind.Trauma,
                Trauma = kind,
                Amount = magnitude,
                SecondaryAmount = playerHeightMeters
            });
        }

        public int GetPrice(ProvisionKind kind)
        {
            if (network.IsSessionActive && !network.IsAuthority && lastHostMessage != null)
                return lastHostMessage.GetPrice(kind);
            switch (kind)
            {
                case ProvisionKind.Meal: return ProvisionShopPricing.LocalPrice(settings.MealPrice);
                case ProvisionKind.Water: return ProvisionShopPricing.LocalPrice(settings.WaterPrice);
                case ProvisionKind.Coffee: return ProvisionShopPricing.LocalPrice(settings.CoffeePrice);
                case ProvisionKind.FirstAid: return ProvisionShopPricing.LocalPrice(settings.FirstAidPrice);
                case ProvisionKind.HeatPack: return ProvisionShopPricing.LocalPrice(settings.HeatPackPrice);
                default: return -1;
            }
        }

        public void Notify(string message)
        {
            if (hud != null) hud.Notify(message);
        }

        public void SaveSettings()
        {
            settings.Save(entry);
        }

        public void Dispose()
        {
            if (disposed) return;
            Stop();
            disposed = true;
            network.HelloReceived -= OnHelloReceived;
            network.ActionReceived -= OnActionReceived;
            network.EnvironmentReceived -= OnEnvironmentReceived;
            network.PlayerDisconnected -= OnPlayerDisconnected;
            network.StateReceived -= OnStateReceived;
            network.Dispose();
            cabHeaters.Dispose();
        }

        private void OnWorldLoaded()
        {
            if (!started || disposed) return;
            try
            {
                if (!TryPrepareSession()) return;
                sessionReady = true;
                simulationAccumulator = 0f;
                nextStateBroadcast = Time.realtimeSinceStartup;
                nextSaveCheckpoint = Time.realtimeSinceStartup + SaveCheckpointSeconds;
                nextLocalEnvironmentUpdate = 0f;
                UpdateLocalEnvironment(true);
                RebaseGameCalendar();
                lastAuthority = network.IsAuthority;
                if (network.IsSessionActive && !network.IsAuthority) SendHello();
                else EnsureLocalAuthorityRecord();
                ProcessPendingHellos();
            }
            catch (Exception exception)
            {
                entry.Logger.Warning("Survival session initialization failed: " + exception.Message);
            }
        }

        private bool TryPrepareSession()
        {
            if (UnloadWatcher.isUnloading) return false;
            var manager = UnityEngine.Object.FindObjectOfType<SaveGameManager>();
            if (manager == null || manager.data == null) return false;
            if (manager == saveManager && ReferenceEquals(manager.data, sessionSaveData)) return true;
            if (sessionSaveData != null) EndSession();
            ResetCabHeaterControls();
            confirmedCabHeaterStates.Clear();
            ClearPendingCabHeater();
            saveManager = manager;
            sessionSaveData = manager.data;
            saveManager.OnInternalDataUpdate += OnSaveDataUpdate;
            if (network.IsAuthority)
            {
                document = SurvivalSaveData.Read(sessionSaveData, settings.ToTuning());
                BeginAuthorityEpoch();
                LoadCabHeatersFromDocument();
            }
            else
                document = null;
            currentState = SurvivalState.CreateDefault(settings.ToTuning());
            hasLocalStateSnapshot = false;
            if (hud != null) hud.ResetStateWarnings();
            ResetHomeRespawn();
            receivedNetworkState = false;
            lastReceivedSequence = 0;
            lastHostMessage = null;
            retiredHostSessions.Clear();
            retiredHostSessionOrder.Clear();
            RebaseGameCalendar();
            return true;
        }

        private void HandleAuthorityTransition()
        {
            if (lastAuthority == network.IsAuthority) return;
            if (lastAuthority && document != null) PersistAuthorityDocument();
            lastAuthority = network.IsAuthority;
            activeRecords.Clear();
            actionRequests.Clear();
            pendingItemUses.Clear();
            if (personalItems != null) personalItems.Clear();
            nextActionTimes.Clear();
            nextTraumaTimes.Clear();
            remoteEnvironments.Clear();
            ResetCabHeaterControls();
            confirmedCabHeaterStates.Clear();
            ClearPendingCabHeater();
            ResetCalendarReconciliation();
            RebaseGameCalendar();
            receivedNetworkState = false;
            lastReceivedSequence = 0;
            lastHostMessage = null;
            hasLocalStateSnapshot = false;
            if (hud != null) hud.ResetStateWarnings();
            ResetHomeRespawn();
            if (network.IsAuthority)
            {
                document = SurvivalSaveData.Read(sessionSaveData, settings.ToTuning());
                BeginAuthorityEpoch();
                LoadCabHeatersFromDocument();
                EnsureLocalAuthorityRecord();
            }
            else
            {
                document = null;
                currentState = SurvivalState.CreateDefault(settings.ToTuning());
                SendHello();
            }
        }

        private void EnsureLocalAuthorityRecord()
        {
            if (!sessionReady || !network.IsAuthority || document == null) return;
            var localId = network.IsSessionActive ? network.LocalPlayerId : byte.MaxValue;
            if (lastLocalAuthorityId != localId)
            {
                SurvivalPlayerRecord old;
                if (activeRecords.TryGetValue(lastLocalAuthorityId, out old) &&
                    string.Equals(old.IdentityId, settings.IdentityId, StringComparison.OrdinalIgnoreCase))
                    activeRecords.Remove(lastLocalAuthorityId);
                lastLocalAuthorityId = localId;
            }
            SurvivalPlayerRecord existing;
            if (activeRecords.TryGetValue(localId, out existing) &&
                string.Equals(existing.IdentityId, settings.IdentityId, StringComparison.OrdinalIgnoreCase))
                return;
            var record = document.GetOrCreate(settings.IdentityId, LocalDisplayName(), settings.ToTuning());
            activeRecords[localId] = record;
            SetLocalState(record.State);
            stateDirty = true;
        }

        private void AdvanceAuthority(float realSeconds, float gameHours,
            SleepCalendarAdvance calendarAdvance = null)
        {
            if (gameHours <= 0f) return;
            var tuning = settings.ToTuning();
            if (!network.IsSessionActive)
            {
                SurvivalPlayerRecord local;
                if (activeRecords.TryGetValue(byte.MaxValue, out local))
                {
                    var playerHours = calendarAdvance == null
                        ? gameHours : (float)calendarAdvance.GetAwakeHours(byte.MaxValue);
                    if (playerHours <= 0f) return;
                    var sample = (lastLocalEnvironment ?? environment.SampleLocal(0f)).Clone();
                    var previousCollapse = local.State.CollapseCount;
                    AdvanceInCalendarChunks(local.State, sample, tuning, playerHours,
                        ScaleRealSeconds(realSeconds, playerHours, gameHours));
                    if (ApplyDeathPenalty(local.State, previousCollapse))
                        SendResult(byte.MaxValue, local.State, 0, SurvivalResultCode.None, "death");
                    else
                        SetLocalState(local.State);
                    stateDirty = true;
                }
                return;
            }

            var players = network.GetPlayers();
            foreach (var player in players)
            {
                if (player == null || !player.IsLoaded) continue;
                if (player.IsHost && !activeRecords.ContainsKey(player.PlayerId))
                    activeRecords[player.PlayerId] = document.GetOrCreate(settings.IdentityId,
                        player.DisplayName, tuning);
                SurvivalPlayerRecord record;
                if (!activeRecords.TryGetValue(player.PlayerId, out record)) continue;
                var playerHours = calendarAdvance == null
                    ? gameHours : (float)calendarAdvance.GetAwakeHours(player.PlayerId);
                if (playerHours <= 0f) continue;
                var sample = player.PlayerId == network.LocalPlayerId
                    ? (lastLocalEnvironment ?? environment.SampleLocal(0f)).Clone()
                    : GetRemoteEnvironment(player, 0f, realSeconds);
                var previousCollapse = record.State.CollapseCount;
                AdvanceInCalendarChunks(record.State, sample, tuning, playerHours,
                    ScaleRealSeconds(realSeconds, playerHours, gameHours));
                var died = ApplyDeathPenalty(record.State, previousCollapse);
                record.LastSeenUtcTicks = DateTime.UtcNow.Ticks;
                if (died)
                    SendResult(player.PlayerId, record.State, 0, SurvivalResultCode.None, "death");
                else if (player.PlayerId == network.LocalPlayerId)
                    SetLocalState(record.State);
                stateDirty = true;
            }
        }

        private bool RequestAction(SurvivalActionRequest request)
        {
            if (request != null && (request.Action == SurvivalActionKind.Sleep ||
                request.Action == SurvivalActionKind.SleepWithoutTimeAdvance))
                NativeSleepOriginPatch.CaptureBed(request);
            if (!sessionReady)
            {
                Notify(ModLocalization.Result(SurvivalResultCode.NotReady, string.Empty));
                return false;
            }
            if (network.IsSessionActive && !network.IsSupported)
            {
                Notify(ModLocalization.Result(SurvivalResultCode.UnsupportedMultiplayerVersion, string.Empty));
                return false;
            }
            request.SessionId = network.IsSessionActive && !network.IsAuthority
                ? (lastHostMessage == null ? string.Empty : lastHostMessage.SessionId)
                : (document == null ? string.Empty : document.SessionId);
            if (network.IsSessionActive && !network.IsAuthority)
            {
                return network.SendAction(request);
            }
            EnsureLocalAuthorityRecord();
            var localId = network.IsSessionActive ? network.LocalPlayerId : byte.MaxValue;
            ProcessAction(localId, GetLocalPlayerInfo(localId), request);
            return true;
        }

        private void OnActionReceived(SurvivalPlayerInfo player, SurvivalActionRequest request)
        {
            if (!sessionReady || !network.IsAuthority || player == null || request == null) return;
            ProcessAction(player.PlayerId, player, request);
        }

        private void ProcessAction(byte playerId, SurvivalPlayerInfo player, SurvivalActionRequest request)
        {
            SurvivalPlayerRecord record;
            if (request.Protocol != SurvivalConstants.ProtocolVersion)
            {
                SendResult(playerId, null, request.RequestId, SurvivalResultCode.ProtocolMismatch, string.Empty);
                return;
            }
            if (document == null || string.IsNullOrEmpty(request.SessionId) ||
                !string.Equals(request.SessionId, document.SessionId, StringComparison.Ordinal))
            {
                SendResult(playerId, null, request.RequestId, SurvivalResultCode.NotReady, string.Empty);
                return;
            }
            if (!activeRecords.TryGetValue(playerId, out record))
            {
                SendResult(playerId, null, request.RequestId, SurvivalResultCode.NotReady, string.Empty);
                return;
            }
            if (HasPendingNativeSleep(playerId))
            {
                // Preserve action order while the exact world-calendar jump is being matched.
                SendResult(playerId, record.State, request.RequestId,
                    SurvivalResultCode.RateLimited, string.Empty);
                return;
            }
            if (!actionRequests.TryAccept(playerId, request.RequestId))
            {
                SendResult(playerId, record.State, request.RequestId, SurvivalResultCode.InvalidRequest, string.Empty);
                return;
            }
            float nextAllowed;
            if (nextActionTimes.TryGetValue(playerId, out nextAllowed) && Time.realtimeSinceStartup < nextAllowed)
            {
                SendResult(playerId, record.State, request.RequestId, SurvivalResultCode.RateLimited, string.Empty);
                return;
            }
            nextActionTimes[playerId] = Time.realtimeSinceStartup + 0.25f;
            var tuning = settings.ToTuning();
            var result = SurvivalResultCode.InvalidRequest;
            var status = string.Empty;
            var previousCollapse = record.State.CollapseCount;
            if (request.Action == SurvivalActionKind.Consume)
            {
                result = SurvivalSimulator.Consume(record.State, request.Provision, tuning);
                if (result == SurvivalResultCode.Success) status = ProvisionStatus(request.Provision);
            }
            else if (request.Action == SurvivalActionKind.ConsumePhysical)
            {
                result = PhysicalItemLedger.Consume(document.ConsumedItems, request.ItemIdentity,
                    record.State, request.Provision, tuning, playerId == lastLocalAuthorityId
                        ? environment.SampleLocal(0f) : GetRemoteEnvironment(player, 0f, 0f));
                if (result == SurvivalResultCode.Success) status = ProvisionStatus(request.Provision);
            }
            else if (request.Action == SurvivalActionKind.Sleep ||
                request.Action == SurvivalActionKind.SleepWithoutTimeAdvance)
            {
                var nearBed = playerId == lastLocalAuthorityId
                    // The native call site proves local bed use, including just-streamed beds
                    // missing from the 30-second spatial cache. Never trust this for remote players.
                    ? NativeSleepOriginPatch.IsAdvancingSleep || environment.IsLocalNearBed()
                    : environment.IsPlayerNearNativeBed(player, request);
                if (!nearBed)
                {
                    entry.Logger.Warning("Native sleep rejected: player " + playerId + " is not near a known bed.");
                    result = SurvivalResultCode.NotNearBed;
                }
                else if (request.Action == SurvivalActionKind.SleepWithoutTimeAdvance)
                {
                    // No shared calendar jump exists in this MP mode. Apply only this player's
                    // validated bed sleep, without queuing a jump or advancing anyone else.
                    if (PersonalSleepValidator.IsValid(network.IsSessionActive,
                        IsPersonalSleepMode, nearBed, request))
                    {
                        result = SurvivalSimulator.Sleep(record.State, request.Amount,
                            playerId == lastLocalAuthorityId ? environment.SampleLocal(0f)
                                : GetRemoteEnvironment(player, 0f, 0f), tuning);
                        if (result == SurvivalResultCode.Success) status = "sleep_ok";
                        entry.Logger.Log("Personal native sleep without world time advance: player=" +
                            playerId + ", hours=" + request.Amount + ", rest=" + record.State.Rest);
                    }
                }
                else
                {
                    var sleepEnvironment = playerId == lastLocalAuthorityId
                        ? environment.SampleLocal(0f)
                        : GetRemoteEnvironment(player, 0f, 0f);
                    if (TryQueueNativeSleep(playerId, request, sleepEnvironment)) return;
                    result = SurvivalResultCode.InvalidRequest;
                }
            }
            else if (request.Action == SurvivalActionKind.Trauma)
            {
                float nextTrauma;
                if (nextTraumaTimes.TryGetValue(playerId, out nextTrauma) &&
                    Time.realtimeSinceStartup < nextTrauma)
                    result = SurvivalResultCode.RateLimited;
                else if (request.Trauma == TraumaKind.Fall &&
                    (request.Amount > 60f || request.SecondaryAmount < 1.4f ||
                     request.SecondaryAmount > 2.4f))
                    result = SurvivalResultCode.InvalidRequest;
                else if (request.Trauma == TraumaKind.TrainCollision &&
                    (player == null || player.IsOnCar || request.Amount > 300f || request.Amount <= 7f ||
                     !environment.IsPlayerNearMovingTrain(player, 7f)))
                    result = SurvivalResultCode.InvalidRequest;
                else
                {
                    nextTraumaTimes[playerId] = Time.realtimeSinceStartup + TraumaCooldownSeconds;
                    result = SurvivalSimulator.ApplyTrauma(record.State, request.Trauma,
                        request.Amount, request.SecondaryAmount, tuning);
                    if (result == SurvivalResultCode.Success)
                        status = request.Trauma == TraumaKind.Fall ? "fall_damage" : "train_damage";
                }
            }
            else if (request.Action == SurvivalActionKind.SetCabHeater)
            {
                if (!CabHeaterActionValidator.IsValid(player, request))
                    result = SurvivalResultCode.InvalidRequest;
                else
                {
                    var level = CabHeaterSetting.NormalizeSwitch(request.Amount);
                    document.SetCabHeater(player.OccupiedCarId, level);
                    ApplyConfirmedCabHeater(player.OccupiedCarId, level);
                    result = SurvivalResultCode.Success;
                    status = level > 0f ? "cab_heater_on" : "cab_heater_off";
                    nextStateBroadcast = 0f;
                }
            }
            if (ApplyDeathPenalty(record.State, previousCollapse)) status = "death";
            record.LastSeenUtcTicks = DateTime.UtcNow.Ticks;
            if (playerId == lastLocalAuthorityId) SetLocalState(record.State);
            SendResult(playerId, record.State, request.RequestId, result, status, true, player);
            stateDirty = true;
        }

        private void OnHelloReceived(SurvivalPlayerInfo player, SurvivalHello hello)
        {
            if (player == null || hello == null || !network.IsAuthority) return;
            if (!sessionReady)
            {
                pendingHellos[player.PlayerId] = new PendingHello(player, hello);
                return;
            }
            BindHello(player, hello);
        }

        private void BindHello(SurvivalPlayerInfo player, SurvivalHello hello)
        {
            Guid identityGuid;
            if (hello.Protocol != SurvivalConstants.ProtocolVersion ||
                !Guid.TryParse(hello.IdentityId, out identityGuid) || identityGuid == Guid.Empty)
            {
                SendResult(player.PlayerId, null, 0, SurvivalResultCode.ProtocolMismatch, string.Empty);
                return;
            }
            var identity = hello.IdentityId;
            var duplicate = activeRecords.Any(pair => pair.Key != player.PlayerId &&
                string.Equals(pair.Value.IdentityId, identity, StringComparison.OrdinalIgnoreCase));
            var status = string.Empty;
            if (duplicate)
            {
                identity = identity + "#" + player.PlayerId;
                status = "identity_in_use";
            }
            var record = document.GetOrCreate(identity, player.DisplayName, settings.ToTuning());
            activeRecords[player.PlayerId] = record;
            stateDirty = true;
            SendResult(player.PlayerId, record.State, 0, SurvivalResultCode.None, status);
        }

        private void ProcessPendingHellos()
        {
            if (!sessionReady || !network.IsAuthority || document == null || pendingHellos.Count == 0) return;
            var values = pendingHellos.Values.ToArray();
            pendingHellos.Clear();
            foreach (var value in values) BindHello(value.Player, value.Hello);
        }

        private void SendHello()
        {
            if (!sessionReady || !network.IsSessionActive || network.IsAuthority || !network.IsSupported) return;
            network.SendHello(new SurvivalHello
            {
                IdentityId = settings.IdentityId,
                ModVersion = SurvivalConstants.ModVersion
            });
            nextHello = Time.realtimeSinceStartup + HelloRetrySeconds;
        }

        private void SendAllStates()
        {
            var players = network.GetPlayers();
            foreach (var pair in activeRecords)
            {
                if (pair.Key == lastLocalAuthorityId) continue;
                var player = players.FirstOrDefault(value => value != null && value.PlayerId == pair.Key);
                SendResult(pair.Key, pair.Value.State, 0, SurvivalResultCode.None,
                    string.Empty, false, player);
            }
        }

        private void SendResult(byte playerId, SurvivalState state, uint requestId,
            SurvivalResultCode result, string status, bool reliable = true,
            SurvivalPlayerInfo player = null)
        {
            var tuning = settings.ToTuning();
            if (player == null)
            {
                if (playerId == lastLocalAuthorityId)
                    player = GetLocalPlayerInfo(playerId);
                else if (network.IsSessionActive)
                    player = network.GetPlayers().FirstOrDefault(value =>
                        value != null && value.PlayerId == playerId);
            }
            var heaterCarId = player != null && player.OccupiedCarSupportsCabHeater
                ? player.OccupiedCarId ?? string.Empty : string.Empty;
            var message = new SurvivalStateMessage
            {
                Sequence = ++stateSequence,
                RequestId = requestId,
                Result = result,
                SessionId = document == null ? string.Empty : document.SessionId,
                StatusKey = status ?? string.Empty,
                State = state == null ? SurvivalState.CreateDefault(tuning) : state.Clone(),
                MealPrice = GetPrice(ProvisionKind.Meal),
                WaterPrice = GetPrice(ProvisionKind.Water),
                CoffeePrice = GetPrice(ProvisionKind.Coffee),
                FirstAidPrice = GetPrice(ProvisionKind.FirstAid),
                HeatPackPrice = GetPrice(ProvisionKind.HeatPack),
                CabHeaterCarId = heaterCarId,
                CabHeaterLevel = document != null ? document.GetCabHeater(heaterCarId) : 0f,
            };
            if (playerId == lastLocalAuthorityId)
            {
                ApplyLocalMessage(message, false);
                return;
            }
            network.SendStateToPlayer(playerId, message, reliable);
        }

        private void OnStateReceived(SurvivalStateMessage message)
        {
            HandleNetworkConnectionGeneration();
            if (!sessionReady || network.IsAuthority || message == null ||
                message.Protocol != SurvivalConstants.ProtocolVersion || message.State == null ||
                !message.State.IsValid())
                return;
            if (string.IsNullOrEmpty(message.SessionId) ||
                retiredHostSessions.Contains(message.SessionId))
                return;
            if (lastHostMessage != null &&
                !string.Equals(message.SessionId, lastHostMessage.SessionId,
                    StringComparison.Ordinal))
            {
                // MPAPI's clientbound callback has no sender and its remote wrappers do not expose
                // a reliable host identity. Inside one connection, therefore, an arbitrary new
                // GUID is never adopted: it could be an unseen delayed epoch. Reconnect, world
                // load and local authority transitions explicitly clear lastHostMessage first.
                return;
            }
            if (message.Sequence <= lastReceivedSequence)
            {
                // A newer periodic snapshot may overtake the reliable receipt; never rewind state.
                if (lastHostMessage != null && message.SessionId == lastHostMessage.SessionId)
                {
                    ConfirmPhysicalItem(message);
                    if (message.RequestId == pendingCabHeaterRequestId)
                        ConfirmStaleCabHeaterReceipt(message);
                }
                return;
            }
            lastReceivedSequence = message.Sequence;
            if (message.Result != SurvivalResultCode.NotReady &&
                message.Result != SurvivalResultCode.ProtocolMismatch &&
                !string.IsNullOrEmpty(message.SessionId))
                receivedNetworkState = true;
            lastHostMessage = message;
            ApplyLocalMessage(message, true);
        }

        private void HandleNetworkConnectionGeneration()
        {
            var generation = network.ConnectionGeneration;
            if (generation == lastConnectionGeneration) return;
            lastConnectionGeneration = generation;
            if (lastHostMessage != null) RetireHostSession(lastHostMessage.SessionId);
            lastReceivedSequence = 0u;
            receivedNetworkState = false;
            ReleasePendingPhysicalItems();
            RejectPendingCabHeater();
            ResetCabHeaterControls();
            confirmedCabHeaterStates.Clear();
            lastHostMessage = null;
            nextHello = 0f;
        }

        private void BeginAuthorityEpoch()
        {
            if (document == null) return;
            document.SessionId = Guid.NewGuid().ToString("D");
            stateSequence = 0u;
            stateDirty = true;
        }

        private void LoadCabHeatersFromDocument()
        {
            if (document == null || document.CabHeaters == null) return;
            foreach (var heater in document.CabHeaters)
                if (heater != null && !string.IsNullOrWhiteSpace(heater.CarId))
                    ApplyConfirmedCabHeater(heater.CarId, heater.EffectiveLevel);
        }

        private void RetireHostSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId) || !retiredHostSessions.Add(sessionId)) return;
            retiredHostSessionOrder.Enqueue(sessionId);
            while (retiredHostSessionOrder.Count > 16)
                retiredHostSessions.Remove(retiredHostSessionOrder.Dequeue());
        }

        private void ReleasePendingPhysicalItems()
        {
            foreach (var token in pendingItemUses.Values)
                if (token != null) token.Confirm(SurvivalResultCode.NotReady);
            pendingItemUses.Clear();
        }

        private void ApplyLocalMessage(SurvivalStateMessage message, bool fromNetwork)
        {
            SetLocalState(message.State);
            ApplyCabHeaterMessage(message);
            ConfirmPhysicalItem(message);
            if (message.RequestId != 0 || message.Result != SurvivalResultCode.None ||
                !string.IsNullOrEmpty(message.StatusKey))
            {
                var text = ModLocalization.Result(message.Result, message.StatusKey);
                if (!string.IsNullOrEmpty(text)) Notify(text);
            }
            if (fromNetwork && message.Result == SurvivalResultCode.ProtocolMismatch)
                receivedNetworkState = false;
        }

        private void ApplyCabHeaterMessage(SurvivalStateMessage message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.CabHeaterCarId)) return;
            var samePendingCar = pendingCabHeaterRequestId != 0u &&
                string.Equals(message.CabHeaterCarId, pendingCabHeaterCarId,
                    StringComparison.OrdinalIgnoreCase);
            if (samePendingCar)
            {
                if (message.RequestId == pendingCabHeaterRequestId)
                    ClearPendingCabHeater();
                else if (Time.realtimeSinceStartup < pendingCabHeaterExpiresAt)
                    return;
                else
                    ClearPendingCabHeater();
            }
            ApplyConfirmedCabHeater(message.CabHeaterCarId, message.CabHeaterLevel);
        }

        private void ConfirmStaleCabHeaterReceipt(SurvivalStateMessage receipt)
        {
            if (receipt == null || pendingCabHeaterRequestId == 0u ||
                receipt.RequestId != pendingCabHeaterRequestId) return;
            var carId = pendingCabHeaterCarId;
            ClearPendingCabHeater();
            if (lastHostMessage != null && string.Equals(lastHostMessage.CabHeaterCarId,
                    carId, StringComparison.OrdinalIgnoreCase))
                ApplyConfirmedCabHeater(carId, lastHostMessage.CabHeaterLevel);
            else if (string.Equals(receipt.CabHeaterCarId, carId,
                    StringComparison.OrdinalIgnoreCase))
                ApplyConfirmedCabHeater(carId, receipt.CabHeaterLevel);
        }

        private float GetConfirmedCabHeaterState(string carId)
        {
            if (string.IsNullOrWhiteSpace(carId)) return 0f;
            float confirmed;
            if (confirmedCabHeaterStates.TryGetValue(carId, out confirmed)) return confirmed;
            if (network.IsAuthority && document != null) return document.GetCabHeater(carId);
            if (lastHostMessage != null && string.Equals(lastHostMessage.CabHeaterCarId,
                    carId, StringComparison.OrdinalIgnoreCase))
                return lastHostMessage.CabHeaterLevel;
            return 0f;
        }

        private void ApplyConfirmedCabHeater(string carId, float level)
        {
            if (string.IsNullOrWhiteSpace(carId)) return;
            level = CabHeaterSetting.NormalizeSwitch(level);
            confirmedCabHeaterStates[carId] = level;
            cabHeaters.ApplyConfirmed(carId, level);
        }

        private void ResolveExpiredCabHeaterRequest()
        {
            if (pendingCabHeaterRequestId == 0u ||
                Time.realtimeSinceStartup < pendingCabHeaterExpiresAt) return;
            var carId = pendingCabHeaterCarId;
            var previous = pendingCabHeaterPreviousState;
            ClearPendingCabHeater();
            var confirmed = lastHostMessage != null && string.Equals(
                lastHostMessage.CabHeaterCarId, carId, StringComparison.OrdinalIgnoreCase)
                ? lastHostMessage.CabHeaterLevel : previous;
            ApplyConfirmedCabHeater(carId, confirmed);
        }

        private void RejectPendingCabHeater()
        {
            if (pendingCabHeaterRequestId == 0u) return;
            var carId = pendingCabHeaterCarId;
            var previous = pendingCabHeaterPreviousState;
            ClearPendingCabHeater();
            ApplyConfirmedCabHeater(carId, previous);
        }

        private void ClearPendingCabHeater()
        {
            pendingCabHeaterRequestId = 0u;
            pendingCabHeaterCarId = string.Empty;
            pendingCabHeaterPreviousState = 0f;
            pendingCabHeaterExpiresAt = 0f;
        }

        private void ConfirmPhysicalItem(SurvivalStateMessage message)
        {
            NativeProvisionToken token;
            if (message.RequestId == 0 || !pendingItemUses.TryGetValue(message.RequestId, out token)) return;
            pendingItemUses.Remove(message.RequestId);
            if (token != null) token.Confirm(message.Result);
        }

        private void SetLocalState(SurvivalState state)
        {
            if (state == null) return;
            var previousCollapse = currentState == null ? lastCollapseCount : currentState.CollapseCount;
            currentState = state.Clone();
            currentState.Clamp();
            CustomProvisionCatalog.RefreshPrices(this);
            movementMultiplier = SurvivalSimulator.GetMovementMultiplier(currentState);
            if (hasLocalStateSnapshot && currentState.CollapseCount > previousCollapse)
            {
                if (hud != null) hud.TriggerCollapse();
                homeRespawn.Begin();
                nextHomeRespawnAttempt = 0f;
                nextRespawnWarning = Time.realtimeSinceStartup + 10f;
                entry.Logger.Log("Death detected: returning the local player to the marked house.");
                ProcessPendingHomeRespawn();
            }
            hasLocalStateSnapshot = true;
            lastCollapseCount = currentState.CollapseCount;
        }

        private bool ApplyDeathPenalty(SurvivalState state, uint previousCollapse)
        {
            if (state == null || state.CollapseCount <= previousCollapse) return false;
            var deaths = state.CollapseCount - previousCollapse;
            for (uint index = 0; index < deaths; index++) environment.DeductMoneyUpTo(DeathPenalty);
            return true;
        }

        private void ResetHomeRespawn()
        {
            homeRespawn.Reset();
            respawnTraumaGraceUntil = 0f;
            nextHouseFallbackScan = 0f;
            nextRespawnWarning = 0f;
            cachedHouseDestination = null;
        }

        private void ProcessPendingHomeRespawn()
        {
            if (!homeRespawn.Pending || homeRespawn.Running || Time.realtimeSinceStartup < nextHomeRespawnAttempt) return;
            nextHomeRespawnAttempt = Time.realtimeSinceStartup + HomeRespawnRetrySeconds;
            if (!WorldStreamingInit.IsLoaded || PlayerManager.PlayerTransform == null ||
                SingletonBehaviour<APlayerTeleport>.Instance == null || UnloadWatcher.isUnloading)
                return;
            var travel = SingletonBehaviour<FastTravelController>.Instance;
            if (travel == null || FastTravelController.IsFastTravelling) return;
            Vector3 position;
            Quaternion rotation;
            if (!TryGetHouseRespawnPose(out position, out rotation))
            {
                WarnRespawnDelay("House map destination is not loaded yet.");
                return;
            }
            var controller = impactMonitor == null ? null : impactMonitor.GetComponent<CustomFirstPersonController>();
            if (!homeRespawn.TryStart(controller != null, FastTravelController.IsFastTravelling)) return;
            var revision = homeRespawn.Revision;
            try
            {
                NativeHomeFastTravel.Start(travel, cachedHouseDestination, controller,
                    error => OnHomeFastTravelFinished(revision, error));
            }
            catch (Exception error)
            {
                OnHomeFastTravelFinished(revision, error);
            }
        }

        private void OnHomeFastTravelFinished(uint revision, Exception error)
        {
            // Ignore callbacks from a previous save/session or an superseded death.
            if (!homeRespawn.Finish(revision, error == null)) return;
            if (error != null)
            {
                WarnRespawnDelay("Native home fast travel failed: " + error.Message);
                return;
            }
            respawnTraumaGraceUntil = Time.realtimeSinceStartup + 2f;
            entry.Logger.Log("House respawn completed through native fast travel, without a travel fee.");
            Notify(ModLocalization.Text("Вы очнулись у дома. Здоровье: 10%.", "You woke up at home. Health: 10%."));
            if (impactMonitor != null) impactMonitor.ResetTracking();
            UpdateLocalEnvironment(true);
        }

        private void WarnRespawnDelay(string reason)
        {
            if (Time.realtimeSinceStartup < nextRespawnWarning) return;
            nextRespawnWarning = Time.realtimeSinceStartup + 10f;
            entry.Logger.Warning(reason);
            Notify(ModLocalization.Text("Возвращение к дому: ожидаю загрузку места появления.",
                "Returning home: waiting for the respawn location to load."));
        }

        private bool TryGetHouseRespawnPose(out Vector3 position, out Quaternion rotation)
        {
            if (!IsHouseDestination(cachedHouseDestination))
            {
                cachedHouseDestination = null;
                foreach (var destination in FastTravelDestination.ActiveDestinations)
                    if (IsHouseDestination(destination)) { cachedHouseDestination = destination; break; }
                // Map visibility can temporarily remove the real destination from ActiveDestinations.
                // Look only at loaded scene objects, never prefab assets or arbitrary station positions.
                if (cachedHouseDestination == null && Time.realtimeSinceStartup >= nextHouseFallbackScan)
                {
                    nextHouseFallbackScan = Time.realtimeSinceStartup + 5f;
                    foreach (var destination in Resources.FindObjectsOfTypeAll<WorldFastTravelDestination>())
                        if (IsHouseDestination(destination) && destination.gameObject.scene.IsValid() &&
                            destination.gameObject.scene.isLoaded)
                        { cachedHouseDestination = destination; break; }
                }
            }
            if (cachedHouseDestination != null)
            {
                position = cachedHouseDestination.playerTeleportAnchor.position;
                rotation = cachedHouseDestination.playerTeleportAnchor.rotation;
                // A negative coordinate is valid after origin shifting; reject only corrupt coordinates.
                return IsFinite(position.x) && IsFinite(position.y) && IsFinite(position.z);
            }
            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        private static bool IsHouseDestination(FastTravelDestination destination)
        {
            return destination != null && !destination.IsDynamic &&
                (destination.markerType & FastTravelDestination.MarkerType.House) != 0 &&
                destination.playerTeleportAnchor != null;
        }

        private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

        private void OnPlayerDisconnected(byte playerId)
        {
            actionRequests.Remove(playerId);
            activeRecords.Remove(playerId);
            pendingHellos.Remove(playerId);
            nextActionTimes.Remove(playerId);
            nextTraumaTimes.Remove(playerId);
            remoteEnvironments.Remove(playerId);
            sleepCalendar.DisconnectPlayer(playerId);
            RemovePendingNativeSleeps(playerId);
            environment.ForgetPlayer(playerId);
        }

        private void OnEnvironmentReceived(SurvivalPlayerInfo player, SurvivalEnvironmentReport report)
        {
            if (!sessionReady || !network.IsAuthority || player == null || report == null ||
                report.Protocol != SurvivalConstants.ProtocolVersion || report.Environment == null ||
                !report.Environment.IsValid())
                return;
            RemoteEnvironmentRecord existing;
            if (remoteEnvironments.TryGetValue(player.PlayerId, out existing) &&
                report.Sequence <= existing.Sequence)
                return;
            var sample = report.Environment.Clone();
            sample.GameHours = 0f;
            sample.Clamp();
            remoteEnvironments[player.PlayerId] = new RemoteEnvironmentRecord(
                report.Sequence, Time.realtimeSinceStartup, sample);
        }

        private SurvivalEnvironment GetRemoteEnvironment(SurvivalPlayerInfo player, float gameHours,
            float realSeconds)
        {
            RemoteEnvironmentRecord report;
            if (remoteEnvironments.TryGetValue(player.PlayerId, out report) &&
                Time.realtimeSinceStartup - report.ReceivedAt <= EnvironmentReportTimeoutSeconds)
            {
                var sample = report.Environment.Clone();
                sample.GameHours = gameHours;
                return sample;
            }
            return environment.SampleRemote(player, gameHours, realSeconds);
        }

        private void UpdateLocalEnvironment(bool force = false)
        {
            var now = Time.realtimeSinceStartup;
            if (!force && now < nextLocalEnvironmentUpdate) return;
            nextLocalEnvironmentUpdate = now + LocalEnvironmentIntervalSeconds;
            if (PlayerManager.PlayerTransform == null) return;
            EnsureImpactMonitor();
            lastLocalEnvironment = environment.SampleLocal(0f);
        }

        private void EnsureImpactMonitor()
        {
            var player = PlayerManager.PlayerTransform;
            if (player == null) return;
            var controller = player.GetComponent<CustomFirstPersonController>();
            if (controller == null)
                controller = player.GetComponentInChildren<CustomFirstPersonController>(true);
            if (controller == null)
                controller = player.GetComponentInParent<CustomFirstPersonController>();
            var physicsTransform = controller == null ? player : controller.transform;
            if (impactMonitor != null && impactMonitor.transform == physicsTransform) return;
            DestroyImpactMonitor();
            impactMonitor = physicsTransform.gameObject.AddComponent<PlayerImpactMonitor>();
            impactMonitor.Initialize(this, controller);
        }

        private void DestroyImpactMonitor()
        {
            if (impactMonitor == null) return;
            UnityEngine.Object.Destroy(impactMonitor);
            impactMonitor = null;
        }

        private void OnPlayerTeleportStarted()
        {
            if (impactMonitor != null) impactMonitor.ResetTracking();
        }

        private void OnSaveDataUpdate(SaveGameData data)
        {
            if (!sessionReady || !network.IsAuthority || document == null ||
                !ReferenceEquals(data, sessionSaveData))
                return;
            try
            {
                SurvivalSaveData.Write(data, document, settings.ToTuning());
                stateDirty = false;
            }
            catch (Exception exception)
            {
                entry.Logger.Warning("Could not add survival data to the game save: " + exception.Message);
            }
        }

        private void Checkpoint()
        {
            WriteToMemorySave();
        }

        private void WriteToMemorySave()
        {
            if (!sessionReady || !network.IsAuthority || document == null || sessionSaveData == null ||
                !stateDirty)
                return;
            PersistAuthorityDocument();
        }

        private void PersistAuthorityDocument()
        {
            if (document == null || sessionSaveData == null || !stateDirty) return;
            try
            {
                SurvivalSaveData.Write(sessionSaveData, document, settings.ToTuning());
                stateDirty = false;
            }
            catch (Exception exception)
            {
                entry.Logger.Warning("Survival checkpoint failed: " + exception.Message);
            }
        }

        private void OnWorldUnloading()
        {
            if (!started || disposed) return;
            try { EndSession(); }
            catch (Exception exception)
            {
                entry.Logger.Warning("Survival session cleanup failed: " + exception.Message);
            }
        }

        private void EndSession()
        {
            pendingItemUses.Clear();
            if (personalItems != null) personalItems.Clear();
            actionRequests.Clear();
            if (hud != null) hud.ResetStateWarnings();
            if (sessionSaveData == null && !sessionReady) return;
            if (network.IsAuthority && document != null)
                DrainCalendarOperations(double.MaxValue);
            WriteToMemorySave();
            if (saveManager != null) saveManager.OnInternalDataUpdate -= OnSaveDataUpdate;
            saveManager = null;
            sessionSaveData = null;
            document = null;
            sessionReady = false;
            receivedNetworkState = false;
            lastHostMessage = null;
            retiredHostSessions.Clear();
            retiredHostSessionOrder.Clear();
            activeRecords.Clear();
            pendingHellos.Clear();
            nextActionTimes.Clear();
            nextTraumaTimes.Clear();
            remoteEnvironments.Clear();
            ResetCalendarReconciliation();
            simulationAccumulator = 0f;
            nextLocalEnvironmentUpdate = 0f;
            nextEnvironmentReport = 0f;
            nextSessionPrepareAttempt = 0f;
            stateDirty = false;
            hasLocalStateSnapshot = false;
            ResetHomeRespawn();
            nextHomeRespawnAttempt = 0f;
            movementMultiplier = 1f;
            appUtil = null;
            nextAppUtilProbe = 0f;
            environment.Reset();
            ResetCabHeaterControls();
            confirmedCabHeaterStates.Clear();
            ClearPendingCabHeater();
            gameCalendarClock.Reset();
        }

        private float ObserveAndQueueCalendar(float fallbackRealSeconds)
        {
            DateTime current;
            if (environment.TryGetGameDateTime(out current))
                return ObserveCalendar(current, fallbackRealSeconds, false, false);
            gameCalendarClock.Reset();
            var dayMinutes = environment.GetDayLengthMinutes();
            return dayMinutes > 0f
                ? Mathf.Clamp(fallbackRealSeconds * 24f / (dayMinutes * 60f), 0f, 24f)
                : 0f;
        }

        private float ObserveCalendar(DateTime current, float fallbackRealSeconds,
            bool capturedTimeAdvance, bool allowsDurationAlignment = false)
        {
            var beforeTicks = gameCalendarClock.LastObservedTicks;
            var gameHours = gameCalendarClock.Observe(current);
            if (gameHours <= 0d || beforeTicks <= 0L) return 0f;
            var afterTicks = current.Ticks;
            var maximumTicks = (long)(GameCalendarClock.MaximumObservedJumpHours *
                TimeSpan.TicksPerHour);
            if (afterTicks - beforeTicks > maximumTicks)
                beforeTicks = afterTicks - maximumTicks;
            var sequence = ++calendarJumpSequence;
            if (sequence == 0u) sequence = ++calendarJumpSequence;
            var jump = new SleepCalendarJump(sequence, beforeTicks, afterTicks,
                Time.realtimeSinceStartup, allowsDurationAlignment);
            var needsCorrelation = capturedTimeAdvance ||
                GameCalendarClock.IsDiscontinuousAdvance(gameHours,
                    fallbackRealSeconds, environment.GetDayLengthMinutes()) ||
                sleepCalendar.IntersectsPendingSleep(beforeTicks, afterTicks);
            var queued = needsCorrelation
                ? sleepCalendar.TryRecordJump(jump)
                : sleepCalendar.PendingJumpCount > 0 &&
                    sleepCalendar.TryRecordOrderedAdvance(jump);
            if (queued)
            {
                calendarJumpRealSeconds[sequence] = Math.Max(0f, fallbackRealSeconds);
                return 0f;
            }
            return (float)gameHours;
        }

        private void ResetCabHeaterControls()
        {
            cabHeaters.Reset();
            environment.InvalidateCabControls();
        }

        private void RebaseGameCalendar()
        {
            ResetCalendarReconciliation();
            gameCalendarClock.Reset();
            DateTime current;
            if (environment.TryGetGameDateTime(out current)) gameCalendarClock.Observe(current);
        }

        private void DrainCalendarOperations(double nowSeconds)
        {
            var operations = sleepCalendar.DrainOperations(nowSeconds);
            for (var i = 0; i < operations.Count; i++)
            {
                var operation = operations[i];
                if (operation.Kind == SleepCalendarOperationKind.Advance)
                {
                    var advance = operation.Advance;
                    float sourceRealSeconds;
                    if (!calendarJumpRealSeconds.TryGetValue(advance.SourceJumpSequence,
                        out sourceRealSeconds))
                        sourceRealSeconds = 0f;
                    var segmentRealSeconds = sourceRealSeconds * (float)advance.SourceFraction;
                    AdvanceAuthority(segmentRealSeconds, (float)advance.TotalHours, advance);
                    if (advance.IsFinalSourceSegment)
                        calendarJumpRealSeconds.Remove(advance.SourceJumpSequence);
                }
                else if (operation.Kind == SleepCalendarOperationKind.CommitSleep)
                    CommitPendingNativeSleep(operation.Sleep);
                else if (operation.Kind == SleepCalendarOperationKind.RejectSleep)
                    RejectPendingNativeSleep(operation.Sleep);
            }
        }

        private bool TryQueueNativeSleep(byte playerId, SurvivalActionRequest request,
            SurvivalEnvironment sleepEnvironment)
        {
            if (request == null || sleepEnvironment == null || request.RequestId == 0u ||
                float.IsNaN(request.Amount) || float.IsInfinity(request.Amount) ||
                request.Amount < 0.1f || request.Amount > 24f ||
                request.CalendarBeforeTicks <= 0L ||
                request.CalendarAfterTicks <= request.CalendarBeforeTicks ||
                pendingNativeSleeps.Count >= 32)
                return false;
            try
            {
                var measuredHours = TimeSpan.FromTicks(
                    request.CalendarAfterTicks - request.CalendarBeforeTicks).TotalHours;
                var tolerance = Math.Max(0.01d, request.Amount * 0.01d);
                if (measuredHours < 0.1d || measuredHours > 24d ||
                    Math.Abs(measuredHours - request.Amount) > tolerance)
                    return false;
                var key = NativeSleepKey(playerId, request.RequestId);
                if (pendingNativeSleeps.ContainsKey(key)) return false;
                var sleep = new SleepCalendarSleep(playerId, request.RequestId,
                    request.CalendarBeforeTicks, request.CalendarAfterTicks,
                    Time.realtimeSinceStartup);
                if (!sleepCalendar.TryRecordSleep(sleep)) return false;
                pendingNativeSleeps.Add(key, new PendingNativeSleepAction(playerId,
                    request.RequestId, (float)measuredHours, sleepEnvironment.Clone()));
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private void CommitPendingNativeSleep(SleepCalendarSleep sleep)
        {
            var key = NativeSleepKey(sleep.PlayerId, sleep.RequestId);
            PendingNativeSleepAction pending;
            if (!pendingNativeSleeps.TryGetValue(key, out pending)) return;
            pendingNativeSleeps.Remove(key);
            SurvivalPlayerRecord record;
            if (!activeRecords.TryGetValue(sleep.PlayerId, out record)) return;
            var previousCollapse = record.State.CollapseCount;
            var result = SurvivalSimulator.Sleep(record.State, pending.Hours,
                pending.Environment, settings.ToTuning());
            entry.Logger.Log("Native sleep committed: player=" + sleep.PlayerId +
                ", hours=" + pending.Hours + ", result=" + result +
                ", rest=" + record.State.Rest + ", deprivationHours=" + record.State.LowRestGameHours);
            var status = result == SurvivalResultCode.Success ? "sleep_ok" : string.Empty;
            if (ApplyDeathPenalty(record.State, previousCollapse)) status = "death";
            record.LastSeenUtcTicks = DateTime.UtcNow.Ticks;
            if (sleep.PlayerId == lastLocalAuthorityId) SetLocalState(record.State);
            SendResult(sleep.PlayerId, record.State, sleep.RequestId, result, status);
            stateDirty = true;
        }

        private void RejectPendingNativeSleep(SleepCalendarSleep sleep)
        {
            var key = NativeSleepKey(sleep.PlayerId, sleep.RequestId);
            if (!pendingNativeSleeps.Remove(key)) return;
            entry.Logger.Warning("Native sleep rejected: no matching calendar advance for player " + sleep.PlayerId + ".");
            SurvivalPlayerRecord record;
            if (activeRecords.TryGetValue(sleep.PlayerId, out record))
                SendResult(sleep.PlayerId, record.State, sleep.RequestId,
                    SurvivalResultCode.InvalidRequest, string.Empty);
        }

        private bool HasPendingNativeSleep(byte playerId)
        {
            foreach (var pending in pendingNativeSleeps.Values)
                if (pending.PlayerId == playerId) return true;
            return false;
        }

        private void RemovePendingNativeSleeps(byte playerId)
        {
            if (pendingNativeSleeps.Count == 0) return;
            var keys = new List<ulong>();
            foreach (var pair in pendingNativeSleeps)
                if (pair.Value.PlayerId == playerId) keys.Add(pair.Key);
            for (var i = 0; i < keys.Count; i++) pendingNativeSleeps.Remove(keys[i]);
        }

        private static ulong NativeSleepKey(byte playerId, uint requestId)
        {
            return ((ulong)playerId << 32) | requestId;
        }

        private void ResetCalendarReconciliation()
        {
            sleepCalendar.Reset();
            calendarJumpRealSeconds.Clear();
            pendingNativeSleeps.Clear();
            calendarJumpSequence = 0u;
        }

        private static float ScaleRealSeconds(float realSeconds, float playerHours, float totalHours)
        {
            return totalHours > 0f ? Math.Max(0f, realSeconds) * playerHours / totalHours : 0f;
        }

        private static void AdvanceInCalendarChunks(SurvivalState state, SurvivalEnvironment sample,
            SurvivalTuning tuning, float gameHours, float realSeconds)
        {
            var remaining = gameHours;
            while (remaining > 0f)
            {
                var step = Math.Min(24f, remaining);
                var stepEnvironment = sample.Clone();
                stepEnvironment.GameHours = step;
                var stepRealSeconds = gameHours > 0f ? realSeconds * step / gameHours : 0f;
                SurvivalSimulator.Advance(state, stepEnvironment, tuning, stepRealSeconds);
                remaining -= step;
            }
        }

        private void CreateHud()
        {
            if (hud != null) return;
            var gameObject = new GameObject("[DVSurvival HUD]");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            hud = gameObject.AddComponent<SurvivalHud>();
            hud.Initialize(this, settings);
            personalItems = gameObject.AddComponent<PersonalProvisionItems>();
            personalItems.Initialize(this);
        }

        private void DestroyHud()
        {
            if (hud == null) return;
            UnityEngine.Object.Destroy(hud.gameObject);
            hud = null;
            personalItems = null;
        }

        private SurvivalPlayerInfo GetLocalPlayerInfo(byte playerId)
        {
            if (network.IsSessionActive)
            {
                var fromNetwork = network.GetPlayers().FirstOrDefault(p => p.PlayerId == playerId);
                if (fromNetwork != null) return fromNetwork;
            }
            var position = PlayerManager.PlayerTransform == null ? Vector3.zero : PlayerManager.PlayerTransform.position;
            var car = PlayerManager.Car;
            return new SurvivalPlayerInfo
            {
                PlayerId = playerId,
                DisplayName = LocalDisplayName(),
                PositionX = position.x,
                PositionY = position.y,
                PositionZ = position.z,
                IsHost = network.IsAuthority,
                IsLoaded = sessionReady,
                IsOnCar = car != null,
                OccupiedCarIsLocomotive = car != null && car.IsLoco,
                OccupiedCarId = car == null ? string.Empty : car.CarGUID ?? string.Empty,
                OccupiedCarSupportsCabHeater = SupportsCabHeater(car),
            };
        }

        private static bool SupportsCabHeater(TrainCar car)
        {
            if (car == null) return false;
            return car.carType == DV.ThingTypes.TrainCarType.LocoShunter ||
                car.carType == DV.ThingTypes.TrainCarType.LocoDH4 ||
                car.carType == DV.ThingTypes.TrainCarType.LocoDM3 ||
                car.carType == DV.ThingTypes.TrainCarType.LocoDiesel;
        }

        private string LocalDisplayName()
        {
            if (network.IsSessionActive)
            {
                var local = network.GetPlayers().FirstOrDefault(p => p.PlayerId == network.LocalPlayerId);
                if (local != null && !string.IsNullOrWhiteSpace(local.DisplayName)) return local.DisplayName;
            }
            return "Local player";
        }

        private static string ProvisionStatus(ProvisionKind kind)
        {
            switch (kind)
            {
                case ProvisionKind.Meal: return "meal_used";
                case ProvisionKind.Water: return "water_used";
                case ProvisionKind.Coffee: return "coffee_used";
                case ProvisionKind.FirstAid: return "first_aid_used";
                case ProvisionKind.HeatPack: return "heat_pack_used";
                default: return string.Empty;
            }
        }

        private bool IsGamePaused()
        {
            try
            {
                if (appUtil == null && Time.realtimeSinceStartup >= nextAppUtilProbe)
                {
                    nextAppUtilProbe = Time.realtimeSinceStartup + AppUtilProbeIntervalSeconds;
                    appUtil = UnityEngine.Object.FindObjectOfType<DV.AppUtil>();
                }
                return appUtil != null ? appUtil.IsTimePaused : Time.timeScale <= 0f;
            }
            catch { return Time.timeScale <= 0f; }
        }

        private ISurvivalNetworkBridge CreateNetworkBridge(string modPath)
        {
            try
            {
                if (Type.GetType("MPAPI.MultiplayerAPI, MultiplayerAPI", false) == null)
                    return new OfflineSurvivalBridge();
                var adapterPath = Path.Combine(modPath, "DVSurvival.Multiplayer.dll");
                if (!File.Exists(adapterPath))
                {
                    entry.Logger.Warning("DVSurvival.Multiplayer.dll not found; using local mode.");
                    return new OfflineSurvivalBridge();
                }
                var assembly = Assembly.LoadFrom(adapterPath);
                var type = assembly.GetType("DVSurvival.Multiplayer.MultiplayerSurvivalBridge", true);
                return (ISurvivalNetworkBridge)Activator.CreateInstance(type);
            }
            catch (Exception exception)
            {
                entry.Logger.Warning("Multiplayer adapter unavailable; using local mode: " + exception.Message);
                return new OfflineSurvivalBridge();
            }
        }

        private sealed class PendingNativeSleepAction
        {
            public PendingNativeSleepAction(byte playerId, uint requestId, float hours,
                SurvivalEnvironment environment)
            {
                PlayerId = playerId;
                RequestId = requestId;
                Hours = hours;
                Environment = environment;
            }

            public byte PlayerId { get; private set; }
            public uint RequestId { get; private set; }
            public float Hours { get; private set; }
            public SurvivalEnvironment Environment { get; private set; }
        }

        private sealed class PendingHello
        {
            public PendingHello(SurvivalPlayerInfo player, SurvivalHello hello)
            {
                Player = player;
                Hello = hello;
            }

            public SurvivalPlayerInfo Player { get; private set; }
            public SurvivalHello Hello { get; private set; }
        }

        private sealed class RemoteEnvironmentRecord
        {
            public RemoteEnvironmentRecord(uint sequence, float receivedAt,
                SurvivalEnvironment environment)
            {
                Sequence = sequence;
                ReceivedAt = receivedAt;
                Environment = environment;
            }

            public uint Sequence { get; private set; }
            public float ReceivedAt { get; private set; }
            public SurvivalEnvironment Environment { get; private set; }
        }

    }
}
