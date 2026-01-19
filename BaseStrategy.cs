// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Data;
using Binance.Spot;
using Binance.Spot.Models;
using DbSpace;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Bnncmd.Strategy;
using Microsoft.Extensions.Logging;
using bnncmd.Strategy;

namespace Bnncmd
{
    internal abstract class BaseStrategy
    {
        protected AccountManager _manager;

        /* protected string BinanceName { get; set; } = "Binance";
        protected string BybitName { get; set; } = "Bybit";
        protected string MexcName { get; set; } = "Mexc";*/

        public string SymbolName { get; set; }
        protected readonly long SymbolId;

        protected decimal _priceStep;
        protected decimal _lotStep;
        protected decimal _fee;

        private bool _active = true;
        private readonly bool _saveStatistic = true;

        protected Order? _order = null;


        public decimal CurrPrice = -1;
        protected decimal _confirmExtrVal = 0;

        protected decimal _longPrice;
        protected decimal _shortPrice;
        protected decimal _stopLossPrice;
        private decimal _extrPrice = decimal.MaxValue;
        protected double _someValue = 0; // 3
        protected int _checkDealPriceInterval;

        protected int _leftKlines = 2;
        protected int _rightKlines = 2;

        public decimal RealPrice { get; set; }

        public bool Active
        {
            get { return _active; }
            set
            {
                if (_active == value) return;
                CurrPrice = -1;
                _active = value;
                if (value)
                {
                    _extrPrice = decimal.MaxValue;
                    _lastLongCheckTime = DateTime.Now;
                    _longPrice = GetLongValue(_longPrice);
                }
            }
        }

        protected string GetCurrencyName()
        {
            return SymbolName.Replace(_manager.QuoteCurrencyName, string.Empty);
        }

        public decimal CurrencyAmount { get; set; } = 0;

        public decimal PositionAmount { get; set; } = 0;

        protected DealParams _dealParams;

        protected List<Kline>? _klines;

        public static BaseStrategy CreateStrategy(string symbolName, AccountManager manager)
        {
            var strategyName = AccountManager.Config.GetValue<string>("Strategy");

            BaseStrategy ts; // FuturesSpread
            if (strategyName == "a") ts = new Arbitration(symbolName, manager);
            else if (strategyName == "b") ts = new Breakthrough(symbolName, manager);
            else if (strategyName == "c") ts = new RedCorrection(symbolName, manager);
            else if (strategyName == "d") ts = new DiffBook(symbolName, manager);
            else if (strategyName == "e") ts = new Earn(symbolName, manager);
            else if (strategyName == "f") ts = new SpotFuturesArbitration(symbolName, manager);
            else if (strategyName == "g") ts = new HighGreenKline(symbolName, manager);
            else if (strategyName == "h") { ts = new FundingHedge(symbolName, manager); Console.WriteLine("new FundingHedge !!!"); }
            else if (strategyName == "i") ts = new Impulse(symbolName, manager);
            else if (strategyName == "j") ts = new FuturesSpread(symbolName, manager);
            else if (strategyName == "k") ts = new Exchanger(symbolName, manager);
            else if (strategyName == "l") ts = new StableCoins(symbolName, manager);
            else if (strategyName == "m") ts = new MovingAverage(symbolName, manager);
            else if (strategyName == "n") ts = new OpenDay001(symbolName, manager);
            else if (strategyName == "o") ts = new OnePercent(symbolName, manager);
            else if (strategyName == "p") ts = new SnP500(symbolName, manager);
            else if (strategyName == "q") ts = new FibDiv(symbolName, manager);
            else if (strategyName == "r") ts = new RSI(symbolName, manager);
            else if (strategyName == "s") ts = new SpotArbitration(symbolName, manager);
            else if (strategyName == "t") ts = new EmaTrend(symbolName, manager);
            else if (strategyName == "u") ts = new RsiDump(symbolName, manager);
            else if (strategyName == "v") ts = new MovingRange(symbolName, manager);
            else if (strategyName == "w") ts = new ThinKline(symbolName, manager);
            else if (strategyName == "x") ts = new EMA(symbolName, manager);
            else if (strategyName == "y") ts = new Density(symbolName, manager);
            else if (strategyName == "z") ts = new SafetyTrade(symbolName, manager);
            else if (strategyName == "3") ts = new ThreeMinsRange(symbolName, manager);
            else throw new Exception($"Strategy not found ({strategyName})");
            return ts;
        }

        public abstract string GetName();

        public BaseStrategy(string symbolName, AccountManager manager)
        {
            _manager = manager;

            _dealParams = new DealParams(    // 0.011, 0, 0.019, 0)
                AccountManager.Config.GetValue<double>("FlatParams:DealProfit"),
                AccountManager.Config.GetValue<double>("FlatParams:Threshold"),
                AccountManager.Config.GetValue<double>("FlatParams:StopLoss"),
                AccountManager.Config.GetValue<double>("FlatParams:ConfirmExtrPart")
            );

            InitialParams.CopyFrom(_dealParams);

            _checkDealPriceInterval = AccountManager.Config.GetValue<int>("CheckDealPriceInterval");
            Slippage = AccountManager.Config.GetValue<decimal>("Slippage");
            SymbolName = symbolName;
            SymbolId = GetSymbolId(SymbolName);
        }


        protected List<Order> _orders = [];

        #region DbRoutines
        /// <summary>
        /// Database Routines
        /// </summary>
        /// <returns></returns>

        protected List<Kline> GroupKlines(List<Kline> klines, int secCount)
        {
            return klines.GroupBy(k => k.OpenTime / 1000 / secCount).Select(t => new Kline
            {
                OpenTime = t.First().OpenTime,
                OpenPrice = t.First().OpenPrice,
                ClosePrice = t.Last().ClosePrice,
                HighPrice = t.Max(k => k.HighPrice),
                LowPrice = t.Min(K => K.LowPrice)
            }).ToList();
        }

        protected (Extremum, Extremum, Extremum, Extremum) GetExtremums(List<Kline> klines)
        {
            var lastMinimum = new Extremum(0, -1);
            var lastMaximum = new Extremum(0, -1);
            var penultMaximum = new Extremum(0, -1);
            var penultMinimum = new Extremum(0, -1);

            for (var i = klines.Count - _leftKlines - 2; i > _rightKlines + 1; i--)
            // for (var i = 0; i < hourKlines.Count; i++)
            {
                // local maximin
                var high = klines[i].HighPrice;

                var isLeftMaximum = true;
                for (var j = 1; j <= _leftKlines; j++)
                {
                    isLeftMaximum = isLeftMaximum && (high >= klines[Math.Max(i - j, 0)].HighPrice);
                }

                var isRightMaximum = true;
                for (var j = 1; j <= _rightKlines; j++)
                {
                    isRightMaximum = isRightMaximum && (high > klines[Math.Min(i + j, klines.Count - 1)].HighPrice);
                }

                // if (isLeftMaximum && (high > klines[i + 1].HighPrice) && (high > klines[i + 2].HighPrice))
                // if ((high >= klines[i - 2].HighPrice) && (high >= klines[i - 1].HighPrice) && (high > klines[i + 1].HighPrice) && (high > klines[i + 2].HighPrice))
                if (isLeftMaximum && isRightMaximum)
                {
                    if (lastMaximum.Value > 0)
                    {
                        if (penultMaximum.Value < 0)
                        {
                            penultMaximum = new Extremum(klines[i].OpenTime, (decimal)high);
                            if (penultMinimum.Value > 0) break;
                        }
                    }
                    else lastMaximum = new Extremum(klines[i].OpenTime, (decimal)high);
                }

                // local minimum
                var low = klines[i].LowPrice;

                var isLeftMinimum = true;
                for (var j = 1; j <= _leftKlines; j++)
                {
                    isLeftMinimum = isLeftMinimum && (low <= klines[Math.Max(i - j, 0)].LowPrice);
                }

                var isRightMinimum = true;
                for (var j = 1; j <= _rightKlines; j++)
                {
                    isRightMinimum = isRightMinimum && (low < klines[Math.Min(i + j, klines.Count - 1)].LowPrice);
                }

                if (isLeftMinimum && isRightMinimum)
                // if ((low <= klines[i - 2].LowPrice) && (low <= klines[i - 1].LowPrice) && (low < klines[i + 1].LowPrice) && (low < klines[i + 2].LowPrice))
                {
                    if (lastMinimum.Value > 0)
                    {
                        if (penultMinimum.Value < 0)
                        {
                            penultMinimum = new Extremum(klines[i].OpenTime, (decimal)low);
                            if (penultMaximum.Value > 0) break;
                        }
                    }
                    else lastMinimum = new Extremum(klines[i].OpenTime, (decimal)low);
                }
            }
            return (penultMinimum, penultMaximum, lastMinimum, lastMaximum);
        }

        protected List<Kline> LoadKlinesFromDB(long startTime, long endTime)
        {
            var tableKlines = AccountManager.Timeframe < 1 ? "candlestick1s" : "candlestick1m";
            var klinesScript = "SELECT OpenTime, OpenPrice, HighPrice, LowPrice, ClosePrice " +
                $"from {tableKlines} " +
                $"where OpenTime >= {startTime} and OpenTime < {endTime} and SymbolId = {SymbolId}";

            if (AccountManager.LogLevel == 10) Log("\n\r" + klinesScript);

            var klines = new List<Kline>();
            DB.OpenQuery(_manager.DbConnection, klinesScript, null, dr =>
            {
                klines.Add(new Kline()
                {
                    // OpenTime = (long)(decimal)dr["OpenTime"],
                    OpenTime = dr["OpenTime"] is long ot ? ot : (long)(decimal)dr["OpenTime"],
                    OpenPrice = (decimal)dr["OpenPrice"], //  is decimal op ? (double)op : (double)dr["OpenPrice"],
                    // OpenPrice = dr["OpenPrice"] is double op ? (decimal)op : (dr["OpenPrice"] is long opp ? opp : (decimal)dr["OpenPrice"]),
                    HighPrice = dr["HighPrice"] is double hp ? (decimal)hp : (dr["HighPrice"] is long hpp ? hpp : (decimal)dr["HighPrice"]),
                    LowPrice = dr["LowPrice"] is double lp ? (decimal)lp : (dr["LowPrice"] is long lpp ? lpp : (decimal)dr["LowPrice"]),
                    //LowPrice = dr["LowPrice"] is decimal lp ? (double)lp : (double)dr["LowPrice"],
                    ClosePrice = dr["ClosePrice"] is double cp ? (decimal)cp : (dr["ClosePrice"] is long cpp ? cpp : (decimal)dr["ClosePrice"]),
                });
            });


            if (AccountManager.Timeframe > 1)
            {
                klines = GroupKlines(klines, 60 * (int)AccountManager.Timeframe);
            }

            if (AccountManager.LogLevel == 10) Console.WriteLine($"LoadKlinesFromDB: {BnnUtils.FormatUnixTime(startTime)} - {BnnUtils.FormatUnixTime(endTime)} => {klines.Count} records");
            return klines;
        }

        private decimal LoadLongPrice()
        {
            _longPrice = -1;
            DB.OpenSingleQuery($"select * from deal order by Time desc limit 1;", null, dr => // where SymbolId = {SymbolId} 
            {
                // if (dr["StopLoss"] == null) return;
                if (dr["StopLoss"] is System.DBNull) return;

                // long entered
                Active = (long)dr["SymbolId"] == SymbolId;
                if (!Active) return;
                _longPrice = (decimal)dr["AimPrice"];
                _shortPrice = (decimal)dr["ShortPrice"];
                _stopLossPrice = (decimal)dr["StopLoss"];
                // BnnUtils.Log($"{SymbolName} long {LongPrice:0.###} short aim {ShortPrice:0.###}");
            });
            return _longPrice;

            // var fileContent = File.ReadAllText(_longFile).Trim();
            // return double.Parse(fileContent);
        }

        private void SaveLongPrice(decimal price)
        {
            // BnnUtils.Log($"{DateTime.Now:dd.MM.yyyy HH:mm:ss} {SymbolName} long {LongPrice:0.###}  real price {RealPrice:0.###}  short aim {ShortPrice:0.###}  sl {_stopLossValue:0.###}");
            var script = "INSERT INTO deal (Time, SymbolId, AimPrice, RealPrice, ShortPrice, StopLoss) VALUES " +
                    $"({BnnUtils.GetUnixNow()}, {SymbolId}, {_longPrice}, {RealPrice}, {_shortPrice}, {_stopLossPrice});";
            DB.ExecQuery(_manager.DbConnection, script, null);
        }

        private void SaveShortPrice(decimal price)
        {
            var script = "INSERT INTO deal (Time, SymbolId, AimPrice, RealPrice) VALUES " +
                    $"({BnnUtils.GetUnixNow()}, {SymbolId}, {_shortPrice}, {RealPrice});";
            // Console.WriteLine(script);
            DB.ExecQuery(_manager.DbConnection, script, null);
        }

        private long GetSymbolId(string symbolName)
        {
            long result = -1;
            DB.OpenQuery(_manager.DbConnection, $"SELECT * FROM symbol WHERE Name = '{symbolName}'", null, dr =>
            {
                result = (long)dr["Id"];
                _priceStep = (decimal)dr["PriceStep"];
                _lotStep = (decimal)dr["LotStep"];
                _fee = (decimal)dr["Fee"];
            });
            return result;
        }

        protected void SaveNewOrder(Order order)
        {
            var script = "INSERT INTO exchanger (OrderId, SymbolId, Time, IsBuyer, Price, Amount, Filled) VALUES " +
                    $"({order.Id}, {SymbolId}, {order.DateTime}, {order.IsBuyer}, {order.Price}, {order.Amount}, 0);";
            DB.ExecQuery(_manager.DbConnection, script, null);
        }

        protected void LoadOrders()
        {
            var loadExchangerScript = $"select * from exchanger where SymbolId={SymbolId} order by Time desc limit 1";
            DB.OpenQuery(_manager.DbConnection, loadExchangerScript, null, dr =>
            {
                if ((decimal)dr["Amount"] == (decimal)dr["Filled"]) return;
                bool isBuyer = (long)dr["IsBuyer"] == 1;
                decimal price = (decimal)dr["Price"]; // dr["Price"] is decimal op ? (decimal)op : (decimal)								
                var o = new Order()
                {
                    Id = (long)dr["OrderId"], // BnnUtils.GetUnixNow(), // (long)dr["OrderId"],
                                              // CurrencyToSell = isBuyer ? _manager.QuoteCurrencyName : GetCurrencyName(),
                                              // CurrencyToBuy = isBuyer ? GetCurrencyName() : _manager.QuoteCurrencyName,

                    Price = price,
                    Amount = (decimal)dr["Amount"],
                    DateTime = (long)dr["Time"],
                    IsBuyer = isBuyer,
                    BorderPrice = isBuyer ? 0 : price, // 
                                                       // InitialQueue = _defaultInitialQueue,
                    TotalQuote = _manager.SpotAmount + price * (decimal)dr["Amount"]
                };

                if (AccountManager.IsTest)
                {
                    CurrencyAmount = 0; // CurrencyAmount - quantity;
                    _manager.SpotAmount = _manager.SpotAmount -= o.Amount * o.Price; //  _manager.Amount % (o.Amount * o.Price);
                };

                _orders.Add(o);
            });
        }

        protected void ChangeOrderData(long id, long newId, decimal amount, decimal price)
        {
            DB.ExecQuery(_manager.DbConnection, $"update exchanger set OrderId = {newId}, Amount = {amount}, Price = {price} where OrderId = {id}", null);
        }

        private long GetLastKline()
        {
            long result = -1;
            var klinesTable = AccountManager.Timeframe < 1 ? "candlestick1s" : "candlestick1m";
            var script = $"SELECT MAX(OpenTime) LastKline FROM {klinesTable} WHERE SymbolId = {SymbolId}";

            DB.OpenQuery(_manager.DbConnection, script, null, dr =>
            {
                result = dr["LastKline"] is DBNull ? -1 : (long)dr["LastKline"];
            });
            return result;
        }

        protected void UpdateFilledOrder(Order order)
        {
            DB.ExecQuery(_manager.DbConnection, $"UPDATE exchanger set Filled = Amount where OrderId = {order.Id};", null);
        }
        #endregion

        /// <summary>
        /// Candlesticks Routines
        /// </summary>
        /// <returns></returns>
        private long _lastSymbolKline = -1;

        private long ProcessOnePack() // async Task<long> Market market
        {
            if (_lastSymbolKline == -1) _lastSymbolKline = GetLastKline();
            var firstKlineTime = AccountManager.Timeframe < 1 ? 1703970000000 : 1577815200000; // 01.01.2024 for seconds / Tue Dec 31 2019 18:00:00 GMT+0000 for minutes
            var startTime = _lastSymbolKline == -1 ? firstKlineTime : _lastSymbolKline + (AccountManager.Timeframe < 1 ? 1 : 60) * 1000;
            var endTime = BnnUtils.GetUnixNow(); //  ((DateTimeOffset)DateTime.Now).ToUnixTimeMilliseconds();

            if (endTime < startTime) return endTime;
            var klines = _market.KlineCandlestickData(SymbolName, AccountManager.Timeframe < 1 ? Interval.ONE_SECOND : Interval.ONE_MINUTE, startTime, endTime, 1000).Result; // 1000

            dynamic? klinesInfo = JsonConvert.DeserializeObject(klines) ?? throw new Exception("KlineCandlestickData returned no data");
            if (klinesInfo.Count == 0) return endTime;
            var counter = 0;
            var klinesTable = AccountManager.Timeframe < 1 ? "candlestick1s" : "candlestick1m";
            var script = $"INSERT INTO {klinesTable} (SymbolId, OpenTime, OpenPrice, HighPrice, LowPrice, ClosePrice) VALUES "; // , Volume, QuoteVolume, Trades, TakerBaseVolume
            foreach (var k in klinesInfo)
            {
                if (k[0] <= _lastSymbolKline) continue;
                _lastSymbolKline = k[0];
                script += $"({SymbolId}, {_lastSymbolKline}, {k[1]}, {k[2]}, {k[3]}, {k[4]}), ";
                if ((AccountManager.LogLevel == 10) && (counter++ % 100 == 0)) Console.Write(".");
            }
            script = string.Concat(script.AsSpan(0, script.Length - 2), ";");
            DB.ExecQuery(_manager.DbConnection, script, null);
            return endTime;
        }

        private readonly Market _market = new();

        public long UpdateCandlesticks(int klinesCount = 1) // async Task<long> 
        {
            if (klinesCount == 0) klinesCount = 1;
            long endTime = 0;
            for (var i = 0; i < klinesCount; i++)
            {
                var t = ProcessOnePack();
                endTime = t;
            }
            if (AccountManager.LogLevel == 10) Console.WriteLine("ok");
            return endTime;
        }

        protected decimal _priceChangeThreshold;

        protected Order? CreateSpotLimitOrder(decimal price, bool isBuyer, bool isStopLoss = true, decimal amount = 0) //decimal amount, 
        {
            // if (isBuyer && ((amount == 0) || (amount * price > _manager.SpotAmount))) amount = _manager.SpotAmount / price;

            if (isBuyer && ((amount == 0) || (amount > _manager.SpotAmount))) amount = _manager.SpotAmount;
            price = FormatPrice(price);
            var quantity = isBuyer ? amount / price : CurrencyAmount;
            quantity = FormatQuantity(quantity);
            if (quantity < _lotStep) return null;

            long orderID;
            try
            {
                orderID = _manager.SpotLimitOrder(SymbolName, isBuyer ? Side.BUY : Side.SELL, quantity, price, isStopLoss ? _priceStep : 0);
                if (AccountManager.IsTest && isStopLoss)
                {
                    var ap = GetActualPrice();
                    if ((isBuyer && ap > price) || (!isBuyer && ap < price)) throw new BnnException(string.Empty, BnnException.StopPriceWouldTrigger);
                }
            }
            catch (BnnException ex)
            {
                if (ex.Code == BnnException.StopPriceWouldTrigger)
                {
                    var actualPrice = GetActualPrice();
                    var newPrice = isBuyer ? actualPrice + _priceChangeThreshold : actualPrice - _priceChangeThreshold;
                    Log($"create order: limit stop price would trigger immediately (calced price: {price}, current price: {CurrPrice}, actual price: {actualPrice}, new price: {newPrice})");
                    return CreateSpotLimitOrder(newPrice, isBuyer);
                }
                else throw new Exception(ex.Message);
            }

            var newOrder = new Order()
            {
                Id = orderID,
                Price = price,
                Amount = quantity,
                DateTime = BnnUtils.GetUnixNow(),
                IsBuyer = isBuyer,
                TotalQuote = amount,
                StopLossPrice = isStopLoss ? price : 0
            };

            if (isBuyer) _manager.SpotAmount -= newOrder.Amount * newOrder.Price;
            else CurrencyAmount -= quantity;

            if (AccountManager.IsTest) Log($"new spot order {newOrder}");
            else SaveNewOrder(newOrder);
            return newOrder;
        }

        protected Order? CreateFuturesLimitOrder(decimal price, bool isBuyer, decimal amount = 0) //decimal amount - in coints 
        {
            price = FormatPrice(price);
            if (!isBuyer && ((amount == 0) || (amount * price > _manager.FuturesAmount))) amount = _manager.FuturesAmount / price;
            var quantity = isBuyer ? PositionAmount : amount;
            // Log($"quantity: {quantity}, _lotStep: {_lotStep}");
            quantity = FormatQuantity(quantity);
            // Log($"quantity: {quantity}");

            if (quantity < _lotStep)
            {
                Log($"futures order: quantity ({quantity}) < lot step ({_lotStep})");
                return null;
            }

            long orderID;
            try
            {
                orderID = _manager.FuturesLimitOrder(SymbolName, isBuyer ? Side.BUY : Side.SELL, quantity, price);
            }
            catch (BnnException ex)
            {
                throw new Exception(ex.Message);
            }

            var newOrder = new Order()
            {
                Id = orderID,
                Price = price,
                Amount = quantity,
                DateTime = BnnUtils.GetUnixNow(),
                IsBuyer = isBuyer,
                TotalQuote = amount,
                StopLossPrice = 0,
                IsFutures = true
            };

            if (isBuyer) PositionAmount -= newOrder.Amount; //  _manager.FuturesAmount -= newOrder.Amount * newOrder.Price;
            else PositionAmount = quantity; // CurrencyAmount -= quantity;

            if (AccountManager.IsTest) Log($"new futures order {newOrder}");
            else SaveNewOrder(newOrder);
            return newOrder;
        }


        protected virtual void ChangeOrderPrice(Order order, decimal price)
        {
            if (order.Price == price) return;
            order.Price = price;

            if (order.IsBuyer && !order.IsFutures) // ) || (!order.IsBuyer && order.IsFutures)
            {
                var newQuantity = FormatQuantity(order.TotalQuote / order.Price);
                order.Amount = newQuantity;
                order.Filled = 0;
                //if (order.IsFutures) _manager.FuturesAmount = order.TotalQuote - order.Amount * order.Price;
                //else 
                _manager.SpotAmount = order.TotalQuote - order.Amount * order.Price;
            }
            if (AccountManager.IsTest) Log($"limit {(order.IsFutures ? "futures" : "spot")} order {order}"); //  (need to recalc qauntity!!!) : IsBuyer: {_order.IsBuyer}, curr price={(_order.IsBuyer ? bestBid : bestAsk)}, current stop: {_order.Price}, CurrPrice: {CurrPrice} stop-
        }

        protected void ExecuteOrder(Order order)
        {
            if (order.IsFutures)
            {
                if (order.IsBuyer) _manager.FuturesAmount -= order.Amount * order.Price * (1 - AccountManager.Fee / 2); //  PositionAmount -= order.Amount * (1 - AccountManager.Fee / 2); //  * order.Price
                else _manager.FuturesAmount += order.Amount * order.Price * (1 - AccountManager.Fee / 2);
                Log($"{order}: FILLED ({GetCurrencyName().ToLower()[0]} {PositionAmount:0.#######} {_manager.QuoteCurrencyName.ToLower()[0]} {_manager.FuturesAmount:0.#######})");
            }
            else
            {
                if (order.IsBuyer) CurrencyAmount += order.Amount * (1 - AccountManager.Fee / 2);
                else _manager.SpotAmount += order.Amount * order.Price * (1 - AccountManager.Fee / 2);
                Log($"{order}: FILLED ({GetCurrencyName().ToLower()[0]} {CurrencyAmount:0.#######} {_manager.QuoteCurrencyName.ToLower()[0]} {_manager.SpotAmount:0.#######})");
            }

            if (IsDealEntered)
            {
                _dealParams.TotalProfit = (double)_manager.SpotAmount;
                _manager.Statistics.TotalProfit = _dealParams.TotalProfit;
            }
        }

        protected void CancelOrder(Order order)
        {
            if (order.IsFutures)
            {
                if (order.IsBuyer) PositionAmount += order.Amount;
                else _manager.FuturesAmount -= order.Amount * order.Price;
            }
            else
            {
                if (order.IsBuyer) _manager.SpotAmount += order.Amount * order.Price;
                else CurrencyAmount += order.Amount;
            }
            order.Filled = order.Amount;
            Log($"canceled {order}");
        }

        protected decimal GetActualPrice()
        {
            if (IsBacktest()) return (decimal)CurrPrice;
            var priceAnswer = _market.SymbolPriceTicker(SymbolName).Result;
            dynamic? priceData = JsonConvert.DeserializeObject(priceAnswer.Trim()) ?? throw new Exception("aggTrade returned no data");
            return priceData.price;
        }

        /// <summary>
        /// BL Routines
        /// </summary>
        /// <returns></returns>

        private bool _isLong = false;
        public bool IsDealEntered
        {
            get { return _isLong; }
            set
            {
                _isLong = value;
                if (value) _extrPrice = -1;
                else _extrPrice = decimal.MaxValue;

            }
        }

        protected abstract decimal GetLongValue(List<Kline> klines, decimal previousValue = -1);

        protected abstract double GetShortValue(decimal longPrice, out decimal stopLossValue);

        protected string _lastKlineInfo = string.Empty;

        private decimal GetLongValue(decimal previousValue = -1)
        {
            if (!Active) return previousValue;
            UpdateCandlesticks(50);
            var endTime = ((DateTimeOffset)DateTime.Now).ToUnixTimeMilliseconds();
            var startTime = endTime - AccountManager.FindRangeMinsCount() * 60L * 1000;
            var klines = LoadKlinesFromDB(startTime, endTime);
            if (klines.Count < 70) throw new Exception("Too little archive klines");

            return GetLongValue(klines, previousValue);
        }

        protected virtual double GetShortValue(List<Kline> klines, double previousValue = -1)
        {
            return previousValue;
        }

        private bool CheckForLong(decimal currPrice)
        {
            if (_isLimit) return false;

            // new minimum
            if ((CurrPrice > -1) && (currPrice < _extrPrice) && (currPrice < _longPrice))
            {
                if (_extrPrice > _longPrice)
                {
                    if (_lastKlineInfo != string.Empty) BnnUtils.Log(_lastKlineInfo);
                    _manager.DeactivateAll(this);
                    Console.Beep();
                }
                BnnUtils.Log($"{DateTime.Now:dd.MM.yyyy HH:mm:ss} {SymbolName} {currPrice} new min");
                _extrPrice = currPrice;
            }

            // deal
            if ((currPrice >= _extrPrice + _confirmExtrVal) && (currPrice < _longPrice + _confirmExtrVal))
            {
                _manager.Order(this, Side.BUY, (decimal)_manager.SpotAmount);
                EnterLong((decimal)currPrice);
                Console.Beep();
                _manager.CheckBalance();

                // if (!AccountManager.IsTest) 
                SaveLongPrice(currPrice);
            }

            // update long price 
            if (!_isLong && (_extrPrice > _longPrice) && (DateTime.Now.Subtract(_lastLongCheckTime).TotalMinutes >= _checkDealPriceInterval)) // 7
            {
                // Console.WriteLine($"// update long price --- {_isLong} --- {LongPrice} --- {_extrPrice}");				
                _longPrice = GetLongValue(_longPrice); // 
                _lastLongCheckTime = DateTime.Now;
            }

            return _isLong;
        }

        private bool CheckForShort(decimal currPrice)
        {
            if (_isLimit) return false;

            // new maximum
            if ((currPrice > _extrPrice) && ((decimal)currPrice > _shortPrice))
            {
                if ((decimal)_extrPrice < _shortPrice)
                {
                    Console.Beep();
                    BnnUtils.Log($"{DateTime.Now:dd.MM.yyyy HH:mm:ss} {currPrice} max");
                }
                _extrPrice = currPrice;
            }

            // deal
            if ((currPrice <= _extrPrice - _confirmExtrVal) && ((decimal)currPrice > _shortPrice - (decimal)_confirmExtrVal))
            {
                EnterShort(currPrice, false);
            }

            return !_isLong;
        }

        private void CheckForStopLoss(decimal currPrice)
        {
            if ((decimal)currPrice < _stopLossPrice) EnterShort(currPrice, true);
        }

        private bool _isBacktest = false;

        protected bool IsBacktest()
        {
            return _isBacktest;
        }

        private void EnterLong(decimal price, bool recalcShort = true)
        {
            _isLong = true;
            _longPrice = price;
            _confirmExtrVal = (decimal)_dealParams.ConfirmExtrPart * price; //  recalc if program restarted
            if (recalcShort) _shortPrice = (decimal)GetShortValue(RealPrice, out _stopLossPrice);
            if ((_lastKlineInfo != string.Empty) && IsBacktest()) BnnUtils.Log(_lastKlineInfo);

            var currTime = $"{DateTime.Now:dd.MM.yyyy HH:mm:ss}";
            if (BacktestTime > 0) currTime = $"{BnnUtils.FormatUnixTime(BacktestTime)}";
            // --- always out this information
            if (AccountManager.LogLevel >= 5) BnnUtils.Log($"{currTime} {SymbolName} {price:0.###} long aim {_shortPrice:0.###} sl {_stopLossPrice:0.###}", false);
            _extrPrice = -1;
        }

        public decimal FormatQuantity(decimal availableSum)
        {
            // var log10 = Math.Log10(price);
            var log10 = Math.Log10((double)_lotStep);
            double power = Math.Pow(10, -log10); // 5 --- 			
            return (decimal)(Math.Floor((double)availableSum * power) / power); // to prevent LOT_SIZE error
        }

        public decimal FormatPrice(decimal price)
        {
            var digitsCount = (int)Math.Log10((double)_priceStep);
            var power = (decimal)Math.Pow(10, -digitsCount); // 5 --- 				
            return Math.Floor(price * power) / power; // to prevent LOT_SIZE error						
        }

        private void EnterShort(decimal price, bool sl)
        {
            var log10 = Math.Log10((double)price);
            log10 = Math.Floor(log10) + 1;
            var power = (decimal)Math.Pow(10, log10); // 5 --- 
            CurrencyAmount = FormatQuantity(CurrencyAmount); // Math.Floor(CurrencyAmount * power) / power; // to prevent LOT_SIZE error

            _manager.Order(this, Side.SELL, CurrencyAmount);
            Console.Beep();
            _isLong = false;
            if (AccountManager.LogLevel >= 5) BnnUtils.Log($"{price:0.###} {(sl ? "stoploss" : "short")} => {_manager.SpotAmount}\n\r");

            if (AccountManager.IsTest)
            {
                _manager.SpotAmount = CurrencyAmount * (decimal)price;
                CurrencyAmount = 0;
                SaveShortPrice(price);
            }
            else
            {
                SaveShortPrice(price);
                _manager.CheckBalance();
            }

            _active = false;
            _manager.ActivateAll();
        }


        private DateTime _lastLongCheckTime = DateTime.Now;

        public long BacktestTime { get; set; } = -1; // private 

        public virtual void Prepare()
        {
            Log(string.Empty);
            Log($"{GetName()} / {SymbolName}: {_dealParams.GetParamsDesciption()}");
            //BnnUtils.Log(string.Empty);
            //BnnUtils.Log($"{GetName()} / {SymbolName}: {_dealParams.GetParamsDesciption()}");
            // if ((_manager.UsdtAmount < 1) && (CurrencyAmount < (decimal)0.00001)) throw new Exception("Balance is too small");
            // _isLong = File.Exists(_longFile);

            _longPrice = LoadLongPrice();
            _isLong = _longPrice > 0;
            if (_isLong) EnterLong(_longPrice, false); // LoadLongPrice()
            else _longPrice = GetLongValue();
        }

        public virtual async void Start()
        {
            var ws = new MarketDataWebSocket($"{SymbolName.ToLower()}@aggTrade");
            ws.OnMessageReceived(data =>
            {
                if (!Active) return Task.CompletedTask;

                dynamic? tradeData = JsonConvert.DeserializeObject(data.Trim()) ?? throw new Exception("aggTrade returned no data");
                CurrPrice = tradeData.p;

                try
                {
                    if (_isLong)
                    {
                        CheckForShort(CurrPrice);
                        CheckForStopLoss(CurrPrice);
                    }
                    else CheckForLong(CurrPrice);
                }
                catch (Exception ex)
                {
                    BnnUtils.Log(ex.Message);
                }
                return Task.CompletedTask;
            }, CancellationToken.None);

            await ws.ConnectAsync(CancellationToken.None);
        }

        protected bool _isLimit = false;

        private decimal CheckKlineForLong(Kline currKline)
        {
            if (_isLimit)
            {
                if (currKline.HighPrice > _longPrice) return _longPrice;  // (currKline.LowPrice < LongPrice) && (
                else return -1;
            }

            // current price exceeds previous minimum - deal
            if ((_extrPrice != decimal.MaxValue) && (currKline.LowPrice > (decimal)_extrPrice) && (currKline.HighPrice - _extrPrice > _confirmExtrVal)) return _extrPrice + _confirmExtrVal;

            // do not reach threshold
            if (_longPrice - currKline.LowPrice < 0) return -1; // with _confirmExtrVal value can be less than minimum

            // jump inside red kline body
            var fiftyPercentChance = ((currKline.OpenTime / 1000 / 60) % 2 == 0) && (currKline.ClosePrice < currKline.OpenPrice);
            var thresOpenMin = Math.Min((decimal)currKline.OpenPrice, _longPrice);
            if (((decimal)thresOpenMin - currKline.LowPrice > 1.1M * (decimal)_confirmExtrVal) && fiftyPercentChance) // 1.5 * 
            {
                if (AccountManager.OutLog) Console.WriteLine($"{BnnUtils.FormatUnixTime(currKline.OpenTime)} random jump inside a kline [ {currKline.HighPrice} - {currKline.LowPrice} ]");
                var avgJumpPrice = thresOpenMin - (thresOpenMin - currKline.LowPrice) / 2 + (decimal)_confirmExtrVal; // ClosePrice
                return Math.Min(avgJumpPrice, _extrPrice);
            }

            // top shade from open price more than threshold
            if (_extrPrice > currKline.OpenPrice) _extrPrice = currKline.OpenPrice;
            if ((currKline.OpenPrice > currKline.ClosePrice) && (_longPrice - _extrPrice > _confirmExtrVal) && (currKline.HighPrice - _extrPrice > _confirmExtrVal)) return _extrPrice + _confirmExtrVal;

            // hit low price
            if ((decimal)_extrPrice > currKline.LowPrice) _extrPrice = currKline.LowPrice;

            // bottom shadow more than theshold
            if (Math.Min(currKline.OpenPrice, currKline.ClosePrice) - _extrPrice > _confirmExtrVal) return _extrPrice + _confirmExtrVal;

            // green kline
            if ((currKline.OpenPrice < currKline.ClosePrice) && (currKline.HighPrice - _extrPrice > _confirmExtrVal)) return _extrPrice + _confirmExtrVal;

            return -1;
        }

        private decimal CheckKlineForShort(Kline currKline)
        {
            if (_isLimit)
            {
                // Console.WriteLine($"{BnnUtils.FormatUnixTime(currKline.OpenTime)} CheckKlineForShort: LowPrice={currKline.LowPrice}; ShortPrice={ShortPrice}");
                if ((decimal)currKline.LowPrice < _shortPrice) return _shortPrice; // ) && (ShortPrice < currKline.HighPrice)
                else return -1;
            }

            // current price do not exceeds previous maximum
            if ((_extrPrice != 0) && ((decimal)_extrPrice > currKline.HighPrice) && (_extrPrice - currKline.HighPrice > _confirmExtrVal)) return _extrPrice - _confirmExtrVal;

            // do not reach threshold
            if ((decimal)currKline.HighPrice - _shortPrice < (decimal)_confirmExtrVal) return -1;

            // jump inside green body
            var fiftyPercentChance = ((currKline.OpenTime / 1000 / 60) % 2 == 0) && (currKline.ClosePrice > currKline.OpenPrice);
            var openHighMax = Math.Max(currKline.OpenPrice, _shortPrice + _confirmExtrVal);
            if ((currKline.HighPrice - openHighMax > 1.1M * _confirmExtrVal) && fiftyPercentChance) // 1.5 * --- ClosePrice
            {
                if (AccountManager.OutLog) Console.WriteLine($"{BnnUtils.FormatUnixTime(currKline.OpenTime)} random jump inside kline [ {currKline.LowPrice} - {currKline.HighPrice} ]");
                var avgJumpPrice = openHighMax + (currKline.HighPrice - openHighMax) / 2 - _confirmExtrVal;
                return Math.Max(avgJumpPrice, _extrPrice);
            }

            // bottom shadow from open price more than threshold
            if (currKline.OpenPrice > _extrPrice) _extrPrice = currKline.OpenPrice;
            if ((currKline.OpenPrice < currKline.ClosePrice) && ((decimal)_extrPrice - _shortPrice > (decimal)_confirmExtrVal) && (_extrPrice - currKline.LowPrice > _confirmExtrVal)) return _extrPrice - _confirmExtrVal;

            // hit low price
            if (currKline.HighPrice > (decimal)_extrPrice) _extrPrice = currKline.HighPrice;

            // top shadow more than theshold
            if (_extrPrice - Math.Max(currKline.OpenPrice, currKline.ClosePrice) > _confirmExtrVal) return _extrPrice - _confirmExtrVal;

            // red kline
            if ((currKline.OpenPrice > currKline.ClosePrice) && (_extrPrice - currKline.LowPrice > _confirmExtrVal)) return _extrPrice - _confirmExtrVal;

            return -1;
        }

        protected abstract void SaveStatistics(List<BaseDealParams> tradeResults);

        // public static readonly double  Slippage = 0.000065; // market price 0.99993541; // 0.99987;
        // public static readonly double Slippage = 0.00000; //  limit stop sometimes reset
        // public static readonly double Slippage = 0.000075; //  for limit
        // public static readonly double Slippage = 0.00033; //  for limit
        public static decimal Slippage { get; set; } // = 0.00055; //  for limit

        public virtual void PrepareBacktest(List<Kline>? klines = null)
        {
            _isBacktest = true;
            if (AccountManager.OutLog) Console.WriteLine($"{SymbolName}  {_dealParams.GetParamsDesciption()}");
            var startTime = _manager.StartTime - AccountManager.FindRangeMinsCount() * 60L * 1000; //  * AccountManager.Timeframe
            _klines ??= LoadKlinesFromDB(startTime, _manager.EndTime);
            if (AccountManager.FindRangeKlinesCount() > _klines.Count) throw new Exception($"Too little klines for a period ({_klines.Count} < {AccountManager.FindRangeMinsCount()})"); //  && (AccountManager.Timeframe >= 1)
            InitialParams.CopyFrom(_dealParams);
            InitBacktestLong(AccountManager.FindRangeKlinesCount());
        }

        public virtual void InitBacktestLong(int klineIndex)
        {
            _extrPrice = decimal.MaxValue;
            _isLong = false;
            CurrPrice = -1;
            if (_klines == null) return;
            _longPrice = GetLongValue(_klines[(klineIndex - AccountManager.FindRangeKlinesCount())..klineIndex], -1); // 
        }

        protected void Log(string message)
        {
            if (IsBacktest())
            {
                if (AccountManager.LogLevel == 0) return;
                if (message == string.Empty) BnnUtils.Log(message, false);
                else BnnUtils.Log($"{BnnUtils.FormatUnixTime(BacktestTime)} {SymbolName} {FormatPrice(CurrPrice)}: {message}", false); //  {FormatPrice(CurrPrice)}
            }
            else
            {
                if (message == string.Empty) BnnUtils.Log(message, message != string.Empty);
                else BnnUtils.Log($"{FormatPrice(CurrPrice)}: {message}");
            }
        }

        public void ProcessBacktestKline(int klineIndex)
        {
            // Log($"ProcessBacktestKline - Base - {_klines}");
            if ((_klines == null) || (klineIndex >= _klines.Count) || (klineIndex < 0)) return;
            BacktestTime = _klines[klineIndex].OpenTime;
            CurrPrice = ((_klines[klineIndex].OpenPrice + _klines[klineIndex].ClosePrice) / 2);
            var firstRangeKline = klineIndex - AccountManager.FindRangeKlinesCount(); // minutes - safety trade

            if (IsDealEntered)
            {
                if ((klineIndex % _checkDealPriceInterval == 0) && (_extrPrice < _shortPrice)) _shortPrice = (decimal)GetShortValue(_klines[firstRangeKline..klineIndex], (double)_shortPrice); // 
                if (_isLimit) return;
                var dealValue = CheckKlineForShort(_klines[klineIndex]) * (1 - Slippage);
                if ((dealValue > 0) || (_klines[klineIndex].LowPrice < _stopLossPrice))
                {
                    var isStopLoss = _klines[klineIndex].LowPrice < _stopLossPrice;
                    CurrPrice = (isStopLoss ? _stopLossPrice * (1 - Slippage) : dealValue);
                    if (_isLimit) CurrPrice = _shortPrice;
                    _isLong = false;

                    _manager.Statistics.TotalProfit = _manager.Statistics.TotalProfit * (double)CurrPrice / (double)_longPrice * (1 - (double)AccountManager.Fee) * (1 - (double)AccountManager.Fee);
                    _dealParams.TotalProfit = _manager.Statistics.TotalProfit;
                    if ((AccountManager.LogLevel >= 5) && !_isLimit) BnnUtils.Log($"{BnnUtils.FormatUnixTime(_klines[klineIndex].OpenTime)} {CurrPrice:0.###} {(isStopLoss ? "stoploss" : "short")} => {_manager.Statistics.TotalProfit}\n\r", false);
                    _extrPrice = decimal.MaxValue;
                    _longPrice = GetLongValue(_klines[firstRangeKline..klineIndex], -1); // 

                    if (isStopLoss) _manager.Statistics.StopLossCount++;
                    else _manager.Statistics.DealCount++;
                }
            }
            else
            {
                // Log($"ProcessBacktestKline - IsLong - {BacktestTime}");
                if ((klineIndex % _checkDealPriceInterval == 0) && (_extrPrice > _longPrice)) _longPrice = GetLongValue(_klines[firstRangeKline..klineIndex], _longPrice); // 

                if (_isLimit) return;
                var longDealValue = CheckKlineForLong(_klines[klineIndex]) * (1 + Slippage);
                if (longDealValue > 0)
                {
                    CurrPrice = longDealValue;
                    RealPrice = longDealValue;
                    _longPrice = longDealValue;
                    EnterLong(CurrPrice);
                }
                else
                {
                    // if (AccountManager.OutLog) BnnUtils.Log($"{BnnUtils.FormatUnixTime(_klines[klineIndex].OpenTime)} 1 - {LongPrice:0.###}", false);					
                }
            }
        }

        protected DealParams InitialParams = new DealParams(0.011, 0, 0.019, 0);

        protected virtual List<BaseDealParams> CalculateParams(List<Kline> klines)
        {
            /* int[] _someValueArr = [0]; // , 7, 9 --- 1, 2, 3, 5, 9 --- , 1, 2, 3, 5
			int[] _checkRangeIntervalArr = [1]; // 1, , 30, 300, 720

			double[] _profitsArr = [0.001, 0.003, 0.005, 0.009]; //  ... , 0.04, 0.055, 0.07
			double[] _stopLossesArr = [0.035, 0.045, 0.055];
			double[] _thresholdsArr = [-0.95, -0.75, -0.55]; // ...  , 0.15 ... 0.21, 0.45, 0.75, 1.0

			double[] _confirmExtrParts = [0.00001];*/

            /* double[] _profitsArr = [0.003, 0.007, 0.011, 0.015, 0.021, 0.029, 0.04, 0.055, 0.07, 0.11];
			double[] _stopLossesArr = [0.001, 0.01, 0.019, 0.045, 0.07];
			double[] _thresholdsArr = [-1.0, -0.75, -0.45, -0.21, -0.15, -0.1, -0.05, -0.03, 0, 0.03, 0.05, 0.1, 0.15, 0.21, 0.45, 0.75, 1.0];
			double[] _confirmExtrParts = [0, 0.00001, 0.0001, 0.0003, 0.0009, 0.0015, 0.003]; 
			double[] _confirmExtrParts = [0.00001];

			double[] _profitsArr = [0.007, 0.011, 0.015, 0.021, 0.029]; //  ... , 0.04, 0.055, 0.07
			double[] _stopLossesArr = [0.019, 0.045, 0.07];
			double[] _thresholdsArr = [-1.5, -1.0, -0.75, -0.45, -0.21, 0, 0.21]; // ...  , 0.15 ... 0.21, 0.45, 0.75, 1.0 

			double[] _profitsArr = [0.028, 0.029]; // 0.029
			double[] _stopLossesArr = [0.041, 0.045]; // 0.045
			double[] _thresholdsArr = [-0.22, -0.21]; // -0.21
			double[] _confirmExtrParts = [0.00001];

			double[] _profitsArr = [0.028]; // , 0.04, 0.055, 0.07
			double[] _stopLossesArr = [0.040, 0.041, 0.042, 0.043, 0.045];
			double[] _stopLossesArr = [0.041];
			double[] _thresholdsArr = [-0.24, -0.23, -0.22, -0.21, -0.20]; // 
			double[] _thresholdsArr = [-0.05, 0, 0.05]; // 
			double[] _confirmExtrParts = [0.00001];*/

            var deals = new List<BaseDealParams>();
            /* long paramsSetCount = _profitsArr.Length * _stopLossesArr.Length * _thresholdsArr.Length * _confirmExtrParts.Length * _checkRangeIntervalArr.Length * _someValueArr.Length;
            long counter = 0;
            foreach (var sv in _someValueArr)
            {
                _someValue = sv;
                foreach (var ri in _checkRangeIntervalArr)
                {
                    _checkDealPriceInterval = ri;
                    foreach (var pr in _profitsArr)
                    {
                        foreach (var sl in _stopLossesArr)
                        {
                            foreach (var th in _thresholdsArr)
                            {
                                foreach (var ce in _confirmExtrParts)
                                {
                                    var dp = new DealParams(pr, th, sl, ce);
                                    dp.MaxDealInterval = (long)(_someValue); // Math.Truncate
                                    _dealParams = dp;
                                    BackTest(klines);
                                    _dealParams.CopyFrom(InitialParams);
                                    deals.Add(dp);

                                    counter++;
                                    BnnUtils.ClearCurrentConsoleLine();
                                    Console.Write($"{counter * 100.0 / paramsSetCount:0.##}%"); // IsAdaptive --- OutLog
                                }
                            }
                        }
                    }
                }
            }*/
            return deals;
        }

        public virtual string GetCurrentInfo()
        {
            return $"{SymbolName.ToUpper()} {CurrPrice}";
        }

        public void FindBestParams()
        {
            Console.WriteLine($"TA {BnnUtils.FormatUnixTime(_manager.StartTime)} - {BnnUtils.FormatUnixTime(_manager.EndTime)}");
            // _outLog = false;
            var klines = LoadKlinesFromDB(_manager.StartTime - AccountManager.FindRangeMinsCount() * 60L * 1000, _manager.EndTime);
            var deals = CalculateParams(klines);

            List<BaseDealParams> sortedParams = [.. deals.OrderByDescending(d => d.TotalProfit)];
            Console.WriteLine("\r\n");
            foreach (var p in sortedParams)
            {
                Console.WriteLine(p);
            };

            if (_saveStatistic) SaveStatistics(sortedParams);
        }
    }
}