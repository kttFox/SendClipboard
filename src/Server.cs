using System.Net;
using System.Net.Sockets;

namespace SendClipboard;

/// <summary>
/// TCP の待ち受け。ペアリング要求は常に受け付け、クリップボードは「受け取る」が有効なときだけ、
/// ペア済みの相手からのものだけを受け付ける。同じローカルネットワーク以外からの接続は拒否する。
/// 認証前の失敗 (不明な相手・復号できない・形式不正・ペアリング失敗など) は LAN 内の誰でも起こせるので、
/// 通知は出さずログにだけ残す。通知するのは正規の相手と確認できた後の失敗だけ。
/// </summary>
internal sealed class Server : IDisposable
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PairingCooldown = TimeSpan.FromSeconds(30);

    /// <summary>同時に処理する接続数の上限。受信バッファ (最大 MaxSizeMB) × この数がメモリ使用量の上限になる。</summary>
    private const int MaxConcurrentConnections = 4;

    private readonly AppConfig config;
    private readonly bool receiveEnabled;
    private readonly SynchronizationContext uiContext;
    private readonly Func<string, string, Task<bool>> askPairing;
    private readonly TcpListener listener;
    private readonly CancellationTokenSource cts = new();
    private readonly SemaphoreSlim connectionSlots = new(MaxConcurrentConnections, MaxConcurrentConnections);

    // 処理中の接続の送信元IP。1台が同時に張れる接続は1本だけにして、接続枠を独占させない
    private readonly object activeLock = new();
    private readonly HashSet<IPAddress> activeAddresses = new();

    // ペアリングは同時に1件だけ。拒否・失敗した相手はしばらく受け付けない (確認ダイアログの連打対策)
    private readonly SemaphoreSlim pairingSlot = new(1, 1);
    private readonly object cooldownLock = new();
    private readonly Dictionary<IPAddress, DateTime> pairingCooldown = new();

    public event Action<string, bool>? StatusChanged;

    /// <summary>true の間は受信内容を破棄する。</summary>
    public bool Paused { get; set; }

    /// <summary>クリップボードに反映した直後に UI スレッドで呼ばれる。引数は受信したバイト列。</summary>
    public event Action<byte[]>? Applied;

    /// <summary>ペアリングが成立したときに呼ばれる。スレッドは不定。</summary>
    public event Action<PairedDevice>? Paired;

    /// <param name="askPairing">(相手名, 確認コード) を受けて、ユーザーが許可したら true を返す。</param>
    public Server(AppConfig config, bool receiveEnabled, SynchronizationContext uiContext, Func<string, string, Task<bool>> askPairing)
    {
        this.config = config;
        this.receiveEnabled = receiveEnabled;
        this.uiContext = uiContext;
        this.askPairing = askPairing;
        listener = new TcpListener(IPAddress.Any, config.Port);
        listener.Start();
        Log.Write($"待ち受け開始: TCP {config.Port} (受け取る={receiveEnabled})");
        _ = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cts.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (Exception e)
            {
                Log.Write($"Accept失敗: {e.Message}");
                continue;
            }

            IPAddress? address = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
            if (address?.IsIPv4MappedToIPv6 == true)
            {
                address = address.MapToIPv4();
            }

            if (address == null || !TryEnter(address))
            {
                Log.Write($"同じ相手からの接続が処理中のため切断: {address}");
                client.Dispose();
                continue;
            }

            if (!connectionSlots.Wait(0))
            {
                Log.Write($"同時接続数の上限を超えたため切断: {address}");
                Leave(address);
                client.Dispose();
                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleAsync(client);
                }
                finally
                {
                    connectionSlots.Release();
                    Leave(address);
                }
            });
        }
    }

    private bool TryEnter(IPAddress address)
    {
        lock (activeLock)
        {
            return activeAddresses.Add(address);
        }
    }

    private void Leave(IPAddress address)
    {
        lock (activeLock)
        {
            activeAddresses.Remove(address);
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            var remote = (IPEndPoint?)client.Client.RemoteEndPoint;
            try
            {
                if (remote == null || !LocalNetwork.IsLocal(remote.Address))
                {
                    Log.Write($"LAN外からの接続を拒否: {remote}");
                    return;
                }

                await using NetworkStream stream = client.GetStream();
                byte[] kind = new byte[1];
                using (var first = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
                {
                    first.CancelAfter(TimeSpan.FromSeconds(5));
                    await stream.ReadExactlyAsync(kind, first.Token);
                }

                switch ((ConnectionKind)kind[0])
                {
                    case ConnectionKind.Pairing:
                        await HandlePairingAsync(stream, remote);
                        break;
                    case ConnectionKind.Clipboard:
                        await HandleClipboardAsync(stream, remote);
                        break;
                    default:
                        Log.Write($"不明な接続種別 {kind[0]} ({remote})");
                        break;
                }
            }
            catch (Exception e) when (!cts.IsCancellationRequested)
            {
                Report($"受信失敗 ({remote?.Address}): {e.Message}", false);
            }
        }
    }

    private async Task HandlePairingAsync(NetworkStream stream, IPEndPoint remote)
    {
        IPAddress address = remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address;
        lock (cooldownLock)
        {
            if (pairingCooldown.TryGetValue(address, out DateTime until) && DateTime.UtcNow < until)
            {
                Log.Write($"ペアリング要求を無視 (拒否から間もない): {address}");
                return;
            }
        }

        if (!pairingSlot.Wait(0))
        {
            Log.Write($"ペアリング要求を無視 (別のペアリング中): {address}");
            return;
        }

        PairedDevice? device;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            timeout.CancelAfter(Pairing.UserTimeout + TimeSpan.FromSeconds(10));
            device = await Pairing.RespondAsync(config, stream, askPairing, timeout.Token);
        }
        catch (Exception e) when (!cts.IsCancellationRequested)
        {
            StartCooldown(address);
            Log.Write($"ペアリング失敗 ({address}): {e.Message}");
            return;
        }
        finally
        {
            pairingSlot.Release();
        }

        if (device == null)
        {
            StartCooldown(address);
            Log.Write($"ペアリングを拒否: {address}");
            return;
        }

        config.AddPair(device);
        Report($"ペアリングしました: {device.Name}", true);
        Paired?.Invoke(device);
    }

    private void StartCooldown(IPAddress address)
    {
        lock (cooldownLock)
        {
            DateTime now = DateTime.UtcNow;
            foreach (var old in pairingCooldown.Where(p => p.Value < now).Select(p => p.Key).ToList())
            {
                pairingCooldown.Remove(old);
            }

            pairingCooldown[address] = now + PairingCooldown;
        }
    }

    private async Task HandleClipboardAsync(NetworkStream stream, IPEndPoint remote)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        timeout.CancelAfter(ReceiveTimeout);

        bool authenticated = false;
        string senderId;
        byte[] bytes;
        try
        {
            (senderId, bytes) = await Protocol.ReadAsync(
                stream,
                id => config.FindPair(id)?.KeyBytes,
                config.MaxSizeMB * 1024 * 1024,
                () => authenticated = true,
                timeout.Token);
        }
        catch (Exception e) when (!authenticated && !cts.IsCancellationRequested)
        {
            Log.Write($"受信を拒否 (認証前, {remote.Address}): {e.Message}");
            return;
        }

        string name = config.FindPair(senderId)?.Name ?? senderId;
        if (!receiveEnabled)
        {
            await stream.WriteAsync(new byte[] { 0 }, timeout.Token);
            Log.Write($"「受け取る」が無効なため破棄: {name}");
            return;
        }

        if (Paused)
        {
            await stream.WriteAsync(new byte[] { 0 }, timeout.Token);
            Log.Write($"一時停止中のため破棄: {name}");
            return;
        }

        ClipboardPayload payload = ClipboardPayload.Deserialize(bytes);

        var done = new TaskCompletionSource();
        uiContext.Post(
            async _ =>
            {
                try
                {
                    await payload.ApplyAsync();
                    Applied?.Invoke(bytes);
                    done.SetResult();
                }
                catch (Exception e)
                {
                    done.SetException(e);
                }
            },
            null);
        await done.Task;

        await stream.WriteAsync(new byte[] { 1 }, timeout.Token);
        Report($"受信しました ← {name}: {payload.Describe()}", true);
    }

    private void Report(string message, bool success)
    {
        Log.Write(message);
        StatusChanged?.Invoke(message, success);
    }

    public void Dispose()
    {
        cts.Cancel();
        listener.Stop();
    }
}
