using System.Net.Http;

namespace Kotori.Images.Gelbooru
{
    public sealed class Safebooru : GelbooruBase<SafebooruPost>
    {
        protected override string Domain => @"safebooru.org";

        public Safebooru(HttpClient httpClient) : base(httpClient)
        {
        }
    }
}
