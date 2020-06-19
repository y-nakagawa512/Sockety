﻿using MessagePack;
using Microsoft.Extensions.Logging;
using Sockety.Model;
using Sockety.Service;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Sockety.Server
{
    public class ClientHub<T> : IDisposable where T : IService
    {
        private Socket serverSocket = null;
        private Thread TcpThread;
        public ClientInfo ClientInfo { get; private set; }
        private T UserClass;
        private UdpPort<T> UdpPort;
        private ServerCore<T> Parent;
        /// <summary>
        /// クライアントが切断時に発火
        /// </summary>
        public Action<ClientInfo> ConnectionReset;
        public bool KillSW = false;
        private readonly ILogger Logger;

        PacketSerivce<T> PacketSerivce;

        public ClientHub(Socket _handler,
            ClientInfo _clientInfo,
            UdpPort<T> udpPort,
            T userClass,
            ServerCore<T> parent,
            ILogger logger)
        {
            this.UserClass = userClass;
            this.serverSocket = _handler;
            this.ClientInfo = _clientInfo;
            this.UdpPort = udpPort;
            this.Parent = parent;
            this.Logger = logger;

            PacketSerivce = new PacketSerivce<T>();
            PacketSerivce.SetUp(userClass);

            MakeHeartBeat();
        }

        public void Dispose()
        {
            if (serverSocket != null)
            {
                serverSocket.Shutdown(SocketShutdown.Both);
                serverSocket.Close();
                serverSocket = null;
            }
        }

        #region HeartBeat
        private void MakeHeartBeat()
        {
            Task.Run(async () =>
            {
                while(serverSocket != null)
                {
                    await SendHeartBeat();
                    Thread.Sleep(1000);
                }
            });
        }
        private async Task SendHeartBeat()
        {
            try
            {
                lock (serverSocket)
                {
                    var packet = new SocketyPacket() { SocketyPacketType = SocketyPacket.SOCKETY_PAKCET_TYPE.HaertBeat };
                    var d = MessagePackSerializer.Serialize(packet);
                    var sizeb = BitConverter.GetBytes(d.Length);
                    serverSocket.Send(sizeb, sizeof(int), SocketFlags.None);
                    serverSocket.Send(d, d.Length, SocketFlags.None);
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine("SendHeartBeat:DisConnect");
                await DisConnect();
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        #endregion

        internal void SendNonReturn(string ClientMethodName, byte[] data)
        {
            try
            {
                lock (serverSocket)
                {
                    var packet = new SocketyPacket() { MethodName = ClientMethodName, PackData = data };
                    var d = MessagePackSerializer.Serialize(packet);
                    var sizeb = BitConverter.GetBytes(d.Length);
                    serverSocket.Send(sizeb, sizeof(int), SocketFlags.None);
                    serverSocket.Send(d, d.Length, SocketFlags.None);
                }
            }
            catch (SocketException ex)
            {
                throw ex;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        internal void SendUdp(SocketyPacketUDP packet)
        {
            try
            {
                lock (UdpPort.PunchingSocket)
                {
                    var bytes = MessagePackSerializer.Serialize(packet);
                    UdpPort.PunchingSocket.SendTo(bytes, SocketFlags.None, UdpPort.PunchingPoint);
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public class StateObject
        {
            // Client socket.  
            public Socket workSocket = null;
            // Receive buffer.  
            public byte[] Buffer;
        }

        public void Run()
        {
            TcpThread = new Thread(new ThreadStart(ReceiveProcess));
            TcpThread.Start();

            //UDPの受信を開始
            var UdpStateObject = new StateObject() { 
                Buffer = new byte[SocketySetting.MAX_BUFFER], 
                workSocket = UdpPort.PunchingSocket };

            UdpPort.PunchingSocket.BeginReceive(UdpStateObject.Buffer, 0, UdpStateObject.Buffer.Length, 0, new AsyncCallback(UdpReceiver), UdpStateObject);
        }

        /// <summary>
        /// UDP受信
        /// </summary>
        /// <param name="ar"></param>
        private void UdpReceiver(IAsyncResult ar)
        {
            try
            {
                StateObject state = (StateObject)ar.AsyncState;
                Socket client = state.workSocket;
                int bytesRead = client.EndReceive(ar);
                if (bytesRead > 0)
                {
                    Task.Run(() =>
                    {
                        var packet = MessagePackSerializer.Deserialize<SocketyPacketUDP>(state.Buffer);

                        //親クラスを呼び出す
                        PacketSerivce.ReceiverSocketyPacketUDP(packet);
                    });

                    //  受信を再スタート  
                    client.BeginReceive(state.Buffer, 0, state.Buffer.Length, 0,
                        new AsyncCallback(UdpReceiver), state);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        /// <summary>
        /// 受信を一括して行う
        /// </summary>
        private async void ReceiveProcess()
        {
            byte[] sizeb = new byte[sizeof(int)];

            while (!KillSW)
            {
                try
                {
                    if (serverSocket == null)
                    {
                        return;
                    }

                    int bytesRec = serverSocket.Receive(sizeb, sizeof(int), SocketFlags.None);
                    if (bytesRec == 0)
                    {
                        await DisConnect();
                        //受信スレッド終了
                        return;
                    }
                    int size = BitConverter.ToInt32(sizeb, 0);

                    byte[] buffer = new byte[size];
                    int DataSize = 0;
                    do
                    {

                        bytesRec = serverSocket.Receive(buffer, DataSize, size - DataSize, SocketFlags.None);

                        DataSize += bytesRec;

                    } while (size > DataSize);

                    var packet = MessagePackSerializer.Deserialize<SocketyPacket>(buffer);

                    //メソッドの戻り値を詰め替える
                    packet.PackData = await InvokeMethodAsync(packet);


                    //InvokeMethodAsyncの戻り値を送り返す
                    var d = MessagePackSerializer.Serialize(packet);
                    sizeb = BitConverter.GetBytes(d.Length);
                    serverSocket.Send(sizeb, sizeof(int), SocketFlags.None);

                    serverSocket.Send(d, d.Length, SocketFlags.None);
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.ConnectionReset)
                    {
                        await DisConnect();

                        //受信スレッド終了
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
                Thread.Sleep(10);
            }
        }

        /// <summary>
        /// クライアント起因による切断処理
        /// </summary>
        /// <returns></returns>
        private async Task DisConnect()
        {
            Logger.LogInformation($"ReceiveProcess DisConnect:{ClientInfo.ClientID}");

            //クライアント一覧から削除
            SocketClient<T>.GetInstance().ClientHubs.Remove(this);
            serverSocket = null;
            //通信切断
            await Task.Run(() => ConnectionReset?.Invoke(ClientInfo));
        }

        private async Task<byte[]> InvokeMethodAsync(SocketyPacket packet)
        {
            Type t = UserClass.GetType();
            var method = t.GetMethod(packet.MethodName);

            if (method == null)
            {
                throw new Exception("not found Method");
            }
            byte[] ret = (byte[])await Task.Run(() => method.Invoke(UserClass, new object[] { ClientInfo, packet.PackData }));

            return ret;
        }

    }
}
