using AMAIS.WKFLO.Common.ApiClient.Extensions;
using Flurl.Http;
using Flurl.Http.Newtonsoft;
using Polly;
using System.Runtime.CompilerServices;
using System.Web;

namespace AMAIS.WKFLO.Common.ApiClient.Client
{
    /// <summary>
    /// API client is mainly responible for making the HTTP call to the API backend.
    /// </summary>
    public class ApiClient
    {
        static ApiClient()
        {
            FlurlHttp.Clients.WithDefaults(b => b.UseNewtonsoft());
        }

        // Flurl 4.x sets HttpClient.Timeout in FlurlClient's constructor, which throws if the
        // HttpClient has already sent a request. Caching by HttpClient identity ensures we only
        // construct one FlurlClient per HttpClient instance regardless of how many ApiClients share it.
        // Using ConditionalWeakTable is the right tool here: its keys are weak references, so the HttpClient can be GC'd normally when nothing else holds it,
        // and the entry is automatically removed.
        private static readonly ConditionalWeakTable<HttpClient, IFlurlClient> _flurlClientCache = new();

        private readonly HttpClient _httpClient;
        private readonly IFlurlClient _flurlClient;
        private readonly Dictionary<String, String> _defaultHeaderMap = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="ApiClient" /> class.
        /// </summary>
        /// <param name="cacheProvider">The Memory Cache.</param>
        /// <param name="httpClient">The httpClient.</param>
        public ApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _flurlClient = _flurlClientCache.GetOrAdd(httpClient, hc =>
            {
                var c = new FlurlClient(hc);
                c.Settings.JsonSerializer = new NewtonsoftJsonSerializer();
                return c;
            });
            BasePath = httpClient.BaseAddress.ToString();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ApiClient" /> class.
        /// </summary>
        /// <param name="cacheProvider">The Memory Cache.</param>
        /// <param name="basePath">The base path.</param>
        public ApiClient(string basePath = "/")
        {
            BasePath = basePath;
        }

        /// <summary>
        /// Gets or sets the base path.
        /// </summary>
        /// <value>The base path</value>
        public string BasePath { get; set; }

        /// <summary>
        /// Gets the default header.
        /// </summary>
        public Dictionary<String, String> DefaultHeader
        {
            get { return _defaultHeaderMap; }
        }

        /// <summary>
        /// Makes the HTTP request (Sync).
        /// </summary>
        /// <param name="path">URL path.</param>
        /// <param name="method">HTTP method.</param>
        /// <param name="queryParams">Query parameters.</param>
        /// <param name="postBody">HTTP body (POST request).</param>
        /// <param name="headerParams">Header parameters.</param>
        /// <param name="formParams">Form parameters.</param>
        /// <param name="authSettings">Authentication settings.</param>
        /// <returns>Object</returns>
        public async Task<IFlurlResponse> CallApiAsync(String path, HttpMethod method, Dictionary<String, String> queryParams = null, Object postBody = null,
            Dictionary<String, String> headerParams = null, Dictionary<String, String> formParams = null, String[] authSettings = null, int timeoutInSeconds = 100)
        {
            IFlurlClient client;
            if (_httpClient != null)
            {
                client = _flurlClient;
            }
            else
            {
                client = new FlurlClient(BasePath);
                client.Settings.JsonSerializer = new NewtonsoftJsonSerializer();
            }

            client.Settings.JsonSerializer = new NewtonsoftJsonSerializer();

            var request = client.Request(path).WithTimeout(timeoutInSeconds)
                .AllowAnyHttpStatus();

            // add default header, if any
            foreach (var defaultHeader in _defaultHeaderMap)
                request.WithHeader(defaultHeader.Key, defaultHeader.Value);

            // add header parameter, if any
            if (headerParams != null)
                foreach (var param in headerParams)
                    request.WithHeader(param.Key, param.Value);

            // add query parameter, if any
            if (queryParams != null)
                foreach (var param in queryParams)
                    request.SetQueryParam(param.Key, param.Value);

            // add form parameter, if any
            if (formParams != null)
                foreach (var param in formParams)
                    request.SetQueryParam(param.Key, param.Value);

            if (method == HttpMethod.Post)
            {
                if (postBody is string postBodyString)
                {
                    return await SendPollyRequestAsync(request.PostAsync(new StringContent(postBodyString, null, "application/json-patch+json")));
                }
                else
                {
                    return await SendPollyRequestAsync(request.PostJsonAsync(postBody));
                }

            }
            else if (method == HttpMethod.Delete)
            {
                return await SendPollyRequestAsync(request.DeleteAsync());
            }
            else if (method == HttpMethod.Get)
            {
                return await SendPollyRequestAsync(request.GetAsync());
            }
            else if (method == HttpMethod.Put)
            {
                return await SendPollyRequestAsync(request.PutJsonAsync(postBody));
            }
            else
            {
                return await SendPollyRequestAsync(request.SendJsonAsync(HttpMethod.Get, null));
            }
        }

        private Task<IFlurlResponse> SendPollyRequestAsync(Task<IFlurlResponse> responseTask)
        {
            return Policy
                .HandleResult<IFlurlResponse>(r => r.IsWorthRetrying())
                .WaitAndRetryAsync(new[] {
                    TimeSpan.FromSeconds(16),
                    TimeSpan.FromSeconds(32)
                })
                .ExecuteAsync(() => responseTask);
        }

        /// <summary>
        /// Add default header.
        /// </summary>
        /// <param name="key">Header field name.</param>
        /// <param name="value">Header field value.</param>
        /// <returns></returns>
        public void AddDefaultHeader(string key, string value)
        {
            _defaultHeaderMap.Add(key, value);
        }

        /// <summary>
        /// Escape string (url-encoded).
        /// </summary>
        /// <param name="str">String to be escaped.</param>
        /// <returns>Escaped string.</returns>
        public static string EscapeString(string str)
        {
            return HttpUtility.UrlEncode(str);
        }

        /// <summary>
        /// If parameter is DateTime, output in a formatted string (default ISO 8601), customizable with Configuration.DateTime.
        /// If parameter is a list of string, join the list with ",".
        /// Otherwise just return the string.
        /// </summary>
        /// <param name="obj">The parameter (header, path, query, form).</param>
        /// <returns>Formatted string.</returns>
        public static string ParameterToString(object obj)
        {
            if (obj is DateTimeOffset timeOffset)
                // Return a formatted date string - Can be customized with Configuration.DateTimeFormat
                // Defaults to an ISO 8601, using the known as a Round-trip date/time pattern ("o")
                // https://msdn.microsoft.com/en-us/library/az4se3k1(v=vs.110).aspx#Anchor_8
                // For example: 2009-06-15T13:45:30.0000000
                return timeOffset.ToString(Configuration.DateTimeFormat);
            else if (obj is DateTime time)
                // Return a formatted date string - Can be customized with Configuration.DateTimeFormat
                // Defaults to an ISO 8601, using the known as a Round-trip date/time pattern ("o")
                // https://msdn.microsoft.com/en-us/library/az4se3k1(v=vs.110).aspx#Anchor_8
                // For example: 2009-06-15T13:45:30.0000000
                return time.ToString(Configuration.DateTimeFormat);
            else if (obj is List<string>)
                return String.Join(",", (obj as List<string>).ToArray());
            else
                return Convert.ToString(obj);
        }
    }
}
