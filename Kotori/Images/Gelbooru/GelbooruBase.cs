using System.Collections.Generic;
using System.Net.Http;
using System.Xml;
using System.Xml.Serialization;

namespace Kotori.Images.Gelbooru
{
    public abstract class GelbooruBase<T> : IBooru
        where T : IBooruPost
    {
        private readonly HttpClient HttpClient;

        protected virtual string Domain => @"gelbooru.com";

        public GelbooruBase(HttpClient httpClient)
        {
            HttpClient = httpClient;
        }

        public int MaxPostsPerPage => 100;

        public IEnumerable<IBooruPost> GetPosts(string[] tags, int page = 0, int limit = 10)
        {
            GelbooruPosts<T> output = null;
            XmlSerializer serialiser = new XmlSerializer(typeof(GelbooruPosts<T>));

            HttpClient.GetAsync($@"https://{Domain}/index.php?page=dapi&s=post&q=index&limit={limit}&pid={page}&tags={string.Join(@"+", tags)}").ContinueWith(hrm => {
                if (!hrm.IsCompletedSuccessfully)
                    return;

                hrm.Result.Content.ReadAsStreamAsync().ContinueWith(res => {
                    using (XmlReader xr = XmlReader.Create(res.Result))
                        output = (GelbooruPosts<T>)serialiser.Deserialize(xr);
                }).Wait();
            }).Wait();

            if (output?.Posts == null)
                return null;

            return output.Posts as IEnumerable<IBooruPost>;
        }
    }
}
