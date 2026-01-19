// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;
using Microsoft.Extensions.Configuration;
using Org.BouncyCastle.Asn1.X509;

namespace Bnncmd.Strategy
{
    internal class FibDivParams : DealParams
    {
        public decimal BuyLevel { get; set; }
        public decimal SellLevel { get; set; }
        public decimal StopLossLevel { get; set; }

        public FibDivParams(decimal buyLevel, decimal sellLevel, decimal stopLossLevel)
        {
            BuyLevel = buyLevel;
            SellLevel = sellLevel;
            StopLossLevel = stopLossLevel;
            // ShortAngle = shortAngle;

            DealProfit = 100;
            PriceThreshold = -100;
            StopLossPerc = 100;
            ConfirmExtrPart = 0.00000;
        }

        public override string ToString()
        {
            var results = $"{TotalProfit:0.##}\t\t{MaxDealInterval}\t{DealCount}\t{StopLossCount}";
            var conditions = $"bl\t{BuyLevel}\tsl\t{SellLevel:0.###}\tsll\t{StopLossLevel:0.###}"; // \tdi\t{DiffInterval}
            return results + "\t" + conditions;
        }

        public override string GetParamsDesciption()
        {
            return $"bl {BuyLevel}   sl {SellLevel}   sll {StopLossLevel:0.#####}"; //    sa {ShortAngle}
        }
    }


    internal class Fib(decimal level)
    {
        public decimal Level { get; set; } = level;

        public decimal Price { get; set; } = -1;
    }


    internal class FibDiv : BaseStrategy
    {
        #region ConstructorAndVariables
        public FibDiv(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            _isLimit = true;

            _limitBuyLevel = AccountManager.Config.GetValue<decimal>("Strategies:FibDiv:BuyLevel");
            _limitSellLevel = AccountManager.Config.GetValue<decimal>("Strategies:FibDiv:SellLevel");
            _stopLossLevel = AccountManager.Config.GetValue<decimal>("Strategies:FibDiv:StopLossLevel");

            var dp = new FibDivParams(_limitBuyLevel, _limitSellLevel, _stopLossLevel);
            _dealParams = dp;
        }

        public override string GetName() { return "FibDiv"; }

        private Extremum _lastMinimum = new(0, -1);
        private Extremum _penultMinimum = new(0, -1);
        private Extremum _lastMaximum = new(0, -1);
        private Extremum _penultMaximum = new(0, -1);

        private decimal _currentExtremum = -1;

        private int _lastCheckSecond = 59;
        private bool _isBullTrend = false;
        private readonly decimal _minPriceChange = 0.03M;

        private readonly Fib[] _fibLevels = [new(-2.618M), new(-1.618M), new(-0.618M), new(-0.27M), new(0), new(0.236M), new(0.382M), new(0.5M), new(0.618M), new(0.786M), new(1), new(1.618M), new(2.618M), new(3.618M), new(4.618M)];

        // settings
        private readonly decimal _minEnterLongLevel = 0.618M; // 0.382M;
        private decimal _limitBuyLevel = 0.618M;
        private decimal _limitSellLevel = -0.618M;
        private decimal _stopLossLevel = 1;
        #endregion

        #region Backtest
        protected override double GetShortValue(decimal longPrice, out decimal stopLossValue)
        {
            stopLossValue = -1;
            if (IsBacktest()) return int.MaxValue;
            else return (double)_shortPrice;
        }


        private void CalcMinMaxs(List<Kline> klines)
        {
            if ((_order != null) && (!_order.IsBuyer) && (_limitSellLevel != -1)) return; // in long position do not recalc SL and aim 

            var minuteKlines = klines.GroupBy(k => k.OpenTime / 1000 / 60).Select(t => new Kline
            {
                OpenTime = t.First().OpenTime,
                HighPrice = t.Max(k => k.HighPrice),
                LowPrice = t.Min(K => K.LowPrice)
            }).ToList(); //   .ToArray();

            _lastMinimum.Value = -1;
            _lastMaximum.Value = -1;
            _penultMaximum.Value = -1;
            _penultMinimum.Value = -1;

            for (var i = minuteKlines.Count - 4; i > 2; i--)
            {
                // local maximin
                var high = minuteKlines[i].HighPrice;
                if ((high >= minuteKlines[i - 2].HighPrice) && (high >= minuteKlines[i - 1].HighPrice) && (high > minuteKlines[i + 1].HighPrice) && (high > minuteKlines[i + 2].HighPrice))
                {
                    if (_lastMaximum.Value > 0)
                    {
                        if (_penultMaximum.Value < 0)
                        {
                            _penultMaximum = new Extremum(minuteKlines[i].OpenTime, (decimal)high);
                            if (_penultMinimum.Value > 0) break;
                        }
                    }
                    else _lastMaximum = new Extremum(minuteKlines[i].OpenTime, (decimal)high);
                }

                // local minimum
                var low = minuteKlines[i].LowPrice;
                if ((low <= minuteKlines[i - 2].LowPrice) && (low <= minuteKlines[i - 1].LowPrice) && (low < minuteKlines[i + 1].LowPrice) && (low < minuteKlines[i + 2].LowPrice))
                {
                    if (_lastMinimum.Value > 0)
                    {
                        if (_penultMinimum.Value < 0)
                        {
                            _penultMinimum = new Extremum(minuteKlines[i].OpenTime, (decimal)low);
                            if (_penultMaximum.Value > 0) break;
                        }
                    }
                    else _lastMinimum = new Extremum(minuteKlines[i].OpenTime, (decimal)low);
                }
            }

            if ((_currentExtremum > 0) && (_currentExtremum < _lastMinimum.Value)) return;
            else if (_currentExtremum > _lastMaximum.Value)
            {
                _penultMaximum.Value = _lastMaximum.Value;
                _penultMaximum.Time = _lastMaximum.Time;
                _lastMaximum.Value = _currentExtremum;
                _lastMaximum.Time = BnnUtils.GetUnixNow();
            }
            else _currentExtremum = -1;

            // check if trend changed
            var trendIsBull = (((_lastMaximum.Time > _lastMinimum.Time) || (_lastMinimum.Value == _penultMinimum.Value)) && (_lastMaximum.Value > _penultMaximum.Value));
            trendIsBull = trendIsBull || (((_lastMinimum.Time >= _lastMaximum.Time) || (_lastMaximum.Value == _penultMaximum.Value)) && (_lastMinimum.Value > _penultMinimum.Value));
            CalcFibs();
            ChangeTrend(trendIsBull);

            // if (AccountManager.LogLevel > 7) 
            // Log($"{PrintFibs()}");
        }


        private void CalcFibs()
        {
            if ((_order != null) && !_order.IsBuyer) return; // in long posotion do not recalc SL and aim  -- - && (_limitSellLevel != -1)
            var impulse = _lastMaximum.Value - _lastMinimum.Value;
            for (var i = 0; i < _fibLevels.Length; i++)
            {
                _fibLevels[i].Price = FormatPrice(_lastMaximum.Value - _fibLevels[i].Level * impulse);
            }
        }


        private string PrintFibs()
        {
            return $"[{GetLevelPrice(0)} => {GetLevelPrice(0.382M)} => {GetLevelPrice(0.5M)} => {GetLevelPrice(0.618M)} => {GetLevelPrice(1)}]";
        }


        private decimal GetLevelPrice(decimal level)
        {
            for (var i = 0; i < _fibLevels.Length; i++)
            {
                if (_fibLevels[i].Level == level) return _fibLevels[i].Price;
            }
            return -1;
        }


        private int CalcAimPrice()
        {
            if ((_order != null) && !_order.IsBuyer && (_limitSellLevel != -1)) return -1; // in long posotion do not recalc SL and aim 
            if ((_order != null) && _order.IsBuyer && !_order.IsStopLoss) return -1; // if limit order then catch up price

            // if (_order != null) Log($"CalcAimPrice: {_order != null} {_order.IsBuyer} {!_order.IsStopLoss}");
            // else Log($"CalcAimPrice: _order == null");

            // if (_currentExtremum > 0) return -1;
            // CalcFibs();
            if (!IsDealEntered && (_limitBuyLevel == -1) && (CurrPrice > GetLevelPrice(_minEnterLongLevel))) return -1; //  _lastMaximum.Value - _minEnterLongLevel * impulse)

            for (var i = 2; i < _fibLevels.Length; i++)
            {
                if (!IsDealEntered)
                {
                    if (_limitBuyLevel != -1)
                    {
                        if (_fibLevels[i].Level == _limitBuyLevel) return i;
                    }
                    else
                    {
                        if ((CurrPrice > _fibLevels[i].Price) && (CurrPrice <= _fibLevels[i - 1].Price))
                        {
                            if (AccountManager.LogLevel > 9) Log($"{_fibLevels[i].Price} ({_fibLevels[i].Level}) < {CurrPrice} < {_fibLevels[i - 1].Price} ({_fibLevels[i - 1].Level}) => {_fibLevels[i - 2].Price} ({_fibLevels[i - 2].Level})");
                            return i - 2;
                        }
                    }
                }

                if (IsDealEntered)
                {
                    if (_order == null) return -1;
                    if (_limitSellLevel != -1)
                    {
                        if (_fibLevels[i].Level == _limitSellLevel) return i;
                    }
                    else
                    {
                        if ((CurrPrice > _fibLevels[i - 1].Price) && (CurrPrice <= _fibLevels[i - 2].Price))
                        {
                            // if (_longPrice > _fibLevels[i].Price) return -1;
                            if (_fibLevels[i].Price < _order.Price) return -1;
                            if (AccountManager.LogLevel > 9) Log($"{_fibLevels[i - 1].Price} ({_fibLevels[i - 1].Level}) < {CurrPrice} < {_fibLevels[i - 2].Price} ({_fibLevels[i - 2].Level}) => {_fibLevels[i].Price} ({_fibLevels[i].Level})");
                            return i;
                        }
                    }
                }
            }
            return -1;
        }


        private void ChangeTrend(bool isBull)
        {
            if (_isBullTrend == isBull) return;
            _isBullTrend = isBull;
            Log($"-{_penultMinimum.Value} +{_penultMaximum.Value} -{_lastMinimum.Value}{(_lastMaximum.Time < _lastMinimum.Time ? "*" : "")} +{_lastMaximum.Value}{(_lastMaximum.Time > _lastMinimum.Time ? "*" : "")}");
            var fibs = string.Empty;
            if (isBull)
            {
                CalcFibs();
                fibs = PrintFibs();
            }
            Log($"{(isBull ? "bull" : "bear")} trend {fibs}");
            Log(string.Empty);

            if (isBull && (_order == null) && !IsDealEntered)
            {
                var index = CalcAimPrice();
                if ((index > 0) && (CurrPrice < _fibLevels[index].Price)) _order = CreateSpotLimitOrder(CurrPrice - _priceStep, !IsDealEntered, false);
            }
        }


        private void CheckStopLoss()
        {
            // stop-loss
            if (CurrPrice < GetLevelPrice(_stopLossLevel)) // !_isBullTrend && (
            {
                // Log($"CheckStopLoss() - {GetLevelPrice(_bullStopLossLevel)} - {PrintFibs()}");

                if (_order == null) _order = CreateSpotLimitOrder(CurrPrice, false, false);
                else
                {
                    if (_order.IsBuyer)
                    {
                        CancelOrder(_order);
                        _order = null;
                    }
                    else
                    {
                        _order.StopLossPrice = 0;
                        ChangeOrderPrice(_order, Math.Min(CurrPrice, _order.Price));
                    }
                }
            }
        }


        private void ProcessExtremum()
        {
            var newTrendIsBull = CurrPrice > _lastMaximum.Value;
            if (newTrendIsBull && (CurrPrice > _currentExtremum)) _currentExtremum = CurrPrice;
            if (!newTrendIsBull && ((CurrPrice < _currentExtremum) || (_currentExtremum < 0))) _currentExtremum = CurrPrice;

            if (_isBullTrend != newTrendIsBull)
            {
                if (!newTrendIsBull && (_order != null) && _order.IsBuyer)
                {
                    CancelOrder(_order);
                    _order = null;
                }

                Log($"archive {(newTrendIsBull ? "max" : "min")} exceeded");
                ChangeTrend(CurrPrice > _lastMaximum.Value);
            }

            CheckStopLoss();
        }


        private void CalcOrderPrice()
        {
            if (!_isBullTrend && !IsDealEntered) return;
            var index = CalcAimPrice();
            // Log($"index: {index}");
            if (index >= 0)
            {
                if (_fibLevels[index].Price < 0) return;
                var newPrice = IsDealEntered ? _fibLevels[index].Price + Slippage : _fibLevels[index].Price - Slippage;
                if (!IsDealEntered && (_limitBuyLevel != -1) && (newPrice > CurrPrice)) newPrice = CurrPrice - _priceStep;
                // var isNewPrice = (_order == null) || (!IsLong && (Math.Abs(newPrice - _order.Price) > _minPriceChange)) || (IsLong && (newPrice - _order.Price > _minPriceChange)); //  && (newPrice > _order.Price)
                var isNewPrice = (_order == null) || (!IsDealEntered && (_order.Price - newPrice > _minPriceChange)) || (IsDealEntered && (newPrice - _order.Price > _minPriceChange)); //  && (newPrice > _order.Price)
                if (isNewPrice)
                {
                    Log($"aim level {_fibLevels[index].Level} ({_fibLevels[index].Price})");
                    var isLimit = (IsDealEntered && ((newPrice > CurrPrice) || (_limitSellLevel != -1))) || (!IsDealEntered && (_limitBuyLevel != -1)); //  !IsLong || (newPrice < CurrPrice);
                    if (_order == null) _order = CreateSpotLimitOrder(newPrice, !IsDealEntered, !isLimit); // IsLong && (newPrice < CurrPrice)
                    else
                    {
                        // if ((IsLong && (_fibLevels[index].Price > _order.Price)) || (!IsLong && (_fibLevels[index].Price < _order.Price))) 
                        // if (!IsLong) 
                        ChangeOrderPrice(_order, newPrice);
                    }
                }
            }
        }


        private void ProcessPrice(decimal price)
        {
            CurrPrice = price;
            if (AccountManager.LogLevel > 9) Log($"{CurrPrice} / {_order}"); // CurrPrice

            var stopLossPrice = GetLevelPrice(_stopLossLevel);
            if ((CurrPrice > _lastMaximum.Value) || (CurrPrice < stopLossPrice)) ProcessExtremum(); // _lastMinimum.Value -- ((--- (_lastMinimum.Value > 0) && 
            if (CurrPrice > stopLossPrice) CalcOrderPrice();

            CheckStopLoss();

            if ((_order == null) || (_order.Filled > 1)) return; // (_order.Price < 0) || 
            if ((!_order.IsBuyer && (CurrPrice < _order.Price)) || (_order.IsBuyer && (CurrPrice > _order.Price))) //  && _order.IsStopLoss
            {
                if ((_order.Filled == 0) && !_order.IsStopLoss) Log("stop-order triggered");
                _order.StopLossPrice = 0;
                ChangeOrderPrice(_order, CurrPrice);
                _order.Filled = 1;
            }

            if (AccountManager.IsTest && (_order.IsTriggered || !_order.IsStopLoss) && ((IsDealEntered && (CurrPrice > _order.Price)) || (!IsDealEntered && (CurrPrice < _order.Price))))
            {
                ExecuteOrder(_order);
                _longPrice = _order.Price;
                _order = null;
                IsDealEntered = !IsDealEntered;

                if (IsDealEntered)
                {
                    if (_limitSellLevel == -1) _order = CreateSpotLimitOrder(GetLevelPrice(_stopLossLevel), false, true);
                    else _order = CreateSpotLimitOrder(GetLevelPrice(_limitSellLevel), false, false);
                }
            }
        }


        public override void PrepareBacktest(List<Kline>? klines = null)
        {
            base.PrepareBacktest(klines);
            CurrencyAmount = 0;
            _manager.CheckBalance();
            Slippage = 0; //  0.03M;

            _lastMinimum = new(0, -1);
            _penultMinimum = new(0, -1);
            _lastMaximum = new(0, -1);
            _penultMaximum = new(0, -1);

            _lastCheckSecond = 0;
            _isBullTrend = false;
            _currentExtremum = -1;
        }


        private void ProcessKline(List<Kline> klines)
        {
            var lastKline = klines[^1];
            BacktestTime = lastKline.OpenTime;
            CurrPrice = (decimal)lastKline.ClosePrice;

            var currSecond = BnnUtils.UnitTimeToDateTime(BacktestTime).Second;
            if (currSecond < _lastCheckSecond) CalcMinMaxs(klines);
            _lastCheckSecond = currSecond;

            ProcessPrice((decimal)lastKline.OpenPrice);
            if (lastKline.ClosePrice >= lastKline.OpenPrice)
            {
                if (lastKline.LowPrice < (decimal)lastKline.OpenPrice) ProcessPrice((decimal)lastKline.LowPrice);
                if (lastKline.HighPrice > (decimal)lastKline.ClosePrice) ProcessPrice((decimal)lastKline.HighPrice);
            }
            else
            {
                if (lastKline.HighPrice > (decimal)lastKline.OpenPrice) ProcessPrice((decimal)lastKline.HighPrice);
                if (lastKline.LowPrice < (decimal)lastKline.ClosePrice) ProcessPrice((decimal)lastKline.LowPrice);
            }
            if (lastKline.ClosePrice != lastKline.OpenPrice) ProcessPrice((decimal)lastKline.ClosePrice);
        }


        protected override double GetShortValue(List<Kline> klines, double previousValue = -1)
        {
            ProcessKline(klines);
            return (double)_shortPrice; //  1000000.0;
        }


        protected override decimal GetLongValue(List<Kline> klines, decimal previousValue = -1)
        {
            ProcessKline(klines);
            return _longPrice; //  double.MaxValue;
        }


        protected override void SaveStatistics(List<BaseDealParams> tradeResults)
        {
            //
        }


        protected override List<BaseDealParams> CalculateParams(List<Kline> klines)
        {
            decimal[] buyLevels = [0.382M, 0.5M, 0.618M, 0.786M, 1, 1.618M];
            decimal[] sellLevels = [-1.618M, -0.618M, -0.27M, 0, 0.236M];
            decimal[] stopLossLevels = [1, 1.618M, 2.618M];

            /* decimal[] buyLevels = [0.618M];
            decimal[] sellLevels = [-1.618M]; //, -0.618M
            decimal[] stopLossLevels = [1, 2.618M];*/

            /* decimal[] buyLevels = [0.786M];
            decimal[] sellLevels = [0];
            decimal[] stopLossLevels = [1, 1.618M, 2.618M]; // */

            var deals = new List<BaseDealParams>();
            long paramsSetCount = buyLevels.Length * sellLevels.Length * stopLossLevels.Length; //  * shortAngles.Length
            long counter = 0;
            foreach (var bl in buyLevels)
            {
                _limitBuyLevel = bl;
                foreach (var sl in sellLevels)
                {
                    _limitSellLevel = sl;
                    foreach (var sll in stopLossLevels)
                    {
                        _stopLossLevel = sll;
                        var dp = new FibDivParams(_limitBuyLevel, _limitSellLevel, _stopLossLevel);
                        _dealParams = dp;
                        _order = null;
                        _manager.BackTest(); // klines

                        // Console.WriteLine($"{_dealParams.TotalProfit} / {CurrPrice} / {CurrencyAmount} / {_manager.Amount} / {_order.TotalQuote}");
                        // _dealParams.TotalProfit = CurrPrice * (double)CurrencyAmount + (double)_manager.Amount;
                        // _dealParams.TotalProfit = (_order == null) ? CurrPrice * (double)CurrencyAmount + (double)_manager.Amount : (double)(_order.Amount * _order.Price);
                        deals.Add(dp);

                        counter++;
                        BnnUtils.ClearCurrentConsoleLine();
                        Console.Write($"{counter * 100.0 / paramsSetCount:0.##}%");
                    }
                }
            }
            return deals;
        }
        #endregion
    }
}
