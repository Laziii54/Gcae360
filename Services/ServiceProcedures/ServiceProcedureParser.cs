using System.Text.Json;
using System.Text.Json.Serialization;
using CarScanner.Models;

namespace CarScanner.Services.ServiceProcedures;

public sealed record ServiceProcedureIssue(string Id, string Code, bool Excluded);

public sealed record ServiceProcedureParseResult(
    int SchemaVersion,
    int CatalogVersion,
    IReadOnlyList<ServiceProcedureDefinition> Procedures,
    IReadOnlyList<ServiceProcedureIssue> Issues);

/// <summary>
/// Reads service_procedures.json record by record: one broken record is excluded and reported, it never takes the
/// whole catalog (or the page) down. Old records without the newer optional fields load exactly as before.
/// </summary>
public static class ServiceProcedureParser
{
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public static ServiceProcedureParseResult Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var schema = root.TryGetProperty("schemaVersion", out var schemaElement) && schemaElement.TryGetInt32(out var parsedSchema)
            ? parsedSchema
            : SupportedSchemaVersion;
        if (schema != SupportedSchemaVersion)
            throw new InvalidOperationException($"Unsupported service procedure schema version: {schema}");

        var catalogVersion = root.TryGetProperty("catalogVersion", out var versionElement) && versionElement.TryGetInt32(out var parsedVersion)
            ? parsedVersion
            : 1;

        var issues = new List<ServiceProcedureIssue>();
        var records = new List<ServiceProcedureDefinition>();

        if (!root.TryGetProperty("procedures", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Service procedure catalog has no procedures list.");

        var index = 0;
        foreach (var element in list.EnumerateArray())
        {
            index++;
            try
            {
                var record = element.Deserialize<ServiceProcedureDefinition>(Options);
                if (record is null)
                {
                    issues.Add(new ServiceProcedureIssue($"#{index}", "Unreadable", true));
                    continue;
                }

                records.Add(record);
            }
            catch (JsonException)
            {
                var id = element.ValueKind == JsonValueKind.Object && element.TryGetProperty("id", out var idElement) &&
                         idElement.ValueKind == JsonValueKind.String
                    ? idElement.GetString() ?? $"#{index}"
                    : $"#{index}";
                issues.Add(new ServiceProcedureIssue(id, "Unreadable", true));
            }
        }

        var validation = ServiceProcedureValidator.Validate(records);
        issues.AddRange(validation.Issues);
        return new ServiceProcedureParseResult(schema, catalogVersion, validation.Valid, issues);
    }
}

public static class ServiceProcedureValidator
{
    public sealed record Result(IReadOnlyList<ServiceProcedureDefinition> Valid, IReadOnlyList<ServiceProcedureIssue> Issues);

    public static Result Validate(IEnumerable<ServiceProcedureDefinition> records)
    {
        var valid = new List<ServiceProcedureDefinition>();
        var issues = new List<ServiceProcedureIssue>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            var id = string.IsNullOrWhiteSpace(record.Id) ? "(no id)" : record.Id;
            var fatal = false;

            void Fail(string code)
            {
                issues.Add(new ServiceProcedureIssue(id, code, true));
                fatal = true;
            }

            if (string.IsNullOrWhiteSpace(record.Id))
                Fail("MissingId");
            else if (!seen.Add(record.Id))
                Fail("DuplicateId");

            if (string.IsNullOrWhiteSpace(record.Brand))
                Fail("MissingBrand");
            if (string.IsNullOrWhiteSpace(record.Model))
                Fail("MissingModel");
            if (string.IsNullOrWhiteSpace(record.NameEn) && string.IsNullOrWhiteSpace(record.NameKa))
                Fail("MissingName");

            if (record.MinimumYear < 0 || record.MaximumYear < 0 ||
                (record.MinimumYear > 0 && record.MaximumYear > 0 && record.MinimumYear > record.MaximumYear))
                Fail("InvalidYearRange");

            if (record.ExecutionType == ServiceProcedureExecutionType.OemDiagnostic)
            {
                if (string.IsNullOrWhiteSpace(record.OemOperationId))
                    Fail("OemWithoutOperationId");
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(record.OemOperationId))
                    Fail("ManualWithOperationId");
                if (record.Steps.Count == 0)
                    Fail("ManualWithoutSteps");
            }

            if (record.Steps.Any(step => string.IsNullOrWhiteSpace(step.TextEn) && string.IsNullOrWhiteSpace(step.TextKa)))
                Fail("EmptyStep");
            if (record.Steps.Select(step => step.Order).Distinct().Count() != record.Steps.Count || record.Steps.Any(step => step.Order <= 0))
                Fail("InvalidStepOrder");

            // Non-fatal: the record is shown, the problem is reported.
            if (!string.IsNullOrWhiteSpace(record.Kind) && !ServiceKindClassifier.TryParseKind(record.Kind, out _))
                issues.Add(new ServiceProcedureIssue(id, "UnknownKind", false));
            if (string.IsNullOrWhiteSpace(record.Source))
                issues.Add(new ServiceProcedureIssue(id, "MissingSource", false));
            if (!string.IsNullOrWhiteSpace(record.VerificationStatus) &&
                !Enum.TryParse<ServiceVerification>(record.VerificationStatus, true, out _))
                issues.Add(new ServiceProcedureIssue(id, "UnknownVerificationStatus", false));

            if (!fatal)
                valid.Add(record);
        }

        return new Result(valid, issues);
    }
}
