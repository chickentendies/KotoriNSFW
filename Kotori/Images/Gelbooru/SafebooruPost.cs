using System.IO;
using System.Xml.Serialization;

namespace Kotori.Images.Gelbooru
{
    public sealed class SafebooruPost : IBooruPost
    {
        [XmlAttribute(@"id")]
        public string PostId { get; set; }

        public string PostUrl => $@"https://safebooru.org/index.php?page=post&s=view&id={PostId}";

        private string FileUrlValue;

        [XmlAttribute(@"file_url")]
        public string FileUrl
        {
            get => FileUrlValue;
            set => FileUrlValue = @"https:" + value;
        }

        [XmlAttribute(@"md5")]
        public string FileHash { get; set; }

        public string FileExtension { get => Path.GetExtension(FileUrl).Trim('.'); }

        [XmlAttribute(@"rating")]
        public string Rating { get; set; }
    }
}
