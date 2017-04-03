﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.IO.Ports;
using System.Timers;
using System.ComponentModel;
using System.Threading;

namespace SerialportSample
{
    public class ModbusRTU:Component
    {

        #region//////////////////修改过待删除的量///////////////////////
        //public const byte ReadCoils        = 0x01;
        //public const byte ReadDistrbuteBits= 0x02;
        //public const byte ReadStorageRegs  = 0x03;
        //public const byte ReadInputRegs    = 0x04;
        //public const byte WriteSingleCoil  = 0x05;
        //public const byte WriteSingleReg   = 0x06;
        //public const byte WriteCoils       = 0x0F;
        //public const byte WriteRegs        = 0x10;


        //public const byte ERR_OK= 0x00;
        //public const byte ERR_Station = 0x01;
        //public const byte ERR_FunctionCode = 0x02;
        //public const byte ERR_Address = 0x03;
        //public const byte ERR_NumOrData = 0x04;//与NumOrData相对应
        //public const byte ERR_CRCCode = 0x05;
        //public const byte ERR_BreakFrame = 0x06;
        //public const byte ERR_Response = 0xFF;

        #endregion

        public static Queue<byte[]> HighPriorityTxBufferQueue = new Queue<byte[]>(502); //注意限制FIFO的数量，以免占用太多的内存
        public static Queue<byte[]> LowPriorityTxBufferQueue = new Queue<byte[]>(502);//注意限制FIFO的数量，以免占用太多的内存
        public static Queue<int> HPControlsIndexQueue = new Queue<int>(502);//存储使用Modbus的控件的索引
        public static Queue<int> LPControlsIndexQueue = new Queue<int>(502);//存储使用Modbus的控件的索引
        private static int TempControlIndex;
        public static bool HighPriorityFrameRdy = false;
        public static bool LowPriorityFrameRdy = false;

        public static byte[][] DataStorage;//以各控件的索引为序存储接收到的数据
        public static bool[] DataStorageFlag;

        private TranmitingStatus TxRxStatus= TranmitingStatus.Idle;


        public static byte[] TxRxBuffer = new byte[256];
        public byte[] DatasInByte = new byte[250];
        public UInt16[] DatasInWord = new UInt16[250];
        public static UInt16 TxLength = 0;
        public static UInt16 RxLength = 0;
        private static UInt16 Pointer = 0;
        public UInt16 RetryCNT = 0;
        
        private Boolean IsACKTimeout = false;
        private Boolean IsBroadcastTimeout = false;
        private Boolean IsRxDone = false;
        public byte StationID = 0;
        public byte FunctionCode = 0;
        public UInt16 ErrorCode = 0;
        public UInt16 CRCCode = 0;
        public UInt16 Address = 0;
        private static UInt16 NumOrData = 0;//存放读取（写入）的寄存器数量或者写入单个线圈（寄存器）的值
        private static UInt16 DataByteNumber = 0;
        public UInt16 DataWordNumber = 0;
        private static Word2Byte TempWord;
        public ByteBits[] ByteBitsExchange=new ByteBits[2];

        private static System.Timers.Timer RxDataTimer =new System.Timers.Timer();
        private static System.Timers.Timer ACKTimer = new System.Timers.Timer();
        private static System.Timers.Timer BroadcastTimer = new System.Timers.Timer();
        private static System.Timers.Timer PeriodicTxTimer = new System.Timers.Timer();

        #region////////////////////委托与事件的声明///////////////////////
        public delegate void ModbusSendFrameDelegate(byte[] buffer,int offset,int count);//声明委托类型（就像声明结构体类型一样，委托也是一种类型）
        public static ModbusSendFrameDelegate ModbusSendFrame;//定义委托变量（就像申明了结构体以后，用所申明的结构体定义变量一样）  最后给这个委托变量赋值，就可以使用了，不赋值就是空的，直接使用会报错

        public delegate void ModbusWriteSuccessDelegate();
        public static event ModbusWriteSuccessDelegate ModbusWriteSuccessEvent;//声明写成功事件

        public delegate void ModbusReceiveExceptionDelegate();
        public static event ModbusReceiveExceptionDelegate ModbusReceiveExceptionEvent;//声明收到异常帧事件

        public delegate void ModbusReadSuccessDelegate();
        public static event ModbusReadSuccessDelegate ModbusReadSuccessEvent;//声明收到异常帧事件

        #endregion

        #region/////////////////////ModbusRTU类构造器//////////////////////
        public ModbusRTU()
        {
            
            ACKTimer.Interval= ACKTimerInterval;
            BroadcastTimer.Interval= BroadcastTimerInterval;
            RxDataTimer.Interval = RxTimerInterval;
            PeriodicTxTimer.Interval = 10;//待修改
            PeriodicTxTimer.Enabled = true;

            RxDataTimer.Elapsed += RxDataTimer_Elapsed;//为接收数据定时器创建定时函数
            BroadcastTimer.Elapsed += BroadcastTimer_Elapsed;//为广播定时器创建定时函数
            ACKTimer.Elapsed += ACKTimer_Elapsed;//为应答超时定时器创建定时函数
            PeriodicTxTimer.Elapsed += PeriodicTxTimer_Elapsed;//为周期发送定时器创建定时函数

            ModbusWriteSuccessEvent += ModbusRTU_ModbusWriteSuccessEvent;
            ModbusReadSuccessEvent += ModbusRTU_ModbusReadSuccessEvent;
            ModbusReceiveExceptionEvent += ModbusRTU_ModbusReceiveExceptionEvent;
        }

        private void ModbusRTU_ModbusReceiveExceptionEvent()
        {
            DataStorageFlag[TempControlIndex] = false;
        }

        private void ModbusRTU_ModbusWriteSuccessEvent()
        {
            DataStorageFlag[TempControlIndex] = true;
            TxRxStatus = TranmitingStatus.Idle;
        }

        private void ModbusRTU_ModbusReadSuccessEvent()
        {
            DataStorageFlag[TempControlIndex] = true;
            TxRxStatus = TranmitingStatus.Idle;
        }


        #endregion

        #region///////////////////////ModbusRTU类属性//////////////////////////
        private static Boolean _IsMaster = true;
        private static int _ControlIndex =-1;
        private static double _ACKTimerInterval = 50;
        private static double _BroadcastTimerInterval = 200;
        private static double _RxTimerInterval = 10;
        private static UInt16 _DepthOfFIFO = 500;
        //private static int _test = 0;
        //[LocalizableAttribute(true)]
        //[Category("ModbusProperty"), Description("test")]
        //public int test
        //{
        //    get { return _test; }
        //    set { _test = value; }

        //}
        [LocalizableAttribute(true)]
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
        public static int TotalControlNumber //外部控件的索引
        {
            get
            {
                return  _ControlIndex + 1;
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

        #region//////////////////////ModbusRTU类方法//////////////////////////
        public static int GetMyModbusIndex() //让外部使用本ModbusRTU类的控件都获得一个唯一的编号
        {
            _ControlIndex += 1;
            return _ControlIndex;
        }

        public static void SetDataStorage()
        {
            DataStorage = new byte[TotalControlNumber][];
            DataStorageFlag = new bool[TotalControlNumber];
        }
        public static bool GetDataStorageFlag(int ControlIndex)
        {
            return DataStorageFlag[ControlIndex];
        }
        public static byte[] GetStorageData(int ControlIndex)
        {
            DataStorageFlag[ControlIndex]=false;//取数据前，将数据的标识位置false，表面数据取用后即失效
            return DataStorage[ControlIndex];
        }

        #endregion

        #region//////////////////////定时器Tick事件////////////////////////
        
        private void PeriodicTxTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            MasterSendFrame();
        }

        private void ACKTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            TxRxStatus = TranmitingStatus.Idle;
            ACKTimer.Enabled = false;
            Console.Write("NO ACK!");
            IsACKTimeout = true;
        }

        private void BroadcastTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            TxRxStatus = TranmitingStatus.Idle;
            BroadcastTimer.Enabled = false;
            Console.Write("Broadcasting done!");
            IsBroadcastTimeout = true;
        }

        private void RxDataTimer_Elapsed(object sender, EventArgs e)
        {
            RxDataTimer.Enabled = false;
            ModbusReceiveData_SerialPort_Done(); //数据流的间隙时间超过帧间间隔时间表示一帧接收完成
            IsRxDone = true;
        }
        private void ModbusReceiveData_SerialPort_Done()
        {
            Disassemble_ReceivedADU();//解析收到的帧
            Pointer = 0;
        }

        #endregion
   
        #region//////////////////////////ModbusRTU类收/发事件//////////////////////////
        private Boolean MasterSendFrame()
        {
            byte[] TxBuffer;
            Console.Write(TxRxStatus+"\n");
            if (TxRxStatus == TranmitingStatus.Idle)
            {
                if ((HighPriorityFrameRdy == true) || (LowPriorityFrameRdy == true))
                {
                    Console.Write(LowPriorityTxBufferQueue.Count+" "+ LPControlsIndexQueue.Count+"\n");
                   
                    if (HighPriorityFrameRdy == true)
                    {
                        if (HighPriorityTxBufferQueue.Count <= 0)//如过FIFO空，则复位Ready标志位，禁止周期行为
                        {
                            HighPriorityFrameRdy = false;
                            return true;
                        }
                        lock (HighPriorityTxBufferQueue)//锁定，防止线程间串扰（非常重要）
                        {
                            lock (HPControlsIndexQueue)
                            {
                                TxBuffer = HighPriorityTxBufferQueue.Dequeue();//从FIFO中取用待发送的帧                         
                                TempControlIndex = HPControlsIndexQueue.Dequeue();//取用待发送帧对应的控件的索引
                            }
                        }
                    }
                    else
                    {
                        if (LowPriorityTxBufferQueue.Count <= 0)
                        {
                            LowPriorityFrameRdy = false;
                            return true;
                        }
                        lock (LowPriorityTxBufferQueue)
                        {
                            lock (LPControlsIndexQueue)
                            {
                                TxBuffer = LowPriorityTxBufferQueue.Dequeue();
                                TempControlIndex = LPControlsIndexQueue.Dequeue();
                            } 
                            
                        }
                    }

                    //Console.Write(TxBuffer.Length);
                    try //此处不能用if语句简化
                    {
                        ModbusSendFrame(TxBuffer, 0, TxBuffer.Length);//调用发送函数（委托）
                    }
                    catch
                    {
                        Console.Write("Error:ModbusSendFrame未关联 或 Serialport未打开！\n");
                        return false;
                    }

                    TxRxStatus = TranmitingStatus.Sending;//Modbus收发标志位置 “发送中”

                    if (TxBuffer[0] == 0)//此处用到StationID，说明全局的StationID变量是必要的，不可弃用
                    {
                        IsBroadcastTimeout = false;//如果是广播帧，先清广播帧超时标志位（其实没必要，只是为了以防不可预知的改变）
                        BroadcastTimer.Enabled = true;//打开广播帧超时定时器
                    }
                    else
                    {
                        IsACKTimeout = false;
                        ACKTimer.Enabled = true;
                    }

                }

            }        
           
            return true;

        }

        public void ModbusReceiveData_SerialPort(object sender, SerialDataReceivedEventArgs e)//供外部SerialPort调用的数据接收事件
        {
            RxDataTimer.Enabled = false;//定时器停止计数并将计数值归零
            RxDataTimer.Enabled = true;//定时器重新开始计数
            TxRxStatus = TranmitingStatus.Receiving;//Modbus状态为标记为“接收中”
            UInt16 n = (UInt16)((SerialPort)sender).BytesToRead;//先记录下来，避免某种原因，人为的原因，操作几次之间时间长，缓存不一致
            ((SerialPort)sender).Read(TxRxBuffer, Pointer, n);//读取缓冲数据
            Pointer += n;//增加接收计数
            RxLength = Pointer;
            
        }


        #endregion

        #region/////////////////////////ModbusRTU(主)读写帧组装方法/////////////////////////
        public static void AssembleRequestADU(int ControlIndex,Boolean IsHighPriority, byte ID,byte FuncCode,UInt16 Addr,UInt16 NumCmdData,byte[] DataToTx)
        {
            byte[] TxFrame;
            //FunctionCode = FuncCode;
            NumOrData = NumCmdData;
            UInt16 DataByteNumber = 0;
            UInt16 Pointer = 0;

            switch (FuncCode)
            {
                case (byte)ModbusFuncCode.ReadCoils:
                case (byte)ModbusFuncCode.ReadDistrbuteBits:
                case (byte)ModbusFuncCode.ReadStorageRegs:
                case (byte)ModbusFuncCode.ReadInputRegs:   
                case (byte)ModbusFuncCode.WriteSingleCoil:         
                case (byte)ModbusFuncCode.WriteSingleReg:
                    TxLength = 8;
                    break;

                case (byte)ModbusFuncCode.WriteCoils:
                    DataByteNumber= (UInt16)( NumCmdData / 8 + (NumCmdData % 8 == 0 ? 0 : 1));
                    TxLength =(UInt16)(9 + DataByteNumber);
                    break;

                case (byte)ModbusFuncCode.WriteRegs:
                    DataByteNumber = (UInt16)( 2 * NumCmdData);
                    TxLength = (UInt16)( 9 + DataByteNumber);
                    break;

                default:
                    break;
            }
            TxFrame = new byte[TxLength];
            TxFrame[0] = ID;
            TxFrame[1] = FuncCode;
            TempWord.Word = Addr;
            TxFrame[2] = TempWord.HByte;
            TxFrame[3] = TempWord.LByte;
            TempWord.Word = NumCmdData;
            TxFrame[4] = TempWord.HByte;
            TxFrame[5] = TempWord.LByte;
            Pointer = 6;
            if (DataByteNumber != 0)
            {
                TxFrame[Pointer++] = (byte)DataByteNumber;//填入字节数           
                for (UInt16 i = 0; i < DataByteNumber; i++)
                    TxFrame[Pointer++] = DataToTx[i];//bytes
            }
            TempWord.Word = CRCCaculation.CRC16(TxFrame, Pointer);
            TxFrame[Pointer++] = TempWord.HByte;//crc code
            TxFrame[Pointer++] = TempWord.LByte;
            Pointer = 0;//为接收逻辑的使用复位

            if (IsHighPriority == true)
            {
                if (HighPriorityTxBufferQueue.Count >= _DepthOfFIFO) return;//禁止FIFO无节制扩大
                lock (HighPriorityTxBufferQueue)
                {
                    lock (HPControlsIndexQueue)
                    {
                        HighPriorityTxBufferQueue.Enqueue(TxFrame);//高优先级的帧进高优先级FIFO                 
                        HPControlsIndexQueue.Enqueue(ControlIndex);//高优先级帧所对应的控件索引进高优先级FIFO
                    }
                }
                if (HighPriorityTxBufferQueue.Count != 0)//检查高优先级帧FIFO是否有数据，如果有则Ready标志位置位
                    HighPriorityFrameRdy = true;
            }
            else
            {
                if (LowPriorityTxBufferQueue.Count >= _DepthOfFIFO) return;//禁止FIFO无节制扩大
                lock (LowPriorityTxBufferQueue)
                {
                    lock (LPControlsIndexQueue)
                    { 
                        LowPriorityTxBufferQueue.Enqueue(TxFrame);//低优先级的帧进低优先级FIFO
                        LPControlsIndexQueue.Enqueue(ControlIndex);//低优先级帧所对应的控件索引进低优先级FIFO
                     }
                }
                if (LowPriorityTxBufferQueue.Count != 0)//检查低优先级帧FIFO是否有数据，如果有则Ready标志位置位
                    LowPriorityFrameRdy = true;
            }         

        }

        #endregion

        #region//////////////////////////ModbusRTU(从)响应帧组装方法/////////////////////////
        public Boolean Assemble_ResponseADU()//Slaver To DO...
        {

            return true;
        }
        public Boolean Assemble_ExceptionADU()
        {

            return true;
        }

        #endregion
    
        #region///////////////////////ModbusRTU(主/从)Rx帧解析方法//////////////////////////
       public byte Disassemble_ReceivedADU()
        {
            if (IsMaster == true)
            {
                if (StationID != TxRxBuffer[0]) //站号错误不进行任何处理，立即返回（ACKTimeout定时器继续定时）
                    return (byte)ErrorStatus.ERR_Station;

                ACKTimer.Enabled = false;//通过上面站号检测的说明是期望子节点发送来的响应帧，因而停止ACKTimeout倒计时。
                TxRxStatus = TranmitingStatus.Idle;//总线状态置为空闲，允许发送其他帧（会不会放在本函数最后好，不然要是Modbus发送了其他的帧，并接收了返回的帧，上一次此处的处理还未完成怎么办？）

                if (RxLength < 5) return (byte)ErrorStatus.ERR_BreakFrame;

                TempWord.HByte = TxRxBuffer[RxLength - 2];
                TempWord.LByte = TxRxBuffer[RxLength - 1];
                if (TempWord.Word != CRCCaculation.CRC16(TxRxBuffer, (UInt16)(RxLength - 2)))
                    return (byte)ErrorStatus.ERR_CRCCode;//检测CRC是否错误

                FunctionCode = TxRxBuffer[1];//记录功能码
                switch (FunctionCode)
                {
                    case (byte)ModbusFuncCode.WriteSingleCoil:
                    case (byte)ModbusFuncCode.WriteCoils:
                    case (byte)ModbusFuncCode.WriteRegs:
                    case (byte)ModbusFuncCode.WriteSingleReg:
                        TempWord.HByte = TxRxBuffer[2];//检查地址是否正确
                        TempWord.LByte = TxRxBuffer[3];
                        if (TempWord.Word != Address)
                            return (byte)ErrorStatus.ERR_Address;
                        TempWord.HByte = TxRxBuffer[4];//检查写入单元数量或写入的单个数据是否正确
                        TempWord.LByte = TxRxBuffer[5];
                        if (TempWord.Word != NumOrData)
                            return (byte)ErrorStatus.ERR_NumOrData;

                            ModbusWriteSuccessEvent();//（事件引发）如果以上均正确，说明Modbus主向外写操作成功，引发“写成功”事件

                        break;

                    case (byte)ModbusFuncCode.ReadCoils:
                    case (byte)ModbusFuncCode.ReadDistrbuteBits:
                    case (byte)ModbusFuncCode.ReadStorageRegs:
                    case (byte)ModbusFuncCode.ReadInputRegs:
                        DataByteNumber = TxRxBuffer[2];
                        DataStorage[TempControlIndex] = new byte[DataByteNumber];
                        for (byte i = 0; i < DataByteNumber; i++)
                        {
                            DataStorage[TempControlIndex][i] = TxRxBuffer[i + 3];                   
                        }

                            ModbusReadSuccessEvent();//（事件引发）如果以上均正确，说明Modbus主读输入寄存器成功，引发“读输入寄存器成功”事件
                        break;


                    case ((byte)ModbusFuncCode.ReadCoils + 0x80):
                    case ((byte)ModbusFuncCode.ReadDistrbuteBits + 0x80):
                    case ((byte)ModbusFuncCode.ReadStorageRegs + 0x80):
                    case ((byte)ModbusFuncCode.ReadInputRegs + 0x80):
                    case ((byte)ModbusFuncCode.WriteSingleCoil + 0x80):
                    case ((byte)ModbusFuncCode.WriteSingleReg + 0x80):
                    case ((byte)ModbusFuncCode.WriteCoils + 0x80):
                    case ((byte)ModbusFuncCode.WriteRegs + 0x80):

                        ModbusReceiveExceptionEvent();//（事件引发）接收到异常帧
                        return (byte)ErrorStatus.ERR_Response;                      

                    default:
                        return (byte)ErrorStatus.ERR_FunctionCode;//检测FunctionCode是否错误
                       
                }
                

            }
            else
            {
                //Slaver To Do...

            }
            return (byte)ErrorStatus.ERR_OK;
        }

        #endregion

        #region/////////////////////ModbusRTU类中的枚举量////////////////////////
        public enum TranmitingStatus
        {
            Sending = 0x00,
            Receiving = 0x01,
            Idle = 0x02
        }

        public enum ErrorStatus
        {
            ERR_OK = 0x00,
            ERR_Station = 0x01,
            ERR_FunctionCode = 0x02,
            ERR_Address = 0x03,
            ERR_NumOrData = 0x04,//与NumOrData相对应
            ERR_CRCCode = 0x05,
            ERR_BreakFrame = 0x06,
            ERR_Response = 0xFF
        }

        public enum ModbusFuncCode
        {
            ReadCoils = 0x01,
            ReadDistrbuteBits = 0x02,
            ReadStorageRegs = 0x03,
            ReadInputRegs = 0x04,
            WriteSingleCoil = 0x05,
            WriteSingleReg = 0x06,
            WriteCoils = 0x0F,
            WriteRegs = 0x10
        }

        #endregion

    }
    //ModbusRTU类到此结束

    #region/////////////////////////ModbusRTU类之外 自定义结构体及附加功能/////////////////////
    public class CRCCaculation
    {

        /* CRC 高位字节值表 */
        public static readonly  byte[] auchCRCHi = {
            0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0,
            0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41,
            0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0,
            0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40,
            0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1,
            0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41,
            0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1,
            0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41,
            0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0,
            0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40,
            0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1,
            0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40,
            0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0,
            0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40,
            0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0,
            0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40,
            0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0,
            0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41,
            0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0,
            0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41,
            0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0,
            0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40,
            0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1,
            0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41,
            0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0,
            0x80, 0x41, 0x00, 0xC1, 0x81, 0x40 };
        /* CRC低位字节值表*/
        public static readonly byte[] auchCRCLo = {
            0x00, 0xC0, 0xC1, 0x01, 0xC3, 0x03, 0x02, 0xC2, 0xC6, 0x06,
            0x07, 0xC7, 0x05, 0xC5, 0xC4, 0x04, 0xCC, 0x0C, 0x0D, 0xCD,
            0x0F, 0xCF, 0xCE, 0x0E, 0x0A, 0xCA, 0xCB, 0x0B, 0xC9, 0x09,
            0x08, 0xC8, 0xD8, 0x18, 0x19, 0xD9, 0x1B, 0xDB, 0xDA, 0x1A,
            0x1E, 0xDE, 0xDF, 0x1F, 0xDD, 0x1D, 0x1C, 0xDC, 0x14, 0xD4,
            0xD5, 0x15, 0xD7, 0x17, 0x16, 0xD6, 0xD2, 0x12, 0x13, 0xD3,
            0x11, 0xD1, 0xD0, 0x10, 0xF0, 0x30, 0x31, 0xF1, 0x33, 0xF3,
            0xF2, 0x32, 0x36, 0xF6, 0xF7, 0x37, 0xF5, 0x35, 0x34, 0xF4,
            0x3C, 0xFC, 0xFD, 0x3D, 0xFF, 0x3F, 0x3E, 0xFE, 0xFA, 0x3A,
            0x3B, 0xFB, 0x39, 0xF9, 0xF8, 0x38, 0x28, 0xE8, 0xE9, 0x29,
            0xEB, 0x2B, 0x2A, 0xEA, 0xEE, 0x2E, 0x2F, 0xEF, 0x2D, 0xED,
            0xEC, 0x2C, 0xE4, 0x24, 0x25, 0xE5, 0x27, 0xE7, 0xE6, 0x26,
            0x22, 0xE2, 0xE3, 0x23, 0xE1, 0x21, 0x20, 0xE0, 0xA0, 0x60,
            0x61, 0xA1, 0x63, 0xA3, 0xA2, 0x62, 0x66, 0xA6, 0xA7, 0x67,
            0xA5, 0x65, 0x64, 0xA4, 0x6C, 0xAC, 0xAD, 0x6D, 0xAF, 0x6F,
            0x6E, 0xAE, 0xAA, 0x6A, 0x6B, 0xAB, 0x69, 0xA9, 0xA8, 0x68,
            0x78, 0xB8, 0xB9, 0x79, 0xBB, 0x7B, 0x7A, 0xBA, 0xBE, 0x7E,
            0x7F, 0xBF, 0x7D, 0xBD, 0xBC, 0x7C, 0xB4, 0x74, 0x75, 0xB5,
            0x77, 0xB7, 0xB6, 0x76, 0x72, 0xB2, 0xB3, 0x73, 0xB1, 0x71,
            0x70, 0xB0, 0x50, 0x90, 0x91, 0x51, 0x93, 0x53, 0x52, 0x92,
            0x96, 0x56, 0x57, 0x97, 0x55, 0x95, 0x94, 0x54, 0x9C, 0x5C,
            0x5D, 0x9D, 0x5F, 0x9F, 0x9E, 0x5E, 0x5A, 0x9A, 0x9B, 0x5B,
            0x99, 0x59, 0x58, 0x98, 0x88, 0x48, 0x49, 0x89, 0x4B, 0x8B,
            0x8A, 0x4A, 0x4E, 0x8E, 0x8F, 0x4F, 0x8D, 0x4D, 0x4C, 0x8C,
            0x44, 0x84, 0x85, 0x45, 0x87, 0x47, 0x46, 0x86, 0x82, 0x42,
            0x43, 0x83, 0x41, 0x81, 0x80, 0x40 };

        //int *puchMsg ; 							/* 要进行CRC校验的消息 */ 
        //unsigned int usDataLen ; 							/* 消息中字节数 */ 

       public static UInt16 CRC16(byte[] puchMsgg, UInt16 usDataLen)
        {
            int i;
            byte uchCRCHi = 0xFF;              /* 高CRC字节初始化 */
            byte uchCRCLo = 0xFF;              /* 低CRC 字节初始化 */
            int uIndex;                          /* CRC循环中的索引 */
            for (i = 0; i < usDataLen; i++)
            {                       /* 传输消息缓冲区 */
                uIndex = uchCRCHi ^ puchMsgg[i];      /* 计算CRC */
                uchCRCHi = (byte)(uchCRCLo ^ auchCRCHi[uIndex]);
                uchCRCLo = auchCRCLo[uIndex];
            }
            return (ushort)(uchCRCHi << 8 | uchCRCLo);
        }

    }

    [StructLayout(LayoutKind.Explicit)]
    public struct Word2Byte
    {
        [FieldOffset(0)]
        public byte LByte;

        [FieldOffset(1)]
        public byte HByte;

        [FieldOffset(0)]
        public UInt16 Word;
    }

   public struct ByteBits //使用索引器取用byte中的位值
    {
        public byte ALL;

        public bool this [byte index]
        {
            get
            {
                return (ALL & (1 << index)) != 0;
            }

            set
            {
                if (value)
                    ALL |=(byte)(1 << index);
                else
                    ALL &=(byte) (~(1 << index));
            }

        }

    }

    #endregion

}