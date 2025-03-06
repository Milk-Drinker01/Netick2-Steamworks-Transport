using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Netick.Unity;
using Steamworks;
using Netick.Transports.Steamworks;
using UnityEditor.Search;

public class SteamLobbyExample : MonoBehaviour
{
    public static event Action<CSteamID> OnLobbyEnteredEvent;
    public static event Action OnLobbyLeftEvent;
    public static event Action OnLobbySearchStart;
    public static event Action<List<CSteamID>> OnLobbySearchFinished;
    public static event Action OnGameServerShutdown;
    public static CSteamID CurrentLobby;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void OnLoad()
    {
        OnLobbyEnteredEvent = delegate { };
        OnLobbyLeftEvent = delegate { };
        OnLobbySearchStart = delegate { };
        OnLobbySearchFinished = delegate { };
        OnGameServerShutdown = delegate { };
    }

    public static bool OwnsCurrentLobby => SteamMatchmaking.GetLobbyOwner(CurrentLobby) == SteamUser.GetSteamID();

    [SerializeField] bool AutoStartServerWithLobby = true;

    [Header("Lobby Host Settings")]
    [SerializeField] int NumberOfSlots = 16;

    [Header("Lobby Search Settings")]
    [Tooltip("this is just so that you dont find other peoples lobbies while testing with app id 480! make it unique!")]
    [SerializeField] string GameName = "Your Games Name";
    [SerializeField] DistanceFilter LobbySearchDistance = DistanceFilter.WorldWide;
    [SerializeField] int MinimumSlotsAvailable = 1;

    [Header("Netick Settings")]
    [SerializeField] NetworkTransportProvider Transport;
    [SerializeField] GameObject SandboxPrefab;
    [SerializeField] int Port = 4050;

    private void Start()
    {
        Init();
    }
    
    async void Init()
    {
        while (!SteamManager.Initialized)
        {
            await Task.Yield();
        }
        InitLobbyCallbacks();
    }

    void OnDestroy()
    {
        SteamworksTransport.OnNetickServerStarted -= OnNetickServerStarted;
        SteamworksTransport.OnNetickShutdownEvent -= OnNetickShutdown;
    }

    private void InitLobbyCallbacks()
    {
        SteamworksTransport.OnNetickServerStarted += OnNetickServerStarted;
        SteamworksTransport.OnNetickShutdownEvent += OnNetickShutdown;

        Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);

        Callback<LobbyCreated_t>.Create(OnLobbyCreated);

        //SteamMatchmaking.OnLobbyMemberJoined += (lobby, friend) => {
        //    Debug.Log(friend.Name + " Joined the lobby");
        //};

        void OnLobbyEntered(LobbyEnter_t callback)
        {
            CSteamID lobby = (CSteamID)callback.m_ulSteamIDLobby;
            Debug.Log($"You joined {SteamMatchmaking.GetLobbyData(lobby, "LobbyName")}");
            CurrentLobby = lobby;
            OnLobbyEnteredEvent?.Invoke(lobby);

            if (AutoStartServerWithLobby && !OwnsCurrentLobby)
                ConnectToGameServer();
        }
        Callback<LobbyEnter_t>.Create(OnLobbyEntered);

        void OnLobbyGameCreated(LobbyGameCreated_t callback)
        {
            CSteamID serverGameID = (CSteamID)callback.m_ulSteamIDGameServer;
            if ((ulong)serverGameID != 0)
                Debug.Log("A server has been associated with this Lobby");

            if (AutoStartServerWithLobby && !OwnsCurrentLobby)
                ConnectToGameServer();
        }
        Callback<LobbyGameCreated_t>.Create(OnLobbyGameCreated);

        Callback<LobbyMatchList_t>.Create(OnLobbyMatchListReceived);


        //THIS CODE WILL AUTO JOIN A LOBBY IF THE GAME WAS LAUNCHED BY CLICKING "join friend" ON STEAM
        string[] args = System.Environment.GetCommandLineArgs();
        if (args.Length >= 2)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].ToLower() == "+connect_lobby")
                {
                    if (ulong.TryParse(args[i + 1], out ulong lobbyID))
                    {
                        if (lobbyID > 0)
                        {
                            SteamMatchmaking.JoinLobby((CSteamID)lobbyID);
                        }
                    }
                    break;
                }
            }
        }
    }

    #region Lobby Stuff

    private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t callback)
    {
        SteamMatchmaking.JoinLobby(callback.m_steamIDLobby);
    }

    LobbyType _lobbyType;
    List<CSteamID> Matches = new List<CSteamID>();
    public async void SearchPublicLobbies()
    {
        _lobbyType = LobbyType.Public;

        SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(MinimumSlotsAvailable);
        SteamMatchmaking.AddRequestLobbyListStringFilter("GameName", GameName, ELobbyComparison.k_ELobbyComparisonEqual);

        switch(LobbySearchDistance)
        {
            case DistanceFilter.Close: SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterClose); break;
            case DistanceFilter.Far: SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterFar); break;
            case DistanceFilter.WorldWide: SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide); break;
        }

        SteamMatchmaking.RequestLobbyList();
    }

    void OnLobbyMatchListReceived(LobbyMatchList_t callback)
    {
        Matches.Clear();
        OnLobbySearchStart?.Invoke();

        for (int i = 0; i < callback.m_nLobbiesMatching; i++)
        {
            CSteamID cSteamID = SteamMatchmaking.GetLobbyByIndex(i);

            if (!Matches.Contains(cSteamID) && SteamMatchmaking.GetNumLobbyMembers(cSteamID) != 0)
                Matches.Add(cSteamID);
        }

        OnLobbySearchFinished?.Invoke(Matches);
    }

    public void CreateLobby(LobbyType lobbyType = LobbyType.Public)
    {
        _lobbyType = lobbyType;
        SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePrivate, NumberOfSlots);
    }

    public void CreateLobby(int lobbyType = 0)
    {
        _lobbyType = (LobbyType)lobbyType;
        SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePrivate, NumberOfSlots);
    }

    void OnLobbyCreated(LobbyCreated_t callback)
    {
        CSteamID lobby = (CSteamID)callback.m_ulSteamIDLobby;
        SteamMatchmaking.SetLobbyData(lobby, "GameName", GameName);
        SteamMatchmaking.SetLobbyData(lobby, "LobbyName", $"{SteamFriends.GetPersonaName()}'s lobby.");
        SteamMatchmaking.SetLobbyJoinable(lobby, true);

        CurrentLobby = lobby;

        Debug.Log($"lobby {lobby} was created");

        switch (_lobbyType)
        {
            case LobbyType.Public:
                SteamMatchmaking.SetLobbyType(lobby, ELobbyType.k_ELobbyTypePublic);
                break;
            case LobbyType.FriendsOnly:
                SteamMatchmaking.SetLobbyType(lobby, ELobbyType.k_ELobbyTypeFriendsOnly);
                break;
            case LobbyType.Private:
                SteamMatchmaking.SetLobbyType(lobby, ELobbyType.k_ELobbyTypePrivate);
                break;
        }

        if (AutoStartServerWithLobby)
            StartGameServer();
    }

    public static void JoinLobby(CSteamID id)
    {
        SteamMatchmaking.JoinLobby(id);
    }

    public void LeaveLobby()
    {
        Debug.Log("leaving lobby");
        SteamMatchmaking.LeaveLobby(CurrentLobby);
        DisconnectFromServer();
        OnLobbyLeftEvent?.Invoke();
    }

    public enum DistanceFilter
    {
        Close,
        Default,
        Far,
        WorldWide
    }

    public enum LobbyType
    {
        Public,
        FriendsOnly,
        Private
    }
    #endregion

    #region Server Stuff

    public void StartGameServer()
    {
        if (!OwnsCurrentLobby)
        {
            Debug.LogWarning("you cant start a server, you dont own the lobby");
            return;
        }
        if (Netick.Unity.Network.IsRunning)
        {
            Debug.LogWarning("a game server is already running");
            return;
        }

        Netick.Unity.Network.StartAsHost(Transport, Port, SandboxPrefab);
    }

    #endregion

    #region Client Stuff
    public void ConnectToGameServer()
    {
        if (!SteamMatchmaking.GetLobbyGameServer(CurrentLobby, out uint ip, out ushort port, out CSteamID serverID))
        {
            Debug.LogWarning("Trying to connect to the lobbys server, but one has not been assigned");
            return;
        }

        var sandbox = Netick.Unity.Network.StartAsClient(Transport, Port, SandboxPrefab);
        sandbox.Connect(Port, serverID.m_SteamID.ToString());
    }
    #endregion

    public void DisconnectFromServer()
    {
        Debug.Log("Shutting Down Netick....");
        Netick.Unity.Network.Shutdown(true);
    }

    public void OnNetickServerStarted()
    {
        if (OwnsCurrentLobby)
        {
            SteamMatchmaking.SetLobbyGameServer(CurrentLobby, (uint)0, 0, SteamUser.GetSteamID());
        }
    }

    public void OnNetickShutdown()
    {
        OnGameServerShutdown?.Invoke();
        if (OwnsCurrentLobby)
            SteamMatchmaking.SetLobbyGameServer(CurrentLobby, 0, 0, (CSteamID)0);
        Debug.Log(SteamMatchmaking.GetLobbyGameServer(CurrentLobby, out uint ip, out ushort port, out CSteamID serverID));
    }
}