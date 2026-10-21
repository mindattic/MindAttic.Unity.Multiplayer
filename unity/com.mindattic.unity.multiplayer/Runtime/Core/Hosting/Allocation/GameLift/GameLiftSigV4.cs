using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// AWS Signature Version 4 for <see cref="HttpGameLiftApiClient"/>'s calls to the GameLift
    /// service API. A small, self-contained copy of the same published algorithm
    /// <c>Unity.Multiplayer.Hosting.Cli.Provisioning.Aws.AwsSigV4Signer</c> implements for mp-host's
    /// EC2/Lightsail/GameLift-fleet clients: Core is a separate assembly compiled for Unity
    /// (netstandard2.1/C# 9, no project reference to mp-host's own .NET 10 project is possible), so
    /// duplicating this ~50-line algorithm is simpler than inventing a shared package for it.
    /// </summary>
    internal static class GameLiftSigV4
    {
        /// <summary>The headers a signed request must carry, beyond whatever the caller already set.</summary>
        public struct SignedHeaders
        {
            /// <summary>The <c>X-Amz-Date</c> header value used in the signature.</summary>
            public string AmzDate;

            /// <summary>The <c>Authorization</c> header value.</summary>
            public string Authorization;
        }

        /// <summary>
        /// Signs one request. <paramref name="extraSignedHeaders"/> are the other headers (lower-cased
        /// names, e.g. <c>content-type</c>, <c>x-amz-target</c>) that must be part of the signature
        /// beyond <c>host</c> and <c>x-amz-date</c>, which this method always includes itself.
        /// </summary>
        public static SignedHeaders Sign(
            string method,
            string host,
            string canonicalUri,
            string body,
            IDictionary<string, string> extraSignedHeaders,
            string accessKeyId,
            string secretAccessKey,
            string region,
            string service,
            DateTime utcNow)
        {
            string amzDate = utcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
            string dateStamp = utcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

            var headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { "host", host },
                { "x-amz-date", amzDate },
            };
            if (extraSignedHeaders != null)
            {
                foreach (KeyValuePair<string, string> pair in extraSignedHeaders)
                {
                    headers[pair.Key.ToLowerInvariant()] = pair.Value;
                }
            }

            var canonicalHeaders = new StringBuilder();
            var signedNames = new List<string>();
            foreach (KeyValuePair<string, string> pair in headers)
            {
                canonicalHeaders.Append(pair.Key).Append(':').Append(pair.Value).Append('\n');
                signedNames.Add(pair.Key);
            }

            string signedHeaderNames = string.Join(";", signedNames.ToArray());
            string payloadHash = Sha256Hex(body ?? string.Empty);
            string canonicalRequest = method + "\n" + canonicalUri + "\n" + string.Empty + "\n"
                + canonicalHeaders + "\n" + signedHeaderNames + "\n" + payloadHash;
            string credentialScope = dateStamp + "/" + region + "/" + service + "/aws4_request";
            string stringToSign = "AWS4-HMAC-SHA256\n" + amzDate + "\n" + credentialScope + "\n" + Sha256Hex(canonicalRequest);

            byte[] signingKey = DeriveSigningKey(secretAccessKey, dateStamp, region, service);
            string signature = HmacHex(signingKey, stringToSign);

            string authorization = "AWS4-HMAC-SHA256 Credential=" + accessKeyId + "/" + credentialScope
                + ", SignedHeaders=" + signedHeaderNames + ", Signature=" + signature;

            SignedHeaders result;
            result.AmzDate = amzDate;
            result.Authorization = authorization;
            return result;
        }

        private static byte[] DeriveSigningKey(string secretAccessKey, string dateStamp, string region, string service)
        {
            byte[] kSecret = Encoding.UTF8.GetBytes("AWS4" + secretAccessKey);
            byte[] kDate = Hmac(kSecret, dateStamp);
            byte[] kRegion = Hmac(kDate, region);
            byte[] kService = Hmac(kRegion, service);
            return Hmac(kService, "aws4_request");
        }

        private static byte[] Hmac(byte[] key, string data)
        {
            using (var hmac = new HMACSHA256(key))
            {
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            }
        }

        private static string HmacHex(byte[] key, string data)
        {
            return ToHex(Hmac(key, data));
        }

        private static string Sha256Hex(string data)
        {
            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(data)));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }
    }
}
