# HttpMock

`HttpMock` provides Moq-like mocking of HTTP endpoints.

## Usage

```C#
using(var mock = HttpMock.Create())
{
    mock.Setup("endpoint")
        .Get()
        .ReturnsBody("some body");

    var client = new WebClient();
    var result = await client.DownloadStringTaskAsync(mock.BaseUrl + "endpoint");
    //result == "some body"
}
```
