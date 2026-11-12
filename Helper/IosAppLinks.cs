using System.Text.RegularExpressions;

namespace CarCareTracker.Helper
{
    public static class IosAppLinks
    {
        //an Apple team id is 10 letters or digits; a bundle id is reverse-domain.
        private static readonly Regex AppId = new Regex(@"^[A-Z0-9]{10}\.[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$", RegexOptions.Compiled);

        /// <summary>The well-formed app ids in the setting, in order, without duplicates.</summary>
        public static List<string> ParseAppIds(string? setting)
        {
            return (setting ?? string.Empty)
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => AppId.IsMatch(x))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
    }
}
