using System.Collections.Concurrent;
using OpenQCY_Desktop.Protocol;
using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace OpenQCY_Desktop.Bluetooth;

public sealed class WindowsBluetoothTransport : IBluetoothTransport
{
    private const string BatteryLifeProperty = "System.Devices.BatteryLife";
    private const string IsConnectedProperty = "System.Devices.Aep.IsConnected";

    public async Task<BluetoothBatteryInfo?> FindWindowsBatteryAsync(
        CancellationToken cancellationToken = default)
    {
        var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var properties = new[] { BatteryLifeProperty, IsConnectedProperty };
        var pairedDevices = await DeviceInformation.FindAllAsync(selector, properties);
        cancellationToken.ThrowIfCancellationRequested();

        return pairedDevices
            .Where(device => QcyDeviceRegistry.LooksLikeSupported(device.Name))
            .Select(device => new
            {
                device.Name,
                Battery = ReadByteProperty(device, BatteryLifeProperty),
                IsConnected = ReadBooleanProperty(device, IsConnectedProperty),
            })
            .Where(device => device.Battery is <= 100)
            .OrderByDescending(device => device.IsConnected)
            .Select(device => new BluetoothBatteryInfo(
                device.Name,
                device.Battery!.Value,
                device.IsConnected))
            .FirstOrDefault();
    }

    public async Task<IReadOnlyList<BluetoothDeviceInfo>> FindPairedQcyDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        var devices = new List<BluetoothDeviceInfo>();
        var knownServices = new List<DeviceInformation>();
        foreach (var serviceUuid in new[] { QcyUuids.MainService, QcyUuids.SecondaryService })
        {
            var serviceSelector = GattDeviceService.GetDeviceSelectorFromUuid(serviceUuid);
            knownServices.AddRange(await DeviceInformation.FindAllAsync(serviceSelector));
        }
        foreach (var knownService in knownServices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GattDeviceService? service = null;
            try
            {
                service = await GattDeviceService.FromIdAsync(knownService.Id);
                cancellationToken.ThrowIfCancellationRequested();
                if (service is null)
                {
                    continue;
                }

                var device = await CreateKnownDeviceAsync(
                    service.Session.DeviceId.Id,
                    knownService.Name,
                    cancellationToken);
                if (device is not null)
                {
                    devices.Add(device);
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                service?.Dispose();
            }
        }

        var pairedSelector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
        var pairedDevices = await DeviceInformation.FindAllAsync(pairedSelector);
        foreach (var pairedDevice in pairedDevices.Where(device => QcyDeviceRegistry.LooksLikeSupported(device.Name)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var device = await CreateKnownDeviceAsync(
                    pairedDevice.Id,
                    pairedDevice.Name,
                    cancellationToken);
                if (device is not null)
                {
                    devices.Add(device);
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A cached Windows entry may be stale. Other candidates can still be valid.
            }
        }

        var classicSelector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var pairedClassicDevices = await DeviceInformation.FindAllAsync(classicSelector);
        foreach (var pairedClassic in pairedClassicDevices.Where(device => QcyDeviceRegistry.LooksLikeSupported(device.Name)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var classicDevice = await BluetoothDevice.FromIdAsync(pairedClassic.Id);
                cancellationToken.ThrowIfCancellationRequested();
                if (classicDevice is null)
                {
                    continue;
                }

                var matchedModel = QcyDeviceRegistry.FindByBluetoothName(classicDevice.Name)
                    ?? QcyDeviceRegistry.FindByBluetoothName(pairedClassic.Name);
                var name = string.IsNullOrWhiteSpace(classicDevice.Name)
                    ? pairedClassic.Name ?? matchedModel?.DisplayName ?? "QCY Earbuds"
                    : classicDevice.Name;
                var vendorId = matchedModel?.VendorIds.FirstOrDefault() ?? QcyUuids.N70BlackVendorId;

                devices.Add(new BluetoothDeviceInfo(
                    pairedClassic.Id,
                    name,
                    classicDevice.BluetoothAddress,
                    null,
                    null,
                    vendorId,
                    short.MinValue,
                    0,
                    0,
                    0,
                    false,
                    false,
                    false,
                    DateTimeOffset.UtcNow));
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }

        return devices
            .DistinctBy(device => device.BluetoothAddress)
            .ToArray();
    }

    public async Task<IReadOnlyList<BluetoothDeviceInfo>> ScanForQcyDevicesAsync(
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var devices = new ConcurrentDictionary<ulong, BluetoothDeviceInfo>();
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
            AllowExtendedAdvertisements = true,
        };

        void OnReceived(BluetoothLEAdvertisementWatcher _, BluetoothLEAdvertisementReceivedEventArgs eventArgs)
        {
            foreach (var manufacturerData in eventArgs.Advertisement.ManufacturerData)
            {
                if (manufacturerData.CompanyId != QcyUuids.CompanyId)
                {
                    continue;
                }

                var advertisement = QcyAdvertisement.Parse(ReadBuffer(manufacturerData.Data));
                if (advertisement is null)
                {
                    continue;
                }

                var name = eventArgs.Advertisement.LocalName;
                if (string.IsNullOrWhiteSpace(name))
                {
                    var model = QcyDeviceRegistry.FindByVendorId(advertisement.VendorId);
                    name = model?.DisplayName ?? $"QCY device ({advertisement.VendorId})";
                }

                var info = new BluetoothDeviceInfo(
                    QcyAdvertisement.FormatAddress(eventArgs.BluetoothAddress),
                    name,
                    eventArgs.BluetoothAddress,
                    advertisement.ControlAddress,
                    advertisement.OtherAddress,
                    advertisement.VendorId,
                    eventArgs.RawSignalStrengthInDBm,
                    advertisement.LeftBattery,
                    advertisement.RightBattery,
                    advertisement.CaseBattery,
                    advertisement.LeftCharging,
                    advertisement.RightCharging,
                    advertisement.CaseCharging,
                    eventArgs.Timestamp);

                devices.AddOrUpdate(eventArgs.BluetoothAddress, info, (_, previous) =>
                    info with
                    {
                        Name = string.IsNullOrWhiteSpace(info.Name) ? previous.Name : info.Name,
                        ControlAddress = info.ControlAddress ?? previous.ControlAddress,
                        OtherAddress = info.OtherAddress ?? previous.OtherAddress,
                    });
            }
        }

        watcher.Received += OnReceived;
        try
        {
            watcher.Start();
            await Task.Delay(duration, cancellationToken);
        }
        finally
        {
            watcher.Stop();
            watcher.Received -= OnReceived;
        }

        return devices.Values
            .OrderByDescending(device => QcyDeviceRegistry.IsSupported(device.VendorId))
            .ThenByDescending(device => device.SignalStrength)
            .ToArray();
    }

    public async Task<IBluetoothDeviceConnection> ConnectAsync(
        BluetoothDeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        var candidateAddresses = new List<ulong>();
        if (device.ControlAddress.HasValue)
        {
            candidateAddresses.Add(device.ControlAddress.Value);
        }

        if (device.BluetoothAddress != 0)
        {
            candidateAddresses.Add(device.BluetoothAddress);
            candidateAddresses.Add(device.BluetoothAddress ^ 1);
            candidateAddresses.Add(device.BluetoothAddress + 1);
        }

        if (device.OtherAddress.HasValue)
        {
            candidateAddresses.Add(device.OtherAddress.Value);
        }

        var candidates = candidateAddresses.Distinct().ToArray();

        var diagnostics = new List<string>();
        foreach (var address in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BluetoothLEDevice? bluetoothDevice = null;
            try
            {
                bluetoothDevice = await GetBluetoothLEDeviceAsync(address, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (bluetoothDevice is null)
                {
                    diagnostics.Add($"{QcyAdvertisement.FormatAddress(address)}: BLE device unavailable");
                    continue;
                }

                var service = await FindQcyServiceAsync(bluetoothDevice, cancellationToken, diagnostics, address);
                if (service is null)
                {
                    bluetoothDevice.Dispose();
                    continue;
                }

                return await WindowsBluetoothDeviceConnection.CreateAsync(
                    bluetoothDevice,
                    service,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                bluetoothDevice?.Dispose();
                diagnostics.Add($"{QcyAdvertisement.FormatAddress(address)}: {exception.Message}");
            }
        }

        throw new InvalidOperationException(
            diagnostics.Count == 0
                ? "No QCY control address was advertised."
                : string.Join(Environment.NewLine, diagnostics));
    }

    private static async Task<BluetoothLEDevice?> GetBluetoothLEDeviceAsync(
        ulong address,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address, BluetoothAddressType.Public);
            if (device is not null)
            {
                return device;
            }
        }
        catch
        {
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address, BluetoothAddressType.Random);
            if (device is not null)
            {
                return device;
            }
        }
        catch
        {
        }

        try
        {
            return await BluetoothLEDevice.FromBluetoothAddressAsync(address);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<GattDeviceService?> FindQcyServiceAsync(
        BluetoothLEDevice bluetoothDevice,
        CancellationToken cancellationToken,
        List<string> diagnostics,
        ulong address)
    {
        foreach (var serviceUuid in new[] { QcyUuids.MainService, QcyUuids.SecondaryService })
        {
            try
            {
                var directResult = await bluetoothDevice.GetGattServicesForUuidAsync(
                    serviceUuid,
                    BluetoothCacheMode.Uncached);
                cancellationToken.ThrowIfCancellationRequested();

                if (directResult.Status == GattCommunicationStatus.Success && directResult.Services.Count > 0)
                {
                    for (var i = 1; i < directResult.Services.Count; i++)
                    {
                        directResult.Services[i].Dispose();
                    }

                    return directResult.Services[0];
                }
            }
            catch
            {
            }
        }

        GattDeviceServicesResult? allServicesResult = null;
        try
        {
            allServicesResult = await bluetoothDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
        }

        if (allServicesResult is null || allServicesResult.Status != GattCommunicationStatus.Success || allServicesResult.Services.Count == 0)
        {
            try
            {
                allServicesResult = await bluetoothDevice.GetGattServicesAsync(BluetoothCacheMode.Cached);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch
            {
            }
        }

        if (allServicesResult is null || allServicesResult.Status != GattCommunicationStatus.Success || allServicesResult.Services.Count == 0)
        {
            diagnostics.Add($"{QcyAdvertisement.FormatAddress(address)}: A001/A002 service not found ({allServicesResult?.Status.ToString() ?? "Failed"})");
            return null;
        }

        var services = allServicesResult.Services;
        var foundUuids = services.Select(s => s.Uuid.ToString()).ToArray();

        var matchedIndex = -1;
        for (var i = 0; i < services.Count; i++)
        {
            var uuid = services[i].Uuid;
            if (uuid == QcyUuids.MainService ||
                uuid == QcyUuids.SecondaryService ||
                uuid.ToString().StartsWith("0000a001", StringComparison.OrdinalIgnoreCase) ||
                uuid.ToString().StartsWith("0000a002", StringComparison.OrdinalIgnoreCase))
            {
                matchedIndex = i;
                break;
            }
        }

        if (matchedIndex < 0)
        {
            for (var i = 0; i < services.Count; i++)
            {
                try
                {
                    var chars = await services[i].GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                    if (chars.Status == GattCommunicationStatus.Success &&
                        chars.Characteristics.Any(c => c.Uuid == QcyUuids.Command || c.Uuid == QcyUuids.Notification))
                    {
                        matchedIndex = i;
                        break;
                    }
                }
                catch
                {
                }
            }
        }

        if (matchedIndex >= 0)
        {
            var selected = services[matchedIndex];
            for (var i = 0; i < services.Count; i++)
            {
                if (i != matchedIndex)
                {
                    services[i].Dispose();
                }
            }

            return selected;
        }

        foreach (var s in services)
        {
            s.Dispose();
        }

        diagnostics.Add($"{QcyAdvertisement.FormatAddress(address)}: Services found: [{string.Join(", ", foundUuids)}], but none matched A001/A002");
        return null;
    }

    internal static byte[] ReadBuffer(IBuffer buffer)
    {
        using var reader = DataReader.FromBuffer(buffer);
        var value = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(value);
        return value;
    }

    private static async Task<BluetoothDeviceInfo?> CreateKnownDeviceAsync(
        string deviceId,
        string? fallbackName,
        CancellationToken cancellationToken)
    {
        using var bluetoothDevice = await BluetoothLEDevice.FromIdAsync(deviceId);
        cancellationToken.ThrowIfCancellationRequested();
        if (bluetoothDevice is null)
        {
            return null;
        }

        var matchedModel = QcyDeviceRegistry.FindByBluetoothName(bluetoothDevice.Name)
            ?? QcyDeviceRegistry.FindByBluetoothName(fallbackName);
        var name = string.IsNullOrWhiteSpace(bluetoothDevice.Name)
            ? fallbackName ?? matchedModel?.DisplayName ?? "QCY Earbuds"
            : bluetoothDevice.Name;
        var vendorId = matchedModel?.VendorIds.FirstOrDefault() ?? QcyUuids.N70BlackVendorId;

        return new BluetoothDeviceInfo(
            deviceId,
            name,
            bluetoothDevice.BluetoothAddress,
            null,
            null,
            vendorId,
            short.MinValue,
            0,
            0,
            0,
            false,
            false,
            false,
            DateTimeOffset.UtcNow);
    }

    private static byte? ReadByteProperty(DeviceInformation device, string propertyName)
    {
        if (!device.Properties.TryGetValue(propertyName, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            byte byteValue => byteValue,
            ushort ushortValue when ushortValue <= byte.MaxValue => (byte)ushortValue,
            uint uintValue when uintValue <= byte.MaxValue => (byte)uintValue,
            int intValue when intValue is >= byte.MinValue and <= byte.MaxValue => (byte)intValue,
            _ => null,
        };
    }

    private static bool ReadBooleanProperty(DeviceInformation device, string propertyName) =>
        device.Properties.TryGetValue(propertyName, out var value) && value is true;
}
