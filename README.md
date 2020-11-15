# Yove.Http | Http Client / Http Framework

[![NuGet version](https://badge.fury.io/nu/Yove.Http.svg)](https://badge.fury.io/nu/Yove.Http)
[![Downloads](https://img.shields.io/nuget/dt/Yove.Http.svg)](https://www.nuget.org/packages/Yove.Http)
[![Target](https://img.shields.io/badge/.NET%20Standard-2.0-green.svg)](https://docs.microsoft.com/ru-ru/dotnet/standard/net-standard)

Nuget: https://www.nuget.org/packages/Yove.Http/

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
    Authorization = $"Bot {Token}", // Add Authorization header
    EnableAutoRedirect = false, // Disable automatic redirection if the server responded with a Location header
    EnableCookies = false, // Disable cookies
    EnableProtocolError = false, // Disable exceptions associated with server response
    EnableReconnect = false, // Disable reconnection in case of connection errors or data reading
    ReconnectDelay = 1000, // Delay in attempting a new connection
    ReconnectLimit = 3, // Maximum number of reconnection attempts
    UserAgent = HttpUtils.GenerateUserAgent() // Set Random User-Agent
})
{
    HttpResponse postResponse = await client.Post("https://example.com/", "name=value");

    JToken jsonContent = postResponse.Json["content"];

    string getResponse = await client.GetString("https://example.com/");

    JToken getJsonResponse = await client.GetJson("https://example.com/list.json");

    string jsonItem = (string)getJsonResponse["count"];
}
```

### Proxy Client

```csharp
HttpClient client = new HttpClient("Base URL")
{
    Proxy = new ProxyClient("195.208.172.70", 8080, ProxyType.Http),
    Proxy = new ProxyClient("195.208.172.70", 8080, ProxyType.Socks4),
    Proxy = new ProxyClient("195.208.172.70", 8080, ProxyType.Socks5),
    Proxy = new ProxyClient("195.208.172.70:8080", ProxyType.Http)
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
| `await client.GetToFile("http://example.com/", "Save path");`    | Makes a GET request and save file                             |
| `await client.Post("http://example.com/", "id=0&message=test");` | Simple POST request, supports up to 5 reload                  |
| `await client.Raw(HttpMethod.DELETE, "http://example.com/");`    | Raw method, can accept any parameters included in HttpContent |

### Upload or download events

```csharp
client.DownloadProgressChanged += (s, e) =>
{
    Console.WriteLine($"{e.Received} / {e.Total} | {e.ProgressPercentage} | {e.Speed.MegaBytes} MB/s | {e.Speed.GigaBytes} GB/s");
};

client.UploadProgressChanged += (s, e) =>
{
    Console.WriteLine($"{e.Sent} / {e.Total} | {e.ProgressPercentage} | {e.Speed.MegaBytes} MB/s | {e.Speed.GigaBytes} GB/s");
};
```

### Add header / Read header

```csharp
client.Headers.Add("Token", Token);
client.AddTempHeader("Token", Token); // This header will be deleted after the request

client["Token"] = Token;

HttpResponse postResponse = await client.Post("https://example.com/", "name=value");

string token = postResponse["Token"];
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

string body = response.Body; // Receives the response string body

string result = response.Parser("<h1>", "</h1>"); // Parsing HTML body

MemoryStream stream = await response.ToMemoryStream(); // Return the response in MemoryStream

byte[] bytes = await response.ToBytes(); // Return the response in byte[]

string savePath = await response.ToFile("Path to save", "Filename"); // If you do not specify a Filename, the client will try to find the file name, and save it, otherwise you will get an error

string username = (string)response.Json["users"][0]["username"]; // Return JToken object [JSON]
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


### Other

If you are missing something in the library, do not be afraid to write me :)

<thesunny@tuta.io>
