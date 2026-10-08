using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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

    public Task<VerifiedPurchase?> VerifyAsync(
        string store,
        string productId,
        string transactionId,
        CancellationToken token) =>
        VerifyAsync(store, productId, transactionId, null, token);

    /// <summary>
    /// Verifies a purchase with the store. For Apple the product and expiry
    /// always come from Apple's answer (the client's productId is only a
    /// hint); <paramref name="originalTransactionId"/> is optional.
    /// </summary>
    public async Task<VerifiedPurchase?> VerifyAsync(
        string store,
        string? productId,
        string transactionId,
        string? originalTransactionId,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
            return null;

        if (store.Equals("google", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(productId))
                return null;
            return await VerifyGoogleAsync(productId, transactionId, token);
        }

        if (store.Equals("apple", StringComparison.OrdinalIgnoreCase))
            return await VerifyAppleAsync(productId, transactionId, originalTransactionId, token);

        return null;
    }

    // ============================================================
    // GOOGLE PLAY
    // ============================================================

    private sealed record GoogleAccess(HttpClient Client, string AccessToken, string PackageName);

    /// <summary>OAuth access token for the Play Developer API (service account).</summary>
    private async Task<GoogleAccess?> GetGoogleAccessAsync(CancellationToken ct)
    {
        var serviceJson =
            _config["Stores:Google:ServiceAccountJson"]
            ?? Environment.GetEnvironmentVariable("GCS_GOOGLE_SERVICE_ACCOUNT_JSON");

        // The package name must be exactly the one shown in Play Console.
        var packageName =
            _config["Stores:Google:PackageName"]
            ?? Environment.GetEnvironmentVariable("GCS_ANDROID_PACKAGE_NAME")
            ?? AndroidPackageName;

        if (string.IsNullOrWhiteSpace(serviceJson) || string.IsNullOrWhiteSpace(packageName))
            return null;

        using var creds = JsonDocument.Parse(serviceJson);
        var root = creds.RootElement;
        var email = root.GetProperty("client_email").GetString();
        var privateKey = (root.GetProperty("private_key").GetString() ?? string.Empty).Replace("\\n", "\n");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(privateKey))
            return null;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Base64Url(JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT" }));
        var claims = Base64Url(JsonSerializer.Serialize(new
        {
            iss = email,
            scope = "https://www.googleapis.com/auth/androidpublisher",
            aud = "https://oauth2.googleapis.com/token",
            iat = now,
            exp = now + 3600
        }));
        var unsigned = header + "." + claims;

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKey);
        var signature = rsa.SignData(Encoding.UTF8.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var assertion = unsigned + "." + Base64Url(signature);

        var client = _clients.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        using var tokenReq = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token");
        tokenReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = assertion
        });
        using var tokenRes = await client.SendAsync(tokenReq, ct);
        if (!tokenRes.IsSuccessStatusCode)
            return null;

        using var tokenDoc = JsonDocument.Parse(await tokenRes.Content.ReadAsStringAsync(ct));
        var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();
        return string.IsNullOrWhiteSpace(accessToken) ? null : new GoogleAccess(client, accessToken, packageName.Trim());
    }

    private sealed record GoogleSubscriptionInfo(
        string State, string? ProductId, DateTimeOffset? ExpiresAt, bool AcknowledgementPending);

    /// <summary>purchases.subscriptionsv2.get for a purchase token (null when Google does not know it).</summary>
    private static async Task<GoogleSubscriptionInfo?> QueryGoogleSubscriptionAsync(
        GoogleAccess google, string purchaseToken, string? preferredProductId, CancellationToken ct)
    {
        var verifyUri =
            "https://androidpublisher.googleapis.com/androidpublisher/v3/applications/" +
            Uri.EscapeDataString(google.PackageName) +
            "/purchases/subscriptionsv2/tokens/" +
            Uri.EscapeDataString(purchaseToken);

        using var verifyRequest = new HttpRequestMessage(HttpMethod.Get, verifyUri);
        verifyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", google.AccessToken);
        using var verifyResponse = await google.Client.SendAsync(verifyRequest, ct);
        if (!verifyResponse.IsSuccessStatusCode)
            return null;

        using var doc = JsonDocument.Parse(await verifyResponse.Content.ReadAsStringAsync(ct));
        var subscription = doc.RootElement;

        var state = subscription.TryGetProperty("subscriptionState", out var stateElement)
            ? stateElement.GetString() ?? string.Empty
            : string.Empty;

        DateTimeOffset? selectedExpiry = null;
        string? selectedProduct = null;
        if (subscription.TryGetProperty("lineItems", out var lineItems) && lineItems.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in lineItems.EnumerateArray())
            {
                if (!line.TryGetProperty("productId", out var productElement))
                    continue;
                var currentProductId = productElement.GetString();
                // The product always comes from Google; only our own subscriptions count.
                if (!ProductCatalog.IsSubscription(currentProductId))
                    continue;
                if (!line.TryGetProperty("expiryTime", out var expiryElement) ||
                    !DateTimeOffset.TryParse(expiryElement.GetString(), out var expiry))
                    continue;

                var preferred = string.Equals(currentProductId, preferredProductId, StringComparison.Ordinal);
                var selectedPreferred = string.Equals(selectedProduct, preferredProductId, StringComparison.Ordinal);
                if (selectedProduct is null ||
                    (preferred && !selectedPreferred) ||
                    (preferred == selectedPreferred && expiry > selectedExpiry))
                {
                    selectedProduct = currentProductId;
                    selectedExpiry = expiry;
                }
            }
        }

        var ackPending = subscription.TryGetProperty("acknowledgementState", out var ack) &&
                         string.Equals(ack.GetString(), "ACKNOWLEDGEMENT_STATE_PENDING", StringComparison.Ordinal);

        return new GoogleSubscriptionInfo(state, selectedProduct, selectedExpiry, ackPending);
    }

    /// <summary>
    /// Active, grace period, or cancelled-but-paid-until-expiry. A cancelled
    /// subscription stays usable until its expiryTime.
    /// </summary>
    private static bool GoogleGrantsAccess(GoogleSubscriptionInfo info) =>
        info.State is "SUBSCRIPTION_STATE_ACTIVE"
            or "SUBSCRIPTION_STATE_IN_GRACE_PERIOD"
            or "SUBSCRIPTION_STATE_CANCELED" &&
        info.ProductId is not null &&
        info.ExpiresAt is { } expires &&
        expires > DateTimeOffset.UtcNow;

    private async Task<VerifiedPurchase?> VerifyGoogleAsync(
        string productId,
        string purchaseToken,
        CancellationToken ct)
    {
        if (!ProductCatalog.IsKnown(productId))
            return null;

        try
        {
            var google = await GetGoogleAccessAsync(ct);
            if (google is null)
                return null;

            if (ProductCatalog.IsConsumable(productId))
            {
                return await VerifyGoogleProductAsync(
                    google.Client, google.AccessToken, google.PackageName, productId, purchaseToken, ct);
            }

            var info = await QueryGoogleSubscriptionAsync(google, purchaseToken, productId, ct);
            if (info is null || !GoogleGrantsAccess(info))
                return null;

            // A new subscription purchase must be acknowledged.
            // Renewals do not need another acknowledgement.
            if (info.AcknowledgementPending &&
                !await AcknowledgeGoogleSubscriptionAsync(
                    google.Client, google.AccessToken, google.PackageName, info.ProductId!, purchaseToken, ct))
            {
                return null;
            }

            return new VerifiedPurchase(info.ProductId!, info.ExpiresAt!.Value, purchaseToken);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Current state of a Google subscription, re-read from Google (RTDN).
    /// Null when Google could not be asked (configuration / network).
    /// </summary>
    public async Task<StoreSubscriptionState?> GetGoogleSubscriptionStateAsync(string purchaseToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(purchaseToken) || purchaseToken.Length > 8192)
            return null;
        try
        {
            var google = await GetGoogleAccessAsync(ct);
            if (google is null)
                return null;
            var info = await QueryGoogleSubscriptionAsync(google, purchaseToken, null, ct);
            if (info is null)
                return new StoreSubscriptionState(false, null, null, "NOT_FOUND", purchaseToken);

            var active = GoogleGrantsAccess(info);
            if (active && info.AcknowledgementPending)
                await AcknowledgeGoogleSubscriptionAsync(google.Client, google.AccessToken, google.PackageName, info.ProductId!, purchaseToken, ct);
            return new StoreSubscriptionState(active, info.ProductId, info.ExpiresAt, info.State, purchaseToken);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// True when Google lists the purchase token as voided (refund /
    /// chargeback) in the last 30 days. Null when Google could not be asked.
    /// </summary>
    public async Task<bool?> IsGoogleVoidedAsync(string purchaseToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(purchaseToken) || purchaseToken.Length > 8192)
            return null;
        try
        {
            var google = await GetGoogleAccessAsync(ct);
            if (google is null)
                return null;

            string? pageToken = null;
            for (var page = 0; page < 10; page++)
            {
                var uri = "https://androidpublisher.googleapis.com/androidpublisher/v3/applications/" +
                          Uri.EscapeDataString(google.PackageName) +
                          "/purchases/voidedpurchases?type=1&maxResults=1000" +
                          (pageToken is null ? "" : "&token=" + Uri.EscapeDataString(pageToken));
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", google.AccessToken);
                using var response = await google.Client.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                    return null;

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.TryGetProperty("voidedPurchases", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in list.EnumerateArray())
                    {
                        if (item.TryGetProperty("purchaseToken", out var t) &&
                            string.Equals(t.GetString(), purchaseToken, StringComparison.Ordinal))
                            return true;
                    }
                }

                pageToken = doc.RootElement.TryGetProperty("tokenPagination", out var pagination) &&
                            pagination.TryGetProperty("nextPageToken", out var next)
                    ? next.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(pageToken))
                    break;
            }

            return false;
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

            using var request = new HttpRequestMessage(HttpMethod.Post, acknowledgeUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ============================================================
    // APP STORE / APPLE (App Store Server API)
    // ============================================================

    private sealed record AppleApi(HttpClient Client, string Jwt, string[] Hosts, string BundleId);

    private AppleApi? CreateAppleApi()
    {
        var issuer = _config["Stores:Apple:IssuerId"] ?? Environment.GetEnvironmentVariable("GCS_APPLE_ISSUER_ID");
        var keyId = _config["Stores:Apple:KeyId"] ?? Environment.GetEnvironmentVariable("GCS_APPLE_KEY_ID");
        var p8 = _config["Stores:Apple:PrivateKeyP8"] ?? Environment.GetEnvironmentVariable("GCS_APPLE_PRIVATE_KEY_P8");
        var environment = (_config["Stores:Apple:Environment"] ?? Environment.GetEnvironmentVariable("GCS_APPLE_ENVIRONMENT") ?? "auto")
            .ToLowerInvariant();
        var bundleId = AppleBundleId;

        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(p8))
            return null;

        p8 = p8.Replace("\\n", "\n");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Base64Url(JsonSerializer.Serialize(new { alg = "ES256", kid = keyId, typ = "JWT" }));
        var claims = Base64Url(JsonSerializer.Serialize(new
        {
            iss = issuer,
            aud = "appstoreconnect-v1",
            bid = bundleId,
            iat = now,
            exp = now + 300
        }));
        var unsigned = header + "." + claims;

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(p8);
        var signature = ecdsa.SignData(Encoding.UTF8.GetBytes(unsigned), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        string[] hosts = environment switch
        {
            "sandbox" => ["https://api.storekit-sandbox.apple.com"],
            "production" => ["https://api.storekit.apple.com"],
            _ => ["https://api.storekit.apple.com", "https://api.storekit-sandbox.apple.com"]
        };

        var client = _clients.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        return new AppleApi(client, unsigned + "." + Base64Url(signature), hosts, bundleId);
    }

    private string AppleBundleId =>
        _config["Stores:Apple:BundleId"] ?? Environment.GetEnvironmentVariable("GCS_APPLE_BUNDLE_ID") ?? "com.gcar360.app";

    /// <summary>GET on every configured environment until Apple knows the id.</summary>
    private static async Task<JsonDocument?> AppleGetAsync(AppleApi api, string path, CancellationToken ct)
    {
        foreach (var host in api.Hosts)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, host + path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", api.Jwt);
            using var res = await api.Client.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
                continue;
            return JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        }
        return null;
    }

    /// <summary>One transaction as Apple reports it (GET /inApps/v1/transactions/{id}).</summary>
    public async Task<AppleTransactionInfo?> GetAppleTransactionAsync(string transactionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(transactionId) || transactionId.Length > 64)
            return null;
        try
        {
            var api = CreateAppleApi();
            if (api is null)
                return null;
            using var doc = await AppleGetAsync(api, "/inApps/v1/transactions/" + Uri.EscapeDataString(transactionId.Trim()), ct);
            if (doc is null || !doc.RootElement.TryGetProperty("signedTransactionInfo", out var info))
                return null;
            var parsed = ParseAppleTransaction(DecodeJwtPayload(info.GetString() ?? string.Empty));
            return parsed is not null && string.Equals(parsed.BundleId ?? api.BundleId, api.BundleId, StringComparison.Ordinal)
                ? parsed
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Latest state of an auto-renewable subscription
    /// (GET /inApps/v1/subscriptions/{originalTransactionId}): the newest
    /// renewal, its product and expiry — all taken from Apple.
    /// </summary>
    public async Task<StoreSubscriptionState?> GetAppleSubscriptionStateAsync(string originalTransactionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(originalTransactionId) || originalTransactionId.Length > 64)
            return null;
        originalTransactionId = originalTransactionId.Trim();
        try
        {
            var api = CreateAppleApi();
            if (api is null)
                return null;
            using var doc = await AppleGetAsync(api, "/inApps/v1/subscriptions/" + Uri.EscapeDataString(originalTransactionId), ct);
            if (doc is null)
                return new StoreSubscriptionState(false, null, null, "NOT_FOUND", originalTransactionId);

            var root = doc.RootElement;
            if (root.TryGetProperty("bundleId", out var bundle) &&
                !string.Equals(bundle.GetString(), api.BundleId, StringComparison.Ordinal))
                return new StoreSubscriptionState(false, null, null, "WRONG_BUNDLE", originalTransactionId);

            StoreSubscriptionState? best = null;
            if (root.TryGetProperty("data", out var groups) && groups.ValueKind == JsonValueKind.Array)
            {
                foreach (var group in groups.EnumerateArray())
                {
                    if (!group.TryGetProperty("lastTransactions", out var items) || items.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var item in items.EnumerateArray())
                    {
                        if (!item.TryGetProperty("originalTransactionId", out var otid) ||
                            !string.Equals(otid.GetString(), originalTransactionId, StringComparison.Ordinal))
                            continue;

                        // 1 active, 2 expired, 3 billing retry, 4 grace period, 5 revoked.
                        var status = item.TryGetProperty("status", out var st) && st.TryGetInt32(out var s) ? s : 0;
                        var tx = ParseAppleTransaction(DecodeJwtPayload(
                            item.TryGetProperty("signedTransactionInfo", out var sti) ? sti.GetString() ?? "" : ""));
                        if (tx is null || !ProductCatalog.IsSubscription(tx.ProductId))
                            continue;

                        var expires = tx.ExpiresAt;
                        if (status == 4 && item.TryGetProperty("signedRenewalInfo", out var sri) &&
                            DecodeJwtPayload(sri.GetString() ?? "") is JsonElement renewal &&
                            renewal.TryGetProperty("gracePeriodExpiresDate", out var grace) &&
                            grace.TryGetInt64(out var graceMs))
                        {
                            var graceEnd = DateTimeOffset.FromUnixTimeMilliseconds(graceMs);
                            if (expires is null || graceEnd > expires)
                                expires = graceEnd;
                        }

                        var active = status is 1 or 4 &&
                                     tx.RevokedAt is null &&
                                     expires is { } end && end > DateTimeOffset.UtcNow;
                        var state = status switch
                        {
                            1 => "ACTIVE",
                            2 => "EXPIRED",
                            3 => "BILLING_RETRY",
                            4 => "GRACE_PERIOD",
                            5 => "REVOKED",
                            _ => "UNKNOWN"
                        };
                        var candidate = new StoreSubscriptionState(active, tx.ProductId, expires, state, originalTransactionId);
                        if (best is null || (candidate.Active && !best.Active) ||
                            (candidate.Active == best.Active && candidate.ExpiresAt > best.ExpiresAt))
                            best = candidate;
                    }
                }
            }

            return best ?? new StoreSubscriptionState(false, null, null, "NOT_FOUND", originalTransactionId);
        }
        catch
        {
            return null;
        }
    }

    private async Task<VerifiedPurchase?> VerifyAppleAsync(
        string? productIdHint,
        string transactionId,
        string? originalTransactionId,
        CancellationToken ct)
    {
        transactionId = transactionId.Trim();
        originalTransactionId = string.IsNullOrWhiteSpace(originalTransactionId) ? null : originalTransactionId.Trim();

        // The transaction the app reported, as Apple knows it.
        var tx = await GetAppleTransactionAsync(transactionId, ct);
        if (tx is not null)
        {
            if (originalTransactionId is not null && tx.OriginalTransactionId is not null &&
                !string.Equals(tx.OriginalTransactionId, originalTransactionId, StringComparison.Ordinal))
                return null;

            if (ProductCatalog.IsConsumable(tx.ProductId))
            {
                return tx.RevokedAt is null
                    ? new VerifiedPurchase(tx.ProductId!, DateTimeOffset.UtcNow, tx.TransactionId ?? transactionId, tx.OriginalTransactionId)
                    : null;
            }

            originalTransactionId ??= tx.OriginalTransactionId;
        }
        else if (originalTransactionId is null)
        {
            return null;
        }

        // Subscriptions: the newest renewal decides (status endpoint).
        var state = await GetAppleSubscriptionStateAsync(originalTransactionId ?? transactionId, ct);
        if (state is not { Active: true, ProductId: not null, ExpiresAt: not null })
            return null;

        // The claim key is the original transaction id, so every renewal
        // belongs to the same purchase and cannot be claimed twice.
        return new VerifiedPurchase(state.ProductId, state.ExpiresAt.Value, state.Key, state.Key);
    }

    private static AppleTransactionInfo? ParseAppleTransaction(JsonElement? payload)
    {
        if (payload is not JsonElement p || p.ValueKind != JsonValueKind.Object)
            return null;

        string? Text(string name) =>
            p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        DateTimeOffset? Ms(string name) =>
            p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                : null;

        return new AppleTransactionInfo(
            Text("transactionId"), Text("originalTransactionId"), Text("productId"), Text("bundleId"),
            Ms("expiresDate"), Ms("revocationDate"), Text("type"));
    }

    // ============================================================
    // APPLE NOTIFICATIONS V2 (signedPayload JWS, x5c chain)
    // ============================================================

    // SHA-256 of "Apple Root CA - G3" (https://www.apple.com/certificateauthority/AppleRootCA-G3.cer).
    private const string AppleRootG3Sha256 = "63343ABFB89A6A03EBB57E9B3F5FA7BE7C4F5C756F3017B3A8C488C3653E9179";

    /// <summary>
    /// Verifies a JWS signed by Apple: ES256 signature with the leaf
    /// certificate, leaf → intermediate → Apple Root CA G3 chain (root pinned
    /// by fingerprint, override with GCS_APPLE_ROOT_CA_SHA256) and Apple's
    /// certificate OIDs. Returns the payload, or null with the reason.
    /// </summary>
    public static JsonElement? VerifyAppleJws(string jws, out string? error)
    {
        error = null;
        var certificates = new List<System.Security.Cryptography.X509Certificates.X509Certificate2>();
        try
        {
            var parts = (jws ?? string.Empty).Split('.');
            if (parts.Length != 3 || jws!.Length > 64_000)
            {
                error = "not a JWS";
                return null;
            }

            using var header = JsonDocument.Parse(FromBase64Url(parts[0]));
            if (!header.RootElement.TryGetProperty("alg", out var alg) || alg.GetString() != "ES256" ||
                !header.RootElement.TryGetProperty("x5c", out var x5c) || x5c.ValueKind != JsonValueKind.Array ||
                x5c.GetArrayLength() < 3)
            {
                error = "missing x5c";
                return null;
            }

            foreach (var item in x5c.EnumerateArray())
                certificates.Add(System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(
                    Convert.FromBase64String(item.GetString() ?? string.Empty)));

            var leaf = certificates[0];
            var intermediate = certificates[1];
            var root = certificates[^1];

            var pin = (Environment.GetEnvironmentVariable("GCS_APPLE_ROOT_CA_SHA256") ?? AppleRootG3Sha256)
                .Replace(":", "").Replace(" ", "").Trim();
            var rootHash = Convert.ToHexString(SHA256.HashData(root.RawData));
            if (!string.Equals(rootHash, pin, StringComparison.OrdinalIgnoreCase))
            {
                error = "root certificate is not Apple Root CA G3";
                return null;
            }

            // Apple's leaf / intermediate certificate markers.
            if (leaf.Extensions["1.2.840.113635.100.6.11.1"] is null ||
                intermediate.Extensions["1.2.840.113635.100.6.2.1"] is null)
            {
                error = "not an Apple signing certificate";
                return null;
            }

            using (var chain = new System.Security.Cryptography.X509Certificates.X509Chain())
            {
                chain.ChainPolicy.TrustMode = System.Security.Cryptography.X509Certificates.X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(root);
                chain.ChainPolicy.ExtraStore.Add(intermediate);
                chain.ChainPolicy.RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;
                if (!chain.Build(leaf))
                {
                    error = "certificate chain is not valid";
                    return null;
                }
            }

            using var key = leaf.GetECDsaPublicKey();
            if (key is null ||
                !key.VerifyData(
                    Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]),
                    FromBase64Url(parts[2]),
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                error = "bad signature";
                return null;
            }

            return DecodeJwtPayload(jws);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException or ArgumentException or InvalidOperationException)
        {
            error = "invalid JWS: " + ex.GetType().Name;
            return null;
        }
        finally
        {
            foreach (var certificate in certificates)
                certificate.Dispose();
        }
    }

    /// <summary>The ids inside a decoded notification's signedTransactionInfo (only used to re-query Apple).</summary>
    public static AppleTransactionInfo? NotificationTransaction(JsonElement payload)
    {
        if (!payload.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("signedTransactionInfo", out var info) || info.ValueKind != JsonValueKind.String)
            return null;
        return ParseAppleTransaction(DecodeJwtPayload(info.GetString() ?? string.Empty));
    }

    private static byte[] FromBase64Url(string value) =>
        Convert.FromBase64String(Pad(value.Replace('-', '+').Replace('_', '/')));

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
    string TransactionId,
    string? OriginalTransactionId = null);

/// <summary>A subscription's state as the store reports it right now. Key = purchase token / original transaction id.</summary>
public sealed record StoreSubscriptionState(
    bool Active,
    string? ProductId,
    DateTimeOffset? ExpiresAt,
    string State,
    string Key);

public sealed record AppleTransactionInfo(
    string? TransactionId,
    string? OriginalTransactionId,
    string? ProductId,
    string? BundleId,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    string? Type);
