// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Org.BouncyCastle.Asn1.X509;

namespace Bnncmd.Strategy
{
    internal class EmaTrendParams : EmaDealParams
    {
        public int EmaHourLength { get; set; }
        public int LeftExtrKlines { get; set; }
        public int RightExtrKlines { get; set; }

        public EmaTrendParams(int emaHourLength, double longAngle, int leftExtrKlines, int rightExtrKlines) : base(emaHourLength, 1, longAngle, rightExtrKlines)
        {
            LeftExtrKlines = leftExtrKlines;
            RightExtrKlines = rightExtrKlines;
        }

        public override string ToString()
        {
            var results = $"{TotalProfit:0.##}\t\t{DealCount}";
            var conditions = $"ehi\t{EmaLength}\tla\t{LongAngle}\tlk\t{LeftExtrKlines:0.###}\trk\t{RightExtrKlines:0.###}";
            return results + "\t" + conditions;
        }

        public override string GetParamsDesciption()
        {
            return $"ehi {EmaLength}   la {LongAngle}   lk {LeftExtrKlines}   rk {RightExtrKlines}";
        }
    }


    internal class EmaTrend : EMA
    {
        #region ConstructorAndVariables
        public EmaTrend(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            _dealParams = new EmaTrendParams(EmaLength, _longAngle, _leftKlines, _rightKlines);
            _isLimit = true;
        }

        public override string GetName() { return "Ema Trend"; }

        private Extremum _lastMinimum = new(0, -1);
        private Extremum _penultMinimum = new(0, -1);
        private Extremum _lastMaximum = new(0, -1);
        private Extremum _penultMaximum = new(0, -1);
        private decimal _currentExtremum = 0;

        private int _lastCheckMinute = 59;
        private bool _isBullTrend = false;
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
            var hourKlines = GroupKlines(klines, 60 * 60);

            if (_previousCloseEMA == 0)
            {
                _rangeKlines = hourKlines;
                _previousCloseEMA = (decimal)CalcEMA(EmaLength, _rangeKlines.Count - 1);
            }
            else
            {
                var newCloseEMA = hourKlines[^2].ClosePrice * _alpha + (1 - _alpha) * _previousCloseEMA;
                _trendCloseAngle = (double)((newCloseEMA - _previousCloseEMA) / _previousCloseEMA * 100);
                _previousCloseEMA = newCloseEMA;
            }

            var extremums = GetExtremums(hourKlines);

            _penultMinimum = extremums.Item1;
            _penultMaximum = extremums.Item2;
            _lastMinimum = extremums.Item3;
            _lastMaximum = extremums.Item4;

            if (AccountManager.LogLevel > 5) Log($"-{_penultMinimum.Value} +{_penultMaximum.Value} -{_lastMinimum.Value}{(_lastMaximum.Time < _lastMinimum.Time ? "*" : "")} +{_lastMaximum.Value}{(_lastMaximum.Time > _lastMinimum.Time ? "*" : "")}   EMA{EmaLength} {_previousCloseEMA:0.000}   TA {_trendCloseAngle:0.000}/{_longAngle}   CE {_currentExtremum:0.000}");

            if (_currentExtremum > _lastMaximum.Value)
            {
                _penultMaximum.Value = _lastMaximum.Value;
                _penultMaximum.Time = _lastMaximum.Time;
                _lastMaximum.Value = _currentExtremum;
                _lastMaximum.Time = BnnUtils.GetUnixNow();
            }
            else if ((_currentExtremum < _lastMinimum.Value) && (_currentExtremum > 0))
            {
                _penultMinimum.Value = _lastMinimum.Value;
                _penultMinimum.Time = _lastMinimum.Time;
                _lastMinimum.Value = _currentExtremum;
                _lastMinimum.Time = BnnUtils.GetUnixNow();
            }
            else _currentExtremum = 0;

            // check if trend changed
            // var trendIsBull = (((_lastMaximum.Time > _lastMinimum.Time) || (_lastMinimum.Value == _penultMinimum.Value)) && (_lastMaximum.Value > _penultMaximum.Value));
            var trendIsBull = (_lastMinimum.Value > _penultMinimum.Value) && (_lastMaximum.Value > _penultMaximum.Value) && (_trendCloseAngle > _longAngle);
            // trendIsBull = trendIsBull || (((_lastMinimum.Time >= _lastMaximum.Time) || (_lastMaximum.Value == _penultMaximum.Value)) && (_lastMinimum.Value > _penultMinimum.Value));
            ChangeTrend(trendIsBull);
        }


        private void ProcessExtremum()
        {
            if (_lastMaximum.Value < 0) return;
            var newTrendIsBull = (CurrPrice > _lastMaximum.Value) && (_lastMinimum.Value > _penultMinimum.Value) && (_trendCloseAngle > _longAngle);
            if (newTrendIsBull && (CurrPrice > _currentExtremum)) _currentExtremum = CurrPrice;
            if (!newTrendIsBull && ((CurrPrice < _currentExtremum) || (_currentExtremum == 0))) _currentExtremum = CurrPrice;

            if (_isBullTrend != newTrendIsBull)
            {
                if (!newTrendIsBull && (_order != null) && _order.IsBuyer)
                {
                    CancelOrder(_order);
                    _order = null;
                }

                Log($"new realtime {(newTrendIsBull ? "max" : "min")}"); //  exceeded
                ChangeTrend(CurrPrice > _lastMaximum.Value);
            }
        }


        private void ChangeTrend(bool isBull)
        {
            if (_isBullTrend == isBull) return;
            _isBullTrend = isBull;
            Log($"-{_penultMinimum.Value} +{_penultMaximum.Value} -{_lastMinimum.Value}{(_lastMaximum.Time < _lastMinimum.Time ? "*" : "")} +{_lastMaximum.Value}{(_lastMaximum.Time > _lastMinimum.Time ? "*" : "")}   EMA{EmaLength} {_previousCloseEMA:0.000}   TA {_trendCloseAngle:0.000}/{_longAngle}   CE {_currentExtremum:0.000}");
            Log($"{(isBull ? "bull" : "bear")} trend"); //  {fibs}
            if (isBull) EnterLong();
            else EnterShort();
            Log(string.Empty);
        }


        private void EnterLong()
        {
            // Log($"enter long"); //  * [{downLevel:0.###} - {upLevel:0.###}]
            IsDealEntered = true;
            _longPrice = CurrPrice;
        }


        private void EnterShort()
        {
            if (!IsDealEntered) return;
            // Log($"enter short"); //  [{downLevel:0.###} - {upLevel:0.###}] *
            IsDealEntered = false;

            var balance = (decimal)_manager.Statistics.TotalProfit;
            var newBalance = CurrPrice / _longPrice * balance * (1 - Slippage) * (1 - Slippage);
            Log($"{CurrPrice} / {_longPrice} * {balance:0.###} * {1 - Slippage}^2 = {newBalance:0.###}");
            _dealParams.TotalProfit = (double)newBalance;
            _manager.Statistics.DealCount++;
            _dealParams.DealCount = _manager.Statistics.DealCount;
            _manager.Statistics.TotalProfit = _dealParams.TotalProfit;
            // Log(string.Empty);
        }


        public override void PrepareBacktest(List<Kline>? klines = null)
        {
            base.PrepareBacktest(klines);
            CurrencyAmount = 0;
            _manager.CheckBalance();
            // Slippage = 0; //  0.03M;

            _lastMinimum = new(0, -1);
            _penultMinimum = new(0, -1);
            _lastMaximum = new(0, -1);
            _penultMaximum = new(0, -1);
            _currentExtremum = 0;

            _lastCheckMinute = 59;
            _isBullTrend = false;
        }


        private void ProcessKline(List<Kline> klines)
        {
            var lastKline = klines[^1];
            BacktestTime = lastKline.OpenTime;
            CurrPrice = (decimal)lastKline.ClosePrice;

            var currMinute = BnnUtils.UnitTimeToDateTime(BacktestTime).Minute;
            if (currMinute < _lastCheckMinute) CalcMinMaxs(klines);
            _lastCheckMinute = currMinute;

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


        private void ProcessPrice(decimal price)
        {
            CurrPrice = price;
            if (AccountManager.LogLevel > 9) Log($"{CurrPrice} / {_order}"); // CurrPrice
            if ((CurrPrice > _lastMaximum.Value) || (CurrPrice < _lastMinimum.Value)) ProcessExtremum(); // _lastMinimum.Value -- ((--- (_lastMinimum.Value > 0) && 
        }


        protected override double GetShortValue(List<Kline> klines, double previousValue = -1)
        {
            ProcessKline(klines);
            return (double)_shortPrice;
        }


        protected override decimal GetLongValue(List<Kline> klines, decimal previousValue = -1)
        {
            ProcessKline(klines);
            return _longPrice;
        }


        protected override void SaveStatistics(List<BaseDealParams> tradeResults)
        {
            //
        }


        protected override List<BaseDealParams> CalculateParams(List<Kline> klines)
        {
            int[] leftExtrKlines = [1, 5, 7];
            int[] rightExtrKlines = [0, 1, 3, 5, 7];
            int[] emas = [30, 110, 300];
            double[] longAngles = [-0.1, 0.01, 0.05, 0.1, 0.3];

            var deals = new List<BaseDealParams>();
            long paramsSetCount = leftExtrKlines.Length * rightExtrKlines.Length * emas.Length * longAngles.Length; //  * shortAngles.Length
            long counter = 0;
            foreach (var lk in leftExtrKlines)
            {
                _leftKlines = lk;
                foreach (var rk in rightExtrKlines)
                {
                    _rightKlines = rk;
                    foreach (var el in emas)
                    {
                        EmaLength = el;
                        // _alpha = 2.0M / (_emaLength + 1);
                        foreach (var la in longAngles)
                        {
                            _longAngle = la;
                            var dp = new EmaTrendParams(EmaLength, _longAngle, _leftKlines, _rightKlines);
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
            }
            return deals;
        }
        #endregion
    }
}
