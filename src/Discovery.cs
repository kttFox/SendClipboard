using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SendClipboard;

/// <summary>同じLANで起動している SendClipboard。複数NICのPCは Addresses に複数入る (先頭が最初に見えたもの)。</summary>
internal sealed record Peer(string Id, string Alias, IReadOnlyList<IPAddress> Addresses, int Port, bool CanSend, bool CanReceive, DateTime LastSeenUtc)
{
    public IPAddress Address => Addresses[0];
}

/// <summary>
/// LocalSend の探索方式を参考にした LAN 内探索。
/// - マルチキャスト 239.255.83.67 に JSON を送る。ポートは設定値。
///   LocalSend (224.0.0.167) とは別グループにして、互いのパケットが届かないようにする。
///   239.255.0.0/16 は組織内で自由に使える範囲。TTL=1 なので同じサブネットの外には出ない。
/// - announce=true を受け取ったPCは、送信元へ announce=false で応答する (起動直後にすぐ一覧が埋まる)。
/// - 生存確認のため定期的にも announce し、一定時間届かない相手は一覧から消す。
/// - マルチキャストが通らないネットワーク向けに、サブネットのブロードキャストにも送る。
/// 探索パケットは認証しない (一覧表示のみ)。転送はペアリングで作った共有キーでのみ行う。
/// </summary>
internal sealed class Discovery : IDisposable
{
    public static readonly IPAddress MulticastGroup = IPAddress.Parse("239.255.83.67");
    private const string ProtocolVersion = "sc-1";
    private static readonly TimeSpan AnnounceInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(16);
    private const int MaxPacketBytes = 2048;

    private readonly AppConfig config;
    private readonly bool canSend;
    private readonly bool canReceive;
    private readonly UdpClient udp;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly CancellationTokenSource cts = new();
    private readonly object peersLock = new();
    private readonly Dictionary<string, Peer> peers = new();

    // 相手ごと・アドレスごとの最終受信時刻。探索パケットは認証しないので、なりすましで追加された
    // アドレスは送信が続かなければ PeerTimeout で消えるよう、アドレス単位で期限を管理する。
    private readonly Dictionary<string, Dictionary<IPAddress, DateTime>> addressSeen = new();

    /// <summary>一覧が変わったとき (追加・消失・状態変化) に呼ばれる。スレッドは不定。</summary>
    public event Action? PeersChanged;

    /// <summary>canSend / canReceive には設定値ではなく、実際に起動できた機能を渡す。</summary>
    public Discovery(AppConfig config, bool canSend, bool canReceive)
    {
        this.config = config;
        this.canSend = canSend;
        this.canReceive = canReceive;

        udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, config.Port));
        udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
        udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);
        udp.Client.Ttl = 1;

        foreach (LocalNetwork.Subnet subnet in LocalNetwork.GetSubnets())
        {
            try
            {
                udp.JoinMulticastGroup(MulticastGroup, subnet.Address);
            }
            catch (SocketException e)
            {
                Log.Write($"マルチキャスト参加失敗 ({subnet.Address}): {e.Message}");
            }
        }

        _ = Task.Run(ReceiveLoopAsync);
        _ = Task.Run(AnnounceLoopAsync);
        Log.Write($"LAN探索開始: {MulticastGroup}:{config.Port}");
    }

    public IReadOnlyList<Peer> Peers
    {
        get
        {
            lock (peersLock)
            {
                return peers.Values.OrderBy(p => p.Alias, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
    }

    /// <summary>今すぐ announce する (一覧の更新ボタンなど)。</summary>
    public void Refresh() => _ = SendAsync(announce: true, unicastTo: null);

    private async Task AnnounceLoopAsync()
    {
        while (!cts.IsCancellationRequested)
        {
            await SendAsync(announce: true, unicastTo: null);
            ExpirePeers();
            try
            {
                await Task.Delay(AnnounceInterval, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task SendAsync(bool announce, IPEndPoint? unicastTo)
    {
        var message = new DeviceInfo
        {
            Alias = config.DisplayName,
            Version = ProtocolVersion,
            DeviceModel = "Windows",
            DeviceType = "desktop",
            Fingerprint = config.DeviceId,
            Port = config.Port,
            Announce = announce,
            Send = canSend,
            Receive = canReceive,
        };
        byte[] packet = JsonSerializer.SerializeToUtf8Bytes(message);

        try
        {
            await sendLock.WaitAsync(cts.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (unicastTo != null)
            {
                await udp.SendAsync(packet, unicastTo, cts.Token);
                return;
            }

            // NIC ごとにマルチキャスト + ブロードキャスト
            foreach (LocalNetwork.Subnet subnet in LocalNetwork.GetSubnets())
            {
                try
                {
                    udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, subnet.Address.GetAddressBytes());
                    await udp.SendAsync(packet, new IPEndPoint(MulticastGroup, config.Port), cts.Token);
                    await udp.SendAsync(packet, new IPEndPoint(subnet.Broadcast, config.Port), cts.Token);
                }
                catch (SocketException)
                {
                    // ネットワーク切断中など。次回に再試行する
                }
            }

            // 同じPCで複数起動したときの確認用
            await udp.SendAsync(packet, new IPEndPoint(IPAddress.Loopback, config.Port), cts.Token);
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync()
    {
        while (!cts.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(cts.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue; // ICMP port unreachable 等
            }

            try
            {
                HandlePacket(result.Buffer, result.RemoteEndPoint);
            }
            catch (Exception e)
            {
                Log.Write($"探索パケット処理失敗 ({result.RemoteEndPoint}): {e.Message}");
            }
        }
    }

    private void HandlePacket(byte[] packet, IPEndPoint from)
    {
        if (packet.Length > MaxPacketBytes || !LocalNetwork.IsLocal(from.Address))
        {
            return;
        }

        DeviceInfo? message;
        try
        {
            message = JsonSerializer.Deserialize<DeviceInfo>(packet);
        }
        catch (JsonException)
        {
            return; // 別アプリのパケット
        }

        if (message == null || message.Version != ProtocolVersion ||
            string.IsNullOrEmpty(message.Fingerprint) || message.Fingerprint.Length > 64 ||
            message.Fingerprint == config.DeviceId)
        {
            return;
        }

        IPAddress address = from.Address.IsIPv4MappedToIPv6 ? from.Address.MapToIPv4() : from.Address;
        string alias = message.Alias.Length > 64 ? message.Alias[..64] : message.Alias;

        Peer peer;
        bool changed;
        lock (peersLock)
        {
            peers.TryGetValue(message.Fingerprint, out Peer? old);
            if (!addressSeen.TryGetValue(message.Fingerprint, out var seen))
            {
                seen = new Dictionary<IPAddress, DateTime>();
                addressSeen[message.Fingerprint] = seen;
            }

            seen[address] = DateTime.UtcNow;

            // 既存の順序を保ち、新しいアドレスは後ろへ。loopback は同じPC内の確認用なので最後に回す
            var addresses = (old?.Addresses ?? Array.Empty<IPAddress>())
                .Where(seen.ContainsKey)
                .Append(address)
                .Distinct()
                .OrderBy(IPAddress.IsLoopback)
                .ToList();

            peer = new Peer(message.Fingerprint, alias, addresses, message.Port, message.Send, message.Receive, DateTime.UtcNow);
            changed = old == null || old.Alias != peer.Alias || old.Port != peer.Port ||
                      old.CanSend != peer.CanSend || old.CanReceive != peer.CanReceive;
            peers[peer.Id] = peer;
        }

        // LocalSend と同様、announce には応答して相手の一覧にもすぐ載るようにする
        if (message.Announce)
        {
            _ = SendAsync(announce: false, unicastTo: new IPEndPoint(address, message.Port));
        }

        if (changed)
        {
            Log.Write($"PCを検出: {peer.Alias} ({peer.Address}) 送る={peer.CanSend} 受け取る={peer.CanReceive}");
            PeersChanged?.Invoke();
        }
    }

    private void ExpirePeers()
    {
        List<Peer> removed;
        lock (peersLock)
        {
            DateTime now = DateTime.UtcNow;
            removed = peers.Values.Where(p => now - p.LastSeenUtc > PeerTimeout).ToList();
            foreach (Peer p in removed)
            {
                peers.Remove(p.Id);
                addressSeen.Remove(p.Id);
            }

            // 生きている相手でも、しばらく届いていないアドレスは外す
            foreach (Peer p in peers.Values.ToList())
            {
                var seen = addressSeen[p.Id];
                foreach (var stale in seen.Where(s => now - s.Value > PeerTimeout).Select(s => s.Key).ToList())
                {
                    seen.Remove(stale);
                }

                if (seen.Count < p.Addresses.Count)
                {
                    peers[p.Id] = p with { Addresses = p.Addresses.Where(seen.ContainsKey).ToList() };
                }
            }
        }

        foreach (Peer p in removed)
        {
            Log.Write($"PCが見えなくなりました: {p.Alias} ({p.Address})");
        }

        if (removed.Count > 0)
        {
            PeersChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        udp.Dispose();
    }

    /// <summary>LocalSend の DeviceInfo に合わせたフィールド名 + 本アプリ固有の send / receive。</summary>
    private sealed class DeviceInfo
    {
        [JsonPropertyName("alias")]
        public string Alias { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("deviceModel")]
        public string DeviceModel { get; set; } = "";

        [JsonPropertyName("deviceType")]
        public string DeviceType { get; set; } = "";

        [JsonPropertyName("fingerprint")]
        public string Fingerprint { get; set; } = "";

        [JsonPropertyName("port")]
        public int Port { get; set; }

        [JsonPropertyName("announce")]
        public bool Announce { get; set; }

        [JsonPropertyName("send")]
        public bool Send { get; set; }

        [JsonPropertyName("receive")]
        public bool Receive { get; set; }
    }
}
