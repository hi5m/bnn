// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Binance.Spot;
using Bnncmd;
using DbSpace;
using Mexc.Net.Clients;
using Newtonsoft.Json;

namespace Bnncmd.Strategy
{
    internal class SpotArbitration : BaseStrategy
    {
        public SpotArbitration(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            BnnUtils.Log($"{GetName()}");
            _dealParams = new DummyParams();
        }

        private readonly List<SfSpread> _spreads = [];

        private SpreadState _state = SpreadState.Updating;

        private static readonly string s_binanceShortName = "bn";

        // private static readonly string BybitShortName = "bb";

        private static readonly string s_mexcShortName = "mx";

        public override void Start()
        {
            _state = SpreadState.CollectInformation;
            // _collectStart = DateTime.Now;
            //_state = SpreadState.WaitingForNarrowSpread;
            // _state = SpreadState.Updating;
        }

        public override void Prepare()
        {
            // exchanges stablecoins
            var binanceSpotCoin = "FDUSD";
            var binanceFuturesCoin = "USDC";
            var mexcSpotCoin = "USDT";

            // load trade rules from db
            var spreadScript = $"select SpotCoins.BaseAsset, SpotQuoteAsset, SpotPriceStep, FuturesQuoteAsset, FuturesPriceStep from " +
                "\t(select substr(Name, 0, length(Name) - 4) BaseAsset, '" + binanceSpotCoin + "' SpotQuoteAsset, PriceStep SpotPriceStep " +
                "\tfrom symbol where name like '%" + binanceSpotCoin + "') SpotCoins " +
                "inner join" +
                "\t(select substr(Name, 0, length(Name) - 3) BaseAsset, '" + binanceFuturesCoin + "' FuturesQuoteAsset, PriceStep FuturesPriceStep " +
                "\tfrom symbol where name like '%" + binanceFuturesCoin + "') FuturesCoins " +
                "on SpotCoins.BaseAsset = FuturesCoins.BaseAsset";

            DB.OpenQuery(_manager.DbConnection, spreadScript, null, dr =>
            {
                // if (_spreads.Count == 9) return;
                // if (((string)dr["BaseAsset"] != "HBAR") && ((string)dr["BaseAsset"] != "BOME") && ((string)dr["BaseAsset"] != "PNUT") && ((string)dr["BaseAsset"] != "WLD") && ((string)dr["BaseAsset"] != "ORDI")) return;
                if (((string)dr["BaseAsset"] != "BTC") && ((string)dr["BaseAsset"] != "SOL") && ((string)dr["BaseAsset"] != "ETH") && ((string)dr["BaseAsset"] != "SUI")
                    && ((string)dr["BaseAsset"] != "WIF") && ((string)dr["BaseAsset"] != "BOME") && ((string)dr["BaseAsset"] != "HBAR")) return;

                // add binance spot-mexc spot futures
                var bnnSpread = new SfSpread();
                bnnSpread.BaseAsset = (string)dr["BaseAsset"];
                bnnSpread.Spot.ExchangeName = s_binanceShortName;
                bnnSpread.Spot.QuoteAsset = binanceSpotCoin;
                bnnSpread.Spot.PriceStep = (decimal)dr["SpotPriceStep"];
                bnnSpread.Futures.ExchangeName = s_mexcShortName;
                bnnSpread.Futures.QuoteAsset = binanceFuturesCoin;
                bnnSpread.Futures.PriceStep = (decimal)dr["FuturesPriceStep"];
                _spreads.Add(bnnSpread);

                // binance spot handler
                var bnnMarketDataSocket = new MarketDataWebSocket($"{bnnSpread.BaseAsset.ToLower()}{binanceSpotCoin.ToLower()}@depth5@100ms"); // 20
                bnnMarketDataSocket.OnMessageReceived(data =>
                {
                    dynamic? orderBookData = JsonConvert.DeserializeObject(data.Trim()) ?? throw new Exception("aggTrade returned no data");
                    decimal bestAsk = orderBookData.asks[0][0];
                    decimal bestBid = orderBookData.bids[0][0];
                    bnnSpread.SpotMessageReceived(bestAsk, bestBid);
                    return Task.CompletedTask;
                }, CancellationToken.None);
                bnnMarketDataSocket.ConnectAsync(CancellationToken.None);

                // mexc spot handler
                var socketClient = new MexcSocketClient();
                socketClient.SpotApi.SubscribeToBookTickerUpdatesAsync($"{bnnSpread.BaseAsset.ToUpper()}{mexcSpotCoin.ToUpper()}", update =>
                {
                    bnnSpread.FuturesMessageReceived(update.Data.BestAskPrice, update.Data.BestBidPrice);
                });
            });

            var funds = _spreads.OrderByDescending(r => r.FundingRate).ToList();
            foreach (var f in funds)
            {
                BnnUtils.Log($"{f.BaseAsset}: rate {f.FundingRate * 100:0.####}%, spot {f.Spot.ExchangeName} tick {f.Spot.PriceStep:0.#######}, futures {f.Futures.ExchangeName} tick {f.Futures.PriceStep:0.#######}", false);
            }
        }

        public override string GetCurrentInfo()
        {
            // if ((_state == SpreadState.CollectInformation) && (DateTime.Now.Subtract(_collectStart).TotalSeconds > CollectSeconds)) _state = SpreadState.WaitingForNarrowSpread;
            if (_state == SpreadState.Updating) return "updating...";
            var spreadsInfo = string.Empty;
            // var activeSpreadStates = new[] { SpreadState.SpotEnterOrderCreated, SpreadState.FuturesEnterOrderCreated, SpreadState.WaitingForWideSpread, SpreadState.SpotExitOrderCreated, SpreadState.FuturesExitOrderCreated, SpreadState.Updating };

            // var spreads = _spreads.OrderByDescending(r => r.MinSpread == decimal.MaxValue ? r.MaxSpread : r.MaxSpread - r.MinSpread).ToList();
            var spreads = _spreads.OrderBy(r => r.CurrSpread).ToList();
            foreach (var spread in spreads)
            {
                // if (activeSpreadStates.Contains(spread.State)) spreadsInfo = string.Empty;
                spreadsInfo += $"{spread}   "; // spreads[i]
                // if (spreadsInfo.Length > 151) break; // for notebook
                if (spreadsInfo.Length > 231) break; // for desktop
            }
            // spreadsInfo += $" {_state}...";
            // if (_state == SpreadState.CollectInformation) spreadsInfo += $" {CollectSeconds - DateTime.Now.Subtract(_collectStart).TotalSeconds:0.##}s";
            return spreadsInfo;
        }

        #region Parent Methods
        protected override double GetShortValue(decimal longPrice, out decimal stopLossValue)
        {
            stopLossValue = 0;
            return double.MaxValue;
        }

        public override string GetName() { return "Spot Arbitration"; }

        protected override decimal GetLongValue(List<Bnncmd.Kline> klines, decimal previousValue = -1)
        {
            return 0;
        }

        protected override double GetShortValue(List<Bnncmd.Kline> klines, double previousValue = -1)
        {
            return double.MaxValue;
        }

        protected override void SaveStatistics(List<BaseDealParams> tradeResults)
        {
            // override abstract method
        }
        #endregion
    }
}
