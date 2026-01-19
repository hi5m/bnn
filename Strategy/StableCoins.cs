// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Bnncmd.Strategy;
using Bnncmd;

namespace Bnncmd.Strategy
{
    internal class StableCoinsParams : DealParams
    {
        public int StableInterval { get; set; }

        public StableCoinsParams(int stableInterval)
        {
            StableInterval = stableInterval;
        }

        public override string ToString()
        {
            var results = $"{TotalProfit:0.##}\t\t{DealCount}\t{StopLossCount}"; // {MaxDealInterval}\t
            var conditions = $"si\t{StableInterval}\t";
            // var conditions = $"rln\t{RsiLength}\trlv\t{RsiLevel}\tshl\t{SellFibLevel:0.###}\tstl\t{StopFibLevel:0.###}\tmmk\t{MinMaxKlines:0.###}";
            return results + "\t" + conditions;
        }

        public override string GetParamsDesciption()
        {
            return $"si {StableInterval}   "; //    rlv {RsiLevel}   shl {SellFibLevel:0.##}   stl {StopFibLevel}   mmk {MinMaxKlines}
        }
    }

    internal class StableCoins : EMA
    {
        #region VariablesAndConstructor
        private int _stableInterval = 250; // 90;
        private readonly int _tradeInterval = 120;
        private int _minsTraded = 0;

        private decimal _lastStablePrice = 0;
        private decimal _tempStablePrice = 0;
        private long _tempPriceTime;


        public StableCoins(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            BnnUtils.Log($"{GetName()}", false);
            _isLimit = true;
            // IsLong = false;
            _dealParams = new StableCoinsParams(_stableInterval);
        }


        public override string GetName() { return $"Stable Coins - {SymbolName} - TF{AccountManager.Timeframe}"; }
        #endregion

        #region BackTest
        private void InitBacktest(List<Kline> klines)
        {
            CurrencyAmount = 0;
            _manager.CheckBalance();
            _backtestInited = true;

            // decimal lastSellPrice = 0;
            var stableInterval = 0;
            for (var i = klines.Count - 1; i > 0; i--)
            {
                // Log($"find stable: {klines[i]} / {_lastStablePrice} / {stableInterval}");
                if ((_lastStablePrice == klines[i].HighPrice) && (klines[i].HighPrice == klines[i].LowPrice + _priceStep))
                {
                    stableInterval++;
                    if (stableInterval >= _stableInterval)
                    {
                        var buyPrice = klines[i].LowPrice;
                        if (klines[^1].LowPrice < buyPrice) _longPrice = klines[^1].LowPrice;
                        Log($"last stable high price ({_stableInterval} mins): {_lastStablePrice}; order price: {buyPrice}");
                        _order = CreateSpotLimitOrder(buyPrice, true, false);
                        break;
                    }
                }
                else
                {
                    _lastStablePrice = klines[i].HighPrice;
                    stableInterval = 0;
                }
            }
        }


        public override void InitBacktestLong(int klineIndex)
        {
            base.InitBacktestLong(klineIndex);
        }


        private void ProcessBacktestKline(List<Kline> klines)
        {
            var lastKline = klines[^1];
            var penultKline = klines[^2];
            BacktestTime = lastKline.OpenTime;
            CurrPrice = lastKline.ClosePrice;

            // Log($"ProcessBacktestKline: {lastKline}");

            if (!_backtestInited)
            {
                InitBacktest(klines);
                return;
            };

            if (_order == null) return;

            if (_order.IsBuyer)
            {
                if (lastKline.LowPrice == _order.Price) _minsTraded++;
                if ((lastKline.LowPrice < _order.Price) || (_minsTraded >= _tradeInterval)) EnterLong(lastKline);
            }
            else
            {
                if (lastKline.HighPrice == _order.Price) _minsTraded++;
                if ((lastKline.HighPrice > _order.Price) || (_minsTraded >= _tradeInterval)) ExitLong(lastKline);
            }

            if (CheckStablePrice(lastKline))
            {
                _minsTraded = 0;
                if (_order.IsBuyer) ChangeOrderPrice(_order, _lastStablePrice - _priceStep);
                else ChangeOrderPrice(_order, _lastStablePrice);
            }
        }


        private bool CheckStablePrice(Kline lastKline)
        {
            if ((_lastStablePrice != lastKline.HighPrice) && (lastKline.HighPrice == lastKline.LowPrice + _priceStep))
            {
                if (_tempStablePrice == lastKline.HighPrice)
                {
                    if ((lastKline.OpenTime - _tempPriceTime) / 1000 / 60 >= _stableInterval)
                    {
                        _lastStablePrice = _tempStablePrice;
                        // Log($"new stable high price ({_stableInterval} mins): {_lastStablePrice}");
                        return true;
                    }
                }
                else
                {
                    _tempStablePrice = lastKline.HighPrice;
                    _tempPriceTime = lastKline.OpenTime;
                }
            }
            else _tempStablePrice = 0;
            return false;
        }


        private void EnterLong(Kline kline, decimal limitPrice = 0)
        {
            _longPrice = _order == null ? 0 : _order.Price;// _longAim;
            if (_order != null) ExecuteOrder(_order);
            IsDealEntered = true;
            // Log($"EL: {_longPrce:0.####}");

            _minsTraded = 0;
            _order = CreateSpotLimitOrder(_longPrice + _priceStep, false, false);

            // if (_order != null) _dealParams.TotalProfit = (double)_order.Amount; // if (_order != null) 
        }


        protected override void ExitLong(Kline kline, decimal limitPrice = 0)
        {
            if (_order == null) return;
            ExecuteOrder(_order);
            IsDealEntered = false;

            // Log($"ES: {_order.Price}"); // ; slippage: {Slippage:0.###} ; {100 * (limitPrice - _longPrice) / _longPrice:0.##} %
            // base.EnterShort(kline, _order.Price);

            _minsTraded = 0;
            _order = CreateSpotLimitOrder(_lastStablePrice - _priceStep, true, false);

            if (_order != null) _dealParams.TotalProfit = (double)_order.TotalQuote;
            _manager.Statistics.TotalProfit = _dealParams.TotalProfit;
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
        }


        protected override double GetShortValue(List<Kline> klines, double previousValue = -1)
        {
            ProcessBacktestKline(klines);
            return (double)_shortPrice; //  1000000.0;
            //return double.MaxValue;
        }


        protected override void SaveStatistics(List<BaseDealParams> tradeResults)
        {
            /*var script = "INSERT INTO backtest (BeginDate, EndDate, SymbolId, Param1, Param2, Param3, Param4, Param5, Param6, IsBull, IsBear, Balance, Strategy) VALUES ";
            foreach (var tr in tradeResults)
            {
                var dp = (RsiDumpParams)tr;
                script += $"({_manager.StartTime}, {_manager.EndTime}, {SymbolId}, {dp.RsiLength}, {dp.RsiLevel}, {dp.SellFibLevel}, {dp.StopFibLevel}, {dp.MinMaxKlines}, {dp.DealCount}, {_highTimeframe}, 3, {dp.TotalProfit}, 'u'), ";
            };
            script = string.Concat(script.AsSpan(0, script.Length - 2), ";");
            Console.WriteLine(script);
            DB.ExecQuery(_manager.DbConnection, script, null);*/
        }


        protected override List<BaseDealParams> CalculateParams(List<Kline> klines)
        {
            int[] stableIntervals = [0, 5, 15, 30, 60, 90, 120, 180, 250];
            /* int[] buyRsiLevels = [40, 50, 55, 60, 65, 70, 80];
            int[] sellRsiLevels = [40, 50, 55, 60, 65, 70, 80];
            decimal[] longKlineHighs = [0.001M];
            decimal[] longKlineHighs = [0];// [0.001M, 0.005M, 0.01M, 0.013M, 0.015M, 0.017M, 0.019M, 0.023M, 0.03M];*/

            var deals = new List<BaseDealParams>();
            long paramsSetCount = stableIntervals.Length; //  * buyRsiLevels.Length * sellRsiLevels.Length * longKlineHighs.Length;
            long counter = 0;
            /*foreach (var rl in rsiLengths)
            {
                _rsiLength = rl;
                foreach (var bl in buyRsiLevels)
                {
                    _rsiBuyLevel = bl;
                    foreach (var sl in sellRsiLevels)
                    {
                        _rsiSellLevel = sl;*/
            foreach (var si in stableIntervals)
            {
                _stableInterval = si;
                var dp = new StableCoinsParams(_stableInterval); // , _rsiBuyLevel, _rsiSellLevel, _longKlineHigh
                _dealParams = dp;
                _order = null;
                _backtestInited = false;
                _manager.BackTest();
                deals.Add(dp);

                counter++;
                BnnUtils.ClearCurrentConsoleLine();
                Console.Write($"{counter * 100.0 / paramsSetCount:0.##}%");
            }
            return deals;
        }
        #endregion

        #region RealTimeRoutines
        #endregion
    }

}
