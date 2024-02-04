using System;
using System.IO;
using System.Threading.Tasks;

using Fody;

using Microsoft.IO;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Yove.Http.Exceptions;

namespace Yove.Http;

[ConfigureAwait(false)]
public class Content : IDisposable
{
    #region Public

    public bool IsDisposed { get; set; }

    #endregion

    #region Private

    private HttpResponse _response { get; }

    #endregion

    #region Internal

    private readonly RecyclableMemoryStreamManager _streamManager = new();

    internal RecyclableMemoryStream Stream { get; set; }

    #endregion

    internal Content(HttpResponse response)
    {
        _response = response;
    }

    public async Task<string> ReadAsString()
    {
        if (_response.IsEmpytyBody)
            throw new HttpResponseException("Content not found.");
        else if (_response.ContentLength.HasValue && _response.ContentLength > int.MaxValue)
            throw new HttpResponseException("The data array is too large (Use ToFile).");

        RecyclableMemoryStream outputStream = await ReadAsStream() as RecyclableMemoryStream;

        if (outputStream.Length > int.MaxValue)
        {
            outputStream.Dispose();

            throw new HttpResponseException("The data array is too large (Use ToFile).");
        }

        byte[] buffer = outputStream.GetBuffer();

        return _response.CharacterSet.GetString(buffer, 0, buffer.Length);
    }

    public async Task<JToken> ReadAsJson()
    {
        string body = await ReadAsString();

        if (string.IsNullOrEmpty(body))
            throw new HttpResponseException("Content not found.");

        return JToken.Parse(body);
    }

    public async Task<T> ReadAsJson<T>()
    {
        string body = await ReadAsString();

        if (string.IsNullOrEmpty(body))
            throw new HttpResponseException("Content not found.");

        return JsonConvert.DeserializeObject<T>(body);
    }

    public async Task<byte[]> ReadAsBytes()
    {
        if (_response.IsEmpytyBody)
            throw new HttpResponseException("Content not found.");
        if (_response.ContentLength.HasValue && _response.ContentLength > int.MaxValue)
            throw new HttpResponseException("The data array is too large (Use ToFile).");

        RecyclableMemoryStream outputStream = await ReadAsStream() as RecyclableMemoryStream;

        if (outputStream.Length > int.MaxValue)
        {
            outputStream.Dispose();

            throw new HttpResponseException("The data array is too large (Use ToFile).");
        }

        return outputStream.GetBuffer();
    }

    public async Task<Stream> ReadAsStream()
    {
        if (_response.IsEmpytyBody)
            throw new HttpResponseException("Content not found.");

        Stream ??= _streamManager.GetStream();

        if (Stream.Length == 0)
        {
            await foreach (Memory<byte> source in _response.GetBodyContent())
                Stream.Write(source.ToArray(), 0, source.Length);
        }

        Stream.Seek(0, SeekOrigin.Begin);

        return Stream;
    }

    public async Task<string> ToFile(string filepath)
    {
        if (string.IsNullOrEmpty(filepath))
            throw new NullReferenceException("filepath is null or empty.");

        string path = Path.GetDirectoryName(filepath);
        string filename = Path.GetFileName(filepath);

        return await ToFile(path, filename);
    }

    public async Task<string> ToFile(string localPath, string filename = null)
    {
        if (_response.IsEmpytyBody)
            throw new NullReferenceException("Content not found.");

        if (string.IsNullOrEmpty(localPath))
            throw new NullReferenceException("Path is null or empty.");

        if (filename == null)
        {
            if (_response.Headers["Content-Disposition"] != null)
            {
                filename = $"{localPath.TrimEnd('/')}/{HttpUtils.Parser("filename=\"", _response.Headers["Content-Disposition"], "\"")}";
            }
            else
            {
                filename = Path.GetFileName(new Uri(_response.Address.AbsoluteUri).LocalPath);

                if (string.IsNullOrEmpty(filename))
                    throw new NullReferenceException("Could not find filename.");
            }
        }

        string outputPath = Path.Join(localPath.TrimEnd('/'), filename);

        Stream ??= _streamManager.GetStream();

        using FileStream fileStream = new(outputPath, FileMode.OpenOrCreate);

        if (Stream.Length == 0)
        {
            await foreach (Memory<byte> source in _response.GetBodyContent())
                await fileStream.WriteAsync(source);
        }
        else
        {
            Stream.Seek(0, SeekOrigin.Begin);

            await Stream.CopyToAsync(fileStream);
        }

        return outputPath;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || IsDisposed)
            return;

        IsDisposed = true;

        Stream?.Close();
        Stream?.Dispose();
    }

    public void Dispose()
    {
        Dispose(true);

        GC.SuppressFinalize(this);
    }
}
