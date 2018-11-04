using System;
using System.Net;
using System.Numerics;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Xunit;

namespace HttpMock.Tests
{
    public class HttpMockTests : IDisposable
    {
        HttpMock _mock;
        
        public HttpMockTests() => _mock = HttpMock.Create();
        
        public void Dispose() => _mock.Dispose();

        [Fact]
        public async Task Replies_To_Get_As_Instructed()
        {
            var vector = new Vector2(1, 2);
            _mock.Setup("endpoint").ReturnsBody(vector);

            WebClient client = new WebClient();
            var result = await client.DownloadStringTaskAsync(_mock.BaseUrl + "endpoint");

            var resultingVector = JsonConvert.DeserializeObject<Vector2>(result);
            Assert.Equal(vector, resultingVector);
        }

        [Fact]
        public async Task Filters_By_Request_Method()
        {
            var vector = new Vector2(1, 2);
            _mock.Setup("endpoint").Post();
            _mock.Setup("endpoint").Get().ReturnsBody(vector);

            WebClient client = new WebClient();
            var result = await client.DownloadStringTaskAsync(_mock.BaseUrl + "endpoint");

            var resultingVector = JsonConvert.DeserializeObject<Vector2>(result);
            Assert.Equal(vector, resultingVector);
        }

        [Fact]
        public async Task Filters_By_Request_When_Lambda()
        {
            var vector = new Vector2(1, 2);
            _mock.Setup("endpoint").When(x => x.Headers["cake"] == "not nice");
            _mock.Setup("endpoint").ReturnsBody(vector).When(x => x.Headers["cake"] == "nice");

            WebClient client = new WebClient();
            client.Headers.Add("cake", "nice");
            var result = await client.DownloadStringTaskAsync(_mock.BaseUrl + "endpoint");

            var resultingVector = JsonConvert.DeserializeObject<Vector2>(result);
            Assert.Equal(vector, resultingVector);
        }

        [Fact]
        public async Task Filters_By_Request_When_Body_Lambda()
        {
            var vector1 = new Vector2(1, 0);
            var vector2 = new Vector2(2, 0);

            _mock.Setup("endpoint").WhenBody<Vector2>(x => x.X == 1).ReturnsBody(vector1);
            _mock.Setup("endpoint").WhenBody<Vector2>(x => x.X == 2).ReturnsBody(vector2);

            WebClient client = new WebClient();
            var result1 = await client.UploadStringTaskAsync(_mock.BaseUrl + "endpoint",
                JsonConvert.SerializeObject(vector1));
            var result2 = await client.UploadStringTaskAsync(_mock.BaseUrl + "endpoint",
                JsonConvert.SerializeObject(vector2));

            Assert.Equal(vector1, JsonConvert.DeserializeObject<Vector2>(result1));
            Assert.Equal(vector2, JsonConvert.DeserializeObject<Vector2>(result2));
        }

        [Fact]
        public async Task Generates_Response_From_Function()
        {
            var vector1 = new Vector2(1, 0);
            var vector2 = new Vector2(2, 0);
            var vector3 = new Vector2(3, 0);
            var vector4 = new Vector2(4, 0);

            _mock.Setup("endpoint").WhenBody<Vector2>(x => x.X == 1).Handler<Vector2>(vec => new Response("hahahaha"));
            _mock.Setup("endpoint").WhenBody<Vector2>(x => x.X == 2).Handler<Vector2>((vec, request) => new Response("blahblahblah"));
            _mock.Setup("endpoint").WhenBody<Vector2>(x => x.X == 3).Handler(request => new Response("333"));
            _mock.Setup("endpoint").WhenBody<Vector2>(x => x.X == 4).Handler(() => new Response("4444"));

            var client = new WebClient();
            var result1 = await client.UploadStringTaskAsync(_mock.BaseUrl + "endpoint", JsonConvert.SerializeObject(vector1));
            var result2 = await client.UploadStringTaskAsync(_mock.BaseUrl + "endpoint", JsonConvert.SerializeObject(vector2));
            var result3 = await client.UploadStringTaskAsync(_mock.BaseUrl + "endpoint", JsonConvert.SerializeObject(vector3));
            var result4 = await client.UploadStringTaskAsync(_mock.BaseUrl + "endpoint", JsonConvert.SerializeObject(vector4));

            Assert.Equal("hahahaha", JsonConvert.DeserializeObject<string>(result1));
            Assert.Equal("blahblahblah", JsonConvert.DeserializeObject<string>(result2));
            Assert.Equal("333", JsonConvert.DeserializeObject<string>(result3));
            Assert.Equal("4444", JsonConvert.DeserializeObject<string>(result4));
        }

        [Fact]
        public void Generates_Response_With_Headers()
        {
            var random = new Random();
            var key = random.Next().ToString();
            var value = random.Next().ToString();
            _mock.Setup("endpoint").ReturnsHeader(key, value);

            WebRequest request = WebRequest.Create(_mock.BaseUrl + "endpoint");
            request.Method = "GET";
            var result = request.GetResponse();

            Assert.Equal(value, result.Headers[key]);
        }

        [Fact]
        public void Generates_Response_For_Headers_With_Multiple_HeaderValues()
        {
            var random = new Random();
            var key = random.Next().ToString();
            var value1 = random.Next().ToString();
            var value2 = random.Next().ToString();
            _mock.Setup("endpoint").ReturnsHeader(key, value1, value2);

            WebRequest request = WebRequest.Create(_mock.BaseUrl + "endpoint");
            request.Method = "GET";
            var result = request.GetResponse();

            Assert.Equal(value1 + "," + value2, result.Headers[key]);
        }

        [Fact]
        public void Explodes_If_Required_Call_Is_Not_Made_Before_Disposal()
        {
            var mock = HttpMock.Create();
            mock.Setup("endpoint").Required();

            var exception = Assert.Throws<InvalidOperationException>(() => mock.Dispose());
            Assert.Contains($"Expected a matching call to {mock.BaseUrl}endpoint", exception.ToString());
        }

        [Fact]
        public async Task Explodes_If_Multiple_Matches()
        {
            var mock = HttpMock.Create();
            mock.Setup("endpoint");
            mock.Setup("endpoint").When(x => true);

            var client = new WebClient();
            await client.DownloadStringTaskAsync(mock.BaseUrl + "endpoint");

            var exception = Assert.Throws<InvalidOperationException>(() => mock.Dispose());
            Assert.Contains($"Multiple setups found matching call to {mock.BaseUrl}endpoint", exception.ToString());
        }

        [Fact]
        public async Task Explodes_With_Message_If_Handler_Explodes()
        {
            var mock = HttpMock.Create();
            mock.Setup("endpoint").Handler(x => throw new Exception("death to endpoint!"));
            mock.Setup("otherendpoint").When(x => throw new Exception("death to otherendpoint!"));

            var client = new WebClient();
            await client.DownloadStringTaskAsync(mock.BaseUrl + "endpoint");
            await client.DownloadStringTaskAsync(mock.BaseUrl + "otherendpoint");

            var exception = Assert.Throws<InvalidOperationException>(() => mock.Dispose());
            Assert.Contains("death to otherendpoint!", exception.ToString());
            Assert.Contains("death to endpoint!", exception.ToString());
        }

        [Fact]
        public async Task Explodes_With_Message_If_Strict_And_Unexpected()
        {
            var mock = HttpMock.Create(strict: true);

            var client = new WebClient();
            await client.DownloadStringTaskAsync(mock.BaseUrl + "endpoint");

            var exception = Assert.Throws<InvalidOperationException>(() => mock.Dispose());
            Assert.Contains($"Unmocked GET to {mock.BaseUrl}endpoint was not set up", exception.ToString());
        }

        [Fact]
        public void Uses_BaseUrl()
        {
            using (var mock = HttpMock.Create("crap"))
            {
                Assert.Contains("/crap", mock.BaseUrl);
                Assert.Contains("http://localhost", mock.BaseUrl);
            }
        }
    }
}
