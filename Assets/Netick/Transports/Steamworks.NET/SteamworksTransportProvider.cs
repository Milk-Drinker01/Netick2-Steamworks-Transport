using Netick.Unity;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net;
using System.Runtime.InteropServices;
using UnityEngine;
using Network = Netick.Unity.Network;
using static Netick.Transports.Steamworks.SteamworksTransportProvider;

namespace Netick.Transports.Steamworks
{
    [CreateAssetMenu(fileName = "SteamworksTransport", menuName = "Netick/Transport/SteamworksTransport", order = 2)]
    public class SteamworksTransportProvider : NetworkTransportProvider
    {
        //[SerializeField]
        //EP2PSend SteamDataSendType = EP2PSend.k_EP2PSendUnreliableNoDelay;
        [SerializeField]
        SteamSendType SteamDataSendType = SteamSendType.NoNagle;

        [SerializeField]
        bool FlushMessages = true;

#if UNITY_EDITOR
        public void OnValidate()
        {
            SteamworksTransport.SetSendType(SteamDataSendType);
            SteamworksTransport.ForceFlush = FlushMessages;
        }
#endif
        public override NetworkTransport MakeTransportInstance() => new SteamworksTransport(SteamDataSendType, FlushMessages);

        public enum SteamSendType : int
        {
            Unreliable = 0,
            Reliable = 1,
            NoNagle = 2,
            NoDelay = 3
        }
    }

    public class SteamworksTransport : NetworkTransport
    {
        public static event Action OnNetickServerStarted;
        public static event Action OnNetickClientStarted;
        public static event Action OnNetickShutdownEvent;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnLoad()
        {
            OnNetickServerStarted = delegate { };
            OnNetickClientStarted = delegate { };
            OnNetickShutdownEvent = delegate { };
        }

        public class SteamworksConnection : TransportConnection
        {
            public SteamworksTransport Transport;
            public HSteamNetConnection Connection;
            public ulong PlayerSteamID;
            public override IEndPoint EndPoint => new IPEndPoint(IPAddress.Any, 4050).ToNetickEndPoint();
            public override int Mtu => Constants.k_cbMaxSteamNetworkingSocketsMessageSizeSend;
            public unsafe override void Send(IntPtr ptr, int length)
            {
                SteamNetworkingSockets.SendMessageToConnection(Connection, ptr, (uint)length, SteamSendFlag, out long _);
                if (SteamworksTransport.ForceFlush)
                    SteamNetworkingSockets.FlushMessagesOnConnection(Connection);
            }
            public unsafe override void SendUserData(IntPtr ptr, int length, TransportDeliveryMethod method)
            {
                int sendType = method == TransportDeliveryMethod.Unreliable ? Constants.k_nSteamNetworkingSend_Unreliable : Constants.k_nSteamNetworkingSend_Reliable;
                SteamNetworkingSockets.SendMessageToConnection(Connection, ptr, (uint)length, sendType, out long _);
            }
        }

        const int MAX_MESSAGES = 256;
        IntPtr[] _ptrs = new IntPtr[MAX_MESSAGES];

        public static int SteamSendFlag = 0;
        public static bool ForceFlush;

        static readonly Dictionary<HSteamNetConnection, SteamworksConnection> InternalConnections = new Dictionary<HSteamNetConnection, SteamworksConnection>();

        static SteamworksConnection clientToServerConnection;

        BitBuffer _buffer;

        private HSteamListenSocket listenSocket;
        private Callback<SteamNetConnectionStatusChangedCallback_t> c_onConnectionChange = null;
        private HSteamNetConnection HostConnection;

        public bool IsServer;

        public SteamworksTransport(SteamSendType sendType, bool forceFlush)
        {
            SetSendType(sendType);
            ForceFlush = forceFlush;
        }

        public static void SetSendType(SteamSendType sendType)
        {
            switch (sendType)
            {
                case SteamSendType.Unreliable: SteamSendFlag = Constants.k_nSteamNetworkingSend_Unreliable; break;
                case SteamSendType.Reliable: SteamSendFlag = Constants.k_nSteamNetworkingSend_Reliable; break;
                case SteamSendType.NoNagle: SteamSendFlag = Constants.k_nSteamNetworkingSend_UnreliableNoNagle; break;
                case SteamSendType.NoDelay: SteamSendFlag = Constants.k_nSteamNetworkingSend_UnreliableNoDelay; break;
            }
        }

        public static ulong SteamID { get; private set; }

        public static ulong GetPlayerSteamID(NetworkPlayer player)
        {
            //return server player id
            if (player is Server) return SteamID;

            //return client player id
            var networkConnection = (NetworkConnection)player;
            var steamworksConnection = (SteamworksConnection)networkConnection.TransportConnection;
            return steamworksConnection.PlayerSteamID;
        }

        private Queue<SteamworksConnection> _freeConnections = new Queue<SteamworksConnection>();
        public override void Init()
        {
            Debug.Log($"[{nameof(SteamworksTransport)}] - Initializing Transport");

            if (!SteamManager.Initialized)
            {
                Debug.Log($"[{nameof(SteamworksTransport)}] - SteamClient wasn't initialized. ");
                return;
            }

            InitSteamworks();

            _buffer = new BitBuffer(createChunks: false);

            for (int i = 0; i < Engine.MaxClients; i++)
                _freeConnections.Enqueue(new SteamworksConnection());
        }

        async void InitSteamworks()
        {
            while (!SteamManager.Initialized)
            {
                await Task.Yield();
            }

            SteamNetworkingUtils.InitRelayNetworkAccess();

            SteamID = SteamUser.GetSteamID().m_SteamID;
        }

        public override void Run(RunMode mode, int port)
        {
            switch (mode)
            {
                
                case RunMode.Server:
                    {
                        Debug.Log($"[{nameof(SteamworksTransport)}] - Starting as server");

                        SteamNetworkingConfigValue_t[] options = new SteamNetworkingConfigValue_t[] { };
                        listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, options.Length, options);
                        c_onConnectionChange = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChangedServer);

                        IsServer = true;
                        OnNetickServerStarted?.Invoke();
                        break;
                    }
                case RunMode.Client:
                    {
                        Debug.Log($"[{nameof(SteamworksTransport)}] - Starting as client");

                        IsServer = false;
                        OnNetickClientStarted?.Invoke();
                        break;
                    }
            }
        }

        public override void Shutdown()
        {
            try
            {
                if (IsServer)
                {
                    SteamNetworkingSockets.CloseListenSocket(listenSocket);
                }
                else
                {
                    if (HostConnection.m_HSteamNetConnection != 0)
                    {
                        Debug.Log($"[{nameof(SteamworksTransport)}] - Sending Disconnect message");
                        SteamNetworkingSockets.CloseConnection(HostConnection, 0, "Graceful disconnect", false);
                        HostConnection.m_HSteamNetConnection = 0;
                    }
                }

                if (c_onConnectionChange != null)
                {
                    c_onConnectionChange.Dispose();
                    c_onConnectionChange = null;
                }
            }
            catch (Exception e)
            {
                Debug.Log($"[{nameof(SteamworksTransport)}] - Shutting down error: {e}");
            }

            OnNetickShutdownEvent?.Invoke();
        }

        [System.Serializable]
        [StructLayout(LayoutKind.Sequential)]
        public struct SteamNetworkingMessage
        {
            /// Message payload
            public IntPtr m_pData;

            /// Size of the payload.
            public int m_cbSize;
        }

        public override void PollEvents()
        {
            if (IsServer)
            {
                foreach(KeyValuePair<HSteamNetConnection, SteamworksConnection> conn in InternalConnections)
                {
                    int messageCount;
                    if ((messageCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(conn.Key, _ptrs, MAX_MESSAGES)) > 0)
                    {
                        for (int i = 0; i < messageCount; i++)
                        {
                            OnMessage(conn.Value, _ptrs[i]);
                        }
                    }
                }
            }
            else
            {
                int messageCount;
                if ((messageCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(HostConnection, _ptrs, MAX_MESSAGES)) > 0)
                {
                    for (int i = 0; i < messageCount; i++)
                    {
                        OnMessage(clientToServerConnection, _ptrs[i]);
                    }
                }
            }
        }

        unsafe void OnMessage(SteamworksConnection conn, IntPtr msg)
        {
            SteamNetworkingMessage data = *(SteamNetworkingMessage*)msg;

            var ptr = (byte*)data.m_pData;
            _buffer.SetFrom(ptr, data.m_cbSize, data.m_cbSize);
            NetworkPeer.Receive(conn, _buffer);

            SteamNetworkingMessage_t.Release(msg);
        }


        public override void Disconnect(TransportConnection connection)
        {
            SteamworksConnection steamworksonnection = (SteamworksConnection)connection;
            SteamNetworkingSockets.FlushMessagesOnConnection(steamworksonnection.Connection);
            SteamNetworkingSockets.CloseConnection(steamworksonnection.Connection, 0, "Disconnected by server", false);
            InternalConnections.Remove(steamworksonnection.Connection);

            Debug.Log($"[{nameof(SteamworksTransport)}] - Player {(steamworksonnection).PlayerSteamID} Disconnected from server.");
        }

        #region SERVER
        
        void OnConnectionStatusChangedServer(SteamNetConnectionStatusChangedCallback_t param)
        {
            switch(param.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting: ServerOnConnecting(param); break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected: ServerOnConnected(param); break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer: ServerOnDisconnected(param); break;
            }
        }

        void ServerOnConnecting(SteamNetConnectionStatusChangedCallback_t param)
        {
            ulong clientSteamID = param.m_info.m_identityRemote.GetSteamID64();
            if (Engine.ConnectedPlayers.Count == Engine.MaxClients)
            {
                Debug.Log($"[{nameof(SteamworksTransport)}] - Declining connection from Steam user {clientSteamID}. (server is full)");
                SteamNetworkingSockets.CloseConnection(param.m_hConn, 0, "Max Connection Count", false);
            }
            else
            {
                EResult res;
                if ((res = SteamNetworkingSockets.AcceptConnection(param.m_hConn)) == EResult.k_EResultOK)
                    Debug.Log($"[{nameof(SteamworksTransport)}] - Accepting connection from Steam user {clientSteamID}.");
                else
                    Debug.Log($"[{nameof(SteamworksTransport)}] - Connection {clientSteamID} could not be accepted: {res.ToString()}.");
            }
        }

        void ServerOnConnected(SteamNetConnectionStatusChangedCallback_t param)
        {
            ulong clientSteamID = param.m_info.m_identityRemote.GetSteamID64();

            var steamworksConnection = _freeConnections.Dequeue();
            steamworksConnection.Connection = param.m_hConn;
            steamworksConnection.PlayerSteamID = clientSteamID;

            if (InternalConnections.TryAdd(param.m_hConn, steamworksConnection))
            {
                Debug.Log($"[{nameof(SteamworksTransport)}] - Connected with Steam user {clientSteamID}.");
                NetworkPeer.OnConnected(InternalConnections[param.m_hConn]);
            }
            else
                Debug.LogWarning($"[{nameof(SteamworksTransport)}] - Failed to connect client with ID {clientSteamID}, client already connected.");

        }

        //void OnDisconnected(Steamworks.Data.Connection connection, ConnectionInfo info)
        void ServerOnDisconnected(SteamNetConnectionStatusChangedCallback_t param)
        {
            Debug.Log($"[{nameof(SteamworksTransport)}] - Disconnected Steam user {InternalConnections[param.m_hConn].PlayerSteamID}");

            _freeConnections.Enqueue(InternalConnections[param.m_hConn]);
            NetworkPeer.OnDisconnected(InternalConnections[param.m_hConn], TransportDisconnectReason.Timeout);
            InternalConnections.Remove(param.m_hConn);

            SteamNetworkingSockets.CloseConnection(param.m_hConn, 0, "Graceful disconnect", false);
        }

        #endregion

        #region CLIENT

        public override void Connect(string address, int port, byte[] connectionData, int connectionDataLen)
        {
            if (!ulong.TryParse(address, out var ID))
                return;

            c_onConnectionChange = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChangedClient);

            CSteamID hostSteamId = new CSteamID(ID);

            SteamNetworkingIdentity smi = new SteamNetworkingIdentity();
            smi.SetSteamID(hostSteamId);

            SteamNetworkingConfigValue_t[] options = new SteamNetworkingConfigValue_t[] { };
            HostConnection = SteamNetworkingSockets.ConnectP2P(ref smi, 0, options.Length, options);
        }

        void OnConnectionStatusChangedClient(SteamNetConnectionStatusChangedCallback_t param)
        {
            switch (param.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting: ServerOnConnecting(param); break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected: ServerOnConnected(param); break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer: ServerOnDisconnected(param); break;
            }
        }

        void ClientOnConnecting(SteamNetConnectionStatusChangedCallback_t param)
        {
            Debug.Log($"[{nameof(SteamworksTransport)}] - Connecting with Steam user {param.m_info.m_identityRemote.GetSteamID64()}.");
        }

        void ClientOnConnected(SteamNetConnectionStatusChangedCallback_t param)
        {
            ulong clientSteamID = param.m_info.m_identityRemote.GetSteamID64();

            var steamworksConnection = new SteamworksConnection
            {
                Connection = param.m_hConn,
            };

            if (InternalConnections.TryAdd(param.m_hConn, steamworksConnection))
            {
                Debug.Log($"[{nameof(SteamworksTransport)}] - Connected with Steam user {clientSteamID}.");

                clientToServerConnection = steamworksConnection;
                NetworkPeer.OnConnected(InternalConnections[param.m_hConn]);

            }
            else
                Debug.LogWarning($"[{nameof(SteamworksTransport)}] - Failed to connect with Steam user {clientSteamID}, client already connected.");
        }

        void ClientOnDisconnected(SteamNetConnectionStatusChangedCallback_t param)
        {
            NetworkPeer.OnDisconnected(InternalConnections[param.m_hConn], TransportDisconnectReason.Timeout);
            InternalConnections.Clear();
            clientToServerConnection = null;

            Debug.Log($"[{nameof(SteamworksTransport)}] - You have been removed from the server (either you were kicked, or the server shut down).");

            Network.Shutdown(true);
        }

        #endregion

    }
}
