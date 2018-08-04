using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net.Http;

namespace Kotori.Images.Yandere
{
    public sealed class Yandere : IBooru
    {
        public readonly HttpClient HttpClient;
        
        public Yandere(HttpClient httpClient)
        {
            HttpClient = httpClient;
        }

        public int MaxPostsPerPage => 100;

        public IEnumerable<IBooruPost> GetPosts(string[] tags, int page = 0, int limit = 10)
        {
            page += 1; // yandere's pagination starts at 1
            List<YanderePost> output = null;

            HttpClient.GetAsync($@"https://yande.re/post.json?limit={limit}&page={page}&tags={string.Join(@"+", tags)}").ContinueWith(hrm => {
                if (!hrm.IsCompletedSuccessfully)
                    return;

                hrm.Result.Content.ReadAsStringAsync().ContinueWith(res => {
                    output = JsonConvert.DeserializeObject<List<YanderePost>>(res.Result);
                }).Wait();
            }).Wait();

            if (output?.Count < 1)
                return null;

            return output;
        }
    }
}
