using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace Kankei.Desktop;

public static class YouTubePlayback
{
    // Allow timestamp rounding and playback during page loading, consistent with
    // the playing-state verification. A mismatch is observed, never key-corrected.
    public static bool IsAtRestorePosition(YouTubePlaybackState current, YouTubePlaybackState expected) =>
        SameVideo(current.Url, expected.Url)
        && double.IsFinite(current.PositionSeconds) && current.PositionSeconds >= 0
        && double.IsFinite(expected.PositionSeconds) && expected.PositionSeconds >= 0
        && Math.Abs(current.PositionSeconds - expected.PositionSeconds) <= 3;

    public static string? NormalizeVideoUrl(string value)
    {
        if (!value.Contains("://", StringComparison.Ordinal)) value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443
            || !string.IsNullOrEmpty(uri.UserInfo) || uri.Host is not ("www.youtube.com" or "youtube.com" or "m.youtube.com")
            || uri.AbsolutePath != "/watch") return null;
        var query = QueryHelpers.ParseQuery(uri.Query);
        return query.TryGetValue("v", out var video) && video.Count == 1 && Regex.IsMatch(video.ToString(), @"^[A-Za-z0-9_-]+$")
            ? uri.AbsoluteUri : null;
    }

    public static string RestoreUrl(YouTubePlaybackState state)
    {
        var url = NormalizeVideoUrl(state.Url);
        if (state.Version != 1 || url is null || !double.IsFinite(state.PositionSeconds) || state.PositionSeconds < 0)
            throw new InvalidDataException("保存された YouTube 再生状態が不正です。");
        var uri = new Uri(url);
        var query = QueryHelpers.ParseQuery(uri.Query);
        query.Remove("t");
        query.Remove("start");
        var preserved = QueryHelpers.AddQueryString(uri.GetLeftPart(UriPartial.Path), query);
        return QueryHelpers.AddQueryString(preserved, "t", Math.Floor(state.PositionSeconds).ToString(CultureInfo.InvariantCulture) + "s");
    }

    public static double ParsePosition(string value)
    {
        var current = value.Split('/')[0].Trim();
        if (Regex.IsMatch(current, @"^\d+(?::\d{1,2}){1,2}$"))
            return current.Split(':').Aggregate(0d, (total, part) => total * 60 + double.Parse(part, CultureInfo.InvariantCulture));
        var units = Regex.Matches(current, @"(\d+)\s*(時間|分|秒|hours?|minutes?|seconds?)", RegexOptions.IgnoreCase);
        if (units.Count == 0) throw new InvalidDataException("YouTube の再生位置を読み取れません。ライブ配信は対象外です。");
        return units.Sum(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) *
            (match.Groups[2].Value.ToLowerInvariant() switch { "時間" or "hour" or "hours" => 3600, "分" or "minute" or "minutes" => 60, _ => 1 }));
    }

    public static bool ParsePaused(string buttonName)
    {
        if (buttonName.StartsWith("一時停止", StringComparison.Ordinal) || buttonName.StartsWith("Pause", StringComparison.OrdinalIgnoreCase)) return false;
        if (buttonName.StartsWith("再生", StringComparison.Ordinal) || buttonName.StartsWith("Play", StringComparison.OrdinalIgnoreCase)) return true;
        throw new InvalidDataException("YouTube の再生／一時停止状態を読み取れません。");
    }

    public static bool SameVideo(string left, string right)
    {
        var a = NormalizeVideoUrl(left);
        var b = NormalizeVideoUrl(right);
        if (a is null || b is null) return false;
        var aq = QueryHelpers.ParseQuery(new Uri(a).Query);
        var bq = QueryHelpers.ParseQuery(new Uri(b).Query);
        // YouTube can canonicalize index (for example when a playlist contains unavailable entries).
        // The video and playlist IDs identify the intended playback context.
        return new[] { "v", "list" }.All(key =>
        {
            aq.TryGetValue(key, out var av);
            bq.TryGetValue(key, out var bv);
            return av.ToString() == bv.ToString();
        });
    }

    public static bool SamePlaybackContext(string left, string right)
    {
        var a = NormalizeVideoUrl(left);
        var b = NormalizeVideoUrl(right);
        if (a is null || b is null) return false;
        var aq = QueryHelpers.ParseQuery(new Uri(a).Query);
        var bq = QueryHelpers.ParseQuery(new Uri(b).Query);
        if (aq.TryGetValue("list", out var list) && !string.IsNullOrWhiteSpace(list.ToString()))
            return bq.TryGetValue("list", out var other) && list.ToString() == other.ToString();
        return SameVideo(a, b);
    }
}
