using Microsoft.AspNetCore.Http;

namespace CarCareTracker.Helper
{
    /// <summary>
    /// How long browsers (and the Android app, which is Chrome) may keep the files in wwwroot,
    /// and which of them are compressed on the way out.
    /// </summary>
    public static class StaticAssetCaching
    {
        /// <summary>
        /// Files linked with asp-append-version carry ?v=&lt;hash of the file&gt;. A new file gets a new URL,
        /// so the old one can be kept for a year without asking the server again.
        /// </summary>
        public const string Immutable = "public, max-age=31536000, immutable";
        /// <summary>Icon fonts and app icons: they only change with a library upgrade.</summary>
        public const string LongLived = "public, max-age=2592000";
        /// <summary>Everything else (sw.js, manifest.json, offline page): cached, but checked with the ETag each time.</summary>
        public const string Revalidate = "no-cache";

        private static readonly string[] LongLivedExtensions = { ".woff", ".woff2", ".ttf", ".eot", ".ico", ".png", ".svg", ".webp" };

        public static string GetCacheControl(PathString path, IQueryCollection query)
        {
            var value = path.Value ?? string.Empty;
            // the service worker and the web manifest must always be checked, or app updates stop.
            if (value.Equals("/sw.js", StringComparison.OrdinalIgnoreCase) || value.EndsWith(".webmanifest", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                return Revalidate;
            }
            if (query.TryGetValue("v", out var version) && !string.IsNullOrWhiteSpace(version.ToString()))
            {
                return Immutable;
            }
            var extension = Path.GetExtension(value);
            if (LongLivedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return LongLived;
            }
            return Revalidate;
        }

        /// <summary>
        /// Only static text assets are compressed. Pages and JSON with financial data are left as they are:
        /// compressing secret data together with text an attacker can influence over HTTPS opens the
        /// BREACH side channel, and those responses are small anyway.
        /// </summary>
        public static readonly string[] CompressedMimeTypes =
        {
            "text/css",
            "text/javascript",
            "application/javascript",
            "application/x-javascript",
            "image/svg+xml",
            "image/x-icon",
            "font/ttf",
            "application/vnd.ms-fontobject"
        };
    }
}
