using System;
using System.Collections.Generic;

namespace MPHCore.Models;

//public class BleDevice
//{
//    public string Name { get; set; } = string.Empty;
//    public string Address { get; set; } = string.Empty;
//    public int Rssi { get; set; }
//    public Dictionary<string, string> AdvertisementData { get; set; } = new();
//    public override string ToString() => $"{Name} ({Address}) RSSI: {Rssi}";
//}
/// <summary>
/// A device that can be connected to via BLE.
/// </summary>
public class BleDevice
{
    public BleDevice(string name, Guid deviceId, int rssi) => (Name, Id, Rssi) = (name, deviceId, rssi);
    public string Name { get; set; }
    public Guid Id { get; set; }
    public int Rssi { get; set; }

    public override string ToString()
    {
        return $"{Name}: {Id}: {Rssi}";
    }
}
