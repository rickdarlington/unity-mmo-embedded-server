using System;
using System.Collections.Generic;
using DarkRift;
using DarkRift.Server;
using DarkRift.Server.Unity;
using UnityEngine;

public class ServerManager : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField]
    public GameObject playerPrefab;
    
    public static ServerManager Instance;

    private XmlUnityServer xmlServer;
    private DarkRiftServer server;
    
    public Dictionary<ushort, ServerPlayer> Players = new Dictionary<ushort, ServerPlayer>();
    public Dictionary<string, ServerPlayer> PlayersByName = new Dictionary<string, ServerPlayer>();
    
    private List<NetworkingData.PlayerStateData> playerStateData = new List<NetworkingData.PlayerStateData>(4);
    
    public uint ServerTick = 0;
    
    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(this);
    }
    
    void Start()
    {
        xmlServer = GetComponent<XmlUnityServer>();
        server = xmlServer.Server;
        server.ClientManager.ClientConnected += Connected;
        server.ClientManager.ClientDisconnected += Disconnected;
    }
    
    void OnDestroy()
    {
        server.ClientManager.ClientConnected -= Connected;
        server.ClientManager.ClientDisconnected -= Disconnected;
    }

    void Connected(object sender, ClientConnectedEventArgs args)
    {
        Debug.Log($"{server.ClientManager.Count} client connections total.");
        args.Client.MessageReceived += OnMessage;
    }

    void Disconnected(object sender, ClientDisconnectedEventArgs args)
    {
        IClient client = args.Client;
        ServerPlayer player;
        if (Players.TryGetValue(client.ID, out player))
        {
            Debug.Log($"{player.Name} disconnected.");
            RemovePlayer(args.Client.ID, player.Name);
        }

        args.Client.MessageReceived -= OnMessage;
    }

    private void OnMessage(object sender, MessageReceivedEventArgs e)
    {
        IClient client = (IClient) sender;
        using (Message message = e.GetMessage())
        {
            switch ((NetworkingData.Tags) message.Tag)
            {
                case NetworkingData.Tags.LoginRequest:
                    OnClientLogin(client, message.Deserialize<NetworkingData.LoginRequestData>());
                    break;
                default:
                    Debug.Log($"Unhandled tag: {message.Tag}");
                    break;
            }
        }
    }

    private void OnClientLogin(IClient client, NetworkingData.LoginRequestData data)
    {
        Debug.Log($"Login request from ({data.Name}).");
        if (PlayersByName.ContainsKey(data.Name))
        {
            using (Message message = Message.CreateEmpty((ushort) NetworkingData.Tags.LoginRequestDenied))
            {
                Debug.Log($"{data.Name} is already logged in!");
                client.SendMessage(message, SendMode.Reliable);
            }

            return;
        }

        GameObject go = Instantiate(playerPrefab, transform);
        ServerPlayer player = go.GetComponent<ServerPlayer>();
        player.Initialize(client, ServerTick, data.Name, Instance);
        client.MessageReceived -= OnMessage;

        AddPlayer(client.ID, data.Name, player);
    }

    private void AddPlayer(ushort id, string name, ServerPlayer p)
    {
        Players.Add(id, p);
        PlayersByName.Add(name, p);
        Debug.Log($"{Players.Count} players online.");
        SendNewSpawn(p);
    }

    public void RemovePlayer(ushort id, String name)
    {
        Players.Remove(id);
        PlayersByName.Remove(name);
        Debug.Log($"{Players.Count} players online.");
        SendDeSpawn(id);
    }

    public NetworkingData.PlayerSpawnData[] getAllSpawnInfo()
    {
        NetworkingData.PlayerSpawnData[] players = new NetworkingData.PlayerSpawnData[Players.Count];
        int i = 0;
        foreach (ServerPlayer player in Players.Values)
        {
            players[i] = new NetworkingData.PlayerSpawnData(player.Client.ID, player.Name, player.transform.position);
            i++;
        }

        return players;
    }

    public void SendNewSpawn(ServerPlayer p)
    {
        using (Message m = Message.Create((ushort) NetworkingData.Tags.PlayerSpawn, p.getSpawnInfo()))
        {
            foreach (KeyValuePair<ushort, ServerPlayer> kv in Players)
            {
                kv.Value.Client.SendMessage(m, SendMode.Reliable);
            }
        }
    }
    
    public void SendDeSpawn(ushort id)
    {
        using (Message m = Message.Create((ushort) NetworkingData.Tags.PlayerDeSpawn, ServerPlayer.getDespawnData(id)))
        {
            foreach (KeyValuePair<ushort, ServerPlayer> kv in Players)
            {
                kv.Value.Client.SendMessage(m, SendMode.Reliable);
            }
        }
    }


    void FixedUpdate()
    {
        ServerTick++;

        foreach (KeyValuePair<ushort, ServerPlayer> kv in Players)
        {
            ServerPlayer player = kv.Value;
            playerStateData.Add(player.PlayerUpdate());
        }

        // Send update message to all players.
        NetworkingData.PlayerStateData[] playerStateDataArray = playerStateData.ToArray();

        foreach (KeyValuePair<ushort, ServerPlayer> kv in Players)
        {

            using (Message m = Message.Create((ushort)NetworkingData.Tags.GameUpdate, new NetworkingData.GameUpdateData(kv.Value.InputTick, playerStateDataArray)))
            {
                kv.Value.Client.SendMessage(m, SendMode.Reliable);
            }
        }

        playerStateData.Clear();

    }
}