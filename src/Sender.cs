using System.Net.Sockets;
using System.Security.Cryptography;

namespace SendClipboard;

/// <summary>送信先。Key はその相手とのペアリングで作った共有キー。Label はログ・通知用の表示名。</summary>
internal readonly record struct SendTarget(string Id, IReadOnlyList<System.Net.IPAddress> Addresses, int Port, byte[] Key, string Label);

/// <summary>クリップボード内容を受信側PCへ送る。新しい内容が来たら古い送信待ちは捨てる。</summary>
internal sealed class Sender : IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(60);

    private readonly AppConfig config;
    private readonly Func<IReadOnlyList<SendTarget>> getTargets;
    private readonly SemaphoreSlim signal = new(0, 1);
    private readonly CancellationTokenSource cts = new();
    private readonly object pendingLock = new();
    private ClipboardPayload? pending;
    private volatile byte[]? lastHash;

    // 「送信先がありません」を通知済みか。送信先が見つかるまで、コピーのたびに同じ警告を出さない
    private bool noTargetReported;

    // 相手ごとに通知済みの失敗理由。同じ相手・同じ理由の失敗は、成功するまで再通知しない (送信ループ内だけで使う)
    private readonly Dictionary<string, string> reportedFailures = new();

    // 相手ごとに最後に送信が成功した (= 正しい鍵で ACK が返った) アドレス。次回はこれを最初に試す。
    // 探索パケットのなりすましで偽のアドレスが一覧に混ざっても、本物への送信が遅れないようにする。
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Net.IPAddress> lastGood = new();

    public event Action<string, bool>? StatusChanged;

    public Sender(AppConfig config, Func<IReadOnlyList<SendTarget>> getTargets)
    {
        this.config = config;
        this.getTargets = getTargets;
        _ = Task.Run(LoopAsync);
    }

    /// <summary>相手から受け取った内容を送信済み扱いにして、送り返さないようにする。</summary>
    public void MarkAsKnown(byte[] serialized)
    {
        lastHash = SHA256.HashData(serialized);
    }

    public void Enqueue(ClipboardPayload payload)
    {
        lock (pendingLock)
        {
            pending = payload;
        }

        if (signal.CurrentCount == 0)
        {
            try
            {
                signal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    private async Task LoopAsync()
    {
        while (!cts.IsCancellationRequested)
        {
            try
            {
                await signal.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            ClipboardPayload? payload;
            lock (pendingLock)
            {
                (payload, pending) = (pending, null);
            }

            if (payload == null || payload.IsEmpty)
            {
                continue;
            }

            byte[] bytes = payload.Serialize();
            if (bytes.Length > config.MaxSizeMB * 1024 * 1024)
            {
                Report($"サイズ超過のため送信しません ({bytes.Length:N0} bytes)", false);
                continue;
            }

            byte[] hash = SHA256.HashData(bytes);
            if (lastHash != null && hash.AsSpan().SequenceEqual(lastHash))
            {
                continue;
            }

            IReadOnlyList<SendTarget> targets = getTargets();
            if (targets.Count == 0)
            {
                const string message = "送信先がありません (ペアリング済みで「受け取る」が有効なPCが同じLANに見つかりません)";
                if (noTargetReported)
                {
                    Log.Write(message);
                }
                else
                {
                    noTargetReported = true;
                    Report(message, false);
                }

                continue;
            }

            noTargetReported = false;

            var results = await Task.WhenAll(targets.Select(async target =>
            {
                try
                {
                    await SendAsync(target, bytes);
                    return (target, error: (string?)null);
                }
                catch (Exception e) when (e is not OperationCanceledException || !cts.IsCancellationRequested)
                {
                    return (target, error: e.Message);
                }
            }));

            if (cts.IsCancellationRequested)
            {
                return;
            }

            foreach (var (target, _) in results.Where(r => r.error == null))
            {
                reportedFailures.Remove(target.Id);
            }

            var succeeded = results.Where(r => r.error == null).Select(r => r.target.Label).ToList();
            if (succeeded.Count > 0)
            {
                lastHash = hash;
                Report($"送信しました → {string.Join(", ", succeeded)}: {payload.Describe()}", true);
            }

            foreach (var (target, error) in results.Where(r => r.error != null))
            {
                string message = $"送信失敗 ({target.Label}): {error}";
                if (reportedFailures.TryGetValue(target.Id, out string? last) && last == error)
                {
                    Log.Write(message);
                }
                else
                {
                    reportedFailures[target.Id] = error!;
                    Report(message, false);
                }
            }
        }
    }

    private async Task SendAsync(SendTarget target, byte[] bytes)
    {
        // 複数NICのPCはつながるアドレスが見つかるまで順に試す
        var addresses = target.Addresses.ToList();
        if (lastGood.TryGetValue(target.Id, out var good) && addresses.Remove(good))
        {
            addresses.Insert(0, good);
        }

        Exception? lastError = null;
        foreach (System.Net.IPAddress address in addresses)
        {
            try
            {
                await SendToAsync(address, target, bytes);
                lastGood[target.Id] = address;
                return;
            }
            catch (Exception e) when (e is not RejectedException && !cts.IsCancellationRequested)
            {
                // 接続できない・正しい ACK が返らない (なりすましの可能性) ときは次のアドレスへ
                lastError = e;
            }
        }

        throw lastError ?? new IOException("接続先アドレスがありません");
    }

    /// <summary>1つのアドレスへ送る。ACK まで確認できたら成功。</summary>
    private async Task SendToAsync(System.Net.IPAddress address, SendTarget target, byte[] bytes)
    {
        using var client = new TcpClient(address.AddressFamily) { NoDelay = true };
        using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
        {
            connectCts.CancelAfter(ConnectTimeout);
            await client.ConnectAsync(address, target.Port, connectCts.Token);
        }

        using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        sendCts.CancelAfter(SendTimeout);
        await using NetworkStream stream = client.GetStream();
        await Protocol.WriteAsync(stream, config.DeviceId, target.Key, bytes, sendCts.Token);

        // 受信側の処理完了 (1バイトACK) を待つ
        byte[] ack = new byte[1];
        await stream.ReadExactlyAsync(ack, sendCts.Token);
        if (ack[0] != 1)
        {
            throw new RejectedException();
        }
    }

    private void Report(string message, bool success)
    {
        Log.Write(message);
        StatusChanged?.Invoke(message, success);
    }

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}

/// <summary>本物の相手が「受け取る」無効などで受信を断った。別アドレスへ送り直さない。</summary>
internal sealed class RejectedException() : IOException("受信側で拒否されました");
