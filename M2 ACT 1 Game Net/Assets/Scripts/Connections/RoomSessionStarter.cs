// CompactRoomSessionStarter.cs (with TMP UI)
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
    [SerializeField] Button startSessionButton;

    [Header("UI (assign a TextMeshProUGUI to display countdown seconds)")]
    [SerializeField] TextMeshProUGUI countdownText;
    [SerializeField] float uiUpdateInterval = 0.25f; // how often the UI polls remaining time

    const string K_START = "session_countdown_start";
    const string K_DUR = "session_countdown_duration";

    Coroutine masterWatcher;
    Coroutine uiUpdater;

    void Start()
    {
        if (startSessionButton) startSessionButton.onClick.AddListener(OnStartClicked);
        if (startSessionButton) startSessionButton.interactable = PhotonNetwork.IsMasterClient;

        if (PhotonNetwork.InRoom)
            EvaluateAndSyncCountdown();

        // start UI updater (clients + master) if a TMP field is assigned
        if (countdownText != null)
            uiUpdater = StartCoroutine(CountdownUIUpdater());
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)  { if (PhotonNetwork.IsMasterClient) EvaluateAndSyncCountdown(); }
    public override void OnPlayerLeftRoom(Player otherPlayer)    { if (PhotonNetwork.IsMasterClient) EvaluateAndSyncCountdown(); }
    public override void OnMasterClientSwitched(Player newMaster) { if (PhotonNetwork.IsMasterClient) EvaluateAndSyncCountdown(); }

    void OnDestroy()
    {
        if (startSessionButton) startSessionButton.onClick.RemoveListener(OnStartClicked);
        if (uiUpdater != null) StopCoroutine(uiUpdater);
    }

    void OnStartClicked()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        PhotonNetwork.CurrentRoom?.SetCustomProperties(new Hashtable { { K_START, 0.0 }, { K_DUR, 0f } });
        PhotonNetwork.LoadLevel(sessionSceneName);
    }

    void EvaluateAndSyncCountdown()
    {
        if (!PhotonNetwork.InRoom) return;
        int p = PhotonNetwork.CurrentRoom.PlayerCount;
        float desired = (p >= 4) ? 5f : (p == 3 ? 15f : (p == 2 ? 30f : 0f));
        if (p < 2) { ClearCountdown(); return; }

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
        UpdateCountdownText(0f); // clear UI immediately
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        base.OnRoomPropertiesUpdate(propertiesThatChanged);
        // Update UI immediately when props change
        float rem = GetRemainingSecondsFromRoom();
        UpdateCountdownText(rem);
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
        if (countdownText == null) return;

        if (secondsLeft <= 0f)
        {
            countdownText.text = "";
            return;
        }

        // show whole seconds (ceil so UI shows "1" until it hits 0)
        int secs = Mathf.CeilToInt(secondsLeft);
        countdownText.text = secs.ToString();
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
}
