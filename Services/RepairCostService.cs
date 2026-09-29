using CarScanner.AI;
using CarScanner.Models;

namespace CarScanner.Services;

public sealed class RepairCostService
{
    private readonly AiDiagnosticService _ai;

    public RepairCostService(AiDiagnosticService ai) => _ai = ai;

    public async Task<RepairCostResult> EstimateAsync(
        VehicleInfo vehicle,
        IReadOnlyList<ErrorCode> codes,
        CancellationToken token = default)
    {
        if (codes.Count == 0)
            return new(Array.Empty<RepairCostEstimate>(), false, string.Empty);

        try
        {
            var ai = await _ai.EstimateRepairCostsAsync(vehicle, codes, token);
            var byCode = ai.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);
            var complete = codes.Select(code =>
                byCode.TryGetValue(Normalize(code.Code), out var estimate)
                    ? estimate
                    : BuildFallback(code)).ToArray();
            return new(complete, true, "AI შეფასება · ავტომობილის მოდელი · ქართული ბაზარი");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new(
                codes.Select(BuildFallback).ToArray(),
                false,
                "საწყისი სავარაუდო დიაპაზონები · AI დროებით მიუწვდომელია");
        }
    }

    private static RepairCostEstimate BuildFallback(ErrorCode code)
    {
        var normalized = Normalize(code.Code);
        if (Known.TryGetValue(normalized, out var known))
            return known.WithError(code);

        var urgency = InferUrgency(code);
        var (partMin, partMax, laborMin, laborMax) = normalized.FirstOrDefault() switch
        {
            'B' => (80, 1200, 50, 350),
            'C' => (100, 1800, 80, 500),
            'U' => (50, 900, 60, 400),
            _ => (60, 1500, 50, 450)
        };
        return new RepairCostEstimate
        {
            Code = normalized,
            ProblemName = code.DisplayTitle,
            PartMinGel = partMin,
            PartMaxGel = partMax,
            LaborMinGel = laborMin,
            LaborMaxGel = laborMax,
            Urgency = urgency,
            Reason = "ზუსტი მიზეზი და შესაცვლელი ნაწილი დიაგნოსტიკით უნდა დადასტურდეს.",
            Source = "საწყისი სავარაუდო დიაპაზონი"
        };
    }

    private static RepairUrgency InferUrgency(ErrorCode code)
    {
        var text = $"{code.DisplayTitle} {code.DisplayDescription} {code.DisplaySymptoms}".ToLowerInvariant();
        if (new[] { "brake", "airbag", "steering", "overheat", "oil pressure", "მუხრუჭ", "აირბაგ", "საჭ", "გადახურ", "ზეთის წნევა" }.Any(text.Contains))
            return RepairUrgency.StopNow;
        return code.Severity switch
        {
            "სერიოზული" or "მაღალი" or "High" or "Critical" => RepairUrgency.RepairSoon,
            _ => RepairUrgency.CanWait
        };
    }

    private static string Normalize(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    private static readonly IReadOnlyDictionary<string, KnownEstimate> Known =
        new Dictionary<string, KnownEstimate>(StringComparer.OrdinalIgnoreCase)
        {
            ["P0420"] = new("კატალიზატორის ეფექტურობა", 300, 900, 50, 150, RepairUrgency.RepairSoon),
            ["P0430"] = new("კატალიზატორის ეფექტურობა (Bank 2)", 350, 1000, 60, 180, RepairUrgency.RepairSoon),
            ["P0300"] = new("ძრავის არათანაბარი წვა", 40, 800, 50, 250, RepairUrgency.RepairSoon),
            ["P0171"] = new("ღარიბი საწვავის ნარევი", 30, 500, 50, 220, RepairUrgency.RepairSoon),
            ["P0128"] = new("თერმოსტატი / დაბალი სამუშაო ტემპერატურა", 70, 300, 70, 180, RepairUrgency.CanWait),
            ["P0455"] = new("EVAP სისტემის დიდი გაჟონვა", 20, 450, 50, 180, RepairUrgency.CanWait),
            ["P0442"] = new("EVAP სისტემის მცირე გაჟონვა", 20, 350, 50, 180, RepairUrgency.CanWait),
            ["P0011"] = new("შემშვები კამშაფტის ფაზა", 80, 900, 80, 350, RepairUrgency.RepairSoon),
            ["P0014"] = new("გამშვები კამშაფტის ფაზა", 80, 900, 80, 350, RepairUrgency.RepairSoon)
        };

    private sealed record KnownEstimate(
        string Name,
        int PartMin,
        int PartMax,
        int LaborMin,
        int LaborMax,
        RepairUrgency Urgency)
    {
        public RepairCostEstimate WithError(ErrorCode code) => new()
        {
            Code = code.Code,
            ProblemName = Name,
            PartMinGel = PartMin,
            PartMaxGel = PartMax,
            LaborMinGel = LaborMin,
            LaborMaxGel = LaborMax,
            Urgency = Urgency,
            Reason = "ფასი დამოკიდებულია ზუსტ მიზეზზე, ნაწილის ბრენდსა და ავტომობილის მოდიფიკაციაზე.",
            Source = "ხშირი კოდის საწყისი დიაპაზონი"
        };
    }
}

public sealed record RepairCostResult(
    IReadOnlyList<RepairCostEstimate> Estimates,
    bool UsedAi,
    string SourceMessage);
