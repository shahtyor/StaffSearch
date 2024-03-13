using SAE.AmadeusPRD;
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

            Methods.conn.Open();
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

        public static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            policyuser = new CacheItemPolicy() { SlidingExpiration = TimeSpan.Zero, AbsoluteExpiration = DateTimeOffset.Now.AddMonths(Properties.Settings.Default.CacheUserNResult) };

            // Некоторые действия
            eventLogBot.WriteEntry(Newtonsoft.Json.JsonConvert.SerializeObject(update));
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

                var msg = message?.Text?.Split(' ')[0];

                if (message?.Text?.ToLower() == "\U0001F7e1" || message?.Text?.ToLower() == "U+1F534" || message?.Text?.ToLower() == "/stop")
                {
                    return;
                }

                if (message?.Text?.ToLower() == "/start")
                {
                    await botClient.SendTextMessageAsync(message.Chat, "Инструкция, как пользоваться ботом");

                    if (user.Token == null)
                    {
                        await botClient.SendTextMessageAsync(message.Chat, "Enter the token:");

                        cache.Add("User" + userid.Value, "entertoken", policyuser);
                    }
                    else if (user.own_ac == "??")
                    {
                        await botClient.SendTextMessageAsync(message.Chat, "Own ac: " + user.own_ac + Environment.NewLine + "Specify your airline. Enter your airline's code:");

                        cache.Add("User" + userid.Value, "preset", policyuser);
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(message.Chat, "Own ac: " + user.own_ac + Environment.NewLine + "You can do a search!");
                    }

                    //await botClient.SendTextMessageAsync(message.Chat, "Enter the token:");

                    //cache.Add("User" + userid.Value, "entertoken", policyuser);

                    return;
                }
                else if (message?.Text?.ToLower() == "/profile")
                {
                    await botClient.SendTextMessageAsync(message.Chat, "Enter the token:");

                    cache.Add("User" + userid.Value, "entertoken", policyuser);

                    return;
                }
                else if (message?.Text?.ToLower() == "/preset")
                {
                    await botClient.SendTextMessageAsync(message.Chat, "Specify your airline. Enter your airline's code:");

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
                                await botClient.SendTextMessageAsync(message.Chat, "Own ac: " + user.own_ac + Environment.NewLine + "Specify your airline. Enter your airline's code:");

                                cache.Add("User" + userid.Value, "preset", policyuser);
                            }
                            else
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "Own ac: " + user.own_ac + Environment.NewLine + "Permitted: " + (string.IsNullOrEmpty(user.permitted_ac) ? "All Airlines permitted" : user.permitted_ac) + Environment.NewLine + "You can do a search!");
                            }
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(message.Chat, alert);
                            if (user is null || user.id == 0 || user.Token is null)
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "Enter the token:");
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
                        await botClient.SendTextMessageAsync(message.Chat, "The GUID must contain 32 digits and 4 hyphens!");
                        if (user is null || user.Token is null)
                        {
                            await botClient.SendTextMessageAsync(message.Chat, "Enter the token:");
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
                        Methods.UpdateUserAC(message.Chat.Id, ac.ToUpper());
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

                        await botClient.SendTextMessageAsync(message.Chat, "Own ac: " + ac.ToUpper() + Environment.NewLine + "Permitted: " + (string.IsNullOrEmpty(sperm) ? "All Airlines permitted" : sperm) + Environment.NewLine + "You can do a search!");
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(message.Chat, "Own ac: " + ac + " not found! Enter the correct code of your airline:");
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
                    if (comsplit.Length > 1)
                    {
                        pax = Convert.ToInt32(comsplit[1]);
                    }

                    eventLogBot.WriteEntry("From: " + From + ", To: " + To + ", dt: " + dt + ", pax: " + pax);

                    if (dt.Length <= 4)
                    {
                        DateTime searchdt = DateTime.Today;
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

                            if (searchdt < DateTime.Today)
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
                        }
                        else if (dt.Length == 0)
                        {
                            searchdt = DateTime.Today;
                        }

                        DateTime DepNow = DateTime.Now;
                        try
                        {
                            DepNow = Methods.GetDepartureTime(From, DateTime.Now);
                        }
                        catch
                        {
                            await botClient.SendTextMessageAsync(message.Chat, "Неизвестный пункт вылета!");
                            return;
                        }

                        eventLogBot.WriteEntry("DepNow: " + DepNow.ToString("dd-MM-yyyy HH:mm") + ", user=" + user?.id + ", token=" + user?.Token?.id_user);

                        bool SearchAvailable = true;
                        if (DepNow.Date != searchdt.Date)
                        {
                            if (user == null || user.Token == null)
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "Для поиска не на текущую дату необходимо авторизоваться (/profile)");
                                SearchAvailable = false;
                            }
                            else
                            {
                                var Prof = await Methods.TokenProfile(user.Token.type + "_" + user.Token.id_user);

                                eventLogBot.WriteEntry("Prof: " + Prof.SubscribeTokens + "-" + Prof.NonSubscribeTokens + "-" + Prof.Premium.ToString());

                                if (!Prof.Premium)
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, "Для поиска на любую дату необходимо приобрести подписку");
                                    SearchAvailable = false;
                                }
                            }
                        }

                        if (SearchAvailable)
                        {
                            var findbeg = "Search from " + From + " to " + To + " on " + searchdt.ToString("MMMM d", CultureInfo.CreateSpecificCulture("en-US")) + " for " + pax + " pax";
                            await botClient.SendTextMessageAsync(message.Chat, findbeg);

                            if (user == null)
                            {
                                user = new telegram_user() { id = message.Chat.Id };
                            }
                            if (user.permitted_ac == null) user.permitted_ac = "";

                            eventLogBot.WriteEntry("permitted_ac: " + user?.permitted_ac);

                            ExtendedResult exres0 = await Methods.ExtendedSearch(From, To, searchdt, user.permitted_ac, false, GetNonDirectType.Off, pax, "USD", "EN", "RU", "bot" + message.Chat.Id, false, "3.0");
                            SetTSOnResult(exres0);
                            user.SearchParameters = new SearchParam() { Origin = From, Destination = To, Date = searchdt, Pax = pax };
                            user.exres = exres0;

                            eventLogBot.WriteEntry("DirectRes.Count: " + exres0.DirectRes?.Count + ", Uri=" + exres0.Alert);

                            UpdateUserInCache(user);

                            if (exres0.DirectRes?.Count > 0)
                            {
                                string res = string.Empty;
                                int i = 0;
                                var dlen = exres0.DirectRes.Max(x => x.DepartureTerminal.Length);
                                var alen = exres0.DirectRes.Max(x => x.ArrivalTerminal.Length);
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
                                await botClient.SendTextMessageAsync(message.Chat, string.IsNullOrEmpty(exres0.Alert) ? "Not Found!" : exres0.Alert);
                            }
                        }
                    }

                    return;
                }

                int n;
                Guid g;
                bool isNumeric = int.TryParse(message?.Text, out n);
                bool isGuid = Guid.TryParse(message?.Text, out g);
                if (isNumeric && !isGuid && user != null && user.exres != null)
                {
                    int? cntf = user.exres?.DirectRes?.Count;
                    if (user.exres?.DirectRes?.Count > 0)
                    {
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
                                FlightInfo FInfo = await Methods.GetFlightInfo(Fl.Origin, Fl.Destination, Fl.DepartureDateTime, px, Fl.MarketingCarrier, Fl.FlightNumber);
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

                            string res = Fl.MarketingName + " " + Fl.MarketingCarrier + Fl.FlightNumber + " " + Fl.EquipmentName + " -" + Fl.TS + "-" + Environment.NewLine + Environment.NewLine +
                                Fl.DepartureDateTime.ToString("dd MMM, ddd", CultureInfo.CreateSpecificCulture("en-US")) + " " + Fl.DepartureDateTime.ToString("HH:mm") + " " + (!string.IsNullOrEmpty(Fl.DepartureCityName) ? Fl.DepartureCityName + ", " : "") + Fl.DepartureName + " (" + Fl.Origin + ")" + (!string.IsNullOrEmpty(Fl.DepartureTerminal) ? ", Terminal " + Fl.DepartureTerminal : "") + Environment.NewLine +
                                "In flight " + GetTimeAsHM2(Fl.Duration) + Environment.NewLine +
                                Fl.ArrivalDateTime.ToString("dd MMM, ddd", CultureInfo.CreateSpecificCulture("en-US")) + " " + Fl.ArrivalDateTime.ToString("HH:mm") + " " + (!string.IsNullOrEmpty(Fl.ArrivalCityName) ? Fl.ArrivalCityName + ", " : "") + Fl.ArrivalName + " (" + Fl.Destination + ")" + (!string.IsNullOrEmpty(Fl.ArrivalTerminal) ? ", Terminal " + Fl.ArrivalTerminal : "") + Environment.NewLine + Environment.NewLine +
                            "<b>Status (current): " + (Fl.Rating == RType.Red ? "Bad" : (Fl.Rating == RType.Green ? "Good" : "Medium")) + ", " + Fl.AllPlaces + " seats" + "</b> " + srat + Environment.NewLine + Environment.NewLine +
                            "Classes available: " + string.Join(" ", Fl.NumSeatsForBookingClass) + Environment.NewLine + Environment.NewLine +
                                (Fl.C > 0 ? "<b>Status (forecast): " + ForecastStatus + " (Accuracy: " + Fl.Accuracy + ")" + "</b> " + ForeIcon : "");

                            cache.Add("key:" + Fl.MarketingCarrier + Fl.FlightNumber, res, policyuser);

                            var ikm = new InlineKeyboardMarkup(new[]
                            {
                                new[]
                                {
                                    InlineKeyboardButton.WithCallbackData("Request", "/com " + Fl.Origin + " " + Fl.Destination + " " + Fl.DepartureDateTime.ToString("ddMMyy HHmm") + " " + Fl.MarketingCarrier + Fl.FlightNumber + " " + Fl.OperatingCarrier),
                                },
                            });

                            Message tm = await botClient.SendTextMessageAsync(message.Chat, res, null, Telegram.Bot.Types.Enums.ParseMode.Html, replyMarkup: ikm);
                            Methods.SaveMessageParameters(tm.Chat.Id, tm.MessageId, 0, 3);
                        }
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(message.Chat, "Do a search first!");
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
                        Methods.UpdateUserAC(userid.Value, "??");
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

                        await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Own ac: ??" + Environment.NewLine + "Permitted: All Airlines permitted", replyMarkup: ikm);
                    }
                    else if (message == "/preset")
                    {
                        await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Enter your airline's code:");

                        cache.Add("User" + callbackquery.From.Id, "preset", policyuser);
                    }

                    if (message.Length >= 4 && message.Substring(0, 4) == "/com")
                    {
                        var pars = message.Split(' ');
                        if (pars.Length == 7)
                        {
                            bool RequestAvailable = true;
                            if (user == null || user.Token == null)
                            {
                                RequestAvailable = false;
                                await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Для отправки запроса необходимо авторизоваться (/profile)");
                            }
                            else
                            {
                                int AmountTokensForRequest = int.Parse(Properties.Settings.Default.AmountTokensForRequest);
                                var Prof = await Methods.TokenProfile(user.Token.type + "_" + user.Token.id_user);

                                eventLogBot.WriteEntry("TokenProfile: " + user.Token.type + "_" + user.Token.id_user + ", Prof: " + Newtonsoft.Json.JsonConvert.SerializeObject(Prof));

                                var CntTok = Prof.SubscribeTokens + Prof.NonSubscribeTokens;
                                if (CntTok < AmountTokensForRequest)
                                {
                                    RequestAvailable = false;
                                    await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Для запроса необходимы токены (" + AmountTokensForRequest + ")");
                                }
                            }

                            if (RequestAvailable)
                            {
                                eventLogBot.WriteEntry(user.Token.type + "_" + user.Token.id_user);

                                var remreq = Methods.AddRequest(callbackquery, user.Token.type + "_" + user.Token.id_user);

                                if (remreq.Cnt == -1)
                                {
                                    await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "You have made the maximum number of requests!");
                                }
                                else if (remreq.Cnt == -2)
                                {
                                    await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "You have already made a request for this flight!");
                                }
                                else
                                {
                                    var mespar = Methods.GetMessageParameters(callbackquery.Message.Chat.Id);
                                    await botClient.EditMessageReplyMarkupAsync(callbackquery.Message.Chat.Id, mespar.MessageId, replyMarkup: null);
                                    Methods.DelMessageParameters(callbackquery.Message.Chat.Id);

                                    DateTime dreq = new DateTime(2000 + int.Parse(pars[3].Substring(4, 2)), int.Parse(pars[3].Substring(2, 2)), int.Parse(pars[3].Substring(0, 2)), int.Parse(pars[4].Substring(0, 2)), int.Parse(pars[4].Substring(2, 2)), 0);

                                    var reqpost = "You request " + pars[5].Substring(0, 2) + " " + dreq.ToString("dMMM HH:mm", CultureInfo.CreateSpecificCulture("en-US")) + " posted. You have " + remreq.Cnt + " requests left. SubscribeTokens: " + remreq.Tokens.SubscribeTokens + ", NonSubscribeTokens: " + remreq.Tokens.NonSubscribeTokens + ", DebtSubscribeTokens: " + remreq.Tokens.DebtSubscribeTokens + ", DebtNonSubscribeTokens: " + remreq.Tokens.DebtNonSubscribeTokens;
                                    await botClient.SendTextMessageAsync(callbackquery.Message.Chat, reqpost);
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
                }
                else if (CM.Status == ChatMemberStatus.Member) // Разблокировал чат
                {
                    Methods.UserBlockChat(userid.Value, AirlineAction.Add);
                }
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
