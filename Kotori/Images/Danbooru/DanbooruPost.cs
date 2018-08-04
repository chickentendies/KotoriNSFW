using Newtonsoft.Json;

namespace Kotori.Images.Danbooru
{
    public sealed class DanbooruPost : IBooruPost
    {
        [JsonProperty(@"id")]
        public string PostId { get; set; }

        public string PostUrl { get => $@"https://danbooru.donmai.us/posts/{PostId}"; }

        [JsonProperty(@"file_url")]
        public string FileUrl { get; set; }

        [JsonProperty(@"md5")]
        public string FileHash { get; set; }

        [JsonProperty(@"file_ext")]
        public string FileExtension { get; set; }

        [JsonProperty(@"rating")]
        public string Rating { get; set; }

        [JsonProperty(@"is_banned")]
        public bool IsBanned { get; set; }

        [JsonProperty(@"is_deleted")]
        public bool IsDeleted { get; set; }
    }
}
