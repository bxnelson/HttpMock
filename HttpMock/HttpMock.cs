using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace HttpMock
{
    public partial class HttpMock : IDisposable
    {
        readonly string _baseUrl;
        readonly HttpListener _httpListener;
        readonly List<Exception> _failures = new List<Exception>();
        bool _legallyDisposed = false;

        readonly List<EndpointHttpEndpointMockSetup> _setups = new List<EndpointHttpEndpointMockSetup>();
        readonly bool _strict;

        public static HttpMock Create(string urlPrefix = "", bool strict = false)
        {
            if (!urlPrefix.EndsWith("/"))
                urlPrefix += "/";

            if (urlPrefix.StartsWith("/"))
                urlPrefix = urlPrefix.Substring(1);

            urlPrefix = urlPrefix != "/" ? urlPrefix : string.Empty;

            HttpListenerException err = null;
            for (var i = 0; i < 15; i++)
            {
                try
                {
                    return new HttpMock($"http://localhost:{8888 + i}/{urlPrefix}", strict);
                }
                catch (HttpListenerException e)
                {
                    err = e;
                }
            }

            throw err;
        }

        ~HttpMock()
        {
            if (_legallyDisposed)
                return;

            ((IDisposable)_httpListener)?.Dispose();
            Console.WriteLine($"HttpMock for URL {_baseUrl} was not legally disposed!");
            Debug.WriteLine($"HttpMock for URL {_baseUrl} was not legally disposed!");
            Trace.WriteLine($"HttpMock for URL {_baseUrl} was not legally disposed!");

            Environment.Exit(-1);
        }

        HttpMock(string baseUrl, bool strict = false)
        {
            Console.WriteLine($"Setting up HttpMock for {baseUrl}.\nFor unexpected failures, verify you are disposing this mock.");
            _baseUrl = baseUrl;
            _strict = strict;
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add(baseUrl);
                _httpListener.Start();
            }
            catch (HttpListenerException)
            {
                Dispose();
                throw;
            }
            ScheduleNext();
        }

        void HandleRequest(IAsyncResult task)
        {
            HttpListenerContext context = null;
            try
            {
                context = _httpListener.EndGetContext(task);
            }
            catch
            {
                // ignored
            }

            if (_httpListener.IsListening)
                ScheduleNext();

            if (context == null)
                return;

            ProcessRequest(context);
        }

        void ProcessRequest(HttpListenerContext context)
        {
            try
            {
                using (var stream = new StreamReader(context.Request.InputStream))
                {
                    var body = stream.ReadToEnd();
                    var match = GetMatch(context, body);

                    if (match != null)
                    {
                        match.RecordCall();
                        var response = match.GetResponse(context.Request, body);
                        context.Response.StatusCode = response.Status;

                        if (response.Headers != null)
                        {
                            foreach (var key in response.Headers.Keys)
                            context.Response.AddHeader(key, response.Headers[key]);
                        }

                        if (response.Body != null)
                        {
                            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response.Body));
                            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                        }
                    }
                    else if (_strict)
                    {
                        _failures.Add(new Exception($"Unmocked {context.Request.HttpMethod} to {context.Request.Url} was not set up"));
                    }
                }
            }
            catch (Exception e)
            {
                _failures.Add(e);
            }

            context.Response.Close();
        }

        EndpointHttpEndpointMockSetup GetMatch(HttpListenerContext context, string body)
        {
            var matches = _setups.Where(x => x.Matches(context.Request, body, _baseUrl)).ToList();

            if (matches.Count > 1)
            {
                _failures.Add(new Exception($"Multiple setups found matching call to {context.Request.Url}"));
            }

            return matches.SingleOrDefault();
        }


        void ScheduleNext()
        {
            try
            {
                _httpListener.BeginGetContext(HandleRequest, null);
            }
            catch (HttpListenerException)
            {

            }
        }

        public IHttpEndpointMockSetup Setup(string endpoint)
        {
            var setup = new EndpointHttpEndpointMockSetup(endpoint);
            _setups.Add(setup);
            return setup;
        }

        public void Dispose()
        {
            _legallyDisposed = true;
            ((IDisposable)_httpListener)?.Dispose();
            if (_failures.Any())
            {
                throw new InvalidOperationException(String.Join("\r\n", _failures.Select(x => x.ToString())));
            }

            var uncalled = _setups.Where(x => !x.Called && x.IsRequired).ToArray();
            if (uncalled.Any())
            {
                throw new InvalidOperationException(String.Join("\r\n", uncalled.Select(x => $"Expected a matching call to {_baseUrl}{x.Endpoint}")));
            }
        }

        public string BaseUrl => _baseUrl;
    }

    public interface IHttpEndpointMockSetup : IConfigurableHttpEndpointMockSetup
    {
        IConfigurableHttpEndpointMockSetup Get();
        IConfigurableHttpEndpointMockSetup Post();
        IConfigurableHttpEndpointMockSetup Put();
        IConfigurableHttpEndpointMockSetup Delete();
    }

    public interface IConfigurableHttpEndpointMockSetup
    {
        IConfigurableHttpEndpointMockSetup Handler<TBody>(Func<TBody, HttpListenerRequest, Response> handler);
        IConfigurableHttpEndpointMockSetup Handler<TBody>(Func<TBody, Response> handler);
        IConfigurableHttpEndpointMockSetup Handler(Func<HttpListenerRequest, Response> handler);
        IConfigurableHttpEndpointMockSetup Handler(Func<Response> handler);
        IConfigurableHttpEndpointMockSetup When(Func<HttpListenerRequest, bool> matcher);
        IConfigurableHttpEndpointMockSetup WhenBody<TBody>(Predicate<TBody> matcher);
        IConfigurableHttpEndpointMockSetup ReturnsBody(object body);
        IConfigurableHttpEndpointMockSetup ReturnsHeader(string header, params string[] headerValues);
        IConfigurableHttpEndpointMockSetup ReturnsCode(int code);
        IConfigurableHttpEndpointMockSetup Required();
        void ResetCalls();
        Task WaitForCall(int count = 1, int seconds = 15);
        bool Called { get; }
        int CallCount { get; }
    }

    public class Response
    {
        public Response(object body, int status)
        {
            Body = body;
            Status = status;
        }

        public Response(object body)
        {
            Body = body;
            Status = 200;
        }

        public Response(int status)
        {
            Status = status;
        }

        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>();
        public object Body { get; set; }
        public int Status { get; set; }
    }

    public partial class HttpMock
    {
        class EndpointHttpEndpointMockSetup : IHttpEndpointMockSetup
        {
            internal EndpointHttpEndpointMockSetup(string endpoint)
            {
                Endpoint = endpoint;
            }

            public string Endpoint { get; }
            Response Response { get; set; } = new Response(new object(), 200);
            Func<HttpListenerRequest, string, Response> ResponseFactory { get; set; }
            Func<HttpListenerRequest, bool> Matcher { get; set; }
            Func<string, bool> BodyMatcher { get; set; }
            HttpMethod Method { get; set; }
            public bool IsRequired { get; private set; }

            public void ResetCalls() => CallCount = 0;

            /// <summary>
            /// Wait for the setup endpoint to be invoked
            /// </summary>
            /// <param name="count">The number of times the endpoint should be called</param>
            /// <param name="seconds">How long to wait before timing out</param>
            /// <returns>A Task</returns>
            /// <exception cref="TimeoutException"></exception>
            public async Task WaitForCall(int count = 1, int seconds = 15)
            {
                for (var i = 0; i < seconds * 2; i++)
                {
                    if (CallCount >= count)
                        return;

                    await Task.Delay(500);
                }
                throw new TimeoutException($"Timed out waiting for {Method} call to {Endpoint}");
            }

            internal void RecordCall() => CallCount++;

            /// <summary>
            /// How many times the setup has been matched to HTTP calls
            /// </summary>
            public int CallCount { get; private set; }

            /// <summary>
            /// Whether the setup has been matched to a HTTP call
            /// </summary>
            public bool Called => CallCount > 0;

            internal Response GetResponse(HttpListenerRequest request, string json)
            {
                return ResponseFactory != null ? ResponseFactory(request, json) : Response;
            }

            /// <summary>
            /// Specify a handler that will use the request and request body to generate the response on-the-fly
            /// </summary>
            /// <param name="handler">The response factory</param>
            /// <typeparam name="TBody">The type to deserialize the request as</typeparam>
            public IConfigurableHttpEndpointMockSetup Handler<TBody>(Func<TBody, HttpListenerRequest, Response> handler)
            {
                ResponseFactory = (request, json) =>
                {
                    if (TryGetJson(json, out TBody body))
                    {
                        return handler(body, request);
                    }

                    throw new ArgumentException("Could not convert request body to type " + typeof(TBody).FullName);
                };
                return this;
            }

            /// <summary>
            /// Specify a handler that will use the request body to generate the response on-the-fly
            /// </summary>
            /// <param name="handler">The response factory</param>
            /// <typeparam name="TBody">The type to deserialize the request as</typeparam>
            public IConfigurableHttpEndpointMockSetup Handler<TBody>(Func<TBody, Response> handler)
            {
                ResponseFactory = (request, json) =>
                {
                    if (TryGetJson(json, out TBody body))
                    {
                        return handler(body);
                    }

                    throw new ArgumentException("Could not convert request body to type " + typeof(TBody).FullName);
                };
                return this;
            }

            /// <summary>
            /// Specify a handler that will use the request to generate the response on-the-fly
            /// </summary>
            /// <param name="handler">The response factory</param>
            public IConfigurableHttpEndpointMockSetup Handler(Func<HttpListenerRequest, Response> handler)
            {
                ResponseFactory = (request, json) => handler(request);
                return this;
            }

            /// <summary>
            /// Specify a handler that will generate the response on-the-fly
            /// </summary>
            /// <param name="handler">The response factory</param>
            public IConfigurableHttpEndpointMockSetup Handler(Func<Response> handler)
            {
                ResponseFactory = (request, json) => handler();
                return this;
            }

            /// <summary>
            /// Specify a predicate that should be applied to match requests
            /// </summary>
            /// <param name="matcher">The predicate</param>
            public IConfigurableHttpEndpointMockSetup When(Func<HttpListenerRequest, bool> matcher)
            {
                Matcher = matcher;
                return this;
            }

            /// <summary>
            /// Specify a predicate that should be applied to match request bodies
            /// </summary>
            /// <param name="matcher">The predicate</param>
            /// <typeparam name="T">The type to deserialize the body as</typeparam>
            public IConfigurableHttpEndpointMockSetup WhenBody<T>(Predicate<T> matcher)
            {
                BodyMatcher = request => TryGetJson(request, out T body) && matcher(body);
                return this;
            }

            /// <summary>
            /// Specify a header that should be returned when this setup is matched
            /// </summary>
            /// <param name="header">The name of the header</param>
            /// <param name="headerValues">One or more values to return</param>
            public IConfigurableHttpEndpointMockSetup ReturnsHeader(string header, params string[] headerValues)
            {
                Response.Headers.Add(header, string.Join(",", headerValues));
                return this;
            }

            /// <summary>
            /// Specify a response body that should be JSON serialized and returned when this setup is matched
            /// </summary>
            /// <param name="body">The body to return</param>
            public IConfigurableHttpEndpointMockSetup ReturnsBody(object body)
            {
                Response.Body = body;
                return this;
            }

            /// <summary>
            /// Specify the return code of this setup
            /// </summary>
            /// <param name="code">The HTTP status code to return</param>
            public IConfigurableHttpEndpointMockSetup ReturnsCode(int code)
            {
                Response.Status = code;
                return this;
            }

            /// <summary>
            /// Specify that an exceptions should be thrown if this setup is not called before the mock is disposed
            /// </summary>
            public IConfigurableHttpEndpointMockSetup Required()
            {
                IsRequired = true;
                return this;
            }

            /// <summary>
            /// Specify that this setup should only match GET requests
            /// </summary>
            public IConfigurableHttpEndpointMockSetup Get()
            {
                Method = HttpMethod.Get;
                return this;
            }

            /// <summary>
            /// Specify that this setup should only match POST requests
            /// </summary>
            public IConfigurableHttpEndpointMockSetup Post()
            {
                Method = HttpMethod.Post;
                return this;
            }

            /// <summary>
            /// Specify that this setup should only match PUT requests
            /// </summary>
            public IConfigurableHttpEndpointMockSetup Put()
            {
                Method = HttpMethod.Put;
                return this;
            }

            /// <summary>
            /// Specify that this setup should only match DELETE requests
            /// </summary>
            public IConfigurableHttpEndpointMockSetup Delete()
            {
                Method = HttpMethod.Delete;
                return this;
            }
            
            internal bool Matches(HttpListenerRequest request, string body, string baseUrl)
            {
                if (Method != null && Method.Method != request.HttpMethod)
                    return false;

                if (request.Url.AbsoluteUri.Substring(baseUrl.Length) != Endpoint)
                    return false;

                if (Matcher != null && !Matcher(request))
                    return false;

                if (BodyMatcher != null && !BodyMatcher(body))
                    return false;

                return true;
            }

            static bool TryGetJson<T>(string json, out T result)
            {
                try
                {
                    result = JsonConvert.DeserializeObject<T>(json);
                    return true;
                }
                catch
                {
                    result = default(T);
                    return false;
                }
            }
        }
    }
}
