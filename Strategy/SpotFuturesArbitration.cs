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
using CryptoExchange.Net.Objects.Sockets;
using DbSpace;
using Binance.Net.Clients;
using Newtonsoft.Json;
using Bnncmd.Strategy;
using Microsoft.Extensions.Logging;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Mexc.Net.Clients;

namespace Bnncmd.Strategy
{
    internal class SfSpread
    {
        public string BaseAsset { get; set; } = string.Empty;
        public SymbolInfo Spot { get; set; } = new SymbolInfo();
        public SymbolInfo Futures { get; set; } = new SymbolInfo();
        public SpreadState State { get; set; } = SpreadState.WaitingForNarrowSpread;
        public decimal MaxSpread { get; set; } = decimal.MinValue;
        public decimal MinSpread { get; set; } = decimal.MaxValue;
        public decimal CurrSpread { get; set; } = 0;
        public decimal FundingRate { get; set; } = 0;
        // public decimal SpotAmount { get; set; } = 0;

        public static readonly decimal SpreadToEnter = 0.05M; // 0.1M;

        public static readonly decimal DiffToExit = 0.07M;

        private readonly StringBuilder _balanceFile = new();

        private DateTime _lastLogUpdate = DateTime.Now;

        // private decimal _spreadToEnter = 0;

        // private decimal _spreadToExit = 0;

        public decimal EnterSpread { get; set; } = 0;

        // private readonly decimal _spreadDelta = 0.03M;

        /*private void RecalcSpread()
        {
            if ((Spot.BestBid == 0) || (Futures.BestAsk == 0)) return;
            CurrSpread = (Spot.BestBid - Futures.BestAsk) / Spot.BestBid * 100;
            if (CurrSpread > MaxSpread) MaxSpread = CurrSpread;
            if (CurrSpread < MinSpread) MinSpread = CurrSpread;
            if ((MaxSpread == CurrSpread) || (MinSpread == CurrSpread))
            {
                var avgSpread = (MaxSpread + MinSpread) / 2;
                _spreadToEnter = avgSpread - SpreadToEnter;
            }

             switch (State)
            {
                case SpreadState.WaitingForNarrowSpread:
                    if (CurrSpread < _spreadToEnter - _spreadDelta) MinSpreadReached?.Invoke(this);
                    break;

                case SpreadState.WaitingForWideSpread:
                    // EnterSpread = (Spot.EnterPrice - Futures.EnterPrice) / Futures.EnterPrice * 100;
                    _spreadToExit = EnterSpread + DiffToExit + _spreadDelta;
                    if (_spreadToExit > MaxSpread - _spreadDelta) _spreadToExit = MaxSpread - _spreadDelta;
                    if (CurrSpread > _spreadToExit) MaxSpreadReached?.Invoke(this);
                    break;
            }
        }*/

        /*public SfSpread(string baseAsset, string quoteSpotAsset, string quoteFuturesAsset, decimal fee)
        {
            BaseAsset = baseAsset;
            Spot.QuoteAsset = quoteSpotAsset;
            Futures.QuoteAsset = quoteFuturesAsset;
            Spot.Fee = fee;

            /*MarketDataSocket = new MarketDataWebSocket($"{baseAsset.ToLower()}{quoteSpotAsset.ToLower()}@depth20@100ms");
            MarketDataSocket.OnMessageReceived(SpotMessageReceived, CancellationToken.None);
            MarketDataSocket.ConnectAsync(CancellationToken.None);

            FuturesDataSocket = new BinanceSocketClient();
            FuturesDataSocket.UsdFuturesApi.ExchangeData.SubscribeToBookTickerUpdatesAsync($"{baseAsset.ToUpper()}{quoteFuturesAsset.ToUpper()}", FuturesMessageReceived);
        }*/

        private void RecalcSpread()
        {
            if ((Spot.BestBid == 0) || (Futures.BestAsk == 0)) return;
            CurrSpread = (Spot.BestBid - Futures.BestAsk) / Spot.BestBid * 100;

            // log
            _balanceFile.AppendLine($"{DateTime.Now};{Spot.BestBid};{Futures.BestAsk};{CurrSpread};");
            if ((DateTime.Now.Minute != _lastLogUpdate.Minute) && (DateTime.Now.Minute % 10 == 0))
            {
                var csvFileName = $"{BaseAsset}-{DateTime.Now.ToString().Replace('.', '-').Replace('/', '-').Replace(':', '-').Replace(' ', '-')}.csv";
                File.WriteAllText(csvFileName, _balanceFile.ToString());
                _balanceFile.Clear();
            };
            _lastLogUpdate = DateTime.Now;

            if (CurrSpread > MaxSpread) MaxSpread = CurrSpread;
            if (CurrSpread < MinSpread) MinSpread = CurrSpread;
            /*if ((MaxSpread == CurrSpread) || (MinSpread == CurrSpread))
            {
                var avgSpread = (MaxSpread + MinSpread) / 2;
                _spreadToEnter = avgSpread - SpreadToEnter;
            }

            switch (State)
            {
                case SpreadState.WaitingForNarrowSpread:
                    if (CurrSpread < _spreadToEnter - _spreadDelta) MinSpreadReached?.Invoke(this);
                    break;

                case SpreadState.WaitingForWideSpread:
                    // EnterSpread = (Spot.EnterPrice - Futures.EnterPrice) / Futures.EnterPrice * 100;
                    _spreadToExit = EnterSpread + DiffToExit + _spreadDelta;
                    if (_spreadToExit > MaxSpread - _spreadDelta) _spreadToExit = MaxSpread - _spreadDelta;
                    if (CurrSpread > _spreadToExit) MaxSpreadReached?.Invoke(this);
                    break;
            }*/
        }

        public void SpotMessageReceived(decimal bestAsk, decimal bestBid)
        {
            // if (Spot.QuoteAsset.ToLower() != coin) return;
            Spot.BestAsk = bestAsk;
            Spot.BestBid = bestBid;
            RecalcSpread();
        }

        public void FuturesMessageReceived(decimal bestAsk, decimal bestBid)
        {
            Futures.BestAsk = bestAsk;
            Futures.BestBid = bestBid;
            RecalcSpread();

            /*if (State == SpreadState.FuturesEnterOrderCreated)
            {
                if (Futures.BestAsk > Futures.EnterPrice) EnterOrderExecuted?.Invoke(this);
                if (Futures.BestAsk < Futures.EnterPrice) MinSpreadChanged?.Invoke(this);
            }

            if (State == SpreadState.FuturesExitOrderCreated)
            {
                if (Futures.BestBid < Futures.ExitPrice) ExitOrderExecuted?.Invoke(this);

                if (CurrSpread < _spreadToExit - _spreadDelta) MaxSpreadLost?.Invoke(this);
                else
                {
                    if (Futures.BestBid > Futures.ExitPrice) MaxSpreadChanged?.Invoke(this);
                }
            }*/
        }

        public override string ToString()
        {
            /*string? addInfo;
            switch (State)
            {
                /*case SpreadState.Updating:
                    addInfo = string.Empty;
                    break;
                case SpreadState.WaitingForNarrowSpread:
                    addInfo = $"=>{_spreadToEnter - _spreadDelta:0.##}";
                    break;
                case SpreadState.WaitingForWideSpread:
                    addInfo = $"=>{_spreadToExit:0.##}";
                    break;
                default:
                    addInfo = $"/{State}";
                    break;
            }*/

            var exitStatuses = new[] { SpreadState.WaitingForWideSpread, SpreadState.FuturesExitOrderCreated, SpreadState.SpotExitOrderCreated };
            var bestBidAsk = exitStatuses.Contains(State) ? $"{Spot.BestAsk:0.#######}/{Futures.BestBid:0.#######}" : $"{Spot.BestBid:0.#######}/{Futures.BestAsk:0.#######}";
            return $"{Spot.ExchangeName.ToUpper()}-{Futures.ExchangeName.ToUpper()} {BaseAsset} {bestBidAsk}|{MinSpread:0.##}<{CurrSpread:0.##}<{MaxSpread:0.##}"; // {addInfo}
        }
    }


    internal class SpotFuturesArbitration : BaseStrategy
    {
        public SpotFuturesArbitration(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            BnnUtils.Log($"{GetName()}");
            _dealParams = new DummyParams();
        }

        private readonly List<SfSpread> _spreads = new();

        private SpreadState _state = SpreadState.Updating;

        private static readonly string s_binanceShortName = "bn";

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

            var dbSymbols = new List<TempDbInfo>();
            DB.OpenQuery(_manager.DbConnection, spreadScript, null, dr =>
            {
                if (((string)dr["BaseAsset"] != "BTC") && ((string)dr["BaseAsset"] != "SOL") && ((string)dr["BaseAsset"] != "ETH") && ((string)dr["BaseAsset"] != "SUI")
                    && ((string)dr["BaseAsset"] != "WIF") && ((string)dr["BaseAsset"] != "BOME") && ((string)dr["BaseAsset"] != "HBAR")) return;

                // if (((string)dr["BaseAsset"] != "HBAR") && ((string)dr["BaseAsset"] != "BOME") && ((string)dr["BaseAsset"] != "PNUT") && ((string)dr["BaseAsset"] != "WLD") && ((string)dr["BaseAsset"] != "ORDI")) return;
                dbSymbols.Add(new TempDbInfo()
                {
                    BaseAsset = (string)dr["BaseAsset"],
                    SpotPriceStep = (decimal)dr["SpotPriceStep"],
                    FuturesPriceStep = (decimal)dr["FuturesPriceStep"]
                });
            });

            // load funding rate from binance
            var futuresClient = new BinanceRestClient();
            var bncFundingInfo = futuresClient.UsdFuturesApi.ExchangeData.GetMarkPricesAsync().Result;
            foreach (var s in bncFundingInfo.Data)
            {
                var baseAsset = s.Symbol[..^4];
                var dbSymbol = dbSymbols.Find(sp => sp.BaseAsset == baseAsset);
                if ((dbSymbol == null) || !s.Symbol.Contains(binanceFuturesCoin) || (s.FundingRate <= 0)) continue;

                // add binance spot-binance futures
                var bnnSpread = new SfSpread();
                bnnSpread.BaseAsset = dbSymbol.BaseAsset;
                bnnSpread.Spot.ExchangeName = s_binanceShortName;
                bnnSpread.Spot.QuoteAsset = binanceSpotCoin;
                bnnSpread.Spot.PriceStep = dbSymbol.SpotPriceStep;
                bnnSpread.Futures.ExchangeName = s_binanceShortName;
                bnnSpread.Futures.QuoteAsset = binanceFuturesCoin;
                bnnSpread.Futures.PriceStep = dbSymbol.FuturesPriceStep;
                bnnSpread.FundingRate = s.FundingRate ?? 0;
                _spreads.Add(bnnSpread);

                // add mexc spot-binance futures
                var mexcSpread = new SfSpread();
                mexcSpread.BaseAsset = dbSymbol.BaseAsset;
                mexcSpread.Spot.ExchangeName = s_mexcShortName;
                mexcSpread.Spot.QuoteAsset = mexcSpotCoin;
                mexcSpread.Spot.PriceStep = 1; // dbSymbol.SpotPriceStep;
                mexcSpread.Futures.ExchangeName = s_binanceShortName;
                mexcSpread.Futures.QuoteAsset = binanceFuturesCoin;
                mexcSpread.Futures.PriceStep = dbSymbol.FuturesPriceStep;
                mexcSpread.FundingRate = s.FundingRate ?? 0;
                _spreads.Add(mexcSpread);

                // binance spot handler
                var bnnMarketDataSocket = new MarketDataWebSocket($"{baseAsset.ToLower()}{binanceSpotCoin.ToLower()}@depth5@100ms"); // 20
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
                socketClient.SpotApi.SubscribeToBookTickerUpdatesAsync($"{baseAsset.ToUpper()}{mexcSpotCoin.ToUpper()}", update =>
                {
                    mexcSpread.SpotMessageReceived(update.Data.BestAskPrice, update.Data.BestBidPrice);
                });

                // binance futures handler
                var bnnFuturesDataSocket = new BinanceSocketClient();
                bnnFuturesDataSocket.UsdFuturesApi.ExchangeData.SubscribeToBookTickerUpdatesAsync($"{baseAsset.ToUpper()}{binanceFuturesCoin.ToUpper()}", evnt =>
                {
                    bnnSpread.FuturesMessageReceived(evnt.Data.BestAskPrice, evnt.Data.BestAskPrice);
                    mexcSpread.FuturesMessageReceived(evnt.Data.BestAskPrice, evnt.Data.BestAskPrice);
                });

                // if (_spreads.Count > 0) break;
            }

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

        public override string GetName() { return "Spot-Futures Arbitration"; }

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