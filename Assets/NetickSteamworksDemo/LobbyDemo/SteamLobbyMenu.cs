using Steamworks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Netick.Examples.Steam
{
    public class SteamLobbyMenu : MonoBehaviour
    {
        public static SteamLobbyMenu instance;

        public GameObject SearchMenu;
        public GameObject LobbyMenu;
        public GameObject LobbyContent;
        public GameObject LobbyInfoPrefab;
        public Button StartServerButton;
        public Button ConnectToServerButton;
        public Button StopServerButton;
        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                SteamLobbyExample.OnLobbyEnteredEvent += JoinedLobby;
                SteamLobbyExample.OnLobbyLeftEvent += LeftLobby;
                SteamLobbyExample.OnLobbySearchStart += ClearLobbyList;
                SteamLobbyExample.OnLobbySearchFinished += UpdateLobbyList;
                SteamLobbyExample.OnGameServerShutdown += ResetLobbyCamera;
            }
            else
                Destroy(gameObject);
        }
        
        private bool WasRunningLastFrame;
        private void Update()
        {
            bool IsRunning = Netick.Unity.Network.IsRunning;

            if (WasRunningLastFrame != IsRunning)
            {
                if (IsRunning)
                {
                    StartServerButton.interactable = false;
                    ConnectToServerButton.interactable = false;
                    StopServerButton.interactable = true;
                }
                else
                {

                    bool IsOwner = SteamUser.GetSteamID() == SteamMatchmaking.GetLobbyOwner(SteamLobbyExample.CurrentLobby);
                    if (IsOwner)
                    {
                        StartServerButton.interactable = true;
                        ConnectToServerButton.interactable = false;
                    }
                    else
                    {
                        StartServerButton.interactable = false;
                        ConnectToServerButton.interactable = true;
                    }
                    StopServerButton.interactable = false;
                }
            }

            WasRunningLastFrame = IsRunning;
        }

        public void ClearLobbyList()
        {
            for (int i = 0; i < LobbyContent.transform.childCount; i++)
                Destroy(LobbyContent.transform.GetChild(i).gameObject);
        }

        public void UpdateLobbyList(List<CSteamID> LobbyList)
        {
            foreach (var lobby in LobbyList)
            {
                var lobbyGO = Instantiate(LobbyInfoPrefab, LobbyContent.transform);
                lobbyGO.transform.GetChild(0).GetComponent<Text>().text = SteamMatchmaking.GetLobbyData(lobby, "LobbyName");
                lobbyGO.GetComponent<Button>().onClick.AddListener(() => {
                    SteamLobbyExample.JoinLobby(lobby);
                });
            }
        }

        public void JoinedLobby(CSteamID lobby)
        {
            bool IsOwner = SteamUser.GetSteamID() == SteamMatchmaking.GetLobbyOwner(lobby);
            if (IsOwner)
            {
                StartServerButton.interactable = true;
                ConnectToServerButton.interactable = false;
            }
            else
            {
                StartServerButton.interactable = false;
                ConnectToServerButton.interactable = true;
            }
            SearchMenu.SetActive(false);
            LobbyMenu.SetActive(true);
        }

        public void LeftLobby()
        {
            SearchMenu.SetActive(true);
            LobbyMenu.SetActive(false);
        }

        public void ResetLobbyCamera()
        {
            Camera cam = FindObjectOfType<Camera>();
            if (cam != null)
                cam.transform.SetParent(null);

            Cursor.lockState = CursorLockMode.Confined;
            Cursor.visible = true;
        }
    }
}