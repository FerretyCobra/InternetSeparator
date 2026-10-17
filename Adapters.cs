using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace InternetSeparator;

// Label = stable name used in rules ("Wi-Fi", "USB Tethering"); Name = Windows adapter name mihomo binds to.
public record Adapter(string Label, string Name, string Ip, NetworkInterface Nic);

public static class Adapters
{
    public const string TunName = "InternetSeparator";

    // Every adapter that is up and has an IPv4 gateway, i.e. can reach the internet.
    public static List<Adapter> Detect()
    {
        var list = new List<Adapter>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.Name == TunName) continue;
            var props = nic.GetIPProperties();
            if (!props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))) continue;
            var ip = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "";
            var kind = Kind(nic);
            var label = kind;
            for (var n = 2; list.Any(a => a.Label == label); n++) label = $"{kind} {n}";
            list.Add(new Adapter(label, nic.Name, ip, nic));
        }
        return list;
    }

    static string Kind(NetworkInterface nic)
    {
        if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) return "Wi-Fi";
        var d = nic.Description;
        string[] tether = ["Remote NDIS", "RNDIS", "Apple Mobile Device", "Tether"];
        return tether.Any(t => d.Contains(t, StringComparison.OrdinalIgnoreCase)) ? "USB Tethering" : "Ethernet";
    }
}
