using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ToolsBox.Core.WebResources;

public sealed record DouyinMediaVariant(string Url, string Label, int Height, long BitRate);

public sealed record DouyinMediaItem(string Id, string Title, double? DurationSeconds, IReadOnlyList<DouyinMediaVariant> Variants);

public static class DouyinMediaParser
{
    private const int MaxJsonBytes = 4 * 1024 * 1024;
    private const int MaxRecords = 100;
    private const int MaxVariants = 32;

    private static readonly HashSet<string> MetadataPaths = new(StringComparer.Ordinal)
    {
        "/aweme/v1/web/aweme/detail/",
        "/aweme/v1/web/feed/",
        "/aweme/v1/web/tab/feed/",
        "/aweme/v2/web/module/feed/",
        "/aweme/v1/web/aweme/post/",
        "/aweme/v1/web/aweme/related/"
    };

    public static bool IsMetadataResponse(string responseUrl) =>
        TryGetWebUri(responseUrl, out var uri) &&
        uri!.Host is "www.douyin.com" or "douyin.com" &&
        uri.IsDefaultPort && MetadataPaths.Contains(uri.AbsolutePath);

    public static IReadOnlyList<DouyinMediaItem> Parse(string json)
    {
        if (json is null || json.Length > MaxJsonBytes || string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
            return Array.Empty<DouyinMediaItem>();

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var items = new Dictionary<string, DouyinMediaItem>(StringComparer.Ordinal);
            int records = 0;
            Visit(document.RootElement, items, ref records);
            return items.Values.ToArray();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            // JsonDocument defers decoding strings; malformed surrogate escapes can fail during access.
            return Array.Empty<DouyinMediaItem>();
        }
    }

    private static void Visit(JsonElement node, Dictionary<string, DouyinMediaItem> items, ref int records)
    {
        if (records >= MaxRecords) return;
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray())
            {
                Visit(child, items, ref records);
                if (records >= MaxRecords) break;
            }
        }
        else if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
            {
                if (property.NameEquals("aweme_detail"))
                {
                    records++;
                    AddItem(property.Value, items);
                }
                else if (property.NameEquals("aweme_list") && property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        records++;
                        AddItem(item, items);
                        if (records >= MaxRecords) break;
                    }
                }
                else
                {
                    Visit(property.Value, items, ref records);
                }
                if (records >= MaxRecords) break;
            }
        }
    }

    private static void AddItem(JsonElement node, Dictionary<string, DouyinMediaItem> items)
    {
        if (node.ValueKind != JsonValueKind.Object || HasProtection(node) || IsImageOrLive(node)) return;
        string? id = GetString(node, "aweme_id");
        if (id is not { Length: >= 1 and <= 20 } || !id.All(c => c is >= '0' and <= '9')) return;
        if (!node.TryGetProperty("video", out var video) || video.ValueKind != JsonValueKind.Object || HasProtection(video)) return;

        var variants = new Dictionary<string, DouyinMediaVariant>(StringComparer.Ordinal);
        if (video.TryGetProperty("bit_rate", out var bitRates) && bitRates.ValueKind == JsonValueKind.Array)
        {
            foreach (var bitRate in bitRates.EnumerateArray())
            {
                if (bitRate.ValueKind != JsonValueKind.Object || HasProtection(bitRate)) continue;
                if (bitRate.TryGetProperty("play_addr", out var address))
                    AddAddress(address, bitRate, video, variants);
            }
        }
        if (video.TryGetProperty("play_addr", out var defaultAddress))
            AddAddress(defaultAddress, video, video, variants);
        if (variants.Count == 0) return;

        string title = SanitizeText(GetString(node, "desc"), 200);
        double? duration = video.TryGetProperty("duration", out var durationNode) &&
            durationNode.ValueKind == JsonValueKind.Number && durationNode.TryGetDouble(out double milliseconds) &&
            double.IsFinite(milliseconds) && milliseconds > 0 ? milliseconds / 1000d : null;

        if (items.TryGetValue(id, out var previous))
        {
            foreach (var variant in previous.Variants) AddVariant(variants, variant);
            title = previous.Title.Length > 0 ? previous.Title : title;
            duration = previous.DurationSeconds ?? duration;
        }

        items[id] = new(id, title, duration, variants.Values
            .OrderByDescending(variant => variant.Height)
            .ThenByDescending(variant => variant.BitRate)
            .ThenByDescending(variant => IsOfficialPlayUrl(variant.Url))
            .ToArray());
    }

    private static void AddAddress(JsonElement address, JsonElement quality, JsonElement video,
        Dictionary<string, DouyinMediaVariant> variants)
    {
        if (address.ValueKind != JsonValueKind.Object || HasProtection(address) ||
            !address.TryGetProperty("url_list", out var urls) || urls.ValueKind != JsonValueKind.Array) return;

        int height = GetPositiveInt(address, "height");
        if (height == 0) height = GetPositiveInt(quality, "height");
        if (height == 0) height = GetPositiveInt(video, "height");
        int width = GetPositiveInt(address, "width");
        if (width == 0) width = GetPositiveInt(quality, "width");
        if (width == 0) width = GetPositiveInt(video, "width");
        long bitRate = quality.TryGetProperty("bit_rate", out var rate) && rate.ValueKind == JsonValueKind.Number &&
            rate.TryGetInt64(out long value) && value > 0 ? value : 0;
        string gear = SanitizeText(GetString(quality, "gear_name"), 64);
        string label = height > 0 ? $"{height}p" : width > 0 ? $"{width}px" : "原始视频";
        if (gear.Length > 0) label += " · " + gear;
        if (bitRate > 0) label += " · " + (bitRate / 1_000_000d).ToString("0.##", CultureInfo.InvariantCulture) + " Mbps";

        foreach (var urlNode in urls.EnumerateArray())
        {
            if (urlNode.ValueKind != JsonValueKind.String) continue;
            string? url = urlNode.GetString();
            if (!TryGetWebUri(url, out _)) continue;
            // Keep the original signed URL; Uri normalization can invalidate its signature.
            AddVariant(variants, new(url!, label, height, bitRate));
        }
    }

    private static void AddVariant(Dictionary<string, DouyinMediaVariant> variants, DouyinMediaVariant candidate)
    {
        if (variants.TryGetValue(candidate.Url, out var existing))
        {
            if (CompareQuality(candidate, existing) > 0) variants[candidate.Url] = candidate;
            return;
        }
        if (variants.Count < MaxVariants)
        {
            variants.Add(candidate.Url, candidate);
            return;
        }
        var worst = variants.Values.MinBy(variant => (variant.Height, variant.BitRate, IsOfficialPlayUrl(variant.Url)))!;
        if (CompareQuality(candidate, worst) <= 0) return;
        variants.Remove(worst.Url);
        variants.Add(candidate.Url, candidate);
    }

    private static int CompareQuality(DouyinMediaVariant left, DouyinMediaVariant right) =>
        (left.Height, left.BitRate, IsOfficialPlayUrl(left.Url))
            .CompareTo((right.Height, right.BitRate, IsOfficialPlayUrl(right.Url)));

    // A supplied same-quality play URL can issue a fresh ordinary CDN redirect where
    // embedded CDN addresses have already expired. Never synthesize a URL or change its signature.
    private static bool IsOfficialPlayUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.Host == "www.douyin.com" && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        uri.AbsolutePath == "/aweme/v1/play/";

    private static bool IsImageOrLive(JsonElement node)
    {
        if (node.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array && images.GetArrayLength() > 0)
            return true;
        if (GetPositiveInt(node, "aweme_type") is 68 or 101) return true;
        return IsEnabledProperty(node, "is_live") || IsEnabledProperty(node, "is_image");
    }

    private static bool HasProtection(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in node.EnumerateObject())
        {
            if (property.Name is "is_drm" or "has_drm" or "drm_type" or "drm_protected" or "is_encrypted" or "is_encrypt" or "encrypted")
            {
                if (IsEnabled(property.Value)) return true;
            }
        }
        return false;
    }

    private static bool IsEnabledProperty(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && IsEnabled(value);

    private static bool IsEnabled(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.Number => !value.TryGetDouble(out double number) || number != 0,
        JsonValueKind.String => value.GetString()?.Trim().ToLowerInvariant() is not (null or "" or "0" or "false" or "none" or "clear"),
        _ => false
    };

    private static int GetPositiveInt(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out int number) && number > 0 ? number : 0;

    private static string? GetString(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string SanitizeText(string? value, int limit)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var result = new StringBuilder(Math.Min(value.Length, limit));
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                if (result.Length > 0 && result.Length < limit && result[^1] != ' ') result.Append(' ');
            }
            else if (Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.Control or UnicodeCategory.Format))
            {
                if (result.Length + rune.Utf16SequenceLength > limit) break;
                result.Append(rune.ToString());
            }
            if (result.Length >= limit) break;
        }
        return result.ToString().Trim();
    }

    private static bool TryGetWebUri(string? value, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrEmpty(value) || value.Length > 16384 || value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\'))
            return false;
        for (int i = 0; i + 2 < value.Length; i++)
        {
            if (value[i] == '%' && byte.TryParse(value.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte escaped) &&
                (escaped < 32 || escaped == 127)) return false;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length != 0 || uri.Host.Length == 0) return false;

        // Uri.UserInfo is also empty for an explicitly empty user name (https://@host).
        int authorityStart = value.IndexOf("://", StringComparison.Ordinal);
        if (authorityStart < 0) return false;
        var authority = value.AsSpan(authorityStart + 3);
        int authorityEnd = authority.IndexOfAny('/', '?', '#');
        if (authorityEnd >= 0) authority = authority[..authorityEnd];
        return !authority.Contains('@');
    }
}
