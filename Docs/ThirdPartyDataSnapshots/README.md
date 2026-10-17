# Quarantined third-party data — safe copy

This folder is **not shipped with the app** (it lives outside `Resources/Raw/`, the only
directory `CarScanner.csproj`'s `MauiAsset Include="Resources\Raw\**"` picks up). It exists
so that two sources removed from the bundled `DtcCatalog.sqlite.br` are preserved rather than
deleted, in case a confirmed-compliant way to use them is found later.

## What's here

`AutoDiag2_PSA_safe_copy.sqlite.br` — every row that was imported from the two sources below,
copied out of the working catalog before those rows were deleted from it. Brotli-compressed the
same way the shipped `Resources/Raw/DtcCatalog.sqlite.br` is (decompress with a plain
`BrotliStream(CompressionMode.Decompress)`, or `brotli -d` on the command line). Tables:
`source_catalogs` (the original metadata rows, ids 2 and 4), `dtc_definitions`,
`ecu_addresses`, `build_exclusions`, `source_snapshots`.

## Why each source was pulled, and what would need to be true to bring it back

### AutoDiag2/database (22,172 `dtc_definitions` rows, 6,661 `build_exclusions` rows)

- Repo: https://github.com/autodiag2/database, snapshot revision `11e73fd6d105a071fa472f426d23225344921a19`.
- Data license: **ODbL-1.0** (database) + **DbCL-1.0** (contents). Code (the Python import
  tooling): GPL-3.0-or-later — irrelevant here since no code was imported, only data.
- Imported from the repo's per-code `data-src/**/*.yml` files (one YAML file per DTC: code,
  manufacturer/ECU, English definition, protocol/standard tags, evidence links).
- **The problem**: ODbL §4.2 ("Notices") and §4.4 ("Share alike") apply once a Substantial
  part of the Contents is Publicly Conveyed. Bundling the Database in a closed-source app
  package is a Conveyance. DbCL doesn't escape this — DbCL §2.3 says explicitly that it "does
  not cover any Database Rights... as part of the Database. Please see the ODbL." So even
  though the raw facts (a 5-character code and a short definition) are individually weak
  copyright subject matter, the sui generis EU-style Database Right that ODbL licenses can
  still apply to a Substantial, systematic extraction like this one — and that extraction was
  Conveyed to every user who installed the app.
- Section 4.3 ("Produced Work") notice-only exception does **not** help here: it covers
  showing a user one looked-up fact at runtime, not shipping a copy of the dataset inside the
  app package itself (which is what `Resources/Raw/DtcCatalog.sqlite.br` did).
- **What would allow re-inclusion**: either (a) ship this slice of the catalog as a
  separately-identified, ODbL-licensed export — with the license text, a URI notice, and
  anyone receiving the app able to get an equivalent copy under ODbL — or (b) get a
  qualified legal opinion that the specific facts involved (bare DTC code + 3–10 word
  description) fall outside Database Right / DbCL's scope entirely. Neither has been done.

### ludwig-v/arduino-psa-diag (40,840 `dtc_definitions` rows, 515 `ecu_addresses` rows)

- Repo: https://github.com/ludwig-v/arduino-psa-diag, user-provided snapshot
  (`arduino-psa-diag-master`).
- License: **GPL-3.0**, repository-wide (GitHub's own "GPL-3.0 license" badge; no
  file-level license override found anywhere in the repo).
- Imported from:
  - `dtc/*.md` — hundreds of Markdown files, one per PSA/Stellantis ECU (e.g. `AAS_MMC.md`,
    `ABS.md`, `BSI.md`, `EDC17C10.md`...), each a table of `(DTC code, short English fault
    description)` for that ECU.
  - `ECU_LIST.md` — `CAN_EMIT_ID:CAN_RECV_ID` request/response address pairs per ECU.
  - Nothing was imported from the actual Arduino `.ino` sketches (`arduino-psa-diag/`,
    `arduino-psa-diag-low-speed/`, `actuators/`, `libraries/`, `zones/`) — those implement the
    UDS/KWP unlock/coding/download protocol itself and were never used as a data source here.
- **The problem**: GPL-3.0 licenses "the Program" as the copyright holder chose to release it.
  Unlike ODbL, it draws no distinction between "the database" and "the contents" — there is no
  separate facts-only carve-out. The repository was released as one GPL-3.0 work, including
  its documentation files, with no evidence the author intended `dtc/*.md`/`ECU_LIST.md` to
  carry different terms. Bundling that content into OBD360's closed-source catalog, without
  releasing the corresponding program under GPL-3.0, is not confirmed compliant.
- **What would allow re-inclusion**: a qualified legal opinion either that (a) a bare
  `(ECU model, DTC code, 3–8 word description)` triple is a fact not protected by copyright
  independent of the author's chosen license (thin-copyright / facts doctrine), so the GPL
  terms don't reach it, or (b) the specific files qualify for separate treatment some other
  way. Neither has been confirmed.

## Restoring from this safe copy

If either determination above is resolved, re-importing is a straight copy — no need to
re-download or re-parse the original repositories:

```sql
-- decompress AutoDiag2_PSA_safe_copy.sqlite.br to AutoDiag2_PSA_safe_copy.sqlite first
ATTACH DATABASE 'AutoDiag2_PSA_safe_copy.sqlite' AS safe;
INSERT INTO main.dtc_definitions SELECT * FROM safe.dtc_definitions WHERE source_catalog_id = 2; -- or 4
INSERT INTO main.ecu_addresses SELECT * FROM safe.ecu_addresses WHERE source_catalog_id = 4;
INSERT INTO main.build_exclusions SELECT * FROM safe.build_exclusions WHERE source_catalog_id = 2;
```

Then update `source_catalogs.notes` for that id to remove the `QUARANTINED` marker, and bump
`DtcCatalogService.CatalogRevision` so devices re-extract the updated bundle.
