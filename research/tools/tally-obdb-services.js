#!/usr/bin/env node
'use strict';

/*
 * Counts which diagnostic services the OBDb signal sets use, to back the statement "every OBDb command is a read".
 *
 * Usage:  node tally-obdb-services.js <folder written by fetch-obdb-sets.js>
 *
 * Prints, for every service id found in a command ("cmd": {"22": "054B"}), how many signals use it and in how many vehicles.
 * Signal sets that are not strict JSON (comments, trailing commas) are read after those are removed.
 */

const fs = require('fs');
const path = require('path');

const folder = process.argv[2];
if (!folder) {
  console.error('usage: node tally-obdb-services.js <folder>');
  process.exit(2);
}

const signalsByService = new Map();
const vehiclesByService = new Map();
let read = 0;
let unreadable = 0;

for (const file of fs.readdirSync(folder).filter((f) => f.endsWith('.json'))) {
  const text = fs.readFileSync(path.join(folder, file), 'utf8');
  let set;
  try {
    set = JSON.parse(text);
  } catch {
    try {
      set = JSON.parse(text.replace(/\/\/.*$/gm, '').replace(/\/\*[\s\S]*?\*\//g, '').replace(/,\s*([}\]])/g, '$1'));
    } catch {
      unreadable++;
      continue;
    }
  }
  read++;
  for (const command of set.commands || []) {
    const service = Object.keys(command.cmd || {})[0];
    if (!service) continue;
    signalsByService.set(service, (signalsByService.get(service) || 0) + (command.signals || []).length);
    if (!vehiclesByService.has(service)) vehiclesByService.set(service, new Set());
    vehiclesByService.get(service).add(file);
  }
}

console.log(`${read} signal sets read, ${unreadable} unreadable`);
for (const [service, signals] of [...signalsByService.entries()].sort((a, b) => b[1] - a[1])) {
  console.log(`service ${service}: ${signals} signals in ${vehiclesByService.get(service).size} vehicles`);
}
