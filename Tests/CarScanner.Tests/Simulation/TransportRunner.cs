using CarScanner.Services;
using CarScanner.Services.OBD;

namespace CarScanner.Tests.Simulation;

public enum SimTransport
{
    /// <summary>The simulator is the link itself (a whole reply per call).</summary>
    Direct,

    /// <summary>The real Wi-Fi transport over a loopback TCP socket; replies arrive in small pieces.</summary>
    Wifi,

    /// <summary>The real BLE code of BluetoothService over a fake GATT characteristic; replies arrive as 7-byte notifications.</summary>
    Ble
}

/// <summary>Runs the same protocol-level test body over each way the app can reach an adapter.</summary>
public static class TransportRunner
{
    public static IEnumerable<object[]> All => new[]
    {
        new object[] { SimTransport.Direct },
        new object[] { SimTransport.Wifi },
        new object[] { SimTransport.Ble }
    };

    public static async Task<T> RunAsync<T>(SimTransport transport, IBluetoothService adapter, Func<AdapterRig, Task<T>> body)
    {
        switch (transport)
        {
            case SimTransport.Wifi:
            {
                await using var server = await TcpSimulatedElm327.StartAsync(adapter);
                server.SegmentSize = 4;
                server.SegmentDelayMilliseconds = 1;
                var wifi = new WifiObdTransport();
                await wifi.ConnectAsync(ObdTransportConnectionOptions.ForWifi("127.0.0.1", server.Port));
                try
                {
                    return await body(await AdapterRigs.CreateAsync(new WifiTransportBridge(wifi), connect: false));
                }
                finally
                {
                    await wifi.DisconnectAsync();
                }
            }

            case SimTransport.Ble:
            {
                var ble = await BleHarness.CreateAsync(adapter, gatt => gatt.FragmentSize = 7);
                return await body(await AdapterRigs.CreateAsync(ble.Service, connect: false));
            }

            default:
                return await body(await AdapterRigs.CreateAsync(adapter));
        }
    }
}
