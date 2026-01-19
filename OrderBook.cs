// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Threading.Tasks;
using Binance.Common;
using Binance.Spot;
using Binance.Spot.Models;
using Newtonsoft.Json;

namespace Bnncmd
{
    internal class OrderBook
    {
        private static readonly Market s_market = new();
        private static readonly System.Threading.Timer s_timer = new(ReadDepthApi, null, s_checkPeriod, Timeout.Infinite);
        private static readonly int s_checkPeriod = 500;
        // private static int _recordCount = 25;

        public static int Depth { get; set; }

        private static void ReadDepthApi(Object? state)
        {
            try
            {
                // Console.WriteLine($"{DateTime.Now:hh-hh-ss}");
                var data = s_market.OrderBook("BTCUSDT", Depth).Result; // 100 - 1000
                OutOrders(data); // , false
                s_timer.Change(s_checkPeriod, Timeout.Infinite);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }


        public static void Monitor() // Api
        {
            Console.WriteLine("Order book: Api");
            // var timer = new Timer(ReadDepthApi, null, 1000, 1 * 1000); // 0 - 10s
            // _timer = new Timer(ReadDepthApi, null, _checkPeriod, Timeout.Infinite); // 0 - 10s - 1000
            // ReadDepthApi(null);
        }


        private static (double, double) OutOrders(string orders, bool showOrderBook = true)
        {
            // if (showOrderBook) 
            //{
            Console.Clear();
            Console.WriteLine(); // $"{DateTime.Now:HH-mm-ss}"
                                 //}

            double askSum = 0;
            double bidSum = 0;
            var outRecordCount = Depth <= 20 ? Depth : 10;

            dynamic ordersData = JsonConvert.DeserializeObject(orders.Trim()) ?? throw new Exception("depth returned no data");
            if (showOrderBook && (outRecordCount < Depth)) Console.WriteLine($"{Depth - outRecordCount} rows more...");
            for (int i = ordersData.asks.Count - 1; i >= 0; i--) // ordersData.asks.Count
            {
                askSum += (double)ordersData.asks[i][1];
                if (showOrderBook && (i < outRecordCount)) Console.WriteLine($"{ordersData.asks[i][0]}: {ordersData.asks[i][1]}"); // ordersData.asks.Count - i <= outRecordCount
            }

            if (showOrderBook) Console.WriteLine("--------------------------");

            // foreach(var b in ordersData.bids)
            for (var i = 0; i < ordersData.bids.Count; i++) // ordersData.asks.Count
            {
                bidSum += (double)ordersData.bids[i][1];
                if (showOrderBook && (i < outRecordCount)) Console.WriteLine($"{ordersData.bids[i][0]}: {ordersData.bids[i][1]}");
            }

            if (showOrderBook)
            {
                if (outRecordCount < Depth) Console.WriteLine($"{Depth - outRecordCount} rows more...");
                Console.WriteLine("\r\n"); // \r\n
            }

            double currPrice = ordersData.bids[0][0];

            // check previous prediction
            if (s_previousPrice > 0)
            {
                if (currPrice - s_previousPrice > 0.01)
                {
                    if (s_isGrows) s_successCount++;
                    else s_failCount++;
                }

                if (s_previousPrice - currPrice > 0.01)
                {
                    if (s_isGrows) s_failCount++;
                    else s_successCount++;
                }

                if (s_successCount + s_failCount > 0) Console.WriteLine($"{s_successCount}:{s_failCount} ( {s_successCount * 100 / (s_successCount + s_failCount)}% )"); // .0 
            }

            // next prediction
            s_isGrows = askSum < bidSum;
            Console.WriteLine($"{currPrice}: {askSum} / {bidSum} => {(s_isGrows ? "raise" : "fall")}");

            s_previousPrice = currPrice;

            return (askSum, bidSum);
        }


        private static bool s_isGrows = true;
        private static double s_previousPrice = -1;
        private static int s_successCount = 0;
        private static int s_failCount = 0;

        public async void MonitorStream() // 
        {
            Console.WriteLine("Order book: Stream");
            // var ws = new WebSocketApi(); // $"btcusdt@depth10"
            var ws = new MarketDataWebSocket($"btcusdt@depth20"); // 20 --- @100ms
            ws.OnMessageReceived(data =>
            {
                dynamic? ordersData = JsonConvert.DeserializeObject(data.Trim()) ?? throw new Exception("depth returned no data");

                // predict
                try
                {
                    OutOrders(ordersData); // 
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    Environment.Exit(0);
                }
                return Task.CompletedTask;
            }, CancellationToken.None);

            await ws.ConnectAsync(CancellationToken.None);
            // await ws.Market.OrderBookAsync(symbol: "BTCUSDT", limit: 10, cancellationToken: CancellationToken.None);
        }
    }
}