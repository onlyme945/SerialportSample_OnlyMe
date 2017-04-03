using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace SerialportSample
{
    public partial class testcomponent : Component
    {
        public testcomponent()
        {
            InitializeComponent();
        }

        public testcomponent(IContainer container)
        {
            container.Add(this);

            InitializeComponent();
        }

        #region///////////////////////ModbusRTU类属性//////////////////////////
        private static Boolean _IsMaster = true;
        private static int _ControlIndex = -1;
        private static double _ACKTimerInterval = 50;
        private static double _BroadcastTimerInterval = 200;
        private static double _RxTimerInterval = 10;
        private static UInt16 _DepthOfFIFO = 500;
        //private int _test = 0;
        ////[LocalizableAttribute(true)]
        //[Category("ModbusProperty"), Description("test")]
        //public int test
        //{
        //    get { return _test; }
        //    set { _test = value; }

        //}

        [Category("ModbusProperty"), Description("主/从选择")]
        public  Boolean IsMaster
        {
            get
            {
                return _IsMaster;
            }
            set
            {
                _IsMaster = value;
            }

        }

        [Category("ModbusProperty"), Description("调用Modbus功能的控件数量")]
        public  int TotalControlNumber //外部控件的索引
        {
            get
            {
                return _ControlIndex + 1;
            }
        }

        [Category("ModbusProperty"), Description("调用Modbus功能的控件数量")]
        public  double ACKTimerInterval
        {
            get
            {
                return _ACKTimerInterval;
            }
            set
            {
                _ACKTimerInterval = value;
            }

        }
        [Category("ModbusProperty"), Description("调用Modbus功能的控件数量")]
        public  double BroadcastTimerInterval
        {
            get
            {
                return _BroadcastTimerInterval;
            }
            set
            {
                _BroadcastTimerInterval = value;
            }

        }
        [Category("ModbusProperty"), Description("调用Modbus功能的控件数量")]
        public  double RxTimerInterval
        {
            get
            {
                return _RxTimerInterval;
            }
            set
            {
                _RxTimerInterval = value;
            }

        }
        [Category("ModbusProperty"), Description("FIFO深度")]
        public  UInt16 DepthOfFIFO
        {
            get
            {
                return _DepthOfFIFO;
            }
            set
            {
                _DepthOfFIFO = value;
                if (value < 1) _DepthOfFIFO = 1;
                if (value > 1000) _DepthOfFIFO = 1000;
            }
        }

        #endregion


    }
}
