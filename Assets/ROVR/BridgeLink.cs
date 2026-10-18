// Assets/ROVR/BridgeLink.cs
// Finds the ROVR bridge on the PC (Bridge/rovr_bridge.py), over the USB cable or over Wi-Fi, and keeps
// track of it. Url is the bridge in use; the panel sends the LLM requests, the speech and the transcript there.
//
// The cable first: the panel's Bridge Url (127.0.0.1:8765, which adb reverse leads to the PC; in the
// Editor it is this PC). If nothing answers there, it asks the Wi-Fi network: a "ROVR-BRIDGE?" broadcast
// that a bridge started with --wifi answers, so the PC's address never has to be put into a build.
// Every few seconds it checks that the bridge still answers. It goes back to the cable as soon as the
// cable answers, and searches again when the cable stops answering, or after two missed checks over Wi-Fi
// (PC asleep, Wi-Fi dropped, bridge restarted).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace ROVR
{
    public class BridgeLink : MonoBehaviour
    {
        public const int Port = 8765;          // the bridge's TCP port, and the UDP port it answers searches on
        const string Question = "ROVR-BRIDGE?"; // rovr_bridge.py's DISCOVERY_QUESTION
        const float CheckEverySec = 3f;
        const float SearchSec = 1f;            // how long one search waits for answers
        const int MissesToLose = 2;

        string _cableUrl;
        bool _searchWifi;
        int _misses;

        // The bridge in use ("http://192.168.68.113:8765"), or null while none answers.
        public string Url { get; private set; }
        public bool OverWifi { get; private set; }
        public string PcName { get; private set; } // the PC's name, as the bridge gives it

        // Url changed (null = the bridge was lost).
        public event Action<string> OnChanged;

        // cableUrl: where the bridge is over the cable. searchWifi: also look on the network. Starts afresh.
        public void Begin(string cableUrl, bool searchWifi)
        {
            _cableUrl = cableUrl.TrimEnd('/');
            _searchWifi = searchWifi;
            Url = PcName = null;
            OverWifi = false;
            _misses = 0;
            StopAllCoroutines();
            StartCoroutine(Keep());
        }

        IEnumerator Keep()
        {
            while (true)
            {
                string name = null;
                yield return Ping(_cableUrl, n => name = n);
                if (name != null) Use(_cableUrl, false, name);
                else if (Url == _cableUrl) Use(null, false, null); // the cable stopped answering: unplugged
                else if (Url != null)
                {
                    string still = null;
                    yield return Ping(Url, n => still = n);
                    if (still != null) _misses = 0;
                    else if (++_misses >= MissesToLose) Use(null, false, null); // Wi-Fi can drop one; not two
                }

                if (Url == null && _searchWifi)
                {
                    var search = SearchWifi();
                    yield return new WaitUntil(() => search.IsCompleted);
                    string found = search.Result;
                    if (found != null)
                    {
                        yield return Ping(found, n => name = n); // a bridge that answers HTTP too
                        if (name != null) Use(found, true, name);
                    }
                }
                yield return new WaitForSecondsRealtime(CheckEverySec);
            }
        }

        void Use(string url, bool wifi, string pcName)
        {
            _misses = 0;
            if (url == Url) return;
            Url = url;
            OverWifi = url != null && wifi;
            PcName = url != null ? pcName : null;
            OnChanged?.Invoke(url);
        }

        // GET /ping: "rovr-bridge <PC name>". Hands back the PC's name, or null if no bridge answered.
        static IEnumerator Ping(string url, Action<string> answer)
        {
            using (var request = UnityWebRequest.Get(url + "/ping"))
            {
                request.timeout = 2;
                yield return request.SendWebRequest();
                string text = request.result == UnityWebRequest.Result.Success ? request.downloadHandler.text : "";
                answer(text.StartsWith("rovr-bridge") ? text.Substring("rovr-bridge".Length).Trim() : null);
            }
        }

        [Serializable]
        class Answer
        {
            public int rovr_bridge;
            public int port;
        }

        // One "ROVR-BRIDGE?" broadcast; the address of the first bridge that answers within SearchSec, or
        // null. On a worker thread, so a frame never waits for the network.
        static Task<string> SearchWifi() => Task.Run(() =>
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.EnableBroadcast = true;
                    socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                    byte[] question = Encoding.ASCII.GetBytes(Question);
                    foreach (var to in BroadcastAddresses())
                    {
                        try { socket.SendTo(question, new IPEndPoint(to, Port)); }
                        catch (SocketException) { } // that network is down
                    }

                    var buffer = new byte[512];
                    DateTime until = DateTime.UtcNow.AddSeconds(SearchSec);
                    while (DateTime.UtcNow < until)
                    {
                        socket.ReceiveTimeout = Math.Max(1, (int)(until - DateTime.UtcNow).TotalMilliseconds);
                        EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                        int length = socket.ReceiveFrom(buffer, ref from);
                        Answer answer = null;
                        try { answer = JsonUtility.FromJson<Answer>(Encoding.UTF8.GetString(buffer, 0, length)); }
                        catch (ArgumentException) { } // not an answer from a bridge
                        if (answer != null && answer.rovr_bridge == 1 && answer.port > 0)
                            return "http://" + ((IPEndPoint)from).Address + ":" + answer.port;
                    }
                }
            }
            catch (SocketException) { } // nothing answered in time, or there is no network
            return null;
        });

        // 255.255.255.255, plus each network's own broadcast address where the system says what it is
        // (a PC with several networks only sends 255.255.255.255 out of one of them).
        static List<IPAddress> BroadcastAddresses()
        {
            var addresses = new List<IPAddress> { IPAddress.Broadcast };
            try
            {
                foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (network.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (var unicast in network.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null) continue;
                        byte[] ip = unicast.Address.GetAddressBytes(), mask = unicast.IPv4Mask.GetAddressBytes();
                        if (!IsLocalNetwork(ip) || mask.Length != 4) continue; // not over a VPN or the internet
                        var broadcast = new byte[4];
                        for (int i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | ~mask[i]);
                        var address = new IPAddress(broadcast);
                        if (!addresses.Contains(address)) addresses.Add(address);
                    }
                }
            }
            catch (Exception) { } // some Android versions don't let apps list networks: 255.255.255.255 then
            return addresses;
        }

        // 10.x, 172.16-31.x, 192.168.x: home, lab and hotspot networks.
        static bool IsLocalNetwork(byte[] ip)
        {
            return ip[0] == 10 || (ip[0] == 172 && ip[1] >= 16 && ip[1] <= 31) || (ip[0] == 192 && ip[1] == 168);
        }
    }
}
