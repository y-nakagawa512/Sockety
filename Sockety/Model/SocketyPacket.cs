﻿using MessagePack;

namespace Sockety.Model
{
    /// <summary>
    /// TCPで送るパケット
    /// </summary>
    [MessagePackObject]
    public class SocketyPacket
    {
        [Key(0)]
        public string MethodName { get; set; }
        [Key(1)]
        public ClientInfo clientInfo { get; set; }
        [Key(2)]
        public SOCKETY_PAKCET_TYPE SocketyPacketType { get; set; }
        [Key(3)]
        public byte[] PackData { get; set; }
        [Key(4)]
        public AuthenticationToken Toekn { get; set; }

        public enum SOCKETY_PAKCET_TYPE
        {
            Data,
            HaertBeat
        }
    }
}
