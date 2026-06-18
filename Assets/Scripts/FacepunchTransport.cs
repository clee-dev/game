using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Minimal Steam P2P transport for NGO using Facepunch.Steamworks.
/// Replaces the archived community contributions package.
///
/// Drop this file in your project. Add it as a component on your NetworkManager
/// GameObject instead of Unity Transport.
///
/// Before calling NetworkManager.StartClient(), set targetSteamId to the host's Steam ID.
/// SteamLobbyManager already does this automatically.
/// </summary>
public class FacepunchTransport : NetworkTransport
{
    [Tooltip("Steam ID of the host. SteamLobbyManager sets this before StartClient() is called.")]
    public SteamId targetSteamId;

    private FacepunchSocketManager     _server;
    private FacepunchConnectionManager _client;

    private readonly Queue<TransportEvent>          _eventQueue      = new();
    private readonly Dictionary<ulong, Connection>  _clientToConn    = new();
    private readonly Dictionary<Connection, ulong>  _connToClient    = new();
    private ulong _nextClientId = 1;

    public override ulong ServerClientId => 0;

    // -------------------------------------------------------------------------
    // NetworkTransport lifecycle
    // -------------------------------------------------------------------------

    public override void Initialize(NetworkManager networkManager = null) { }

    public override bool StartServer()
    {
        _server = SteamNetworkingSockets.CreateRelaySocket<FacepunchSocketManager>(0);
        _server.Transport = this;
        Debug.Log("[FacepunchTransport] Relay socket opened. Waiting for connections.");
        return true;
    }

    public override bool StartClient()
    {
        if (!targetSteamId.IsValid)
        {
            Debug.LogError("[FacepunchTransport] targetSteamId is not set. Cannot connect.");
            return false;
        }

        _client = SteamNetworkingSockets.ConnectRelay<FacepunchConnectionManager>(targetSteamId, 0);
        _client.Transport = this;
        Debug.Log($"[FacepunchTransport] Connecting to {targetSteamId}...");
        return true;
    }

    // NGO calls this in a loop each tick until NetworkEvent.Nothing is returned.
    // We call Receive() here to pump Facepunch callbacks into our queue,
    // then dequeue one event per call.
    public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
    {
        _server?.Receive(256);
        _client?.Receive(256);

        if (_eventQueue.Count > 0)
        {
            var e = _eventQueue.Dequeue();
            clientId    = e.ClientId;
            payload     = new ArraySegment<byte>(e.Data ?? Array.Empty<byte>());
            receiveTime = Time.realtimeSinceStartup;
            return e.Type;
        }

        clientId    = 0;
        payload     = default;
        receiveTime = 0f;
        return NetworkEvent.Nothing;
    }

    public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery delivery)
    {
        var sendType = ToSendType(delivery);

        // Copy the payload into a plain byte array (the ArraySegment offset may be non-zero)
        byte[] data = new byte[payload.Count];
        Array.Copy(payload.Array!, payload.Offset, data, 0, payload.Count);

        if (NetworkManager.Singleton.IsServer)
        {
            // Host sends to a specific connected client
            if (_clientToConn.TryGetValue(clientId, out var conn))
                conn.SendMessage(data, sendType);
        }
        else
        {
            // Client sends to host
            _client?.Connection.SendMessage(data, sendType);
        }
    }

    public override void DisconnectRemoteClient(ulong clientId)
    {
        if (_clientToConn.TryGetValue(clientId, out var conn))
            conn.Close();
    }

    public override void DisconnectLocalClient()
    {
        _client?.Connection.Close();
    }

    public override ulong GetCurrentRtt(ulong clientId) => 0;

    public override void Shutdown()
    {
        _server?.Close();
        _client?.Close();
        _server = null;
        _client = null;
        _eventQueue.Clear();
        _clientToConn.Clear();
        _connToClient.Clear();
        _nextClientId = 1;
        Debug.Log("[FacepunchTransport] Shutdown.");
    }

    // -------------------------------------------------------------------------
    // Internal callbacks -- called by the socket/connection managers below
    // -------------------------------------------------------------------------

    internal void OnClientConnectedToServer(Connection conn)
    {
        ulong id = _nextClientId++;
        _clientToConn[id]   = conn;
        _connToClient[conn] = id;
        _eventQueue.Enqueue(new TransportEvent { ClientId = id, Type = NetworkEvent.Connect });
        Debug.Log($"[FacepunchTransport] Client {id} connected.");
    }

    internal void OnClientDisconnectedFromServer(Connection conn)
    {
        if (_connToClient.TryGetValue(conn, out ulong id))
        {
            _clientToConn.Remove(id);
            _connToClient.Remove(conn);
            _eventQueue.Enqueue(new TransportEvent { ClientId = id, Type = NetworkEvent.Disconnect });
            Debug.Log($"[FacepunchTransport] Client {id} disconnected.");
        }
    }

    internal void OnMessageFromClient(Connection conn, IntPtr data, int size)
    {
        if (_connToClient.TryGetValue(conn, out ulong id))
            Enqueue(id, data, size);
    }

    internal void OnConnectedToServer()
    {
        _eventQueue.Enqueue(new TransportEvent { ClientId = ServerClientId, Type = NetworkEvent.Connect });
        Debug.Log("[FacepunchTransport] Connected to host.");
    }

    internal void OnDisconnectedFromServer()
    {
        _eventQueue.Enqueue(new TransportEvent { ClientId = ServerClientId, Type = NetworkEvent.Disconnect });
        Debug.Log("[FacepunchTransport] Disconnected from host.");
    }

    internal void OnMessageFromServer(IntPtr data, int size)
        => Enqueue(ServerClientId, data, size);

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void Enqueue(ulong clientId, IntPtr ptr, int size)
    {
        byte[] managed = new byte[size];
        Marshal.Copy(ptr, managed, 0, size);
        _eventQueue.Enqueue(new TransportEvent { ClientId = clientId, Data = managed, Type = NetworkEvent.Data });
    }

    private static SendType ToSendType(NetworkDelivery delivery) => delivery switch
    {
        NetworkDelivery.Unreliable          => SendType.Unreliable,
        NetworkDelivery.UnreliableSequenced => SendType.Unreliable,
        _                                   => SendType.Reliable,
    };

    private struct TransportEvent
    {
        public ulong        ClientId;
        public byte[]       Data;
        public NetworkEvent Type;
    }

    // -------------------------------------------------------------------------
    // Server socket manager -- handles incoming client connections
    // -------------------------------------------------------------------------

    private class FacepunchSocketManager : Steamworks.SocketManager
    {
        public FacepunchTransport Transport { get; set; }

        public override void OnConnecting(Connection connection, ConnectionInfo info)
        {
            base.OnConnecting(connection, info);
            connection.Accept();
        }

        public override void OnConnected(Connection connection, ConnectionInfo info)
        {
            base.OnConnected(connection, info);
            Transport?.OnClientConnectedToServer(connection);
        }

        public override void OnDisconnected(Connection connection, ConnectionInfo info)
        {
            base.OnDisconnected(connection, info);
            Transport?.OnClientDisconnectedFromServer(connection);
        }

        public override void OnMessage(Connection connection, NetIdentity identity,
            IntPtr data, int size, long messageNum, long recvTime, int channel)
        {
            Transport?.OnMessageFromClient(connection, data, size);
        }
    }

    // -------------------------------------------------------------------------
    // Client connection manager -- handles the outgoing connection to the host
    // -------------------------------------------------------------------------

    private class FacepunchConnectionManager : Steamworks.ConnectionManager
    {
        public FacepunchTransport Transport { get; set; }

        public override void OnConnected(ConnectionInfo info)
        {
            base.OnConnected(info);
            Transport?.OnConnectedToServer();
        }

        public override void OnDisconnected(ConnectionInfo info)
        {
            base.OnDisconnected(info);
            Transport?.OnDisconnectedFromServer();
        }

        public override void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel)
        {
            Transport?.OnMessageFromServer(data, size);
        }
    }
}