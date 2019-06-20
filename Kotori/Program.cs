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
using System.Text;
using System.Threading;
using Tweetinvi.Models;

namespace Kotori
{
    public static partial class Program
    {
        public static readonly Version Version = new Version(4, 1);

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

        private static readonly object LogLock = new object();
        private static readonly object DownloadLock = new object();

        public static void Log(object log = null, ConsoleColor fg = ConsoleColor.Gray, ConsoleColor bg = ConsoleColor.Black)
        {
            lock (LogLock)
            {
                Console.BackgroundColor = bg;
                Console.ForegroundColor = fg;
                Console.WriteLine(log);
                Console.ResetColor();
            }
        }

        public static void Main(string[] args)
        {
            Cancellation.Token.Register(() => MRE.Set());
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; Cancellation.Cancel(); };
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            Log($@"Kotori v{Version}", ConsoleColor.Green);
            Log(@"THis is still experimental, don't look at it.");

            LogHeader(@"Creating database manager...");
            Database = new DatabaseManager();

            LogHeader(@"Running migrations...");
            Database.RunMigrations();

            MigrateIni();

            if (string.IsNullOrEmpty(Database.ReadConfig(@"twitter_consumer_key")))
            {
                LogHeader(@"Set up Twitter API keys...");
                Log(@"Please enter your Twitter Consumer Key:");
                Database.WriteConfig(@"twitter_consumer_key", Console.ReadLine());
                Log(@"And now your Twitter Consumer Secret:");
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
                        Log();
                        Log($@"====> Authenticate {botInfo.Name} <====", ConsoleColor.Yellow);
                        Log(ac.AuthorizationURL);
                        Log(@"Enter the pin code you received:");

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

            LogHeader(@"Checking cache for all bots...");
            lock (Bots)
                foreach (TwitterBot bot in Bots)
                    if (!bot.EnsureCacheReady())
                    {
                        Log($@"Refreshing cache for {bot.Name}, this may take a little bit...");
                        bot.RefreshCache();
                    }

#if DEBUG
            Run();
            Console.ReadLine();
#else
            Log($@"Posting after {UntilNextRun}, then a new post will be made every {Interval}!", ConsoleColor.Magenta);
            StartTimer(Run);
            MRE.WaitOne();
#endif

            lock (Bots)
                foreach (TwitterBot bot in Bots)
                    bot.Dispose();

            Database.Dispose();
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            string filename = DumpException(e.ExceptionObject as Exception, e);

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

        public static string DumpException(Exception ex, UnhandledExceptionEventArgs ev = null)
        {
#if !DEBUG
            return string.Empty;
#endif

            DateTime now = DateTime.Now;
            StringBuilder sb = new StringBuilder();

            sb.Append(@"Kotori v");
            sb.Append(Version);
#if DEBUG
            sb.Append(@" Debug Build");
#endif
            sb.AppendLine();

            sb.AppendLine($@"Unhandled exception on {now}");
            if (ev != null)
                sb.AppendLine($@"Is Terminating: {ev.IsTerminating}");
            sb.AppendLine();

            while (ex != null)
            {
                sb.Append(ex);
                sb.AppendLine();
                sb.AppendLine();
                ex = ex.InnerException;
            }

            string filename = $@"Kotori {now:yyyy-MM-dd HH.mm.ss}.log";
#if DEBUG
            Debug.WriteLine(sb.ToString());
#else
            File.WriteAllText(filename, sb.ToString());
#endif

            return filename;
        }

        public static void ExitIfCancelled(int exitCode = 0)
        {
            if (Cancellation.IsCancellationRequested)
                Environment.Exit(exitCode);
        }

        public static void LogHeader(string header, ConsoleColor fg = ConsoleColor.Blue, ConsoleColor bg = ConsoleColor.Black)
        {
            ExitIfCancelled();
            Log();
            Log(header, fg, bg);
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
        
        public static void Run()
        {
            lock (Bots)
            {
                foreach (TwitterBot bot in Bots)
                {
                    LogHeader($@"Posting to Twitter from {bot.Name}");

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
                        Log(randomPost?.PostUrl ?? @"No posts available");

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
                            Log(@"Error during request!", ConsoleColor.Black, ConsoleColor.Red);
                            DumpException(ex);
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
                            Log(@"Error during stream handle!", ConsoleColor.Black, ConsoleColor.Red);
                            DumpException(ex);
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

                        Log($@"{bot.Name.PadRight(20)} - Tries: {tries} - Media Null? {media == null}", ConsoleColor.Black, ConsoleColor.Blue);
                    }

                    if (media != null)
                        try
                        {
                            bot.Client.PostTweet(randomPost.PostUrl, media);
                            bot.DeletePost(randomPost.PostId);
                        }
                        catch (Exception ex)
                        {
                            Log(ex, ConsoleColor.Red);
                            DumpException(ex);
                        }

                    Log($@"Validating cache for {bot.Name}");

                    if (!bot.EnsureCacheReady())
                    {
                        Log($@"Refreshing cache for {bot.Name}, this may take a little bit...");
                        new Thread(bot.RefreshCache) { IsBackground = true }.Start();
                    }
                }
            }
        }
    }
}
