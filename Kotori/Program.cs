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
using System.Text;
using System.Threading;
using Tweetinvi.Models;

namespace Kotori
{
    public static partial class Program
    {
        public const double BOTS_PER_THREAD = 3d;

        public static readonly Version Version = new Version(4, 0);

        public static readonly HttpClient HttpClient = new HttpClient();
        public static DatabaseManager Database { get; private set; }

        private static readonly ManualResetEvent MRE = new ManualResetEvent(false);
        private static readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
        private static Timer Timer;

        public static TimeSpan Interval => TimeSpan.FromSeconds(30);
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

            CheckUpdates();

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

                    Bots.Add(new TwitterBot(
                        Database,
                        boorus,
                        botInfo,
                        new TwitterClient(creds)
                    ));
                }

            Log($@"Posting after {UntilNextRun}, then a new post will be made every {Interval}!", ConsoleColor.Magenta);
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
                Log(@"Failed to check for updates.", ConsoleColor.Red);
            else if (updateLines[0].Trim() != Version.ToString())
            {
                AvailableUpdate = updateLines;

                lock (LogLock)
                {
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
        }

        public static void RunThread(int thread, int count)
        {
            ConsoleColor bg = (ConsoleColor)thread;
            int offset = thread * count;
            LogHeader($@"Running Thread #{thread}");

            IEnumerable<TwitterBot> bots = Bots.Skip(offset).Take(count);

            foreach (TwitterBot bot in bots)
            {
                LogHeader($@"Validating cache for {bot.Name}", bg: bg);

                if (!bot.EnsureCacheReady())
                {
                    Log($@"Refreshing cache for {bot.Name}, this may take a little bit...", bg: bg);
                    bot.RefreshCache();
                }

                LogHeader($@"Posting to Twitter from {bot.Name}", bg: bg);

                IBooruPost randomPost = bot.GetRandomPost();
                IMedia media = null;

                Log(randomPost.PostUrl, bg: bg);

                HttpClient.GetAsync(randomPost.FileUrl).ContinueWith(get =>
                {
                    if (!get.IsCompletedSuccessfully)
                        return;

                    get.Result.Content.ReadAsStreamAsync().ContinueWith(bytes =>
                    {
                        if (!bytes.IsCompletedSuccessfully)
                            return;

                        //media = bot.Client.UploadMedia(bytes.Result);
                        using (MemoryStream ms = new MemoryStream())
                        {
                            bytes.Result.CopyTo(ms);
                            ms.Seek(0, SeekOrigin.Begin);
                            File.WriteAllBytes(randomPost.FileHash + @"." + randomPost.FileExtension, ms.ToArray());
                        }
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
                    Log(ex, ConsoleColor.Red, bg);
                    Cancellation.Cancel();
                    break;
                }
            }
        }

        public static void Run()
        {
            lock (Bots)
            {
                LogHeader(@"Spawning threads...");

                int threadCount = (int)Math.Ceiling(Bots.Count / BOTS_PER_THREAD);
                List<Thread> threads = new List<Thread>();

                for (int i = 0; i < threadCount; i++)
                {
                    int thread = i;
                    Thread t = new Thread(() => RunThread(thread, (int)BOTS_PER_THREAD));
                    threads.Add(t);
                    t.Start();
                }

                while (!threads.All(t => !t.IsAlive)) Thread.Sleep(500);
            }

            CheckUpdates();
        }
    }
}
