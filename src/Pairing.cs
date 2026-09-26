using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace SendClipboard;

/// <summary>
/// ペアリング (LocalSend の PIN 確認を参考)。
///   1. 要求側 → 相手: 'P' + {Hash = SHA-256(要求側の Hello)}  (コミットメント)
///   2. 相手 → 要求側: {Id, Name, 公開鍵}
///   3. 要求側 → 相手: {Id, Name, 公開鍵}  相手は 1 のハッシュと一致するか検証する
///   4. 両者が ECDH(P-256) で同じ共有キーと6桁の確認コードを計算し、画面に表示
///   5. 相手のユーザーが確認コードを見て「許可」→ {Accepted: true}
///   6. 両者が共有キーを保存
/// 共有キーは通信路に流れない。確認コードが一致していれば中間者がいないことを確認できる。
/// コミットメントにより、中間者は要求側の公開鍵を知る前に自分の鍵を決めなければならず、
/// 確認コードが一致する鍵を総当たりで探すことができない (成功確率は 1/1,000,000)。
/// </summary>
internal static class Pairing
{
    public static readonly TimeSpan UserTimeout = TimeSpan.FromSeconds(60);
    private const int MaxMessageBytes = 4096;

    public sealed class Hello
    {
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public string PublicKey { get; set; } = "";
    }

    public sealed class Commit
    {
        public string Hash { get; set; } = "";
    }

    public sealed class Answer
    {
        public bool Accepted { get; set; }
    }

    /// <summary>要求側。onCode は確認コードが決まったときに呼ばれる。成功時は相手の PairedDevice を返す。</summary>
    public static async Task<PairedDevice> RequestAsync(
        AppConfig config, Peer peer, Action<string> onCode, CancellationToken ct)
    {
        using var client = new TcpClient(peer.Address.AddressFamily);
        using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connectCts.CancelAfter(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(peer.Address, peer.Port, connectCts.Token);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(UserTimeout + TimeSpan.FromSeconds(10));
        await using NetworkStream stream = client.GetStream();
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        byte[] myHello = JsonSerializer.SerializeToUtf8Bytes(MakeHello(config, ecdh));
        await stream.WriteAsync(new[] { (byte)ConnectionKind.Pairing }, timeout.Token);
        await WriteJsonAsync(stream, new Commit { Hash = Convert.ToBase64String(SHA256.HashData(myHello)) }, timeout.Token);

        Hello remote = await ReadJsonAsync<Hello>(stream, timeout.Token);
        if (remote.Id != peer.Id)
        {
            throw new InvalidDataException("相手のIDが一覧と一致しません");
        }

        await WriteBytesAsync(stream, myHello, timeout.Token);

        var (key, code) = Derive(ecdh, remote);
        onCode(code);

        Answer answer = await ReadJsonAsync<Answer>(stream, timeout.Token);
        if (!answer.Accepted)
        {
            throw new OperationCanceledException("相手に拒否されました");
        }

        return new PairedDevice { Id = remote.Id, Name = remote.Name, Key = Convert.ToBase64String(key), PairedAt = DateTime.Now };
    }

    /// <summary>
    /// 受け側。種別バイトは読み取り済み。askUser(相手名, 確認コード) で許可を求める。
    /// 許可されたら PairedDevice、拒否なら null を返す。
    /// </summary>
    public static async Task<PairedDevice?> RespondAsync(
        AppConfig config, Stream stream, Func<string, string, Task<bool>> askUser, CancellationToken ct)
    {
        Commit commit = await ReadJsonAsync<Commit>(stream, ct);

        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        await WriteJsonAsync(stream, MakeHello(config, ecdh), ct);

        byte[] remoteHello = await ReadBytesAsync(stream, ct);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(remoteHello), Convert.FromBase64String(commit.Hash)))
        {
            throw new InvalidDataException("コミットメントが一致しません (中間者の可能性)");
        }

        Hello remote = JsonSerializer.Deserialize<Hello>(remoteHello) ?? throw new InvalidDataException("ペアリングメッセージが不正");
        if (string.IsNullOrEmpty(remote.Id) || remote.Id.Length > 64 || remote.Id == config.DeviceId)
        {
            throw new InvalidDataException("不正なペアリング要求");
        }
        var (key, code) = Derive(ecdh, remote);

        Task<bool> ask = askUser(remote.Name, code);
        bool accepted = await Task.WhenAny(ask, Task.Delay(UserTimeout, ct)) == ask && ask.Result;

        await WriteJsonAsync(stream, new Answer { Accepted = accepted }, ct);
        return accepted
            ? new PairedDevice { Id = remote.Id, Name = remote.Name, Key = Convert.ToBase64String(key), PairedAt = DateTime.Now }
            : null;
    }

    private static Hello MakeHello(AppConfig config, ECDiffieHellman ecdh) => new()
    {
        Id = config.DeviceId,
        Name = config.DisplayName,
        PublicKey = Convert.ToBase64String(ecdh.ExportSubjectPublicKeyInfo()),
    };

    /// <summary>共有キー (32B) と確認コード (6桁)。両側で同じ結果になるよう公開鍵は順序を揃える。</summary>
    private static (byte[] Key, string Code) Derive(ECDiffieHellman mine, Hello remote)
    {
        byte[] myPublic = mine.ExportSubjectPublicKeyInfo();
        byte[] remotePublic = Convert.FromBase64String(remote.PublicKey);

        using var other = ECDiffieHellman.Create();
        other.ImportSubjectPublicKeyInfo(remotePublic, out _);
        byte[] secret = mine.DeriveRawSecretAgreement(other.PublicKey);

        byte[][] ordered = new[] { myPublic, remotePublic }
            .OrderBy(k => Convert.ToHexString(k), StringComparer.Ordinal)
            .ToArray();
        byte[] transcript = SHA256.HashData(ordered[0].Concat(ordered[1]).ToArray());

        byte[] key = HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, transcript, "SendClipboard pair key"u8.ToArray());
        byte[] codeBytes = HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 4, transcript, "SendClipboard pair code"u8.ToArray());
        string code = (BinaryPrimitives.ReadUInt32BigEndian(codeBytes) % 1_000_000).ToString("D6");
        return (key, code);
    }

    private static Task WriteJsonAsync<T>(Stream stream, T value, CancellationToken ct) =>
        WriteBytesAsync(stream, JsonSerializer.SerializeToUtf8Bytes(value), ct);

    private static async Task<T> ReadJsonAsync<T>(Stream stream, CancellationToken ct) =>
        JsonSerializer.Deserialize<T>(await ReadBytesAsync(stream, ct)) ?? throw new InvalidDataException("ペアリングメッセージが不正");

    private static async Task WriteBytesAsync(Stream stream, byte[] json, CancellationToken ct)
    {
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, json.Length);
        await stream.WriteAsync(length, ct);
        await stream.WriteAsync(json, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<byte[]> ReadBytesAsync(Stream stream, CancellationToken ct)
    {
        byte[] length = new byte[4];
        await stream.ReadExactlyAsync(length, ct);
        int size = BinaryPrimitives.ReadInt32BigEndian(length);
        if (size is <= 0 or > MaxMessageBytes)
        {
            throw new InvalidDataException("ペアリングメッセージのサイズが不正");
        }

        byte[] json = new byte[size];
        await stream.ReadExactlyAsync(json, ct);
        return json;
    }
}
