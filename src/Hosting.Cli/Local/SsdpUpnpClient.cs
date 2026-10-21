using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Unity.Multiplayer.Hosting.Cli.Local;

/// <summary>
/// Real <see cref="IUpnpClient"/>: SSDP M-SEARCH discovery of an Internet Gateway Device on the LAN,
/// then SOAP calls against its <c>WANIPConnection</c> (falling back to <c>WANPPPConnection</c> for a
/// PPPoE modem/router) service. Three steps, same as any UPnP IGD client:
/// <list type="number">
/// <item>Broadcast <c>M-SEARCH</c> to <c>239.255.255.250:1900</c> and read the first reply's
/// <c>LOCATION</c> header -- the device description document's URL.</item>
/// <item>GET that document and find the WAN connection service's <c>controlURL</c>.</item>
/// <item>POST a SOAP <c>AddPortMapping</c>/<c>DeletePortMapping</c>/<c>GetExternalIPAddress</c>
/// envelope to it.</item>
/// </list>
/// A router with UPnP disabled (most HOA/managed networks, per RFC 0001) simply never answers step 1
/// within <see cref="DiscoveryTimeout"/> -- that is reported as a normal, expected
/// <see cref="UpnpMappingResult"/> failure, not an exception.
/// </summary>
public sealed class SsdpUpnpClient : IUpnpClient
{
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(3);
    private const int SsdpPort = 1900;
    private const string SsdpMulticastAddress = "239.255.255.250";
    private static readonly string[] SearchTargets =
    {
        "urn:schemas-upnp-org:service:WANIPConnection:1",
        "urn:schemas-upnp-org:service:WANPPPConnection:1",
    };

    private readonly HttpClient _http;

    public SsdpUpnpClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    /// <inheritdoc />
    public async Task<UpnpMappingResult> TryAddPortMappingAsync(int externalPort, int internalPort, string protocol, string description, CancellationToken ct)
    {
        GatewayService? gateway;
        try
        {
            gateway = await DiscoverGatewayAsync(ct);
        }
        catch (Exception ex) when (ex is SocketException or HttpRequestException or TaskCanceledException or XmlStructureException)
        {
            return new UpnpMappingResult(false, null, $"UPnP discovery failed: {ex.Message}");
        }

        if (gateway is null)
        {
            return new UpnpMappingResult(false, null, "No UPnP Internet Gateway Device answered the discovery broadcast within " + DiscoveryTimeout + " (UPnP is likely disabled on this router -- common on an HOA/managed network).");
        }

        string localIp = LanAddresses.GetLocalIPv4Addresses().FirstOrDefault() ?? "127.0.0.1";
        var args = new (string Name, string Value)[]
        {
            ("NewRemoteHost", ""),
            ("NewExternalPort", externalPort.ToString()),
            ("NewProtocol", protocol.ToUpperInvariant()),
            ("NewInternalPort", internalPort.ToString()),
            ("NewInternalClient", localIp),
            ("NewEnabled", "1"),
            ("NewPortMappingDescription", description),
            ("NewLeaseDuration", "0"),
        };

        try
        {
            await SoapCallAsync(gateway, "AddPortMapping", args, ct);
        }
        catch (UpnpSoapFaultException fault)
        {
            return new UpnpMappingResult(false, null, $"The router refused the port mapping ({fault.Message}).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new UpnpMappingResult(false, null, $"Could not reach the router's control URL: {ex.Message}");
        }

        string? externalIp = null;
        try
        {
            string body = await SoapCallAsync(gateway, "GetExternalIPAddress", Array.Empty<(string, string)>(), ct);
            var match = Regex.Match(body, "<NewExternalIPAddress>([^<]*)</NewExternalIPAddress>");
            if (match.Success) externalIp = match.Groups[1].Value;
        }
        catch (Exception)
        {
            // Best-effort only: the mapping already succeeded even if this follow-up call fails.
        }

        return new UpnpMappingResult(true, externalIp, null);
    }

    /// <inheritdoc />
    public async Task TryRemovePortMappingAsync(int externalPort, string protocol, CancellationToken ct)
    {
        try
        {
            GatewayService? gateway = await DiscoverGatewayAsync(ct);
            if (gateway is null) return;
            await SoapCallAsync(gateway, "DeletePortMapping", new (string, string)[]
            {
                ("NewRemoteHost", ""),
                ("NewExternalPort", externalPort.ToString()),
                ("NewProtocol", protocol.ToUpperInvariant()),
            }, ct);
        }
        catch (Exception)
        {
            // Best-effort: the mapping will simply time out on the router's side otherwise.
        }
    }

    private sealed record GatewayService(string ControlUrl, string ServiceType);

    private sealed class XmlStructureException : Exception
    {
        public XmlStructureException(string message) : base(message) { }
    }

    private sealed class UpnpSoapFaultException : Exception
    {
        public UpnpSoapFaultException(string message) : base(message) { }
    }

    private async Task<GatewayService?> DiscoverGatewayAsync(CancellationToken ct)
    {
        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        var endpoint = new IPEndPoint(IPAddress.Parse(SsdpMulticastAddress), SsdpPort);

        foreach (string searchTarget in SearchTargets)
        {
            string message =
                "M-SEARCH * HTTP/1.1\r\n" +
                $"HOST: {SsdpMulticastAddress}:{SsdpPort}\r\n" +
                "MAN: \"ssdp:discover\"\r\n" +
                "MX: 2\r\n" +
                $"ST: {searchTarget}\r\n\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(message);
            await client.SendAsync(bytes, bytes.Length, endpoint);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(DiscoveryTimeout);
        try
        {
            while (!timeoutCts.IsCancellationRequested)
            {
                UdpReceiveResult received = await client.ReceiveAsync(timeoutCts.Token);
                string response = Encoding.ASCII.GetString(received.Buffer);
                string? location = ExtractHeader(response, "LOCATION");
                if (location is null) continue;

                GatewayService? service = await TryResolveServiceAsync(location, ct);
                if (service is not null) return service;
            }
        }
        catch (OperationCanceledException)
        {
            // Discovery window elapsed with no usable reply -- treated as "no gateway" by the caller.
        }

        return null;
    }

    private async Task<GatewayService?> TryResolveServiceAsync(string descriptionUrl, CancellationToken ct)
    {
        string xml = await _http.GetStringAsync(descriptionUrl, ct);
        XDocument document = XDocument.Parse(xml);
        XNamespace ns = document.Root?.Name.Namespace ?? "urn:schemas-upnp-org:device-1-0";

        foreach (string searchTarget in SearchTargets)
        {
            XElement? service = document.Descendants(ns + "service")
                .FirstOrDefault(s => string.Equals((string?)s.Element(ns + "serviceType"), searchTarget, StringComparison.Ordinal));
            if (service is null) continue;

            string? controlPath = (string?)service.Element(ns + "controlURL");
            if (string.IsNullOrWhiteSpace(controlPath)) continue;

            var baseUri = new Uri(descriptionUrl);
            string controlUrl = Uri.TryCreate(controlPath, UriKind.Absolute, out var absolute) ? absolute.ToString() : new Uri(baseUri, controlPath).ToString();
            return new GatewayService(controlUrl, searchTarget);
        }

        return null;
    }

    private async Task<string> SoapCallAsync(GatewayService gateway, string action, IReadOnlyList<(string Name, string Value)> arguments, CancellationToken ct)
    {
        var argsXml = new StringBuilder();
        foreach (var (name, value) in arguments)
        {
            argsXml.Append('<').Append(name).Append('>').Append(System.Security.SecurityElement.Escape(value)).Append("</").Append(name).Append('>');
        }

        string envelope =
            "<?xml version=\"1.0\"?>" +
            "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
            "<s:Body>" +
            $"<u:{action} xmlns:u=\"{gateway.ServiceType}\">{argsXml}</u:{action}>" +
            "</s:Body></s:Envelope>";

        using var request = new HttpRequestMessage(HttpMethod.Post, gateway.ControlUrl);
        request.Content = new StringContent(envelope, Encoding.UTF8, "text/xml");
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{gateway.ServiceType}#{action}\"");

        HttpResponseMessage response = await _http.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var match = Regex.Match(body, "<errorDescription>([^<]*)</errorDescription>");
            throw new UpnpSoapFaultException(match.Success ? match.Groups[1].Value : $"HTTP {(int)response.StatusCode}");
        }

        return body;
    }

    private static string? ExtractHeader(string httpMessage, string headerName)
    {
        foreach (string line in httpMessage.Split("\r\n"))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            if (string.Equals(line[..colon].Trim(), headerName, StringComparison.OrdinalIgnoreCase))
            {
                return line[(colon + 1)..].Trim();
            }
        }

        return null;
    }
}
