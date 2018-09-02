using Kotori.Images;
using Kotori.Images.Danbooru;
using Kotori.Images.Gelbooru;
using Kotori.Images.Konachan;
using Kotori.Images.Yandere;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Tweetinvi.Models;

// TODO: add cancellation check after every step
//       would make sense to also split actions up into functions

namespace Kotori
{
    public static partial class Program
    {
        public static readonly HttpClient HttpClient = new HttpClient();
        public static DatabaseManager Database { get; private set; }

        public static readonly ManualResetEvent MRE = new ManualResetEvent(false);
        public static readonly CancellationTokenSource Cancellation = new CancellationTokenSource();

        public static void Main(string[] args)
        {
            Cancellation.Token.Register(() => MRE.Set());
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; Cancellation.Cancel(); };

            Console.WriteLine(@"Kotori REDUX v1");
            Console.WriteLine(@"THis is still experimental, don't look at it.");

            Console.WriteLine(@"Creating database manager...");
            Database = new DatabaseManager();
            Console.WriteLine(@"Running migrations...");
            Database.RunMigrations();

            MigrateIni();

            if (string.IsNullOrEmpty(Database.ReadConfig(@"twitter_consumer_key")))
            {
                Console.WriteLine(@"Please enter your Twitter Consumer Key:");
                Database.WriteConfig(@"twitter_consumer_key", Console.ReadLine());
                Console.WriteLine(@"And now your Twitter Consumer Secret:");
                Database.WriteConfig(@"twitter_consumer_secret", Console.ReadLine());
            }

            Console.WriteLine();
            Console.WriteLine(@"Creating booru clients...");

            IBooru[] boorus = new IBooru[] {
                new Danbooru(HttpClient),
                new Gelbooru(HttpClient),
                new Safebooru(HttpClient),
                new Yandere(HttpClient),
                new Konachan(HttpClient),
            };
            IConsumerCredentials cc = new ConsumerCredentials(
                Database.ReadConfig(@"twitter_consumer_key"),
                Database.ReadConfig(@"twitter_consumer_secret")
            );
            List<TwitterBot> bots = new List<TwitterBot>();

            Console.WriteLine();
            Console.WriteLine(@"Creating bots...");

            foreach (TwitterBotInfo botInfo in Database.GetBots())
            {
                ITwitterCredentials creds;

                if (string.IsNullOrEmpty(botInfo.AccessToken) || string.IsNullOrEmpty(botInfo.AccessTokenSecret))
                {
                    IAuthenticationContext ac = TwitterClient.BeginCreateClient(cc);
                    Console.WriteLine();
                    Console.WriteLine($@"====> Authenticate {botInfo.Name} <====");
                    Console.WriteLine(ac.AuthorizationURL);
                    Console.WriteLine(@"Enter the pin code you received:");

                    string pin = Console.ReadLine();
                    creds = TwitterClient.EndCreateClient(ac, pin);
                }
                else creds = new TwitterCredentials(cc.ConsumerKey, cc.ConsumerSecret, botInfo.AccessToken, botInfo.AccessTokenSecret);

                bots.Add(new TwitterBot(
                    Database,
                    boorus,
                    botInfo,
                    new TwitterClient(creds)
                ));
            }

            Console.WriteLine();
            Console.WriteLine(@"Validating cache...");

            foreach (TwitterBot bot in bots)
                if (!bot.EnsureCacheReady())
                {
                    Console.WriteLine($@"Refreshing cache for {bot.Name}, this may take a little bit...");
                    bot.RefreshCache();
                }

            Console.WriteLine();
            Console.WriteLine(@"Making a post on all accounts...");

            foreach (TwitterBot bot in bots)
            {
                Console.WriteLine(bot.Name);
                IBooruPost randomPost = bot.GetRandomPost();
                IMedia media = null;

                Console.WriteLine(randomPost.PostUrl);

                HttpClient.GetAsync(randomPost.FileUrl).ContinueWith(get =>
                {
                    if (!get.IsCompletedSuccessfully)
                        return;

                    get.Result.Content.ReadAsStreamAsync().ContinueWith(bytes =>
                    {
                        if (!bytes.IsCompletedSuccessfully)
                            return;

                        media = bot.Client.UploadMedia(bytes.Result);
                    }).Wait();
                }).Wait();

                if (media == null)
                    continue;
                
                try
                {
                    bot.Client.PostTweet(randomPost.PostUrl, media);
                    bot.DeletePost(randomPost.PostId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    Cancellation.Cancel();
                    break;
                }
            }

            MRE.WaitOne();

            foreach (TwitterBot bot in bots)
                bot.Dispose();

            Database.Dispose();
            Console.ReadLine();
        }
    }
}
