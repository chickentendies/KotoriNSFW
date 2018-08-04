using System.Net.Http;

namespace Kotori.Images.Gelbooru
{
    public sealed class Gelbooru : GelbooruBase<GelbooruPost>
    {
        public Gelbooru(HttpClient httpClient) : base(httpClient)
        {
        }
    }
}
