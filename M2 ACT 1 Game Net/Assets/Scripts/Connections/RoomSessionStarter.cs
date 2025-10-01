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
    [SerializeField] GameObject countdownPanel;      // panel container (holds text + cancel)
    [SerializeField] TextMeshProUGUI countdownText;  // countdown number text
    [SerializeField] float uiUpdateInterval = 0.25f; // how often UI polls remaining time

    [Header("Player List UI")]
    [SerializeField] Transform playerListContainer;      // parent object (with VerticalLayoutGroup)
    [SerializeField] GameObject playerNamePrefab;

    [SerializeField] Color hostColor = Color.yellow;     // highlight color for host
    [SerializeField] Color normalColor = Color.white;    // normal player color

    const string K_START = "session_countdown_start";
    const string K_DUR = "session_countdown_duration";

    Coroutine masterWatcher;
    Coroutine uiUpdater;

    void Start()
    {
        if (startSessionButton) startSessionButton.onClick.AddListener(OnStartClicked);
        if (cancelCountdownButton) cancelCountdownButton.onClick.AddListener(OnCancelClicked);

        if (startSessionButton) startSessionButton.interactable = PhotonNetwork.IsMasterClient;
        if (cancelCountdownButton) cancelCountdownButton.interactable = PhotonNetwork.IsMasterClient;

        if (PhotonNetwork.InRoom)
        {
            EvaluateAndSyncCountdown();
            RefreshPlayerList();
        }

        if (uiUpdater != null) StopCoroutine(uiUpdater);
        uiUpdater = StartCoroutine(CountdownUIUpdater());

        if (countdownPanel) countdownPanel.SetActive(false);
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (PhotonNetwork.IsMasterClient) EvaluateAndSyncCountdown();
        RefreshPlayerList();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (PhotonNetwork.IsMasterClient) EvaluateAndSyncCountdown();
        RefreshPlayerList();
    }

    public override void OnMasterClientSwitched(Player newMaster)
    {
        if (PhotonNetwork.IsMasterClient) EvaluateAndSyncCountdown();
        RefreshPlayerList();
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
    if (PhotonNetwork.IsMasterClient)
    {
        ClearCountdown(); // host cancels directly
    }
    else
    {
        // ask the host to cancel
        photonView.RPC(nameof(RequestCancel), RpcTarget.MasterClient);
    }
}

[PunRPC]
void RequestCancel()
{
    ClearCountdown();
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
                PhotonNetwork.CurrentRoom?.SetCustomProperties(new Hashtable { { K_START, 0.0 }, { K_DUR, 0f } });
                PhotonNetwork.LoadLevel(sessionSceneName);
                yield break;
            }

            yield return new WaitForSeconds(0.5f);
        }
    }

   void ClearCountdown()
{
    PhotonNetwork.CurrentRoom?.SetCustomProperties(new Hashtable { { K_START, 0.0 }, { K_DUR, 0f } });
    if (masterWatcher != null) { StopCoroutine(masterWatcher); masterWatcher = null; }
    UpdateCountdownText(0f);
}


    // ---------------------------
    // UI UPDATES
    // ---------------------------

public override void OnJoinedRoom()
{
    base.OnJoinedRoom();
    RefreshCountdownFromRoom();
    RefreshPlayerList();   // 👈 make sure new joiners build their list
}

public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
{
    base.OnRoomPropertiesUpdate(propertiesThatChanged);
    RefreshCountdownFromRoom();
    RefreshPlayerList();   // 👈 keep both in sync when host changes
}


    void RefreshCountdownFromRoom()
    {
        if (!PhotonNetwork.InRoom || countdownPanel == null) return;

        var props = PhotonNetwork.CurrentRoom.CustomProperties;
        double start = props.ContainsKey(K_START) ? Convert.ToDouble(props[K_START]) : 0.0;
        float dur = props.ContainsKey(K_DUR) ? Convert.ToSingle(props[K_DUR]) : 0f;

        if (start > 0 && dur > 0)
        {
            countdownPanel.SetActive(true);  // show to everyone when countdown exists
            float remaining = Mathf.Max(0f, (float)(start + dur - PhotonNetwork.Time));
            UpdateCountdownText(remaining);
        }
        else
        {
            countdownPanel.SetActive(false); // hide otherwise
            UpdateCountdownText(0f);
        }
    Debug.Log($"[CountdownPanel] start={start}, dur={dur}, active={countdownPanel.activeSelf}");

}



    IEnumerator CountdownUIUpdater()
    {
        while (true)
        {
            if (!PhotonNetwork.InRoom)
            {
                UpdateCountdownText(0f);
            }
            else
            {
                float remaining = GetRemainingSecondsFromRoom();
                UpdateCountdownText(remaining);
            }

            yield return new WaitForSeconds(uiUpdateInterval);
        }
    }

    void UpdateCountdownText(float secondsLeft)
    {
        if (countdownText == null || countdownPanel == null) return;

        if (secondsLeft <= 0f)
        {
            countdownText.text = "";
            countdownPanel.SetActive(false); // hide panel when no countdown
            return;
        }

        int secs = Mathf.CeilToInt(secondsLeft);
        countdownText.text = secs.ToString();

        if (!countdownPanel.activeSelf)
            countdownPanel.SetActive(true); // show panel if hidden
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
    if (playerListContainer == null || playerNamePrefab == null) return;

    // Clear old list
    foreach (Transform child in playerListContainer)
        Destroy(child.gameObject);

    foreach (var kvp in PhotonNetwork.CurrentRoom.Players)
    {
        Player p = kvp.Value;

        // Create entry object
        GameObject entryObj = Instantiate(playerNamePrefab, playerListContainer);

        // Find the Text component inside
        Text entryText = entryObj.GetComponentInChildren<Text>(); // <- using UnityEngine.UI

        entryText.text = p.NickName;

        if (p == PhotonNetwork.MasterClient)
            entryText.color = hostColor;
        else
            entryText.color = normalColor;
    }
}

}
