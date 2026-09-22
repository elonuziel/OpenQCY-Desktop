using System.Text;
using OpenQCY_Desktop.Bluetooth;
using OpenQCY_Desktop.Device;
using OpenQCY_Desktop.Protocol;

Console.OutputEncoding = Encoding.UTF8;

var logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "probe-log.txt");
var logArgIndex = Array.FindIndex(args, a => string.Equals(a, "--log", StringComparison.OrdinalIgnoreCase));
if (logArgIndex >= 0 && logArgIndex < args.Length - 1)
{
    logFilePath = args[logArgIndex + 1];
}

using var fileWriter = new StreamWriter(logFilePath, append: false, Encoding.UTF8) { AutoFlush = true };
var originalOut = Console.Out;
var originalError = Console.Error;
var teeOut = new TeeWriter(originalOut, fileWriter);
var teeError = new TeeWriter(originalError, fileWriter);
Console.SetOut(teeOut);
Console.SetError(teeError);

try
{
    Console.WriteLine($"[OpenQCY Probe · {DateTime.Now:yyyy-MM-dd HH:mm:ss}]");
    Console.WriteLine($"Logging output to: {logFilePath}");
    Console.WriteLine();

    var willDisableWearDetection = args.Contains("--disable-wear-detection", StringComparer.OrdinalIgnoreCase);
    var windowsBatteryOnly = args.Contains("--windows-battery", StringComparer.OrdinalIgnoreCase);

    var addressArgIndex = Array.FindIndex(args, a =>
        string.Equals(a, "--address", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(a, "--control", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(a, "-a", StringComparison.OrdinalIgnoreCase));
    string? directAddressStr = null;
    if (addressArgIndex >= 0 && addressArgIndex < args.Length - 1)
    {
        directAddressStr = args[addressArgIndex + 1];
    }

    var customModelArgIndex = Array.FindIndex(args, a => string.Equals(a, "--custom-model", StringComparison.OrdinalIgnoreCase));
    if (customModelArgIndex >= 0 && customModelArgIndex < args.Length - 1)
    {
        var customModelPath = args[customModelArgIndex + 1];
        if (File.Exists(customModelPath))
        {
            var success = QcyDeviceRegistry.LoadCustomJson(File.ReadAllText(customModelPath));
            if (success)
            {
                Console.WriteLine($"Loaded custom model definition(s) from {customModelPath}.");
            }
            else
            {
                Console.Error.WriteLine($"Failed to parse custom model JSON from {customModelPath}.");
            }
        }
        else
        {
            Console.Error.WriteLine($"Custom model file not found: {customModelPath}");
        }
    }

    Console.WriteLine(willDisableWearDetection
        ? "OpenQCY Probe · diagnostics and requested wear-detection change"
        : "OpenQCY Probe · read-only local diagnostics");

    var transport = new WindowsBluetoothTransport();
    if (windowsBatteryOnly)
    {
        var windowsBattery = await transport.FindWindowsBatteryAsync();
        if (windowsBattery is null)
        {
            Console.Error.WriteLine("Windows did not provide a battery reading for any supported QCY device.");
            return 3;
        }

        Console.WriteLine(
            $"Windows cache: {windowsBattery.DeviceName} · {windowsBattery.Percentage}% · " +
            $"connected: {YesNo(windowsBattery.IsConnected)}");
        return 0;
    }

    BluetoothDeviceInfo? target = null;
    if (!string.IsNullOrWhiteSpace(directAddressStr))
    {
        if (QcyAdvertisement.TryParseAddress(directAddressStr, out var parsedAddress))
        {
            var formatted = QcyAdvertisement.FormatAddress(parsedAddress);
            Console.WriteLine($"Direct target address specified: {formatted}");
            target = new BluetoothDeviceInfo(
                formatted,
                "QCY Earbuds",
                parsedAddress,
                parsedAddress,
                null,
                QcyUuids.T13AncVendorId,
                short.MinValue,
                0, 0, 0, false, false, false,
                DateTimeOffset.UtcNow);
        }
        else
        {
            Console.Error.WriteLine($"Invalid Bluetooth MAC address: {directAddressStr}");
            Console.Error.WriteLine("Expected format: XX:XX:XX:XX:XX:XX (e.g. 84:AC:60:C0:9B:6F)");
            return 1;
        }
    }

    if (target is null)
    {
        Console.WriteLine("Scanning for QCY BLE advertisement (12s)…");
        Console.WriteLine("Tip: To broadcast telemetry, place earbuds in case, close the lid for 3s, then open the lid.");
        Console.WriteLine("Note: Ensure phone Bluetooth is disconnected so it doesn't occupy the BLE control channel.");
        Console.WriteLine();

        var devices = await transport.ScanForQcyDevicesAsync(TimeSpan.FromSeconds(12));
        if (devices.Count == 0)
        {
            Console.WriteLine("No live BLE advertisement found; checking Windows paired QCY devices…");
            var paired = await transport.FindPairedQcyDevicesAsync();
            if (paired.Count > 0)
            {
                devices = paired;
                Console.WriteLine($"Found {paired.Count} paired QCY device(s) in Windows cache.");
            }
        }

        if (devices.Count == 0)
        {
            var cachedItems = QcyDeviceCache.Instance.GetAll()
                .Where(item => item.ControlAddress.HasValue && item.ControlAddress.Value != 0)
                .ToArray();
            if (cachedItems.Length > 0)
            {
                Console.WriteLine($"Found {cachedItems.Length} remembered QCY device(s) in local cache.");
                devices = cachedItems.Select(item => new BluetoothDeviceInfo(
                    QcyAdvertisement.FormatAddress(item.ControlAddress!.Value),
                    string.IsNullOrWhiteSpace(item.Name) ? "QCY Earbuds" : item.Name,
                    item.ClassicAddress ?? item.ControlAddress!.Value,
                    item.ControlAddress,
                    null,
                    item.VendorId != 0 ? item.VendorId : QcyUuids.T13AncVendorId,
                    short.MinValue,
                    0, 0, 0, false, false, false,
                    item.LastSeen)).ToArray();
            }
        }

        if (devices.Count == 0)
        {
            Console.Error.WriteLine("No QCY BLE advertisement (0x521C), paired QCY device, or cached device was found.");
            Console.Error.WriteLine("Tip: Close the earbud case lid, wait 3 seconds, open the lid again near the PC, and retry.");
            Console.Error.WriteLine("Tip: Or run with --address <mac>, e.g.: OpenQCY.Probe.exe --address 84:AC:60:C0:9B:6F");
            return 2;
        }

        foreach (var device in devices)
        {
            var recognized = QcyDeviceRegistry.FindByVendorId(device.VendorId);
            var label = recognized is not null ? $"[{recognized.DisplayName}] " : "";
            Console.WriteLine(
                $"Found: {label}{device.Name} · vendor {device.VendorId} (0x{device.VendorId:X4}) · RSSI {device.SignalStrength} dBm · " +
                $"L {device.LeftBattery}% / R {device.RightBattery}% / case {device.CaseBattery}%");
        }

        target = devices.FirstOrDefault(device => QcyDeviceRegistry.IsSupported(device.VendorId)) ?? devices[0];
    }

    var targetAddr = target.ControlAddress ?? target.BluetoothAddress;
    Console.WriteLine($"Connecting to {target.Name} ({QcyAdvertisement.FormatAddress(targetAddr)}) control channel…");

    await using var connection = await transport.ConnectAsync(target);

    Console.WriteLine($"QCY characteristics found: {connection.Characteristics.Count}");
    foreach (var characteristic in connection.Characteristics.OrderBy(item => item.Uuid))
    {
        Console.WriteLine(
            $"  {characteristic.Uuid:D} · read {YesNo(characteristic.CanRead)} · " +
            $"write {YesNo(characteristic.CanWrite)} · notify {YesNo(characteristic.CanNotify)}");
    }

    var willListen = args.Contains("--listen", StringComparer.OrdinalIgnoreCase);

    if (willListen)
    {
        Console.WriteLine("Listen mode: subscribing to all notify characteristics for 30s (no commands sent).");
        Console.WriteLine("Press a button on the earbuds to capture protocol packets.");

        // Subscribe to every notify characteristic
        var notifyChars = connection.Characteristics.Where(c => c.CanNotify).ToArray();
        foreach (var chr in notifyChars)
        {
            await connection.SubscribeAsync(chr.Uuid);
            Console.WriteLine($"  Subscribed to {chr.Uuid:D}");
        }

        connection.ValueChanged += (_, e) =>
        {
            var hex = string.Join(" ", e.Value.Select(b => b.ToString("X2")));
            Console.WriteLine($"  [{DateTime.Now:HH:mm:ss.fff}] {e.CharacteristicUuid:D} → {hex}");
        };

        Console.WriteLine("Listening…");
        await Task.Delay(TimeSpan.FromSeconds(30));
        Console.WriteLine("Listen complete.");
        return 0;
    }

    await using var client = await QcyDeviceClient.CreateAsync(connection);
    client.ProtocolTrace += (_, line) => Console.WriteLine($"  {line}");
    await client.RefreshAsync();

    var state = client.State;
    Console.WriteLine($"Connected: {state.DeviceName} (Model: {state.Model?.DisplayName ?? "Generic / Fallback"})");
    Console.WriteLine($"Capabilities: ANC scenes={state.Capabilities.SupportsAncScenes}, LDAC={state.Capabilities.SupportsLdac}, Multipoint={state.Capabilities.SupportsMultipoint}, Wind={state.Capabilities.SupportsWindDetection}, EQ bands={state.Capabilities.EqualizerBands.Count}");
    Console.WriteLine($"Firmware: {state.FirmwareVersion ?? "not reported"}");
    Console.WriteLine($"Battery: L {Percent(state.Battery.Left)} · R {Percent(state.Battery.Right)} · case {Percent(state.Battery.Case)}");
    Console.WriteLine($"Wear detection: {BooleanText(state.WearDetectionEnabled)} · protocol {state.WearDetectionProtocol}");
    Console.WriteLine($"ANC: {state.NoiseMode?.ToString() ?? "not reported"}");
    Console.WriteLine($"Game mode: {BooleanText(state.GameModeEnabled)}");
    Console.WriteLine($"LDAC: {BooleanText(state.LdacEnabled)} · multipoint: {BooleanText(state.MultipointEnabled)}");
    Console.WriteLine($"Equalizer: preset {state.EqualizerPreset?.ToString() ?? "not reported"} · {state.EqualizerGains.Count} bands");

    if (willDisableWearDetection)
    {
        Console.WriteLine($"Disabling wear detection and waiting for {state.DeviceName} confirmation…");
        await client.SetWearDetectionAsync(false);
        Console.WriteLine($"Wear detection after confirmation: {BooleanText(client.State.WearDetectionEnabled)}");
    }

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("Probe error encountered:");
    Console.Error.WriteLine(ex.ToString());
    return 1;
}
finally
{
    Console.WriteLine();
    Console.WriteLine($"Diagnostic log successfully saved to: {logFilePath}");
}

static string Percent(byte? value) => value.HasValue ? $"{value}%" : "—";
static string YesNo(bool value) => value ? "yes" : "no";
static string BooleanText(bool? value) => value switch
{
    true => "enabled",
    false => "disabled",
    null => "not reported",
};

internal sealed class TeeWriter : TextWriter
{
    private readonly TextWriter _first;
    private readonly TextWriter _second;

    public TeeWriter(TextWriter first, TextWriter second)
    {
        _first = first;
        _second = second;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        _first.Write(value);
        _second.Write(value);
    }

    public override void Write(string? value)
    {
        _first.Write(value);
        _second.Write(value);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        _first.Write(buffer, index, count);
        _second.Write(buffer, index, count);
    }

    public override void WriteLine()
    {
        _first.WriteLine();
        _second.WriteLine();
    }

    public override void WriteLine(string? value)
    {
        _first.WriteLine(value);
        _second.WriteLine(value);
    }

    public override void Flush()
    {
        _first.Flush();
        _second.Flush();
    }
}
