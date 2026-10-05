using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CarScanner.Api.Services;

public sealed class StoreVerificationService
{
    private const string AndroidPackageName =
        "com.gcar360.app"; // must equal ApplicationId in CarScanner.csproj

    private readonly IHttpClientFactory _clients;
    private readonly IConfiguration _config;

    public StoreVerificationService(
        IHttpClientFactory clients,
        IConfiguration config)
    {
        _clients = clients;
        _config = config;
    }

    public async Task<VerifiedPurchase?> VerifyAsync(
        string store,
        string productId,
        string transactionId,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(productId) ||
            string.IsNullOrWhiteSpace(transactionId))
        {
            return null;
        }

        if (store.Equals(
                "google",
                StringComparison.OrdinalIgnoreCase))
        {
            return await VerifyGoogleAsync(
                productId,
                transactionId,
                token);
        }

        if (store.Equals(
                "apple",
                StringComparison.OrdinalIgnoreCase))
        {
            return await VerifyAppleAsync(
                productId,
                transactionId,
                token);
        }

        return null;
    }

    // ============================================================
    // GOOGLE PLAY
    // ============================================================

    private async Task<VerifiedPurchase?> VerifyGoogleAsync(
        string productId,
        string purchaseToken,
        CancellationToken ct)
    {
        if (!ProductCatalog.IsKnown(productId))
            return null;

        var serviceJson =
            _config["Stores:Google:ServiceAccountJson"]
            ?? Environment.GetEnvironmentVariable(
                "GCS_GOOGLE_SERVICE_ACCOUNT_JSON");

        var packageName =
            _config["Stores:Google:PackageName"]
            ?? Environment.GetEnvironmentVariable(
                "GCS_ANDROID_PACKAGE_NAME")
            ?? AndroidPackageName;

        // The package name must be exactly the one shown in Play Console.
        // It is read from the server settings so it can match the app's
        // ApplicationId without a code change.
        if (string.IsNullOrWhiteSpace(serviceJson) ||
            string.IsNullOrWhiteSpace(packageName))
        {
            return null;
        }

        packageName = packageName.Trim();

        try
        {
            // ----------------------------------------------------
            // Read Google service account
            // ----------------------------------------------------

            using var creds =
                JsonDocument.Parse(serviceJson);

            var root =
                creds.RootElement;

            var email =
                root.GetProperty(
                    "client_email")
                    .GetString();

            var privateKey =
                (
                    root.GetProperty(
                        "private_key")
                        .GetString()
                    ?? string.Empty
                ).Replace(
                    "\\n",
                    "\n");

            if (string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(privateKey))
            {
                return null;
            }

            // ----------------------------------------------------
            // Create Google OAuth JWT
            // ----------------------------------------------------

            var now =
                DateTimeOffset.UtcNow
                    .ToUnixTimeSeconds();

            var header =
                Base64Url(
                    JsonSerializer.Serialize(
                        new
                        {
                            alg = "RS256",
                            typ = "JWT"
                        }));

            var claims =
                Base64Url(
                    JsonSerializer.Serialize(
                        new
                        {
                            iss = email,
                            scope =
                                "https://www.googleapis.com/auth/androidpublisher",
                            aud =
                                "https://oauth2.googleapis.com/token",
                            iat = now,
                            exp = now + 3600
                        }));

            var unsigned =
                header + "." + claims;

            using var rsa =
                RSA.Create();

            rsa.ImportFromPem(
                privateKey);

            var signature =
                rsa.SignData(
                    Encoding.UTF8.GetBytes(
                        unsigned),
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

            var assertion =
                unsigned + "." +
                Base64Url(signature);

            // ----------------------------------------------------
            // Exchange JWT for access token
            // ----------------------------------------------------

            var client =
                _clients.CreateClient();

            client.Timeout =
                TimeSpan.FromSeconds(30);

            using var tokenReq =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    "https://oauth2.googleapis.com/token");

            tokenReq.Content =
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["grant_type"] =
                            "urn:ietf:params:oauth:grant-type:jwt-bearer",

                        ["assertion"] =
                            assertion
                    });

            using var tokenRes =
                await client.SendAsync(
                    tokenReq,
                    ct);

            if (!tokenRes.IsSuccessStatusCode)
                return null;

            var tokenRaw =
                await tokenRes.Content
                    .ReadAsStringAsync(ct);

            using var tokenDoc =
                JsonDocument.Parse(tokenRaw);

            var accessToken =
                tokenDoc.RootElement
                    .GetProperty("access_token")
                    .GetString();

            if (string.IsNullOrWhiteSpace(accessToken))
                return null;

            if (ProductCatalog.IsConsumable(productId))
            {
                return await VerifyGoogleProductAsync(
                    client,
                    accessToken,
                    packageName,
                    productId,
                    purchaseToken,
                    ct);
            }

            // ----------------------------------------------------
            // Get subscription from Google Play
            // ----------------------------------------------------

            var verifyUri =
                "https://androidpublisher.googleapis.com/" +
                "androidpublisher/v3/applications/" +
                Uri.EscapeDataString(packageName) +
                "/purchases/subscriptionsv2/tokens/" +
                Uri.EscapeDataString(purchaseToken);

            using var verifyRequest =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    verifyUri);

            verifyRequest.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    accessToken);

            using var verifyResponse =
                await client.SendAsync(
                    verifyRequest,
                    ct);

            if (!verifyResponse.IsSuccessStatusCode)
                return null;

            var verifyRaw =
                await verifyResponse.Content
                    .ReadAsStringAsync(ct);

            using var subscriptionDoc =
                JsonDocument.Parse(verifyRaw);

            var subscription =
                subscriptionDoc.RootElement;

            // ----------------------------------------------------
            // Subscription state
            // ----------------------------------------------------

            if (!subscription.TryGetProperty(
                    "subscriptionState",
                    out var stateElement))
            {
                return null;
            }

            var state =
                stateElement.GetString();

            var validState =
                state is
                    "SUBSCRIPTION_STATE_ACTIVE"
                    or
                    "SUBSCRIPTION_STATE_IN_GRACE_PERIOD";

            if (!validState)
                return null;

            // ----------------------------------------------------
            // Line items
            // ----------------------------------------------------

            if (!subscription.TryGetProperty(
                    "lineItems",
                    out var lineItems) ||
                lineItems.ValueKind !=
                    JsonValueKind.Array ||
                lineItems.GetArrayLength() == 0)
            {
                return null;
            }

            DateTimeOffset? selectedExpiry =
                null;

            string? verifiedProductId =
                null;

            foreach (var line in lineItems.EnumerateArray())
            {
                if (!line.TryGetProperty(
                        "productId",
                        out var productElement))
                {
                    continue;
                }

                var currentProductId =
                    productElement.GetString();

                if (!string.Equals(
                        currentProductId,
                        productId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (!line.TryGetProperty(
                        "expiryTime",
                        out var expiryElement))
                {
                    continue;
                }

                var expiryText =
                    expiryElement.GetString();

                if (!DateTimeOffset.TryParse(
                        expiryText,
                        out var expiry))
                {
                    continue;
                }

                verifiedProductId =
                    currentProductId;

                if (!selectedExpiry.HasValue ||
                    expiry > selectedExpiry.Value)
                {
                    selectedExpiry =
                        expiry;
                }
            }

            if (verifiedProductId is null ||
                !selectedExpiry.HasValue)
            {
                return null;
            }

            if (selectedExpiry.Value <=
                DateTimeOffset.UtcNow)
            {
                return null;
            }

            // ----------------------------------------------------
            // Acknowledgement
            //
            // A new subscription purchase must be acknowledged.
            // Renewals do not need another acknowledgement.
            // ----------------------------------------------------

            var acknowledgementState =
                subscription.TryGetProperty(
                    "acknowledgementState",
                    out var acknowledgementElement)
                    ? acknowledgementElement.GetString()
                    : null;

            var needsAcknowledgement =
                string.Equals(
                    acknowledgementState,
                    "ACKNOWLEDGEMENT_STATE_PENDING",
                    StringComparison.Ordinal);

            if (needsAcknowledgement)
            {
                var acknowledged =
                    await AcknowledgeGoogleSubscriptionAsync(
                        client,
                        accessToken,
                        packageName,
                        productId,
                        purchaseToken,
                        ct);

                if (!acknowledged)
                {
                    return null;
                }
            }

            return new VerifiedPurchase(
                verifiedProductId,
                selectedExpiry.Value,
                purchaseToken);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks a one-time VIN purchase and consumes it so the same pack
    /// can be bought again. Double crediting is prevented by the
    /// server-side purchase claim, not by the consume call.
    /// </summary>
    private static async Task<VerifiedPurchase?> VerifyGoogleProductAsync(
        HttpClient client,
        string accessToken,
        string packageName,
        string productId,
        string purchaseToken,
        CancellationToken ct)
    {
        var baseUri =
            "https://androidpublisher.googleapis.com/" +
            "androidpublisher/v3/applications/" +
            Uri.EscapeDataString(packageName) +
            "/purchases/products/" +
            Uri.EscapeDataString(productId) +
            "/tokens/" +
            Uri.EscapeDataString(purchaseToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, baseUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        // purchaseState: 0 purchased, 1 cancelled, 2 pending.
        if (!root.TryGetProperty("purchaseState", out var state) ||
            state.ValueKind != JsonValueKind.Number ||
            state.GetInt32() != 0)
        {
            return null;
        }

        if (root.TryGetProperty("productId", out var pid) &&
            pid.ValueKind == JsonValueKind.String &&
            !string.Equals(pid.GetString(), productId, StringComparison.Ordinal))
        {
            return null;
        }

        var consumed =
            root.TryGetProperty("consumptionState", out var consumption) &&
            consumption.ValueKind == JsonValueKind.Number &&
            consumption.GetInt32() == 1;

        if (!consumed)
        {
            try
            {
                using var consume = new HttpRequestMessage(HttpMethod.Post, baseUri + ":consume");
                consume.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                consume.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                using var _ = await client.SendAsync(consume, ct);
            }
            catch
            {
                // The app consumes the purchase as a fallback.
            }
        }

        return new VerifiedPurchase(
            productId,
            DateTimeOffset.UtcNow,
            purchaseToken);
    }

    private static async Task<bool>
        AcknowledgeGoogleSubscriptionAsync(
            HttpClient client,
            string accessToken,
            string packageName,
            string productId,
            string purchaseToken,
            CancellationToken ct)
    {
        try
        {
            var acknowledgeUri =
                "https://androidpublisher.googleapis.com/" +
                "androidpublisher/v3/applications/" +
                Uri.EscapeDataString(packageName) +
                "/purchases/subscriptions/" +
                Uri.EscapeDataString(productId) +
                "/tokens/" +
                Uri.EscapeDataString(purchaseToken) +
                ":acknowledge";

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    acknowledgeUri);

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    accessToken);

            request.Content =
                new StringContent(
                    "{}",
                    Encoding.UTF8,
                    "application/json");

            using var response =
                await client.SendAsync(
                    request,
                    ct);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ============================================================
    // APP STORE / APPLE
    // ============================================================

    private async Task<VerifiedPurchase?> VerifyAppleAsync(
        string productId,
        string transactionId,
        CancellationToken ct)
    {
        if (!ProductCatalog.IsKnown(productId))
            return null;

        var issuer =
            _config["Stores:Apple:IssuerId"]
            ?? Environment.GetEnvironmentVariable(
                "GCS_APPLE_ISSUER_ID");

        var keyId =
            _config["Stores:Apple:KeyId"]
            ?? Environment.GetEnvironmentVariable(
                "GCS_APPLE_KEY_ID");

        var p8 =
            _config["Stores:Apple:PrivateKeyP8"]
            ?? Environment.GetEnvironmentVariable(
                "GCS_APPLE_PRIVATE_KEY_P8");

        var environment =
            (
                _config["Stores:Apple:Environment"]
                ?? Environment.GetEnvironmentVariable(
                    "GCS_APPLE_ENVIRONMENT")
                ?? "auto"
            ).ToLowerInvariant();

        var expectedBundleId =
            _config["Stores:Apple:BundleId"]
            ?? Environment.GetEnvironmentVariable(
                "GCS_APPLE_BUNDLE_ID")
            ?? "com.gcar360.app";

        if (string.IsNullOrWhiteSpace(issuer) ||
            string.IsNullOrWhiteSpace(keyId) ||
            string.IsNullOrWhiteSpace(p8))
        {
            return null;
        }

        p8 =
            p8.Replace(
                "\\n",
                "\n");

        try
        {
            var now =
                DateTimeOffset.UtcNow
                    .ToUnixTimeSeconds();

            var header =
                Base64Url(
                    JsonSerializer.Serialize(
                        new
                        {
                            alg = "ES256",
                            kid = keyId,
                            typ = "JWT"
                        }));

            var claims =
                Base64Url(
                    JsonSerializer.Serialize(
                        new
                        {
                            iss = issuer,
                            aud = "appstoreconnect-v1",
                            bid = expectedBundleId,
                            iat = now,
                            exp = now + 300
                        }));

            var unsigned =
                header + "." + claims;

            using var ecdsa =
                ECDsa.Create();

            ecdsa.ImportFromPem(p8);

            var signature =
                ecdsa.SignData(
                    Encoding.UTF8.GetBytes(
                        unsigned),
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat
                        .IeeeP1363FixedFieldConcatenation);

            var jwt =
                unsigned + "." +
                Base64Url(signature);

            string[] hosts = environment switch
            {
                "sandbox" =>
                ["https://api.storekit-sandbox.apple.com"],
                "production" =>
                ["https://api.storekit.apple.com"],
                _ =>
                [
                    "https://api.storekit.apple.com",
                    "https://api.storekit-sandbox.apple.com"
                ]
            };

            var client =
                _clients.CreateClient();

            client.Timeout =
                TimeSpan.FromSeconds(30);

            foreach (var host in hosts)
            {
                using var req =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        $"{host}/inApps/v1/transactions/" +
                        $"{Uri.EscapeDataString(transactionId)}");

                req.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        jwt);

                using var res =
                    await client.SendAsync(
                        req,
                        ct);

                if (!res.IsSuccessStatusCode)
                    continue;

                var raw =
                    await res.Content
                        .ReadAsStringAsync(ct);

                using var doc =
                    JsonDocument.Parse(raw);

                if (!doc.RootElement.TryGetProperty(
                        "signedTransactionInfo",
                        out var info))
                {
                    continue;
                }

                var payload =
                    DecodeJwtPayload(
                        info.GetString() ?? string.Empty);

                if (payload is not JsonElement
                    payloadElement)
                {
                    continue;
                }

                if (!payloadElement.TryGetProperty(
                        "productId",
                        out var pid) ||
                    !string.Equals(
                        pid.GetString(),
                        productId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (payloadElement.TryGetProperty(
                        "bundleId",
                        out var bundleId) &&
                    !string.Equals(
                        bundleId.GetString(),
                        expectedBundleId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (payloadElement.TryGetProperty(
                        "revocationDate",
                        out var revoked) &&
                    revoked.ValueKind != JsonValueKind.Null)
                {
                    continue;
                }

                if (ProductCatalog.IsConsumable(productId))
                {
                    return new VerifiedPurchase(
                        productId,
                        DateTimeOffset.UtcNow,
                        transactionId);
                }

                if (!payloadElement.TryGetProperty(
                        "expiresDate",
                        out var exp) ||
                    !exp.TryGetInt64(
                        out var expMs))
                {
                    continue;
                }

                var expires =
                    DateTimeOffset
                        .FromUnixTimeMilliseconds(
                            expMs);

                if (expires <=
                    DateTimeOffset.UtcNow)
                {
                    continue;
                }

                return new VerifiedPurchase(
                    productId,
                    expires,
                    transactionId);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    // ============================================================
    // HELPERS
    // ============================================================

    private static JsonElement? DecodeJwtPayload(
        string jwt)
    {
        try
        {
            var parts =
                jwt.Split('.');

            if (parts.Length != 3)
                return null;

            var normalized =
                Pad(parts[1])
                    .Replace('-', '+')
                    .Replace('_', '/');

            var bytes =
                Convert.FromBase64String(
                    normalized);

            using var doc =
                JsonDocument.Parse(bytes);

            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static string Base64Url(
        object value)
    {
        var bytes =
            Encoding.UTF8.GetBytes(
                value is string text
                    ? text
                    : JsonSerializer.Serialize(
                        value));

        return Base64Url(bytes);
    }

    private static string Base64Url(
        byte[] bytes)
    {
        return Convert
            .ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string Pad(
        string value)
    {
        return value +
               new string(
                   '=',
                   (4 - value.Length % 4) % 4);
    }
}

public sealed record VerifiedPurchase(
    string ProductId,
    DateTimeOffset ExpiresAt,
    string TransactionId);
