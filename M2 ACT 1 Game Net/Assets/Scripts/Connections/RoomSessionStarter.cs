using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using Hashtable = ExitGames.Client.Photon.Hashtable;
using TMPro;

public class CompactRoomSessionStarter : MonoBehaviourPunCallbacks
{
    [SerializeField] string sessionSceneName = "SessionScene";

    [Header("Buttons")]
    [SerializeField] Button startSessionButton;
    [SerializeField] Button cancelCountdownButton;

    [Header("Countdown UI")]
    [SerializeField] GameObject countdownPanel;      
    [SerializeField] TextMeshProUGUI countdownText;  
    [SerializeField] float uiUpdateInterval = 0.25f; 

    [Header("Player List UI")]
    [SerializeField] Transform playerListContainer;     
    [SerializeField] GameObject playerNamePrefab;

    [SerializeField] Color hostColor = Color.yellow;     
    [SerializeField] Color normalColor = Color.white;    

    const string K_START = "session_countdown_start";
    const string K_DUR = "session_countdown_duration";

    Coroutine masterWatcher;
    Coroutine uiUpdater;

    void Start()
    {
        if (startSessionButton) startSessionButton.onClick.AddListener(OnStartClicked);
        if (cancelCountdownButton) cancelCountdownButton.onClick.AddListener(OnCancelClicked);

        if (startSessionButton) startSessionButton.interactable = PhotonNetwork.IsMasterClient;
        if (cancelCountdownButton) cancelCountdownButton.interactable = true; // everyone can cancel

        if (PhotonNetwork.InRoom)
        {
            EvaluateAndSyncCountdown();
            RefreshPlayerList();
        }

        if (uiUpdater != null) StopCoroutine(uiUpdater);
        uiUpdater = StartCoroutine(CountdownUIUpdater());

        if (countdownPanel) countdownPanel.SetActive(false);
    }

    void OnDestroy()
    {
        if (startSessionButton) startSessionButton.onClick.RemoveListener(OnStartClicked);
        if (cancelCountdownButton) cancelCountdownButton.onClick.RemoveListener(OnCancelClicked);
        if (uiUpdater != null) StopCoroutine(uiUpdater);
    }

    // ---------------------------
    // BUTTON LOGIC
    // ---------------------------
    void OnStartClicked()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        StartCountdownAsMaster(10f); // fixed 10s
    }

    public void OnCancelClicked()
    {
        if (!PhotonNetwork.InRoom) return;

        // Reset countdown for everyone
        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
        {
            { K_START, 0.0 },
            { K_DUR, 0f }
        });

        // stop host coroutine if needed
        if (PhotonNetwork.IsMasterClient && masterWatcher != null)
        {
            StopCoroutine(masterWatcher);
            masterWatcher = null;
        }

        // update local UI immediately
        UpdateCountdownText(0f);
    }

    void ClearCountdown()
    {
        if (masterWatcher != null)
        {
            StopCoroutine(masterWatcher);
            masterWatcher = null;
        }

        PhotonNetwork.CurrentRoom?.SetCustomProperties(new Hashtable
        {
            { K_START, 0.0 },
            { K_DUR, 0f }
        });

        UpdateCountdownText(0f);
    }

    // ---------------------------
    // COUNTDOWN HANDLING
    // ---------------------------
    void EvaluateAndSyncCountdown()
    {
        if (!PhotonNetwork.InRoom) return;

        int p = PhotonNetwork.CurrentRoom.PlayerCount;
        float desired = (p >= 4) ? 5f : (p == 3 ? 15f : (p == 2 ? 10f : 0f));

        if (p < 4) { ClearCountdown(); return; }

        var props = PhotonNetwork.CurrentRoom.CustomProperties;
        double start = props.ContainsKey(K_START) ? Convert.ToDouble(props[K_START]) : 0.0;
        float dur = props.ContainsKey(K_DUR) ? Convert.ToSingle(props[K_DUR]) : 0f;
        double now = PhotonNetwork.Time;
        double remaining = (dur > 0f) ? (start + dur - now) : -1.0;

        if (remaining <= 0.0)
            StartCountdownAsMaster(desired);
        else if (desired < remaining)
            StartCountdownAsMaster(desired); // shorten only
    }

    void StartCountdownAsMaster(float duration)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        double start = PhotonNetwork.Time;
        PhotonNetwork.CurrentRoom?.SetCustomProperties(new Hashtable { { K_START, start }, { K_DUR, duration } });

        if (masterWatcher != null) StopCoroutine(masterWatcher);
        masterWatcher = StartCoroutine(MasterWatchAndLoad());
    }

    IEnumerator MasterWatchAndLoad()
    {
        while (true)
        {
            if (!PhotonNetwork.InRoom) yield break;

            var props = PhotonNetwork.CurrentRoom.CustomProperties;
            double start = props.ContainsKey(K_START) ? Convert.ToDouble(props[K_START]) : 0.0;
            float dur = props.ContainsKey(K_DUR) ? Convert.ToSingle(props[K_DUR]) : 0f;
            double remaining = (dur > 0f) ? (start + dur - PhotonNetwork.Time) : -1.0;

            if (remaining <= 0.0 && dur > 0f)
            {
                ClearCountdown();
                PhotonNetwork.LoadLevel(sessionSceneName);
                yield break;
            }

            yield return new WaitForSeconds(0.5f);
        }
    }

    // ---------------------------
    // UI UPDATES & PHOTON CALLBACKS
    // ---------------------------
    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();
        RefreshCountdownFromRoom();
        RefreshPlayerList();
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        base.OnRoomPropertiesUpdate(propertiesThatChanged);
        RefreshCountdownFromRoom();
        RefreshPlayerList();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer) => RefreshPlayerList();
    public override void OnPlayerLeftRoom(Player otherPlayer) => RefreshPlayerList();
    public override void OnMasterClientSwitched(Player newMaster) => RefreshPlayerList();

    void RefreshCountdownFromRoom()
    {
        if (!PhotonNetwork.InRoom || countdownPanel == null) return;

        var props = PhotonNetwork.CurrentRoom.CustomProperties;
        double start = props.ContainsKey(K_START) ? Convert.ToDouble(props[K_START]) : 0.0;
        float dur = props.ContainsKey(K_DUR) ? Convert.ToSingle(props[K_DUR]) : 0f;

        if (start > 0 && dur > 0)
        {
            countdownPanel.SetActive(true);
            float remaining = Mathf.Max(0f, (float)(start + dur - PhotonNetwork.Time));
            UpdateCountdownText(remaining);
        }
        else
        {
            countdownPanel.SetActive(false);
            UpdateCountdownText(0f);
        }
    }

    IEnumerator CountdownUIUpdater()
    {
        while (true)
        {
            float remaining = PhotonNetwork.InRoom ? GetRemainingSecondsFromRoom() : 0f;
            UpdateCountdownText(remaining);
            yield return new WaitForSeconds(uiUpdateInterval);
        }
    }

    void UpdateCountdownText(float secondsLeft)
    {
        if (countdownText == null || countdownPanel == null) return;

        if (secondsLeft <= 0f)
        {
            countdownText.text = "";
            countdownPanel.SetActive(false);
            return;
        }

        int secs = Mathf.CeilToInt(secondsLeft);
        countdownText.text = secs.ToString();

        if (!countdownPanel.activeSelf)
            countdownPanel.SetActive(true);
    }

    public static float GetRemainingSecondsFromRoom()
    {
        if (!PhotonNetwork.InRoom) return 0f;

        var props = PhotonNetwork.CurrentRoom.CustomProperties;
        double start = props.ContainsKey(K_START) ? Convert.ToDouble(props[K_START]) : 0.0;
        float dur = props.ContainsKey(K_DUR) ? Convert.ToSingle(props[K_DUR]) : 0f;
        double remaining = (dur > 0f) ? (start + dur - PhotonNetwork.Time) : 0.0;
        return Mathf.Max(0f, (float)remaining);
    }

    // ---------------------------
    // PLAYER LIST UI
    // ---------------------------
    void RefreshPlayerList()
    {
        if (playerListContainer == null || playerNamePrefab == null || PhotonNetwork.CurrentRoom == null) return;

        foreach (Transform child in playerListContainer)
            Destroy(child.gameObject);

        foreach (var kvp in PhotonNetwork.CurrentRoom.Players)
        {
            Player p = kvp.Value;
            GameObject entryObj = Instantiate(playerNamePrefab, playerListContainer);
            Text entryText = entryObj.GetComponentInChildren<Text>();
            entryText.text = p.NickName;
            entryText.color = (p.ActorNumber == PhotonNetwork.MasterClient.ActorNumber) ? hostColor : normalColor;
        }
    }
}
