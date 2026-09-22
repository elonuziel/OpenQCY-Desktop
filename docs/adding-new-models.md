# Adding Support for New QCY Earbud Models

OpenQCY Desktop is built on an extensible, capability-based device architecture. Adding support for new QCY earbud models does not require modifying UI pages or protocol framing logic.

---

## 1. Supported Models Out of the Box

The built-in device registry (`QcyDeviceRegistry`) includes verified definitions for:

| Model | Bluetooth Name(s) | Vendor IDs | ANC | LDAC | Multipoint | Wind Reduction | Custom EQ | Wear Detection |
| :--- | :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: |
| **MeloBuds N70** | `QCY MeloBuds N70`, `QCY N70` | `23872`, `23877` | Yes | Yes | Yes | Yes | Yes | Yes (Modern) |
| **T13 ANC** | `QCY T13 ANC`, `QCY-T13 ANC` | `18290` | Yes | No | No | No | Yes | No |
| **T13 ANC 2** | `QCY T13 ANC 2`, `QCY-T13 ANC 2` | `18294`, `18295` | Yes | No | No | No | Yes | No |
| **T15 ANC / Buds ANC** | `QCY Buds ANC`, `QCY HT15`, `QCY T15 ANC` | `32882` | Yes | No | Yes | Yes | Yes | No |

---

## 2. Capturing Diagnostics for a New Model

Before adding a model, you need to identify its advertised Vendor ID and supported protocol capabilities.

1. Open the earbud charging case and keep the earbuds near your PC.
2. Build and run the diagnostic probe from the command line:
   ```powershell
   dotnet run --project Diagnostics/OpenQCY.Probe/OpenQCY.Probe.csproj
   ```
3. Observe the output:
   - **Advertisement banner**:
     ```text
     Found: QCY Buds ANC · vendor 32882 (0x8072) · RSSI -58 dBm · L 90% / R 90% / case 100%
     ```
   - **Characteristic discovery**: Note whether characteristics like `0000000D` (direct touch configuration) or `00001001`/`00001002` (framed command channel) are exposed.
   - **Protocol trace**: Watch which query opcodes succeed and which time out.

---

## 3. Testing via `custom-devices.json` (No Compilation Required)

You can test a new model immediately in both the diagnostic probe and the desktop app without recompiling.

### In the Desktop Application

Create or edit the custom devices JSON file in your local app data folder:
`%LocalAppData%\OpenQCY Desktop\custom-devices.json`

Example definition:
```json
[
  {
    "modelId": "qcy-ht07",
    "displayName": "QCY ArcsBuds (HT07)",
    "bluetoothNames": [
      "QCY ArcsBuds",
      "QCY HT07"
    ],
    "vendorIds": [
      18285
    ],
    "wearDetectionProtocol": "None",
    "capabilities": {
      "supportsNoiseControl": true,
      "supportsLdac": false,
      "supportsMultipoint": false,
      "supportsWindNoiseReduction": false,
      "supportsEqualizerPresets": false,
      "supportsCustomEqualizer": true,
      "supportsWearDetection": false,
      "supportsGameMode": true
    }
  }
]
```

When OpenQCY Desktop starts, `MainPageViewModel` automatically scans for `%LocalAppData%\OpenQCY Desktop\custom-devices.json` and loads any valid definitions into `QcyDeviceRegistry`.

### In the Diagnostic Probe

You can pass the definition directly to the probe using the `--custom-model` argument:
```powershell
dotnet run --project Diagnostics/OpenQCY.Probe/OpenQCY.Probe.csproj -- --custom-model path/to/my-device.json
```

---

## 4. Contributing a Built-in Model in C#

To add a model directly to OpenQCY Desktop's codebase:

### 1. Add Vendor ID Constants in [`Protocol/QcyUuids.cs`](file:///home/elonu/github/OpenQCY-Desktop/Protocol/QcyUuids.cs)
```csharp
public const ushort Ht07VendorId = 18285;
```

### 2. Register Definition in [`Protocol/QcyDeviceRegistry.cs`](file:///home/elonu/github/OpenQCY-Desktop/Protocol/QcyDeviceRegistry.cs)
Add an entry to the `BuiltInDefinitions` collection:
```csharp
new QcyDeviceDefinition
{
    ModelId = "qcy-ht07",
    DisplayName = "QCY ArcsBuds (HT07)",
    BluetoothNames = ["QCY ArcsBuds", "QCY HT07"],
    VendorIds = [QcyUuids.Ht07VendorId],
    WearDetectionProtocol = QcyWearDetectionProtocol.None,
    Capabilities = new QcyDeviceCapabilities
    {
        SupportsNoiseControl = true,
        SupportsLdac = false,
        SupportsMultipoint = false,
        SupportsWindNoiseReduction = false,
        SupportsEqualizerPresets = false,
        SupportsCustomEqualizer = true,
        SupportsWearDetection = false,
        SupportsGameMode = true
    }
},
```

### 3. Add Tests in [`tests/DeviceRegistryTests.cs`](file:///home/elonu/github/OpenQCY-Desktop/tests/DeviceRegistryTests.cs)
Add an assertion verifying vendor ID lookup and name resolution:
```csharp
[Fact]
public void Registry_ResolvesHt07ByVendorIdAndName()
{
    var model = QcyDeviceRegistry.Find(QcyUuids.Ht07VendorId);
    Assert.NotNull(model);
    Assert.Equal("QCY ArcsBuds (HT07)", model.DisplayName);
    Assert.False(model.Capabilities.SupportsLdac);
}
```

---

## 5. Important Capability & Safety Rules

- **Do Not Guess Unsupported Opcodes**: If a model does not support LDAC (`0x23`), Multipoint (`0x24`), or Wind Noise Reduction (`0x2A`), ensure the corresponding capability flags in `QcyDeviceCapabilities` are set to `false`. Querying unsupported opcodes results in a 2.5-second timeout per command, delaying connection readiness and battery retrieval.
- **ANC Opcode Compatibility**: QCY ANC models using company ID `0x521C` and service `0xA001` consistently respond to opcode `0x17` with a 3-byte payload (`[mode, subScene, noiseValue]`).
- **Touch Gestures**: Models that expose characteristic `0000000D` support direct key-action mapping. For models without this characteristic, touch function configuration is safely ignored without errors.

---

## 6. Running Tests

### Cross-Platform / Linux
You can test the protocol and device model registry on any operating system with the .NET 10 SDK:
```bash
dotnet test tests/OpenQCY.Desktop.Tests.csproj -c Release
```

### Windows
On Windows, the test suite multi-targets both `net10.0` and `net10.0-windows10.0.26100.0`, running both the platform-agnostic suite and Windows-specific tests:
```powershell
dotnet test tests/OpenQCY.Desktop.Tests.csproj -c Release
```

