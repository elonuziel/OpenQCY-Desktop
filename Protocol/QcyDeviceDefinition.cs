namespace OpenQCY_Desktop.Protocol;

public enum QcyWearDetectionProtocol
{
    Unknown,
    None,
    Legacy,
    WearingDetection,
}

public sealed record QcyDeviceCapabilities
{
    public bool SupportsLdac { get; init; }
    public bool SupportsMultipoint { get; init; }
    public bool SupportsWindDetection { get; init; }
    public bool SupportsPromptVolume { get; init; } = true;
    public bool SupportsAutoPowerOff { get; init; } = true;
    public bool SupportsGameMode { get; init; } = true;
    public bool SupportsSleepMode { get; init; } = true;
    public QcyWearDetectionProtocol WearDetectionProtocol { get; init; } = QcyWearDetectionProtocol.None;
    public IReadOnlyList<QcyNoiseCancellationMode> SupportedAncScenes { get; init; } = [];
    public IReadOnlyList<int> EqualizerBands { get; init; } =
        [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    public bool SupportsAncScenes => SupportedAncScenes.Count > 0;
}

public sealed record QcyDeviceDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? ModelCode { get; init; }
    public IReadOnlyList<ushort> VendorIds { get; init; } = [];
    public IReadOnlyList<string> BluetoothNamePatterns { get; init; } = [];
    public QcyDeviceCapabilities Capabilities { get; init; } = new();

    public bool MatchesVendorId(ushort vendorId) =>
        VendorIds.Contains(vendorId);

    public int GetBluetoothNameMatchScore(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return 0;
        }

        var maxMatchLength = 0;
        foreach (var pattern in BluetoothNamePatterns)
        {
            if (name.Contains(pattern, StringComparison.OrdinalIgnoreCase) && pattern.Length > maxMatchLength)
            {
                maxMatchLength = pattern.Length;
            }
        }

        return maxMatchLength;
    }

    public bool MatchesBluetoothName(string? name) =>
        GetBluetoothNameMatchScore(name) > 0;
}

