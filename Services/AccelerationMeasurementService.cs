using CarScanner.Database;
using CarScanner.Models;
using CarScanner.Services.OBD;
using System.Text.Json;

namespace CarScanner.Services;

/// <summary>
/// Measures a user-initiated performance run from OBD vehicle-speed samples.
/// OBD update frequency differs by vehicle and adapter, so results are labelled
/// as estimates rather than laboratory-grade timing.
/// </summary>
public sealed class AccelerationMeasurementService
{
    private readonly LiveDataEngine _liveData;
    private readonly AppDatabase _database;
    private readonly UnitSystemService _units;
    private readonly VehicleInfoService _vehicles;
    private readonly VehicleProfileResolverService _profiles;
    private readonly UserProfileService _users;
    private readonly RunGpsRecorder _gps;
    private readonly object _gate = new();

    private bool _isArmed;
    private DateTime? _startedAt;
    private DateTime? _previousAt;
    private int _previousSpeed;
    // Stationary starts: last sample at a standstill and the first sample that
    // moved after it, so timing begins when the car actually starts to roll
    // (~0.5 km/h) instead of at the last sample ≤ 3 km/h (a rollout).
    private DateTime? _standstillAt;
    private DateTime? _firstMoveAt;
    private int _firstMoveSpeed;
    private const double RollStartKph = 0.5;
    private int _samples;
    private readonly Dictionary<int, double> _splits = new();
    private bool _startConditionObserved;
    private double _distanceMeters;
    private double _peakAcceleration;
    private double _accelerationSum;
    private int _accelerationSamples;
    private bool _isPreviewing;
    private bool _previewOwnsLiveData;
    private bool _hasCurrentSpeed;
    private bool _hasCurrentRpm;
    private bool _hasCurrentThrottle;
    private DateTime? _lastSpeedAt;
    private PerformanceTestDefinition _selectedTest;
    private PerformanceTestDefinition _activeTest;
    private double _startSpeedKph;
    private double _targetSpeedKph = 100d;
    private double? _targetDistanceMeters;
    private double _trapSpeedKph;
    private int[] _splitTargetsKph;
    private readonly List<ObdTelemetrySample> _runSamples = new();
    public int CurrentSpeed { get; private set; }
    public int CurrentRpm { get; private set; }
    public int CurrentThrottle { get; private set; }
    public int MaximumSpeed { get; private set; }
    public int MaximumRpm { get; private set; }
    public double DistanceMeters { get; private set; }
    public double PeakAcceleration { get; private set; }
    public double AverageAcceleration => _accelerationSamples == 0 ? 0 : _accelerationSum / _accelerationSamples;
    public IReadOnlyDictionary<int, double> Splits { get { lock (_gate) return new Dictionary<int, double>(_splits); } }
    public IReadOnlyList<int> SplitTargetsKph { get { lock (_gate) return _splitTargetsKph.ToArray(); } }
    public IReadOnlyList<ObdTelemetrySample> RunSamples { get { lock (_gate) return _runSamples.ToArray(); } }

    public bool IsArmed { get { lock (_gate) return _isArmed; } }

    /// <summary>True while a run is being timed (after the car pulled away).</summary>
    public bool IsTiming { get { lock (_gate) return _isArmed && _startedAt.HasValue; } }

    /// <summary>True when armed and the start condition (standstill) was seen.</summary>
    public bool IsReadyToGo { get { lock (_gate) return _isArmed && !_startedAt.HasValue && _startConditionObserved; } }

    /// <summary>Real ECU speed samples per second over the last 2 seconds.</summary>
    public double SpeedSampleRate
    {
        get
        {
            lock (_gate)
            {
                var cutoff = DateTime.UtcNow.AddSeconds(-2);
                while (_speedSampleTimes.Count > 0 && _speedSampleTimes.Peek() < cutoff)
                    _speedSampleTimes.Dequeue();
                return _speedSampleTimes.Count / 2d;
            }
        }
    }

    private readonly Queue<DateTime> _speedSampleTimes = new();
    public bool HasCurrentSpeed { get { lock (_gate) return _hasCurrentSpeed; } }
    // Distinguishes "no ECU reading has arrived yet" from a genuine 0 value
    // (idle RPM at engine-off, or 0% throttle) so the UI never shows a
    // stale-looking zero before the adapter has actually answered.
    public bool HasCurrentRpm { get { lock (_gate) return _hasCurrentRpm; } }
    public bool HasCurrentThrottle { get { lock (_gate) return _hasCurrentThrottle; } }
    public DateTime? LastSpeedAt { get { lock (_gate) return _lastSpeedAt; } }
    public string Status { get; private set; } = "გაზომვისთვის დააკავშირე OBD2";
    public PerformanceTestDefinition CurrentTest { get { lock (_gate) return _isArmed ? _activeTest : _selectedTest; } }
    public string CurrentTestLabel => CurrentTest.DisplayName;
    public event EventHandler? StateChanged;
    public event EventHandler<AccelerationRun>? Completed;

    public AccelerationMeasurementSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new AccelerationMeasurementSnapshot(
                _isArmed,
                _hasCurrentSpeed,
                CurrentSpeed,
                _hasCurrentRpm,
                CurrentRpm,
                _hasCurrentThrottle,
                CurrentThrottle,
                _lastSpeedAt,
                Status,
                _isArmed ? _activeTest : _selectedTest,
                MaximumSpeed,
                MaximumRpm,
                DistanceMeters,
                PeakAcceleration,
                _accelerationSamples == 0 ? 0 : _accelerationSum / _accelerationSamples,
                new Dictionary<int, double>(_splits),
                _splitTargetsKph.ToArray(),
                _runSamples.ToArray());
        }
    }

    public AccelerationMeasurementService(
        LiveDataEngine liveData,
        AppDatabase database,
        UnitSystemService units,
        VehicleInfoService vehicles,
        VehicleProfileResolverService profiles,
        UserProfileService users,
        RunGpsRecorder gps)
    {
        _gps = gps;
        _liveData = liveData;
        _database = database;
        _units = units;
        _vehicles = vehicles;
        _profiles = profiles;
        _users = users;
        _selectedTest = PerformanceTests.Default(units);
        _activeTest = _selectedTest;
        // Initialize displayed split targets to match the default selected test
        // so the UI preview shows the correct checkpoints before a run starts.
        _splitTargetsKph = _selectedTest.SplitTargetsKph.ToArray();
        _liveData.LiveDataUpdated += OnLiveDataUpdated;
    }

    /// <summary>Changes the selected speed interval before a run starts.</summary>
    public bool SetTest(PerformanceTestDefinition test)
    {
        lock (_gate)
        {
            if (_isArmed || test.TargetSpeedKph <= test.StartSpeedKph)
                return false;
            _selectedTest = test;
            // Update split targets immediately so the preview and UI reflect
            // the newly selected performance test even before arming.
            _splitTargetsKph = test.SplitTargetsKph.ToArray();
            Status = $"{test.DisplayName}: {test.StartInstruction}";
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Starts a read-only, speed-first preview as soon as Performance Lab is
    /// opened. This is intentionally separate from arming a timed run: the
    /// speedometer must show real PID 01 0D samples before the user presses
    /// ARM.
    /// </summary>
    public async Task StartPreviewAsync(CancellationToken token = default, bool speedOnly = false)
    {
        bool startLiveData;
        lock (_gate)
        {
            _isPreviewing = true;
            if (!_isArmed)
                Status = "Vehicle Speed PID 01 0D-ის რეალურ პასუხს ველოდები…";
            startLiveData = !_liveData.IsRunning;
        }

        _liveData.SetPerformanceMode(true, speedOnly);
        StateChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            if (startLiveData)
            {
                await _liveData.StartAsync(token).ConfigureAwait(false);
                lock (_gate)
                    _previewOwnsLiveData = true;
            }
        }
        catch
        {
            lock (_gate)
                _isPreviewing = false;
            _liveData.SetPerformanceMode(false);
            throw;
        }
    }

    /// <summary>Returns the shared live-data engine to its regular profile.</summary>
    public async Task StopPreviewAsync()
    {
        bool stopLiveData;
        lock (_gate)
        {
            if (_isArmed)
                return;

            _isPreviewing = false;
            stopLiveData = _previewOwnsLiveData;
            _previewOwnsLiveData = false;
        }

        _liveData.SetPerformanceMode(false);
        if (stopLiveData)
            await _liveData.StopAsync().ConfigureAwait(false);
    }

    /// <summary>True while a run is timed from the phone GPS (no OBD adapter).</summary>
    public bool GpsMode { get; private set; }

    public async Task StartAsync(CancellationToken token = default)
    {
        StopGpsMode();
        await StartPreviewAsync(token).ConfigureAwait(false);
        Arm();

        // Phone GPS runs alongside: the ranking checks ECU speed against it.
        _ = _gps.StartAsync();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Times a run from the phone GPS when no OBD adapter is connected (or the
    /// car does not give its speed, e.g. Tesla). GPS updates about once a
    /// second, so the result is an estimate and is not ranked.
    /// </summary>
    public async Task<bool> StartGpsAsync()
    {
        StopGpsMode();
        await _gps.StartAsync().ConfigureAwait(false);
        if (!_gps.IsAvailable)
        {
            lock (_gate)
                Status = CarScanner.Services.Localization.LanguageCatalog.UsesEnglishBase
                    ? "GPS is not available. Allow location for GCar360 and try outdoors."
                    : "GPS მიუწვდომელია. მიეცი GCar360-ს ლოკაციის ნებართვა და სცადე ღია ცის ქვეშ.";
            StateChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }

        GpsMode = true;
        _gps.SpeedSample += OnGpsSpeed;
        Arm();
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void StopGpsMode()
    {
        if (!GpsMode)
            return;
        _gps.SpeedSample -= OnGpsSpeed;
        GpsMode = false;
    }

    // GPS speed jitters by a few km/h while standing still; below 4 km/h it
    // counts as 0 so the run does not start by itself.
    private void OnGpsSpeed(DateTime utc, double kph) =>
        OnLiveDataUpdated(this, new LiveDataModel { Timestamp = utc, Speed = kph < 4 ? 0 : (int)Math.Round(kph), HasSpeed = true });

    private void Arm()
    {
        lock (_gate)
        {
            _activeTest = _selectedTest;
            _startSpeedKph = _activeTest.StartSpeedKph;
            _targetSpeedKph = _activeTest.TargetSpeedKph;
            _targetDistanceMeters = _activeTest.TargetDistanceMeters;
            _trapSpeedKph = 0;
            _splitTargetsKph = _activeTest.SplitTargetsKph.ToArray();
            _isArmed = true;
            _startedAt = null;
            _previousAt = null;
            _previousSpeed = 0;
            ClearRollStart();
            _samples = 0;
            _splits.Clear(); MaximumSpeed = MaximumRpm = 0;
            _startConditionObserved = false; _distanceMeters = 0; _peakAcceleration = 0; _accelerationSum = 0; _accelerationSamples = 0;
            _runSamples.Clear();
            DistanceMeters = 0; PeakAcceleration = 0;
            Status = $"READY — {_activeTest.StartInstruction}";
        }
    }

    public void Cancel(string message = "გაზომვა გაუქმდა")
    {
        lock (_gate)
        {
            _isArmed = false;
            _startedAt = null;
            Status = message;
        }

        StopGpsMode();
        _gps.Stop();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnLiveDataUpdated(object? sender, LiveDataModel data)
    {
        // In GPS mode only GPS samples time the run.
        if (GpsMode && !ReferenceEquals(sender, this))
            return;

        DateTime? completionAt = null;
        DateTime? startedAt = null;
        int sampleCount = 0;
        var publishState = false;
        CompletedRunDetails? completedDetails = null;

        lock (_gate)
        {
            var now = data.Timestamp.ToUniversalTime();

            if (!data.HasSpeed)
            {
                // Do not leave an old value looking like a current ECU
                // response after the speed PID has been silent for a while.
                if (_lastSpeedAt is { } lastSpeedAt && now - lastSpeedAt > TimeSpan.FromSeconds(3))
                    _hasCurrentSpeed = false;

                if (_isPreviewing || _isArmed)
                {
                    Status = "Vehicle Speed PID 01 0D-მა ECU-დან პასუხი არ დააბრუნა. სიჩქარე არ არის გამოგონილი.";
                    publishState = true;
                }
            }
            else
            {
                CurrentSpeed = data.Speed;
                _hasCurrentSpeed = true;
                _lastSpeedAt = now;
                _speedSampleTimes.Enqueue(DateTime.UtcNow);
                while (_speedSampleTimes.Count > 200)
                    _speedSampleTimes.Dequeue();
                if (data.HasRpm) { CurrentRpm = data.Rpm; _hasCurrentRpm = true; }
                if (data.HasThrottlePosition) { CurrentThrottle = data.ThrottlePosition; _hasCurrentThrottle = true; }
                publishState = true;

                if (!_isArmed)
                {
                    if (_isPreviewing)
                        Status = "რეალური სიჩქარე მიიღება ECU-დან (PID 01 0D).";
                }
                else
                {
                    MaximumSpeed = Math.Max(MaximumSpeed, data.Speed);
                    MaximumRpm = Math.Max(MaximumRpm, CurrentRpm);
                    _runSamples.Add(new ObdTelemetrySample { Timestamp = now, Pid = 0x0D, Name = "Vehicle speed", Unit = "km/h", Value = data.Speed });
                    if (_runSamples.Count > 1000)
                        _runSamples.RemoveAt(0);

                    if (_startedAt.HasValue)
                    {
                        // The car stopped / slowed below the start speed: the
                        // attempt is not valid any more, wait for a new start.
                        var dropped = _activeTest.RequiresStationaryStart
                            ? data.Speed <= 0
                            : data.Speed < _startSpeedKph - 3d;
                        if (dropped)
                        {
                            ResetAttempt(now, data.Speed);
                            Status = _activeTest.RequiresStationaryStart
                                ? "მანქანა გაჩერდა — ცდა თავიდან დაიწყება მოძრაობისას."
                                : $"სიჩქარე დაეცა — ცდა თავიდან დაიწყება {_startSpeedKph:0} km/h-ის გადაკვეთისას.";
                        }
                        else if ((now - _startedAt.Value).TotalSeconds > MaxRunSeconds(_activeTest))
                        {
                            ResetAttempt(now, data.Speed);
                            _isArmed = false;
                            Status = "დრო ამოიწურა — საბოლოო სიჩქარეს ვერ მიაღწიე. სცადე თავიდან.";
                        }
                    }

                    if (_isArmed && !_startedAt.HasValue)
                    {
                        var allowedStartCeiling = _activeTest.RequiresStationaryStart ? 3d : _startSpeedKph;
                        if (!_startConditionObserved && data.Speed <= allowedStartCeiling)
                        {
                            _startConditionObserved = true;
                            Status = _activeTest.RequiresStationaryStart
                                ? "READY — GO ავტომატურად დაიწყება მოძრაობისას."
                                : $"READY — დრო ავტომატურად დაიწყება {_startSpeedKph:0} km/h-ის გადაკვეთისას.";
                            _previousAt = now;
                            _previousSpeed = data.Speed;
                            TrackRollStart(now, data.Speed);
                        }
                        else if (!_startConditionObserved)
                        {
                            Status = $"ტესტის დასაწყებად ჯერ {_startSpeedKph:0} km/h-მდე ან ქვემოთ ჩამოდი.";
                            _previousAt = now;
                            _previousSpeed = data.Speed;
                        }
                        else if (data.Speed <= (_activeTest.RequiresStationaryStart
                                     ? allowedStartCeiling
                                     : _startSpeedKph))
                        {
                            _previousAt = now;
                            _previousSpeed = data.Speed;
                            TrackRollStart(now, data.Speed);
                        }
                        else
                        {
                            // Time is derived from two real ECU speed samples;
                            // it is never started by a UI timer or demo value.
                            _startedAt = _activeTest.RequiresStationaryStart
                                ? RollStartTime(now, data.Speed)
                                : InterpolateSpeedTime(
                                    _previousAt ?? now,
                                    _previousSpeed,
                                    now,
                                    data.Speed,
                                    _startSpeedKph);
                            Status = $"GO! — {CurrentTestLabel} იზომება რეალური ECU სიჩქარით…";
                        }
                    }

                    if (_isArmed && _startedAt.HasValue && data.Speed > _startSpeedKph)
                    {
                        _samples++;
                        var previousAt = _previousAt ?? now;
                        var elapsedSeconds = Math.Max(0, (now - previousAt).TotalSeconds);
                        DateTime? distanceReachedAt = null;
                        if (elapsedSeconds > 0)
                        {
                            var distanceBefore = _distanceMeters;
                            var acceleration = ((data.Speed - _previousSpeed) / 3.6) / elapsedSeconds;
                            if (acceleration > 0)
                            {
                                _peakAcceleration = Math.Max(_peakAcceleration, acceleration);
                                _accelerationSum += acceleration;
                                _accelerationSamples++;
                            }
                            _distanceMeters += ((_previousSpeed + data.Speed) / 2d / 3.6) * elapsedSeconds;
                            DistanceMeters = _distanceMeters;
                            PeakAcceleration = _peakAcceleration;

                            // Distance runs (¼ / ⅛ mile): the finish line is
                            // placed inside this sample interval by distance.
                            if (_targetDistanceMeters is { } finishMeters && _distanceMeters >= finishMeters && _distanceMeters > distanceBefore)
                            {
                                var fraction = Math.Clamp((finishMeters - distanceBefore) / (_distanceMeters - distanceBefore), 0d, 1d);
                                distanceReachedAt = previousAt.AddTicks((long)((now - previousAt).Ticks * fraction));
                                _trapSpeedKph = _previousSpeed + (data.Speed - _previousSpeed) * fraction;
                                _distanceMeters = finishMeters;
                                DistanceMeters = finishMeters;
                            }
                        }

                        foreach (var split in _splitTargetsKph)
                        {
                            if (_splits.ContainsKey(split) || data.Speed < split)
                                continue;
                            var splitAt = InterpolateSpeedTime(
                                _previousAt ?? now,
                                _previousSpeed,
                                now,
                                data.Speed,
                                split);
                            _splits[split] = Math.Max(0, (splitAt - _startedAt.Value).TotalSeconds);
                        }

                        if (distanceReachedAt is not null || (_targetDistanceMeters is null && data.Speed >= _targetSpeedKph))
                        {
                            completionAt = distanceReachedAt ?? InterpolateTargetTime(
                                _previousAt ?? now,
                                _previousSpeed,
                                now,
                                data.Speed);
                            startedAt = _startedAt;
                            sampleCount = _samples;
                            // Record the final target speed itself in the same
                            // splits dictionary so it renders as one more field
                            // alongside 20/40/60/80, instead of only appearing
                            // in the separate "latest result" summary.
                            if (_targetDistanceMeters is null)
                                _splits[(int)Math.Round(_targetSpeedKph)] =
                                    Math.Max(0, (completionAt.Value - _startedAt.Value).TotalSeconds);
                            _isArmed = false;
                            Status = "შედეგი ინახება…";
                            completedDetails = new CompletedRunDetails(
                                _activeTest,
                                _targetDistanceMeters is null ? MaximumSpeed : (int)Math.Round(_trapSpeedKph),
                                MaximumRpm,
                                _distanceMeters,
                                _peakAcceleration,
                                new Dictionary<int, double>(_splits),
                                _runSamples.ToArray());
                        }
                        else
                        {
                            _previousAt = now;
                            _previousSpeed = data.Speed;
                        }
                    }
                }
            }
        }

        if (publishState)
            StateChanged?.Invoke(this, EventArgs.Empty);

        if (completionAt.HasValue && startedAt.HasValue && completedDetails is not null)
        {
            // Beep + vibration at the target speed, so the driver does not need to look at the screen.
            CarScanner.Helpers.PerformanceBeep.Play();
            _ = SaveCompletedRunAsync(startedAt.Value, completionAt.Value, sampleCount, completedDetails);
        }
    }

    private async Task SaveCompletedRunAsync(DateTime startedAt, DateTime finishedAt, int sampleCount, CompletedRunDetails details)
    {
        var seconds = Math.Max(0, (finishedAt - startedAt).TotalSeconds);

        // Evidence for the ranking: ECU speed and phone GPS speed, both as
        // (seconds from the start, km/h). GPS gets a few seconds extra on
        // both sides because it lags behind the ECU.
        var startUtc = startedAt.ToUniversalTime();
        var speedTrace = details.Samples
            .Select(s => new[] { Math.Round((s.Timestamp.ToUniversalTime() - startUtc).TotalSeconds, 2), Math.Round(s.Value, 1) })
            .Where(p => p[0] >= -3)
            .ToArray();
        var gpsSamples = _gps.Between(startUtc.AddSeconds(-3), finishedAt.ToUniversalTime().AddSeconds(4));
        var timedByGps = GpsMode;
        StopGpsMode();
        _gps.Stop();
        var gpsTrace = gpsSamples
            .Select(s => new[]
            {
                Math.Round((s.Utc - startUtc).TotalSeconds, 2), Math.Round(s.Kph, 1),
                Math.Round(s.Latitude, 6), Math.Round(s.Longitude, 6), Math.Round(s.AccuracyMeters, 1)
            })
            .ToArray();
        var gpsCheck = RunGpsCheck.Check(
            speedTrace.Select(p => (p[0], p[1])).ToArray(),
            gpsTrace.Select(p => (p[0], p[1])).ToArray());
        var vehicle = _vehicles.CurrentVehicle;
        var resolved = _profiles.Current;
        var validVin = vehicle is not null && VinResponseParser.IsValidVin(vehicle.VIN);
        var verifiedModel = resolved is not null &&
            resolved.HasValidVin && resolved.HasVerifiedModel && resolved.HasVerifiedYear;
        // Ranked tests: 0–100 km/h, 0–60 mph and ¼ mile.
        var ranked = PerformanceTestDefinition.IsRanked(details.Test.Id) && details.Test.StartSpeedKph <= 0.5;
        var (minSeconds, minSamples) = details.Test.Id switch
        {
            "quarter-mile" => (6.0, 8),
            _ => (1.2, 5)
        };
        var reachedTarget = details.Test.IsDistanceTest
            ? details.DistanceMeters >= details.Test.TargetDistanceMeters!.Value - 1
            : details.MaxSpeedKph >= details.Test.TargetSpeedKph - 1;
        var plausible = seconds >= minSeconds && seconds <= 60 && sampleCount >= minSamples && reachedTarget;
        var gpsOk = gpsCheck.Status == RunGpsCheck.Passed;
        // GPS-timed runs are estimates (≈1 update a second): never ranked.
        var eligible = !timedByGps && ranked && validVin && verifiedModel && plausible && gpsOk;
        var en = CarScanner.Services.Localization.LanguageCatalog.UsesEnglishBase;
        var rejection = eligible ? string.Empty
            : timedByGps ? (en ? "Timed by phone GPS (no adapter): an estimate, not ranked." : "გაიზომა ტელეფონის GPS-ით (ადაპტერის გარეშე): შეფასებაა და რეიტინგში არ შედის.")
            : !ranked ? (en ? "Only 0–100 km/h, 0–60 mph and ¼ mile runs are ranked." : "რეიტინგში მიიღება მხოლოდ 0–100 km/h, 0–60 mph და ¼ მილი.")
            : !validVin || !verifiedModel ? (en ? "The model and year are not confirmed by VIN." : "VIN-ით მოდელი და წელი დადასტურებული არ არის.")
            : seconds < minSeconds ? (en ? "The result is physically impossible." : "შედეგი ფიზიკურად შეუძლებლად სწრაფია.")
            : sampleCount < minSamples ? (en ? "Not enough ECU speed samples." : "ECU სიჩქარის ნიმუშები საკმარისი არ არის.")
            : !plausible ? (en ? "The measurement does not meet the ranking rules." : "გაზომვის მონაცემები რეიტინგის წესებს არ აკმაყოფილებს.")
            : gpsCheck.Status == RunGpsCheck.Missing
                ? (en ? "No GPS during the run. Turn on location for GCar360 and measure again outdoors." : "გაზომვისას GPS არ იყო. ჩართე ლოკაცია GCar360-ისთვის და გაზომე ღია ცის ქვეშ.")
                : (en ? $"GPS speed does not match the car's speed (difference {gpsCheck.MedianDiffKph:0} km/h)." : $"GPS-ის სიჩქარე მანქანის სიჩქარეს არ ემთხვევა (სხვაობა {gpsCheck.MedianDiffKph:0} km/h).");
        var run = new AccelerationRun
        {
            StartedAt = startedAt.ToLocalTime(),
            FinishedAt = finishedAt.ToLocalTime(),
            // VIN reads send an OBD command and must not interrupt a live run.
            // The run remains available as a device-local history entry.
            VIN = validVin ? vehicle!.VIN.Trim().ToUpperInvariant() : string.Empty,
            DurationSeconds = Math.Round(seconds, 2),
            SampleCount = sampleCount,
            Source = timedByGps ? $"GPS · {details.Test.DisplayName}" : $"OBD · {details.Test.DisplayName}",
            Confidence = timedByGps ? "Low" : sampleCount >= 25 ? "High" : sampleCount >= 12 ? "Medium" : "Low",
            TestId = details.Test.Id,
            TestName = details.Test.DisplayName,
            StartSpeedKph = details.Test.StartSpeedKph,
            TargetSpeedKph = details.Test.IsDistanceTest ? 0 : details.Test.TargetSpeedKph,
            MaxSpeedKph = details.MaxSpeedKph,
            MaxRpm = details.MaxRpm,
            DistanceMeters = Math.Round(details.DistanceMeters, 1),
            PeakAcceleration = Math.Round(details.PeakAcceleration, 2),
            SplitsJson = JsonSerializer.Serialize(details.Splits
                .OrderBy(item => item.Key)
                .ToDictionary(item => item.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), item => Math.Round(item.Value, 2))),
            UserName = string.IsNullOrWhiteSpace(_users.Name) ? "GCar360 Driver" : _users.Name.Trim(),
            VehicleDisplayName = resolved?.DisplayName ?? string.Empty,
            VinVerified = validVin && verifiedModel,
            LeaderboardEligible = eligible,
            LeaderboardRejectionReason = rejection,
            SpeedTraceJson = JsonSerializer.Serialize(speedTrace),
            GpsTraceJson = JsonSerializer.Serialize(gpsTrace),
            GpsStatus = gpsCheck.Status,
            GpsMedianDiffKph = gpsCheck.MedianDiffKph
        };

        try
        {
            await _database.SaveAccelerationRunAsync(run);
            lock (_gate)
                Status = details.Test.IsDistanceTest
                    ? $"{details.Test.DisplayName}: {run.DurationSeconds:F2} s @ {_units.FormatSpeed(details.MaxSpeedKph)} ({(timedByGps ? "GPS" : "OBD")})"
                    : $"{details.Test.DisplayName}: {run.DurationSeconds:F2} s ({(timedByGps ? "GPS" : "OBD")})";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Acceleration run save failed: {ex}");
            lock (_gate)
                Status = $"{details.Test.DisplayName}: {run.DurationSeconds:F2} წმ — შენახვა ვერ მოხერხდა.";
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Completed?.Invoke(this, run);
            StateChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>Clears the current attempt but stays armed (unless the caller disarms).</summary>
    private void ClearRollStart()
    {
        _standstillAt = null;
        _firstMoveAt = null;
        _firstMoveSpeed = 0;
    }

    /// <summary>Remembers the standstill sample and the first moving sample after it.</summary>
    private void TrackRollStart(DateTime at, int speed)
    {
        if (!_activeTest.RequiresStationaryStart)
            return;

        if (speed <= 0)
        {
            _standstillAt = at;
            _firstMoveAt = null;
            _firstMoveSpeed = 0;
        }
        else if (_firstMoveAt is null)
        {
            _firstMoveAt = at;
            _firstMoveSpeed = speed;
        }
    }

    /// <summary>
    /// The moment the car crossed ~0.5 km/h, interpolated between the last
    /// standstill sample and the first moving one. Without a standstill
    /// sample (car already creeping when armed) it is extrapolated back from
    /// the two latest samples, at most 2 s — never later than the old start.
    /// </summary>
    private DateTime RollStartTime(DateTime now, int speed)
    {
        var moveAt = _firstMoveAt ?? now;
        var moveSpeed = _firstMoveAt is null ? speed : _firstMoveSpeed;

        if (_standstillAt is { } stillAt && moveAt > stillAt && moveSpeed > 0)
            return InterpolateSpeedTime(stillAt, 0, moveAt, moveSpeed, RollStartKph);

        var previousAt = _previousAt ?? now;
        if (now <= previousAt || speed <= _previousSpeed)
            return previousAt;

        var slopeKphPerTick = (speed - _previousSpeed) / (double)(now - previousAt).Ticks;
        var backTicks = (long)Math.Max(0, (_previousSpeed - RollStartKph) / slopeKphPerTick);
        backTicks = Math.Min(backTicks, TimeSpan.FromSeconds(2).Ticks);
        return previousAt.AddTicks(-backTicks);
    }

    private void ResetAttempt(DateTime now, int speed)
    {
        _startedAt = null;
        ClearRollStart();
        TrackRollStart(now, speed);
        _startConditionObserved = _activeTest.RequiresStationaryStart ? speed <= 3 : speed <= _startSpeedKph;
        _previousAt = now;
        _previousSpeed = speed;
        _samples = 0;
        _splits.Clear();
        _distanceMeters = 0;
        _peakAcceleration = 0;
        _accelerationSum = 0;
        _accelerationSamples = 0;
        DistanceMeters = 0;
        PeakAcceleration = 0;
        MaximumSpeed = speed;
        MaximumRpm = CurrentRpm;
        _runSamples.Clear();
    }

    /// <summary>A run that takes longer than this is not a real acceleration test.</summary>
    private static double MaxRunSeconds(PerformanceTestDefinition test) =>
        test.IsDistanceTest
            ? 45d
            : Math.Max(30d, 20d + (test.TargetSpeedKph - test.StartSpeedKph) * 0.6d);

    private sealed record CompletedRunDetails(
        PerformanceTestDefinition Test,
        int MaxSpeedKph,
        int MaxRpm,
        double DistanceMeters,
        double PeakAcceleration,
        Dictionary<int, double> Splits,
        ObdTelemetrySample[] Samples);

    private DateTime InterpolateTargetTime(
        DateTime previousAt, int previousSpeed, DateTime currentAt, int currentSpeed)
        => InterpolateSpeedTime(previousAt, previousSpeed, currentAt, currentSpeed, _targetSpeedKph);

    private static DateTime InterpolateSpeedTime(
        DateTime previousAt, int previousSpeed, DateTime currentAt, int currentSpeed, double targetSpeed)
    {
        if (currentSpeed <= previousSpeed || currentAt <= previousAt)
            return currentAt;

        var ratio = (targetSpeed - previousSpeed) / (double)(currentSpeed - previousSpeed);
        ratio = Math.Clamp(ratio, 0d, 1d);
        return previousAt.AddTicks((long)((currentAt - previousAt).Ticks * ratio));
    }
}

public sealed record AccelerationMeasurementSnapshot(
    bool IsArmed,
    bool HasCurrentSpeed,
    int CurrentSpeed,
    bool HasCurrentRpm,
    int CurrentRpm,
    bool HasCurrentThrottle,
    int CurrentThrottle,
    DateTime? LastSpeedAt,
    string Status,
    PerformanceTestDefinition CurrentTest,
    int MaximumSpeed,
    int MaximumRpm,
    double DistanceMeters,
    double PeakAcceleration,
    double AverageAcceleration,
    IReadOnlyDictionary<int, double> Splits,
    IReadOnlyList<int> SplitTargetsKph,
    IReadOnlyList<ObdTelemetrySample> RunSamples);
