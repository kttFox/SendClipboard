using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SendClipboard;

/// <summary>このPCが直接つながっているローカルネットワーク (IPv4 サブネット) の情報。</summary>
internal static class LocalNetwork
{
    internal readonly record struct Subnet(IPAddress Address, IPAddress Mask)
    {
        public IPAddress Broadcast
        {
            get
            {
                byte[] a = Address.GetAddressBytes();
                byte[] m = Mask.GetAddressBytes();
                for (int i = 0; i < 4; i++)
                {
                    a[i] = (byte)(a[i] | ~m[i]);
                }

                return new IPAddress(a);
            }
        }

        public bool Contains(IPAddress other)
        {
            byte[] a = Address.GetAddressBytes();
            byte[] m = Mask.GetAddressBytes();
            byte[] o = other.GetAddressBytes();
            for (int i = 0; i < 4; i++)
            {
                if ((a[i] & m[i]) != (o[i] & m[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);
    private static readonly object CacheLock = new();
    private static List<Subnet>? cache;
    private static DateTime cachedAtUtc;

    /// <summary>
    /// 稼働中の物理/無線インターフェースの IPv4 サブネット一覧。
    /// パケット・接続ごとに呼ばれるので、NIC の列挙結果は数秒キャッシュする。
    /// </summary>
    public static IReadOnlyList<Subnet> GetSubnets()
    {
        lock (CacheLock)
        {
            if (cache == null || DateTime.UtcNow - cachedAtUtc > CacheDuration)
            {
                cache = Enumerate();
                cachedAtUtc = DateTime.UtcNow;
            }

            return cache;
        }
    }

    private static List<Subnet> Enumerate()
    {
        var result = new List<Subnet>();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
            {
                if (info.Address.AddressFamily == AddressFamily.InterNetwork && info.IPv4Mask != null &&
                    !info.IPv4Mask.Equals(IPAddress.Any) && !IsLinkLocal(info.Address))
                {
                    result.Add(new Subnet(info.Address, info.IPv4Mask));
                }
            }
        }

        return result;
    }

    /// <summary>169.254.x.x (DHCP 失敗時の自動割り当て) は対象外。</summary>
    private static bool IsLinkLocal(IPAddress address)
    {
        byte[] b = address.GetAddressBytes();
        return b[0] == 169 && b[1] == 254;
    }

    /// <summary>相手のアドレスが同じローカルネットワーク内 (または自分自身) か。</summary>
    public static bool IsLocal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        return address.AddressFamily == AddressFamily.InterNetwork && GetSubnets().Any(s => s.Contains(address));
    }
}
