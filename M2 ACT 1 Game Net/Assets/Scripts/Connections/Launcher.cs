// Launcher.cs (LobbyScene)
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon; // for Hashtable

namespace Com.MyCompany.MyGame
{
    public class Launcher : MonoBehaviourPunCallbacks
    {
        [SerializeField] private byte maxPlayersPerRoom = 4;
        string gameVersion = "1";
        bool isConnecting;

        [Header("General UI")]
        [SerializeField] private GameObject controlPanel;   // connect UI
        [SerializeField] private GameObject progressLabel;

        [Header("Lobby UI")]
        [SerializeField] private GameObject lobbyPanel;
        [SerializeField] private InputField createRoomInput;
        [SerializeField] private Transform roomListContent; // Content transform inside Scroll View
        [SerializeField] private GameObject roomListEntryPrefab;

        [Header("Room Scene")]
        [SerializeField] private string roomSceneName = "RoomScene";

        private Dictionary<string, GameObject> roomListEntries = new Dictionary<string, GameObject>();
        public static Launcher Instance { get; private set; }

        void Awake()
        {
            // ensure scene sync is enabled before connecting
            PhotonNetwork.AutomaticallySyncScene = true;

            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            // Try to get into the lobby flow (this method is defensive about current Photon state)
            TryEnterLobbyFlow();
        }

        void Start()
        {
            if (progressLabel != null) progressLabel.SetActive(true);
            if (controlPanel != null) controlPanel.SetActive(true);
            if (lobbyPanel != null) lobbyPanel.SetActive(false);
        }

        #region UI Actions
        // Public Connect can be called by other scripts (fallback)
        public void Connect()
        {
            TryEnterLobbyFlow();
        }

        // Core defensive logic to attempt lobby/connect without racing
        private void TryEnterLobbyFlow()
        {
            // Show progress while we decide / connect
            if (progressLabel != null) progressLabel.SetActive(false);
            if (controlPanel != null) controlPanel.SetActive(false);

            PhotonNetwork.GameVersion = gameVersion;

            Debug.Log($"Launcher: TryEnterLobbyFlow. IsConnected={PhotonNetwork.IsConnected}, IsConnectedAndReady={PhotonNetwork.IsConnectedAndReady}, InLobby={PhotonNetwork.InLobby}, InRoom={PhotonNetwork.InRoom}");

            if (!PhotonNetwork.IsConnected)
            {
                // Not connected at all -> start connection
                isConnecting = PhotonNetwork.ConnectUsingSettings();
                Debug.Log("Launcher: ConnectUsingSettings() called, isConnecting=" + isConnecting);
                return;
            }

            // If already connected AND in lobby -> show lobby UI
            if (PhotonNetwork.InLobby)
            {
                Debug.Log("Launcher: Already in lobby — showing lobby UI.");
                OnJoinedLobby(); // reuse the callback behavior
                return;
            }

            // Connected but not in lobby
            // Only call JoinLobby if client is ready (connected to master)
            if (PhotonNetwork.IsConnectedAndReady)
            {
                Debug.Log("Launcher: Connected and ready -> JoinLobby()");
                PhotonNetwork.JoinLobby();
            }
            else
            {
                // Connected but not fully ready yet (e.g. ConnectingToMasterServer). Wait for OnConnectedToMaster callback.
                Debug.Log("Launcher: Connected but not yet ready. Waiting for OnConnectedToMaster to JoinLobby.");
                isConnecting = true;
                // Keep progress UI active — OnConnectedToMaster will call JoinLobby when ready.
            }
        }

        public void CreateRoomFromInput()
        {
            string rn = (createRoomInput != null) ? createRoomInput.text.Trim() : "";
            if (string.IsNullOrEmpty(rn))
            {
                rn = "Room_" + Random.Range(1000, 9999);
            }

            Hashtable props = new Hashtable { { "scene", roomSceneName } };
            RoomOptions options = new RoomOptions
            {
                MaxPlayers = maxPlayersPerRoom,
                CustomRoomProperties = props,
                CustomRoomPropertiesForLobby = new string[] { "scene" }
            };

            PhotonNetwork.CreateRoom(rn, options);
        }

        public void JoinRoomByName(string roomName)
        {
            if (string.IsNullOrEmpty(roomName)) return;
            PhotonNetwork.JoinRoom(roomName);
        }
        #endregion

        #region Photon Callbacks
        public override void OnConnectedToMaster()
        {
            Debug.Log("Launcher: OnConnectedToMaster");
            // Only automatically JoinLobby if this connection attempt was triggered by our flow
            if (isConnecting)
            {
                Debug.Log("Launcher: OnConnectedToMaster -> JoinLobby()");
                PhotonNetwork.JoinLobby();
            }
        }

        public override void OnDisconnected(DisconnectCause cause)
        {
            Debug.LogWarningFormat("Launcher: OnDisconnected {0}", cause);
            isConnecting = false;
            if (progressLabel != null) progressLabel.SetActive(false);
            if (controlPanel != null) controlPanel.SetActive(true);
            if (lobbyPanel != null) lobbyPanel.SetActive(false);
        }

        public override void OnJoinedLobby()
        {
            Debug.Log("Launcher: OnJoinedLobby - show lobby UI");
            isConnecting = false;
            if (progressLabel != null) progressLabel.SetActive(false);
            if (controlPanel != null) controlPanel.SetActive(false);
            if (lobbyPanel != null) lobbyPanel.SetActive(true);
        }

        public override void OnRoomListUpdate(List<RoomInfo> roomList)
        {
            foreach (RoomInfo info in roomList)
            {
                if (info.RemovedFromList)
                {
                    if (roomListEntries.ContainsKey(info.Name))
                    {
                        Destroy(roomListEntries[info.Name]);
                        roomListEntries.Remove(info.Name);
                    }
                }
                else
                {
                    if (!roomListEntries.ContainsKey(info.Name))
                    {
                        GameObject entry = Instantiate(roomListEntryPrefab, roomListContent);
                        var comp = entry.GetComponent<RoomListEntry>();
                        if (comp != null) comp.SetInfo(info.Name, info.PlayerCount, info.MaxPlayers);
                        roomListEntries.Add(info.Name, entry);
                    }
                    else
                    {
                        var comp = roomListEntries[info.Name].GetComponent<RoomListEntry>();
                        if (comp != null) comp.SetInfo(info.Name, info.PlayerCount, info.MaxPlayers);
                    }
                }
            }
        }

        public override void OnCreateRoomFailed(short returnCode, string message)
        {
            Debug.LogErrorFormat("Launcher: CreateRoomFailed {0} {1}", returnCode, message);
        }

        public override void OnJoinedRoom()
        {
            Debug.LogFormat("Launcher: OnJoinedRoom. Room: {0}", PhotonNetwork.CurrentRoom.Name);
            if (PhotonNetwork.IsMasterClient)
            {
                string sceneToLoad = roomSceneName;
                if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("scene"))
                {
                    object obj = PhotonNetwork.CurrentRoom.CustomProperties["scene"];
                    if (obj is string s && !string.IsNullOrEmpty(s)) sceneToLoad = s;
                }
                PhotonNetwork.LoadLevel(sceneToLoad);
            }
        }

        public override void OnJoinRoomFailed(short returnCode, string message)
        {
            Debug.LogWarningFormat("Launcher: OnJoinRoomFailed {0} {1}", returnCode, message);
        }

        public override void OnLeftLobby()
        {
            foreach (var kv in roomListEntries) Destroy(kv.Value);
            roomListEntries.Clear();
        }
        #endregion

        // Helper to directly show lobby UI (safe)
        private void ShowLobbyUIImmediate()
        {
            if (progressLabel != null) progressLabel.SetActive(false);
            if (controlPanel != null) controlPanel.SetActive(false);
            if (lobbyPanel != null) lobbyPanel.SetActive(true);
        }
    }
}
