using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace Kotori.Images.Gelbooru
{
    public sealed class GelbooruPost : IBooruPost
    {
        [XmlAttribute(@"id")]
        public string PostId { get; set; }

        public string PostUrl => $@"https://gelbooru.com/index.php?page=post&s=view&id={PostId}";

        [XmlAttribute(@"file_url")]
        public string FileUrl { get; set; }

        [XmlAttribute(@"md5")]
        public string FileHash { get; set; }

        public string FileExtension { get => Path.GetExtension(FileUrl).Trim('.'); }

        [XmlAttribute(@"rating")]
        public string Rating { get; set; }
    }
}
