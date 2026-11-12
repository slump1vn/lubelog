using CarCareTracker.Helper;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CarCareTracker.Tests
{
    public class StaticAssetCachingTests
    {
        private static string For(string path, string query = "") =>
            StaticAssetCaching.GetCacheControl(new PathString(path), new DefaultHttpContext { Request = { QueryString = new QueryString(query) } }.Request.Query);

        [Fact]
        public void VersionedFilesAreKeptForAYear()
        {
            Assert.Equal(StaticAssetCaching.Immutable, For("/js/budget.js", "?v=Q2m0n8x"));
            Assert.Equal(StaticAssetCaching.Immutable, For("/lib/bootstrap/dist/css/bootstrap.min.css", "?v=abc"));
        }

        [Fact]
        public void UnversionedScriptsAreRevalidated()
        {
            Assert.Equal(StaticAssetCaching.Revalidate, For("/js/budget.js"));
            Assert.Equal(StaticAssetCaching.Revalidate, For("/js/budget.js", "?v="));
            Assert.Equal(StaticAssetCaching.Revalidate, For("/offline.html"));
        }

        [Fact]
        public void ServiceWorkerAndManifestAreAlwaysRevalidated()
        {
            Assert.Equal(StaticAssetCaching.Revalidate, For("/sw.js", "?v=abc"));
            Assert.Equal(StaticAssetCaching.Revalidate, For("/manifest.json"));
        }

        [Fact]
        public void FontsAndIconsAreLongLived()
        {
            Assert.Equal(StaticAssetCaching.LongLived, For("/lib/bootstrap/dist/css/fonts/bootstrap-icons.woff2", "?7141511ac37f13e1a387fb9fc6646256"));
            Assert.Equal(StaticAssetCaching.LongLived, For("/defaults/app_icon_192.png"));
        }

        [Fact]
        public void PagesAndJsonAreNeverCompressed()
        {
            Assert.DoesNotContain("text/html", StaticAssetCaching.CompressedMimeTypes);
            Assert.DoesNotContain("application/json", StaticAssetCaching.CompressedMimeTypes);
            Assert.Contains("text/css", StaticAssetCaching.CompressedMimeTypes);
            Assert.Contains("text/javascript", StaticAssetCaching.CompressedMimeTypes);
        }
    }
}
