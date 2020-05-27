﻿using iSocket.Model;
using MessagePack;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace iSocket.Client
{
    public class ClientReceiver<T> : IDisposable
    {
        private Socket serverSocket = null;
        private Thread thread = null;

        private byte[] CommunicateButter = new byte[1024];
        /// <summary>
        /// 通信が切断時に発火
        /// </summary>
        public Action ConnectionReset;

        #region IDisposable
        public void Dispose()
        {
            AbortReceiveProcess();
        }
        #endregion

        public void AbortReceiveProcess()
        {
            if (thread != null)
            {
                thread.Abort();
                thread = null;
            }

        }

        private ManualResetEvent RecieveSyncEvent = new ManualResetEvent(false);

        private T Parent;
        public void Run(Socket handler, T parent)
        {
            Parent = parent;
            serverSocket = handler;
            thread = new Thread(new ThreadStart(ReceiveProcess));
            thread.Start();
        }

        private string ServerCallMethodName = "";
        private byte[] ServerResponse;

        public byte[] Send(string serverMethodName,byte[] data)
        {
            lock (ServerCallMethodName)
            {
                ServerCallMethodName = serverMethodName;
            }
            ISocketPacket packet = new ISocketPacket { PackData = data };
            RecieveSyncEvent.Reset();
            serverSocket.Send(MessagePackSerializer.Serialize(packet));
            RecieveSyncEvent.WaitOne();

            return ServerResponse;
        }

        /// <summary>
        /// サーバの受信を一括して行う
        /// </summary>
        private void ReceiveProcess()
        {
            while (true)
            {
                try
                {
                    int bytesRec = serverSocket.Receive(CommunicateButter);
                    var packet = MessagePackSerializer.Deserialize<ISocketPacket>(CommunicateButter);
                    lock (RecieveSyncEvent)
                    {
                        if (string.IsNullOrEmpty(ServerCallMethodName) != true && ServerCallMethodName == packet.MethodName)
                        {
                            ServerResponse = packet.PackData;
                            RecieveSyncEvent.Set();
                        }
                        else
                        {
                            InvokeMethod(packet);
                        }
                    }
                }catch(SocketException ex)
                { 
                    if (ex.SocketErrorCode == SocketError.ConnectionReset)
                    {
                        //通信切断
                        Task.Run(() => ConnectionReset?.Invoke());

                        //受信スレッド終了
                        return;
                    }
                }catch(Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
                Thread.Sleep(10);
            }
        }

        private void InvokeMethod(ISocketPacket packet)
        {
            Type t = Parent.GetType();
            var method = t.GetMethod(packet.MethodName);

            if (method == null)
            {
                throw new Exception("not found Method");
            }
            Task.Run(() => method.Invoke(Parent, new object[] { packet.PackData }));
        }
    }
}
