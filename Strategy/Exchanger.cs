// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Binance.Spot;
using Binance.Spot.Models;
using Newtonsoft.Json;

namespace Bnncmd.Strategy
{
    public enum State
    {
        Waiting,
        Long,
        Short,
        Trading,
        Stopping
    }


    internal class ExchangerDealParams(int emaMinsLength, int diffInterval, double longAngle, double shortAngle) : EmaDealParams(emaMinsLength, diffInterval, longAngle, shortAngle)
    {
        public int TradeStepCoef { get; set; }

        public double TradeAngle { get; set; }

        public override string ToString()
        {
            var results = $"{TotalProfit:0.##}\t\t{MaxDealInterval}\t{DealCount}\t{StopLossCount}";
            var conditions = $"emi\t{EmaLength}\tla\t{LongAngle:0.###}\tts\t{TradeStepCoef:0.###}\tta\t{TradeAngle:0.###}";
            return results + "\t" + conditions;
        }

        public override string GetParamsDesciption()
        {
            return $"eml {EmaLength}   edi {DiffInterval}   la {LongAngle:0.#####}   sa {ShortAngle}"; // \n\r
        }
    }

    internal class Exchanger : EMA
    {
        #region VariablesAndConstructor
        public Exchanger(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            BnnUtils.Log($"{GetName()} {SymbolName}", false);
            _dealParams = new DummyParams();
            _isLimit = true;
        }

        private readonly Market _market = new();

        private State _state = State.Trading;

        public static int Thousands { get; private set; } = 1;
        public static string ThousandsDescr { get; private set; } = "?";
        public static string PriceMask { get; private set; } = string.Empty;

        private double _currAngle;

        private decimal _bestAskPrice = 0;
        private double _bestAskVolume = 0;
        private decimal _bestBidPrice = 0;
        private double _bestBidVolume = 0;

        private long _lastCheckAmountTime; //  = Int64.MaxValue;

        private double _dayPriceMax = 0;
        private double _dayPriceMin = int.MaxValue;

        private long _lastVCheckOrdersTime = 0;
        private decimal _lastDealPrice = -1;

        private decimal _newBuyPrice;
        private decimal _newSellPrice;
        private double _escapeToLowerRatio;
        private readonly double _returnToHigherRatio = 1.7;
        private readonly double _escapeToHigherRatio = 0.15;
        // private int _lastCheckSecond = 60;
        private double _askBidRatio;
        private dynamic? _ordersData = null;

        // private readonly bool _allowRaisePrice = false;
        private int _tradeStepCoef = 75;
        private double _tradeAngle = 0; // 0125

        public override string GetName() { return "Exchanger"; }
        #endregion

        #region BackTest
        private void ProcessBacktestKline(List<Kline> klines)
        {
            var lastKline = klines[^1];
            BacktestTime = lastKline.OpenTime;

            if (!_backtestInited)
            {
                CurrencyAmount = 0;
                _manager.CheckBalance();
                _previousCloseEMA = (1 - _alpha) * klines[^2].ClosePrice + _alpha * klines[^3].ClosePrice; // a bit reversal
                _lastDealPrice = klines[^2].ClosePrice;
                // CalcStopsFromOrderBook();
                _backtestInited = true;
            }

            if (lastKline.ClosePrice >= lastKline.OpenPrice)
            {
                ProcessOrderBook(lastKline.OpenPrice + _priceStep, lastKline.OpenPrice);
                if (lastKline.LowPrice < lastKline.OpenPrice) ProcessOrderBook(lastKline.LowPrice + _priceStep, lastKline.LowPrice);
                if (lastKline.HighPrice > lastKline.ClosePrice) ProcessOrderBook(lastKline.HighPrice, lastKline.HighPrice - _priceStep);
                if (lastKline.ClosePrice != lastKline.OpenPrice) ProcessOrderBook(lastKline.ClosePrice, lastKline.ClosePrice - _priceStep);
            }
            else
            {
                // optimistic
                ProcessOrderBook(lastKline.OpenPrice + _priceStep, lastKline.OpenPrice);
                if (lastKline.HighPrice > lastKline.OpenPrice) ProcessOrderBook(lastKline.HighPrice + _priceStep, lastKline.HighPrice);
                if (lastKline.LowPrice < lastKline.ClosePrice) ProcessOrderBook(lastKline.LowPrice, lastKline.LowPrice - _priceStep);
                if (lastKline.ClosePrice != lastKline.OpenPrice) ProcessOrderBook(lastKline.ClosePrice, lastKline.ClosePrice - _priceStep);
            }

            if (AccountManager.LogLevel == 10) Log($"{GetCurrentInfo()}"); // protected override double GetLongValue:  : ema{_emaLength} {_previousCloseEMA:0.###} {_order} --- BnnUtils.

            var currSecond = BnnUtils.UnitTimeToDateTime(BacktestTime).Second;
            /*if (currSecond < _lastCheckSecond) // (lastKline.OpenTime % 60000 == 0)
            {
                CalcStopsFromOrderBook(); // recalc EMA
                ChangeOrderStoploss();
            }
            _lastCheckSecond = currSecond;

            if (_order == null) InitOrder();*/
        }


        public override string GetCurrentInfo()
        {
            if (Thousands == 1 && _bestBidVolume > 0)
            {
                var maxVolume = _bestBidVolume + _bestAskVolume;
                if (maxVolume > 1000000)
                {
                    Thousands = 1000000;
                    ThousandsDescr = "M";
                }
                else if (maxVolume > 1000)
                {
                    Thousands = 1000;
                    ThousandsDescr = "k";
                }
                else
                {
                    Thousands = 1;
                    ThousandsDescr = string.Empty;
                };
            };


            var addInfo = string.Empty;
            addInfo += addInfo + $" angle {_currAngle:0.###} ";

            switch (_state)
            {
                case State.Waiting:
                    addInfo = "...";
                    break;
                case State.Long:
                    addInfo += $"> {_longAngle}";
                    break;
                case State.Short:
                    addInfo += $"< {_shortAngle}";
                    break;
                default:
                    foreach (var o in _orders)
                    {
                        if (o.IsBuyer) addInfo += $"({(_newBuyPrice > o.Price ? _returnToHigherRatio : _escapeToLowerRatio):0.###} >";
                        else addInfo += $"({_escapeToHigherRatio:0.###} <";
                        addInfo += $" {_bestAskVolume / _bestBidVolume:0.###})   ";
                        addInfo += $"[{o.GetQueueInfo()}] ";
                        // long secToRaise = (_changePriceTimount - BnnUtils.GetUnixNow() + _priceChangeTime) / 1000;
                        // if (o.IsBuyer && (_newBuyPrice > o.Price) && (secToRaise > 0)) addInfo += $"--- {secToRaise / 60:00}:{secToRaise % 60:00} to raise ";
                    };
                    break;
            }

            if (new[] { State.Waiting, State.Long, State.Short }.Contains(_state))
            {
                addInfo = $"{_state.ToString().ToLower()}{addInfo}";
                // addInfo = $"{_state.ToString().ToLower()}{addInfo}   [{_lastMinutesExtremums[0, 0].ToString(PriceMask)}-{_lastMinutesExtremums[0, 1].ToString(PriceMask)}][{_lastMinutesExtremums[1, 0].ToString(PriceMask)}-{_lastMinutesExtremums[1, 1].ToString(PriceMask)}][{_lastMinutesExtremums[2, 0].ToString(PriceMask)}-{_lastMinutesExtremums[2, 1].ToString(PriceMask)}]";
            }

            return $"{_bestAskPrice.ToString(PriceMask)}:{_bestAskVolume / Thousands:0.000}{ThousandsDescr} / {_bestBidPrice.ToString(PriceMask)}:{_bestBidVolume / Thousands:0.000}{ThousandsDescr} {addInfo}";
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
            // override abstract method
        }


        private void FillTestOrder(Order order)
        {
            if (order.IsBuyer) CurrencyAmount += order.Amount * (1 - AccountManager.Fee / 2);
            else _manager.SpotAmount += order.Amount * order.Price * (1 - AccountManager.Fee / 2);
            _lastDealPrice = order.Price;
            order.Filled = order.Amount;

            var comment = order.QueueOvercome > order.InitialQueue ? $"{order.QueueOvercome / 1000:0.###}/{order.InitialQueue / 1000:0.###}k" : $"{order.Price.ToString(PriceMask)} => {(order.IsBuyer ? _bestBidPrice : _bestAskPrice).ToString(PriceMask)}";
            Log($"{order}: FILLED [{comment}]  ({GetCurrencyName().ToLower()[0]} {CurrencyAmount:0.###} {_manager.QuoteCurrencyName.ToLower()[0]} {_manager.SpotAmount:0.###})");
            if (!order.IsBuyer)
            {
                Log(string.Empty);
                _dealParams.TotalProfit = (double)_manager.SpotAmount;
                _manager.Statistics.TotalProfit = _dealParams.TotalProfit;
            }
        }


        protected override List<BaseDealParams> CalculateParams(List<Kline> klines)
        {
            /* int[] emaIntervals = [1, 3, 5]; // 7, 11
            double[] longAngles = [0.001, 0.003, 0.007];
            double[] shortAngles = [-0.001, -0.005, -0.009];*/

            int[] emaIntervals = [103, 105, 107]; // 7, 11 240, 720, 1440
            double[] longAngles = [0.036, 0.039, 0.042];
            int[] tradeStepCoefs = [75]; // 7, 11 240, 720, 1440
            double[] tradeAngle = [0]; // 7, 11 240, 720, 1440

            /*int[] emaIntervals = [80, 105, 130]; // 55, 80, 105
            double[] longAngles = [0.039];
            int[] tradeStepCoefs = [75]; // 7, 11 240, 720, 1440
            double[] tradeAngle = [-0.001, 0.001, 0]; // 7, 11 240, 720, 1440 */

            // double[] shortAngles = [-0.031, -0.032, -0.033];

            var deals = new List<BaseDealParams>();
            long paramsSetCount = emaIntervals.Length * longAngles.Length * tradeStepCoefs.Length * tradeAngle.Length; //  * shortAngles.Length
            long counter = 0;
            foreach (var el in emaIntervals)
            {
                EmaLength = el;
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
                            // _alpha = 2.0M / (_emaLength + 1);
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
            }
            return deals;
        }
        #endregion

        #region RealTimeRoutines
        private bool CheckRests()
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
            _currAngle = (double)((CurrPrice * _alpha + (1 - _alpha) * _previousCloseEMA) / _previousCloseEMA - 1) * 100;

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
            if (CurrPrice > _longPrice && _state != State.Long || CurrPrice < _shortPrice && _state != State.Short)
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
                        /* if (o.IsBuyer) _manager.Amount += o.Amount * o.Price;
                        else CurrencyAmount += o.Amount;
                        o.Filled = o.Amount;
                        Log($"{CurrPrice} canceled {o}");*/
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
            if ((_orders.Count == 0) && (CurrencyAmount < _lotStep) && (_manager.SpotAmount / _newBuyPrice < _lotStep) && !AccountManager.IsTest) _manager.CheckBalance();
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
                quantity = _manager.SpotAmount / price;
            }
            else
            {
                if (price < borderPrice) price = borderPrice;
                quantity = CurrencyAmount;
            }
            quantity = FormatQuantity(quantity);
            if (quantity < _lotStep) return null;

            var orderID = _manager.SpotLimitOrder(SymbolName, isBuyer ? Side.BUY : Side.SELL, quantity, price);
            var newOrder = new Order()
            {
                LongId = orderID,
                Price = price,
                Amount = quantity,
                DateTime = BnnUtils.GetUnixNow(),
                IsBuyer = isBuyer,
                BorderPrice = borderPrice, // isBuyer ? 0 : 
                TotalQuote = _manager.SpotAmount
            };

            if (isBuyer) newOrder.InitialQueue = price == _bestBidPrice ? _bestBidVolume : Order.DefaultInitialQueue;
            else newOrder.InitialQueue = price == _bestAskPrice ? _bestAskVolume : Order.DefaultInitialQueue;

            _orders.Add(newOrder);
            if (isBuyer) _manager.SpotAmount -= newOrder.Amount * newOrder.Price;
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
                _manager.SpotAmount = order.TotalQuote - newQuantity * price;
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
                var newId = _manager.ReplaceOrder(order.LongId, SymbolName, order.IsBuyer ? Side.BUY : Side.SELL, newQuantity, price);
                ChangeOrderData(order.LongId, newId, order.Amount, order.Price);
                order.LongId = newId;
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
            var status = _manager.CheckOrder(this, order.LongId);
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
                if ((double)klines[i].HighPrice > _dayPriceMax) _dayPriceMax = (double)klines[i].HighPrice;
                if ((double)klines[i].LowPrice < _dayPriceMin) _dayPriceMin = (double)klines[i].LowPrice;
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
        }
        #endregion
    }
}