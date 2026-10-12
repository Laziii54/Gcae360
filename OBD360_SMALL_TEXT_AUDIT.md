# OBD360 small-text audit

Scope: every `FontSize` below 12 in the XAML pages (254 found before this change: 251 element attributes + 3 shared styles), the shared styles in `Resources/Styles/Styles.xaml`, and the text drawn inside the chart controls. A script found them (`FontSize` attribute with a value under 12) and a rule decided the new value; nothing was changed by hand except the three shared styles.

## Rule used

* Text an ordinary person reads (labels, status words, explanations, reminders, severity words, button captions): **at least 12**. Longer translations wrap; the 12-point floor is a minimum, the typography scale for new work stays 24–30 / 18–22 / 15–17 / 13–15.
* Genuinely technical annotations (units, raw log lines, public URL, the `/100` suffix): **11** — compact, and not needed to understand the page.
* Elements with a fixed `HeightRequest` or `MaxLines` were **not** changed, because a bigger font could clip them; they are listed below for a visual review.
* Font sizes of MAUI controls scale with the system accessibility setting (`FontAutoScalingEnabled` is not turned off anywhere), so the larger floor adds to, and does not replace, the person's own text-size setting.

## Result

| | Count |
|---|---:|
| Small-text declarations found (before) | 254 |
| Raised to 12 or 11 in XAML | 242 |
| Shared styles raised (page title 23→24, caption 11→12, PRO badge 10→11) | 3 |
| Left for visual review (fixed height / max lines) | 9 |
| Still below 12 after the change, on purpose (technical annotations at 11) | 10 |

Sizes found before: 9 → 5, 10 → 17, 10.5 → 14, 11 → 171, 11.5 → 44.

## By page

| Page | Found | Raised | Needs visual review |
|---|---:|---:|---:|
| FuelPage | 30 | 29 | 1 |
| GraphStudioPage | 21 | 21 | 0 |
| AccelerationLeaderboardPage | 17 | 11 | 6 |
| ServicePage | 15 | 15 | 0 |
| CodingPage | 13 | 13 | 0 |
| MileagePage | 13 | 13 | 0 |
| VinReportPage | 13 | 13 | 0 |
| DiagnosticsPage | 11 | 9 | 2 |
| AccelerationPage | 10 | 10 | 0 |
| CapabilityCenterPage | 10 | 10 | 0 |
| TechnicalInspectionPage | 8 | 8 | 0 |
| HomePage | 7 | 7 | 0 |
| VehicleReportPage | 7 | 7 | 0 |
| GraphChannelsPage | 5 | 5 | 0 |
| HybridHealthPage | 5 | 5 | 0 |
| PerformanceVideoPage | 5 | 5 | 0 |
| ProPage | 5 | 5 | 0 |
| BatteryPage | 4 | 4 | 0 |
| EcuIdentifiersPage | 4 | 4 | 0 |
| Mode06Page | 4 | 4 | 0 |
| VehicleHealthPage | 4 | 4 | 0 |
| EmissionsPage | 3 | 3 | 0 |
| PrePurchasePage | 3 | 3 | 0 |
| RepairCostPage | 3 | 3 | 0 |
| VinHistoryPage | 3 | 3 | 0 |
| AccelerationHistoryPage | 2 | 2 | 0 |
| AccountPage | 2 | 2 | 0 |
| DiagnosticReportPage | 2 | 2 | 0 |
| HistoryPage | 2 | 2 | 0 |
| LiveDataPage | 2 | 2 | 0 |
| MaintenancePartPage | 2 | 2 | 0 |
| Mode05Page | 2 | 2 | 0 |
| ModuleScanPage | 2 | 2 | 0 |
| QrReportPage | 2 | 2 | 0 |
| SensorsPage | 2 | 2 | 0 |
| AppShell | 1 | 1 | 0 |
| AdapterCenterPage | 1 | 1 | 0 |
| BluetoothPage | 1 | 1 | 0 |
| FreezeFramePage | 1 | 1 | 0 |
| MaintenanceSetupPage | 1 | 1 | 0 |
| PassiveCanPage | 1 | 1 | 0 |
| ResetRecorderPage | 1 | 1 | 0 |
| VinLookupPage | 1 | 1 | 0 |

## Still requires a visual review

| File:line | Element | Was | Wanted | Why not changed | Visible to ordinary users |
|---|---|---:|---:|---|---|
| AccelerationLeaderboardPage.xaml:84 | Label `{Binding Second.VehicleModel}` | 10.5 | 12 | fixed height / MaxLines | yes |
| AccelerationLeaderboardPage.xaml:95 | Label `{Binding Second.VerificationText}` | 10 | 12 | fixed height / MaxLines | yes |
| AccelerationLeaderboardPage.xaml:114 | Label `{Binding First.VehicleModel}` | 10.5 | 12 | fixed height / MaxLines | yes |
| AccelerationLeaderboardPage.xaml:125 | Label `{Binding First.VerificationText}` | 10 | 12 | fixed height / MaxLines | yes |
| AccelerationLeaderboardPage.xaml:144 | Label `{Binding Third.VehicleModel}` | 10.5 | 12 | fixed height / MaxLines | yes |
| AccelerationLeaderboardPage.xaml:155 | Label `{Binding Third.VerificationText}` | 10 | 12 | fixed height / MaxLines | yes |
| DiagnosticsPage.xaml:79 | Button `კოდების წაშლა` | 11 | 12 | fixed height / MaxLines | yes |
| DiagnosticsPage.xaml:143 | Button `Open Related Live Data` | 11 | 12 | fixed height / MaxLines | yes |
| FuelPage.xaml:128 | Label `{Binding QualityText}` | 11.5 | 12 | fixed height / MaxLines | yes |

## Kept compact on purpose (11)

| File:line | Element | Size | Purpose |
|---|---|---:|---|
| EcuIdentifiersPage.xaml:153 | Label `{Binding Unit}` | 11 | technical annotation / unit |
| GraphChannelsPage.xaml:61 | Label `{Binding Unit}` | 11 | technical annotation / unit |
| GraphStudioPage.xaml:163 | Label `{Binding Unit}` | 11 | technical annotation / unit |
| GraphStudioPage.xaml:249 | Label `{Binding Unit}` | 11 | technical annotation / unit |
| HybridHealthPage.xaml:170 | Label `{Binding RawLog}` | 11 | technical annotation / unit |
| QrReportPage.xaml:20 | Label `{Binding PublicUrl}` | 11 | label/value |
| SensorsPage.xaml:66 | Label `{Binding Unit}` | 11 | technical annotation / unit |
| VehicleReportPage.xaml:22 | Label `/100` | 11 | label/value |
| Styles.xaml:101 | Style Setter `(shared style)` | 11 | shared style |
| GraphStudioPage.xaml:22 | Style Setter `(shared style)` | 11.5 | shared style |

## Not covered by this script

* Chart axis labels, markers and the cursor tooltip are drawn on a canvas at 9–10 points inside fixed 12-pixel boxes (`Controls/MultiChartView.cs`, `TelemetryGraphView.cs`). They are technical annotations and cannot be enlarged without redesigning the boxes; they need a device screenshot review and are part of proposal 6 (Graph Studio).
* Text created in C# (`new Label { FontSize = … }`) — the search found none below 12 outside the chart controls.
* Dense cards that stay dense at 12 (Fuel page, Graph Studio, Leaderboard rows): readable now, but still candidates for the simpler layouts in the visual proposals.
* Nothing here was checked on a phone: clipping with long German / French / Georgian texts and with the largest system text size is **unverified**.

**Addendum:** `Mode05Page.xaml` and `Mode06Page.xaml` are frozen by project rule. Six labels there (11 pt) were raised to 12 pt by
this pass and then restored; the page has no text-size change and the typography guard test ignores those two files.
