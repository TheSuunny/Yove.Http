using System;
using System.IO;
using System.Threading.Tasks;

using Fody;

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

    internal MemoryStream Stream { get; } = new();

    #endregion

    internal Content(HttpResponse response)
    {
        _response = response;
    }

    public async Task<string> ReadAsString()
    {
        if (_response.IsEmpytyBody)
            throw new HttpResponseException("Content not found.");

        MemoryStream outputStream = await _response.GetBodyContent();

        return _response.CharacterSet.GetString(outputStream.ToArray(), 0, (int)outputStream.Length);
    }

    public async Task<JToken> ReadAsJson()
    {
        string body = await ReadAsString();

        if (string.IsNullOrEmpty(body))
            throw new HttpResponseException("Content not found.");

        return JToken.Parse(body);
    }

    public async Task<MemoryStream> ReadAsStream()
    {
        if (_response.IsEmpytyBody)
            throw new HttpResponseException("Content not found.");

        return await _response.GetBodyContent();
    }

    public async Task<byte[]> ReadAsBytes()
    {
        MemoryStream outputStream = await ReadAsStream();

        return outputStream?.ToArray();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || IsDisposed)
            return;

        IsDisposed = true;

        Stream.Close();
        Stream.Dispose();
    }

    public void Dispose()
    {
        Dispose(true);

        GC.SuppressFinalize(this);
    }
}
