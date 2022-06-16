namespace Yove.Http.Models
{
    public record RedirectItem
    {
        public string From { get; set; }
        public string To { get; set; }
        public int StatusCode { get; set; }
        public long? Length { get; set; }
        public string ContentType { get; set; }
    }
}
