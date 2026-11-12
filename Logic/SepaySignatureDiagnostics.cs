using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CarCareTracker.Logic
{
    /// <summary>
    /// Explains why a SePay webhook signature did not match, for the server log. It never writes the
    /// secret, the signature or field values; only the shape of the request and which of several
    /// plausible signing variants would have matched (raw body, re-encoded JSON, body only...).
    /// Temporary aid while the real SePay signing format is being confirmed.
    /// </summary>
    public static class SepaySignatureDiagnostics
    {
        public static string Describe(string secret, string? timestampHeader, string? signatureHeader, byte[] rawBody, long nowUnixSeconds, string? contentType)
        {
            var parts = new List<string>();
            parts.Add($"contentType={(string.IsNullOrEmpty(contentType) ? "-" : contentType)}");
            parts.Add($"bodyBytes={rawBody.Length}");
            parts.Add($"bodyNonAscii={rawBody.Any(b => b > 0x7F)}");
            var bodyText = Encoding.UTF8.GetString(rawBody);
            parts.Add($"bodyHasUnicodeEscapes={bodyText.Contains("\\u", StringComparison.Ordinal)}");
            parts.Add($"bodyHasEscapedSlash={bodyText.Contains("\\/", StringComparison.Ordinal)}");
            parts.Add($"bodyHasSpaceAfterColon={bodyText.Contains("\": ", StringComparison.Ordinal)}");
            parts.Add($"bodyHasNewline={bodyText.Contains('\n')}");
            parts.Add($"bodyStartsWithBom={rawBody.Length >= 3 && rawBody[0] == 0xEF && rawBody[1] == 0xBB && rawBody[2] == 0xBF}");

            //timestamp
            var ts = timestampHeader?.Trim();
            long timestamp = 0;
            if (string.IsNullOrEmpty(ts))
            {
                parts.Add("timestamp=missing");
            }
            else if (!long.TryParse(ts, NumberStyles.None, CultureInfo.InvariantCulture, out timestamp))
            {
                parts.Add($"timestamp=unparsable(len {ts.Length})");
            }
            else
            {
                parts.Add($"timestampDigits={ts.Length}");
                parts.Add($"skewSeconds={nowUnixSeconds - timestamp}");
                if (ts.Length >= 13)
                {
                    parts.Add($"skewSecondsIfMillis={nowUnixSeconds - timestamp / 1000}");
                }
            }

            //signature header shape
            var sig = signatureHeader?.Trim();
            byte[]? given = null;
            string encoding = "none";
            if (string.IsNullOrEmpty(sig))
            {
                parts.Add("signature=missing");
            }
            else
            {
                var eq = sig.IndexOf('=');
                var scheme = eq > 0 && eq < 12 ? sig.Substring(0, eq) : "";
                var value = eq > 0 && eq < 12 ? sig.Substring(eq + 1) : sig;
                parts.Add($"signatureScheme={(scheme.Length == 0 ? "-" : scheme)}");
                parts.Add($"signatureValueLen={value.Length}");
                if (value.Length == 64 && value.All(Uri.IsHexDigit))
                {
                    given = Convert.FromHexString(value);
                    encoding = "hex";
                }
                else
                {
                    try { given = Convert.FromBase64String(value); encoding = "base64"; } catch (FormatException) { }
                }
                parts.Add($"signatureEncoding={encoding}");
            }

            //which signing variant matches
            if (given != null && !string.IsNullOrEmpty(secret))
            {
                var key = Encoding.UTF8.GetBytes(secret);
                var bodies = new List<(string Name, byte[] Bytes)> { ("raw", rawBody) };
                foreach (var (name, text) in ReEncodings(bodyText))
                {
                    bodies.Add((name, Encoding.UTF8.GetBytes(text)));
                }
                var prefixes = new List<(string Name, string Prefix)> { ("none", string.Empty) };
                if (!string.IsNullOrEmpty(ts))
                {
                    prefixes.Add(("ts.", ts + "."));
                    prefixes.Add(("ts", ts));
                }
                var matches = new List<string>();
                foreach (var (bodyName, bytes) in bodies)
                {
                    foreach (var (prefixName, prefix) in prefixes)
                    {
                        var signed = Encoding.UTF8.GetBytes(prefix).Concat(bytes).ToArray();
                        if (CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key, signed), given))
                        {
                            matches.Add($"{prefixName}+{bodyName}");
                        }
                    }
                    if (!string.IsNullOrEmpty(ts))
                    {
                        //body first, then timestamp
                        var signed = bytes.Concat(Encoding.UTF8.GetBytes("." + ts)).ToArray();
                        if (CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key, signed), given))
                        {
                            matches.Add($"{bodyName}+.ts");
                        }
                    }
                }
                parts.Add($"matchingVariants={(matches.Any() ? string.Join("|", matches) : "none")}");
            }
            return string.Join(" ", parts);
        }

        /// <summary>The same JSON written the ways common server stacks write it.</summary>
        private static IEnumerable<(string Name, string Text)> ReEncodings(string bodyText)
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(bodyText);
            }
            catch (JsonException)
            {
                yield break;
            }
            using (doc)
            {
                var relaxed = Write(doc, JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
                yield return ("compactUnescaped", relaxed);
                //PHP json_encode default: \uXXXX in lowercase hex and "\/"
                var php = PhpEscape(relaxed, escapeUnicode: true, escapeSlash: true);
                yield return ("phpDefault", php);
                yield return ("phpUnescapedSlashes", PhpEscape(relaxed, escapeUnicode: true, escapeSlash: false));
                yield return ("phpUnescapedUnicode", PhpEscape(relaxed, escapeUnicode: false, escapeSlash: true));
                //Python json.dumps default: ", " and ": " separators, \uXXXX lowercase
                yield return ("pythonDefault", PhpEscape(relaxed, escapeUnicode: true, escapeSlash: false).Replace("\",\"", "\", \"").Replace("\":", "\": "));
                //.NET / JS default escaping (uppercase hex)
                yield return ("dotnetDefault", Write(doc, JavaScriptEncoder.Default));
            }
        }

        private static string Write(JsonDocument doc, JavaScriptEncoder encoder)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = encoder, Indented = false }))
            {
                doc.WriteTo(writer);
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static string PhpEscape(string json, bool escapeUnicode, bool escapeSlash)
        {
            var sb = new StringBuilder(json.Length + 16);
            foreach (var c in json)
            {
                if (escapeUnicode && c > 0x7F)
                {
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                }
                else if (escapeSlash && c == '/')
                {
                    sb.Append("\\/");
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
