using CarScanner.Models;
using Microsoft.Extensions.Logging;
using Plugin.BLE;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using System.Text;

#if ANDROID
using Android.Bluetooth;
using Android.Content;
using Android.OS;
#endif

namespace CarScanner.Services.Bluetooth;

public sealed class BluetoothService : IBluetoothService, IDisposable
{
    private static readonly (Guid Service, Guid Write, Guid Notify)[] KnownUartProfiles =
    {
        (
            Guid.Parse("6e400001-b5a3-f393-e0a9-e50e24dcca9e"),
            Guid.Parse("6e400002-b5a3-f393-e0a9-e50e24dcca9e"),
            Guid.Parse("6e400003-b5a3-f393-e0a9-e50e24dcca9e")
        ),
        (
            Guid.Parse("0000ffe0-0000-1000-8000-00805f9b34fb"),
            Guid.Parse("0000ffe1-0000-1000-8000-00805f9b34fb"),
            Guid.Parse("0000ffe1-0000-1000-8000-00805f9b34fb")
        ),
        (
            Guid.Parse("0000fff0-0000-1000-8000-00805f9b34fb"),
            Guid.Parse("0000fff2-0000-1000-8000-00805f9b34fb"),
            Guid.Parse("0000fff1-0000-1000-8000-00805f9b34fb")
        )
    };

#if ANDROID

    private static readonly Java.Util.UUID SppUuid =
        Java.Util.UUID.FromString(
            "00001101-0000-1000-8000-00805F9B34FB")
        ?? throw new InvalidOperationException("Bluetooth SPP UUID could not be created.");

    private BluetoothSocket? _classicSocket;
    private Stream? _classicInput;
    private Stream? _classicOutput;

    private CancellationTokenSource? _classicReadCts;
    private Task? _classicReadTask;

    private ClassicDiscoveryReceiver? _classicReceiver;
    private bool _classicDiscoveryRegistered;

#endif

    private readonly IAdapter _adapter;
    private readonly IBluetoothLE _ble;
    private readonly ILogger<BluetoothService> _logger;

    private readonly SemaphoreSlim _commandLock = new(1, 1);

    private EventHandler<DeviceEventArgs>? _deviceDiscoveredHandler;

    private ICharacteristic? _writeCharacteristic;
    private ICharacteristic? _notifyCharacteristic;
    private IDevice? _connectedBleDevice;

    private readonly StringBuilder _responseBuffer = new();

    private TaskCompletionSource<string>? _pendingResponse;

    private CancellationTokenSource? _scanCts;

    private bool _disposed;

    // ---- Keeping the link alive ----------------------------------------
    // A slow or silent ECU is not a lost adapter. Commands that time out are
    // followed by a short adapter check; only when the adapter itself stops
    // answering several times in a row is the link treated as lost, and then
    // the app reconnects to the same adapter on its own.
    private int _failedAdapterChecks;
    private BleDeviceItem? _lastDevice;
    private volatile bool _userDisconnect;
    private int _reconnecting;
    private readonly SemaphoreSlim _connectGate = new(1, 1);

    /// <summary>
    /// Upper bound on a single connect attempt. Some BLE stacks and cheap ELM327 clones never answer
    /// <c>ConnectToDeviceAsync</c>/the classic <c>BluetoothSocket.Connect()</c> again once the attempt is
    /// interrupted (for example by the user leaving the page right after tapping Connect) — and the classic
    /// path's blocking socket call ignores cancellation entirely once started. Without this bound, such a
    /// stuck attempt holds <see cref="_connectGate"/> forever, so every later connect AND every disconnect
    /// (it needs the same kind of gate one layer up, in ObdConnectionService) hangs too: the whole OBD layer
    /// freezes app-wide and only a force-kill recovers. See <see cref="ConnectAsync"/>.
    /// </summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);

    /// <summary>True while the app is reconnecting after the link dropped.</summary>
    public bool IsReconnecting => Volatile.Read(ref _reconnecting) == 1;

    public bool IsConnected { get; private set; }

    public string ConnectedDeviceName { get; private set; } = string.Empty;

    public event EventHandler<bool>? ConnectionChanged;

    public BluetoothService(ILogger<BluetoothService> logger)
    {
        _logger = logger;

        _ble = CrossBluetoothLE.Current;
        _adapter = CrossBluetoothLE.Current.Adapter;

        _adapter.DeviceDisconnected += OnDeviceDisconnected;
        _adapter.DeviceConnectionLost += OnDeviceConnectionLost;
        _adapter.DeviceConnectionError += OnDeviceConnectionError;
        _ble.StateChanged += OnBluetoothStateChanged;
    }

    // =========================================================
    // SCAN
    // =========================================================

    public async Task StartScanAsync(
        Action<BleDeviceItem> onDeviceFound,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await EnsureBluetoothPermissionsAsync()
            .ConfigureAwait(false);

        if (!_ble.IsAvailable && !IsAndroidClassicAvailable())
        {
            throw new InvalidOperationException(
                "Bluetooth ამ მოწყობილობაზე მიუწვდომელია.");
        }

        if (_ble.IsAvailable &&
            !_ble.IsOn &&
            !IsAndroidClassicAvailable())
        {
            throw new InvalidOperationException(
                "ჩართე Bluetooth და სცადე თავიდან.");
        }

        await StopScanAsync()
            .ConfigureAwait(false);

        _scanCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        var seen =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        // -----------------------------------------------------
        // BLE
        // -----------------------------------------------------

        if (_ble.IsAvailable && _ble.IsOn)
        {
            _deviceDiscoveredHandler = async (_, e) =>
            {
                if (e.Device == null)
                    return;

                try
                {
                    var device = e.Device;

                    // First use the advertised name.
                    var item =
                        new BleDeviceItem
                        {
                            Device = device,
                            NameOverride = device.Name,
                            Rssi = device.Rssi
                        };

                    // Only show adapters that identify themselves as OBD/ELM
                    // devices. Generic BLE UART services are also used by
                    // lights, watches and other peripherals; displaying them
                    // produced unnamed duplicate cards and failed connections.
                    if (!item.LooksLikeObd)
                        return;

                    if (seen.Add(item.Id))
                    {
                        MainThread.BeginInvokeOnMainThread(
                            () => onDeviceFound(item));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(
                        ex,
                        "BLE device inspection failed.");
                }
            };

            _adapter.DeviceDiscovered +=
                _deviceDiscoveredHandler;

            try
            {
                _ = _adapter
                    .StartScanningForDevicesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "BLE scan could not start.");
            }
        }

#if ANDROID

        // -----------------------------------------------------
        // CLASSIC BLUETOOTH
        // -----------------------------------------------------

        await StartClassicDiscoveryAsync(
            item =>
            {
                if (seen.Add(item.Id))
                {
                    MainThread.BeginInvokeOnMainThread(
                        () => onDeviceFound(item));
                }
            },
            _scanCts.Token)
            .ConfigureAwait(false);

#endif

        try
        {
            await Task.Delay(
                TimeSpan.FromSeconds(12),
                _scanCts.Token)
                .ConfigureAwait(false);
        }
        catch (System.OperationCanceledException)
        {
        }
        finally
        {
            await StopScanAsync()
                .ConfigureAwait(false);
        }
    }


    private async Task<bool> HasKnownObdUartProfileAsync(
        IDevice device)
    {
        try
        {
            var services =
                await device
                    .GetServicesAsync()
                    .ConfigureAwait(false);

            foreach (var profile in KnownUartProfiles)
            {
                if (services.Any(
                    s => s.Id == profile.Service))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Could not inspect BLE services.");
        }

        return false;
    }


    // =========================================================
    // STOP SCAN
    // =========================================================

    public async Task StopScanAsync()
    {
        _scanCts?.Cancel();

        if (_adapter.IsScanning)
        {
            try
            {
                await _adapter
                    .StopScanningForDevicesAsync()
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }

        if (_deviceDiscoveredHandler != null)
        {
            _adapter.DeviceDiscovered -=
                _deviceDiscoveredHandler;

            _deviceDiscoveredHandler = null;
        }

#if ANDROID
        StopClassicDiscovery();
#endif
    }


    // =========================================================
    // CONNECT
    // =========================================================

    public async Task ConnectAsync(
        BleDeviceItem item,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (item == null)
            throw new ArgumentNullException(nameof(item));

        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var timeoutSource = new CancellationTokenSource();
            var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            var connectTask = ConnectCoreAsync(item, linkedSource.Token);
            var delayTask = Task.Delay(ConnectTimeout);

            if (await Task.WhenAny(connectTask, delayTask).ConfigureAwait(false) != connectTask)
            {
                // The native call ignored cancellation (the classic socket path cannot be interrupted once
                // Connect() is blocking): stop waiting on it so the gate is released, and let it finish on its
                // own in the background instead of hanging this call — and the whole app with it — forever.
                // Disposal of the two sources is deferred to whenever that background attempt actually settles.
                timeoutSource.Cancel();
                ObserveAbandonedConnectAttempt(connectTask, item, linkedSource, timeoutSource);
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("OBD ადაპტერთან დაკავშირების ვადა ამოეწურა.");
            }

            try
            {
                await connectTask.ConfigureAwait(false);
            }
            finally
            {
                linkedSource.Dispose();
                timeoutSource.Dispose();
            }

            _lastDevice = item;
            _userDisconnect = false;
            _failedAdapterChecks = 0;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    /// <summary>
    /// After giving up on a connect attempt that ignored its timeout, the native call may still finish on its
    /// own in the background. This only observes the result so it never surfaces as an unobserved task
    /// exception, and releases the cancellation sources once it does — it deliberately does not try to undo a
    /// late success, since the device may simply have taken longer than <see cref="ConnectTimeout"/> and is now
    /// genuinely connected and usable.
    /// </summary>
    private void ObserveAbandonedConnectAttempt(Task connectTask, BleDeviceItem item, CancellationTokenSource linkedSource, CancellationTokenSource timeoutSource)
    {
        _ = connectTask.ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                    _logger.LogDebug(task.Exception, "Abandoned connect attempt to {Device} finished with an error after the timeout.", item.DisplayName);
                else if (task.IsCompletedSuccessfully)
                    _logger.LogWarning("Abandoned connect attempt to {Device} succeeded after the timeout had already given up on it.", item.DisplayName);

                linkedSource.Dispose();
                timeoutSource.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task ConnectCoreAsync(
        BleDeviceItem item,
        CancellationToken cancellationToken)
    {
        await StopScanAsync()
            .ConfigureAwait(false);

        if (IsConnected || _connectedBleDevice != null)
        {
            await DisconnectAsync()
                .ConfigureAwait(false);
        }

        if (item.IsClassicBluetooth)
        {
#if ANDROID
            await ConnectClassicAsync(
                item,
                cancellationToken)
                .ConfigureAwait(false);

            return;
#else
            throw new PlatformNotSupportedException(
                "Classic Bluetooth OBD adapters are supported on Android.");
#endif
        }

        if (item.Device == null)
        {
            throw new InvalidOperationException(
                "BLE მოწყობილობის მონაცემები არასწორია.");
        }

        try
        {
            await _adapter
                .ConnectToDeviceAsync(
                    item.Device,
                    new ConnectParameters(
                        forceBleTransport: true),
                    cancellationToken)
                .ConfigureAwait(false);

            // Keep a strong reference to the active peripheral on iOS and use
            // it to distinguish this connection from unrelated BLE events.
            _connectedBleDevice = item.Device;

            await DiscoverElm327CharacteristicsAsync(
                item.Device)
                .ConfigureAwait(false);

            IsConnected = true;

            ConnectedDeviceName =
                item.DisplayName;

            ConnectionChanged?.Invoke(
                this,
                true);
        }
        catch
        {
            var failedDevice = _connectedBleDevice ?? item.Device;
            MarkBleDisconnected(failedDevice, "OBD Bluetooth კავშირი ვერ დამყარდა.");

            try
            {
                await _adapter
                    .DisconnectDeviceAsync(failedDevice)
                    .ConfigureAwait(false);
            }
            catch
            {
            }

            throw;
        }
    }


    // =========================================================
    // DISCONNECT
    // =========================================================

    public async Task DisconnectAsync()
    {
        // The user (or a new connection) ends this link: never auto-reconnect it.
        _userDisconnect = true;
        var connectedBleDevice = _connectedBleDevice;

        try
        {
            await StopScanAsync()
                .ConfigureAwait(false);
        }
        catch
        {
        }

#if ANDROID
        try
        {
            if (_classicSocket != null)
            {
                await DisconnectClassicAsync()
                    .ConfigureAwait(false);
            }
        }
        catch
        {
        }
#endif

        if (connectedBleDevice != null)
        {
            try
            {
                using var disconnectTimeout =
                    new CancellationTokenSource(TimeSpan.FromSeconds(3));

                await _adapter
                    .DisconnectDeviceAsync(
                        connectedBleDevice,
                        disconnectTimeout.Token)
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }

        MarkBleDisconnected(
            connectedBleDevice,
            "OBD Bluetooth კავშირი გათიშულია.");
    }


    // =========================================================
    // SEND COMMAND
    // =========================================================

    public async Task<string> SendCommandAsync(
        string command,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!IsConnected)
        {
            throw new InvalidOperationException(
                "OBD2 ადაპტერი არ არის დაკავშირებული.");
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException(
                "ბრძანება ცარიელია.",
                nameof(command));
        }

        await _commandLock
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
#if ANDROID

            if (_classicSocket != null)
            {
                var classicAnswer = await SendClassicCommandAsync(
                    command,
                    timeout ?? TimeSpan.FromSeconds(5),
                    cancellationToken)
                    .ConfigureAwait(false);
                _failedAdapterChecks = 0;
                return classicAnswer;
            }

#endif

            if (_writeCharacteristic == null)
            {
                throw new InvalidOperationException(
                    "BLE write characteristic ვერ მოიძებნა.");
            }

            await DrainStaleInputAsync()
                .ConfigureAwait(false);

            _pendingResponse =
                new TaskCompletionSource<string>(
                    TaskCreationOptions
                        .RunContinuationsAsynchronously);

            var isMonitor =
                CarScanner.Services.OBD.ObdMonitorCommand.TryParse(
                    command,
                    out var monitorMilliseconds,
                    out var monitorCommand);

            var payload =
                Encoding.ASCII.GetBytes(
                    (isMonitor ? monitorCommand : command).Trim() + "\r");

            await WriteBlePayloadAsync(
                payload,
                cancellationToken)
                .ConfigureAwait(false);

            if (isMonitor)
            {
                // Collect broadcast frames, then stop monitoring with a carriage return.
                await Task.WhenAny(
                        _pendingResponse.Task,
                        Task.Delay(monitorMilliseconds, cancellationToken))
                    .ConfigureAwait(false);

                if (!_pendingResponse.Task.IsCompleted)
                {
                    await WriteBlePayloadAsync(
                        Encoding.ASCII.GetBytes("\r"),
                        cancellationToken)
                        .ConfigureAwait(false);
                }

                var stopped =
                    await Task.WhenAny(
                        _pendingResponse.Task,
                        Task.Delay(2500, cancellationToken))
                        .ConfigureAwait(false);

                if (stopped == _pendingResponse.Task)
                    return await _pendingResponse.Task.ConfigureAwait(false);

                lock (_responseBuffer)
                {
                    var partial = _responseBuffer.ToString();
                    _responseBuffer.Clear();
                    return partial;
                }
            }

            var timeoutTask =
                Task.Delay(
                    timeout ?? TimeSpan.FromSeconds(5),
                    cancellationToken);

            var completed =
                await Task.WhenAny(
                    _pendingResponse.Task,
                    timeoutTask)
                    .ConfigureAwait(false);

            if (completed == timeoutTask)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                throw new TimeoutException(
                    $"OBD2 ადაპტერმა არ უპასუხა: {command}");
            }

            var answer = await _pendingResponse.Task
                .ConfigureAwait(false);
            _failedAdapterChecks = 0;
            return answer;
        }
        catch (TimeoutException)
        {
            // The ECU (not necessarily the adapter) did not answer in time.
            await CheckAdapterAfterFailureAsync()
                .ConfigureAwait(false);
            throw;
        }
        catch (System.OperationCanceledException) when (IsConnected)
        {
            // The caller gave up while the adapter may still be answering:
            // stop it and discard the late reply so the next command is not
            // paired with this one's answer. The link itself is fine.
            try
            {
                await ResyncAfterFailureAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception resyncError)
            {
                _logger.LogDebug(resyncError, "Resync after a cancelled command failed.");
            }
            throw;
        }
        catch (Exception ex) when (
            _connectedBleDevice != null &&
            IsConnected &&
            ex is not System.OperationCanceledException &&
            ex is not IOException)
        {
            // A single failed BLE write is often a short radio hiccup.
            await CheckAdapterAfterFailureAsync()
                .ConfigureAwait(false);
            throw;
        }
        finally
        {
#if IOS
            // Cheap BLE ELM327 adapters can drop an iPhone connection when
            // commands are started back-to-back with no radio quiet period.
            if (IsConnected)
            {
                await Task.Delay(20)
                    .ConfigureAwait(false);
            }
#endif
            _pendingResponse = null;
            _commandLock.Release();
        }
    }


    // =========================================================
    // BLE CHARACTERISTICS
    // =========================================================

    private async Task WriteBlePayloadAsync(
        byte[] payload,
        CancellationToken token)
    {
        if (_writeCharacteristic == null)
        {
            throw new InvalidOperationException(
                "BLE write characteristic ვერ მოიძებნა.");
        }

        const int chunkSize = 20;

        for (
            var offset = 0;
            offset < payload.Length;
            offset += chunkSize)
        {
            token.ThrowIfCancellationRequested();

            var len =
                Math.Min(
                    chunkSize,
                    payload.Length - offset);

            await _writeCharacteristic
                .WriteAsync(
                    payload
                        .AsMemory(
                            offset,
                            len)
                        .ToArray())
                .ConfigureAwait(false);

            if (offset + len < payload.Length)
            {
                await Task.Delay(
                    5,
                    token)
                    .ConfigureAwait(false);
            }
        }
    }


    private async Task DiscoverElm327CharacteristicsAsync(
        IDevice device)
    {
        var services =
            await device
                .GetServicesAsync()
                .ConfigureAwait(false);

        foreach (var profile in KnownUartProfiles)
        {
            var service =
                services.FirstOrDefault(
                    s => s.Id == profile.Service);

            if (service == null)
                continue;

            var chars =
                await service
                    .GetCharacteristicsAsync()
                    .ConfigureAwait(false);

            var write =
                chars.FirstOrDefault(
                    c =>
                        c.Id == profile.Write &&
                        c.CanWrite);

            var notify =
                chars.FirstOrDefault(
                    c =>
                        c.Id == profile.Notify &&
                        c.CanUpdate);

            if (write != null &&
                notify != null)
            {
                await AttachCharacteristicsAsync(
                    write,
                    notify)
                    .ConfigureAwait(false);

                return;
            }
        }

        ICharacteristic? writeFallback = null;
        ICharacteristic? notifyFallback = null;

        foreach (var service in services)
        {
            var chars =
                await service
                    .GetCharacteristicsAsync()
                    .ConfigureAwait(false);

            notifyFallback ??=
                chars.FirstOrDefault(
                    c => c.CanUpdate);

            writeFallback ??=
                chars.FirstOrDefault(
                    c => c.CanWrite);
        }

        if (writeFallback == null ||
            notifyFallback == null)
        {
            throw new InvalidOperationException(
                "ეს BLE მოწყობილობა OBD-II ადაპტერად ვერ დადასტურდა.");
        }

        await AttachCharacteristicsAsync(
            writeFallback,
            notifyFallback)
            .ConfigureAwait(false);
    }


    private async Task AttachCharacteristicsAsync(
        ICharacteristic write,
        ICharacteristic notify)
    {
        DetachCharacteristics();

        _writeCharacteristic = write;
        _notifyCharacteristic = notify;

        _notifyCharacteristic.ValueUpdated +=
            OnNotifyValueUpdated;

        await _notifyCharacteristic
            .StartUpdatesAsync()
            .ConfigureAwait(false);
    }


    private void OnNotifyValueUpdated(
        object? sender,
        CharacteristicUpdatedEventArgs e)
    {
        var bytes =
            e.Characteristic.Value;

        if (bytes == null ||
            bytes.Length == 0)
            return;

        var chunk =
            Encoding.ASCII.GetString(bytes);

        lock (_responseBuffer)
        {
            _responseBuffer.Append(chunk);

            if (!chunk.Contains('>'))
                return;

            var response =
                _responseBuffer.ToString();

            _responseBuffer.Clear();

            _pendingResponse?
                .TrySetResult(response);
        }
    }

#if !ANDROID
    private static bool IsAndroidClassicAvailable() => false;
#endif

#if ANDROID

    // =========================================================
    // CLASSIC BLUETOOTH
    // =========================================================

    private static bool IsAndroidClassicAvailable()
    {
        try
        {
            return GetAndroidBluetoothAdapter()?
                .IsEnabled == true;
        }
        catch
        {
            return false;
        }
    }


    private async Task StartClassicDiscoveryAsync(
        Action<BleDeviceItem> onFound,
        CancellationToken token)
    {
        var adapter =
            GetAndroidBluetoothAdapter();

        if (adapter == null ||
            !adapter.IsEnabled)
            return;

        // Discover only devices currently advertising nearby.  Listing every
        // device ever paired with Android made headphones and old phones look
        // like live OBD adapters even when they were nowhere nearby.
        if (!adapter.IsDiscovering)
        {
            _classicReceiver =
                new ClassicDiscoveryReceiver(
                    device =>
                    {
                        if (!LooksLikePossibleObd(
                                device.Name))
                            return;

                        onFound(
                            new BleDeviceItem
                            {
                                ClassicAddress =
                                    device.Address,

                                NameOverride =
                                    device.Name,

                                Rssi = -60
                            });
                    });

            var filter =
                new IntentFilter(
                    BluetoothDevice.ActionFound);

            filter.AddAction(
                BluetoothAdapter
                    .ActionDiscoveryFinished);

            Android.App.Application.Context
                .RegisterReceiver(
                    _classicReceiver,
                    filter);

            _classicDiscoveryRegistered =
                true;

            try
            {
                adapter.StartDiscovery();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Classic Bluetooth discovery failed.");
            }
        }

        try
        {
            await Task.Delay(
                100,
                token)
                .ConfigureAwait(false);
        }
        catch (System.OperationCanceledException)
        {
        }
    }


    private void StopClassicDiscovery()
    {
        try
        {
            GetAndroidBluetoothAdapter()?
                .CancelDiscovery();
        }
        catch
        {
        }

        if (_classicDiscoveryRegistered &&
            _classicReceiver != null)
        {
            try
            {
                Android.App.Application.Context
                    .UnregisterReceiver(
                        _classicReceiver);
            }
            catch
            {
            }
        }

        _classicDiscoveryRegistered =
            false;

        _classicReceiver =
            null;
    }


    private static bool LooksLikePossibleObd(
        string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var n =
            name.Trim().ToUpperInvariant();

        string[] obdNames =
        {
            "OBD",
            "ELM",
            "V-LINK",
            "VLINK",
            "VGATE",
            "OBDLINK",
            "KONNWEI",
            "ICAR",
            "VEEPEAK",
            "LAUNCH",
            "THINKDIAG",
            "AUTOPHIX",
            "ANCEL",
            "FOXWELL",
            "TOPDON"
        };

        return obdNames.Any(
            x => n.Contains(x));
    }


    // =========================================================
    // CLASSIC CONNECT
    // =========================================================

    private async Task ConnectClassicAsync(
        BleDeviceItem item,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(
                item.ClassicAddress))
        {
            throw new InvalidOperationException(
                "Classic Bluetooth მისამართი არ არსებობს.");
        }

        var adapter =
            GetAndroidBluetoothAdapter()
            ?? throw new InvalidOperationException(
                "Bluetooth adapter ვერ მოიძებნა.");

        var remote =
            adapter.GetRemoteDevice(
                item.ClassicAddress);

        if (remote == null)
        {
            throw new InvalidOperationException(
                "OBD2 მოწყობილობა ვეღარ მოიძებნა.");
        }

        StopClassicDiscovery();

        await DisconnectClassicAsync()
            .ConfigureAwait(false);

        try
        {
            await EnsureClassicPairingAsync(remote, token)
                .ConfigureAwait(false);

            // Most inexpensive ELM327 adapters use an insecure SPP channel;
            // try it first and use the secure profile as a compatibility
            // fallback for adapters that require it.
            _classicSocket = remote.CreateInsecureRfcommSocketToServiceRecord(SppUuid);

            try
            {
                await Task.Run(() => _classicSocket!.Connect(), token)
                    .ConfigureAwait(false);
            }
            catch
            {
                try { _classicSocket?.Close(); } catch { }
                _classicSocket = remote.CreateRfcommSocketToServiceRecord(SppUuid);
                await Task.Run(() => _classicSocket!.Connect(), token)
                    .ConfigureAwait(false);
            }

            var connectedSocket = _classicSocket
                ?? throw new IOException("Classic Bluetooth socket was not created.");

            _classicInput = connectedSocket.InputStream;

            _classicOutput = connectedSocket.OutputStream;

            _classicReadCts =
                new CancellationTokenSource();

            _classicReadTask =
                Task.Run(
                    () => ClassicReadLoopAsync(
                        _classicReadCts.Token));

            IsConnected = true;

            ConnectedDeviceName =
                item.DisplayName;

            ConnectionChanged?.Invoke(
                this,
                true);
        }
        catch
        {
            await DisconnectClassicAsync()
                .ConfigureAwait(false);

            throw new InvalidOperationException(
                "Classic Bluetooth OBD2-თან დაკავშირება ვერ მოხერხდა. დარწმუნდი, რომ ადაპტერი ტელეფონის Bluetooth-ში დაპეარებულია.");
        }
    }


    private static async Task EnsureClassicPairingAsync(
        BluetoothDevice device,
        CancellationToken token)
    {
        if (device.BondState == Bond.Bonded)
            return;

        if (!device.CreateBond())
        {
            throw new InvalidOperationException(
                "ადაპტერის დაპეარება ვერ დაიწყო. ტელეფონის Bluetooth პარამეტრებში დააპეარე OBD ადაპტერი და სცადე თავიდან.");
        }

        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (device.BondState == Bond.Bonding && DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(250, token).ConfigureAwait(false);
        }

        if (device.BondState != Bond.Bonded)
        {
            throw new InvalidOperationException(
                "ადაპტერის დაპეარება არ დასრულდა. დაადასტურე Bluetooth-ის მოთხოვნა ტელეფონზე და სცადე თავიდან.");
        }
    }


    private async Task<string> SendClassicCommandAsync(
        string command,
        TimeSpan timeout,
        CancellationToken token)
    {
        if (_classicOutput == null)
        {
            throw new IOException(
                "Classic Bluetooth output stream მიუწვდომელია.");
        }

        await DrainStaleInputAsync()
            .ConfigureAwait(false);

        _pendingResponse =
            new TaskCompletionSource<string>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

        var isMonitor =
            CarScanner.Services.OBD.ObdMonitorCommand.TryParse(
                command,
                out var monitorMilliseconds,
                out var monitorCommand);

        var payload =
            Encoding.ASCII.GetBytes(
                (isMonitor ? monitorCommand : command).Trim() + "\r");

        await _classicOutput
            .WriteAsync(
                payload,
                0,
                payload.Length,
                token)
            .ConfigureAwait(false);

        await _classicOutput
            .FlushAsync(token)
            .ConfigureAwait(false);

        if (isMonitor)
        {
            // Collect broadcast frames, then stop monitoring with a carriage return.
            await Task.WhenAny(
                    _pendingResponse.Task,
                    Task.Delay(monitorMilliseconds, token))
                .ConfigureAwait(false);

            if (!_pendingResponse.Task.IsCompleted)
            {
                var stop = Encoding.ASCII.GetBytes("\r");
                await _classicOutput.WriteAsync(stop, 0, stop.Length, token).ConfigureAwait(false);
                await _classicOutput.FlushAsync(token).ConfigureAwait(false);
            }

            var stopped =
                await Task.WhenAny(
                    _pendingResponse.Task,
                    Task.Delay(2500, token))
                    .ConfigureAwait(false);

            if (stopped == _pendingResponse.Task)
                return await _pendingResponse.Task.ConfigureAwait(false);

            lock (_responseBuffer)
            {
                var partial = _responseBuffer.ToString();
                _responseBuffer.Clear();
                return partial;
            }
        }

        var timeoutTask =
            Task.Delay(
                timeout,
                token);

        var completed =
            await Task.WhenAny(
                _pendingResponse.Task,
                timeoutTask)
                .ConfigureAwait(false);

        if (completed == timeoutTask)
        {
            token.ThrowIfCancellationRequested();

            throw new TimeoutException(
                $"OBD2 ადაპტერმა არ უპასუხა: {command}");
        }

        return await _pendingResponse.Task
            .ConfigureAwait(false);
    }


    private async Task ClassicReadLoopAsync(
        CancellationToken token)
    {
        var input =
            _classicInput;

        if (input == null)
            return;

        var buffer =
            new byte[1024];

        try
        {
            while (!token.IsCancellationRequested)
            {
                var count =
                    await input.ReadAsync(
                        buffer,
                        0,
                        buffer.Length,
                        token)
                        .ConfigureAwait(false);

                if (count <= 0)
                {
                    // End of stream: the adapter closed the link.
                    _logger.LogInformation("Classic Bluetooth stream ended.");
                    _pendingResponse?
                        .TrySetException(
                            new IOException(
                                "OBD2 Bluetooth კავშირი გაითიშა."));
                    break;
                }

                var chunk =
                    Encoding.ASCII.GetString(
                        buffer,
                        0,
                        count);

                lock (_responseBuffer)
                {
                    _responseBuffer.Append(chunk);

                    if (!chunk.Contains('>'))
                        continue;

                    var response =
                        _responseBuffer.ToString();

                    _responseBuffer.Clear();

                    _pendingResponse?
                        .TrySetResult(response);
                }
            }
        }
        catch (System.OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Classic Bluetooth read loop ended.");

            _pendingResponse?
                .TrySetException(
                    new IOException(
                        "OBD2 Bluetooth კავშირი გაითიშა.",
                        ex));
        }

        // The socket closed by itself (adapter unplugged, out of range …).
        if (!token.IsCancellationRequested && !_userDisconnect)
            _ = Task.Run(HandleClassicLinkLostAsync);
    }

    private async Task HandleClassicLinkLostAsync()
    {
        var wasConnected = IsConnected;
        await DisconnectClassicAsync().ConfigureAwait(false);
        IsConnected = false;
        ConnectedDeviceName = string.Empty;
        if (wasConnected)
        {
            ConnectionChanged?.Invoke(this, false);
            StartAutoReconnect();
        }
    }


    private async Task DisconnectClassicAsync()
    {
        try
        {
            _classicReadCts?.Cancel();
        }
        catch
        {
        }

        try
        {
            // A plain await here can hang forever: Stream.ReadAsync on some Android InputStream bridges
            // blocks on the native read and only checks the token before starting, same as the classic
            // socket Connect() call above. Bounding this wait keeps a disconnect (and the ObdConnectionService
            // gate it runs under) from freezing the app when the adapter stops answering mid-read.
            if (_classicReadTask != null)
                await Task.WhenAny(_classicReadTask, Task.Delay(TimeSpan.FromSeconds(3)))
                    .ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            _classicInput?.Dispose();
        }
        catch
        {
        }

        try
        {
            _classicOutput?.Dispose();
        }
        catch
        {
        }

        try
        {
            _classicSocket?.Close();
            _classicSocket?.Dispose();
        }
        catch
        {
        }

        try
        {
            _classicReadCts?.Dispose();
        }
        catch
        {
        }

        _classicReadCts = null;
        _classicReadTask = null;
        _classicInput = null;
        _classicOutput = null;
        _classicSocket = null;
    }


    private static BluetoothAdapter? GetAndroidBluetoothAdapter()
    {
        var manager = Android.App.Application.Context.GetSystemService(
            Context.BluetoothService) as BluetoothManager;
        return manager?.Adapter;
    }

    private sealed class ClassicDiscoveryReceiver
        : BroadcastReceiver
    {
        private readonly Action<BluetoothDevice> _onFound;

        public ClassicDiscoveryReceiver(
            Action<BluetoothDevice> onFound)
        {
            _onFound = onFound;
        }

        public override void OnReceive(
            Context? context,
            Intent? intent)
        {
            if (intent?.Action !=
                BluetoothDevice.ActionFound)
                return;

            // Required for Android versions below API 33.
#pragma warning disable CA1422
            var device =
                (BluetoothDevice?)intent
                    .GetParcelableExtra(
                        BluetoothDevice.ExtraDevice);
#pragma warning restore CA1422

            if (device == null)
                return;

            if (LooksLikePossibleObd(
                    device.Name))
            {
                _onFound(device);
            }
        }
    }

#endif


    // =========================================================
    // PERMISSIONS
    // =========================================================

    private async Task EnsureBluetoothPermissionsAsync()
    {
#if ANDROID

        var bluetoothStatus =
            await Permissions
                .CheckStatusAsync<
                    Permissions.Bluetooth>()
                .ConfigureAwait(false);

        if (bluetoothStatus !=
            PermissionStatus.Granted)
        {
            bluetoothStatus =
                await Permissions
                    .RequestAsync<
                        Permissions.Bluetooth>()
                    .ConfigureAwait(false);
        }

        if (Build.VERSION.SdkInt >=
            BuildVersionCodes.S)
        {
            if (bluetoothStatus !=
                PermissionStatus.Granted)
            {
                throw new UnauthorizedAccessException(
                    "Bluetooth permission საჭიროა. " +
                    "აპის Settings-ში ჩართე Nearby devices / " +
                    "ახლომდებარე მოწყობილობები.");
            }
        }
        else
        {
            var locationStatus =
                await Permissions
                    .CheckStatusAsync<
                        Permissions.LocationWhenInUse>()
                    .ConfigureAwait(false);

            if (locationStatus !=
                PermissionStatus.Granted)
            {
                locationStatus =
                    await Permissions
                        .RequestAsync<
                            Permissions.LocationWhenInUse>()
                    .ConfigureAwait(false);
            }

            if (locationStatus !=
                    PermissionStatus.Granted ||
                bluetoothStatus !=
                    PermissionStatus.Granted)
            {
                throw new UnauthorizedAccessException(
                    "Bluetooth/Location permission საჭიროა.");
            }
        }

#endif
    }


    // =========================================================
    // DISCONNECTED EVENT
    // =========================================================

    private void OnDeviceDisconnected(
        object? sender,
        DeviceEventArgs e)
    {
        MarkBleDisconnected(
            e.Device,
            "OBD Bluetooth კავშირი გაითიშა.");
    }


    private void OnDeviceConnectionLost(
        object? sender,
        DeviceErrorEventArgs e)
    {
        MarkBleDisconnected(
            e.Device,
            "OBD Bluetooth კავშირი მოულოდნელად დაიკარგა.");
    }


    private void OnDeviceConnectionError(
        object? sender,
        DeviceErrorEventArgs e)
    {
        MarkBleDisconnected(
            e.Device,
            "OBD Bluetooth კავშირი შეცდომით დასრულდა.");
    }


    private void OnBluetoothStateChanged(
        object? sender,
        BluetoothStateChangedArgs e)
    {
        if (e.NewState == BluetoothState.On)
            return;

        MarkBleDisconnected(
            _connectedBleDevice,
            "Bluetooth გამორთულია ან მიუწვდომელია.");
    }


    private async Task DisconnectUnresponsiveBleDeviceAsync()
    {
        var device = _connectedBleDevice;
        if (device == null)
            return;

        MarkBleDisconnected(
            device,
            "OBD Bluetooth კავშირი აღარ პასუხობს.");

        try
        {
            using var disconnectTimeout =
                new CancellationTokenSource(TimeSpan.FromSeconds(3));

            await _adapter
                .DisconnectDeviceAsync(
                    device,
                    disconnectTimeout.Token)
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }


    private void MarkBleDisconnected(
        IDevice? device,
        string reason)
    {
        var activeDevice = _connectedBleDevice;

        if (device != null &&
            activeDevice != null &&
            device.Id != activeDevice.Id)
        {
            return;
        }

        var wasConnected = IsConnected;

        DetachCharacteristics();

        _connectedBleDevice = null;
        IsConnected = false;
        ConnectedDeviceName = string.Empty;

        _pendingResponse?
            .TrySetException(
                new IOException(reason));

        _pendingResponse = null;

        if (wasConnected)
        {
            ConnectionChanged?.Invoke(
                this,
                false);

            if (!_userDisconnect)
                StartAutoReconnect();
        }
    }

    // =========================================================
    // ADAPTER CHECK + AUTOMATIC RECONNECT
    // =========================================================

    /// <summary>
    /// After a command failed (timeout or write error): resynchronise with the
    /// adapter by sending a bare carriage return, waiting briefly for the '>'
    /// prompt and discarding whatever arrived. A prompt means the adapter is
    /// alive (only the car was slow), so the link stays up. Only after three
    /// failures in a row without any adapter answer (plus a final "ATI" check)
    /// is the link closed, which then starts the automatic reconnect. A real
    /// GATT / socket loss is handled separately by the disconnect events.
    /// Runs inside the command lock.
    /// </summary>
    private async Task CheckAdapterAfterFailureAsync()
    {
        if (!IsConnected)
            return;

        try
        {
            if (await ResyncAfterFailureAsync().ConfigureAwait(false))
            {
                _failedAdapterChecks = 0;
                return;
            }

            _failedAdapterChecks++;
            if (_failedAdapterChecks < MaxConsecutiveAdapterFailures)
                return;

            if (await RawExchangeAsync("ATI", TimeSpan.FromSeconds(3)).ConfigureAwait(false) is not null)
            {
                _failedAdapterChecks = 0;
                await SettleAsync().ConfigureAwait(false);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Adapter check failed.");
            _failedAdapterChecks++;
            if (_failedAdapterChecks < MaxConsecutiveAdapterFailures)
                return;
        }

        if (!IsConnected)
            return;

        _logger.LogInformation("OBD adapter stopped answering; reconnecting.");
        _failedAdapterChecks = 0;
#if ANDROID
        if (_classicSocket != null)
        {
            _ = Task.Run(HandleClassicLinkLostAsync);
            return;
        }
#endif
        await DisconnectUnresponsiveBleDeviceAsync().ConfigureAwait(false);
    }

    private const int MaxConsecutiveAdapterFailures = 3;

    /// <summary>
    /// Sends "\r" (stops a command the ELM327 is still busy with), waits up to
    /// ~500 ms for the prompt and clears the buffers so a late answer cannot be
    /// taken as the reply to the next command. True when the adapter answered.
    /// </summary>
    private async Task<bool> ResyncAfterFailureAsync()
    {
        var answered = await RawExchangeAsync(string.Empty, TimeSpan.FromMilliseconds(500)).ConfigureAwait(false) is not null;
        await SettleAsync().ConfigureAwait(false);
        return answered;
    }

    /// <summary>
    /// Discards adapter output that is still in the buffer (a late or partial
    /// answer of an earlier command) before a new command is written.
    /// </summary>
    private async Task DrainStaleInputAsync()
    {
        bool hadStale;
        lock (_responseBuffer)
        {
            hadStale = _responseBuffer.Length > 0;
            _responseBuffer.Clear();
        }

        if (!hadStale)
            return;

        _logger.LogDebug("Discarding stale adapter output before the next command.");
        await Task.Delay(80).ConfigureAwait(false);
        lock (_responseBuffer)
            _responseBuffer.Clear();
    }

    /// <summary>A late answer of the aborted command must not reach the next one.</summary>
    private async Task SettleAsync()
    {
        await Task.Delay(120).ConfigureAwait(false);
        lock (_responseBuffer)
            _responseBuffer.Clear();
    }

    /// <summary>One raw command without the command lock (already held). Null on timeout.</summary>
    private async Task<string?> RawExchangeAsync(string command, TimeSpan timeout)
    {
        lock (_responseBuffer)
            _responseBuffer.Clear();

        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingResponse = pending;
        var payload = Encoding.ASCII.GetBytes(command + "\r");

#if ANDROID
        if (_classicSocket != null)
        {
            if (_classicOutput == null)
                return null;
            await _classicOutput.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
            await _classicOutput.FlushAsync().ConfigureAwait(false);
        }
        else
#endif
        {
            if (_writeCharacteristic == null)
                return null;
            await WriteBlePayloadAsync(payload, CancellationToken.None).ConfigureAwait(false);
        }

        var completed = await Task.WhenAny(pending.Task, Task.Delay(timeout)).ConfigureAwait(false);
        return completed == pending.Task && pending.Task.Status == TaskStatus.RanToCompletion
            ? pending.Task.Result
            : null;
    }

    private void StartAutoReconnect()
    {
        if (_disposed || _userDisconnect || _lastDevice is null)
            return;
        if (!Microsoft.Maui.Storage.Preferences.Default.Get("auto_reconnect", true))
            return;
        if (Interlocked.Exchange(ref _reconnecting, 1) == 1)
            return;

        var device = _lastDevice;
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var seconds in new[] { 1, 2, 3, 5, 8, 13, 20 })
                {
                    await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
                    if (_disposed || _userDisconnect || IsConnected)
                        return;

                    if (!await _connectGate.WaitAsync(0).ConfigureAwait(false))
                        return; // the user is connecting right now

                    try
                    {
                        using var attempt = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                        await ConnectCoreAsync(device, attempt.Token).ConfigureAwait(false);
                        _userDisconnect = false;
                        _failedAdapterChecks = 0;
                        _logger.LogInformation("OBD adapter reconnected.");
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Automatic reconnect attempt failed.");
                    }
                    finally
                    {
                        _connectGate.Release();
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _reconnecting, 0);
            }
        });
    }


    private void DetachCharacteristics()
    {
        if (_notifyCharacteristic != null)
        {
            _notifyCharacteristic.ValueUpdated -=
                OnNotifyValueUpdated;

            try
            {
                _ = _notifyCharacteristic
                    .StopUpdatesAsync();
            }
            catch
            {
            }
        }

        _writeCharacteristic = null;
        _notifyCharacteristic = null;

        lock (_responseBuffer)
        {
            _responseBuffer.Clear();
        }
    }


    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(BluetoothService));
        }
    }


    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _adapter.DeviceDisconnected -=
            OnDeviceDisconnected;
        _adapter.DeviceConnectionLost -=
            OnDeviceConnectionLost;
        _adapter.DeviceConnectionError -=
            OnDeviceConnectionError;
        _ble.StateChanged -=
            OnBluetoothStateChanged;

        _ = StopScanAsync();

        DetachCharacteristics();

#if ANDROID
        _ = DisconnectClassicAsync();
#endif

        _commandLock.Dispose();
    }
}
