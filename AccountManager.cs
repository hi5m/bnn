// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Data.Common;
using System.Net;
using System.Text;
using Binance.Spot;
using Binance.Spot.Models;
using DbSpace;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Globalization;
using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Objects;
using Binance.Net.Objects.Models.Futures;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects;
using System;
using System.Net.Http.Json;
// using Bybit

namespace Bnncmd
{
    public class BnnException : Exception
    {
        public BnnException(string message) : base(message) { }
        public BnnException(string message, int code) : base(message) { Code = code; }
        public int Code { get; private set; }

        public const int StopPriceWouldTrigger = -2010;

        public const int UnknownOrderSent = -2011;
    }


    public static class OrderStatus
    {
        public static string New { get { return "NEW"; } }

        public static string Filled { get { return "FILLED"; } }

        public static string Canceled { get { return "CANCELED"; } }

        public static string Unknown { get { return "UNKNOWN"; } }
    }


    internal class AccountManager : IDisposable
    {
        #region Variables
        public readonly DbConnection DbConnection = DB.CreateConnection();

        private readonly List<BaseStrategy> _coins = [];

        public static IConfigurationRoot Config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .Build();

        public decimal SpotAmount { get; set; } = 0;
        public decimal FuturesAmount { get; set; } = 0;
        public BaseDealParams Statistics { get; private set; } = new BaseDealParams();

        // public static readonly decimal Fee = 0.002M; // double
        public static decimal Fee = 0; // 0.002M; // double

        public long StartTime { get; private set; } = 0;
        public long EndTime { get; private set; } = 0;

        public static int OrderBookDepth { get; set; } = 20;
        public static bool IsTest { get; private set; }
        public static bool OutLog { get; private set; } = true;
        public static int LogLevel { get; set; } = 1;

        public static readonly int OneDayKlines = 1 * 24 * 60;

        private static readonly int _findRangeInterval = 9;
        //private static readonly int _findRangeInterval = 55;

        public static decimal Timeframe { get; private set; } = 5;

        public static int FindRangeMinsCount() { return _findRangeInterval * OneDayKlines; }

        public static int FindRangeKlinesCount() { return Timeframe < 1 ? FindRangeMinsCount() * 60 : (int)(FindRangeMinsCount() / Timeframe); }

        private readonly bool _autonomic = true;

        private System.Threading.Timer? _tickersTimer;
        private readonly int _tickersPeriod = 1000;

        public string QuoteCurrencyName { get; set; } = "USDT";
        #endregion

        public AccountManager()
        {
            IsTest = Config.GetValue<double>("IsTest") == 1;
            BnnUtils.IsTest = IsTest;
            if (!IsTest) _timer = new System.Threading.Timer(UpdateSystemTime, null, 0, 45 * 60 * 1000); // for user stream update less than hour
            var dummy = "111" + "111";

            var startDate = DateTime.ParseExact(Config.GetValue<string>("StartTime") ?? "", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            StartTime = ((DateTimeOffset)startDate).ToUnixTimeSeconds() * 1000;
            var endDate = DateTime.ParseExact(Config.GetValue<string>("EndTime") ?? "", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            EndTime = ((DateTimeOffset)endDate).ToUnixTimeSeconds() * 1000;

            Fee = Config.GetValue<decimal>("Fee");
            Timeframe = Config.GetValue<decimal>("Timeframe");
            OrderBookDepth = Config.GetValue<int>("OrderBookDepth");
            LogLevel = Config.GetValue<int>("LogLevel");
            // DetailedLog = Config.GetValue<int>("DetailedLog") == 1;
            // OutLog = Config.GetValue<int>("OutLog") == 1;

            var tK = Config.GetValue<string>("TK") ?? string.Empty;
            var tk = Crypto.DecryptStringAES(tK, dummy);
            var tS = Config.GetValue<string>("TS") ?? string.Empty;
            var ts = Crypto.DecryptStringAES(tS, dummy);
            _sa = new SpotAccountTrade(apiKey: tk, apiSecret: ts);
            _us = new UserDataStreams(apiKey: tk, apiSecret: ts);

            var creds = new ApiCredentials(tk, ts);
            BinanceClient.SetApiCredentials(creds);
            // Exchange.Binance.Client.SetApiCredentials(creds);

            // Console.WriteLine("Exchange.Binance.Client.SetApiCredentials");

            var activeSymbols = Config.GetSection($"ActiveSymbols");
            var currencies = activeSymbols.Get<string[]>();
            if (currencies == null) return;
            foreach (var c in currencies)
            {
                var newCoin = BaseStrategy.CreateStrategy(c, this);
                if (newCoin.SymbolName.Contains("USDT")) QuoteCurrencyName = "USDT"; // hack for exchanger
                else if (newCoin.SymbolName.Contains("FDUSD")) QuoteCurrencyName = "FDUSD";
                else QuoteCurrencyName = "BTC";
                _coins.Add(newCoin);
            }
        }

        private BaseStrategy? GetCurrencyByName(string coinName)
        {
            coinName = coinName.ToLower();
            foreach (var c in _coins)
            {
                if (c.SymbolName.ToLower() == coinName) return c;
            }
            return null;
        }

        public void LoadSnpData()
        {
            long lastSymbolKline = 0;
            DB.OpenQuery(DbConnection, "SELECT MAX(OpenTime) LastKline FROM candlestick1m WHERE SymbolId = 22", null, dr =>
            {
                lastSymbolKline = dr["LastKline"] is DBNull ? -1 : (long)dr["LastKline"];
            });
            var lasLoadedtTime = BnnUtils.UnitTimeToDateTime(lastSymbolKline).AddDays(-1);
            // Console.WriteLine($"{lastSymbolKline} - {lasLoadedtTime}");
            var year = lasLoadedtTime.Year + (lasLoadedtTime.Month + 1) / 13;
            var month = (lasLoadedtTime.Month + 1).ToString();
            if (lasLoadedtTime.Month == 12) month = "01";
            if (month.Length == 1) month = '0' + month;
            var queryUrl = $"https://www.alphavantage.co/query?function=TIME_SERIES_INTRADAY&interval=1min&symbol=SPY&apikey=TWDL3GNB7B3KX09O&month={year}-{month}&outputsize=full";
            Console.WriteLine(queryUrl);

            // return;
            try
            {
                // var queryUrl = "https://www.alphavantage.co/query?function=TIME_SERIES_INTRADAY&symbol=IBM&interval=5min&apikey=demo";
                var queryUri = new Uri(queryUrl);

                using var client = new HttpClient();
                var klinesData = client.GetStringAsync(queryUri).Result;
                var jsonData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, dynamic>>(klinesData);
                if (jsonData == null) return;

                var counter = 0;
                var days = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, dynamic>>(jsonData.ElementAt(1).Value);
                var script = $"INSERT INTO candlestick1m (SymbolId, OpenTime, OpenPrice, HighPrice, LowPrice, ClosePrice, Volume) VALUES "; // , Volume, QuoteVolume, Trades, TakerBaseVolume
                foreach (var d in days)
                {
                    var dateTime = DateTime.ParseExact(d.Key, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    var estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                    // DateTime cstTime = TimeZoneInfo.ConvertTimeFromUtc(timeUtc, cstZone);
                    DateTime utcTime = TimeZoneInfo.ConvertTimeToUtc(dateTime, estZone);
                    var timeStamp = BnnUtils.DateTimeToUnitTime(utcTime); // .AddHours(7)

                    Dictionary<string, dynamic> klines = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, dynamic>>(d.Value);
                    if (timeStamp <= lastSymbolKline) continue;
                    /// lastSymbolKline = k[0];
                    script += $"(22, {timeStamp}, {klines.ElementAt(0).Value}, {klines.ElementAt(1).Value}, {klines.ElementAt(2).Value}, {klines.ElementAt(3).Value}, {klines.ElementAt(4).Value}), ";
                    if (counter++ % 100 == 0) Console.Write("."); // 100
                }
                script = string.Concat(script.AsSpan(0, script.Length - 2), ";");
                DB.ExecQuery(DbConnection, script, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        #region ApiRoutines
        /// <summary>
        /// API routines
        /// </summary>
        /// <returns></returns>
        private readonly SpotAccountTrade _sa;
        private readonly UserDataStreams _us;
        public BinanceRestClient BinanceClient { get; private set; } = new();
        private readonly BinanceSocketClient _binanceSocket = new();

        private string _userDataKey = string.Empty;

        public static void GetFundingRates()
        {
            Console.WriteLine($"{DateTime.Now}\n\t");
            var futuresClient = new BinanceRestClient();
            StringBuilder profitsFile = new();
            // var fundingInfo = futuresClient.UsdFuturesApi.ExchangeData.GetMarkPricesAsync().Result;
            // var queryCount = 0;
            // string[] symbolsArray = { "XCNUSDT", "SWARMSUSDT", "XTZUSDT", "BTCUSDC", "PROMPTUSDT", "BROCCOLIF3BUSDT", "IOTAUSDT", "SCUSDT", "KAITOUSDT", "GRASSUSDT", "BBUSDT", "WCTUSDT", "STEEMUSDT", "VICUSDT", "1000000MOGUSDT", "ANKRUSDT", "LINKUSDC", "EDUUSDT", "FLUXUSDT", "KAIAUSDT", "MEWUSDT", "ALPHAUSDT", "DUSKUSDT", "VANAUSDT", "GMXUSDT", "OPUSDT", "MUBARAKUSDT", "SFPUSDT", "1000SHIBUSDT", "AMBUSDT", "RVNUSDT", "COOKIEUSDT", "PENGUUSDT", "NULSUSDT", "MEMEUSDT", "BCHUSDC", "APTUSDT", "GOATUSDT", "GLMRUSDT", "TUSDT", "BADGERUSDT", "DOGEUSDC", "LQTYUSDT", "BIDUSDT", "DASHUSDT", "ACTUSDT", "CATIUSDT", "API3USDT", "RSRUSDT", "ARBUSDT", "KERNELUSDT", "MINAUSDT", "XVSUSDT", "COWUSDT", "MOVRUSDT", "RLCUSDT", "KAITOUSDC", "ANIMEUSDT", "SOONUSDT", "MAGICUSDT", "ALTUSDT", "ARUSDT", "ZKUSDT", "ZENUSDT", "INJUSDT", "1000XUSDT", "1000SHIBUSDC" };
            // string[] symbolsArray = { "VIRTUALUSDT", "AUCTIONUSDT", "AIUSDT", "KLAYUSDT", "LTCUSDT", "FUNUSDT", "DOODUSDT", "MANTAUSDT", "QNTUSDT", "LRCUSDT", "BANKUSDT", "ALPINEUSDT", "COMBOUSDT", "LDOUSDT", "SPXUSDT", "CETUSUSDT", "ONEUSDT", "LPTUSDT", "BONDUSDT", "OBOLUSDT", "STXUSDT", "IDUSDT", "BLZUSDT", "1000LUNCUSDT", "AAVEUSDT", "NEIROUSDT", "RAYSOLUSDT", "GRTUSDT", "MOCAUSDT", "ETHWUSDT", "DYDXUSDT", "HIPPOUSDT", "BUSDT", "AGTUSDT", "FTTUSDT", "RONINUSDT", "NMRUSDT", "ICPUSDT", "WIFUSDC", "IPUSDC", "ONDOUSDT", "COSUSDT", "SWELLUSDT", "PIPPINUSDT", "XRPUSDC", "KSMUSDT", "KEYUSDT", "KNCUSDT", "REIUSDT", "VIDTUSDT", "FETUSDT", "BSWUSDT", "BAKEUSDT", "SKYAIUSDT", "XMRUSDT", "EGLDUSDT", "ARKMUSDT", "TIAUSDC", "FIOUSDT", "JSTUSDT", "QUICKUSDT", "PROMUSDT", "SNXUSDT", "MATICUSDT", "RPLUSDT", "HIGHUSDT", "ZKJUSDT", "PIXELUSDT" };
            // string[] symbolsArray = { "CVCUSDT", "1000SATSUSDT", "CHESSUSDT", "RDNTUSDT", "SOLUSDT", "SANDUSDT", "JTOUSDT", "PHAUSDT", "FLOWUSDT", "XAIUSDT", "LEVERUSDT", "CRVUSDT", "KMNOUSDT", "BNBUSDC", "HAEDALUSDT", "1000CHEEMSUSDT", "BANANAS31USDT", "BOMEUSDT", "DEGENUSDT", "BTCDOMUSDT", "ORBSUSDT", "CFXUSDT", "BMTUSDT", "NEARUSDC", "WAVESUSDT", "ZILUSDT", "PAXGUSDT", "WUSDT", "MILKUSDT", "HOOKUSDT", "LINAUSDT", "AKTUSDT", "TSTUSDT", "USUALUSDT", "ARCUSDT", "VINEUSDT", "PUMPUSDT", "NILUSDT", "FISUSDT", "NKNUSDT", "STRKUSDT", "AVAUSDT", "ZEREBROUSDT", "COMPUSDT", "1MBABYDOGEUSDT", "VELODROMEUSDT", "UNIUSDT", "HBARUSDC", "GHSTUSDT", "CHRUSDT", "RAREUSDT", "1000BONKUSDC", "IOSTUSDT", "FTMUSDT", "GRIFFAINUSDT", "DOLOUSDT", "STOUSDT", "1INCHUSDT", "ETCUSDT", "HBARUSDT", "GMTUSDT", "GASUSDT" };
            // string[] symbolsArray = { "FHEUSDT", "SUSHIUSDT", "AGIXUSDT", "OMUSDT", "CRVUSDC", "LUMIAUSDT", "BTCUSDT", "ALCHUSDT", "APEUSDT", "STGUSDT", "JUPUSDT", "ETHBTC", "SUSDT", "BNXUSDT", "REEFUSDT", "CELRUSDT", "BABYUSDT", "TWTUSDT", "SXPUSDT", "TRUMPUSDC", "SYSUSDT", "SUPERUSDT", "SCRUSDT", "ACEUSDT", "BCHUSDT", "JELLYJELLYUSDT", "CAKEUSDT", "DOGSUSDT", "BIOUSDT", "1000WHYUSDT", "NFPUSDT", "ADAUSDT", "KDAUSDT", "GUNUSDT", "MANAUSDT", "FARTCOINUSDT", "MTLUSDT", "GPSUSDT", "LITUSDT", "IOUSDT", "TNSRUSDT", "DOGEUSDT", "BELUSDT", "DEEPUSDT", "PYTHUSDT", "XRPUSDT", "RAYUSDT", "DEGOUSDT", "FILUSDC", "HYPERUSDT", "MOODENGUSDT", "AXSUSDT", "QTUMUSDT", "MLNUSDT", "1000PEPEUSDC", "SIGNUSDT", "DENTUSDT", "HIVEUSDT", "TRXUSDT", "MEMEFIUSDT", "STMXUSDT", "SUIUSDC", "ZROUSDT", "BALUSDT", "RENDERUSDT", "ATOMUSDT", "DIAUSDT", "MKRUSDT", "AGLDUSDT", "STRAXUSDT", "ATAUSDT", "ALPACAUSDT", "HFTUSDT", "TROYUSDT", "PEOPLEUSDT", "CYBERUSDT", "NTRNUSDT", "TONUSDT", "ETHFIUSDT", "BNTUSDT", "BANANAUSDT", "USDCUSDT", "SOLUSDC", "BTCSTUSDT", "ARBUSDC", "SCRTUSDT", "1000RATSUSDT", "BNBUSDT", "ALGOUSDT", "SAFEUSDT", "MAVIAUSDT", "1000XECUSDT", "PNUTUSDC", "RUNEUSDT", "NEIROETHUSDT", "NEARUSDT", "ENAUSDT", "MDTUSDT", "BSVUSDT", "SAGAUSDT", "LISTAUSDT", "BICOUSDT", "BOMEUSDC", "UMAUSDT", "SLPUSDT", "RENUSDT", "ADAUSDC", "BLURUSDT", "PORTALUSDT", "IOTXUSDT", "ENSUSDT", "EPICUSDT", "LINKUSDT", "IPUSDT", "SUIUSDT", "1000PEPEUSDT", "DUSDT", "ILVUSDT", "OGNUSDT", "LOKAUSDT", "WAXPUSDT", "PARTIUSDT", "XEMUSDT", "BANDUSDT", "FXSUSDT", "FORMUSDT", "ORDIUSDC", "AERGOUSDT", "PUNDIXUSDT", "BERAUSDT", "FILUSDT", "ORDIUSDT", "ORCAUSDT" };
            // string[] symbolsArray = { "ONGUSDT", "PONKEUSDT", "VTHOUSDT", "TUTUSDT", "PERPUSDT", "PENDLEUSDT", "SIRENUSDT", "LSKUSDT", "ACXUSDT", "DEFIUSDT", "ICXUSDT", "AVAAIUSDT", "JOEUSDT", "SOLVUSDT", "SXTUSDT", "SYRUPUSDT", "BATUSDT", "MELANIAUSDT", "JASMYUSDT", "CGPTUSDT", "FORTHUSDT", "LTCUSDC", "ETHFIUSDC", "SUNUSDT", "AVAXUSDT", "VVVUSDT", "TRUUSDT", "HMSTRUSDT", "OMGUSDT", "BIGTIMEUSDT", "CTKUSDT", "AIOTUSDT", "KASUSDT", "SEIUSDT", "AIXBTUSDT", "BEAMXUSDT", "BRUSDT", "ASRUSDT", "DODOXUSDT", "YGGUSDT", "AEVOUSDT", "FIDAUSDT", "NXPCUSDT", "DARUSDT", "UXLINKUSDT", "AI16ZUSDT", "ETHUSDT", "DRIFTUSDT", "VOXELUSDT", "C98USDT", "SLERFUSDT", "BANUSDT", "OXTUSDT", "RADUSDT", "CELOUSDT", "KAVAUSDT", "KOMAUSDT", "LAYERUSDT", "BRETTUSDT", "BROCCOLI714USDT", "TOKENUSDT", "GUSDT", "AXLUSDT", "EIGENUSDT", "ZECUSDT", "IMXUSDT", "UNFIUSDT", };
            string[] symbolsArray = { "REDUSDT", "TURBOUSDT", "1000FLOKIUSDT", "COTIUSDT", "FRONTUSDT", "THETAUSDT", "VETUSDT", "WLDUSDT", "GTCUSDT", "OMNIUSDT", "POLUSDT", "NEOUSDC", "WALUSDT", "1000CATUSDT", "ARPAUSDT", "ARKUSDT", "METISUSDT", "MAVUSDT", "HOTUSDT", "1000BONKUSDT", "REZUSDT", "SANTOSUSDT", "DEXEUSDT", "STPTUSDT", "ALICEUSDT", "MYROUSDT", "GALAUSDT", "THEUSDT", "MBOXUSDT", "TRUMPUSDT", "TIAUSDT", "DOTUSDT", "EOSUSDT", "YFIUSDT", "TAOUSDT", "INITUSDT", "NOTUSDT", "MASKUSDT", "AVAXUSDC", "SSVUSDT", "SHELLUSDT", "LOOMUSDT", "XVGUSDT", "FLMUSDT", "SNTUSDT", "TLMUSDT", "CKBUSDT", "VANRYUSDT", "RIFUSDT", "WIFUSDT", "CVXUSDT", "PNUTUSDT", "CHZUSDT", "HIFIUSDT", "PLUMEUSDT", "MORPHOUSDT", "GLMUSDT", "ACHUSDT", "ONTUSDT", "TRBUSDT", "USTCUSDT", "ZRXUSDT", "CTSIUSDT", "XLMUSDT", "SONICUSDT" };

            foreach (var s in symbolsArray) // fundingInfo.Data
            {
                var currDate = DateTime.Now.AddYears(-1);
                decimal yearRateSum = 0;
                decimal monthRateSum = 0;
                while (DateTime.Now.Subtract(currDate).TotalHours > 9)
                {
                    var fundingRates = futuresClient.UsdFuturesApi.ExchangeData.GetFundingRatesAsync(s, currDate, DateTime.Now, 1000).Result;
                    foreach (var r in fundingRates.Data)
                    {
                        yearRateSum += r.FundingRate;
                        if (DateTime.Now.Subtract(r.FundingTime).TotalDays < 31) monthRateSum += r.FundingRate;
                        currDate = r.FundingTime.AddHours(1);
                        // Console.WriteLine($"{r.FundingTime}: {r.FundingRate}");
                    }
                    // Console.WriteLine($"--------");
                }

                profitsFile.AppendLine($"{s};{yearRateSum};{monthRateSum};");
                Console.WriteLine($"{s}: year rate sum: {yearRateSum * 100: 0.###}%; month rate cum: {monthRateSum * 100: 0.###}%");
                // queryCount++;
                // if (queryCount > 2) break;*/
            }

            // Console.WriteLine(symbolsArray);
            File.WriteAllText($"funding-rates-{symbolsArray[0].ToLower()}.csv", profitsFile.ToString());
            Environment.Exit(0);
        }

        public async void ConnectToUserStream(Func<string, Task> onMessageReceived) // async 
        {
            UserDataWebSocket? uws = null;
            if (_userDataKey == string.Empty)
            {
                var answer = _us.CreateSpotListenKey().Result;
                dynamic? streamData = JsonConvert.DeserializeObject(answer) ?? throw new Exception("KlineCandlestickData returned no data");
                _userDataKey = streamData.listenKey;
                uws = new UserDataWebSocket(_userDataKey);
                await uws.ConnectAsync(CancellationToken.None);
            }
            if (uws != null) uws.OnMessageReceived(onMessageReceived, CancellationToken.None);
        }

        public void CheckBalance(bool outLog = true)
        {
            if (IsTest && _autonomic)
            {
                SpotAmount = 1000;
                FuturesAmount = 1000;
                IsDealEntered = false;
                return;
            };

            var logString = string.Empty;
            try
            {
                UpdateSystemTime(null);
                var accInfo = _sa.AccountInformation().Result;
                dynamic? accountData = JsonConvert.DeserializeObject(accInfo.Trim()) ?? throw new Exception("acc info returned no data");

                foreach (var b in accountData.balances)
                {
                    if (b.asset == QuoteCurrencyName)
                    {
                        if (IsTest) SpotAmount = 1000;
                        else SpotAmount = b.free;
                        logString += $"{QuoteCurrencyName.ToLower()} {SpotAmount}   ";
                    }
                    else
                    {
                        string currencyName = b.asset;
                        var coin = GetCurrencyByName(currencyName + QuoteCurrencyName);
                        if (coin == null) continue;
                        if (IsTest) coin.CurrencyAmount = 0;
                        else coin.CurrencyAmount = b.free;
                        logString += $"{currencyName.ToLower()} {coin.CurrencyAmount}   ";
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("-2015")) BnnUtils.Log(BnnUtils.GetPublicIp(), false); // {"code":-2015,"msg":"Invalid API-key, IP, or permissions for action."}	
                ProcessServerError($"CheckBalance: " + ex.Message);
            }

            if (outLog) BnnUtils.Log(logString, false);
        }

        public static decimal GetActualPrice(string symbolName)
        {
            return 0;
        }

        public void Order(BaseStrategy currency, Side side, decimal sum)
        {
            try
            {
                decimal? quote = null;
                decimal? quantity = null;
                if (side == Side.SELL) quantity = sum;
                else quote = sum;

                string answer;
                if (IsTest)
                {
                    BnnUtils.Log("Real order is not accessable due to test mode");
                    BnnUtils.Log($"quantity: {quantity}, quote: {quote}"); // , answer: {answer}, Side.SELL: {side == Side.SELL}
                    answer = _sa.TestNewOrder(currency.SymbolName, side, OrderType.MARKET, null, quantity, quote).Result; // "BTCUSDT"
                    BnnUtils.Log($"answer: {answer}");
                    currency.RealPrice = currency.CurrPrice;
                }
                else
                {
                    answer = _sa.NewOrder(currency.SymbolName, side, OrderType.MARKET, null, quantity, quote).Result;
                    dynamic? orderData = JsonConvert.DeserializeObject(answer.Trim()) ?? throw new Exception("Order returned no data");
                    foreach (var f in orderData.fills)
                    {
                        BnnUtils.Log("real market price: " + f.price);
                        currency.RealPrice = f.price;
                    }
                }

                if (side == Side.BUY) DeactivateAll(currency);
            }
            catch (Exception ex)
            {
                ProcessServerError("Order: " + ex.Message);
                /* BnnUtils.Log(ex.Message);
				Console.Beep();
                Environment.Exit(0);*/
            }
        }

        public long SpotLimitOrder(string symbolName, Side side, decimal sum, decimal price, decimal stopStep = 0) // bool isStopLoss = false
        {
            try
            {
                string answer;
                var orderType = stopStep > 0 ? OrderType.STOP_LOSS_LIMIT : OrderType.LIMIT_MAKER;
                decimal? stopPrice = stopStep > 0 ? (side == Side.BUY ? price + stopStep : price - stopStep) : null;
                if (AccountManager.IsTest)
                {
                    if (_autonomic) return BnnUtils.GetUnixNow();
                    answer = _sa.TestNewOrder(symbolName, side, orderType, stopStep > 0 ? Binance.Spot.Models.TimeInForce.GTC : null, sum, null, price, null, null, null, stopPrice).Result;
                    // answer = _sa.TestNewOrder(symbolName, side, orderType, null, sum, null, price, null, null, null, isStopLoss ? price : null).Result; // isStopLoss ? null : 
                }
                else
                {
                    answer = _sa.NewOrder(symbolName, side, orderType, stopStep > 0 ? Binance.Spot.Models.TimeInForce.GTC : null, sum, null, price, null, null, null, stopPrice, null, null, NewOrderResponseType.RESULT).Result; // LIMIT  - newOrderRespType = "FULL" -- isStopLoss ? null : 
                    // BnnUtils.Log($"answer: {answer.Trim()}");
                    dynamic? orderData = JsonConvert.DeserializeObject(answer.Trim()) ?? throw new Exception("LimitOrder returned no data");
                    BnnUtils.Log($"spot limit order: orderId={orderData.orderId}; side={orderData.side}; status = {orderData.status}; price={orderData.price}; origQty={orderData.origQty}; {orderData.type}");
                    return orderData.orderId;
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains(BnnException.StopPriceWouldTrigger.ToString())) throw new BnnException("Stop price would trigger immediately - seems like price changed a lot", BnnException.StopPriceWouldTrigger); // {"code":-2010,"msg":"Stop price would trigger immediately."}
                else ProcessServerError($"SpotLimitOrder ({symbolName}, side={side}, sum={sum}, price={price}{(stopStep > 0 ? ", sl" : string.Empty)}): " + ex.Message);
            }
            return BnnUtils.GetUnixNow();
        }

        public long FuturesLimitOrder(string symbolName, Side side, decimal sum, decimal price)
        {
            try
            {
                BinanceUsdFuturesOrder answer;
                // var orderType = OrderType.LIMIT_MAKER;
                // decimal? stopPrice = null;
                if (IsTest)
                {
                    if (_autonomic) return BnnUtils.GetUnixNow();
                    // C:\bnn\Binance.Net\Clients\UsdFuturesApi\BinanceRestClientUsdFuturesApiTrading.cs
                    // _binanceSocket.UsdFuturesApi.Trading.
                    // answer = _binanceSocket.UsdFuturesApi.Trading.(symbolName, side == Side.BUY ? OrderSide.Buy : OrderSide.Sell, FuturesOrderType.Limit, sum, price, null, null, null, null, null, null, null, null, null, OrderResponseType.Result).Result.Data.Result;
                    // answer = _sa.TestNewOrder(symbolName, side, orderType, null, sum, null, price, null, null, null, stopPrice).Result;
                }
                else
                {
                    // answer = _sa.NewOrder(symbolName, side, orderType, null, sum, null, price, null, null, null, stopPrice, null, null, NewOrderResponseType.RESULT).Result;
                    // dynamic? orderData = JsonConvert.DeserializeObject(answer.Trim()) ?? throw new Exception("LimitOrder returned no data");
                    answer = _binanceSocket.UsdFuturesApi.Trading.PlaceOrderAsync(symbolName, side == Side.BUY ? OrderSide.Buy : OrderSide.Sell, FuturesOrderType.Limit, sum, price, null, null, null, null, null, null, null, null, null, OrderResponseType.Result).Result.Data.Result;
                    BnnUtils.Log($"futures limit order: orderId={answer.Id}; side={answer.Side}; status = {answer.Status}; price={answer.Price}; origQty={answer.QuantityFilled}; {answer.Type}");
                    return answer.Id;
                }
            }
            catch (Exception ex)
            {
                ProcessServerError($"FuturesLimitOrder ({symbolName}, side={side}, sum={sum}, price={price}): " + ex.Message);
            }
            return BnnUtils.GetUnixNow();
        }

        public long ReplaceOrder(long cancelOrderId, string symbolName, Side side, decimal sum, decimal price, decimal stopStep = 0)
        {
            if (AccountManager.IsTest) return BnnUtils.GetUnixNow();
            try
            {
                var orderType = stopStep > 0 ? OrderType.STOP_LOSS_LIMIT : OrderType.LIMIT_MAKER;
                decimal? stopPrice = stopStep > 0 ? (side == Side.BUY ? price + stopStep : price - stopStep) : null;
                var answer = _sa.CancelAnExistingOrderAndSendANewOrder(symbolName, side, orderType, "STOP_ON_FAILURE", stopStep > 0 ? Binance.Spot.Models.TimeInForce.GTC : null, sum, null, price, null, null, cancelOrderId, null, null, null, stopPrice, null, null, NewOrderResponseType.RESULT).Result; // LIMIT - TimeInForce.GTC
                // BnnUtils.Log($"answer: {answer}");
                dynamic? orderData = JsonConvert.DeserializeObject(answer.Trim()) ?? throw new Exception("NewOrder returned no data");
                BnnUtils.Log($"replace order: orderId={orderData.newOrderResponse.orderId}; side={orderData.newOrderResponse.side}; price={orderData.newOrderResponse.price}; origQty={orderData.newOrderResponse.origQty}; {orderData.newOrderResponse.type}");
                return orderData.newOrderResponse.orderId;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains(BnnException.UnknownOrderSent.ToString())) throw new BnnException("Unknown order sent - possible market price and fee", BnnException.UnknownOrderSent); // "cancelResponse":{"code":-2011,"msg":"Unknown order sent."}
                else if (ex.Message.Contains(BnnException.StopPriceWouldTrigger.ToString())) throw new BnnException("Stop price would trigger immediately - seems like price changed a lot", BnnException.StopPriceWouldTrigger); // {"code":-2010,"msg":"Stop price would trigger immediately."}
                else ProcessServerError($"ReplaceOrder (id={cancelOrderId}, sum={sum}, price={price}): {ex.Message}");
                return -1;
            }
        }

        public string CheckOrder(BaseStrategy currency, long orderId)
        {
            try
            {
                if (AccountManager.IsTest) return OrderStatus.Unknown;
                var answer = _sa.QueryOrder(currency.SymbolName, orderId).Result;
                dynamic? orderData = JsonConvert.DeserializeObject(answer.Trim()) ?? throw new Exception("CheckOrder returned no data");
                if (orderData.status != OrderStatus.New) BnnUtils.Log($"check order: orderId={orderData.orderId}; side={orderData.side}; status={orderData.status}; price={orderData.price}; origQty={orderData.origQty}");
                return orderData.status;
            }
            catch (Exception ex)
            {
                ProcessServerError($"CheckOrder ({orderId}): {ex.Message}");
            }
            return OrderStatus.Unknown;
        }

        private void ProcessServerError(string errorMessage)
        {
            BnnUtils.Log(errorMessage);
            Console.Beep();
            Environment.Exit(0);
        }
        #endregion

        #region BacktestRoutines 

        public void FindBestParams(List<Kline>? klines = null)
        {
            OutLog = false;
            if (_coins.Count > 0) _coins[0].FindBestParams();
            /* foreach (var c in _coins)
            {
                c.FindBestParams();
            }*/
        }

        public void UpdateCandlesticks(int klinesCount = 150)
        {
            Console.WriteLine($"Update klines\n\r");
            foreach (var c in _coins)
            {
                Console.WriteLine($"{c.SymbolName}");
                c.UpdateCandlesticks(klinesCount);
            }
        }
        #endregion

        private void OutTickers(object? state)
        {
            var tickersString = $"{DateTime.Now:dd.MM.yyyy HH:mm:ss} ";
            foreach (var c in _coins)
            {
                // if (c.Active) tickersString += $"{c.SymbolName.ToUpper()} {c.CurrPrice}   ";
                if (c.Active) tickersString += $"{c.GetCurrentInfo()}   ";
            }
            BnnUtils.ClearCurrentConsoleLine();
            Console.Write(tickersString.Trim());
            // Console.WriteLine(tickersString);
            if (_tickersTimer != null) _tickersTimer.Change(_tickersPeriod, Timeout.Infinite);
        }

        public void DeactivateAll(BaseStrategy currency)
        {
            foreach (var c in _coins)
            {
                c.Active = c == currency;
            }
        }

        public void ActivateAll()
        {
            foreach (var c in _coins)
            {
                c.Active = true;
            }
        }

        private readonly System.Threading.Timer? _timer;

        public void UpdateSystemTime(object? state)
        {
            var sc = new SNTPClient("time.windows.com");
            sc.Connect(true);
            if (_userDataKey != string.Empty) _us.PingSpotListenKey(_userDataKey);
        }

        public void Dispose()
        {
            DbConnection.Close();
        }

        public bool IsDealEntered { get; private set; } = false;

        public void BackTest() // List<Kline>? klines = null
        {
            var writeBalanceFile = false;
            if (OutLog) Console.WriteLine($"BT {BnnUtils.FormatUnixTime(StartTime)} - {BnnUtils.FormatUnixTime(EndTime)}");
            var operationStartTime = DateTime.Now;
            Statistics.TotalProfit = BaseDealParams.InitialBalance;
            Statistics.DealCount = 0;
            var balanceFile = new StringBuilder();

            foreach (var c in _coins)
            {
                c.PrepareBacktest();
            }
            if (OutLog) Console.WriteLine();


            var firstKline = FindRangeKlinesCount();
            var klinesCount = firstKline + (EndTime - StartTime) / 1000 / 60 / Timeframe;

            for (var i = firstKline + 1; i < klinesCount - 1; i++)
            {
                foreach (var c in _coins)
                {
                    var currInLong = c.IsDealEntered;
                    c.ProcessBacktestKline(i); // calc ema and rsi

                    // deal entered
                    if (c.IsDealEntered) IsDealEntered = true; // !currInLong && 

                    // deal exited
                    if (currInLong && !c.IsDealEntered)
                    {
                        IsDealEntered = false;
                        if (writeBalanceFile) balanceFile.AppendLine($"{StartTime + i * 60 * 1000};{Statistics.TotalProfit};");
                        foreach (var curr in _coins)
                        {
                            if (curr != c) curr.InitBacktestLong(i);
                        }
                        break;
                    }
                }
            }

            if (OutLog)
            {
                Console.WriteLine($"\n\r{Statistics}   [{DateTime.Now.Subtract(operationStartTime)}]");
                if (writeBalanceFile)
                {
                    var csvFileName = $"{BnnUtils.FormatUnixTime(StartTime, false).Replace('.', '-')} - {BnnUtils.FormatUnixTime(EndTime, false).Replace('.', '-')}.csv";
                    File.WriteAllText(csvFileName, balanceFile.ToString());
                }
            }
        }

        public void Start()
        {
            // if (Fee == 0) BnnUtils.Log($"FEE = 0!!!", false); // {DateTime.Now:dd.MM.yyyy HH:mm:ss} 
            if (IsTest) BnnUtils.Log("PROGRAMM RUNNED IN TEST MODE!!!\n\r", false);
            // UpdateSystemTime(null);
            LogLevel = 0; // silent update klines
            CheckBalance();

            foreach (var c in _coins)
            {
                c.Prepare();
            }
            BnnUtils.Log("---------------------------------------------------------------------------", false);

            Console.WriteLine(string.Empty);
            Console.WriteLine("All right? ('y' to continue):", false);
            var confirmMessage = Console.ReadLine() ?? "N";
            if (confirmMessage.ToLower()[0] != 'y') return;
            BnnUtils.Log(string.Empty, false);

            _tickersTimer = new(OutTickers, null, _tickersPeriod, Timeout.Infinite);
            foreach (var c in _coins)
            {
                c.Start();
            }
        }
    }
}