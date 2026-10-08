using CarScanner.Database;
using CarScanner.Models;
using CarScanner.Services.OBD;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CarScanner.Services;

/// <summary>
/// Automatically identifies a connected vehicle using VIN/OBD data, then enriches the profile
/// with a public VIN decoder when available. Never invents model/engine/fuel data.
/// </summary>
public sealed class VehicleInfoService
{
    private readonly OBDService _obd;
    private readonly AppDatabase _db;
    private readonly ILogger<VehicleInfoService> _logger;
    private readonly HttpClient _http;
    private readonly VehicleProfileResolverService _profileResolver;
    private readonly SemaphoreSlim _identityGate = new(1, 1);

    /// <summary>Raised after a real OBD vehicle profile has been read.</summary>
    public event EventHandler<VehicleInfo>? VehicleInfoUpdated;

    /// <summary>Most recent real vehicle identity, shared by read-only views.</summary>
    public VehicleInfo? CurrentVehicle { get; private set; }

    public VehicleInfoService(
        OBDService obd,
        AppDatabase db,
        ILogger<VehicleInfoService> logger,
        VehicleProfileResolverService profileResolver)
    {
        _obd = obd;
        _db = db;
        _logger = logger;
        _profileResolver = profileResolver;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GeorgianCarScanner/1.0");
    }

    public async Task<VehicleInfo> IdentifyConnectedVehicleAsync(
        CancellationToken token = default)
    {
        await _identityGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await ReadVehicleInfoAsync(token).ConfigureAwait(false);
        }
        finally
        {
            _identityGate.Release();
        }
    }

    public async Task<string> ReadVinAsync(
    CancellationToken token = default)
    {
        try
        {
            if (!_obd.IsInitialized)
                await _obd.InitializeAsync(token)
                    .ConfigureAwait(false);

            // Standard OBD-II VIN
            var raw = await _obd.SendRawCommandAsync(
                    "0902",
                    token)
                .ConfigureAwait(false);

            var vin = VinResponseParser.ParseMode09Vin(raw);

            if (VinResponseParser.IsValidVin(vin))
                return vin!;

            // UDS fallback - VIN DID F190
            try
            {
                var udsRaw = await _obd.SendRawCommandAsync(
                        "22F190",
                        token)
                    .ConfigureAwait(false);

                vin = VinResponseParser.ParseUdsVin(udsRaw);

                if (VinResponseParser.IsValidVin(vin))
                    return vin!;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "UDS VIN fallback unavailable");
            }

            return "Unknown";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "VIN read failed");
            return "Unknown";
        }
    }

    // ============================================================
    // CONNECTION-TIME IDENTITY (Home: ტრანსპორტი / VIN / კომპლექტაცია / საწვავი)
    // ============================================================

    private readonly object _identityTaskLock = new();
    private Task<VehicleInfo>? _identityTask;
    private int _connectionGeneration;

    /// <summary>
    /// True while the connection-time identity read is running. Home uses it
    /// to show "იკითხება…" instead of "მიუწვდომელია" for fields not read yet.
    /// </summary>
    public bool IsReadingIdentity
    {
        get
        {
            lock (_identityTaskLock)
                return _identityTask is { IsCompleted: false };
        }
    }

    /// <summary>
    /// Starts (or joins) the single shared identity read for the connected
    /// vehicle. The read is NOT tied to any page's timeout: a page that stops
    /// waiting never cancels it, and every partial result (VIN, then make/year,
    /// then fuel, then online model/trim) is published immediately through
    /// <see cref="VehicleInfoUpdated"/>.
    /// </summary>
    public Task<VehicleInfo> StartConnectedIdentityReadAsync(bool forceVinRefresh = false)
    {
        lock (_identityTaskLock)
        {
            if (_identityTask is { IsCompleted: false })
                return _identityTask;

            var current = CurrentVehicle;
            if (!forceVinRefresh && current is not null && IsIdentityComplete(current))
                return Task.FromResult(current);

            var generation = _connectionGeneration;
            _identityTask = Task.Run(() => ReadIdentityProgressiveAsync(generation));
            return _identityTask;
        }
    }

    /// <summary>
    /// Kept for existing callers. Joins the shared identity read; the caller's
    /// token only limits how long the caller waits, it never aborts the read.
    /// </summary>
    public async Task<VehicleInfo> EnsureConnectedVehicleInfoAsync(
        bool forceVinRefresh = false,
        CancellationToken token = default)
    {
        var task = StartConnectedIdentityReadAsync(forceVinRefresh);
        return await task.WaitAsync(token).ConfigureAwait(false);
    }

    public void ClearConnectedVehicleState()
    {
        lock (_identityTaskLock)
        {
            // Any read still running belongs to the previous connection and
            // must not publish into the next one.
            _connectionGeneration++;
            _identityTask = null;
            TrimUnavailableReason = TrimUnavailableReasonKind.None;
            CurrentVehicle = null;
        }
    }

    private static bool IsIdentityComplete(VehicleInfo info) =>
        VinResponseParser.IsValidVin(info.VIN) &&
        !IsUnknown(info.Manufacturer) &&
        !IsUnknown(info.Trim) &&
        !IsUnknown(info.FuelType);

    private async Task<VehicleInfo> ReadIdentityProgressiveAsync(int generation)
    {
        // Generous overall budget: ELM327 protocol search alone may need ~12 s
        // on the first connection. Individual steps have their own limits.
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = budget.Token;

        await _identityGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var info = CurrentVehicle is null ? new VehicleInfo() : Clone(CurrentVehicle);

            // 1) ELM327 initialization (skipped when already initialized).
            if (!_obd.IsInitialized)
            {
                try
                {
                    await _obd.InitializeAsync(token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "OBD initialization failed during identity read; retrying once");
                    await Task.Delay(800, token).ConfigureAwait(false);
                    await _obd.InitializeAsync(token).ConfigureAwait(false);
                }
            }

            info.Protocol = _obd.CurrentProtocolDescription;
            info.ProtocolOrigin = IsUnknown(info.Protocol)
                ? VehicleDataOrigin.Unknown
                : VehicleDataOrigin.AdapterNegotiated;

            // 2) VIN (Mode 09 PID 02, UDS F190 fallback). One retry because
            //    the first request after protocol search is sometimes dropped.
            if (!VinResponseParser.IsValidVin(info.VIN))
            {
                var vin = await ReadVinAsync(token).ConfigureAwait(false);
                if (!VinResponseParser.IsValidVin(vin))
                {
                    await Task.Delay(500, token).ConfigureAwait(false);
                    vin = await ReadVinAsync(token).ConfigureAwait(false);
                }

                if (VinResponseParser.IsValidVin(vin))
                {
                    info = new VehicleInfo
                    {
                        VIN = vin,
                        VinOrigin = VehicleDataOrigin.EcuMode09,
                        Protocol = info.Protocol,
                        ProtocolOrigin = info.ProtocolOrigin
                    };
                }
            }

            // 3) Instant local decode from the VIN itself (make + model year).
            if (VinResponseParser.IsValidVin(info.VIN))
            {
                if (IsUnknown(info.Manufacturer))
                {
                    info.Manufacturer = DecodeWmi(info.VIN[..3]);
                    if (!IsUnknown(info.Manufacturer))
                        info.ManufacturerOrigin = VehicleDataOrigin.VinDecoded;
                }

                if (IsUnknown(info.Year))
                {
                    info.Year = ResolveYear(DecodeVinYear(info.VIN[9]));
                    if (!IsUnknown(info.Year))
                        info.YearOrigin = VehicleDataOrigin.VinDecoded;
                }

                if (IsUnknown(info.Model))
                {
                    info.Model = InferModel(info.VIN, info.Manufacturer, info.Year);
                    if (!IsUnknown(info.Model))
                        info.ModelOrigin = IsManualModelOverride(info.Model)
                            ? VehicleDataOrigin.UserSelection
                            : VehicleDataOrigin.VerifiedLocalDefinition;
                }

                Publish(info, generation);

                // 4) Identity previously confirmed for this exact VIN
                //    (instant on every later connection, works offline).
                ApplyIdentityCache(info);
                await ApplyDatabaseCacheAsync(info).ConfigureAwait(false);
                Publish(info, generation);
            }

            // 5) Fuel type straight from the ECU (PID 01 51).
            if (IsUnknown(info.FuelType))
            {
                await TryReadFuelTypeAsync(info, token).ConfigureAwait(false);
                Publish(info, generation);
            }

            // 6) Online VIN decoder: model, trim/series, engine, fuel.
            if (VinResponseParser.IsValidVin(info.VIN) &&
                (IsUnknown(info.Model) || IsUnknown(info.Trim) || IsUnknown(info.FuelType) || IsUnknown(info.Year)))
            {
                await EnrichFromPublicVinDecoderAsync(info, token).ConfigureAwait(false);
                Publish(info, generation);
            }

            // 7) Last real ECU fallback for fuel: PID 01 01 byte B bit 3 says
            //    whether the engine is compression ignition (diesel) or spark
            //    ignition (petrol). Only used when nothing better is known.
            if (IsUnknown(info.FuelType))
            {
                await TryReadIgnitionTypeAsync(info, token).ConfigureAwait(false);
            }

            Publish(info, generation);

            if (VinResponseParser.IsValidVin(info.VIN) && generation == Volatile.Read(ref _connectionGeneration))
            {
                SaveIdentityCache(info);
                await SaveDatabaseCacheAsync(info).ConfigureAwait(false);
            }

            return info;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connection-time vehicle identity read ended early");
            var partial = CurrentVehicle ?? new VehicleInfo();
            // Final publish so Home switches "იკითხება…" to a final state.
            Publish(partial, generation, notifyOnly: true);
            return partial;
        }
        finally
        {
            _identityGate.Release();
        }
    }

    private void Publish(VehicleInfo info, int generation, bool notifyOnly = false)
    {
        VehicleInfo snapshot;
        lock (_identityTaskLock)
        {
            if (generation != _connectionGeneration)
                return;

            snapshot = Clone(info);
            if (!notifyOnly)
                CurrentVehicle = snapshot;
        }

        try { _profileResolver.Resolve(snapshot); } catch { }
        VehicleInfoUpdated?.Invoke(this, snapshot);
    }

    private static VehicleInfo Clone(VehicleInfo source) => new()
    {
        VIN = source.VIN,
        Trim = source.Trim,
        Manufacturer = source.Manufacturer,
        Model = source.Model,
        Year = source.Year,
        Engine = source.Engine,
        FuelType = source.FuelType,
        Protocol = source.Protocol,
        ECUName = source.ECUName,
        CalibrationId = source.CalibrationId,
        CVN = source.CVN,
        VinOrigin = source.VinOrigin,
        ManufacturerOrigin = source.ManufacturerOrigin,
        ModelOrigin = source.ModelOrigin,
        YearOrigin = source.YearOrigin,
        EngineOrigin = source.EngineOrigin,
        FuelTypeOrigin = source.FuelTypeOrigin,
        ProtocolOrigin = source.ProtocolOrigin
    };

    private async Task TryReadIgnitionTypeAsync(VehicleInfo info, CancellationToken token)
    {
        try
        {
            var data = await _obd.ReadMode01PidDataAsync(0x01, token).ConfigureAwait(false);
            if (data is null || data.Length < 2)
                return;

            var compressionIgnition = (data[1] & 0x08) != 0;
            info.FuelType = compressionIgnition ? "დიზელი" : "ბენზინი";
            info.FuelTypeOrigin = VehicleDataOrigin.EcuPid;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PID 01 01 ignition type read failed");
        }
    }

    private async Task ApplyDatabaseCacheAsync(VehicleInfo info)
    {
        try
        {
            var cached = await _db.GetVehicleAsync(info.VIN).ConfigureAwait(false);
            if (cached is null)
                return;

            if (IsUnknown(info.Model) && !IsUnknown(cached.Model) && IsTrustedCachedOrigin(cached.ModelOrigin))
            {
                info.Model = cached.Model;
                info.ModelOrigin = VehicleDataOrigin.ExactVinCache;
            }
            if (IsUnknown(info.Engine) && !IsUnknown(cached.Engine) && IsTrustedCachedOrigin(cached.EngineOrigin))
            {
                info.Engine = cached.Engine;
                info.EngineOrigin = VehicleDataOrigin.ExactVinCache;
            }
            if (IsUnknown(info.FuelType) && !IsUnknown(cached.FuelType) && IsTrustedCachedOrigin(cached.FuelTypeOrigin))
            {
                info.FuelType = cached.FuelType;
                info.FuelTypeOrigin = VehicleDataOrigin.ExactVinCache;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Exact-VIN cache lookup failed");
        }
    }

    private async Task SaveDatabaseCacheAsync(VehicleInfo info)
    {
        try
        {
            await _db.SaveVehicleAsync(new VehicleProfile
            {
                VIN = info.VIN,
                Manufacturer = info.Manufacturer,
                Model = info.Model,
                Year = info.Year,
                Engine = info.Engine,
                FuelType = info.FuelType,
                Protocol = info.Protocol,
                ECUName = info.ECUName,
                CalibrationId = info.CalibrationId,
                CVN = info.CVN,
                ManufacturerOrigin = info.ManufacturerOrigin,
                ModelOrigin = info.ModelOrigin,
                YearOrigin = info.YearOrigin,
                EngineOrigin = info.EngineOrigin,
                FuelTypeOrigin = info.FuelTypeOrigin,
                LastSeen = DateTime.Now
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Connected vehicle state could not be cached");
        }
    }

    // Small per-VIN identity cache (includes Trim, which the database model
    // does not store). Only values that came from the ECU or a VIN decoder
    // are cached, never manual selections.
    private const string IdentityCachePrefix = "vehicle_identity_v1_";

    private sealed class IdentityCacheEntry
    {
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? Year { get; set; }
        public string? Trim { get; set; }
        public string? Engine { get; set; }
        public string? FuelType { get; set; }
    }

    private void ApplyIdentityCache(VehicleInfo info)
    {
        try
        {
            var json = Preferences.Default.Get(IdentityCachePrefix + info.VIN.ToUpperInvariant(), string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return;

            var entry = JsonSerializer.Deserialize<IdentityCacheEntry>(json);
            if (entry is null)
                return;

            if (!IsUnknown(entry.Manufacturer) && info.ManufacturerOrigin != VehicleDataOrigin.OnlineVinDecoder)
            {
                info.Manufacturer = entry.Manufacturer!;
                info.ManufacturerOrigin = VehicleDataOrigin.ExactVinCache;
            }
            if (IsUnknown(info.Model) && !IsUnknown(entry.Model))
            {
                info.Model = entry.Model!;
                info.ModelOrigin = VehicleDataOrigin.ExactVinCache;
            }
            if (!IsUnknown(entry.Year) && info.YearOrigin != VehicleDataOrigin.OnlineVinDecoder)
            {
                info.Year = entry.Year!;
                info.YearOrigin = VehicleDataOrigin.ExactVinCache;
            }
            if (IsUnknown(info.Trim) && !IsUnknown(entry.Trim))
                info.Trim = CleanTrim(entry.Trim!);
            if (IsUnknown(info.Engine) && !IsUnknown(entry.Engine))
            {
                info.Engine = entry.Engine!;
                info.EngineOrigin = VehicleDataOrigin.ExactVinCache;
            }
            if (IsUnknown(info.FuelType) && !IsUnknown(entry.FuelType))
            {
                info.FuelType = entry.FuelType!;
                info.FuelTypeOrigin = VehicleDataOrigin.ExactVinCache;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Identity cache read failed");
        }
    }

    private void SaveIdentityCache(VehicleInfo info)
    {
        try
        {
            string? Trusted(string value, VehicleDataOrigin origin) =>
                IsUnknown(value) || origin == VehicleDataOrigin.UserSelection ? null : value;

            var entry = new IdentityCacheEntry
            {
                Manufacturer = Trusted(info.Manufacturer, info.ManufacturerOrigin),
                Model = Trusted(info.Model, info.ModelOrigin),
                Year = Trusted(info.Year, info.YearOrigin),
                Trim = IsUnknown(info.Trim) ? null : info.Trim,
                Engine = Trusted(info.Engine, info.EngineOrigin),
                FuelType = Trusted(info.FuelType, info.FuelTypeOrigin)
            };

            Preferences.Default.Set(
                IdentityCachePrefix + info.VIN.ToUpperInvariant(),
                JsonSerializer.Serialize(entry));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Identity cache write failed");
        }
    }

    public async Task<VehicleInfo> ReadVehicleInfoAsync(CancellationToken token = default)
    {
        var previous = CurrentVehicle;
        var vin = await ReadVinAsync(token).ConfigureAwait(false);
        if (!VinResponseParser.IsValidVin(vin) && VinResponseParser.IsValidVin(previous?.VIN))
            vin = previous!.VIN;

        var info = new VehicleInfo
        {
            VIN = vin,
            VinOrigin = IsValidVin(vin) ? VehicleDataOrigin.EcuMode09 : VehicleDataOrigin.Unknown,
            Protocol = _obd.CurrentProtocolDescription,
            ProtocolOrigin = IsUnknown(_obd.CurrentProtocolDescription)
                ? VehicleDataOrigin.Unknown
                : VehicleDataOrigin.AdapterNegotiated
        };

        if (info.VIN.Length >= 3)
        {
            info.Manufacturer = DecodeWmi(info.VIN[..3]);
            if (!IsUnknown(info.Manufacturer))
                info.ManufacturerOrigin = VehicleDataOrigin.VinDecoded;
        }

        if (info.VIN.Length >= 10)
        {
            info.Year = ResolveYear(DecodeVinYear(info.VIN[9]));
            if (!IsUnknown(info.Year))
                info.YearOrigin = VehicleDataOrigin.VinDecoded;
        }

        try { info.ECUName = await ReadMode09TextAsync(0x0A, token).ConfigureAwait(false); } catch { }
        try { info.CalibrationId = await ReadMode09TextAsync(0x04, token).ConfigureAwait(false); } catch { }
        try { info.CVN = await ReadMode09CvnAsync(token).ConfigureAwait(false); } catch { }

        // First resolve known local/manual profiles
        info.Model = InferModel(
            info.VIN,
            info.Manufacturer,
            info.Year);

        if (!IsUnknown(info.Model))
        {
            info.ModelOrigin =
                IsManualModelOverride(info.Model)
                    ? VehicleDataOrigin.UserSelection
                    : VehicleDataOrigin.VerifiedLocalDefinition;
        }

        // Fuel type directly from ECU when supported
        await TryReadFuelTypeAsync(
            info,
            token).ConfigureAwait(false);

        // VIN-ის მიღების შემდეგ დამატებითი ინფორმაცია
        // ავტომატურად შევავსოთ ონლაინ decoder-იდან
        if (info.VIN != "Unknown")
        {
            await EnrichFromPublicVinDecoderAsync(
                info,
                token).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(info.Model)) info.Model = "Unknown";
        if (string.IsNullOrWhiteSpace(info.Engine)) info.Engine = "Unknown";
        if (string.IsNullOrWhiteSpace(info.FuelType)) info.FuelType = "Unknown";

        PreserveConfirmedFields(info, previous);

        if (info.VIN != "Unknown")
        {
            var cached = await _db.GetVehicleAsync(info.VIN).ConfigureAwait(false);
            if (cached is not null)
            {
                if (IsUnknown(info.Model) && !IsUnknown(cached.Model) && IsTrustedCachedOrigin(cached.ModelOrigin))
                {
                    info.Model = cached.Model;
                    info.ModelOrigin = VehicleDataOrigin.ExactVinCache;
                }
                if (IsUnknown(info.Engine) && !IsUnknown(cached.Engine) && IsTrustedCachedOrigin(cached.EngineOrigin))
                {
                    info.Engine = cached.Engine;
                    info.EngineOrigin = VehicleDataOrigin.ExactVinCache;
                }
                if (IsUnknown(info.FuelType) && !IsUnknown(cached.FuelType) && IsTrustedCachedOrigin(cached.FuelTypeOrigin))
                {
                    info.FuelType = cached.FuelType;
                    info.FuelTypeOrigin = VehicleDataOrigin.ExactVinCache;
                }
                if (IsUnknown(info.ECUName)) info.ECUName = cached.ECUName;
            }

            await _db.SaveVehicleAsync(new VehicleProfile
            {
                VIN = info.VIN,
                Manufacturer = info.Manufacturer,
                Model = info.Model,
                Year = info.Year,
                Engine = info.Engine,
                FuelType = info.FuelType,
                Protocol = info.Protocol,
                ECUName = info.ECUName,
                CalibrationId = info.CalibrationId,
                CVN = info.CVN,
                ManufacturerOrigin = info.ManufacturerOrigin,
                ModelOrigin = info.ModelOrigin,
                YearOrigin = info.YearOrigin,
                EngineOrigin = info.EngineOrigin,
                FuelTypeOrigin = info.FuelTypeOrigin,
                LastSeen = DateTime.Now
            }).ConfigureAwait(false);
        }

        CurrentVehicle = info;
        _profileResolver.Resolve(info);
        VehicleInfoUpdated?.Invoke(this, info);
        return info;
    }

    /// <summary>
    /// Fast connection-time vehicle identity. It reads only the standard VIN
    /// response and derives manufacturer/year locally; expensive Mode 09
    /// identifiers and the optional web VIN decoder are reserved for pages
    /// that explicitly need a full profile.
    /// </summary>
    public async Task<VehicleInfo> ReadVehicleInfoQuickAsync(CancellationToken token = default)
    {
        var previous = CurrentVehicle;
        var vin = await ReadVinAsync(token).ConfigureAwait(false);
        if (!VinResponseParser.IsValidVin(vin) && VinResponseParser.IsValidVin(previous?.VIN))
            vin = previous!.VIN;

        var info = new VehicleInfo
        {
            VIN = vin,
            VinOrigin = IsValidVin(vin) ? VehicleDataOrigin.EcuMode09 : VehicleDataOrigin.Unknown,
            Protocol = _obd.CurrentProtocolDescription,
            ProtocolOrigin = IsUnknown(_obd.CurrentProtocolDescription)
                ? VehicleDataOrigin.Unknown
                : VehicleDataOrigin.AdapterNegotiated
        };

        if (info.VIN.Length >= 3)
        {
            info.Manufacturer = DecodeWmi(info.VIN[..3]);
            if (!IsUnknown(info.Manufacturer))
                info.ManufacturerOrigin = VehicleDataOrigin.VinDecoded;
        }

        if (info.VIN.Length >= 10)
        {
            info.Year = ResolveYear(DecodeVinYear(info.VIN[9]));
            if (!IsUnknown(info.Year))
                info.YearOrigin = VehicleDataOrigin.VinDecoded;
        }

        info.Model = InferModel(info.VIN, info.Manufacturer, info.Year);
        if (!IsUnknown(info.Model))
            info.ModelOrigin = IsManualModelOverride(info.Model)
                ? VehicleDataOrigin.UserSelection
                : VehicleDataOrigin.VerifiedLocalDefinition;

        // PID 01 51 is the standard ECU fuel-type value. Read it only when
        // the ECU advertised support; otherwise leave it unavailable rather
        // than deriving a value from the selected brand or vehicle model.
        await TryReadFuelTypeAsync(info, token).ConfigureAwait(false);

        PreserveConfirmedFields(info, previous);

        if (info.VIN != "Unknown")
        {
            try
            {
                // Reuse only information previously confirmed for this exact
                // VIN.  This fills model/engine/fuel instantly on the next
                // connection without guessing or contacting an online service.
                var cached = await _db.GetVehicleAsync(info.VIN).ConfigureAwait(false);
                if (cached is not null)
                {
                    if (IsUnknown(info.Model) && !IsUnknown(cached.Model) && IsTrustedCachedOrigin(cached.ModelOrigin))
                    {
                        info.Model = cached.Model;
                        info.ModelOrigin = VehicleDataOrigin.ExactVinCache;
                    }
                    if (IsUnknown(info.Engine) && !IsUnknown(cached.Engine) && IsTrustedCachedOrigin(cached.EngineOrigin))
                    {
                        info.Engine = cached.Engine;
                        info.EngineOrigin = VehicleDataOrigin.ExactVinCache;
                    }
                    if (IsUnknown(info.FuelType) && !IsUnknown(cached.FuelType) && IsTrustedCachedOrigin(cached.FuelTypeOrigin))
                    {
                        info.FuelType = cached.FuelType;
                        info.FuelTypeOrigin = VehicleDataOrigin.ExactVinCache;
                    }
                    if (IsUnknown(info.ECUName)) info.ECUName = cached.ECUName;
                }

                await _db.SaveVehicleAsync(new VehicleProfile
                {
                    VIN = info.VIN,
                    Manufacturer = info.Manufacturer,
                    Model = info.Model,
                    Year = info.Year,
                    Engine = info.Engine,
                    FuelType = info.FuelType,
                    Protocol = info.Protocol,
                    ManufacturerOrigin = info.ManufacturerOrigin,
                    ModelOrigin = info.ModelOrigin,
                    YearOrigin = info.YearOrigin,
                    EngineOrigin = info.EngineOrigin,
                    FuelTypeOrigin = info.FuelTypeOrigin,
                    LastSeen = DateTime.Now
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Fast vehicle profile could not be cached");
            }
        }

        CurrentVehicle = info;
        _profileResolver.Resolve(info);
        VehicleInfoUpdated?.Invoke(this, info);
        return info;
    }

    private static void PreserveConfirmedFields(VehicleInfo target, VehicleInfo? previous)
    {
        if (previous is null ||
            !VinResponseParser.IsValidVin(target.VIN) ||
            !VinResponseParser.IsValidVin(previous.VIN) ||
            !string.Equals(target.VIN, previous.VIN, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        static bool Missing(string? value) =>
            string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase);

        if (Missing(target.Manufacturer) && !Missing(previous.Manufacturer))
        {
            target.Manufacturer = previous.Manufacturer;
            target.ManufacturerOrigin = previous.ManufacturerOrigin;
        }
        if (Missing(target.Model) && !Missing(previous.Model))
        {
            target.Model = previous.Model;
            target.ModelOrigin = previous.ModelOrigin;
        }
        if (Missing(target.Year) && !Missing(previous.Year))
        {
            target.Year = previous.Year;
            target.YearOrigin = previous.YearOrigin;
        }
        if (Missing(target.Engine) && !Missing(previous.Engine))
        {
            target.Engine = previous.Engine;
            target.EngineOrigin = previous.EngineOrigin;
        }
        if (Missing(target.FuelType) && !Missing(previous.FuelType))
        {
            target.FuelType = previous.FuelType;
            target.FuelTypeOrigin = previous.FuelTypeOrigin;
        }
        if (Missing(target.Trim) && !Missing(previous.Trim))
            target.Trim = previous.Trim;
        if (Missing(target.ECUName) && !Missing(previous.ECUName))
            target.ECUName = previous.ECUName;
        if (Missing(target.CalibrationId) && !Missing(previous.CalibrationId))
            target.CalibrationId = previous.CalibrationId;
        if (Missing(target.CVN) && !Missing(previous.CVN))
            target.CVN = previous.CVN;
    }

    private static bool IsUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase);

    private static bool IsTrustedCachedOrigin(VehicleDataOrigin origin) => origin is
        VehicleDataOrigin.EcuMode09 or
        VehicleDataOrigin.VinDecoded or
        VehicleDataOrigin.EcuPid or
        VehicleDataOrigin.ExactVinCache or
        VehicleDataOrigin.OnlineVinDecoder or
        VehicleDataOrigin.VerifiedLocalDefinition;

    private async Task TryReadFuelTypeAsync(VehicleInfo info, CancellationToken token)
    {
        if (!_obd.IsInitialized || !_obd.IsPidSupported(0x51))
            return;

        try
        {
            var value = await _obd.ReadSensorAsync(0x51, token).ConfigureAwait(false);
            if (value is null ||
                !int.TryParse(value.Value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var code))
            {
                return;
            }

            var fuelType = DescribeFuelType(code);
            if (fuelType is not null)
            {
                info.FuelType = fuelType;
                info.FuelTypeOrigin = VehicleDataOrigin.EcuPid;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Fuel type is optional. Preserve already read VIN/year data if
            // this final optional PID outlives its short connection budget.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ECU fuel type read failed");
        }
    }

    private static string? DescribeFuelType(int code) => code switch
    {
        1 => "ბენზინი",
        2 => "მეთანოლი",
        3 => "ეთანოლი",
        4 => "დიზელი",
        5 => "LPG",
        6 => "CNG",
        7 => "პროპანი",
        8 => "ელექტრო",
        9 => "ბენზინი / ელექტრო",
        10 => "მეთანოლი / ელექტრო",
        11 => "ეთანოლი / ელექტრო",
        12 => "LPG / ელექტრო",
        13 => "CNG / ელექტრო",
        14 => "პროპანი / ელექტრო",
        15 => "ორმაგი საწვავი / ელექტრო",
        16 => "ჰიბრიდი",
        17 => "ბენზინი ჰიბრიდი",
        18 => "ეთანოლი ჰიბრიდი",
        19 => "დიზელი ჰიბრიდი",
        20 => "ელექტრო ჰიბრიდი",
        21 => "შერეული ჰიბრიდი",
        22 => "რეგენერაციული ჰიბრიდი",
        23 => "დიზელი / ელექტრო",
        24 => "Flex Fuel",
        _ => null
    };

    public enum TrimUnavailableReasonKind
    {
        None,
        DecoderUnreachable,
        NotInVinData
    }

    /// <summary>Why Trim is still unknown after the last online decode.</summary>
    public TrimUnavailableReasonKind TrimUnavailableReason { get; private set; }

    /// <summary>
    /// Queries the public VIN decoder with retries: Extended endpoint first,
    /// then the basic endpoint (with model year, which improves matches).
    /// Returns null only when the decoder could not be reached at all.
    /// </summary>
    private async Task<VinApiRow?> FetchVinDecoderRowAsync(string vin, string year, CancellationToken token)
    {
        var escapedVin = Uri.EscapeDataString(vin);
        var yearQuery = int.TryParse(year, out var y) && y > 1980 ? $"&modelyear={y}" : string.Empty;
        var urls = new[]
        {
            $"https://vpic.nhtsa.dot.gov/api/vehicles/DecodeVinValuesExtended/{escapedVin}?format=json{yearQuery}",
            $"https://vpic.nhtsa.dot.gov/api/vehicles/DecodeVinValuesExtended/{escapedVin}?format=json{yearQuery}",
            $"https://vpic.nhtsa.dot.gov/api/vehicles/DecodeVinValues/{escapedVin}?format=json{yearQuery}"
        };

        foreach (var url in urls)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var response = await _http.GetAsync(url, token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    continue;

                await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                var payload = await JsonSerializer.DeserializeAsync<VinApiResponse>(stream, cancellationToken: token).ConfigureAwait(false);
                var row = payload?.Results?.FirstOrDefault();
                if (row is not null)
                    return row;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "VIN decoder request failed: {Url}", url);
                await Task.Delay(700, token).ConfigureAwait(false);
            }
        }

        return null;
    }

    private async Task EnrichFromPublicVinDecoderAsync(VehicleInfo info, CancellationToken token)
    {
        try
        {
            var row = await FetchVinDecoderRowAsync(info.VIN, info.Year, token).ConfigureAwait(false);
            if (row is null)
            {
                TrimUnavailableReason = TrimUnavailableReasonKind.DecoderUnreachable;
                return;
            }

            if (!string.IsNullOrWhiteSpace(row.Make))
            {
                info.Manufacturer = row.Make;
                info.ManufacturerOrigin = VehicleDataOrigin.OnlineVinDecoder;
            }
            if (!string.IsNullOrWhiteSpace(row.Model))
            {
                info.Model = row.Model;
                info.ModelOrigin = VehicleDataOrigin.OnlineVinDecoder;
            }
            if (!string.IsNullOrWhiteSpace(row.ModelYear))
            {
                info.Year = row.ModelYear;
                info.YearOrigin = VehicleDataOrigin.OnlineVinDecoder;
            }
            var trim = BuildTrim(row);
            if (!string.IsNullOrWhiteSpace(trim))
            {
                info.Trim = trim;
                TrimUnavailableReason = TrimUnavailableReasonKind.None;
            }
            else
            {
                TrimUnavailableReason = TrimUnavailableReasonKind.NotInVinData;
            }

            if (!string.IsNullOrWhiteSpace(row.EngineCylinders) || !string.IsNullOrWhiteSpace(row.EngineDisplacementCi))
            {
                var liters = double.TryParse(row.EngineDisplacementL, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var l)
                    ? $"{l:0.###} L"
                    : string.Empty;
                info.Engine = string.Join(" ", new[] { liters,
                    string.IsNullOrWhiteSpace(row.EngineCylinders) ? string.Empty : $"{row.EngineCylinders} cyl",
                    string.IsNullOrWhiteSpace(row.EngineModel) ? string.Empty : row.EngineModel }
                    .Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
                if (!string.IsNullOrWhiteSpace(info.Engine))
                    info.EngineOrigin = VehicleDataOrigin.OnlineVinDecoder;
            }
            if (!string.IsNullOrWhiteSpace(row.FuelTypePrimary) && IsUnknown(info.FuelType))
            {
                info.FuelType = row.FuelTypePrimary;
                info.FuelTypeOrigin = VehicleDataOrigin.OnlineVinDecoder;
            }
            if (!string.IsNullOrWhiteSpace(row.TransmissionStyle) && info.ECUName == "Unknown")
                info.ECUName = row.TransmissionStyle;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Public VIN enrichment unavailable for {Vin}", info.VIN);
        }
    }

    /// <summary>
    /// Trim/grade from the VIN decoder. Prefers the real trim, then series.
    /// When the decoder has neither (common for non-US VINs), the equipment
    /// line is composed from other decoded facts (engine size, body, drive,
    /// transmission) so the field still shows real VIN data, never a guess.
    /// </summary>
    private static string BuildTrim(VinApiRow row)
    {
        static bool Has(string? v) =>
            !string.IsNullOrWhiteSpace(v) &&
            !v.Equals("Not Applicable", StringComparison.OrdinalIgnoreCase) &&
            !v.Equals("0", StringComparison.Ordinal);

        var named = string.Join(" ", new[] { row.Trim, row.Trim2 }.Where(Has).Select(v => v!.Trim()));
        if (string.IsNullOrWhiteSpace(named))
            named = string.Join(" ", new[] { row.Series, row.Series2 }.Where(Has).Select(v => v!.Trim()));
        named = CleanTrim(named);
        if (!string.IsNullOrWhiteSpace(named))
            return named;

        var parts = new List<string>();
        if (double.TryParse(row.EngineDisplacementL, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var liters) && liters > 0)
            parts.Add($"{liters:0.0} L");
        if (Has(row.BodyClass)) parts.Add(row.BodyClass!.Split('/')[0].Trim());
        if (Has(row.DriveType)) parts.Add(row.DriveType!.Split('/')[0].Trim());
        if (Has(row.TransmissionStyle)) parts.Add(row.TransmissionStyle!.Trim());
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Keeps only the trim name: "Premium w/o Navigation" → "Premium".
    /// Option notes such as "w/o …", "w/ …", "with …", "without …" and
    /// parenthesised remarks are removed.
    /// </summary>
    private static string CleanTrim(string trim)
    {
        if (string.IsNullOrWhiteSpace(trim))
            return string.Empty;

        var cleaned = System.Text.RegularExpressions.Regex.Replace(
            trim,
            @"\s*(\(.*?\)|\b(w/o|w/|with|without)\b.*)$",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        cleaned = cleaned.Trim(' ', ',', '-', '/', ';');
        return string.IsNullOrWhiteSpace(cleaned) ? trim.Trim() : cleaned;
    }

    private async Task<string> ReadMode09TextAsync(int pid, CancellationToken token)
    {
        // Many inexpensive ELM327 clones do not support every Mode 09 identifier.
        // One short attempt gives a responsive UI; unavailable values stay empty.
        var raw = await _obd.ReadMode09QuickAsync(pid, token).ConfigureAwait(false);
        var values = new List<string>();
        foreach (var payload in ExtractMode09Payloads(raw, (byte)pid))
        {
            // Calibration IDs are 16 bytes each (several per ECU are possible);
            // the ECU name is one 20-byte field.
            var chunk = pid == 0x04 ? 16 : payload.Length;
            for (var offset = 0; offset < payload.Length; offset += Math.Max(1, chunk))
            {
                var length = Math.Min(Math.Max(1, chunk), payload.Length - offset);
                var text = ObdResponseParser.DecodeAscii(payload.Skip(offset).Take(length).ToArray());
                if (!string.IsNullOrWhiteSpace(text) && !values.Contains(text))
                    values.Add(text);
            }
        }
        return string.Join(", ", values);
    }

    /// <summary>Calibration verification numbers (Mode 09 PID 06): 4 bytes each, shown as hex.</summary>
    private async Task<string> ReadMode09CvnAsync(CancellationToken token)
    {
        var raw = await _obd.ReadMode09QuickAsync(0x06, token).ConfigureAwait(false);
        var values = new List<string>();
        foreach (var payload in ExtractMode09Payloads(raw, 0x06))
        {
            for (var offset = 0; offset + 4 <= payload.Length; offset += 4)
            {
                var hex = string.Concat(payload.Skip(offset).Take(4).Select(b => b.ToString("X2")));
                if (!values.Contains(hex))
                    values.Add(hex);
            }
        }
        return string.Join(", ", values);
    }

    /// <summary>
    /// Data bytes of a Mode 09 reply without the "49 PID count/sequence" marker.
    /// Handles CAN multi-frame output (length line + "0:", "1:" … lines, one
    /// entry per ECU), CAN single frames, and non-CAN frames where every line
    /// repeats "49 PID seq" (those are joined into one entry).
    /// </summary>
    private static List<byte[]> ExtractMode09Payloads(string? raw, byte pid)
    {
        var result = new List<byte[]>();
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        List<byte>? multiFrame = null;
        int multiFrameLength = -1;
        var singleFrames = new List<byte>();

        void FlushMultiFrame()
        {
            if (multiFrame is null)
                return;
            var bytes = multiFrameLength > 0 && multiFrame.Count > multiFrameLength
                ? multiFrame.GetRange(0, multiFrameLength)
                : multiFrame;
            if (bytes.Count >= 3 && bytes[0] == 0x49 && bytes[1] == pid)
                result.Add(bytes.Skip(3).ToArray());
            multiFrame = null;
            multiFrameLength = -1;
        }

        foreach (var rawLine in raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim().TrimStart('>').Trim();
            if (line.Length == 0)
                continue;

            if (line.Length == 3 && System.Text.RegularExpressions.Regex.IsMatch(line, "^[0-9A-Fa-f]{3}$"))
            {
                FlushMultiFrame();
                multiFrame = new List<byte>();
                multiFrameLength = Convert.ToInt32(line, 16);
                continue;
            }

            var continuation = line.Length >= 2 && line[1] == ':' && Uri.IsHexDigit(line[0]);
            if (continuation)
                line = line[2..].Trim();

            var bytes = ObdResponseParser.ParseHex(line);
            if (bytes.Length == 0)
                continue;

            if (continuation && multiFrame is not null)
            {
                multiFrame.AddRange(bytes);
                continue;
            }

            for (var i = 0; i + 2 < bytes.Length; i++)
            {
                if (bytes[i] != 0x49 || bytes[i + 1] != pid)
                    continue;
                singleFrames.AddRange(bytes.Skip(i + 3));
                break;
            }
        }

        FlushMultiFrame();
        if (singleFrames.Count > 0)
            result.Add(singleFrames.ToArray());
        return result;
    }

    private static bool IsValidVin(string vin) => VinResponseParser.IsValidVin(vin);

    private static string ResolveYear(string raw)
    {
        if (raw.Contains('/'))
        {
            var parts = raw.Split('/', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var a) && int.TryParse(parts[1], out var b))
            {
                var now = DateTime.Now.Year;
                return Math.Abs(now - a) <= Math.Abs(now - b) ? a.ToString() : b.ToString();
            }
        }
        return raw;
    }

    private static string InferModel(string vin, string manufacturer, string year)
    {
        var manual = Preferences.Default.Get("vehicle_model_override", string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(manual)) return manual;

        if (manufacturer.Contains("Chevrolet", StringComparison.OrdinalIgnoreCase) &&
            new[] { "2011", "2012", "2013", "2014", "2015" }.Contains(year) &&
            vin.StartsWith("1G1", StringComparison.OrdinalIgnoreCase)) return "Volt";

        return "Unknown";
    }

    private static bool IsManualModelOverride(string model)
    {
        var manual = Preferences.Default.Get("vehicle_model_override", string.Empty).Trim();
        return !string.IsNullOrWhiteSpace(manual) &&
               manual.Equals(model, StringComparison.OrdinalIgnoreCase);
    }

    private static string DecodeWmi(string wmi) => wmi.ToUpperInvariant() switch
    {
        // Toyota / Lexus / Scion
        "JT2" or "JT3" or "JTD" or "JTE" or "JTG" or "JTJ" or "JTK" or "JTL" or
        "JTM" or "JTN" or "4T1" or "4T3" or "4T4" or "5TD" or "5TE" or "5TF" or "5TB" or
        "2T1" or "2T2" or "2T3" => "Toyota",
        "JTH" or "JTHB" => "Lexus",
        // Honda / Acura
        "1HG" or "2HG" or "3HG" or "5FN" or "5FR" or "5J6" or "5J8" or "JHM" or "JHL" or
        "JHN" or "SHH" or "SHS" or "19X" or "19U" => "Honda",
        "19UYA" or "JH4" => "Acura",
        // Nissan / Infiniti
        "JN1" or "JN6" or "JN8" or "JNA" or "SJN" or "1N4" or "1N6" or "3N1" or
        "5N1" or "5N3" => "Nissan",
        "JNK" or "JNR" or "JNC" => "Infiniti",
        // Mazda
        "JM1" or "JM3" or "JM7" or "1YV" or "4F2" or "4F4" => "Mazda",
        // Subaru
        "JF1" or "JF2" or "4S3" or "4S4" => "Subaru",
        // Mitsubishi
        "JA3" or "JA4" or "JMB" or "4A3" or "4A4" or "6MM" => "Mitsubishi",
        // Suzuki
        "JS1" or "JS2" or "JS3" or "2S3" => "Suzuki",
        // Isuzu
        "JAA" or "JAC" => "Isuzu",
        // Daewoo / GM Korea (Chevrolet Korea)
        "KL1" or "KLA" or "KLY" => "Chevrolet",
        // Hyundai / Kia / Genesis
        "KMH" or "KMY" or "TMA" or "MAL" or "5NP" or "5NM" => "Hyundai",
        "KNA" or "KND" or "KNM" or "U5Y" or "5XX" or "5XY" => "Kia",
        "KMT" => "Genesis",
        // Volkswagen Group
        "WVW" or "WV1" or "WV2" or "1VW" or "3VW" or "9BW" => "Volkswagen",
        "WAU" or "WA1" or "TRU" => "Audi",
        "TMB" or "TMK" => "Skoda",
        "VSS" or "VSK" => "SEAT",
        // BMW / Mini
        "WBA" or "WBS" or "WBX" or "WBY" or "4US" or "5UX" or "5YM" => "BMW",
        "WMW" or "WMX" => "Mini",
        // Mercedes-Benz / Smart
        "WDB" or "WDD" or "WDC" or "WDF" or "4JG" or "55S" => "Mercedes-Benz",
        "WME" => "Smart",
        // Porsche
        "WP0" or "WP1" => "Porsche",
        // Ford / Lincoln
        "1FA" or "1FB" or "1FC" or "1FD" or "1FM" or "1FT" or "2FA" or "3FA" or "MAJ" or "WF0" => "Ford",
        "1LN" or "5LM" => "Lincoln",
        // GM (Chevrolet / GMC / Cadillac / Buick)
        "1G1" or "1GC" or "1GB" or "2G1" or "3G1" or "6G1" => "Chevrolet",
        "1GT" or "2GT" or "3GT" => "GMC",
        "1G6" or "1GY" => "Cadillac",
        "1G4" or "2G4" => "Buick",
        // Chrysler / Jeep / Dodge / RAM
        "1C3" or "1C4" or "1C6" or "2C3" or "2C4" or "3C4" or "3C6" => "Chrysler/Dodge/Jeep",
        // Volvo
        "YV1" or "YV4" or "YV2" or "7JR" => "Volvo",
        // Renault / Dacia
        "VF1" or "VF2" or "UU1" => "Renault",
        "UU6" => "Dacia",
        // Peugeot / Citroën
        "VF3" => "Peugeot",
        "VF7" => "Citroën",
        // Fiat / Alfa Romeo / Lancia
        "ZFA" => "Fiat",
        "ZAR" => "Alfa Romeo",
        "ZLA" => "Lancia",
        // Land Rover / Jaguar
        "SAL" or "SAD" => "Land Rover",
        "SAJ" => "Jaguar",
        // Opel/Vauxhall
        "W0L" or "W0V" => "Opel",
        // Saab
        "YS3" => "Saab",
        // Tesla
        "5YJ" or "7SA" => "Tesla",
        _ => "Unknown"
    };

    // ISO 3780 10th-character model-year code. The letter/digit sequence repeats
    // every 30 years, so each code is checked against every cycle that could
    // plausibly exist on the road today (OBD-II was mandated from 1996) and the
    // one candidate that is not in the future is used — no more guessing between
    // "2003 or 2033" and defaulting to the wrong, decades-later value.
    private static readonly Dictionary<char, int> VinYearBaseCodes = new()
    {
        ['A'] = 1980,
        ['B'] = 1981,
        ['C'] = 1982,
        ['D'] = 1983,
        ['E'] = 1984,
        ['F'] = 1985,
        ['G'] = 1986,
        ['H'] = 1987,
        ['J'] = 1988,
        ['K'] = 1989,
        ['L'] = 1990,
        ['M'] = 1991,
        ['N'] = 1992,
        ['P'] = 1993,
        ['R'] = 1994,
        ['S'] = 1995,
        ['T'] = 1996,
        ['V'] = 1997,
        ['W'] = 1998,
        ['X'] = 1999,
        ['Y'] = 2000,
        ['1'] = 2001,
        ['2'] = 2002,
        ['3'] = 2003,
        ['4'] = 2004,
        ['5'] = 2005,
        ['6'] = 2006,
        ['7'] = 2007,
        ['8'] = 2008,
        ['9'] = 2009,
    };

    private static string DecodeVinYear(char c)
    {
        if (!VinYearBaseCodes.TryGetValue(char.ToUpperInvariant(c), out var baseYear))
            return "Unknown";

        var currentYear = DateTime.Now.Year;
        var best = new[] { baseYear, baseYear + 30, baseYear + 60 }
            .Where(y => y >= 1996 && y <= currentYear)
            .OrderByDescending(y => y)
            .Cast<int?>()
            .FirstOrDefault();

        return best?.ToString() ?? "Unknown";
    }

    private sealed class VinApiResponse
    {
        public List<VinApiRow>? Results { get; set; }
    }

    private sealed class VinApiRow
    {
        public string? Make { get; set; }
        public string? Model { get; set; }
        public string? ModelYear { get; set; }
        public string? Trim { get; set; }
        public string? Trim2 { get; set; }
        public string? Series { get; set; }
        public string? Series2 { get; set; }
        public string? BodyClass { get; set; }
        public string? DriveType { get; set; }
        public string? EngineCylinders { get; set; }
        // vPIC field names are "DisplacementL" / "DisplacementCI".
        [System.Text.Json.Serialization.JsonPropertyName("DisplacementL")]
        public string? EngineDisplacementL { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("DisplacementCI")]
        public string? EngineDisplacementCi { get; set; }
        public string? EngineModel { get; set; }
        public string? FuelTypePrimary { get; set; }
        public string? TransmissionStyle { get; set; }
    }
}
