using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SendClipboard;

/// <summary>
/// ペアリング済みの相手。Key は2台だけが知っている共有キー (Base64, 32バイト)。
/// 設定ファイルには DPAPI (現在のWindowsユーザー) で暗号化した ProtectedKey として保存する。
/// </summary>
public sealed class PairedDevice
{
    private static readonly byte[] Entropy = "SendClipboard pair key"u8.ToArray();

    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    [JsonIgnore]
    public string Key { get; set; } = "";

    public string ProtectedKey
    {
        get => string.IsNullOrEmpty(Key) ? "" : Convert.ToBase64String(
            ProtectedData.Protect(Encoding.ASCII.GetBytes(Key), Entropy, DataProtectionScope.CurrentUser));
        set
        {
            try
            {
                Key = string.IsNullOrEmpty(value) ? "" : Encoding.ASCII.GetString(
                    ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.CurrentUser));
            }
            catch (Exception e) when (e is CryptographicException or FormatException)
            {
                Key = ""; // 読み込み後に破棄する (設定全体を失わないよう例外にしない)
            }
        }
    }

    public DateTime PairedAt { get; set; }

    [JsonIgnore]
    public byte[] KeyBytes => Convert.FromBase64String(Key);
}

public sealed class AppConfig
{
    /// <summary>このPCを識別するID (LocalSend の fingerprint 相当)。初回起動時に生成し、以後変えない。</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>一覧に表示される名前 (LocalSend の alias 相当)。空ならコンピューター名。</summary>
    public string Alias { get; set; } = "";

    /// <summary>このPCのクリップボードを相手に送る。</summary>
    public bool SendEnabled { get; set; } = true;

    /// <summary>相手のクリップボードを受け取る。</summary>
    public bool ReceiveEnabled { get; set; }

    /// <summary>送受信の成功をバルーン通知する (失敗は常に通知)。トレイメニューで切り替える。</summary>
    public bool NotifyEnabled { get; set; } = true;

    /// <summary>通信ポート (TCP: 転送・ペアリング / UDP: マルチキャスト探索)。全PCで同じ値にする。
    /// LocalSend (53317) と共存できるよう隣の番号を既定にする。</summary>
    public int Port { get; set; } = 53318;

    /// <summary>1回に転送する最大サイズ (MB)。</summary>
    public int MaxSizeMB { get; set; } = 50;

    public List<PairedDevice> PairedDevices { get; set; } = new();

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? Environment.MachineName : Alias.Trim();

    /// <summary>設定・ログの保存先。既定は実行ファイルと同じフォルダ。起動オプション --data-dir で変更できる。</summary>
    public static string DirectoryPath { get; set; } = AppContext.BaseDirectory;

    public static string FilePath => Path.Combine(DirectoryPath, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object saveLock = new();

    public PairedDevice? FindPair(string id)
    {
        lock (saveLock)
        {
            return PairedDevices.FirstOrDefault(p => p.Id == id);
        }
    }

    public List<PairedDevice> GetPairs()
    {
        lock (saveLock)
        {
            return PairedDevices.ToList();
        }
    }

    public void AddPair(PairedDevice device)
    {
        lock (saveLock)
        {
            PairedDevices.RemoveAll(p => p.Id == device.Id);
            PairedDevices.Add(device);
            Save();
        }
    }

    public void RemovePair(string id)
    {
        lock (saveLock)
        {
            PairedDevices.RemoveAll(p => p.Id == id);
            Save();
        }
    }

    /// <summary>設定画面で変更できる項目だけを取り込む (ペア情報・DeviceId は維持)。</summary>
    public void ApplyOptions(AppConfig other)
    {
        lock (saveLock)
        {
            Alias = other.Alias;
            SendEnabled = other.SendEnabled;
            ReceiveEnabled = other.ReceiveEnabled;
            Port = other.Port;
        }
    }

    public bool IsValid(out string error)
    {
        if (Port is < 1 or > 65535)
        {
            error = "ポート番号が不正です。";
            return false;
        }

        error = "";
        return true;
    }

    public static AppConfig Load()
    {
        AppConfig config;
        try
        {
            config = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), JsonOptions) ?? new AppConfig()
                : new AppConfig();
        }
        catch (Exception e)
        {
            Log.Write($"設定の読み込みに失敗: {e.Message}");
            config = new AppConfig();
        }

        // 読めないキー (別ユーザー・別PCで暗号化されたもの) は捨てる。ペアリングし直してもらう
        int broken = config.PairedDevices.RemoveAll(p => string.IsNullOrEmpty(p.Key));
        if (broken > 0)
        {
            Log.Write($"復号できないペア情報を {broken} 件削除しました");
        }

        bool changed = broken > 0;
        if (string.IsNullOrEmpty(config.DeviceId))
        {
            config.DeviceId = Guid.NewGuid().ToString("N");
            changed = true;
        }

        if (changed)
        {
            config.Save();
        }

        return config;
    }

    public void Save()
    {
        lock (saveLock)
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
    }
}
