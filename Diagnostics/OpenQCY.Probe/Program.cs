using System.Text;
using OpenQCY_Desktop.Bluetooth;
using OpenQCY_Desktop.Device;
using OpenQCY_Desktop.Protocol;

Console.OutputEncoding = Encoding.UTF8;
var willDisableWearDetection = args.Contains("--disable-wear-detection", StringComparer.OrdinalIgnoreCase);
var windowsBatteryOnly = args.Contains("--windows-battery", StringComparer.OrdinalIgnoreCase);

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
Console.WriteLine("Open the case and keep both earbuds near the computer.");

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

var devices = await transport.ScanForQcyDevicesAsync(TimeSpan.FromSeconds(12));
if (devices.Count == 0)
{
    Console.Error.WriteLine("No QCY BLE advertisement (0x521C) was found.");
    return 2;
}

foreach (var device in devices)
{
    var recognized = QcyDeviceRegistry.Find(device.VendorId);
    var label = recognized is not null ? $"[{recognized.DisplayName}] " : "";
    Console.WriteLine(
        $"Found: {label}{device.Name} · vendor {device.VendorId} (0x{device.VendorId:X4}) · RSSI {device.SignalStrength} dBm · " +
        $"L {device.LeftBattery}% / R {device.RightBattery}% / case {device.CaseBattery}%");
}

var target = devices.FirstOrDefault(device => QcyDeviceRegistry.IsSupported(device.VendorId)) ?? devices[0];
Console.WriteLine($"Connecting to the {target.Name} control channel…");

await using var connection = await transport.ConnectAsync(target);
await using var client = await QcyDeviceClient.CreateAsync(connection);
client.ProtocolTrace += (_, line) => Console.WriteLine($"  {line}");
await client.RefreshAsync();

var state = client.State;
Console.WriteLine($"Connected: {state.DeviceName} (Model: {state.Model?.DisplayName ?? "Generic / Fallback"})");
Console.WriteLine($"Capabilities: ANC={state.Capabilities.SupportsNoiseControl}, LDAC={state.Capabilities.SupportsLdac}, Multipoint={state.Capabilities.SupportsMultipoint}, Wind={state.Capabilities.SupportsWindNoiseReduction}, Presets={state.Capabilities.SupportsEqualizerPresets}");
Console.WriteLine($"Firmware: {state.FirmwareVersion ?? "not reported"}");
Console.WriteLine($"Battery: L {Percent(state.Battery.Left)} · R {Percent(state.Battery.Right)} · case {Percent(state.Battery.Case)}");
Console.WriteLine($"Wear detection: {BooleanText(state.WearDetectionEnabled)} · protocol {state.WearDetectionProtocol}");
Console.WriteLine($"ANC: {state.NoiseMode?.ToString() ?? "not reported"}");
Console.WriteLine($"Game mode: {BooleanText(state.GameModeEnabled)}");
Console.WriteLine($"LDAC: {BooleanText(state.LdacEnabled)} · multipoint: {BooleanText(state.MultipointEnabled)}");
Console.WriteLine($"Equalizer: preset {state.EqualizerPreset?.ToString() ?? "not reported"} · {state.EqualizerGains.Count} bands");
Console.WriteLine($"QCY characteristics found: {connection.Characteristics.Count}");
foreach (var characteristic in connection.Characteristics.OrderBy(item => item.Uuid))
{
    Console.WriteLine(
        $"  {characteristic.Uuid:D} · read {YesNo(characteristic.CanRead)} · " +
        $"write {YesNo(characteristic.CanWrite)} · notify {YesNo(characteristic.CanNotify)}");
}

if (willDisableWearDetection)
{
    Console.WriteLine($"Disabling wear detection and waiting for {state.DeviceName} confirmation…");
    await client.SetWearDetectionAsync(false);
    Console.WriteLine($"Wear detection after confirmation: {BooleanText(client.State.WearDetectionEnabled)}");
}

return 0;

static string Percent(byte? value) => value.HasValue ? $"{value}%" : "—";
static string YesNo(bool value) => value ? "yes" : "no";
static string BooleanText(bool? value) => value switch
{
    true => "enabled",
    false => "disabled",
    null => "not reported",
};
