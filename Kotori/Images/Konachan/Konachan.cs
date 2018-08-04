using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace Kotori.Images.Konachan
{
    public sealed class Konachan : IBooru
    {
        public readonly HttpClient HttpClient;
        public readonly bool Worksafe;

        private const string R18_DOMAIN = @"konachan.com";
        private const string WS_DOMAIN = @"konachan.net";

        public Konachan(HttpClient httpClient, bool worksafe = true)
        {
            HttpClient = httpClient;
            Worksafe = worksafe;
        }

        public int MaxPostsPerPage => 100;

        public IEnumerable<IBooruPost> GetPosts(string[] tags, int page = 0, int limit = 10)
        {
            page += 1; // konachan's pagination starts at 1
            IEnumerable<IBooruPost> output = null;
            string domain = Worksafe ? WS_DOMAIN : R18_DOMAIN;

            HttpClient.GetAsync($@"https://{domain}/post.json?limit={limit}&page={page}&tags={string.Join(@"+", tags)}").ContinueWith(hrm => {
                if (!hrm.IsCompletedSuccessfully)
                    return;

                hrm.Result.Content.ReadAsStringAsync().ContinueWith(res => {
                    if (Worksafe)
                        output = JsonConvert.DeserializeObject<List<KonachanWorksafePost>>(res.Result);
                    else
                        output = JsonConvert.DeserializeObject<List<KonachanPost>>(res.Result);
                }).Wait();
            }).Wait();

            if (output?.Count() < 1)
                return null;

            return output;
        }
    }
}
