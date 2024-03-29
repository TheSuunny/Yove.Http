# Yove.Http | Http Client / Http Framework

[![NuGet version](https://badge.fury.io/nu/Yove.Http.svg)](https://badge.fury.io/nu/Yove.Http)
[![Downloads](https://img.shields.io/nuget/dt/Yove.Http.svg)](https://www.nuget.org/packages/Yove.Http)

Nuget: https://www.nuget.org/packages/Yove.Http

```
Install-Package Yove.Http
```

```
dotnet add package Yove.Http
```

---

### Example HttpClient

```csharp
using(HttpClient client = new HttpClient
{
    Authorization = $"Bearer {Token}", // Add Authorization header
    EnableAutoRedirect = false, // Disable automatic redirection if the server responded with a Location header
    EnableCookies = false, // Disable cookies
    EnableProtocolError = false, // Disable exceptions associated with server response
    EnableReconnect = false, // Disable reconnection in case of connection errors or data reading
    ReconnectDelay = 1000, // Delay in attempting a new connection
    ReconnectLimit = 3, // Maximum number of reconnection attempts
    UserAgent = HttpUtils.GenerateUserAgent() // Set Random User-Agent,
    MaxReciveBufferSize = 3147483647 // Maximum size of the response body.
})
{
    HttpResponse postResponse = await client.Post("https://example.com/", "name=value");

    string getResponse = await client.GetString("https://example.com/");

    JToken getJsonResponse = await client.GetJson("https://example.com/list.json");
    Modal getJsonResponse = await client.GetJson<Modal>("https://example.com/list.json");
}
```

### Proxy Client

```csharp
HttpClient client = new HttpClient("Base URL")
{
    Proxy = new HttpProxy("195.208.172.70", 8080),
    Proxy = new Socks4Proxy("195.208.172.70", 8080)
    {
        UserId = "jfdafjnasjkfkjnsa"
    },
    Proxy = new Socks5Proxy("195.208.172.70", 8080)
    {
        Username = "admin",
        Password = "123456"
    },
    Proxy = new HttpProxy("195.208.172.70:8080")
    {
        Username = "admin",
        Password = "123456"
    }
};
```

### Create Request

| Link                                                             | README                                                        |
| ---------------------------------------------------------------- | ------------------------------------------------------------- |
| `await client.Get("http://example.com/");`                       | Simple GET request                                            |
| `await client.GetBytes("http://example.com/");`                  | Makes a GET request and returns a response byte[]             |
| `await client.GetStream("http://example.com/");`                 | Makes a GET request and returns a response MemoryStream       |
| `await client.GetString("http://example.com/");`                 | Makes a GET request and returns a response ToString           |
| `await client.GetJson("http://example.com/");`                   | Makes a GET request and returns a response JToken [JSON]      |
| `await client.GetJson<T>("http://example.com/");`                | Makes a GET request and returns a response T [Object]         |
| `await client.Post("http://example.com/", "id=0&message=test");` | Simple POST request, supports up to 5 reload                  |
| `await client.Raw(HttpMethod.DELETE, "http://example.com/");`    | Raw method, can accept any parameters included in HttpContent |

### Upload or download events

```csharp
client.DownloadProgressChanged += (s, e) =>
{
    Console.WriteLine($"{e.Received} / {e.Total} | {e.ProgressPercentage}");
};

client.UploadProgressChanged += (s, e) =>
{
    Console.WriteLine($"{e.Sent} / {e.Total} | {e.ProgressPercentage}");
};
```

### Add header / Read header

```csharp
client.Headers.Add("Token", Token);
client.AddTempHeader("Token", Token); // This header will be deleted after the request

client["Token"] = Token;

HttpResponse response = await client.Post("https://example.com/", "name=value");

string token = response["Token"];
```

### Upload file [Multipart]

```csharp
MultipartContent content = new MultipartContent
{
    { "file", new FileContent("Path") }, // If you do not specify the file name, the client will transfer the file name from the path
    { "file", new FileContent("Path"), "Filename" },
    { "content", new StringContent("Message") },
    { "document", new FileContent(Stream), "Test.txt" }
};

HttpResponse uploadResponse = await client.Post("http://example.com/", content);
```

### Http Response

```csharp
HttpResponse response = await Client.Get("http://example.com/");

string body = await response.Content.ReadAsString();
MemoryStream stream = await response.Content.ReadAsStream();
byte[] bytes = await response.Content.ReadAsBytes();
JToken json = await response.Content.ReadAsJson();
Modal json = await response.Content.ReadAsJson<Modal>();

string path = await response.Content.ToFile("/etc/filename.pack");
string path = await response.Content.ToFile("Path to save", "Filename"); // If you do not specify a Filename, the client will try to find the file name, and save it, otherwise you will get an error
```

---

### Methods

Supports both default requests and WebDAV

| Method    | README                                                                                   |
| --------- | ---------------------------------------------------------------------------------------- |
| GET       | Used to query the contents of the specified resource                                     |
| POST      | Used to transfer user data to a specified resource                                       |
| HEAD      | Used to extract metadata                                                                 |
| PUT       | Used to load the contents of the request to the URI specified in the request             |
| DELETE    | Deletes the specified resource                                                           |
| PATCH     | Similar to PUT, but applies only to a resource fragment                                  |
| OPTIONS   | Used to determine web server capabilities or connection settings for a specific resource |
| PROPFIND  | Getting object properties on the server in XML format                                    |
| PROPPATCH | Change properties in a single transaction                                                |
| MKCOL     | Create a collection of objects (directory in the case of access to files)                |
| COPY      | Copy from one URI to another                                                             |
| MOVE      | Move from one URI to another                                                             |
| LOCK      | Put a lock on the object                                                                 |
| UNLOCK    | Unlock a resource                                                                        |

### TODO

- [] HtmlParser to Regex