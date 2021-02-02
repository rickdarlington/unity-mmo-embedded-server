using System;
using System.Collections.Generic;
using System.Linq;
using DarkRift;
using DarkRift.Server;
using UnityEngine;
using Debug = UnityEngine.Debug;

[RequireComponent(typeof(PlayerLogic))]
public class ServerPlayer : MonoBehaviour
{
    private NetworkingData.PlayerStateData currentPlayerStateData;
    private Buffer<NetworkingData.PlayerInputData> inputBuffer = new Buffer<NetworkingData.PlayerInputData>(1, 2);

    private ServerManager Instance;
    
    public PlayerLogic PlayerLogic { get; private set; }
    public uint InputTick { get; private set; }
    public IClient Client { get; private set; }

    public String Name = "";
    
    public List<NetworkingData.PlayerStateData> PlayerStateDataHistory { get; } = new List<NetworkingData.PlayerStateData>();

    private NetworkingData.PlayerInputData[] inputs = {};

    void Awake()
    {
        PlayerLogic = GetComponent<PlayerLogic>();
    }

    public void Initialize(IClient client, uint tick, String name, ServerManager instance)
    {
        Client = client;
        currentPlayerStateData = new NetworkingData.PlayerStateData(client.ID, new Vector2(0, 0), 0);
        InputTick = tick;
        Name = name;
        Instance = instance;
        
        Client.MessageReceived += OnMessage;
        
        transform.position = new Vector2(0,0);

        Debug.Log($"Connection for {Name} configured, sending login accept.");

        using (Message m = Message.Create((ushort) NetworkingData.Tags.LoginRequestAccepted,
            new NetworkingData.LoginInfoData(client.ID)))
        {
            client.SendMessage(m, SendMode.Reliable);
        }
    }

    private void OnMessage(object sender, MessageReceivedEventArgs args)
    {
        using (Message message = args.GetMessage())
        {
            switch ((NetworkingData.Tags) message.Tag)
            {
                case NetworkingData.Tags.PlayerReady:
                    PlayerReady();
                    break;
                case NetworkingData.Tags.GamePlayerInput:
                    RecieveInput(message.Deserialize<NetworkingData.PlayerInputData>());
                    break;
                default:
                    Debug.Log($"Unhandled tag: {message.Tag}");
                    break;
            }
        }
    }

    private void PlayerReady()
    {
        using (Message m = Message.Create((ushort) NetworkingData.Tags.GameStartData,
            new NetworkingData.GameStartData(ServerManager.Instance.getAllSpawnInfo(), Instance.ServerTick)))
        {
            Debug.Log("Sending Game Start Data");

            Client.SendMessage(m, SendMode.Reliable);
        }
    }

    public NetworkingData.PlayerSpawnData getSpawnInfo()
    {
        return new NetworkingData.PlayerSpawnData(Client.ID, Name, transform.position);
    }
    
    public static NetworkingData.PlayerDespawnData getDespawnData(ushort id)
    {
        return new NetworkingData.PlayerDespawnData(id);
    }

    public void RecieveInput(NetworkingData.PlayerInputData input)
    {
        inputBuffer.Add(input);
    }

    public NetworkingData.PlayerStateData PlayerUpdate()
    {
        inputs = inputBuffer.Get();
        if (inputs.Length > 0)
        {
            NetworkingData.PlayerInputData input = inputs.First();
            InputTick++;

            for (int i = 1; i < inputs.Length; i++)
            {
                InputTick++;
                for (int j = 0; j < input.Keyinputs.Length; j++)
                {
                    input.Keyinputs[j] = input.Keyinputs[j] || inputs[i].Keyinputs[j];
                }
                input.LookDirection = inputs[i].LookDirection;
            }

            currentPlayerStateData = PlayerLogic.GetNextFrameData(Client.ID, input);
        }
        
        PlayerStateDataHistory.Add(currentPlayerStateData);
        if (PlayerStateDataHistory.Count > 10)
        {
            PlayerStateDataHistory.RemoveAt(0);
        }

        /*if (Name == "asdf")
        {
            Debug.Log(transform.localPosition);
        }*/
        transform.localPosition = currentPlayerStateData.Position;
        return currentPlayerStateData;
    }
}