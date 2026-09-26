using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SendClipboard;

/// <summary>TCP 接続の先頭1バイトで用途を区別する。</summary>
internal enum ConnectionKind : byte
{
    Clipboard = (byte)'C',
    Pairing = (byte)'P',
}

/// <summary>
/// クリップボード転送のフォーマット。1接続 = 1メッセージ。
///   [kind 'C'][magic "SCB1"][idLen 1B][送信元DeviceId]
///   [ヘッダー: nonce 12B][tag 16B][暗号化された (length 4B + timestamp 8B) 12B]
///   [本文:     nonce 12B][tag 16B][暗号化された ClipboardPayload (length バイト)]
/// 鍵はペアリング時に作った2台だけの共有キー。受信側は送信元IDで鍵を引き、AES-256-GCM で復号+改ざん検知する。
/// 先に小さなヘッダーだけを認証し、正規の相手と確認できてから本文用のメモリを確保する
/// (送信元IDは探索パケットで公開されているので、IDだけでは相手を信用できない)。
/// タイムスタンプが古すぎるもの・同じヘッダー nonce の再送は拒否する(再送攻撃対策)。
/// 本文の AAD にはヘッダー nonce を含め、別メッセージのヘッダーと本文を組み合わせられないようにする。
/// </summary>
internal static class Protocol
{
    private static readonly byte[] Magic = "SCB1"u8.ToArray();
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderPlainSize = 4 + 8;
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(2);

    /// <summary>ヘッダーの認証が済むまでの制限時間。データを送らずに接続を占有されるのを防ぐ。</summary>
    private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(5);

    public static async Task WriteAsync(Stream stream, string senderId, byte[] key, byte[] payload, CancellationToken ct)
    {
        byte[] id = Encoding.ASCII.GetBytes(senderId);
        byte[] aad = BuildAad(id);

        byte[] headerPlain = new byte[HeaderPlainSize];
        BinaryPrimitives.WriteInt32BigEndian(headerPlain, payload.Length);
        BinaryPrimitives.WriteInt64BigEndian(headerPlain.AsSpan(4), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        byte[] headerNonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] headerTag = new byte[TagSize];
        byte[] headerCipher = new byte[HeaderPlainSize];
        byte[] bodyNonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] bodyTag = new byte[TagSize];
        byte[] bodyCipher = new byte[payload.Length];
        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(headerNonce, headerPlain, headerCipher, headerTag, aad);
            aes.Encrypt(bodyNonce, payload, bodyCipher, bodyTag, BuildBodyAad(aad, headerNonce));
        }

        await stream.WriteAsync(new[] { (byte)ConnectionKind.Clipboard }, ct);
        await stream.WriteAsync(aad, ct);
        await stream.WriteAsync(headerNonce, ct);
        await stream.WriteAsync(headerTag, ct);
        await stream.WriteAsync(headerCipher, ct);
        await stream.WriteAsync(bodyNonce, ct);
        await stream.WriteAsync(bodyTag, ct);
        await stream.WriteAsync(bodyCipher, ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>
    /// 種別バイトは読み取り済みの前提。送信元IDと平文を返す。
    /// onAuthenticated はヘッダーの認証と検査を通った時点 (= 鍵を持つ正規の相手と確認できた時点) で呼ばれる。
    /// </summary>
    public static async Task<(string SenderId, byte[] Payload)> ReadAsync(
        Stream stream, Func<string, byte[]?> findKey, int maxBytes, Action onAuthenticated, CancellationToken ct)
    {
        using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        headerCts.CancelAfter(HeaderTimeout);

        byte[] head = new byte[Magic.Length + 1];
        await stream.ReadExactlyAsync(head, headerCts.Token);
        if (!head.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException("不正なヘッダー");
        }

        byte[] id = new byte[head[^1]];
        await stream.ReadExactlyAsync(id, headerCts.Token);
        string senderId = Encoding.ASCII.GetString(id);
        byte[] aad = BuildAad(id);

        byte[] key = findKey(senderId) ?? throw new UnauthorizedAccessException("ペアリングされていない相手");
        using var aes = new AesGcm(key, TagSize);

        // 1. 小さな固定長ヘッダーを認証する。ここを通れるのは鍵を持つ相手だけ
        byte[] header = new byte[NonceSize + TagSize + HeaderPlainSize];
        await stream.ReadExactlyAsync(header, headerCts.Token);
        byte[] headerNonce = header[..NonceSize];
        byte[] headerPlain = new byte[HeaderPlainSize];
        try
        {
            aes.Decrypt(headerNonce, header.AsSpan(NonceSize + TagSize), header.AsSpan(NonceSize, TagSize), headerPlain, aad);
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new InvalidDataException("復号に失敗 (ペア情報が古い可能性。ペアリングし直してください)");
        }

        int length = BinaryPrimitives.ReadInt32BigEndian(headerPlain);
        var sentAt = DateTimeOffset.FromUnixTimeMilliseconds(BinaryPrimitives.ReadInt64BigEndian(headerPlain.AsSpan(4)));
        if (length < 0 || length > maxBytes)
        {
            throw new InvalidDataException($"サイズ超過または不正: {length} bytes");
        }

        if ((DateTimeOffset.UtcNow - sentAt).Duration() > MaxClockSkew)
        {
            throw new InvalidDataException("タイムスタンプが古すぎる (PCの時計ずれの可能性)");
        }

        if (!ReplayGuard.TryAdd(senderId, headerNonce))
        {
            throw new InvalidDataException("同じメッセージの再送を拒否");
        }

        onAuthenticated();

        // 2. 認証済みの length で本文を受け取る。復号はその場で行い、2倍のメモリを確保しない
        byte[] bodyHead = new byte[NonceSize + TagSize];
        await stream.ReadExactlyAsync(bodyHead, ct);
        byte[] plain = new byte[length];
        await stream.ReadExactlyAsync(plain, ct);
        try
        {
            aes.Decrypt(bodyHead.AsSpan(0, NonceSize), plain, bodyHead.AsSpan(NonceSize), plain, BuildBodyAad(aad, headerNonce));
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new InvalidDataException("本文の復号に失敗 (改ざんの可能性)");
        }

        return (senderId, plain);
    }

    private static byte[] BuildBodyAad(byte[] aad, byte[] headerNonce) => [.. aad, .. headerNonce];

    private static byte[] BuildAad(byte[] id)
    {
        byte[] aad = new byte[Magic.Length + 1 + id.Length];
        Magic.CopyTo(aad, 0);
        aad[Magic.Length] = (byte)id.Length;
        id.CopyTo(aad, Magic.Length + 1);
        return aad;
    }

    /// <summary>
    /// 受信済みの (送信元, nonce) を時計ずれ許容時間の2倍だけ覚えておき、同じメッセージの再送を拒否する。
    /// それより古いメッセージはタイムスタンプ検査で弾かれるので、記録はその期間だけで足りる。
    /// </summary>
    private static class ReplayGuard
    {
        private static readonly object Lock = new();
        private static readonly Dictionary<string, DateTime> Seen = new();

        public static bool TryAdd(string senderId, byte[] nonce)
        {
            string key = senderId + ":" + Convert.ToHexString(nonce);
            DateTime now = DateTime.UtcNow;
            lock (Lock)
            {
                foreach (var old in Seen.Where(p => now - p.Value > MaxClockSkew * 2).Select(p => p.Key).ToList())
                {
                    Seen.Remove(old);
                }

                return Seen.TryAdd(key, now);
            }
        }
    }
}
