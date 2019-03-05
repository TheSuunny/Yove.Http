# Yove.Http - Http Client / Http Framework

[![NuGet version](https://badge.fury.io/nu/Yove.Http.svg)](https://badge.fury.io/nu/Yove.Http)
[![Downloads](https://img.shields.io/nuget/dt/Yove.Http.svg)](https://www.nuget.org/packages/Yove.Http)
[![Target](https://img.shields.io/badge/.NET%20Standard-2.0-green.svg)](https://docs.microsoft.com/ru-ru/dotnet/standard/net-standard)

Nuget: https://www.nuget.org/packages/Yove.Http/1.0.0

```
Install-Package Yove.Http -Version 1.0.0
```

```
dotnet add package Yove.Http --version 1.0.0
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

using constructor parameters

```csharp
HttpClient Client = new HttpClient
{
    Authorization = $"Bot {Token}", //Custom Authorization header
    EnableAutoRedirect = false, //Disable automatic redirection if the server responded with a Location header
    EnableCookies = false, //Disable cookies
    EnableProtocolError = false, //Disable exceptions associated with server response
    EnableReconnect = false, //Disable reconnection in case of connection errors or data reading
    ReconnectDelay = 1000, //Connection delay
    ReconnectLimit = 3, //Maximum number of reconnection attempts
    UserAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/108.0.3440.84 Safari/537.36" //Sets User Agent
};
```

### Create Request

| Link | README |
| ------ | ------ |
| ```await Client.Get("http://example.com/");``` | Simple GET request |
| ```await Client.GetBytes("http://example.com/");``` | Makes a GET request and returns a response byte[] |
| ```await Client.GetStream("http://example.com/");``` | Makes a GET request and returns a response MemoryStream |
| ```await Client.GetString("http://example.com/");``` | Makes a GET request and returns a response ToString |
| ```await Client.Post("http://example.com/", "id=0&message=test");``` | Simple POST request (supports up to 5 parameters) |

### Add header / Read header

```csharp
Client.Headers.Add("Token", Token);
```

or

```csharp
Client["Token"] = Token;

string Token = Response["Token"];
```

### Upload file [Multipart]

```csharp
MultipartContent Content = new MultipartContent()
{
    { "file", new FileContent("Path") }, //If you do not specify the filename, the client will transfer the filename from the path
    { "file", new FileContent("Path"), "Filename" },
    { "content", new StringContent("Message") }
};

HttpResponse Response = await Client.Post("http://example.com/", Content);
```

### Http Response

```csharp

HttpResponse Response = await Client.Get("http://example.com/");

string Body = Response.Body; //Receives the response body from the server

string Result = Response.Parser("<h1>", "</h1>"); //Parsing HTML data

MemoryStream Stream = Response.ToMemoryStream(); //Return the response in MemoryStream

byte[] Bytes = Response.ToBytes(); //Return the response in byte[]

string SavePath = Response.ToFile("Path to save", "Filename"); //If you do not specify a filename, the client will try to find the filename, and save it, otherwise you will get an error
```

___

### Methods

Supports both default requests and WebDAV

| Method | README |
| ------ | ------ |
| GET | Request a representation of the specified resource. |
| POST | Submit an entity to the specified resource, often causing a change in state or side effects on the server. |
| HEAD | Ask for a response identical to that of a `GET` request, but without the response body. |
| PUT | Replace all current representations of the target resource with the request payload. |
| DELETE | Delete the specified resource. |
| PATCH | Apply partial modifications to a resource. |
| OPTIONS | Describe the communication options for the target resource. |
| PROPFIND | Get object properties on the server in XML format. |
| PROPPATCH | Change properties in a single transaction. |
| MKCOL | Create a collection of objects (directory in the case of access to files). |
| COPY | Copy from one URI to another. |
| MOVE | Move from one URI to another. |
| LOCK | Put a lock on the object. |
| UNLOCK | Unlock a resource. |

___

### TODO

- [ ] - Proxy Client
- [ ] - Json Parser
- [ ] - Keep Alive

___

### Other

This project is not related to xYove or xNet.

If you are missing something in the library, do not be afraid to contact me :)

<yove@keemail.me>
