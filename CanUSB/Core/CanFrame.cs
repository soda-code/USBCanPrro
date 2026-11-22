using System;

namespace CanUSB.Core
{
    public enum CanFrameType
    {
        Standard,
        Extended
    }

    public enum CanProtocolType
    {
        CAN20,
        CANFD
    }

    public class CanFrame
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public uint Id { get; set; }
        public CanFrameType FrameType { get; set; } = CanFrameType.Standard;
        public CanProtocolType Protocol { get; set; } = CanProtocolType.CAN20;
        public bool IsRemote { get; set; } = false;
        public bool BRS { get; set; } = false; // Bit Rate Switch (CAN FD)
        public bool ESI { get; set; } = false; // Error State Indicator (CAN FD)
        public byte Dlc { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();

        public string DataHexString => BitConverter.ToString(Data).Replace("-", " ");
    }
}