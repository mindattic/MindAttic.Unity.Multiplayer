using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>One HTTP exchange with the rendezvous service, as <see cref="RendezvousClient"/> sees it.</summary>
    public interface IRendezvousHttp
    {
        /// <summary>
        /// Sends <paramref name="method"/> <paramref name="path"/> (relative to the service's base URL) with an
        /// optional JSON body and extra headers, and returns the status and body. Throws only when no
        /// response came back (the service is unreachable, or the request timed out).
        /// </summary>
        Task<RendezvousHttpResponse> SendAsync(
            string method, string path, string jsonBody, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken);
    }

    /// <summary>An HTTP response: status code and body text.</summary>
    public sealed class RendezvousHttpResponse
    {
        /// <summary>Creates a response.</summary>
        public RendezvousHttpResponse(int statusCode, string body)
        {
            StatusCode = statusCode;
            Body = body ?? string.Empty;
        }

        /// <summary>The HTTP status code.</summary>
        public int StatusCode { get; }

        /// <summary>The body text; empty when there was none.</summary>
        public string Body { get; }

        /// <summary>True for a 2xx status.</summary>
        public bool IsSuccess
        {
            get { return StatusCode >= 200 && StatusCode < 300; }
        }
    }

    /// <summary>
    /// <see cref="IRendezvousHttp"/> over <see cref="HttpClient"/>, which Unity's Mono and IL2CPP players support on
    /// desktop, mobile and Linux servers (not WebGL).
    /// </summary>
    public sealed class HttpClientRendezvousHttp : IRendezvousHttp, IDisposable
    {
        /// <summary>How long one request may take by default.</summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

        private readonly HttpClient client;
        private readonly bool ownsClient;

        /// <summary>Talks to the service at <paramref name="baseUrl"/> with a client of its own.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="baseUrl"/> is null.</exception>
        public HttpClientRendezvousHttp(Uri baseUrl)
            : this(new HttpClient { BaseAddress = WithTrailingSlash(baseUrl), Timeout = DefaultTimeout }, true)
        {
        }

        /// <summary>
        /// <paramref name="baseUrl"/> ending in a slash, so relative routes resolve under its path
        /// (<c>https://example.com/rendezvous</c> + <c>v1/register</c> → <c>https://example.com/rendezvous/v1/register</c>).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="baseUrl"/> is null.</exception>
        public static Uri WithTrailingSlash(Uri baseUrl)
        {
            if (baseUrl == null)
            {
                throw new ArgumentNullException(nameof(baseUrl));
            }

            string text = baseUrl.AbsoluteUri;
            return text.EndsWith("/", StringComparison.Ordinal) ? baseUrl : new Uri(text + "/");
        }

        /// <summary>
        /// Uses <paramref name="client"/>, whose <see cref="HttpClient.BaseAddress"/> must be the service's base URL.
        /// Disposes it only when <paramref name="ownsClient"/> is true.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="client"/> is null.</exception>
        public HttpClientRendezvousHttp(HttpClient client, bool ownsClient)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.ownsClient = ownsClient;
        }

        /// <inheritdoc />
        public async Task<RendezvousHttpResponse> SendAsync(
            string method, string path, string jsonBody, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            // A relative path without the leading slash, so a base URL with a path prefix
            // (https://example.com/rendezvous/) keeps it.
            using (var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path.TrimStart('/'), UriKind.Relative)))
            {
                if (jsonBody != null)
                {
                    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                }

                if (headers != null)
                {
                    foreach (KeyValuePair<string, string> header in headers)
                    {
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                using (HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    string body = response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return new RendezvousHttpResponse((int)response.StatusCode, body);
                }
            }
        }

        /// <summary>Disposes the HttpClient if this object created it.</summary>
        public void Dispose()
        {
            if (ownsClient)
            {
                client.Dispose();
            }
        }
    }
}
