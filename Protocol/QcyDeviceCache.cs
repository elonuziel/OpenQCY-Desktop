using System.Text.Json;

namespace OpenQCY_Desktop.Protocol;

public sealed class QcyDeviceCacheItem
{
    public string Name { get; set; } = string.Empty;
    public ushort VendorId { get; set; }
    public ulong? ControlAddress { get; set; }
    public ulong? ClassicAddress { get; set; }
    public ulong? AdvertisingAddress { get; set; }
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class QcyDeviceCache
{
    private static readonly Lazy<QcyDeviceCache> DefaultInstance = new(() => new QcyDeviceCache());
    public static QcyDeviceCache Instance => DefaultInstance.Value;

    private readonly object _lock = new();
    private readonly string? _filePath;
    private readonly List<QcyDeviceCacheItem> _items = [];

    public QcyDeviceCache(string? customPath = null)
    {
        _filePath = customPath ?? GetDefaultCacheFilePath();
        Load();
    }

    public IReadOnlyList<QcyDeviceCacheItem> GetAll()
    {
        lock (_lock)
        {
            return _items.ToList();
        }
    }

    public ulong? FindControlAddress(ulong? address, string? name = null)
    {
        lock (_lock)
        {
            if (address.HasValue && address.Value != 0)
            {
                var match = _items.FirstOrDefault(item =>
                    item.ClassicAddress == address.Value ||
                    item.ControlAddress == address.Value ||
                    item.AdvertisingAddress == address.Value);

                if (match?.ControlAddress is { } control && control != 0)
                {
                    return control;
                }
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                var match = _items.FirstOrDefault(item =>
                    string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

                if (match?.ControlAddress is { } control && control != 0)
                {
                    return control;
                }
            }

            return null;
        }
    }

    public void Record(
        string? name,
        ushort vendorId,
        ulong? controlAddress,
        ulong? classicAddress = null,
        ulong? advertisingAddress = null)
    {
        if ((controlAddress is null or 0) && (classicAddress is null or 0) && (advertisingAddress is null or 0))
        {
            return;
        }

        lock (_lock)
        {
            var existing = _items.FirstOrDefault(item =>
                (controlAddress.HasValue && controlAddress.Value != 0 && item.ControlAddress == controlAddress.Value) ||
                (classicAddress.HasValue && classicAddress.Value != 0 && item.ClassicAddress == classicAddress.Value) ||
                (advertisingAddress.HasValue && advertisingAddress.Value != 0 && item.AdvertisingAddress == advertisingAddress.Value) ||
                (!string.IsNullOrWhiteSpace(name) && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)));

            if (existing is not null)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    existing.Name = name;
                }

                if (vendorId != 0)
                {
                    existing.VendorId = vendorId;
                }

                existing.ControlAddress = controlAddress ?? existing.ControlAddress;
                existing.ClassicAddress = classicAddress ?? existing.ClassicAddress;
                existing.AdvertisingAddress = advertisingAddress ?? existing.AdvertisingAddress;
                existing.LastSeen = DateTimeOffset.UtcNow;
            }
            else
            {
                _items.Add(new QcyDeviceCacheItem
                {
                    Name = name ?? string.Empty,
                    VendorId = vendorId,
                    ControlAddress = controlAddress,
                    ClassicAddress = classicAddress,
                    AdvertisingAddress = advertisingAddress,
                    LastSeen = DateTimeOffset.UtcNow,
                });
            }

            Save();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
            Save();
        }
    }

    private void Load()
    {
        if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<List<QcyDeviceCacheItem>>(json);
            if (loaded is not null)
            {
                lock (_lock)
                {
                    _items.Clear();
                    _items.AddRange(loaded);
                }
            }
        }
        catch
        {
            // Transient read/parse errors should not crash transport
        }
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_filePath))
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Writing cache errors should never disrupt core operations
        }
    }

    private static string? GetDefaultCacheFilePath()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                return Path.Combine(localAppData, "OpenQCY Desktop", "device-cache.json");
            }
        }
        catch
        {
        }

        try
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device-cache.json");
        }
        catch
        {
            return null;
        }
    }
}

