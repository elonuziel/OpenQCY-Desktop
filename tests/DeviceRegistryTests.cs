using OpenQCY_Desktop.Protocol;

namespace OpenQCY.Desktop.Tests;

[TestClass]
public sealed class DeviceRegistryTests
{
    [TestInitialize]
    public void Setup()
    {
        QcyDeviceRegistry.ResetToDefaults();
    }

    [TestMethod]
    public void BuiltInCatalogContainsN70T13AncAndT15Anc()
    {
        Assert.IsTrue(QcyDeviceRegistry.IsSupported(QcyUuids.N70BlackVendorId));
        Assert.IsTrue(QcyDeviceRegistry.IsSupported(QcyUuids.N70AlternateVendorId));
        Assert.IsTrue(QcyDeviceRegistry.IsSupported(QcyUuids.T13AncVendorId));
        Assert.IsTrue(QcyDeviceRegistry.IsSupported(QcyUuids.T13Anc2VendorId));
        Assert.IsTrue(QcyDeviceRegistry.IsSupported(QcyUuids.T15AncVendorId));

        var n70 = QcyDeviceRegistry.FindByVendorId(QcyUuids.N70BlackVendorId);
        Assert.IsNotNull(n70);
        Assert.AreEqual("QCY MeloBuds N70", n70.DisplayName);
        Assert.IsTrue(n70.Capabilities.SupportsLdac);
        Assert.IsTrue(n70.Capabilities.SupportsMultipoint);
        Assert.AreEqual(QcyWearDetectionProtocol.WearingDetection, n70.Capabilities.WearDetectionProtocol);
        Assert.HasCount(5, n70.Capabilities.SupportedAncScenes);

        var t13 = QcyDeviceRegistry.FindByVendorId(QcyUuids.T13AncVendorId);
        Assert.IsNotNull(t13);
        Assert.AreEqual("QCY T13 ANC", t13.DisplayName);
        Assert.IsFalse(t13.Capabilities.SupportsLdac);
        Assert.IsFalse(t13.Capabilities.SupportsMultipoint);
        Assert.AreEqual(QcyWearDetectionProtocol.Legacy, t13.Capabilities.WearDetectionProtocol);
        Assert.IsFalse(t13.Capabilities.SupportsAncScenes);

        var t15 = QcyDeviceRegistry.FindByVendorId(QcyUuids.T15AncVendorId);
        Assert.IsNotNull(t15);
        Assert.AreEqual("QCY Buds ANC (T15)", t15.DisplayName);
        Assert.IsFalse(t15.Capabilities.SupportsLdac);
        Assert.IsTrue(t15.Capabilities.SupportsMultipoint);
        Assert.AreEqual(QcyWearDetectionProtocol.Legacy, t15.Capabilities.WearDetectionProtocol);
        Assert.IsTrue(t15.Capabilities.SupportsAncScenes);
    }

    [TestMethod]
    [DataRow("QCY-T13 ANC", "t13-anc")]
    [DataRow("QCY-T13ANC", "t13-anc")]
    [DataRow("T13ANC", "t13-anc")]
    [DataRow("QCY T13 ANC2", "t13-anc-2")]
    [DataRow("QCY Buds ANC", "t15-anc")]
    [DataRow("QCY HT15", "t15-anc")]
    [DataRow("QCY MeloBuds N70", "n70")]
    [DataRow("HT18 Earbuds", "n70")]
    public void NameMatchingResolvesExpectedDevice(string bluetoothName, string expectedId)
    {
        var model = QcyDeviceRegistry.FindByBluetoothName(bluetoothName);
        Assert.IsNotNull(model, $"Model should be found for '{bluetoothName}'");
        Assert.AreEqual(expectedId, model.Id);
    }

    [TestMethod]
    public void LooksLikeSupportedRecognizesAnyQcyDeviceName()
    {
        Assert.IsTrue(QcyDeviceRegistry.LooksLikeSupported("QCY-T13 ANC"));
        Assert.IsTrue(QcyDeviceRegistry.LooksLikeSupported("QCY T13"));
        Assert.IsTrue(QcyDeviceRegistry.LooksLikeSupported("Headphones QCY-T13 ANC"));
        Assert.IsFalse(QcyDeviceRegistry.LooksLikeSupported("Sony WH-1000XM4"));
        Assert.IsFalse(QcyDeviceRegistry.LooksLikeSupported(null));
    }

    [TestMethod]
    public void RegisterAllowsDeveloperToAddModelAtRuntime()
    {
        var custom = new QcyDeviceDefinition
        {
            Id = "custom-ht99",
            DisplayName = "QCY HT99 Pro",
            VendorIds = [44556],
            BluetoothNamePatterns = ["HT99", "QCY HT99"],
            Capabilities = new QcyDeviceCapabilities
            {
                SupportsLdac = true,
                SupportsMultipoint = true,
            },
        };

        QcyDeviceRegistry.Register(custom);

        Assert.IsTrue(QcyDeviceRegistry.IsSupported(44556));
        var found = QcyDeviceRegistry.FindByVendorId(44556);
        Assert.IsNotNull(found);
        Assert.AreEqual("QCY HT99 Pro", found.DisplayName);
        Assert.IsTrue(found.Capabilities.SupportsLdac);

        var byName = QcyDeviceRegistry.FindByBluetoothName("QCY HT99 Pro Edition");
        Assert.IsNotNull(byName);
        Assert.AreEqual("custom-ht99", byName.Id);
    }

    [TestMethod]
    public void LoadCustomJsonParsesDefinitionsCorrectly()
    {
        var json = """
        [
          {
            "id": "json-model",
            "displayName": "QCY JSON Buds",
            "vendorIds": [55112],
            "bluetoothNamePatterns": ["JSON Buds"],
            "capabilities": {
              "supportsLdac": false,
              "supportsMultipoint": true,
              "wearDetectionProtocol": "Legacy"
            }
          }
        ]
        """;

        var success = QcyDeviceRegistry.LoadCustomJson(json);

        Assert.IsTrue(success);
        Assert.IsTrue(QcyDeviceRegistry.IsSupported(55112));
        var model = QcyDeviceRegistry.FindByVendorId(55112);
        Assert.IsNotNull(model);
        Assert.AreEqual("QCY JSON Buds", model.DisplayName);
        Assert.IsFalse(model.Capabilities.SupportsLdac);
        Assert.IsTrue(model.Capabilities.SupportsMultipoint);
        Assert.AreEqual(QcyWearDetectionProtocol.Legacy, model.Capabilities.WearDetectionProtocol);
    }

    [TestMethod]
    public void CreateFallbackReturnsSafeBaselineForUnknownModel()
    {
        var fallback = QcyDeviceRegistry.CreateFallback(12345, "My Custom QCY");

        Assert.IsNotNull(fallback);
        Assert.AreEqual("My Custom QCY", fallback.DisplayName);
        Assert.IsFalse(fallback.Capabilities.SupportsLdac);
        Assert.IsFalse(fallback.Capabilities.SupportsMultipoint);
        Assert.IsFalse(fallback.Capabilities.SupportsWindDetection);
        Assert.IsTrue(fallback.Capabilities.SupportsGameMode);
    }

    [TestMethod]
    public void QcyUuidsMaintainsCompatibility()
    {
        Assert.IsTrue(QcyUuids.IsN70(23872));
        Assert.IsTrue(QcyUuids.IsN70(23877));
        Assert.IsFalse(QcyUuids.IsN70(18290));

        Assert.IsTrue(QcyUuids.IsSupported(23872));
        Assert.IsTrue(QcyUuids.IsSupported(18290));
        Assert.IsTrue(QcyUuids.IsSupported(32882));
        Assert.IsFalse(QcyUuids.IsSupported(9999));
    }
}

