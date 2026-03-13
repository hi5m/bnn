// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Binance.Common;
using Binance.Spot;
using Newtonsoft.Json;
using System.Diagnostics.Metrics;
using System.Data.Common;
using System.Data;
using Bnncmd;
using System.Globalization;
using Bnncmd.Strategy;
using Microsoft.Extensions.Options;
using System;
using bnncmd.Strategy;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Web;


using Binance.Net.Clients;
using CryptoExchange.Net.Objects.Sockets;
using Binance.Net.Objects.Models.Futures.Socket;
using Mexc.Net.Clients;

internal class Program
{
    private static bool s_isTerminate = false;

    private static readonly AccountManager s_am = new();

    static void Help()
    {
        Console.WriteLine("h                       - help");
        Console.WriteLine("g                       - ping the server");
        Console.WriteLine("s                       - synchromize time");
        Console.WriteLine("t                       - test");
        Console.WriteLine();

        Console.WriteLine("p                       - find the best params for a period");
        Console.WriteLine("b                       - backtest");

        Console.WriteLine("a                       - account information");
        Console.WriteLine("c                       - candlesticks");
        Console.WriteLine("e                       - earn: ");
        Console.WriteLine("                                b - buy pair (b xo spotExch futExch 10000 fdusd usdc)");
        Console.WriteLine("                                s - sell pair (s xo spotExch futExch)");
        Console.WriteLine("                                f - find best offers");
        Console.WriteLine("                                r - get funding rate statisctics (r symbol1,symbol2,symbol3 [daysCount])");
        Console.WriteLine("                                m - monitor");
        Console.WriteLine();

        Console.WriteLine("q                       - quit");
        Environment.Exit(0);
    }

    static async void Ping()
    {
        var httpClient = BnnUtils.BuildLoggingClient();
        var market = new Market(httpClient);
        var result = await market.TestConnectivity();
        Console.WriteLine($"TestConnectivity: {result}");
        if (s_isTerminate) Environment.Exit(0);
    }

    /*static void Monitor()
    {
        s_am.Start();
    }*/

    static void Backtest()
    {
        s_am.BackTest();
        Environment.Exit(0);
    }

    public static void ClearCurrentConsoleLine()
    {
        var currentLineCursor = Console.CursorTop;
        Console.SetCursorPosition(0, Console.CursorTop);
        Console.Write(new string(' ', Console.WindowWidth));
        Console.SetCursorPosition(0, currentLineCursor);
    }

    private static void Encrypt()
    {
        /* Console.WriteLine("Please enter a string:");
        var plaintext = Console.ReadLine();
        Console.WriteLine("");*/
        var plaintext = "...=";

        Console.WriteLine("Result:");
        var encryptedstring = Crypto.DecryptStringAES(plaintext, "1" + "11111");
        // string encryptedstring = StringCipher.Encrypt(plaintext, password);
        Console.WriteLine(encryptedstring);
    }

    private static Task ProcessUserDataMessage(string message)
    {
        dynamic? userData = JsonConvert.DeserializeObject(message.Trim()) ?? throw new Exception("user data message returned no data");
        if ((userData.e != "executionReport") || (userData.X != "FILLED")) return Task.CompletedTask;
        Console.WriteLine($"order {userData.i} was {userData.X} ({userData.s}, {userData.S}, {userData.q} x {userData.p})");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Test Unsubscribe Stream
    /// </summary>
    /*private static readonly BinanceSocketClient _binanceDataSocket = new();
    private static int counter = 0;
    private static bool s_isSubscribedTrades = false;
    private static UpdateSubscription? s_tradesSubscription;
    private static UpdateSubscription? s_priceSubscription;

    private void TestMessageReceived(DataEvent<Binance.Net.Objects.Models.Spot.Socket.BinanceStreamTrade> update)
    {
        if (update.Data.BuyerIsMaker)
        {
            Console.WriteLine($"{DateTime.Now}: quantity: {update.Data.Quantity}; price: {update.Data.Price}; counter: {counter}");
            counter++;
            if (counter == 4) _binanceDataSocket.UnsubscribeAsync(s_tradesSubscription);
        }
    }*/

    /*private static int _dummyInt = 0;

    private static readonly object s_locker = new();  // объект-заглушка

    private static void IncInt(object? obj)
    {
        lock (s_locker)
        {
            Console.WriteLine($"{obj}: dummy int: {_dummyInt}");
        }
        _dummyInt++;
        // Thread.Sleep(30);
    }*/

    private static void SimpleEarn()
    {
        var operation = s_secondParam[0];
        var args = Environment.GetCommandLineArgs();
        var spotExchange = args.Length > 4 ? Exchange.GetExchangeByName(args[4]) : null;
        var futuresExchange = args.Length > 5 ? Exchange.GetExchangeByName(args[5]) : null;
        var earn = new Earn(string.Empty, s_am);
        Console.Clear();

        switch (operation)
        {
            case 'f':
                Earn.FindBestProduct();
                Environment.Exit(0);
                break;
            case 'b':
                if (args.Length < 7) throw new Exception("wrong params number (example: bnncmd e b ZAMA Mexc Binance 1000)");
                if (spotExchange == null) throw new Exception($"spot exchange not found {args[4]}");
                if (futuresExchange == null) throw new Exception($"futures exchange not found {args[5]}");
                if (!decimal.TryParse(args[6], out var quantity)) throw new Exception($"amount format is wrong: {args[6]}");

                var spotStablecoin = ((args.Length < 8) || args[7] == '-'.ToString()) ? string.Empty : args[7];
                var futuresStablecoin = ((args.Length < 9) || args[8] == '-'.ToString()) ? string.Empty : args[8];
                // Console.WriteLine($"coin: {args[3].ToUpper()}, spot exch: {spotExchange.Name}, futures exch: {futuresExchange.Name}, amount: {quantity}, spot stable: {spotStablecoin}, futures stable: {futuresStablecoin}");
                try
                {
                    earn.BuyPair(args[3].ToUpper(), spotExchange, futuresExchange, quantity, spotStablecoin, futuresStablecoin);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    Environment.Exit(0);
                }
                break;
            case 's':
                if (args.Length < 6) throw new Exception("wrong params number (example: bnncmd e s ZAMA Mexc Binance)");
                if (spotExchange == null) throw new Exception($"spot exchange not found {args[4]}");
                if (futuresExchange == null) throw new Exception($"futures exchange not found {args[5]}");
                try
                {
                    earn.SellPair(args[3].ToUpper(), spotExchange, futuresExchange);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    Environment.Exit(0);
                }
                break;
            case 'r':
                var symbols = args[3].Split(',');
                var dayCount = args.Length > 4 ? int.Parse(args[4]) : 99;
                foreach (var s in symbols)
                {
                    Exchange.Binance.GetFundingRateStat(s, dayCount);
                    Console.WriteLine();
                }
                Environment.Exit(0);
                break;
            case 'm':
                Earn.Monitor();
                break;
            default: throw new Exception($"Unknown operation: {operation}");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;

    private static readonly BinanceSocketClient _socketClient = new();
    private static bool _isOutSpan = false;
    private static int _outCounter = 1;
    private static decimal _bestAskPricePerf = 0M;
    private static decimal _updateIdPerf = 0M;
    // private static UpdateSubscription? futuresOrderBookSubscription = null;

    private static void ProcessTick(DataEvent<BinanceFuturesStreamBookPrice> data)
    {
        // BnnUtils.ClearCurrentConsoleLine();
        // Console.Write($"{data.Data.BestAskPrice} | {data.Data.BestBidPrice} [ Thread: {Environment.CurrentManagedThreadId}, DataTime: {data.DataTime:HH:mm:ss.fff} / ReceiveTime: {data.ReceiveTime:HH:mm:ss.fff} / Now: {DateTime.Now:HH:mm:ss.fff} / DataAge: {(data.DataAge == null ? string.Empty : data.DataAge.Value.TotalMicroseconds)} ms / Event Time: {data.Data.EventTime:HH:mm:ss.fff} / TransactionTime: {data.Data.TransactionTime:HH:mm:ss.fff} ]");
        // Console.WriteLine($"{data.Data.BestAskPrice} | {data.Data.BestBidPrice} [ Thread: {Environment.CurrentManagedThreadId}, DataTime: {data.DataTime:HH:mm:ss.fff} / ReceiveTime: {data.ReceiveTime:HH:mm:ss.fff} / Now: {DateTime.Now:HH:mm:ss.fff} / DataAge: {(data.DataAge == null ? string.Empty : data.DataAge.Value.TotalMicroseconds)} ms / Event Time: {data.Data.EventTime:HH:mm:ss.fff} / TransactionTime: {data.Data.TransactionTime:HH:mm:ss.fff} ]");

        // Console.WriteLine($"{data.BestAskPrice} | {data.BestBidPrice} [ Thread: {Environment.CurrentManagedThreadId} / data: {data} / Now: {DateTime.Now:HH:mm:ss.fff} ]");

        var tempIsOut = (DateTime.Now.AddHours(-3) - data.Data.EventTime).TotalMilliseconds > 190;
        if (tempIsOut != _isOutSpan)
        {
            Console.WriteLine($"{_outCounter}. {data.Data.BestAskPrice} | {data.Data.BestBidPrice} / {_bestAskPricePerf} | {_updateIdPerf} [ Thread: {Environment.CurrentManagedThreadId}, UpdateId: {data.Data.UpdateId}, DataTime: {data.DataTime:HH:mm:ss.fff} / ReceiveTime: {data.ReceiveTime:HH:mm:ss.fff} / Event Time: {data.Data.EventTime:HH:mm:ss.fff} / Now: {DateTime.Now:HH:mm:ss.fff} / TransactionTime: {data.Data.TransactionTime:HH:mm:ss.fff} / Diff: {(DateTime.Now.AddHours(-3) - data.Data.EventTime).TotalMilliseconds:0.#}ms ]");
            if (!tempIsOut)
            {
                Console.WriteLine();
                _outCounter++;
            }
        }
        _isOutSpan = tempIsOut;
    }

    private static async void Test() // async 
    {
        Console.WriteLine($"Start... [{DateTime.Now}]");

        // _socketClient.UsdFuturesApi.ExchangeData.SubscribeToBookTickerUpdatesPerfAsync(["ETHUSDC"], data =>
        /* await _socketClient.UsdFuturesApi.ExchangeData.SubscribeToBookTickerUpdatesAsync("ETHUSDC", data =>
        {
            Task.Run(() => ProcessTick(data)); // to prevent "Recursive write lock acquisitions not allowed in this mode"
        }); // 

        await _socketClient.UsdFuturesApi.ExchangeData.SubscribeToBookTickerUpdatesPerfAsync(["ETHUSDC"], data =>
        {
            _bestAskPricePerf = data.BestAskPrice;
            _updateIdPerf = data.UpdateId;
            // Task.Run(() => ProcessTick(data)); // to prevent "Recursive write lock acquisitions not allowed in this mode"
        }, new CancellationToken()); // ,*/

        var mexcSocketClient = new MexcSocketClient();
        await mexcSocketClient.SpotApi.SubscribeToBookTickerUpdatesAsync("ETHUSDT", data =>
        {
            var diffTime = (DateTime.Now.AddHours(-3) - (data.DataTime ?? DateTime.Now)).TotalMilliseconds;
            if (diffTime < 190) return;
            Console.WriteLine($"{_outCounter}. {data.Data.BestAskPrice} | {data.Data.BestBidPrice} / {_bestAskPricePerf} | {_updateIdPerf} [ Thread: {Environment.CurrentManagedThreadId}, DataTime: {data.DataTime:HH:mm:ss.fff} / ReceiveTime: {data.ReceiveTime:HH:mm:ss.fff} / Now: {DateTime.Now:HH:mm:ss.fff} / Diff: {diffTime:0.}ms ]");
        });
    }

    private static void Orders()
    {
        // OrderBook.Depth = s_ops.OrderBookDepth;
        //var ob = new OrderBook();
        // OrderBook.Monitor();
        // Environment.Exit(0);
    }

    private static void FindParamsForPeriod()
    {
        // Console.WriteLine($"Use emulateFlatTunnel (m)");
        // var ft = new FlatTunnel();
        s_am.FindBestParams();
        Environment.Exit(0);
    }

    private static void FindBestBullValue()
    {
        // s_ops.FindBestBullValue();
        Environment.Exit(0);
    }

    private static void Candlesticks()
    {
        var packsCount = int.Parse(s_secondParam);
        if (packsCount == 0) packsCount = 150;
        // Console.WriteLine($"{packsCount}");		
        AccountManager.LogLevel = 10;
        s_am.UpdateCandlesticks(packsCount);
        Environment.Exit(0);
    }

    private static void TestEvaluationIntervals()
    {
        // s_ft.TestIntervals();
        Environment.Exit(0);
    }

    private static void Account()
    {
        s_am.CheckBalance();
        Environment.Exit(0);
    }

    static void ProcessCommand(string command)
    {
        if (command.Length < 1) return;
        var ch = command.ToLower()[0];
        Console.WriteLine();

        try
        {
            switch (ch)
            {
                case 't':
                    Test();
                    break;
                case 'c':
                    Candlesticks();
                    break;
                case 'g':
                    Ping();
                    break;
                case 'h':
                    Help();
                    break;
                case 'b':
                    Backtest();
                    break;
                /*case 'm':
                    Monitor();
                    break;*/
                case 'q':
                    Environment.Exit(0);
                    break;
                case 'i':
                    TestEvaluationIntervals();
                    break;
                case 'p':
                    FindParamsForPeriod();
                    break;
                case 's':
                    Console.WriteLine("Synchromize time...");
                    var sc = new SNTPClient("time.windows.com");
                    sc.Connect(true);
                    Console.WriteLine("Done");
                    Environment.Exit(0);
                    break;
                /* case 'b':
                    FindBestBullValue();
                    break;*/
                case 'a':
                    Account();
                    break;
                case 'f':
                    AccountManager.GetFundingRates();
                    break;
                case 'o':
                    Orders();
                    break;
                case 'e':
                    SimpleEarn();
                    break;
                default:
                    Console.WriteLine("Unknow command");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    private static string s_secondParam = "0";

    private static void Main(string[] args)
    {
        var nfi = new CultureInfo("en-US", false);
        Thread.CurrentThread.CurrentCulture = nfi;

        Console.Clear();
        if (args.Length > 1) s_secondParam = args[1];
        if (args.Length > 0)
        {
            ProcessCommand(args[0]);
            s_isTerminate = true;
        }

        while (true)
        {
            if (!s_isTerminate)
            {
                Console.WriteLine();
                Console.WriteLine("Press key to perform an operation. Use 'h' for the help");
                Console.WriteLine();
            }
            var command = Console.ReadLine();
            ProcessCommand(command ?? "");
        }
    }
}