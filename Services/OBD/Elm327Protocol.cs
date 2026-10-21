using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CarScanner.Models;
using CarScanner.Services.UDS;
using Microsoft.Extensions.Logging;

namespace CarScanner.Services.OBD
{
    public enum ObdProtocol
    {
        Unknown = 0,
        Automatic = 1,
        SAE_J1850_PWM = 2,
        SAE_J1850_VPW = 3,
        ISO_9141_2 = 4,
        ISO_14230_4_KWP_5BAUD = 5,
        ISO_14230_4_KWP_FAST = 6,
        ISO_15765_4_CAN_11BIT_500K = 7,
        ISO_15765_4_CAN_29BIT_500K = 8,
        ISO_15765_4_CAN_11BIT_250K = 9,
        ISO_15765_4_CAN_29BIT_250K = 10,
        SAE_J1939_CAN = 11
    }

    public sealed class Elm327InitializationException : Exception
    {
        public Elm327InitializationException(string message) : base(message) { }
        public Elm327InitializationException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class Elm327TimeoutException : Exception
    {
        public Elm327TimeoutException(string message) : base(message) { }
    }

    public sealed class Elm327CommandException : Exception
    {
        public string RawResponse { get; }

        /// <summary>
        /// Set when the adapter itself refused a setup command it was required to accept (for example '?' on a chip that
        /// does not know the command): the problem is the adapter's command set, not the car.
        /// </summary>
        public string? UnsupportedCommand { get; init; }

        public Elm327CommandException(string message, string rawResponse) : base(message)
        {
            RawResponse = rawResponse ?? string.Empty;
        }
    }

    public sealed class Elm327NotConnectedException : Exception
    {
        public Elm327NotConnectedException(string message) : base(message) { }
    }

    /// <summary>
    /// Production-grade ELM327 protocol handler built directly on top of the existing
    /// IBluetoothService. Responsible ONLY for adapter initialization, reset, command
    /// send/receive with retry + timeout, response cleaning, status-token detection,
    /// and protocol detection. Does NOT parse PIDs, VIN, or DTCs.
    /// </summary>
    public sealed class Elm327Protocol : IAsyncDisposable
    {
        #region Constants

        private const string CMD_RESET = "ATZ";
        private const string CMD_ECHO_OFF = "ATE0";
        private const string CMD_LINEFEEDS_OFF = "ATL0";
        private const string CMD_SPACES_ON = "ATS1";
        private const string CMD_HEADERS_OFF = "ATH0";
        private const string CMD_SET_PROTOCOL_AUTO = "ATSP0";
        private const string CMD_ADAPTIVE_TIMING_2 = "ATAT2";
        private const string CMD_SPACES_OFF = "ATS0";
        private const string CMD_DESCRIBE_PROTOCOL_NUM = "ATDPN";
        private const string CMD_DESCRIBE_PROTOCOL = "ATDP";
        private const string CMD_TEST_PID_0100 = "0100";

        private const string PROMPT_TERMINATOR = ">";

        private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ResetCommandTimeout = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan InitCommandTimeout = TimeSpan.FromSeconds(6);
        private static readonly TimeSpan ProtocolSearchTimeout = TimeSpan.FromSeconds(12);
        private static readonly TimeSpan PostResetSettleDelay = TimeSpan.FromMilliseconds(600);

        private const int DefaultMaxRetries = 3;
        private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(250);

        private static readonly string[] NegativeStatusTokens =
        {
            "NO DATA",
            "STOPPED",
            "ERROR",
            "UNABLE TO CONNECT",
            "BUS INIT: ERROR",
            "BUS INIT... ERROR",
            "BUS BUSY",
            "BUFFER FULL",
            "CAN ERROR",
            "FB ERROR",
            "DATA ERROR",
            "<DATA ERROR",
            "ACT ALERT",
            "LP ALERT",
            "LV RESET",
            "RX ERROR",
            "?"
        };

        private static readonly string[] TransientStatusTokens =
        {
            "SEARCHING...",
            "SEARCHING",
            "BUS INIT...",
            "BUS INIT",
            "BUSINIT...",
            "BUSINIT"
        };

        #endregion

        #region Fields

        private readonly IBluetoothService _bluetooth;
        private readonly ILogger<Elm327Protocol> _logger;
        // Serializes complete adapter modes/sessions. A sequence such as
        // ATH1 -> ATSH -> ATCRA -> UDS request must be indivisible; otherwise
        // a concurrent generic PID request can inherit the OEM header/filter.
        private readonly SemaphoreSlim _sessionGate = new(1, 1);
        private readonly SemaphoreSlim _commandGate = new(1, 1);

        /// <summary>Backs the polling off when the adapter shows strain; see <see cref="AdapterLoadGovernor"/>.</summary>
        public AdapterLoadGovernor Load { get; } = new();
        private long _lastDataCommandEndTicks = System.Diagnostics.Stopwatch.GetTimestamp();

        private volatile bool _isInitialized;
        private volatile bool _disposed;
        private ObdProtocol _currentProtocol = ObdProtocol.Unknown;
        private string? _currentProtocolRawDescription;

        #endregion

        #region Public Properties

        public bool IsInitialized => _isInitialized;

        public ObdProtocol CurrentProtocol => _currentProtocol;

        public string? CurrentProtocolDescription => _currentProtocolRawDescription;

        #endregion

        #region Constructor

        public Elm327Protocol(
            IBluetoothService bluetooth,
            ILogger<Elm327Protocol> logger)
        {
            _bluetooth = bluetooth ?? throw new ArgumentNullException(nameof(bluetooth));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _bluetooth.ConnectionChanged += OnConnectionChanged;
            Load.LevelChanged += (from, to, reason) =>
                LinkEventLog.Add($"Load: {from} -> {to} ({reason})");
        }

        #endregion

        #region Public API

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // A fresh session starts with full polling; the recovery path calls InitializeCoreAsync directly and keeps its level.
                if (!_isInitialized)
                    Load.Reset();
                await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sessionGate.Release();
            }
        }

        private async Task InitializeCoreAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            EnsureConnected();
            if (_isInitialized)
            {
                _logger.LogDebug("ELM327 is already initialized; skipping reset/reinitialization.");
                return;
            }

            _logger.LogInformation("Starting ELM327 initialization sequence...");
            _isInitialized = false;
            _currentProtocol = ObdProtocol.Unknown;
            _currentProtocolRawDescription = null;

            try
            {
                await ExecuteResetInternalAsync(cancellationToken).ConfigureAwait(false);

                await SendInitCommandAsync(CMD_ECHO_OFF, "disable echo", cancellationToken).ConfigureAwait(false);
                await SendInitCommandAsync(CMD_LINEFEEDS_OFF, "disable linefeeds", cancellationToken).ConfigureAwait(false);
                await SendInitCommandAsync(CMD_SPACES_ON, "keep spaces in responses", cancellationToken).ConfigureAwait(false);
                await SendInitCommandAsync(CMD_HEADERS_OFF, "disable headers", cancellationToken).ConfigureAwait(false);
                await SendInitCommandAsync(CMD_SET_PROTOCOL_AUTO, "set protocol auto", cancellationToken).ConfigureAwait(false);
                _adaptiveTimingAccepted = await SendInitCommandAsync(CMD_ADAPTIVE_TIMING_2, "adaptive timing 2", cancellationToken).ConfigureAwait(false);

                string? probeResponse = null;
                Elm327TimeoutException? probeTimeout = null;
                try
                {
                    probeResponse = await SendCommandInternalAsync(
                        CMD_TEST_PID_0100,
                        ProtocolSearchTimeout,
                        DefaultMaxRetries,
                        treatNegativeStatusAsError: false,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Elm327TimeoutException ex)
                {
                    probeTimeout = ex;
                }

                // Older cars (K-line / J1850) and some clones never finish the
                // automatic search: try every protocol explicitly.
                if (probeTimeout is not null || ProbeNeedsProtocolFallback(probeResponse ?? string.Empty))
                {
                    var fallbackResponse = await TryExplicitProtocolsAsync(cancellationToken).ConfigureAwait(false);
                    if (fallbackResponse is not null)
                    {
                        probeResponse = fallbackResponse;
                    }
                    else if (probeTimeout is not null)
                    {
                        throw probeTimeout;
                    }
                }

                LogInitProbe(probeResponse, probeTimeout is not null);
                ValidateInitialProbeResponse(probeResponse ?? string.Empty);

                await DetectAndCacheProtocolAsync(cancellationToken).ConfigureAwait(false);

                _isInitialized = true;
                _logger.LogInformation(
                    "ELM327 initialization completed successfully. Protocol: {Protocol} ({Description})",
                    _currentProtocol, _currentProtocolRawDescription ?? "unknown");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("ELM327 initialization was cancelled.");
                _isInitialized = false;
                throw;
            }
            catch (Elm327InitializationException)
            {
                _isInitialized = false;
                throw;
            }
            catch (Exception ex)
            {
                _isInitialized = false;
                _logger.LogError(ex, "Unexpected error during ELM327 initialization.");
                throw new Elm327InitializationException(
                    "Unexpected error during ELM327 initialization.", ex);
            }
        }


        /// <summary>
        /// Executes one ISO-TP/UDS request as an atomic ELM327 session. The
        /// adapter supplies the ISO-TP PCI byte for the outgoing request and
        /// automatically sends flow-control frames for a multi-frame reply.
        /// Verified OEM addressing is mandatory at the call site.
        /// </summary>
        public async Task<string> SendCanIsoTpPayloadAsync(
            ReadOnlyMemory<byte> payload,
            int transmitCanId,
            int receiveCanId,
            bool extended29Bit,
            TimeSpan timeout,
            CancellationToken token = default)
        {
            if (payload.Length is < 1 or > 7)
                throw new NotSupportedException(
                    "This ELM327 transport safely supports ISO-TP requests up to 7 payload bytes. Larger requests require a verified adapter-specific segmented transmit implementation.");

            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout));

            ValidateCanIdentifiers(transmitCanId, receiveCanId, extended29Bit);
            ThrowIfDisposed();
            EnsureConnected();

            await _sessionGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await RecoverIfDueAsync(token).ConfigureAwait(false);
                if (!IsCanProtocol(_currentProtocol))
                    throw new NotSupportedException(
                        $"Active vehicle protocol '{_currentProtocol}' is not ISO 15765 CAN.");

                if (extended29Bit != Is29BitCanProtocol(_currentProtocol))
                    throw new NotSupportedException(
                        $"Requested {(extended29Bit ? "29-bit" : "11-bit")} addressing does not match active protocol '{_currentProtocol}'.");

                var rejectedSetup = new List<string>();
                var receiveFilter = PhysicalFilterState.None;

                try
                {
                    receiveFilter = await ConfigureCanCoreAsync(
                        transmitCanId,
                        receiveCanId,
                        extended29Bit,
                        automaticFormatting: true,
                        rejectedSetup,
                        token).ConfigureAwait(false);

                    var command = string.Concat(payload.ToArray().Select(b => b.ToString("X2")));
                    var requestStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                    var answer = await SendReadRequestAsync(command, timeout, receiveCanId, payload.Span[0], token).ConfigureAwait(false);
                    LogRead(transmitCanId, receiveCanId, extended29Bit, command, answer, requestStarted);

                    // No hardware filter on this adapter: keep only the asked module frames, as a hardware filter would.
                    if ((receiveFilter & PhysicalFilterState.Hardware) == 0)
                        answer = KeepOnlyFramesFrom(answer, receiveCanId, extended29Bit);

                    ObserveModule(transmitCanId, receiveCanId, extended29Bit, command, payload.Span[0], answer);
                    return answer;
                }
                finally
                {
                    // Never expose a manufacturer-specific physical header or
                    // receive filter to the next generic OBD request.
                    if ((receiveFilter & PhysicalFilterState.NeedsClear) != 0 &&
                        !await TryResetReceiveFilterAsync(CancellationToken.None).ConfigureAwait(false))
                    {
                        _isInitialized = false;
                    }

                    if (_isInitialized)
                    {
                        try
                        {
                            await RestoreGenericCanSettingsCoreAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception restoreError)
                        {
                            _logger.LogWarning(restoreError, "Could not restore the generic CAN settings after an ISO-TP read.");
                        }
                    }

                    _lastPhysicalReadSetupNote = DescribePhysicalSetup(rejectedSetup, receiveFilter);
                    LogReadSetup(transmitCanId, extended29Bit, _lastPhysicalReadSetupNote);
                }
            }
            finally
            {
                _sessionGate.Release();
            }
        }

        /// <summary>
        /// Sends one physically addressed, read-only CAN diagnostic request.
        /// Only UDS 0x19/0x22 are accepted. This path can temporarily switch
        /// between 11-bit and 29-bit ISO-15765 at the already-detected bus
        /// speed, then restores the original generic protocol/settings.
        /// Optional ELM CAN extended addressing (ATCEA) is supported for
        /// read-only BMW-style OBDb definitions.
        /// </summary>
        public Task<string> SendPhysicalCanReadOnlyPayloadAsync(
            ReadOnlyMemory<byte> payload,
            int transmitCanId,
            TimeSpan timeout,
            CancellationToken token = default) =>
            SendPhysicalCanReadOnlyPayloadAsync(
                payload,
                transmitCanId,
                receiveCanId: null,
                extended29Bit: false,
                extendedAddress: null,
                timeout,
                token);

        public async Task<string> SendPhysicalCanReadOnlyPayloadAsync(
            ReadOnlyMemory<byte> payload,
            int transmitCanId,
            int? receiveCanId,
            bool extended29Bit,
            byte? extendedAddress,
            TimeSpan timeout,
            CancellationToken token = default)
        {
            if (payload.Length is < 1 or > 7)
                throw new NotSupportedException(
                    "Read-only physical CAN requests are limited to 1-7 payload bytes.");
            // 0x21 = KWP2000 ReadDataByLocalIdentifier (read-only, Toyota/Lexus).
            if (payload.Span[0] is not (0x19 or 0x21 or 0x22))
                throw new InvalidOperationException(
                    "Only read-only diagnostic services 0x19, 0x21 and 0x22 are allowed by this path.");
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout));

            var maxCanId = extended29Bit ? 0x1FFFFFFF : 0x7FF;
            if (transmitCanId is < 0 || transmitCanId > maxCanId)
                throw new ArgumentOutOfRangeException(nameof(transmitCanId));
            if (receiveCanId.HasValue &&
                (receiveCanId.Value < 0 || receiveCanId.Value > maxCanId))
                throw new ArgumentOutOfRangeException(nameof(receiveCanId));
            if (extended29Bit && extendedAddress.HasValue)
                throw new NotSupportedException(
                    "CAN extended addressing is supported only on 11-bit BMW-style read definitions in this path.");

            ThrowIfDisposed();
            EnsureConnected();

            await _sessionGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await RecoverIfDueAsync(token).ConfigureAwait(false);
                if (!IsCanProtocol(_currentProtocol))
                    throw new NotSupportedException(
                        $"Active vehicle protocol '{_currentProtocol}' is not ISO 15765 CAN.");

                var originalProtocol = _currentProtocol;
                var originalElmProtocolNumber = GetElmCanProtocolNumber(originalProtocol);
                var requestedElmProtocolNumber = GetCompatibleElmCanProtocolNumber(
                    originalProtocol,
                    extended29Bit);
                var switchedProtocol =
                    Is29BitCanProtocol(originalProtocol) != extended29Bit;

                // Setup commands the adapter did not accept, and how the replies were limited to the asked module.
                // Kept per read (see LastPhysicalReadSetupNote), never remembered as "this adapter cannot".
                var rejectedSetup = new List<string>();
                var receiveFilter = PhysicalFilterState.None;
                var answerReceived = false;

                try
                {
                    if (switchedProtocol)
                    {
                        await RequireOkInternalAsync(
                            $"ATSP{requestedElmProtocolNumber}",
                            token).ConfigureAwait(false);
                    }

                    await RequireOkInternalAsync("ATL0", token).ConfigureAwait(false);
                    // S1, CFC1 and D0 are the adapter's own defaults. Elm Electronics' command table lists ATS and ATD
                    // as first appearing in firmware 1.3 and cheap "v1.5" clones are widely reported to run older
                    // microcode, so they answer '?': the read goes on with the default, which is what S1/CFC1/D0 ask
                    // for anyway. (The module scan already treats ATD0/ATCFC1 this way.)
                    await TrySetupAsync("ATS1", rejectedSetup, token).ConfigureAwait(false);
                    await RequireOkInternalAsync("ATH1", token).ConfigureAwait(false);
                    await RequireOkInternalAsync("ATCAF1", token).ConfigureAwait(false);
                    await TrySetupAsync("ATCFC1", rejectedSetup, token).ConfigureAwait(false);
                    await TrySetupAsync("ATD0", rejectedSetup, token).ConfigureAwait(false);
                    await WidenAnswerWindowIfNeededAsync(rejectedSetup, token).ConfigureAwait(false);

                    if (extendedAddress.HasValue)
                    {
                        await RequireOkInternalAsync(
                            $"ATCEA{extendedAddress.Value:X2}",
                            token).ConfigureAwait(false);
                    }

                    var width = extended29Bit ? 8 : 3;
                    await RequireOkInternalAsync(
                        $"ATSH{transmitCanId.ToString($"X{width}")}",
                        token).ConfigureAwait(false);

                    if (receiveCanId.HasValue)
                    {
                        receiveFilter = await SetPhysicalReceiveFilterAsync(
                            transmitCanId, receiveCanId.Value, extended29Bit, width, rejectedSetup, token).ConfigureAwait(false);
                    }
                    else
                    {
                        // Clear any stale receive filter (ATCRA, else ATAR; neither exists on the oldest chips, which
                        // then keep their default). The caller still requires an exact 0x62 DID echo before accepting data.
                        await TryResetReceiveFilterAsync(token).ConfigureAwait(false);
                    }

                    var command = string.Concat(
                        payload.ToArray().Select(value => value.ToString("X2")));

                    var requestStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                    var answer = await SendReadRequestAsync(command, timeout, receiveCanId, payload.Span[0], token).ConfigureAwait(false);
                    LogRead(transmitCanId, receiveCanId, extended29Bit, command, answer, requestStarted);

                    // No hardware filter on this adapter: keep the frames of the asked module only, exactly as the
                    // adapter would have delivered them, so no caller ever sees another module's frames.
                    if (receiveCanId.HasValue && (receiveFilter & PhysicalFilterState.Hardware) == 0)
                        answer = KeepOnlyFramesFrom(answer, receiveCanId.Value, extended29Bit);

                    // A BMW-style extended address cannot be described by a CAN id pair: not reported.
                    if (!extendedAddress.HasValue)
                        ObserveModule(transmitCanId, receiveCanId, extended29Bit, command, payload.Span[0], answer);
                    answerReceived = true;
                    return answer;
                }
                finally
                {
                    // Extended addressing must never leak into normal OBD.
                    if (extendedAddress.HasValue)
                    {
                        try
                        {
                            await SendCommandInternalAsync(
                                "ATCEA",
                                TimeSpan.FromSeconds(2),
                                maxRetries: 1,
                                treatNegativeStatusAsError: false,
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch
                        {
                            _isInitialized = false;
                        }
                    }

                    // An ID filter set with ATCF/ATCM stays until it is cleared: ATCRA / ATAR do that; if neither
                    // exists the only way back to the default is a full re-initialisation (ATZ).
                    if ((receiveFilter & PhysicalFilterState.NeedsClear) != 0 &&
                        !await TryResetReceiveFilterAsync(CancellationToken.None).ConfigureAwait(false))
                    {
                        _isInitialized = false;
                    }

                    if (switchedProtocol)
                    {
                        try
                        {
                            await RequireOkInternalAsync(
                                $"ATSP{originalElmProtocolNumber}",
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch
                        {
                            _isInitialized = false;
                        }
                    }

                    if (_isInitialized)
                    {
                        try
                        {
                            await RestoreGenericCanSettingsCoreAsync(
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception restoreError)
                        {
                            // The adapter state can no longer be trusted (the restore already forced a full
                            // re-initialisation for the next operation). This must neither throw away an answer that
                            // was already read nor hide the real error of a read that failed.
                            _logger.LogWarning(restoreError,
                                "Could not restore the generic CAN settings after a physical read (answer received: {Answered}).",
                                answerReceived);
                        }
                    }

                    _lastPhysicalReadSetupNote = DescribePhysicalSetup(rejectedSetup, receiveFilter);
                    LogReadSetup(transmitCanId, extended29Bit, _lastPhysicalReadSetupNote);
                }
            }
            finally
            {
                _sessionGate.Release();
            }
        }

        /// <summary>
        /// How the last physically addressed read limited the replies to the asked module and which setup commands the
        /// adapter did not accept (empty before the first read). Evidence for comparing adapters; never used to decide
        /// that an adapter "cannot" do something — every read tries its own setup afresh.
        /// </summary>
        public string LastPhysicalReadSetupNote => _lastPhysicalReadSetupNote;

        private string _lastPhysicalReadSetupNote = string.Empty;

        [Flags]
        private enum PhysicalFilterState
        {
            None = 0,

            /// <summary>The adapter itself limits the replies (ATCRA, or ATCF + ATCM).</summary>
            Hardware = 1,

            /// <summary>An ATCF/ATCM filter was set (even if only half of it was accepted) and must be cleared afterwards.</summary>
            NeedsClear = 2,

            /// <summary>No filter was set: the adapter's own Auto Receive address follows the header (standard OBD pairing).</summary>
            AutoReceive = 4
        }

        /// <summary>
        /// Sets one setup command that is the adapter's default anyway. Returns whether the adapter accepted it; a
        /// refusal ('?' on a chip that does not know the command, or no usable answer) is recorded and the read goes on.
        /// </summary>
        private async Task<bool> TrySetupAsync(string command, List<string> rejected, CancellationToken token)
        {
            try
            {
                var response = await SendCommandInternalAsync(
                    command, TimeSpan.FromSeconds(2), maxRetries: 1, treatNegativeStatusAsError: false, token).ConfigureAwait(false);
                if (IsAffirmativeAtResponse(response))
                    return true;

                rejected.Add($"{command} → {SafeEvidence(response)}");
                _logger.LogInformation("Adapter did not accept '{Command}' ('{Response}'); continuing with its default.", command, response);
                return false;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                rejected.Add($"{command} → {ex.GetType().Name}");
                _logger.LogDebug(ex, "Setup command '{Command}' failed; continuing.", command);
                return false;
            }
        }

        /// <summary>
        /// Limits the replies to one module: ATCRA (firmware 1.3+), else ATCF + ATCM (every firmware), else nothing —
        /// the adapter keeps its default filter and the caller filters by CAN id. The module scan uses the same ladder.
        /// </summary>
        private async Task<PhysicalFilterState> SetPhysicalReceiveFilterAsync(
            int transmitCanId, int receiveCanId, bool extended29Bit, int width, List<string> rejected, CancellationToken token)
        {
            var receive = receiveCanId.ToString($"X{width}");
            if (await TrySetupAsync($"ATCRA{receive}", rejected, token).ConfigureAwait(false))
                return PhysicalFilterState.Hardware;

            // The standard OBD pairing (7E0..7E7 answered on 7E8..7EF): Auto Receive is on by default and, according to the
            // ELM327 datasheet ("AR"), chooses the receive address from the header bytes and follows them. Nothing more has
            // to be set, no filter state is changed, so nothing has to be cleared (and no re-initialisation is forced).
            // The app still keeps only this module's frames.
            if (!extended29Bit && transmitCanId is >= 0x7E0 and <= 0x7E7 && receiveCanId == transmitCanId + 8)
                return PhysicalFilterState.AutoReceive;

            var mask = width == 8 ? "1FFFFFFF" : "7FF";
            if (!await TrySetupAsync($"ATCF{receive}", rejected, token).ConfigureAwait(false))
                return PhysicalFilterState.None;

            return await TrySetupAsync($"ATCM{mask}", rejected, token).ConfigureAwait(false)
                ? PhysicalFilterState.Hardware | PhysicalFilterState.NeedsClear
                : PhysicalFilterState.NeedsClear;   // half a filter is set: it must be cleared, and it filters nothing reliably
        }

        /// <summary>One connection-log line for a finished physical read: who was asked, who answered, what that adds up to. Never throws.</summary>
        private static void LogRead(int transmitCanId, int? receiveCanId, bool extended29Bit, string requestHex, string? reply, long startedAt)
        {
            try
            {
                LinkEventLog.Add(AdapterExchangeClassifier.DescribeRead(
                    transmitCanId, receiveCanId, extended29Bit, requestHex, reply, System.Diagnostics.Stopwatch.GetElapsedTime(startedAt)));
            }
            catch
            {
                // A log line must never affect a diagnostic read.
            }
        }

        private static void LogReadSetup(int transmitCanId, bool extended29Bit, string note) =>
            LinkEventLog.Add($"Read setup for tx {transmitCanId.ToString(extended29Bit ? "X8" : "X3")}: {note}");

        private static string DescribePhysicalSetup(IReadOnlyList<string> rejected, PhysicalFilterState filter)
        {
            var filterText = (filter & PhysicalFilterState.Hardware) == 0
                ? (filter & PhysicalFilterState.AutoReceive) != 0
                    ? "adapter's automatic receive address (frames also filtered in the app)"
                    : "no hardware receive filter (frames filtered in the app)"
                : (filter & PhysicalFilterState.NeedsClear) != 0 ? "ID filter via ATCF/ATCM" : "ATCRA";
            return rejected.Count == 0
                ? $"setup: all accepted; filter: {filterText}"
                : $"setup: not accepted: {string.Join(", ", rejected)}; filter: {filterText}";
        }

        /// <summary>
        /// Keeps only the frames sent by <paramref name="canId"/> (the adapter output of ATH1, spaced or compact).
        /// Status words are kept; frames that all came from other modules become "NO DATA", like a hardware filter.
        /// </summary>
        internal static string KeepOnlyFramesFrom(string response, int canId, bool extended29Bit)
        {
            if (string.IsNullOrWhiteSpace(response))
                return response;

            var width = extended29Bit ? 8 : 3;
            var id = canId.ToString(extended29Bit ? "X8" : "X3", System.Globalization.CultureInfo.InvariantCulture);
            var kept = new List<string>();
            var current = new List<string>();
            var currentIsOwn = false;
            var frames = 0;
            var ownFrames = 0;

            void Flush()
            {
                if (current.Count == 0)
                    return;

                frames++;
                if (currentIsOwn)
                {
                    ownFrames++;
                    kept.AddRange(current);
                }

                current.Clear();
            }

            foreach (var token in response.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var upper = token.ToUpperInvariant();
                var isHex = upper.All(Uri.IsHexDigit);

                if (isHex && upper.Length == width)
                {
                    // Spaced output: a bare CAN id starts a frame, its data bytes follow as two-digit tokens.
                    Flush();
                    current.Add(token);
                    currentIsOwn = string.Equals(upper, id, StringComparison.Ordinal);
                }
                else if (isHex && upper.Length == 2 && current.Count > 0)
                {
                    current.Add(token);
                }
                else if (isHex && upper.Length > width)
                {
                    // Compact output (ATS0): id and data in one token.
                    Flush();
                    current.Add(token);
                    currentIsOwn = upper.StartsWith(id, StringComparison.Ordinal);
                    Flush();
                }
                else
                {
                    Flush();
                    kept.Add(token);
                }
            }

            Flush();
            return frames > 0 && ownFrames == 0 ? "NO DATA" : string.Join(' ', kept);
        }

        /// <summary>
        /// Runs several physically addressed diagnostic trouble-code requests
        /// in one adapter session (much faster than configuring the adapter
        /// for every module). Only DTC read services and the standard DTC
        /// clear services are accepted; the generic OBD settings are always
        /// restored afterwards.
        /// </summary>
        public async Task RunDtcModuleSessionAsync(
            Func<IDtcModuleSession, Task> work,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(work);
            ThrowIfDisposed();
            EnsureConnected();

            await _sessionGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!IsCanProtocol(_currentProtocol))
                    throw new NotSupportedException(
                        $"Active vehicle protocol '{_currentProtocol}' is not ISO 15765 CAN.");

                var session = new DtcModuleSession(this, _currentProtocol);
                var tally = _sessionTally = new ExchangeTally();
                var sessionStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    await session.PrepareAsync(token).ConfigureAwait(false);
                    await work(session).ConfigureAwait(false);
                }
                finally
                {
                    await session.RestoreAsync().ConfigureAwait(false);
                    _sessionTally = null;
                    LinkEventLog.Add($"Module session over {AdapterDiagnosticCapture.TransportLabel}: {session.RequestCount} send(s) in " +
                                     $"{(long)System.Diagnostics.Stopwatch.GetElapsedTime(sessionStarted).TotalMilliseconds} ms; {tally}");

                    // A long scan leaves the adapter with many changed settings and a lot of traffic behind it; cheap adapters
                    // then drift (the symptom: data stops after several features, until a reconnect). A full reset puts it back
                    // to the state a fresh connection has. Short sessions do not need it.
                    if (session.RequestCount >= HygieneResetAfterRequests && _isInitialized)
                    {
                        try
                        {
                            LinkEventLog.Add($"Module session sent {session.RequestCount} requests: resetting the adapter (ATZ) so later features start clean");
                            await ResetCoreAsync(CancellationToken.None).ConfigureAwait(false);
                            await InitializeCoreAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception resetError)
                        {
                            _isInitialized = false;
                            _logger.LogWarning(resetError, "Clean-up reset after a long module session failed.");
                        }
                    }
                }
            }
            finally
            {
                _sessionGate.Release();
            }
        }

        /// <summary>One adapter session for module DTC requests (see <see cref="RunDtcModuleSessionAsync"/>).</summary>
        public interface IDtcModuleSession
        {
            /// <summary>ISO-TP request (the adapter adds the PCI byte). Returns frames as "HEADER+DATA" tokens.</summary>
            Task<string> SendAsync(int transmitCanId, int receiveCanId, bool extended29Bit, byte[] payload, bool longWait, CancellationToken token, byte? extendedAddress = null);

            /// <summary>GM (GMLAN) $A9 DTC request: single frame out, unsegmented frames back on <paramref name="uudtReceiveCanId"/>.</summary>
            Task<string> SendGmlanDtcRequestAsync(int transmitCanId, int uudtReceiveCanId, byte[] payload, CancellationToken token);

            /// <summary>
            /// Waits for one periodic (broadcast) frame with the given 11-bit ID, e.g. the
            /// odometer frame a car sends by itself. Only the standard "supported PIDs"
            /// request (01 00) is sent; its reply is filtered out.
            /// </summary>
            Task<string> ListenBroadcastAsync(int canId, CancellationToken token);

            /// <summary>
            /// Monitors the bus ("ATMA", filtered to one ID) for the given time and returns
            /// every frame the car broadcast with that ID. Nothing is sent to the car.
            /// </summary>
            Task<string> MonitorBroadcastAsync(int canId, bool extended29Bit, int milliseconds, CancellationToken token);

            /// <summary>
            /// Listens to the bus ("ATMA") for the given time and returns every frame
            /// that passes the filter (filter/mask as in ATCF/ATCM; null = all frames).
            /// Nothing is sent to the car. Used by the reset recorder.
            /// </summary>
            Task<string> MonitorRawAsync(int? filter, int? mask, bool extended29Bit, int milliseconds, CancellationToken token);
        }

        private sealed class DtcModuleSession : IDtcModuleSession
        {
            private readonly Elm327Protocol _elm;
            private readonly ObdProtocol _originalProtocol;
            public int RequestCount { get; private set; }
            private bool _is29Bit;
            private bool _protocolSwitched;
            private bool? _caf;
            private string? _header;
            private string? _receive;
            private string? _timing;
            private bool _flowControlSet;
            private bool _flowControlSupported = true;
            private string? _extendedAddress;
            private string? _flowControlData;
            // When the adapter rejects ATCRA (and ATCF/ATCM), replies are
            // filtered by CAN ID in software (headers are on, ATH1).
            private string? _softwareFilterId;
            private bool _hardwareFilterViaMask;
            private readonly HashSet<string> _loggedOnce = new(StringComparer.Ordinal);

            /// <summary>Writes a setup finding to the connection log the first time only: a scan configures many modules the same way.</summary>
            private void LogOnce(string key, string text)
            {
                if (_loggedOnce.Add(key))
                    LinkEventLog.Add(text);
            }

            public DtcModuleSession(Elm327Protocol elm, ObdProtocol originalProtocol)
            {
                _elm = elm;
                _originalProtocol = originalProtocol;
                _is29Bit = Is29BitCanProtocol(originalProtocol);
            }

            public async Task PrepareAsync(CancellationToken token)
            {
                await _elm.RequireOkInternalAsync("ATL0", token).ConfigureAwait(false);
                // S0: every received frame becomes one token (header+data).
                await _elm.RequireOkInternalAsync("ATS0", token).ConfigureAwait(false);
                await _elm.RequireOkInternalAsync("ATH1", token).ConfigureAwait(false);
                // D0 and CFC1 are the adapter defaults: some clones reject the
                // commands themselves, which must not stop the whole scan.
                if (!await TryAsync("ATD0", token).ConfigureAwait(false))
                {
                    LinkEventLog.Add("Module session setup: ATD0 not accepted; the adapter's default is used");
                    _elm._logger.LogInformation("Module session: adapter rejected ATD0; continuing with its default.");
                }
                if (!await TryAsync("ATCFC1", token).ConfigureAwait(false))
                {
                    LinkEventLog.Add("Module session setup: ATCFC1 not accepted; the adapter's default is used");
                    _elm._logger.LogInformation("Module session: adapter rejected ATCFC1; continuing with its default.");
                }
            }

            /// <summary>
            /// Sets the receive filter for one module: ATCRA, else ATCF + ATCM,
            /// else no hardware filter and the reply is filtered by ID in software.
            /// </summary>
            private async Task SetReceiveFilterAsync(string receive, bool ext29, CancellationToken token)
            {
                if (await TryAsync($"ATCRA{receive}", token).ConfigureAwait(false))
                {
                    _softwareFilterId = null;
                    return;
                }

                var mask = ext29 ? "1FFFFFFF" : "7FF";
                if (await TryAsync($"ATCF{receive}", token).ConfigureAwait(false) &&
                    await TryAsync($"ATCM{mask}", token).ConfigureAwait(false))
                {
                    LogOnce("filter-mask", "Module session: ATCRA not accepted; an ATCF/ATCM filter is used instead");
                    _elm._logger.LogInformation("Module session: ATCRA rejected; using ATCF/ATCM filter for {Receive}.", receive);
                    _hardwareFilterViaMask = true;
                    _softwareFilterId = null;
                    return;
                }

                LogOnce("filter-software", "Module session: no receive-filter command accepted; frames are filtered by CAN id in the app");
                _elm._logger.LogWarning("Module session: adapter has no receive filter; filtering {Receive} in software.", receive);
                _softwareFilterId = receive;
            }

            /// <summary>Keeps only frames whose CAN ID equals the software filter (ATH1 output).</summary>
            private string ApplySoftwareFilter(string response)
            {
                var id = _softwareFilterId;
                if (id is null || string.IsNullOrEmpty(response))
                    return response;

                var kept = new List<string>();
                var sawFrame = false;
                foreach (var rawLine in response.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0)
                        continue;

                    var compact = line.Replace(" ", string.Empty).ToUpperInvariant();
                    if (!Regex.IsMatch(compact, @"^[0-9A-F]+$"))
                    {
                        // Status text (NO DATA, STOPPED …) is kept as is.
                        kept.Add(line);
                        continue;
                    }

                    sawFrame = true;

                    // Several frames in one line (the protocol layer has joined the adapter's lines with spaces): split them into
                    // frames, keep the asked module's frames, each on a line of its own as the adapter printed them, and drop the
                    // frames of other modules. They are never merged and no byte is changed. Text that is not made of frames alone
                    // (an echoed command, a status word) is not split and is judged as one line, as before.
                    if (CarScanner.Services.OemDiagnostics.ModuleReplyAnalysis.SplitFrameTexts(line, id.Length > 3) is { Count: >= 2 } several)
                    {
                        kept.AddRange(several.Where(item => string.Equals(item.Header, id, StringComparison.Ordinal)).Select(item => item.Text));
                        continue;
                    }

                    if (compact.Length > id.Length && compact.StartsWith(id, StringComparison.Ordinal))
                        kept.Add(line);
                }

                if (sawFrame && kept.Count == 0)
                    return "NO DATA";
                return string.Join("\r", kept);
            }

            private static void EnsureReadOrClear(byte[] payload)
            {
                if (payload.Length is < 1 or > 7)
                    throw new NotSupportedException("DTC requests are limited to 1-7 payload bytes.");

                var ok = payload[0] switch
                {
                    // UDS ReadDTCInformation: number of DTC, DTC by status, snapshot, extended data.
                    0x19 => payload.Length >= 2 && payload[1] is 0x01 or 0x02 or 0x03 or 0x04 or 0x06,
                    // UDS ReadDataByIdentifier (read-only).
                    0x22 => payload.Length == 3,
                    // KWP ReadDataByLocalIdentifier / ReadEcuIdentification, GMLAN ReadDataByIdentifier (read-only).
                    0x21 => payload.Length == 2,
                    0x1A => payload.Length == 2,
                    // KWP2000 ReadDTCByStatus (read-only).
                    0x18 => true,
                    // UDS / KWP ClearDiagnosticInformation.
                    0x14 => true,
                    // GMLAN ReadDiagnosticInformation.
                    0xA9 => true,
                    // GMLAN ClearDiagnosticInformation.
                    0x04 => payload.Length == 1,
                    _ => false
                };

                if (!ok)
                    throw new InvalidOperationException(
                        $"Service 0x{payload[0]:X2} is not allowed in a DTC module session.");
            }

            private async Task SetAsync(string command, CancellationToken token) =>
                await _elm.RequireOkInternalAsync(command, token).ConfigureAwait(false);

            private async Task<bool> TryAsync(string command, CancellationToken token)
            {
                try
                {
                    var response = await _elm.SendCommandInternalAsync(command, TimeSpan.FromSeconds(2), 1, false, token).ConfigureAwait(false);
                    return IsAffirmativeAtResponse(response);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    return false;
                }
            }

            private async Task ConfigureAsync(int tx, int rx, bool ext29, bool caf, bool longWait, CancellationToken token, byte? extendedAddress = null)
            {
                if (ext29 && extendedAddress.HasValue)
                    throw new NotSupportedException("CAN extended addressing is used only with 11-bit identifiers.");

                if (ext29 != _is29Bit)
                {
                    var number = GetCompatibleElmCanProtocolNumber(_originalProtocol, ext29);
                    await SetAsync($"ATSP{number}", token).ConfigureAwait(false);
                    _is29Bit = ext29;
                    _protocolSwitched = ext29 != Is29BitCanProtocol(_originalProtocol);
                    _header = null;
                    _receive = null;
                }

                if (_caf != caf)
                {
                    await SetAsync(caf ? "ATCAF1" : "ATCAF0", token).ConfigureAwait(false);
                    _caf = caf;
                }

                var width = ext29 ? 8 : 3;
                var header = tx.ToString($"X{width}");
                if (_header != header)
                {
                    await SetAsync($"ATSH{header}", token).ConfigureAwait(false);
                    _header = header;

                    // Flow control must go to this module's request ID (not
                    // "reply − 8", which is wrong for many OEM modules).
                    // Adapters without user flow control keep their automatic
                    // flow control; the scan continues either way.
                    if (_flowControlSupported)
                    {
                        _flowControlSupported =
                            await TryAsync($"ATFCSH{header}", token).ConfigureAwait(false) &&
                            (_flowControlSet ||
                             (await TryAsync("ATFCSD300000", token).ConfigureAwait(false) &&
                              await TryAsync("ATFCSM1", token).ConfigureAwait(false)));
                        _flowControlSet = _flowControlSupported;
                        if (!_flowControlSupported)
                        {
                            LogOnce("flow-control", "Module session: user flow control (ATFCSH/ATFCSD/ATFCSM1) not accepted; the adapter's automatic flow control is used");
                            _elm._logger.LogInformation("Module session: adapter rejected user flow control (ATFCSH/ATFCSM1); using automatic flow control.");
                        }
                    }
                }

                var receive = rx.ToString($"X{width}");
                if (_receive != receive)
                {
                    await SetReceiveFilterAsync(receive, ext29, token).ConfigureAwait(false);
                    _receive = receive;
                }

                // BMW-style CAN extended addressing (target address in the first data byte).
                var extended = extendedAddress?.ToString("X2");
                if (_extendedAddress != extended)
                {
                    if (extended is null)
                        await TryAsync("ATCEA", token).ConfigureAwait(false);
                    else
                        await SetAsync($"ATCEA{extended}", token).ConfigureAwait(false);
                    _extendedAddress = extended;
                }

                if (_flowControlSupported && caf)
                {
                    var fcData = extended is null ? "300000" : extended + "300000";
                    if (_flowControlData != fcData)
                    {
                        if (await TryAsync($"ATFCSD{fcData}", token).ConfigureAwait(false))
                            _flowControlData = fcData;
                    }
                }

                // Answer window: 400 ms normally, ~1 s when the ECU asked for more time.
                var timing = longWait ? "ATSTFF" : "ATST64";
                if (_timing != timing)
                {
                    if (!await TryAsync(timing, token).ConfigureAwait(false))
                    {
                        LogOnce("timing-" + timing, $"Module session: {timing} not accepted; the adapter keeps its own answer window");
                        _elm._logger.LogInformation("Module session: adapter rejected {Timing}; keeping its timeout.", timing);
                    }
                    _timing = timing;
                }
            }

            public async Task<string> SendAsync(int transmitCanId, int receiveCanId, bool extended29Bit, byte[] payload, bool longWait, CancellationToken token, byte? extendedAddress = null)
            {
                RequestCount++;
                EnsureReadOrClear(payload);
                ValidateCanIdentifiers(transmitCanId, receiveCanId, extended29Bit);
                await ConfigureAsync(transmitCanId, receiveCanId, extended29Bit, caf: true, longWait, token, extendedAddress).ConfigureAwait(false);
                var command = string.Concat(payload.Select(b => b.ToString("X2")));
                var received = await _elm.SendCommandInternalAsync(
                    command,
                    TimeSpan.FromSeconds(longWait ? 4 : 2.5),
                    maxRetries: 1,
                    treatNegativeStatusAsError: false,
                    token).ConfigureAwait(false);
                // Who answered is noted before the software filter turns other modules' frames into "NO DATA".
                _elm.NoteSessionSenders(received, receiveCanId, extended29Bit);
                var answer = ApplySoftwareFilter(received);

                // Only the read services are reported (never clears or anything else that passes through here), and
                // under the CAN id the reply really came from: when the adapter delivers the engine ECU's frames for
                // another module's request, that is a finding and must not be filed under the module that was asked.
                if (ObdExchangeTap.HasSubscribers && payload[0] is 0x19 or 0x21 or 0x22)
                    ObdExchangeTap.Publish(new ObdRawExchange(transmitCanId, RealSenderHeader(answer, extended29Bit, receiveCanId), extended29Bit, command, answer));

                return answer;
            }

            public async Task<string> MonitorBroadcastAsync(int canId, bool extended29Bit, int milliseconds, CancellationToken token)
            {
                var tx = extended29Bit ? 0x18DB33F1 : 0x7DF;
                ValidateCanIdentifiers(tx, canId, extended29Bit);
                await ConfigureAsync(tx, canId, extended29Bit, caf: false, longWait: false, token).ConfigureAwait(false);
                return ApplySoftwareFilter(await _elm.SendCommandInternalAsync(
                    ObdMonitorCommand.Create("ATMA", milliseconds),
                    TimeSpan.FromMilliseconds(milliseconds + 4000),
                    maxRetries: 1,
                    treatNegativeStatusAsError: false,
                    token).ConfigureAwait(false));
            }

            public async Task<string> MonitorRawAsync(int? filter, int? mask, bool extended29Bit, int milliseconds, CancellationToken token)
            {
                if (extended29Bit != _is29Bit)
                {
                    var number = GetCompatibleElmCanProtocolNumber(_originalProtocol, extended29Bit);
                    await SetAsync($"ATSP{number}", token).ConfigureAwait(false);
                    _is29Bit = extended29Bit;
                    _protocolSwitched = extended29Bit != Is29BitCanProtocol(_originalProtocol);
                    _header = null;
                }

                if (_caf != false)
                {
                    await SetAsync("ATCAF0", token).ConfigureAwait(false);
                    _caf = false;
                }

                var width = extended29Bit ? 8 : 3;
                if (filter is { } f && mask is { } m)
                {
                    await SetAsync($"ATCF{f.ToString($"X{width}")}", token).ConfigureAwait(false);
                    await SetAsync($"ATCM{m.ToString($"X{width}")}", token).ConfigureAwait(false);
                }
                else
                {
                    // No receive address and no filter: every frame on the bus.
                    await TryAsync("ATCRA", token).ConfigureAwait(false);
                    await TryAsync("ATCF" + new string('0', width), token).ConfigureAwait(false);
                    await TryAsync("ATCM" + new string('0', width), token).ConfigureAwait(false);
                }

                // The next normal request sets its own receive address again.
                _receive = null;
                _softwareFilterId = null;

                try
                {
                    return await _elm.SendCommandInternalAsync(
                        ObdMonitorCommand.Create("ATMA", milliseconds),
                        TimeSpan.FromMilliseconds(milliseconds + 4000),
                        maxRetries: 1,
                        treatNegativeStatusAsError: false,
                        token).ConfigureAwait(false);
                }
                finally
                {
                    await TryAsync("ATCRA", CancellationToken.None).ConfigureAwait(false);
                }
            }

            public async Task<string> ListenBroadcastAsync(int canId, CancellationToken token)
            {
                ValidateCanIdentifiers(0x7DF, canId, extended29Bit: false);
                await ConfigureAsync(0x7DF, canId, ext29: false, caf: false, longWait: true, token).ConfigureAwait(false);

                // Raw frame "02 01 00" (standard OBD request, harmless) + response count 1:
                // the adapter returns as soon as the first frame with the wanted ID arrives.
                return ApplySoftwareFilter(await _elm.SendCommandInternalAsync(
                    "02010000000000001",
                    TimeSpan.FromSeconds(3),
                    maxRetries: 1,
                    treatNegativeStatusAsError: false,
                    token).ConfigureAwait(false));
            }

            public async Task<string> SendGmlanDtcRequestAsync(int transmitCanId, int uudtReceiveCanId, byte[] payload, CancellationToken token)
            {
                EnsureReadOrClear(payload);
                if (payload[0] != 0xA9)
                    throw new InvalidOperationException("Only GMLAN $A9 uses unsegmented replies.");
                ValidateCanIdentifiers(transmitCanId, uudtReceiveCanId, extended29Bit: false);
                await ConfigureAsync(transmitCanId, uudtReceiveCanId, ext29: false, caf: false, longWait: false, token).ConfigureAwait(false);

                // Raw single frame: PCI + payload, padded to 8 bytes.
                var frame = new byte[8];
                frame[0] = (byte)payload.Length;
                Array.Copy(payload, 0, frame, 1, payload.Length);
                var command = string.Concat(frame.Select(b => b.ToString("X2")));
                return ApplySoftwareFilter(await _elm.SendCommandInternalAsync(
                    command,
                    TimeSpan.FromSeconds(3),
                    maxRetries: 1,
                    treatNegativeStatusAsError: false,
                    token).ConfigureAwait(false));
            }

            public async Task RestoreAsync()
            {
                try
                {
                    if (_timing is not null)
                        await _elm.SendCommandInternalAsync("ATST32", TimeSpan.FromSeconds(2), 1, false, CancellationToken.None).ConfigureAwait(false);

                    if (_flowControlSet)
                        await _elm.SendCommandInternalAsync("ATFCSM0", TimeSpan.FromSeconds(2), 1, false, CancellationToken.None).ConfigureAwait(false);

                    if (_extendedAddress is not null)
                        await _elm.SendCommandInternalAsync("ATCEA", TimeSpan.FromSeconds(2), 1, false, CancellationToken.None).ConfigureAwait(false);

                    // An ATCF/ATCM filter is cleared by ATCRA / ATAR. If neither
                    // works the filter would stay: force a full re-init (ATZ).
                    if (_hardwareFilterViaMask && !await _elm.TryResetReceiveFilterAsync(CancellationToken.None).ConfigureAwait(false))
                        _elm._isInitialized = false;

                    if (_protocolSwitched || _is29Bit != Is29BitCanProtocol(_originalProtocol))
                        await SetAsync($"ATSP{GetElmCanProtocolNumber(_originalProtocol)}", CancellationToken.None).ConfigureAwait(false);

                    if (_elm._isInitialized)
                        await _elm.RestoreGenericCanSettingsCoreAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    _elm._isInitialized = false;
                }
            }
        }

        private static int GetCompatibleElmCanProtocolNumber(
            ObdProtocol activeProtocol,
            bool requested29Bit) => activeProtocol switch
            {
                ObdProtocol.ISO_15765_4_CAN_11BIT_500K or
                ObdProtocol.ISO_15765_4_CAN_29BIT_500K => requested29Bit ? 7 : 6,

                ObdProtocol.ISO_15765_4_CAN_11BIT_250K or
                ObdProtocol.ISO_15765_4_CAN_29BIT_250K => requested29Bit ? 9 : 8,

                _ => throw new NotSupportedException(
                    $"Protocol '{activeProtocol}' cannot be converted to a CAN ISO-TP addressing width.")
            };

        private static int GetElmCanProtocolNumber(ObdProtocol protocol) => protocol switch
        {
            ObdProtocol.ISO_15765_4_CAN_11BIT_500K => 6,
            ObdProtocol.ISO_15765_4_CAN_29BIT_500K => 7,
            ObdProtocol.ISO_15765_4_CAN_11BIT_250K => 8,
            ObdProtocol.ISO_15765_4_CAN_29BIT_250K => 9,
            _ => throw new NotSupportedException(
                $"Protocol '{protocol}' is not a supported ISO-15765 CAN protocol.")
        };

        /// <summary>
        /// Runs a read-only adapter capability benchmark. Only ELM AT commands
        /// plus standard Generic OBD requests 0100 and 0902 are sent. No OEM
        /// ECU address, session-control, security, routine or write request is
        /// used by this detector.
        /// </summary>
        public async Task<AdapterCapabilitySnapshot> DetectAdapterCapabilitiesAsync(
            IProgress<AdapterTestProgress>? progress = null,
            CancellationToken token = default)
        {
            ThrowIfDisposed();
            EnsureConnected();
            if (!_isInitialized)
                throw new InvalidOperationException("ELM327 must be initialized before capability detection.");

            await _sessionGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var checks = new List<AdapterCapabilityCheck>();
                var latencies = new List<long>();
                var attempted = 0;
                var passed = 0;
                var deviceDescription = "Unavailable";
                var protocolNumber = "Unknown";

                async Task<(bool Passed, string Response)> ProbeAtAsync(
                    string id,
                    string name,
                    string command,
                    int percent,
                    Func<string, bool>? validator = null)
                {
                    progress?.Report(new AdapterTestProgress(percent, $"{name}…"));
                    attempted++;
                    try
                    {
                        var response = await SendCommandInternalAsync(
                            command,
                            TimeSpan.FromMilliseconds(1200),
                            maxRetries: 1,
                            treatNegativeStatusAsError: false,
                            token).ConfigureAwait(false);
                        var ok = validator?.Invoke(response) ?? IsMeaningfulCapabilityResponse(response);
                        if (ok) passed++;
                        checks.Add(new AdapterCapabilityCheck(
                            id,
                            name,
                            ok ? AdapterCapabilityCheckState.Passed : AdapterCapabilityCheckState.NotSupported,
                            ok ? $"'{command}' accepted by adapter." : $"'{command}' response: {SafeEvidence(response)}"));
                        return (ok, response);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        checks.Add(new AdapterCapabilityCheck(
                            id,
                            name,
                            AdapterCapabilityCheckState.Failed,
                            ex.Message));
                        return (false, string.Empty);
                    }
                }

                var identity = await ProbeAtAsync(
                    "elm.identity",
                    "ELM identity / firmware",
                    "ATI",
                    76).ConfigureAwait(false);
                if (identity.Passed)
                    deviceDescription = identity.Response;

                var description = await ProbeAtAsync(
                    "elm.description",
                    "Adapter description",
                    "AT@1",
                    78).ConfigureAwait(false);
                if (description.Passed)
                    deviceDescription = description.Response;

                var dpn = await ProbeAtAsync(
                    "elm.protocol-number",
                    "Protocol number",
                    "ATDPN",
                    80,
                    response => !string.IsNullOrWhiteSpace(response) &&
                                !ObdResponseParser.IsNegative(response)).ConfigureAwait(false);
                if (dpn.Passed)
                    protocolNumber = dpn.Response;

                var longMessages = await ProbeAtAsync(
                    "elm.long-messages",
                    "Long message command",
                    "ATAL",
                    81,
                    IsAffirmativeAtResponse).ConfigureAwait(false);
                if (longMessages.Passed)
                    await ProbeAtAsync("elm.normal-length", "Normal message restore", "ATNL", 82, IsAffirmativeAtResponse).ConfigureAwait(false);

                await ProbeAtAsync(
                    "elm.adaptive-timing",
                    "Adaptive timing",
                    "ATAT2",
                    83,
                    IsAffirmativeAtResponse).ConfigureAwait(false);

                var addressing = "Not verified on active vehicle";
                if (IsCanProtocol(_currentProtocol))
                {
                    var is29Bit = Is29BitCanProtocol(_currentProtocol);
                    addressing = is29Bit ? "29-bit CAN verified" : "11-bit CAN verified";
                    checks.Add(new AdapterCapabilityCheck(
                        is29Bit ? "can.addressing-29" : "can.addressing-11",
                        is29Bit ? "29-bit CAN addressing" : "11-bit CAN addressing",
                        AdapterCapabilityCheckState.Passed,
                        $"Active negotiated protocol: {_currentProtocolRawDescription ?? _currentProtocol.ToString()}"));
                    checks.Add(new AdapterCapabilityCheck(
                        is29Bit ? "can.addressing-11" : "can.addressing-29",
                        is29Bit ? "11-bit CAN addressing" : "29-bit CAN addressing",
                        AdapterCapabilityCheckState.NotVerified,
                        "The inactive CAN identifier width cannot be proven on this vehicle without changing the negotiated bus protocol."));

                    var headers = await ProbeAtAsync("can.headers", "CAN headers", "ATH1", 84, IsAffirmativeAtResponse).ConfigureAwait(false);
                    var spaces = await ProbeAtAsync("can.spacing", "CAN response spacing", "ATS1", 85, IsAffirmativeAtResponse).ConfigureAwait(false);
                    var dlc = await ProbeAtAsync("can.dlc", "CAN DLC display control", "ATD0", 86, IsAffirmativeAtResponse).ConfigureAwait(false);
                    var formatting = await ProbeAtAsync("can.auto-format", "CAN automatic formatting", "ATCAF1", 87, IsAffirmativeAtResponse).ConfigureAwait(false);
                    var flow = await ProbeAtAsync("can.flow-control", "ISO-TP automatic flow control", "ATCFC1", 88, IsAffirmativeAtResponse).ConfigureAwait(false);

                    var functionalHeader = is29Bit ? "18DB33F1" : "7DF";
                    var header = await ProbeAtAsync(
                        "can.set-header",
                        "CAN transmit header",
                        $"ATSH{functionalHeader}",
                        89,
                        IsAffirmativeAtResponse).ConfigureAwait(false);
                    var filterReset = await ProbeAtAsync(
                        "can.receive-filter-command",
                        "CAN receive filter command",
                        "ATCRA",
                        90,
                        IsAffirmativeAtResponse).ConfigureAwait(false);

                    // Spacing, DLC display, flow-control switch and the receive-filter command are defaults or have fallbacks in the
                    // app (older chips answer "?"); only headers, automatic formatting and the transmit header are needed to test the bus.
                    if (headers.Passed && formatting.Passed && header.Passed)
                    {
                        string firstProbeRaw = string.Empty;
                        var successfulVehicleQueries = 0;
                        string? genericProbeError = null;
                        for (var sample = 0; sample < 3; sample++)
                        {
                            token.ThrowIfCancellationRequested();
                            var clock = System.Diagnostics.Stopwatch.StartNew();
                            attempted++;
                            try
                            {
                                var raw = await SendCommandInternalAsync(
                                    "0100",
                                    TimeSpan.FromMilliseconds(1500),
                                    maxRetries: 1,
                                    treatNegativeStatusAsError: false,
                                    token).ConfigureAwait(false);

                                if (HasModeResponse(raw, 0x41, 0x00))
                                {
                                    passed++;
                                    successfulVehicleQueries++;
                                    if (string.IsNullOrEmpty(firstProbeRaw))
                                        firstProbeRaw = raw;
                                }
                            }
                            catch (OperationCanceledException) when (token.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                genericProbeError = ex.Message;
                            }
                            finally
                            {
                                clock.Stop();
                                latencies.Add(clock.ElapsedMilliseconds);
                            }
                        }

                        checks.Add(new AdapterCapabilityCheck(
                            "can.generic-request",
                            "CAN request/response",
                            successfulVehicleQueries == 3
                                ? AdapterCapabilityCheckState.Passed
                                : AdapterCapabilityCheckState.Failed,
                            genericProbeError is null
                                ? $"Standard 0100 responses: {successfulVehicleQueries}/3."
                                : $"Standard 0100 responses: {successfulVehicleQueries}/3. Last error: {genericProbeError}"));

                        var responseIds = ObdResponseParser.ExtractCanIdentifiers(firstProbeRaw);
                        if (responseIds.Count > 0)
                        {
                            var responseId = responseIds[0];
                            var width = is29Bit ? 8 : 3;
                            var filter = await ProbeAtAsync(
                                "can.receive-filter",
                                "CAN receive filter",
                                $"ATCRA{responseId.ToString($"X{width}")}",
                                91,
                                IsAffirmativeAtResponse).ConfigureAwait(false);

                            if (filter.Passed)
                            {
                                attempted++;
                                try
                                {
                                    var filteredRaw = await SendCommandInternalAsync(
                                        "0100",
                                        TimeSpan.FromMilliseconds(1500),
                                        maxRetries: 1,
                                        treatNegativeStatusAsError: false,
                                        token).ConfigureAwait(false);
                                    var filteredIds = ObdResponseParser.ExtractCanIdentifiers(filteredRaw);
                                    var filterVerified = HasModeResponse(filteredRaw, 0x41, 0x00) &&
                                                         filteredIds.Count > 0 &&
                                                         filteredIds.All(id => id == responseId);
                                    if (filterVerified) passed++;
                                    checks.Add(new AdapterCapabilityCheck(
                                        "can.receive-filter-verified",
                                        "CAN receive filter verification",
                                        filterVerified ? AdapterCapabilityCheckState.Passed : AdapterCapabilityCheckState.Failed,
                                        filterVerified
                                            ? $"Only ECU response ID 0x{responseId:X} passed the active filter."
                                            : "The adapter accepted ATCRA but the filtered ECU response could not be verified."));
                                }
                                catch (OperationCanceledException) when (token.IsCancellationRequested)
                                {
                                    throw;
                                }
                                catch (Exception ex)
                                {
                                    checks.Add(new AdapterCapabilityCheck(
                                        "can.receive-filter-verified",
                                        "CAN receive filter verification",
                                        AdapterCapabilityCheckState.Failed,
                                        ex.Message));
                                }
                                finally
                                {
                                    await RequireOkInternalAsync("ATCRA", CancellationToken.None).ConfigureAwait(false);
                                }
                            }
                        }
                        else
                        {
                            checks.Add(new AdapterCapabilityCheck(
                                "can.receive-filter-verified",
                                "CAN receive filter verification",
                                AdapterCapabilityCheckState.NotVerified,
                                "The standard ECU reply did not expose a response CAN ID."));
                        }

                        progress?.Report(new AdapterTestProgress(93, "ISO-TP multi-frame პასუხის შემოწმება (0902)…"));
                        attempted++;
                        try
                        {
                            var vinRaw = await SendCommandInternalAsync(
                                "0902",
                                TimeSpan.FromSeconds(3),
                                maxRetries: 1,
                                treatNegativeStatusAsError: false,
                                token).ConfigureAwait(false);
                            var isoTpVerified = HasCompleteMode09IsoTpResponse(vinRaw);
                            if (isoTpVerified) passed++;
                            checks.Add(new AdapterCapabilityCheck(
                                "isotp.multiframe",
                                "ISO-TP multi-frame response",
                                isoTpVerified
                                    ? AdapterCapabilityCheckState.Passed
                                    : AdapterCapabilityCheckState.NotVerified,
                                isoTpVerified
                                    ? "A complete multi-frame Mode 09 response was reassembled and validated."
                                    : "This ECU did not provide a complete multi-frame Mode 09 response; support is not assumed."));
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            checks.Add(new AdapterCapabilityCheck(
                                "isotp.multiframe",
                                "ISO-TP multi-frame response",
                                AdapterCapabilityCheckState.NotVerified,
                                $"Mode 09 multi-frame test could not be completed: {ex.Message}"));
                        }
                    }
                    else
                    {
                        checks.Add(new AdapterCapabilityCheck(
                            "isotp.multiframe",
                            "ISO-TP multi-frame response",
                            AdapterCapabilityCheckState.NotSupported,
                            "Required CAN formatting/header/flow-control commands were not all accepted."));
                    }
                }
                else
                {
                    checks.Add(new AdapterCapabilityCheck(
                        "can.transport",
                        "CAN / ISO-TP transport",
                        AdapterCapabilityCheckState.NotVerified,
                        $"Active vehicle protocol is {_currentProtocol}; CAN cannot be proven on this connection."));

                    if (IsLegacyPhysicalProtocol(_currentProtocol))
                    {
                        addressing = "Legacy ISO/KWP/J1850 active";
                        checks.Add(new AdapterCapabilityCheck(
                            "legacy.transport",
                            "ISO 9141 / KWP2000 / J1850 transport",
                            AdapterCapabilityCheckState.Passed,
                            $"Active negotiated protocol: {_currentProtocolRawDescription ?? _currentProtocol.ToString()}"));
                    }
                }

                var average = latencies.Count == 0 ? 0 : (long)Math.Round(latencies.Average());
                var totalQueryMs = latencies.Sum();
                var throughput = latencies.Count == 0 || totalQueryMs <= 0
                    ? 0
                    : latencies.Count / (totalQueryMs / 1000d);

                progress?.Report(new AdapterTestProgress(94, "Adapter capability შედეგების დამუშავება…"));
                LogCapabilityChecks(checks, attempted, passed, latencies);
                return new AdapterCapabilitySnapshot
                {
                    DeviceDescription = SafeEvidence(deviceDescription),
                    ProtocolNumber = SafeEvidence(protocolNumber),
                    ActiveBus = _currentProtocolRawDescription ?? _currentProtocol.ToString(),
                    Addressing = addressing,
                    SafeCommandsAttempted = attempted,
                    SafeCommandsPassed = passed,
                    MinimumLatencyMs = latencies.Count == 0 ? 0 : latencies.Min(),
                    AverageLatencyMs = average,
                    MaximumLatencyMs = latencies.Count == 0 ? 0 : latencies.Max(),
                    CommandThroughputPerSecond = throughput,
                    Checks = checks.ToArray()
                };
            }
            finally
            {
                try
                {
                    if (IsCanProtocol(_currentProtocol))
                        await RestoreGenericCanSettingsCoreAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    _sessionGate.Release();
                }
            }
        }

        /// <summary>The result of the user-started adapter test, one line per check, so a copied connection log carries it. Never throws.</summary>
        private static void LogCapabilityChecks(IReadOnlyList<AdapterCapabilityCheck> checks, int attempted, int passed, IReadOnlyList<long> latencies)
        {
            try
            {
                LinkEventLog.Add($"Adapter test over {AdapterDiagnosticCapture.TransportLabel}: {passed}/{attempted} safe commands passed" +
                                 (latencies.Count == 0 ? string.Empty : $"; 0100 latency min/avg/max {latencies.Min()}/{(long)Math.Round(latencies.Average())}/{latencies.Max()} ms"));
                foreach (var check in checks)
                    LinkEventLog.Add($"Adapter test: {check.Id} = {check.State}" +
                                     (check.State == AdapterCapabilityCheckState.Passed ? string.Empty : $" ({AdapterDiagnosticCapture.Brief(check.Evidence, 80)})"));
            }
            catch
            {
                // A log line must never affect the adapter test.
            }
        }

        /// <summary>
        /// Sends one short, physically addressed request over the already
        /// detected ISO 9141, KWP2000 or J1850 vehicle protocol. The caller
        /// must supply a verified three-byte OEM header. A complete automatic
        /// OBD session is rebuilt before this method releases the adapter.
        /// </summary>
        public async Task<string> SendLegacyPhysicalPayloadAsync(
            ReadOnlyMemory<byte> payload,
            int header,
            TimeSpan timeout,
            CancellationToken token = default)
        {
            if (payload.Length is < 1 or > 7)
                throw new NotSupportedException(
                    "Legacy ELM327 physical requests are limited to 1-7 verified payload bytes.");
            if (header < 0 || header > 0xFFFFFF)
                throw new ArgumentOutOfRangeException(nameof(header));
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout));

            ThrowIfDisposed();
            EnsureConnected();

            await _sessionGate.WaitAsync(token).ConfigureAwait(false);
            var adapterModeChanged = false;
            try
            {
                if (!IsLegacyPhysicalProtocol(_currentProtocol))
                    throw new NotSupportedException(
                        $"Active protocol '{_currentProtocol}' is not ISO 9141, KWP2000 or J1850.");

                adapterModeChanged = true;
                await RequireOkInternalAsync("ATL0", token).ConfigureAwait(false);
                await RequireOkInternalAsync("ATS1", token).ConfigureAwait(false);
                await RequireOkInternalAsync("ATH1", token).ConfigureAwait(false);
                await RequireOkInternalAsync($"ATSH{header:X6}", token).ConfigureAwait(false);

                var command = string.Concat(payload.ToArray().Select(b => b.ToString("X2")));
                return await SendCommandInternalAsync(
                    command,
                    timeout,
                    maxRetries: 1,
                    treatNegativeStatusAsError: false,
                    token).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (adapterModeChanged)
                    {
                        // Legacy default headers vary by protocol. A full reset
                        // is safer than guessing which header to restore.
                        await ResetCoreAsync(CancellationToken.None).ConfigureAwait(false);
                        await InitializeCoreAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _sessionGate.Release();
                }
            }
        }

        public Task<string> GetAdapterVersionAsync(CancellationToken cancellationToken = default) => SendCommandAsync("ATI", cancellationToken);
        public Task<string> GetAdapterVoltageAsync(CancellationToken cancellationToken = default) => SendCommandAsync("ATRV", cancellationToken);

        public async Task ResetAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            EnsureConnected();

            await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ResetCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sessionGate.Release();
            }
        }

        /// <summary>Atomically resets and rebuilds the standard automatic OBD
        /// session. Used as a recovery path after an interrupted OEM flow.</summary>
        public async Task RestoreAutomaticObdSessionAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            EnsureConnected();

            await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ResetCoreAsync(cancellationToken).ConfigureAwait(false);
                await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sessionGate.Release();
            }
        }

        private async Task ResetCoreAsync(CancellationToken cancellationToken)
        {

            _logger.LogInformation("Resetting ELM327 adapter (ATZ)...");
            _isInitialized = false;
            _currentProtocol = ObdProtocol.Unknown;
            _currentProtocolRawDescription = null;

            await ExecuteResetInternalAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("ELM327 adapter reset complete. Re-initialization required before use.");
        }

        public Task<string> SendCommandAsync(string command, CancellationToken token = default)
            => SendCommandAsync(command, DefaultCommandTimeout, DefaultMaxRetries, true, token);

        public async Task<string> SendCommandAsync(string command, TimeSpan timeout, int retries, bool treatNegativeStatusAsError, CancellationToken token = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(command))
            {
                throw new ArgumentException("Command must not be null or empty.", nameof(command));
            }

            EnsureConnected();

            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
            if (retries < 1) throw new ArgumentOutOfRangeException(nameof(retries));

            await _sessionGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await RecoverIfDueAsync(token).ConfigureAwait(false);
                var trimmed = command.Trim();
                try
                {
                    var answer = await SendCommandInternalAsync(
                        trimmed,
                        timeout,
                        retries,
                        treatNegativeStatusAsError,
                        token).ConfigureAwait(false);
                    ObserveStandard(trimmed, answer);
                    return answer;
                }
                catch (Elm327TimeoutException)
                {
                    ObserveStandard(trimmed, "TIMEOUT");
                    throw;
                }
                catch (Elm327CommandException ex)
                {
                    ObserveStandard(trimmed, ex.RawResponse);
                    throw;
                }
            }
            finally
            {
                _sessionGate.Release();
            }
        }

        /// <summary>
        /// Reports a finished physically addressed module read to the read-only tap. Only the read services
        /// 0x19, 0x21 and 0x22 are ever reported: sessions, tester-present, writes, routines and security
        /// requests that pass through the same transport are not. Cheap when nobody listens; never throws.
        /// </summary>
        private static void ObserveModule(int transmitCanId, int? receiveCanId, bool extended29Bit, string requestHex, byte service, string response)
        {
            try
            {
                if (!ObdExchangeTap.HasSubscribers || service is not (0x19 or 0x21 or 0x22))
                    return;

                var frames = FramesForTap(response, extended29Bit, out var firstHeader);
                // The id the reply really came from wins over the id that was asked for: when they differ, that is
                // itself a finding, and it must not be filed under the module that was addressed.
                var receive = firstHeader ?? receiveCanId;
                if (frames is null || receive is null)
                    return;

                ObdExchangeTap.Publish(new ObdRawExchange(transmitCanId, receive.Value, extended29Bit, requestHex, frames));
            }
            catch
            {
                // observation must never affect diagnostics
            }
        }

        /// <summary>
        /// The CAN id a reply really came from: the asked id when any frame carries it, otherwise the id of the first
        /// frame that does arrive, otherwise the asked id (no frame at all, e.g. "NO DATA").
        /// </summary>
        internal static int RealSenderHeader(string? answer, bool extended29Bit, int expectedReceiveId)
        {
            if (string.IsNullOrWhiteSpace(answer))
                return expectedReceiveId;

            var width = extended29Bit ? 8 : 3;
            int? first = null;
            foreach (var raw in answer.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var token = raw.Trim();
                if (token.Length < width || !token.All(Uri.IsHexDigit) || (token.Length != width && (token.Length - width) % 2 != 0))
                    continue;

                var id = int.Parse(token.AsSpan(0, width), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
                if (id == expectedReceiveId)
                    return expectedReceiveId;
                first ??= id;
            }

            return first ?? expectedReceiveId;
        }

        /// <summary>
        /// The adapter prints spaced bytes in these paths ("7E8 04 62 F4 0C 1A") and CleanResponse joins the
        /// lines, so frame boundaries are rebuilt from the CAN id tokens (3 hex digits, or 8 for 29-bit). Replies
        /// the adapter formats over several lines ("0:" / "1:") cannot be rebuilt reliably and are not reported.
        /// </summary>
        internal static string? FramesForTap(string? response, bool extended29Bit, out int? firstHeader)
        {
            firstHeader = null;
            if (string.IsNullOrWhiteSpace(response) || response.Contains(':'))
                return null;

            var width = extended29Bit ? 8 : 3;
            var parts = new List<string>();
            System.Text.StringBuilder? frame = null;
            void Flush()
            {
                if (frame is not null)
                    parts.Add(frame.ToString());
                frame = null;
            }

            foreach (var raw in response.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var token = raw.ToUpperInvariant();
                var hex = token.All(Uri.IsHexDigit);
                if (hex && token.Length == width)
                {
                    Flush();
                    frame = new System.Text.StringBuilder(token);
                    firstHeader ??= int.Parse(token, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (hex && token.Length == 2 && frame is not null)
                {
                    frame.Append(token);
                }
                else
                {
                    Flush();
                    parts.Add(token);
                }
            }

            Flush();
            return parts.Count == 0 ? null : string.Join(" ", parts);
        }

        /// <summary>
        /// Reports a finished standard OBD2 exchange (Mode 01 PIDs, Modes 03/07/0A only) to the read-only tap.
        /// Cheap when nobody listens; never throws.
        /// </summary>
        private void ObserveStandard(string command, string response)
        {
            try
            {
                if (!ObdExchangeTap.HasSubscribers || !ObdExchangeTap.IsObservableStandardRequest(command))
                    return;

                ObdExchangeTap.Publish(new ObdRawExchange(
                    0x7DF, 0, false, command.Replace(" ", string.Empty).ToUpperInvariant(), response ?? string.Empty,
                    ObdExchangeOrigin.StandardObd, IsCanProtocol(_currentProtocol)));
            }
            catch
            {
                // observation must never affect diagnostics
            }
        }

        private async Task<PhysicalFilterState> ConfigureCanCoreAsync(
            int transmitCanId,
            int receiveCanId,
            bool extended29Bit,
            bool automaticFormatting,
            List<string> rejectedSetup,
            CancellationToken token)
        {
            // Per the ELM327 data sheet: CAF1 creates the outgoing PCI byte;
            // CFC1 automatically answers an ECU First Frame; H1 exposes the
            // response PCI bytes so the app can independently validate and
            // reassemble every segment.
            await RequireOkInternalAsync("ATL0", token).ConfigureAwait(false);
            // S1, D0 and (with automatic formatting) CFC1 are the adapter defaults, and ATS/ATD only exist from firmware 1.3
            // on (Elm Electronics version history): a chip that answers "?" keeps working with its default. CAF and the
            // explicit CFC0 are real choices and stay required.
            await TrySetupAsync("ATS1", rejectedSetup, token).ConfigureAwait(false);
            await RequireOkInternalAsync("ATH1", token).ConfigureAwait(false);
            await RequireOkInternalAsync(automaticFormatting ? "ATCAF1" : "ATCAF0", token).ConfigureAwait(false);
            if (automaticFormatting)
                await TrySetupAsync("ATCFC1", rejectedSetup, token).ConfigureAwait(false);
            else
                await RequireOkInternalAsync("ATCFC0", token).ConfigureAwait(false);
            await TrySetupAsync("ATD0", rejectedSetup, token).ConfigureAwait(false);
            await WidenAnswerWindowIfNeededAsync(rejectedSetup, token).ConfigureAwait(false);

            var width = extended29Bit ? 8 : 3;
            await RequireOkInternalAsync($"ATSH{transmitCanId.ToString($"X{width}")}", token).ConfigureAwait(false);
            return await SetPhysicalReceiveFilterAsync(transmitCanId, receiveCanId, extended29Bit, width, rejectedSetup, token).ConfigureAwait(false);
        }

        private async Task RequireOkInternalAsync(string command, CancellationToken token)
        {
            var response = await SendCommandInternalAsync(
                command,
                TimeSpan.FromSeconds(3),
                maxRetries: 2,
                treatNegativeStatusAsError: false,
                token).ConfigureAwait(false);

            if (!IsAffirmativeAtResponse(response))
            {
                LinkEventLog.Add($"Setup: required command {command} NOT accepted (reply \"{AdapterDiagnosticCapture.Brief(response)}\", " +
                                 $"{AdapterExchangeClassifier.Classify(command, response)})");
                throw new Elm327CommandException(
                    $"Adapter does not support required command '{command}'.",
                    response)
                {
                    UnsupportedCommand = command
                };
            }
        }

        /// <summary>Sends an AT command and reports whether the adapter accepted it. Never throws except on cancellation.</summary>
        private async Task<bool> TryOkInternalAsync(string command, CancellationToken token)
        {
            try
            {
                var response = await SendCommandInternalAsync(
                    command,
                    TimeSpan.FromSeconds(2),
                    maxRetries: 1,
                    treatNegativeStatusAsError: false,
                    token).ConfigureAwait(false);
                if (IsAffirmativeAtResponse(response))
                    return true;
                _logger.LogInformation("Adapter rejected optional command '{Command}': '{Response}'.", command, response);
                return false;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Optional adapter command '{Command}' failed.", command);
                return false;
            }
        }

        /// <summary>Clears the receive filter: ATCRA, falling back to ATAR. Failures are ignored.</summary>
        private async Task<bool> TryResetReceiveFilterAsync(CancellationToken token)
        {
            if (await TryOkInternalAsync("ATCRA", token).ConfigureAwait(false))
                return true;
            return await TryOkInternalAsync("ATAR", token).ConfigureAwait(false);
        }

        private async Task RestoreGenericCanSettingsCoreAsync(CancellationToken token)
        {
            try
            {
                await RestoreAnswerWindowAsync(token).ConfigureAwait(false);
                // Clones that reject ATCRA never had a receive address set by
                // it, so a rejection here is not a reason to distrust the adapter.
                await TryResetReceiveFilterAsync(token).ConfigureAwait(false);
                await RequireOkInternalAsync("ATCAF1", token).ConfigureAwait(false);
                await TryOkInternalAsync("ATCFC1", token).ConfigureAwait(false);
                await TryOkInternalAsync("ATD0", token).ConfigureAwait(false);
                // Spaces on is the adapter's default; ATS only exists from firmware 1.3 on, and a chip that rejects
                // ATS1 never had its spaces changed (it would have rejected ATS0 too), so a refusal here is harmless.
                await TryOkInternalAsync("ATS1", token).ConfigureAwait(false);

                var functionalHeader = Is29BitCanProtocol(_currentProtocol)
                    ? "18DB33F1"
                    : "7DF";
                await RequireOkInternalAsync($"ATSH{functionalHeader}", token).ConfigureAwait(false);
                await RequireOkInternalAsync("ATH0", token).ConfigureAwait(false);
            }
            catch
            {
                // The adapter state can no longer be trusted. Force the next
                // normal OBD operation through a complete initialization.
                _isInitialized = false;
                throw;
            }
        }

        private static void ValidateCanIdentifiers(int transmitCanId, int receiveCanId, bool extended29Bit)
        {
            var max = extended29Bit ? 0x1FFFFFFF : 0x7FF;
            if (transmitCanId < 0 || transmitCanId > max)
                throw new ArgumentOutOfRangeException(nameof(transmitCanId));
            if (receiveCanId < 0 || receiveCanId > max)
                throw new ArgumentOutOfRangeException(nameof(receiveCanId));
        }

        private static bool IsCanProtocol(ObdProtocol protocol) => protocol is
            ObdProtocol.ISO_15765_4_CAN_11BIT_500K or
            ObdProtocol.ISO_15765_4_CAN_29BIT_500K or
            ObdProtocol.ISO_15765_4_CAN_11BIT_250K or
            ObdProtocol.ISO_15765_4_CAN_29BIT_250K;

        private static bool Is29BitCanProtocol(ObdProtocol protocol) => protocol is
            ObdProtocol.ISO_15765_4_CAN_29BIT_500K or
            ObdProtocol.ISO_15765_4_CAN_29BIT_250K;

        private static bool IsLegacyPhysicalProtocol(ObdProtocol protocol) => protocol is
            ObdProtocol.SAE_J1850_PWM or
            ObdProtocol.SAE_J1850_VPW or
            ObdProtocol.ISO_9141_2 or
            ObdProtocol.ISO_14230_4_KWP_5BAUD or
            ObdProtocol.ISO_14230_4_KWP_FAST;

        private static bool IsMeaningfulCapabilityResponse(string response) =>
            !string.IsNullOrWhiteSpace(response) &&
            !ObdResponseParser.IsNegative(response) &&
            !response.Contains("STOPPED", StringComparison.OrdinalIgnoreCase) &&
            !response.Contains("UNABLE TO CONNECT", StringComparison.OrdinalIgnoreCase);

        private static string SafeEvidence(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return "No response";

            var oneLine = Regex.Replace(response, @"\s+", " ").Trim();
            return oneLine.Length <= 120 ? oneLine : oneLine[..120] + "…";
        }

        private static bool HasModeResponse(string raw, byte positiveService, byte pid) =>
            ObdResponseParser.ExtractPayload(raw, positiveService, pid).Length > 0;

        private static bool HasCompleteMode09IsoTpResponse(string raw)
        {
            foreach (var canId in ObdResponseParser.ExtractCanIdentifiers(raw))
            {
                var frames = ObdResponseParser.ParseCanFrames(raw, canId);
                for (var i = 0; i < frames.Count; i++)
                {
                    if (frames[i].Length < 2 || (frames[i][0] >> 4) != 1)
                        continue;

                    try
                    {
                        var payload = new IsoTpReassembler().Reassemble(frames.Skip(i));
                        for (var p = 0; p + 1 < payload.Length; p++)
                        {
                            if (payload[p] == 0x49 && payload[p + 1] == 0x02)
                                return true;
                        }
                    }
                    catch
                    {
                        // Try another responding ECU; partial traffic is not a
                        // successful ISO-TP capability result.
                    }
                }
            }

            return false;
        }

        public void EnsureConnected()
        {
            if (!_bluetooth.IsConnected)
            {
                _logger.LogWarning("EnsureConnected check failed: Bluetooth service is not connected.");
                throw new Elm327NotConnectedException("The Bluetooth service is not connected to an ELM327 device.");
            }
        }

        #endregion

        #region Initialization Helpers

        private async Task<bool> SendInitCommandAsync(string command, string description, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Init step: {Description} ({Command})", description, command);

            var stepStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                var response = await SendCommandInternalAsync(
                    command,
                    InitCommandTimeout,
                    DefaultMaxRetries,
                    treatNegativeStatusAsError: false,
                    cancellationToken).ConfigureAwait(false);
                LogInitStep(command, response, stepStarted);

                if (!IsAffirmativeAtResponse(response))
                {
                    _logger.LogWarning(
                        "Init step '{Description}' ({Command}) returned unexpected response: '{Response}'. Continuing.",
                        description, command, response);
                }

                return IsAffirmativeAtResponse(response);
            }
            catch (Elm327TimeoutException ex)
            {
                LinkEventLog.Add($"Init {command}: no reply in time ({(long)System.Diagnostics.Stopwatch.GetElapsedTime(stepStarted).TotalMilliseconds} ms) — initialization stops here");
                _logger.LogError(ex, "Init step '{Description}' ({Command}) timed out.", description, command);
                throw new Elm327InitializationException(
                    $"Initialization failed at step '{description}' ({command}): timed out.", ex);
            }
        }

        /// <summary>One connection-log line for an init command: accepted or not, what the adapter said, how long it took. Never throws.</summary>
        private static void LogInitStep(string command, string? response, long startedAt)
        {
            try
            {
                var verdict = IsAffirmativeAtResponse(response ?? string.Empty) ? "accepted" : "NOT accepted";
                LinkEventLog.Add($"Init {command}: {verdict} (reply \"{AdapterDiagnosticCapture.Brief(response)}\", " +
                                 $"{AdapterExchangeClassifier.Classify(command, response)}), {(long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds} ms");
            }
            catch
            {
                // A log line must never affect initialization.
            }
        }

        /// <summary>The adapter's own banner after ATZ (for example "ELM327 v1.5") and how it is reached: identification without any extra command.</summary>
        private static void LogAdapterBanner(string? response, long startedAt)
        {
            try
            {
                LinkEventLog.Add($"Init ATZ: banner \"{AdapterDiagnosticCapture.Brief(response)}\" over {AdapterDiagnosticCapture.TransportLabel}, " +
                                 $"{(long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds} ms");
            }
            catch
            {
                // A log line must never affect initialization.
            }
        }

        private static bool IsAffirmativeAtResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return false;
            }

            var normalized = response.Trim().ToUpperInvariant();
            return normalized.Contains("OK")
                   || normalized.Contains("ELM327")
                   || normalized.StartsWith("ATZ", StringComparison.Ordinal)
                   || Regex.IsMatch(normalized, @"^[0-9A-F]+$");
        }

        private async Task ExecuteResetInternalAsync(CancellationToken cancellationToken)
        {
            var resetStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                var response = await SendCommandInternalAsync(
                    CMD_RESET,
                    ResetCommandTimeout,
                    maxRetries: 1,
                    treatNegativeStatusAsError: false,
                    cancellationToken).ConfigureAwait(false);

                LogAdapterBanner(response, resetStarted);
                _logger.LogDebug("ATZ response: '{Response}'", response);
            }
            catch (Elm327TimeoutException ex)
            {
                LinkEventLog.Add($"Init ATZ: no reply in time ({(long)System.Diagnostics.Stopwatch.GetElapsedTime(resetStarted).TotalMilliseconds} ms) over {AdapterDiagnosticCapture.TransportLabel}");
                _logger.LogError(ex, "ATZ (reset) timed out.");
                throw new Elm327InitializationException("ELM327 did not respond to reset (ATZ) in time.", ex);
            }

            await Task.Delay(PostResetSettleDelay, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>The result of the 0100 probe that ends initialization, and whether the automatic search had to give up first. Never throws.</summary>
        private static void LogInitProbe(string? response, bool automaticSearchTimedOut)
        {
            try
            {
                LinkEventLog.Add($"Init 0100 probe: {AdapterExchangeClassifier.Classify(CMD_TEST_PID_0100, response)}" +
                                 (automaticSearchTimedOut ? " (the automatic protocol search timed out first)" : string.Empty));
            }
            catch
            {
                // A log line must never affect initialization.
            }
        }

        private static bool ProbeNeedsProtocolFallback(string probeResponse)
        {
            var normalized = (probeResponse ?? string.Empty).Trim().ToUpperInvariant();
            if (Regex.IsMatch(normalized, @"41\s*00"))
                return false;

            return ContainsToken(normalized, "UNABLE TO CONNECT")
                   || (ContainsToken(normalized, "BUS INIT") && ContainsToken(normalized, "ERROR"))
                   || ContainsToken(normalized, "NO DATA");
        }

        // CAN first (most cars), then K-line (ISO 9141 / KWP), then J1850.
        private static readonly int[] FallbackProtocolNumbers = { 6, 8, 7, 9, 3, 4, 5, 1, 2 };
        private static readonly TimeSpan ProtocolFallbackBudget = TimeSpan.FromSeconds(45);

        /// <summary>
        /// Tries ATSP6, 8, 7, 9, 3, 4, 5, 1, 2 one by one (0100 each time) and
        /// keeps the first protocol that answers "41 00". K-line protocols get
        /// a longer ATST because their ECUs answer slowly after the slow init.
        /// Returns the 0100 answer, or null (adapter back on ATSP0) when no
        /// protocol answered. Total time is bounded to ~45 s.
        /// </summary>
        private async Task<string?> TryExplicitProtocolsAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Automatic protocol search failed; trying protocols explicitly.");
            var elapsed = System.Diagnostics.Stopwatch.StartNew();

            foreach (var number in FallbackProtocolNumbers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (elapsed.Elapsed > ProtocolFallbackBudget)
                {
                    LinkEventLog.Add("Protocol search: time budget used up before every protocol was tried");
                    _logger.LogWarning("Protocol fallback stopped: time budget used up.");
                    break;
                }

                var isKLine = number is 3 or 4 or 5;
                var probeTimeout = isKLine
                    ? TimeSpan.FromSeconds(7)
                    : number is 1 or 2
                        ? TimeSpan.FromSeconds(4)
                        : TimeSpan.FromSeconds(3);

                try
                {
                    var selected = await SendCommandInternalAsync(
                        $"ATSP{number}", InitCommandTimeout, 1, treatNegativeStatusAsError: false, cancellationToken).ConfigureAwait(false);
                    if (!IsAffirmativeAtResponse(selected))
                    {
                        LinkEventLog.Add($"Protocol search: ATSP{number} NOT accepted (reply \"{AdapterDiagnosticCapture.Brief(selected)}\")");
                        continue;
                    }

                    // ATST is in 4 ms units: C8 ≈ 800 ms for slow K-line ECUs, 32 ≈ 200 ms default.
                    await SendCommandInternalAsync(
                        isKLine ? "ATSTC8" : "ATST32", InitCommandTimeout, 1, treatNegativeStatusAsError: false, cancellationToken).ConfigureAwait(false);

                    var response = await SendCommandInternalAsync(
                        CMD_TEST_PID_0100, probeTimeout, 1, treatNegativeStatusAsError: false, cancellationToken).ConfigureAwait(false);

                    LinkEventLog.Add($"Protocol search: ATSP{number} -> 0100 {AdapterExchangeClassifier.Classify(CMD_TEST_PID_0100, response)}");
                    if (Regex.IsMatch(response.ToUpperInvariant(), @"41\s*00"))
                    {
                        _logger.LogInformation("Protocol fallback: ATSP{Number} answered 0100.", number);
                        return response;
                    }

                    _logger.LogDebug("Protocol fallback: ATSP{Number} gave '{Response}'.", number, response);
                }
                catch (Elm327TimeoutException ex)
                {
                    LinkEventLog.Add($"Protocol search: ATSP{number} -> no reply in time");
                    _logger.LogDebug(ex, "Protocol fallback: ATSP{Number} timed out.", number);
                }
                catch (Elm327CommandException ex)
                {
                    LinkEventLog.Add($"Protocol search: ATSP{number} -> adapter said \"{AdapterDiagnosticCapture.Brief(ex.RawResponse)}\"");
                    _logger.LogDebug(ex, "Protocol fallback: ATSP{Number} failed.", number);
                }
            }

            // Nothing answered: leave the adapter in automatic mode as before.
            LinkEventLog.Add("Protocol search: no protocol answered 0100; the adapter goes back to automatic");
            try
            {
                await SendCommandInternalAsync("ATST32", InitCommandTimeout, 1, treatNegativeStatusAsError: false, cancellationToken).ConfigureAwait(false);
                await SendCommandInternalAsync(CMD_SET_PROTOCOL_AUTO, InitCommandTimeout, 1, treatNegativeStatusAsError: false, cancellationToken).ConfigureAwait(false);
            }
            catch (Elm327TimeoutException ex)
            {
                _logger.LogDebug(ex, "Could not return the adapter to ATSP0 after the protocol fallback.");
            }

            return null;
        }

        private void ValidateInitialProbeResponse(string probeResponse)
        {
            if (string.IsNullOrWhiteSpace(probeResponse))
            {
                throw new Elm327InitializationException(
                    "Vehicle probe (0100) returned an empty response. The vehicle may be off or the adapter is not seated correctly.");
            }

            var normalized = probeResponse.Trim().ToUpperInvariant();

            if (ContainsToken(normalized, "UNABLE TO CONNECT"))
            {
                throw new Elm327InitializationException(
                    "Unable to connect to the vehicle ECU. Check ignition is ON and the adapter is fully seated in the OBD-II port.");
            }

            if (ContainsToken(normalized, "BUS INIT") && ContainsToken(normalized, "ERROR"))
            {
                throw new Elm327InitializationException(
                    "Bus initialization error while probing the vehicle. The vehicle bus may be unresponsive.");
            }

            if (ContainsToken(normalized, "NO DATA"))
            {
                throw new Elm327InitializationException(
                    "No data received from vehicle for supported-PID probe (0100). Protocol could not be established.");
            }

            if (normalized.Contains("?"))
            {
                throw new Elm327InitializationException(
                    "ELM327 did not understand the probe command (0100). Adapter may be malfunctioning or in an inconsistent state.");
            }

            if (!Regex.IsMatch(normalized, @"41\s*00"))
            {
                _logger.LogWarning(
                    "0100 probe response did not contain expected '4100' header: '{Response}'. Proceeding cautiously.",
                    probeResponse);
            }
        }

        private async Task DetectAndCacheProtocolAsync(CancellationToken cancellationToken)
        {
            string? protocolNumberRaw = null;

            try
            {
                protocolNumberRaw = await SendCommandInternalAsync(
                    CMD_DESCRIBE_PROTOCOL_NUM,
                    InitCommandTimeout,
                    DefaultMaxRetries,
                    treatNegativeStatusAsError: false,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Elm327TimeoutException ex)
            {
                _logger.LogWarning(ex, "ATDPN (describe protocol number) timed out. Falling back to ATDP text description only.");
            }

            _currentProtocol = ParseProtocolNumber(protocolNumberRaw);

            try
            {
                var description = await SendCommandInternalAsync(
                    CMD_DESCRIBE_PROTOCOL,
                    InitCommandTimeout,
                    DefaultMaxRetries,
                    treatNegativeStatusAsError: false,
                    cancellationToken).ConfigureAwait(false);

                _currentProtocolRawDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
            }
            catch (Elm327TimeoutException ex)
            {
                _logger.LogWarning(ex, "ATDP (describe protocol) timed out. Protocol description will be unavailable.");
                _currentProtocolRawDescription = null;
            }

            if (_currentProtocol == ObdProtocol.Unknown && !string.IsNullOrWhiteSpace(_currentProtocolRawDescription))
            {
                _currentProtocol = InferProtocolFromDescription(_currentProtocolRawDescription!);
            }

            LinkEventLog.Add($"Protocol: ATDPN \"{AdapterDiagnosticCapture.Brief(protocolNumberRaw)}\", ATDP \"{AdapterDiagnosticCapture.Brief(_currentProtocolRawDescription, 60)}\" -> {_currentProtocol}");
            _logger.LogInformation(
                "Detected OBD protocol: {Protocol} (raw number response: '{RawNumber}', description: '{Description}')",
                _currentProtocol, protocolNumberRaw ?? "<none>", _currentProtocolRawDescription ?? "<none>");
        }

        private static ObdProtocol ParseProtocolNumber(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return ObdProtocol.Unknown;
            }

            var normalized = raw.Trim().Replace("\r", "").Replace("\n", "").Trim().ToUpperInvariant();
            if (normalized.StartsWith("A", StringComparison.Ordinal) && normalized.Length > 1) normalized = normalized[1..];

            return normalized switch
            {
                "0" => ObdProtocol.Automatic,
                "1" => ObdProtocol.SAE_J1850_PWM,
                "2" => ObdProtocol.SAE_J1850_VPW,
                "3" => ObdProtocol.ISO_9141_2,
                "4" => ObdProtocol.ISO_14230_4_KWP_5BAUD,
                "5" => ObdProtocol.ISO_14230_4_KWP_FAST,
                "6" => ObdProtocol.ISO_15765_4_CAN_11BIT_500K,
                "7" => ObdProtocol.ISO_15765_4_CAN_29BIT_500K,
                "8" => ObdProtocol.ISO_15765_4_CAN_11BIT_250K,
                "9" => ObdProtocol.ISO_15765_4_CAN_29BIT_250K,
                "A" => ObdProtocol.SAE_J1939_CAN,
                "J1939" => ObdProtocol.SAE_J1939_CAN,
                _ => ObdProtocol.Unknown
            };
        }

        private static ObdProtocol InferProtocolFromDescription(string description)
        {
            var normalized = description.ToUpperInvariant();

            if (normalized.Contains("CAN"))
            {
                if (normalized.Contains("29") && normalized.Contains("250"))
                {
                    return ObdProtocol.ISO_15765_4_CAN_29BIT_250K;
                }
                if (normalized.Contains("29"))
                {
                    return ObdProtocol.ISO_15765_4_CAN_29BIT_500K;
                }
                if (normalized.Contains("250"))
                {
                    return ObdProtocol.ISO_15765_4_CAN_11BIT_250K;
                }
                if (normalized.Contains("J1939"))
                {
                    return ObdProtocol.SAE_J1939_CAN;
                }
                return ObdProtocol.ISO_15765_4_CAN_11BIT_500K;
            }

            if (normalized.Contains("ISO 9141") || normalized.Contains("ISO9141"))
            {
                return ObdProtocol.ISO_9141_2;
            }

            if (normalized.Contains("KWP") || normalized.Contains("14230"))
            {
                return normalized.Contains("FAST")
                    ? ObdProtocol.ISO_14230_4_KWP_FAST
                    : ObdProtocol.ISO_14230_4_KWP_5BAUD;
            }

            if (normalized.Contains("J1850"))
            {
                return normalized.Contains("VPW")
                    ? ObdProtocol.SAE_J1850_VPW
                    : ObdProtocol.SAE_J1850_PWM;
            }

            return ObdProtocol.Unknown;
        }

        #endregion

        #region Command Execution (Retry + Timeout + Cleaning)


        // ---- Older-microcode clones: answer window and "response pending" ------------------------------------------
        // Elm Electronics' command table: adaptive timing (ATAT) first appears in v1.2 and the chip handling "response
        // pending" (7F xx 78) by itself in v2.1. An older chip therefore waits a fixed, short time (about 200 ms) and
        // hands a pending frame to the app. Both are read-only fixes: a wider window for this one read, and one repeat
        // of the same read request when the only thing that came back was "pending".
        private bool _adaptiveTimingAccepted = true;
        private bool _answerWindowWidened;

        private async Task WidenAnswerWindowIfNeededAsync(List<string> rejectedSetup, CancellationToken token)
        {
            if (_adaptiveTimingAccepted)
                return;

            if (await TrySetupAsync("ATST64", rejectedSetup, token).ConfigureAwait(false))
                _answerWindowWidened = true;   // 0x64 x 4 ms = 400 ms
        }

        private async Task RestoreAnswerWindowAsync(CancellationToken token)
        {
            if (!_answerWindowWidened)
                return;

            _answerWindowWidened = false;
            await TryOkInternalAsync("ATST32", token).ConfigureAwait(false);   // the default, 200 ms
        }

        private static bool IsResponsePendingOnly(string answer, int? receiveCanId, byte service)
        {
            var frames = ObdResponseParser.ParseCanFrames(answer, receiveCanId);
            return frames.Count > 0 && frames.All(frame =>
                frame.Length >= 4 && (frame[0] >> 4) == 0 && frame[1] == 0x7F && frame[2] == service && frame[3] == 0x78);
        }

        /// <summary>Sends one read request; if only "response pending" came back, asks once more with a longer wait.</summary>
        private async Task<string> SendReadRequestAsync(string command, TimeSpan timeout, int? receiveCanId, byte service, CancellationToken token)
        {
            var answer = await SendCommandInternalAsync(command, timeout, maxRetries: 1, treatNegativeStatusAsError: false, token).ConfigureAwait(false);
            if (!IsResponsePendingOnly(answer, receiveCanId, service))
                return answer;

            LinkEventLog.Add($"Response pending for {command}: asking once more with a longer wait");
            await Task.Delay(400, token).ConfigureAwait(false);
            return await SendCommandInternalAsync(command, timeout + timeout, maxRetries: 1, treatNegativeStatusAsError: false, token).ConfigureAwait(false);
        }
        // ---- Adapter self-recovery -------------------------------------------------------------
        // Cheap adapters (and a long session of module scans) can end up in a state where every data request times out
        // or is answered with an error word, although the Bluetooth link itself is fine; until now only disconnecting and
        // reconnecting helped. When data requests keep failing in a row, the next operation first resets the adapter
        // (ATZ) and initialises it again — the same thing a reconnect does for the adapter's own state. Bounded: a
        // streak is needed, attempts are spaced out and back off when they do not help (for example ignition off).
        private const int HygieneResetAfterRequests = 30;
        private const int FailureStreakThreshold = 6;
        private static readonly TimeSpan RecoveryMinimumGap = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan RecoveryMaximumGap = TimeSpan.FromMinutes(5);
        private int _failureStreak;
        private DateTime _lastRecoveryAt = DateTime.MinValue;
        private TimeSpan _recoveryGap = RecoveryMinimumGap;

        /// <summary>How many data requests in a row failed at adapter level (timeout, '?', bus/buffer errors).</summary>
        internal int FailureStreak => Volatile.Read(ref _failureStreak);

        private static bool IsDataRequest(string command) =>
            command.Length >= 2 && command.All(Uri.IsHexDigit);

        private static bool IsAdapterLevelFailure(string cleaned)
        {
            var text = cleaned.Trim().ToUpperInvariant();
            return text == "?" || text.Contains("BUFFER FULL") || text.Contains("CAN ERROR") || text.Contains("FB ERROR") ||
                   text.Contains("BUS BUSY") || text.Contains("UNABLE TO CONNECT") || text.Contains("DATA ERROR") ||
                   text.Contains("STOPPED");
        }

        private async Task<string> SendCommandInternalAsync(
            string command,
            TimeSpan timeout,
            int maxRetries,
            bool treatNegativeStatusAsError,
            CancellationToken cancellationToken)
        {
            var countable = IsDataRequest(command.Trim());
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                var answer = await SendCommandCoreAsync(command, timeout, maxRetries, treatNegativeStatusAsError, cancellationToken).ConfigureAwait(false);
                if (countable)
                {
                    _sessionTally?.Add(AdapterExchangeClassifier.Classify(command, answer));
                    var failed = IsAdapterLevelFailure(answer);
                    Load.Record(System.Diagnostics.Stopwatch.GetElapsedTime(started), ok: !failed);
                    if (failed)
                        Interlocked.Increment(ref _failureStreak);
                    else if (!answer.Contains("NO DATA", StringComparison.OrdinalIgnoreCase))
                        Interlocked.Exchange(ref _failureStreak, 0);   // a real answer (positive or an ECU refusal): the adapter works
                }

                return answer;
            }
            catch (Elm327TimeoutException)
            {
                LinkEventLog.Add($"Timeout: {command.Trim()} (streak {Volatile.Read(ref _failureStreak) + (IsDataRequest(command.Trim()) ? 1 : 0)}); " +
                                 $"no reply within {AdapterDiagnosticCapture.Seconds(timeout)} s, {maxRetries} attempt(s), {(long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds} ms spent");
                if (countable)
                {
                    _sessionTally?.Add(new ExchangeAssessment(ExchangeKind.TimedOut));
                    Load.Record(System.Diagnostics.Stopwatch.GetElapsedTime(started), ok: false);
                    Interlocked.Increment(ref _failureStreak);
                }
                throw;
            }
            catch (Elm327CommandException ex) when (countable && IsAdapterLevelFailure(ex.RawResponse))
            {
                _sessionTally?.Add(AdapterExchangeClassifier.Classify(ex));
                Load.Record(System.Diagnostics.Stopwatch.GetElapsedTime(started), ok: false);
                Interlocked.Increment(ref _failureStreak);
                throw;
            }
            catch (Exception ex) when (TallyOtherFailure(countable, ex))
            {
                throw;   // not reached: the filter only counts the failure for the module-session summary and returns false
            }
        }

        /// <summary>The one module session that is running (null otherwise): a count of what its data requests turned out to be.</summary>
        private volatile ExchangeTally? _sessionTally;

        /// <summary>Notes which CAN ids the frames of a module-session reply came from. Never throws.</summary>
        private void NoteSessionSenders(string? reply, int expectedReceiveId, bool extended29Bit)
        {
            try
            {
                _sessionTally?.NoteSenders(reply, expectedReceiveId, extended29Bit);
            }
            catch
            {
                // Observation must never affect a diagnostic request.
            }
        }

        /// <summary>Exception filter for the module-session summary. It never catches anything: it always returns false.</summary>
        private bool TallyOtherFailure(bool countable, Exception exception)
        {
            if (countable)
                _sessionTally?.Add(AdapterExchangeClassifier.Classify(exception));
            return false;
        }

        /// <summary>Runs inside the session gate, before an operation: resets and re-initialises a failing adapter.</summary>
        private async Task RecoverIfDueAsync(CancellationToken token)
        {
            if (Volatile.Read(ref _failureStreak) < FailureStreakThreshold || !_isInitialized)
                return;
            if (DateTime.UtcNow - _lastRecoveryAt < _recoveryGap)
                return;

            _lastRecoveryAt = DateTime.UtcNow;
            Interlocked.Exchange(ref _failureStreak, 0);
            LinkEventLog.Add("Recovery: data requests kept failing, resetting the adapter (ATZ) and initialising again");
            _logger.LogWarning("Adapter stopped answering data requests; resetting and re-initialising it (recovery).");
            try
            {
                await ResetCoreAsync(token).ConfigureAwait(false);
                await InitializeCoreAsync(token).ConfigureAwait(false);
                Interlocked.Exchange(ref _failureStreak, 0);
                _recoveryGap = RecoveryMinimumGap;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // It did not help (car asleep, adapter really gone): wait longer before trying again.
                _recoveryGap = TimeSpan.FromTicks(Math.Min(_recoveryGap.Ticks * 2, RecoveryMaximumGap.Ticks));
                throw;
            }
        }

        private async Task<string> SendCommandCoreAsync(
            string command,
            TimeSpan timeout,
            int maxRetries,
            bool treatNegativeStatusAsError,
            CancellationToken cancellationToken)
        {
            EnsureConnected();

            Exception? lastException = null;

            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    string rawResponse;

                    // A strained adapter gets a quiet moment between data requests (init and reset commands are never delayed).
                    var gap = Load.MinimumGap;
                    if (gap > TimeSpan.Zero && IsDataRequest(command.Trim()))
                    {
                        var quiet = System.Diagnostics.Stopwatch.GetElapsedTime(Volatile.Read(ref _lastDataCommandEndTicks));
                        if (quiet < gap)
                            await Task.Delay(gap - quiet, cancellationToken).ConfigureAwait(false);
                    }

                    var sentAt = System.Diagnostics.Stopwatch.GetTimestamp();
                    try
                    {
                        rawResponse = await _bluetooth.SendCommandAsync(command, timeout, cancellationToken).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        AdapterDiagnosticCapture.RecordTimeout(command, attempt, maxRetries, timeout, System.Diagnostics.Stopwatch.GetElapsedTime(sentAt));
                        throw new Elm327TimeoutException(
                            $"Command '{command}' timed out after {timeout.TotalSeconds:F1}s on attempt {attempt}/{maxRetries}.");
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        AdapterDiagnosticCapture.RecordTimeout(command, attempt, maxRetries, timeout, System.Diagnostics.Stopwatch.GetElapsedTime(sentAt));
                        throw new Elm327TimeoutException(
                            $"Command '{command}' timed out after {timeout.TotalSeconds:F1}s on attempt {attempt}/{maxRetries}.");
                    }
                    catch (Exception ex) when (ObserveTransportFailure(command, attempt, maxRetries, timeout, sentAt, ex))
                    {
                        throw;   // not reached: the filter only records the failure for the optional raw capture and returns false
                    }

                    AdapterDiagnosticCapture.RecordReply(command, attempt, maxRetries, timeout, System.Diagnostics.Stopwatch.GetElapsedTime(sentAt), rawResponse);
                    var cleaned = CleanResponse(rawResponse);

                    _logger.LogDebug(
                        "Command '{Command}' attempt {Attempt}/{MaxRetries} raw='{Raw}' cleaned='{Cleaned}'",
                        command, attempt, maxRetries, rawResponse, cleaned);

                    if (ContainsTransientStatus(cleaned))
                    {
                        _logger.LogDebug(
                            "Command '{Command}' returned transient status '{Cleaned}' on attempt {Attempt}. Retrying if attempts remain.",
                            command, cleaned, attempt);

                        lastException = new Elm327CommandException(
                            $"Adapter reported transient status: {cleaned}", cleaned);

                        if (attempt < maxRetries)
                        {
                            await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        if (treatNegativeStatusAsError)
                        {
                            throw lastException;
                        }

                        return cleaned;
                    }

                    if (IsMismatchedModeReply(command, cleaned))
                    {
                        // A late answer to an earlier request (after a timeout)
                        // must never be used as the value of this PID.
                        _logger.LogWarning(
                            "Command '{Command}' got a reply for a different request: '{Cleaned}'.", command, cleaned);
                        lastException = new Elm327CommandException(
                            $"Reply does not match request '{command}'.", cleaned);

                        if (attempt < maxRetries)
                        {
                            await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        return cleaned;
                    }

                    if (treatNegativeStatusAsError && ContainsNegativeStatus(cleaned))
                    {
                        _logger.LogWarning(
                            "Command '{Command}' returned negative status: '{Cleaned}'.", command, cleaned);
                        throw new Elm327CommandException(
                            $"Adapter/vehicle returned an error status: {cleaned}", cleaned);
                    }

                    return cleaned;
                }
                catch (Elm327CommandException)
                {
                    throw;
                }
                catch (Elm327TimeoutException ex)
                {
                    lastException = ex;

                    _logger.LogWarning(
                        "Command '{Command}' timed out on attempt {Attempt}/{MaxRetries} (timeout={TimeoutSeconds}s).",
                        command, attempt, maxRetries, timeout.TotalSeconds);

                    if (attempt < maxRetries)
                    {
                        await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;

                    _logger.LogError(
                        ex,
                        "Command '{Command}' failed with unexpected exception on attempt {Attempt}/{MaxRetries}.",
                        command,
                        attempt,
                        maxRetries);

                    if (attempt < maxRetries)
                    {
                        await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }
                finally
                {
                    Volatile.Write(ref _lastDataCommandEndTicks, System.Diagnostics.Stopwatch.GetTimestamp());
                    _commandGate.Release();
                }
            }

            if (lastException is Elm327TimeoutException timeoutEx)
            {
                throw timeoutEx;
            }

            throw new Elm327TimeoutException(
                $"Command '{command}' failed after {maxRetries} attempt(s).");
        }

        /// <summary>
        /// True when a Mode 01 / 09 / 22 request ("01PP", "09PP", "22DDDD",
        /// optionally with a response-count digit) got a positive answer of the
        /// same service for a different PID/DID and no answer for the requested
        /// one. Only spaced output (ATS1, the generic session) is checked;
        /// negative/status replies are left to the normal handling.
        /// </summary>
        private static bool IsMismatchedModeReply(string command, string cleaned)
        {
            if (string.IsNullOrEmpty(cleaned) || !cleaned.Contains(' '))
                return false;

            var cmd = command.Replace(" ", string.Empty).ToUpperInvariant();
            if (!Regex.IsMatch(cmd, @"^[0-9A-F]+$"))
                return false;

            byte[] expected;
            if (cmd.Length is 4 or 5 && (cmd.StartsWith("01", StringComparison.Ordinal) || cmd.StartsWith("09", StringComparison.Ordinal)))
            {
                var service = cmd.StartsWith("01", StringComparison.Ordinal) ? (byte)0x41 : (byte)0x49;
                expected = new[] { service, Convert.ToByte(cmd.Substring(2, 2), 16) };
            }
            else if (cmd.Length is 6 or 7 && cmd.StartsWith("22", StringComparison.Ordinal))
            {
                expected = new[] { (byte)0x62, Convert.ToByte(cmd.Substring(2, 2), 16), Convert.ToByte(cmd.Substring(4, 2), 16) };
            }
            else
            {
                return false;
            }

            if (ContainsTransientStatus(cleaned) || ContainsNegativeStatus(cleaned))
                return false;

            var bytes = ObdResponseParser.ParseHex(cleaned);
            if (bytes.Length < expected.Length)
                return false;

            var otherPositive = false;
            for (var i = 0; i + expected.Length <= bytes.Length; i++)
            {
                if (bytes[i] != expected[0])
                    continue;

                var match = true;
                for (var k = 1; k < expected.Length; k++)
                {
                    if (bytes[i + k] != expected[k]) { match = false; break; }
                }

                if (match)
                    return false;
                otherPositive = true;
            }

            return otherPositive;
        }

        /// <summary>
        /// Exception filter that only hands the failure to the optional raw capture (a no-op unless somebody switched it on).
        /// It never catches anything: it always returns false, so the exception travels on exactly as before.
        /// </summary>
        private static bool ObserveTransportFailure(string command, int attempt, int attempts, TimeSpan budget, long sentAt, Exception exception)
        {
            AdapterDiagnosticCapture.RecordFailure(command, attempt, attempts, budget, System.Diagnostics.Stopwatch.GetElapsedTime(sentAt), exception);
            return false;
        }

        private static async Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken)
        {
            var delay = TimeSpan.FromMilliseconds(RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Response Cleaning & Status Detection

        private static string CleanResponse(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            var text = raw
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');

            text = text.Replace(PROMPT_TERMINATOR, string.Empty);

            var lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToArray();

            if (lines.Length == 0)
            {
                return string.Empty;
            }

            var joined = string.Join(" ", lines);
            joined = Regex.Replace(joined, @"\s+", " ").Trim();

            return joined;
        }

        private static bool ContainsToken(string normalizedUpperText, string token)
        {
            return normalizedUpperText.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsTransientStatus(string cleaned)
        {
            if (string.IsNullOrEmpty(cleaned))
            {
                return false;
            }

            var normalized = cleaned.ToUpperInvariant();
            return TransientStatusTokens.Any(token => ContainsToken(normalized, token));
        }

        private static bool ContainsNegativeStatus(string cleaned)
        {
            if (string.IsNullOrEmpty(cleaned))
            {
                return true;
            }

            var normalized = cleaned.ToUpperInvariant();

            if (normalized == "?" || normalized == "ERROR")
            {
                return true;
            }

            return NegativeStatusTokens.Any(token => ContainsToken(normalized, token));
        }

        #endregion

        #region Connection state

        private void OnConnectionChanged(object? sender, bool connected)
        {
            if (connected) return;
            _isInitialized = false;
            _currentProtocol = ObdProtocol.Unknown;
            _currentProtocolRawDescription = null;
            _logger.LogInformation("ELM327 state reset because Bluetooth connection was lost.");
        }

        #endregion

        #region Disposal

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(Elm327Protocol));
            }
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            _bluetooth.ConnectionChanged -= OnConnectionChanged;
            _sessionGate.Dispose();
            _commandGate.Dispose();

            _logger.LogDebug("Elm327Protocol disposed.");

            return ValueTask.CompletedTask;
        }

        #endregion
    }
}
