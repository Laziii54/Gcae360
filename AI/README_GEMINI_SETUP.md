# Gemini / AI setup

The MAUI app never asks users for an AI API key or custom AI endpoint. The app calls only the configured CarScanner.Api HTTPS backend.

Server environment/configuration:
- GEMINI_API_KEY
- GCS_ANDROID_PACKAGE_NAME
- GCS_GOOGLE_SERVICE_ACCOUNT_JSON (for Google Play subscription verification)
- GCS_APPLE_ISSUER_ID
- GCS_APPLE_KEY_ID
- GCS_APPLE_PRIVATE_KEY_P8
- GCS_APPLE_ENVIRONMENT=production or sandbox

The backend defaults to Gemini 3.6 Flash. Google currently lists Gemini 3.6 Flash as a stable production model.

The app deliberately does not embed the Gemini secret in the APK.


## AI key placement

Do not put the Gemini key in the MAUI app. Put it on the CarScanner.Api server only.
For local development, edit `Backend/CarScanner.Api/appsettings.json` and set:

```json
{
  "Gemini": {
    "ApiKey": "YOUR_GEMINI_KEY"
  }
}
```

For production hosting, prefer the `GEMINI_API_KEY` environment variable so the secret is not committed to source control.

The mobile app uses only `Services/AIConfig.cs` for the public HTTPS backend URL.
