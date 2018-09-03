using Kotori.Images;
using Kotori.Images.Danbooru;
using Kotori.Images.Gelbooru;
using Kotori.Images.Konachan;
using Kotori.Images.Yandere;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using Tweetinvi.Models;

namespace Kotori
{
    public static partial class Program
    {
        public static readonly Version Version = new Version(4, 0);

        public static readonly HttpClient HttpClient = new HttpClient();
        public static DatabaseManager Database { get; private set; }

        private static readonly ManualResetEvent MRE = new ManualResetEvent(false);
        private static readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
        private static Timer Timer;

        public static TimeSpan Interval => TimeSpan.FromMinutes(15);
        public static TimeSpan UntilNextRun
        {
            get
            {
                DateTime now = DateTime.Now;
                return now.RoundUp(Interval) - now;
            }
        }

        public static readonly List<TwitterBot> Bots = new List<TwitterBot>();

        public static void Main(string[] args)
        {
            Cancellation.Token.Register(() => MRE.Set());
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; Cancellation.Cancel(); };
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            Console.WriteLine($@"Kotori v{Version}");
            Console.WriteLine(@"THis is still experimental, don't look at it.");

            CheckUpdates();

            LogHeader(@"Creating database manager...");
            Database = new DatabaseManager();

            LogHeader(@"Running migrations...");
            Database.RunMigrations();

            MigrateIni();

            if (string.IsNullOrEmpty(Database.ReadConfig(@"twitter_consumer_key")))
            {
                LogHeader(@"Set up Twitter API keys...");
                Console.WriteLine(@"Please enter your Twitter Consumer Key:");
                Database.WriteConfig(@"twitter_consumer_key", Console.ReadLine());
                Console.WriteLine(@"And now your Twitter Consumer Secret:");
                Database.WriteConfig(@"twitter_consumer_secret", Console.ReadLine());
            }

            LogHeader(@"Creating booru clients...");

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

            LogHeader(@"Creating bots...");

            lock (Bots)
                foreach (TwitterBotInfo botInfo in Database.GetBots())
                {
                    if (Cancellation.IsCancellationRequested)
                        break;

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

                    Bots.Add(new TwitterBot(
                        Database,
                        boorus,
                        botInfo,
                        new TwitterClient(creds)
                    ));
                }

            Console.WriteLine($@"Posting after {UntilNextRun}, then a new post will be made every {Interval}!");
            StartTimer(Run);
            MRE.WaitOne();

            lock (Bots)
                foreach (TwitterBot bot in Bots)
                    bot.Dispose();

            Database.Dispose();
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            DateTime now = DateTime.Now;
            StringBuilder sb = new StringBuilder();

            sb.Append($@"Kotori v{Version}");
#if DEBUG
            sb.Append(@" Debug Build");
#endif
            sb.AppendLine();

            sb.AppendLine($@"Unhandled exception on {now}");
            sb.AppendLine($@"Is Terminating: {e.IsTerminating}");
            sb.AppendLine();

            Exception ex = e.ExceptionObject as Exception;

            while (ex != null)
            {
                sb.Append(ex);
                sb.AppendLine();
                sb.AppendLine();
                ex = ex.InnerException;
            }

            string filename = $@"Kotori {now:yyyy-MM-dd HH.mm.ss}.log";
            File.WriteAllText(filename, sb.ToString());

            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine(@"An unhandled exception has occurred.");
            Console.WriteLine($@"The log has been saved to '{filename}'.");
            Console.WriteLine(@"Please send this file to Flashwave <me@flash.moe> so he can fix it!");
            Console.WriteLine(@"Press enter to exit...");
            Console.ResetColor();
            Console.ReadLine();
        }

        public static void ExitIfCancelled(int exitCode = 0)
        {
            if (Cancellation.IsCancellationRequested)
                Environment.Exit(exitCode);
        }

        public static void LogHeader(string header)
        {
            ExitIfCancelled();
            Console.WriteLine();
            Console.WriteLine(header);
        }

        public static void StartTimer(Action action)
        {
            StopTimer();
            Timer = new Timer(s => action.Invoke(), null, UntilNextRun, Interval);
        }

        public static void StopTimer()
        {
            Timer?.Dispose();
            Timer = null;
        }

        private static string[] AvailableUpdate = null;

        public static void CheckUpdates()
        {
            string[] updateLines = AvailableUpdate;

            if (updateLines == null)
            {
                LogHeader(@"Checking for updates...");

                try
                {
                    updateLines = HttpClient.GetAsync($@"https://flash.moe/kotori-version.txt?{DateTime.Now.Ticks}").Result.Content.ReadAsStringAsync().Result.Split('\n');
                }
                catch { }
            }

            if (updateLines == null)
                Console.WriteLine(@"Failed to check for updates.");
            else if (updateLines[0].Trim() != Version.ToString())
            {
                AvailableUpdate = updateLines;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.BackgroundColor = ConsoleColor.Black;
                Console.WriteLine($@"An update is available, the latest version is v{updateLines[0].Trim()}!");

                if (updateLines.Length > 1)
                    for (int i = 1; i < updateLines.Length; i++)
                        Console.WriteLine(updateLines[i].Trim());
                else Console.WriteLine(@"Check the download page or contact Flashwave <me@flash.moe> for more information.");

                Console.ResetColor();
            }
        }

        public static void Run()
        {
            lock (Bots)
            {
                LogHeader(@"Validating cache...");

                foreach (TwitterBot bot in Bots)
                {
                    if (Cancellation.IsCancellationRequested)
                        break;

                    if (!bot.EnsureCacheReady())
                    {
                        Console.WriteLine($@"Refreshing cache for {bot.Name}, this may take a little bit...");
                        bot.RefreshCache();
                    }
                }

                LogHeader(@"Making a post on all accounts...");

                foreach (TwitterBot bot in Bots)
                {
                    if (Cancellation.IsCancellationRequested)
                        break;

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
            }

            CheckUpdates();
        }
    }
}
