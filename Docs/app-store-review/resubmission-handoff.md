# Resubmission hand-off — what is done, what is left

Build **68 (1.1.2)** · branch `feat/accountless-iap-app-review`
Covers the two rejections on build 56: **2.1** (demo video) and **5.1.1(v)** (registration before IAP).

---

## 1. Done in the codebase

| Item | Where | State |
|---|---|---|
| No login anywhere in the purchase path | `ProViewModel.CanBuyInApp` → `BuyPlan` before any sign-in check | ✅ |
| Apple purchase with no account | `PurchaseService` accountless owner + `/api/store/apple/anonymous/*` | ✅ |
| Restore Purchases with no account | `PurchaseService.RestoreDetailedAsync` → anonymous verify/refresh | ✅ |
| Subscription belongs to the Apple Account (multi-device, no revocation) | `StoreEntitlementLinks.SubscriptionKey` uses `originalTransactionId` | ✅ |
| PRO only from a server-signed token | `EntitlementTokenVerifier` + `ProService` | ✅ |
| Three monthly plans, no yearly anywhere | `ProService`, `ProPage.xaml`, ASC yearly products removed | ✅ |
| Localized StoreKit prices, no fake discounts | `iOSBillingService` + `ProPage.xaml` | ✅ |
| The app opens on the main screen (no chooser) | `AppShell.xaml` | ✅ |
| Reply letter for App Review | `apple-review-reply.md` | ✅ needs the video link |
| Recording checklist | `physical-device-recording-checklist.md` | ✅ |

## 2. Your part (cannot be done from here)

### 2.1 — the video link (only you)

You said the video is recorded. Two places need it:

1. **App Store Connect → App Information → App Review Information → Notes** (or the demo-video
   field, if your ASC shows one): paste the shareable link.
2. In `apple-review-reply.md` replace `[DEMO VIDEO LINK — paste the shareable link here]` with the
   same link (so the written reply and the ASC field agree).

The link must be playable by the reviewer without a password. YouTube unlisted, Vimeo with a
password written in the notes, or a direct HTTPS link all work; a Google Drive link sometimes fails
for reviewers, so prefer a real video host.

### 2.2 — App Store Connect version record

The rejected version is **1.0.0 (56)**. The project now builds as **1.1.2 (68)** for both platforms
(`CarScanner.csproj`: `ApplicationDisplayVersion` / `ApplicationVersion`).

In ASC: create a new iOS version **1.1.2** (or edit the existing version record and set 1.0.1 —
whichever you prefer, but the string must match the build you upload). Attach the three monthly
subscriptions to that version; they are already "Ready for Review". The yearly products are
"Removed" — leave them removed.

### 2.3 — the IPA (needs macOS)

Windows cannot produce a signed IPA. On a Mac, or on Codemagic:

```bash
git fetch origin
git checkout feat/accountless-iap-app-review
dotnet publish CarScanner.csproj -f net10.0-ios -c Release -p:BuildIpa=true
```

Then upload the `.ipa` with Transporter / Xcode, wait for "Processing" to finish in ASC, and attach
build **68** to version 1.1.2.

Codemagic is only worth paying for if you have no Mac available — the build itself is one command.

### 2.4 — Submit

1. Attach build 68 to version 1.1.2.
2. Paste the reply text from `apple-review-reply.md` into **Reply to App Review**.
3. Confirm the App Review Information still has the demo video link.
4. Submit for review.

## 3. Five-minute self-check before you submit

On a physical iPhone, in the sandbox:

| Check | Expected |
|---|---|
| Fresh install, launch | Main screen, no sign-in prompt |
| PRO screen | 3 monthly plans, Apple-localized prices, no yearly, no struck-through price |
| Tap a plan | Apple's purchase sheet immediately |
| Complete the purchase | PRO unlocks; no account screen appears |
| Delete app, reinstall, Restore Purchases | PRO restores without sign-in |
| Second device, same Apple Account, Restore | PRO restores; the first device keeps PRO |

If all six pass, the reviewer will see the same thing.

## 4. Known trade-offs (so nobody is surprised)

- VIN credit packs still require an account — they are a per-account balance, and the reply letter
  explains that explicitly. Subscriptions and Restore never do.
- An expired entitlement token (7 days) needs one online refresh; the PRO screen then says
  "connect to the internet to renew" instead of pretending the plan was never bought.
- The app cannot stop a repackaged binary; the token design stops *data* tampering (database /
  preferences), which is what the previous build allowed.
