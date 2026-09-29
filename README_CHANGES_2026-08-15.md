# Georgian Car Scanner — requested changes

Implemented in this build:

1. OBD Bluetooth scan deduplication: generic OBD/ELM-style adapters are collapsed to one representative device (strongest RSSI), avoiding multiple duplicate OBD2 entries.
2. Settings: Theme (Dark/Light/System) and Language (ქართული/English) are persisted with MAUI Preferences and applied live. The Settings page labels Settings/Theme/Language stay in English as requested.
3. New Contact page in Settings. Messages are addressed to `zukalazashvili1999@gmail.com` through the user's mail composer.
4. App language defaults to Georgian and persists between launches. Static UI strings are translated for both Georgian and English; AI is instructed to answer in the selected language. User preferences persist locally.
5. Local notification reminder: after a successful diagnostic/full scan, a new reminder is scheduled for 14 days later. Notification can be disabled from Settings.
6. PRO price changed to 3.99 GEL/month and product ID changed to `com.georgiancarscanner.pro.monthly`. Billing is wired to a recurring store subscription API. Restore purchase is included.
7. First launch asks for the user's name and stores it persistently. AI prompts include the name so the assistant can address the user personally.
8. DTC clearing: Diagnostics now has a large dedicated `CLEAR DTC` button and Service keeps the dedicated clear button so it is visible on phone layouts.

## Important release setup

- Google Play Console / App Store Connect must contain the monthly subscription with the exact product ID `com.georgiancarscanner.pro.monthly`. Store-side localized price should be configured as 3.99 GEL per month; the app cannot force the store's displayed local price by itself.
- AI still requires a deployed backend endpoint and server-side provider key. The mobile app intentionally does not ship a provider secret. The included backend exposes `/api/ai/diagnose` and expects `GCS_AI_PROVIDER_URL` and `GCS_AI_PROVIDER_KEY` on the server.
- The project was modified without a local .NET SDK in this environment, so the final ZIP is source-cleaned but should be restored/built in Visual Studio with NuGet restore.
