#!/usr/bin/env node
'use strict';

/*
 * Builds the coverage files of the research folder from three inputs:
 *
 *   1. classified-signals.json  written by the test CatalogResearchExportTests (see ../README.md). Every per-vehicle signal of the
 *      shipped catalog that the Service page or the Mileage page treats as service- or distance-related, labelled by the app's
 *      own classifiers. This script contains no second copy of their rules.
 *   2. The shipped catalog Resources/Raw/DtcCatalog.sqlite.br (read with node:sqlite, Node 22.5 or newer): provenance rows and
 *      the CAN odometer signals imported from opendbc.
 *   3. Resources/Raw/service_procedures.json and oem_operations.json: what the app already ships for guided resets and for
 *      diagnostic write operations.
 *
 * Optional: --obdb-sets <folder> with one <Make-Model>.json per vehicle (OBDb signalsets/v3/default.json, see fetch-obdb-sets.js)
 * adds the module types (cluster, engine, ...) that OBDb states for each header.
 *
 * Usage:  node build-research-files.js --signals <classified-signals.json> [--catalog <path>] [--obdb-sets <folder>] [--out <folder>]
 *
 * Output is deterministic for the same inputs (no clock is read).
 * The numbers describe the public definitions in the catalog. Nothing here was measured on a vehicle.
 */

const fs = require('fs');
const os = require('os');
const path = require('path');
const zlib = require('zlib');

function argument(name, fallback) {
  const index = process.argv.indexOf(name);
  return index >= 0 && index + 1 < process.argv.length ? process.argv[index + 1] : fallback;
}

const researchDir = path.resolve(__dirname, '..');
const repoRoot = path.resolve(researchDir, '..');
const signalsPath = argument('--signals', null);
const catalogPath = argument('--catalog', path.join(repoRoot, 'Resources', 'Raw', 'DtcCatalog.sqlite.br'));
const setsDir = argument('--obdb-sets', null);
const outDir = path.resolve(argument('--out', researchDir));
const curatedDir = path.join(__dirname, 'curated');

if (!signalsPath) {
  console.error('missing --signals <classified-signals.json>; see research/README.md');
  process.exit(2);
}

const readJson = (file) => JSON.parse(fs.readFileSync(file, 'utf8'));
const write = (name, text) => fs.writeFileSync(path.join(outDir, name), text.endsWith('\n') ? text : text + '\n', 'utf8');
const writeJson = (name, value) => write(name, JSON.stringify(value, null, 2));
const sortedEntries = (map) => [...map.entries()].sort((a, b) => b[1] - a[1] || String(a[0]).localeCompare(String(b[0])));
const count = (list, key) => {
  const map = new Map();
  for (const item of list) map.set(key(item), (map.get(key(item)) || 0) + 1);
  return map;
};
const pct = (part, whole) => (whole === 0 ? '0%' : `${Math.round((part / whole) * 1000) / 10}%`);

// ---------------------------------------------------------------------------------------------------------------------
// Inputs
// ---------------------------------------------------------------------------------------------------------------------

const exported = readJson(signalsPath);
const curatedService = readJson(path.join(curatedDir, 'service-operations.curated.json'));
const curatedMileage = readJson(path.join(curatedDir, 'mileage-sources.curated.json'));
const procedures = readJson(path.join(repoRoot, 'Resources', 'Raw', 'service_procedures.json'));
const oemOperations = readJson(path.join(repoRoot, 'Resources', 'Raw', 'oem_operations.json'));

// Same distance units and size limits as CatalogOdometerProvider.LoadSignalsAsync (a value outside them is never requested).
const DISTANCE_UNITS = new Set(['kilometers', 'kilometer', 'km', 'miles', 'mile', 'meters', 'meter']);
const DISTANCE_CLASSES = new Set(['TotalOdometer', 'OdometerAtEvent', 'ModuleInstallRecord', 'ComponentDistance']);

const signals = exported.signals.map((s) => {
  const unit = String(s.unit || '').toLowerCase();
  const eax = String(s.eax || '').toUpperCase();
  const module = `${s.tx || String(s.header).toUpperCase()}${eax ? '/' + eax : ''}`;
  return {
    ...s,
    unit,
    vehicle: `${s.make}|${s.model}`,
    module,
    request: `${s.service} ${String(s.identifier).toUpperCase()} @ ${module}`,
    production: !s.debug,
    // What the Mileage page can request: an addressable module, a distance unit, a field of 16..32 bits.
    distanceUsable:
      DISTANCE_CLASSES.has(s.mileage) &&
      s.addressable === true &&
      DISTANCE_UNITS.has(unit) &&
      s.bitLength >= 16 &&
      s.bitLength <= 32 &&
      s.bitOffset >= 0,
  };
});

const vehicles = new Map();
for (const v of exported.vehicles) {
  vehicles.set(`${v.make}|${v.model}`, {
    make: v.make,
    model: v.model,
    catalogRows: v.rows,
    debugRows: v.debugRows,
    sig: [],
  });
}
for (const s of signals) {
  if (!vehicles.has(s.vehicle)) vehicles.set(s.vehicle, { make: s.make, model: s.model, catalogRows: 0, debugRows: 0, sig: [] });
  vehicles.get(s.vehicle).sig.push(s);
}
const vehicleList = [...vehicles.values()].sort((a, b) => a.make.localeCompare(b.make, 'en') || a.model.localeCompare(b.model, 'en'));

// Catalog provenance and the CAN signals of opendbc, from the shipped catalog itself.
function openCatalog() {
  let sqlite;
  try {
    sqlite = require('node:sqlite');
  } catch {
    return null;
  }
  const raw = fs.readFileSync(catalogPath);
  const bytes = catalogPath.endsWith('.br') ? zlib.brotliDecompressSync(raw) : raw;
  const temp = path.join(os.tmpdir(), `obd360-research-${process.pid}.sqlite`);
  fs.writeFileSync(temp, bytes);
  const db = new sqlite.DatabaseSync(temp, { readOnly: true });
  return { db, temp };
}

const catalog = openCatalog();
const catalogInfo = { sources: exported.sources, metaRevision: null, canSignalCount: null };
let canRows = [];
if (catalog) {
  const meta = Object.fromEntries(catalog.db.prepare('SELECT key, value FROM meta').all().map((r) => [r.key, r.value]));
  catalogInfo.metaRevision = meta.obdb_pid_revision || null;
  catalogInfo.catalogGeneratedUtc = meta.generated_utc || null;
  catalogInfo.canSignalCount = catalog.db.prepare('SELECT COUNT(*) AS c FROM can_signals').get().c;
  canRows = catalog.db
    .prepare(
      `SELECT s.dbc_file AS dbc, s.message_id AS id, m.message_name AS message, s.signal_name AS signal, s.start_bit AS startBit,
              s.bit_length AS bitLength, s.byte_order AS byteOrder, s.factor AS factor, s.offset AS offset, s.unit AS unit
       FROM can_signals s
       LEFT JOIN can_messages m ON m.dbc_file = s.dbc_file AND m.message_id = s.message_id AND m.source_catalog_id = s.source_catalog_id
       ORDER BY s.dbc_file, s.message_id, s.start_bit`
    )
    .all()
    .filter((r) => /odometer|odomtr|kilometerstand|km_stand|odocount/i.test(r.signal));
  catalog.db.close();
  try {
    fs.unlinkSync(catalog.temp);
  } catch {
    /* a temp file left behind is harmless */
  }
}

// Module types stated by OBDb for each header (optional input).
const ecuTypes = new Map();
let ecuSetsRead = 0;
if (setsDir && fs.existsSync(setsDir)) {
  for (const file of fs.readdirSync(setsDir)) {
    if (!file.endsWith('.json')) continue;
    let set;
    try {
      set = JSON.parse(fs.readFileSync(path.join(setsDir, file), 'utf8'));
    } catch {
      continue;
    }
    ecuSetsRead++;
    const repo = file.replace(/\.json$/, '');
    for (const ecu of set.ecu || []) {
      const header = String(ecu.hdr || '').toUpperCase();
      const eax = String(ecu.eax || '').toUpperCase();
      ecuTypes.set(`${repo}|${header}${eax ? '/' + eax : ''}`, ecu.type);
      if (!ecuTypes.has(`${repo}|${header}`)) ecuTypes.set(`${repo}|${header}`, ecu.type);
    }
  }
}
const ecuTypeOf = (s) => {
  if (ecuTypes.size === 0) return null;
  const repo = `${s.make}-${s.model}`;
  const header = String(s.header).toUpperCase();
  const eax = String(s.eax || '').toUpperCase();
  return ecuTypes.get(`${repo}|${header}${eax ? '/' + eax : ''}`) || ecuTypes.get(`${repo}|${header}`) || null;
};

// ---------------------------------------------------------------------------------------------------------------------
// Per-vehicle figures
// ---------------------------------------------------------------------------------------------------------------------

const SERVICE_KINDS = [
  'OilLifePercent',
  'OilDegradationPercent',
  'DistanceToNextService',
  'DaysToNextService',
  'DistanceSinceOilChange',
  'OilChangeInterval',
  'MaintenanceCounter',
  'OilLevel',
  'Other',
];

function serviceCount(v, kind, productionOnly) {
  return v.sig.filter((s) => s.serviceKind === kind && s.addressable && (!productionOnly || s.production)).length;
}

for (const v of vehicleList) {
  const current = v.sig.filter((s) => s.mileage === 'TotalOdometer' && s.distanceUsable);
  v.current = {
    signals: current.length,
    requests: new Set(current.map((s) => s.request)).size,
    modules: new Set(current.map((s) => s.module)).size,
    productionModules: new Set(current.filter((s) => s.production).map((s) => s.module)).size,
  };
  v.history = v.sig.filter((s) => s.mileage === 'OdometerAtEvent' && s.distanceUsable).length;
  v.install = v.sig.filter((s) => s.mileage === 'ModuleInstallRecord' && s.distanceUsable).length;
  v.component = v.sig.filter((s) => s.mileage === 'ComponentDistance' && s.distanceUsable).length;
  v.service = {};
  v.serviceProduction = {};
  for (const kind of SERVICE_KINDS) {
    v.service[kind] = serviceCount(v, kind, false);
    v.serviceProduction[kind] = serviceCount(v, kind, true);
  }
  v.anyServiceRead = SERVICE_KINDS.some((k) => k !== 'Other' && k !== 'OilLevel' && v.service[k] > 0);
  v.anyServiceReadProduction = SERVICE_KINDS.some((k) => k !== 'Other' && k !== 'OilLevel' && v.serviceProduction[k] > 0);
}

// ---------------------------------------------------------------------------------------------------------------------
// vehicle-coverage-matrix.csv
// ---------------------------------------------------------------------------------------------------------------------

const csvHeader = [
  'make', 'model', 'catalogRows', 'debugRows',
  'odometerSignals', 'odometerRequests', 'odometerModules', 'odometerModulesProduction', 'canCompareModules',
  'historySignals', 'installSignals', 'componentSignals',
  'oilLife', 'oilLifeProduction', 'oilDegradation', 'oilDegradationProduction',
  'distanceToService', 'daysToService', 'distanceSinceOilChange', 'oilChangeInterval', 'maintenanceCounter',
  'anyServiceReadProduction',
];
const csvLines = [csvHeader.join(',')];
for (const v of vehicleList) {
  csvLines.push(
    [
      v.make, v.model, v.catalogRows, v.debugRows,
      v.current.signals, v.current.requests, v.current.modules, v.current.productionModules, v.current.modules >= 2 ? 'yes' : 'no',
      v.history, v.install, v.component,
      v.service.OilLifePercent, v.serviceProduction.OilLifePercent, v.service.OilDegradationPercent, v.serviceProduction.OilDegradationPercent,
      v.service.DistanceToNextService, v.service.DaysToNextService, v.service.DistanceSinceOilChange, v.service.OilChangeInterval,
      v.service.MaintenanceCounter,
      v.anyServiceReadProduction ? 'yes' : 'no',
    ]
      .map((cell) => (/[",\n]/.test(String(cell)) ? `"${String(cell).replace(/"/g, '""')}"` : String(cell)))
      .join(',')
  );
}
write('vehicle-coverage-matrix.csv', csvLines.join('\n'));

// ---------------------------------------------------------------------------------------------------------------------
// Totals and per-make figures
// ---------------------------------------------------------------------------------------------------------------------

const vehicleCount = vehicleList.length;
const withKind = (kind, productionOnly) => vehicleList.filter((v) => (productionOnly ? v.serviceProduction[kind] : v.service[kind]) > 0).length;

const totals = {
  vehiclesInCatalog: vehicleCount,
  catalogRows: vehicleList.reduce((n, v) => n + v.catalogRows, 0),
  catalogRowsFlaggedDebug: vehicleList.reduce((n, v) => n + v.debugRows, 0),
  service: Object.fromEntries(
    SERVICE_KINDS.map((kind) => [
      kind,
      { vehicles: withKind(kind, false), vehiclesWithProductionDefinition: withKind(kind, true), signals: signals.filter((s) => s.serviceKind === kind).length },
    ])
  ),
  vehiclesWithAnyServiceRead: vehicleList.filter((v) => v.anyServiceRead).length,
  vehiclesWithAnyServiceReadInProductionDefinitions: vehicleList.filter((v) => v.anyServiceReadProduction).length,
  mileage: {
    vehiclesWithCurrentOdometer: vehicleList.filter((v) => v.current.signals > 0).length,
    vehiclesWithCurrentOdometerInProductionDefinitions: vehicleList.filter((v) => v.current.productionModules > 0).length,
    vehiclesWithTwoOrMoreModules: vehicleList.filter((v) => v.current.modules >= 2).length,
    vehiclesWithTwoOrMoreModulesInProductionDefinitions: vehicleList.filter((v) => v.current.productionModules >= 2).length,
    vehiclesWithHistoryRecords: vehicleList.filter((v) => v.history > 0).length,
    vehiclesWithInstallRecords: vehicleList.filter((v) => v.install > 0).length,
    vehiclesWithComponentCounters: vehicleList.filter((v) => v.component > 0).length,
    vehiclesWithAnyRecord: vehicleList.filter((v) => v.history + v.install + v.component > 0).length,
  },
};

const makeNames = [...new Set(vehicleList.map((v) => v.make))].sort((a, b) => a.localeCompare(b, 'en'));
const byMake = makeNames.map((make) => {
  const list = vehicleList.filter((v) => v.make === make);
  const has = (predicate) => list.filter(predicate).length;
  return {
    make,
    vehicles: list.length,
    oilLifeVehicles: has((v) => v.service.OilLifePercent > 0),
    oilLifeProductionVehicles: has((v) => v.serviceProduction.OilLifePercent > 0),
    oilDegradationVehicles: has((v) => v.service.OilDegradationPercent > 0),
    distanceToServiceVehicles: has((v) => v.service.DistanceToNextService > 0),
    distanceSinceOilChangeVehicles: has((v) => v.service.DistanceSinceOilChange > 0),
    oilChangeIntervalVehicles: has((v) => v.service.OilChangeInterval > 0),
    maintenanceCounterVehicles: has((v) => v.service.MaintenanceCounter > 0),
    anyServiceReadVehicles: has((v) => v.anyServiceRead),
    anyServiceReadProductionVehicles: has((v) => v.anyServiceReadProduction),
    currentOdometerVehicles: has((v) => v.current.signals > 0),
    twoOrMoreModulesVehicles: has((v) => v.current.modules >= 2),
    historyRecordVehicles: has((v) => v.history + v.install + v.component > 0),
  };
});

// ---------------------------------------------------------------------------------------------------------------------
// oil-life-coverage.json
// ---------------------------------------------------------------------------------------------------------------------

function requestTargets(kinds) {
  const groups = new Map();
  for (const s of signals.filter((x) => kinds.includes(x.serviceKind) && x.addressable)) {
    const key = [s.make, s.request, s.name, s.unit, s.bitLength, s.bitOffset].join('||');
    if (!groups.has(key)) {
      groups.set(key, {
        make: s.make,
        request: s.request,
        transmitCanId: s.tx,
        receiveCanId: s.rx || null,
        name: s.name,
        kind: s.serviceKind,
        unit: s.unit,
        bitLength: s.bitLength,
        bitOffset: s.bitOffset,
        models: [],
      });
    }
    groups.get(key).models.push({ model: s.model, definitionFlaggedDebug: s.debug });
  }
  return [...groups.values()]
    .map((g) => ({ ...g, models: g.models.sort((a, b) => a.model.localeCompare(b.model, 'en')) }))
    .sort((a, b) => a.make.localeCompare(b.make, 'en') || a.request.localeCompare(b.request, 'en'));
}

const oilLifeSignals = signals.filter((s) => s.serviceKind === 'OilLifePercent');
const degradationSignals = signals.filter((s) => s.serviceKind === 'OilDegradationPercent');
const unitCounts = (list) => Object.fromEntries(sortedEntries(count(list, (s) => s.unit || '(none)')));

const oilLife = {
  schema: 'obd360.research.oil-life-coverage/1',
  about:
    'What the shipped catalog (OBDb public signal definitions) can read about engine-oil condition and service intervals, per vehicle. ' +
    'These are definitions, not measurements: nothing here was read from a vehicle by this research.',
  generatedFrom: provenance(),
  kinds: curatedService.signalKinds,
  totals: {
    vehiclesInCatalog: vehicleCount,
    oilLifeRemaining: {
      signals: oilLifeSignals.length,
      vehicles: totals.service.OilLifePercent.vehicles,
      vehiclesWithProductionDefinition: totals.service.OilLifePercent.vehiclesWithProductionDefinition,
      units: unitCounts(oilLifeSignals),
    },
    oilDegradation: {
      signals: degradationSignals.length,
      vehicles: totals.service.OilDegradationPercent.vehicles,
      vehiclesWithProductionDefinition: totals.service.OilDegradationPercent.vehiclesWithProductionDefinition,
      units: unitCounts(degradationSignals),
    },
    otherServiceKinds: Object.fromEntries(
      ['DistanceToNextService', 'DaysToNextService', 'DistanceSinceOilChange', 'OilChangeInterval', 'MaintenanceCounter', 'OilLevel'].map((k) => [k, totals.service[k]])
    ),
  },
  byMake: byMake
    .filter((m) => m.oilLifeVehicles + m.oilDegradationVehicles + m.distanceToServiceVehicles + m.distanceSinceOilChangeVehicles + m.oilChangeIntervalVehicles + m.maintenanceCounterVehicles > 0)
    .map((m) => ({
      make: m.make,
      vehiclesInCatalog: m.vehicles,
      oilLifeRemainingVehicles: m.oilLifeVehicles,
      oilLifeRemainingProductionVehicles: m.oilLifeProductionVehicles,
      oilDegradationVehicles: m.oilDegradationVehicles,
      distanceToServiceVehicles: m.distanceToServiceVehicles,
      distanceSinceOilChangeVehicles: m.distanceSinceOilChangeVehicles,
      oilChangeIntervalVehicles: m.oilChangeIntervalVehicles,
      maintenanceCounterVehicles: m.maintenanceCounterVehicles,
    })),
  oilLifeRemainingTargets: requestTargets(['OilLifePercent']),
  oilDegradationTargets: requestTargets(['OilDegradationPercent']),
  distanceAndTimeTargets: requestTargets(['DistanceToNextService', 'DaysToNextService', 'DistanceSinceOilChange', 'OilChangeInterval']),
  notes: curatedService.oilLifeNotes,
};

writeJson('oil-life-coverage.json', oilLife);

// ---------------------------------------------------------------------------------------------------------------------
// service-operation-coverage.json
// ---------------------------------------------------------------------------------------------------------------------

const normaliseBrand = (name) => {
  const text = String(name).toLowerCase().replace(/[^a-z0-9]/g, '');
  const aliases = { mercedesbenz: 'mercedes', vw: 'volkswagen', skoda: 'skoda', 'škoda': 'skoda', vauxhall: 'opel', landrover: 'land', rangerover: 'land', alfaromeo: 'alfa', rollsroyce: 'rolls', ds: 'ds', dsautomobiles: 'ds' };
  return aliases[text] || text.replace(/š/g, 's');
};

const procedureBrands = new Map();
for (const p of procedures.procedures) {
  for (const brand of String(p.brand).split('|')) {
    const key = normaliseBrand(brand);
    if (!procedureBrands.has(key)) procedureBrands.set(key, { names: new Set(), procedures: 0, modelSpecific: 0, brandWide: 0, oilService: 0, toolRequired: 0 });
    const entry = procedureBrands.get(key);
    entry.names.add(brand);
    entry.procedures++;
    if (p.model === '*') entry.brandWide++;
    else entry.modelSpecific++;
    if (p.category === 'Oil Service' || p.kind === 'OilService') entry.oilService++;
    if (p.toolRequired === true) entry.toolRequired++;
  }
}

const catalogBrandKeys = new Map(makeNames.map((m) => [normaliseBrand(m), m]));
const guidedBrands = [...procedureBrands.entries()]
  .map(([key, e]) => ({
    brand: [...e.names].sort().join(' | '),
    procedures: e.procedures,
    modelSpecific: e.modelSpecific,
    brandWide: e.brandWide,
    oilServiceGuides: e.oilService,
    needsWorkshopTool: e.toolRequired,
    catalogMake: catalogBrandKeys.get(key) || null,
    catalogHasAnyServiceReadDefinition: catalogBrandKeys.has(key) ? byMake.find((m) => m.make === catalogBrandKeys.get(key)).anyServiceReadVehicles > 0 : false,
    catalogHasNonDebugServiceRead: catalogBrandKeys.has(key) ? byMake.find((m) => m.make === catalogBrandKeys.get(key)).anyServiceReadProductionVehicles > 0 : false,
  }))
  .sort((a, b) => b.procedures - a.procedures || a.brand.localeCompare(b.brand));

const familyStats = curatedService.brandFamilies.map((family) => {
  const makes = byMake.filter((m) => family.catalogMakes.includes(m.make));
  const sum = (field) => makes.reduce((n, m) => n + m[field], 0);
  const guided = guidedBrands.filter((g) => g.catalogMake && family.catalogMakes.includes(g.catalogMake));
  return {
    ...family,
    computed: {
      vehiclesInCatalog: sum('vehicles'),
      oilLifeRemainingVehicles: sum('oilLifeVehicles'),
      oilLifeRemainingProductionVehicles: sum('oilLifeProductionVehicles'),
      oilDegradationVehicles: sum('oilDegradationVehicles'),
      distanceToServiceVehicles: sum('distanceToServiceVehicles'),
      distanceSinceOilChangeVehicles: sum('distanceSinceOilChangeVehicles'),
      oilChangeIntervalVehicles: sum('oilChangeIntervalVehicles'),
      maintenanceCounterVehicles: sum('maintenanceCounterVehicles'),
      guidedProcedures: guided.reduce((n, g) => n + g.procedures, 0),
    },
  };
});

const candidatesByStatus = count(oemOperations.vehicleResearchCandidates || [], (c) => c.status);
const serviceOperationCoverage = {
  schema: 'obd360.research.service-operation-coverage/1',
  about:
    'Service-page operations: what each one is, what the app does with it today, and what evidence exists per brand family. ' +
    'An operation that changes the car is offered only with exact, per-vehicle, reviewed evidence; none of that exists for any vehicle yet.',
  generatedFrom: provenance(),
  principle: curatedService.principle,
  operationTypes: curatedService.operationTypes,
  currentAppBehaviour: curatedService.currentAppBehaviour,
  shippedCatalogs: {
    guidedProcedures: {
      file: 'Resources/Raw/service_procedures.json',
      catalogVersion: procedures.catalogVersion,
      procedures: procedures.procedures.length,
      executionTypes: Object.fromEntries(sortedEntries(count(procedures.procedures, (p) => p.executionType))),
      verificationStatus: Object.fromEntries(sortedEntries(count(procedures.procedures, (p) => p.verificationStatus || '(empty)'))),
      verificationMethod: Object.fromEntries(sortedEntries(count(procedures.procedures, (p) => p.verificationMethod || '(empty)'))),
      categories: Object.fromEntries(sortedEntries(count(procedures.procedures, (p) => p.category))),
      brandWideEntries: procedures.procedures.filter((p) => p.model === '*').length,
      modelSpecificEntries: procedures.procedures.filter((p) => p.model !== '*').length,
      entriesNeedingAWorkshopTool: procedures.procedures.filter((p) => p.toolRequired === true).length,
      entriesLinkedToADiagnosticOperation: procedures.procedures.filter((p) => p.oemOperationId).length,
      brands: guidedBrands,
    },
    diagnosticWriteOperations: {
      file: 'Resources/Raw/oem_operations.json',
      catalogStatus: oemOperations.catalogStatus,
      operationsEnabled: (oemOperations.operations || []).length,
      productionRule: oemOperations.productionRule,
      promotionChecklist: oemOperations.promotionChecklist,
      familyResearchCandidates: (oemOperations.familyResearchCandidates || []).map((c) => ({
        candidateId: c.candidateId,
        brandFamily: c.brandFamily,
        system: c.system,
        targetFunction: c.targetFunction,
        status: c.status,
      })),
      vehicleResearchCandidatesByStatus: Object.fromEntries(sortedEntries(candidatesByStatus)),
    },
  },
  brandFamilies: familyStats,
  gaps: {
    note: 'A guide tells the owner what to press; a read definition tells the app where a value is. The two are independent, so a brand can have either, both or neither.',
    brandsWithGuidesAndAServiceReadDefinition: guidedBrands.filter((g) => g.catalogHasAnyServiceReadDefinition).map((g) => g.brand),
    brandsWithGuidesAndANonDebugServiceRead: guidedBrands.filter((g) => g.catalogHasNonDebugServiceRead).map((g) => g.brand),
    brandsWithGuidesButNoServiceReadDefinition: guidedBrands.filter((g) => !g.catalogHasAnyServiceReadDefinition).map((g) => g.brand),
    makesWithAServiceReadDefinitionButNoGuide: byMake
      .filter((m) => m.anyServiceReadVehicles > 0 && !procedureBrands.has(normaliseBrand(m.make)))
      .map((m) => m.make),
  },
  negativeFindings: curatedService.negativeFindings,
};
writeJson('service-operation-coverage.json', serviceOperationCoverage);

// ---------------------------------------------------------------------------------------------------------------------
// mileage-source-catalog.json
// ---------------------------------------------------------------------------------------------------------------------

const classOrder = ['TotalOdometer', 'OdometerAtEvent', 'ModuleInstallRecord', 'ComponentDistance', 'Excluded'];
const classStats = Object.fromEntries(
  classOrder.map((cls) => {
    const list = signals.filter((s) => s.mileage === cls);
    const usable = list.filter((s) => s.distanceUsable);
    return [
      cls,
      {
        signals: list.length,
        distinctNames: new Set(list.map((s) => s.name)).size,
        vehicles: new Set(list.map((s) => s.vehicle)).size,
        signalsTheMileagePageCanRequest: usable.length,
        vehiclesTheMileagePageCanRequest: new Set(usable.map((s) => s.vehicle)).size,
        flaggedDebug: list.filter((s) => s.debug).length,
        units: Object.fromEntries(sortedEntries(count(list, (s) => s.unit || '(none)'))),
      },
    ];
  })
);

const totalSignals = signals.filter((s) => s.mileage === 'TotalOdometer');
const notRequestable = totalSignals.filter((s) => !s.distanceUsable);
const moduleDistribution = Object.fromEntries(
  [...count(vehicleList.filter((v) => v.current.modules > 0), (v) => v.current.modules).entries()].sort((a, b) => a[0] - b[0])
);
const requestCap = curatedMileage.firstPassRequestLimit;

const eventKinds = Object.fromEntries(sortedEntries(count(signals.filter((s) => s.mileage === 'OdometerAtEvent'), (s) => s.event || 'None')));

// Many OBDb names say which module holds the value ("Odometer, instrument panel"). This only counts what the names state.
const MODULE_NAME_HINTS = [
  ['instrument cluster / dashboard', /instrument|cluster|dash|meter group|combination meter/i],
  ['engine / powertrain computer', /engine|powertrain|pcm|ecm/i],
  ['brake / ABS / ESP', /brake|abs|esp|stability/i],
  ['transmission', /transmission|gearbox/i],
  ['gateway', /gateway/i],
  ['body / comfort', /body|bcm|comfort/i],
  ['parking / assistance', /parking|assist/i],
];
const nameHint = (name) => (MODULE_NAME_HINTS.find(([, rx]) => rx.test(name)) || ['the name does not state a module'])[0];
const moduleNameHints = Object.fromEntries(
  sortedEntries(count(signals.filter((s) => s.mileage === 'TotalOdometer' && s.distanceUsable), (s) => nameHint(s.name)))
);

const moduleTypeRows = (cls) => {
  if (ecuTypes.size === 0) return null;
  const seen = new Map();
  for (const s of signals.filter((x) => x.mileage === cls && x.distanceUsable)) {
    seen.set(`${s.vehicle}|${s.module}`, ecuTypeOf(s) || '(not stated)');
  }
  return Object.fromEntries(sortedEntries(count([...seen.values()], (t) => t)));
};

const targetGroups = new Map();
for (const s of totalSignals.filter((x) => x.distanceUsable)) {
  const key = [s.make, s.request, s.name, s.unit, s.bitLength].join('||');
  if (!targetGroups.has(key)) {
    targetGroups.set(key, { make: s.make, request: s.request, transmitCanId: s.tx, receiveCanId: s.rx || null, name: s.name, unit: s.unit, bitLength: s.bitLength, moduleType: ecuTypeOf(s), vehicles: 0, vehiclesFlaggedDebug: 0 });
  }
  const g = targetGroups.get(key);
  g.vehicles++;
  if (s.debug) g.vehiclesFlaggedDebug++;
}
const totalTargets = [...targetGroups.values()].sort((a, b) => a.make.localeCompare(b.make, 'en') || b.vehicles - a.vehicles || a.request.localeCompare(b.request, 'en'));

const usedProfiles = curatedMileage.implementedBroadcastProfiles;
const canSignals = canRows.map((r) => {
  const used = usedProfiles.find((p) => p.canId === r.id && p.startBit === r.startBit && p.bitLength === r.bitLength);
  // The first note that matches wins; a note may name the file and the frame id to be specific.
  const note = curatedMileage.canSignalNotes.find((n) => n.signal === r.signal && (!n.dbc || n.dbc === r.dbc) && (!n.id || n.id === r.id));
  return {
    dbcFile: r.dbc,
    canId: `0x${r.id.toString(16).toUpperCase()}`,
    message: r.message,
    signal: r.signal,
    layout: `${r.startBit}|${r.bitLength}@${r.byteOrder}`,
    factor: r.factor,
    offset: r.offset,
    unit: r.unit || '',
    usedByTheApp: used ? used.family : null,
    assessment: note
      ? note.assessment
      : used
        ? 'The app already listens to this field (profile: ' + used.family + '); a value is kept only when two frames agree.'
        : 'A candidate by name only. Needs the vehicle-specific evidence listed in the source entry before any use.',
  };
});

const mileageCatalog = {
  schema: 'obd360.research.mileage-source-catalog/1',
  about:
    'Where a car keeps distance information, how the app asks for each kind, and how much evidence stands behind each way. ' +
    'Every kind is kept separate; only values of the same kind are ever compared, and no finding names an original mileage.',
  generatedFrom: provenance(),
  principle: curatedMileage.principle,
  sources: curatedMileage.sources,
  comparisonRules: curatedMileage.comparisonRules,
  obdbCatalog: {
    note:
      'Counts are over the OBDb rows of the shipped catalog (read services 22 and 21). "Requestable" applies the limits CatalogOdometerProvider applies: ' +
      'an addressable module, a distance unit, a 16 to 32 bit field. The model year is not part of a row; the app reads the year filter and the per-year debug flag of OBDb from ' +
      'Resources/Raw/obdb_year_evidence.json when the vehicle\'s model year is known, and obdb-evidence-report.md counts the same signals by replies recorded per model year.',
    vehiclesInCatalog: vehicleCount,
    classes: classStats,
    odometerEventKinds: eventKinds,
    totalOdometerRowsNotRequestable: {
      count: notRequestable.length,
      reasons: Object.fromEntries(
        sortedEntries(
          count(notRequestable, (s) => {
            if (!s.addressable) return 'module address cannot be resolved safely';
            if (!DISTANCE_UNITS.has(s.unit)) return `unit "${s.unit || '(none)'}" is not a distance`;
            return `field of ${s.bitLength} bits is outside 16 to 32`;
          })
        )
      ),
      examples: [...new Set(notRequestable.map((s) => `${s.name} [${s.unit || 'no unit'}] (${s.make})`))].slice(0, 12),
    },
    currentOdometerModulesPerVehicle: moduleDistribution,
    vehiclesBeyondTheFirstPassRequestLimit: vehicleList.filter((v) => v.current.requests > requestCap).map((v) => `${v.make} ${v.model} (${v.current.requests} requests)`),
    moduleNamedByTheSignalName: moduleNameHints,
    moduleTypesHoldingACurrentCopy: moduleTypeRows('TotalOdometer'),
    moduleTypesHoldingAHistoryRecord: moduleTypeRows('OdometerAtEvent'),
    moduleTypeSource:
      ecuTypes.size === 0 ? 'not supplied' : `OBDb signalsets/v3/default.json of ${ecuSetsRead} vehicles (--obdb-sets); a header without a stated type is counted as "(not stated)"`,
    byMake: byMake.map((m) => ({
      make: m.make,
      vehicles: m.vehicles,
      withCurrentOdometer: m.currentOdometerVehicles,
      withTwoOrMoreModules: m.twoOrMoreModulesVehicles,
      withHistoryOrInstallRecords: m.historyRecordVehicles,
    })),
    currentOdometerRequestTargets: totalTargets,
  },
  canBroadcast: {
    note: 'opendbc signals of the shipped catalog whose name says odometer. "usedByTheApp" is the profile family of BroadcastOdometerProvider that listens to the same frame field.',
    catalogCanSignals: catalogInfo.canSignalCount,
    odometerNamedSignals: canSignals,
  },
};
writeJson('mileage-source-catalog.json', mileageCatalog);

// ---------------------------------------------------------------------------------------------------------------------
// coverage-report.md
// ---------------------------------------------------------------------------------------------------------------------

function mdTable(headers, rows) {
  const head = `| ${headers.join(' | ')} |`;
  const line = `| ${headers.map(() => '---').join(' | ')} |`;
  return [head, line, ...rows.map((r) => `| ${r.map((c) => String(c).replace(/\|/g, '\\|')).join(' | ')} |`)].join('\n');
}

const t = totals;
const lines = [];
lines.push('# Coverage report: Service oil-life reads and multi-module mileage');
lines.push('');
lines.push('Generated by `research/tools/build-research-files.js` from the shipped catalog. The figures count public definitions (OBDb, CC BY-SA 4.0), not');
lines.push('measurements: **no number below was observed on a vehicle by this research.** A definition flagged `debug` in OBDb is one the community is');
lines.push('still checking. "Non-debug" below is the catalog\'s command-level flag only: the catalog rows carry no model year, so it does **not** mean that the');
lines.push('definition was seen working for a given model year. `obdb-evidence-report.md` counts the same signals by model-year evidence (a reply recorded in OBDb\'s');
lines.push('own tests) and is the figure to use for "confirmed". The Service page\'s normal refresh sends the non-debug definitions of the exact model and the');
lines.push('brand-wide identifiers of the make, and with a verified model year applies that year\'s flag and filter; the Mileage page requests the exact model\'s');
lines.push('definitions whether or not they are flagged and says when one is still under test or has a recorded reply.');
lines.push('');
lines.push(`Catalog: ${catalogInfo.sources.map((s) => `${s.name} (${s.rows} rows, revision ${String(s.revision).split(' ')[0].slice(0, 10)})`).join('; ')}.`);
lines.push(`${vehicleCount} vehicles (make and model), ${t.catalogRows} signal rows, ${t.catalogRowsFlaggedDebug} (${pct(t.catalogRowsFlaggedDebug, t.catalogRows)}) flagged debug.`);
lines.push('');
lines.push('## 1. Service page: what the catalog can read');
lines.push('');
lines.push(
  mdTable(
    ['Reading', 'Vehicles', 'Vehicles with a non-debug definition', 'Signals'],
    [
      ['Oil life **remaining** (%, shrinks)', t.service.OilLifePercent.vehicles, t.service.OilLifePercent.vehiclesWithProductionDefinition, t.service.OilLifePercent.signals],
      ['Oil **degradation** (%, grows - opposite scale)', t.service.OilDegradationPercent.vehicles, t.service.OilDegradationPercent.vehiclesWithProductionDefinition, t.service.OilDegradationPercent.signals],
      ['Distance to next service / oil change', t.service.DistanceToNextService.vehicles, t.service.DistanceToNextService.vehiclesWithProductionDefinition, t.service.DistanceToNextService.signals],
      ['Days to next service', t.service.DaysToNextService.vehicles, t.service.DaysToNextService.vehiclesWithProductionDefinition, t.service.DaysToNextService.signals],
      ['Distance since the last oil change', t.service.DistanceSinceOilChange.vehicles, t.service.DistanceSinceOilChange.vehiclesWithProductionDefinition, t.service.DistanceSinceOilChange.signals],
      ['Oil change interval', t.service.OilChangeInterval.vehicles, t.service.OilChangeInterval.vehiclesWithProductionDefinition, t.service.OilChangeInterval.signals],
      ['Maintenance counters (e.g. Honda codes)', t.service.MaintenanceCounter.vehicles, t.service.MaintenanceCounter.vehiclesWithProductionDefinition, t.service.MaintenanceCounter.signals],
      ['Oil level (not oil life)', t.service.OilLevel.vehicles, t.service.OilLevel.vehiclesWithProductionDefinition, t.service.OilLevel.signals],
    ]
  )
);
lines.push('');
lines.push(`Vehicles with **any** service-interval or oil-life read: ${t.vehiclesWithAnyServiceRead} of ${vehicleCount} (${pct(t.vehiclesWithAnyServiceRead, vehicleCount)}); with a non-debug definition: ${t.vehiclesWithAnyServiceReadInProductionDefinitions} (${pct(t.vehiclesWithAnyServiceReadInProductionDefinitions, vehicleCount)}).`);
lines.push('');
lines.push('Per make (only makes with at least one such reading):');
lines.push('');
lines.push(
  mdTable(
    ['Make', 'Vehicles', 'Oil life', 'Oil life (non-debug)', 'Degradation', 'Dist. to service', 'Dist. since oil', 'Interval', 'Maint. counters'],
    byMake
      .filter((m) => m.oilLifeVehicles + m.oilDegradationVehicles + m.distanceToServiceVehicles + m.distanceSinceOilChangeVehicles + m.oilChangeIntervalVehicles + m.maintenanceCounterVehicles > 0)
      .map((m) => [m.make, m.vehicles, m.oilLifeVehicles, m.oilLifeProductionVehicles, m.oilDegradationVehicles, m.distanceToServiceVehicles, m.distanceSinceOilChangeVehicles, m.oilChangeIntervalVehicles, m.maintenanceCounterVehicles])
  )
);
lines.push('');
lines.push('### Resets');
lines.push('');
lines.push(`* Guided procedures shipped: ${procedures.procedures.length} (all manual, all \`SourceDocumented\`, none \`VehicleVerified\`); ${serviceOperationCoverage.shippedCatalogs.guidedProcedures.brandWideEntries} are brand-wide, ${serviceOperationCoverage.shippedCatalogs.guidedProcedures.modelSpecificEntries} model-specific; ${serviceOperationCoverage.shippedCatalogs.guidedProcedures.entriesNeedingAWorkshopTool} state that a workshop tool is needed.`);
lines.push(`* Diagnostic reset/write operations enabled: **${serviceOperationCoverage.shippedCatalogs.diagnosticWriteOperations.operationsEnabled}**. ${(oemOperations.familyResearchCandidates || []).length} brand-family and ${(oemOperations.vehicleResearchCandidates || []).length} vehicle research candidates are recorded as non-executable.`);
lines.push('* Direct set-to-50% is offered nowhere: it needs a reviewed entry that says in so many words that the module accepts arbitrary percentages (`AllowsPercentTargets`), and no entry exists.');
lines.push('');
const gaps = serviceOperationCoverage.gaps;
lines.push(`Guided procedures exist for ${guidedBrands.length} brand names. ${gaps.brandsWithGuidesAndAServiceReadDefinition.length} of them also have at least one service-read definition in the catalog (${gaps.brandsWithGuidesAndANonDebugServiceRead.length} with a non-debug one): ${gaps.brandsWithGuidesAndAServiceReadDefinition.join(', ')}. The other ${gaps.brandsWithGuidesButNoServiceReadDefinition.length} brand names have a guide and no read definition. Makes with a read definition and no guide: ${gaps.makesWithAServiceReadDefinitionButNoGuide.join(', ') || 'none'}.`);
lines.push('');
lines.push('## 2. Mileage page: what the catalog can read');
lines.push('');
lines.push(
  mdTable(
    ['Kind of distance', 'Signals', 'Distinct names', 'Vehicles', 'Requestable signals', 'Requestable vehicles', 'Flagged debug'],
    classOrder.map((cls) => [cls, classStats[cls].signals, classStats[cls].distinctNames, classStats[cls].vehicles, classStats[cls].signalsTheMileagePageCanRequest, classStats[cls].vehiclesTheMileagePageCanRequest, classStats[cls].flaggedDebug])
  )
);
lines.push('');
lines.push(`* Vehicles with a current odometer copy the Mileage page can request: ${t.mileage.vehiclesWithCurrentOdometer} of ${vehicleCount} (${pct(t.mileage.vehiclesWithCurrentOdometer, vehicleCount)}); ${t.mileage.vehiclesWithCurrentOdometerInProductionDefinitions} with a non-debug definition.`);
lines.push(`* With **two or more modules** (so a comparison is possible): ${t.mileage.vehiclesWithTwoOrMoreModules} (${pct(t.mileage.vehiclesWithTwoOrMoreModules, vehicleCount)}); with non-debug definitions only: ${t.mileage.vehiclesWithTwoOrMoreModulesInProductionDefinitions}.`);
lines.push(`* With history, installation or component records: ${t.mileage.vehiclesWithAnyRecord} (history ${t.mileage.vehiclesWithHistoryRecords}, installation ${t.mileage.vehiclesWithInstallRecords}, component ${t.mileage.vehiclesWithComponentCounters}).`);
lines.push(`* Modules per vehicle (current copies): ${Object.entries(moduleDistribution).map(([k, v]) => `${k} module(s): ${v}`).join(', ')}.`);
lines.push(`* Vehicles with more requests than the first-pass limit of ${requestCap}: ${mileageCatalog.obdbCatalog.vehiclesBeyondTheFirstPassRequestLimit.length ? mileageCatalog.obdbCatalog.vehiclesBeyondTheFirstPassRequestLimit.join('; ') : 'none'}.`);
lines.push(`* "Total odometer" rows the page cannot request: ${notRequestable.length} (${Object.entries(mileageCatalog.obdbCatalog.totalOdometerRowsNotRequestable.reasons).map(([k, v]) => `${v}x ${k}`).join('; ')}).`);
lines.push('');
lines.push(`What the names of the current-odometer signals say about the holding module: ${Object.entries(moduleNameHints).map(([k, v]) => `${k} ${v}`).join(', ')}.`);
if (ecuTypes.size > 0) {
  lines.push('');
  lines.push(`Module types holding a current copy (vehicle x module pairs, OBDb types of ${ecuSetsRead} vehicles): ${Object.entries(mileageCatalog.obdbCatalog.moduleTypesHoldingACurrentCopy).map(([k, v]) => `${k} ${v}`).join(', ')}.`);
  lines.push('');
  lines.push(`Module types holding a history record: ${Object.entries(mileageCatalog.obdbCatalog.moduleTypesHoldingAHistoryRecord).map(([k, v]) => `${k} ${v}`).join(', ')}.`);
}
lines.push('');
lines.push('Event kinds of the history records: ' + Object.entries(eventKinds).map(([k, v]) => `${k} ${v}`).join(', ') + '.');
lines.push('');
lines.push('Per make:');
lines.push('');
lines.push(
  mdTable(
    ['Make', 'Vehicles', 'Current odometer', '2+ modules', 'Records'],
    byMake.filter((m) => m.currentOdometerVehicles + m.historyRecordVehicles > 0).map((m) => [m.make, m.vehicles, m.currentOdometerVehicles, m.twoOrMoreModulesVehicles, m.historyRecordVehicles])
  )
);
lines.push('');
lines.push('## 3. Passive CAN odometer (opendbc, MIT)');
lines.push('');
lines.push(`${canSignals.length} signals of the ${catalogInfo.canSignalCount} CAN signals in the shipped catalog have an odometer name; ${canSignals.filter((c) => c.usedByTheApp).length} entries are fields \`BroadcastOdometerProvider\` already listens to (a field that several DBC files repeat counts once per file). The full list with an assessment of each is in \`mileage-source-catalog.json\` (\`canBroadcast\`).`);
lines.push('');
lines.push('## 4. Reading this report');
lines.push('');
for (const limit of curatedService.reportLimits) lines.push(`* ${limit}`);
write('coverage-report.md', lines.join('\n'));

// ---------------------------------------------------------------------------------------------------------------------

function provenance() {
  return {
    classifiedSignals: path.basename(signalsPath),
    exportedUtc: exported.generatedUtc,
    classifiers: exported.classifiers,
    catalogSources: catalogInfo.sources.map((s) => ({ id: s.id, name: s.name, revision: s.revision, rows: s.rows })),
    catalogMetaRevision: catalogInfo.metaRevision,
    note: 'OBDb-derived tables: CC BY-SA 4.0, attribution and share-alike apply (see README.md).',
  };
}

console.log(`vehicles ${vehicleCount}, signals ${signals.length}`);
console.log(`oil life: ${t.service.OilLifePercent.vehicles} vehicles (${t.service.OilLifePercent.vehiclesWithProductionDefinition} non-debug); degradation: ${t.service.OilDegradationPercent.vehicles}`);
console.log(`current odometer: ${t.mileage.vehiclesWithCurrentOdometer} vehicles, 2+ modules: ${t.mileage.vehiclesWithTwoOrMoreModules}, records: ${t.mileage.vehiclesWithAnyRecord}`);
console.log(`module types: ${ecuTypes.size === 0 ? 'not supplied' : ecuSetsRead + ' vehicle files'}`);
console.log(`wrote files to ${outDir}`);
