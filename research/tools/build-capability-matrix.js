#!/usr/bin/env node
'use strict';

/*
 * The oil-life capability matrix: for every vehicle that has an oil-life or oil-degradation definition in the shipped catalog,
 * and separately for each thing that can be asked of it:
 *
 *   - can the value be READ, and what stands behind that (OBDb's recorded replies per model year, not just a definition);
 *   - can it be RESET to 100% (by a guided procedure the app shows; by the app itself: never, no request is verified);
 *   - can a chosen percentage (50%) be SET (by the app: never; by the maker's own tool: only where a bulletin says so);
 *   - can only a SERVICE REMINDER be reset (the guided procedures of the other kinds that exist for the vehicle);
 *   - what the ADAPTER has to be able to do to read it.
 *
 * Inputs:
 *   --evidence  research/obdb-evidence-signals.json            (evidence-obdb.js)
 *   --guides    guide-matches.json                              (ServiceGuideMatchExportTests, see README.md)
 *   curated/oil-life-oem-facts.curated.json                     (what the makers' own documents say, with sources)
 * Outputs (--out, default the research folder): oil-life-capability-matrix.json and .csv, and vehicles-for-guides.json (the vehicle
 * list the guide export reads).
 *
 * Usage:
 *   node build-capability-matrix.js --list           writes vehicles-for-guides.json
 *   node build-capability-matrix.js --guides <guide-matches.json>
 *
 * Nothing here was measured on a vehicle. The output carries OBDb-derived data (CC BY-SA 4.0; see README.md).
 */

const fs = require('fs');
const path = require('path');

function argument(name, fallback) {
  const index = process.argv.indexOf(name);
  return index >= 0 && index + 1 < process.argv.length ? process.argv[index + 1] : fallback;
}

const researchDir = path.resolve(__dirname, '..');
const outDir = path.resolve(argument('--out', researchDir));
const evidencePath = argument('--evidence', path.join(researchDir, 'obdb-evidence-signals.json'));
const guidesPath = argument('--guides', null);
const facts = JSON.parse(fs.readFileSync(path.join(__dirname, 'curated', 'oil-life-oem-facts.curated.json'), 'utf8'));

const evidence = JSON.parse(fs.readFileSync(evidencePath, 'utf8'));
const oil = evidence.filter((r) => r.serviceKind === 'OilLifePercent' || r.serviceKind === 'OilDegradationPercent');

const yearsOf = (text) => {
  const years = [];
  for (const part of String(text || '').split(',').map((s) => s.trim()).filter(Boolean)) {
    const range = /^(\d{4})-(\d{4})$/.exec(part);
    if (range) for (let y = Number(range[1]); y <= Number(range[2]); y++) years.push(y);
    else years.push(Number(part));
  }
  return years;
};
const rangesOf = (years) => {
  const sorted = [...new Set(years)].sort((a, b) => a - b);
  const parts = [];
  for (let i = 0; i < sorted.length; ) {
    let j = i;
    while (j + 1 < sorted.length && sorted[j + 1] === sorted[j] + 1) j++;
    parts.push(j > i ? `${sorted[i]}-${sorted[j]}` : String(sorted[i]));
    i = j + 1;
  }
  return parts.join(', ');
};

// The repository name is "<make token>-<model>"; the catalog (and the app) split it at the first hyphen.
const split = (repo) => {
  const i = repo.indexOf('-');
  return { make: repo.slice(0, i), model: repo.slice(i + 1) };
};

const byRepo = new Map();
for (const r of oil) {
  if (!byRepo.has(r.repo)) byRepo.set(r.repo, []);
  byRepo.get(r.repo).push(r);
}

if (process.argv.includes('--list')) {
  const list = [...byRepo.keys()].sort().map((repo) => {
    const { make, model } = split(repo);
    const years = new Set();
    for (const r of byRepo.get(repo)) {
      for (const y of yearsOf(r.applicableYears)) if (y >= 2005 && y <= 2026) years.add(y);
      for (const y of yearsOf(r.recordedYears)) years.add(y);
    }
    return { make, model, years: [...years].sort((a, b) => a - b) };
  });
  fs.writeFileSync(path.join(outDir, 'vehicles-for-guides.json'), JSON.stringify(list));
  console.log(`${list.length} vehicles written to vehicles-for-guides.json`);
  process.exit(0);
}

if (!guidesPath) {
  console.error('missing --guides <guide-matches.json> (or --list); see research/README.md');
  process.exit(2);
}

const guideRows = JSON.parse(fs.readFileSync(guidesPath, 'utf8'));
const guidesByVehicle = new Map();
for (const entry of guideRows) {
  const key = `${entry.make}|${entry.model}`;
  if (!guidesByVehicle.has(key)) guidesByVehicle.set(key, []);
  for (const guide of entry.guides) guidesByVehicle.get(key).push({ ...guide, year: entry.year });
}

function addressing(header, eax) {
  const h = String(header).toUpperCase();
  if (eax) return 'extended addressing (the request goes to one module behind a gateway address; the adapter must support it)';
  if (/^7E[0-7]$/.test(h)) return '11-bit CAN, standard OBD address (request 0x' + h + ', reply 0x' + (parseInt(h, 16) + 8).toString(16).toUpperCase() + ')';
  if (/^DA[0-9A-F]{2}$/.test(h)) return '29-bit CAN, ISO-TP normal-fixed addressing (request 18DA' + h.slice(2) + 'F1): the adapter must handle 29-bit headers';
  if (h.length === 3) return '11-bit CAN, module address 0x' + h;
  return 'header ' + h;
}

const factFor = (make) => facts.brandFacts.find((f) => f.makes.some((m) => m.toLowerCase() === make.toLowerCase())) || null;

const rows = [];
for (const repo of [...byRepo.keys()].sort()) {
  const { make, model } = split(repo);
  const defs = byRepo.get(repo);
  const kinds = [...new Set(defs.map((d) => (d.serviceKind === 'OilDegradationPercent' ? 'degradation' : 'remaining')))];

  const reads = defs.map((d) => ({
    request: `${d.request} @ ${d.header}${d.eax ? '/' + d.eax : ''}`,
    kind: d.serviceKind === 'OilDegradationPercent' ? 'oil degradation (0 = new oil, grows)' : 'oil life remaining (100 = new oil, shrinks)',
    signalId: d.signalId,
    name: d.name,
    unit: d.unit,
    addressing: addressing(d.header, d.eax),
    applicableYears: d.applicableYears || 'all',
    verifiedYears: d.verifiedYears,
    debugYears: d.debugYears,
    recordedReplyYears: d.recordedYears,
    recordedReplies: d.recordedCases,
    recordedValueRange: d.values,
  }));

  const recorded = [...new Set(defs.flatMap((d) => yearsOf(d.recordedYears)))];
  const verified = [...new Set(defs.flatMap((d) => yearsOf(d.verifiedYears)))];
  const applicable = [...new Set(defs.flatMap((d) => yearsOf(d.applicableYears)))];
  const readStatus = recorded.length
    ? 'a reply was recorded in OBDb tests for model year(s) ' + rangesOf(recorded)
    : verified.length
      ? 'defined, not flagged debug for ' + rangesOf(verified.filter((y) => y >= 2005 && y <= 2026)) + ', but no recorded reply'
      : 'defined, still flagged debug in every applicable year, no recorded reply';

  const guideRowsOfVehicle = guidesByVehicle.get(`${make}|${model}`) || [];
  const guideMap = new Map();
  for (const g of guideRowsOfVehicle) {
    if (!guideMap.has(g.id)) guideMap.set(g.id, { id: g.id, name: g.name, kind: g.kind, status: g.status, method: g.method, effect: g.effect, verification: g.verification, brandWide: g.brandWide, source: g.source, years: [] });
    guideMap.get(g.id).years.push(g.year);
  }
  const guides = [...guideMap.values()].map((g) => ({ ...g, years: rangesOf(g.years) }));
  const oilGuides = guides.filter((g) => g.kind === 'OilService');
  const reminderGuides = guides.filter((g) => g.kind !== 'OilService' && ['MaintenanceReset', 'Inspection'].includes(g.kind));

  const brand = factFor(make);
  const gmSet = (facts.chosenPercentByMakerTool || []).filter((e) => e.make.toLowerCase() === make.toLowerCase() && e.models.some((m) => model.toLowerCase().includes(m.toLowerCase().replace(/ /g, '-')) || model.toLowerCase() === m.toLowerCase().replace(/ /g, '-')));

  rows.push({
    make,
    model,
    kindOfValue: kinds.join(' + '),
    read: {
      status: readStatus,
      modelYearsWithARecordedReply: rangesOf(recorded) || null,
      applicableYearsOfTheDefinitions: rangesOf(applicable.filter((y) => y >= 2005 && y <= 2026)) || null,
      definitions: reads,
    },
    resetTo100: {
      byTheApp: 'not available: no request is verified for any vehicle, so the app sends nothing',
      guidedProcedures: oilGuides.length ? oilGuides : 'no oil-service guide in the catalog for this model',
      makersDocumentedFunction: brand ? brand.resetFunction : null,
    },
    setChosenPercent: {
      byTheApp: 'not available: needs a reviewed entry that states the module accepts a chosen value (AllowsPercentTargets); none exists',
      makersOwnTool: gmSet.length ? gmSet.map((e) => ({ statement: e.statement, years: e.years, source: e.source })) : 'no source found for this model',
    },
    serviceReminderOnly: {
      guidedProcedures: reminderGuides.length ? reminderGuides : 'none beyond the oil-service guide',
    },
    adapter: {
      forReading: reads.map((r) => r.addressing).filter((v, i, a) => a.indexOf(v) === i),
      forResetting: 'nothing to execute: the app has no verified reset request for this vehicle, so no adapter can run one from the app',
    },
    executingModuleAccordingToTheMaker: brand ? brand.executingModule : null,
  });
}

fs.writeFileSync(path.join(outDir, 'oil-life-capability-matrix.json'), JSON.stringify({
  schema: 'obd360.research.oil-life-capability-matrix/1',
  about: 'Per vehicle with an oil-life or oil-degradation definition: read, reset to 100%, set a chosen percentage, reminder-only reset and adapter requirements, kept separate. Nothing here was measured on a vehicle by this research; "recorded reply" means a reply recorded in OBDb\'s own tests for that model year.',
  vehicles: rows.length,
  counts: {
    withARecordedReply: rows.filter((r) => r.read.modelYearsWithARecordedReply).length,
    degradationOnly: rows.filter((r) => r.kindOfValue === 'degradation').length,
    withAGuidedOilServiceReset: rows.filter((r) => Array.isArray(r.resetTo100.guidedProcedures)).length,
    withAnyAutomaticReset: 0,
    withAnyAutomaticChosenPercent: 0,
    withAMakerDocumentedChosenPercentTool: rows.filter((r) => Array.isArray(r.setChosenPercent.makersOwnTool)).length,
  },
  rows,
}, null, 2) + '\n');

const csv = [['make', 'model', 'value', 'readStatus', 'recordedReplyYears', 'requests', 'guidedOilServiceReset', 'automaticReset', 'chosenPercentByTheApp', 'chosenPercentByMakersTool', 'reminderOnlyGuides', 'executingModuleAccordingToTheMaker']];
for (const r of rows) {
  csv.push([
    r.make, r.model, r.kindOfValue, r.read.status, r.read.modelYearsWithARecordedReply || '',
    r.read.definitions.map((d) => d.request).join(' ; '),
    Array.isArray(r.resetTo100.guidedProcedures) ? r.resetTo100.guidedProcedures.map((g) => g.id).join(' ; ') : 'no guide',
    'no', 'no',
    Array.isArray(r.setChosenPercent.makersOwnTool) ? 'documented by the maker (see JSON)' : 'no source',
    Array.isArray(r.serviceReminderOnly.guidedProcedures) ? r.serviceReminderOnly.guidedProcedures.map((g) => g.id).join(' ; ') : 'none',
    r.executingModuleAccordingToTheMaker || '',
  ]);
}
fs.writeFileSync(path.join(outDir, 'oil-life-capability-matrix.csv'), csv.map((line) => line.map((c) => (/[",\n]/.test(String(c)) ? `"${String(c).replace(/"/g, '""')}"` : String(c))).join(',')).join('\n') + '\n');

console.log(`${rows.length} vehicles; ${rows.filter((r) => r.read.modelYearsWithARecordedReply).length} with a recorded reply; ${rows.filter((r) => Array.isArray(r.resetTo100.guidedProcedures)).length} with a guided oil-service reset`);
