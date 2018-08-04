using Newtonsoft.Json;

namespace Kotori.Images.Yandere
{
    public sealed class YanderePost : IBooruPost
    {
        [JsonProperty(@"id")]
        public string PostId { get; set; }

        public string PostUrl { get => $@"https://yande.re/post/show/{PostId}"; }

        [JsonProperty(@"file_url")]
        public string FileUrl { get; set; }

        [JsonProperty(@"md5")]
        public string FileHash { get; set; }

        [JsonProperty(@"file_ext")]
        public string FileExtension { get; set; }

        [JsonProperty(@"rating")]
        public string Rating { get; set; }
    }
}
