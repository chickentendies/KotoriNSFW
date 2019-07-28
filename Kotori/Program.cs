using Kotori.Images;
using Kotori.Images.Danbooru;
using Kotori.Images.Gelbooru;
using Kotori.Images.Konachan;
using Kotori.Images.Yandere;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using Tweetinvi.Models;

namespace Kotori
{
    public static partial class Program
    {
        public static readonly Version Version = new Version(4, 2);

        public static readonly HttpClient HttpClient = new HttpClient();
        public static DatabaseManager Database { get; private set; }

        private static readonly CancellationTokenSource Cancellation = new CancellationTokenSource();

        public static readonly List<TwitterBot> Bots = new List<TwitterBot>();

        private static readonly object DownloadLock = new object();

        public static void Main(string[] args)
        {
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; Cancellation.Cancel(); };
            Logger.Write($@"Kotori v{Version}");

            if (Cancellation.IsCancellationRequested)
                return;

            Logger.Write(@"Creating database manager...");
            Database = new DatabaseManager();

            Logger.Write(@"Running migrations...");
            Database.RunMigrations();

            MigrateIni();

            if (Cancellation.IsCancellationRequested)
            {
                Database.Dispose();
                return;
            }

            if (string.IsNullOrEmpty(Database.ReadConfig(@"twitter_consumer_key")))
            {
                Logger.Write(@"Set up Twitter API keys...");
                Logger.Write(@"Please enter your Twitter Consumer Key:");
                Database.WriteConfig(@"twitter_consumer_key", Console.ReadLine());
                Logger.Write(@"And now your Twitter Consumer Secret:");
                Database.WriteConfig(@"twitter_consumer_secret", Console.ReadLine());
            }

            Logger.Write(@"Creating booru clients...");

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

            if (!Cancellation.IsCancellationRequested)
            {
                Logger.Write(@"Setting bots up...");
                lock (Bots)
                    foreach (TwitterBotInfo botInfo in Database.GetBots())
                    {
                        if (Cancellation.IsCancellationRequested)
                            break;

                        ITwitterCredentials creds;

                        if (string.IsNullOrEmpty(botInfo.AccessToken) || string.IsNullOrEmpty(botInfo.AccessTokenSecret))
                        {
                            IAuthenticationContext ac = TwitterClient.BeginCreateClient(cc);
                            Logger.Write(string.Empty);
                            Logger.Write($@"====> Authenticate {botInfo.Name} <====");
                            Logger.Write(ac.AuthorizationURL);
                            Logger.Write(@"Enter the pin code you received:");

                            string pin = Console.ReadLine();
                            creds = TwitterClient.EndCreateClient(ac, pin);
                        }
                        else creds = new TwitterCredentials(cc.ConsumerKey, cc.ConsumerSecret, botInfo.AccessToken, botInfo.AccessTokenSecret);

                        TwitterBot bot = new TwitterBot(
                            Database,
                            boorus,
                            botInfo,
                            new TwitterClient(creds)
                        );
                        bot.SaveCredentials();
                        Bots.Add(bot);
                    }
            }

            if (!Cancellation.IsCancellationRequested)
            {
                Logger.Write(@"Checking cache for all bots...");
                lock (Bots)
                    foreach (TwitterBot bot in Bots)
                    {
                        if (Cancellation.IsCancellationRequested)
                            break;

                        if (!bot.EnsureCacheReady())
                        {
                            Logger.Write($@"Refreshing cache for {bot.Name}, this may take a little bit...");
                            bot.RefreshCache();
                        }
                    }
            }

            if (!Cancellation.IsCancellationRequested)
            {
                Run();
#if DEBUG
                Console.ReadLine();
#endif
            }

            lock (Bots)
                foreach (TwitterBot bot in Bots)
                    bot.Dispose();

            Database.Dispose();
        }

        public static void Run()
        {
            lock (Bots)
            {
                foreach (TwitterBot bot in Bots)
                {
                    Logger.Write($@"Posting to Twitter from {bot.Name}");

                    IBooruPost randomPost = null;
                    IMedia media = null;

                    int tries = 0;

                    while (media == null && tries < 3)
                    {
                        ++tries;
                        Debug.WriteLine($@"Attempting to prepare media attempt {tries}");

                        if (randomPost != null) // if we're here, we probably failed once and we don't want to retry
                            bot.DeletePost(randomPost.PostId);

                        randomPost = bot.GetRandomPost();
                        Logger.Write(randomPost?.PostUrl ?? @"No posts available");

                        if (randomPost == null)
                            break;

                        HttpResponseMessage httpResponse;

                        try
                        {
                            lock (DownloadLock)
                            {
                                Debug.WriteLine($@"Downloading {randomPost.FileUrl}");
                                httpResponse = HttpClient.GetAsync(randomPost.FileUrl).Result;
                                Thread.Sleep(TwitterBot.REQUEST_INTERVAL);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Write(@"Error during request!");
                            Logger.Write(ex);
                            bot.DeletePost(randomPost.PostId);
                            media = null;
                            continue;
                        }

                        try
                        {
                            Debug.WriteLine($@"Uploading to Twitter");
                            using (Stream hrs = httpResponse.Content.ReadAsStreamAsync().Result)
                                media = bot.Client.UploadMedia(hrs);
                        }
                        catch (Exception ex)
                        {
                            Logger.Write(@"Error during stream handle!");
                            Logger.Write(ex);
                            bot.DeletePost(randomPost.PostId);
                            media = null;
                            continue;
                        }

                        httpResponse?.Dispose(); // fuck it

                        int seconds = 0;

                        while (!(media?.IsReadyToBeUsed ?? false))
                        {
                            Thread.Sleep(1000);
                            if (++seconds >= 5)
                            {
                                bot.DeletePost(randomPost.PostId);
                                media = null;
                                break;
                            }
                        }

                        if (media?.Data == null)
                            media = null;
                    }

                    if (media != null)
                        try
                        {
                            bot.Client.PostTweet(randomPost.PostUrl, media);
                            bot.DeletePost(randomPost.PostId);
                        }
                        catch (Exception ex)
                        {
#if DEBUG
                            throw ex;
#else
                            Logger.Write(ex);
#endif
                        }
                }
            }
        }
    }
}
