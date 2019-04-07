# Yove.Http - Http Client / Http Framework

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
___

### Create HttpClient

```csharp
HttpClient Client = new HttpClient();

Client.UserAgent = HttpUtils.GenerateUserAgent(); //Full random (Linux, Windows, Mac, ChromeOS) / (Chrome, Firefox, Opera, Edge, Safari)
Client.UserAgent = HttpUtils.GenerateUserAgent(HttpSystem.Linux); //Partial random (Linux) / (Chrome, Firefox, Opera, Edge, Safari)
Client.UserAgent = HttpUtils.GenerateUserAgent(HttpBrowser.Firefox); //Partial random (Linux, Windows, Mac, ChromeOS) / (Firefox)
Client.UserAgent = HttpUtils.GenerateUserAgent(HttpSystem.Windows, HttpBrowser.Chrome); //No random (Windows) / (Chrome)
```

or

```csharp
HttpClient Client = new HttpClient
{
    Authorization = $"Bot {Token}", //Add Authorization header
    EnableAutoRedirect = false, //Disable automatic redirection if the server responded with a Location header
    EnableCookies = false, //Disable cookies
    EnableProtocolError = false, //Disable exceptions associated with server response
    EnableReconnect = false, //Disable reconnection in case of connection errors or data reading
    ReconnectDelay = 1000, //Delay in attempting a new connection
    ReconnectLimit = 3, //Maximum number of reconnection attempts
    UserAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/108.0.3440.84 Safari/537.36" //Sets User Agent
};
```

### Proxy Client

```
HttpClient Client = new HttpClient
{
    Proxy = new ProxyClient("195.208.172.70", 8080, ProxyType.Http),
    Proxy = new ProxyClient("195.208.172.70", 8080, ProxyType.Socks4),
    Proxy = new ProxyClient("195.208.172.70", 8080, ProxyType.Socks5),
    Proxy = new ProxyClient("195.208.172.70:8080", ProxyType.Http),
};
```

### Create Request

| Link | README |
| ------ | ------ |
| ```await Client.Get("http://example.com/");``` | Simple GET request |
| ```await Client.GetBytes("http://example.com/");``` | Makes a GET request and returns a response byte[] |
| ```await Client.GetStream("http://example.com/");``` | Makes a GET request and returns a response MemoryStream |
| ```await Client.GetString("http://example.com/");``` | Makes a GET request and returns a response ToString |
| ```await Client.Post("http://example.com/", "id=0&message=test");``` | Simple POST request, supports up to 5 reload |
| ```await Client.Raw(HttpMethod.DELETE, "http://example.com/");``` | Raw method, can accept any parameters included in HttpContent |

### Add header / Read header

```csharp
Client.Headers.Add("Token", Token);

Client["Token"] = Token;

string Token = Response["Token"];
```

### Upload file [Multipart]

```csharp
MultipartContent Content = new MultipartContent
{
    { "file", new FileContent("Path") }, //If you do not specify the file name, the client will transfer the file name from the path
    { "file", new FileContent("Path"), "Filename" },
    { "content", new StringContent("Message") }.
    { "document", new FileContent(Stream), "Test.txt" }
};

HttpResponse Response = await Client.Post("http://example.com/", Content);
```

### Http Response

```csharp

HttpResponse Response = await Client.Get("http://example.com/");

string Body = Response.Body; //Receives the response body from the server

string Result = Response.Parser("<h1>", "</h1>"); //Parsing HTML data

MemoryStream Stream = await Response.ToMemoryStream(); //Return the response in MemoryStream

byte[] Bytes = await Response.ToBytes(); //Return the response in byte[]

string SavePath = await Response.ToFile("Path to save", "Filename"); //If you do not specify a Filename, the client will try to find the file name, and save it, otherwise you will get an error
```

___

### Methods

Supports both default requests and WebDAV

| Method | README |
| ------ | ------ |
| GET | Used to query the contents of the specified resource |
| POST | Used to transfer user data to a specified resource |
| HEAD | Used to extract metadata |
| PUT | Used to load the contents of the request to the URI specified in the request |
| DELETE | Deletes the specified resource |
| PATCH | Similar to PUT, but applies only to a resource fragment |
| OPTIONS | Used to determine web server capabilities or connection settings for a specific resource |
| PROPFIND | Getting object properties on the server in XML format |
| PROPPATCH | Change properties in a single transaction |
| MKCOL | Create a collection of objects (directory in the case of access to files) |
| COPY | Copy from one URI to another |
| MOVE | Move from one URI to another |
| LOCK | Put a lock on the object |
| UNLOCK | Unlock a resource |

___

### TODO

- [x] - Proxy Client
- [ ] - Json Parser
- [ ] - Keep Alive

___

### Other

This project is not related to xYove or xNet.

If you are missing something in the library, do not be afraid to write me :)

<yove@keemail.me>