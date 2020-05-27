﻿using MessagePack;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Text;

namespace iSocket.Model
{
    [MessagePackObject]
    public class ISocketPacket
    {
        [Key(0)]
        public string MethodName { get; set; }
        [Key(1)]
        public object PackData { get; set; }
    }
}
