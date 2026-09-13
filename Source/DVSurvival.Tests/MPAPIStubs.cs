using System.IO;

namespace MPAPI.Interfaces.Packets
{
    public interface ISerializablePacket
    {
        void Serialize(BinaryWriter writer);
        void Deserialize(BinaryReader reader);
    }
}
