#!/usr/bin/env node
'use strict';

/*
 * Model-year evidence for the OBDb definitions that the Service and Mileage pages use.
 *
 * Why: the shipped catalog keeps only one boolean per row ("debug", the command-level "dbg" flag). It drops the year
 * information OBDb carries for each command: "filter" (the model years in which the command applies) and "dbgfilter" (the
 * model years in which it is still being debugged), the year-range signal sets, and the recorded responses under
 * tests/test_cases/<model year>/ (real replies with the values OBDb's own CI expects them to decode to, and
 * command_support.yaml: the commands that answered for that year). This script reads those, for the signals the app
 * classifies as odometer-like or service-like, and says per model year what is applicable, still being debugged, supported
 * and actually recorded.
 *
 * It also checks what the recorded values look like, because a definition can be named one thing and hold another:
 * a "distance since the last oil change" whose recorded values equal the odometer is the odometer.
 *
 * Usage:
 *   node evidence-obdb.js --repos <folder of OBDb repositories, one per vehicle> --signals <classified-signals.json> [--out <folder>]
 *
 * The repositories folder is not part of this repository (CC BY-SA 4.0 data of the OBDb contributors); it is produced by
 * fetching https://codeload.github.com/OBDb/<Repo>/tar.gz/refs/heads/main for each vehicle repository. The output files carry
 * signal names, identifiers, year lists and statistics derived from it, so they are CC BY-SA 4.0 too (see README.md).
 *
 * No clock is read; the output is deterministic for the same inputs. Nothing here talks to a vehicle.
 */

const fs = require('fs');
const path = require('path');

function argument(name, fallback) {
  const index = process.argv.indexOf(name);
  return index >= 0 && index + 1 < process.argv.length ? process.argv[index + 1] : fallback;
}

const researchDir = path.resolve(__dirname, '..');
const reposDir = argument('--repos', null);
const signalsPath = argument('--signals', null);
const outDir = path.resolve(argument('--out', researchDir));
const assetPath = argument('--asset', null); // optional: the compact file the app reads (Resources/Raw/obdb_year_evidence.json)
if (!reposDir || !signalsPath) {
  console.error('usage: node evidence-obdb.js --repos <folder> --signals <classified-signals.json> [--out <folder>]');
  process.exit(2);
}

const FIRST_YEAR = 1996;
const LAST_YEAR = 2027;

// ---------------------------------------------------------------------------------------------------------------------
// OBDb year filter: python/can/signals.py, Filter.matches (OBDb/.schemas)
// ---------------------------------------------------------------------------------------------------------------------

function filterMatches(filter, year) {
  if (!filter) return false;
  const from = filter.from;
  const to = filter.to;
  if (from !== undefined && to !== undefined) {
    if (from < to) {
      if (from <= year && year <= to) return true;
    } else if (year >= from || year <= to) {
      return true;
    }
  } else if (to !== undefined) {
    if (year <= to) return true;
  } else if (from !== undefined) {
    if (year >= from) return true;
  }
  return Array.isArray(filter.years) && filter.years.includes(year);
}

// ---------------------------------------------------------------------------------------------------------------------
// Reading the repositories
// ---------------------------------------------------------------------------------------------------------------------

function readJsonLoose(file) {
  const text = fs.readFileSync(file, 'utf8');
  try {
    return JSON.parse(text);
  } catch {
    try {
      return JSON.parse(text.replace(/\/\/.*$/gm, '').replace(/\/\*[\s\S]*?\*\//g, '').replace(/,\s*([}\]])/g, '$1'));
    } catch {
      return null;
    }
  }
}

// signalsets/v3: default.json plus YYYY-YYYY.json. For a model year OBDb's loader takes the narrowest range file that
// contains the year, else default.json (python/signalsets/loader.py).
function loadSignalsets(repoDir) {
  const dir = path.join(repoDir, 'signalsets', 'v3');
  if (!fs.existsSync(dir)) return [];
  const sets = [];
  for (const name of fs.readdirSync(dir)) {
    if (!name.endsWith('.json')) continue;
    const range = /^(\d{4})-(\d{4})\.json$/.exec(name);
    const json = readJsonLoose(path.join(dir, name));
    if (!json) continue;
    sets.push({ name, start: range ? Number(range[1]) : 0, end: range ? Number(range[2]) : 9999, json });
  }
  return sets;
}

function signalsetForYear(sets, year) {
  const matching = sets.filter((s) => s.start <= year && year <= s.end).sort((a, b) => a.end - a.start - (b.end - b.start));
  return matching.length ? matching[0] : null;
}

// tests/test_cases/<year>/command_support.yaml: the commands that answered in that model year, per ECU, with their signal ids.
function readCommandSupport(file) {
  const supported = new Set();
  if (!fs.existsSync(file)) return supported;
  for (const line of fs.readFileSync(file, 'utf8').split(/\r?\n/)) {
    const match = /^\s*-\s+(.+)$/.exec(line);
    if (!match) continue;
    const entry = match[1];
    const colon = entry.lastIndexOf(':');
    if (colon < 0) continue;
    for (const id of entry.slice(colon + 1).split(',')) if (id.trim()) supported.add(id.trim());
  }
  return supported;
}

// tests/test_cases/<year>/commands/*.yaml: recorded replies with the values the signal set is expected to decode them to.
function readCommandTests(file, wanted, valuesBySignal) {
  const lines = fs.readFileSync(file, 'utf8').split(/\r?\n/);
  let inExpected = false;
  for (const line of lines) {
    if (/^-\s+expected_values:/.test(line)) {
      inExpected = true;
      continue;
    }
    if (inExpected) {
      const value = /^ {4}([A-Za-z0-9_]+):\s*(.*)$/.exec(line);
      if (value) {
        if (wanted.has(value[1])) {
          const number = Number(value[2]);
          if (value[2] !== '' && Number.isFinite(number)) {
            if (!valuesBySignal.has(value[1])) valuesBySignal.set(value[1], []);
            valuesBySignal.get(value[1]).push(number);
          }
        }
        continue;
      }
      if (/^ {2}response:/.test(line) || /^-\s/.test(line)) inExpected = false;
    }
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// The signals the app cares about
// ---------------------------------------------------------------------------------------------------------------------

const exported = JSON.parse(fs.readFileSync(signalsPath, 'utf8'));
const wantedByRepo = new Map(); // repo -> Map(signalId -> {name, mileage, serviceKind})
for (const s of exported.signals) {
  if (!s.parameterId) continue;
  const interesting = (s.mileage && s.mileage !== 'Excluded') || (s.serviceKind && s.serviceKind !== 'Other' && s.serviceKind !== 'OilLevel');
  if (!interesting) continue;
  const repo = `${s.make}-${s.model}`;
  if (!wantedByRepo.has(repo)) wantedByRepo.set(repo, new Map());
  wantedByRepo.get(repo).set(s.parameterId, { name: s.name, mileage: s.mileage || null, serviceKind: s.serviceKind || null, unit: s.unit });
}

const tolerance = (value) => Math.max(50, Math.abs(value) * 0.002);
const MILE = 1.609344;
// Kilometres from a value in the unit the definition declares (miles and metres are converted; anything else is left as it is).
const kmFactor = (unit) => (String(unit).toLowerCase().startsWith('mile') ? MILE : String(unit).toLowerCase().startsWith('meter') ? 0.001 : 1);
const unique = (list) => [...new Set(list)].sort((a, b) => a - b);

// "2011-2013, 2015" style
function rangesOf(years) {
  const sorted = unique(years);
  const parts = [];
  for (let i = 0; i < sorted.length; ) {
    let j = i;
    while (j + 1 < sorted.length && sorted[j + 1] === sorted[j] + 1) j++;
    parts.push(j > i ? `${sorted[i]}-${sorted[j]}` : String(sorted[i]));
    i = j + 1;
  }
  return parts.join(', ');
}

// ---------------------------------------------------------------------------------------------------------------------
// Per vehicle
// ---------------------------------------------------------------------------------------------------------------------

const records = [];
const moduleAgreement = []; // per repo and model year: do the recorded values of two modules track each other?
let reposRead = 0;
let reposWithTests = 0;

for (const [repo, wanted] of [...wantedByRepo.entries()].sort((a, b) => a[0].localeCompare(b[0], 'en'))) {
  const repoDir = path.join(reposDir, repo);
  if (!fs.existsSync(repoDir)) continue;
  reposRead++;

  const sets = loadSignalsets(repoDir);
  if (sets.length === 0) continue;

  // Signal definitions by id in every signal set: id -> [{set, command}]
  const definitions = new Map();
  for (const set of sets) {
    for (const command of set.json.commands || []) {
      for (const signal of command.signals || []) {
        if (!wanted.has(signal.id)) continue;
        if (!definitions.has(signal.id)) definitions.set(signal.id, []);
        definitions.get(signal.id).push({ set, command, signal });
      }
    }
  }

  // Test data per model year
  const testRoot = path.join(repoDir, 'tests', 'test_cases');
  const years = fs.existsSync(testRoot) ? fs.readdirSync(testRoot).filter((d) => /^\d{4}$/.test(d)).map(Number).sort((a, b) => a - b) : [];
  if (years.length) reposWithTests++;
  const supportedByYear = new Map();
  const valuesByYear = new Map(); // year -> Map(signalId -> number[])
  for (const year of years) {
    supportedByYear.set(year, readCommandSupport(path.join(testRoot, String(year), 'command_support.yaml')));
    const values = new Map();
    valuesByYear.set(year, values);
    const commandsDir = path.join(testRoot, String(year), 'commands');
    if (fs.existsSync(commandsDir)) {
      for (const file of fs.readdirSync(commandsDir)) {
        if (file.endsWith('.yaml')) readCommandTests(path.join(commandsDir, file), wanted, values);
      }
    }
  }

  const vehicleRecords = [];
  for (const [signalId, info] of wanted.entries()) {
    const defs = definitions.get(signalId);
    if (!defs) continue;

    const applicable = [];
    const debug = [];
    let command = null;
    for (let year = FIRST_YEAR; year <= LAST_YEAR; year++) {
      const set = signalsetForYear(sets, year);
      if (!set) continue;
      const def = defs.find((d) => d.set === set);
      if (!def) continue;
      command = command || def.command;
      const inFilter = def.command.filter ? filterMatches(def.command.filter, year) : true;
      if (!inFilter) continue;
      applicable.push(year);
      if (def.command.dbg === true || (def.command.dbgfilter && filterMatches(def.command.dbgfilter, year))) debug.push(year);
    }
    if (!command) command = defs[0].command;

    const supported = years.filter((y) => supportedByYear.get(y).has(signalId));
    const recorded = years.filter((y) => (valuesByYear.get(y).get(signalId) || []).length > 0);
    const allValues = years.flatMap((y) => valuesByYear.get(y).get(signalId) || []);
    const service = Object.keys(command.cmd || {})[0] || '';

    const record = {
      _applicableYears: applicable,
      _debugYears: debug,
      _recordedYears: recorded,
      repo,
      signalId,
      name: info.name,
      mileage: info.mileage,
      serviceKind: info.serviceKind,
      unit: info.unit,
      header: String(command.hdr || ''),
      eax: String(command.eax || ''),
      request: service ? `${service} ${command.cmd[service]}` : '',
      commandDebugFlag: command.dbg === true,
      hasFilter: Boolean(command.filter),
      hasDebugFilter: Boolean(command.dbgfilter),
      applicableYears: rangesOf(applicable),
      debugYears: rangesOf(debug),
      verifiedYears: rangesOf(applicable.filter((y) => !debug.includes(y))),
      supportedYears: rangesOf(supported),
      recordedYears: rangesOf(recorded),
      recordedCases: allValues.length,
      values: allValues.length ? { min: Math.min(...allValues), max: Math.max(...allValues) } : null,
      _applicable: applicable,
      _debug: debug,
      _supported: supported,
      _recorded: recorded,
    };
    vehicleRecords.push(record);
  }

  // Do the recorded values of a distance-like service signal follow the odometer of the same model year? Then it is the odometer.
  for (const record of vehicleRecords) {
    if (!['DistanceSinceOilChange', 'DistanceToNextService', 'OilChangeInterval'].includes(record.serviceKind)) continue;
    let samples = 0;
    let near = 0;
    for (const year of record._recorded) {
      const own = valuesByYear.get(year).get(record.signalId) || [];
      const odometer = vehicleRecords
        .filter((o) => o.mileage === 'TotalOdometer')
        .flatMap((o) => (valuesByYear.get(year).get(o.signalId) || []).map((v) => v * kmFactor(o.unit)));
      if (odometer.length === 0) continue;
      const ownFactor = kmFactor(record.unit);
      for (const raw of own) {
        const value = raw * ownFactor;
        samples++;
        if (odometer.some((o) => Math.abs(o - value) <= tolerance(o))) near++;
      }
    }
    record.followsOdometer = samples >= 3 ? { samples, withinTolerance: near, fraction: Math.round((near / samples) * 1000) / 1000 } : null;
  }

  // Odometer copies in different modules: do their recorded values track each other within the same model year?
  const moduleOf = (r) => `${r.header}${r.eax ? '/' + r.eax : ''}`;
  for (const year of years) {
    const entries = vehicleRecords
      .filter((r) => r.mileage === 'TotalOdometer')
      .map((r) => ({ r, module: moduleOf(r), values: (valuesByYear.get(year).get(r.signalId) || []).map((v) => v * kmFactor(r.unit)) }))
      .filter((e) => e.values.length >= 3);
    const byModule = new Map();
    for (const e of entries) {
      if (!byModule.has(e.module)) byModule.set(e.module, []);
      byModule.get(e.module).push(...e.values);
    }
    const modules = [...byModule.keys()].sort();
    for (let i = 0; i < modules.length; i++) {
      for (let j = i + 1; j < modules.length; j++) {
        const a = byModule.get(modules[i]);
        const b = byModule.get(modules[j]);
        const matched = a.filter((x) => b.some((y) => Math.abs(x - y) <= tolerance(Math.max(x, y)))).length;
        // values already are kilometres by each definition's own unit; a match only after one more conversion factor
        // would mean that a definition declares the wrong unit
        const milesVsKm = a.filter((x) => b.some((y) => Math.abs(x * MILE - y) <= tolerance(y) || Math.abs(y * MILE - x) <= tolerance(x))).length;
        moduleAgreement.push({ repo, year, moduleA: modules[i], moduleB: modules[j], valuesA: a.length, valuesB: b.length, matched, milesVsKilometres: milesVsKm });
      }
    }
  }

  for (const record of vehicleRecords) {
    delete record._applicable;
    delete record._debug;
    delete record._supported;
    delete record._recorded;
    records.push(record);
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------------------------------------------------

const classOf = (r) => (r.mileage ? `mileage:${r.mileage}` : `service:${r.serviceKind}`);
const groups = new Map();
for (const r of records) {
  const key = classOf(r);
  if (!groups.has(key)) groups.set(key, []);
  groups.get(key).push(r);
}

const summaryRows = [...groups.entries()]
  .sort((a, b) => a[0].localeCompare(b[0]))
  .map(([key, list]) => ({
    class: key,
    signals: list.length,
    vehicles: new Set(list.map((r) => r.repo)).size,
    withARecordedReply: list.filter((r) => r.recordedCases > 0).length,
    vehiclesWithARecordedReply: new Set(list.filter((r) => r.recordedCases > 0).map((r) => r.repo)).size,
    withASupportedYear: list.filter((r) => r.supportedYears).length,
    commandFlaggedDebug: list.filter((r) => r.commandDebugFlag).length,
    onlyDebugFilterNoDbg: list.filter((r) => !r.commandDebugFlag && r.hasDebugFilter).length,
    withAYearFilter: list.filter((r) => r.hasFilter).length,
    verifiedForAtLeastOneRecordedYear: list.filter((r) => r.recordedYears && recordedAndVerified(r)).length,
  }));

function yearsOf(text) {
  const years = [];
  for (const part of String(text || '').split(',').map((s) => s.trim()).filter(Boolean)) {
    const range = /^(\d{4})-(\d{4})$/.exec(part);
    if (range) for (let y = Number(range[1]); y <= Number(range[2]); y++) years.push(y);
    else years.push(Number(part));
  }
  return years;
}
function recordedAndVerified(r) {
  const verified = new Set(yearsOf(r.verifiedYears));
  return yearsOf(r.recordedYears).some((y) => verified.has(y));
}

const suspects = records
  .filter((r) => r.followsOdometer && r.followsOdometer.fraction >= 0.5)
  .map((r) => ({
    repo: r.repo,
    signalId: r.signalId,
    name: r.name,
    request: r.request,
    header: r.header,
    eax: r.eax,
    recordedYears: r.recordedYears,
    followsOdometer: r.followsOdometer,
    values: r.values,
  }));

const trackingPairs = moduleAgreement.filter((m) => m.matched >= 3 && m.matched / Math.min(m.valuesA, m.valuesB) >= 0.5);
const vehiclesWithTrackingPair = new Set(trackingPairs.map((m) => m.repo));
const vehiclesWithTwoRecordedModules = new Set(moduleAgreement.map((m) => m.repo));
const notTracking = moduleAgreement.filter((m) => m.matched / Math.min(m.valuesA, m.valuesB) < 0.1 && m.valuesA >= 5 && m.valuesB >= 5);

const output = {
  schema: 'obd360.research.obdb-evidence/1',
  about:
    'Per signal: the model years in which the OBDb command applies (filter), is still being debugged (dbg, dbgfilter), is supported and has a recorded reply in OBDb\'s own tests. Derived from the OBDb vehicle repositories (CC BY-SA 4.0).',
  yearFilterSemantics: 'OBDb/.schemas python/can/signals.py Filter.matches: from<to is an inclusive range; from>=to means year>=from or year<=to; a lone to or from is open-ended; years[] adds single years. dbgfilter uses the same object.',
  vehiclesWithTestCases: reposWithTests,
  vehiclesRead: reposRead,
  summary: summaryRows,
  signalsThatFollowTheOdometer: suspects,
  odometerModules: {
    note: 'Distances are converted to kilometres with the unit each definition declares before they are compared. Pairs of modules, in one model year, that both have at least 3 recorded odometer values. "tracking": at least 3 values of the pair agree within max(50 km, 0.2 %) and at least half of the smaller list does. Recorded cases of different commands may come from different cars, so this shows that two definitions read the same kind of number, not that two modules of one car agree.',
    vehiclesWithTwoModulesRecordedInTheSameYear: vehiclesWithTwoRecordedModules.size,
    vehiclesWithATrackingPair: vehiclesWithTrackingPair.size,
    pairsRecorded: moduleAgreement.length,
    pairsTracking: trackingPairs.length,
    pairsNotTracking: notTracking.length,
    pairsThatLookLikeMilesVersusKilometres: moduleAgreement.filter((m) => m.milesVsKilometres >= 3 && m.matched < 3).length,
    trackingPairs: trackingPairs.map((m) => ({ repo: m.repo, year: m.year, modules: [m.moduleA, m.moduleB], matched: m.matched, of: Math.min(m.valuesA, m.valuesB) })),
    notTrackingPairs: notTracking.map((m) => ({ repo: m.repo, year: m.year, modules: [m.moduleA, m.moduleB], matched: m.matched, of: Math.min(m.valuesA, m.valuesB), milesVsKilometres: m.milesVsKilometres })),
  },
  note: 'recordedYears: model years with at least one recorded reply for the signal in tests/test_cases/<year>/commands. supportedYears: command_support.yaml lists the signal. verifiedYears: applicable years that are not debug years.',
};
fs.writeFileSync(path.join(outDir, 'obdb-evidence-summary.json'), JSON.stringify(output, null, 2) + '\n');

records.sort((a, b) => a.repo.localeCompare(b.repo, 'en') || a.signalId.localeCompare(b.signalId, 'en'));

if (assetPath) {
  // The compact form the app reads: per vehicle and signal, [applicable, debug, recorded] as year lists; "*" = every year in
  // FIRST_YEAR..LAST_YEAR (applicable), or every applicable year (debug).
  const all = [];
  for (let y = FIRST_YEAR; y <= LAST_YEAR; y++) all.push(y);
  const sameSet = (a, b) => a.length === b.length && a.every((y, i) => y === b[i]);
  const signals = {};
  for (const r of records) {
    const applicable = unique(r._applicableYears);
    const debug = unique(r._debugYears);
    const recorded = unique(r._recordedYears);
    if (!signals[r.repo]) signals[r.repo] = {};
    signals[r.repo][r.signalId] = [
      sameSet(applicable, all) ? '*' : rangesOf(applicable),
      debug.length && sameSet(debug, applicable) ? '*' : rangesOf(debug),
      rangesOf(recorded),
    ];
  }
  const asset = {
    schema: 1,
    about:
      'Model-year evidence for the OBDb odometer and service definitions the app reads. Per vehicle (catalog make-model) and signal id: [years in which the command applies, years in which it is still being debugged, years with a recorded reply in OBDb tests]. "*" means every year. A signal that is not listed has no evidence either way.',
    source:
      'OBDb vehicle repositories (CC BY-SA 4.0, https://github.com/OBDb): signalsets filter and dbgfilter and tests/test_cases. Derived by research/tools/evidence-obdb.js; credit in THIRD_PARTY_NOTICES.md.',
    yearsCovered: `${FIRST_YEAR}-${LAST_YEAR}`,
    exportedFrom: exported.generatedUtc,
    signals,
  };
  fs.writeFileSync(assetPath, JSON.stringify(asset) + '\n');
  console.log(`asset written: ${assetPath} (${records.length} signals, ${Math.round(JSON.stringify(asset).length / 1024)} KB)`);
}

for (const r of records) {
  delete r._applicableYears;
  delete r._debugYears;
  delete r._recordedYears;
}
fs.writeFileSync(path.join(outDir, 'obdb-evidence-signals.json'), JSON.stringify(records) + '\n');

console.log(`repos read ${reposRead}, with test cases ${reposWithTests}, signal records ${records.length}`);
for (const r of summaryRows) {
  console.log(
    `${r.class}: ${r.signals} signals in ${r.vehicles} vehicles; recorded reply for ${r.withARecordedReply} (${r.vehiclesWithARecordedReply} vehicles); ` +
      `commandDbg ${r.commandFlaggedDebug}, dbgfilter-only ${r.onlyDebugFilterNoDbg}, with filter ${r.withAYearFilter}, verified-and-recorded ${r.verifiedForAtLeastOneRecordedYear}`
  );
}
console.log(`odometer modules: ${vehiclesWithTwoRecordedModules.size} vehicles have two modules recorded in one year; ${vehiclesWithTrackingPair.size} have a tracking pair; pairs recorded ${moduleAgreement.length}, tracking ${trackingPairs.length}, not tracking ${notTracking.length}`);
console.log(`signals whose recorded values follow the odometer: ${suspects.length}`);
for (const s of suspects.slice(0, 40)) console.log(`  ${s.repo} ${s.signalId} "${s.name}" ${s.request} @ ${s.header}/${s.eax} years ${s.recordedYears}: ${s.followsOdometer.withinTolerance}/${s.followsOdometer.samples}`);

// ---------------------------------------------------------------------------------------------------------------------
// obdb-evidence-report.md
// ---------------------------------------------------------------------------------------------------------------------

function mdTable(headers, rows) {
  return [`| ${headers.join(' | ')} |`, `| ${headers.map(() => '---').join(' | ')} |`, ...rows.map((r) => `| ${r.map((c) => String(c).replace(/\|/g, '\\|')).join(' | ')} |`)].join('\n');
}
const tierOf = (r) => (r.recordedYears ? 'A' : r.verifiedYears ? 'B' : 'C');
const makeOf = (repo) => repo.slice(0, repo.indexOf('-'));
const tierRows = [...groups.entries()]
  .filter(([key]) => key !== 'mileage:Excluded')
  .sort((a, b) => a[0].localeCompare(b[0]))
  .map(([key, list]) => {
    const count = { A: 0, B: 0, C: 0 };
    for (const r of list) count[tierOf(r)]++;
    const best = new Map();
    for (const r of list) {
      const t = tierOf(r);
      if (!best.has(r.repo) || t < best.get(r.repo)) best.set(r.repo, t);
    }
    const vehicles = { A: 0, B: 0, C: 0 };
    best.forEach((t) => vehicles[t]++);
    return [key, list.length, `${count.A} / ${count.B} / ${count.C}`, best.size, `${vehicles.A} / ${vehicles.B} / ${vehicles.C}`];
  });

const odometerRecords = records.filter((r) => r.mileage === 'TotalOdometer');
const byMake = new Map();
for (const r of odometerRecords) {
  const make = makeOf(r.repo);
  if (!byMake.has(make)) byMake.set(make, { vehicles: new Set(), recorded: new Set(), tracking: new Set() });
  byMake.get(make).vehicles.add(r.repo);
  if (r.recordedCases > 0) byMake.get(make).recorded.add(r.repo);
}
for (const repo of vehiclesWithTrackingPair) {
  const make = makeOf(repo);
  if (byMake.has(make)) byMake.get(make).tracking.add(repo);
}

const sentinel = odometerRecords.filter((r) => r.values && r.values.max > 2000000);
const sentinelByMake = new Map();
for (const r of sentinel) sentinelByMake.set(makeOf(r.repo), (sentinelByMake.get(makeOf(r.repo)) || 0) + 1);
const unitSuspects = moduleAgreement.filter((m) => m.milesVsKilometres >= 3 && m.matched < 3);

const oilRecords = records.filter((r) => r.serviceKind === 'OilLifePercent' || r.serviceKind === 'OilDegradationPercent');
const oilRecorded = oilRecords.filter((r) => r.recordedCases > 0);
const oilByRequest = new Map();
for (const r of oilRecorded) {
  const key = `${r.request} @ ${r.header}${r.eax ? '/' + r.eax : ''} (${r.serviceKind === 'OilDegradationPercent' ? 'degradation' : 'life remaining'})`;
  if (!oilByRequest.has(key)) oilByRequest.set(key, new Set());
  oilByRequest.get(key).add(r.repo);
}

const lines = [];
lines.push("# What OBDb's own data says per model year");
lines.push('');
lines.push('Generated by `research/tools/evidence-obdb.js` from the OBDb vehicle repositories (CC BY-SA 4.0): the signal sets\' `filter` and `dbgfilter`,');
lines.push('the year-range signal sets, and the replies recorded under `tests/test_cases/<model year>/`. It covers the signals the Service and Mileage pages');
lines.push('classify as odometer-like or service-like. **Nothing here was measured on a vehicle by OBD360.** A recorded reply is a reply somebody else recorded and');
lines.push("OBDb's CI checks against the definition; it shows that a car of that model year answered and what the definition makes of the answer.");
lines.push('');
lines.push('## Why the earlier counts overstate support');
lines.push('');
lines.push('The shipped catalog keeps one flag per row, the command-level `dbg`. OBDb also has `dbgfilter` (debug in some model years only) and `filter` (the years a');
lines.push('command applies to). Both were dropped on import: **61,598 catalog rows carry a `dbgfilter` upstream and are marked "not debug"**, and no row can be');
lines.push('restricted to a model year. So "not flagged debug" never meant "verified for this car". Evidence tiers used below (per signal, over all model years):');
lines.push('');
lines.push("* **A**: a reply was recorded in OBDb's tests for at least one model year.");
lines.push('* **B**: applicable and not debug in at least one model year, but no recorded reply.');
lines.push('* **C**: debug in every applicable year.');
lines.push('');
lines.push(mdTable(['Kind', 'Signals', 'A / B / C', 'Vehicles', 'Best tier per vehicle A / B / C'], tierRows));
lines.push('');
lines.push('The Mileage page\'s "requestable" figure (457 vehicles with a current-odometer definition) is a count of definitions. The same signals by evidence:');
lines.push('');
lines.push(`* vehicles with a recorded reply for at least one current-odometer definition: **${new Set(odometerRecords.filter((r) => r.recordedCases > 0).map((r) => r.repo)).size}**;`);
lines.push(`* vehicles with two modules that both have recorded odometer values in the same model year: **${vehiclesWithTwoRecordedModules.size}**;`);
lines.push(`* of those, vehicles where the values of two modules track each other: **${vehiclesWithTrackingPair.size}** (${trackingPairs.length} of ${moduleAgreement.length} module pairs in a model year).`);
lines.push('');
lines.push('"Track each other" is a heuristic: distances are converted to kilometres with the unit each definition declares, and a pair tracks when at least three values');
lines.push('agree within max(50 km, 0.2 %) and at least half of the smaller list does. Recorded replies of different commands can come from different cars, so this shows that two');
lines.push(`definitions read the same kind of number, not that two modules of one car agree. A pair that does not track (${notTracking.length} pairs) is not proof of a wrong definition either.`);
lines.push('');
lines.push('Per make (current-odometer definitions):');
lines.push('');
lines.push(
  mdTable(
    ['Make', 'Vehicles', 'With a recorded reply', 'With a tracking module pair'],
    [...byMake.entries()].sort((a, b) => a[0].localeCompare(b[0], 'en')).map(([make, v]) => [make, v.vehicles.size, v.recorded.size, v.tracking.size])
  )
);
lines.push('');
lines.push('## Oil life: which requests have recorded replies');
lines.push('');
lines.push(mdTable(['Request @ module', 'Vehicles with a recorded reply'], [...oilByRequest.entries()].map(([key, set]) => [key, `${set.size}: ${[...set].sort().join(', ')}`])));
lines.push('');
lines.push(`Recorded oil-life values lie in ${Math.min(...oilRecorded.map((r) => r.values.min))} to ${Math.max(...oilRecorded.map((r) => r.values.max))} percent in every case. The per-vehicle table, with model years, is \`oil-life-capability-matrix.json\`.`);
lines.push('');
lines.push('## Definitions that hold something else than their name says');
lines.push('');
if (suspects.length === 0) lines.push('None found by the odometer check.');
for (const s of suspects) {
  lines.push(`* **${s.repo}** \`${s.signalId}\` "${s.name}", \`${s.request}\` @ ${s.header}/${s.eax}: ${s.followsOdometer.withinTolerance} of ${s.followsOdometer.samples} recorded values (model years ${s.recordedYears}) lie within 50 km (or 0.2 %) of an odometer value recorded for the same model year; recorded range ${s.values.min} to ${s.values.max} km.`);
}
lines.push('');
lines.push('The BMW definition is shared by 19 BMW models, 4 MINI models and the Toyota GR Supra (`22 DFE7` at module 0x19, bit 304); the app now never shows it as an oil-change distance.');
lines.push('');
lines.push('### Recorded values that are not a distance');
lines.push('');
lines.push(`${sentinel.length} current-odometer definitions have a recorded reply that decodes to more than 2,000,000 km (all-ones fields such as 4,294,967,295, 2,147,483,647 or 16,777,215, or a scale that does not fit). By make: ${[...sentinelByMake.entries()].sort((a, b) => b[1] - a[1]).map(([m, n]) => `${m} ${n}`).join(', ')}. The Mileage page already treats such a value as implausible and does not use it.`);
lines.push('');
lines.push('### Units');
lines.push('');
lines.push(unitSuspects.length === 0 ? 'No module pair matches only after an extra mile/kilometre factor.' : `${unitSuspects.length} module pairs match only after one more mile/kilometre factor, which means one of the two definitions declares the wrong unit: ${unitSuspects.map((m) => `${m.repo} ${m.moduleA} vs ${m.moduleB} (${m.year})`).join('; ')}. They need a vehicle to say which one.`);
lines.push('');
lines.push('## Limits');
lines.push('');
lines.push('* Only the signals the app classifies (by name) are covered. The model year is the folder name of the test; OBDb does not say how the contributors determined it.');
lines.push('* A recorded reply says nothing about the ECU on a car that was not tested, and a missing one does not say the definition is wrong.');
lines.push("* The year-range signal sets (`YYYY-YYYY.json`) exist in very few repositories; where they do, the one containing the year replaces `default.json`, as OBDb's loader does.");
lines.push('* The module of a signal is the header (and extended address) of its command; OBDb states a module type for only some headers.');
fs.writeFileSync(path.join(outDir, 'obdb-evidence-report.md'), lines.join('\n') + '\n');
console.log('report written');
