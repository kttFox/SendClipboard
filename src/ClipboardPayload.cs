using System.Text;

namespace SendClipboard;

/// <summary>
/// 転送するクリップボード内容。形式ごとに (名前, バイト列) を持つ。
/// 対応形式はテキストのみ。
/// </summary>
internal sealed class ClipboardPayload
{
    private const string FormatText = "text";

    private readonly List<(string Name, byte[] Data)> items = new();

    public bool IsEmpty => items.Count == 0;

    public string Describe() => string.Join(", ", items.Select(i => $"{i.Name}:{i.Data.Length:N0}B"));

    /// <summary>現在のクリップボードから取得する。UIスレッド(STA)で呼ぶこと。</summary>
    public static async Task<ClipboardPayload> CaptureAsync()
    {
        var payload = new ClipboardPayload();
        IDataObject? data = await RetryAsync(Clipboard.GetDataObject);
        if (data?.GetData(DataFormats.UnicodeText) is string text)
        {
            payload.items.Add((FormatText, Encoding.UTF8.GetBytes(text)));
        }

        return payload;
    }

    /// <summary>クリップボードに設定する。UIスレッド(STA)で呼ぶこと。</summary>
    public async Task ApplyAsync()
    {
        var data = new DataObject();
        foreach (var (name, bytes) in items)
        {
            if (name == FormatText)
            {
                data.SetData(DataFormats.UnicodeText, Encoding.UTF8.GetString(bytes));
            }
        }

        await RetryAsync(() =>
        {
            Clipboard.SetDataObject(data, copy: true);
            return true;
        });
    }

    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(items.Count);
        foreach (var (name, bytes) in items)
        {
            w.Write(name);
            w.Write(bytes.Length);
            w.Write(bytes);
        }

        w.Flush();
        return ms.ToArray();
    }

    public static ClipboardPayload Deserialize(byte[] buffer)
    {
        var payload = new ClipboardPayload();
        using var r = new BinaryReader(new MemoryStream(buffer));
        int count = r.ReadInt32();
        if (count is < 0 or > 16)
        {
            throw new InvalidDataException("形式数が不正");
        }

        for (int i = 0; i < count; i++)
        {
            string name = r.ReadString();
            int length = r.ReadInt32();
            if (length < 0 || length > buffer.Length)
            {
                throw new InvalidDataException("データ長が不正");
            }

            payload.items.Add((name, r.ReadBytes(length)));
        }

        return payload;
    }

    /// <summary>
    /// 他アプリがクリップボードを開いていると失敗するので数回リトライする。
    /// 待機中も UI が固まらないよう非同期で待つ (UIスレッドで呼べば続きもUIスレッドで動く)。
    /// </summary>
    private static async Task<T?> RetryAsync<T>(Func<T?> action)
    {
        for (int i = 0; ; i++)
        {
            try
            {
                return action();
            }
            catch (System.Runtime.InteropServices.ExternalException) when (i < 10)
            {
                await Task.Delay(50);
            }
        }
    }
}
