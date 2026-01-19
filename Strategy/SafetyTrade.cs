// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Numerics;
using Binance.Spot;
using Binance.Spot.Models;
using DbSpace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bnncmd.Strategy
{
    public enum SafetyTradeState
    {
        Waiting,
        LowLevel,
        UpLevel
    }

    internal class SafetyTradeParams : EmaDealParams
    {
        public int EmaHourLength { get; set; }
        public double ShiftDownOffset { get; set; }
        public double ShiftUpOffset { get; set; }
        public double ShiftDownCoef { get; set; }
        public double ShiftUpCoef { get; set; }

        public SafetyTradeParams(int emaMinsLength, int emaHourLength, double shiftDownOffset, double shiftUpOffset, double shiftDownCoef, double shiftUpCoef) : base(emaMinsLength, 1, shiftDownOffset, shiftUpOffset)
        {
            EmaHourLength = emaHourLength;
            ShiftDownOffset = shiftDownOffset;
            ShiftUpOffset = shiftUpOffset;
            ShiftDownCoef = shiftDownCoef;
            ShiftUpCoef = shiftUpCoef;
        }

        public override string ToString()
        {
            var results = $"{TotalProfit:0.##}\t\t{DealCount}";
            var conditions = $"emi\t{EmaLength}\temih\t{EmaHourLength}\tsdo\t{ShiftDownOffset:0.###}\tsuo\t{ShiftUpOffset:0.###}\tsdc\t{ShiftDownCoef:0.###}\tsuc\t{ShiftUpCoef:0.###}";
            return results + "\t" + conditions;
        }

        public override string GetParamsDesciption()
        {
            return $"eml {EmaLength}   emlh {EmaHourLength}   sdo {ShiftDownOffset}   suo {ShiftUpOffset}   sdc {ShiftDownCoef}   suc {ShiftUpCoef}";
        }
    }


    internal class SafetyTrade : EMA
    {
        #region VariablesAndConstructor
        public SafetyTrade(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            EmaLength = AccountManager.Config.GetValue<int>("Strategies:SafetyTrade:Length");
            _emaLengthHour = AccountManager.Config.GetValue<int>("Strategies:SafetyTrade:HourLength");
            _shiftDownOffset = AccountManager.Config.GetValue<double>("Strategies:SafetyTrade:ShiftDownOffset");
            _shiftUpOffset = AccountManager.Config.GetValue<double>("Strategies:SafetyTrade:ShiftUpOffset");
            _shiftDownCoef = AccountManager.Config.GetValue<double>("Strategies:SafetyTrade:ShiftDownCoef");
            _shiftUpCoef = AccountManager.Config.GetValue<double>("Strategies:SafetyTrade:ShiftUpCoef");
            // _alpha = 2.0M / (_emaLength + 1);
            _alphaHour = 2.0 / (_emaLengthHour + 1);

            BnnUtils.Log($"{GetName()}", false); //  {SymbolName} {_emaLength}
            _dealParams = new SafetyTradeParams(EmaLength, _emaLengthHour, _shiftDownOffset, _shiftUpOffset, _shiftDownCoef, _shiftUpCoef);
            _isLimit = true;
        }

        public override string GetName() { return $"Safety Trade - {SymbolName}"; }

        private double _shiftUp = 0.51;
        private double _shiftDown = 0.64;
        private double _shiftDownOffset = 0.767;
        private double _shiftUpOffset = 0.572;
        private double _shiftDownCoef = -0.247;
        private double _shiftUpCoef = 0.088;

        private static int _emaLengthHour = 72; // 72;
        protected double _alphaHour = 2.0 / (_emaLengthHour + 1);
        private double _previousHoursEMA;
        private double _trendHourAngle;

        private SafetyTradeState _status = SafetyTradeState.Waiting;
        #endregion

        #region BackTest
        private void InitBacktest(List<Kline> klines)
        {
            CurrencyAmount = 0;
            _manager.CheckBalance();
            _rangeKlines = klines;
            _previousCloseEMA = (decimal)CalcEMA(EmaLength, klines.Count - 1);
            // CalcStopsFromOrderBook();

            CalcTrendFromArchive(klines);
            CalcShifts();
            // if (AccountManager.LogLevel > 9) Log($"InitBacktest - _previousHoursEMA: {_previousHoursEMA}, penultHoursEMA: {penultHoursEMA}, _trendHourAngle: {_trendHourAngle}");

            _backtestInited = true;
        }

        private void CalcTrendFromArchive(List<Kline> klines, int klineIndex = -1) // 
        {
            var firstInd = 0;
            var lastInd = klines.Count;
            if (klineIndex > -1)
            {
                firstInd = klineIndex - EmaLength;
                lastInd = klineIndex;
            }
            _previousHoursEMA = (double)klines[0].ClosePrice;
            var penultHoursEMA = _previousHoursEMA;
            for (var i = firstInd; i < lastInd; i++)
            {
                var kline = klines[i];
                if ((kline.OpenTime / 1000 / 60) % 60 == 59)
                {
                    penultHoursEMA = _previousHoursEMA;
                    _previousHoursEMA = ((double)kline.ClosePrice * _alphaHour + (1 - _alphaHour) * _previousHoursEMA);
                    // Log($"    OpenTime: {BnnUtils.FormatUnixTime(kline.OpenTime)}, _previousHoursEMA: {_previousHoursEMA}, penultHoursEMA: {penultHoursEMA}, kline.ClosePrice: {kline.ClosePrice}, length: {_emaLengthHour}, alpha: {_alphaHour}");
                }
            }
            _trendHourAngle = (_previousHoursEMA - penultHoursEMA) / penultHoursEMA * 100;
        }


        private void CalcShifts()
        {
            _shiftDown = _shiftDownOffset + _shiftDownCoef * (24 * _trendHourAngle); // / 100; // 
            _shiftUp = _shiftUpOffset + _shiftUpCoef * (24 * _trendHourAngle); // 24 *
            if (AccountManager.LogLevel > 8) Log($"last hour EMA{_emaLengthHour}: {_previousHoursEMA:0.###}, trend angle: {_trendHourAngle:0.###}, shift down: {_shiftDown:0.###}, shift up: {_shiftUp:0.###}");
        }


        private void ProcessBacktestKline(List<Kline> klines)
        {
            var lastKline = klines[^1];
            BacktestTime = lastKline.OpenTime;
            CurrPrice = (decimal)lastKline.ClosePrice;

            if (_backtestInited)
            {
                _previousCloseEMA = CurrPrice * _alpha + (1 - _alpha) * _previousCloseEMA;
                // Log($"ProcessBacktestKline - OpenTime: {(lastKline.OpenTime / 1000 / 60) % 60}");
                if ((lastKline.OpenTime / 1000 / 60) % 60 == 59)
                {
                    var newHoursEMA = (double)lastKline.ClosePrice * _alphaHour + (1 - _alphaHour) * _previousHoursEMA;
                    _trendHourAngle = (newHoursEMA - _previousHoursEMA) / _previousHoursEMA * 100;
                    _previousHoursEMA = newHoursEMA;
                    CalcShifts();
                    // if (AccountManager.LogLevel > 9) Log($"ProcessBacktestKline - _previousHoursEMA: {_previousHoursEMA}, newHoursEMA: {newHoursEMA}, _trendHourAngle: {_trendHourAngle}, lastKline.ClosePrice: {lastKline.ClosePrice}");
                }
            }
            else InitBacktest(klines);

            var upLevel = (double)_previousCloseEMA * (1 + _shiftUp / 100);
            var downLevel = (double)_previousCloseEMA * (1 - _shiftDown / 100);

            if (IsDealEntered && (lastKline.HighPrice >= (decimal)upLevel)) _status = SafetyTradeState.UpLevel;
            if (!IsDealEntered && (lastKline.LowPrice <= (decimal)downLevel)) _status = SafetyTradeState.LowLevel;

            if ((_status == SafetyTradeState.LowLevel) && ((double)lastKline.ClosePrice > downLevel)) EnterLong(downLevel, upLevel);
            if ((_status == SafetyTradeState.UpLevel) && ((double)lastKline.ClosePrice < upLevel)) EnterShort(downLevel, upLevel);

            if (AccountManager.LogLevel > 9) Log($"[{downLevel:0.###} - {upLevel:0.###}] {_status}"); // ) || (_status != SafetyTradeState.Waiting)
        }


        private void EnterLong(double downLevel, double upLevel)
        {
            Log($"enter long * [{downLevel:0.###} - {upLevel:0.###}]");
            IsDealEntered = true;
            _longPrice = CurrPrice;
            _status = SafetyTradeState.Waiting;
        }


        private void EnterShort(double downLevel, double upLevel)
        {
            Log($"enter short [{downLevel:0.###} - {upLevel:0.###}] *");
            IsDealEntered = false;
            _status = SafetyTradeState.Waiting;

            var balance = (decimal)_manager.Statistics.TotalProfit;
            var newBalance = CurrPrice / _longPrice * balance * (1 - Slippage) * (1 - Slippage);
            Log($"{CurrPrice} / {_longPrice} * {balance:0.###} * {1 - Slippage}^2 = {newBalance:0.###}");
            _dealParams.TotalProfit = (double)newBalance;
            _manager.Statistics.DealCount++;
            _dealParams.DealCount = _manager.Statistics.DealCount;
            _manager.Statistics.TotalProfit = _dealParams.TotalProfit;
            Log(string.Empty);
        }


        public override void InitBacktestLong(int klineIndex)
        {
            _status = SafetyTradeState.Waiting;
            if ((_klines != null) && (klineIndex > 0) && (klineIndex < _klines.Count))
            {
                // _rangeKlines = _klines;
                // _previousCloseEMA = CalcEMA(_emaLength, klineIndex);
                CalcTrendFromArchive(_klines, klineIndex);
            }
        }


        public override string GetCurrentInfo()
        {
            return $"GetCurrentInfo";
        }


        protected override double GetShortValue(decimal longPrice, out decimal stopLossValue)
        {
            stopLossValue = 0;
            return double.MaxValue;
        }


        protected override decimal GetLongValue(List<Kline> klines, decimal previousValue = -1)
        {
            ProcessBacktestKline(klines);
            return _longPrice; //  double.MaxValue;
            // return 0;
        }


        protected override double GetShortValue(List<Kline> klines, double previousValue = -1)
        {
            ProcessBacktestKline(klines);
            return (double)_shortPrice; //  1000000.0;
            //return double.MaxValue;
        }


        protected override void SaveStatistics(List<BaseDealParams> tradeResults)
        {
            var script = "INSERT INTO backtest (BeginDate, EndDate, SymbolId, Param1, Param2, Param3, Param4, Param5, IsBull, IsBear, Balance, Strategy) VALUES ";
            foreach (var tr in tradeResults)
            {
                var dp = (SafetyTradeParams)tr;
                script += $"({_manager.StartTime}, {_manager.EndTime}, {SymbolId}, {dp.EmaLength}, {dp.ShiftDownOffset}, {dp.ShiftUpOffset}, {dp.ShiftDownCoef}, {dp.ShiftUpCoef}, 6, {dp.EmaHourLength}, {dp.TotalProfit}, 's'), ";
            };
            script = string.Concat(script.AsSpan(0, script.Length - 2), ";");
            Console.WriteLine(script);
            DB.ExecQuery(_manager.DbConnection, script, null);
        }


        protected override List<BaseDealParams> CalculateParams(List<Kline> klines)
        {
            /* int[] emaIntervals = [200, 220, 240];
            double[] shiftUps = [0.01, 0.012, 0.015];
            double[] shiftDowns = [0.019, 0.021, 0.025];*/

            /* int[] emaIntervals = [150, 200, 250, 300, 450];
            double[] shiftUps = [0.8, 1, 1.1, 1.4, 1.7];
            double[] shiftDowns = [0.11, 0.35, 4.1, 4.5, 5.5];*/

            /* int[] emaIntervals = [3, 5, 9, 15, 30];
            double[] shiftUps = [0.9, 1.2, 1.5, 2.3, 3.5];
            double[] shiftDowns = [0.05, 0.15, 0.3, 0.5, 0.7];*/

            /* int[] emaIntervals = [7, 250, 1350];
            int[] emaHourIntervals = [7, 72, 288];
            double[] downOffsets = [0.5, 0.767, 1];
            double[] upOffsets = [0.3, 0.572, 0.8];
            double[] downCoefs = [-0.5, -0.247, -0.1];
            double[] upCoefs = [0.05, 0.088, 0.12];

            int[] emaIntervals = [900, 1300, 1700];
            int[] emaHourIntervals = [100, 300, 500];
            double[] downOffsets = [0.9, 1.1, 1.3];
            double[] upOffsets = [0.2, 0.5, 0.9];
            double[] downCoefs = [-0.3, -0.15, 0];
            double[] upCoefs = [0.03, 0.09, 0.15];*/

            int[] emaIntervals = [1500, 1800, 2300];
            int[] emaHourIntervals = [90, 350, 700];
            double[] downOffsets = [1.0, 1.4, 1.7];
            double[] upOffsets = [0.1, 0.6, 1.1];
            double[] downCoefs = [-0.45, -0.25, -0.1];
            double[] upCoefs = [0.01, 0.05, 0.13];

            var deals = new List<BaseDealParams>();
            long paramsSetCount = emaIntervals.Length * emaHourIntervals.Length * upOffsets.Length * downOffsets.Length * upCoefs.Length * downCoefs.Length;
            long counter = 0;
            foreach (var el in emaIntervals)
            {
                EmaLength = el;
                // _alpha = 2.0M / (_emaLength + 1);
                foreach (var ehl in emaHourIntervals)
                {
                    _emaLengthHour = ehl;
                    _alphaHour = 2.0 / (_emaLengthHour + 1);
                    foreach (var uo in upOffsets)
                    {
                        _shiftUpOffset = uo;
                        foreach (var uc in upCoefs)
                        {
                            _shiftUpCoef = uc;
                            foreach (var dno in downOffsets)
                            {
                                _shiftDownOffset = dno;
                                foreach (var dc in downCoefs)
                                {
                                    _shiftDownCoef = dc;
                                    var dp = new SafetyTradeParams(el, ehl, dno, uo, dc, uc);
                                    _dealParams = dp;
                                    _order = null;
                                    _backtestInited = false;
                                    _manager.BackTest(); // klines
                                    deals.Add(dp);

                                    counter++;
                                    BnnUtils.ClearCurrentConsoleLine();
                                    Console.Write($"{counter * 100.0 / paramsSetCount:0.##}%");
                                }
                            }
                        }
                    }
                }
            }
            return deals;
        }
        #endregion

        #region RealTimeRoutines
        /*private bool CheckRests()
        {
            if (_orders.Count == 0) return false;
            var now = BnnUtils.GetUnixNow();
            if (now - _lastCheckAmountTime < 55 * 1000) return false;
            var trades = _market.CompressedAggregateTradesList(SymbolName, null, _lastCheckAmountTime, now, 1000).Result;
            _lastCheckAmountTime = now;

            dynamic? tradesInfo = JsonConvert.DeserializeObject(trades) ?? throw new Exception("KlineCandlestickData returned no data");
            if (tradesInfo.Count == 0) return false;

            var result = false;
            foreach (var o in _orders)
            {
                // o.QueueOvercome = 0;
                foreach (var t in tradesInfo)
                {
                    decimal tradePrice = t.p;
                    if (t.T < o.DateTime || tradePrice != o.Price) continue;
                    bool isBuyerMaker = t.m;
                    var quantity = (double)t.q;
                    if (o.IsBuyer ^ !isBuyerMaker) o.QueueOvercome += quantity; // test purposes 1000 * 
                };
                result = result || o.QueueOvercome >= o.InitialQueue;

                if (o.QueueOvercome >= o.InitialQueue && o.InitialQueue > 0)
                {
                    if (AccountManager.IsTest) FillTestOrder(o);
                    else CheckOrderStatus(o);
                }
            }
            return result;
        }


        private void CheckOrdersStatus(bool force)
        {
            if (!force && BnnUtils.GetUnixNow() - _lastVCheckOrdersTime < 59 * 1000) return;
            _lastVCheckOrdersTime = BnnUtils.GetUnixNow();

            var filled = false;
            foreach (var o in _orders)
            {
                filled = filled || CheckOrderStatus(o);
            };
        }


        private static decimal GetPriceVolume(decimal price, dynamic orderArray)
        {
            foreach (var a in orderArray)
            {
                decimal bookPrice = a[0];
                if (bookPrice == price) return a[1];
            }
            return Order.DefaultInitialQueue;
        }


        private double CalcTradePrices() // dynamic ordersData
        {
            _askBidRatio = _bestAskVolume / _bestBidVolume;

            var tmpPrice = _bestBidPrice;
            _escapeToLowerRatio = 3.5; // 9 - 8 * ((double)_bestAskPrice - _dayPriceMin) / (_dayPriceMax - _dayPriceMin); 
            if (_askBidRatio > _escapeToLowerRatio) tmpPrice -= _priceStep; // min - 9, max 1
            // var now = BnnUtils.GetUnixNow();
            // if ((tmpPrice != _newBuyPrice) && (now - _priceChangeTime > _changePriceTimount + 1000)) _priceChangeTime = now; // 3s to changes price in volatile markets
            _newBuyPrice = tmpPrice;

            _newSellPrice = _bestAskPrice;
            if (_askBidRatio < _escapeToHigherRatio) _newSellPrice += _priceStep; // 0.25

            // get current price
            if (_bestAskPrice < CurrPrice) CurrPrice = _bestAskPrice;
            else CurrPrice = _bestBidPrice;
            CurrPrice = FormatPrice(CurrPrice);

            // update last mins values
            var actualSecond = IsBacktest() ? BnnUtils.UnitTimeToDateTime(BacktestTime).Second : DateTime.Now.Second;
            if (actualSecond < _lastCheckSecond) CalcStopsFromOrderBook();
            _lastCheckSecond = actualSecond;

            // current trend angle
            _currAngle = (((double)CurrPrice * _alpha + (1 - _alpha) * _previousCloseEMA) / _previousCloseEMA - 1) * 100;

            return _askBidRatio;
        }


        private void CheckIfPriceOvercame()
        {
            // filled - record exhausted
            foreach (var o in _orders)
            {
                if (o.IsBuyer && _bestBidPrice < o.Price || !o.IsBuyer && _bestAskPrice > o.Price)
                {
                    if (AccountManager.IsTest) FillTestOrder(o);
                    else CheckOrderStatus(o);
                }
            }
        }


        private void ChangeOrdersPrice()
        {
            foreach (var o in _orders)
            {
                if (o.Filled == o.Amount) continue;

                // change order price - buying
                if (o.IsBuyer && (_newBuyPrice < o.Price)) //  || (_newBuyPrice <= o.BorderPrice) _allowRaisePrice --- (o.Price != _newBuyPrice) && 
                {
                    var allowPriceToRaise = (_newBuyPrice > o.Price) && (_askBidRatio < _returnToHigherRatio); // (BnnUtils.GetUnixNow() - _priceChangeTime > _changePriceTimount) && --- for stablecions
                    if (_newBuyPrice < o.Price || allowPriceToRaise) ChangeOrderPrice(o, _newBuyPrice);  // threshold if return --- 3
                };

                // change order price - selling
                if (!o.IsBuyer && (_newSellPrice > o.Price)) // o.BorderPrice = --- (o.Price != _newSellPrice) && 
                {
                    if (_newSellPrice > o.Price || _askBidRatio > 0.47) ChangeOrderPrice(o, _newSellPrice); // threshold if return 0.33							
                }

                // check price volume 
                if (_ordersData != null)
                {
                    var currPriceVolume = (double)GetPriceVolume(o.Price, o.IsBuyer ? _ordersData.bids : _ordersData.asks);
                    if (o.QueueOvercome + currPriceVolume < o.InitialQueue) o.InitialQueue = o.QueueOvercome + currPriceVolume;
                }
            }
        }


        private void CheckForTrend()
        {
            // enter short/long
            if (CurrPrice > _longPrice && _state != State.Long || (double)CurrPrice < _shortPrice && _state != State.Short)
            {
                _state = CurrPrice > _longPrice ? State.Long : State.Short;
                if (!BnnUtils.LastLogLineIsEmpty()) Log(string.Empty);
                Log($"{CurrPrice} enter {_state.ToString().ToLower()}");
            }

            // check trend order
            if (new[] { State.Long, State.Short }.Contains(_state))
            {
                foreach (var o in _orders)
                {
                    if (o.Filled == o.Amount) continue;

                    // catch up
                    if (_state == State.Long && o.IsBuyer || _state == State.Short && !o.IsBuyer)
                    {
                        if (o.IsBuyer && _bestBidPrice > o.Price || !o.IsBuyer && _bestAskPrice < o.Price) ChangeOrderPrice(o, o.IsBuyer ? _bestBidPrice : _bestAskPrice);
                        if (o.IsBuyer && _bestBidPrice < o.Price || !o.IsBuyer && _bestAskPrice > o.Price) FillTestOrder(o);
                    }
                    // cancel
                    else
                    {
                        CancelOrder(o);
                    }
                }

                CreateOrder(_state == State.Long ? _bestAskPrice : _bestBidPrice, _state == State.Long);
            }

            if ((_state != State.Trading) && (_currAngle < _tradeAngle) && (_currAngle > -_tradeAngle))
            {
                _state = State.Trading;
                // if ((_orders.Count > 0) && (_orders[0].Amount != _orders[0].Filled) BnnUtils.Log(string.Empty, false);
                if (!BnnUtils.LastLogLineIsEmpty()) Log(string.Empty);
                Log($"{CurrPrice} {_state.ToString().ToLower()} session");
            }
        }


        public void ProcessOrderBook(decimal bestAskPrice, decimal bestBidPrice, double bestAskVolume = 1000, double bestBidVolume = 1000) // dynamic ordersData
        {
            _bestAskPrice = bestAskPrice;
            _bestAskVolume = bestAskVolume;
            _bestBidPrice = bestBidPrice;
            _bestBidVolume = bestBidVolume;
            _askBidRatio = CalcTradePrices(); // ordersData

            CheckIfPriceOvercame();
            CheckForTrend();
            // CheckRests(); sol is too volatile
            if (_state == State.Trading) ChangeOrdersPrice();
            // CheckOrdersStatus(false); 59s for sol?
            RemoveFilledOrders();

            // check if balance info is not updated yet
            if ((_orders.Count == 0) && (CurrencyAmount < _lotStep) && (_manager.Amount / _newBuyPrice < _lotStep) && !AccountManager.IsTest) _manager.CheckBalance();
            if (_state == State.Trading) CreateNewOrders();
        }


        private void RemoveFilledOrders()
        {
            for (var i = _orders.Count - 1; i >= 0; i--)
            {
                if (_orders[i].Filled == _orders[i].Amount)
                {
                    if (_orders[i].IsBuyer && _newSellPrice <= _orders[i].Price) _newSellPrice = _orders[i].Price + _priceStep;
                    if (_state == State.Stopping)
                    {
                        _state = _orders[i].IsBuyer ? State.Long : State.Short;
                        BnnUtils.Log(GetCurrentInfo());
                    }
                    _orders.Remove(_orders[i]);
                }
            }
        }


        private void CreateNewOrders()
        {
            if (_state != State.Trading) return;

            // new buy order
            var buyPrice = _newBuyPrice - _priceStep;
            var order = CreateOrder(buyPrice, true); // , amountToBuy,
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            if ((order != null) && (_ordersData != null)) order.InitialQueue = (double)GetPriceVolume(buyPrice, _ordersData.bids);
#pragma warning restore CS8602 // Dereference of a possibly null reference.

            // sell currency routines
            order = CreateOrder(_newSellPrice, false); // , CurrencyAmount
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            if ((order != null) && (_ordersData != null)) order.InitialQueue = (double)GetPriceVolume(_newSellPrice, _ordersData.asks);
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        }


        private Order? CreateOrder(decimal price, bool isBuyer) //decimal amount, 
        {
            // var borderPrice = isBuyer ? _lastDealPrice - _priceStep : _lastDealPrice + _priceStep;
            var borderPrice = isBuyer ? _lastDealPrice - _tradeStepCoef * _priceStep : _lastDealPrice + _tradeStepCoef * _priceStep;
            decimal quantity;
            if (isBuyer)
            {
                if (price > borderPrice) price = borderPrice;
                quantity = _manager.Amount / price;
            }
            else
            {
                if (price < borderPrice) price = borderPrice;
                quantity = CurrencyAmount;
            }
            quantity = FormatQuantity(quantity);
            if (quantity < _lotStep) return null;

            var orderID = _manager.LimitOrder(SymbolName, isBuyer ? Side.BUY : Side.SELL, quantity, price);
            var newOrder = new Order()
            {
                Id = orderID,
                Price = price,
                Amount = quantity,
                DateTime = BnnUtils.GetUnixNow(),
                IsBuyer = isBuyer,
                BorderPrice = borderPrice, // isBuyer ? 0 : 
                TotalQuote = _manager.Amount
            };

            if (isBuyer) newOrder.InitialQueue = price == _bestBidPrice ? _bestBidVolume : Order.DefaultInitialQueue;
            else newOrder.InitialQueue = price == _bestAskPrice ? _bestAskVolume : Order.DefaultInitialQueue;

            _orders.Add(newOrder);
            if (isBuyer) _manager.Amount -= newOrder.Amount * newOrder.Price;
            else CurrencyAmount = CurrencyAmount - quantity;

            if (AccountManager.IsTest) Log($"{CurrPrice} new order {newOrder}"); //  to buy -  ( c {CurrencyAmount} a {_manager.Amount} )
            else SaveNewOrder(newOrder);
            return newOrder;
        }


        protected override void ChangeOrderPrice(Order order, decimal price)
        {
            if (order.Price == price) return;

            if (CheckOrderStatus(order))
            {
                if (AccountManager.IsTest) FillTestOrder(order);
                return; // true;
            }

            decimal newQuantity;
            if (order.IsBuyer)
            {
                newQuantity = FormatQuantity(order.TotalQuote / price);
                // _manager.Amount -= newQuantity * price;
                _manager.Amount = order.TotalQuote - newQuantity * price;
            }
            else newQuantity = order.Amount + FormatQuantity(CurrencyAmount); // :0.########
            Log($"{CurrPrice} order price changed: {order} => {FormatPrice(price)} [{_bestAskVolume / Thousands:0.###}/{_bestBidVolume / Thousands:0.###}{ThousandsDescr}]"); //  | {order.GetQueueInfo()}
            order.Price = price;
            // if (order.IsBuyer ? price < order.StopLossPrice : price > order.StopLossPrice) order.StopLossPrice = order.IsBuyer ? price + _stopLossDelta : price - _stopLossDelta;
            order.Amount = newQuantity;
            order.InitialQueue = Order.DefaultInitialQueue;
            order.QueueOvercome = 0;
            order.DateTime = BnnUtils.GetUnixNow();

            try
            {
                var newId = _manager.ReplaceOrder(order.Id, SymbolName, order.IsBuyer ? Side.BUY : Side.SELL, newQuantity, price);
                ChangeOrderData(order.Id, newId, order.Amount, order.Price);
                order.Id = newId;
            }
            catch
            {
                if (!CheckOrderStatus(order)) // check for "order does not exists"
                {
                    Console.Beep();
                    Environment.Exit(0);
                }
            }
            // BnnUtils.Log($"{DateTime.Now:dd.MM.yyyy HH:mm:ss} !!! balance: {GetCurrencyName().ToLower()[0]} {CurrencyAmount} {_manager.QuoteCurrencyName.ToLower()[0]} {_manager.Amount} ");			
            // return false;
        }


        private bool CheckOrderStatus(Order order)
        {
            var status = _manager.CheckOrder(this, order.Id);
            var result = status == OrderStatus.Filled || status == OrderStatus.Canceled;
            if (result)
            {
                _lastDealPrice = order.Price;
                // BnnUtils.Log($"CheckOrderStatus._lastDealPrice: {_lastDealPrice}");
                order.Filled = order.Amount;
                if (!AccountManager.IsTest) _manager.CheckBalance();
                if (!order.IsBuyer)
                {
                    Log(string.Empty);
                    // CheckDayRange();
                }
            }
            return result;
        }


        private void CheckDayRange()
        {
            UpdateCandlesticks(50);
            var endTime = ((DateTimeOffset)DateTime.Now).ToUnixTimeMilliseconds();
            var startTime = endTime - 24 * 60 * 60L * 1000;
            var klines = LoadKlinesFromDB(startTime, endTime);
            // GetLongValue(klines);
            for (var i = klines.Count - 1; i > 0; i--)
            {
                if (klines.Count - i > 24 * 60) break;
                if (klines[i].HighPrice > _dayPriceMax) _dayPriceMax = klines[i].HighPrice;
                if (klines[i].LowPrice < _dayPriceMin) _dayPriceMin = klines[i].LowPrice;
            };
            Console.WriteLine($"day min: {_dayPriceMin}; day max: {_dayPriceMax}");
        }


        public override void Prepare()
        {
            base.Prepare();
            CheckDayRange();

            var digitsCount = -(int)Math.Log10((double)_priceStep);
            // PriceMask = "0.".PadRight(digitsCount + 2, '#');
            PriceMask = "0.".PadRight(digitsCount + 2, '0');
            // _stopLossDelta = 15 * PriceStep;

            _lastDealPrice = decimal.MaxValue;
            if (!AccountManager.IsTest) LoadOrders();
            foreach (var o in _orders)
            {
                BnnUtils.Log($"loaded order: {o}", false);
            }
        }


        public override async void Start()
        {
            if (!AccountManager.IsTest) CheckOrdersStatus(true);
            // MonitorObStream();

            var ws = new MarketDataWebSocket($"{SymbolName.ToLower()}@depth20@100ms"); // 20 --- @100ms
            ws.OnMessageReceived(data =>
            {
                _ordersData = JsonConvert.DeserializeObject(data.Trim()) ?? throw new Exception("depth returned no data");

                try
                {
                    decimal bestAskPrice = _ordersData.asks[0][0];
                    double bestAskVolume = _ordersData.asks[0][1];
                    decimal bestBidPrice = _ordersData.bids[0][0];
                    double bestBidVolume = _ordersData.bids[0][1];
                    ProcessOrderBook(bestAskPrice, bestBidPrice, bestAskVolume, bestBidVolume);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    Environment.Exit(0);
                }
                return Task.CompletedTask;
            }, CancellationToken.None);

            await ws.ConnectAsync(CancellationToken.None);
        }*/
        #endregion
    }
}