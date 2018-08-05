namespace Kotori.Images
{
    public sealed class DatabaseBooruPost : IBooruPost
    {
        public string PostId { get; set; }

        public string PostUrl { get; set; }

        public string FileUrl { get; set; }

        public string FileHash { get; set; }

        public string FileExtension { get; set; }

        public string Rating { get; set; }
    }
}
