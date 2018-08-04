using Newtonsoft.Json;
using System.IO;

namespace Kotori.Images.Konachan
{
    public class KonachanPost : IBooruPost
    {
        [JsonProperty(@"id")]
        public string PostId { get; set; }

        public virtual string PostUrl { get => $@"https://konachan.com/post/show/{PostId}"; }

        [JsonProperty(@"file_url")]
        public string FileUrl { get; set; }

        [JsonProperty(@"md5")]
        public string FileHash { get; set; }

        public string FileExtension { get => Path.GetExtension(FileUrl).Trim('.'); }

        [JsonProperty(@"rating")]
        public string Rating { get; set; }
    }
}
