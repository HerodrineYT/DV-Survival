namespace DVSurvival.Core
{
    public sealed class HomeRespawnFlow
    {
        public bool Pending { get; private set; }
        public bool Running { get; private set; }
        public uint Revision { get; private set; }
        public void Begin() { Revision++; Pending = true; Running = false; }
        public void Reset() { Revision++; Pending = false; Running = false; }
        public bool TryStart(bool ready, bool nativeTravelBusy)
        {
            if (!Pending || Running || !ready || nativeTravelBusy) return false;
            Running = true;
            return true;
        }
        public bool Finish(uint revision, bool success)
        {
            if (revision != Revision || !Running) return false;
            Running = false;
            if (success) Pending = false;
            return true;
        }
    }
}
