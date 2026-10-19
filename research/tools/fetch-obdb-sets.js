#!/usr/bin/env node
'use strict';

/*
 * Downloads the default signal set (signalsets/v3/default.json) of every OBDb vehicle repository into a folder, one
 * <Make-Model>.json per vehicle, plus repos.tsv with each repository's licence and last push. The folder is the optional
 * --obdb-sets input of build-research-files.js (it adds the module types OBDb states for each header) and the input of
 * tally-obdb-services.js (it counts the diagnostic services the signal sets use).
 *
 * Usage:  node fetch-obdb-sets.js <output folder> [--limit N]
 *
 * Uses the public GitHub API without a token (60 requests an hour), so the repository list takes about nine requests and the files come
 * from raw.githubusercontent.com. Set GITHUB_TOKEN to raise the API limit. The output folder must be outside this repository:
 * the files are CC BY-SA 4.0 data of the OBDb contributors and are not redistributed here.
 *
 * Only repositories whose licence the API reports as CC-BY-SA-4.0 are downloaded; a repository without that licence is listed in
 * repos.tsv and skipped.
 */

const fs = require('fs');
const path = require('path');
const https = require('https');

const outDir = process.argv[2];
if (!outDir) {
  console.error('usage: node fetch-obdb-sets.js <output folder> [--limit N]');
  process.exit(2);
}
const limitIndex = process.argv.indexOf('--limit');
const limit = limitIndex >= 0 ? Number(process.argv[limitIndex + 1]) : Infinity;
fs.mkdirSync(outDir, { recursive: true });

function get(url) {
  return new Promise((resolve, reject) => {
    const headers = { 'User-Agent': 'obd360-research', Accept: 'application/vnd.github+json' };
    if (process.env.GITHUB_TOKEN && url.startsWith('https://api.github.com/')) headers.Authorization = `Bearer ${process.env.GITHUB_TOKEN}`;
    https
      .get(url, { headers }, (res) => {
        let body = '';
        res.setEncoding('utf8');
        res.on('data', (chunk) => (body += chunk));
        res.on('end', () => (res.statusCode === 200 ? resolve(body) : reject(new Error(`${res.statusCode} ${url}`))));
      })
      .on('error', reject);
  });
}

(async () => {
  const repos = [];
  for (let page = 1; ; page++) {
    const batch = JSON.parse(await get(`https://api.github.com/orgs/OBDb/repos?per_page=100&page=${page}`));
    if (batch.length === 0) break;
    repos.push(...batch);
  }

  const lines = ['name\tdefault_branch\tlicense\tpushed_at'];
  let saved = 0;
  let skipped = 0;
  for (const repo of repos) {
    const license = repo.license ? repo.license.spdx_id : 'none';
    lines.push([repo.name, repo.default_branch, license, repo.pushed_at].join('\t'));
    // Infrastructure repositories (.github, .schemas, the website and editor repositories) are not vehicles.
    if (repo.name.startsWith('.') || repo.name.includes('.obdb.community') || repo.name === 'obdb.community' || repo.name === 'vscode-obdb') continue;
    if (license !== 'CC-BY-SA-4.0') {
      skipped++;
      continue;
    }
    if (saved >= limit) continue;
    try {
      const text = await get(`https://raw.githubusercontent.com/OBDb/${repo.name}/${repo.default_branch}/signalsets/v3/default.json`);
      fs.writeFileSync(path.join(outDir, `${repo.name}.json`), text);
      saved++;
    } catch (error) {
      console.error(`skipped ${repo.name}: ${error.message}`);
      skipped++;
    }
  }
  fs.writeFileSync(path.join(outDir, 'repos.tsv'), lines.join('\n') + '\n');
  console.log(`${repos.length} repositories listed, ${saved} signal sets saved, ${skipped} skipped. Folder: ${outDir}`);
})().catch((error) => {
  console.error(error.message);
  process.exit(1);
});
