using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenQCY_Desktop.Protocol;

public static class QcyDeviceRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly ConcurrentDictionary<string, QcyDeviceDefinition> ModelsById = new();

    static QcyDeviceRegistry()
    {
        ResetToDefaults();
    }

    public static IReadOnlyCollection<QcyDeviceDefinition> AllModels => ModelsById.Values.ToArray();

    public static void ResetToDefaults()
    {
        ModelsById.Clear();

        Register(new QcyDeviceDefinition
        {
            Id = "n70",
            DisplayName = "QCY MeloBuds N70",
            ModelCode = "HT18",
            VendorIds = [23872, 23877],
            BluetoothNamePatterns = ["N70", "HT18", "MeloBuds N70"],
            Capabilities = new QcyDeviceCapabilities
            {
                SupportsLdac = true,
                SupportsMultipoint = true,
                SupportsWindDetection = true,
                SupportsPromptVolume = true,
                SupportsAutoPowerOff = true,
                SupportsGameMode = true,
                SupportsSleepMode = true,
                WearDetectionProtocol = QcyWearDetectionProtocol.WearingDetection,
                SupportedAncScenes =
                [
                    QcyNoiseCancellationMode.Adaptive,
                    QcyNoiseCancellationMode.Indoor,
                    QcyNoiseCancellationMode.Commuting,
                    QcyNoiseCancellationMode.Noisy,
                    QcyNoiseCancellationMode.AntiWind,
                ],
            },
        });

        Register(new QcyDeviceDefinition
        {
            Id = "t13-anc",
            DisplayName = "QCY T13 ANC",
            ModelCode = "BH22T13A",
            VendorIds = [18290],
            BluetoothNamePatterns = ["T13 ANC", "QCY-T13 ANC", "T13ANC", "QCY-T13ANC"],
            Capabilities = new QcyDeviceCapabilities
            {
                SupportsLdac = false,
                SupportsMultipoint = false,
                SupportsWindDetection = false,
                SupportsPromptVolume = true,
                SupportsAutoPowerOff = true,
                SupportsGameMode = true,
                SupportsSleepMode = true,
                WearDetectionProtocol = QcyWearDetectionProtocol.None,
                SupportedAncScenes = [],
            },
        });

        Register(new QcyDeviceDefinition
        {
            Id = "t13-anc-2",
            DisplayName = "QCY T13 ANC 2",
            ModelCode = "BH23T13B",
            VendorIds = [18294, 18295],
            BluetoothNamePatterns = ["T13 ANC2", "T13 ANC 2", "QCY T13 ANC2"],
            Capabilities = new QcyDeviceCapabilities
            {
                SupportsLdac = false,
                SupportsMultipoint = false,
                SupportsWindDetection = false,
                SupportsPromptVolume = true,
                SupportsAutoPowerOff = true,
                SupportsGameMode = true,
                SupportsSleepMode = true,
                WearDetectionProtocol = QcyWearDetectionProtocol.Legacy,
                SupportedAncScenes = [],
            },
        });

        Register(new QcyDeviceDefinition
        {
            Id = "t15-anc",
            DisplayName = "QCY Buds ANC (T15)",
            ModelCode = "HT15",
            VendorIds = [32882],
            BluetoothNamePatterns = ["Buds ANC", "T15", "HT15", "QCY Buds ANC"],
            Capabilities = new QcyDeviceCapabilities
            {
                SupportsLdac = false,
                SupportsMultipoint = true,
                SupportsWindDetection = false,
                SupportsPromptVolume = true,
                SupportsAutoPowerOff = true,
                SupportsGameMode = true,
                SupportsSleepMode = true,
                WearDetectionProtocol = QcyWearDetectionProtocol.Legacy,
                SupportedAncScenes =
                [
                    QcyNoiseCancellationMode.Adaptive,
                    QcyNoiseCancellationMode.Indoor,
                    QcyNoiseCancellationMode.Commuting,
                    QcyNoiseCancellationMode.Noisy,
                    QcyNoiseCancellationMode.AntiWind,
                ],
            },
        });
    }

    public static void Register(QcyDeviceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ModelsById[definition.Id] = definition;
    }

    public static bool LoadCustomJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var trimmed = json.Trim();
            if (trimmed.StartsWith('['))
            {
                var definitions = JsonSerializer.Deserialize<List<QcyDeviceDefinition>>(json, JsonOptions);
                if (definitions is null)
                {
                    return false;
                }

                foreach (var definition in definitions)
                {
                    Register(definition);
                }

                return true;
            }

            var single = JsonSerializer.Deserialize<QcyDeviceDefinition>(json, JsonOptions);
            if (single is not null)
            {
                Register(single);
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    public static QcyDeviceDefinition? FindByVendorId(ushort vendorId) =>
        ModelsById.Values.FirstOrDefault(model => model.MatchesVendorId(vendorId));

    public static QcyDeviceDefinition? FindByBluetoothName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return ModelsById.Values
            .Select(model => (Model: model, Score: model.GetBluetoothNameMatchScore(name)))
            .Where(entry => entry.Score > 0)
            .OrderByDescending(entry => entry.Score)
            .Select(entry => entry.Model)
            .FirstOrDefault();
    }

    public static bool IsSupported(ushort vendorId) =>
        ModelsById.Values.Any(model => model.MatchesVendorId(vendorId));

    public static bool LooksLikeSupported(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return ModelsById.Values.Any(model => model.MatchesBluetoothName(name)) ||
               name.Contains("QCY", StringComparison.OrdinalIgnoreCase);
    }

    public static QcyDeviceDefinition CreateFallback(ushort vendorId, string? fallbackName = null)
    {
        var name = !string.IsNullOrWhiteSpace(fallbackName)
            ? fallbackName
            : $"QCY device ({vendorId})";

        return new QcyDeviceDefinition
        {
            Id = $"generic-{vendorId}",
            DisplayName = name,
            VendorIds = [vendorId],
            BluetoothNamePatterns = [name],
            Capabilities = new QcyDeviceCapabilities
            {
                SupportsLdac = false,
                SupportsMultipoint = false,
                SupportsWindDetection = false,
                SupportsPromptVolume = true,
                SupportsAutoPowerOff = true,
                SupportsGameMode = true,
                SupportsSleepMode = true,
                WearDetectionProtocol = QcyWearDetectionProtocol.Unknown,
                SupportedAncScenes = [],
            },
        };
    }
}

