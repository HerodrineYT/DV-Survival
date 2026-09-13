using System;
using System.Collections.Generic;
using DVSurvival.Core;

namespace DVSurvival.Mod
{
    internal sealed class OfflineSurvivalBridge : ISurvivalNetworkBridge
    {
        private static readonly IReadOnlyList<SurvivalPlayerInfo> NoPlayers =
            new List<SurvivalPlayerInfo>().AsReadOnly();

        public bool IsAvailable { get { return false; } }
        public bool IsSupported { get { return true; } }
        public bool IsSessionActive { get { return false; } }
        public bool IsAuthority { get { return true; } }
        public bool IsNativeSleepTimeSuppressed { get { return false; } }
        public uint ConnectionGeneration { get { return 0u; } }
        public byte LocalPlayerId { get { return byte.MaxValue; } }
        public string MultiplayerVersion { get { return string.Empty; } }
        public string Status { get { return "Local mode"; } }
        public event Action<SurvivalPlayerInfo, SurvivalHello> HelloReceived { add { } remove { } }
        public event Action<SurvivalPlayerInfo, SurvivalActionRequest> ActionReceived { add { } remove { } }
        public event Action<SurvivalPlayerInfo, SurvivalEnvironmentReport> EnvironmentReceived { add { } remove { } }
        public event Action<byte> PlayerDisconnected { add { } remove { } }
        public event Action<SurvivalStateMessage> StateReceived { add { } remove { } }
        public void Initialize(string modId) { }
        public void SetEnabled(bool enabled) { }
        public IReadOnlyList<SurvivalPlayerInfo> GetPlayers() { return NoPlayers; }
        public void SendHello(SurvivalHello hello) { }
        public bool SendAction(SurvivalActionRequest request) { return false; }
        public void SendEnvironment(SurvivalEnvironmentReport report) { }
        public void SendStateToPlayer(byte playerId, SurvivalStateMessage message, bool reliable) { }
        public void Dispose() { }
    }
}
