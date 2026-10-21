# OBD360 — Final Comprehensive Automotive Research and Engineering Project
Universal Service Intelligence, Oil-Life Configuration, and Original Mileage Verification

Project type: Final major research, implementation, data-quality, compatibility, and release-readiness improvement.

Primary systems:
1. Service procedures, maintenance functions, oil-life percentages, and reset compatibility.
2. Original Mileage reading, ECU/source validation, odometer evidence, and vehicle coverage.

Priority: Maximum practical coverage, evidence quality, correctness, regression safety, and production readiness.
This is the final major research and engineering run before the owner intends to proceed toward launching OBD360.
The owner is prepared to allow an extended working session. Use the available time and tools for a systematic, comprehensive investigation rather than a superficial pass.
Do not stop after reading a few files, searching a handful of websites, or creating a plan.
Inspect the entire relevant implementation, research existing catalog entries, investigate new opportunities, verify claims against original sources, implement safe and evidence-backed improvements, expand testing, build the application where possible, and prepare a complete final report.
Do not ask the owner routine questions. Make reasonable decisions from the actual repository and available evidence.
Do not claim that work continues in the background if no process is running. Work through the phases, preserve progress in durable files, and report verified results.
The goal is to leave OBD360 substantially more capable and trustworthy across its supported vehicles.
Do not confuse a large report, a large dataset, or a high number of catalog entries with genuine functionality.
The quality of the final result matters more than the number of lines written.

## 1. Mission and definition of success
The final objective is to make the existing Service and Original Mileage systems as complete, reliable, well-documented, and broadly compatible as the available evidence permits.
The project must deliver actual improvements, not only recommendations.
The investigation must cover every existing service-procedure entry and every currently implemented mileage provider or candidate in the repository.
After the complete audit, expand coverage to additional makes, models, model years, engines, and powertrains where adequate evidence supports it.
Do not limit the research arbitrarily to a small number of popular vehicles. Do not declare universal coverage when the actual implementation does not support every vehicle configuration.

Maintain a clear distinction between: Verified capability / Guided procedure / Corroborated but unverified capability / Research-only candidate / Quarantined or contradictory evidence / Unsupported operation / Functionality requiring physical vehicle validation.
A feature should only be described as supported to the extent demonstrated by the implementation and its supporting evidence.

The final work should improve: (1) manufacturer and vehicle coverage; (2) model-year and powertrain accuracy; (3) service-procedure discovery and compatibility matching; (4) 50% and 100% oil-life/service-operation behavior; (5) Original Mileage candidate discovery and source validation; (6) ECU/module response attribution; (7) evidence provenance and confidence; (8) error reporting and diagnostics; (9) automated test coverage; (10) service and mileage documentation; (11) release readiness.

The separate 0–100 km/h acceleration and leaderboard system is explicitly OUTSIDE this task. Do not investigate acceleration, leaderboard ranking, or unrelated features.

## 2. Non-negotiable safety and quality rules

### 2.1 Preserve the existing project
Before making changes: inspect current Git branch/HEAD/status/staged/unstaged/untracked; inspect diffs of every file you may touch; identify all pre-existing uncommitted work from other sessions; determine the actual status of previous adapter-compatibility/frame-parsing work; run relevant baseline tests and record existing known failures before changing code; identify existing release artifacts and record their hashes where practical.
The working tree has substantial uncommitted work from several concurrent sessions — preserve it carefully. Never run destructive reset/clean/checkout/restore commands. Never overwrite unrelated modifications. Do not replace large existing catalogs wholesale if a narrow, additive update suffices. **Do not commit, push, deploy, or publish anything.** Do not change package identity, signing configuration, or production infrastructure. Do not introduce broad architectural rewrites merely because they appear cleaner. If existing code conflicts with this spec, inspect actual behavior, preserve working functionality, and document the conflict before deciding.

### 2.2 Do not invent automotive functionality
Never invent or guess: OEM service-reset commands, ECU addresses, diagnostic identifiers, mileage DIDs, reset write sequences, response formats, scaling factors, model-year applicability, supported engine variants, manufacturer-specific service capabilities, or evidence that an odometer value is genuine.
Do not turn a community guess into a production-supported diagnostic operation. Do not generalize a procedure from one vehicle/year to an entire manufacturer. Do not treat an ECU's numeric response as authoritative mileage merely because it looks plausible. Never introduce an unsupported odometer-writing/programming/altering operation — Original Mileage is READ-ONLY verification only.

### 2.3 Preserve diagnostic safety
Manufacturer-specific write operations must be supported by appropriate technical documentation, exact vehicle applicability, established implementation safeguards, and reliable confirmation handling. Never send speculative diagnostic commands to a real vehicle. Do not execute research commands against live vehicles automatically. Use simulators for parser/control-flow tests; physical validation only through explicitly approved, supported operations. Do not bypass security access, immobilizer protections, or other vehicle security mechanisms.

### 2.4 Preserve honesty
A successful software build is not proof of vehicle compatibility. A passing simulator test is not proof of OEM support. A source that lists a feature is not necessarily proof of its exact diagnostic implementation. An undocumented function must not be described as verified simply because it appears to work on one vehicle. Where uncertainty remains, report it explicitly.

## 3. Phase A — Exhaustive repository audit
Before researching externally, understand the whole current implementation using the actual repository, not remembered file paths. Investigate where they still exist: `service_procedures.json`; Original Mileage page + ViewModels; Service page + ViewModels; OEM operation catalog and execution services; `ActualMileageService`; `CatalogOdometerProvider`; `OdometerResponseDecoder`; `obdb_year_evidence.json`; mileage evidence/confidence logic; generic PID A6 provider; distance-context logic; learned mileage evidence; ECU/module discovery; vehicle metadata and model-year matching; `HybridReadDiagnosis` and `MileageReadDiagnosis` where present; existing diagnostic trace/logging systems; localization/translation registration; relevant test simulators and regression tests.

**3.1 Service architecture** — trace the complete workflow from UI to diagnostic operation: vehicle identification; available make/model/year/engine fields; how Service selects applicable procedures; how a procedure is represented in the catalog; how automated/guided/read-only is decided; how the operation reaches the diagnostic service; command/response processing; failure classification; success confirmation; whether the UI can accidentally imply a guided procedure executed automatically; localization; how tests validate applicability/execution. Inspect the complete current catalog; establish the actual number of unique procedures, vehicles, year ranges, manufacturers, operation types — do not rely on outdated size estimates.

**3.2 Original Mileage architecture** — trace the complete scanning sequence: scan initiation; vehicle metadata collection; provider selection/ordering; requests issued; ECU responses; decoding; source attribution; model-year evidence application; competing-candidate comparison; confidence/validation; final result + diagnostics display. Identify every existing provider and its exact run conditions; which pieces are manufacturer-specific vs. standard OBD vs. learned; how foreign-module responses, malformed data, unsupported identifiers, conflicting values are handled; whether existing evidence could be reused correctly but is inaccessible due to restrictive matching/incomplete wiring. Do not broaden provider applicability before understanding its validation/confidence rules.

**3.3 Produce a current-state inventory** (machine-readable): existing service procedures; existing mileage candidates/providers; supported vehicle metadata; supported model-year ranges; available source evidence; existing tests; missing evidence; known contradictions; known compatibility limitations. This becomes the baseline against which all improvement is measured.

## 4. Phase B — Comprehensive service-procedure research (highest priority)
Expand verified Service coverage as broadly as practical across the entire manufacturer/model inventory. Do not stop after a few common models.

**4.1 Audit every existing service entry** in `service_procedures.json` and other active catalogs. For each: Procedure ID; Manufacturer; Model; Model-year range; Market/regional variant; Engine/powertrain; Relevant module; Operation category; Exact semantics; Required ignition/vehicle state; Prerequisites; Actual diagnostic implementation if any; Expected confirmation; Source evidence; Existing tests; Known limitations.
Check for: duplicates; ambiguous IDs; overly broad model-year ranges; wrong/missing model restrictions; incorrect powertrain assumptions; misleading labels; missing prerequisites/evidence; procedures incorrectly marked automated; unverified expected success; stale/contradictory source records.
Do not indiscriminately remove procedures with incomplete research — classify evidence and decide: remain enabled / guided-only / restricted to verified configs / quarantine. Preserve useful existing functionality whenever it can be represented honestly.

**4.2 Research manufacturers comprehensively.** First determine every manufacturer already represented; investigate all existing procedures/configs; then expand per evidence availability: Chevrolet/GMC/Buick/Cadillac; Ford/Lincoln; Toyota/Lexus; Honda/Acura; Nissan/Infiniti; Hyundai/Kia/Genesis; Mazda; BMW/MINI; Mercedes-Benz; VW/Audi/SEAT/Škoda; Subaru; Volvo; Jeep/RAM/Chrysler/Dodge; Mitsubishi; other makes already in project metadata. This is a research queue, not a claim every manufacturer supports all operations. Do not create entries for every make/model/year combination — implement only variants evidence supports. Distinguish gasoline/diesel/hybrid/PHEV/EV where data permits; do not assume identical service behavior across powertrains/markets for the same model.

**4.3 Source hierarchy (use web search/browser tools):**
- Tier 1 — OEM primary docs (service-info portals, workshop docs, owner's manuals, TSBs, factory tool docs). Follow access restrictions; never bypass paywalls.
- Tier 2 — Government/regulatory (NHTSA, manufacturer bulletin repositories). A recall is not evidence of an OBD reset command unless it says so explicitly.
- Tier 3 — Professional info providers (ALLDATA, Mitchell 1, Identifix, scan-tool function catalogs). Marketing claims ≠ proof of an exact command.
- Tier 4 — Professional diagnostic-tool documentation. Separate read / guided-manual / electronic-reset / coding-adaptation / unsupported. Don't infer a command from a feature listing.
- Tier 5 — Open-source technical databases (OBDb/OBDb Community, public vehicle-specific definitions, repos already used in OBD360). Evaluate provenance/licensing before importing.
- Tier 6 — Community/forum reports: research leads only unless corroborated.

**4.4 Research strategy** — for each important entry/candidate, search with several distinct queries (e.g. "make model year oil life reset owner manual", "...TSB...", "...professional scan tool...", exact catalog identifiers already in code). These are search patterns, not permission to invent identifiers. Open and verify the underlying source, don't rely on snippets. Record source URL, title, publisher, access date, exact claim, supported configs, limitations. Seek primary evidence + independent corroboration for high-impact functionality.

**4.5 Research and implement additional valid procedures**: oil-life reset; maintenance interval reset; service reminder reset; inspection interval reset; documented battery registration/initialization where in scope; other verified Service categories the architecture supports. Don't add unrelated repair operations just to grow the catalog. Each addition needs precise applicability + adequate evidence. Guided workflow only when manual instructions are reliably documented, and must never be represented as an ECU write that occurred.

## 5. Phase C — Make the 50% / 100% experience complete and honest
The owner wants 50% and 100% choices available across vehicle selections — the broadest technically-justified support, never a faked reset or forced unsupported write.

**5.1** Establish exactly what 50%/100% currently mean in the UI/code (oil-life target / dashboard reset / guided action / ECU reset / local reminder / read-only comparison / partial). Trace end-to-end: UI → ViewModel → catalog → execution → transport → response validation → final UI state. Don't confuse manually entering a dashboard value with writing it to the ECU.

**5.2** For every vehicle selection where Service is available, both choices must resolve to an explicit classified outcome: verified automated / verified guided / read-only info / unsupported arbitrary target / incompatible config / unverified-needs-more-evidence. No button that silently does nothing. Never hide an unsupported result behind an apparently-successful button; show a clear reason + a verified alternative if one exists.

**5.3** Don't assume every manufacturer allows an arbitrary 50% target — investigate reading-current-value / OEM-100%-reset / arbitrary-50% / inspection-interval-reset / maintenance-time-or-distance-reset / local-reminder-only, separately per exact vehicle config. A verified 100% reset is NOT proof 50% is supported. If no documented 50% method exists, don't invent a command — keep the UI choice visible with a clear "unsupported/unavailable for this configuration" state and a guided alternative only if genuinely supported. Never claim success when only instructions were shown.

**5.4 Execution safeguards** for every supported automated operation: exact config match; prerequisite verification; command/protocol support confirmation; cancellation/disconnect handling; "request sent" ≠ "operation confirmed"; read-back validation where safe; accurate unsupported-command reporting; never convert a timeout into fake success; preserve prior displayed state if confirmation fails.

**5.5** Add validation that detects: missing 50%/100% definitions; unsupported ops falsely labeled verified; entries missing prerequisites/evidence; ambiguous automatic-vs-guided; duplicates; missing localization; model-year mismatches; buttons with no clear outcome.

## 6. Phase D — Comprehensive Original Mileage research
Increase the number of vehicles for which OBD360 can reliably retrieve/validate actual odometer mileage — by establishing genuine support and rejecting ambiguous candidates more intelligently, not by accepting more numeric responses indiscriminately.

**6.1** Audit current implementations: `ActualMileageService`; `CatalogOdometerProvider`; `OdometerResponseDecoder`; generic PID A6; distance-context evidence; learned mileage evidence; multi-module scanning; OEM addressing/resolution; `obdb_year_evidence.json`; mileage evidence records; existing quarantined candidates; Stage 2A error reporting/trace; all supporting tests/fixtures. Establish the actual vehicle DB and provider inventory — what each provider can read, applicability decision logic, guarantees about decoded-value meaning. Find opportunities to use existing verified data better before adding new identifiers.

**6.2** Do not confuse: total odometer distance / distance-since-DTC-clear / distance-with-MIL-on / distance-since-service / trip distance / module-specific accumulated distance / battery-or-powertrain operating distance / a locally calculated estimate / an unverified numeric candidate. A plausible number ≠ proof of genuine odometer. For every candidate, record source ECU, exact measurement semantics, supported configs, decoding method, units, evidence classification.

**6.3** Build the full make/model/year/powertrain inventory from the actual app databases. For every config, investigate reliable evidence for real-odometer retrieval through the current architecture: make/model/year/market/engine/powertrain/source ECU/protocol/request category/DID (if adequately documented)/response layout/scaling/units/semantics/known year differences/validation vs. dashboard mileage. Research existing candidates before adding new ones; avoid duplicate IDs and overly-generic definitions that raise false positives.

**6.4** Discover additional documented sources via OEM docs, legitimately accessible professional service info, diagnostic-tool docs, OBDb/public repos, existing OBD360 source repos, credible captures with clear provenance. Respect license/provenance. Community-only evidence must be labeled as such and withheld from automatic production selection until evidence rules are satisfied. Don't assume a manufacturer-wide identifier works across the whole make. Don't infer a DID from an unrelated one. Don't extrapolate across model years without evidence.

**6.5** Build a complete candidate ledger: identifier; make/model/year applicability; powertrain/market restrictions; expected ECU; request category; exact identifier if documented; response format/units; semantic meaning; source refs; confidence; test coverage; known contradictions; quarantine status; physical-validation status; implementation status; next required action. Specifically re-inspect the current treatment of the BMW/Toyota `DFE7` candidate (previously quarantined, mislabeled meaning) — do not restore to production unless new evidence verifies the intended measurement and applicability.

**6.6** Improve distinguishing a correct odometer value from: foreign ECU response / stale reply / generic distance counter / unrelated module's stored value / unsupported-or-guessed DID / malformed value / contradictory cross-source mileage. Preserve response attribution. Don't blindly accept any responding ECU, but also don't blindly reject every engine-ECU value — if exact vehicle evidence establishes the engine ECU gives the correct odometer on a given config, it should be usable subject to existing evidence/validation rules. Goal: vehicle-specific, evidence-backed trust.

**6.7** Make the distinction clear between OEM-documented / independently-corroborated / generic-OBD-limited-semantics / awaiting-physical-validation / quarantined-ambiguous / unsupported-config. Don't claim full model-year coverage just because a related vehicle can be scanned. Improve the coverage system so UI/reports can accurately describe the current support level.

## 7. Phase E — Research source ledger and data provenance
Durable record of every important research claim, using the repo's existing documentation conventions (don't overwrite prior research reports; organize under an existing docs/research directory where appropriate).

**7.1 Source record fields**: unique source ID; full URL; title; publisher; access date; category/tier; supported make/model/year; engine/powertrain restrictions; exact claim supported; procedure/candidate ID; document section/page if verified; concise evidence summary; confidence classification; contradictory info; licensing/provenance notes; related catalog record; related tests; implementation status. Never invent page numbers/dates. Never store large copyrighted workshop documents in the repo — use attributed concise summaries, respecting licensing.

**7.2 Evidence classifications** (adapt to existing naming where it exists, preserve these semantic distinctions): `OEM_VERIFIED`, `CORROBORATED`, `GUIDED_VERIFIED`, `COMMUNITY_LEAD`, `PARTIAL_YEAR_SUPPORT`, `RESEARCH_ONLY`, `QUARANTINED`, `UNSUPPORTED`. Many sources never compensate for weak evidence; a strong source for one year never auto-authorizes a different year.

**7.3** Every enabled production capability must reference its supporting source evidence (via stable source IDs into the ledger where the catalog schema can't hold detail directly). Validate the links resolve. Document what each important source does and does NOT establish.

## 8. Phase F — Implement evidence-backed improvements
Don't wait for exhaustive worldwide research before adding valid coverage — but fully audit the existing shipped catalog before calling the work comprehensive.

**8.1 Service catalog expansion** — new procedures only with adequate evidence + exact applicability, via the existing catalog/execution architecture, with source refs, prerequisites, expected confirmation, model-year/powertrain restrictions, automated-vs-guided distinction, and regression tests, while preserving other vehicle definitions. Correct an existing mislabeled/overbroad entry only when evidence+tests justify it — don't rewrite the whole catalog for formatting.

**8.2 Service compatibility improvements**: reliable matching; unsupported years never silently inherit commands; missing engine metadata never triggers unsafe assumptions; duplicate/ambiguous matches reported; guided ops never presented as automated; both percentage choices always have a clear state; failed ops never reported as successful; the user always understands why something is/isn't available. Prefer the smallest shared-component change over ad hoc conditional branches.

**8.3 Original Mileage improvements** only where evidence justifies: additional verified vehicle-specific candidates; corrected model-year applicability; better source-ECU attribution/semantic classification/contradiction handling/response validation/error classification/use of existing evidence/compatibility reporting. Never change a candidate's measurement semantics just to pass a test. Never invent diagnostic requests. Never turn every plausible number into a supported odometer.

**8.4 Keep diagnostic transport stable** — reuse existing transport/session abstractions; no general BLE/Wi-Fi rewrite; don't touch adapter init/protocol selection/retries/addresses/timing unless a specific verified defect directly blocks an evidence-backed Service/Mileage feature AND has an isolated regression test. Keep research-driven data changes separate from transport fixes.

## 9. Phase G — Comprehensive testing and QA
**9.1 Service tests**: catalog entries load; duplicate IDs rejected; model-year boundaries respected; vehicle/powertrain matching correct; missing metadata handled safely; source refs resolve; unsupported procedures stay unavailable; guided stays guided-only; automated requires verified applicability; 50%/100% have explicit correct states; unsupported arbitrary % never fakes a write; success requires real completion evidence; failure preserves accurate status; new localization keys present in every supported language; existing behavior intact.

**9.2 Mileage tests**: correct source ECU + valid value; unexpected ECU responding; no response; explicit negative response; unsupported identifier; stale response; malformed payload; truncated multi-frame; invalid scaling/units; plausible-but-wrong distance; conflicting multi-module values; generic PID that doesn't establish total odometer; exact model-year evidence; unsupported model year; quarantined candidate; missing source docs; corroborated-not-OEM-verified; all providers + selection rules. Tests must exercise actual production code wherever practical; synthetic fixtures must be labeled synthetic, never counted as physical compatibility evidence.

**9.3 Cross-feature consistency**: oil-life % never confused with mileage; distance-since-service never confused with total odometer; guided resets never reported as executed ECU ops; mileage evidence never auto-authorizes unrelated Service ops; shared vehicle metadata never causes wrong feature-specific compatibility; existing adapter/protocol behavior unchanged.

**9.4 Final regression sequence**: all Service tests → all Mileage tests → relevant adapter/transport tests → catalog/schema validation tests → localization tests → full suite → full suite 3x consecutively where practical → Android Debug build → Release build if signing env supports it → new installable APK in a separate output dir if changes require a new artifact → signature/package-identity/version/size/SHA-256 verification → confirm prior verified Release artifacts unchanged. Record actual results, distinguish code failures from environment limits. Never delete/weaken pre-existing tests. Investigate any new failure before declaring done. **Preserve the known pre-existing failure** `ObdbYearEvidenceTests.TheMileagePage_SaysWhenARecordedReplyExistsForTheModelYear` unless separately authorized and proven safe to fix.

## 10. Phase H — Documentation and coverage matrices
Durable deliverables with actual outcomes (adapt names to repo convention; don't duplicate if an equivalent up-to-date doc exists — update narrowly instead):
`OBD360_FINAL_SERVICE_RESEARCH.md`, `OBD360_SERVICE_SOURCE_LEDGER.json`, `OBD360_SERVICE_COVERAGE_MATRIX.csv`, `OBD360_ORIGINAL_MILEAGE_RESEARCH.md`, `OBD360_MILEAGE_EVIDENCE_LEDGER.json`, `OBD360_MILEAGE_COVERAGE_MATRIX.csv`, `OBD360_UNVERIFIED_CAPABILITIES.md`, `OBD360_FINAL_RELEASE_READINESS_REPORT.md`.

**10.1 Service coverage matrix columns**: Make, Model, Model year/range, Engine/powertrain restrictions, Operation, 50% status, 100% status, Automated/guided/read-only classification, Evidence status, Source references, Test status, Known limitations. Cover every existing + newly added entry.

**10.2 Mileage coverage matrix columns**: Make, Model, Model year, Powertrain/market restrictions, Existing provider, Candidate ECU, Candidate measurement, Source evidence, Applicability confidence, Physical validation status, Test status, Current production eligibility, Outstanding limitations. Cover all configs represented by the current DB/provider catalog.

**10.3 Research coverage report** — actual counts (based strictly on the real repo/artifacts, never inflated): manufacturers studied; existing service entries audited; entries with adequate evidence; automated procedures; guided procedures; withheld/quarantined procedures; new entries added; vehicles/model-year ranges represented; mileage candidates audited; candidates with sufficient evidence; candidates awaiting physical validation; quarantined/unsupported candidates; tests added and passing; remaining unresolved research items.

## 11. Phase I — Final release-readiness review
Review Service + Original Mileage for: user-facing compatibility messages; unexpected empty states; missing localization; incorrect success notifications; timeout/disconnected-adapter handling; unsupported configs; catalog loading failures; malformed research data; logging that could over-expose vehicle-identifying info; inaccurate UI claims; missing links/diagnostic references; misleading data-quality issues.
Do not expand into unrelated app-wide refactoring or silently modify other diagnostic features. Fix a critical defect in-scope only with an established root cause and a regression test; otherwise document the risk and the minimum additional evidence needed. Never claim this proves universal compatibility or completes all vehicle-specific support.

## 12. Execution strategy
Phase 0 safety/state inspection → Phase 1 complete inventory → Phase 2 comprehensive research (don't stop after a handful of manufacturers) → Phase 3 evidence reconciliation (compare research to implementation, pick highest-value proven corrections) → Phase 4 implementation (only where applicability/behavior is adequately established) → Phase 5 testing (deterministic regression tests + catalog validation + full regression sequence) → Phase 6 release readiness (build/verify artifacts, preserve release config/package identity/prior artifacts) → Phase 7 reporting (ledgers, matrices, unverified-capability register, final report).
Keep progress in durable project files so the investigation survives a context reset. If network/source/session/context limits prevent exhaustive research, state precisely what was completed, what remains, and where the evidence lives. Never falsely claim exhaustive manufacturer/model/year verification. Don't spend the whole session on a proposal while leaving safe, verified improvements unimplemented. Never perform unverified writes to gain apparent coverage. Prioritize real capability + evidence quality + regression safety together.

## 13. Final acceptance criteria (all must hold)
1. Current Service catalog fully inventoried and audited. 2. Current Mileage providers/evidence rules fully inventoried and audited. 3. Major documentation gaps researched systematically. 4. External claims have traceable source references. 5. Existing+new service procedures have precise compatibility classifications. 6. 50%/100% choices explicitly accounted for across vehicle selections, honestly and accurately. 7. No unsupported procedure claims successful execution. 8. Mileage candidates evaluated by source/semantics/ECU attribution/model-year applicability/evidence strength. 9. New production capabilities added only where supported. 10. Existing functionality protected by regression tests. 11. Automated validation ran successfully apart from documented pre-existing/environmental issues. 12. Final research artifacts exist in the repo. 13. Final test results and build artifacts accurately reported. 14. Unverified/hardware-dependent cases clearly listed. 15. Working tree preserved — no commits/pushes/deploys/publication.
Never claim completion merely because the report is long. Never invent coverage numbers, source verification, test results, or physical vehicle compatibility. The final product must be a demonstrably better Service and Original Mileage implementation with traceable evidence and clear limitations.

## 14. Required final response
A comprehensive report containing: executive summary; current-state architecture findings; total service catalog audit results; research sources + source-ledger location; detailed 50%/100% support findings; new service capabilities actually implemented; Original Mileage candidate/source research; mileage coverage improvements actually implemented; unsupported/unverified/quarantined items; source licensing/provenance findings; test results including repeated full-suite runs; Android build + artifact verification; exact files changed and why; quantified before/after coverage; known limitations + required physical validation; final Git status + confirmation unrelated work remains intact; a clear distinction between verified source claims, simulator results, and hardware-confirmed behavior.

**Do not commit, push, deploy, or publish. Do not begin the separate acceleration/leaderboard investigation.** Complete the maximum amount of useful, evidence-backed work possible in this run and leave OBD360 in a well-documented, locally validated state ready for final owner review.
