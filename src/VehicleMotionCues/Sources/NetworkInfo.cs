using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace VehicleMotionCues.Sources;

public sealed record LanAddress(string Label, IPAddress Address);

public static partial class NetworkInfo
{
    [GeneratedRegex("vEthernet|Virtual|VMware|VirtualBox|Hyper-V|WSL|Docker|Loopback|Bluetooth|TAP-|Tailscale|ZeroTier|VPN", RegexOptions.IgnoreCase)]
    private static partial Regex VirtualAdapter();

    /// <summary>IPv4 addresses the phone could reach, best guess first (iPhone hotspot, then Wi-Fi).</summary>
    public static IReadOnlyList<LanAddress> GetLanAddresses()
    {
        var found = new List<(int Rank, LanAddress Address)>();
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return [];
        }

        foreach (var ni in interfaces)
        {
            if (ni.OperationalStatus != OperationalStatus.Up ||
                ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            foreach (var unicast in ni.GetIPProperties().UnicastAddresses)
            {
                var ip = unicast.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip))
                    continue;
                var b = ip.GetAddressBytes();
                if (b[0] == 169 && b[1] == 254)
                    continue; // link-local, no DHCP

                // iPhone Personal Hotspot (Wi-Fi or USB) always hands out 172.20.10.0/28.
                bool hotspot = b[0] == 172 && b[1] == 20 && b[2] == 10;
                bool isVirtual = VirtualAdapter().IsMatch(ni.Name + " " + ni.Description);
                bool wifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;

                int rank = hotspot ? 0 : isVirtual ? 3 : wifi ? 1 : 2;
                string label = hotspot ? "iPhone Personal Hotspot" : wifi ? "Wi-Fi" : ni.Name;
                found.Add((rank, new LanAddress(label, ip)));
            }
        }

        return found.OrderBy(f => f.Rank).Select(f => f.Address).ToList();
    }
}
