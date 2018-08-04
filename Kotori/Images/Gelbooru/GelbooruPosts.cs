using System.Xml.Serialization;

namespace Kotori.Images.Gelbooru
{
    [XmlRoot(@"posts")]
    public sealed class GelbooruPosts<T>
        where T : IBooruPost
    {
        [XmlElement(@"post")]
        public T[] Posts { get; set; }
    }
}
