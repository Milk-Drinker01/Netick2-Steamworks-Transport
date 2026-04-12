using Steamworks;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Netick.Examples.Steam
{
    public class SteamLobbyButton : MonoBehaviour
    {
        public Text LobbyNameText;
        public Text LobbyLatencyText;

        public CSteamID AssociatedLobby;

        private int currentGeneration = 0;

        public void OnPress()
        {
            SteamLobbyExample.JoinLobby(AssociatedLobby);
        }

        public void SetupButton(CSteamID lobby)
        {
            currentGeneration++;
            AssociatedLobby = lobby;

            LobbyNameText.text = SteamMatchmaking.GetLobbyData(AssociatedLobby, SteamLobbyExample.LobbyNameKey);
            StartCoroutine(EstimateLatency(currentGeneration));
        }

        IEnumerator EstimateLatency(int generation)
        {
            LobbyLatencyText.text = "-ms";

            float age = SteamNetworkingUtils.GetLocalPingLocation(out var pingLocation);

            while (age < 0f)
            {
                if (generation != currentGeneration || !gameObject.activeInHierarchy)
                    yield break;
                yield return new WaitForSeconds(0.5f);
                age = SteamNetworkingUtils.GetLocalPingLocation(out pingLocation);
            }

            if (generation != currentGeneration)
                yield break;

            string pingStr = SteamMatchmaking.GetLobbyData(AssociatedLobby, SteamLobbyExample.LobbyLocationKey);
            while (string.IsNullOrEmpty(pingStr))
            {
                if (generation != currentGeneration || !gameObject.activeInHierarchy)
                    yield break;
                if (!AssociatedLobby.IsValid())
                    yield break;
                SteamMatchmaking.RequestLobbyData(AssociatedLobby);
                yield return new WaitForSeconds(1f);
                pingStr = SteamMatchmaking.GetLobbyData(AssociatedLobby, SteamLobbyExample.LobbyLocationKey);
            }

            if (!SteamNetworkingUtils.ParsePingLocationString(pingStr, out var lobbyLocation))
                yield break;

            int ping = SteamNetworkingUtils.EstimatePingTimeBetweenTwoLocations(ref pingLocation, ref lobbyLocation);
            if (ping < 0)
                yield break;

            LobbyLatencyText.text = $"{ping} ms";
        }
    }
}
