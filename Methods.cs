﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using RestSharp;
using SAE;
using SAE.AmadeusPRD;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Caching;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using static System.Net.WebRequestMethods;

namespace StaffSearch
{
    public class Methods
    {
        static ObjectCache cache = MemoryCache.Default;
        static CacheItemPolicy policyuser = new CacheItemPolicy() { SlidingExpiration = TimeSpan.Zero, AbsoluteExpiration = DateTimeOffset.Now.AddMonths(Properties.Settings.Default.CacheUserNResult) };

        //public static NpgsqlConnection conn = new NpgsqlConnection("Server = localhost; Port = 5432; User Id = postgres; Password = e4r5t6; Database = sae");
        //public static NpgsqlConnection conn = new NpgsqlConnection("Server = localhost; Port = 5432; User Id = postgres; Password = OVBtoBAX1972; Database = sae");
        public static NpgsqlConnection conn = new NpgsqlConnection(Properties.Settings.Default.ConnectionString);

        //const string Url = "http://dev-api.staffairlines.com:8033/api";
        const string username = "sae2";
        const string pwd = "ISTbetweenVAR1999";

        public static telegram_user ProfileCommand(long id, string strtoken, EventLog eventLogBot, out string message)
        {
            telegram_user user = new telegram_user() { id = id };
            message = null;

            var token = GetToken(strtoken);

            if (token != null)
            {
                eventLogBot.WriteEntry("GetToken: " + token.type + "/" + token.id_user);

                var exist = TokenAlreadySet(token, id);
                if (exist)
                {
                    eventLogBot.WriteEntry("These authorization parameters have already been assigned to another user!");

                    message = "These authorization parameters have already been assigned to another user!";
                }
                else
                {
                    NpgsqlCommand com = new NpgsqlCommand("select * from telegram_user where id_user=@id_user", conn);
                    com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_user", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = token.type + "_" + token.id_user });
                    try
                    {
                        NpgsqlDataReader reader = com.ExecuteReader();

                        var PT = GetProfile(token.type + "_" + token.id_user).Result;
                        //var PT = new ProfileTokens();
                        string new_ac = PT?.OwnAC ?? "??";

                        if (reader.Read())
                        {
                            user = new telegram_user() { first_use = (DateTime)reader["first_use"], own_ac = reader["own_ac"].ToString(), is_reporter = (bool)reader["is_reporter"], is_requestor = true, Token = token };
                            if (user.id == null)
                            {
                                user.id = id;
                            }

                            eventLogBot.WriteEntry("ProfileCommand. " + JsonConvert.SerializeObject(user));

                            reader.Close();
                            reader.Dispose();
                            com.Dispose();

                            NpgsqlCommand com3 = new NpgsqlCommand("update telegram_user set is_requestor=@is_requestor, id=@id, own_ac=@own_ac where id_user=@id_user", conn);
                            com3.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
                            com3.Parameters.Add(new NpgsqlParameter() { ParameterName = "is_requestor", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean, Value = true });
                            com3.Parameters.Add(new NpgsqlParameter() { ParameterName = "own_ac", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Char, Value = new_ac });
                            com3.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_user", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = token.type + "_" + token.id_user });

                            eventLogBot.WriteEntry(com3.CommandText);

                            try
                            {
                                com3.ExecuteNonQuery();
                            }
                            catch (Exception ex)
                            {
                                eventLogBot.WriteEntry(ex.Message + "..." + ex.StackTrace);
                            }
                            com3.Dispose();
                        }
                        else
                        {
                            reader.Close();
                            reader.Dispose();
                            com.Dispose();

                            NpgsqlCommand com2 = new NpgsqlCommand("insert into telegram_user (id, first_use, own_ac, is_reporter, is_requestor, id_user) values (@id, @first_use, @own_ac, @is_reporter, @is_requestor, @id_user)", conn);
                            com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
                            com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "first_use", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Timestamp, Value = DateTime.Now });
                            com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "own_ac", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Char, Value = new_ac });
                            com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "is_reporter", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean, Value = false });
                            com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "is_requestor", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean, Value = true });
                            com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_user", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = token.type + "_" + token.id_user });

                            eventLogBot.WriteEntry(com2.CommandText);

                            try
                            {
                                com2.ExecuteNonQuery();
                            }
                            catch (Exception ex)
                            {
                                var e = ex.StackTrace;
                                eventLogBot.WriteEntry(ex.Message + "..." + e);
                            }
                            com2.Dispose();

                            user = new telegram_user() { id = id, first_use = DateTime.Now, own_ac = new_ac, is_reporter = false, is_requestor = true, Token = token };
                        }
                    }
                    catch (Exception ex)
                    {
                        eventLogBot.WriteEntry(ex.Message + "..." + ex.StackTrace);
                    }
                }
            }
            else
            {
                eventLogBot.WriteEntry("A valid token was not found!");

                message = "A valid token was not found!";
            }

            if (user != null && user.own_ac != "??")
            {
                var permitted = GetPermittedAC(user.own_ac);
                user.permitted_ac = string.Join("-", permitted.Select(p => p.Permit));
            }

            return user;
        }

        public static List<PermittedAC> GetPermittedAC(string code)
        {
            List<PermittedAC> result = new List<PermittedAC>();

            string keyperm = "permac:" + code;
            var permexist = cache.Contains(keyperm);
            if (permexist) result = (List<PermittedAC>)cache.Get(keyperm);
            else
            {
                NpgsqlCommand com = new NpgsqlCommand("select * from permitted_ac where code=@code", conn);
                com.Parameters.Add(new NpgsqlParameter() { ParameterName = "code", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = code });
                try
                {
                    NpgsqlDataReader reader = com.ExecuteReader();
                    while (reader.Read())
                    {
                        result.Add(new PermittedAC() { Code = code, Permit = reader["permit"].ToString() });
                    }

                    reader.Close();
                    reader.Dispose();
                    com.Dispose();

                    return result;
                }
                catch (Exception ex)
                {
                }
            }

            cache.Add(keyperm, result, policyuser);
            return result;
        }

        public static void UserBlockChat(long id, AirlineAction action)
        {
            NpgsqlCommand com = new NpgsqlCommand("update telegram_user set is_requestor=@is_requestor where id=@id", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "is_requestor", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean, Value = action == AirlineAction.Add });
            com.ExecuteNonQuery();
            com.Dispose();
        }

        public static sign_in GetToken(string token)
        {
            sign_in result = null;
            NpgsqlCommand com = new NpgsqlCommand("select * from tokens where token=@token and ts_valid>now() order by ts_valid limit 1", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "token", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid, Value = Guid.Parse(token) });
            NpgsqlDataReader reader = com.ExecuteReader();
            if (reader.Read())
            {
                result = new sign_in();
                result.type = (short)reader["type"];
                result.id_user = reader["id_user"].ToString();
            }
            reader.Close();
            reader.Dispose();
            com.Dispose();

            return result;
        }

        public static bool TokenAlreadySet(sign_in token, long telegram_user)
        {
            NpgsqlCommand com = new NpgsqlCommand("select count(*) from telegram_user where id<>@id and id_user=@id_user", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = telegram_user });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_user", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = token.type + "_" + token.id_user });
            var o = com.ExecuteScalar();
            //var cnt = (int)com.ExecuteScalar();
            int cnt = 0;
            if (o != null)
            {
                cnt = int.Parse(o.ToString());
            }
            com.Dispose();
            if (cnt > 0) return true; 
            else return false;
        }

        public static void UpdateUserAC(long id, string ac, string current_ac)
        {
            NpgsqlCommand com = new NpgsqlCommand("update telegram_user set own_ac=@own_ac where id=@id", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "own_ac", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Char, Value = ac });
            com.ExecuteNonQuery();
            com.Dispose();

            UpdateAirlinesReporter(current_ac, AirlineAction.Delete);
            UpdateAirlinesReporter(ac, AirlineAction.Add);
        }

        public static void UpdateAirlinesReporter(string ac, AirlineAction action)
        {
            if (!string.IsNullOrEmpty(ac) && ac.Length == 2)
            {
                NpgsqlCommand com = new NpgsqlCommand("select count(*) from telegram_user where is_reporter=true and own_ac=@ac", conn);
                com.Parameters.Add(new NpgsqlParameter() { ParameterName = "ac", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Char, Value = ac });
                var cnt = Convert.ToInt32(com.ExecuteScalar());
                com.Dispose();

                if (action == AirlineAction.Delete)
                {
                    if (cnt == 0)
                    {
                        com = new NpgsqlCommand("update airlines set reporter=false where code=@ac", conn);
                        com.Parameters.Add(new NpgsqlParameter() { ParameterName = "ac", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = ac });
                        com.ExecuteNonQuery();
                        com.Dispose();

                        // отправляем событие «change reporters for ac» в амплитуд
                        string DataJson2 = "[{\"event_type\":\"change reporters for ac\"," +
                            "\"event_properties\":{\"ac\":\"" + ac + "\"," +
                            "\"new_status\":\"false\"}}]";
                        var task2 = Task.Run(async () => await AmplitudePOST(DataJson2));
                    }
                }
                else
                {
                    if (cnt > 0)
                    {
                        com = new NpgsqlCommand("update airlines set reporter=true where code=@ac", conn);
                        com.Parameters.Add(new NpgsqlParameter() { ParameterName = "ac", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = ac });
                        com.ExecuteNonQuery();
                        com.Dispose();

                        // отправляем событие «change reporters for ac» в амплитуд
                        string DataJson = "[{\"event_type\":\"change reporters for ac\"," +
                            "\"event_properties\":{\"ac\":\"" + ac + "\"," +
                            "\"new_status\":\"true\"}}]";
                        var task = Task.Run(async () => await AmplitudePOST(DataJson));
                    }
                }
            }
        }

        public static async Task<string> AmplitudePOST(string Data)
        {
            string result = null;
            var client = new RestClient("https://api.amplitude.com/httpapi");
            var request = new RestRequest((string)null, Method.Post);
            request.AddHeader("content-type", "application/x-www-form-urlencoded");
            request.AddParameter("api_key", "95be547554caecf51c57c691bafb2640");
            request.AddParameter("event", Data);
            await client.ExecuteAsync(request).ContinueWith(t => { result = t.Result.Content; });
            return result;
        }

        public static int TestAC(string ac)
        {
            int result = 0;
            NpgsqlCommand com = new NpgsqlCommand("select * from airlines where code=@ac", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "ac", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = ac });
            try
            {
                NpgsqlDataReader reader = com.ExecuteReader();
                if (reader.Read())
                {
                    result = (int)reader["id"];
                }

                reader.Close();
                reader.Dispose();
                com.Dispose();

                return result;
            }
            catch (Exception ex)
            {
                string s = "123";
                return 0;
            }
        }

        public static AddRequestResponse AddRequest(CallbackQuery query, string id_user)
        {
            var pars = query.Data.Split(' ');
            if (pars.Length == 7)
            {
                NpgsqlCommand com01 = new NpgsqlCommand("select * from telegram_request where id_requestor=@id_requestor and request_status in (0, 1, 2, 3, 4) and origin=@origin and destination=@destination and date_flight=@date_flight and time_flight=@time_flight and operating=@operating and number_flight=@number_flight", conn);
                com01.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_requestor", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = id_user });
                com01.Parameters.Add(new NpgsqlParameter() { ParameterName = "origin", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = pars[1] });
                com01.Parameters.Add(new NpgsqlParameter() { ParameterName = "destination", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = pars[2] });
                com01.Parameters.Add(new NpgsqlParameter() { ParameterName = "date_flight", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = pars[3] });
                com01.Parameters.Add(new NpgsqlParameter() { ParameterName = "time_flight", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = pars[4] });
                com01.Parameters.Add(new NpgsqlParameter() { ParameterName = "operating", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = pars[6] });
                com01.Parameters.Add(new NpgsqlParameter() { ParameterName = "number_flight", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = pars[5] });

                NpgsqlDataReader reader = com01.ExecuteReader();
                var ExistReq = reader.HasRows;
                reader.Close();
                reader.Dispose();
                com01.Dispose();

                if (!ExistReq)
                {
                    var Coll = DebtToken(id_user).Result;

                    string desc = "";
                    var descexist = cache.Contains("key:" + pars[5]);
                    if (descexist) desc = (string)cache.Get("key:" + pars[5]);

                    DateTime DepartureDateTime = new DateTime(int.Parse(pars[3].Substring(4, 2)) + 2000, int.Parse(pars[3].Substring(2, 2)), int.Parse(pars[3].Substring(0, 2)), int.Parse(pars[4].Substring(0, 2)), int.Parse(pars[4].Substring(2, 2)), 0);

                    var msk_time = GetDepartureTimeMsk(pars[1], DepartureDateTime);

                    //var reps = GetReporters(pars[5].Substring(0, 2));
                    //var repgroup = GetReporterGroup(reps);
                    var group_id = GetMaxIdGroup();

                    NpgsqlCommand com = new NpgsqlCommand("insert into telegram_request (id_group, version_request, id_requestor, origin, destination, date_flight, time_flight, operating, number_flight, desc_flight, departure_dt_msk, subscribe_tokens, paid_tokens) values (@id_group, 0, @id_requestor, @origin, @destination, @date_flight, @time_flight, @operating, @number_flight, @desc_flight, @departure_dt_msk, @subscribe_tokens, @paid_tokens)", conn);
                    com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_group", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = group_id });
                    com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_requestor", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = id_user });
                    com.Parameters.Add(new NpgsqlParameter() { ParameterName = "origin", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = pars[1] });
                    com.Parameters.Add(new NpgsqlParameter() { ParameterName = "destination", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = pars[2] });
                    com.Parameters.Add(new NpgsqlParameter() { ParameterName = "date_flight", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = pars[3] });
                    com.Parameters.Add(new NpgsqlParameter() { ParameterName = "time_flight", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = pars[4] });
                    com.Parameters.Add(new NpgsqlParameter() { ParameterName = "operating", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = pars[6] });
                    com.Parameters.Add(new NpgsqlParameter() { ParameterName = "number_flight", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = pars[5] });
                    com.Parameters.Add(new NpgsqlParameter() { ParameterName = "desc_flight", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = desc });
                    com.Parameters.Add(new NpgsqlParameter() { ParameterName = "departure_dt_msk", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Timestamp, Value = msk_time });
                    com.Parameters.Add(new NpgsqlParameter() { ParameterName = "subscribe_tokens", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer, Value = Coll.DebtSubscribeTokens });
                    com.Parameters.Add(new NpgsqlParameter() { ParameterName = "paid_tokens", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer, Value = Coll.DebtNonSubscribeTokens });

                    try
                    {
                        com.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        var e = ex.StackTrace;
                    }
                    com.Dispose();

                    if (Properties.Settings.Default.AgentControl)
                    {
                        com = new NpgsqlCommand("insert into telegram_request (id_group, version_request, id_requestor, origin, destination, date_flight, time_flight, operating, number_flight, desc_flight, departure_dt_msk, subscribe_tokens, paid_tokens) values (@id_group, 1, @id_requestor, @origin, @destination, @date_flight, @time_flight, @operating, @number_flight, @desc_flight, @departure_dt_msk, @subscribe_tokens, @paid_tokens)", conn);
                        com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_group", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = group_id });
                        com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_requestor", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = id_user });
                        com.Parameters.Add(new NpgsqlParameter() { ParameterName = "origin", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = pars[1] });
                        com.Parameters.Add(new NpgsqlParameter() { ParameterName = "destination", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = pars[2] });
                        com.Parameters.Add(new NpgsqlParameter() { ParameterName = "date_flight", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = pars[3] });
                        com.Parameters.Add(new NpgsqlParameter() { ParameterName = "time_flight", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = pars[4] });
                        com.Parameters.Add(new NpgsqlParameter() { ParameterName = "operating", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = pars[6] });
                        com.Parameters.Add(new NpgsqlParameter() { ParameterName = "number_flight", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = pars[5] });
                        com.Parameters.Add(new NpgsqlParameter() { ParameterName = "desc_flight", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = desc });
                        com.Parameters.Add(new NpgsqlParameter() { ParameterName = "departure_dt_msk", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Timestamp, Value = msk_time });
                        com.Parameters.Add(new NpgsqlParameter() { ParameterName = "subscribe_tokens", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer, Value = Coll.DebtSubscribeTokens });
                        com.Parameters.Add(new NpgsqlParameter() { ParameterName = "paid_tokens", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer, Value = Coll.DebtNonSubscribeTokens });

                        try
                        {
                            com.ExecuteNonQuery();
                        }
                        catch (Exception ex)
                        {
                            var e = ex.StackTrace;
                        }
                        com.Dispose();
                    }

                    return new AddRequestResponse() { Cnt = 10, Tokens = Coll };
                }
                else
                {
                    return new AddRequestResponse() { Cnt = -2 };
                }

            }
            return new AddRequestResponse();
        }

        // настройка клиента
        private static HttpClient GetClient()
        {
            HttpClientHandler handler = new HttpClientHandler() { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };

            HttpClient client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("ngrok-skip-browser-warning", "1233");

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(
                        System.Text.ASCIIEncoding.ASCII.GetBytes(
                           $"{username}:{pwd}")));
            return client;
        }

        public static async Task<ExtendedResult> ExtendedSearch(string origin, string destination, DateTime date, string list, bool GetTransfer, GetNonDirectType ntype, int pax, string currency, string lang, string country, string token = "void token", bool sa = true, string ver = "1.0", string ac = "--", string id_user = "")
        {
            try
            {
                using (HttpClient client = GetClient())
                {
                    string Uri = Properties.Settings.Default.UrlApi + "/amadeus/ExtendedSearch?origin=" + origin + "&destination=" + destination + "&date=" + date.ToString("yyyy-MM-dd") + "&list=" + list + "&GetTransfer=" + GetTransfer.ToString() + "&GetNonDirect=" + ntype.ToString() + "&pax=" + pax + "&token=" + token + "&sa=" + sa.ToString() + "&ver=" + ver + "&ac=" + ac + "&currency=" + currency + "&lang=" + lang + "&country=" + country + "&id_user=" + id_user;
                    var response = await client.GetAsync(Uri);
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        ExtendedResult result = JsonConvert.DeserializeObject<ExtendedResult>(json);
                        //result.Alert = Uri;
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                return new ExtendedResult() { Alert = ex.Message + "..." + ex.StackTrace };
            }
            return new ExtendedResult();
        }

        public static async Task<FlightInfo> GetFlightInfo(string origin, string destination, DateTime date, int pax, string aircompany, string number, string token = "void token")
        {
            try
            {
                using (HttpClient client = GetClient())
                {
                    string Uri = Properties.Settings.Default.UrlApi + "/amadeus/GetFlightInfo?origin=" + origin + "&destination=" + destination + "&date=" + date.ToString("yyyy-MM-dd HH:mm") + "&pax=" + pax + "&aircompany=" + aircompany + "&number=" + number + "&now=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "&token=" + token;
                    var response = await client.GetAsync(Uri);
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        FlightInfo result = JsonConvert.DeserializeObject<FlightInfo>(json);
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                return new FlightInfo() { Alert = ex.Message + "..." + ex.StackTrace };
            }
            return new FlightInfo();
        }

        public static async Task<ProfileTokens> TokenProfile(string id_user)
        {
            try
            {
                using (HttpClient client = GetClient())
                {
                    string Uri = Properties.Settings.Default.UrlApi + "/token/Profile?id_user=" + id_user;
                    var response = await client.GetAsync(Uri);
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        ProfileTokens result = JsonConvert.DeserializeObject<ProfileTokens>(json);
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                return new ProfileTokens() { Error = ex.Message + "..." + ex.StackTrace };
            }
            return new ProfileTokens();
        }

        public static async Task<TokenCollection> DebtToken(string id_user)
        {
            try
            {
                using (HttpClient client = GetClient())
                {
                    string Uri = Properties.Settings.Default.UrlApi + "/token/DebtToken?id_user=" + id_user + "&type=oper&operation=request&amount=0";
                    var response = await client.GetAsync(Uri);
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        TokenCollection result = JsonConvert.DeserializeObject<TokenCollection>(json);
                        return result;
                    }
                }
            }
            catch (Exception ex) 
            {
                return new TokenCollection() { Error = ex.Message + "..." + ex.StackTrace };
            }
            return new TokenCollection();
        }

        public static async Task<ProfileTokens> GetProfile(string id_user)
        {
            try
            {
                using (HttpClient client = GetClient())
                {
                    string Uri = Properties.Settings.Default.UrlApi + "/token/Profile?id_user=" + id_user;
                    var response = await client.GetAsync(Uri);
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        ProfileTokens result = JsonConvert.DeserializeObject<ProfileTokens>(json);
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                return new ProfileTokens() { Error = ex.Message + "..." + ex.StackTrace };
            }
            return new ProfileTokens();
        }

        public static DateTime GetDepartureTime(string iata, DateTime dt)
        {
            var result = dt.AddMinutes(GetTimeOffset(iata));
            return result;
        }

        public static string CorrectTimePassed(int minutes)
        {
            var ts = TimeSpan.FromMinutes(minutes);
            return ts.Days + "d " + ts.Hours + "h " + ts.Minutes + "m";
        }

        public static DateTime GetDepartureTimeMsk(string iata, DateTime dt)
        {
            var result = dt.AddMinutes(-GetTimeOffset(iata));
            return result;
        }

        private static int GetTimeOffset(string iata)
        {
            ObjectCache cache = MemoryCache.Default;

            CacheItemPolicy policydir = new CacheItemPolicy() { SlidingExpiration = TimeSpan.Zero, AbsoluteExpiration = DateTimeOffset.Now.AddHours(Properties.Settings.Default.cache_time_offset_hour) };

            string keyts = "offsetiata:" + iata;
            bool contts = cache.Contains(keyts);
            object objts = null;
            if (contts) objts = cache.Get(keyts);

            int result = 0;
            if (contts && (objts is int))
            {
                result = (int)objts;
            }
            else
            {
                var dt = GetAmadCurrentTime(iata);
                result = (int)(dt - DateTime.Now).TotalMinutes;
                cache.Add(keyts, result, policydir);
            }

            return result;
        }

        private static DateTime GetAmadCurrentTime(string iata)
        {
            string AmadeusName = Properties.Settings.Default.AmadeusName;
            string AmadeusPass = Properties.Settings.Default.AmadeusPass;
            string AmadeusPCI = Properties.Settings.Default.AmadeusPCI;
            string AlexAmadeusName = Properties.Settings.Default.AlexAmadeusName;
            string AlexAmadeusPass = Properties.Settings.Default.AlexAmadeusPass;
            string AlexAmadeusPCI = Properties.Settings.Default.AlexAmadeusPCI;
            string AmplitudeUserID = Properties.Settings.Default.AmplitudeUserID;
            bool UseProxy = Properties.Settings.Default.UseProxy;
            string UrlProxy = Properties.Settings.Default.UrlProxy;
            string LogProxy = Properties.Settings.Default.LogProxy;
            string ParProxy = Properties.Settings.Default.ParProxy;


            Amadeus Amad = new Amadeus(AmadeusName, AmadeusPass, AmadeusPCI, "c:\\xmlcrypt", "95be547554caecf51c57c691bafb2640", AmplitudeUserID, false, UseProxy, UrlProxy, LogProxy, ParProxy);
            Amadeus Amad2 = new Amadeus(AlexAmadeusName, AlexAmadeusPass, AlexAmadeusPCI, "c:\\xmlcrypt", "95be547554caecf51c57c691bafb2640", AmplitudeUserID, false, false, null, null, null);

            var gto = Properties.Settings.Default.gate_time_offset;
            DateTime result = new DateTime();
            if (gto == "MOWR228SG")
            {
                result = Amad2.GetDateTimeInAirport(iata);
            }
            else
            {
                result = Amad.GetDateTimeInAirport(iata);
            }
            return result;
        }

        public static void SaveMessageParameters(long chat_id, int message_id, long request_id, short type)
        {
            NpgsqlCommand com = new NpgsqlCommand("insert into telegram_history (chat_id, message_id, request_id, type) values (@chat_id, @message_id, @request_id, @type)", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "chat_id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = chat_id });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "message_id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer, Value = message_id });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "request_id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = request_id });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "type", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Smallint, Value = type });
            com.ExecuteNonQuery();
            com.Dispose();
        }

        public static TelMessage GetMessageParameters(long chat_id)
        {
            TelMessage result = null;
            NpgsqlCommand com = new NpgsqlCommand("select message_id from telegram_history where chat_id=@chat_id and type=3", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "chat_id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = chat_id });
            using (NpgsqlDataReader reader = com.ExecuteReader())
            {
                while (reader.Read())
                {
                    result = new TelMessage() { ChatId = chat_id, MessageId = (int)reader["message_id"] };
                }
                reader.Close();
                reader.Dispose();
            }
            
            com.Dispose();
            return result;
        }

        public static void DelMessageParameters(long chat_id)
        {
            NpgsqlCommand com = new NpgsqlCommand("delete from telegram_history where chat_id=@chat_id and type=3", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "chat_id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = chat_id });
            com.ExecuteNonQuery();
            com.Dispose();
        }

        public static telegram_user GetUser(string id_user)
        {
            telegram_user user = null;

            string keyus = "getuser:" + id_user;
            var usexist = cache.Contains(keyus);
            if (usexist) user = (telegram_user)cache.Get(keyus);
            else
            {
                NpgsqlCommand com = new NpgsqlCommand("select * from telegram_user where id_user=@id_user", conn);
                com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_user", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = id_user });
                NpgsqlDataReader reader = com.ExecuteReader();

                if (reader.Read())
                {
                    var sid = reader["id"].ToString();
                    long? iid = null;
                    if (!string.IsNullOrEmpty(sid))
                    {
                        iid = long.Parse(sid);
                    }
                    var id_user_arr = id_user.Split('_');
                    user = new telegram_user() { id = iid, first_use = (DateTime)reader["first_use"], own_ac = reader["own_ac"].ToString(), Token = new sign_in() { type = short.Parse(id_user_arr[0]), id_user = id_user_arr[1] } };

                    cache.Add(keyus, user, policyuser);
                }
                reader.Close();
                reader.Dispose();
                com.Dispose();
            }

            return user;
        }

        public static telegram_user GetUser(long id)
        {
            telegram_user user = null;

            NpgsqlCommand com = new NpgsqlCommand("select * from telegram_user where id=@id", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
            NpgsqlDataReader reader = com.ExecuteReader();

            if (reader.Read())
            {
                var sid = reader["id"].ToString();
                long? iid = null;
                if (!string.IsNullOrEmpty(sid))
                {
                    iid = long.Parse(sid);
                }
                user = new telegram_user() { id = iid, first_use = (DateTime)reader["first_use"], own_ac = reader["own_ac"].ToString(), is_reporter = (bool)reader["is_reporter"], is_requestor = (bool)reader["is_requestor"] };
                var id_user = reader["id_user"].ToString();
                var arr_id_user = id_user.Split('_');
                if (!string.IsNullOrEmpty(id_user))
                {
                    user.Token = new sign_in() { type = short.Parse(arr_id_user[0]), id_user = arr_id_user[1] };
                }
                if (!user.is_requestor)
                {
                    using (NpgsqlConnection connect = new NpgsqlConnection(Properties.Settings.Default.ConnectionString))
                    {
                        connect.Open();
                        NpgsqlCommand com2 = new NpgsqlCommand("update telegram_user set is_requestor=@is_requestor where id=@id", connect);
                        com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
                        com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "is_requestor", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean, Value = true });
                        com2.ExecuteNonQuery();
                        com2.Dispose();
                        connect.Close();
                    }
                }
            }
            else
            {
                user = new telegram_user() { id = id, own_ac = "??", is_reporter = false, is_requestor = true };
            }

            reader.Close();
            reader.Dispose();
            com.Dispose();

            return user;
        }

        public static List<long> GetReporters(string ac)
        {
            List<long> result = new List<long>();
            NpgsqlCommand com = new NpgsqlCommand("select id from telegram_user where is_reporter=true and own_ac=@ac order by first_use", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "ac", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Char, Value = ac });
            using (NpgsqlDataReader reader = com.ExecuteReader())
            {
                while (reader.Read())
                {
                    result.Add((long)reader["id"]);
                }
                reader.Close();
                reader.Dispose();
            }
            com.Dispose();
            return result;
        }

        public static ReporterGroup GetReporterGroup(List<long> all)
        {
            ReporterGroup result = new ReporterGroup();
            if (all.Count <= 1)
            {
                result.Main.Add(all[0]);
            }
            else
            {
                var half = Convert.ToInt32(Math.Round(all.Count / 2.0));
                result.Main = all.GetRange(0, half);
                result.Control = all.GetRange(half, all.Count - half);
            }
            return result;
        }

        public static long GetMaxIdGroup()
        {
            NpgsqlCommand com = new NpgsqlCommand("select max(id_group)+1 from telegram_request", conn);
            var result = Convert.ToInt64(com.ExecuteScalar());
            return result;
        }
    }
}
