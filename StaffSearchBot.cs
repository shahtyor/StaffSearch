using Newtonsoft.Json.Linq;
using Npgsql;
//using SAE.AmadeusPRD;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq;
using System.Runtime.Caching;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace StaffSearch
{
    public partial class StaffSearchBot : ServiceBase
    {
        //static ITelegramBotClient bot = new TelegramBotClient("6887022781:AAFHUUlU4japCWTaRli9BAx_aLM4d2vmFmU");
        static ITelegramBotClient bot = new TelegramBotClient(Properties.Settings.Default.BotToken);
        static ObjectCache cache = MemoryCache.Default;
        static CacheItemPolicy policyuser = new CacheItemPolicy() { SlidingExpiration = TimeSpan.Zero, AbsoluteExpiration = DateTimeOffset.Now.AddMonths(Properties.Settings.Default.CacheUserNResult) };

        public enum ServiceState
        {
            SERVICE_STOPPED = 0x00000001,
            SERVICE_START_PENDING = 0x00000002,
            SERVICE_STOP_PENDING = 0x00000003,
            SERVICE_RUNNING = 0x00000004,
            SERVICE_CONTINUE_PENDING = 0x00000005,
            SERVICE_PAUSE_PENDING = 0x00000006,
            SERVICE_PAUSED = 0x00000007,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ServiceStatus
        {
            public int dwServiceType;
            public ServiceState dwCurrentState;
            public int dwControlsAccepted;
            public int dwWin32ExitCode;
            public int dwServiceSpecificExitCode;
            public int dwCheckPoint;
            public int dwWaitHint;
        };

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetServiceStatus(System.IntPtr handle, ref ServiceStatus serviceStatus);

        public StaffSearchBot()
        {
            InitializeComponent();

            eventLogBot = new EventLog();
            if (!EventLog.SourceExists("StaffSearch"))
            {
                EventLog.CreateEventSource(
                    "StaffSearch", "StaffSearchLog");
            }
            eventLogBot.Source = "StaffSearch";
            eventLogBot.Log = "StaffSearchLog";

            //Methods.conn.Open();
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                // Update the service state to Start Pending.
                ServiceStatus serviceStatus = new ServiceStatus();
                serviceStatus.dwCurrentState = ServiceState.SERVICE_START_PENDING;
                serviceStatus.dwWaitHint = 100000;
                SetServiceStatus(this.ServiceHandle, ref serviceStatus);

                eventLogBot.WriteEntry("Staff Search Bot --- OnStart");

                // Update the service state to Running.
                serviceStatus.dwCurrentState = ServiceState.SERVICE_RUNNING;
                SetServiceStatus(this.ServiceHandle, ref serviceStatus);

                var task = Task.Run(async () => await bot.GetMeAsync());
                var s = task.Result.FirstName;

                eventLogBot.WriteEntry("Запущен бот " + bot.GetMeAsync().Result.FirstName);

                System.Timers.Timer aTimer = new System.Timers.Timer();

                /*aTimer.Elapsed += new ElapsedEventHandler(this.OnTimedEvent);

                aTimer.Interval = 60000;
                aTimer.Enabled = true;
                aTimer.Start();*/

                var cts = new CancellationTokenSource();
                var cancellationToken = cts.Token;
                var receiverOptions = new ReceiverOptions
                {
                    AllowedUpdates = { }, // receive all update types
                };
                bot.StartReceiving(
                    HandleUpdateAsync,
                    HandleErrorAsync,
                    receiverOptions,
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                eventLogBot.WriteEntry(ex.Message + "..." + ex.StackTrace);
            }
        }

        /*public void OnTimedEvent(object source, ElapsedEventArgs e)
        {
            try
            {
                var task = Task.Run(async () => await FindResponse());
            }
            catch (Exception ex)
            {
                eventLogBot.WriteEntry(ex.Message + "..." + ex.StackTrace);
            }
        }

        public static async Task FindResponse()
        {

        }*/

        public static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            policyuser = new CacheItemPolicy() { SlidingExpiration = TimeSpan.Zero, AbsoluteExpiration = DateTimeOffset.Now.AddMonths(Properties.Settings.Default.CacheUserNResult) };

            // Некоторые действия
            eventLogBot.WriteEntry(Newtonsoft.Json.JsonConvert.SerializeObject(update));

            try
            {
                if (update.Type == Telegram.Bot.Types.Enums.UpdateType.Message)
                {
                    var message = update.Message;
                    long? userid = null;
                    telegram_user user = null;
                    string keyuser = null;

                    if (message != null)
                    {
                        userid = message.Chat.Id;
                        keyuser = "teluser:" + userid;
                        var userexist = cache.Contains(keyuser);
                        if (userexist) user = (telegram_user)cache.Get(keyuser);
                        else
                        {
                            user = Methods.GetUser(userid.Value);
                            cache.Add(keyuser, user, policyuser);
                        }

                        try
                        {
                            eventLogBot.WriteEntry(Newtonsoft.Json.JsonConvert.SerializeObject(user));
                        }
                        catch { }
                    }

                    var arrmsg = message?.Text?.Split(' ');
                    var msg = arrmsg[0].ToLower();

                    if (msg == "/start" && message != null && !string.IsNullOrEmpty(message.Text) && arrmsg.Length == 2)
                    {
                        cache.Add("start" + userid, message.Text, new CacheItemPolicy() { SlidingExpiration = TimeSpan.Zero, AbsoluteExpiration = DateTimeOffset.Now.AddMinutes(10) });
                    }

                    if (message?.Text?.ToLower() == "\U0001F7e1" || message?.Text?.ToLower() == "U+1F534" || message?.Text?.ToLower() == "/stop")
                    {
                        return;
                    }

                    /*if ((bool)message?.Text.ToLower().Contains("agent just reported this"))
                    {
                        var strings = message?.Text.Split('\r');
                    }*/

                    string idus = message.Chat.Id.ToString();
                    if (user.Token != null)
                    {
                        idus = user.Token.type + "_" + user.Token.id_user;
                    }

                    var intro = "Welcome to the Staff Airlines bot" + Environment.NewLine + Environment.NewLine +
                        "Main commands:" + Environment.NewLine + Environment.NewLine + "SEARCH FOR FLIGHTS" + Environment.NewLine +
                        "To search, use airport or city codes. For example, <b>NYCLAX</b> - search from New York (NYC) to Los Angeles (LAX)." + Environment.NewLine +
                        "<b>NYCLAX</b> - search for current date" + Environment.NewLine + "<b>NYCLAX15</b> - search for the 15th" + Environment.NewLine +
                        "<b>NYCLAX15/2</b> - search for two passengers" + Environment.NewLine + Environment.NewLine + "FLIGHT DETAILS" + Environment.NewLine + "Number of line in the flights list, for example <b>1</b> or <b>15</b>";

                    if (msg == "/start")
                    {
                        await botClient.SendTextMessageAsync(message.Chat, intro, parseMode: ParseMode.Html);

                        // отправляем событие «первое появление  пользователя в поисковом боте (/start)» в амплитуд
                        string DataJson = "[{\"user_id\":\"" + message.Chat.Id + "\",\"platform\":\"Telegram\",\"event_type\":\"tg sb join\"," +
                             "\"user_properties\":{\"is_requestor\":\"no\"," +
                             "\"id_telegram\":\"" + message.Chat.Id + "\"}}]";
                        var r = Methods.AmplitudePOST(DataJson);

                        if (arrmsg.Length == 1)
                        {
                            var startexist = cache.Contains("start" + userid);
                            if (startexist)
                            {
                                var startmsg = (string)cache.Get("start" + userid);
                                arrmsg = startmsg.Split(' ');
                                cache.Remove("start" + userid);
                            }
                        }

                        if (arrmsg.Length == 2)
                        {
                            cache.Remove("start" + userid);

                            var payload = arrmsg[1].ToLower();
                            Guid gu;
                            bool isGuid0 = Guid.TryParse(payload, out gu);
                            if (isGuid0)
                            {
                                string alert = null;
                                user = Methods.ProfileCommand(userid.Value, payload, eventLogBot, out alert);
                                UpdateUserInCache(user);
                            }
                        }

                        if (user.Token == null)
                        {
                            await botClient.SendTextMessageAsync(message.Chat, "To link your Staff Airlines profile to your Telegram ID, log into the Staff Airlines app (now only for iOS users, coming soon for Android users) in the “Profile” section and click “For request seat loads via Telegram”");

                            //cache.Add("User" + userid.Value, "entertoken", policyuser);
                        }
                        else if (user.own_ac == "??")
                        {
                            await botClient.SendTextMessageAsync(message.Chat, "Your airline: " + user.own_ac + Environment.NewLine + "Specify your airline. Enter your airline's code (for example: AA):");

                            cache.Add("User" + userid.Value, "preset", policyuser);
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(message.Chat, "Your airline: " + user.own_ac.ToUpper() + Environment.NewLine + "Setup completed successfully. You can start using the bot!");
                        }

                        //await botClient.SendTextMessageAsync(message.Chat, "Enter the token:");

                        //cache.Add("User" + userid.Value, "entertoken", policyuser);

                        return;
                    }
                    else if (message?.Text?.ToLower() == "/help")
                    {
                        await botClient.SendTextMessageAsync(message.Chat, intro, parseMode: ParseMode.Html);
                        return;
                    }
                    else if (message?.Text?.ToLower() == "/profile")
                    {
                        await botClient.SendTextMessageAsync(message.Chat, "Enter the UID (generate it in the Staff Airlines app in the Profile section, after logging in):");

                        // отправляем событие «запрос uid для линковки профиля (/profile)» в амплитуд
                        string DataJson = "[{\"user_id\":\"" + idus + "\",\"platform\":\"Telegram\",\"event_type\":\"tg start link profile\"," +
                            "\"event_properties\":{\"bot\":\"sb\"}}]";
                        var r = Methods.AmplitudePOST(DataJson);

                        cache.Add("User" + userid.Value, "entertoken", policyuser);

                        return;
                    }
                    else if (message?.Text?.ToLower() == "/airline")
                    {
                        await botClient.SendTextMessageAsync(message.Chat, "Specify your airline. Enter your airline's code (for example: AA):");

                        string DataJson = "[{\"user_id\":\"" + idus + "\",\"platform\":\"Telegram\",\"event_type\":\"tg start set ac\"," +
                            "\"event_properties\":{\"bot\":\"sb\"}}]";
                        var r = Methods.AmplitudePOST(DataJson);

                        cache.Add("User" + userid.Value, "preset", policyuser);

                        return;
                    }

                    string comm = "";
                    var commexist = cache.Contains("User" + userid.Value);
                    if (commexist) comm = (string)cache.Get("User" + userid.Value);

                    if (comm == "entertoken" && !string.IsNullOrEmpty(message.Text))
                    {
                        Guid gu;
                        bool isGuid0 = Guid.TryParse(message.Text, out gu);

                        if (isGuid0)
                        {
                            string alert = null;
                            user = Methods.ProfileCommand(userid.Value, message.Text, eventLogBot, out alert);

                            if (string.IsNullOrEmpty(alert))
                            {
                                UpdateUserInCache(user);
                                //cache.Add(keyuser, user, policyuser);
                                cache.Remove("User" + message.Chat.Id);

                                if (user.own_ac == "??")
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, "Your airline: " + user.own_ac + Environment.NewLine + "Specify your airline. Enter your airline's code (for example: AA):");

                                    cache.Add("User" + userid.Value, "preset", policyuser);
                                }
                                else
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, "Your airline: " + user.own_ac + Environment.NewLine + "Airlines in results: " + (string.IsNullOrEmpty(user.permitted_ac) ? "All airlines" : user.permitted_ac) + Environment.NewLine + "Setup completed successfully. You can start using the bot!");
                                }
                            }
                            else
                            {
                                await botClient.SendTextMessageAsync(message.Chat, alert);
                                if (user is null || user.id == 0 || user.Token is null)
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, "Enter the UID (generate it in the Staff Airlines app in the Profile section, after logging in):");
                                }
                                else
                                {
                                    cache.Remove("User" + message.Chat.Id);
                                }
                            }
                        }
                        else if (message.Text == "/back")
                        {
                            await botClient.SendTextMessageAsync(message.Chat, "Later");
                            cache.Remove("User" + message.Chat.Id);
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(message.Chat, "The UID must contain 32 digits/letters." + Environment.NewLine + "(For example: 7ece3818-c4b4-4f3a-b94e-82d37b1ff8a1)");
                            if (user is null || user.Token is null)
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "Enter the UID (generate it in the Staff Airlines app in the Profile section, after logging in):");
                            }
                            else
                            {
                                cache.Remove("User" + message.Chat.Id);
                            }
                        }

                        return;
                    }

                    if (comm == "preset")
                    {
                        //string[] presetstr = message.Text.Split(' ');
                        //if (presetstr.Length > 1)
                        //{
                        var ac = message.Text;
                        var test = Methods.TestAC(ac.ToUpper());
                        if (test > 0)
                        {
                            Methods.UpdateUserAC(message.Chat.Id, ac.ToUpper(), user.own_ac.ToUpper());
                            var permitted = Methods.GetPermittedAC(ac.ToUpper());
                            var sperm = string.Join("-", permitted.Select(p => p.Permit));
                            user.own_ac = ac;
                            user.permitted_ac = sperm;
                            UpdateUserInCache(user);

                            /*var ikm = new InlineKeyboardMarkup(new[]
                            {
                                new[]
                                {
                                    InlineKeyboardButton.WithCallbackData("Nopreset", "/nopreset"),
                                },
                            });*/

                            cache.Remove("User" + message.Chat.Id);

                            string DataJson = "[{\"user_id\":\"" + idus + "\",\"platform\":\"Telegram\",\"event_type\":\"tg set ac\"," +
                                "\"user_properties\":{\"ac\":\"" + ac.ToUpper() + "\"}," +
                                "\"event_properties\":{\"bot\":\"sb\"}}]";
                            var r = Methods.AmplitudePOST(DataJson);

                            await botClient.SendTextMessageAsync(message.Chat, "Your airline: " + ac.ToUpper() + Environment.NewLine + "Airlines in results: " + (string.IsNullOrEmpty(sperm) ? "All airlines" : sperm) + Environment.NewLine + "Setup completed successfully. You can start using the bot!");
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(message.Chat, "Airline code" + ac + "not found! Enter your airline's code:");
                        }
                        //}
                        return;
                    }

                    var command = message?.Text;

                    eventLogBot.WriteEntry("command: " + command);

                    if (command != null && command[0] == '/' && user != null)
                    {
                        command = command.Remove(0, 1);
                    }

                    if (command != null && command[0] != '/' && command?.Length >= 6)
                    {
                        var comsplit = command.Split('/');
                        var From = comsplit[0].Substring(0, 3).ToUpper();
                        var To = comsplit[0].Substring(3, 3).ToUpper();
                        var dt = comsplit[0].Substring(6);
                        var islash = command.IndexOf('/');
                        var pax = 1;
                        bool paxint = true;
                        if (comsplit.Length > 1)
                        {
                            paxint = int.TryParse(comsplit[1], out pax);
                        }

                        if (paxint && (pax > 4 || pax < 1))
                        {
                            pax = 1;
                        }

                        eventLogBot.WriteEntry("From: " + From + ", To: " + To + ", dt: " + dt + ", pax: " + pax);

                        if (dt.Length <= 4 && paxint)
                        {
                            DateTime searchdt = DateTime.Today;
                            DateTime DepNow = DateTime.Now;
                            try
                            {
                                DepNow = Methods.GetDepartureTime(From, DateTime.Now);
                            }
                            catch
                            {
                                await botClient.SendTextMessageAsync(message.Chat, From.ToUpper() + " – departure point is unknown!");
                                return;
                            }

                            var FromLoc = Methods.GetLocation(From);
                            if (FromLoc == null)
                            {
                                await botClient.SendTextMessageAsync(message.Chat, From.ToUpper() + " – departure point is unknown!");
                                return;
                            }

                            var ToLoc = Methods.GetLocation(To);
                            if (ToLoc == null)
                            {
                                await botClient.SendTextMessageAsync(message.Chat, To.ToUpper() + " - destination point is unknown!");
                                return;
                            }

                            if (dt.Length == 4)
                            {
                                var dd = dt.Substring(0, 2);
                                var mm = dt.Substring(2, 2);
                                try
                                {
                                    searchdt = new DateTime(DateTime.Now.Year, Convert.ToInt32(mm.TrimStart('0')), Convert.ToInt32(dd.TrimStart('0')));
                                }
                                catch { }

                                if (searchdt < DateTime.Today)
                                {
                                    searchdt = searchdt.AddYears(1);
                                }
                            }
                            else if (dt.Length == 2)
                            {
                                try
                                {
                                    searchdt = new DateTime(DateTime.Now.Year, DateTime.Now.Month, Convert.ToInt32(dt.TrimStart('0')));
                                }
                                catch { }

                                if (searchdt < DepNow.Date)
                                {
                                    searchdt = searchdt.AddMonths(1);
                                }
                            }
                            else if (dt.Length == 1)
                            {
                                try
                                {
                                    searchdt = new DateTime(DateTime.Now.Year, DateTime.Now.Month, Convert.ToInt32(dt));
                                }
                                catch { }

                                if (searchdt < DepNow.Date)
                                {
                                    searchdt = searchdt.AddMonths(1);
                                }
                            }
                            else if (dt.Length == 0)
                            {
                                searchdt = DepNow.Date;
                            }

                            eventLogBot.WriteEntry("DepNow: " + DepNow.ToString("dd-MM-yyyy HH:mm") + ", user=" + user?.id + ", token=" + user?.Token?.id_user);

                            bool SearchAvailable = true;
                            if (DepNow.Date != searchdt.Date)
                            {
                                if (user == null || user.Token == null)
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, "To search for dates other than the current date, you have to log in (/profile)");
                                    SearchAvailable = false;
                                }
                                else
                                {
                                    var Prof = await Methods.TokenProfile(user.Token.type + "_" + user.Token.id_user);

                                    string DataJson = "[{\"user_id\":\"" + user.Token.type + "_" + user.Token.id_user + "\",\"platform\":\"Telegram\",\"event_type\":\"void\"," +
                                        "\"user_properties\":{\"paidStatus\":\"" + (Prof.Premium ? "premiumAccess" : "free plan") + "\"}}]";
                                    var r = Methods.AmplitudePOST(DataJson);

                                    eventLogBot.WriteEntry("Prof: " + Prof.SubscribeTokens + "-" + Prof.NonSubscribeTokens + "-" + Prof.Premium.ToString());

                                    if (!Prof.Premium)
                                    {
                                        await botClient.SendTextMessageAsync(message.Chat, "To search for any date, you must purchase a subscription (in the Staff Airlines app or in the @StaffAirlinesBot channel, if you earned tokens there)");
                                        SearchAvailable = false;
                                    }
                                }
                            }

                            if (SearchAvailable)
                            {
                                var findbeg = "Search from " + FromLoc.Name + " (" + From + ") to " + ToLoc.Name + " (" + To + ") on " + searchdt.ToString("MMMM d", CultureInfo.CreateSpecificCulture("en-US")) + " for " + pax + " pax";
                                await botClient.SendTextMessageAsync(message.Chat, findbeg);

                                if (user == null)
                                {
                                    user = new telegram_user() { id = message.Chat.Id };
                                }
                                if (user.permitted_ac == null) user.permitted_ac = "";

                                eventLogBot.WriteEntry("permitted_ac: " + user?.permitted_ac);

                                string id_user_search = null;
                                if (user.Token != null)
                                {
                                    id_user_search = user.Token.type + "_" + user.Token.id_user;
                                }

                                ExtendedResult exres0 = await Methods.ExtendedSearch(From, To, searchdt, user.permitted_ac, false, GetNonDirectType.Off, pax, "USD", "EN", "RU", "bot" + message.Chat.Id, false, "3.0", null, id_user_search);

                                //Пользователь запустил поиск
                                string DataJson = "[{\"user_id\":\"" + idus + "\",\"event_type\":\"Extended search started\",\"platform\":\"Telegram\"," +
                                    "\"event_properties\":{\"Origin\":\"" + From + "\"," +
                                    "\"Destination\":\"" + To + "\"," +
                                    "\"Date\":" + Convert.ToInt32((searchdt - DateTime.Today).TotalDays) + "," +
                                    "\"Passengers\":" + pax + "," +
                                    "\"Country origin\":\"" + FromLoc.CountryName + "\"," +
                                    "\"Country destination\":\"" + ToLoc.CountryName + "\"}}]";
                                var r = Methods.AmplitudePOST(DataJson);

                                SetTSOnResult(exres0);
                                user.SearchParameters = new SearchParam() { Origin = From, Destination = To, Date = searchdt, Pax = pax };
                                user.exres = exres0;

                                eventLogBot.WriteEntry("DirectRes.Count: " + exres0.DirectRes?.Count + ", Uri=" + exres0.Alert);

                                UpdateUserInCache(user);

                                if (exres0.DirectRes?.Count > 0)
                                {
                                    string res = string.Empty;
                                    int i = 0;
                                    var dlen = exres0.DirectRes.Max(x => x.DepartureTerminal?.Length);
                                    var alen = exres0.DirectRes.Max(x => x.ArrivalTerminal?.Length);
                                    foreach (Flight fl in exres0.DirectRes)
                                    {
                                        i++;

                                        string srat = "";
                                        if (fl.Rating == RType.Red)
                                        {
                                            srat = "\uD83D\uDD34";
                                        }
                                        else if (fl.Rating == RType.Yellow)
                                        {
                                            srat = "\uD83D\uDFE1";
                                        }
                                        else
                                        {
                                            srat = "\uD83D\uDFE2";
                                        }

                                        var NextDay = fl.DepartureDateTime.Day != fl.ArrivalDateTime.Day ? "+1/" : "   ";

                                        res = res + "<code>" + i.ToString().PadLeft(2, ' ') + " " + fl.MarketingCarrier + " " + fl.DepartureDateTime.ToString("HH:mm") + " " + fl.Origin + " " +
                                            fl.ArrivalDateTime.ToString("HH:mm") + " " + fl.Destination + " </code> " + srat + "<code>" + (fl.AllPlaces.LastOrDefault() == '+' ? fl.AllPlaces.PadLeft(3, ' ') : fl.AllPlaces.PadLeft(2, ' ')) + "</code>" + Environment.NewLine;
                                    }
                                    await botClient.SendTextMessageAsync(message.Chat, res, null, Telegram.Bot.Types.Enums.ParseMode.Html);
                                }
                                else
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, string.IsNullOrEmpty(exres0.Alert) || exres0.Alert == "Not Found" ? "Direct flights not found" : exres0.Alert);
                                }
                            }
                        }

                        return;
                    }

                    int n;
                    Guid g;
                    bool isNumeric = int.TryParse(message?.Text, out n);
                    bool isGuid = Guid.TryParse(message?.Text, out g);
                    if (isNumeric && !isGuid && user != null)
                    {
                        if (user.exres?.DirectRes?.Count > 0)
                        {
                            int? cntf = user.exres?.DirectRes?.Count;
                            if (cntf != null && cntf.Value > 0)
                            {
                                if (n < 1) { n = 1; };
                                if (n > cntf.Value) { n = cntf.Value; };
                                var Fl = user.exres.DirectRes[n - 1];

                                var TimePassed = ((TimeSpan)(DateTime.Now - Fl.TS)).TotalMinutes;
                                if (TimePassed > Properties.Settings.Default.OutdatedAfter)
                                {
                                    int px = 1;
                                    if (user.SearchParameters != null)
                                    {
                                        px = user.SearchParameters.Pax;
                                    }
                                    FlightInfo FInfo = await Methods.GetFlightInfo(Fl.Origin, Fl.Destination, Fl.DepartureDateTime, px, Fl.MarketingCarrier, Fl.FlightNumber, user.Token?.type + "_" + user.Token?.id_user);
                                    Fl = FInfo.Flight;
                                    Fl.TS = DateTime.Now;
                                }

                                string srat = "";
                                if (Fl.Rating == RType.Red)
                                {
                                    srat = "\uD83D\uDD34";
                                }
                                else if (Fl.Rating == RType.Yellow)
                                {
                                    srat = "\uD83D\uDFE1";
                                }
                                else
                                {
                                    srat = "\uD83D\uDFE2";
                                }

                                string ForecastStatus = "";
                                string ForeIcon = "";
                                if (Fl.Forecast <= 1)
                                {
                                    ForeIcon = "\uD83D\uDD34";
                                    ForecastStatus = "Bad";
                                }
                                else if (Fl.Forecast <= 2)
                                {
                                    ForeIcon = "\uD83D\uDFE1";
                                    ForecastStatus = "So-so";
                                }
                                else
                                {
                                    ForeIcon = "\uD83D\uDFE2";
                                    ForecastStatus = "Good";
                                }

                                string classes = "Classes available: ";

                                bool HideSomeInfo = false;
                                if (Fl.AgentInfo != null)
                                {
                                    int cntreq = 0;
                                    if (user.Token != null)
                                    {
                                        using (NpgsqlConnection conr = new NpgsqlConnection(Properties.Settings.Default.ConnectionString))
                                        {
                                            conr.Open();
                                            NpgsqlCommand cmdr = new NpgsqlCommand("select count(*) from telegram_request where id_requestor=@id_user and request_status=5 and number_flight=@number and concat(date_flight, ' ', time_flight)=@dep", conr);
                                            cmdr.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_user", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = user.Token.type + "_" + user.Token.id_user });
                                            cmdr.Parameters.Add(new NpgsqlParameter() { ParameterName = "number", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = Fl.MarketingCarrier + Fl.FlightNumber });
                                            cmdr.Parameters.Add(new NpgsqlParameter() { ParameterName = "dep", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = Fl.DepartureDateTime.ToString("ddMMyy HHmm") });
                                            cntreq = Convert.ToInt32(cmdr.ExecuteScalar());
                                            conr.Close();
                                        }
                                    }

                                    HideSomeInfo = cntreq == 0;

                                    if (Fl.AgentInfo.TimePassed <= Properties.Settings.Default.AgentTimePassed)
                                    {
                                        if (HideSomeInfo)
                                        {
                                            classes += "SA:" + Fl.AgentInfo.CntSAPassenger + " (agent reported " + Fl.AgentInfo.TimePassed + " min ago), (" + string.Join(" ", Fl.NumSeatsForBookingClass) + ")";
                                        }
                                        else
                                        {
                                            classes += "Economy:" + Fl.AgentInfo.EconomyPlaces + " Business:" + Fl.AgentInfo.BusinessPlaces + " SA:" + Fl.AgentInfo.CntSAPassenger + " (agent reported " + Fl.AgentInfo.TimePassed + " min ago)";
                                        }
                                    }
                                    else
                                    {
                                        if (HideSomeInfo)
                                        {
                                            classes += "SA:" + Fl.AgentInfo.CntSAPassenger + " (agent reported " + Methods.CorrectTimePassed(Fl.AgentInfo.TimePassed) + " ago), (" + string.Join(" ", Fl.NumSeatsForBookingClass) + ")";
                                        }
                                        else
                                        {
                                            classes += "Economy:" + Fl.AgentInfo.EconomyPlaces + " Business:" + Fl.AgentInfo.BusinessPlaces + " SA:" + Fl.AgentInfo.CntSAPassenger + " (agent reported " + Methods.CorrectTimePassed(Fl.AgentInfo.TimePassed) + " ago), (" + string.Join(" ", Fl.NumSeatsForBookingClass) + ")";
                                        }
                                    }
                                }
                                else
                                {
                                    classes += string.Join(" ", Fl.NumSeatsForBookingClass);
                                }

                                string strOper = "";
                                if (Fl.OperatingCarrier != Fl.MarketingCarrier)
                                {
                                    strOper = Environment.NewLine + "operated by " + Fl.OperatingCarrier + " (" + Fl.OperatingName + ")";
                                }

                                string res = Fl.MarketingName + " " + Fl.MarketingCarrier + Fl.FlightNumber + strOper + Environment.NewLine + Fl.EquipmentName + Environment.NewLine + Environment.NewLine +
                                Fl.DepartureDateTime.ToString("dd MMM, ddd", CultureInfo.CreateSpecificCulture("en-US")) + " " + Fl.DepartureDateTime.ToString("HH:mm") + " " + (!string.IsNullOrEmpty(Fl.DepartureCityName) ? Fl.DepartureCityName + ", " : "") + Fl.DepartureName + " (" + Fl.Origin + ")" + (!string.IsNullOrEmpty(Fl.DepartureTerminal) ? ", Terminal " + Fl.DepartureTerminal : "") + Environment.NewLine +
                                "In flight " + GetTimeAsHM2(Fl.Duration) + Environment.NewLine +
                                Fl.ArrivalDateTime.ToString("dd MMM, ddd", CultureInfo.CreateSpecificCulture("en-US")) + " " + Fl.ArrivalDateTime.ToString("HH:mm") + " " + (!string.IsNullOrEmpty(Fl.ArrivalCityName) ? Fl.ArrivalCityName + ", " : "") + Fl.ArrivalName + " (" + Fl.Destination + ")" + (!string.IsNullOrEmpty(Fl.ArrivalTerminal) ? ", Terminal " + Fl.ArrivalTerminal : "") + Environment.NewLine + Environment.NewLine;
                                string res2 = "<b>Status (current): " + (Fl.Rating == RType.Red ? "Bad" : (Fl.Rating == RType.Green ? "Good" : "Medium")) + ", " + Fl.AllPlaces + " seats" + "</b> " + srat + Environment.NewLine + Environment.NewLine +
                                classes + Environment.NewLine + Environment.NewLine +
                                (Fl.C > 0 ? "<b>Status (forecast): " + ForecastStatus + " (Accuracy: " + Fl.Accuracy + ")" + "</b> " + ForeIcon : "");

                                cache.Add("key:" + Fl.MarketingCarrier + Fl.FlightNumber, res, policyuser);

                                bool AgentExist = Methods.AgentExist(Fl.OperatingCarrier);
                                if (AgentExist)
                                {
                                    var ikm = new InlineKeyboardMarkup(new[]
                                    {
                                    new[]
                                    {
                                        InlineKeyboardButton.WithCallbackData("Request to agent (" + Properties.Settings.Default.AmountTokensForRequest + " token)", "/com " + Fl.Origin + " " + Fl.Destination + " " + Fl.DepartureDateTime.ToString("ddMMyy HHmm") + " " + Fl.MarketingCarrier + Fl.FlightNumber + " " + Fl.OperatingCarrier + " " + user.SearchParameters?.Pax),
                                    },
                                });

                                    Message tm = await botClient.SendTextMessageAsync(message.Chat, res + res2, null, ParseMode.Html, replyMarkup: ikm);
                                    Methods.SaveMessageParameters(tm.Chat.Id, tm.MessageId, 0, 3);
                                }
                                else
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, res + res2 + Environment.NewLine + "No agents yet", null, ParseMode.Html);
                                }

                                string ForRat = "None";
                                if (!string.IsNullOrEmpty(Fl.Accuracy))
                                {
                                    if (Fl.Forecast <= 1)
                                    {
                                        ForRat = "Bad";
                                    }
                                    else if (Fl.Forecast <= 2)
                                    {
                                        ForRat = "So-so";
                                    }
                                    else
                                    {
                                        ForRat = "Good";
                                    }
                                }

                                //Детализация варианта, посылаем такой из аппов
                                string DataJson = "[{\"user_id\":\"" + idus + "\",\"event_type\":\"Details show direct\",\"platform\":\"Telegram\"," +
                                    "\"event_properties\":{\"ac\":\"" + Fl.OperatingCarrier + "\"," +
                                    "\"ForecastAccuracy\":\"" + Fl.Accuracy + "\"," +
                                    "\"ForecastRating\":\"" + ForRat + "\"," +
                                    "\"Rating\":\"" + (Fl.Rating == RType.Red ? "Bad" : (Fl.Rating == RType.Green ? "Good" : "Medium")) + "\"," +
                                    "\"dataClassesFromAgent\":\"" + (Fl.AgentInfo != null && !HideSomeInfo ? "yes" : "no") + "\"," +
                                    "\"dataSAFromAgent\":\"" + (Fl.AgentInfo != null ? "yes" : "no") + "\"," +
                                    "\"ageDataFromAgent\":" + (Fl.AgentInfo != null ? Fl.AgentInfo.TimePassed : -1) + "," +
                                    "\"agents\":\"" + (AgentExist ? "yes" : "no") + "\"}}]";
                                var r = Methods.AmplitudePOST(DataJson);
                            }
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(message.Chat, "First you need to do a search (for example: NYCLAX)!");
                        }

                        return;
                    }
                }
                else if (update.Type == Telegram.Bot.Types.Enums.UpdateType.CallbackQuery)
                {
                    var callbackquery = update.CallbackQuery;
                    var message = callbackquery.Data;

                    if (message != null)
                    {
                        //var message = update.Message;
                        long? userid = null;
                        telegram_user user = null;

                        if (callbackquery.From != null)
                        {
                            userid = callbackquery.From.Id;
                            string keyuser = "teluser:" + userid;
                            var userexist = cache.Contains(keyuser);
                            if (userexist) user = (telegram_user)cache.Get(keyuser);
                            else
                            {
                                user = Methods.GetUser(userid.Value);
                                cache.Add(keyuser, user, policyuser);
                            }
                        }

                        if (message == "/nopreset")
                        {
                            Methods.UpdateUserAC(userid.Value, "??", user.own_ac.ToUpper());
                            user.own_ac = "??";
                            user.permitted_ac = "";
                            UpdateUserInCache(user);

                            var ikm = new InlineKeyboardMarkup(new[]
                            {
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("Preset", "/preset"),
                            },
                        });

                            await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Your airline: ??" + Environment.NewLine + "Airlines in results: All airlines", replyMarkup: ikm);
                        }
                        else if (message == "/preset")
                        {
                            await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Enter your airline's code:");

                            cache.Add("User" + callbackquery.From.Id, "preset", policyuser);
                        }

                        if (message.Length >= 4 && message.Substring(0, 4) == "/com")
                        {
                            var pars = message.Split(' ');
                            if (pars.Length == 8)
                            {
                                bool RequestAvailable = true;
                                if (user == null || user.Token == null)
                                {
                                    RequestAvailable = false;
                                    await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "To request a load from an agent, you will have to log in (/profile)");
                                }
                                else
                                {
                                    int AmountTokensForRequest = int.Parse(Properties.Settings.Default.AmountTokensForRequest);
                                    var Prof = await Methods.TokenProfile(user.Token.type + "_" + user.Token.id_user);

                                    string DataJson = "[{\"user_id\":\"" + user.Token.type + "_" + user.Token.id_user + "\",\"platform\":\"Telegram\",\"event_type\":\"void\"," +
                                        "\"user_properties\":{\"paidStatus\":\"" + (Prof.Premium ? "premiumAccess" : "free plan") + "\"}}]";
                                    var r = Methods.AmplitudePOST(DataJson);

                                    eventLogBot.WriteEntry("TokenProfile: " + user.Token.type + "_" + user.Token.id_user + ", Prof: " + Newtonsoft.Json.JsonConvert.SerializeObject(Prof));

                                    var CntTok = Prof.SubscribeTokens + Prof.NonSubscribeTokens;
                                    if (CntTok < AmountTokensForRequest)
                                    {
                                        RequestAvailable = false;
                                        await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "One request to agent costs " + AmountTokensForRequest + " token. Tokens are included in the Premium subscription. If you spend all the tokens from subscription, you could purchase an additional package of tokens on Staff Airlines app. You can also earn tokens as airline’s agent to reply on requests about load your airline's flights in the @StaffAirlinesBot channel. More details staffairlines.com");
                                    }
                                }

                                if (RequestAvailable)
                                {
                                    eventLogBot.WriteEntry(user.Token.type + "_" + user.Token.id_user);

                                    var remreq = await Methods.AddRequest(callbackquery, user.Token.type + "_" + user.Token.id_user);

                                    if (remreq != null)
                                    {
                                        if (remreq.Status == ReportStatus.already_in_progress)
                                        {
                                            await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "You already have an active request for this flight!");
                                        }
                                        else
                                        {
                                            var mespar = Methods.GetMessageParameters(callbackquery.Message.Chat.Id);
                                            await botClient.EditMessageReplyMarkupAsync(callbackquery.Message.Chat.Id, mespar.MessageId, replyMarkup: null);
                                            Methods.DelMessageParameters(callbackquery.Message.Chat.Id);

                                            DateTime dreq = new DateTime(2000 + int.Parse(pars[3].Substring(4, 2)), int.Parse(pars[3].Substring(2, 2)), int.Parse(pars[3].Substring(0, 2)), int.Parse(pars[4].Substring(0, 2)), int.Parse(pars[4].Substring(2, 2)), 0);

                                            var reqpost = "Request for flight " + pars[5] + " " + pars[1] + "-" + pars[2] + " at " + dreq.ToString("dd-MM-yyyy HH:mm") + " posted. Your balance: " + (remreq.Tokens.SubscribeTokens + remreq.Tokens.NonSubscribeTokens) + " token(s)";
                                            await botClient.SendTextMessageAsync(callbackquery.Message.Chat, reqpost);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    return;
                }
                else if (update.Type == UpdateType.MyChatMember)
                {
                    var myChatMember = update.MyChatMember;

                    //var message = update.Message;
                    long? userid = null;
                    telegram_user user = null;

                    if (myChatMember.From != null)
                    {
                        userid = myChatMember.From.Id;
                        string keyuser = "teluser:" + userid;
                        var userexist = cache.Contains(keyuser);
                        if (userexist) user = (telegram_user)cache.Get(keyuser);
                        else
                        {
                            user = Methods.GetUser(userid.Value);
                            cache.Add(keyuser, user, policyuser);
                        }
                    }

                    var CM = myChatMember.NewChatMember;
                    if (CM.Status == ChatMemberStatus.Kicked) // Заблокировал чат
                    {
                        Methods.UserBlockChat(userid.Value, AirlineAction.Delete);

                        string idus = myChatMember.From.Id.ToString();
                        if (user.Token != null)
                        {
                            idus = user.Token.type + "_" + user.Token.id_user;
                        }

                        //пользователь покинул агентский бот
                        string DataJson = "[{\"user_id\":\"" + idus + "\",\"platform\":\"Telegram\",\"event_type\":\"tg left user\"," +
                            "\"user_properties\":{\"is_requestor\":\"no\"}," +
                            "\"event_properties\":{\"ac\":\"" + user.own_ac + "\"}}]";
                        var r = Methods.AmplitudePOST(DataJson);
                    }
                    else if (CM.Status == ChatMemberStatus.Member) // Разблокировал чат
                    {
                        Methods.UserBlockChat(userid.Value, AirlineAction.Add);
                    }

                    return;
                }
            }
            catch (Exception exx)
            {
                eventLogBot.WriteEntry("Exception: " + exx.Message + "..." + exx.StackTrace);
                return;
            }
        }

        private static InlineKeyboardMarkup GetIkmPresetNopreset(string own_ac)
        {
            InlineKeyboardButton ikb;
            if (own_ac == "??")
            {
                ikb = InlineKeyboardButton.WithCallbackData("Preset", "/preset");
            }
            else
            {
                ikb = InlineKeyboardButton.WithCallbackData("Nopreset", "/nopreset");
            }

            var ikm = new InlineKeyboardMarkup(new[]
            {
                        new[]
                        {
                            ikb,
                        },
                    });

            return ikm;
        }

        private static void UpdateUserInCache(telegram_user user)
        {
            string keyuser = "teluser:" + user.id;
            cache.Remove(keyuser);
            cache.Add(keyuser, user, policyuser);
            try
            {
                eventLogBot.WriteEntry("UpdateUserInCache. keyuser=" + keyuser + ". " + Newtonsoft.Json.JsonConvert.SerializeObject(user));
            }
            catch { }
        }

        private static void SetTSOnResult(ExtendedResult exres)
        {
            if (exres.DirectRes != null)
            {
                foreach (var F in exres.DirectRes)
                {
                    F.TS = DateTime.Now;
                }
            }
        }

        private static string GetTimeAsHM2(int minutes)
        {
            int Hours = minutes / 60;
            int Days = Hours / 24;
            int Hours2 = Hours - Days * 24;
            int Mins = minutes - Days * 24 * 60 - Hours2 * 60;

            List<string> listtime = new List<string>();
            if (Days > 0) listtime.Add(Days + "d");
            if (Hours2 > 0) listtime.Add(Hours2 + "h");
            if (Mins > 0) listtime.Add(Mins + "min");
            string result = string.Join(" ", listtime);

            return result;
        }

        public static async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            // Некоторые действия 
            eventLogBot.WriteEntry("Error - " + exception.Message + "..." + exception.StackTrace);
        }

        protected override void OnStop()
        {
            eventLogBot.WriteEntry("Staff Search Bot --- OnStop");
        }
    }
}
