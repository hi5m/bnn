// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Data;
using Binance.Net.Clients;
using Binance.Net.Objects.Models.Futures.Socket;
using Binance.Spot;
using Bnncmd;
using Bnncmd.Strategy;
using CryptoExchange.Net.Objects.Sockets;
using DbSpace;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Org.BouncyCastle.Asn1.Mozilla;

namespace Bnncmd.Strategy
{
    public enum SpreadState
    {
        Updating,
        CollectInformation,
        WaitingForNarrowSpread,
        SpotEnterOrderCreated,
        FuturesEnterOrderCreated,
        WaitingForWideSpread,
        SpotExitOrderCreated,
        FuturesExitOrderCreated
    }

    public enum ExecutionType
    {
        ByTrades,
        ByPrice
    }

    internal class TempDbInfo
    {
        public string BaseAsset { get; set; } = string.Empty;
        public decimal SpotPriceStep { get; set; }
        public decimal FuturesPriceStep { get; set; }
    }


    internal class SymbolInfo
    {
        public decimal ExchangeID { get; set; } = 1;
        public string ExchangeName { get; set; } = string.Empty;
        public string QuoteAsset { get; set; } = string.Empty;
        public decimal BestBid { get; set; } = 0;
        public decimal BestAsk { get; set; } = 0;
        public decimal PriceStep { get; set; } = 0;
        public decimal EnterPrice { get; set; } = 0;
        public decimal ExitPrice { get; set; } = 0;
        public decimal Fee { get; set; } = 0;
    }


    internal class Spread
    {
        public string BaseAsset { get; set; }
        public SymbolInfo Spot { get; set; } = new SymbolInfo();
        public SymbolInfo Futures { get; set; } = new SymbolInfo();
        public SpreadState State { get; set; } = SpreadState.WaitingForNarrowSpread;
        public decimal MaxSpread { get; set; } = decimal.MinValue;
        public decimal MinSpread { get; set; } = decimal.MaxValue;
        public decimal CurrSpread { get; set; } = 0;
        public decimal FundingRate { get; set; } = 0;
        public decimal SpotOrderAmount { get; set; } = 0; // in coins

        #region ConsiderToMove
        // public MarketDataWebSocket MarketDataSocket { get; set; }
        public BinanceSocketClient BinanceDataSocket { get; set; }

        public event Action<Spread>? MinSpreadReached;

        public event Action<Spread>? MinSpreadLost;

        public event Action<Spread>? MinSpreadChanged;

        public event Action<Spread>? EnterOrderExecuted;

        public event Action<Spread>? MaxSpreadReached;

        public event Action<Spread>? MaxSpreadLost;

        public event Action<Spread>? MaxSpreadChanged;

        public event Action<Spread>? ExitOrderExecuted;
        #endregion

        public static readonly decimal SpreadToEnter = 0.05M; // 0.1M;

        public static readonly decimal DiffToExit = 0.03M;

        // private decimal _spreadToEnter = 0;

        private UpdateSubscription? _tradesSubscription = null;

        public decimal EnterSpread { get; set; } = 0;

        /*private readonly decimal _richDeltaSpread = 0.005M;

        private readonly decimal _lossDeltaSpread = 0.001M;*/

        private readonly decimal _richDeltaSpread = 0.020M;

        private readonly decimal _lossDeltaSpread = 0.010M;

        // options
        // private readonly bool _executionByTrades = false;
        public ExecutionType[] ExecType { get; set; } // = [ExecutionType.ByPrice, ExecutionType.ByTrades];

        public bool OverrideBestAskBid { set; get; } = true;

        // private readonly object _subscribeLocker = new();  // объект-заглушка

        public async void UnsubscribeTradeStream() // 
        {
            // lock (_subscribeLocker)
            //{
            // BnnUtils.Log($"unsubscribe - start - {BaseAsset.ToUpper()} / {(_tradesSubscription == null ? "NULL" : (_tradesSubscription.Id))}");
            if (_tradesSubscription == null) return;
            var tempSubscription = _tradesSubscription;
            _tradesSubscription = null;
            await BinanceDataSocket.UnsubscribeAsync(tempSubscription);
            // BnnUtils.Log($"unsubscribe - ok - {BaseAsset.ToUpper()} / {_tradesSubscription}");
            //}
        }

        public async void SubscribeSpotTrades()
        {
            if ((Order == null) || (_tradesSubscription != null) || !ExecType.Contains(ExecutionType.ByTrades)) return;
            // var symbol = ;
            // var marker = DateTime.Now.ToString();
            // BnnUtils.Log($"subscribe {symbol} - {marker}");

            _tradesSubscription = (await BinanceDataSocket.SpotApi.ExchangeData.SubscribeToTradeUpdatesAsync($"{BaseAsset.ToUpper()}{Spot.QuoteAsset.ToUpper()}", update =>
            {
                /*if (_tradesSubscription == null)
                {
                    BnnUtils.Log($"NULL {update.Data.Symbol} / {marker} / {BaseAsset.ToUpper()}{Spot.QuoteAsset.ToUpper()}; update: {update.Symbol}, {update.Data.Symbol}, {update.Data.Price}");
                    return;
                }

                BnnUtils.Log($"TRADE {update.Data.Symbol} / {marker} / {BaseAsset.ToUpper()}{Spot.QuoteAsset.ToUpper()}; update: {update.Symbol}, {update.Data.Symbol}, {update.Data.Price}, {update.Data.Quantity}");*/

                if ((update.Data.BuyerIsMaker && (State == SpreadState.SpotEnterOrderCreated) && (update.Data.Price <= Order.Price)) || (!update.Data.BuyerIsMaker && (State == SpreadState.SpotExitOrderCreated) && (update.Data.Price >= Order.Price)))
                {
                    var trueState = State;
                    State = SpreadState.Updating; // try to prevent doubled calls

                    Order.Filled += update.Data.Quantity; // if (Order.Filled >= Order.Amount) 
                    UnsubscribeTradeStream();
                    BnnUtils.Log($"{update.Data.Symbol}; quantity: {update.Data.Quantity:0.#####} / {Order.Amount}; trade price: {update.Data.Price:0.#######}, best bid: {Spot.BestBid:0.#####}, best ask: {Spot.BestAsk:0.#####}"); //  * {Order.Filled:0.#####}
                    if (trueState == SpreadState.SpotEnterOrderCreated)
                    {
                        SpotOrderAmount = update.Data.Quantity; // order partially executed
                        EnterOrderExecuted?.Invoke(this);
                    }
                    else ExitOrderExecuted?.Invoke(this);
                }
            })).Data;
            // BnnUtils.Log($"subscribe - {marker} - !!! - end - {BaseAsset.ToUpper()}{Spot.QuoteAsset.ToUpper()} - {symbol} - {_tradesSubscription}");
        }

        public void SubscribeFuturesTrades()
        {
            if ((Order == null) || !ExecType.Contains(ExecutionType.ByTrades)) return;
            _tradesSubscription = BinanceDataSocket.UsdFuturesApi.ExchangeData.SubscribeToTradeUpdatesAsync($"{BaseAsset.ToUpper()}{Futures.QuoteAsset.ToUpper()}", update =>
            {
                if ((!update.Data.BuyerIsMaker && (State == SpreadState.FuturesEnterOrderCreated) && (update.Data.Price >= Order.Price)) || (update.Data.BuyerIsMaker && (State == SpreadState.FuturesExitOrderCreated) && (update.Data.Price <= Order.Price)))
                {
                    Order.Filled += update.Data.Quantity;
                    BnnUtils.Log($"{update.Data.Symbol}; quantity: {update.Data.Quantity:0.#####} / {Order.Amount}; trade price: {update.Data.Price:0.#######}, best bid: {Spot.BestBid:0.#####}, best ask: {Spot.BestAsk:0.#####}"); //  => {Order.Filled:0.#####}
                    UnsubscribeTradeStream();
                    if (State == SpreadState.FuturesEnterOrderCreated) EnterOrderExecuted?.Invoke(this);
                    else ExitOrderExecuted?.Invoke(this);
                }
            }).Result.Data;
        }

        private void RecalcSpread()
        {
            if ((Spot.BestBid == 0) || (Futures.BestAsk == 0)) return;
            CurrSpread = (Spot.BestBid - Futures.BestAsk) / Spot.BestBid * 100;
            if (CurrSpread > MaxSpread) MaxSpread = CurrSpread;
            if (CurrSpread < MinSpread) MinSpread = CurrSpread;
            /* if ((MaxSpread == CurrSpread) || (MinSpread == CurrSpread))
            {
                var avgSpread = (MaxSpread + MinSpread) / 2;
                // _spreadToEnter = avgSpread - SpreadToEnter;
            }*/

            switch (State)
            {
                case SpreadState.WaitingForNarrowSpread:
                    // if ((CurrSpread < _spreadToEnter - _spreadDelta) && (CurrSpread < _maxSpreadToEnter)) MinSpreadReached?.Invoke(this);
                    if ((QueueRates.Count > 0) && (CurrSpread < QueueRates.Average() - _richDeltaSpread)) MinSpreadReached?.Invoke(this);
                    if (State == SpreadState.SpotEnterOrderCreated)
                    {
                        SubscribeSpotTrades();
                        if (State != SpreadState.SpotEnterOrderCreated) UnsubscribeTradeStream(); // spread lost
                    }
                    break;

                case SpreadState.WaitingForWideSpread:
                    // EnterSpread = (Spot.EnterPrice - Futures.EnterPrice) / Futures.EnterPrice * 100;
                    //_spreadToExit = EnterSpread + DiffToExit + _spreadDelta;
                    //if (_spreadToExit > MaxSpread - _spreadDelta) _spreadToExit = MaxSpread - _spreadDelta;
                    // if (CurrSpread > _spreadToExit) MaxSpreadReached?.Invoke(this);
                    if ((QueueRates.Count > 0) && (CurrSpread > QueueRates.Average() + _richDeltaSpread)) MaxSpreadReached?.Invoke(this);
                    if (State == SpreadState.FuturesExitOrderCreated)
                    {
                        SubscribeFuturesTrades();
                        if (State != SpreadState.FuturesExitOrderCreated) UnsubscribeTradeStream(); // spread lost
                    }
                    break;

                case SpreadState.FuturesExitOrderCreated:
                    if ((Futures.BestAsk <= Futures.ExitPrice) && ExecType.Contains(ExecutionType.ByPrice)) // --- !_executionByTrades) ---_executionType
                    {
                        BnnUtils.Log($"an ask order replaced the bid: order price: order price: {Futures.ExitPrice:0.#####}, best bid: {Futures.BestBid:0.#####}, best ask: {Futures.BestAsk:0.#####}");
                        UnsubscribeTradeStream();
                        ExitOrderExecuted?.Invoke(this);
                    }

                    // if (CurrSpread > _spreadToEnter + _spreadDelta)
                    if (CurrSpread < QueueRates.Average() + _lossDeltaSpread)
                    {
                        UnsubscribeTradeStream();
                        MaxSpreadLost?.Invoke(this);
                    }
                    break;

                case SpreadState.SpotEnterOrderCreated:
                    if ((Spot.BestAsk <= Spot.EnterPrice) && ExecType.Contains(ExecutionType.ByPrice))
                    {
                        SpotOrderAmount = (Spot.BestAsk + Spot.BestBid) / 4;
                        BnnUtils.Log($"an ask order replaced the bid: order price: {Spot.EnterPrice}, best bid: {Spot.BestBid:0.#####}, best ask: {Spot.BestAsk:0.#####}, amount: {SpotOrderAmount}");
                        UnsubscribeTradeStream();
                        EnterOrderExecuted?.Invoke(this);
                    }

                    // if (CurrSpread > _spreadToEnter + _spreadDelta)
                    if (CurrSpread > QueueRates.Average() - _lossDeltaSpread)
                    {
                        // BnnUtils.Log($"!!! MinSpreadLost - return code on the homeland !!!");
                        UnsubscribeTradeStream();
                        MinSpreadLost?.Invoke(this);
                    }
                    break;
            }
        }

        public void SpotMessageReceived(DataEvent<Binance.Net.Objects.Models.Spot.Socket.BinanceStreamBookPrice> update)
        {
            Spot.BestAsk = update.Data.BestAskPrice;
            Spot.BestBid = update.Data.BestBidPrice;
            RecalcSpread();

            if (State == SpreadState.SpotEnterOrderCreated)
            {
                // !!!
                if (Spot.BestBid > Spot.EnterPrice) MinSpreadChanged?.Invoke(this);
            }

            if (State == SpreadState.SpotExitOrderCreated)
            {
                if ((Spot.BestBid >= Spot.ExitPrice) && ExecType.Contains(ExecutionType.ByPrice))
                {
                    BnnUtils.Log($"a bid order replaced the ask: order price: {Spot.ExitPrice}, best bid: {Spot.BestBid:0.#####}, best ask: {Spot.BestAsk:0.#####}");
                    UnsubscribeTradeStream();
                    ExitOrderExecuted?.Invoke(this);
                }
                // if (Spot.BestAsk > Spot.ExitPrice) ExitOrderExecuted?.Invoke(this);

                if (Spot.BestAsk < Spot.ExitPrice) MaxSpreadChanged?.Invoke(this);
            }
        }

        private void FuturesMessageReceived(DataEvent<BinanceFuturesStreamBookPrice> update)
        {
            Futures.BestAsk = update.Data.BestAskPrice;
            Futures.BestBid = update.Data.BestBidPrice;
            RecalcSpread();

            if (State == SpreadState.FuturesEnterOrderCreated)
            {
                // if (Futures.BestAsk > Futures.EnterPrice) EnterOrderExecuted?.Invoke(this);
                if (Futures.BestAsk < Futures.EnterPrice) MinSpreadChanged?.Invoke(this);

                if ((Futures.BestBid >= Futures.EnterPrice) && ExecType.Contains(ExecutionType.ByPrice))
                {
                    BnnUtils.Log($"a bid order replaced the ask: order price: {Futures.EnterPrice}, best bid: {Futures.BestBid:0.#####}, best ask: {Futures.BestAsk:0.#####}");
                    UnsubscribeTradeStream();
                    EnterOrderExecuted?.Invoke(this);
                }
            }

            if (State == SpreadState.FuturesExitOrderCreated)
            {
                // if (Futures.BestBid < Futures.ExitPrice) ExitOrderExecuted?.Invoke(this);

                //if (CurrSpread < _spreadToExit - _spreadDelta) MaxSpreadLost?.Invoke(this);
                //else
                //{
                if (Futures.BestBid > Futures.ExitPrice) MaxSpreadChanged?.Invoke(this);
                // }
            }
        }

        public Spread(string baseAsset, string quoteSpotAsset, string quoteFuturesAsset, decimal fee, ExecutionType[] execType)
        {
            BaseAsset = baseAsset;
            Spot.QuoteAsset = quoteSpotAsset;
            Futures.QuoteAsset = quoteFuturesAsset;
            Spot.Fee = fee;
            ExecType = execType;

            /* MarketDataSocket = new MarketDataWebSocket($"{baseAsset.ToLower()}{quoteSpotAsset.ToLower()}@depth20@100ms");
            MarketDataSocket.OnMessageReceived(SpotMessageReceivedOld, CancellationToken.None);
            MarketDataSocket.ConnectAsync(CancellationToken.None);*/

            BinanceDataSocket = new BinanceSocketClient();
            BinanceDataSocket.SpotApi.ExchangeData.SubscribeToBookTickerUpdatesAsync($"{baseAsset.ToUpper()}{quoteSpotAsset.ToUpper()}", SpotMessageReceived);
            // FuturesDataSocket.SpotApi.ExchangeData.SubscribeToBookTickerUpdatesAsync($"{baseAsset.ToUpper()}{quoteSpotAsset.ToUpper()}", update => Console.WriteLine($"{update.Data}"));
            BinanceDataSocket.UsdFuturesApi.ExchangeData.SubscribeToBookTickerUpdatesAsync($"{baseAsset.ToUpper()}{quoteFuturesAsset.ToUpper()}", FuturesMessageReceived);
        }

        public Order? Order { set; get; }

        public Queue<decimal> QueueRates { set; get; } = new();

        public override string ToString()
        {
            var addInfo = State switch
            {
                // case SpreadState.CollectInformation:
                SpreadState.Updating => string.Empty,
                SpreadState.WaitingForNarrowSpread => $"=>{QueueRates.Average():0.###}",
                // SpreadState.WaitingForWideSpread => $"=>{_spreadToExit:0.##}",
                SpreadState.WaitingForWideSpread => $"=>{QueueRates.Average():0.###}",
                _ => $"/{State}",
            };
            var exitStatuses = new[] { SpreadState.WaitingForWideSpread, SpreadState.FuturesExitOrderCreated, SpreadState.SpotExitOrderCreated };
            var bestBidAsk = exitStatuses.Contains(State) ? $"{Spot.BestAsk:0.#######}/{Futures.BestBid:0.#######}" : $"{Spot.BestBid:0.#######}/{Futures.BestAsk:0.#######}";
            return $"{BaseAsset} {bestBidAsk}|{MinSpread:0.##}<{CurrSpread:0.###}<{MaxSpread:0.##}{addInfo}";
        }
    }


    internal class FuturesSpread : BaseStrategy
    {
        public FuturesSpread(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            var paramExecBy = AccountManager.Config.GetValue<string>("Strategies:FuturesSpread:ExecutionType") ?? "both";
            paramExecBy = paramExecBy.ToLower();

            if (paramExecBy == "trade") _executionType = [ExecutionType.ByTrades];
            else if (paramExecBy == "price") _executionType = [ExecutionType.ByPrice];
            else _executionType = [ExecutionType.ByPrice, ExecutionType.ByTrades];

            BnnUtils.Log($"{GetName()} {SymbolName} - exec {paramExecBy}");
            _dealParams = new DummyParams();
        }

        private readonly ExecutionType[] _executionType = [ExecutionType.ByPrice, ExecutionType.ByTrades];

        private SpreadState _state = SpreadState.Updating;

        private readonly List<Spread> _spreads = [];

        private DateTime _collectStart = DateTime.MinValue;

        private readonly int _collectSeconds = 60; // 10

        private decimal _totalProfit = 0;

        #region Parent Methods
        protected override double GetShortValue(decimal longPrice, out decimal stopLossValue)
        {
            stopLossValue = 0;
            return double.MaxValue;
        }

        public override string GetName() { return "Futures Spread"; }

        protected override decimal GetLongValue(List<Kline> klines, decimal previousValue = -1)
        {
            return 0;
        }

        protected override double GetShortValue(List<Kline> klines, double previousValue = -1)
        {
            return double.MaxValue;
        }

        protected override void SaveStatistics(List<BaseDealParams> tradeResults)
        {
            // override abstract method
        }
        #endregion
        public override void Prepare()
        {
            var spotCoin = "FDUSD";
            var futuresCoin = "USDC";

            var spreadScript = $"select SpotCoins.BaseAsset, SpotQuoteAsset, SpotPriceStep, FuturesQuoteAsset, FuturesPriceStep from " +
                "\t(select substr(Name, 0, length(Name) - 4) BaseAsset, '" + spotCoin + "' SpotQuoteAsset, PriceStep SpotPriceStep " +
                "\tfrom symbol where name like '%" + spotCoin + "') SpotCoins " +
                "inner join" +
                "\t(select substr(Name, 0, length(Name) - 3) BaseAsset, '" + futuresCoin + "' FuturesQuoteAsset, PriceStep FuturesPriceStep " +
                "\tfrom symbol where name like '%" + futuresCoin + "') FuturesCoins " +
                "on SpotCoins.BaseAsset = FuturesCoins.BaseAsset";

            var dbSymbols = new List<TempDbInfo>();
            DB.OpenQuery(_manager.DbConnection, spreadScript, null, dr =>
            {
                var topCoins = new[] { "BTC", "ETH", "SOL", "XRP", "BNB", "DOGE", "SUI" };
                // var topCoins = new[] { "BTC" };
                if (!topCoins.Contains((string)dr["BaseAsset"])) return;
                // if (_spreads.Count == 9) return;
                // if (((string)dr["BaseAsset"] == "BOME") || ((string)dr["BaseAsset"] == "ORDI") || ((string)dr["BaseAsset"] == "WIF") || ((string)dr["BaseAsset"] == "PNUT") || ((string)dr["BaseAsset"] == "HBAR") || ((string)dr["BaseAsset"] == "TRUMP")) return; // ((string)dr["BaseAsset"] != "WLD")
                dbSymbols.Add(new TempDbInfo()
                {
                    BaseAsset = (string)dr["BaseAsset"],
                    SpotPriceStep = (decimal)dr["SpotPriceStep"],
                    FuturesPriceStep = (decimal)dr["FuturesPriceStep"]
                });
            });

            var futuresClient = new BinanceRestClient();
            var fundingInfo = futuresClient.UsdFuturesApi.ExchangeData.GetMarkPricesAsync().Result;
            // var futuresInfo = futuresClient.UsdFuturesApi.ExchangeData.GetExchangeInfoAsync().Result;
            foreach (var s in fundingInfo.Data)
            {
                var baseAsset = s.Symbol[..^4];
                var dbSymbol = dbSymbols.Find(sp => sp.BaseAsset == baseAsset);
                // if ((dbSymbol == null) || !s.Symbol.Contains(futuresCoin) || (s.FundingRate <= 0)) continue;
                if ((dbSymbol == null) || !s.Symbol.Contains(futuresCoin)) continue;

                var spread = AddSpread(dbSymbol.BaseAsset, spotCoin, futuresCoin);
                spread.Spot.PriceStep = dbSymbol.SpotPriceStep;
                spread.Futures.PriceStep = dbSymbol.FuturesPriceStep;
                spread.FundingRate = s.FundingRate ?? 0;
                // if (_spreads.Count == 6) break;
            }

            // https://api.binance.com/api/v3/exchangeInfo?permissions=SPOT&symbolStatus=TRADING
            /*Market market = new();
            var exchInfo = market.ExchangeInformation().Result;
            dynamic spotInfo = JsonConvert.DeserializeObject(exchInfo.Trim()) ?? throw new Exception("depth returned no data");
            foreach (var s in spotInfo.symbols)
            {
                string baseAsset = s.baseAsset;
                if ((s.status == "TRADING") && (s.quoteAsset == "FDUSD")) spotSymbols.Add(new SpotInfo() { BaseAsset = baseAsset, PriceStep = s.filters[0].tickSize });
            }

            // https://fapi.binance.com/fapi/v1/exchangeInfo?symbolStatus=TRADING
            */

            var funds = _spreads.OrderByDescending(r => r.FundingRate).ToList();
            foreach (var f in funds)
            {
                BnnUtils.Log($"{f.BaseAsset}: rate {f.FundingRate * 100:0.####}%, spot tick {f.Spot.PriceStep:0.#######}, futures tick {f.Futures.PriceStep:0.#######}", false);
            }
        }

        private Spread AddSpread(string baseAsset, string quoteSpotAsset = "FDUSD", string quoteFuturesAsset = "USDC", decimal fee = 0) //  --- async Task< async Task<Rate> async
        {
            var spread = new Spread(baseAsset, quoteSpotAsset, quoteFuturesAsset, fee, _executionType);
            spread.MinSpreadReached += Spread_MinSpreadReached;
            spread.EnterOrderExecuted += Spread_EnterOrderExecuted;
            spread.MinSpreadLost += Spread_MinSpreadLost;
            spread.MaxSpreadReached += Spread_MaxSpreadReached;
            spread.MaxSpreadLost += Spread_MaxSpreadLost;
            spread.ExitOrderExecuted += Spread_ExitOrderExecuted;
            spread.MinSpreadChanged += Spread_MinSpreadChanged;
            spread.MaxSpreadChanged += Spread_MaxSpreadChanged;
            _spreads.Add(spread);
            return spread;
        }

        private void Spread_MaxSpreadChanged(Spread spread)
        {
            if (spread.Order == null) return;

            // if (_state == SpreadState.FuturesExitOrderCreated)
            if (CheckAndUpdateState(spread, SpreadState.FuturesExitOrderCreated, SpreadState.Updating))
            {
                // UpdateState(spread, SpreadState.Updating);
                _priceStep = spread.Futures.PriceStep;
                CurrPrice = spread.Futures.BestBid;
                var orderPrice = spread.Futures.BestBid;
                if (spread.OverrideBestAskBid && (spread.Futures.BestAsk - spread.Futures.BestBid > spread.Futures.PriceStep)) orderPrice = spread.Futures.BestBid + spread.Futures.PriceStep;

                ChangeOrderPrice(spread.Order, orderPrice);
                spread.Futures.ExitPrice = spread.Futures.BestBid;
                UpdateState(spread, SpreadState.FuturesExitOrderCreated);
            }

            // if (_state == SpreadState.SpotExitOrderCreated)
            if (CheckAndUpdateState(spread, SpreadState.SpotExitOrderCreated, SpreadState.Updating))
            {
                // UpdateState(spread, SpreadState.Updating);
                _priceStep = spread.Spot.PriceStep;
                CurrPrice = spread.Spot.BestAsk;
                var orderPrice = spread.Spot.BestAsk;
                if (spread.OverrideBestAskBid && (spread.Spot.BestAsk - spread.Spot.BestBid > spread.Spot.PriceStep)) orderPrice = spread.Spot.BestAsk - spread.Spot.PriceStep;

                ChangeOrderPrice(spread.Order, orderPrice);
                spread.Spot.ExitPrice = spread.Spot.BestAsk;
                UpdateState(spread, SpreadState.SpotExitOrderCreated);
            }
        }

        private void Spread_MinSpreadChanged(Spread spread)
        {
            if (spread.Order == null) return;

            if (_state == SpreadState.FuturesEnterOrderCreated)
            {
                UpdateState(spread, SpreadState.Updating);
                _priceStep = spread.Futures.PriceStep;
                CurrPrice = spread.Futures.BestAsk;
                var orderPrice = spread.Futures.BestAsk;
                if (spread.OverrideBestAskBid && (spread.Futures.BestAsk - spread.Futures.BestBid > spread.Futures.PriceStep)) orderPrice = spread.Futures.BestAsk - spread.Futures.PriceStep;

                ChangeOrderPrice(spread.Order, orderPrice);
                spread.Futures.EnterPrice = spread.Futures.BestAsk;
                UpdateState(spread, SpreadState.FuturesEnterOrderCreated);
            }

            if (_state == SpreadState.SpotEnterOrderCreated)
            {
                UpdateState(spread, SpreadState.Updating);
                _priceStep = spread.Spot.PriceStep;
                CurrPrice = spread.Spot.BestBid;
                var orderPrice = spread.Spot.BestBid;
                if (spread.OverrideBestAskBid && (spread.Spot.BestAsk - spread.Spot.BestBid > spread.Spot.PriceStep)) orderPrice = spread.Spot.BestBid + spread.Spot.PriceStep;

                ChangeOrderPrice(spread.Order, orderPrice);
                spread.Spot.EnterPrice = spread.Spot.BestBid;
                UpdateState(spread, SpreadState.SpotEnterOrderCreated);
            }
        }

        private void Spread_MaxSpreadLost(Spread spread)
        {
            if (!CheckAndUpdateState(spread, SpreadState.FuturesExitOrderCreated, SpreadState.Updating)) return;
            // if (_state != SpreadState.FuturesExitOrderCreated) return;
            // UpdateState(spread, SpreadState.Updating);
            spread.UnsubscribeTradeStream();
            BnnUtils.Log($"{spread} [futures] max spread lost", true);
            if (spread.Order != null) CancelOrder(spread.Order);
            UpdateState(spread, SpreadState.WaitingForWideSpread);
        }

        private void Spread_MinSpreadLost(Spread spread)
        {
            if (!CheckAndUpdateState(spread, SpreadState.SpotEnterOrderCreated, SpreadState.Updating)) return;
            BnnUtils.Log($"{spread} [spot]", true);
            spread.UnsubscribeTradeStream();
            if (spread.Order != null) CancelOrder(spread.Order);
            UpdateState(spread, SpreadState.WaitingForNarrowSpread);
            Log(string.Empty); //, false
        }

        private void Spread_MaxSpreadReached(Spread spread)
        {
            if (!CheckAndUpdateState(spread, SpreadState.WaitingForWideSpread, SpreadState.Updating)) return;

            // if (_state != SpreadState.WaitingForWideSpread) return;
            // UpdateState(spread, SpreadState.Updating);

            var message = $"{spread}, avg: {spread.QueueRates.Average():0.###}: max spread reached [futures]"; //  - {_state}
            var orderPrice = spread.Futures.BestBid;
            if (spread.OverrideBestAskBid && (spread.Futures.BestAsk - spread.Futures.BestBid > spread.Futures.PriceStep))
            {
                orderPrice = spread.Futures.BestBid + spread.Futures.PriceStep;
                message += $", order price: best bid + price step: {orderPrice}";
            };
            BnnUtils.Log(message, true); // : exit price {spread.Futures.ExitPrice}

            CurrPrice = spread.Futures.BestBid;
            spread.Order = CreateFuturesLimitOrder(orderPrice, true, spread.SpotOrderAmount);
            spread.Futures.ExitPrice = spread.Futures.BestBid;
            UpdateState(spread, SpreadState.FuturesExitOrderCreated);
        }

        private void Spread_MinSpreadReached(Spread spread)
        {
            // var tempState = _state;
            if (!CheckAndUpdateState(spread, SpreadState.WaitingForNarrowSpread, SpreadState.Updating)) return;
            // if (_state != SpreadState.WaitingForNarrowSpread) return;
            // UpdateState(spread, SpreadState.Updating);

            var message = $"{spread}, avg {spread.QueueRates.Average():0.###}: min spread reached [spot]"; //  - {_state}

            var orderPrice = spread.Spot.BestBid;
            if (spread.OverrideBestAskBid && (spread.Spot.BestAsk - spread.Spot.BestBid > spread.Spot.PriceStep))
            {
                orderPrice = spread.Spot.BestBid + spread.Spot.PriceStep;
                message += $", order price: best bid + price step: {orderPrice}";
            };
            BnnUtils.Log(message, true);
            SymbolName = spread.BaseAsset + spread.Spot.QuoteAsset;
            CurrPrice = spread.Spot.BestBid;
            _priceStep = spread.Spot.PriceStep;
            spread.Order = CreateSpotLimitOrder(orderPrice, true, false);
            spread.Spot.EnterPrice = spread.Spot.BestBid;
            UpdateState(spread, SpreadState.SpotEnterOrderCreated);
        }

        private void Spread_ExitOrderExecuted(Spread spread)
        {
            // if (_state == SpreadState.SpotExitOrderCreated)
            if (CheckAndUpdateState(spread, SpreadState.SpotExitOrderCreated, SpreadState.Updating))
            {
                // UpdateState(spread, SpreadState.Updating);
                CurrPrice = spread.Spot.BestAsk;
                if (spread.Order != null) ExecuteOrder(spread.Order);
                var exitSpread = (spread.Spot.ExitPrice - spread.Futures.ExitPrice) / spread.Futures.ExitPrice * 100;
                var dealSpread = exitSpread - spread.EnterSpread;
                var dealProfit = spread.SpotOrderAmount * spread.Spot.ExitPrice * dealSpread / 100;
                _totalProfit += dealProfit;
                BnnUtils.Log($"exit order executed {spread} [spot]; exit spread: {exitSpread:0.###}%; deal spread: {dealSpread:0.###}%; spot amount: {spread.SpotOrderAmount * spread.Spot.ExitPrice:0.##} => +{dealProfit:0.###} ={_totalProfit:0.###}", true); // . deal spread: {}
                spread.MinSpread = decimal.MaxValue;
                spread.MaxSpread = 0;
                UpdateState(spread, SpreadState.WaitingForNarrowSpread);
                Log(string.Empty);
            }

            if (CheckAndUpdateState(spread, SpreadState.FuturesExitOrderCreated, SpreadState.Updating))
            //if (_state == SpreadState.FuturesExitOrderCreated)
            {
                // UpdateState(spread, SpreadState.Updating);
                CurrPrice = spread.Futures.BestBid;
                if (spread.Order != null) ExecuteOrder(spread.Order);

                var message = $"exit order executed {spread} [futures] => creating spot exit order... [spot]";
                var orderPrice = spread.Spot.BestAsk;
                if (spread.OverrideBestAskBid && (spread.Spot.BestAsk - spread.Spot.BestBid > spread.Spot.PriceStep))
                {
                    orderPrice = spread.Spot.BestAsk - spread.Spot.PriceStep;
                    message += $", order price: best ask - price step: {orderPrice}";
                };
                BnnUtils.Log(message, true);

                SymbolName = spread.BaseAsset + spread.Spot.QuoteAsset;
                CurrPrice = spread.Spot.BestAsk;
                _priceStep = spread.Spot.PriceStep;
                spread.Order = CreateSpotLimitOrder(orderPrice, false, false, spread.SpotOrderAmount);
                spread.Spot.ExitPrice = spread.Spot.BestAsk;
                UpdateState(spread, SpreadState.SpotExitOrderCreated);
                spread.SubscribeSpotTrades();
                if ((spread.State != SpreadState.SpotExitOrderCreated) && (spread.State != SpreadState.Updating)) spread.UnsubscribeTradeStream(); // spread lost
            }
        }

        private void UpdateState(Spread spread, SpreadState newState)
        {
            _state = newState;
            spread.State = _state;
        }

        private readonly object _statusLocker = new();  // объект-заглушка

        private bool CheckAndUpdateState(Spread spread, SpreadState oldState, SpreadState newState)
        {
            lock (_statusLocker)
            {
                if (_state != oldState) return false;
                _state = newState;
                spread.State = _state;
                return true;
            }
        }

        private void Spread_EnterOrderExecuted(Spread spread)
        {
            if (CheckAndUpdateState(spread, SpreadState.FuturesEnterOrderCreated, SpreadState.Updating))
            // if (_state == SpreadState.FuturesEnterOrderCreated)
            {
                // UpdateState(spread, SpreadState.Updating);
                CurrPrice = spread.Futures.BestAsk;
                if (spread.Order != null) ExecuteOrder(spread.Order);
                spread.EnterSpread = (spread.Spot.EnterPrice - spread.Futures.EnterPrice) / spread.Futures.EnterPrice * 100;
                BnnUtils.Log($"enter order executed {spread} [futures]; enter spread: {spread.EnterSpread:0.###}%", true);
                UpdateState(spread, SpreadState.WaitingForWideSpread);
            }

            if (CheckAndUpdateState(spread, SpreadState.SpotEnterOrderCreated, SpreadState.Updating))
            // if (_state == SpreadState.SpotEnterOrderCreated)
            {
                // UpdateState(spread, SpreadState.Updating);
                CurrPrice = spread.Spot.BestBid;
                // if (spread.SpotOrderAmount * spread.Spot.EnterPrice > _manager.SpotAmount) spread.SpotOrderAmount = _manager.SpotAmount / spread.Spot.EnterPrice;
                if (spread.Order != null)
                {
                    if (spread.SpotOrderAmount < spread.Order.Amount)
                    {
                        _manager.SpotAmount += (spread.Order.Price * (spread.Order.Amount - spread.SpotOrderAmount)); // return rest of order 
                        spread.Order.Amount = spread.SpotOrderAmount;
                    }
                    else spread.SpotOrderAmount = spread.Order.Amount;
                    ExecuteOrder(spread.Order);
                }

                var message = $"enter order executed {spread} [spot] => creating futures short order... [futures]";
                var orderPrice = spread.Futures.BestAsk;
                if (spread.OverrideBestAskBid && (spread.Futures.BestAsk - spread.Futures.BestBid > spread.Futures.PriceStep))
                {
                    orderPrice = spread.Futures.BestAsk - spread.Futures.PriceStep;
                    message += $", order price: best ask - price step: {orderPrice}";
                };
                BnnUtils.Log(message, true);

                // BnnUtils.Log($"SpotOrderAmount: {spread.SpotOrderAmount}", true);
                SymbolName = spread.BaseAsset + spread.Futures.QuoteAsset;
                CurrPrice = spread.Futures.BestAsk;
                _priceStep = spread.Futures.PriceStep;
                spread.Order = CreateFuturesLimitOrder(orderPrice, false, spread.SpotOrderAmount);
                spread.Futures.EnterPrice = spread.Futures.BestAsk;
                UpdateState(spread, SpreadState.FuturesEnterOrderCreated);
                spread.SubscribeFuturesTrades();
                if ((spread.State != SpreadState.FuturesEnterOrderCreated) && (spread.State != SpreadState.Updating)) spread.UnsubscribeTradeStream(); // spread lost
            }

            // BnnUtils.Log($"{f.BaseAsset}: rate {f.FundingRate * 100:0.####}%, spot tick {f.SpotPriceStep:0.#######}", false);
        }

        public override void Start()
        {
            _state = SpreadState.CollectInformation;
            _collectStart = DateTime.Now;
            //_state = SpreadState.WaitingForNarrowSpread;
            // _state = SpreadState.Updating;
        }

        public override string GetCurrentInfo()
        {
            if ((_state == SpreadState.CollectInformation) && (DateTime.Now.Subtract(_collectStart).TotalSeconds > _collectSeconds)) _state = SpreadState.WaitingForNarrowSpread;
            if (_state == SpreadState.Updating) return "updating...";
            var spreadsInfo = string.Empty;
            // var spreads = _spreads;
            var activeSpreadStates = new[] { SpreadState.SpotEnterOrderCreated, SpreadState.FuturesEnterOrderCreated, SpreadState.WaitingForWideSpread, SpreadState.SpotExitOrderCreated, SpreadState.FuturesExitOrderCreated, SpreadState.Updating };
            var spreads = _spreads.OrderByDescending(r => activeSpreadStates.Contains(r.State)).ToList(); // OrderByDescending

            foreach (var spread in spreads)
            {
                if (spread.MinSpread == decimal.MaxValue) continue;

                while (spread.QueueRates.Count > 599) spread.QueueRates.Dequeue();
                spread.QueueRates.Enqueue(spread.CurrSpread);

                // if (spreadsInfo.Length > 151) break; // for notebook
                if (spreadsInfo.Length < 233) spreadsInfo += $"{spread}   "; // for desktop
            }
            // spreadsInfo += $" {_state}...";
            if (_state == SpreadState.CollectInformation) spreadsInfo += $" {_collectSeconds - DateTime.Now.Subtract(_collectStart).TotalSeconds:0.##}s";
            return spreadsInfo;
        }
    }
}