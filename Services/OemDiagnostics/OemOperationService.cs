using System.Globalization;
using CarScanner.Models;
using CarScanner.Services.UDS;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Executes only catalog-authorized, exact OEM operations after runtime safety
/// checks. Generic OBD-II or brand selection alone can never authorize a write.
/// </summary>
public sealed class OemOperationService
{
    private readonly OemOperationCatalogService _catalog;
    private readonly VehicleProfileResolverService _vehicleProfiles;
    private readonly OemAddressRegistry _addresses;
    private readonly OemModuleDiscoveryService _discovery;
    private readonly OBDService _obd;
    private readonly UdsProtocol _uds;
    private readonly SafetyGatingService _safety;
    private readonly OemCodingBackupService _backups;

    public OemOperationAvailabilityResult? CurrentAvailability { get; private set; }
    public event EventHandler<string>? ProgressChanged;

    public OemOperationService(
        OemOperationCatalogService catalog,
        VehicleProfileResolverService vehicleProfiles,
        OemAddressRegistry addresses,
        OemModuleDiscoveryService discovery,
        OBDService obd,
        UdsProtocol uds,
        SafetyGatingService safety,
        OemCodingBackupService backups)
    {
        _catalog = catalog;
        _vehicleProfiles = vehicleProfiles;
        _addresses = addresses;
        _discovery = discovery;
        _obd = obd;
        _uds = uds;
        _safety = safety;
        _backups = backups;
    }

    public async Task<OemOperationAvailabilityResult> DetectAsync(
        VehicleInfo vehicle,
        CancellationToken token = default) =>
        await DetectAsync(vehicle, null, null, token).ConfigureAwait(false);

    public async Task<OemOperationAvailabilityResult> DetectAsync(
        VehicleInfo vehicle,
        OemModuleDiscoveryResult? knownDiscovery,
        AdapterDiagnostics? knownAdapter,
        CancellationToken token = default)
    {
        await _catalog.EnsureLoadedAsync(token).ConfigureAwait(false);
        var resolved = _vehicleProfiles.Resolve(vehicle);
        var operations = _catalog.GetForVehicle(resolved);
        if (operations.Count == 0)
            return Publish(new OemOperationAvailabilityResult(
                Array.Empty<OemOperationAvailability>(),
                "NOT SUPPORTED",
                _catalog.LoadErrors.Count > 0
                    ? $"OEM operation package უარყოფილია: {string.Join("; ", _catalog.LoadErrors)}"
                    : "ამ ზუსტი მოდელის/წლისთვის reviewed Active Test, Service ან Coding operation არ არსებობს."));

        var discovery = knownDiscovery ??
            await _discovery.DiscoverAsync(vehicle, token).ConfigureAwait(false);
        var responding = discovery.Probes
            .Where(probe => probe.State is OemModuleProbeState.Available or OemModuleProbeState.RespondedLimited)
            .Select(probe => probe.DefinitionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var adapter = knownAdapter ??
            await _obd.GetAdapterDiagnosticsAsync(token).ConfigureAwait(false);
        var voltage = ParseVoltage(adapter.Voltage);
        var availability = operations.Select(operation =>
        {
            if (!responding.Contains(operation.DefinitionId))
                return new OemOperationAvailability(
                    operation, OemOperationAvailabilityState.NoResponse,
                    "Verified ECU address exists, but this ECU did not answer the discovery probe.");
            if (operation.Safety.RequiresSecurityAccess)
                return new OemOperationAvailability(
                    operation, OemOperationAvailabilityState.NotSupported,
                    "Operation requires an OEM security provider; no key/security algorithm is configured.");
            if (!voltage.HasValue || voltage < operation.Safety.MinimumVoltage || voltage > operation.Safety.MaximumVoltage)
                return new OemOperationAvailability(
                    operation, OemOperationAvailabilityState.PreconditionsRequired,
                    voltage.HasValue
                        ? $"12V supply is {voltage:0.00} V; required {operation.Safety.MinimumVoltage:0.0}–{operation.Safety.MaximumVoltage:0.0} V."
                        : "Adapter did not provide a reliable 12V voltage reading.");
            return new OemOperationAvailability(
                operation, OemOperationAvailabilityState.Available,
                "Exact vehicle, ECU response, reviewed operation and safe voltage are confirmed. Stationary/engine state is checked at execution.");
        }).ToArray();

        return Publish(new OemOperationAvailabilityResult(
            availability,
            availability.Any(item => item.CanExecute) ? "AVAILABLE" : "NOT READY",
            $"Reviewed operations: {availability.Length} · executable now: {availability.Count(item => item.CanExecute)}."));
    }

    public async Task<OemOperationExecutionResult> ExecuteAsync(
        VehicleInfo vehicle,
        string operationId,
        bool userConfirmed,
        CancellationToken token = default)
    {
        // Cancellation/declined confirmation must precede even read-only discovery.
        if (!userConfirmed || token.IsCancellationRequested)
            return new OemOperationExecutionResult(operationId, DiagnosticOperationStatus.Cancelled,
                "Operation cancelled before ECU communication.", Array.Empty<string>());
        var availability = await DetectAsync(vehicle, token).ConfigureAwait(false);
        var item = availability.Operations.FirstOrDefault(candidate =>
            candidate.Definition.OperationId.Equals(operationId, StringComparison.OrdinalIgnoreCase));
        if (item is null || !item.CanExecute)
            return new OemOperationExecutionResult(
                operationId,
                DiagnosticOperationStatus.Unsupported,
                item?.Detail ?? "Reviewed operation is not available for this exact vehicle.",
                Array.Empty<string>());

        var operation = item.Definition;
        var precondition = await ValidateRuntimePreconditionsAsync(operation, userConfirmed, token).ConfigureAwait(false);
        if (!precondition.IsSuccess)
            return new OemOperationExecutionResult(operationId, precondition.Status, precondition.Message, Array.Empty<string>());

        var resolved = _vehicleProfiles.Resolve(vehicle);
        var address = _addresses.GetForVehicle(resolved).First(candidate =>
            candidate.DefinitionId.Equals(operation.DefinitionId, StringComparison.OrdinalIgnoreCase));
        var completed = new List<string>();
        var backupPath = string.Empty;
        var activeStarted = false;
        try
        {
            if (operation.Type == OemOperationType.Coding)
            {
                ProgressChanged?.Invoke(this, "Creating ECU coding backup…");
                var backup = await CreateBackupAsync(operation, address, vehicle, token).ConfigureAwait(false);
                if (!backup.Result.IsSuccess)
                    return backup.Result;
                backupPath = backup.Path;
                completed.Add("ECU backup saved");
            }

            var executionSteps = operation.Steps
                .Where(step => step.Role is "PREPARE" or "EXECUTE")
                .ToArray();
            for (var index = 0; index < executionSteps.Length; index++)
            {
                var step = executionSteps[index];
                ProgressChanged?.Invoke(this, $"Step {index + 1}/{executionSteps.Length + operation.Steps.Count(item => item.Role == "VERIFY")} · {step.Name}");
                var result = await ExecuteStepAsync(step, address, token).ConfigureAwait(false);
                if (!result.Success)
                    return new OemOperationExecutionResult(
                        operationId, DiagnosticOperationStatus.ProtocolError,
                        $"{step.Name}: {result.Detail}", completed, backupPath);
                completed.Add(step.Name);
                activeStarted |= operation.Type == OemOperationType.ActiveTest && step.Role == "EXECUTE";
                if (step.DelayAfterMs > 0)
                    await Task.Delay(step.DelayAfterMs, token).ConfigureAwait(false);
            }

            if (operation.Type == OemOperationType.ActiveTest)
            {
                ProgressChanged?.Invoke(this, $"ACTIVE TEST RUNNING — automatic stop in {operation.Safety.MaximumRunSeconds}s");
                await Task.Delay(TimeSpan.FromSeconds(operation.Safety.MaximumRunSeconds), token).ConfigureAwait(false);
                var stop = operation.Steps.Single(step => step.Role == "STOP");
                var stopResult = await ExecuteStepAsync(stop, address, CancellationToken.None).ConfigureAwait(false);
                activeStarted = false;
                if (!stopResult.Success)
                    return new OemOperationExecutionResult(
                        operationId, DiagnosticOperationStatus.ProtocolError,
                        $"STOP command was sent but not positively confirmed: {stopResult.Detail}", completed, backupPath);
                completed.Add(stop.Name);
            }

            var verificationSteps = operation.Steps.Where(step => step.Role == "VERIFY").ToArray();
            for (var index = 0; index < verificationSteps.Length; index++)
            {
                var step = verificationSteps[index];
                ProgressChanged?.Invoke(this, $"Verification {index + 1}/{verificationSteps.Length} · {step.Name}");
                var result = await ExecuteStepAsync(step, address, token).ConfigureAwait(false);
                if (!result.Success)
                    return new OemOperationExecutionResult(
                        operationId, DiagnosticOperationStatus.ProtocolError,
                        $"ECU accepted the command, but verification failed: {result.Detail}", completed, backupPath);
                completed.Add(step.Name);
            }

            return new OemOperationExecutionResult(
                operationId,
                DiagnosticOperationStatus.Success,
                operation.Type == OemOperationType.ActiveTest
                    ? "Active test completed and the reviewed STOP command was confirmed."
                    : "ECU accepted the operation and the reviewed verification response matched.",
                completed,
                backupPath);
        }
        catch (OperationCanceledException)
        {
            return new OemOperationExecutionResult(
                operationId, DiagnosticOperationStatus.Cancelled,
                "Operation was cancelled.", completed, backupPath);
        }
        finally
        {
            if (activeStarted)
            {
                var stop = operation.Steps.Single(step => step.Role == "STOP");
                try { await ExecuteStepAsync(stop, address, CancellationToken.None).ConfigureAwait(false); }
                catch { }
            }
            try { await _obd.RestoreGenericSessionAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { }
        }
    }

    public async Task<OemOperationExecutionResult> RestoreLatestCodingBackupAsync(
        VehicleInfo vehicle,
        bool userConfirmed,
        CancellationToken token = default)
    {
        if (!userConfirmed)
            return new OemOperationExecutionResult(
                "restore-backup", DiagnosticOperationStatus.PreconditionsFailed,
                "Explicit user confirmation is required.", Array.Empty<string>());
        var backup = await _backups.GetLatestAsync(vehicle.VIN, token).ConfigureAwait(false);
        if (backup is null)
            return new OemOperationExecutionResult(
                "restore-backup", DiagnosticOperationStatus.Unsupported,
                "No coding backup exists for this VIN.", Array.Empty<string>());

        var availability = await DetectAsync(vehicle, token).ConfigureAwait(false);
        var item = availability.Operations.FirstOrDefault(candidate =>
            candidate.Definition.OperationId.Equals(backup.OperationId, StringComparison.OrdinalIgnoreCase) &&
            candidate.CanExecute);
        if (item is null)
            return new OemOperationExecutionResult(
                backup.OperationId, DiagnosticOperationStatus.Unsupported,
                "The backup's exact vehicle/ECU operation is not currently available.", Array.Empty<string>());

        var operation = item.Definition;
        var precondition = await ValidateRuntimePreconditionsAsync(operation, true, token).ConfigureAwait(false);
        if (!precondition.IsSuccess)
            return new OemOperationExecutionResult(operation.OperationId, precondition.Status, precondition.Message, Array.Empty<string>());

        var payload = Convert.FromHexString(backup.ResponsePayloadHex);
        var restoreRequest = new[] { (byte)0x2E }.Concat(payload).ToArray();
        if (restoreRequest.Length > 7)
            return new OemOperationExecutionResult(
                operation.OperationId, DiagnosticOperationStatus.Unsupported,
                "Saved coding value requires segmented transmit, which this adapter path does not support.", Array.Empty<string>());

        var resolved = _vehicleProfiles.Resolve(vehicle);
        var address = _addresses.GetForVehicle(resolved).First(candidate =>
            candidate.DefinitionId.Equals(operation.DefinitionId, StringComparison.OrdinalIgnoreCase));
        try
        {
            var response = await _uds.SendIsoTpAsync(
                restoreRequest, address.TxCanId, address.RxCanId, address.Extended29Bit,
                TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
            var write = OemOperationResponseValidator.Validate(restoreRequest, response);
            if (!write.Success)
                return new OemOperationExecutionResult(operation.OperationId, DiagnosticOperationStatus.ProtocolError, write.Detail, Array.Empty<string>());

            var verifyRequest = OemOperationPackageParser.ParseRequest(backup.ReadRequestHex);
            var verifyResponse = await _uds.SendIsoTpAsync(
                verifyRequest, address.TxCanId, address.RxCanId, address.Extended29Bit,
                TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
            var expected = backup.ResponsePayloadHex;
            var verify = OemOperationResponseValidator.Validate(verifyRequest, verifyResponse, expected);
            return new OemOperationExecutionResult(
                operation.OperationId,
                verify.Success ? DiagnosticOperationStatus.Success : DiagnosticOperationStatus.ProtocolError,
                verify.Success ? "Original coding backup was restored and verified." : verify.Detail,
                verify.Success ? new[] { "Backup write", "Backup verification" } : new[] { "Backup write" });
        }
        finally
        {
            try { await _obd.RestoreGenericSessionAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { }
        }
    }

    private async Task<DiagnosticOperationResult<bool>> ValidateRuntimePreconditionsAsync(
        OemOperationDefinition operation,
        bool userConfirmed,
        CancellationToken token)
    {
        var adapter = await _obd.GetAdapterDiagnosticsAsync(token).ConfigureAwait(false);
        var voltage = ParseVoltage(adapter.Voltage);
        var stationary = true;
        if (operation.Safety.RequiresStationary)
        {
            var speed = await _obd.ReadSensorAsync(0x0D, token).ConfigureAwait(false);
            stationary = speed is not null && TryValue(speed.Value, out var speedValue) && speedValue < 0.5;
            if (!stationary)
                return new DiagnosticOperationResult<bool>(
                    DiagnosticOperationStatus.PreconditionsFailed, false,
                    speed is null ? "Vehicle speed could not be verified; operation is blocked." : "Vehicle must be stationary.");
        }

        if (operation.Safety.RequiredEngineState != "ANY")
        {
            var rpm = await _obd.ReadSensorAsync(0x0C, token).ConfigureAwait(false);
            if (rpm is null || !TryValue(rpm.Value, out var rpmValue))
                return new DiagnosticOperationResult<bool>(
                    DiagnosticOperationStatus.PreconditionsFailed, false,
                    "Engine state could not be verified from ECU RPM.");
            if (operation.Safety.RequiredEngineState == "OFF" && rpmValue >= 50)
                return new DiagnosticOperationResult<bool>(DiagnosticOperationStatus.PreconditionsFailed, false, "Engine must be OFF.");
            if (operation.Safety.RequiredEngineState == "RUNNING" && rpmValue < 50)
                return new DiagnosticOperationResult<bool>(DiagnosticOperationStatus.PreconditionsFailed, false, "Engine must be RUNNING.");
        }

        var capability = new VehicleCapability
        {
            Code = operation.OperationId,
            Ecu = operation.EcuId,
            VehicleProfile = operation.VehicleProfile,
            RoutineSupported = operation.Type != OemOperationType.Coding,
            WriteSupported = operation.Type == OemOperationType.Coding,
            SafetyLevel = operation.Type.ToString(),
            RequiresVoltageCheck = true,
            RequiresSecurityAccess = operation.Safety.RequiresSecurityAccess,
            EnabledByDefault = false
        };
        return _safety.Validate(
            capability,
            adapter.VehicleResponding,
            ecuIdentified: true,
            stableVoltage: voltage.HasValue && voltage >= operation.Safety.MinimumVoltage && voltage <= operation.Safety.MaximumVoltage,
            sessionValid: stationary,
            securityValid: !operation.Safety.RequiresSecurityAccess,
            userConfirmed: userConfirmed);
    }

    private async Task<(OemOperationExecutionResult Result, string Path)> CreateBackupAsync(
        OemOperationDefinition operation,
        VerifiedEcuAddress address,
        VehicleInfo vehicle,
        CancellationToken token)
    {
        var request = OemOperationPackageParser.ParseRequest(operation.BackupReadHex);
        var response = await _uds.SendIsoTpAsync(
            request, address.TxCanId, address.RxCanId, address.Extended29Bit,
            TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
        var valid = OemOperationResponseValidator.Validate(request, response);
        if (!valid.Success || response.Payload.Length < 3 || response.Payload.Length + 1 > 7)
            return (new OemOperationExecutionResult(
                operation.OperationId, DiagnosticOperationStatus.ProtocolError,
                valid.Success ? "Coding backup is too large for the verified adapter transmit path." : valid.Detail,
                Array.Empty<string>()), string.Empty);

        var backup = new OemCodingBackup
        {
            OperationId = operation.OperationId,
            DefinitionId = operation.DefinitionId,
            Vin = vehicle.VIN,
            EcuId = operation.EcuId,
            ReadRequestHex = Convert.ToHexString(request),
            ResponsePayloadHex = Convert.ToHexString(response.Payload),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            EvidenceSource = operation.Evidence.SourceReference
        };
        var path = await _backups.SaveAsync(backup, token).ConfigureAwait(false);
        return (new OemOperationExecutionResult(operation.OperationId, DiagnosticOperationStatus.Success, "Backup saved.", Array.Empty<string>(), path), path);
    }

    private async Task<(bool Success, string Detail)> ExecuteStepAsync(
        OemOperationStepDefinition step,
        VerifiedEcuAddress address,
        CancellationToken token)
    {
        var request = OemOperationPackageParser.ParseRequest(step.RequestHex);
        ServiceDiagnosticTrace.Record($"{address.DefinitionId}/{address.EcuId}/{step.Role}",
            step.RequestHex, "", "Sending reviewed operation step", address.TxCanId, address.RxCanId);
        var response = await _uds.SendIsoTpAsync(
            request, address.TxCanId, address.RxCanId, address.Extended29Bit,
            TimeSpan.FromMilliseconds(step.TimeoutMs), token).ConfigureAwait(false);
        var validation = OemOperationResponseValidator.Validate(request, response, step.ExpectedResponsePayloadHex);
        ServiceDiagnosticTrace.Record($"{address.DefinitionId}/{address.EcuId}/{step.Role}", step.RequestHex,
            $"Parsed UDS: {response.Service:X2} {Convert.ToHexString(response.Payload)}; NRC={response.NegativeResponseCode:X2}",
            $"{response.Kind}: {validation.Detail}", address.TxCanId, address.RxCanId);
        return validation;
    }

    private OemOperationAvailabilityResult Publish(OemOperationAvailabilityResult result)
    {
        CurrentAvailability = result;
        return result;
    }

    private static double? ParseVoltage(string value)
    {
        var numeric = new string((value ?? string.Empty).Where(character =>
            char.IsDigit(character) || character is '.' or ',').ToArray()).Replace(',', '.');
        return double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static bool TryValue(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
}
