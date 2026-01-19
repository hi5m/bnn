// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Binance.Spot;
using Binance.Spot.Models;
using Newtonsoft.Json;
using Org.BouncyCastle.Security;

namespace Bnncmd.Strategy
{
    internal class DiffBook : EMA
    {
        #region VariablesAndConstructor
        public DiffBook(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            BnnUtils.Log($"{GetName()} 1/{bidAskRatio} {SymbolName}", false);
            _dealParams = new DummyParams();
            _isLimit = true;

            EmaLength = 7;
            // _alpha = 2.0M / (_emaLength + 1);
        }

        private readonly Market _market = new();

        public static int Thousands { get; private set; } = 1;
        public static string ThousandsDescr { get; private set; } = "?";
        public static string PriceMask { get; private set; } = string.Empty;

        private decimal _bestAskPrice = 0;
        private double _bestAskVolume = 0;
        private decimal _bestBidPrice = 0;
        private double _bestBidVolume = 0;

        private dynamic _asks = 0;
        private dynamic _bids = 0;

        private readonly decimal bidAskRatio = 130M;
        private readonly bool _ourOrderBook = true;

        private readonly bool _findFirstPrice = true;

        /*private double _currAngle;

        private long _lastCheckAmountTime; //  = Int64.MaxValue;

        private double _dayPriceMax = 0;
        private double _dayPriceMin = int.MaxValue;

        private long _lastVCheckOrdersTime = 0;
        private decimal _lastDealPrice = -1;

        // private readonly bool _allowRaisePrice = false;
        private int _tradeStepCoef = 75;
        private double _tradeAngle = 0; // 0125*/


        public override string GetName() { return "Order Book"; }
        #endregion

        #region BackTest

        public override string GetCurrentInfo()
        {
            var addInfo = $"EMA{EmaLength} {_previousCloseEMA:0.###} TA {_trendCloseAngle:0.###}";
            // return $"{_bestAskPrice.ToString(PriceMask)}:{_bestAskVolume / Thousands:0.000}{ThousandsDescr} / {_bestBidPrice.ToString(PriceMask)}:{_bestBidVolume / Thousands:0.000}{ThousandsDescr} {addInfo}";
            return $"{_bestAskPrice.ToString(PriceMask)}:{_bestAskVolume:0.000} / {_bestBidPrice.ToString(PriceMask)}:{_bestBidVolume:0.000} {addInfo}";
        }


        protected override double GetShortValue(decimal longPrice, out decimal stopLossValue)
        {
            stopLossValue = 0;
            return double.MaxValue;
        }


        protected override decimal GetLongValue(List<Kline> klines, decimal previousValue = -1)
        {
            return _longPrice; //  0
        }


        protected override double GetShortValue(List<Kline> klines, double previousValue = -1)
        {
            return (double)_shortPrice; //  1000000.0;
        }


        protected override void SaveStatistics(List<BaseDealParams> tradeResults)
        {
            // override abstract method
        }


        private void FillTestOrder()
        {
            if (_order == null) return;
            if (_order.IsBuyer) CurrencyAmount += _order.Amount * (1 - AccountManager.Fee / 2);
            else _manager.SpotAmount += _order.Amount * _order.Price * (1 - AccountManager.Fee / 2);
            // _lastDealPrice = order.Price;
            // order.Filled = order.Amount;

            var comment = _order.QueueOvercome > _order.InitialQueue ? $"{_order.QueueOvercome / 1000:0.###}/{_order.InitialQueue / 1000:0.###}k" : $"{_order.Price.ToString(PriceMask)} => {(_order.IsBuyer ? _bestBidPrice : _bestAskPrice).ToString(PriceMask)}";
            Log($"{_order}: FILLED [{comment}]  ({GetCurrencyName().ToLower()[0]} {CurrencyAmount:0.###} {_manager.QuoteCurrencyName.ToLower()[0]} {_manager.SpotAmount:0.###})");
            if (!_order.IsBuyer)
            {
                Log(string.Empty);
                _dealParams.TotalProfit = (double)_manager.SpotAmount;
                _manager.Statistics.TotalProfit = _dealParams.TotalProfit;
            }
            _order = null;
        }


        protected override List<BaseDealParams> CalculateParams(List<Kline> klines)
        {
            int[] emaIntervals = [103, 105, 107]; // 7, 11 240, 720, 1440
            double[] longAngles = [0.036, 0.039, 0.042];
            int[] tradeStepCoefs = [75]; // 7, 11 240, 720, 1440
            double[] tradeAngle = [0]; // 7, 11 240, 720, 1440

            var deals = new List<BaseDealParams>();
            /*long paramsSetCount = emaIntervals.Length * longAngles.Length * tradeStepCoefs.Length * tradeAngle.Length; //  * shortAngles.Length
            long counter = 0;
            foreach (var el in emaIntervals)
            {
                _emaLength = el;
                _emaDiffInterval = 1;
                foreach (var la in longAngles)
                {
                    _longAngle = la;
                    foreach (var ts in tradeStepCoefs)
                    {
                        _tradeStepCoef = ts;
                        foreach (var ta in tradeAngle)
                        {
                            _tradeAngle = ta;
                            // _shortAngle = -la;
                            _alpha = 2.0 / (_emaLength + 1);
                            var dp = new ExchangerDealParams(el, _emaDiffInterval, la, -la)
                            {
                                TradeStepCoef = _tradeStepCoef,
                                TradeAngle = _tradeAngle
                            };
                            _dealParams = dp;
                            _orders.Clear();
                            _backtestInited = false;
                            _manager.BackTest(); // klines
                            deals.Add(dp);

                            counter++;
                            BnnUtils.ClearCurrentConsoleLine();
                            Console.Write($"{counter * 100.0 / paramsSetCount:0.##}%");
                        }
                    }
                }
            }*/
            return deals;
        }
        #endregion

        #region RealTimeRoutines

        private bool CheckIfPriceOvercame()
        {
            // filled - record exhausted
            if (_order == null) return false;
            if ((_order.IsBuyer && (_bestBidPrice < _order.Price)) || (!_order.IsBuyer && (_bestAskPrice > _order.Price)))
            {
                if (AccountManager.IsTest) FillTestOrder();
                // else CheckOrderStatus(o);
                return true;
            }
            else return false;
        }

        /*
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
        }*/


        private void OutOrderBook(decimal shortPrice, decimal stopPrice)
        {
            if (!_ourOrderBook) return;
            Log("");
            for (int i = _asks.Count - 1; i >= 0; i--)
            {
                decimal price = _asks[i][0]; // FormatPrice
                var addInfo = string.Empty;
                if (_asks[i][0] == shortPrice) addInfo = " ES =>";
                decimal amount = _asks[i][1];
                BnnUtils.Log($"{price.ToString(PriceMask)}: {FormatQuantity(amount):0.000} {addInfo}", false);
            }

            BnnUtils.Log("-----------------", false); // -----

            for (var i = 0; i < _bids.Count; i++)
            {
                decimal price = _bids[i][0];
                var addInfo = string.Empty;
                if (i == 0) addInfo = " EL <=";
                if (_bids[i][0] == stopPrice) addInfo = " SL =>";
                decimal amount = _bids[i][1];
                BnnUtils.Log($"{price.ToString(PriceMask)}: {FormatQuantity(amount):0.000} {addInfo}", false);
            }

            Log("");
            // Environment.Exit(0);
        }


        private void CalcEma()
        {
            if (_previousCloseEMA == 0) _previousCloseEMA = CurrPrice;

            var currentSecond = DateTime.Now.Second;
            if (currentSecond < _lastCheckSecond)
            {
                var newCloseEMA = CurrPrice * _alpha + (1 - _alpha) * _previousCloseEMA;
                _trendCloseAngle = (double)((newCloseEMA - _previousCloseEMA) / _previousCloseEMA * 100);
                _previousCloseEMA = newCloseEMA;
                CalcStopsFromOrderBook();
            }
            _lastCheckSecond = currentSecond;
        }


        public void ProcessOrderBook() // dynamic ordersData
        {
            _bestAskPrice = _asks[0][0];
            _bestAskVolume = _asks[0][1];
            _bestBidPrice = _bids[0][0];
            _bestBidVolume = _bids[0][1];
            CurrPrice = _bestBidPrice;

            CalcEma();

            if (_order == null) CheckSetup();
            else
            {
                if (IsDealEntered)
                {
                    if (_bestAskPrice < _stopLossPrice)
                    {
                        if (_order.StopLossPrice == 0) Log("stoploss");
                        _order.StopLossPrice = Math.Min(_bestAskPrice, _order.Price);
                        ChangeOrderPrice(_order, _order.StopLossPrice);
                        // Environment.Exit(0);
                    }

                    if (CheckIfPriceOvercame()) IsDealEntered = false;
                }
                else
                {
                    if (CheckIfPriceOvercame())
                    {
                        _order = CreateSpotLimitOrder(_shortPrice, false, false);
                        IsDealEntered = true;
                        OutOrderBook(_shortPrice, _stopLossPrice);
                    }

                    if ((_order != null) && (_bestBidPrice > _order.Price + _priceStep))
                    {
                        OutOrderBook(_shortPrice, _stopLossPrice);
                        CancelOrder(_order);
                        _order = null;
                    }
                }
            }

            /* CheckForTrend();
            if ((_orders.Count == 0) && (CurrencyAmount < _lotStep) && (_manager.Amount / _newBuyPrice < _lotStep) && !AccountManager.IsTest) _manager.CheckBalance();*/
        }


        private void CheckSetup()
        {
            if ((CurrPrice < (decimal)_previousCloseEMA) && (_trendCloseAngle < 0)) return;
            // if (_trendCloseAngle < 0) return;

            _shortPrice = 0;
            _stopLossPrice = 0;
            var avgPrice = (_bestAskPrice + _bestBidPrice) / 2;
            decimal askSum = 0;

            for (var i = 0; i < _asks.Count; i++)
            {
                decimal currAskPrice = _asks[i][0];
                decimal currAskVolume = _asks[i][1];
                askSum += currAskVolume;
                if (i < 1) continue;

                decimal bidSum = 0;
                for (var j = 0; j < _bids.Count; j++)
                {
                    decimal currBidVolume = _bids[j][1];
                    bidSum += currBidVolume;
                    decimal currBidPrice = _bids[j][0];
                    if (avgPrice - currBidPrice >= currAskPrice - avgPrice)
                    {
                        if ((bidSum / askSum > bidAskRatio) && ((_bestBidVolume <= _bestAskVolume) || (_bestAskPrice - _bestBidPrice > _priceStep)))
                        {
                            _shortPrice = currAskPrice;
                            _stopLossPrice = currBidPrice;
                        }
                        break;
                    }
                }

                if ((_shortPrice > 0) && _findFirstPrice) break;
            }

            if (_shortPrice == 0M) return;

            var orderPrice = _bestBidPrice;
            if (_bestAskPrice - _bestBidPrice > _priceStep) orderPrice = _bestBidPrice + _priceStep;
            Log($"order: {orderPrice.ToString(PriceMask)}; aim: {_shortPrice.ToString(PriceMask)}; sl: {_stopLossPrice.ToString(PriceMask)}");
            _order = CreateSpotLimitOrder(orderPrice, true, false);
            OutOrderBook(_shortPrice, _stopLossPrice);
        }


        /*protected override void ChangeOrderPrice(Order order, decimal price)
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
        }*/


        public override void Prepare()
        {
            var digitsCount = -(int)Math.Log10((double)_priceStep);
            PriceMask = "0.".PadRight(digitsCount + 2, '0');

            /* _lastDealPrice = decimal.MaxValue;
            if (!AccountManager.IsTest) LoadOrders();
            foreach (var o in _orders)
            {
                BnnUtils.Log($"loaded order: {o}", false);
            }*/
        }


        public override async void Start()
        {
            // if (!AccountManager.IsTest) CheckOrdersStatus(true);
            var ws = new MarketDataWebSocket($"{SymbolName.ToLower()}@depth20@100ms"); // 20 --- @100ms
            ws.OnMessageReceived(data =>
            {
                dynamic ordersData = JsonConvert.DeserializeObject(data.Trim()) ?? throw new Exception("depth returned no data");

                try
                {
                    _asks = ordersData.asks;
                    _bids = ordersData.bids;
                    ProcessOrderBook();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    Environment.Exit(0);
                }
                return Task.CompletedTask;
            }, CancellationToken.None);

            await ws.ConnectAsync(CancellationToken.None);
        }
        #endregion
    }
}