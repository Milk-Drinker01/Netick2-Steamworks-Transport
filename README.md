# Netick2-Steamworks-Transport
 A Steamworks.NET transport for Netick 2

## Requirements
- Unity 2021.3 or newer
- [Netick 2 for Unity](https://github.com/NetickNetworking/NetickForUnity)
- [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET)

## Usage
Steam provides several APIs for games, including matchmaking. In order to connect using Steam you need to pass LobbyID (or friend's SteamID) as an address to `Connect` method of sandbox. 
```cs
var sandbox = Netick.Unity.Network.StartAsClient(Transport, Port, SandboxPrefab);
sandbox.Connect(Port, CurrentLobby.Owner.Id.ToString());
```
For a complete example of the lobby usage check `Assets/NetickSteamDemo/SteamNetick` scene. 

## Demo Features
 - Steam Lobbies and matchmaking example
 - Create public, private, and friends only lobbies
 - Join friends lobbies with the "Join Friend" button on steam
 - Public lobby browser with distance queries
 - Steam command line to auto-join lobbies on startup, when the game is started by clicking "join friend" on steam
 - Voice Chat (hold V to record and transmit steam voice data)

## Additional Documentation
[Netick](https://netick.net/docs/2) | [Steamworks.NET]([https://wiki.facepunch.com/steamworks/](https://steamworks.github.io/)) | [Steamworks](https://partner.steamgames.com/doc/home)
