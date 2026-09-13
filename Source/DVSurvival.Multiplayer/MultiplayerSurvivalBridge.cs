using System;
using System.Collections.Generic;
using System.Linq;
using DV.ThingTypes;
using DVSurvival.Core;
using MPAPI;
using MPAPI.Interfaces;
using MPAPI.Types;
using UnityEngine;

namespace DVSurvival.Multiplayer
{
    public sealed partial class MultiplayerSurvivalBridge : ISurvivalNetworkBridge
    {
        private string modId = "DVSurvival";
        private IServer server;
        private IClient client;
        private bool initialized;
        private bool enabled;
        private bool disposed;
        private bool serverRegistered;
        private bool clientRegistered;
        private bool isSupported;
        private uint connectionGeneration;
        private string multiplayerVersion = string.Empty;
        private float nextPlayerStateWarning;

        public bool IsAvailable { get { return MultiplayerAPI.IsMultiplayerLoaded; } }
        public bool IsSupported
        {
            get { return isSupported; }
        }
        public bool IsSessionActive
        {
            get
            {
                var api = MultiplayerAPI.Instance;
                return api != null && api.IsConnected && !api.IsSinglePlayer;
            }
        }
        public bool IsAuthority
        {
            get
            {
                var api = MultiplayerAPI.Instance;
                return !IsSessionActive || (api != null && api.IsHost);
            }
        }
        public uint ConnectionGeneration { get { return connectionGeneration; } }
        public byte LocalPlayerId { get { return client == null ? byte.MaxValue : client.PlayerId; } }
        public string MultiplayerVersion
        {
            get { return multiplayerVersion; }
        }
        public string Status
        {
            get
            {
                if (!IsAvailable) return "Multiplayer unavailable";
                if (!IsSupported) return "Multiplayer " + MultiplayerVersion + " is unsupported";
                if (!IsSessionActive) return "Multiplayer available; local session";
                return IsAuthority ? "Host authority" : "Connected client";
            }
        }

        public event Action<SurvivalPlayerInfo, SurvivalHello> HelloReceived;
        public event Action<SurvivalPlayerInfo, SurvivalActionRequest> ActionReceived;
        public event Action<SurvivalPlayerInfo, SurvivalEnvironmentReport> EnvironmentReceived;
        public event Action<byte> PlayerDisconnected;
        public event Action<SurvivalStateMessage> StateReceived;

        public void Initialize(string id)
        {
            if (initialized || disposed) return;
            modId = string.IsNullOrWhiteSpace(id) ? "DVSurvival" : id;
            initialized = true;
            var mpAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => assembly.GetName().Name == "Multiplayer");
            var timePatch = mpAssembly == null ? null : mpAssembly.GetType(
                "Multiplayer.Patches.World.TimeAdvancePatch", false);
            var timeProperty = timePatch == null ? null : timePatch.GetProperty("FastTravelAdvancesTime");
            if (timeProperty != null && timeProperty.PropertyType == typeof(bool))
                nativeTimeAdvanceEnabled = (Func<bool>)Delegate.CreateDelegate(
                    typeof(Func<bool>), timeProperty.GetGetMethod());
            MultiplayerAPI.ServerStarted += OnServerStarted;
            MultiplayerAPI.ServerStopped += OnServerStopped;
            MultiplayerAPI.ClientStarted += OnClientStarted;
            MultiplayerAPI.ClientStopped += OnClientStopped;
            ConfigureCompatibility();
            RefreshCompatibility();
            if (VersionUtil.Compare(MultiplayerVersion, SurvivalConstants.TestedMultiplayerVersion) > 0)
                Debug.LogWarning("[DVSurvival MP] Multiplayer " + MultiplayerVersion +
                    " is newer than tested " + SurvivalConstants.TestedMultiplayerVersion +
                    "; verify DVSurvival compatibility.");
            if (MultiplayerAPI.Server != null) RegisterServer(MultiplayerAPI.Server);
            if (MultiplayerAPI.Client != null) RegisterClient(MultiplayerAPI.Client);
        }

        public void SetEnabled(bool value)
        {
            enabled = value;
        }

        public IReadOnlyList<SurvivalPlayerInfo> GetPlayers()
        {
            var result = new List<SurvivalPlayerInfo>();
            IEnumerable<IPlayer> source = null;
            if (IsAuthority && server != null) source = server.Players;
            else if (client != null) source = client.Players;
            if (source != null)
            {
                foreach (var player in source)
                {
                    var converted = ConvertPlayer(player);
                    if (converted != null) result.Add(converted);
                }
            }
            if (client != null && result.All(p => p.PlayerId != client.PlayerId))
            {
                var position = PlayerManager.PlayerTransform == null
                    ? Vector3.zero
                    : PlayerManager.PlayerTransform.position;
                var car = PlayerManager.Car;
                result.Add(new SurvivalPlayerInfo
                {
                    PlayerId = client.PlayerId,
                    DisplayName = "Local player",
                    PositionX = position.x,
                    PositionY = position.y,
                    PositionZ = position.z,
                    IsHost = IsAuthority,
                    IsLoaded = PlayerManager.PlayerTransform != null,
                    IsOnCar = car != null,
                    OccupiedCarIsLocomotive = car != null && car.IsLoco,
                    OccupiedCarId = GetCarId(car),
                    OccupiedCarSupportsCabHeater = SupportsCabHeater(car),
                });
            }
            return result.AsReadOnly();
        }

        public void SendHello(SurvivalHello hello)
        {
            if (!enabled || disposed || client == null || !IsSessionActive || IsAuthority || hello == null) return;
            client.SendSerializablePacketToServer(new SurvivalHelloPacket { Hello = hello }, true);
        }

        public bool SendAction(SurvivalActionRequest request)
        {
            if (!enabled || disposed || client == null || !IsSessionActive || IsAuthority || request == null)
                return false;
            client.SendSerializablePacketToServer(new SurvivalActionPacket { Request = request }, true);
            return true;
        }

        public void SendEnvironment(SurvivalEnvironmentReport report)
        {
            if (!enabled || disposed || client == null || !IsSessionActive || IsAuthority || report == null)
                return;
            client.SendSerializablePacketToServer(new SurvivalEnvironmentPacket { Report = report }, false);
        }

        public void SendStateToPlayer(byte playerId, SurvivalStateMessage message, bool reliable)
        {
            if (!enabled || disposed || server == null || !IsAuthority || message == null) return;
            var player = server.GetPlayer(playerId);
            if (player == null) return;
            server.SendSerializablePacketToPlayer(new SurvivalStatePacket { Message = message }, player, reliable);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            enabled = false;
            if (initialized)
            {
                MultiplayerAPI.ServerStarted -= OnServerStarted;
                MultiplayerAPI.ServerStopped -= OnServerStopped;
                MultiplayerAPI.ClientStarted -= OnClientStarted;
                MultiplayerAPI.ClientStopped -= OnClientStopped;
            }
            DetachServer();
            server = null;
            client = null;
        }

        private void OnServerStarted(IServer value)
        {
            DetachServer();
            serverRegistered = false;
            ConfigureCompatibility();
            RefreshCompatibility();
            RegisterServer(value);
        }

        private void OnServerStopped()
        {
            DetachServer();
            server = null;
            serverRegistered = false;
        }

        private void OnClientStarted(IClient value)
        {
            clientRegistered = false;
            ConfigureCompatibility();
            RefreshCompatibility();
            RegisterClient(value);
        }

        private void OnClientStopped()
        {
            client = null;
            clientRegistered = false;
            AdvanceConnectionGeneration();
        }

        private void RegisterServer(IServer value)
        {
            if (serverRegistered || value == null) return;
            server = value;
            server.RegisterSerializablePacket<SurvivalHelloPacket>(OnHelloPacket);
            server.RegisterSerializablePacket<SurvivalActionPacket>(OnActionPacket);
            server.RegisterSerializablePacket<SurvivalEnvironmentPacket>(OnEnvironmentPacket);
            server.OnPlayerDisconnected += OnServerPlayerDisconnected;
            serverRegistered = true;
            Debug.Log("[DVSurvival MP] Server handlers registered (protocol " +
                SurvivalConstants.ProtocolVersion + ").");
        }

        private void RegisterClient(IClient value)
        {
            if (clientRegistered || value == null) return;
            if (!ReferenceEquals(client, value)) AdvanceConnectionGeneration();
            client = value;
            client.RegisterSerializablePacket<SurvivalStatePacket>(OnStatePacket);
            clientRegistered = true;
            Debug.Log("[DVSurvival MP] Client handler registered (protocol " +
                SurvivalConstants.ProtocolVersion + ", Multiplayer " + MultiplayerVersion + ").");
        }

        private void AdvanceConnectionGeneration()
        {
            unchecked
            {
                connectionGeneration++;
                if (connectionGeneration == 0u) connectionGeneration++;
            }
        }

        private void DetachServer()
        {
            if (server != null) server.OnPlayerDisconnected -= OnServerPlayerDisconnected;
        }

        private void OnHelloPacket(SurvivalHelloPacket packet, IPlayer sender)
        {
            if (!enabled || packet == null || packet.Hello == null || sender == null) return;
            var handler = HelloReceived;
            if (handler != null) handler(ConvertPlayer(sender), packet.Hello);
        }

        private void OnActionPacket(SurvivalActionPacket packet, IPlayer sender)
        {
            if (!enabled || packet == null || packet.Request == null || sender == null) return;
            var handler = ActionReceived;
            if (handler != null) handler(ConvertPlayer(sender), packet.Request);
        }

        private void OnStatePacket(SurvivalStatePacket packet)
        {
            if (!enabled || packet == null || packet.Message == null) return;
            var handler = StateReceived;
            if (handler != null) handler(packet.Message);
        }

        private void OnEnvironmentPacket(SurvivalEnvironmentPacket packet, IPlayer sender)
        {
            if (!enabled || packet == null || packet.Report == null || sender == null) return;
            var handler = EnvironmentReceived;
            if (handler != null) handler(ConvertPlayer(sender), packet.Report);
        }

        private void OnServerPlayerDisconnected(IPlayer player)
        {
            if (!enabled || player == null) return;
            var handler = PlayerDisconnected;
            if (handler != null) handler(player.PlayerId);
        }

        private void ConfigureCompatibility()
        {
            if (MultiplayerAPI.Instance != null)
                MultiplayerAPI.Instance.SetModCompatibility(modId, MultiplayerCompatibility.All);
        }

        private void RefreshCompatibility()
        {
            var api = MultiplayerAPI.Instance;
            multiplayerVersion = api == null ? string.Empty : (api.MultiplayerVersion ?? string.Empty);
            isSupported = VersionUtil.Compare(multiplayerVersion,
                SurvivalConstants.MinimumMultiplayerVersion) >= 0;
        }

        private SurvivalPlayerInfo ConvertPlayer(IPlayer player)
        {
            if (player == null) return null;
            try
            {
                var position = player.Position;
                var car = player.OccupiedCar;
                return new SurvivalPlayerInfo
                {
                    PlayerId = player.PlayerId,
                    DisplayName = player.DisplayName ?? player.Username ?? string.Empty,
                    PositionX = position.x,
                    PositionY = position.y,
                    PositionZ = position.z,
                    IsHost = player.IsHost,
                    IsLoaded = player.IsLoaded,
                    IsOnCar = player.IsOnCar,
                    OccupiedCarIsLocomotive = car != null && car.IsLoco,
                    OccupiedCarId = GetCarId(car),
                    OccupiedCarSupportsCabHeater = SupportsCabHeater(car),
                };
            }
            catch (Exception exception)
            {
                if (Time.realtimeSinceStartup >= nextPlayerStateWarning)
                {
                    nextPlayerStateWarning = Time.realtimeSinceStartup + 10f;
                    Debug.LogWarning("[DVSurvival MP] Could not read player state: " + exception.Message);
                }
                return null;
            }
        }

        private static string GetCarId(TrainCar car)
        {
            if (car == null) return string.Empty;
            var id = car.CarGUID;
            if (string.IsNullOrEmpty(id)) return string.Empty;
            return id.Length <= 80 ? id : id.Substring(0, 80);
        }

        private static bool SupportsCabHeater(TrainCar car)
        {
            if (car == null) return false;
            return car.carType == TrainCarType.LocoShunter ||
                car.carType == TrainCarType.LocoDH4 ||
                car.carType == TrainCarType.LocoDM3 ||
                car.carType == TrainCarType.LocoDiesel;
        }

    }
}
