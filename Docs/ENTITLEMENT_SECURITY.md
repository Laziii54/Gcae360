# PRO entitlement security — signed tokens

**Status:** implemented in this working tree. Nothing has been deployed and no
App Store Connect, Azure or Apple configuration has been changed.

---

## 1. What was wrong

PRO access lived entirely on the device as two booleans in an unencrypted
SQLite file (`CarScanner.db3`, table `ProEntitlement`) plus a preferences flag
for VIN:

```
IsPro = 1, VerifiedByServer = 1, ExpiresAt = <future>
```

`ProService` read those directly, so on a rooted phone — or with an edited
database or preferences file — every locally gated feature (module scan, ABS
diagnostics, coding, AI, fuel, hybrid, reports) could be unlocked permanently,
online or offline, signed in or not. The server was only consulted for AI and
VIN, which stayed safe; the Bluetooth features run entirely on the phone and had
no protection beyond that local flag.

## 2. What replaced it

PRO is now granted **only** by a cryptographically signed entitlement token that
the backend issues and the app verifies locally.

```
Backend                                   App
───────                                   ───
private key (never leaves the server)     public key (embedded)
        │                                        │
   signs JWS ES256  ──── HTTPS ────►  verifies signature, exp, iss, aud
   hasObd / hasVin claims                     │
                                       ProService (token-only state)
                                              │
                                       ProGate / route guard / commands
```

- Algorithm: **ES256** (ECDSA P-256 + SHA-256), JWS compact serialization.
- The app rejects: bad signature, unknown `kid`, any `alg` other than `ES256`
  (so `alg: none` and HMAC-confusion fail), wrong `iss`/`aud`, expired or
  not-yet-valid tokens, malformed tokens, and any claim it cannot read.
- Nobody but the backend can mint a token, so editing the local database or the
  preferences file no longer unlocks anything.

### Claims

| Claim | Meaning |
|---|---|
| `iss` | `obd360-api` |
| `aud` | `obd360-app` |
| `sub` | account id, or `anon:<id>` for an accountless Apple purchase |
| `hasObd` | OBD PRO (module scan, coding, AI, fuel, hybrid, reports) |
| `hasVin` | VIN PRO |
| `tier` | `none` / `obd` / `vin` / `full` (informational) |
| `iat`, `nbf`, `exp` | issued / not-before (−1 min skew) / expiry |
| `jti` | unique token id |
| `dev` | installation id, informational only — never a security decision |
| `kid` | key id, in the JOSE header for rotation |

Adding a future entitlement means adding one claim and one branch in
`ProService`; nothing else changes.

### Lifetime and offline use

- Default **7 days** (`Entitlements:Signing:LifetimeDays`, clamped 1–30).
- A valid token keeps working with no connection at all — the features are
  local Bluetooth operations, and blocking them offline would break the product.
- When the token expires the app grants nothing. The PRO page shows
  *"Your PRO verification has expired. Connect to the internet…"* instead of
  pretending the user never subscribed.
- On any load with network available, tokens are refreshed (throttled to once
  per 5 minutes, single-flight). A lapsed or refunded subscription gets a *valid*
  token that says `hasObd=false`, which is how revocation reaches the device
  without a boolean ever being trusted.

## 3. Who gets a token

| User | Endpoint | Notes |
|---|---|---|
| Signed-in account (website purchase, account-linked store purchase, admin/promo plan) | `GET /api/entitlements/{userId}` → `entitlementToken` | `sub` = account id; refused unless the bearer token matches that account |
| Accountless iOS purchase | `POST /api/store/apple/anonymous/verify` → `entitlementToken` | `sub` = `anon:` owner |
| Accountless refresh (signed out) | `POST /api/store/apple/anonymous/refresh` → `entitlementToken` | works without any OBD360 account; the Apple owner token is the credential |
| Accountless read | `GET /api/store/apple/anonymous` → `entitlementToken` | |

Android website purchases are unchanged: sign in, buy on obd360.ge, come back,
and the account endpoint now returns a signed token.

## 4. Azure App Service configuration

Add these settings (Configuration → Application settings). Use a Key Vault
reference for the key rather than pasting it:

| Setting | Value |
|---|---|
| `Entitlements__Signing__PrivateKeyP8` | the ECDSA P-256 private key, PKCS#8 PEM (Key Vault reference recommended) |
| `Entitlements__Signing__KeyId` | `k1` (must match `EntitlementTokenKeys.CurrentKeyId`) |
| `Entitlements__Signing__LifetimeDays` | `7` |
| `Entitlements__Signing__Issuer` / `__Audience` | leave default (`obd360-api` / `obd360-app`) |

Environment-variable equivalents also work: `GCS_ENTITLEMENT_PRIVATE_KEY_P8`,
`GCS_ENTITLEMENT_KEY_ID`, `GCS_ENTITLEMENT_LIFETIME_DAYS`.

> **Deploy order matters.** If the backend runs without a signing key it issues
> no token, and the app (correctly) keeps PRO locked. Configure the key in the
> same deployment slot change as the new backend build, then verify:
> `GET /api/entitlements/key` must answer `200` with `kid`, `issuer`,
> `audience`, `lifetimeDays` and `publicKey` — never `503 {"configured":false}`.

A local development key pair can be generated with:

```bash
openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out entitlement-signing.p8
openssl ec -in entitlement-signing.p8 -pubout -outform DER | base64 -w0
```

The second command prints the value that goes into
`Services/Entitlements/EntitlementTokenKeys.cs`. The private key must only ever
exist in Azure (or in the gitignored `Backend/CarScanner.Api/secrets/` folder
locally).

## 5. Key rotation

1. Generate a new key pair.
2. Set `Entitlements__Signing__PrivateKeyP8` / `__KeyId` (`k2`) on the backend.
3. Add the `k2` public key to `EntitlementTokenKeys.Trusted` **while keeping
   `k1`**, and rebuild the app.
4. After one token lifetime (default 7 days) the old tokens are gone — delete
   `k1` and rebuild again.

The verifier accepts any trusted `kid`, so old and new tokens coexist during the
window. This is covered by `EntitlementTokenTests.AKeyRotatedIn_VerifiesWhileTheOldOneIsStillPublished`.

## 6. What is protected, and what stays free

**PRO (token required):** AI diagnostics, fuel/energy, diagnostic reports,
module scan (ABS/SRS/…), coding, and the PRO entry points inside module live
data, ECU identifiers, hybrid health and pre-purchase inspection.

**Free by product decision — never gate these:**
- **Original mileage discovery** (`MileagePage` is deliberately absent from
  `AppShell.IsProFeatureRoute`)
- **Service procedures** (`ServicePage`)
- **Oil life reset, 100 % and 50 %** (only the existing vehicle-compatibility
  rules apply)

`FeatureEntitlementPolicyTests` fails the build if any of those three acquires a
PRO check or is added to the route guard.

## 7. Migration

One-time, on first launch of this build:
`AppDatabase.ClearLegacyEntitlementTrustAsync()` zeroes every trusted column in
the `ProEntitlement` row and records a marker setting; the old
`pro.hasVin` / `pro.vinExpires` preferences are removed.

The row itself is kept (the data export and diagnostics read it) but it is no
longer consulted for authorization — a stale or tampered value is inert.

## 8. Known limitations

- **A repackaged app cannot be stopped.** Anyone who patches the binary can
  remove the check. This design raises the bar to "rebuild the app", which is the
  realistic ceiling for a feature that must run locally over Bluetooth.
- **The embedded public key means the app trusts exactly one issuer.** If the
  backend key is lost and no rotation was prepared, existing tokens cannot be
  re-signed; users must refresh after the new key is published *and* the app is
  updated with its public half.
- **`dev` is informational.** Device binding is deliberately not a security
  boundary — the Apple/account credential is.
- Tokens are bearer credentials for 7 days: anyone who extracts one from a
  compromised device gets PRO until it expires. Shortening
  `LifetimeDays` reduces that window at the cost of more offline lockouts.
